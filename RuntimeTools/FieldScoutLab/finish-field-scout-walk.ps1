# Field Scout — after walk: ingest latest session + publish to editor + refresh overlays.
# Usage:
#   .\finish-field-scout-walk.ps1
#   .\finish-field-scout-walk.ps1 -PublishToEditor

param(
    [switch]$PublishToEditor = $true,
    [switch]$ExportMaps
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$configPath = Join-Path $repo "work\field_scout\walk-config.json"

$gameModules = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\modules"
if (Test-Path $configPath) {
    $cfg = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($cfg.gameModules) { $gameModules = [string]$cfg.gameModules }
}

Write-Host ""
Write-Host "=== Field Scout finish (ingest + publish) ===" -ForegroundColor Cyan

$ingestArgs = @{
    GameModules = $gameModules
    OutputRoot = (Join-Path $repo "work\field_scout")
}
if ($PublishToEditor) { $ingestArgs.PublishToEditor = $true }
if ($ExportMaps) { $ingestArgs.ExportMaps = $true }

& (Join-Path $PSScriptRoot "process-scout-session.ps1") @ingestArgs

if ($PublishToEditor) {
    $csproj = Join-Path $repo "FFXProjectEditor\FFXProjectEditor.csproj"
    if (Test-Path $csproj) {
        Write-Host ""
        Write-Host "Refresh overlays (in-process)..."
        dotnet run --project $csproj -c Debug --no-launch-profile -- --field-explorer-refresh-walk 2>&1 | ForEach-Object { Write-Host $_ }
    }
}

Write-Host ""
Write-Host "=== Done - Aurora Field Explorer > filtre macl > Publicar + abrir field ===" -ForegroundColor Green
Write-Host ""
