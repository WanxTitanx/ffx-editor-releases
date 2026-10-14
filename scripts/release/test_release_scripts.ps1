<#
  FFX Mod Studio — self-test dos scripts/contracts de release (§14.2, §10 gates). Sem framework.
  Exit 0 = todos os asserts passam; 1 = falha. Fixtures em %TEMP%, limpas ao final.
  Rode: pwsh -NoProfile -File scripts/release/test_release_scripts.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '\..\..')).Path
$scan = Join-Path $PSScriptRoot 'scan_portability.ps1'
$verify = Join-Path $PSScriptRoot 'verify_package.ps1'
$preflight = Join-Path $PSScriptRoot 'preflight_release.ps1'
$builderSource = Join-Path $PSScriptRoot 'build_portable.ps1'
$generator = Join-Path $PSScriptRoot 'generate_release_contracts.ps1'
$profileSource = Join-Path $repo 'FFXProjectEditor\Properties\PublishProfiles\win-x64-portable.pubxml'
$fails = New-Object System.Collections.Generic.List[string]
$runs = 0
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('ffx-release-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Assert([bool]$ok, [string]$msg) {
  $script:runs++
  if (-not $ok) { $script:fails.Add($msg) }
}

function Wait-ForPath([string]$Path, [int]$Milliseconds) {
  $deadline = [datetime]::UtcNow.AddMilliseconds($Milliseconds)
  while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) { return $true }
    Start-Sleep -Milliseconds 50
  }
  return (Test-Path -LiteralPath $Path -PathType Leaf)
}

# ---- 1) scanner: detecções e allowlist ----
Set-Content -LiteralPath (Join-Path $tmp 'path.txt') -Value 'C:\Users\wande\Documents\ffx-editor-main' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'drive.txt') -Value 'D:\FFX Extracted\FFX\ffx_ps2' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'unicodé.csproj') -Value '<Project><PropertyGroup><X>D:\FFX\ffx_ps2\master</X></PropertyGroup></Project>' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'official.html') -Value 'https://ffxmodstudio.com/api/releases/studio/stable' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'cdn.html') -Value 'https://cdn.jsdelivr.net/npm/three@0.165.0/build/three.module.js' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'clean.txt') -Value 'nothing suspicious here' -Encoding UTF8

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'drive.txt') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'drive.txt: esperado exit 2 (D:\ detectado)'

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'path.txt') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'path.txt: esperado exit 2 (C:\Users detectado)'

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'unicodé.csproj') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'csproj unicode: esperado exit 2 (arquivo .csproj examinado + D:\ detectado)'

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'official.html') | Out-Null
Assert ($LASTEXITCODE -eq 0) 'official.html: esperado exit 0 (origin oficial allowlisted)'

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'cdn.html') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'cdn.html: esperado exit 2 (CDN proibida detectada)'

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'clean.txt') | Out-Null
Assert ($LASTEXITCODE -eq 0) 'clean.txt: esperado exit 0 (zero match)'

# adversarial URL cases (Bloco 1)
Set-Content -LiteralPath (Join-Path $tmp 'http_official.html') -Value 'http://ffxmodstudio.com/' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'evil_sub.html') -Value 'https://evil.ffxmodstudio.com/' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'port.html') -Value 'https://ffxmodstudio.com:444/' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'userinfo.html') -Value 'https://ffxmodstudio.com@evil.example/' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $tmp 'loopback.html') -Value 'http://127.0.0.1:5489/static/x' -Encoding UTF8

& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'http_official.html') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'http_official: http do dominio deve falhar (2)'
& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'evil_sub.html') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'evil_sub: subdominio deve falhar (2)'
& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'port.html') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'port: porta nao default deve falhar (2)'
& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'userinfo.html') | Out-Null
Assert ($LASTEXITCODE -eq 2) 'userinfo: userinfo na URI deve falhar (2)'
& pwsh -NoProfile -File $scan -Root (Join-Path $tmp 'loopback.html') | Out-Null
Assert ($LASTEXITCODE -eq 0) 'loopback: 127.0.0.1 local deve ser permitido (0) (regra separada)'

$j = & pwsh -NoProfile -File $scan -Root $tmp -Json | ConvertFrom-Json
Assert ($j.schemaVersion -eq 1) 'Json: schemaVersion=1'
Assert ($j.filesScanned -ge 6) 'Json: filesScanned preenchido'
Assert ($j.matches.Count -ge 1) 'Json: matches preenchido (ConvertFrom-Json OK sem texto extra)'

# ---- 2) JSON contracts: parse + campos mínimos ----
foreach ($c in @('official-origins.json','package-allowlist.json','tool-dependencies.json','noclip-runtime.manifest.json')) {
  try { $o = Get-Content (Join-Path $repo "release\$c") -Raw | ConvertFrom-Json } catch { Assert $false "contract $c nao parseia"; continue }
}
$oo = Get-Content (Join-Path $repo 'release\official-origins.json') -Raw | ConvertFrom-Json
Assert ($oo.canonicalSiteOrigin -eq 'https://ffxmodstudio.com') 'origins: canonical .com'
$pa = Get-Content (Join-Path $repo 'release\package-allowlist.json') -Raw | ConvertFrom-Json
Assert ($pa.components.'studio-core-win-x64'.requiredFiles.Count -ge 5) 'allowlist: requiredFiles presente'
$td = Get-Content (Join-Path $repo 'release\tool-dependencies.json') -Raw | ConvertFrom-Json
Assert (($td.entries | Where-Object id -eq 'vgmstream-cli').classification -eq 'BUNDLED_REDISTRIBUTION_PROVEN') 'tool-deps: vgmstream BUNDLED_REDISTRIBUTION_PROVEN'

