<#
  FFX Mod Studio — generate exact release contracts from one already-published diagnostic payload.

  This script is intentionally not a bypass: it fails when first-party assemblies contain unreviewed
  path classes, when text findings occur outside vendored/legal/tool documentation, or when any CDN
  finding exists. Every accepted finding and bundled helper byte is pinned by path, size and SHA-256.
#>
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [Parameter(DontShow = $true)][string]$TestPauseBeforeContractWritesSignal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repo 'work\release-readiness')).TrimEnd('\', '/')
$officialRelease = [IO.Path]::GetFullPath((Join-Path $repo 'release')).TrimEnd('\', '/')
$outputFull = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) {
  $OutputDirectory
} else {
  Join-Path $repo $OutputDirectory
})).TrimEnd('\', '/')

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

function Assert-ContractOutputDisjoint([string]$PackagePath, [string]$OutputPath) {
  $packageResolved = Resolve-CanonicalPath $PackagePath
  $outputResolved = Resolve-CanonicalPath $OutputPath
  if ((Test-ContainedPath $packageResolved $outputResolved $true) -or
      (Test-ContainedPath $outputResolved $packageResolved $true)) {
    throw "OUTPUT_DIRECTORY_CANONICAL_OVERLAP_PACKAGE_ROOT:output=$outputResolved,package=$packageResolved"
  }
}

function Invoke-TestContractWritePause([string]$SignalPath) {
  if ([string]::IsNullOrWhiteSpace($SignalPath)) { return }
  if ($env:FFX_RELEASE_CONTRACT_TEST_HOOK -cne 'write-pause-v1') { throw 'CONTRACT_TEST_HOOK_NOT_AUTHORIZED' }
  $signal = [IO.Path]::GetFullPath($SignalPath)
  $signalCanonical = Resolve-CanonicalPath $signal
  $packageResolved = Resolve-CanonicalPath $package
  if ((Test-ContainedPath $packageResolved $signalCanonical $true) -or
      (Test-ContainedPath $signalCanonical $packageResolved $true)) {
    throw 'CONTRACT_TEST_HOOK_SIGNAL_OVERLAPS_PACKAGE'
  }
  $signalParent = Split-Path $signal -Parent
  if (-not (Test-Path -LiteralPath $signalParent -PathType Container)) { throw 'CONTRACT_TEST_HOOK_PARENT_MISSING' }
  $continueSignal = $signal + '.continue'
  if ((Test-Path -LiteralPath $signal) -or (Test-Path -LiteralPath $continueSignal)) { throw 'CONTRACT_TEST_HOOK_SIGNAL_EXISTS' }
  $signalStream = [IO.FileStream]::new($signal, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  $signalStream.Dispose()
  $deadline = [datetime]::UtcNow.AddSeconds(10)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $continueSignal -PathType Leaf) { return }
    Start-Sleep -Milliseconds 25
  }
  throw 'CONTRACT_TEST_HOOK_CONTINUE_TIMEOUT'
}

Assert-CanonicalContainment $repo $releaseRoot $releaseRoot 'OUTPUT_DIRECTORY' $true
Assert-CanonicalContainment $repo $releaseRoot $outputFull 'OUTPUT_DIRECTORY'
if (-not [IO.Path]::GetFileName($outputFull).Equals('contracts-staging', [StringComparison]::OrdinalIgnoreCase)) {
  throw "OUTPUT_DIRECTORY_MUST_END_IN_CONTRACTS_STAGING:$outputFull"
}
if (Test-ContainedPath $outputFull $officialRelease $true) {
  throw "OUTPUT_DIRECTORY_IS_OFFICIAL_RELEASE_ANCESTOR:$outputFull"
}
$package = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\', '/')
Assert-CanonicalContainment $repo $releaseRoot $package 'PACKAGE_ROOT'
Assert-ContractOutputDisjoint $package $outputFull
if (Test-ContainedPath $package $outputFull $true) {
  throw "OUTPUT_DIRECTORY_MUST_NOT_MUTATE_PACKAGE_ROOT:$outputFull"
}
if (Test-Path -LiteralPath $outputFull) {
  if (-not (Test-Path -LiteralPath $outputFull -PathType Container)) { throw "OUTPUT_DIRECTORY_NOT_DIRECTORY:$outputFull" }
  if (@(Get-ChildItem -LiteralPath $outputFull -Force).Count -gt 0) { throw "OUTPUT_DIRECTORY_NOT_EMPTY:$outputFull" }
}

