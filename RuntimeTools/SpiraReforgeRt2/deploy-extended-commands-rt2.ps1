# Deploy Spira Reforge extended commands RT2 (kernel + GridTeach v4 + full sidecar)
param(
    [string]$SteamPath = 'D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$LaunchGame
)

$ErrorActionPreference = 'Stop'

$kernelDst = Join-Path $SteamPath 'data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin'
$packSrc = Join-Path $RepoRoot 'work\spira_reforge_extended_pack\command.bin'
$hooksSrc = Join-Path $RepoRoot 'RuntimeTools\FfxHooksDll\bin\Release\ffx-hooks.dll'
$hooksDst = Join-Path $SteamPath 'modules\ffx-hooks.dll'
$configDir = Join-Path $SteamPath 'modules\config'

if (-not (Test-Path $packSrc)) { throw "Missing pack: $packSrc (run extended-pack-rt0 first)" }
if (-not (Test-Path $hooksSrc)) { throw "Missing DLL: $hooksSrc (run build_hooks.ps1 -WithPolyHook -Release)" }

if (Test-Path $kernelDst) {
    $bak = "$kernelDst.pre-extended-rt2-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Copy-Item $kernelDst $bak -Force
    Write-Host "Backed up kernel -> $bak"
}

Copy-Item $packSrc $kernelDst -Force
Write-Host "Deployed command.bin ($((Get-Item $packSrc).Length) bytes) -> $kernelDst"

Copy-Item $hooksSrc $hooksDst -Force
Write-Host "Deployed ffx-hooks.dll -> $hooksDst"

& (Join-Path $PSScriptRoot 'set-grid-teach-sidecar.ps1') -SteamPath $SteamPath -Pack Full

New-Item -ItemType Directory -Force -Path $configDir | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $configDir 'grid_teach.flag') | Out-Null
Write-Host "grid_teach.flag ON"

Write-Host ""
Write-Host "=== RT2 READY ==="
Write-Host "1) Enter ANY battle (save descartavel OK)"
Write-Host "2) Open battle menu per char idx:"
Write-Host "   Tidus=0 Yuna=1 Auron=2 Kimahri=3 Wakka=4 Lulu=5 Rikku=6"
Write-Host "3) Kimahri: Blue Magic submenu (id #322 menu opener)"
Write-Host "4) Log: $env:TEMP\ffx-hooks.log (GridTeach v4)"
Write-Host "5) Revert: restore command.bin backup + delete grid_teach.flag"

if ($LaunchGame) {
    Write-Host "Launching FFX via Steam..."
    Start-Process 'steam://run/359870'
}