# ---- 3) preflight: JSON puro + modos clean/dirty + signing owner decision ----
$cleanRepo = Join-Path $tmp 'clean-repo'
New-Item -ItemType Directory -Force -Path $cleanRepo | Out-Null
& git -C $cleanRepo init -q
Assert ($LASTEXITCODE -eq 0) 'preflight fixture: git init'
& git -C $cleanRepo config user.email 'release-fixture@invalid.local'
& git -C $cleanRepo config user.name 'FFX Release Fixture'
Set-Content -LiteralPath (Join-Path $cleanRepo 'README.md') -Value 'fixture' -Encoding UTF8
& git -C $cleanRepo add -- README.md
& git -C $cleanRepo commit -q -m 'fixture'
Assert ($LASTEXITCODE -eq 0) 'preflight fixture: commit limpo'

$diagRaw = (& pwsh -NoProfile -File $preflight -Mode Diagnostic -RepoRoot $cleanRepo -Json) | Out-String
$diagExit = $LASTEXITCODE
$diag = $null
try { $diag = $diagRaw | ConvertFrom-Json } catch { Assert $false 'preflight Diagnostic -Json deve emitir somente JSON valido' }
Assert ($diagExit -eq 0) 'preflight Diagnostic clean: exit 0'
Assert ($diag.ready -eq $true) 'preflight Diagnostic clean: ready=true'
Assert ($diag.reason -eq $null) 'preflight Diagnostic clean: reason=null'
Assert ($diag.signingState -eq 'NOT_SIGNED_DEFERRED_BY_OWNER_DECISION') 'preflight: Authenticode deferred pela decisao do owner'

$candidateRaw = (& pwsh -NoProfile -File $preflight -Mode Candidate -RepoRoot $cleanRepo -Json) | Out-String
$candidateExit = $LASTEXITCODE
$candidate = $candidateRaw | ConvertFrom-Json
Assert ($candidateExit -eq 0) 'preflight Candidate clean: exit 0'
Assert ($candidate.ready -eq $true) 'preflight Candidate clean: ready=true'

$releaseRaw = (& pwsh -NoProfile -File $preflight -Mode Release -RepoRoot $cleanRepo -Json) | Out-String
$releaseExit = $LASTEXITCODE
$release = $releaseRaw | ConvertFrom-Json
Assert ($releaseExit -eq 1) 'preflight Release clean: permanece fail-closed ate os gates finais'
Assert ($release.reason -eq 'RELEASE_NOT_YET_VALIDATED_VERSION_GATES') 'preflight Release clean: motivo version gates'

Set-Content -LiteralPath (Join-Path $cleanRepo 'dirty.txt') -Value 'dirty' -Encoding UTF8
$dirtyRaw = (& pwsh -NoProfile -File $preflight -Mode Candidate -RepoRoot $cleanRepo -Json) | Out-String
$dirtyExit = $LASTEXITCODE
$dirty = $dirtyRaw | ConvertFrom-Json
Assert ($dirtyExit -eq 1) 'preflight Candidate dirty: exit 1'
Assert ($dirty.reason -eq 'GIT_DIRTY') 'preflight Candidate dirty: reason=GIT_DIRTY'
Assert ($dirty.sourceDirty -eq $true) 'preflight Candidate dirty: sourceDirty=true'

# Product/source payload assertions are intentionally deferred to the classified integration tasks.
# This infrastructure harness must run green before those files are materialized in the clean tree.
Write-Output 'TEST_SKIP product-dependent SEID sanitization (validated by product integration suite)'

# ---- 5) verify_package: pacote sem manifest deve falhar fechado ----
$pkg = Join-Path $tmp 'pkg'; New-Item -ItemType Directory -Force -Path $pkg | Out-Null
& pwsh -NoProfile -File $verify -PackageRoot $pkg | Out-Null
Assert ($LASTEXITCODE -eq 2) 'verify: pacote sem release-manifest deve falhar (2)'

# ---- 6) bundled Phyre helper: product CLI help must not fall through to an export ----
$phyreHelper = Join-Path $repo 'ExternalLibs\Tools\PhyreMapExportLab\win-x64\PhyreMapExportLab.exe'
if (Test-Path -LiteralPath $phyreHelper -PathType Leaf) {
  $phyreHelp = (& $phyreHelper --help 2>&1) | Out-String
  Assert ($LASTEXITCODE -eq 0) 'Phyre helper: --help deve sair 0 sem tentar usar ps3data'
  Assert ($phyreHelp.Contains('PhyreMapExportLab.exe export')) 'Phyre helper: help portátil deve listar o comando export'
} else {
  Write-Output 'TEST_SKIP payload-dependent Phyre helper (not materialized in infrastructure bootstrap)'
}

