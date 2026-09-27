<#
  FFX Mod Studio — Windows runtime dependency gate.

  Diagnostic records missing/incomplete runtime evidence as warnings so development publishes can
  still be inspected. Candidate and Release fail closed when a selected payload/capability lacks an
  exact version pin, architecture, SHA-256 evidence, expected file, expected PE machine or Microsoft
  signature. This script never downloads or installs anything.

  Exit codes: 0 = contract satisfied (or Diagnostic warnings only); 1 = verification failed;
  2 = invalid invocation/contract root.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PackageRoot,
  [ValidateSet('Diagnostic', 'Candidate', 'Release')][string]$Mode = 'Diagnostic',
  [string]$ContractPath,
  [switch]$Json
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
if (-not $ContractPath) { $ContractPath = Join-Path $repo 'release\windows-runtime-prerequisites.json' }
if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
  Write-Error 'PACKAGE_ROOT_NOT_FOUND'
  exit 2
}
if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
  Write-Error 'RUNTIME_CONTRACT_NOT_FOUND'
  exit 2
}

$packageFull = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\', '/')
$contractFull = (Resolve-Path -LiteralPath $ContractPath).Path
$fails = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$evidence = New-Object System.Collections.Generic.List[string]
$strict = $Mode -in @('Candidate', 'Release')

function Fail([string]$Message) { $script:fails.Add($Message) }
function Warn([string]$Message) { $script:warnings.Add($Message) }
function Gate([string]$Message) {
  if ($script:strict) { Fail $Message } else { Warn $Message }
}
function Is-NonEmptyString($Value) { return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) }
function Is-Sha256($Value) { return $Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$' }

function Resolve-PayloadPath([string]$RelativePath, [string]$Label) {
  if (-not (Is-NonEmptyString $RelativePath)) {
    Gate "CONTRACT_PATH_MISSING: $Label"
    return $null
  }
  if ([System.IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.IndexOf(':') -ge 0) {
    Fail "CONTRACT_PATH_NOT_RELATIVE: $Label <= $RelativePath"
    return $null
  }
  $candidate = [System.IO.Path]::GetFullPath((Join-Path $script:packageFull $RelativePath))
  $prefix = $script:packageFull + [System.IO.Path]::DirectorySeparatorChar
  if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    Fail "CONTRACT_PATH_ESCAPE: $Label <= $RelativePath"
    return $null
  }
  return $candidate
}

function Get-PeMachine([string]$Path) {
  try {
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
      if ($stream.Length -lt 64) { return 'not-pe' }
      $reader = [System.IO.BinaryReader]::new($stream)
      if ($reader.ReadUInt16() -ne 0x5A4D) { return 'not-pe' }
      $stream.Position = 0x3C
      $peOffset = $reader.ReadInt32()
      if ($peOffset -lt 0 -or ($peOffset + 6) -gt $stream.Length) { return 'not-pe' }
      $stream.Position = $peOffset
      if ($reader.ReadUInt32() -ne 0x00004550) { return 'not-pe' }
      switch ($reader.ReadUInt16()) {
        0x8664 { return 'x64' }
        0x014c { return 'x86' }
        0xAA64 { return 'arm64' }
        default { return 'unknown' }
      }
    } finally { $stream.Dispose() }
  } catch { return 'unreadable' }
}

function Require-ComponentMetadata($Component, [string]$Id, [string]$Kind, [string]$Delivery, [string]$PinProperty = 'versionPin') {
  if ($null -eq $Component) { Fail "CONTRACT_COMPONENT_MISSING: $Id"; return $false }
  if ($Component.kind -isnot [string] -or $Component.kind -cne $Kind) { Fail "CONTRACT_KIND_INVALID: $Id" }
  if ($Component.required -isnot [bool] -or $Component.required -ne $true) { Fail "CONTRACT_REQUIRED_NOT_TRUE: $Id" }
  if ($Component.delivery -isnot [string] -or $Component.delivery -cne $Delivery) { Fail "CONTRACT_DELIVERY_INVALID: $Id" }
  if ($Component.architecture -isnot [string] -or $Component.architecture -cne 'x64') { Gate "CONTRACT_ARCH_MISSING: $Id" }
  $pin = $Component.$PinProperty
  if (-not (Is-NonEmptyString $pin)) { Gate "CONTRACT_VERSION_PIN_MISSING: $Id.$PinProperty" }
  return $true
}

