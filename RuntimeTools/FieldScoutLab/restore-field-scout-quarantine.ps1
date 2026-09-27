# Restore hook flags moved by prepare-field-scout-walk.ps1 quarantine.
param(
    [string]$QuarantineDir = "",
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

if (-not $QuarantineDir) {
    $latest = Get-ChildItem -LiteralPath $labRoot -Directory -Filter "quarantine-*" |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $latest) { throw "Nenhuma pasta quarantine-* em $labRoot" }
    $QuarantineDir = $latest.FullName
}

if (-not (Test-Path $QuarantineDir)) { throw "Quarantine nao encontrada: $QuarantineDir" }
if (-not (Test-Path $GameModules)) { throw "Game modules nao encontrado: $GameModules" }

$config = Join-Path $GameModules "config"
New-Item -ItemType Directory -Force -Path $config | Out-Null

$manifestPath = Join-Path $QuarantineDir "manifest.txt"
$restored = @()

if (Test-Path $manifestPath) {
    foreach ($line in Get-Content -LiteralPath $manifestPath -Encoding UTF8) {
        $entry = $line.Trim()
        if (-not $entry) { continue }
        if ($entry -match '^modules/(.+)$') {
            $name = $matches[1]
            $src = Join-Path $QuarantineDir $name
            $dest = Join-Path $GameModules $name
        } elseif ($entry -match '^config/(.+)$') {
            $name = $matches[1]
            $src = Join-Path $QuarantineDir $name
            $dest = Join-Path $config $name
        } else {
            Write-Warning "Ignorando entrada manifest desconhecida: $entry"
            continue
        }
        if (-not (Test-Path $src)) {
            Write-Warning "Arquivo ausente na quarentena: $name"
            continue
        }
        Move-Item -LiteralPath $src -Destination $dest -Force
        $restored += $entry
    }
} else {
    foreach ($f in Get-ChildItem -LiteralPath $QuarantineDir -Filter "*.flag") {
        if ($f.Name -like "field_scout*") { continue }
        # Heuristica: sphere grid / arena root flags costumavam ficar em modules/
        if ($f.Name -like "sg_*") {
            $dest = Join-Path $GameModules $f.Name
        } else {
            $dest = Join-Path $config $f.Name
        }
        Move-Item -LiteralPath $f.FullName -Destination $dest -Force
        $restored += $f.Name
    }
}

Write-Host ""
Write-Host "Restauradas $($restored.Count) flag(s) de:" -ForegroundColor Green
Write-Host "  $QuarantineDir"
Write-Host "  -> $GameModules"
foreach ($r in $restored) { Write-Host "  + $r" }
Write-Host ""

$left = Get-ChildItem -LiteralPath $QuarantineDir -Filter "*.flag" -ErrorAction SilentlyContinue
if (-not $left -or $left.Count -eq 0) {
    Remove-Item -LiteralPath $QuarantineDir -Force -ErrorAction SilentlyContinue
    Write-Host "Quarentena removida (vazia)." -ForegroundColor DarkGray
}