# ---- 7) builder containment and mode propagation use a disposable clean repository ----
$builderRepo = Join-Path $tmp 'builder-repo'
$builderScripts = Join-Path $builderRepo 'scripts\release'
$builderBin = Join-Path $tmp 'fake-bin'
$dotnetLog = Join-Path $tmp 'dotnet-args.log'
$verifyModeLog = Join-Path $tmp 'verify-modes.log'
New-Item -ItemType Directory -Path $builderScripts -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $builderRepo 'FFXProjectEditor') -Force | Out-Null
New-Item -ItemType Directory -Path $builderBin -Force | Out-Null
Copy-Item -LiteralPath $builderSource -Destination (Join-Path $builderScripts 'build_portable.ps1')
Copy-Item -LiteralPath $preflight -Destination (Join-Path $builderScripts 'preflight_release.ps1')
@'
param([Parameter(Mandatory=$true)][string]$PackageRoot,[string]$Mode='Diagnostic')
[IO.File]::AppendAllText($env:FFX_TEST_VERIFY_MODE_LOG, $Mode + "`n")
Write-Output 'VERIFY_OK'
exit 0
'@ | Set-Content -LiteralPath (Join-Path $builderScripts 'verify_package.ps1') -Encoding utf8NoBOM
@'
@echo off
if "%~1"=="--version" (
  echo 8.0.100
  exit /b 0
)
if "%~1"=="--list-sdks" (
  echo 8.0.100 [fixture]
  exit /b 0
)
>>"%FFX_TEST_DOTNET_LOG%" echo %*
set "FFX_TEST_OUT="
:parse
if "%~1"=="" goto parsed
if "%~1"=="-o" set "FFX_TEST_OUT=%~2"
shift
goto parse
:parsed
if "%FFX_TEST_OUT%"=="" exit /b 9
if not exist "%FFX_TEST_OUT%" mkdir "%FFX_TEST_OUT%"
if defined FFX_TEST_DOTNET_SWAP_TARGET (
  pwsh -NoProfile -File "%FFX_TEST_DOTNET_MUTATOR%" -Mode Swap -OutputDirectory "%FFX_TEST_OUT%" -Target "%FFX_TEST_DOTNET_SWAP_TARGET%" -FixtureRoot "%FFX_TEST_FIXTURE_ROOT%"
  exit /b %ERRORLEVEL%
)
>"%FFX_TEST_OUT%\FFXProjectEditor.exe" echo fixture-apphost
if defined FFX_TEST_DOTNET_CHILD_REPARSE_TARGET (
  pwsh -NoProfile -File "%FFX_TEST_DOTNET_MUTATOR%" -Mode Child -OutputDirectory "%FFX_TEST_OUT%" -Target "%FFX_TEST_DOTNET_CHILD_REPARSE_TARGET%" -FixtureRoot "%FFX_TEST_FIXTURE_ROOT%"
  exit /b %ERRORLEVEL%
)
exit /b 0
'@ | Set-Content -LiteralPath (Join-Path $builderBin 'dotnet.cmd') -Encoding ASCII
@'
param(
  [Parameter(Mandatory=$true)][ValidateSet('Swap','Child')][string]$Mode,
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [Parameter(Mandatory=$true)][string]$Target,
  [Parameter(Mandatory=$true)][string]$FixtureRoot
)
$root = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\','/')
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
$targetFull = [IO.Path]::GetFullPath($Target).TrimEnd('\','/')
foreach ($candidate in @($output,$targetFull)) {
  $relative = [IO.Path]::GetRelativePath($root,$candidate)
  if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('..\') -or $relative.StartsWith('../')) {
    throw "fixture mutation path escaped temp root: $candidate"
  }
}
if ($Mode -ceq 'Swap') {
  if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'fixture swap requires empty output' }
  [IO.Directory]::Delete($output,$false)
  New-Item -ItemType Junction -Path $output -Target $targetFull | Out-Null
} else {
  New-Item -ItemType Junction -Path (Join-Path $output 'linked-external') -Target $targetFull | Out-Null
}
'@ | Set-Content -LiteralPath (Join-Path $builderBin 'mutate-output.ps1') -Encoding utf8NoBOM
'<Project Sdk="Microsoft.NET.Sdk"></Project>' | Set-Content -LiteralPath (Join-Path $builderRepo 'FFXProjectEditor\FFXProjectEditor.csproj') -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $builderRepo '.gitignore') -Value "work/`n" -Encoding utf8NoBOM
& git -C $builderRepo init -q
& git -C $builderRepo config user.email 'release-fixture@invalid.local'
& git -C $builderRepo config user.name 'FFX Release Fixture'
& git -C $builderRepo config core.autocrlf false
& git -C $builderRepo add -- .gitignore FFXProjectEditor scripts
& git -C $builderRepo commit -q -m 'builder fixture'
Assert ($LASTEXITCODE -eq 0) 'builder fixture: clean commit'

$oldPath = $env:PATH
$env:PATH = $builderBin + [IO.Path]::PathSeparator + $oldPath
$env:FFX_TEST_DOTNET_LOG = $dotnetLog
$env:FFX_TEST_VERIFY_MODE_LOG = $verifyModeLog
$env:FFX_TEST_DOTNET_MUTATOR = Join-Path $builderBin 'mutate-output.ps1'
$env:FFX_TEST_FIXTURE_ROOT = $tmp
$fixtureBuilder = Join-Path $builderScripts 'build_portable.ps1'
$allowedRoot = Join-Path $builderRepo 'work\release-readiness'
New-Item -ItemType Directory -Path $allowedRoot -Force | Out-Null