function Test-PayloadFile($Spec, [string]$Label, [switch]$RequireSignature) {
  if ($null -eq $Spec) { Gate "CONTRACT_PAYLOAD_SPEC_MISSING: $Label"; return $null }
  if (-not (Is-NonEmptyString $Spec.architecture)) { Gate "CONTRACT_ARCH_MISSING: $Label" }
  if (-not (Is-Sha256 $Spec.sha256)) { Gate "CONTRACT_SHA256_MISSING: $Label" }
  $full = Resolve-PayloadPath ([string]$Spec.path) $Label
  if ($null -eq $full) { return $null }
  if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Gate "PAYLOAD_MISSING: $Label <= $($Spec.path)"; return $null }

  if (Is-Sha256 $Spec.sha256) {
    $actualHash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne ([string]$Spec.sha256).ToLowerInvariant()) { Gate "PAYLOAD_HASH_MISMATCH: $Label <= $($Spec.path)" }
    else { $script:evidence.Add("PAYLOAD_HASH_OK $Label sha256=$actualHash") }
  }
  if ($Spec.peMachine -is [string] -and $Spec.peMachine -ceq 'x64') {
    $machine = Get-PeMachine $full
    if ($machine -cne 'x64') { Gate "PAYLOAD_ARCH_MISMATCH: $Label expected=x64 actual=$machine" }
    else { $script:evidence.Add("PAYLOAD_ARCH_OK $Label machine=x64") }
  }

  # Offline prerequisite installers are mutable download targets.  Hash pinning is authoritative,
  # while this file-version check makes an accidentally selected legacy VC/WebView2 installer
  # fail with an actionable reason before a clean-machine test.
  if ($Spec.installerFileVersionPin -is [string]) {
    $actualFileVersion = [string](Get-Item -LiteralPath $full).VersionInfo.FileVersion
    if (-not [string]::Equals($actualFileVersion, [string]$Spec.installerFileVersionPin, [StringComparison]::Ordinal)) {
      Gate "PAYLOAD_FILE_VERSION_MISMATCH: $Label expected=$($Spec.installerFileVersionPin) actual=$actualFileVersion"
    } else {
      $script:evidence.Add("PAYLOAD_FILE_VERSION_OK $Label version=$actualFileVersion")
    }
  }

  $signatureRequired = $RequireSignature -or ($Spec.requireAuthenticode -is [bool] -and $Spec.requireAuthenticode)
  if ($signatureRequired) {
    $signature = Get-AuthenticodeSignature -LiteralPath $full
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
      Gate "AUTHENTICODE_INVALID: $Label status=$($signature.Status)"
    } elseif (Is-NonEmptyString $Spec.publisherPattern) {
      $subject = [string]$signature.SignerCertificate.Subject
      if ($subject -notmatch [regex]::Escape([string]$Spec.publisherPattern)) { Gate "AUTHENTICODE_PUBLISHER_MISMATCH: $Label" }
      else { $script:evidence.Add("AUTHENTICODE_OK $Label subject=$subject") }
    } else {
      Gate "CONTRACT_PUBLISHER_MISSING: $Label"
    }
  }
  return $full
}

