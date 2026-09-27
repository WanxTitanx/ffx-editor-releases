param(
    [string]$MasterPath = "D:\FFX Extracted\FFX\ffx_ps2\ffx\master",
    [string]$ReportPath,
    [string]$JsonPath,
    [string]$SaveSafetyReportPath,
    [string]$SaveSafetyJsonPath
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$harnessScript = Join-Path $repoRoot "TextLabTools\Run-TextRegressionHarness.ps1"
$smokeProject = Join-Path $scriptDir "ProductionSafetySmoke\ProductionSafetySmoke.csproj"
$smokeOutputDir = Join-Path $scriptDir "ProductionSafetySmoke\bin\Release\net8.0"
$smokeExe = Join-Path $smokeOutputDir "ProductionSafetySmoke.exe"
$smokeDll = Join-Path $smokeOutputDir "ProductionSafetySmoke.dll"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $scriptDir "Reports\production-safety-smoke-text-regression.md"
}

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $JsonPath = Join-Path $scriptDir "Reports\production-safety-smoke-text-regression.json"
}

if ([string]::IsNullOrWhiteSpace($SaveSafetyReportPath)) {
    $SaveSafetyReportPath = Join-Path $scriptDir "Reports\production-safety-smoke-save-safety.md"
}

if ([string]::IsNullOrWhiteSpace($SaveSafetyJsonPath)) {
    $SaveSafetyJsonPath = Join-Path $scriptDir "Reports\production-safety-smoke-save-safety.json"
}

$reportDirectory = Split-Path -Parent $ReportPath
$jsonDirectory = Split-Path -Parent $JsonPath
$saveSafetyReportDirectory = Split-Path -Parent $SaveSafetyReportPath
$saveSafetyJsonDirectory = Split-Path -Parent $SaveSafetyJsonPath

if ($reportDirectory) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

if ($jsonDirectory) {
    New-Item -ItemType Directory -Force -Path $jsonDirectory | Out-Null
}

if ($saveSafetyReportDirectory) {
    New-Item -ItemType Directory -Force -Path $saveSafetyReportDirectory | Out-Null
}

if ($saveSafetyJsonDirectory) {
    New-Item -ItemType Directory -Force -Path $saveSafetyJsonDirectory | Out-Null
}

if (-not (Test-Path $harnessScript)) {
    throw "Harness script not found: $harnessScript"
}

if (-not (Test-Path $smokeProject)) {
    throw "Smoke project not found: $smokeProject"
}

Write-Host "== Production Safety Smoke =="
Write-Host "MasterPath: $MasterPath"
Write-Host "Project:    $smokeProject"

& $harnessScript -MasterPath $MasterPath -ReportPath $ReportPath -JsonPath $JsonPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet build $smokeProject -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path $smokeExe) {
    & $smokeExe --master $MasterPath --report $SaveSafetyReportPath --json $SaveSafetyJsonPath
    exit $LASTEXITCODE
}

if (Test-Path $smokeDll) {
    & dotnet $smokeDll --master $MasterPath --report $SaveSafetyReportPath --json $SaveSafetyJsonPath
    exit $LASTEXITCODE
}

throw "Smoke output not found after build. Expected '$smokeExe' or '$smokeDll'."