function Invoke-FixtureBuild([string]$Mode, [string]$OutputDirectory) {
  $output = (& pwsh -NoProfile -File $fixtureBuilder -Mode $Mode -OutDir $OutputDirectory `
    -DotnetCommand (Join-Path $builderBin 'dotnet.cmd') 2>&1) | Out-String
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
}
function Get-DotnetLogLength { if (Test-Path -LiteralPath $dotnetLog) { return (Get-Item -LiteralPath $dotnetLog).Length }; return 0 }

$beforeCalls = Get-DotnetLogLength
$rootJunctionTarget = Join-Path $tmp 'builder-root-junction-target'
Remove-Item -LiteralPath $allowedRoot -Recurse -Force
New-Item -ItemType Directory -Path $rootJunctionTarget -Force | Out-Null
New-Item -ItemType Junction -Path $allowedRoot -Target $rootJunctionTarget | Out-Null
$result = Invoke-FixtureBuild 'Diagnostic' (Join-Path $allowedRoot 'root-escape-package')
Assert ($result.ExitCode -ne 0 -and $result.Output -match 'ALLOWED_ROOT_.*ESCAPE' -and
  (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects an allowed root that is itself a junction outside the producing repo before publish'
Assert (-not (Test-Path -LiteralPath (Join-Path $rootJunctionTarget 'root-escape-package'))) `
  'builder allowed-root junction target must remain untouched'
Remove-Item -LiteralPath $allowedRoot -Force
New-Item -ItemType Directory -Path $allowedRoot -Force | Out-Null
$beforeCalls = Get-DotnetLogLength

$siblingEscape = Join-Path $builderRepo 'work\release-readiness-escape\package'
$result = Invoke-FixtureBuild 'Diagnostic' $siblingEscape
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects sibling-prefix escape before publish'
Assert (-not (Test-Path -LiteralPath $siblingEscape)) 'sibling-prefix escape must not create output'

$traversalEscape = Join-Path $builderRepo 'work\release-readiness\..\release-readiness-escape\package'
$result = Invoke-FixtureBuild 'Diagnostic' $traversalEscape
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects traversal escape before publish'

$result = Invoke-FixtureBuild 'Diagnostic' $allowedRoot
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects the allowed root itself before publish'

$nonEmpty = Join-Path $allowedRoot 'nonempty\package'
New-Item -ItemType Directory -Path $nonEmpty -Force | Out-Null
Set-Content -LiteralPath (Join-Path $nonEmpty 'keep.txt') -Value 'keep' -NoNewline -Encoding ASCII
$keepHash = (Get-FileHash -LiteralPath (Join-Path $nonEmpty 'keep.txt') -Algorithm SHA256).Hash
$result = Invoke-FixtureBuild 'Diagnostic' $nonEmpty
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects non-empty output before publish'
Assert ((Get-FileHash -LiteralPath (Join-Path $nonEmpty 'keep.txt') -Algorithm SHA256).Hash -ceq $keepHash) 'builder must not remove or overwrite non-empty output'

$outsideTarget = Join-Path $tmp 'builder-outside-target'
New-Item -ItemType Directory -Path $outsideTarget | Out-Null
$junction = Join-Path $allowedRoot 'linked-out'
New-Item -ItemType Junction -Path $junction -Target $outsideTarget | Out-Null
$result = Invoke-FixtureBuild 'Diagnostic' (Join-Path $junction 'package')
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeCalls) 'builder rejects reparse ancestor escaping the allowed root before publish'
Assert (-not (Test-Path -LiteralPath (Join-Path $outsideTarget 'package'))) 'escaping reparse target must remain untouched'

$postPublishSwapTarget = Join-Path $tmp 'post-publish-swap-target'
New-Item -ItemType Directory -Path $postPublishSwapTarget | Out-Null
$externalPdb = Join-Path $postPublishSwapTarget 'must-survive.pdb'
Set-Content -LiteralPath $externalPdb -Value 'external-pdb' -NoNewline -Encoding ASCII
$externalPdbHash = (Get-FileHash -LiteralPath $externalPdb -Algorithm SHA256).Hash
$postPublishSwapOut = Join-Path $allowedRoot 'post-publish-swap\package'
$env:FFX_TEST_DOTNET_SWAP_TARGET = $postPublishSwapTarget
$result = Invoke-FixtureBuild 'Diagnostic' $postPublishSwapOut
$env:FFX_TEST_DOTNET_SWAP_TARGET = $null
$externalPdbPreserved = (Test-Path -LiteralPath $externalPdb -PathType Leaf) -and
  ((Get-FileHash -LiteralPath $externalPdb -Algorithm SHA256).Hash -ceq $externalPdbHash)
Assert ($result.ExitCode -ne 0 -and $result.Output -match 'POST_PUBLISH_TRUST_FAILED') `
  'builder must reject OutDir swapped to an escaping junction immediately after dotnet returns'
Assert ($externalPdbPreserved -and -not (Test-Path -LiteralPath (Join-Path $postPublishSwapTarget 'release-manifest.json'))) `
  'post-publish OutDir swap must not delete external PDBs or write a manifest through the junction'
if (Test-Path -LiteralPath $postPublishSwapOut) { Remove-Item -LiteralPath $postPublishSwapOut -Force }

$postPublishChildTarget = Join-Path $tmp 'post-publish-child-target'
New-Item -ItemType Directory -Path $postPublishChildTarget | Out-Null
$childExternalPdb = Join-Path $postPublishChildTarget 'must-survive-child.pdb'
Set-Content -LiteralPath $childExternalPdb -Value 'external-child-pdb' -NoNewline -Encoding ASCII
$childPdbHash = (Get-FileHash -LiteralPath $childExternalPdb -Algorithm SHA256).Hash
$postPublishChildOut = Join-Path $allowedRoot 'post-publish-child\package'
$env:FFX_TEST_DOTNET_CHILD_REPARSE_TARGET = $postPublishChildTarget
$result = Invoke-FixtureBuild 'Diagnostic' $postPublishChildOut
$env:FFX_TEST_DOTNET_CHILD_REPARSE_TARGET = $null
$childPdbPreserved = (Test-Path -LiteralPath $childExternalPdb -PathType Leaf) -and
  ((Get-FileHash -LiteralPath $childExternalPdb -Algorithm SHA256).Hash -ceq $childPdbHash)
Assert ($result.ExitCode -ne 0 -and $result.Output -match 'POST_PUBLISH_TRUST_FAILED.*REPARSE') `
  'builder must reject any child reparse in the published output tree before recursive operations'
Assert ($childPdbPreserved -and -not (Test-Path -LiteralPath (Join-Path $postPublishChildOut 'release-manifest.json'))) `
  'post-publish child reparse rejection must not delete external PDBs or write a manifest'
if (Test-Path -LiteralPath (Join-Path $postPublishChildOut 'linked-external')) { Remove-Item -LiteralPath (Join-Path $postPublishChildOut 'linked-external') -Force }

$candidateOut = Join-Path $allowedRoot 'candidate\package'
$result = Invoke-FixtureBuild 'Candidate' $candidateOut
Assert ($result.ExitCode -eq 0) ('builder Candidate fixture must pass: ' + $result.Output)
if (-not (Test-Path -LiteralPath (Join-Path $candidateOut 'release-manifest.json') -PathType Leaf)) {
  throw ('builder Candidate did not produce a manifest: ' + $result.Output)
}
$candidateManifest = Get-Content -LiteralPath (Join-Path $candidateOut 'release-manifest.json') -Raw | ConvertFrom-Json
Assert ($candidateManifest.buildMode -ceq 'Candidate') 'Candidate must reach the package manifest'
Assert ((Get-Content -LiteralPath $dotnetLog -Raw).Contains('-p:FFXRuntimeVerificationMode=Candidate')) 'Candidate must reach dotnet publish/MSBuild runtime verification mode'
Assert ((Get-Content -LiteralPath $verifyModeLog -Raw) -match '(?m)^Candidate$') 'Candidate must reach package verifier'

$diagnosticOut = Join-Path $allowedRoot 'diagnostic\package'
$result = Invoke-FixtureBuild 'Diagnostic' $diagnosticOut
Assert ($result.ExitCode -eq 0) ('builder Diagnostic fixture must remain functional: ' + $result.Output)
$diagnosticManifest = Get-Content -LiteralPath (Join-Path $diagnosticOut 'release-manifest.json') -Raw | ConvertFrom-Json
Assert ($diagnosticManifest.buildMode -ceq 'Diagnostic') 'Diagnostic must reach the package manifest'
Assert ((Get-Content -LiteralPath $dotnetLog -Raw).Contains('-p:FFXRuntimeVerificationMode=Diagnostic')) 'Diagnostic must reach dotnet publish/MSBuild runtime verification mode'

$candidateDirtyOut = Join-Path $allowedRoot 'candidate-dirty\package'
$diagnosticDirtyOut = Join-Path $allowedRoot 'diagnostic-dirty\package'
Set-Content -LiteralPath (Join-Path $builderRepo 'candidate-preflight-dirty.txt') -Value 'dirty' -NoNewline -Encoding ASCII
$beforeDirtyCandidateCalls = Get-DotnetLogLength
$result = Invoke-FixtureBuild 'Candidate' $candidateDirtyOut
Assert ($result.ExitCode -ne 0 -and $result.Output -match 'preflight failed.*mode=Candidate' -and
  (Get-DotnetLogLength) -eq $beforeDirtyCandidateCalls -and -not (Test-Path -LiteralPath $candidateDirtyOut)) `
  'builder must behaviorally propagate Candidate to preflight and reject dirty source before publish'
$result = Invoke-FixtureBuild 'Diagnostic' $diagnosticDirtyOut
Assert ($result.ExitCode -eq 0) ('builder Diagnostic must preserve dirty diagnostic behavior: ' + $result.Output)
$diagnosticDirtyManifest = Get-Content -LiteralPath (Join-Path $diagnosticDirtyOut 'release-manifest.json') -Raw | ConvertFrom-Json
Assert ([int]$diagnosticDirtyManifest.sourceDirty -gt 0) 'Diagnostic dirty build must record sourceDirty in its manifest'
Remove-Item -LiteralPath (Join-Path $builderRepo 'candidate-preflight-dirty.txt') -Force

$beforeReleaseCalls = Get-DotnetLogLength
$releaseOut = Join-Path $allowedRoot 'release\package'
$result = Invoke-FixtureBuild 'Release' $releaseOut
Assert ($result.ExitCode -ne 0 -and (Get-DotnetLogLength) -eq $beforeReleaseCalls) 'builder must reject Release before publish'
Assert (-not (Test-Path -LiteralPath $releaseOut)) 'rejected Release must not create output'
$env:PATH = $oldPath

# ---- 8) the real imported publish profile enforces Candidate and removes Tools compile sources ----
$profileFixture = Join-Path $tmp 'profile-fixture'
$profileProjectRoot = Join-Path $profileFixture 'project'
$profileScripts = Join-Path $profileFixture 'scripts\release'
$profilePackage = Join-Path $profileFixture 'package'
$profileModeLog = Join-Path $tmp 'profile-runtime-modes.log'
$profileCompileLog = Join-Path $tmp 'profile-compile-items.log'
New-Item -ItemType Directory -Path (Join-Path $profileProjectRoot 'Tools') -Force | Out-Null
New-Item -ItemType Directory -Path $profileScripts -Force | Out-Null
New-Item -ItemType Directory -Path $profilePackage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $profileFixture 'release') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $profileProjectRoot 'Program.cs') -Value 'internal static class Program { static void Main() {} }' -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $profileProjectRoot 'Tools\MustNotCompile.cs') -Value 'this is deliberately invalid C#' -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $profileFixture 'release\windows-runtime-prerequisites.json') -Value '{}' -Encoding utf8NoBOM
@'
param(
  [Parameter(Mandatory=$true)][string]$PackageRoot,
  [Parameter(Mandatory=$true)][string]$ContractPath,
  [Parameter(Mandatory=$true)][string]$Mode
)
[IO.File]::AppendAllText($env:FFX_TEST_PROFILE_MODE_LOG, ("{0}|{1}|{2}`n" -f $Mode,$PackageRoot,$ContractPath))
exit 0
'@ | Set-Content -LiteralPath (Join-Path $profileScripts 'verify_runtime_dependencies.ps1') -Encoding utf8NoBOM
$profileProject = Join-Path $profileProjectRoot 'ProfileFixture.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <Import Project="$profileSource" />
  <Target Name="ProbePortableProfile" DependsOnTargets="PrepareForBuild;ValidateFFXRuntimeVerificationMode;VerifyWindowsRuntimeDependencies">
    <WriteLinesToFile File="`$(ProfileCompileLog)" Lines="@(Compile)" Overwrite="true" />
  </Target>
</Project>
"@ | Set-Content -LiteralPath $profileProject -Encoding utf8NoBOM
$env:FFX_TEST_PROFILE_MODE_LOG = $profileModeLog
function Invoke-ProfileProbe([string]$Mode) {
  $raw = (& dotnet msbuild $profileProject -t:ProbePortableProfile --nologo `
    "-p:FFXRuntimeVerificationMode=$Mode" "-p:ProfileCompileLog=$profileCompileLog" `
    "-p:PublishDir=$profilePackage\" 2>&1) | Out-String
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $raw.Trim() }
}
$profileResult = Invoke-ProfileProbe 'Candidate'
Assert ($profileResult.ExitCode -eq 0) ('real publish profile Candidate probe must pass: ' + $profileResult.Output)
$compileItems = if (Test-Path -LiteralPath $profileCompileLog) { Get-Content -LiteralPath $profileCompileLog -Raw } else { '' }
Assert ($compileItems -match 'Program\.cs' -and $compileItems -notmatch '(?im)(^|[\\/])Tools[\\/]') `
  'real imported publish profile must remove Tools source from the evaluated Compile items'
$profileModes = if (Test-Path -LiteralPath $profileModeLog) { Get-Content -LiteralPath $profileModeLog -Raw } else { '' }
Assert ($profileModes -match '(?m)^Candidate\|') 'real VerifyWindowsRuntimeDependencies target must receive Candidate'
$profileLogLength = if (Test-Path -LiteralPath $profileModeLog) { (Get-Item -LiteralPath $profileModeLog).Length } else { 0 }
foreach ($invalidProfileMode in @('Release', 'Invalid')) {
  $profileResult = Invoke-ProfileProbe $invalidProfileMode
  Assert ($profileResult.ExitCode -ne 0 -and $profileResult.Output -match 'must be Diagnostic or Candidate') `
    "real publish profile must reject mode $invalidProfileMode"
  Assert ((Get-Item -LiteralPath $profileModeLog).Length -eq $profileLogLength) `
    "rejected profile mode $invalidProfileMode must not invoke the runtime verifier"
}
$env:FFX_TEST_PROFILE_MODE_LOG = $null

# ---- 9) contract generation is staging-only and leaves official contracts/Git untouched ----
$contractRun = Join-Path $repo ('work\release-readiness\contract-test-' + [Guid]::NewGuid().ToString('N'))
$contractPackage = Join-Path $contractRun 'package'
$contractStaging = Join-Path $contractRun 'contracts-staging'
New-Item -ItemType Directory -Path $contractPackage -Force | Out-Null
function Write-ContractFixtureFile([string]$RelativePath) {
  $full = Join-Path $contractPackage $RelativePath
  New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
  Set-Content -LiteralPath $full -Value 'fixture' -NoNewline -Encoding ASCII
}
function Get-ContractPackageFingerprint {
  $rows = Get-ChildItem -LiteralPath $contractPackage -Recurse -File -Force | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($contractPackage, $_.FullName).Replace('\', '/')
    '{0}|{1}|{2}' -f $relative, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
  return [string]::Join("`n", @($rows | Sort-Object -CaseSensitive))
}
foreach ($relative in @(
  'Keystone.Net.dll', 'x64\keystone.dll', 'ExternalLibs\FFXED\FFXED.jar',
  'runtime\java\bin\javaw.exe', 'tools\vgmstream\vgmstream-cli.exe',
  'tools\fsbext\fsbext.exe', 'tools\fsbankcl\fsbankcl.exe',
  'tools\PhyreMapExportLab\PhyreMapExportLab.exe', 'Microsoft.Web.WebView2.Core.dll',
  'WebView2Loader.dll', 'prerequisites\webview2\installer.exe', 'prerequisites\vc2013\vcredist_x64.exe')) {
  Write-ContractFixtureFile $relative
}
[ordered]@{ schemaVersion = 2; target = 'win-x64'; selfContained = $true } |
  ConvertTo-Json | Set-Content -LiteralPath (Join-Path $contractPackage 'release-manifest.json') -Encoding utf8NoBOM