function Test-InstalledWebView2($Component) {
  $cap = $Component.installedCapability
  if ($null -eq $cap) { Gate 'CONTRACT_CAPABILITY_MISSING: webView2Evergreen.installedCapability'; return }
  if ($cap.architecture -isnot [string] -or $cap.architecture -cne 'x64') { Gate 'CONTRACT_ARCH_MISSING: webView2Evergreen.installedCapability' }
  if ($cap.hashPolicy -isnot [string] -or $cap.hashPolicy -cne 'sha256-runtime-evidence') { Gate 'CONTRACT_HASH_POLICY_MISSING: webView2Evergreen.installedCapability' }
  foreach ($field in @('registryClientId', 'binaryName', 'publisherPattern')) {
    if (-not (Is-NonEmptyString $cap.$field)) { Gate "CONTRACT_CAPABILITY_FIELD_MISSING: webView2Evergreen.$field" }
  }
  if (-not (Is-NonEmptyString $cap.registryClientId)) { return }

  $registryPaths = @(
    "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\$($cap.registryClientId)",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$($cap.registryClientId)",
    "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$($cap.registryClientId)"
  )
  $record = $null
  foreach ($registryPath in $registryPaths) {
    if (Test-Path -LiteralPath $registryPath) { $record = Get-ItemProperty -LiteralPath $registryPath; break }
  }
  if ($null -eq $record -or -not (Is-NonEmptyString $record.pv) -or -not (Is-NonEmptyString $record.location)) {
    Gate 'WEBVIEW2_INSTALLED_CAPABILITY_MISSING'
    return
  }

  try {
    $actualVersion = [version]([string]$record.pv)
    $minimumVersion = [version]([string]$Component.minimumRuntimeVersionPin)
    if ($actualVersion -lt $minimumVersion) { Gate "WEBVIEW2_VERSION_BELOW_PIN actual=$actualVersion minimum=$minimumVersion" }
  } catch { Gate 'WEBVIEW2_VERSION_INVALID'; return }

  $binary = Join-Path ([string]$record.location) (Join-Path ([string]$record.pv) ([string]$cap.binaryName))
  if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { Gate "WEBVIEW2_CAPABILITY_BINARY_MISSING: $binary"; return }
  $machine = Get-PeMachine $binary
  if ($machine -cne 'x64') { Gate "WEBVIEW2_CAPABILITY_ARCH_MISMATCH expected=x64 actual=$machine" }
  $signature = Get-AuthenticodeSignature -LiteralPath $binary
  if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) { Gate "WEBVIEW2_CAPABILITY_SIGNATURE_INVALID status=$($signature.Status)" }
  elseif ([string]$signature.SignerCertificate.Subject -notmatch [regex]::Escape([string]$cap.publisherPattern)) { Gate 'WEBVIEW2_CAPABILITY_PUBLISHER_MISMATCH' }
  $hash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
  $script:evidence.Add("WEBVIEW2_CAPABILITY_OK version=$($record.pv) machine=$machine sha256=$hash")
}

try { $contract = Get-Content -LiteralPath $contractFull -Raw | ConvertFrom-Json -ErrorAction Stop }
catch { Write-Error "RUNTIME_CONTRACT_JSON_INVALID: $($_.Exception.Message)"; exit 2 }

if ($contract.schemaVersion -isnot [int] -and $contract.schemaVersion -isnot [long]) { Fail 'CONTRACT_SCHEMA_NOT_INTEGER' }
elseif ($contract.schemaVersion -ne 1) { Fail 'CONTRACT_SCHEMA_UNSUPPORTED' }
if ($contract.target.rid -isnot [string] -or $contract.target.rid -cne 'win-x64') { Fail 'CONTRACT_TARGET_RID_INVALID' }
if ($contract.target.architecture -isnot [string] -or $contract.target.architecture -cne 'x64') { Fail 'CONTRACT_TARGET_ARCH_INVALID' }
if (@($contract.strictModes).Count -ne 2 -or 'Candidate' -cnotin @($contract.strictModes) -or 'Release' -cnotin @($contract.strictModes)) { Fail 'CONTRACT_STRICT_MODES_INVALID' }

$components = $contract.components

