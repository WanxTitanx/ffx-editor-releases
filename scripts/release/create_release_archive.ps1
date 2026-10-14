<#
  Creates a deterministic Candidate ZIP from an already verified package.

  Archive creation is deliberately separate from publish and package verification. The package is
  read-only, all entry paths are ordinal-sorted, timestamps are normalized to the ZIP epoch, and
  the destination must be a new path under this worktree's work/release-readiness root.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [Parameter(Mandatory = $true)][string]$OutputZip,
  [ValidateSet('Candidate')][string]$Mode = 'Candidate',
  [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
  [Parameter(DontShow = $true)][string]$TestPauseAfterSnapshotSignal,
  [Parameter(DontShow = $true)][string]$TestPauseAfterOutputParentSignal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repo 'work\release-readiness')).TrimEnd('\', '/')
$package = (Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path.TrimEnd('\', '/')
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputZip)) { $OutputZip } else { Join-Path $repo $OutputZip }))
$sidecar = $output + '.sha256'
$createdOutput = $false
$createdSidecar = $false
$createdOutputCanonical = $null
$createdSidecarCanonical = $null

function Test-ContainedPath([string]$Root, [string]$Candidate, [bool]$AllowRoot = $false) {
  $relative = [IO.Path]::GetRelativePath($Root, $Candidate)
  if ([IO.Path]::IsPathRooted($relative)) { return $false }
  if ([string]::IsNullOrWhiteSpace($relative) -or $relative -ceq '.') { return $AllowRoot }
  return -not ($relative -ceq '..' -or
    $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
    $relative.StartsWith('../', [StringComparison]::Ordinal))
}

function Resolve-CanonicalPath([string]$Path) {
  $pending = [IO.Path]::GetFullPath($Path)
  $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  for ($hop = 0; $hop -lt 64; $hop++) {
    $pathRoot = [IO.Path]::GetPathRoot($pending)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) { throw "PATH_ROOT_MISSING:$pending" }
    $relative = [IO.Path]::GetRelativePath($pathRoot, $pending)
    $segments = [string[]]@($relative -split '[\\/]+' | Where-Object { $_ -and $_ -ne '.' })
    $cursor = $pathRoot
    $redirected = $false
    $rootItem = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      if (-not $visited.Add($rootItem.FullName)) { throw "PATH_REPARSE_CYCLE:$($rootItem.FullName)" }
      try { $target = $rootItem.ResolveLinkTarget($true) } catch { throw "PATH_REPARSE_UNRESOLVED:$($rootItem.FullName)" }
      if ($null -eq $target) { throw "PATH_REPARSE_UNRESOLVED:$($rootItem.FullName)" }
      $pending = $target.FullName
      foreach ($segment in $segments) { $pending = Join-Path $pending $segment }
      continue
    }
    for ($index = 0; $index -lt $segments.Count; $index++) {
      $cursor = Join-Path $cursor $segments[$index]
      if (-not (Test-Path -LiteralPath $cursor)) {
        for ($tail = $index + 1; $tail -lt $segments.Count; $tail++) { $cursor = Join-Path $cursor $segments[$tail] }
        return [IO.Path]::GetFullPath($cursor).TrimEnd('\', '/')
      }
      $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        if (-not $visited.Add($item.FullName)) { throw "PATH_REPARSE_CYCLE:$($item.FullName)" }
        try { $target = $item.ResolveLinkTarget($true) } catch { throw "PATH_REPARSE_UNRESOLVED:$($item.FullName)" }
        if ($null -eq $target) { throw "PATH_REPARSE_UNRESOLVED:$($item.FullName)" }
        $pending = $target.FullName
        for ($tail = $index + 1; $tail -lt $segments.Count; $tail++) { $pending = Join-Path $pending $segments[$tail] }
        $redirected = $true
        break
      }
      if ($item -isnot [IO.DirectoryInfo] -and $index -lt ($segments.Count - 1)) {
        throw "PATH_ANCESTOR_NOT_DIRECTORY:$($item.FullName)"
      }
    }
    if (-not $redirected) { return [IO.Path]::GetFullPath($cursor).TrimEnd('\', '/') }
  }
  throw "PATH_REPARSE_DEPTH_EXCEEDED:$Path"
}