$officialPackagePolicy = Join-Path $repo 'release\package-allowlist.json'
$officialToolPolicy = Join-Path $repo 'release\tool-dependencies.json'
$officialPackageHash = (Get-FileHash -LiteralPath $officialPackagePolicy -Algorithm SHA256).Hash
$officialToolHash = (Get-FileHash -LiteralPath $officialToolPolicy -Algorithm SHA256).Hash
$gitBeforeContracts = (& git -C $repo status --porcelain=v1) | Out-String

$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory $contractStaging 2>&1) | Out-String
$generatorExit = $LASTEXITCODE
Assert ($generatorExit -eq 0) ('contract staging fixture must pass: ' + $generatorRaw.Trim())
Assert ((Test-Path -LiteralPath (Join-Path $contractStaging 'package-allowlist.json')) -and
  (Test-Path -LiteralPath (Join-Path $contractStaging 'tool-dependencies.json'))) 'contract generator must emit both candidates in staging'
Assert ((Get-FileHash -LiteralPath $officialPackagePolicy -Algorithm SHA256).Hash -ceq $officialPackageHash -and
  (Get-FileHash -LiteralPath $officialToolPolicy -Algorithm SHA256).Hash -ceq $officialToolHash) 'contract staging must not mutate official release contracts'
$gitAfterContracts = (& git -C $repo status --porcelain=v1) | Out-String
Assert ($gitAfterContracts -ceq $gitBeforeContracts) 'contract staging must not alter Git state'

