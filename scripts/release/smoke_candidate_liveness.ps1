<#
  Runs the packaged executable long enough to collect a local startup-liveness gate.

  The harness owns exactly one Process instance and never searches by process name. A child that
  remains alive at the deadline is terminated through that owned handle so unrelated processes,
  including same-name processes, cannot be affected.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [Parameter(Mandatory = $true)][string]$ExecutablePath,
  [Parameter(Mandatory = $true)][string]$OutputJson,
  [ValidateRange(1, 3600)][int]$Seconds = 12,
  [string]$Arguments = '',
  [Parameter(DontShow = $true)][string]$TestPauseBeforeEvidenceWriteSignal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ContainedPath([string]$Root, [string]$Candidate, [bool]$AllowRoot = $false) {
  $relative = [IO.Path]::GetRelativePath($Root, $Candidate)
  if ([IO.Path]::IsPathRooted($relative)) { return $false }
  if ([string]::IsNullOrWhiteSpace($relative) -or $relative -ceq '.') { return $AllowRoot }
  return -not ($relative -ceq '..' -or
    $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
    $relative.StartsWith('../', [StringComparison]::Ordinal))
}

function Assert-NoReparseComponents([string]$Root, [string]$Candidate, [string]$Prefix) {
  if (-not (Test-ContainedPath $Root $Candidate $true)) { throw "${Prefix}_OUTSIDE_ROOT:$Candidate" }
  $cursor = $Root
  $rootItem = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "${Prefix}_REPARSE_FORBIDDEN:$($rootItem.FullName)"
  }
  $relative = [IO.Path]::GetRelativePath($Root, $Candidate)
  $segments = [string[]]@($relative -split '[\\/]+' | Where-Object { $_ -and $_ -ne '.' })
  for ($index = 0; $index -lt $segments.Count; $index++) {
    $cursor = Join-Path $cursor $segments[$index]
    if (-not (Test-Path -LiteralPath $cursor)) { break }
    $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "${Prefix}_REPARSE_FORBIDDEN:$($item.FullName)"
    }
    if ($item -isnot [IO.DirectoryInfo] -and $index -lt ($segments.Count - 1)) {
      throw "${Prefix}_ANCESTOR_NOT_DIRECTORY:$($item.FullName)"
    }
  }
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

function Read-RedirectedTextBounded(
  [Threading.Tasks.Task[string]]$Task,
  [IO.StreamReader]$Reader,
  [int]$Milliseconds = 750
) {
  if ($null -eq $Task -or $null -eq $Reader) { return '' }
  try {
    if ($Task.Wait($Milliseconds) -and $Task.IsCompletedSuccessfully) { return [string]$Task.Result }
  } catch { }
  try { $Reader.Dispose() } catch { }
  if ($Task.IsCompletedSuccessfully) { return [string]$Task.Result }
  return ''
}

function Assert-EvidencePathsDisjoint([string]$PackagePath, [string[]]$EvidencePaths) {
  $packageResolved = Resolve-CanonicalPath $PackagePath
  foreach ($ownedOutput in $EvidencePaths) {
    $ownedCanonical = Resolve-CanonicalPath $ownedOutput
    if ((Test-ContainedPath $packageResolved $ownedCanonical $true) -or
        (Test-ContainedPath $ownedCanonical $packageResolved $true)) {
      throw "EVIDENCE_CANONICAL_OVERLAP_PACKAGE:path=$ownedOutput,canonical=$ownedCanonical"
    }
  }
}

function Invoke-TestEvidenceWritePause([string]$SignalPath) {
  if ([string]::IsNullOrWhiteSpace($SignalPath)) { return }
  if ($env:FFX_RELEASE_LIVENESS_TEST_HOOK -cne 'evidence-write-pause-v1') { throw 'LIVENESS_TEST_HOOK_NOT_AUTHORIZED' }
  $signal = [IO.Path]::GetFullPath($SignalPath)
  $signalCanonical = Resolve-CanonicalPath $signal
  $packageResolved = Resolve-CanonicalPath $package
  if ((Test-ContainedPath $packageResolved $signalCanonical $true) -or
      (Test-ContainedPath $signalCanonical $packageResolved $true)) {
    throw 'LIVENESS_TEST_HOOK_SIGNAL_OVERLAPS_PACKAGE'
  }
  $signalParent = Split-Path $signal -Parent
  if (-not (Test-Path -LiteralPath $signalParent -PathType Container)) { throw 'LIVENESS_TEST_HOOK_PARENT_MISSING' }
  $continueSignal = $signal + '.continue'
  if ((Test-Path -LiteralPath $signal) -or (Test-Path -LiteralPath $continueSignal)) { throw 'LIVENESS_TEST_HOOK_SIGNAL_EXISTS' }
  $signalStream = [IO.FileStream]::new($signal, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $signalStream.Dispose()
  $deadline = [datetime]::UtcNow.AddSeconds(10)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $continueSignal -PathType Leaf) { return }
    Start-Sleep -Milliseconds 25
  }
  throw 'LIVENESS_TEST_HOOK_CONTINUE_TIMEOUT'
}

function Write-Utf8TextCreateNew([string]$Path, [string]$Content) {
  $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Content)
    $stream.Write($bytes, 0, $bytes.Length)
  } finally {
    $stream.Dispose()
  }
}