function Assert-CanonicalContainment(
  [string]$AuthorityRoot,
  [string]$AllowedRoot,
  [string]$Candidate,
  [string]$Prefix,
  [bool]$AllowRoot = $false
) {
  if (-not (Test-ContainedPath $AllowedRoot $Candidate $AllowRoot)) { throw "${Prefix}_OUTSIDE_ALLOWED_ROOT:$Candidate" }
  $authorityCanonical = Resolve-CanonicalPath $AuthorityRoot
  $allowedCanonical = Resolve-CanonicalPath $AllowedRoot
  if (-not (Test-ContainedPath $authorityCanonical $allowedCanonical $true)) {
    throw "${Prefix}_ALLOWED_ROOT_REPARSE_ESCAPE:logical=$AllowedRoot,canonical=$allowedCanonical"
  }
  $candidateCanonical = Resolve-CanonicalPath $Candidate
  if (-not (Test-ContainedPath $allowedCanonical $candidateCanonical $AllowRoot)) {
    throw "${Prefix}_REPARSE_ESCAPE:logical=$Candidate,canonical=$candidateCanonical"
  }
}

function Assert-ArchiveWriteBoundary {
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'RELEASE' $true
  Assert-CanonicalContainment $repo $allowedRoot $package 'PACKAGE'
  Assert-CanonicalContainment $repo $allowedRoot $output 'OUTPUT'
  Assert-CanonicalContainment $repo $allowedRoot $sidecar 'OUTPUT_SIDECAR'
  $packageResolved = Resolve-CanonicalPath $package
  foreach ($ownedArchivePath in @($output, $sidecar)) {
    $ownedCanonical = Resolve-CanonicalPath $ownedArchivePath
    if ((Test-ContainedPath $packageResolved $ownedCanonical $true) -or
        (Test-ContainedPath $ownedCanonical $packageResolved $true)) {
      throw "OUTPUT_CANONICAL_OVERLAP_PACKAGE:path=$ownedArchivePath,canonical=$ownedCanonical"
    }
  }
}

function Remove-OwnedArchiveFile([string]$Path, [string]$ExpectedCanonical) {
  if ([string]::IsNullOrWhiteSpace($ExpectedCanonical)) { return }
  try {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $currentCanonical = Resolve-CanonicalPath $Path
    if ([string]::Equals($currentCanonical, $ExpectedCanonical, [StringComparison]::OrdinalIgnoreCase)) {
      Remove-Item -LiteralPath $Path -Force
    }
  } catch { }
}

function Get-SafePackageFiles([string]$Root) {
  $rootItem = Get-Item -LiteralPath $Root -Force
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "PACKAGE_REPARSE_FORBIDDEN:." }
  $stack = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
  $stack.Push([IO.DirectoryInfo]$rootItem)
  $files = [Collections.Generic.List[IO.FileInfo]]::new()
  while ($stack.Count -gt 0) {
    $directory = $stack.Pop()
    foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force) {
      $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "PACKAGE_REPARSE_FORBIDDEN:$relative"
      }
      if ($item -is [IO.DirectoryInfo]) { $stack.Push($item) }
      elseif ($item -is [IO.FileInfo]) { $files.Add($item) }
    }
  }
  return $files.ToArray()
}

function Get-FileFingerprint([string]$Path) {
  $source = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
  $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
  $length = [long]0
  try {
    $buffer = [byte[]]::new(131072)
    while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
      $hasher.AppendData($buffer, 0, $read)
      $length += $read
    }
    return [pscustomobject]@{
      Length = $length
      Sha256 = [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
    }
  } finally {
    $hasher.Dispose()
    $source.Dispose()
  }
}

function Get-PackageSnapshot([string]$Root) {
  $rows = [Collections.Generic.List[object]]::new()
  foreach ($file in [IO.FileInfo[]]@(Get-SafePackageFiles $Root)) {
    $fingerprint = Get-FileFingerprint $file.FullName
    $rows.Add([pscustomobject]@{
      Relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
      FullName = $file.FullName
      Length = [long]$fingerprint.Length
      Sha256 = [string]$fingerprint.Sha256
    })
  }
  $snapshot = $rows.ToArray()
  [Array]::Sort($snapshot, [Comparison[object]]{
    param($left, $right)
    return [StringComparer]::Ordinal.Compare([string]$left.Relative, [string]$right.Relative)
  })
  return $snapshot
}

