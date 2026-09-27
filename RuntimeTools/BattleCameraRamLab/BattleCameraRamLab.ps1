param(
    [ValidateSet('ListCandidates', 'Poke', 'Freeze', 'Watch', 'PokeFloat', 'FreezeFloat', 'WatchFloat', 'DumpNeighborhood', 'WatchNeighborhood')]
    [string]$Mode = 'Watch',

    [Alias('Pid')]
    [int]$GamePid = 0,
    [string]$ProcessName = 'FFX',

    # Accept hex strings like 0x010978A0 or decimal strings (absolute VA in the target process).
    [string[]]$Address = @(),

    # Module-relative addresses (e.g. 0xD378A0). Resolved as MainModule.BaseAddress + Rva,
    # which survives ASLR rebases across game restarts (FFX.exe is a 32-bit ASLR module).
    [string[]]$Rva = @(),

    # Optional BattleCameraRuntimeScout JSON. ListCandidates prints refSetPos hits.
    # Poke/Watch use refSetPos.xyz candidates from it when -Address is omitted.
    [string]$ScoutJson = '',

    # Vec3 deltas (Poke/Freeze on the refSetPos triplet at Address + 0/4/8).
    [single]$Dx = 0,
    [single]$Dy = 0,
    [single]$Dz = 0,

    # Single-float controls (PokeFloat/FreezeFloat).
    #   -Delta adds to the original float.
    #   -Value sets an absolute float (takes precedence when explicitly passed).
    [single]$Delta = 0,
    [single]$Value = 0,

    # Neighborhood window in bytes around Address (DumpNeighborhood/WatchNeighborhood).
    [int]$Before = 512,
    [int]$After = 768,

    # WatchNeighborhood: a float offset counts as "dynamic" when max-min exceeds this.
    [double]$Epsilon = 0.0001,

    [int]$DurationMs = 1000,
    [int]$IntervalMs = 50,

    [switch]$Capture,
    [switch]$NoRestore,

    [string]$OutDir = 'work\camera_ram',
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

if (-not ('BattleCameraRamLabNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class BattleCameraRamLabNative {
  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern IntPtr OpenProcess(UInt32 access, bool inherit, int pid);

  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern bool CloseHandle(IntPtr h);

  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buffer, int size, out IntPtr read);

  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buffer, int size, out IntPtr written);

  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, UInt32 newProtect, out UInt32 oldProtect);

  [DllImport("ntdll.dll")]
  public static extern int NtSuspendProcess(IntPtr h);

  [DllImport("ntdll.dll")]
  public static extern int NtResumeProcess(IntPtr h);
}
'@
}