$manifestPath = Join-Path $package 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
  throw "release-manifest.json is missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2 -or $manifest.target -cne 'win-x64' -or $manifest.selfContained -ne $true) {
  throw 'Package manifest is not a self-contained win-x64 schema-v2 manifest.'
}

function Get-RelativePath([IO.FileSystemInfo]$item) {
  return $item.FullName.Substring($package.Length).TrimStart('\','/').Replace('\','/')
}

function Get-Sha256([string]$path) {
  return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-PeClassification([string]$path) {
  $stream = [IO.File]::OpenRead($path)
  try {
    $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
    $headers = $reader.PEHeaders
    if ($null -eq $headers -or $null -eq $headers.PEHeader) { throw [IO.InvalidDataException]::new('not PE') }
    if ($reader.HasMetadata) { return @('managed','managed-any') }
    $machine = $headers.CoffHeader.Machine.ToString()
    if ($machine -ceq 'Amd64') { return @('native','x64') }
    if ($machine -ceq 'I386') { return @('native','x86') }
    throw "Unsupported native PE machine '$machine': $path"
  } catch [IO.InvalidDataException] {
    return @('data','data')
  } catch [BadImageFormatException] {
    return @('data','data')
  } finally {
    $stream.Dispose()
  }
}

function New-ApprovedPayload([IO.FileInfo]$file) {
  $kindArch = @(Read-PeClassification $file.FullName)
  return [ordered]@{
    path = Get-RelativePath $file
    sha256 = Get-Sha256 $file.FullName
    bytes = [long]$file.Length
    kind = [string]$kindArch[0]
    arch = [string]$kindArch[1]
    target = 'win-x64'
  }
}

function Get-FilesUnder([string]$relativeRoot) {
  $root = Join-Path $package $relativeRoot.Replace('/', [IO.Path]::DirectorySeparatorChar)
  if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Bundled root missing: $relativeRoot" }
  return @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)
}

function Get-ExactFiles([string[]]$relativePaths) {
  $files = @()
  foreach ($relative in $relativePaths) {
    $full = Join-Path $package $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Bundled file missing: $relative" }
    $files += Get-Item -LiteralPath $full
  }
  return @($files)
}

function New-BundledEntry(
  [string]$id,
  [string]$classification,
  [string]$executionModel,
  [string[]]$fileNames,
  [string[]]$globs,
  [IO.FileInfo[]]$files,
  [string]$consumer,
  [string]$provenance) {
  $entry = [ordered]@{
    id = $id
    consumer = $consumer
    classification = $classification
    executionModel = $executionModel
    provenance = $provenance
  }
  if ($fileNames.Count -gt 0) { $entry.payloadFileNames = @($fileNames) }
  if ($globs.Count -gt 0) { $entry.payloadGlobs = @($globs) }
  $entry.approvedPayloads = @($files | Sort-Object FullName | ForEach-Object { New-ApprovedPayload $_ })
  return $entry
}

function Invoke-ScannerJson([string]$scriptName) {
  $stdout = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $scriptName) -Root $package -Json
  $code = $LASTEXITCODE
  if ($code -notin @(0,2)) { throw "$scriptName failed with exit $code" }
  try { return $stdout | ConvertFrom-Json -NoEnumerate -ErrorAction Stop }
  catch { throw "$scriptName did not return valid JSON: $($_.Exception.Message)" }
}

$allFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName)
$topLevel = @($allFiles | Where-Object { (Get-RelativePath $_) -notmatch '/' } |
  ForEach-Object { Get-RelativePath $_ } | Sort-Object -Unique)
$allowedRoots = @(Get-ChildItem -LiteralPath $package -Directory | Sort-Object Name |
  ForEach-Object { $_.Name + '/' })

$textScan = Invoke-ScannerJson 'scan_portability.ps1'
$assemblyScan = Invoke-ScannerJson 'scan_assembly_portability.ps1'
$scanEntries = New-Object 'Collections.Generic.List[object]'

$textGroups = @($textScan.matches | Group-Object {
  $path = ([string]$_.file).Replace('\','/')
  @($path,[string]$_.pattern,[string]$_.class) -join "`n"
})
foreach ($group in $textGroups) {
  $first = $group.Group[0]
  $path = ([string]$first.file).Replace('\','/')
  $class = [string]$first.class
  $pattern = [string]$first.pattern
  if ($class -ceq 'FORBIDDEN_CDN') { throw "Forbidden CDN text finding: $path" }
  $isVendor = $path -cmatch '^viewers/(?:map|model|magic)/vendor/' -or $path -cmatch '^viewers/noclip/'
  $isLegal = $path -cmatch '^(?:licenses/|runtime/java/legal/)' -or $path -cmatch '^tools/.+[.](?:txt|md|html?|xml|json)$'
  if (-not $isVendor -and -not $isLegal) { throw "Unallowlisted first-party text finding: $path ($class/$pattern)" }
  if ($isLegal -and ($class -cne 'UNCLASSIFIED' -or $pattern -cne 'URL')) {
    throw "Illegal non-URL exception in legal/tool documentation: $path ($class/$pattern)"
  }
  $file = Join-Path $package $path.Replace('/', [IO.Path]::DirectorySeparatorChar)
  $scanEntries.Add([ordered]@{
    scanner = 'text'; path = $path; sha256 = Get-Sha256 $file
    pattern = $pattern; class = $class
    maxCount = [int](($group.Group | Measure-Object -Property count -Sum).Sum)
  })
}

$assemblyGroups = @($assemblyScan.matches | Group-Object {
  @([string]$_.file,[string]$_.sha256,[string]$_.pattern,[string]$_.class,[string]$_.encoding) -join "`n"
})
foreach ($group in $assemblyGroups) {
  $first = $group.Group[0]
  $path = ([string]$first.file).Replace('\','/')
  $class = [string]$first.class
  $pattern = [string]$first.pattern
  if ($class -ceq 'FORBIDDEN_CDN') { throw "Forbidden CDN assembly finding: $path" }

  $isAppDll = $path -ceq 'FFXProjectEditor.dll'
  $isSatellite = $path -cmatch '^[a-z]{2}/FFXProjectEditor[.]resources[.]dll$'
  if ($isAppDll) {
    # Reviewed exceptions (2026-09-08, release v2.234.9.0 triage):
    # - unc-device: FileSystemReparseGuard builds \\?\UNC\ device paths for reparse detection
    #   (Windows technical prefix, not a user path; no drive letter involved).
    # - drive-absolute with value 'C:\Program Files (x86)\FMOD SoundSystem': standard machine-wide
    #   FMOD SDK install roots used as search fallbacks (never owner-specific).
    #   The owner-drive FMOD entries (D:\FMOD, C:\FMOD) were removed in this release.
    $approvedFirstParty = $class -ceq 'REMOTE_URL' -or $class -ceq 'DERIVED_GAME_DATA' -or
      ($class -ceq 'DEV_REFERENCE' -and $pattern -cin @('runtime-tools','ffx-extracted','steamlibrary')) -or
      ($class -ceq 'PATH_CANDIDATE' -and $pattern -ceq 'unc-device') -or
      ($class -ceq 'PATH_CANDIDATE' -and $pattern -ceq 'drive-absolute' -and
        $first.value -ceq 'C:\Program Files (x86)\FMOD SoundSystem') -or
      # work-path: relative lane work-dir override path constant in SeidFsbMapLoader
      # (work\fev9999_corpus_wave8\seid_to_fsb_sample.json; relative, resolved from repo root, no drive).
      ($class -ceq 'DEV_REFERENCE' -and $pattern -ceq 'work-path')
    if (-not $approvedFirstParty) { throw "Unreviewed product assembly finding: $path ($class/$pattern)" }
  }
  if ($isSatellite) {
    $approvedSatellite = $class -ceq 'DERIVED_GAME_DATA' -or
      ($class -ceq 'DEV_REFERENCE' -and $pattern -ceq 'runtime-tools')
    if (-not $approvedSatellite) { throw "Unreviewed satellite finding: $path ($class/$pattern)" }
  }

  $scanEntries.Add([ordered]@{
    scanner = 'assembly'; path = $path; sha256 = [string]$first.sha256
    pattern = $pattern; class = $class; encoding = [string]$first.encoding
    maxCount = [int](($group.Group | Measure-Object -Property count -Sum).Sum)
  })
}

$scanAllowlist = @(
  $scanEntries |
    ForEach-Object { [pscustomobject]$_ } |
    Sort-Object -Property {
      $enc = if ($null -ne $_.PSObject.Properties['encoding']) { [string]$_.encoding } else { '' }
      "@($_.scanner)|$($_.path)|$($_.pattern)|$($_.class)|$enc"
    }
)

# ── Official negative payload closure ──
# Keep these arrays literal so the verifier harness can prove parity with the
# checked-in contract without regenerating hashes from an unreviewed payload.
$officialDenyPaths = @(
  'Assets/Audio/',
  'data/FinalFantasyX/',
  'work/',
  'obj/',
  '.git/',
  'bin/',
  'ExternalLibs/NoclipViewer/dist/',
  'FFX.exe',
  'assets/Saves/',
  'assets/Dumps/',
  'SpiraForge/'
)
$officialDenyExtensions = @(
  '.wav',
  '.fsb',
  '.fev',
  '.iso',
  '.vbf',
  '.pdb',
  '.i64',
  '.id0',
  '.id1',
  '.id2',
  '.nam',
  '.til'
)
$officialDenyPatterns = @(
  'unpkg.com',
  'cdn.jsdelivr.net',
  'NoclipDataRepair',
  'repair_noclip_data'
)

$packagePolicy = [ordered]@{
  '$comment' = 'Generated from one reviewed diagnostic payload. Exact hashes/counts are release gates, not wildcards. SpiraForge/ can remain in historical allowedRoots while deny.paths keeps that payload paused until public-baseline closure is regenerated.'
  schemaVersion = 1
  components = [ordered]@{
    'studio-core-win-x64' = [ordered]@{
      target = 'win-x64'
      selfContained = $true
      hardLimitBytes = 1073741824
      warnLimitBytes = 805306368
      requiredFiles = @(
        'FFXProjectEditor.exe','FFXProjectEditor.dll','FFXProjectEditor.deps.json',
        'FFXProjectEditor.runtimeconfig.json','AuroraFieldExplorer/map-entities.csv',
        'release-manifest.json')
      requiredSatellites = @('de','es','fr','it','ja','ko','pt','zh')
      allowedRoots = $allowedRoots
      allowedTopLevel = $topLevel
      deny = [ordered]@{
        paths = @($officialDenyPaths)
        extensions = @($officialDenyExtensions)
        patterns = @($officialDenyPatterns)
      }
      scanAllowlist = $scanAllowlist
      audioPolicy = 'No game audio is packaged; at most ten explicitly selected tracks are imported locally from user-owned files.'
      mustShipNote = 'No public module may be hidden to obtain a green package gate.'
    }
  }
}

$toolEntries = New-Object 'Collections.Generic.List[object]'
$entryFiles = Get-ExactFiles @('Keystone.Net.dll','x64/keystone.dll')
$entry = New-BundledEntry -id 'keystone-native-x64' -classification 'BUNDLED_OWNER_ACCEPTED_RISK' `
  -executionModel 'in-process' -fileNames @('Keystone.Net.dll') -globs @('x64/keystone.dll') `
  -files $entryFiles -consumer 'MemorySharp in-process assembler' `
  -provenance 'Preserved existing dependency; owner accepted redistribution risk.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'ExternalLibs/FFXED'
$entry = New-BundledEntry -id 'ffxed-java-jar' -classification 'BUNDLED_OWNER_ACCEPTED_RISK' `
  -executionModel 'child-process' -fileNames @() -globs @('ExternalLibs/FFXED/**') `
  -files $entryFiles -consumer 'Save editor bridge' `
  -provenance 'Preserved existing FFXED JAR; owner accepted redistribution risk.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'runtime/java'
$entry = New-BundledEntry -id 'java-private-runtime' -classification 'BUNDLED_REDISTRIBUTION_PROVEN' `
  -executionModel 'child-process' -fileNames @() -globs @('runtime/java/**') `
  -files $entryFiles -consumer 'Private runtime for FFXED' `
  -provenance 'Microsoft Build of OpenJDK private jlink runtime.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'tools/vgmstream'
$entry = New-BundledEntry -id 'vgmstream-cli' -classification 'BUNDLED_REDISTRIBUTION_PROVEN' `
  -executionModel 'child-process' -fileNames @() -globs @('tools/vgmstream/**') `
  -files $entryFiles -consumer 'Audio decode and local music import' `
  -provenance 'Vendored vgmstream payload with bundled notices.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'tools/fsbext'
$entry = New-BundledEntry -id 'fsbext' -classification 'BUNDLED_OWNER_ACCEPTED_RISK' `
  -executionModel 'child-process' -fileNames @() -globs @('tools/fsbext/**') `
  -files $entryFiles -consumer 'FSB extraction' `
  -provenance 'Preserved existing helper; owner accepted redistribution risk.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'tools/fsbankcl'
$entry = New-BundledEntry -id 'fsbankcl' -classification 'BUNDLED_OWNER_ACCEPTED_RISK' `
  -executionModel 'child-process' -fileNames @() -globs @('tools/fsbankcl/**') `
  -files $entryFiles -consumer 'FSB authoring' `
  -provenance 'Preserved existing FMOD-adjacent helper; owner accepted redistribution risk.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'tools/PhyreMapExportLab'
$entry = New-BundledEntry -id 'phyre-map-export-lab' -classification 'BUNDLED_HELPER' `
  -executionModel 'child-process' -fileNames @() -globs @('tools/PhyreMapExportLab/**') `
  -files $entryFiles -consumer 'On-demand Phyre map export' `
  -provenance 'Built from this repository as a self-contained win-x64 helper.'
$toolEntries.Add($entry)

$entryFiles = Get-ExactFiles @('Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll')
$entry = New-BundledEntry -id 'webview2-sdk-runtime' -classification 'IN_PROCESS_BUNDLED' `
  -executionModel 'in-process' -fileNames @('Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll') -globs @() `
  -files $entryFiles -consumer 'Embedded ViewerShell' `
  -provenance 'Microsoft WebView2 SDK loader and managed core.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'prerequisites/webview2'
$entry = New-BundledEntry -id 'webview2-offline-installer' -classification 'BUNDLED_REDISTRIBUTION_PROVEN' `
  -executionModel 'child-process' -fileNames @() -globs @('prerequisites/webview2/**') `
  -files $entryFiles -consumer 'Offline WebView2 prerequisite repair' `
  -provenance 'Official Microsoft Authenticode-signed installer.'
$toolEntries.Add($entry)

$entryFiles = Get-FilesUnder 'prerequisites/vc2013'
$entry = New-BundledEntry -id 'vcpp2013-x64-installer' -classification 'BUNDLED_REDISTRIBUTION_PROVEN' `
  -executionModel 'child-process' -fileNames @() -globs @('prerequisites/vc2013/**') `
  -files $entryFiles -consumer 'Keystone VC++ runtime prerequisite' `
  -provenance 'Official Microsoft Authenticode-signed installer.'
$toolEntries.Add($entry)
$toolEntries.Add([ordered]@{
  id = 'vbfextract'; consumer = 'Optional archive extraction'; classification = 'BLOCKED'
  payloadFileNames = @('VBFExtract.exe'); payloadGlobs = @('tools/vbfextract/**')
  provenance = 'Not shipped; user-provided extraction or clean-room alternatives only.'
})
$toolEntries.Add([ordered]@{
  id = 'node-runtime'; consumer = 'Build tooling only'; classification = 'BUILD_ONLY'
  payloadGlobs = @('tools/node/**')
})
$toolEntries.Add([ordered]@{
  id = 'python-runtime'; consumer = 'Development tooling only'; classification = 'DEV_ONLY'
  payloadGlobs = @('tools/python/**')
})

$toolPolicy = [ordered]@{
  '$comment' = 'Functional and provenance closure for every executable/helper payload in the reviewed win-x64 package.'
  schemaVersion = 1
  entries = $toolEntries.ToArray()
  denyByDefault = @('vbfextract','node-runtime','python-runtime','noesis','FMOD SDK')
  pendingFullClosure = $false
}

$packageJson = $packagePolicy | ConvertTo-Json -Depth 12
$toolJson = $toolPolicy | ConvertTo-Json -Depth 12
New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
Assert-CanonicalContainment $repo $releaseRoot $releaseRoot 'OUTPUT_DIRECTORY' $true
Assert-CanonicalContainment $repo $releaseRoot $outputFull 'OUTPUT_DIRECTORY'
Assert-ContractOutputDisjoint $package $outputFull
if (@(Get-ChildItem -LiteralPath $outputFull -Force).Count -gt 0) {
  throw "OUTPUT_DIRECTORY_NOT_EMPTY_AFTER_CREATE:$outputFull"
}
Invoke-TestContractWritePause $TestPauseBeforeContractWritesSignal
$packageCandidate = Join-Path $outputFull 'package-allowlist.json'
$toolCandidate = Join-Path $outputFull 'tool-dependencies.json'
foreach ($candidate in @(
  [pscustomobject]@{ Path = $packageCandidate; Json = $packageJson },
  [pscustomobject]@{ Path = $toolCandidate; Json = $toolJson }
)) {
  Assert-CanonicalContainment $repo $releaseRoot $releaseRoot 'OUTPUT_DIRECTORY' $true
  Assert-CanonicalContainment $repo $releaseRoot $outputFull 'OUTPUT_DIRECTORY'
  Assert-CanonicalContainment $repo $releaseRoot $candidate.Path 'OUTPUT_FILE'
  Assert-ContractOutputDisjoint $package $outputFull
  Assert-ContractOutputDisjoint $package $candidate.Path
  $stream = [IO.FileStream]::new($candidate.Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
  try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]$candidate.Json)
    $stream.Write($bytes, 0, $bytes.Length)
  } finally {
    $stream.Dispose()
  }
}

Write-Output ('CONTRACTS_GENERATED output={0} packageFiles={1} scanPins={2} toolEntries={3} approvedPayloads={4}' -f
  $outputFull,
  $allFiles.Count,
  $scanAllowlist.Count,
  $toolEntries.Count,
  (@($toolEntries | ForEach-Object {
    if ($null -ne $_.PSObject.Properties['approvedPayloads']) { @($_.approvedPayloads).Count } else { 0 }
  } | Measure-Object -Sum).Sum))