function Test-SamePackageSnapshot([object[]]$Expected, [object[]]$Actual) {
  if ($Expected.Count -ne $Actual.Count) { return $false }
  for ($index = 0; $index -lt $Expected.Count; $index++) {
    if ([string]$Expected[$index].Relative -cne [string]$Actual[$index].Relative -or
        [long]$Expected[$index].Length -ne [long]$Actual[$index].Length -or
        [string]$Expected[$index].Sha256 -cne [string]$Actual[$index].Sha256) {
      return $false
    }
  }
  return $true
}

function Invoke-TestSnapshotPause([string]$SignalPath) {
  if ([string]::IsNullOrWhiteSpace($SignalPath)) { return }
  if ($env:FFX_RELEASE_ARCHIVE_TEST_HOOK -cne 'snapshot-pause-v1') { throw 'ARCHIVE_TEST_HOOK_NOT_AUTHORIZED' }
  $signal = [IO.Path]::GetFullPath($SignalPath)
  if (Test-ContainedPath $package $signal $true) { throw 'ARCHIVE_TEST_HOOK_SIGNAL_INSIDE_PACKAGE' }
  $signalParent = Split-Path $signal -Parent
  if (-not (Test-Path -LiteralPath $signalParent -PathType Container)) { throw 'ARCHIVE_TEST_HOOK_PARENT_MISSING' }
  $continueSignal = $signal + '.continue'
  if ((Test-Path -LiteralPath $signal) -or (Test-Path -LiteralPath $continueSignal)) { throw 'ARCHIVE_TEST_HOOK_SIGNAL_EXISTS' }
  $signalStream = [IO.FileStream]::new($signal, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $signalStream.Dispose()
  $deadline = [datetime]::UtcNow.AddSeconds(10)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $continueSignal -PathType Leaf) { return }
    Start-Sleep -Milliseconds 25
  }
  throw 'ARCHIVE_TEST_HOOK_CONTINUE_TIMEOUT'
}

function Invoke-TestOutputParentPause([string]$SignalPath) {
  if ([string]::IsNullOrWhiteSpace($SignalPath)) { return }
  if ($env:FFX_RELEASE_ARCHIVE_PARENT_TEST_HOOK -cne 'output-parent-pause-v1') { throw 'ARCHIVE_PARENT_TEST_HOOK_NOT_AUTHORIZED' }
  $signal = [IO.Path]::GetFullPath($SignalPath)
  $signalCanonical = Resolve-CanonicalPath $signal
  $packageResolved = Resolve-CanonicalPath $package
  if ((Test-ContainedPath $packageResolved $signalCanonical $true) -or
      (Test-ContainedPath $signalCanonical $packageResolved $true)) {
    throw 'ARCHIVE_PARENT_TEST_HOOK_SIGNAL_OVERLAPS_PACKAGE'
  }
  $signalParent = Split-Path $signal -Parent
  if (-not (Test-Path -LiteralPath $signalParent -PathType Container)) { throw 'ARCHIVE_PARENT_TEST_HOOK_PARENT_MISSING' }
  $continueSignal = $signal + '.continue'
  if ((Test-Path -LiteralPath $signal) -or (Test-Path -LiteralPath $continueSignal)) { throw 'ARCHIVE_PARENT_TEST_HOOK_SIGNAL_EXISTS' }
  $signalStream = [IO.FileStream]::new($signal, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $signalStream.Dispose()
  $deadline = [datetime]::UtcNow.AddSeconds(10)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $continueSignal -PathType Leaf) { return }
    Start-Sleep -Milliseconds 25
  }
  throw 'ARCHIVE_PARENT_TEST_HOOK_CONTINUE_TIMEOUT'
}