function Parse-Address {
    param([Parameter(Mandatory=$true)][string]$Value)
    $v = $Value.Trim()
    if ($v.StartsWith('0x', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($v.Substring(2), [System.Globalization.NumberStyles]::HexNumber, $Invariant)
    }
    return [uint32]::Parse($v, $Invariant)
}

function Format-Address {
    param([uint32]$Value)
    return ('0x{0:X8}' -f $Value)
}

function Format-Float {
    param([single]$Value)
    return $Value.ToString('R', $Invariant)
}

# Culture-invariant rounded key (avoids pt-BR comma decimals in change detection).
function Format-Key3 {
    param([double]$Value)
    return ([math]::Round($Value, 3)).ToString($Invariant)
}

function Get-TargetProcess {
    if ($GamePid -gt 0) {
        return Get-Process -Id $GamePid -ErrorAction Stop
    }
    return Get-Process -Name $ProcessName -ErrorAction Stop | Sort-Object StartTime -Descending | Select-Object -First 1
}

function Get-ScoutRefCandidates {
    if ([string]::IsNullOrWhiteSpace($ScoutJson)) { return @() }
    if (-not (Test-Path -LiteralPath $ScoutJson)) { throw "ScoutJson not found: $ScoutJson" }
    $report = Get-Content -LiteralPath $ScoutJson -Raw | ConvertFrom-Json
    return @($report.Matches | Where-Object { $_.Needle -eq 'refSetPos.xyz' } | ForEach-Object {
        [pscustomobject]@{
            Name = 'refSetPos.xyz'
            Address = [uint32]$_.Address
            AddressHex = Format-Address ([uint32]$_.Address)
            ModuleName = $_.ModuleName
            Score = $_.Score
            ValuesLabel = $_.ValuesLabel
        }
    })
}

function Resolve-Addresses {
    $explicit = @($Address | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Parse-Address $_ })
    if ($explicit.Count -gt 0) { return $explicit }
    $rvas = @($Rva | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($rvas.Count -gt 0) {
        $base = $proc.MainModule.BaseAddress.ToInt64()
        return @($rvas | ForEach-Object {
            $abs = [uint32]($base + (Parse-Address $_))
            Write-Host ("resolved RVA {0} -> VA {1} (base {2})" -f $_, (Format-Address $abs), (Format-Address ([uint32]$base)))
            $abs
        })
    }
    $fromScout = @(Get-ScoutRefCandidates | ForEach-Object { [uint32]$_.Address })
    if ($fromScout.Count -gt 0) { return $fromScout }
    throw "No -Address/-Rva provided and no refSetPos.xyz candidates found in -ScoutJson."
}

function Open-ProcessHandle {
    param([int]$ProcessId, [switch]$Write)
    $PROCESS_QUERY_INFORMATION = 0x0400
    $PROCESS_VM_READ = 0x0010
    $PROCESS_VM_WRITE = 0x0020
    $PROCESS_VM_OPERATION = 0x0008
    $PROCESS_SUSPEND_RESUME = 0x0800
    $access = $PROCESS_QUERY_INFORMATION -bor $PROCESS_VM_READ -bor $PROCESS_SUSPEND_RESUME
    if ($Write) { $access = $access -bor $PROCESS_VM_WRITE -bor $PROCESS_VM_OPERATION }
    $h = [BattleCameraRamLabNative]::OpenProcess([uint32]$access, $false, $ProcessId)
    if ($h -eq [IntPtr]::Zero) {
        throw "OpenProcess failed for PID $ProcessId`: 0x$([Runtime.InteropServices.Marshal]::GetLastWin32Error().ToString('X8'))"
    }
    return $h
}

function Read-Block {
    param([IntPtr]$Handle, [uint32]$Addr, [int]$Size)
    $buf = New-Object byte[] $Size
    $read = [IntPtr]::Zero
    if (-not [BattleCameraRamLabNative]::ReadProcessMemory($Handle, [IntPtr]$Addr, $buf, $Size, [ref]$read) -or $read.ToInt64() -ne $Size) {
        throw "ReadProcessMemory $(Format-Address $Addr) x$Size failed: 0x$([Runtime.InteropServices.Marshal]::GetLastWin32Error().ToString('X8'))"
    }
    return $buf
}

function Read-Vec3 {
    param([IntPtr]$Handle, [uint32]$Addr)
    $buf = Read-Block -Handle $Handle -Addr $Addr -Size 12
    return [pscustomobject]@{
        Bytes = $buf
        X = [BitConverter]::ToSingle($buf, 0)
        Y = [BitConverter]::ToSingle($buf, 4)
        Z = [BitConverter]::ToSingle($buf, 8)
    }
}

function Read-FloatAt {
    param([IntPtr]$Handle, [uint32]$Addr)
    $buf = Read-Block -Handle $Handle -Addr $Addr -Size 4
    return [BitConverter]::ToSingle($buf, 0)
}

function Write-Bytes {
    param([IntPtr]$Handle, [uint32]$Addr, [byte[]]$Bytes)
    $PAGE_EXECUTE_READWRITE = 0x40
    $oldProt = 0
    [void][BattleCameraRamLabNative]::VirtualProtectEx($Handle, [IntPtr]$Addr, [UIntPtr]$Bytes.Length, $PAGE_EXECUTE_READWRITE, [ref]$oldProt)
    $written = [IntPtr]::Zero
    if (-not [BattleCameraRamLabNative]::WriteProcessMemory($Handle, [IntPtr]$Addr, $Bytes, $Bytes.Length, [ref]$written) -or $written.ToInt64() -ne $Bytes.Length) {
        throw "WriteProcessMemory $(Format-Address $Addr) failed: 0x$([Runtime.InteropServices.Marshal]::GetLastWin32Error().ToString('X8'))"
    }
    if ($oldProt -ne 0) {
        $tmp = 0
        [void][BattleCameraRamLabNative]::VirtualProtectEx($Handle, [IntPtr]$Addr, [UIntPtr]$Bytes.Length, [uint32]$oldProt, [ref]$tmp)
    }
}

function Write-FloatAt {
    param([IntPtr]$Handle, [uint32]$Addr, [single]$FloatValue)
    Write-Bytes -Handle $Handle -Addr $Addr -Bytes ([BitConverter]::GetBytes([single]$FloatValue))
}

function Suspend-ProcessHandle {
    param([IntPtr]$Handle)
    $status = [BattleCameraRamLabNative]::NtSuspendProcess($Handle)
    if ($status -ne 0) { throw "NtSuspendProcess failed: 0x$($status.ToString('X8'))" }
}

function Resume-ProcessHandle {
    param([IntPtr]$Handle)
    $status = [BattleCameraRamLabNative]::NtResumeProcess($Handle)
    if ($status -ne 0) { throw "NtResumeProcess failed: 0x$($status.ToString('X8'))" }
}

function Capture-Screen {
    param([string]$Path)
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

function New-RunDir {
    param([string]$Prefix)
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $name = if ([string]::IsNullOrWhiteSpace($Label)) { "${Prefix}_${stamp}" } else { "${Prefix}_${Label}_${stamp}" }
    $dir = Join-Path (Resolve-Path $OutDir).Path $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

# Heuristic tag for a float in a neighborhood dump.
function Get-FloatTag {
    param([single]$F, [uint32]$Addr, [uint32]$Base)
    if ($Addr -eq $Base) { return 'currentRefX' }
    if ($Addr -eq ($Base + 4)) { return 'currentRefY' }
    if ($Addr -eq ($Base + 8)) { return 'currentRefZ' }
    if ($Addr -eq ($Base + 0x50)) { return 'currentRefX(mirror)' }
    if ($Addr -eq ($Base + 0x54)) { return 'currentRefY(mirror)' }
    if ($Addr -eq ($Base + 0x58)) { return 'currentRefZ(mirror)' }
    if ([single]::IsNaN($F) -or [single]::IsInfinity($F)) { return '' }
    $a = [math]::Abs([double]$F)
    if ($a -ge 1e-3 -and $a -le 1e4) { return 'finiteSmall' }
    return ''
}

if ($Mode -eq 'ListCandidates') {
    $candidates = @(Get-ScoutRefCandidates)
    if ($candidates.Count -eq 0) {
        Write-Host "No refSetPos.xyz candidates. Provide -ScoutJson from BattleCameraRuntimeScout."
        exit 2
    }
    $candidates | Format-Table -AutoSize
    exit 0
}

$proc = Get-TargetProcess
$addresses = @(Resolve-Addresses)

if ($Mode -eq 'Poke') {
    $runDir = New-RunDir 'camera_poke'
    $reportPath = Join-Path $runDir 'poke_report.json'
    $beforePng = Join-Path $runDir 'before.png'
    $afterPng = Join-Path $runDir 'after.png'
    $restoredPng = Join-Path $runDir 'restored.png'
    $h = Open-ProcessHandle -ProcessId $proc.Id -Write
    $rows = @()
    $backups = @{}
    try {
        if ($Capture) { Capture-Screen $beforePng }

        Suspend-ProcessHandle $h
        try {
            foreach ($addr in $addresses) {
                $v = Read-Vec3 -Handle $h -Addr $addr
                $backups[(Format-Address $addr)] = $v.Bytes
                $newBytes = New-Object byte[] 12
                [Array]::Copy($v.Bytes, $newBytes, 12)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.X + $Dx)), 0, $newBytes, 0, 4)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.Y + $Dy)), 0, $newBytes, 4, 4)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.Z + $Dz)), 0, $newBytes, 8, 4)
                Write-Bytes -Handle $h -Addr $addr -Bytes $newBytes
                $rows += [pscustomobject]@{
                    Address = Format-Address $addr
                    Original = @($v.X, $v.Y, $v.Z)
                    Poked = @([single]($v.X + $Dx), [single]($v.Y + $Dy), [single]($v.Z + $Dz))
                }
            }
        } finally {
            Resume-ProcessHandle $h
        }

        Start-Sleep -Milliseconds $DurationMs
        if ($Capture) { Capture-Screen $afterPng }

        $readback = foreach ($addr in $addresses) {
            $v = Read-Vec3 -Handle $h -Addr $addr
            [pscustomobject]@{
                Address = Format-Address $addr
                ReadbackAfterResume = @($v.X, $v.Y, $v.Z)
            }
        }

        if (-not $NoRestore) {
            Suspend-ProcessHandle $h
            try {
                foreach ($addr in $addresses) {
                    Write-Bytes -Handle $h -Addr $addr -Bytes ([byte[]]$backups[(Format-Address $addr)])
                }
            } finally {
                Resume-ProcessHandle $h
            }
            Start-Sleep -Milliseconds 250
            if ($Capture) { Capture-Screen $restoredPng }
        }

        $report = [pscustomobject]@{
            Pid = $proc.Id
            ProcessName = $proc.ProcessName
            Mode = 'Poke'
            Delta = @{ X = $Dx; Y = $Dy; Z = $Dz }
            DurationMs = $DurationMs
            Restored = -not $NoRestore
            BeforePng = if ($Capture) { $beforePng } else { $null }
            AfterPng = if ($Capture) { $afterPng } else { $null }
            RestoredPng = if ($Capture -and -not $NoRestore) { $restoredPng } else { $null }
            Rows = $rows
            Readback = $readback
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
        $report | ConvertTo-Json -Depth 8
        Write-Host "wrote $reportPath"
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }
    exit 0
}

