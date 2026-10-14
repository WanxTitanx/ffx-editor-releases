<#
  Deterministic self-test for verify_runtime_dependencies.ps1. Fixtures use signed Windows x64
  binaries as inert PE samples; no fixture executable is launched. Temporary files are removed.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
$verify = Join-Path $PSScriptRoot 'verify_runtime_dependencies.ps1'
$mainContract = Join-Path $repo 'release\windows-runtime-prerequisites.json'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ffx-runtime-deps-test-' + [Guid]::NewGuid().ToString('N'))
$fails = New-Object System.Collections.Generic.List[string]
New-Item -ItemType Directory -Path $tmp | Out-Null

function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:fails.Add($Message) } }
function Ensure-Parent([string]$Path) { $null = New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) }
function Copy-Fixture([string]$Source, [string]$Root, [string]$Relative) {
  $destination = Join-Path $Root $Relative
  Ensure-Parent $destination
  Copy-Item -LiteralPath $Source -Destination $destination
  return $destination
}
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Save-Contract($Contract, [string]$Path) { $Contract | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8 }
function Invoke-Gate([string]$Root, [string]$Contract, [string]$Mode) {
  $output = (& pwsh -NoProfile -File $verify -PackageRoot $Root -ContractPath $Contract -Mode $Mode 2>&1) | Out-String
  return [pscustomobject]@{ Exit = $LASTEXITCODE; Output = $output }
}

