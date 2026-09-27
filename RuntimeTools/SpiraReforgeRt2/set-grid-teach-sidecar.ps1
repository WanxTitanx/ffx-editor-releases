# Spira Reforge RT2 — write grid_teach_learned.bin (party bank ids >= 96)
# Requires: grid_teach.flag + ffx-hooks.dll GridTeach v4
# Lane: Jarvis-MAGIC

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamPath,

    [ValidateSet('Kimahri', 'Lulu', 'Yuna', 'Wakka', 'Rikku', 'Tidus', 'Auron', 'Full', 'Smoke')]
    [string]$Pack = 'Smoke',

    [int[]]$CommandIds,

    [switch]$CopycatRikku,

    [switch]$InvokeCopycatGrant
)

$ErrorActionPreference = 'Stop'

$PackIds = @{
    Kimahri = 322..336
    Lulu    = @(337..342) + @(348)   # Biora lives at stable id #348
    Yuna    = @(343..347) + @(366)   # White Magic+ menu #366 + *ga children
    Wakka   = @(350, 352)            # Quad Foul + Jinx Ball (#351 Twin Reel UNUSED)
    Rikku   = 353..355
    Tidus   = @(358)                   # Bladestorm only; #356–357/#359 deactivated
    Auron   = 360..365
    Full    = 322..366 | Where-Object { $_ -notin 320, 321, 349, 351, 356, 357, 359 }
    Smoke   = @(322, 333, 327, 348)  # Blue Magic menu + Mighty Guard + Thrust Kick + Biora
}

function Set-PartyCommandBits {
    param([int[]]$Ids, [string]$OutPath)

    $words = New-Object uint16[] 18
    foreach ($id in $Ids) {
        if ($id -lt 96) {
            throw "Sidecar GridTeach is for ids >= 96; id $id is per-char (ex. Copycat #40 via probe)"
        }
        $rel = $id - 96
        $w = [math]::Floor($rel / 16)
        $bit = 1 -shl ($rel % 16)
        if ($w -ge 18) {
            throw "id $id exceeds sidecar capacity (max id 383)"
        }
        $words[$w] = [uint16]($words[$w] -bor $bit)
    }
    $bytes = New-Object byte[] 36
    [Buffer]::BlockCopy($words, 0, $bytes, 0, 36)
    $dir = Split-Path -Parent $OutPath
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [IO.File]::WriteAllBytes($OutPath, $bytes)
    Write-Host "Wrote $OutPath ($($bytes.Length) bytes) for ids: $($Ids -join ', ')"
}

$ids = if ($CommandIds) { $CommandIds } else { $PackIds[$Pack] }
$sidecar = Join-Path $SteamPath 'modules\config\grid_teach_learned.bin'
$flag = Join-Path $SteamPath 'modules\config\grid_teach.flag'

Set-PartyCommandBits -Ids $ids -OutPath $sidecar

if (-not (Test-Path $flag)) {
    New-Item -ItemType File -Force -Path $flag | Out-Null
    Write-Host "Created $flag"
} else {
    Write-Host "Flag present: $flag"
}

if ($CopycatRikku -or $InvokeCopycatGrant) {
    $probe = Join-Path $SteamPath 'modules\ffxprobectl.exe'
    $cmd = 'ffxprobectl call 385D10 6 40 1'
    Write-Host "Copycat #40 (Rikku idx 6, per-char bank): $cmd"
    if ($InvokeCopycatGrant) {
        if (Test-Path $probe) {
            & $probe call 385D10 6 40 1
        } else {
            Push-Location (Join-Path $SteamPath 'modules')
            try { Invoke-Expression $cmd } finally { Pop-Location }
        }
    }
}

Write-Host "RT2: restart FFX if running, enter battle, open menu for char idx (Kimahri=3, Lulu=5, ...)"
