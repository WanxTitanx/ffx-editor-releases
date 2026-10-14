<#
  FFX Mod Studio portable builder for Diagnostic and Candidate packages.

  The builder never publishes outside work/release-readiness, never reuses a non-empty output,
  and propagates one exact mode through preflight, MSBuild runtime verification, manifest, and
  package verification. Release remains unavailable until human/version gates exist.
#>
[CmdletBinding()]
param(
  [ValidateSet('Diagnostic', 'Candidate')][string]$Mode = 'Diagnostic',
  [string]$OutDir,
  [Parameter(DontShow = $true)][string]$DotnetCommand = 'dotnet'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
$proj = Join-Path $repo 'FFXProjectEditor\FFXProjectEditor.csproj'

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

function Get-SafeOutputFiles([string]$Root) {
  $rootItem = Get-Item -LiteralPath $Root -Force -ErrorAction Stop
  if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'OUTPUT_TREE_REPARSE_FORBIDDEN:.'
  }
  $stack = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
  $stack.Push([IO.DirectoryInfo]$rootItem)
  $files = [Collections.Generic.List[IO.FileInfo]]::new()
  while ($stack.Count -gt 0) {
    $directory = $stack.Pop()
    foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force) {
      $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "OUTPUT_TREE_REPARSE_FORBIDDEN:$relative"
      }
      if ($item -is [IO.DirectoryInfo]) { $stack.Push($item) }
      elseif ($item -is [IO.FileInfo]) { $files.Add($item) }
    }
  }
  return $files.ToArray()
}

