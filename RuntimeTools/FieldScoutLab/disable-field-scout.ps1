# Disable Field Scout hooks in the game (remove field_scout*.flag only).
param(
    [string]$GameModules = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$labRoot = Join-Path $repo "work\field_scout"

if (-not $GameModules) {
    $cfgPath = Join-Path $labRoot "walk-config.json"
    if (Test-Path $cfgPath) {
        $cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($cfg.gameModules) { $GameModules = [string]$cfg.gameModules }
    }
}
if (-not $GameModules) {
    $GameModules = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\modules"
}

if (Get-Process FFX -ErrorAction SilentlyContinue) {
    Write-Warning "FFX ainda aberto - feche o jogo para garantir que as flags nao recarregam na sessao atual."
}

$config = Join-Path $GameModules "config"
$stash = Join-Path $labRoot "scout-flags-disabled-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Force -Path $stash | Out-Null

$removed = @()
foreach ($root in @($GameModules, $config)) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -LiteralPath $root -Filter "field_scout*.flag" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '\.off-' } |
        ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination (Join-Path $stash $_.Name) -Force
            $removed += $_.FullName
        }
}

Write-Host ""
if ($removed.Count -eq 0) {
    Write-Host "Nenhuma flag Field Scout ativa encontrada." -ForegroundColor Yellow
} else {
    Write-Host "Field Scout DESLIGADO - $($removed.Count) flag(s) movida(s) para:" -ForegroundColor Green
    Write-Host "  $stash"
    foreach ($r in $removed) { Write-Host "  - $r" }
}
Write-Host "Arena+/SG/music/native_menu intactos." -ForegroundColor DarkGray
Write-Host ""
