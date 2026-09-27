# Field Scout - one-shot prep: build, quarantine conflicts, deploy ULTRA, ready to play.
# Usage:
#   .\prepare-field-scout-walk.ps1
#   .\prepare-field-scout-walk.ps1 -GameRoot "D:\...\FINAL FANTASY FFX&FFX-2 HD Remaster"

param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster",
    [switch]$SkipBuild,
    [switch]$KeepConflictingFlags
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$labRoot = Join-Path $repo "work\field_scout"
New-Item -ItemType Directory -Force -Path $labRoot | Out-Null

if (-not (Test-Path $GameRoot)) {
    throw "Jogo nao encontrado: $GameRoot`nPasse -GameRoot com o caminho do FFX HD Remaster."
}
if (Get-Process FFX -ErrorAction SilentlyContinue) {
    throw "Feche o FFX antes do deploy (processo FFX ainda rodando)."
}

$modules = Join-Path $GameRoot "modules"
$config = Join-Path $modules "config"
$scoutDir = Join-Path $modules "field-scout"
New-Item -ItemType Directory -Force -Path $scoutDir | Out-Null

@{
    gameRoot = $GameRoot
    gameModules = $modules
    preparedAt = (Get-Date).ToString("o")
    mode = "ultra"
    regionHint = "macalania (macl*)"
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $labRoot "walk-config.json") -Encoding UTF8

Write-Host ""
Write-Host "=== Field Scout walk prep (Jarvis-FIELD-RE) ===" -ForegroundColor Cyan
Write-Host "Repo:  $repo"
Write-Host "Game:  $GameRoot"
Write-Host ""

foreach ($scope in @("User", "Machine")) {
    $nm = [Environment]::GetEnvironmentVariable("FFXHOOKS_ENABLE_NATIVE_MENU", $scope)
    if ($nm -eq "1") {
        Write-Warning "FFXHOOKS_ENABLE_NATIVE_MENU=1 ($scope) - removendo para este walk."
        [Environment]::SetEnvironmentVariable("FFXHOOKS_ENABLE_NATIVE_MENU", $null, $scope)
    }
}

if (-not $KeepConflictingFlags) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $quarantine = Join-Path $labRoot "quarantine-$stamp"
    New-Item -ItemType Directory -Force -Path $quarantine | Out-Null
    $moved = @()

    foreach ($f in Get-ChildItem -LiteralPath $modules -Filter "*.flag" -ErrorAction SilentlyContinue) {
        Move-Item -LiteralPath $f.FullName -Destination (Join-Path $quarantine $f.Name) -Force
        $moved += "modules/$($f.Name)"
    }
    if (Test-Path $config) {
        foreach ($f in Get-ChildItem -LiteralPath $config -Filter "*.flag" -ErrorAction SilentlyContinue) {
            if ($f.Name -like "field_scout*") { continue }
            Move-Item -LiteralPath $f.FullName -Destination (Join-Path $quarantine $f.Name) -Force
            $moved += "config/$($f.Name)"
        }
    }

    if ($moved.Count -gt 0) {
        $moved | Set-Content -LiteralPath (Join-Path $quarantine "manifest.txt") -Encoding UTF8
        Write-Host "Flags conflitantes movidas para:" -ForegroundColor Yellow
        Write-Host "  $quarantine"
        Write-Host "  $($moved.Count) flags movidas"
    } else {
        Remove-Item -LiteralPath $quarantine -Force -ErrorAction SilentlyContinue
        Write-Host "Nenhuma flag conflitante em modules/ (OK)."
    }
}

$deployArgs = @{ Ultra = $true; GameRoot = $GameRoot }
if ($SkipBuild) { $deployArgs.SkipBuild = $true }
& (Join-Path $PSScriptRoot "deploy-field-scout.ps1") @deployArgs

$ready = @(
    "Field Scout ULTRA pronto - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ""
    "1. Abra o FFX (Steam)."
    "2. Va a Macalania e pisque TODAS as telas (macl*)."
    "3. Batalha random OK - Scout pausa sozinho; fuja e continue."
    "4. Feche o jogo ao terminar (grava session JSONL)."
    ""
    "Depois:"
    "  RuntimeTools\FieldScoutLab\finish-field-scout-walk.ps1"
    "  - ou no editor: Aurora Field Explorer > Publicar Scout"
    ""
    "Session: $scoutDir"
) -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $scoutDir "WALK-READY.txt") -Value $ready -Encoding UTF8

Write-Host ""
Write-Host "=== PRONTO - abra o jogo ===" -ForegroundColor Green
Write-Host $ready
Write-Host ""