$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory (Join-Path $repo 'release') 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0) 'contract generator must reject the official release directory'
$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory $repo 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0) 'contract generator must reject an ancestor of official release directory'
$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory (Join-Path $tmp 'contracts-staging') 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0) 'contract generator must reject output outside work/release-readiness'
$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory $contractStaging 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0) 'contract generator must reject a non-empty staging directory'
$insidePackageStaging = Join-Path $contractPackage 'contracts-staging'
$generatorRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory $insidePackageStaging 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0 -and -not (Test-Path -LiteralPath $insidePackageStaging)) 'contract generator must never stage inside or mutate PackageRoot'

$packageBeforeAlias = Get-ContractPackageFingerprint
$aliasParent = Join-Path $contractRun 'canonical-alias'
New-Item -ItemType Directory -Path $aliasParent -Force | Out-Null
$aliasStaging = Join-Path $aliasParent 'contracts-staging'
New-Item -ItemType Junction -Path $aliasStaging -Target $contractPackage | Out-Null
$generatorAliasRaw = (& pwsh -NoProfile -File $generator -PackageRoot $contractPackage -OutputDirectory $aliasStaging 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0 -and $generatorAliasRaw -match 'OUTPUT_DIRECTORY_CANONICAL_OVERLAP_PACKAGE_ROOT') `
  'contract generator must reject a logical staging alias physically targeting PackageRoot'
Assert ((Get-ContractPackageFingerprint) -ceq $packageBeforeAlias) `
  'contract generator canonical alias rejection must leave PackageRoot byte/entry unchanged'
