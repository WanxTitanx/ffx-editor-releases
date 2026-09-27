# Boot FSM trace via ffx-probe READ (no ffx-hooks). Lane: Jarvis-MAGIC BootSkipLab.
# Prereq: FFX running with ffx-probe.dll; game at boot/title (launch before or during trace).
#
#   .\boot_trace.ps1 [-DurationSec 120] [-IntervalMs 250] [-OutFile trace.csv]
param(
    [int]$DurationSec = 120,
    [int]$IntervalMs = 250,
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ctl = Join-Path $repo "RuntimeTools\FfxDinput8Probe\ctl\bin\ffxprobectl.exe"
if (-not (Test-Path $ctl)) {
    Write-Host "Building ffxprobectl..."
    & (Join-Path $repo "RuntimeTools\FfxDinput8Probe\build.ps1") | Out-Null
}
if (-not (Test-Path $ctl)) { throw "ffxprobectl not found at $ctl" }

# RVAs (image base 0x400000) — match RuntimeTools/FfxHooksDll/shared/ffx_addresses.h
$Rva = @{
    SceneState   = "0xD2CA90"   # g_FFX_SceneStateObject; sceneId @ +0
    MenuScreen   = "0xEFBBF0"   # g_FFX_CurrentMenuScreenId
    MenuActive   = "0xF407E4"   # g_FFX_MenuSubsystemActive
    Controlled   = "0xF00740"   # g_FFX_ControlledChrInstance (ptr)
    ScenePending = "0xF3080C"   # dword_133080C transition pending
}

function Invoke-ProbeRead([string]$RvaHex, [int]$Len = 4) {
    $out = & $ctl read $RvaHex $Len 2>&1 | Out-String
    if ($out -match "status=(\d+)") {
        $st = [int]$Matches[1]
        if ($st -ne 1) { return $null }
    } else {
        if ($out -match "nao encontrado|not found") { throw "MMF missing — is FFX running with ffx-probe.dll?" }
        return $null
    }
    if ($out -match "(?m)^\s+((?:[0-9A-Fa-f]{2}\s+)+)") {
        $bytes = $Matches[1].Trim().Split(" ", [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { [Convert]::ToByte($_, 16) }
        return ,@($bytes)
    }
    return $null
}

function Read-U32([byte[]]$Bytes, [int]$Off = 0) {
    if ($null -eq $Bytes -or ($Bytes.Length - $Off) -lt 4) { return $null }
    return [uint32]($Bytes[$Off] -bor ($Bytes[$Off+1] -shl 8) -bor ($Bytes[$Off+2] -shl 16) -bor ($Bytes[$Off+3] -shl 24))
}

function Get-BootPhase([uint32]$SceneId, [uint32]$MenuActive, [uint32]$Inst, [uint32]$Pending) {
    if ($Inst -ne 0) { return "InField" }
    if ($MenuActive -ne 0) { return "MenuActive" }
    if ($Pending -ne 0) { return "SceneTransition" }
    if ($SceneId -eq 23) { return "IntroScene" }
    return "BootOrTitle"
}

if (-not $OutFile) {
    $OutFile = Join-Path $PSScriptRoot ("boot_trace_{0:yyyyMMdd_HHmmss}.csv" -f (Get-Date))
}

$rows = @()
$deadline = (Get-Date).AddSeconds($DurationSec)
Write-Host "Boot trace $DurationSec s @ ${IntervalMs}ms -> $OutFile"
Write-Host "Launch FFX now if not already at boot/title."

while ((Get-Date) -lt $deadline) {
    $ts = Get-Date -Format "o"
    try {
        $sceneB = Invoke-ProbeRead $Rva.SceneState 16
        $menuSc = Read-U32 (Invoke-ProbeRead $Rva.MenuScreen 4)
        $menuAc = Read-U32 (Invoke-ProbeRead $Rva.MenuActive 4)
        $inst   = Read-U32 (Invoke-ProbeRead $Rva.Controlled 4)
        $pend   = Read-U32 (Invoke-ProbeRead $Rva.ScenePending 4)
        $scene  = if ($sceneB) { Read-U32 $sceneB 0 } else { $null }
        $mapTok = if ($sceneB -and $sceneB.Length -ge 8) { Read-U32 $sceneB 4 } else { $null }
        $phase  = if ($null -ne $scene -and $null -ne $menuAc -and $null -ne $inst) {
            Get-BootPhase $scene $menuAc $inst $pend
        } else { "NoProbe" }

        $line = [pscustomobject]@{
            ts = $ts
            phase = $phase
            sceneId = if ($null -ne $scene) { "0x{0:X}" -f $scene } else { "" }
            mapToken = if ($null -ne $mapTok) { "0x{0:X}" -f $mapTok } else { "" }
            menuScreen = if ($null -ne $menuSc) { "0x{0:X}" -f $menuSc } else { "" }
            menuActive = $menuAc
            controlledInst = if ($null -ne $inst) { "0x{0:X8}" -f $inst } else { "" }
            scenePending = $pend
        }
        $rows += $line
        Write-Host ("{0} {1} scene={2} menu={3} inst={4}" -f $ts.Substring(11,8), $phase, $line.sceneId, $menuAc, $line.controlledInst)
    } catch {
        Write-Warning $_.Exception.Message
        break
    }
    Start-Sleep -Milliseconds $IntervalMs
}

$rows | Export-Csv -Path $OutFile -NoTypeInformation -Encoding UTF8
Write-Host "Wrote $($rows.Count) samples -> $OutFile"
if ($rows.Count -lt 2) {
    Write-Warning "RT-BS-01: fewer than 2 samples — start FFX with probe before running trace."
    exit 2
}
exit 0