# .NET is a true self-contained payload, not an installed machine capability.
$dotnet = $components.dotnetSelfContained
if (Require-ComponentMetadata $dotnet 'dotnetSelfContained' 'DOTNET_SELF_CONTAINED' 'self-contained-payload') {
  $runtimeConfig = Resolve-PayloadPath ([string]$dotnet.runtimeConfigPath) 'dotnetSelfContained.runtimeConfig'
  if ($runtimeConfig -and (Test-Path -LiteralPath $runtimeConfig -PathType Leaf)) {
    try {
      $runtimeJson = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
      $framework = @($runtimeJson.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App') | Select-Object -First 1
      if ($null -eq $framework -or [string]$framework.version -cne [string]$dotnet.versionPin) {
        Gate "DOTNET_RUNTIME_VERSION_MISMATCH expected=$($dotnet.versionPin) actual=$($framework.version)"
      } else { $evidence.Add("DOTNET_RUNTIME_OK version=$($framework.version)") }
    } catch { Gate "DOTNET_RUNTIMECONFIG_INVALID: $($_.Exception.Message)" }
  } else { Gate 'PAYLOAD_MISSING: dotnetSelfContained.runtimeConfig' }

  $deps = Resolve-PayloadPath ([string]$dotnet.depsPath) 'dotnetSelfContained.deps'
  if ($deps -and (Test-Path -LiteralPath $deps -PathType Leaf)) {
    $depsText = Get-Content -LiteralPath $deps -Raw
    try {
      $depsJson = $depsText | ConvertFrom-Json
      if ([string]$depsJson.runtimeTarget.name -notmatch '/win-x64$') { Gate "DOTNET_DEPS_RID_MISMATCH actual=$($depsJson.runtimeTarget.name)" }
    } catch { Gate "DOTNET_DEPS_INVALID: $($_.Exception.Message)" }
    foreach ($forbidden in @($dotnet.forbiddenDependencies)) {
      if (-not (Is-NonEmptyString $forbidden)) { Fail 'CONTRACT_FORBIDDEN_DEPENDENCY_EMPTY'; continue }
      if ($depsText -match ('"' + [regex]::Escape([string]$forbidden) + '/')) { Gate "FORBIDDEN_DEPENDENCY_PRESENT: $forbidden" }
    }
  } else { Gate 'PAYLOAD_MISSING: dotnetSelfContained.deps' }
  foreach ($payloadFile in @($dotnet.payloadFiles)) { $null = Test-PayloadFile $payloadFile "dotnetSelfContained.$($payloadFile.path)" }
}

# WebView2 SDK/loader is payload; the Evergreen browser is either a verified installed capability
# or a pinned, hashed, Microsoft-signed offline installer.
$webView = $components.webView2Evergreen
if (Require-ComponentMetadata $webView 'webView2Evergreen' 'WEBVIEW2_EVERGREEN' ([string]$webView.delivery) 'sdkVersionPin') {
  if (-not (Is-NonEmptyString $webView.minimumRuntimeVersionPin)) { Gate 'CONTRACT_VERSION_PIN_MISSING: webView2Evergreen.minimumRuntimeVersionPin' }
  $null = Test-PayloadFile $webView.loader 'webView2Evergreen.loader'
  switch ([string]$webView.delivery) {
    'installed-capability' { Test-InstalledWebView2 $webView }
    'offline-installer' {
      if ($webView.offlineInstaller.enabled -isnot [bool] -or $webView.offlineInstaller.enabled -ne $true) { Gate 'WEBVIEW2_OFFLINE_INSTALLER_NOT_ENABLED' }
      if (-not (Is-NonEmptyString $webView.offlineInstaller.versionPin)) { Gate 'CONTRACT_VERSION_PIN_MISSING: webView2Evergreen.offlineInstaller' }
      $null = Test-PayloadFile $webView.offlineInstaller 'webView2Evergreen.offlineInstaller' -RequireSignature
    }
    default { Fail "CONTRACT_DELIVERY_INVALID: webView2Evergreen <= $($webView.delivery)" }
  }
}

# FFXED must run on an app-private Java image; global PATH Java is not release evidence.
$java = $components.javaPrivateRuntime
if (Require-ComponentMetadata $java 'javaPrivateRuntime' 'JAVA_PRIVATE_RUNTIME_FOR_FFXED' 'private-payload') {
  $null = Test-PayloadFile $java.javaExecutable 'javaPrivateRuntime.javaExecutable'
  $null = Test-PayloadFile $java.ffxedJar 'javaPrivateRuntime.ffxedJar'
  $releaseFile = Resolve-PayloadPath ([string]$java.runtimeVersionFile) 'javaPrivateRuntime.runtimeVersionFile'
  if ($releaseFile -and (Test-Path -LiteralPath $releaseFile -PathType Leaf)) {
    $releaseText = Get-Content -LiteralPath $releaseFile -Raw
    if ($releaseText -notmatch ('(?m)^JAVA_VERSION="?' + [regex]::Escape([string]$java.versionPin) + '(?:[+._-][^"\r\n]*)?"?\s*$')) {
      Gate "JAVA_VERSION_MISMATCH expected=$($java.versionPin)"
    } else { $evidence.Add("JAVA_PRIVATE_RUNTIME_OK version=$($java.versionPin)") }
  } else { Gate 'PAYLOAD_MISSING: javaPrivateRuntime.runtimeVersionFile' }
}

# The audited Keystone x64 binary imports the VC++ 2013 CRT. The selected release strategy is an
# offline Microsoft redistributable; absence or an unpinned hash blocks Candidate/Release.
$vc = $components.vcpp2013X64
if (Require-ComponentMetadata $vc 'vcpp2013X64' 'VCPLUSPLUS_2013_X64_FOR_KEYSTONE' 'offline-installer') {
  $installer = Test-PayloadFile $vc.installer 'vcpp2013X64.installer' -RequireSignature
  $keystone = Test-PayloadFile $vc.keystoneConsumer 'vcpp2013X64.keystoneConsumer'
  if ($keystone) {
    $nativeAscii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($keystone))
    foreach ($importName in @($vc.keystoneConsumer.requiredImports)) {
      if (-not (Is-NonEmptyString $importName)) { Fail 'CONTRACT_KEYSTONE_IMPORT_EMPTY'; continue }
      if ($nativeAscii.IndexOf([string]$importName, [StringComparison]::OrdinalIgnoreCase) -lt 0) { Gate "KEYSTONE_REQUIRED_IMPORT_MISSING: $importName" }
    }
  }
  foreach ($relative in @($vc.forbiddenPayloadPaths)) {
    $forbiddenPath = Resolve-PayloadPath ([string]$relative) "vcpp2013X64.forbiddenPayloadPaths.$relative"
    if ($forbiddenPath -and (Test-Path -LiteralPath $forbiddenPath -PathType Leaf)) { Gate "FORBIDDEN_ARCH_PAYLOAD_PRESENT: $relative" }
  }
}

$result = [ordered]@{
  schemaVersion = 1
  mode = $Mode
  strict = $strict
  target = 'win-x64'
  ready = ($fails.Count -eq 0)
  failures = @($fails)
  warnings = @($warnings)
  evidence = @($evidence)
}

if ($Json) {
  $result | ConvertTo-Json -Depth 5
} else {
  Write-Output "RUNTIME_DEPENDENCIES mode=$Mode strict=$strict contract=$contractFull"
  $evidence | ForEach-Object { Write-Output ("EVIDENCE: " + $_) }
  $warnings | ForEach-Object { Write-Output ("WARN: " + $_) }
  $fails | ForEach-Object { Write-Output ("FAIL: " + $_) }
  if ($fails.Count -eq 0) { Write-Output "RUNTIME_DEPENDENCIES_OK warnings=$($warnings.Count)" }
  else { Write-Output "RUNTIME_DEPENDENCIES_FAIL errors=$($fails.Count) warnings=$($warnings.Count)" }
}

if ($fails.Count -gt 0) { exit 1 }
exit 0