Remove-Item -LiteralPath $aliasStaging -Force

$packageBeforeBoundarySwap = Get-ContractPackageFingerprint
$boundaryOutput = Join-Path $contractRun 'boundary-swap\contracts-staging'
$boundarySignal = Join-Path $tmp 'contract-boundary-ready.signal'
$boundaryContinue = $boundarySignal + '.continue'
$boundaryStdout = Join-Path $tmp 'contract-boundary.stdout.log'
$boundaryStderr = Join-Path $tmp 'contract-boundary.stderr.log'
$oldContractHook = $env:FFX_RELEASE_CONTRACT_TEST_HOOK
$env:FFX_RELEASE_CONTRACT_TEST_HOOK = 'write-pause-v1'
try {
  $boundaryProcess = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @(
    '-NoProfile', '-File', $generator, '-PackageRoot', $contractPackage, '-OutputDirectory', $boundaryOutput,
    '-TestPauseBeforeContractWritesSignal', $boundarySignal
  ) -RedirectStandardOutput $boundaryStdout -RedirectStandardError $boundaryStderr -PassThru -WindowStyle Hidden
  $boundaryObserved = Wait-ForPath $boundarySignal 3000
  Assert $boundaryObserved 'contract writer boundary hook must pause after creating its empty staging directory'
  if ($boundaryObserved) {
    Remove-Item -LiteralPath $boundaryOutput -Force
    New-Item -ItemType Junction -Path $boundaryOutput -Target $contractPackage | Out-Null
    Set-Content -LiteralPath $boundaryContinue -Value 'continue' -NoNewline -Encoding ASCII
  }
  if (-not $boundaryProcess.WaitForExit(10000)) { $boundaryProcess.Kill(); $boundaryProcess.WaitForExit() }
  $boundaryExit = $boundaryProcess.ExitCode
  $boundaryProcess.Dispose()
} finally {
  $env:FFX_RELEASE_CONTRACT_TEST_HOOK = $oldContractHook
}
$boundaryRaw = ((Get-Content -LiteralPath $boundaryStdout -Raw -ErrorAction SilentlyContinue) + "`n" +
  (Get-Content -LiteralPath $boundaryStderr -Raw -ErrorAction SilentlyContinue))
Assert ($boundaryExit -ne 0 -and $boundaryRaw -match 'OUTPUT_DIRECTORY_CANONICAL_OVERLAP_PACKAGE_ROOT') `
  'contract generator must re-resolve package/output overlap immediately before CreateNew'
Assert ((Get-ContractPackageFingerprint) -ceq $packageBeforeBoundarySwap) `
  'contract writer boundary swap must leave PackageRoot byte/entry unchanged'
if (Test-Path -LiteralPath $boundaryOutput) { Remove-Item -LiteralPath $boundaryOutput -Force }

