# Forge confirm/menu input during title boot (probe SETINPUT only — no ffx-hooks).
#
#   .\auto_title.ps1 [-DurationSec 90] [-ConfirmIntervalMs 800] [-AlsoMenu]
param(
    [int]$DurationSec = 90,
    [int]$ConfirmIntervalMs = 800,
    [switch]$AlsoMenu
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ctl = Join-Path $repo "RuntimeTools\FfxDinput8Probe\ctl\bin\ffxprobectl.exe"
if (-not (Test-Path $ctl)) {
    & (Join-Path $repo "RuntimeTools\FfxDinput8Probe\build.ps1") | Out-Null
}
if (-not (Test-Path $ctl)) { throw "ffxprobectl not found" }

# FFXIN_* bits (Program.cs setinput): confirm=0x10 cancel=0x20 menu=0x40
$MaskConfirm = "0x10"
$MaskMenu    = "0x50"  # confirm + menu

function Invoke-SetInput([string]$Mask) {
    $out = & $ctl setinput $Mask 0 0 2>&1 | Out-String
    if ($out -notmatch "status=1") {
        if ($out -match "nao encontrado|not found") { throw "MMF missing — FFX + ffx-probe required" }
        Write-Warning "setinput $Mask -> $out"
        return $false
    }
    return $true
}

Write-Host "auto_title: ${DurationSec}s confirm every ${ConfirmIntervalMs}ms (AlsoMenu=$AlsoMenu)"
Write-Host "Start FFX at title screen, then run this script."

$deadline = (Get-Date).AddSeconds($DurationSec)
$pulses = 0
while ((Get-Date) -lt $deadline) {
    $mask = if ($AlsoMenu) { $MaskMenu } else { $MaskConfirm }
    if (Invoke-SetInput $mask) { $pulses++ }
    Start-Sleep -Milliseconds $ConfirmIntervalMs
}

& $ctl setinput 0 0 0 | Out-Null
Write-Host "Released forged input. Pulses=$pulses"
if ($pulses -eq 0) { exit 2 }
exit 0