$package = (Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $package -PathType Container)) { throw "PackageRoot is not a directory: $package" }
$executable = if ([IO.Path]::IsPathRooted($ExecutablePath)) {
  [IO.Path]::GetFullPath($ExecutablePath)
} else {
  [IO.Path]::GetFullPath((Join-Path $package $ExecutablePath))
}
if (-not (Test-ContainedPath $package $executable)) { throw "ExecutablePath must be inside PackageRoot: $executable" }
Assert-NoReparseComponents $package $executable 'EXECUTABLE'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "ExecutablePath is missing: $executable" }

$manifestPath = Join-Path $package 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "release-manifest.json is missing: $manifestPath" }
foreach ($path in @($package, $executable)) {
  $item = Get-Item -LiteralPath $path -Force
  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse paths are forbidden for liveness evidence: $path" }
}

$output = [IO.Path]::GetFullPath($OutputJson)
$outputParent = Split-Path $output -Parent
if (Test-ContainedPath $package $output $true) { throw "OutputJson must not mutate PackageRoot: $output" }
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) { throw "OutputJson parent is missing: $outputParent" }
$stdoutPath = [IO.Path]::ChangeExtension($output, 'stdout.log')
$stderrPath = [IO.Path]::ChangeExtension($output, 'stderr.log')
$evidencePaths = [string[]]@($output, $stdoutPath, $stderrPath)
Assert-EvidencePathsDisjoint $package $evidencePaths
foreach ($ownedOutput in @($output, $stdoutPath, $stderrPath)) {
  if (Test-Path -LiteralPath $ownedOutput) { throw "Liveness evidence output already exists: $ownedOutput" }
}

$manifestSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$executableSha = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$startedUtc = [datetime]::UtcNow
$deadlineUtc = $startedUtc.AddSeconds($Seconds)
$process = $null
$stdoutTask = $null
$stderrTask = $null
$stdoutReader = $null
$stderrReader = $null
$spawnedPid = $null
$livenessAtDeadline = $false
$exitCode = $null
$exitBehavior = 'start-failed'
$cleanupSucceeded = $false
$failure = $null

try {
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $executable
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.WorkingDirectory = $package
  $startInfo.Arguments = $Arguments

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) { throw 'Process.Start returned false.' }
  $spawnedPid = [int]$process.Id
  $stdoutReader = $process.StandardOutput
  $stderrReader = $process.StandardError
  $stdoutTask = $stdoutReader.ReadToEndAsync()
  $stderrTask = $stderrReader.ReadToEndAsync()

  $exited = $process.WaitForExit($Seconds * 1000)
  if ($exited) {
    $livenessAtDeadline = $false
    $exitCode = [int]$process.ExitCode
    $exitBehavior = 'exited-before-deadline'
    $cleanupSucceeded = $true
  } else {
    $livenessAtDeadline = $true
    $exitBehavior = 'terminated-owned-after-deadline'
    $process.Kill($false)
    if (-not $process.WaitForExit(5000)) { throw 'Owned process did not terminate after Kill.' }
    $exitCode = [int]$process.ExitCode
    $cleanupSucceeded = $true
  }
} catch {
  $failure = $_.Exception.Message
  if ($null -ne $process) {
    try {
      if (-not $process.HasExited) { $process.Kill($false); [void]$process.WaitForExit(5000) }
      $cleanupSucceeded = $process.HasExited
    } catch { $cleanupSucceeded = $false }
  }
} finally {
  try {
    $stdout = Read-RedirectedTextBounded $stdoutTask $stdoutReader
    $stderr = Read-RedirectedTextBounded $stderrTask $stderrReader
    Invoke-TestEvidenceWritePause $TestPauseBeforeEvidenceWriteSignal
    Assert-EvidencePathsDisjoint $package $evidencePaths
    Write-Utf8TextCreateNew $stdoutPath $stdout
    Assert-EvidencePathsDisjoint $package $evidencePaths
    Write-Utf8TextCreateNew $stderrPath $stderr
  } finally {
    if ($null -ne $process) { $process.Dispose() }
  }
}

$endedUtc = [datetime]::UtcNow
$passed = $livenessAtDeadline -and $cleanupSucceeded -and [string]::IsNullOrEmpty($failure)
$evidence = [ordered]@{
  schemaVersion = 1
  result = if ($passed) { 'PASS' } else { 'FAIL' }
  packageRoot = $package
  executablePath = $executable
  manifestSha256 = $manifestSha
  executableSha256 = $executableSha
  spawnedPid = $spawnedPid
  seconds = $Seconds
  startedUtc = $startedUtc.ToString('o')
  deadlineUtc = $deadlineUtc.ToString('o')
  endedUtc = $endedUtc.ToString('o')
  livenessAtDeadline = $livenessAtDeadline
  exitBehavior = $exitBehavior
  exitCode = $exitCode
  cleanupOwnedProcessSucceeded = $cleanupSucceeded
  stdoutPath = $stdoutPath
  stderrPath = $stderrPath
  failure = $failure
}
Assert-EvidencePathsDisjoint $package $evidencePaths
Write-Utf8TextCreateNew $output ($evidence | ConvertTo-Json -Depth 5)

if ($passed) {
  Write-Output ("LIVENESS_OK pid={0} seconds={1} evidence={2}" -f $spawnedPid, $Seconds, $output)
  exit 0
}
Write-Output ("LIVENESS_FAIL pid={0} behavior={1} evidence={2}" -f $spawnedPid, $exitBehavior, $output)
exit 1