try {
  # Regression: publish-profile properties are imported after the main project body. Product-only
  # compilation must not retain the dev constant while removing the corresponding source files.
  $project = Join-Path $repo 'FFXProjectEditor\FFXProjectEditor.csproj'
  $profileEvaluationRaw = (& dotnet msbuild $project -nologo -p:PublishProfile=win-x64-portable -getProperty:FFXIncludeDevTools -getProperty:DefineConstants -getProperty:IncludeSourceRevisionInInformationalVersion -getProperty:Deterministic -getProperty:DebugType -getProperty:RuntimeFrameworkVersion -getItem:Compile 2>&1) | Out-String
  $profileEvaluationExit = $LASTEXITCODE
  $profileEvaluation = $null
  try { $profileEvaluation = $profileEvaluationRaw | ConvertFrom-Json } catch { Assert $false 'publish profile evaluation must return JSON' }
  Assert ($profileEvaluationExit -eq 0) 'publish profile evaluation: expected exit 0'
  Assert ($profileEvaluation.Properties.FFXIncludeDevTools -ceq 'false') 'publish profile evaluation: FFXIncludeDevTools must be false'
  Assert ([string]$profileEvaluation.Properties.DefineConstants -notmatch '(^|;)FFX_INCLUDE_DEVTOOLS(;|$)') 'publish profile evaluation: dev constant must be absent'
  Assert ($profileEvaluation.Properties.IncludeSourceRevisionInInformationalVersion -ceq 'false') 'publish profile evaluation: IncludeSourceRevisionInInformationalVersion must be false'
  Assert ($profileEvaluation.Properties.Deterministic -ceq 'true') 'publish profile evaluation: Deterministic must be true'
  Assert ($profileEvaluation.Properties.DebugType -ceq 'none') 'publish profile evaluation: DebugType must be none'
  Assert ($profileEvaluation.Properties.RuntimeFrameworkVersion -ceq '8.0.30') 'publish profile evaluation: RuntimeFrameworkVersion must be 8.0.30'
  $compiledFiles = @($profileEvaluation.Items.Compile | ForEach-Object { ([string]$_.Identity).Replace('/', '\') })
  Assert ('Modules\Common\ViewerHub\NoclipDataRepair.cs' -cnotin $compiledFiles) 'publish profile evaluation: NoclipDataRepair.cs must be excluded'
  Assert (@($compiledFiles | Where-Object { $_ -like 'Tools\*' }).Count -eq 0) 'publish profile evaluation: Tools sources must be excluded'
  $profilePath = Join-Path $repo 'FFXProjectEditor\Properties\PublishProfiles\win-x64-portable.pubxml'
  $profileText = Get-Content -LiteralPath $profilePath -Raw
  Assert ($profileText -match '-PackageRoot &quot;\$\(PublishDir\)\.&quot;') 'publish profile: PackageRoot must neutralize PublishDir trailing slash before the quoted boundary'

  $signedX64 = Join-Path $env:SystemRoot 'System32\cmd.exe'
  Assert (Test-Path -LiteralPath $signedX64) 'fixture: cmd.exe x64 must exist'
  Assert ((Get-AuthenticodeSignature -LiteralPath $signedX64).Status -eq [System.Management.Automation.SignatureStatus]::Valid) 'fixture: cmd.exe must have valid Authenticode'

  $pkg = Join-Path $tmp 'package'
  New-Item -ItemType Directory -Path $pkg | Out-Null
  $runtimeConfig = Join-Path $pkg 'FFXProjectEditor.runtimeconfig.json'
  '{"runtimeOptions":{"tfm":"net8.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.30"}]}}' | Set-Content -LiteralPath $runtimeConfig -Encoding UTF8
  $deps = Join-Path $pkg 'FFXProjectEditor.deps.json'
  '{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0/win-x64"},"targets":{},"libraries":{}}' | Set-Content -LiteralPath $deps -Encoding UTF8

  $coreclr = Copy-Fixture $signedX64 $pkg 'coreclr.dll'
  $hostfxr = Copy-Fixture $signedX64 $pkg 'hostfxr.dll'
  $hostpolicy = Copy-Fixture $signedX64 $pkg 'hostpolicy.dll'
  $corelib = Copy-Fixture $signedX64 $pkg 'System.Private.CoreLib.dll'
  $loader = Copy-Fixture $signedX64 $pkg 'WebView2Loader.dll'
  $webInstaller = Copy-Fixture $signedX64 $pkg 'prerequisites\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
  $javaw = Copy-Fixture $signedX64 $pkg 'runtime\java\bin\javaw.exe'
  $jar = Join-Path $pkg 'ExternalLibs\FFXED\FFXED.jar'; Ensure-Parent $jar; [System.IO.File]::WriteAllBytes($jar, [byte[]](1, 2, 3, 4, 5))
  $vcInstaller = Copy-Fixture $signedX64 $pkg 'prerequisites\vc2013\vcredist_x64.exe'
  $keystone = Copy-Fixture (Join-Path $repo 'ExternalLibs\MemorySharp64\x64\keystone.dll') $pkg 'x64\keystone.dll'

  $contract = Get-Content -LiteralPath $mainContract -Raw | ConvertFrom-Json
  $javaRelease = Join-Path $pkg 'runtime\java\release'
  Ensure-Parent $javaRelease
  ('JAVA_VERSION="' + [string]$contract.components.javaPrivateRuntime.versionPin + '"') | Set-Content -LiteralPath $javaRelease -Encoding ASCII
  $contract.components.dotnetSelfContained.payloadFiles[0].sha256 = Hash $coreclr
  $contract.components.dotnetSelfContained.payloadFiles[1].sha256 = Hash $hostfxr
  $contract.components.dotnetSelfContained.payloadFiles[2].sha256 = Hash $hostpolicy
  $contract.components.dotnetSelfContained.payloadFiles[3].sha256 = Hash $corelib
  $contract.components.webView2Evergreen.delivery = 'offline-installer'
  $contract.components.webView2Evergreen.loader.sha256 = Hash $loader
  $contract.components.webView2Evergreen.offlineInstaller.enabled = $true
  $contract.components.webView2Evergreen.offlineInstaller.sha256 = Hash $webInstaller
  $contract.components.webView2Evergreen.offlineInstaller.installerFileVersionPin = (Get-Item -LiteralPath $webInstaller).VersionInfo.FileVersion
  $contract.components.javaPrivateRuntime.javaExecutable.sha256 = Hash $javaw
  $contract.components.javaPrivateRuntime.ffxedJar.sha256 = Hash $jar
  $contract.components.vcpp2013X64.installer.sha256 = Hash $vcInstaller
  $contract.components.vcpp2013X64.installer.installerFileVersionPin = (Get-Item -LiteralPath $vcInstaller).VersionInfo.FileVersion
  $contract.components.vcpp2013X64.keystoneConsumer.sha256 = Hash $keystone
  $contractPath = Join-Path $tmp 'fixture-contract.json'
  Save-Contract $contract $contractPath

  $positive = Invoke-Gate $pkg $contractPath 'Candidate'
  Assert ($positive.Exit -eq 0) "Candidate complete fixture: expected 0, got $($positive.Exit): $($positive.Output)"
  Assert ($positive.Output -match 'RUNTIME_DEPENDENCIES_OK') 'Candidate complete fixture: missing OK marker'

  $contract.components.javaPrivateRuntime.architecture = $null
  Save-Contract $contract $contractPath
  $missingArch = Invoke-Gate $pkg $contractPath 'Candidate'
  Assert ($missingArch.Exit -eq 1) 'Candidate missing Java architecture: expected exit 1'
  Assert ($missingArch.Output -match 'CONTRACT_ARCH_MISSING: javaPrivateRuntime') 'Candidate missing Java architecture: expected fail reason'
  $contract.components.javaPrivateRuntime.architecture = 'x64'

  $contract.components.vcpp2013X64.installer.sha256 = $null
  Save-Contract $contract $contractPath
  $missingHash = Invoke-Gate $pkg $contractPath 'Release'
  Assert ($missingHash.Exit -eq 1) 'Release missing VC++ hash: expected exit 1'
  Assert ($missingHash.Output -match 'CONTRACT_SHA256_MISSING: vcpp2013X64.installer') 'Release missing VC++ hash: expected fail reason'
  $contract.components.vcpp2013X64.installer.sha256 = Hash $vcInstaller

  [System.IO.File]::AppendAllText($jar, 'tamper')
  Save-Contract $contract $contractPath
  $tampered = Invoke-Gate $pkg $contractPath 'Candidate'
  Assert ($tampered.Exit -eq 1) 'Candidate tampered JAR: expected exit 1'
  Assert ($tampered.Output -match 'PAYLOAD_HASH_MISMATCH: javaPrivateRuntime.ffxedJar') 'Candidate tampered JAR: expected hash mismatch'

  $empty = Join-Path $tmp 'empty'; New-Item -ItemType Directory -Path $empty | Out-Null
  $diagnosticContract = Get-Content -LiteralPath $mainContract -Raw | ConvertFrom-Json
  $diagnosticContract.components.vcpp2013X64.installer.sha256 = $null
  $diagnosticContractPath = Join-Path $tmp 'diagnostic-incomplete-contract.json'
  Save-Contract $diagnosticContract $diagnosticContractPath
  $diagnostic = Invoke-Gate $empty $diagnosticContractPath 'Diagnostic'
  Assert ($diagnostic.Exit -eq 0) "Diagnostic incomplete main contract: expected warnings-only exit 0, got $($diagnostic.Exit)"
  Assert ($diagnostic.Output -match 'WARN: CONTRACT_SHA256_MISSING') 'Diagnostic incomplete main contract: expected hash warning'
  $strictMain = Invoke-Gate $empty $mainContract 'Candidate'
  Assert ($strictMain.Exit -eq 1) 'Candidate incomplete main contract: expected fail-closed exit 1'
} finally {
  $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
  $resolvedTmp = [System.IO.Path]::GetFullPath($tmp)
  if ($resolvedTmp.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTmp)) {
    Remove-Item -LiteralPath $resolvedTmp -Recurse -Force
  }
}

if ($fails.Count -gt 0) {
  $fails | ForEach-Object { Write-Output ('ASSERT-FAIL: ' + $_) }
  Write-Output "TEST_FAIL n=$($fails.Count)"
  exit 1
}
Write-Output 'TEST_OK (Windows runtime dependency gate)'
exit 0
