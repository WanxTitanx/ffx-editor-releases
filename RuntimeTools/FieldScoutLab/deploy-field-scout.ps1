# One-shot deploy Field Scout for a full-game walk.
param(
    [string]$GameRoot = "D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster",
    [switch]$SkipBuild,
    [switch]$Light,
    [switch]$Ultra,
    [switch]$Max,
    [switch]$InstallTools
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hooks = Join-Path $repo "RuntimeTools\FfxHooksDll"

if ($InstallTools) {
    & (Join-Path $PSScriptRoot "install-field-scout-tools.ps1")
}

if (-not $SkipBuild) {
    & (Join-Path $hooks "build_hooks.ps1") -WithPolyHook -Release
}

$dll = Join-Path $hooks "bin\Release\ffx-hooks.dll"
$modules = Join-Path $GameRoot "modules"
$config = Join-Path $modules "config"
$configSrc = Join-Path $hooks "config"

if (-not (Test-Path $GameRoot)) { throw "Game not found: $GameRoot" }
if (Get-Process FFX -ErrorAction SilentlyContinue) { throw "Close FFX before deploy." }

$nativeMenuEnv = [Environment]::GetEnvironmentVariable("FFXHOOKS_ENABLE_NATIVE_MENU", "User")
if (-not $nativeMenuEnv) { $nativeMenuEnv = [Environment]::GetEnvironmentVariable("FFXHOOKS_ENABLE_NATIVE_MENU", "Machine") }
if ($nativeMenuEnv -eq "1") {
    Write-Warning "FFXHOOKS_ENABLE_NATIVE_MENU=1 is set - forces NativeMenu + Aurora D3D Present hook even with zero flags. Can soft-lock title boot with Field Scout. Unset User/Machine env before a walk."
}
$rootFlags = Get-ChildItem -LiteralPath $modules -Filter "*.flag" -ErrorAction SilentlyContinue
if ($rootFlags) {
    Write-Warning ("modules/*.flag still present (Arena+/Aurora/etc.): " + ($rootFlags.Name -join ", "))
}

New-Item -ItemType Directory -Force -Path $config | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $modules "ffx-hooks.dll") -Force
New-Item -ItemType File -Force -Path (Join-Path $config "field_scout.flag") | Out-Null

$heavyFlag = Join-Path $config "field_scout_heavy.flag"
$ultraFlag = Join-Path $config "field_scout_ultra.flag"
$maxFlag = Join-Path $config "field_scout_max.flag"
$ultraFlags = @(
    "field_scout_ultra.field_logic.flag",
    "field_scout_ultra.collision.flag",
    "field_scout_ultra.encounters.flag",
    "field_scout_ultra.scene_env.flag",
    "field_scout_ultra.pipeline.flag"
)

function Set-Flag([string]$Name, [bool]$On) {
    $path = Join-Path $config $Name
    if ($On) { New-Item -ItemType File -Force -Path $path | Out-Null }
    elseif (Test-Path $path) { Remove-Item -LiteralPath $path -Force }
}

if ($Light) {
    Set-Flag "field_scout_heavy.flag" $false
    Set-Flag "field_scout_ultra.flag" $false
    Set-Flag "field_scout_max.flag" $false
    foreach ($f in $ultraFlags) { Set-Flag $f $false }
} else {
    Set-Flag "field_scout_heavy.flag" $true
    $enableUltra = $Ultra -or $Max
    if ($enableUltra) {
        Set-Flag "field_scout_ultra.flag" $true
        foreach ($f in $ultraFlags) { Set-Flag $f $true }
    } else {
        Set-Flag "field_scout_ultra.flag" $false
        foreach ($f in $ultraFlags) { Set-Flag $f $false }
    }
    Set-Flag "field_scout_max.flag" ($Max -and -not $Light)
}

Write-Host ""
if ($Light) {
    Write-Host "Field Scout WORLD MODE deployed (standard)."
} elseif ($Max) {
    Write-Host "Field Scout MAX MODE deployed - RE hooks + all ULTRA categories ON."
    Write-Host "  Requires: field_scout + heavy + ultra + max + category flags"
    Write-Host "  Hooks: obtainTreasure/takara, warpToPoint(warp_actor), zone slot calibration"
    Write-Host "  Aurora: NPC/trigger markers when published to WalkManifest"
} elseif ($Ultra) {
    Write-Host "Field Scout ULTRA HEAVY deployed - all category flags ON."
    Write-Host "  Requires: field_scout + field_scout_heavy + field_scout_ultra + category flags"
    Write-Host "  Dedupe: 2M | categories: field_logic, collision, encounters, scene_env, pipeline"
    Write-Host "  Honest: collision/walkmesh + ATEL warp/takara still RE-pending (stubs in JSONL)"
} else {
    Write-Host "Field Scout HEAVY MODE deployed - aggressive capture enabled."
    Write-Host "  Heavy: 1M dedupe, player trace, polyMeta, encounter zones, scene nodes, chests"
}
Write-Host "  DLL:   $modules\ffx-hooks.dll"
Write-Host "  Flag:  $config\field_scout.flag"
if (-not $Light) { Write-Host "  Heavy: $heavyFlag" }
if ($Ultra -or $Max) {
    Write-Host "  Ultra: $ultraFlag"
    foreach ($f in $ultraFlags) { Write-Host "         $config\$f" }
}
if ($Max) { Write-Host "  Max:   $maxFlag" }
Write-Host ""
Write-Host "Play FFX - walk the whole game - close the game."
Write-Host "Then run:"
Write-Host "  RuntimeTools\FieldScoutLab\process-scout-session.ps1 -ExportMaps -ExtractTextures -ConvertTextures -PublishToEditor"
Write-Host ""
Write-Host "Lab output stays in work/ (gitignored). -PublishToEditor copies into AuroraFieldExplorer/WalkManifest for release."
Write-Host ""