# Validate the destination before preflight/publish so an invalid path cannot create or remove data.
if (-not $OutDir) { $OutDir = Join-Path $repo ("work\release-readiness\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '\package') }
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
$outFull = [System.IO.Path]::GetFullPath($OutDir).TrimEnd('\', '/')
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repo 'work\release-readiness')).TrimEnd('\', '/')
try {
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'OUTDIR' $true
  Assert-CanonicalContainment $repo $allowedRoot $outFull 'OUTDIR'
}
catch { Write-Error $_.Exception.Message; exit 2 }
if (Test-Path -LiteralPath $outFull) {
  if (-not (Test-Path -LiteralPath $outFull -PathType Container)) { Write-Error "OUTDIR_NOT_DIRECTORY:$outFull"; exit 2 }
  if (@(Get-ChildItem -LiteralPath $outFull -Force).Count -gt 0) { Write-Error "OUTDIR_NOT_EMPTY:$outFull"; exit 2 }
}

# Candidate preflight enforces a clean source; Diagnostic retains the evidence-only dirty mode.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'preflight_release.ps1') -Mode $Mode -Json | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "preflight failed (mode=$Mode, exit=$LASTEXITCODE)"; exit 1 }

New-Item -ItemType Directory -Force -Path $outFull | Out-Null
try {
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'OUTDIR' $true
  Assert-CanonicalContainment $repo $allowedRoot $outFull 'OUTDIR'
}
catch { Write-Error $_.Exception.Message; exit 2 }
if (@(Get-ChildItem -LiteralPath $outFull -Force).Count -gt 0) {
  Write-Error "OUTDIR_NOT_EMPTY_AFTER_CREATE:$outFull"
  exit 2
}

$diskBefore = @(Get-Volume | Where-Object DriveLetter | Measure-Object -Property SizeRemaining -Sum).Sum

# 3) publish (self-contained win-x64; assets dev-online ficam FORA por default no csproj)
& $DotnetCommand publish $proj -c Release -p:PublishProfile=win-x64-portable -p:RuntimeIdentifier=win-x64 `
  "-p:FFXRuntimeVerificationMode=$Mode" --nologo -o $outFull
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed (mode=$Mode, exit=$LASTEXITCODE)"; exit 1 }

# Rebind trust after the external publisher returns, before any recursive delete or manifest write.
try {
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'OUTDIR' $true
  Assert-CanonicalContainment $repo $allowedRoot $outFull 'OUTDIR'
  $trustedPublishedFiles = [IO.FileInfo[]]@(Get-SafeOutputFiles $outFull)
} catch {
  Write-Error ('POST_PUBLISH_TRUST_FAILED:' + $_.Exception.Message)
  exit 2
}

# PDBs are forbidden in the public ZIP. Delete only files obtained from the reparse-free snapshot.
$trustedPublishedFiles | Where-Object { $_.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) } |
  Remove-Item -Force
try {
  Assert-CanonicalContainment $repo $allowedRoot $allowedRoot 'OUTDIR' $true
  Assert-CanonicalContainment $repo $allowedRoot $outFull 'OUTDIR'
  $trustedPublishedFiles = [IO.FileInfo[]]@(Get-SafeOutputFiles $outFull)
} catch {
  Write-Error ('POST_PUBLISH_TRUST_FAILED:' + $_.Exception.Message)
  exit 2
}

# 4) release-manifest v2 (sourceCommit, sourceDirty, SDK, todos os arquivos, bytes, sha256, origin)
$branch = (git -C $repo branch --show-current)
$head = (git -C $repo rev-parse HEAD)
$dirty = @(git -C $repo status --porcelain=v1 --untracked-files=all).Count
$sdk = (& $DotnetCommand --version)
$files = @()
foreach ($f in $trustedPublishedFiles | Sort-Object FullName) {
  $rel = $f.FullName.Substring($outFull.Length).TrimStart('\', '/').Replace('\', '/')
  $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLower()
  $origin = 'runtime'
  if ($rel -match '^FFXProjectEditor\.(exe|dll|deps\.json|runtimeconfig\.json)$') { $origin = 'app' }
  elseif ($rel -match '^[a-z]{2}/FFXProjectEditor\.resources\.dll$') { $origin = 'satellite' }
  elseif ($rel -match '^(tools/|ExternalLibs/FFXED/|x64/keystone\.dll$|Keystone\.Net\.dll$)') { $origin = 'tool' }
  elseif ($rel -match '^viewers/') { $origin = 'viewer' }
  elseif ($rel -match '^licenses/' -or $rel -match '\.(md|txt|rtf)$') { $origin = 'doc' }
  elseif ($rel -match '^(assets/|data/|FfxLib/|AuroraFieldExplorer/|SpiraForge/|mods/)') { $origin = 'content' }
  $files += [ordered]@{ path = $rel; bytes = $f.Length; sha256 = $h; origin = $origin }
}
$manifest = [ordered]@{
  schemaVersion = 2
  product = 'FFX Mod Studio'
  target = 'win-x64'
  selfContained = $true
  sourceCommit = $head
  sourceDirty = ($dirty -gt 0)
  buildSdk = $sdk
  buildMode = $Mode
  generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
  files = $files
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outFull 'release-manifest.json') -Encoding utf8NoBOM

# 5) inventory hash (hash do manifest normalizado) + sidecar .sha256 do manifest
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $outFull 'release-manifest.json') -Algorithm SHA256).Hash.ToLower()
$zipLike = Join-Path (Split-Path $outFull -Parent) ([System.IO.Path]::GetFileName($outFull) + '.sha256')
"$manifestHash  release-manifest.json" | Set-Content -LiteralPath $zipLike -Encoding ASCII

# 6) verify_package (fail-closed)
$verifyLog = Join-Path (Split-Path $outFull -Parent) 'verify.log'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify_package.ps1') -PackageRoot $outFull -Mode $Mode *> $verifyLog
$verifyCode = $LASTEXITCODE

$diskAfter = @(Get-Volume | Where-Object DriveLetter | Measure-Object -Property SizeRemaining -Sum).Sum

Write-Output ("PUBLISH_OUT=$outFull")
Write-Output ("MANIFEST_SHA256=$manifestHash")
Write-Output ("DISK_BEFORE_BYTES=$diskBefore DISK_AFTER_BYTES=$diskAfter")
Write-Output ("VERIFY_EXIT=$verifyCode (log: verify.log)")
exit $verifyCode