if ($Mode -eq 'Freeze') {
    $runDir = New-RunDir 'camera_freeze'
    $reportPath = Join-Path $runDir 'freeze_report.json'
    $beforePng = Join-Path $runDir 'before.png'
    $afterPng = Join-Path $runDir 'after.png'
    $restoredPng = Join-Path $runDir 'restored.png'
    $h = Open-ProcessHandle -ProcessId $proc.Id -Write
    $rows = @()
    $backups = @{}
    $targets = @{}
    try {
        if ($Capture) { Capture-Screen $beforePng }

        Suspend-ProcessHandle $h
        try {
            foreach ($addr in $addresses) {
                $v = Read-Vec3 -Handle $h -Addr $addr
                $backups[(Format-Address $addr)] = $v.Bytes
                $targetBytes = New-Object byte[] 12
                [Array]::Copy($v.Bytes, $targetBytes, 12)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.X + $Dx)), 0, $targetBytes, 0, 4)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.Y + $Dy)), 0, $targetBytes, 4, 4)
                [Array]::Copy([BitConverter]::GetBytes([single]($v.Z + $Dz)), 0, $targetBytes, 8, 4)
                $targets[(Format-Address $addr)] = $targetBytes
                Write-Bytes -Handle $h -Addr $addr -Bytes $targetBytes
                $rows += [pscustomobject]@{
                    Address = Format-Address $addr
                    Original = @($v.X, $v.Y, $v.Z)
                    Frozen = @([single]($v.X + $Dx), [single]($v.Y + $Dy), [single]($v.Z + $Dz))
                }
            }
        } finally {
            Resume-ProcessHandle $h
        }

        $writes = 0
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt $DurationMs) {
            foreach ($addr in $addresses) {
                Write-Bytes -Handle $h -Addr $addr -Bytes ([byte[]]$targets[(Format-Address $addr)])
                $writes++
            }
            Start-Sleep -Milliseconds $IntervalMs
        }

        if ($Capture) { Capture-Screen $afterPng }

        $readback = foreach ($addr in $addresses) {
            $v = Read-Vec3 -Handle $h -Addr $addr
            [pscustomobject]@{
                Address = Format-Address $addr
                ReadbackAfterFreeze = @($v.X, $v.Y, $v.Z)
            }
        }

        if (-not $NoRestore) {
            Suspend-ProcessHandle $h
            try {
                foreach ($addr in $addresses) {
                    Write-Bytes -Handle $h -Addr $addr -Bytes ([byte[]]$backups[(Format-Address $addr)])
                }
            } finally {
                Resume-ProcessHandle $h
            }
            Start-Sleep -Milliseconds 250
            if ($Capture) { Capture-Screen $restoredPng }
        }

        $report = [pscustomobject]@{
            Pid = $proc.Id
            ProcessName = $proc.ProcessName
            Mode = 'Freeze'
            Delta = @{ X = $Dx; Y = $Dy; Z = $Dz }
            DurationMs = $DurationMs
            IntervalMs = $IntervalMs
            Writes = $writes
            Restored = -not $NoRestore
            BeforePng = if ($Capture) { $beforePng } else { $null }
            AfterPng = if ($Capture) { $afterPng } else { $null }
            RestoredPng = if ($Capture -and -not $NoRestore) { $restoredPng } else { $null }
            Rows = $rows
            Readback = $readback
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
        $report | ConvertTo-Json -Depth 8
        Write-Host "wrote $reportPath"
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }
    exit 0
}

