# Spira Reforge — isolate GridTeach for post-battle stat reset RT2
# Lane: Jarvis-MAGIC. Usage:
#   .\isolation-gridteach-stats.ps1 -SteamPath 'D:\...\FINAL FANTASY FFX&FFX-2 HD Remaster' -Action Disable
#   .\isolation-gridteach-stats.ps1 -SteamPath '...' -Action Restore
#   .\isolation-gridteach-stats.ps1 -SteamPath '...' -Action CheckLog

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamPath,

    [ValidateSet('Disable', 'Restore', 'CheckLog')]
    [string]$Action = 'Disable'
)

$ErrorActionPreference = 'Stop'
$config = Join-Path $SteamPath 'modules\config'
$sidecar = Join-Path $config 'grid_teach_learned.bin'
$sidecarBak = Join-Path $config 'grid_teach_learned.bin.bak'
$flag = Join-Path $config 'grid_teach.flag'
$flagBak = Join-Path $config 'grid_teach.flag.bak-isolation'

function Disable-GridTeachIsolation {
    if (Test-Path $sidecar) {
        if (Test-Path $sidecarBak) { Remove-Item $sidecarBak -Force }
        Rename-Item $sidecar $sidecarBak
        Write-Host "sidecar -> grid_teach_learned.bin.bak"
    }
    if (Test-Path $flag) {
        if (Test-Path $flagBak) { Remove-Item $flagBak -Force }
        Rename-Item $flag $flagBak
        Write-Host "grid_teach.flag -> grid_teach.flag.bak-isolation"
    }
    $u = [Environment]::GetEnvironmentVariable('FFXHOOKS_GRID_TEACH', 'User')
    if ($u) {
        [Environment]::SetEnvironmentVariable('FFXHOOKS_GRID_TEACH', $null, 'User')
        Write-Host "removed FFXHOOKS_GRID_TEACH (User)"
    }
    @(
        "disabled_utc=$(Get-Date -Format o)",
        "sidecar_bak=$sidecarBak",
        "flag_bak=$flagBak"
    ) | Set-Content (Join-Path $config 'isolation_test_marker.txt')
    Write-Host "OK: GridTeach OFF on next FFX launch. Restart game fully."
}

function Restore-GridTeachIsolation {
    if (Test-Path $sidecarBak) {
        if (Test-Path $sidecar) { Remove-Item $sidecar -Force }
        Rename-Item $sidecarBak $sidecar
        Write-Host "restored grid_teach_learned.bin"
    }
    if (Test-Path $flagBak) {
        if (Test-Path $flag) { Remove-Item $flag -Force }
        Rename-Item $flagBak $flag
        Write-Host "restored grid_teach.flag"
    }
    Write-Host "OK: GridTeach lab restored. Restart FFX."
}

function Check-IsolationLog {
    $log = Join-Path $env:TEMP 'ffx-hooks.log'
    if (-not (Test-Path $log)) { Write-Host "WARN: no $log"; return }
    $tail = Get-Content $log -Tail 80
    $gt = $tail | Select-String 'GridTeach'
    if ($gt) {
        Write-Host "GridTeach lines in last 80 log lines (should be EMPTY if isolation OK):"
        $gt | ForEach-Object { Write-Host $_.Line }
    } else {
        Write-Host "OK: no GridTeach in last 80 log lines"
    }
    $prep = $tail | Select-String 'PrepSave|actor shadow|BuildMenu'
    if ($prep) {
        Write-Host "--- other hook lines ---"
        $prep | ForEach-Object { Write-Host $_.Line }
    }
}

switch ($Action) {
    'Disable'  { Disable-GridTeachIsolation }
    'Restore'  { Restore-GridTeachIsolation }
    'CheckLog' { Check-IsolationLog }
}
