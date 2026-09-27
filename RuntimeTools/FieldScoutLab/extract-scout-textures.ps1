# Copy + convert map textures discovered by Field Scout manifest.
param(
    [string]$ReportPath = "work\field_scout\scout-report.json",
    [string]$Ps3Root = "D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data",
    [string]$OutputRoot = "work\field_scout\textures",
    [switch]$ConvertPng,
    [int]$MaxFiles = 0
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path $repo $ReportPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repo $OutputRoot
}

if (-not (Test-Path $ReportPath)) { throw "Report not found: $ReportPath" }
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json

$paths = @($report.assetPaths | Where-Object { $_ -match '/map/.+/tex/' })
if ($MaxFiles -gt 0) { $paths = $paths | Select-Object -First $MaxFiles }

Write-Host "Texture paths to extract: $($paths.Count)"
$copied = 0
$missing = 0
$converted = 0

foreach ($rel in $paths) {
    $norm = ($rel -replace '\\', '/').TrimStart('/')
    if ($norm -notmatch '^(?i)ps3data/') {
        $norm = "ps3data/$norm"
    }
    $src = Join-Path $Ps3Root ($norm -replace '(?i)^ps3data/', '')
    if (-not (Test-Path $src)) {
        $alt = Join-Path $Ps3Root $norm
        if (Test-Path $alt) { $src = $alt } else { ++$missing; continue }
    }

    $destRel = ($norm -replace '(?i)^ps3data/', '') -replace '/', '\'
    $dest = Join-Path $OutputRoot $destRel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest -Force
    ++$copied

    if ($ConvertPng -and $dest -match '\.(dds|DDS)(\.phyre)?$') {
        $texconv = Join-Path $PSScriptRoot "tools\texconv.exe"
        if (-not (Test-Path $texconv)) {
            & (Join-Path $PSScriptRoot "install-field-scout-tools.ps1")
        }
        if (Test-Path $texconv) {
            $outDir = Split-Path $dest -Parent
            & $texconv -y -ft png -o $outDir $dest 2>$null
            if ($LASTEXITCODE -eq 0) { ++$converted }
        }
    }
}

Write-Host "Copied: $copied  Missing: $missing  PNG converted: $converted"
Write-Host "Output: $OutputRoot"