if ($Mode -eq 'Watch') {
    $runDir = New-RunDir 'camera_watch'
    $csvPath = Join-Path $runDir 'watch.csv'
    $changesPath = Join-Path $runDir 'watch_changes.json'
    $summaryPath = Join-Path $runDir 'watch_summary.json'
    $h = Open-ProcessHandle -ProcessId $proc.Id
    $samples = New-Object System.Collections.Generic.List[object]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($sw.ElapsedMilliseconds -lt $DurationMs) {
            $ms = [int]$sw.ElapsedMilliseconds
            foreach ($addr in $addresses) {
                try {
                    $v = Read-Vec3 -Handle $h -Addr $addr
                    $samples.Add([pscustomobject]@{
                        ms = $ms
                        address = Format-Address $addr
                        x = $v.X
                        y = $v.Y
                        z = $v.Z
                    }) | Out-Null
                } catch {
                    $samples.Add([pscustomobject]@{
                        ms = $ms
                        address = Format-Address $addr
                        x = [double]::NaN
                        y = [double]::NaN
                        z = [double]::NaN
                    }) | Out-Null
                }
            }
            Start-Sleep -Milliseconds $IntervalMs
        }
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('ms,address,x,y,z') | Out-Null
    foreach ($s in $samples) {
        $lines.Add(('{0},{1},{2},{3},{4}' -f
            $s.ms,
            $s.address,
            (Format-Float ([single]$s.x)),
            (Format-Float ([single]$s.y)),
            (Format-Float ([single]$s.z)))) | Out-Null
    }
    [System.IO.File]::WriteAllLines($csvPath, $lines)

    $changes = @()
    foreach ($g in ($samples | Group-Object address)) {
        $lastKey = $null
        foreach ($s in $g.Group) {
            $key = ('{0},{1},{2}' -f (Format-Key3 $s.x), (Format-Key3 $s.y), (Format-Key3 $s.z))
            if ($key -eq $lastKey) { continue }
            $changes += [pscustomobject]@{
                ms = $s.ms
                seconds = [math]::Round($s.ms / 1000.0, 3)
                address = $g.Name
                refSetPos = $key
                x = $s.x
                y = $s.y
                z = $s.z
            }
            $lastKey = $key
        }
    }
    $changes | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $changesPath -Encoding UTF8
    [pscustomobject]@{
        Pid = $proc.Id
        ProcessName = $proc.ProcessName
        Mode = 'Watch'
        DurationMs = $DurationMs
        IntervalMs = $IntervalMs
        Csv = $csvPath
        Changes = $changesPath
        Addresses = @($addresses | ForEach-Object { Format-Address $_ })
        ChangeCount = $changes.Count
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host "wrote $csvPath"
    Write-Host "wrote $changesPath"
    $changes | Format-Table -AutoSize
    exit 0
}

if ($Mode -eq 'PokeFloat') {
    $useAbsolute = $PSBoundParameters.ContainsKey('Value')
    $runDir = New-RunDir 'camera_pokefloat'
    $reportPath = Join-Path $runDir 'pokefloat_report.json'
    $beforePng = Join-Path $runDir 'before.png'
    $afterPng = Join-Path $runDir 'after.png'
    $restoredPng = Join-Path $runDir 'restored.png'
    $h = Open-ProcessHandle -ProcessId $proc.Id -Write
    $rows = @()
    $backups = @{}
    try {
        if ($Capture) { Capture-Screen $beforePng }

        Suspend-ProcessHandle $h
        try {
            foreach ($addr in $addresses) {
                $orig = Read-FloatAt -Handle $h -Addr $addr
                $backups[(Format-Address $addr)] = $orig
                $new = if ($useAbsolute) { [single]$Value } else { [single]($orig + $Delta) }
                Write-FloatAt -Handle $h -Addr $addr -FloatValue $new
                $rows += [pscustomobject]@{
                    Address = Format-Address $addr
                    Original = [single]$orig
                    Poked = [single]$new
                    Mode = if ($useAbsolute) { 'absolute' } else { 'delta' }
                }
            }
        } finally {
            Resume-ProcessHandle $h
        }

        Start-Sleep -Milliseconds $DurationMs
        if ($Capture) { Capture-Screen $afterPng }

        $readback = foreach ($addr in $addresses) {
            [pscustomobject]@{
                Address = Format-Address $addr
                ReadbackAfterResume = [single](Read-FloatAt -Handle $h -Addr $addr)
            }
        }

        if (-not $NoRestore) {
            Suspend-ProcessHandle $h
            try {
                foreach ($addr in $addresses) {
                    Write-FloatAt -Handle $h -Addr $addr -FloatValue ([single]$backups[(Format-Address $addr)])
                }
            } finally {
                Resume-ProcessHandle $h
            }
            Start-Sleep -Milliseconds 250
            if ($Capture) { Capture-Screen $restoredPng }
        }

        $report = [pscustomobject]@{
            Pid = $proc.Id
            ProcessName = $proc.ProcessName
            Mode = 'PokeFloat'
            Delta = $Delta
            Value = if ($useAbsolute) { $Value } else { $null }
            DurationMs = $DurationMs
            Restored = -not $NoRestore
            BeforePng = if ($Capture) { $beforePng } else { $null }
            AfterPng = if ($Capture) { $afterPng } else { $null }
            RestoredPng = if ($Capture -and -not $NoRestore) { $restoredPng } else { $null }
            Rows = $rows
            Readback = $readback
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
        $report | ConvertTo-Json -Depth 8
        Write-Host "wrote $reportPath"
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }
    exit 0
}

if ($Mode -eq 'FreezeFloat') {
    $useAbsolute = $PSBoundParameters.ContainsKey('Value')
    $runDir = New-RunDir 'camera_freezefloat'
    $reportPath = Join-Path $runDir 'freezefloat_report.json'
    $beforePng = Join-Path $runDir 'before.png'
    $afterPng = Join-Path $runDir 'after.png'
    $restoredPng = Join-Path $runDir 'restored.png'
    $h = Open-ProcessHandle -ProcessId $proc.Id -Write
    $rows = @()
    $backups = @{}
    $targets = @{}
    try {
        if ($Capture) { Capture-Screen $beforePng }

        Suspend-ProcessHandle $h
        try {
            foreach ($addr in $addresses) {
                $orig = Read-FloatAt -Handle $h -Addr $addr
                $backups[(Format-Address $addr)] = $orig
                $target = if ($useAbsolute) { [single]$Value } else { [single]($orig + $Delta) }
                $targets[(Format-Address $addr)] = $target
                Write-FloatAt -Handle $h -Addr $addr -FloatValue $target
                $rows += [pscustomobject]@{
                    Address = Format-Address $addr
                    Original = [single]$orig
                    Frozen = [single]$target
                    Mode = if ($useAbsolute) { 'absolute' } else { 'delta' }
                }
            }
        } finally {
            Resume-ProcessHandle $h
        }

        $writes = 0
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt $DurationMs) {
            foreach ($addr in $addresses) {
                Write-FloatAt -Handle $h -Addr $addr -FloatValue ([single]$targets[(Format-Address $addr)])
                $writes++
            }
            Start-Sleep -Milliseconds $IntervalMs
        }

        if ($Capture) { Capture-Screen $afterPng }

        $readback = foreach ($addr in $addresses) {
            [pscustomobject]@{
                Address = Format-Address $addr
                ReadbackAfterFreeze = [single](Read-FloatAt -Handle $h -Addr $addr)
            }
        }

        if (-not $NoRestore) {
            Suspend-ProcessHandle $h
            try {
                foreach ($addr in $addresses) {
                    Write-FloatAt -Handle $h -Addr $addr -FloatValue ([single]$backups[(Format-Address $addr)])
                }
            } finally {
                Resume-ProcessHandle $h
            }
            Start-Sleep -Milliseconds 250
            if ($Capture) { Capture-Screen $restoredPng }
        }

        $report = [pscustomobject]@{
            Pid = $proc.Id
            ProcessName = $proc.ProcessName
            Mode = 'FreezeFloat'
            Delta = $Delta
            Value = if ($useAbsolute) { $Value } else { $null }
            DurationMs = $DurationMs
            IntervalMs = $IntervalMs
            Writes = $writes
            Restored = -not $NoRestore
            BeforePng = if ($Capture) { $beforePng } else { $null }
            AfterPng = if ($Capture) { $afterPng } else { $null }
            RestoredPng = if ($Capture -and -not $NoRestore) { $restoredPng } else { $null }
            Rows = $rows
            Readback = $readback
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
        $report | ConvertTo-Json -Depth 8
        Write-Host "wrote $reportPath"
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }
    exit 0
}

if ($Mode -eq 'WatchFloat') {
    $runDir = New-RunDir 'camera_watchfloat'
    $csvPath = Join-Path $runDir 'watchfloat.csv'
    $changesPath = Join-Path $runDir 'watchfloat_changes.json'
    $summaryPath = Join-Path $runDir 'watchfloat_summary.json'
    $h = Open-ProcessHandle -ProcessId $proc.Id
    $samples = New-Object System.Collections.Generic.List[object]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($sw.ElapsedMilliseconds -lt $DurationMs) {
            $ms = [int]$sw.ElapsedMilliseconds
            foreach ($addr in $addresses) {
                $f = [double]::NaN
                try { $f = [double](Read-FloatAt -Handle $h -Addr $addr) } catch { $f = [double]::NaN }
                $samples.Add([pscustomobject]@{ ms = $ms; address = (Format-Address $addr); value = $f }) | Out-Null
            }
            Start-Sleep -Milliseconds $IntervalMs
        }
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('ms,address,value,delta_from_first') | Out-Null
    $firstByAddr = @{}
    foreach ($s in $samples) {
        if (-not $firstByAddr.ContainsKey($s.address)) { $firstByAddr[$s.address] = $s.value }
        $deltaFromFirst = $s.value - $firstByAddr[$s.address]
        $lines.Add(('{0},{1},{2},{3}' -f
            $s.ms, $s.address,
            (Format-Float ([single]$s.value)),
            (Format-Float ([single]$deltaFromFirst)))) | Out-Null
    }
    [System.IO.File]::WriteAllLines($csvPath, $lines)

    $changes = @()
    foreach ($g in ($samples | Group-Object address)) {
        $lastKey = $null
        foreach ($s in $g.Group) {
            $key = Format-Key3 $s.value
            if ($key -eq $lastKey) { continue }
            $changes += [pscustomobject]@{
                ms = $s.ms
                seconds = [math]::Round($s.ms / 1000.0, 3)
                address = $g.Name
                value = $s.value
            }
            $lastKey = $key
        }
    }
    $changes | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $changesPath -Encoding UTF8
    [pscustomobject]@{
        Pid = $proc.Id
        ProcessName = $proc.ProcessName
        Mode = 'WatchFloat'
        DurationMs = $DurationMs
        IntervalMs = $IntervalMs
        Csv = $csvPath
        Changes = $changesPath
        Addresses = @($addresses | ForEach-Object { Format-Address $_ })
        ChangeCount = $changes.Count
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host "wrote $csvPath"
    Write-Host "wrote $changesPath"
    $changes | Format-Table -AutoSize
    exit 0
}

if ($Mode -eq 'DumpNeighborhood') {
    $base = [uint32]$addresses[0]
    $start = [uint32]($base - $Before)
    $size = [int]($Before + $After)
    $count = [int]($size / 4)
    $runDir = New-RunDir 'camera_neighborhood'
    $csvPath = Join-Path $runDir 'neighborhood.csv'
    $h = Open-ProcessHandle -ProcessId $proc.Id
    try {
        $buf = Read-Block -Handle $h -Addr $start -Size $size
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('address,rel,float,uint,tag') | Out-Null
    for ($i = 0; $i -lt $count; $i++) {
        $addr = [uint32]($start + ($i * 4))
        $f = [BitConverter]::ToSingle($buf, $i * 4)
        $u = [BitConverter]::ToUInt32($buf, $i * 4)
        $rel = [int64]$addr - [int64]$base
        $relHex = if ($rel -lt 0) { ('-0x{0:X}' -f [math]::Abs($rel)) } else { ('0x{0:X}' -f $rel) }
        $tag = Get-FloatTag -F $f -Addr $addr -Base $base
        $lines.Add(('"{0}","{1}","{2}","0x{3:X8}","{4}"' -f
            (Format-Address $addr), $relHex, (Format-Float $f), $u, $tag)) | Out-Null
    }
    [System.IO.File]::WriteAllLines($csvPath, $lines)
    Write-Host "wrote $csvPath ($count floats, base $(Format-Address $base), -$('0x{0:X}' -f $Before)..+$('0x{0:X}' -f $After))"
    exit 0
}

if ($Mode -eq 'WatchNeighborhood') {
    $base = [uint32]$addresses[0]
    $start = [uint32]($base - $Before)
    $size = [int]($Before + $After)
    $count = [int]($size / 4)
    $runDir = New-RunDir 'camera_watchneigh'
    $summaryCsv = Join-Path $runDir 'neighborhood_stats.csv'
    $dynamicJson = Join-Path $runDir 'dynamic.json'
    $dynamicCsv = Join-Path $runDir 'dynamic_timeline.csv'
    $metaJson = Join-Path $runDir 'meta.json'

    $h = Open-ProcessHandle -ProcessId $proc.Id
    $snaps = New-Object System.Collections.Generic.List[object]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($sw.ElapsedMilliseconds -lt $DurationMs) {
            $ms = [int]$sw.ElapsedMilliseconds
            try {
                $buf = Read-Block -Handle $h -Addr $start -Size $size
                $vals = New-Object 'single[]' $count
                for ($i = 0; $i -lt $count; $i++) { $vals[$i] = [BitConverter]::ToSingle($buf, $i * 4) }
                $snaps.Add([pscustomobject]@{ ms = $ms; vals = $vals }) | Out-Null
            } catch {
                # transient read failure; skip this tick
            }
            Start-Sleep -Milliseconds $IntervalMs
        }
    } finally {
        [void][BattleCameraRamLabNative]::CloseHandle($h)
    }

    $sampleCount = $snaps.Count
    if ($sampleCount -eq 0) { throw "WatchNeighborhood captured 0 samples." }

    # Per-offset stats (incremental over snapshots).
    $statLines = New-Object System.Collections.Generic.List[string]
    $statLines.Add('rel,address,first,last,min,max,span,changed') | Out-Null
    $dynamic = @()
    for ($i = 0; $i -lt $count; $i++) {
        $addr = [uint32]($start + ($i * 4))
        $rel = [int64]$addr - [int64]$base
        $relHex = if ($rel -lt 0) { ('-0x{0:X}' -f [math]::Abs($rel)) } else { ('0x{0:X}' -f $rel) }
        $first = [double]$snaps[0].vals[$i]
        $last = [double]$snaps[$sampleCount - 1].vals[$i]
        $min = [double]::PositiveInfinity
        $max = [double]::NegativeInfinity
        $anyFinite = $false
        foreach ($snap in $snaps) {
            $v = [double]$snap.vals[$i]
            if ([double]::IsNaN($v) -or [double]::IsInfinity($v)) { continue }
            $anyFinite = $true
            if ($v -lt $min) { $min = $v }
            if ($v -gt $max) { $max = $v }
        }
        $span = if ($anyFinite) { $max - $min } else { 0.0 }
        $changed = $anyFinite -and ($span -gt $Epsilon)
        $statLines.Add(('"{0}","{1}","{2}","{3}","{4}","{5}","{6}","{7}"' -f
            $relHex, (Format-Address $addr),
            (Format-Float ([single]$first)), (Format-Float ([single]$last)),
            (Format-Float ([single]$min)), (Format-Float ([single]$max)),
            (Format-Float ([single]$span)), $changed)) | Out-Null
        if ($changed) {
            $dynamic += [pscustomobject]@{
                rel = $relHex
                address = Format-Address $addr
                index = $i
                first = $first
                last = $last
                min = $min
                max = $max
                span = $span
            }
        }
    }
    [System.IO.File]::WriteAllLines($summaryCsv, $statLines)

    $dynamicSorted = @($dynamic | Sort-Object -Property span -Descending)
    $dynamicSorted | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $dynamicJson -Encoding UTF8

    # Wide timeline of only the dynamic offsets (skip if too many to stay readable).
    $maxWide = 64
    if ($dynamicSorted.Count -gt 0 -and $dynamicSorted.Count -le $maxWide) {
        $header = New-Object System.Collections.Generic.List[string]
        $header.Add('ms') | Out-Null
        foreach ($d in $dynamicSorted) { $header.Add($d.address) | Out-Null }
        $timeline = New-Object System.Collections.Generic.List[string]
        $timeline.Add(($header -join ',')) | Out-Null
        foreach ($snap in $snaps) {
            $row = New-Object System.Collections.Generic.List[string]
            $row.Add([string]$snap.ms) | Out-Null
            foreach ($d in $dynamicSorted) { $row.Add((Format-Float ([single]$snap.vals[$d.index]))) | Out-Null }
            $timeline.Add(($row -join ',')) | Out-Null
        }
        [System.IO.File]::WriteAllLines($dynamicCsv, $timeline)
    } else {
        $dynamicCsv = $null
    }

    [pscustomobject]@{
        Pid = $proc.Id
        ProcessName = $proc.ProcessName
        Mode = 'WatchNeighborhood'
        Base = Format-Address $base
        Start = Format-Address $start
        Before = $Before
        After = $After
        FloatCount = $count
        DurationMs = $DurationMs
        IntervalMs = $IntervalMs
        Samples = $sampleCount
        Epsilon = $Epsilon
        DynamicCount = $dynamicSorted.Count
        StatsCsv = $summaryCsv
        DynamicJson = $dynamicJson
        DynamicTimelineCsv = $dynamicCsv
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metaJson -Encoding UTF8

    Write-Host "wrote $summaryCsv"
    Write-Host "wrote $dynamicJson"
    if ($dynamicCsv) { Write-Host "wrote $dynamicCsv" }
    Write-Host "samples=$sampleCount floats=$count dynamic=$($dynamicSorted.Count)"
    if ($dynamicSorted.Count -gt 0) {
        $dynamicSorted | Select-Object -First 24 rel, address, @{N='first';E={Format-Float ([single]$_.first)}}, @{N='last';E={Format-Float ([single]$_.last)}}, @{N='span';E={Format-Float ([single]$_.span)}} | Format-Table -AutoSize
    } else {
        Write-Host "No dynamic offsets above epsilon=$Epsilon (camera was static during the window)."
    }
    exit 0
}
