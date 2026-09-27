<#
.SYNOPSIS
  Ferramenta RAM-first dos atores de batalha do FFX (party + monstros). Dois modos:

    -Mode Capture : congela e exporta as coordenadas VIVAS de todos os atores + camera
                    (JSON/CSV pronto pro fluxo OFFLINE+ONLINE / Aurora).
    -Mode Move    : MOVE um ator ao vivo (escreve posicao na RAM) pra corrigir na mao
                    o que ficou errado no Aurora offline. Ex.: reposicionar Dark Aeons.

  Sem DLL. Read-only no Capture (suspende so com -Freeze). Cultura invariant.

  Struct de ator (RE reusada do ffx-hooks/IDA; offsets relativos ao modulo FFX.exe):
    +0x01FC44E0 count(u32) | +0x01FC44E4 table ptr(u32) | stride 0x880
    inst+0x000 id(u16) | +0x002 active(u8) | +0x00C/10/14 worldPos XYZ(float)
    inst+0x194 flags(u32; bit 0x80 reserve) | +0x1C0 skel | +0x1D0 world matrix 4x4
    matrix translation (= pos renderizada) = inst+0x1D0+0x30 (= inst+0x200)
    camera ref viva: modulo +0xD378A0 (X/Y/Z)

  NOTA (provado 2026-06-09): a engine reescreve a posicao do ator POR-FRAME. Escrever
  +0xC e a matrix-tx MOVE o modelo na tela, mas brigando com o writer da engine vira
  jitter. -HoldMs escreve em loop apertado pra dominar; o porte in-DLL deve ficar liso
  quando escrever a override no tick do frame, depois do update do ator.
#>
param(
    [ValidateSet('Capture', 'Move')]
    [string]$Mode = 'Capture',

    [Alias('Pid')]
    [int]$GamePid = 0,
    [string]$ProcessName = 'FFX',

    # --- Capture ---
    [switch]$Freeze,
    [switch]$WithMatrix,
    [switch]$IncludeOther,
    [switch]$IncludeUndeployed,

    # --- Move --- (alvo por indice OU por id hex; posicao absoluta OU delta)
    [int]$TargetIdx = -1,
    [string]$TargetId = '',
    [single]$X = 0, [single]$Y = 0, [single]$Z = 0,
    [single]$Dx = 0, [single]$Dy = 0, [single]$Dz = 0,
    [int]$HoldMs = 4000,
    [int]$IntervalMs = 4,
    [switch]$Capture,
    [switch]$NoRestore,

    [string]$OutDir = 'work\actor_ram',
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

$RVA_COUNT = 0x01FC44E0
$RVA_TABLE = 0x01FC44E4
$RVA_REF   = 0x00D378A0
$RVA_HEAD  = 0x00D378B0
$RVA_ELEV  = 0x00D378B4
$STRIDE    = 0x880
$MAX_COUNT = 4096
$OFF_MTX_TX = 0x1D0 + 0x30   # translacao da world matrix (inst+0x200)

if (-not ('BattleActorRamNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BattleActorRamNative {
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool i, int pid);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr r);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr w);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool VirtualProtectEx(IntPtr h, IntPtr a, UIntPtr s, uint np, out uint op);
  [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr h);
  [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr h);
}
'@
}

function Get-TargetProcess {
    if ($GamePid -gt 0) { return Get-Process -Id $GamePid -ErrorAction Stop }
    return Get-Process -Name $ProcessName -ErrorAction Stop | Sort-Object StartTime -Descending | Select-Object -First 1
}
function Format-Float { param([single]$v) return $v.ToString('R', $Invariant) }

$proc = Get-TargetProcess
$base = [uint64]$proc.MainModule.BaseAddress.ToInt64()

