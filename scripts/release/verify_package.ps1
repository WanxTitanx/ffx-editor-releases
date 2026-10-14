<#
  FFX Mod Studio — verify_package.ps1 (Block 1, fail-closed).

  Phases are deliberately isolated. A failing phase is emitted immediately so malformed
  contracts cannot cascade into misleading filesystem/hash/runtime findings.

  Exit: 0 = verified · 1 = package/policy finding · 2 = usage/contract/infrastructure.
  With -Json, stdout contains exactly one JSON object and stderr stays empty.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [ValidateSet('Diagnostic', 'Candidate', 'Release')][string]$Mode = 'Diagnostic',
  [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
$validationErrors = New-Object 'Collections.Generic.HashSet[string]'
$infrastructureErrors = New-Object 'Collections.Generic.HashSet[string]'
$warnings = New-Object 'Collections.Generic.HashSet[string]'
$phase = 'usage'
$pkgFull = $null
$stats = [ordered]@{ totalFiles = 0; totalBytes = 0; manifestEntries = 0 }
$scanResults = [ordered]@{ text = $null; assembly = $null }
$runtimePrerequisitePins = New-Object Collections.Generic.List[object]
$runtimeStrictModes = [string[]]@()
$runtimeForbiddenDependencies = [string[]]@()
$runtimeForbiddenPayloadPaths = [string[]]@()
$dotnetRuntimeVersionPin = $null

function Add-ValidationError([string]$message) { [void]$script:validationErrors.Add($message) }
function Add-InfrastructureError([string]$message) { [void]$script:infrastructureErrors.Add($message) }
function Add-Warning([string]$message) { [void]$script:warnings.Add($message) }

function Get-SortedStrings([Collections.Generic.HashSet[string]]$set) {
  return [string[]]@($set | Sort-Object)
}

function Emit-Result([int]$exitCode, [string]$status) {
  $validation = @(Get-SortedStrings $script:validationErrors)
  $infrastructure = @(Get-SortedStrings $script:infrastructureErrors)
  $warningList = @(Get-SortedStrings $script:warnings)
  $result = [ordered]@{
    schemaVersion = 2
    status = $status
    exitCode = $exitCode
    phase = $script:phase
    mode = $Mode
    packageRoot = $script:pkgFull
    stats = $script:stats
    errors = $validation
    infrastructureErrors = $infrastructure
    warnings = $warningList
    scans = $script:scanResults
  }

  if ($Json) {
    [Console]::Out.WriteLine(($result | ConvertTo-Json -Depth 10 -Compress))
  } else {
    foreach ($message in $infrastructure) { [Console]::Error.WriteLine(('ERROR: ' + $message)) }
    foreach ($message in $validation) { [Console]::Error.WriteLine(('FAIL: ' + $message)) }
    foreach ($message in $warningList) { [Console]::Error.WriteLine(('WARN: ' + $message)) }
    if ($script:pkgFull) { [Console]::Out.WriteLine(('PACKAGE_ROOT=' + $script:pkgFull)) }
    [Console]::Out.WriteLine(('TOTAL_FILES={0} TOTAL_BYTES={1}' -f $script:stats.totalFiles, $script:stats.totalBytes))
    if ($exitCode -eq 0) { [Console]::Out.WriteLine('VERIFY_OK') }
    else { [Console]::Out.WriteLine(('VERIFY_FAIL phase={0} errors={1} infrastructureErrors={2}' -f $script:phase, $validation.Count, $infrastructure.Count)) }
  }
  exit $exitCode
}

function Stop-IfPhaseFailed {
  if ($script:infrastructureErrors.Count -gt 0) { Emit-Result 2 'infrastructure-error' }
  if ($script:validationErrors.Count -gt 0) { Emit-Result 1 'rejected' }
}

function Test-JsonObject($value) {
  return $null -ne $value -and ($value -is [pscustomobject] -or $value -is [Collections.IDictionary])
}

function Test-JsonArray($value) {
  return $null -ne $value -and $value -is [Collections.IList] -and $value -isnot [string]
}

function Has-Property($object, [string]$name) {
  if (-not (Test-JsonObject $object)) { return $false }
  return $null -ne $object.PSObject.Properties[$name]
}

function Get-PropertyValue($object, [string]$name) {
  if (-not (Has-Property $object $name)) { return $null }
  $value = $object.PSObject.Properties[$name].Value
  if ($value -is [Array]) { return ,$value }
  return $value
}

function Test-Integer($value) {
  return $value -is [int] -or $value -is [long]
}

function Read-JsonFileStrict([string]$path, [string]$codePrefix, [bool]$infrastructure) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    if ($infrastructure) { Add-InfrastructureError ($codePrefix + '_MISSING') }
    else { Add-ValidationError ($codePrefix + '_MISSING') }
    return $null
  }
  try {
    $raw = [IO.File]::ReadAllText($path)
    return ($raw | ConvertFrom-Json -NoEnumerate -ErrorAction Stop)
  } catch {
    if ($infrastructure) { Add-InfrastructureError ($codePrefix + '_JSON_INVALID') }
    else { Add-ValidationError ($codePrefix + '_JSON_INVALID') }
    return $null
  }
}

function Assert-JsonObjectDocument($value, [string]$codePrefix, [bool]$infrastructure) {
  if (Test-JsonObject $value) { return $true }

  # A parser/missing-file diagnostic is already complete. Do not add a derived
  # top-level-shape error for the same failed read.
  $alreadyReported = if ($infrastructure) {
    $script:infrastructureErrors.Contains($codePrefix + '_MISSING') -or
      $script:infrastructureErrors.Contains($codePrefix + '_JSON_INVALID')
  } else {
    $script:validationErrors.Contains($codePrefix + '_MISSING') -or
      $script:validationErrors.Contains($codePrefix + '_JSON_INVALID')
  }
  if (-not $alreadyReported) {
    if ($infrastructure) { Add-InfrastructureError ($codePrefix + '_TOP_LEVEL_NOT_OBJECT') }
    else { Add-ValidationError ($codePrefix + '_TOP_LEVEL_NOT_OBJECT') }
  }
  return $false
}