function Copy-ApprovedFile([object]$Row, [IO.Stream]$Destination) {
  $source = [IO.FileStream]::new([string]$Row.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
  $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
  $length = [long]0
  try {
    $buffer = [byte[]]::new(131072)
    while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
      $Destination.Write($buffer, 0, $read)
      $hasher.AppendData($buffer, 0, $read)
      $length += $read
    }
    $sha256 = [Convert]::ToHexString($hasher.GetHashAndReset()).ToLowerInvariant()
    if ($length -ne [long]$Row.Length -or $sha256 -cne [string]$Row.Sha256) {
      throw "PACKAGE_FILE_MUTATED_DURING_ARCHIVE:$($Row.Relative)"
    }
  } finally {
    $hasher.Dispose()
    $source.Dispose()
  }
}

function Assert-CleanProducingRepo([string]$ExpectedHead) {
  $headRaw = (& git -C $repo rev-parse HEAD 2>$null) | Out-String
  $headExit = $LASTEXITCODE
  $actualHead = $headRaw.Trim()
  if ($headExit -ne 0 -or $actualHead -notmatch '^[0-9a-fA-F]{40}$') { throw 'SOURCE_REPO_HEAD_UNAVAILABLE' }
  if (-not [string]::Equals($ExpectedHead, $actualHead, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SOURCE_COMMIT_HEAD_MISMATCH:expected=$actualHead,actual=$ExpectedHead"
  }
  $statusRaw = (& git -C $repo status --porcelain=v2 --untracked-files=all 2>$null) | Out-String
  $statusExit = $LASTEXITCODE
  if ($statusExit -ne 0) { throw 'SOURCE_REPO_STATUS_UNAVAILABLE' }
  if (-not [string]::IsNullOrWhiteSpace($statusRaw)) { throw 'SOURCE_REPO_DIRTY' }
}

if (-not (Test-Path -LiteralPath $package -PathType Container)) { throw "PackageRoot is not a directory: $package" }
Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'RELEASE' $true
Assert-CanonicalContainment $repo $allowedRoot $package 'PACKAGE'
Assert-CanonicalContainment $repo $allowedRoot $output 'OUTPUT'
Assert-ArchiveWriteBoundary
if (Test-ContainedPath $package $output $true) { throw "OUTPUT_MUST_NOT_MUTATE_PACKAGE:$output" }
if ([IO.Path]::GetExtension($output) -cne '.zip') { throw "OUTPUT_EXTENSION_MUST_BE_ZIP:$output" }
foreach ($ownedOutput in @($output, $sidecar)) {
  if (Test-Path -LiteralPath $ownedOutput) { throw "OUTPUT_ALREADY_EXISTS:$ownedOutput" }
}

$manifestPath = Join-Path $package 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "MANIFEST_MISSING:$manifestPath" }
try { $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -NoEnumerate -ErrorAction Stop }
catch { throw 'MANIFEST_JSON_INVALID' }
if ($manifest.schemaVersion -ne 2) { throw 'MANIFEST_SCHEMA_INVALID' }
if ([string]$manifest.buildMode -cne $Mode) { throw "MANIFEST_MODE_MISMATCH:expected=$Mode,actual=$($manifest.buildMode)" }
if (-not [string]::Equals([string]$manifest.sourceCommit, $SourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
  throw "MANIFEST_SOURCE_MISMATCH:expected=$SourceCommit,actual=$($manifest.sourceCommit)"
}
if ($manifest.sourceDirty -ne $false) { throw 'MANIFEST_SOURCE_DIRTY' }
Assert-CleanProducingRepo $SourceCommit

$preVerificationSnapshot = [object[]]@(Get-PackageSnapshot $package)
if (@($preVerificationSnapshot | Where-Object { $_.Relative -ceq 'release-manifest.json' }).Count -ne 1) {
  throw 'MANIFEST_SNAPSHOT_MISSING'
}
$policyPath = Join-Path $repo 'release\package-allowlist.json'
try { $policy = [IO.File]::ReadAllText($policyPath) | ConvertFrom-Json -NoEnumerate -ErrorAction Stop }
catch { throw 'DENYLIST_POLICY_INVALID' }
$deny = $policy.components.'studio-core-win-x64'.deny
$denyPaths = [string[]]@($deny.paths | ForEach-Object { ([string]$_).Replace('\', '/').Trim('/') })
$denyExtensions = [string[]]@($deny.extensions | ForEach-Object { [string]$_ })
foreach ($file in $preVerificationSnapshot) {
  $relative = [string]$file.Relative
  $deniedPath = @($denyPaths | Where-Object {
    $relative.Equals($_, [StringComparison]::OrdinalIgnoreCase) -or
    $relative.StartsWith($_ + '/', [StringComparison]::OrdinalIgnoreCase)
  }).Count -gt 0
  $deniedExtension = @($denyExtensions | Where-Object { $relative.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
  if ($deniedPath -or $deniedExtension -or $relative.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) {
    throw "DENYLIST_PAYLOAD_FORBIDDEN:$relative"
  }
}

$verifyScript = Join-Path $PSScriptRoot 'verify_package.ps1'
$verifyRaw = (& pwsh -NoProfile -File $verifyScript -PackageRoot $package -Mode $Mode -Json) | Out-String
$verifyExit = $LASTEXITCODE
try { $verifyResult = $verifyRaw | ConvertFrom-Json -NoEnumerate -ErrorAction Stop }
catch { throw 'PACKAGE_VERIFIER_JSON_INVALID' }
if ($verifyExit -ne 0 -or $verifyResult.schemaVersion -ne 2 -or [string]$verifyResult.status -cne 'verified' -or
    [int]$verifyResult.exitCode -ne 0 -or [string]$verifyResult.mode -cne $Mode) {
  throw "PACKAGE_VERIFIER_REJECTED:exit=$verifyExit,status=$($verifyResult.status)"
}

$approvedSnapshot = [object[]]@(Get-PackageSnapshot $package)
if (-not (Test-SamePackageSnapshot $preVerificationSnapshot $approvedSnapshot)) {
  throw 'PACKAGE_MUTATED_DURING_VERIFICATION'
}
$rows = $approvedSnapshot
Invoke-TestSnapshotPause $TestPauseAfterSnapshotSignal
Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'RELEASE' $true
Assert-CanonicalContainment $repo $allowedRoot $package 'PACKAGE'
Assert-CanonicalContainment $repo $allowedRoot $output 'OUTPUT'
$outputParent = Split-Path $output -Parent
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
  New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}
Invoke-TestOutputParentPause $TestPauseAfterOutputParentSignal
Assert-ArchiveWriteBoundary

Add-Type -AssemblyName System.IO.Compression
try {
  Assert-ArchiveWriteBoundary
  $createdOutputCanonical = Resolve-CanonicalPath $output
  $stream = [IO.FileStream]::new($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $createdOutput = $true
  try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try {
      $zipEpoch = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
      foreach ($row in $rows) {
        $entry = $zip.CreateEntry([string]$row.Relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $zipEpoch
        $entryStream = $entry.Open()
        try { Copy-ApprovedFile $row $entryStream }
        finally { $entryStream.Dispose() }
      }
    } finally { $zip.Dispose() }
  } finally { $stream.Dispose() }

  $afterArchiveSnapshot = [object[]]@(Get-PackageSnapshot $package)
  if (-not (Test-SamePackageSnapshot $approvedSnapshot $afterArchiveSnapshot)) {
    throw 'PACKAGE_MUTATED_DURING_ARCHIVE'
  }
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'RELEASE' $true
  Assert-CanonicalContainment $repo $allowedRoot $package 'PACKAGE'

  Assert-ArchiveWriteBoundary
  $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
  Assert-ArchiveWriteBoundary
  $createdSidecarCanonical = Resolve-CanonicalPath $sidecar
  $sidecarStream = [IO.FileStream]::new($sidecar, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $createdSidecar = $true
  try {
    $bytes = [Text.Encoding]::ASCII.GetBytes(("{0}  {1}`n" -f $hash, [IO.Path]::GetFileName($output)))
    $sidecarStream.Write($bytes, 0, $bytes.Length)
  } finally { $sidecarStream.Dispose() }
  Assert-CleanProducingRepo $SourceCommit
} catch {
  if ($createdSidecar) { Remove-OwnedArchiveFile $sidecar $createdSidecarCanonical }
  if ($createdOutput) { Remove-OwnedArchiveFile $output $createdOutputCanonical }
  throw
}

Write-Output ("ARCHIVE_OK files={0} sha256={1} output={2}" -f $rows.Count, $hash, $output)
exit 0