$PROCESS_QUERY_INFORMATION = 0x0400
$PROCESS_VM_READ = 0x0010
$PROCESS_VM_WRITE = 0x0020
$PROCESS_VM_OPERATION = 0x0008
$PROCESS_SUSPEND_RESUME = 0x0800
$access = $PROCESS_QUERY_INFORMATION -bor $PROCESS_VM_READ
if ($Mode -eq 'Move') { $access = $access -bor $PROCESS_VM_WRITE -bor $PROCESS_VM_OPERATION }
if ($Freeze) { $access = $access -bor $PROCESS_SUSPEND_RESUME }
$h = [BattleActorRamNative]::OpenProcess([uint32]$access, $false, $proc.Id)
if ($h -eq [IntPtr]::Zero) { throw "OpenProcess falhou pra PID $($proc.Id): 0x$([Runtime.InteropServices.Marshal]::GetLastWin32Error().ToString('X8'))" }

function Read-Block { param([uint64]$Addr, [int]$Size)
    $buf = New-Object byte[] $Size; $r = [IntPtr]::Zero
    if (-not [BattleActorRamNative]::ReadProcessMemory($h, [IntPtr][int64]$Addr, $buf, $Size, [ref]$r) -or $r.ToInt64() -ne $Size) { return $null }
    return $buf
}
function Read-U32 { param([uint64]$a) $b = Read-Block $a 4; if ($null -eq $b) { return $null }; return [BitConverter]::ToUInt32($b, 0) }
function Read-F { param([uint64]$a) $b = Read-Block $a 4; if ($null -eq $b) { return [single]::NaN }; return [BitConverter]::ToSingle($b, 0) }
function Write-F { param([uint64]$a, [single]$v)
    $b = [BitConverter]::GetBytes([single]$v); $w = [IntPtr]::Zero
    [void][BattleActorRamNative]::WriteProcessMemory($h, [IntPtr][int64]$a, $b, 4, [ref]$w)
}
function Get-Kind { param([uint16]$Id)
    if ($Id -ge 0x1000 -and $Id -le 0x1FFF) { return 'monster' }
    if ($Id -lt 0x1000) { return 'party' }
    return 'other'
}
function Capture-Screen { param([string]$Path)
    Add-Type -AssemblyName System.Drawing; Add-Type -AssemblyName System.Windows.Forms
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
function New-RunDir { param([string]$Prefix)
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $name = if ([string]::IsNullOrWhiteSpace($Label)) { "${Prefix}_${stamp}" } else { "${Prefix}_${Label}_${stamp}" }
    $dir = Join-Path (Resolve-Path $OutDir).Path $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

# ─────────────────────────── MOVE ───────────────────────────
if ($Mode -eq 'Move') {
    try {
        $count = Read-U32 ($base + $RVA_COUNT)
        $table = Read-U32 ($base + $RVA_TABLE)
        if ($null -eq $count -or $null -eq $table -or $table -eq 0 -or $count -eq 0 -or $count -gt $MAX_COUNT) {
            throw "Tabela de atores invalida (jogo em batalha?). count=$count table=$table"
        }

        # resolver o ator alvo
        $inst = 0
        $foundIdx = -1; $foundId = 0
        if ($TargetIdx -ge 0) {
            $foundIdx = $TargetIdx
            $inst = [uint64]$table + ($TargetIdx * $STRIDE)
            $foundId = [BitConverter]::ToUInt16((Read-Block $inst 2), 0)
        }
        elseif (-not [string]::IsNullOrWhiteSpace($TargetId)) {
            $tid = [uint16]([Convert]::ToInt32($TargetId, 16))
            for ($i = 0; $i -lt $count; $i++) {
                $ti = [uint64]$table + ($i * $STRIDE)
                $blk = Read-Block $ti 0x18
                if ($null -eq $blk -or $blk[2] -eq 0) { continue }
                if ([BitConverter]::ToUInt16($blk, 0) -eq $tid) {
                    $x = [BitConverter]::ToSingle($blk, 0x0C)
                    if ([math]::Abs($x) -gt 0.01) { $inst = $ti; $foundIdx = $i; $foundId = $tid; break }
                }
            }
            if ($inst -eq 0) { throw "Ator id $TargetId nao encontrado/deployado." }
        }
        else { throw "Move precisa de -TargetIdx N ou -TargetId 0xHHHH." }

        $ox = Read-F ($inst + 0x0C); $oy = Read-F ($inst + 0x10); $oz = Read-F ($inst + 0x14)
        $useAbs = $PSBoundParameters.ContainsKey('X') -or $PSBoundParameters.ContainsKey('Y') -or $PSBoundParameters.ContainsKey('Z')
        $nx = if ($PSBoundParameters.ContainsKey('X')) { [single]$X } elseif ($useAbs) { $ox } else { [single]($ox + $Dx) }
        $ny = if ($PSBoundParameters.ContainsKey('Y')) { [single]$Y } elseif ($useAbs) { $oy } else { [single]($oy + $Dy) }
        $nz = if ($PSBoundParameters.ContainsKey('Z')) { [single]$Z } elseif ($useAbs) { $oz } else { [single]($oz + $Dz) }

        $runDir = New-RunDir 'move'
        $beforePng = Join-Path $runDir 'before.png'
        $afterPng = Join-Path $runDir 'after.png'
        $restoredPng = Join-Path $runDir 'restored.png'
        if ($Capture) { Capture-Screen $beforePng }

        $mtx = $inst + $OFF_MTX_TX
        $writes = 0
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt $HoldMs) {
            Write-F ($inst + 0x0C) $nx; Write-F ($inst + 0x10) $ny; Write-F ($inst + 0x14) $nz
            Write-F $mtx $nx;          Write-F ($mtx + 4) $ny;     Write-F ($mtx + 8) $nz
            $writes++
            if ($IntervalMs -gt 0) { Start-Sleep -Milliseconds $IntervalMs }
        }
        if ($Capture) { Capture-Screen $afterPng }
        $rbx = Read-F ($inst + 0x0C); $rby = Read-F ($inst + 0x10); $rbz = Read-F ($inst + 0x14)

        if (-not $NoRestore) {
            Write-F ($inst + 0x0C) $ox; Write-F ($inst + 0x10) $oy; Write-F ($inst + 0x14) $oz
            Write-F $mtx $ox;          Write-F ($mtx + 4) $oy;     Write-F ($mtx + 8) $oz
            Start-Sleep -Milliseconds 200
            if ($Capture) { Capture-Screen $restoredPng }
        }

        $report = [ordered]@{
            mode = 'Move'; pid = $proc.Id; moduleBase = ('0x{0:X}' -f $base)
            targetIdx = $foundIdx; targetId = ('0x{0:X4}' -f $foundId); kind = (Get-Kind $foundId)
            original = @($ox, $oy, $oz); moved = @([single]$nx, [single]$ny, [single]$nz)
            readbackAfterHold = @($rbx, $rby, $rbz)
            holdMs = $HoldMs; intervalMs = $IntervalMs; writes = $writes; restored = (-not $NoRestore)
            note = 'engine reescreve por-frame -> jitter no lab externo; no porte in-DLL, escrever no tick pos-update deve ficar liso'
        }
        $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDir 'move_report.json') -Encoding UTF8
        Write-Host ("MOVE id=0x{0:X4} idx={1} ({2}): ({3:F2},{4:F2},{5:F2}) -> ({6:F2},{7:F2},{8:F2}) | writes={9} readback=({10:F2},{11:F2},{12:F2}) restored={13}" -f `
            $foundId, $foundIdx, (Get-Kind $foundId), $ox, $oy, $oz, $nx, $ny, $nz, $writes, $rbx, $rby, $rbz, (-not $NoRestore))
        Write-Host "wrote $runDir"
    } finally {
        [void][BattleActorRamNative]::CloseHandle($h)
    }
    exit 0
}

# ─────────────────────────── CAPTURE ───────────────────────────
try {
    if ($Freeze) {
        $st = [BattleActorRamNative]::NtSuspendProcess($h)
        if ($st -ne 0) { throw "NtSuspendProcess falhou: 0x$($st.ToString('X8'))" }
    }

    $count = Read-U32 ($base + $RVA_COUNT)
    $table = Read-U32 ($base + $RVA_TABLE)
    if ($null -eq $count -or $null -eq $table) { throw "Falha lendo count/table (jogo carregado?)." }

    $camRef = @( (Read-F ($base + $RVA_REF)), (Read-F ($base + ($RVA_REF + 4))), (Read-F ($base + ($RVA_REF + 8))) )
    $camHeading = Read-F ($base + $RVA_HEAD)
    $camElev = Read-F ($base + $RVA_ELEV)

    $actors = New-Object System.Collections.Generic.List[object]
    if ($table -ne 0 -and $count -gt 0 -and $count -le $MAX_COUNT) {
        for ($i = 0; $i -lt $count; $i++) {
            $inst = [uint64]$table + ($i * $STRIDE)
            $blk = Read-Block $inst 0x210
            if ($null -eq $blk) { continue }
            if ($blk[2] -eq 0) { continue }
            $id = [BitConverter]::ToUInt16($blk, 0)
            $flags = [BitConverter]::ToUInt32($blk, 0x194)
            $x = [BitConverter]::ToSingle($blk, 0x0C); $y = [BitConverter]::ToSingle($blk, 0x10); $z = [BitConverter]::ToSingle($blk, 0x14)
            $kind = Get-Kind $id
            if ($kind -eq 'other' -and -not $IncludeOther) { continue }
            $zeroPos = ([math]::Abs($x) -lt 0.01 -and [math]::Abs($y) -lt 0.01 -and [math]::Abs($z) -lt 0.01)
            $absurd = ([math]::Abs($x) -gt 1000.0 -or [math]::Abs($y) -gt 1000.0 -or [math]::Abs($z) -gt 1000.0)
            $reserveClone = (($flags -band 0x80) -ne 0)
            $deployed = (-not $zeroPos) -and (-not $absurd)
            if (-not $deployed -and -not $IncludeUndeployed) { continue }
            $row = [ordered]@{
                idx = $i; id = ('0x{0:X4}' -f $id); kind = $kind; deployed = $deployed
                x = $x; y = $y; z = $z; flags = ('0x{0:X8}' -f $flags); reserveClone = $reserveClone
            }
            if ($WithMatrix) { $m = New-Object 'single[]' 16; [System.Buffer]::BlockCopy([byte[]]$blk, 0x1D0, $m, 0, 64); $row.matrix = $m }
            $actors.Add([pscustomobject]$row) | Out-Null
        }
    }
} finally {
    if ($Freeze) { [void][BattleActorRamNative]::NtResumeProcess($h) }
    [void][BattleActorRamNative]::CloseHandle($h)
}

$runDir = New-RunDir 'actors'
$jsonPath = Join-Path $runDir 'actors.json'
$csvPath = Join-Path $runDir 'actors.csv'
$report = [ordered]@{
    pid = $proc.Id; processName = $proc.ProcessName; moduleBase = ('0x{0:X}' -f $base)
    frozen = [bool]$Freeze; capturedAt = (Get-Date -Format 'yyyyMMdd_HHmmss')
    activeChrCount = $count; activeChrTable = ('0x{0:X8}' -f $table)
    camera = [ordered]@{ refX = $camRef[0]; refY = $camRef[1]; refZ = $camRef[2]; headingRad = $camHeading; elevationRad = $camElev
        note = 'ref=alvo (look-at) provado vivo; angulos lagam/zeram (ver doc camera RAM)' }
    actorCount = $actors.Count; actors = $actors
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('idx,id,kind,deployed,x,y,z,flags') | Out-Null
foreach ($a in $actors) {
    $lines.Add(('{0},{1},{2},{3},{4},{5},{6},{7}' -f $a.idx, $a.id, $a.kind, $a.deployed,
        (Format-Float ([single]$a.x)), (Format-Float ([single]$a.y)), (Format-Float ([single]$a.z)), $a.flags)) | Out-Null
}
[System.IO.File]::WriteAllLines($csvPath, $lines)
Write-Host ("module 0x{0:X} | count={1} table=0x{2:X8} | frozen={3} | captured {4} actors" -f $base, $count, $table, [bool]$Freeze, $actors.Count)
Write-Host ("camera ref = ({0:F3}, {1:F3}, {2:F3})" -f $camRef[0], $camRef[1], $camRef[2])
Write-Host "wrote $jsonPath"; Write-Host "wrote $csvPath"
$actors | Select-Object idx, id, kind, deployed, @{N='x';E={'{0:F2}' -f $_.x}}, @{N='y';E={'{0:F2}' -f $_.y}}, @{N='z';E={'{0:F2}' -f $_.z}} | Format-Table -AutoSize