function Normalize-RelativePath([string]$path) {
  return $path.Replace('\', '/')
}

function Add-PathContractError([string]$message, [bool]$infrastructure) {
  if ($infrastructure) { Add-InfrastructureError $message } else { Add-ValidationError $message }
}

function Test-StrictRelativePath([string]$path, [string]$prefix, [bool]$infrastructure = $false) {
  $before = if ($infrastructure) { $script:infrastructureErrors.Count } else { $script:validationErrors.Count }
  if ([string]::IsNullOrWhiteSpace($path)) { Add-PathContractError ($prefix + '_EMPTY') $infrastructure; return $false }
  if ($path -match '^[A-Za-z]:' -or $path.StartsWith('\') -or $path.StartsWith('/')) { Add-PathContractError ($prefix + '_ABS:' + $path) $infrastructure }
  elseif ($path.Contains(':')) { Add-PathContractError ($prefix + '_ADS_OR_COLON:' + $path) $infrastructure }
  if ($path.IndexOfAny([char[]]@('<','>','|','"','?','*')) -ge 0) { Add-PathContractError ($prefix + '_BAD_CHAR:' + $path) $infrastructure }
  if ($path.ToCharArray() | Where-Object { [int]$_ -lt 32 }) { Add-PathContractError ($prefix + '_CONTROL_CHAR') $infrastructure }

  $normalized = Normalize-RelativePath $path
  $segments = @($normalized -split '/')
  if (@($segments | Where-Object { $_ -eq '' }).Count -gt 0) { Add-PathContractError ($prefix + '_REPEATED_SEPARATOR:' + $path) $infrastructure }
  foreach ($segment in $segments) {
    if ($segment -eq '..') { Add-PathContractError ($prefix + '_TRAVERSAL:' + $path) $infrastructure }
    elseif ($segment -eq '.') { Add-PathContractError ($prefix + '_DOT_SEGMENT:' + $path) $infrastructure }
    elseif ($segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$') { Add-PathContractError ($prefix + '_DOS_RESERVED:' + $path) $infrastructure }
    if ($segment.EndsWith('.') -or $segment.EndsWith(' ')) { Add-PathContractError ($prefix + '_TRAILING_DOT_SPACE:' + $path) $infrastructure }
  }
  $after = if ($infrastructure) { $script:infrastructureErrors.Count } else { $script:validationErrors.Count }
  return $after -eq $before
}

function Get-StringArray($object, [string]$property, [string]$errorCode, [bool]$required) {
  $value = Get-PropertyValue $object $property
  if ($null -eq $value) {
    if ($required) { Add-InfrastructureError ($errorCode + '_MISSING') }
    return [string[]]@()
  }
  if (-not (Test-JsonArray $value)) { Add-InfrastructureError ($errorCode + '_NOT_ARRAY'); return [string[]]@() }
  $items = New-Object Collections.Generic.List[string]
  foreach ($item in @($value)) {
    if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item)) { Add-InfrastructureError ($errorCode + '_BAD_ITEM'); continue }
    $items.Add($item)
  }
  return [string[]]$items.ToArray()
}

function Register-RuntimePin(
  $payload,
  [string]$component,
  [bool]$strictOnly,
  [bool]$requireAuthenticode = $false,
  [string]$publisherPattern = ''
) {
  if (-not (Test-JsonObject $payload)) { Add-InfrastructureError ('POLICY_RUNTIME_PIN_INVALID:' + $component); return }
  $path = Get-PropertyValue $payload 'path'
  $sha = Get-PropertyValue $payload 'sha256'
  $architecture = Get-PropertyValue $payload 'architecture'
  $peMachine = Get-PropertyValue $payload 'peMachine'
  if ($path -isnot [string]) { Add-InfrastructureError ('POLICY_RUNTIME_PIN_PATH_INVALID:' + $component); return }
  if (-not (Test-StrictRelativePath $path 'POLICY_RUNTIME_PIN_PATH' $true)) { return }
  if ($sha -isnot [string] -or $sha -cnotmatch '^[0-9a-f]{64}$' -or
      $architecture -isnot [string] -or [string]::IsNullOrWhiteSpace($architecture) -or
      ($null -ne $peMachine -and $peMachine -isnot [string])) {
    Add-InfrastructureError ('POLICY_RUNTIME_PIN_INVALID:' + $component + ':' + $path)
    return
  }
  [void]$script:runtimePrerequisitePins.Add([pscustomobject]@{
    Component = $component; Path = (Normalize-RelativePath $path); Sha = $sha
    Architecture = $architecture; PeMachine = $peMachine; StrictOnly = $strictOnly
    RequireAuthenticode = $requireAuthenticode; PublisherPattern = $publisherPattern
  })
}

function Test-OfficialOrigin([string]$value, [string]$expectedHost, [string]$errorCode) {
  try { $uri = [uri]$value } catch { Add-InfrastructureError $errorCode; return }
  if (-not $uri.IsAbsoluteUri -or $uri.Scheme -cne 'https' -or $uri.Host -cne $expectedHost -or
      -not $uri.IsDefaultPort -or -not [string]::IsNullOrEmpty($uri.UserInfo) -or
      $uri.AbsolutePath -ne '/' -or -not [string]::IsNullOrEmpty($uri.Query) -or
      -not [string]::IsNullOrEmpty($uri.Fragment)) {
    Add-InfrastructureError $errorCode
  }
}

function Test-ExplicitMatcher($entry, [string]$relativePath) {
  $fileName = [IO.Path]::GetFileName($relativePath)
  # Read the JSON array property directly here. Returning a one-element array
  # through a helper can make PowerShell preserve it as a nested Object[],
  # which would silently disable an otherwise explicit matcher.
  $namesProperty = $entry.PSObject.Properties['payloadFileNames']
  if ($null -ne $namesProperty) {
    foreach ($name in @($namesProperty.Value)) {
      if ($name -is [string] -and $fileName -ieq $name) { return $true }
    }
  }
  $globsProperty = $entry.PSObject.Properties['payloadGlobs']
  if ($null -ne $globsProperty) {
    foreach ($glob in @($globsProperty.Value)) {
      if ($glob -is [string] -and $relativePath -ilike (Normalize-RelativePath $glob)) { return $true }
    }
  }
  return $false
}

function Test-MatchesAnyTool($toolEntries, [string]$relativePath) {
  foreach ($toolEntry in @($toolEntries)) {
    if (Test-ExplicitMatcher $toolEntry.Raw $relativePath) { return $true }
  }
  return $false
}

function Test-AllowedOfficialUrl([string]$value, $origins) {
  if ([string]::IsNullOrWhiteSpace($value)) { return $false }
  try { $uri = [uri]$value } catch { return $false }
  if (-not $uri.IsAbsoluteUri -or $uri.Scheme -cne 'https' -or -not $uri.IsDefaultPort -or -not [string]::IsNullOrEmpty($uri.UserInfo)) { return $false }
  return $uri.Host -ceq ([uri]$origins.canonicalSiteOrigin).Host -or $uri.Host -ceq ([uri]$origins.brazilSiteOrigin).Host
}

function Invoke-JsonChildScript([string]$scriptPath, [string]$root) {
  $pwshPath = (Get-Process -Id $PID).Path
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $pwshPath
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  [void]$startInfo.ArgumentList.Add('-NoProfile')
  [void]$startInfo.ArgumentList.Add('-File')
  [void]$startInfo.ArgumentList.Add($scriptPath)
  [void]$startInfo.ArgumentList.Add('-Root')
  [void]$startInfo.ArgumentList.Add($root)
  [void]$startInfo.ArgumentList.Add('-Json')
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  [void]$process.Start()
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  return [pscustomobject]@{
    ExitCode = $process.ExitCode
    Stdout = $stdoutTask.GetAwaiter().GetResult()
    Stderr = $stderrTask.GetAwaiter().GetResult()
  }
}

function Test-TextScannerContract($result, [int]$actualExitCode) {
  $before = $script:infrastructureErrors.Count
  if (-not (Test-JsonObject $result)) { Add-InfrastructureError 'TEXT_SCANNER_TOP_LEVEL_NOT_OBJECT'; return $false }
  if (-not (Test-Integer (Get-PropertyValue $result 'schemaVersion')) -or (Get-PropertyValue $result 'schemaVersion') -ne 1) { Add-InfrastructureError 'TEXT_SCANNER_SCHEMA_INVALID' }
  if ((Get-PropertyValue $result 'root') -isnot [string] -or -not [string]::Equals((Get-PropertyValue $result 'root'), $script:pkgFull, [StringComparison]::OrdinalIgnoreCase)) { Add-InfrastructureError 'TEXT_SCANNER_ROOT_MISMATCH' }
  $filesScanned = Get-PropertyValue $result 'filesScanned'
  $matchCount = Get-PropertyValue $result 'matchCount'
  $unclassifiedCount = Get-PropertyValue $result 'unclassifiedCount'
  $matches = Get-PropertyValue $result 'matches'
  if (-not (Test-Integer $filesScanned) -or $filesScanned -lt 0) { Add-InfrastructureError 'TEXT_SCANNER_FILES_SCANNED_INVALID' }
  if (-not (Test-Integer $matchCount) -or $matchCount -lt 0) { Add-InfrastructureError 'TEXT_SCANNER_MATCH_COUNT_INVALID' }
  if (-not (Test-Integer $unclassifiedCount) -or $unclassifiedCount -lt 0) { Add-InfrastructureError 'TEXT_SCANNER_UNCLASSIFIED_COUNT_INVALID' }
  if (-not (Test-JsonArray $matches)) { Add-InfrastructureError 'TEXT_SCANNER_MATCHES_INVALID' }
  elseif ($matchCount -is [ValueType] -and @($matches).Count -ne [int]$matchCount) { Add-InfrastructureError 'TEXT_SCANNER_MATCH_COUNT_MISMATCH' }
  if ($actualExitCode -notin @(0,2) -or (($actualExitCode -eq 0) -ne (@($matches).Count -eq 0))) { Add-InfrastructureError 'TEXT_SCANNER_EXIT_CONTRACT_INVALID' }
  if (Test-JsonArray $matches) {
    $calculatedUnclassified = 0
    $index = 0
    foreach ($finding in @($matches)) {
      $valid = Test-JsonObject $finding
      $file = if ($valid) { Get-PropertyValue $finding 'file' } else { $null }
      $pattern = if ($valid) { Get-PropertyValue $finding 'pattern' } else { $null }
      $class = if ($valid) { Get-PropertyValue $finding 'class' } else { $null }
      $count = if ($valid) { Get-PropertyValue $finding 'count' } else { $null }
      $sample = if ($valid) { Get-PropertyValue $finding 'sample' } else { $null }
      $normalizedFile = if ($file -is [string]) { Normalize-RelativePath $file } else { '' }
      $segments = @($normalizedFile -split '/')
      if ($file -isnot [string] -or [string]::IsNullOrWhiteSpace($file) -or $normalizedFile.StartsWith('/') -or
          $normalizedFile -match '^[A-Za-z]:' -or $normalizedFile.Contains(':') -or @($segments | Where-Object { $_ -in @('','.', '..') }).Count -gt 0 -or
          -not $script:realByPath.ContainsKey($normalizedFile.ToLowerInvariant()) -or
          $pattern -isnot [string] -or [string]::IsNullOrWhiteSpace($pattern) -or
          $class -isnot [string] -or $class -cnotin @('PATH_CANDIDATE','SECRET_CANDIDATE','MEDIA_CANDIDATE','FORBIDDEN_CDN','UNCLASSIFIED') -or
          -not (Test-Integer $count) -or $count -le 0 -or $sample -isnot [string]) {
        Add-InfrastructureError ('TEXT_SCANNER_FINDING_INVALID:' + $index)
      }
      if ($class -ceq 'UNCLASSIFIED') { $calculatedUnclassified++ }
      $index++
    }
    if ((Test-Integer $unclassifiedCount) -and $unclassifiedCount -ne $calculatedUnclassified) { Add-InfrastructureError 'TEXT_SCANNER_UNCLASSIFIED_COUNT_MISMATCH' }
  }
  return $script:infrastructureErrors.Count -eq $before
}

function Test-AssemblyScannerContract($result, [int]$actualExitCode) {
  $before = $script:infrastructureErrors.Count
  if (-not (Test-JsonObject $result)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_TOP_LEVEL_NOT_OBJECT'; return $false }
  if (-not (Test-Integer (Get-PropertyValue $result 'schemaVersion')) -or (Get-PropertyValue $result 'schemaVersion') -ne 1) { Add-InfrastructureError 'ASSEMBLY_SCANNER_SCHEMA_INVALID' }
  if ((Get-PropertyValue $result 'root') -isnot [string] -or -not [string]::Equals((Get-PropertyValue $result 'root'), $script:pkgFull, [StringComparison]::OrdinalIgnoreCase)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_ROOT_MISMATCH' }
  $reportedExit = Get-PropertyValue $result 'exitCode'
  $status = Get-PropertyValue $result 'status'
  $filesScanned = Get-PropertyValue $result 'filesScanned'
  $matchCount = Get-PropertyValue $result 'matchCount'
  $matches = Get-PropertyValue $result 'matches'
  $errors = Get-PropertyValue $result 'errors'
  if (-not (Test-Integer $reportedExit) -or $reportedExit -ne $actualExitCode) { Add-InfrastructureError 'ASSEMBLY_SCANNER_EXIT_CONTRACT_INVALID' }
  $expectedStatus = if ($actualExitCode -eq 0) { 'clean' } elseif ($actualExitCode -eq 1) { 'error' } elseif ($actualExitCode -eq 2) { 'findings' } else { $null }
  if ($null -eq $expectedStatus -or $status -isnot [string] -or $status -cne $expectedStatus) { Add-InfrastructureError 'ASSEMBLY_SCANNER_STATUS_INVALID' }
  if (-not (Test-Integer $filesScanned) -or $filesScanned -lt 0) { Add-InfrastructureError 'ASSEMBLY_SCANNER_FILES_SCANNED_INVALID' }
  if (-not (Test-Integer $matchCount) -or $matchCount -lt 0) { Add-InfrastructureError 'ASSEMBLY_SCANNER_MATCH_COUNT_INVALID' }
  if (-not (Test-JsonArray $matches)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_MATCHES_INVALID' }
  elseif ($matchCount -is [ValueType] -and @($matches).Count -ne [int]$matchCount) { Add-InfrastructureError 'ASSEMBLY_SCANNER_MATCH_COUNT_MISMATCH' }
  if (-not (Test-JsonArray $errors)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_ERRORS_INVALID' }
  elseif (($actualExitCode -eq 1) -ne (@($errors).Count -gt 0)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_ERRORS_CONTRACT_INVALID' }

  if (Test-JsonArray $matches) {
    $index = 0
    foreach ($finding in @($matches)) {
      $valid = Test-JsonObject $finding
      $file = if ($valid) { Get-PropertyValue $finding 'file' } else { $null }
      $sha = if ($valid) { Get-PropertyValue $finding 'sha256' } else { $null }
      $pattern = if ($valid) { Get-PropertyValue $finding 'pattern' } else { $null }
      $class = if ($valid) { Get-PropertyValue $finding 'class' } else { $null }
      $encoding = if ($valid) { Get-PropertyValue $finding 'encoding' } else { $null }
      $count = if ($valid) { Get-PropertyValue $finding 'count' } else { $null }
      $value = if ($valid) { Get-PropertyValue $finding 'value' } else { $null }
      if ($file -isnot [string] -or $sha -isnot [string] -or $sha -cnotmatch '^[0-9a-f]{64}$' -or
          $pattern -isnot [string] -or [string]::IsNullOrWhiteSpace($pattern) -or
          $class -isnot [string] -or $class -cnotin @('PATH_CANDIDATE','DEV_REFERENCE','DERIVED_GAME_DATA','FORBIDDEN_CDN','REMOTE_URL') -or
          $encoding -isnot [string] -or $encoding -cnotin @('ascii-utf8','utf16le-even','utf16le-odd') -or
          -not (Test-Integer $count) -or $count -le 0 -or ($null -ne $value -and $value -isnot [string])) {
        Add-InfrastructureError ('ASSEMBLY_SCANNER_FINDING_INVALID:' + $index)
      } else {
        $normalizedFile = Normalize-RelativePath $file
        $key = $normalizedFile.ToLowerInvariant()
        if (-not $script:hashByPath.ContainsKey($key) -or $script:hashByPath[$key] -cne $sha) { Add-InfrastructureError ('ASSEMBLY_SCANNER_FINDING_SHA_MISMATCH:' + $normalizedFile) }
      }
      $index++
    }
  }
  return $script:infrastructureErrors.Count -eq $before
}

function Get-PeInfo([string]$path) {
  $stream = [IO.File]::OpenRead($path)
  try {
    $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
    if (-not $reader.PEHeaders.PEHeader) { throw [IO.InvalidDataException]::new('PE header missing') }
    return [pscustomobject]@{
      Machine = $reader.PEHeaders.CoffHeader.Machine.ToString()
      Managed = $reader.HasMetadata
      CorFlags = if ($reader.HasMetadata) { $reader.PEHeaders.CorHeader.Flags.ToString() } else { '' }
    }
  } finally {
    $stream.Dispose()
  }
}

function Get-RelativePayloadPath([IO.FileSystemInfo]$item) {
  return $item.FullName.Substring($script:pkgFull.Length).TrimStart('\', '/').Replace('\', '/')
}

# ==================== Phase 0: usage/root ====================
$phase = 'usage'
if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
  Add-InfrastructureError 'PACKAGE_ROOT_NOT_FOUND_OR_NOT_DIRECTORY'
  Emit-Result 2 'infrastructure-error'
}
$pkgFull = (Resolve-Path -LiteralPath $PackageRoot).Path
$manifestPath = Join-Path $pkgFull 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
  Add-InfrastructureError 'NO_RELEASE_MANIFEST'
  Emit-Result 2 'infrastructure-error'
}

# ==================== Phase 1: manifest contract ====================
$phase = 'manifest-contract'
$manifest = Read-JsonFileStrict $manifestPath 'MANIFEST' $false
$manifestIsObject = Assert-JsonObjectDocument $manifest 'MANIFEST' $false
Stop-IfPhaseFailed

$entries = New-Object Collections.Generic.List[object]
if ($manifestIsObject) {
  $allowedManifestFields = @('schemaVersion','product','target','selfContained','sourceCommit','sourceDirty','buildSdk','buildMode','generatedUtc','files')
  foreach ($property in $manifest.PSObject.Properties.Name) {
    if ($property -cnotin $allowedManifestFields) { Add-ValidationError ('MANIFEST_UNKNOWN_FIELD:' + $property) }
  }

  $schemaVersion = Get-PropertyValue $manifest 'schemaVersion'
  if (-not (Test-Integer $schemaVersion) -or $schemaVersion -ne 2) { Add-ValidationError 'MANIFEST_SCHEMA_INVALID' }
  if ((Get-PropertyValue $manifest 'product') -isnot [string] -or (Get-PropertyValue $manifest 'product') -cne 'FFX Mod Studio') { Add-ValidationError 'MANIFEST_PRODUCT_INVALID' }
  if ((Get-PropertyValue $manifest 'target') -isnot [string] -or (Get-PropertyValue $manifest 'target') -cne 'win-x64') { Add-ValidationError 'MANIFEST_TARGET_INVALID' }
  if ((Get-PropertyValue $manifest 'selfContained') -isnot [bool] -or (Get-PropertyValue $manifest 'selfContained') -ne $true) { Add-ValidationError 'MANIFEST_SELF_CONTAINED_INVALID' }
  $sourceCommit = Get-PropertyValue $manifest 'sourceCommit'
  if ($sourceCommit -isnot [string] -or $sourceCommit -cnotmatch '^[0-9a-f]{40}$') { Add-ValidationError 'MANIFEST_SOURCECOMMIT_INVALID' }
  $sourceDirty = Get-PropertyValue $manifest 'sourceDirty'
  if ($sourceDirty -isnot [bool]) { Add-ValidationError 'MANIFEST_SOURCEDIRTY_INVALID' }
  elseif ($Mode -ne 'Diagnostic' -and $sourceDirty) { Add-ValidationError ('SOURCE_DIRTY_FORBIDDEN_IN_' + $Mode) }
  $buildSdk = Get-PropertyValue $manifest 'buildSdk'
  if ($buildSdk -isnot [string] -or $buildSdk -notmatch '^\d+[.]\d+[.]\d+(?:[-+].+)?$') { Add-ValidationError 'MANIFEST_BUILDSDK_INVALID' }
  $buildMode = Get-PropertyValue $manifest 'buildMode'
  if ($buildMode -isnot [string] -or -not [string]::Equals($buildMode, $Mode, [StringComparison]::Ordinal)) { Add-ValidationError 'MANIFEST_BUILDMODE_MISMATCH' }

  $filesRaw = Get-PropertyValue $manifest 'files'
  if (-not (Test-JsonArray $filesRaw)) { Add-ValidationError 'MANIFEST_FILES_NOT_ARRAY' }
  elseif (@($filesRaw).Count -eq 0) { Add-ValidationError 'MANIFEST_FILES_EMPTY' }
  else {
    $seenPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($raw in @($filesRaw)) {
      if (-not (Test-JsonObject $raw)) { Add-ValidationError 'MANIFEST_ENTRY_NOT_OBJECT'; continue }
      $entryStart = $validationErrors.Count
      foreach ($property in $raw.PSObject.Properties.Name) {
        if ($property -cnotin @('path','bytes','sha256','origin')) { Add-ValidationError ('MANIFEST_ENTRY_UNKNOWN_FIELD:' + $property) }
      }
      $path = Get-PropertyValue $raw 'path'
      if ($path -isnot [string]) { Add-ValidationError 'MANIFEST_PATH_NOT_STRING'; continue }
      [void](Test-StrictRelativePath $path 'MANIFEST_PATH')
      $normalized = Normalize-RelativePath $path
      if (-not $seenPaths.Add($normalized)) { Add-ValidationError ('MANIFEST_DUP_PATH:' + $path) }
      $bytes = Get-PropertyValue $raw 'bytes'
      if (-not (Test-Integer $bytes) -or $bytes -lt 0) { Add-ValidationError ('MANIFEST_BAD_BYTES:' + $path) }
      $sha = Get-PropertyValue $raw 'sha256'
      if ($sha -isnot [string] -or $sha -cnotmatch '^[0-9a-f]{64}$') { Add-ValidationError ('MANIFEST_BAD_SHA:' + $path) }
      $origin = Get-PropertyValue $raw 'origin'
      if ($origin -isnot [string] -or $origin -cnotin @('app','nuget','viewer','tool','doc','runtime','satellite','content')) { Add-ValidationError ('MANIFEST_BAD_ORIGIN:' + $path) }
      if ($validationErrors.Count -eq $entryStart) {
        $entries.Add([pscustomobject]@{ Norm = $normalized; Bytes = [long]$bytes; Sha = $sha; Origin = $origin })
      }
    }
  }
}
$stats.manifestEntries = $entries.Count
Stop-IfPhaseFailed

# ==================== Phase 2: repository policy contracts ====================
$phase = 'policy-contracts'
$packagePolicy = Read-JsonFileStrict (Join-Path $repo 'release\package-allowlist.json') 'POLICY_PACKAGE_ALLOWLIST' $true
$toolPolicy = Read-JsonFileStrict (Join-Path $repo 'release\tool-dependencies.json') 'POLICY_TOOL_DEPENDENCIES' $true
$origins = Read-JsonFileStrict (Join-Path $repo 'release\official-origins.json') 'POLICY_OFFICIAL_ORIGINS' $true
$noclipPolicy = Read-JsonFileStrict (Join-Path $repo 'release\noclip-runtime.manifest.json') 'POLICY_NOCLIP_RUNTIME' $true
$runtimePolicy = Read-JsonFileStrict (Join-Path $repo 'release\windows-runtime-prerequisites.json') 'POLICY_WINDOWS_RUNTIME' $true

$packagePolicyIsObject = Assert-JsonObjectDocument $packagePolicy 'POLICY_PACKAGE_ALLOWLIST' $true
$toolPolicyIsObject = Assert-JsonObjectDocument $toolPolicy 'POLICY_TOOL_DEPENDENCIES' $true
$originsIsObject = Assert-JsonObjectDocument $origins 'POLICY_OFFICIAL_ORIGINS' $true
$noclipPolicyIsObject = Assert-JsonObjectDocument $noclipPolicy 'POLICY_NOCLIP_RUNTIME' $true
$runtimePolicyIsObject = Assert-JsonObjectDocument $runtimePolicy 'POLICY_WINDOWS_RUNTIME' $true
Stop-IfPhaseFailed

$core = $null
$allowedRoots = [string[]]@()
$allowedTopLevel = [string[]]@()
$requiredFiles = [string[]]@()
$requiredSatellites = [string[]]@()
$denyPaths = [string[]]@()
$denyExtensions = [string[]]@()
$denyPatterns = [string[]]@()
$scanAllowlist = @()
$noclipPinnedFiles = @()

if ($packagePolicyIsObject) {
  if (-not (Test-Integer (Get-PropertyValue $packagePolicy 'schemaVersion')) -or (Get-PropertyValue $packagePolicy 'schemaVersion') -ne 1) { Add-InfrastructureError 'POLICY_PACKAGE_ALLOWLIST_SCHEMA_INVALID' }
  $components = Get-PropertyValue $packagePolicy 'components'
  if (-not (Test-JsonObject $components)) { Add-InfrastructureError 'POLICY_COMPONENTS_INVALID' }
  else { $core = Get-PropertyValue $components 'studio-core-win-x64' }
  if (-not (Test-JsonObject $core)) { Add-InfrastructureError 'POLICY_CORE_COMPONENT_MISSING' }
}

if (Test-JsonObject $core) {
  if ((Get-PropertyValue $core 'target') -isnot [string] -or (Get-PropertyValue $core 'target') -cne 'win-x64') { Add-InfrastructureError 'POLICY_CORE_TARGET_INVALID' }
  if ((Get-PropertyValue $core 'selfContained') -isnot [bool] -or (Get-PropertyValue $core 'selfContained') -ne $true) { Add-InfrastructureError 'POLICY_CORE_SELF_CONTAINED_INVALID' }
  foreach ($limitName in @('hardLimitBytes','warnLimitBytes')) {
    $limit = Get-PropertyValue $core $limitName
    if (-not (Test-Integer $limit) -or $limit -le 0) { Add-InfrastructureError ('POLICY_' + $limitName.ToUpperInvariant() + '_INVALID') }
  }
  $requiredFiles = @(Get-StringArray $core 'requiredFiles' 'POLICY_REQUIRED_FILES' $true)
  $requiredSatellites = @(Get-StringArray $core 'requiredSatellites' 'POLICY_REQUIRED_SATELLITES' $true)
  $allowedRoots = @(Get-StringArray $core 'allowedRoots' 'POLICY_ALLOWED_ROOTS' $true)
  $allowedTopLevel = @(Get-StringArray $core 'allowedTopLevel' 'POLICY_ALLOWED_TOP_LEVEL' $true)
  if ($allowedTopLevel.Count -eq 0) { Add-InfrastructureError 'POLICY_ALLOWED_TOP_LEVEL_MISSING' }
  foreach ($requiredFile in $requiredFiles) { [void](Test-StrictRelativePath $requiredFile 'POLICY_REQUIRED_FILE' $true) }
  foreach ($language in $requiredSatellites) {
    if ($language -cnotmatch '^[a-z]{2}$') { Add-InfrastructureError ('POLICY_REQUIRED_SATELLITE_INVALID:' + $language) }
  }
  foreach ($topLevel in $allowedTopLevel) {
    if ($topLevel.Contains('/') -or $topLevel.Contains('\') -or $topLevel.IndexOfAny([char[]]@('*','?')) -ge 0) { Add-InfrastructureError ('POLICY_ALLOWED_TOP_LEVEL_NOT_EXACT:' + $topLevel) }
  }
  foreach ($root in $allowedRoots) {
    $normalizedRoot = Normalize-RelativePath $root
    if (-not $normalizedRoot.EndsWith('/') -or $normalizedRoot.StartsWith('/') -or $normalizedRoot.Contains('..') -or $normalizedRoot.IndexOfAny([char[]]@('*','?')) -ge 0) {
      Add-InfrastructureError ('POLICY_ALLOWED_ROOT_INVALID:' + $root)
    }
  }
  $deny = Get-PropertyValue $core 'deny'
  if (-not (Test-JsonObject $deny)) { Add-InfrastructureError 'POLICY_DENY_INVALID' }
  else {
    $denyPaths = @(Get-StringArray $deny 'paths' 'POLICY_DENY_PATHS' $true)
    $denyExtensions = @(Get-StringArray $deny 'extensions' 'POLICY_DENY_EXTENSIONS' $true)
    $denyPatterns = @(Get-StringArray $deny 'patterns' 'POLICY_DENY_PATTERNS' $true)
  }
  $scanAllowlistValue = Get-PropertyValue $core 'scanAllowlist'
  if ($null -ne $scanAllowlistValue) {
    if (-not (Test-JsonArray $scanAllowlistValue)) { Add-InfrastructureError 'POLICY_SCAN_ALLOWLIST_NOT_ARRAY' }
    else {
      $seenScanExceptions = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
      foreach ($exception in @($scanAllowlistValue)) {
        if (-not (Test-JsonObject $exception)) { Add-InfrastructureError 'POLICY_SCAN_ALLOWLIST_BAD_ENTRY'; continue }
        foreach ($property in $exception.PSObject.Properties.Name) {
          if ($property -cnotin @('scanner','path','sha256','pattern','class','encoding','maxCount')) { Add-InfrastructureError ('POLICY_SCAN_ALLOWLIST_UNKNOWN_FIELD:' + $property) }
        }
        $exceptionScanner = Get-PropertyValue $exception 'scanner'
        $exceptionPath = Get-PropertyValue $exception 'path'
        $exceptionSha = Get-PropertyValue $exception 'sha256'
        $exceptionPattern = Get-PropertyValue $exception 'pattern'
        $exceptionClass = Get-PropertyValue $exception 'class'
        $exceptionEncoding = Get-PropertyValue $exception 'encoding'
        $exceptionMaxCount = Get-PropertyValue $exception 'maxCount'
        if ($exceptionScanner -isnot [string] -or $exceptionScanner -cnotin @('text','assembly') -or
            $exceptionPath -isnot [string] -or $exceptionSha -isnot [string] -or $exceptionSha -cnotmatch '^[0-9a-f]{64}$' -or
            $exceptionPattern -isnot [string] -or [string]::IsNullOrWhiteSpace($exceptionPattern) -or
            $exceptionClass -isnot [string] -or -not (Test-Integer $exceptionMaxCount) -or $exceptionMaxCount -le 0) {
          Add-InfrastructureError 'POLICY_SCAN_ALLOWLIST_BAD_ENTRY'
          continue
        }
        if (-not (Test-StrictRelativePath $exceptionPath 'POLICY_SCAN_ALLOWLIST_PATH' $true)) { continue }
        $normalizedExceptionPath = Normalize-RelativePath $exceptionPath
        if ($exceptionScanner -eq 'text') {
          if ($exceptionClass -cnotin @('PATH_CANDIDATE','SECRET_CANDIDATE','MEDIA_CANDIDATE','FORBIDDEN_CDN','UNCLASSIFIED') -or $null -ne $exceptionEncoding) {
            Add-InfrastructureError 'POLICY_SCAN_ALLOWLIST_BAD_ENTRY'
            continue
          }
          if ($exceptionClass -ceq 'FORBIDDEN_CDN') {
            Add-InfrastructureError ('POLICY_TEXT_EXCEPTION_FORBIDDEN_CDN:' + $normalizedExceptionPath)
            continue
          }
          $isVendorText = $normalizedExceptionPath -cmatch '^viewers/(?:map|model|magic)/vendor/' -or $normalizedExceptionPath -cmatch '^viewers/noclip/'
          $isLegalText = $normalizedExceptionPath -cmatch '^(?:licenses/|runtime/java/legal/)' -or
            $normalizedExceptionPath -cmatch '^tools/.+[.](?:txt|md|html?|xml|json)$'
          if (-not $isVendorText -and -not $isLegalText) {
            Add-InfrastructureError ('POLICY_TEXT_EXCEPTION_NON_VENDOR_OR_LEGAL:' + $normalizedExceptionPath)
            continue
          }
          if ($isLegalText -and ($exceptionClass -cne 'UNCLASSIFIED' -or $exceptionPattern -cne 'URL')) {
            Add-InfrastructureError ('POLICY_TEXT_EXCEPTION_LEGAL_CLASS_INVALID:' + $normalizedExceptionPath)
            continue
          }
        } elseif ($exceptionClass -cnotin @('PATH_CANDIDATE','DEV_REFERENCE','DERIVED_GAME_DATA','FORBIDDEN_CDN','REMOTE_URL') -or
                  $exceptionEncoding -isnot [string] -or $exceptionEncoding -cnotin @('ascii-utf8','utf16le-even','utf16le-odd')) {
          Add-InfrastructureError 'POLICY_SCAN_ALLOWLIST_BAD_ENTRY'
          continue
        }
        if ($exceptionScanner -eq 'assembly' -and $exceptionClass -eq 'FORBIDDEN_CDN') {
          Add-InfrastructureError ('POLICY_ASSEMBLY_EXCEPTION_FORBIDDEN_CDN:' + $normalizedExceptionPath)
          continue
        }
        $exceptionKey = @($exceptionScanner,$normalizedExceptionPath,$exceptionSha,$exceptionPattern,$exceptionClass,[string]$exceptionEncoding) -join "`n"
        if (-not $seenScanExceptions.Add($exceptionKey)) { Add-InfrastructureError ('POLICY_SCAN_ALLOWLIST_DUPLICATE:' + $normalizedExceptionPath); continue }
        $scanAllowlist += [pscustomobject]@{
          Scanner = $exceptionScanner; Path = $normalizedExceptionPath; Sha = $exceptionSha
          Pattern = $exceptionPattern; Class = $exceptionClass; Encoding = $exceptionEncoding; MaxCount = [int]$exceptionMaxCount
        }
      }
    }
  }
}

$toolEntries = @()
if ($toolPolicyIsObject) {
  if (-not (Test-Integer (Get-PropertyValue $toolPolicy 'schemaVersion')) -or (Get-PropertyValue $toolPolicy 'schemaVersion') -ne 1) { Add-InfrastructureError 'POLICY_TOOL_SCHEMA_INVALID' }
  $rawToolEntries = Get-PropertyValue $toolPolicy 'entries'
  if (-not (Test-JsonArray $rawToolEntries)) { Add-InfrastructureError 'POLICY_TOOL_ENTRIES_INVALID' }
  else {
    $seenIds = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $validClassifications = @('IN_PROCESS_BUNDLED','BUNDLED_HELPER','BUNDLED_OWNER_ACCEPTED_RISK','BUNDLED_REDISTRIBUTION_PROVEN','LAUNCHER_COMPONENT','USER_PROVIDED','BUILD_ONLY','DEV_ONLY','LAB_ONLY','BLOCKED')
    foreach ($entry in @($rawToolEntries)) {
      if (-not (Test-JsonObject $entry)) { Add-InfrastructureError 'POLICY_TOOL_ENTRY_NOT_OBJECT'; continue }
      $id = Get-PropertyValue $entry 'id'
      $classification = Get-PropertyValue $entry 'classification'
      $executionModel = Get-PropertyValue $entry 'executionModel'
      if ($id -isnot [string] -or [string]::IsNullOrWhiteSpace($id)) { Add-InfrastructureError 'POLICY_TOOL_ID_INVALID'; continue }
      if (-not $seenIds.Add($id)) { Add-InfrastructureError ('POLICY_TOOL_ID_DUPLICATE:' + $id) }
      if ($classification -isnot [string] -or $classification -cnotin $validClassifications) { Add-InfrastructureError ('POLICY_TOOL_CLASSIFICATION_INVALID:' + $id); continue }

      $names = @(Get-StringArray $entry 'payloadFileNames' ('POLICY_TOOL_FILENAMES:' + $id) $false)
      $globs = @(Get-StringArray $entry 'payloadGlobs' ('POLICY_TOOL_GLOBS:' + $id) $false)
      foreach ($name in $names) {
        if ([IO.Path]::GetFileName($name) -cne $name -or $name.IndexOfAny([char[]]@('*','?',':')) -ge 0) { Add-InfrastructureError ('POLICY_TOOL_FILENAME_NOT_EXPLICIT:' + $id + ':' + $name) }
      }
      foreach ($glob in $globs) {
        $normalizedGlob = Normalize-RelativePath $glob
        if ($normalizedGlob.StartsWith('/') -or $normalizedGlob -match '^[A-Za-z]:' -or $normalizedGlob.Contains('..') -or
            $normalizedGlob -notmatch '/' -or $normalizedGlob.StartsWith('*') -or $normalizedGlob.StartsWith('?')) {
          Add-InfrastructureError ('POLICY_TOOL_GLOB_NOT_EXPLICIT:' + $id + ':' + $glob)
        }
      }
      if ($classification -eq 'BLOCKED' -and ($names.Count + $globs.Count) -eq 0) { Add-InfrastructureError ('POLICY_BLOCKED_MATCHER_MISSING:' + $id) }

      $approved = @()
      if ($classification -in @('IN_PROCESS_BUNDLED','BUNDLED_HELPER','BUNDLED_OWNER_ACCEPTED_RISK','BUNDLED_REDISTRIBUTION_PROVEN')) {
        if ($executionModel -isnot [string] -or $executionModel -cnotin @('in-process','child-process')) { Add-InfrastructureError ('POLICY_TOOL_EXECUTION_MODEL_INVALID:' + $id) }
        if (($names.Count + $globs.Count) -eq 0) { Add-InfrastructureError ('POLICY_BUNDLED_MATCHER_MISSING:' + $id) }
        $approvedValue = Get-PropertyValue $entry 'approvedPayloads'
        if (-not (Test-JsonArray $approvedValue) -or @($approvedValue).Count -eq 0) {
          Add-InfrastructureError ('POLICY_ACCEPTED_RISK_APPROVED_PAYLOADS_MISSING:' + $id)
        } else {
          $seenApprovedPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
          foreach ($payload in @($approvedValue)) {
            if (-not (Test-JsonObject $payload)) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_ENTRY:' + $id); continue }
            foreach ($property in $payload.PSObject.Properties.Name) {
              if ($property -cnotin @('path','sha256','bytes','kind','arch','target')) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_UNKNOWN_FIELD:' + $id + ':' + $property) }
            }
            $payloadPath = Get-PropertyValue $payload 'path'
            $payloadSha = Get-PropertyValue $payload 'sha256'
            $payloadBytes = Get-PropertyValue $payload 'bytes'
            $payloadKind = Get-PropertyValue $payload 'kind'
            $payloadArch = Get-PropertyValue $payload 'arch'
            $payloadTarget = Get-PropertyValue $payload 'target'
            if ($payloadPath -isnot [string]) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_PATH:' + $id); continue }
            if (-not (Test-StrictRelativePath $payloadPath 'POLICY_APPROVED_PATH' $true)) { continue }
            if ($payloadSha -isnot [string] -or $payloadSha -cnotmatch '^[0-9a-f]{64}$') { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_SHA:' + $id); continue }
            if (-not (Test-Integer $payloadBytes) -or $payloadBytes -lt 0) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_BYTES:' + $id); continue }
            if ($payloadKind -isnot [string] -or $payloadKind -cnotin @('data','managed','native')) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_KIND:' + $id + ':' + $payloadPath); continue }
            if ($payloadArch -isnot [string] -or $payloadArch -cnotin @('data','managed-any','x64','x86')) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_ARCH:' + $id + ':' + $payloadPath); continue }
            $kindArchValid = ($payloadKind -eq 'data' -and $payloadArch -eq 'data') -or
              ($payloadKind -eq 'managed' -and $payloadArch -eq 'managed-any') -or
              ($payloadKind -eq 'native' -and $payloadArch -in @('x64','x86'))
            if (-not $kindArchValid) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_KIND_ARCH_INVALID:' + $id + ':' + $payloadPath); continue }
            if ($payloadKind -eq 'native' -and $payloadArch -eq 'x86' -and $executionModel -ne 'child-process') {
              Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_X86_EXECUTION_MODEL_INVALID:' + $id + ':' + $payloadPath)
              continue
            }
            # Cross-target registry (C2/D2): a payload pinned for a FOREIGN target
            # (e.g. the linux-x64 vgmstream-cli living in the same tool entry) is
            # simply out of scope for a win-x64 package and must be skipped, not
            # rejected; a missing/invalid target field stays an infrastructure
            # error. Foreign files leaking INTO the package are still caught by
            # the allowlist deny rules and csproj content gating.
            if ($payloadTarget -isnot [string]) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_BAD_TARGET:' + $id); continue }
            if ($payloadTarget -cne 'win-x64') { continue }
            $normalizedPayloadPath = Normalize-RelativePath $payloadPath
            if (-not $seenApprovedPaths.Add($normalizedPayloadPath)) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_DUPLICATE:' + $id + ':' + $normalizedPayloadPath); continue }
            if (-not (Test-ExplicitMatcher $entry $normalizedPayloadPath)) { Add-InfrastructureError ('POLICY_APPROVED_PAYLOAD_WITHOUT_MATCHER:' + $id + ':' + $normalizedPayloadPath); continue }
            $approved += [pscustomobject]@{ Path = $normalizedPayloadPath; Sha = $payloadSha; Bytes = [long]$payloadBytes; Kind = $payloadKind; Arch = $payloadArch; Target = $payloadTarget }
          }
        }
      }
      $toolEntries += [pscustomobject]@{ Id = $id; Classification = $classification; ExecutionModel = $executionModel; Raw = $entry; Approved = @($approved) }
    }
  }
  $pendingClosure = Get-PropertyValue $toolPolicy 'pendingFullClosure'
  if ($pendingClosure -isnot [bool]) { Add-InfrastructureError 'POLICY_TOOL_PENDING_CLOSURE_INVALID' }
  elseif ($pendingClosure) {
    if ($Mode -eq 'Diagnostic') { Add-Warning 'POLICY_TOOL_CLOSURE_PENDING' }
    else { Add-InfrastructureError 'POLICY_TOOL_CLOSURE_PENDING' }
  }
}

if ($originsIsObject) {
  if (-not (Test-Integer (Get-PropertyValue $origins 'schemaVersion')) -or (Get-PropertyValue $origins 'schemaVersion') -ne 1) { Add-InfrastructureError 'POLICY_OFFICIAL_ORIGINS_SCHEMA_INVALID' }
  Test-OfficialOrigin ([string](Get-PropertyValue $origins 'canonicalSiteOrigin')) 'ffxmodstudio.com' 'POLICY_CANONICAL_ORIGIN_INVALID'
  Test-OfficialOrigin ([string](Get-PropertyValue $origins 'brazilSiteOrigin')) 'ffxmodstudio.com.br' 'POLICY_BRAZIL_ORIGIN_INVALID'
  Test-OfficialOrigin ([string](Get-PropertyValue $origins 'studioReleaseApiOrigin')) 'ffxmodstudio.com' 'POLICY_RELEASE_API_ORIGIN_INVALID'
  $pathTemplate = Get-PropertyValue $origins 'studioReleasePathTemplate'
  if ($pathTemplate -isnot [string] -or $pathTemplate -cnotmatch '^/api/releases/studio/\{channel\}$' -or $pathTemplate.Contains('..') -or $pathTemplate.Contains('?') -or $pathTemplate.Contains('#')) {
    Add-InfrastructureError 'POLICY_RELEASE_PATH_TEMPLATE_INVALID'
  }
}

if ($runtimePolicyIsObject) {
  if (-not (Test-Integer (Get-PropertyValue $runtimePolicy 'schemaVersion')) -or (Get-PropertyValue $runtimePolicy 'schemaVersion') -ne 1) { Add-InfrastructureError 'POLICY_WINDOWS_RUNTIME_SCHEMA_INVALID' }
  $runtimeTargetPolicy = Get-PropertyValue $runtimePolicy 'target'
  if (-not (Test-JsonObject $runtimeTargetPolicy) -or (Get-PropertyValue $runtimeTargetPolicy 'rid') -cne 'win-x64' -or (Get-PropertyValue $runtimeTargetPolicy 'architecture') -cne 'x64') {
    Add-InfrastructureError 'POLICY_WINDOWS_RUNTIME_TARGET_INVALID'
  }
  $runtimeStrictModes = @(Get-StringArray $runtimePolicy 'strictModes' 'POLICY_WINDOWS_RUNTIME_STRICT_MODES' $true)
  if (($runtimeStrictModes | Sort-Object) -join ',' -cne 'Candidate,Release') { Add-InfrastructureError 'POLICY_WINDOWS_RUNTIME_STRICT_MODES_INVALID' }
  $runtimeComponents = Get-PropertyValue $runtimePolicy 'components'
  if (-not (Test-JsonObject $runtimeComponents)) { Add-InfrastructureError 'POLICY_WINDOWS_RUNTIME_COMPONENTS_INVALID' }
  else {
    $dotnetComponent = Get-PropertyValue $runtimeComponents 'dotnetSelfContained'
    if (-not (Test-JsonObject $dotnetComponent) -or (Get-PropertyValue $dotnetComponent 'required') -ne $true -or
        (Get-PropertyValue $dotnetComponent 'delivery') -cne 'self-contained-payload' -or
        (Get-PropertyValue $dotnetComponent 'architecture') -cne 'x64' -or
        (Get-PropertyValue $dotnetComponent 'runtimeConfigPath') -cne 'FFXProjectEditor.runtimeconfig.json' -or
        (Get-PropertyValue $dotnetComponent 'depsPath') -cne 'FFXProjectEditor.deps.json') {
      Add-InfrastructureError 'POLICY_DOTNET_RUNTIME_COMPONENT_INVALID'
    } else {
      $dotnetRuntimeVersionPin = Get-PropertyValue $dotnetComponent 'versionPin'
      if ($dotnetRuntimeVersionPin -isnot [string] -or $dotnetRuntimeVersionPin -cnotmatch '^8[.]\d+[.]\d+$') { Add-InfrastructureError 'POLICY_DOTNET_RUNTIME_VERSION_INVALID' }
      $runtimeForbiddenDependencies = @(Get-StringArray $dotnetComponent 'forbiddenDependencies' 'POLICY_DOTNET_FORBIDDEN_DEPENDENCIES' $true)
      $dotnetPayloadFiles = Get-PropertyValue $dotnetComponent 'payloadFiles'
      if (-not (Test-JsonArray $dotnetPayloadFiles) -or @($dotnetPayloadFiles).Count -ne 4) { Add-InfrastructureError 'POLICY_DOTNET_PAYLOAD_FILES_INVALID' }
      else {
        foreach ($payload in @($dotnetPayloadFiles)) { Register-RuntimePin $payload 'dotnetSelfContained' $false }
        $dotnetPaths = @($dotnetPayloadFiles | ForEach-Object { [string](Get-PropertyValue $_ 'path') } | Sort-Object)
        $expectedDotnetPaths = @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll') | Sort-Object
        if (($dotnetPaths -join ',') -cne ($expectedDotnetPaths -join ',')) { Add-InfrastructureError 'POLICY_DOTNET_PAYLOAD_SET_INVALID' }
      }
    }

    $webViewComponent = Get-PropertyValue $runtimeComponents 'webView2Evergreen'
    if ($null -ne $webViewComponent) {
      if (-not (Test-JsonObject $webViewComponent)) { Add-InfrastructureError 'POLICY_WEBVIEW2_COMPONENT_INVALID' }
      else {
        Register-RuntimePin (Get-PropertyValue $webViewComponent 'loader') 'webView2Loader' $true
        $offline = Get-PropertyValue $webViewComponent 'offlineInstaller'
        if (-not (Test-JsonObject $offline) -or (Get-PropertyValue $offline 'enabled') -ne $true -or (Get-PropertyValue $offline 'requireAuthenticode') -ne $true -or
            (Get-PropertyValue $offline 'publisherPattern') -isnot [string]) { Add-InfrastructureError 'POLICY_WEBVIEW2_INSTALLER_INVALID' }
        else { Register-RuntimePin $offline 'webView2Installer' $true $true ([string](Get-PropertyValue $offline 'publisherPattern')) }
      }
    }

    $javaComponent = Get-PropertyValue $runtimeComponents 'javaPrivateRuntime'
    if ($null -ne $javaComponent) {
      if (-not (Test-JsonObject $javaComponent)) { Add-InfrastructureError 'POLICY_JAVA_COMPONENT_INVALID' }
      else {
        Register-RuntimePin (Get-PropertyValue $javaComponent 'javaExecutable') 'javaPrivateRuntime' $true
        Register-RuntimePin (Get-PropertyValue $javaComponent 'ffxedJar') 'ffxedJar' $true
        $javaVersionFile = Get-PropertyValue $javaComponent 'runtimeVersionFile'
        if ($javaVersionFile -isnot [string]) { Add-InfrastructureError 'POLICY_JAVA_VERSION_FILE_INVALID' }
        else { [void](Test-StrictRelativePath $javaVersionFile 'POLICY_JAVA_VERSION_FILE' $true) }
      }
    }

    $vcppComponent = Get-PropertyValue $runtimeComponents 'vcpp2013X64'
    if ($null -ne $vcppComponent) {
      if (-not (Test-JsonObject $vcppComponent)) { Add-InfrastructureError 'POLICY_VCPP_COMPONENT_INVALID' }
      else {
        $vcppInstaller = Get-PropertyValue $vcppComponent 'installer'
        if (-not (Test-JsonObject $vcppInstaller) -or (Get-PropertyValue $vcppInstaller 'requireAuthenticode') -ne $true -or
            (Get-PropertyValue $vcppInstaller 'publisherPattern') -isnot [string]) { Add-InfrastructureError 'POLICY_VCPP_INSTALLER_INVALID' }
        else { Register-RuntimePin $vcppInstaller 'vcpp2013Installer' $true $true ([string](Get-PropertyValue $vcppInstaller 'publisherPattern')) }
        Register-RuntimePin (Get-PropertyValue $vcppComponent 'keystoneConsumer') 'keystoneConsumer' $true
        $runtimeForbiddenPayloadPaths = @(Get-StringArray $vcppComponent 'forbiddenPayloadPaths' 'POLICY_VCPP_FORBIDDEN_PATHS' $true)
        foreach ($forbiddenRuntimePath in $runtimeForbiddenPayloadPaths) { [void](Test-StrictRelativePath $forbiddenRuntimePath 'POLICY_VCPP_FORBIDDEN_PATH' $true) }
      }
    }

    $seenRuntimePins = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($pin in $runtimePrerequisitePins) {
      if (-not $seenRuntimePins.Add($pin.Path)) { Add-InfrastructureError ('POLICY_RUNTIME_PIN_DUPLICATE:' + $pin.Path) }
    }
  }
}

if ($noclipPolicyIsObject) {
  $noclipSchema = Get-PropertyValue $noclipPolicy 'schemaVersion'
  $noclipState = Get-PropertyValue $noclipPolicy 'state'
  if (-not (Test-Integer $noclipSchema) -or $noclipSchema -notin @(1,2)) { Add-InfrastructureError 'POLICY_NOCLIP_SCHEMA_INVALID' }
  elseif ($noclipSchema -eq 1) {
    if ($noclipState -isnot [string] -or $noclipState -cne 'BLOCKED_RELEASE') { Add-InfrastructureError 'POLICY_NOCLIP_STATE_INVALID' }
  } else {
    if ($noclipState -isnot [string] -or $noclipState -cne 'BUNDLED_RUNTIME_VERIFIED_LOCAL_DATA_REQUIRED') { Add-InfrastructureError 'POLICY_NOCLIP_STATE_INVALID' }
    $studioBuild = Get-PropertyValue $noclipPolicy 'studioBuild'
    if (-not (Test-JsonObject $studioBuild)) { Add-InfrastructureError 'POLICY_NOCLIP_STUDIO_BUILD_INVALID' }
    else {
      if ((Get-PropertyValue $studioBuild 'outputRoot') -isnot [string] -or (Get-PropertyValue $studioBuild 'outputRoot') -cne 'ExternalLibs/NoclipViewer/dist-ffxstudio') { Add-InfrastructureError 'POLICY_NOCLIP_OUTPUT_ROOT_INVALID' }
      $forbiddenMatches = Get-PropertyValue $studioBuild 'forbiddenRuntimeOriginMatches'
      if (-not (Test-Integer $forbiddenMatches) -or $forbiddenMatches -ne 0) { Add-InfrastructureError 'POLICY_NOCLIP_FORBIDDEN_ORIGIN_MATCHES_INVALID' }
      $forbiddenOrigins = @(Get-StringArray $studioBuild 'forbiddenRuntimeOrigins' 'POLICY_NOCLIP_FORBIDDEN_ORIGINS' $true)
      foreach ($requiredOrigin in @('z.noclip.website','unpkg.com','cdn.jsdelivr.net')) {
        if ($requiredOrigin -cnotin $forbiddenOrigins) { Add-InfrastructureError ('POLICY_NOCLIP_FORBIDDEN_ORIGIN_MISSING:' + $requiredOrigin) }
      }
      $noclipFiles = Get-PropertyValue $studioBuild 'files'
      if (-not (Test-JsonArray $noclipFiles) -or @($noclipFiles).Count -ne 16) { Add-InfrastructureError 'POLICY_NOCLIP_FILES_INVALID' }
      else {
        $seenNoclipPaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($noclipFile in @($noclipFiles)) {
          if (-not (Test-JsonObject $noclipFile)) { Add-InfrastructureError 'POLICY_NOCLIP_FILE_BAD_ENTRY'; continue }
          $noclipRelative = Get-PropertyValue $noclipFile 'path'
          $noclipBytes = Get-PropertyValue $noclipFile 'bytes'
          $noclipSha = Get-PropertyValue $noclipFile 'sha256'
          if ($noclipRelative -isnot [string]) { Add-InfrastructureError 'POLICY_NOCLIP_FILE_BAD_PATH'; continue }
          if (-not (Test-StrictRelativePath $noclipRelative 'POLICY_NOCLIP_FILE_PATH' $true)) { continue }
          if (-not (Test-Integer $noclipBytes) -or $noclipBytes -le 0 -or $noclipSha -isnot [string] -or $noclipSha -cnotmatch '^[0-9a-f]{64}$') { Add-InfrastructureError ('POLICY_NOCLIP_FILE_BAD_PIN:' + $noclipRelative); continue }
          $packageRelative = 'viewers/noclip/' + (Normalize-RelativePath $noclipRelative)
          if (-not $seenNoclipPaths.Add($packageRelative)) { Add-InfrastructureError ('POLICY_NOCLIP_FILE_DUPLICATE:' + $packageRelative); continue }
          $noclipPinnedFiles += [pscustomobject]@{ Path = $packageRelative; Bytes = [long]$noclipBytes; Sha = $noclipSha }
        }
      }
    }
    $dataCapability = Get-PropertyValue $noclipPolicy 'dataCapability'
    if (-not (Test-JsonObject $dataCapability) -or
        (Get-PropertyValue $dataCapability 'packageContainsGameData') -isnot [bool] -or (Get-PropertyValue $dataCapability 'packageContainsGameData') -ne $false -or
        (Get-PropertyValue $dataCapability 'repair') -isnot [bool] -or (Get-PropertyValue $dataCapability 'repair') -ne $false -or
        (Get-PropertyValue $dataCapability 'networkRepair') -isnot [bool] -or (Get-PropertyValue $dataCapability 'networkRepair') -ne $false -or
        (Get-PropertyValue $dataCapability 'junctionRepair') -isnot [bool] -or (Get-PropertyValue $dataCapability 'junctionRepair') -ne $false -or
        (Get-PropertyValue $dataCapability 'route') -isnot [string] -or (Get-PropertyValue $dataCapability 'route') -cne '/data/') {
      Add-InfrastructureError 'POLICY_NOCLIP_DATA_CAPABILITY_INVALID'
    }
  }
}

Stop-IfPhaseFailed

# ==================== Phase 3: inventory/allowlist/hash/deny/tools ====================
$phase = 'payload-inventory'
try {
  $allItems = @(Get-ChildItem -LiteralPath $pkgFull -Recurse -Force -ErrorAction Stop)
  foreach ($item in $allItems) {
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { Add-ValidationError ('REPARSE_POINT_FORBIDDEN:' + (Get-RelativePayloadPath $item)) }
  }
  $realFiles = @($allItems | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
} catch {
  Add-InfrastructureError ('PAYLOAD_ENUMERATION_FAILED:' + $_.Exception.GetType().Name)
  Stop-IfPhaseFailed
}
Stop-IfPhaseFailed

$stats.totalFiles = $realFiles.Count
$stats.totalBytes = [long](($realFiles | Measure-Object -Property Length -Sum).Sum)
$manifestByPath = @{}
foreach ($entry in $entries) { $manifestByPath[$entry.Norm.ToLowerInvariant()] = $entry }
$realByPath = @{}
$hashByPath = @{}

foreach ($required in $requiredFiles) {
  $requiredPath = Join-Path $pkgFull $required
  if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { Add-ValidationError ('MISSING_REQUIRED:' + $required) }
  elseif ((Get-Item -LiteralPath $requiredPath).Length -le 0) { Add-ValidationError ('MISSING_REQUIRED_EMPTY:' + $required) }
}

foreach ($file in $realFiles) {
  $relative = Get-RelativePayloadPath $file
  $key = $relative.ToLowerInvariant()
  $realByPath[$key] = $file

  if ($relative -ne 'release-manifest.json') {
    if (-not $manifestByPath.ContainsKey($key)) { Add-ValidationError ('EXTRA_FILE_NOT_IN_MANIFEST:' + $relative) }
    else {
      $entry = $manifestByPath[$key]
      if ($entry.Bytes -ne $file.Length) { Add-ValidationError ('SIZE_MISMATCH:' + $relative) }
      try { $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); $hashByPath[$key] = $hash }
      catch { Add-InfrastructureError ('HASH_READ_FAILED:' + $relative); continue }
      if ($entry.Sha -cne $hash) { Add-ValidationError ('HASH_MISMATCH:' + $relative) }
      if ($entry.Origin -eq 'satellite' -and $relative -cnotmatch '^[a-z]{2}/FFXProjectEditor[.]resources[.]dll$') { Add-ValidationError ('ORIGIN_PATH_MISMATCH:' + $relative + ':satellite') }
      if ($entry.Origin -eq 'tool' -and
          -not $relative.StartsWith('tools/', [StringComparison]::OrdinalIgnoreCase) -and
          -not (Test-MatchesAnyTool $toolEntries $relative)) {
        Add-ValidationError ('ORIGIN_PATH_MISMATCH:' + $relative + ':tool')
      }
    }
  }

  if ($relative.Contains('/')) {
    $insideAllowedRoot = $false
    foreach ($allowedRoot in $allowedRoots) {
      $normalizedRoot = Normalize-RelativePath $allowedRoot
      if ($relative.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) { $insideAllowedRoot = $true; break }
    }
    if (-not $insideAllowedRoot) { Add-ValidationError ('UNALLOWLISTED_ROOT:' + $relative) }
  } else {
    if ($relative -notin $allowedTopLevel) {
      # PowerShell -in is case-insensitive by default; allowedTopLevel is exact names, no globs.
      Add-ValidationError ('UNALLOWLISTED_TOP_LEVEL:' + $relative)
    }
  }

  if ($denyExtensions -icontains $file.Extension) { Add-ValidationError ('FORBIDDEN_EXT:' + $relative) }
  foreach ($denyPath in $denyPaths) {
    $normalizedDeny = (Normalize-RelativePath $denyPath).TrimEnd('/')
    if ($relative.Equals($normalizedDeny, [StringComparison]::OrdinalIgnoreCase) -or $relative.StartsWith($normalizedDeny + '/', [StringComparison]::OrdinalIgnoreCase)) {
      Add-ValidationError ('DENY_PATH:' + $relative)
    }
  }
  foreach ($denyPattern in $denyPatterns) {
    $normalizedPattern = Normalize-RelativePath $denyPattern
    if ($relative.IndexOf($normalizedPattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Add-ValidationError ('DENY_PATTERN_PATH:' + $relative + ':' + $denyPattern) }
  }
}

foreach ($entryKey in @($manifestByPath.Keys)) {
  if (-not $realByPath.ContainsKey($entryKey)) { Add-ValidationError ('MANIFEST_FILE_MISSING_ON_DISK:' + $manifestByPath[$entryKey].Norm) }
}

if ((Get-PropertyValue $core 'hardLimitBytes') -and $stats.totalBytes -gt [long](Get-PropertyValue $core 'hardLimitBytes')) {
  Add-ValidationError ('OVER_HARD_LIMIT:' + $stats.totalBytes)
}
if ((Get-PropertyValue $core 'warnLimitBytes') -and $stats.totalBytes -gt [long](Get-PropertyValue $core 'warnLimitBytes')) {
  Add-Warning ('OVER_WARN_LIMIT:' + $stats.totalBytes)
}
foreach ($language in $requiredSatellites) {
  $satellite = $language + '/FFXProjectEditor.resources.dll'
  if (-not $realByPath.ContainsKey($satellite.ToLowerInvariant())) { Add-ValidationError ('MISSING_SATELLITE:' + $language) }
}

# Contract deny.patterns apply to contents as literals in all encoding lanes, not only filenames.
if ($denyPatterns.Count -gt 0) {
  foreach ($file in $realFiles) {
    $relative = Get-RelativePayloadPath $file
    try {
      $bytes = [IO.File]::ReadAllBytes($file.FullName)
      $lanes = @([Text.Encoding]::Latin1.GetString($bytes))
      foreach ($offset in @(0,1)) {
        $length = $bytes.Length - $offset
        if ($length -gt 1) {
          if (($length % 2) -ne 0) { $length-- }
          $lanes += [Text.Encoding]::Unicode.GetString($bytes, $offset, $length)
        }
      }
      foreach ($denyPattern in $denyPatterns) {
        $variants = @($denyPattern, $denyPattern.Replace('\','/'), $denyPattern.Replace('/','\')) | Sort-Object -Unique
        $hit = $false
        foreach ($lane in $lanes) {
          foreach ($variant in $variants) {
            if (-not [string]::IsNullOrEmpty($variant) -and $lane.IndexOf($variant, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $hit = $true; break }
          }
          if ($hit) { break }
        }
        if ($hit) { Add-ValidationError ('DENY_PATTERN_CONTENT:' + $relative + ':' + $denyPattern) }
      }
    } catch {
      Add-InfrastructureError ('CONTENT_SCAN_READ_FAILED:' + $relative)
    }
  }
}

# Tool policy: BLOCKED always rejects; bundled/owner-risk requires an exact approved tuple.
foreach ($file in $realFiles) {
  $relative = Get-RelativePayloadPath $file
  $matchedTool = $false
  foreach ($toolEntry in $toolEntries) {
    if (-not (Test-ExplicitMatcher $toolEntry.Raw $relative)) { continue }
    $matchedTool = $true
    if ($toolEntry.Classification -eq 'BLOCKED') {
      Add-ValidationError ('BLOCKED_COMPONENT_PRESENT:' + $relative + ':' + $toolEntry.Id)
      continue
    }
    if ($toolEntry.Classification -in @('IN_PROCESS_BUNDLED','BUNDLED_HELPER','BUNDLED_OWNER_ACCEPTED_RISK','BUNDLED_REDISTRIBUTION_PROVEN')) {
      $approved = @($toolEntry.Approved | Where-Object { $_.Path.Equals($relative, [StringComparison]::OrdinalIgnoreCase) })
      if ($approved.Count -ne 1) { Add-ValidationError ('ACCEPTED_RISK_PIN_MISSING:' + $toolEntry.Id + ':' + $relative); continue }
      $pin = $approved[0]
      $key = $relative.ToLowerInvariant()
      if ($file.Length -ne $pin.Bytes -or -not $hashByPath.ContainsKey($key) -or $hashByPath[$key] -cne $pin.Sha) {
        Add-ValidationError ('ACCEPTED_RISK_PIN_MISMATCH:' + $toolEntry.Id + ':' + $relative)
      }
    } else {
      Add-ValidationError ('NON_BUNDLED_COMPONENT_PRESENT:' + $relative + ':' + $toolEntry.Id + ':' + $toolEntry.Classification)
    }
  }
  if ($relative.StartsWith('tools/', [StringComparison]::OrdinalIgnoreCase) -and -not $matchedTool) { Add-ValidationError ('UNLEDGERED_TOOL_FILE:' + $relative) }
}

if ((Get-PropertyValue $noclipPolicy 'state') -eq 'BLOCKED_RELEASE') {
  foreach ($relative in @($realFiles | ForEach-Object { Get-RelativePayloadPath $_ })) {
    if ($relative -match '^(?i:viewers/noclip(?:/|$)|data/FinalFantasyX(?:/|$))') { Add-ValidationError ('NOCLIP_BLOCKED_RELEASE:' + $relative) }
  }
} elseif ((Get-PropertyValue $noclipPolicy 'state') -eq 'BUNDLED_RUNTIME_VERIFIED_LOCAL_DATA_REQUIRED') {
  $noclipPinKeys = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  foreach ($pin in $noclipPinnedFiles) {
    [void]$noclipPinKeys.Add($pin.Path)
    $key = $pin.Path.ToLowerInvariant()
    if (-not $realByPath.ContainsKey($key)) { Add-ValidationError ('NOCLIP_PINNED_FILE_MISSING:' + $pin.Path); continue }
    if ($realByPath[$key].Length -ne $pin.Bytes -or -not $hashByPath.ContainsKey($key) -or $hashByPath[$key] -cne $pin.Sha) {
      Add-ValidationError ('NOCLIP_PIN_MISMATCH:' + $pin.Path)
    }
  }
  foreach ($relative in @($realFiles | ForEach-Object { Get-RelativePayloadPath $_ } | Where-Object { $_ -like 'viewers/noclip/*' })) {
    if (-not $noclipPinKeys.Contains($relative)) { Add-ValidationError ('NOCLIP_UNPINNED_FILE:' + $relative) }
  }
}

Stop-IfPhaseFailed

# ==================== Phase 4: pinned Windows prerequisites ====================
$phase = 'runtime-prerequisite-pins'
$strictRuntimeMode = $Mode -cin $runtimeStrictModes
foreach ($forbiddenRuntimePath in $runtimeForbiddenPayloadPaths) {
  if ($realByPath.ContainsKey((Normalize-RelativePath $forbiddenRuntimePath).ToLowerInvariant())) {
    Add-ValidationError ('FORBIDDEN_RUNTIME_PAYLOAD:' + (Normalize-RelativePath $forbiddenRuntimePath))
  }
}
foreach ($pin in $runtimePrerequisitePins) {
  $key = $pin.Path.ToLowerInvariant()
  if (-not $realByPath.ContainsKey($key)) {
    if (-not $pin.StrictOnly -or $strictRuntimeMode) { Add-ValidationError ('RUNTIME_PINNED_FILE_MISSING:' + $pin.Component + ':' + $pin.Path) }
    continue
  }
  if (-not $hashByPath.ContainsKey($key)) { Add-InfrastructureError ('RUNTIME_PIN_HASH_UNAVAILABLE:' + $pin.Path); continue }
  if ($hashByPath[$key] -cne $pin.Sha) { Add-ValidationError ('RUNTIME_PIN_MISMATCH:' + $pin.Component + ':' + $pin.Path) }
}
Stop-IfPhaseFailed

# ==================== Phase 5: self-contained/runtime/PE ====================
$phase = 'runtime-contract'
$runtimeRequired = @('FFXProjectEditor.exe','FFXProjectEditor.dll','FFXProjectEditor.deps.json','FFXProjectEditor.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll')
foreach ($relative in $runtimeRequired) {
  $key = $relative.ToLowerInvariant()
  if (-not $realByPath.ContainsKey($key)) { Add-ValidationError ('SELF_CONTAINED_FILE_MISSING:' + $relative) }
  elseif ($relative -ne 'release-manifest.json' -and -not $manifestByPath.ContainsKey($key)) { Add-ValidationError ('SELF_CONTAINED_FILE_NOT_MANIFESTED:' + $relative) }
}

$runtimeConfig = Read-JsonFileStrict (Join-Path $pkgFull 'FFXProjectEditor.runtimeconfig.json') 'RUNTIMECONFIG' $false
$deps = Read-JsonFileStrict (Join-Path $pkgFull 'FFXProjectEditor.deps.json') 'DEPS' $false
$runtimeConfigIsObject = Assert-JsonObjectDocument $runtimeConfig 'RUNTIMECONFIG' $false
$depsIsObject = Assert-JsonObjectDocument $deps 'DEPS' $false
Stop-IfPhaseFailed

if ($runtimeConfigIsObject) {
  $runtimeOptions = Get-PropertyValue $runtimeConfig 'runtimeOptions'
  if (-not (Test-JsonObject $runtimeOptions)) { Add-ValidationError 'RUNTIMECONFIG_OPTIONS_INVALID' }
  else {
    if ((Get-PropertyValue $runtimeOptions 'tfm') -isnot [string] -or (Get-PropertyValue $runtimeOptions 'tfm') -cne 'net8.0') { Add-ValidationError 'RUNTIMECONFIG_TFM_INVALID' }
    $included = Get-PropertyValue $runtimeOptions 'includedFrameworks'
    if (-not (Test-JsonArray $included) -or @($included).Count -eq 0) { Add-ValidationError 'RUNTIMECONFIG_NOT_SELF_CONTAINED' }
    else {
      $netCoreFrameworks = @($included | Where-Object {
        (Test-JsonObject $_) -and (Get-PropertyValue $_ 'name') -ceq 'Microsoft.NETCore.App' -and
        (Get-PropertyValue $_ 'version') -is [string] -and (Get-PropertyValue $_ 'version') -match '^8[.]'
      })
      if ($netCoreFrameworks.Count -ne 1) { Add-ValidationError 'RUNTIMECONFIG_NETCORE_FRAMEWORK_INVALID' }
      elseif ((Get-PropertyValue $netCoreFrameworks[0] 'version') -cne $dotnetRuntimeVersionPin) { Add-ValidationError 'RUNTIMECONFIG_VERSION_PIN_MISMATCH' }
    }
    if (Has-Property $runtimeOptions 'framework' -or Has-Property $runtimeOptions 'frameworks') { Add-ValidationError 'RUNTIMECONFIG_FRAMEWORK_DEPENDENT_MARKER' }
  }
}

if ($depsIsObject) {
  $runtimeTarget = Get-PropertyValue $deps 'runtimeTarget'
  $runtimeTargetName = if (Test-JsonObject $runtimeTarget) { Get-PropertyValue $runtimeTarget 'name' } else { $null }
  if ($runtimeTargetName -isnot [string] -or $runtimeTargetName -cnotmatch '/win-x64$') { Add-ValidationError 'DEPS_RUNTIME_TARGET_NOT_WIN_X64' }
  $targets = Get-PropertyValue $deps 'targets'
  $targetGraph = if ((Test-JsonObject $targets) -and $runtimeTargetName -is [string] -and (Has-Property $targets $runtimeTargetName)) {
    Get-PropertyValue $targets $runtimeTargetName
  } else { $null }
  if (-not (Test-JsonObject $targetGraph)) { Add-ValidationError 'DEPS_RUNTIME_TARGET_GRAPH_MISSING' }
  else {
    $runtimePackProperties = @($targetGraph.PSObject.Properties | Where-Object { $_.Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' })
    if ($runtimePackProperties.Count -ne 1 -or -not (Test-JsonObject $runtimePackProperties[0].Value)) {
      Add-ValidationError 'DEPS_RUNTIME_PACK_MISSING'
    } else {
      $runtimePackName = $runtimePackProperties[0].Name
      $runtimePack = $runtimePackProperties[0].Value
      if ($runtimePackName -cnotmatch ('/' + [regex]::Escape($dotnetRuntimeVersionPin) + '$')) { Add-ValidationError 'DEPS_RUNTIME_PACK_VERSION_PIN_MISMATCH' }
      $nativeAssets = Get-PropertyValue $runtimePack 'native'
      $managedAssets = Get-PropertyValue $runtimePack 'runtime'
      foreach ($asset in @('coreclr.dll','hostpolicy.dll')) {
        if (-not (Test-JsonObject $nativeAssets) -or -not (Has-Property $nativeAssets $asset)) { Add-ValidationError ('DEPS_NATIVE_ASSET_MISSING:' + $asset) }
      }
      if (-not (Test-JsonObject $managedAssets) -or -not (Has-Property $managedAssets 'System.Private.CoreLib.dll')) { Add-ValidationError 'DEPS_MANAGED_ASSET_MISSING:System.Private.CoreLib.dll' }

      $libraries = Get-PropertyValue $deps 'libraries'
      if (-not (Test-JsonObject $libraries) -or -not (Has-Property $libraries $runtimePackName)) { Add-ValidationError 'DEPS_RUNTIME_PACK_LIBRARY_MISSING' }
      if (Test-JsonObject $libraries) {
        foreach ($forbiddenDependency in $runtimeForbiddenDependencies) {
          if (@($libraries.PSObject.Properties.Name | Where-Object { $_ -eq $forbiddenDependency -or $_ -like ($forbiddenDependency + '/*') }).Count -gt 0) {
            Add-ValidationError ('DEPS_FORBIDDEN_DEPENDENCY:' + $forbiddenDependency)
          }
        }
      }
    }
  }
}

$appHostPath = Join-Path $pkgFull 'FFXProjectEditor.exe'
if (Test-Path -LiteralPath $appHostPath -PathType Leaf) {
  try {
    $appHostText = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($appHostPath))
    if (-not $appHostText.Contains('FFXProjectEditor.dll')) { Add-ValidationError 'APPHOST_ENTRYPOINT_MISMATCH' }
  } catch { Add-InfrastructureError 'APPHOST_READ_FAILED' }
}

try { Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction Stop } catch { Add-InfrastructureError 'PE_READER_UNAVAILABLE' }
if ($infrastructureErrors.Count -eq 0) {
  $peExpectations = @(
    @{ path='FFXProjectEditor.exe'; managed=$false },
    @{ path='FFXProjectEditor.dll'; managed=$true },
    @{ path='coreclr.dll'; managed=$false },
    @{ path='hostfxr.dll'; managed=$false },
    @{ path='hostpolicy.dll'; managed=$false },
    @{ path='System.Private.CoreLib.dll'; managed=$true }
  )
  foreach ($expectation in $peExpectations) {
    $fullPath = Join-Path $pkgFull $expectation.path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
    try {
      $pe = Get-PeInfo $fullPath
      if ($pe.Machine -cne 'Amd64') { Add-ValidationError ('PE_ARCH_NOT_AMD64:' + $expectation.path + ':' + $pe.Machine) }
      if ($pe.Managed -ne $expectation.managed) { Add-ValidationError ('PE_MANAGED_KIND_MISMATCH:' + $expectation.path) }
    } catch {
      Add-ValidationError ('INVALID_PE:' + $expectation.path)
    }
  }

  foreach ($toolEntry in $toolEntries) {
    foreach ($pin in @($toolEntry.Approved | Where-Object { $_.Kind -in @('managed','native') })) {
      $fullPath = Join-Path $pkgFull $pin.Path
      if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
      try {
        $pe = Get-PeInfo $fullPath
        if ($pin.Kind -eq 'managed' -and -not $pe.Managed) {
          Add-ValidationError ('ACCEPTED_RISK_MANAGED_PE_REQUIRED:' + $toolEntry.Id + ':' + $pin.Path)
        } elseif ($pin.Kind -eq 'native') {
          $expectedMachine = if ($pin.Arch -eq 'x64') { 'Amd64' } else { 'I386' }
          if ($pe.Managed -or $pe.Machine -cne $expectedMachine) {
            Add-ValidationError ('ACCEPTED_RISK_NATIVE_ARCH_REQUIRED:' + $toolEntry.Id + ':' + $pin.Path + ':' + $pin.Arch + ':' + $pe.Machine)
          }
        }
      } catch { Add-ValidationError ('ACCEPTED_RISK_INVALID_PE:' + $toolEntry.Id + ':' + $pin.Path) }
    }
  }

  foreach ($pin in $runtimePrerequisitePins) {
    $fullPath = Join-Path $pkgFull $pin.Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
    if ($pin.PeMachine -in @('x64','managed')) {
      try {
        $pe = Get-PeInfo $fullPath
        if ($pin.PeMachine -eq 'x64' -and $pe.Machine -cne 'Amd64') { Add-ValidationError ('RUNTIME_PIN_ARCH_MISMATCH:' + $pin.Component + ':' + $pin.Path + ':' + $pe.Machine) }
        if ($pin.PeMachine -eq 'managed' -and -not $pe.Managed) { Add-ValidationError ('RUNTIME_PIN_MANAGED_KIND_MISMATCH:' + $pin.Component + ':' + $pin.Path) }
      } catch { Add-ValidationError ('RUNTIME_PIN_INVALID_PE:' + $pin.Component + ':' + $pin.Path) }
    }
    if ($pin.RequireAuthenticode) {
      try {
        $signature = Get-AuthenticodeSignature -LiteralPath $fullPath -ErrorAction Stop
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Subject.IndexOf($pin.PublisherPattern, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
          Add-ValidationError ('RUNTIME_PIN_AUTHENTICODE_INVALID:' + $pin.Component + ':' + $pin.Path)
        }
      } catch { Add-InfrastructureError ('RUNTIME_PIN_AUTHENTICODE_CHECK_FAILED:' + $pin.Component + ':' + $pin.Path) }
    }
  }
}

Stop-IfPhaseFailed

# ==================== Phase 5: textual + assembly scanners ====================
$phase = 'portability-scans'
foreach ($exception in $scanAllowlist) {
  $exceptionKey = $exception.Path.ToLowerInvariant()
  if (-not $realByPath.ContainsKey($exceptionKey)) { Add-ValidationError ('SCAN_ALLOWLIST_FILE_MISSING:' + $exception.Path); continue }
  if (-not $hashByPath.ContainsKey($exceptionKey)) { Add-InfrastructureError ('SCAN_ALLOWLIST_HASH_UNAVAILABLE:' + $exception.Path); continue }
  if ($hashByPath[$exceptionKey] -cne $exception.Sha) { Add-ValidationError ('SCAN_ALLOWLIST_PIN_MISMATCH:' + $exception.Path) }
}
Stop-IfPhaseFailed

$textChild = Invoke-JsonChildScript (Join-Path $PSScriptRoot 'scan_portability.ps1') $pkgFull
if (-not [string]::IsNullOrWhiteSpace($textChild.Stderr)) { Add-InfrastructureError 'TEXT_SCANNER_STDERR_NOT_EMPTY' }
try { $textResult = $textChild.Stdout | ConvertFrom-Json -NoEnumerate -ErrorAction Stop; $scanResults.text = $textResult }
catch { Add-InfrastructureError 'TEXT_SCANNER_JSON_INVALID'; $textResult = $null }
if ($null -ne $textResult) {
  [void](Test-TextScannerContract $textResult $textChild.ExitCode)
}
Stop-IfPhaseFailed

$assemblyChild = Invoke-JsonChildScript (Join-Path $PSScriptRoot 'scan_assembly_portability.ps1') $pkgFull
if (-not [string]::IsNullOrWhiteSpace($assemblyChild.Stderr)) { Add-InfrastructureError 'ASSEMBLY_SCANNER_STDERR_NOT_EMPTY' }
try { $assemblyResult = $assemblyChild.Stdout | ConvertFrom-Json -NoEnumerate -ErrorAction Stop; $scanResults.assembly = $assemblyResult }
catch { Add-InfrastructureError 'ASSEMBLY_SCANNER_JSON_INVALID'; $assemblyResult = $null }
if ($null -ne $assemblyResult) {
  [void](Test-AssemblyScannerContract $assemblyResult $assemblyChild.ExitCode)
}
Stop-IfPhaseFailed

if ($textChild.ExitCode -eq 2) {
  $allowedTextCounts = @{}
  foreach ($finding in @($textResult.matches)) {
    $findingPath = Normalize-RelativePath ([string]$finding.file)
    $findingKey = $findingPath.ToLowerInvariant()
    $findingSha = if ($hashByPath.ContainsKey($findingKey)) { $hashByPath[$findingKey] } else { '' }
    $allowedEntries = @($scanAllowlist | Where-Object {
      $_.Scanner -ceq 'text' -and $_.Path.Equals($findingPath, [StringComparison]::OrdinalIgnoreCase) -and
      $_.Sha -ceq $findingSha -and $_.Pattern -ceq [string]$finding.pattern -and $_.Class -ceq [string]$finding.class
    })
    if ($allowedEntries.Count -ne 1) {
      Add-ValidationError ('TEXT_PORTABILITY:' + $findingPath + ':' + $finding.class + ':' + $finding.pattern)
      continue
    }
    $allowedEntry = $allowedEntries[0]
    $countKey = @($findingPath.ToLowerInvariant(),[string]$finding.pattern,[string]$finding.class) -join "`n"
    if (-not $allowedTextCounts.ContainsKey($countKey)) { $allowedTextCounts[$countKey] = [pscustomobject]@{ Entry = $allowedEntry; Actual = 0 } }
    $allowedTextCounts[$countKey].Actual += [int]$finding.count
  }
  foreach ($countState in $allowedTextCounts.Values) {
    if ($countState.Actual -gt $countState.Entry.MaxCount) {
      Add-ValidationError ('TEXT_ALLOWLIST_MAX_COUNT_EXCEEDED:{0}:{1}:{2}:{3}:{4}' -f
        $countState.Entry.Path,$countState.Entry.Class,$countState.Entry.Pattern,$countState.Actual,$countState.Entry.MaxCount)
    }
  }
}
if ($assemblyChild.ExitCode -eq 2) {
  $allowedAssemblyCounts = @{}
  foreach ($finding in @($assemblyResult.matches)) {
    if ($finding.class -eq 'REMOTE_URL' -and (Test-AllowedOfficialUrl ([string]$finding.value) $origins)) { continue }
    $findingPath = Normalize-RelativePath ([string]$finding.file)
    $allowedEntries = @($scanAllowlist | Where-Object {
      $_.Scanner -ceq 'assembly' -and $_.Path.Equals($findingPath, [StringComparison]::OrdinalIgnoreCase) -and
      $_.Sha -ceq [string]$finding.sha256 -and $_.Pattern -ceq [string]$finding.pattern -and
      $_.Class -ceq [string]$finding.class -and $_.Encoding -ceq [string]$finding.encoding
    })
    if ($allowedEntries.Count -ne 1) {
      Add-ValidationError ('ASSEMBLY_PORTABILITY:' + $findingPath + ':' + $finding.class + ':' + $finding.pattern + ':' + $finding.encoding)
      continue
    }
    $allowedEntry = $allowedEntries[0]
    $countKey = @($findingPath.ToLowerInvariant(),[string]$finding.pattern,[string]$finding.class,[string]$finding.encoding) -join "`n"
    if (-not $allowedAssemblyCounts.ContainsKey($countKey)) { $allowedAssemblyCounts[$countKey] = [pscustomobject]@{ Entry = $allowedEntry; Actual = 0 } }
    $allowedAssemblyCounts[$countKey].Actual += [int]$finding.count
  }
  foreach ($countState in $allowedAssemblyCounts.Values) {
    if ($countState.Actual -gt $countState.Entry.MaxCount) {
      Add-ValidationError ('ASSEMBLY_ALLOWLIST_MAX_COUNT_EXCEEDED:{0}:{1}:{2}:{3}:{4}:{5}' -f
        $countState.Entry.Path,$countState.Entry.Class,$countState.Entry.Pattern,$countState.Entry.Encoding,$countState.Actual,$countState.Entry.MaxCount)
    }
  }
}

Stop-IfPhaseFailed
$phase = 'complete'
Emit-Result 0 'verified'