$generatorFixtureRepo = Join-Path $tmp 'generator-root-fixture'
$generatorFixtureScripts = Join-Path $generatorFixtureRepo 'scripts\release'
$generatorRootTarget = Join-Path $tmp 'generator-root-junction-target'
$generatorAllowedRoot = Join-Path $generatorFixtureRepo 'work\release-readiness'
New-Item -ItemType Directory -Path $generatorFixtureScripts -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $generatorFixtureRepo 'work') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $generatorFixtureRepo 'release') -Force | Out-Null
New-Item -ItemType Directory -Path $generatorRootTarget -Force | Out-Null
Copy-Item -LiteralPath $generator -Destination (Join-Path $generatorFixtureScripts 'generate_release_contracts.ps1')
New-Item -ItemType Junction -Path $generatorAllowedRoot -Target $generatorRootTarget | Out-Null
$generatorRootPackage = Join-Path $generatorAllowedRoot 'run\package'
$generatorRootStaging = Join-Path $generatorAllowedRoot 'run\contracts-staging'
Copy-Item -LiteralPath $contractPackage -Destination $generatorRootPackage -Recurse
$generatorRootRaw = (& pwsh -NoProfile -File (Join-Path $generatorFixtureScripts 'generate_release_contracts.ps1') `
  -PackageRoot $generatorRootPackage -OutputDirectory $generatorRootStaging 2>&1) | Out-String
Assert ($LASTEXITCODE -ne 0 -and $generatorRootRaw -match 'ALLOWED_ROOT_.*ESCAPE' -and
  -not (Test-Path -LiteralPath (Join-Path $generatorRootTarget 'run\contracts-staging'))) `
  'contract generator must reject when its allowed release root is itself a junction outside the producing repo'
Remove-Item -LiteralPath $contractRun -Recurse -Force

# ---- 13) Map Viewer anchor pins stay attached to the movable userData record ----
$mapOverlayPath = Join-Path $repo 'RuntimeTools\FFXMapViewerWeb\aurora-overlay.js'
$mapOverlayRaw = if (Test-Path -LiteralPath $mapOverlayPath -PathType Leaf) {
  Get-Content -LiteralPath $mapOverlayPath -Raw
} else {
  ''
}
$anchorUserDataMatch = [regex]::Match(
  $mapOverlayRaw,
  'mesh[.]userData\s*=\s*\{\s*role:\s*a[.]role,\s*index:\s*a[.]index,\s*anchor:\s*a,\s*stem,\s*(?<tail>[^}]*)\}',
  [Text.RegularExpressions.RegexOptions]::CultureInvariant)
Assert ($anchorUserDataMatch.Success -and $anchorUserDataMatch.Groups['tail'].Value -match '(^|[,\s])pin([,\s]|$)') `
  'Map Viewer movable anchor userData must retain its vertical pin reference'

# Exact third-party/runtime payload bytes must bypass Git text normalization.
$bytePreservedPayloadSamples = @(
  'ExternalLibs/FFXED/runtime/win-x64/conf/net.properties',
  'ExternalLibs/NoclipViewer/LICENSE',
  'ExternalLibs/NoclipViewer/PROVENANCE.md',
  'ExternalLibs/NoclipViewer/dist-ffxstudio/static/js/461.18e06f8b.js',
  'ExternalLibs/Tools/PhyreMapExportLab/win-x64/PhyreMapExportLab.runtimeconfig.json',
  'ExternalLibs/WindowsPrerequisites/VC2013/vcredist_x64.exe',
  'RuntimeTools/FFXMagicViewerWeb/app.js',
  'RuntimeTools/FFXMapViewerWeb/aurora-overlay.js',
  'RuntimeTools/FFXModelViewerWeb/vendor/three/three.module.js'
)
$bytePreservedAttributes = @(& git -C $repo check-attr text -- $bytePreservedPayloadSamples)
Assert ($LASTEXITCODE -eq 0 -and $bytePreservedAttributes.Count -eq $bytePreservedPayloadSamples.Count -and
  @($bytePreservedAttributes | Where-Object { $_ -match ': text: unset$' }).Count -eq $bytePreservedPayloadSamples.Count) `
  'exact non-LFS runtime payload roots must disable Git text normalization'
$whitespaceExemptPayloadSamples = @(
  'ExternalLibs/FFXED/runtime/win-x64/conf/net.properties',
  'ExternalLibs/NoclipViewer/LICENSE',
  'ExternalLibs/NoclipViewer/PROVENANCE.md',
  'ExternalLibs/NoclipViewer/dist-ffxstudio/static/js/461.18e06f8b.js',
  'ExternalLibs/Tools/PhyreMapExportLab/win-x64/PhyreMapExportLab.runtimeconfig.json',
  'ExternalLibs/WindowsPrerequisites/VC2013/vcredist_x64.exe',
  'RuntimeTools/FFXMagicViewerWeb/vendor/three/LICENSE',
  'RuntimeTools/FFXMapViewerWeb/vendor/spector/spector.bundle.js',
  'RuntimeTools/FFXModelViewerWeb/vendor/three/three.module.js'
)
$whitespaceExemptAttributes = @(& git -C $repo check-attr whitespace -- $whitespaceExemptPayloadSamples)
Assert ($LASTEXITCODE -eq 0 -and $whitespaceExemptAttributes.Count -eq $whitespaceExemptPayloadSamples.Count -and
  @($whitespaceExemptAttributes | Where-Object { $_ -match ': whitespace: unset$' }).Count -eq $whitespaceExemptPayloadSamples.Count) `
  'immutable third-party payloads must exempt reviewed whitespace without changing bytes'
$firstPartyViewerSources = @(
  'RuntimeTools/FFXMagicViewerWeb/app.js',
  'RuntimeTools/FFXMapViewerWeb/app.js',
  'RuntimeTools/FFXMapViewerWeb/aurora-overlay.js',
  'RuntimeTools/FFXMapViewerWeb/field-explorer-overlay.js',
  'RuntimeTools/FFXMapViewerWeb/index.html',
  'RuntimeTools/FFXModelViewerWeb/index.html',
  'RuntimeTools/FFXModelViewerWeb/model-preview.html',
  'RuntimeTools/FFXModelViewerWeb/modelviewer-catalog.json'
)
$firstPartyWhitespaceAttributes = @(& git -C $repo check-attr whitespace -- $firstPartyViewerSources)
$firstPartyWhitespacePolicy = 'blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol'
Assert ($LASTEXITCODE -eq 0 -and $firstPartyWhitespaceAttributes.Count -eq $firstPartyViewerSources.Count -and
  @($firstPartyWhitespaceAttributes | Where-Object { $_ -match ": whitespace: $([regex]::Escape($firstPartyWhitespacePolicy))$" }).Count -eq $firstPartyViewerSources.Count) `
  'first-party viewer sources must preserve CRLF while retaining whitespace error detection'
$webViewAttributes = @(& git -C $repo check-attr filter text whitespace -- 'ExternalLibs/WindowsPrerequisites/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe')
Assert ($LASTEXITCODE -eq 0 -and $webViewAttributes.Count -eq 3 -and
  @($webViewAttributes | Where-Object { $_ -match ': filter: lfs$' }).Count -eq 1 -and
  @($webViewAttributes | Where-Object { $_ -match ': text: unset$' }).Count -eq 1 -and
  @($webViewAttributes | Where-Object { $_ -match ': whitespace: unset$' }).Count -eq 1) `
  'WebView2 installer must use exact-path LFS while preserving pinned bytes and whitespace'

Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

if ($fails.Count -gt 0) { $fails | ForEach-Object { Write-Output ('ASSERT-FAIL: ' + $_) }; Write-Output ("TEST_FAIL assertions={0} failures={1}" -f $runs, $fails.Count); exit 1 }
Write-Output ("TEST_OK assertions={0} (release scripts/contracts self-test)" -f $runs)
exit 0
