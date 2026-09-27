param(
    [Parameter(Mandatory = $true)]
    [string]$MasterPath,

    [string]$ReportPath,

    [string]$JsonPath
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptDir "TextRegressionHarness\TextRegressionHarness.csproj"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $scriptDir "Reports\text-regression-last.md"
}

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $JsonPath = Join-Path $scriptDir "Reports\text-regression-last.json"
}

$reportDirectory = Split-Path -Parent $ReportPath
$jsonDirectory = Split-Path -Parent $JsonPath

if ($reportDirectory) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

if ($jsonDirectory) {
    New-Item -ItemType Directory -Force -Path $jsonDirectory | Out-Null
}

& dotnet run --project $projectPath -- --master $MasterPath --report $ReportPath --json $JsonPath
exit $LASTEXITCODE
