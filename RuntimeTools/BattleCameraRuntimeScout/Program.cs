// BattleCameraRuntimeScout
//
// Lab-only RAM scout for the Aurora/Battle Sandbox camera lane. It takes the
// already-proven per-battle camera setup (refSetPos + camSetPolar 0x6004),
// computes the expected ref/eye/polar vectors, then scans a live FFX.exe
// process for matching float triplets. This does not write memory.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

const string DefaultBtlRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";
const int MaxRegionRead = 64 * 1024 * 1024;
const int ChunkSize = 4 * 1024 * 1024;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

string target = args[0];
string btlRoot = DefaultBtlRoot;
string encounterPath = Path.Combine(Path.GetDirectoryName(DefaultBtlRoot) ?? "", "kernel", "btl.bin");
string? jsonOut = null;
int? pidArg = null;
float tolerance = 0.025f;
int limit = 160;
int perNeedle = 40;
bool scanPrivateOnly = false;
bool suspendDuringScan = false;

for (int i = 1; i < args.Length; i++)
{
    string a = args[i];
    if (a == "--btl-root" && i + 1 < args.Length) btlRoot = args[++i];
    else if (a == "--encounter" && i + 1 < args.Length) encounterPath = args[++i];
    else if (a == "--pid" && i + 1 < args.Length) pidArg = ParseInt(args[++i]);
    else if (a == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (a == "--tolerance" && i + 1 < args.Length) tolerance = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a == "--limit" && i + 1 < args.Length) limit = ParseInt(args[++i]);
    else if (a == "--per-needle" && i + 1 < args.Length) perNeedle = ParseInt(args[++i]);
    else if (a == "--private-only") scanPrivateOnly = true;
    else if (a == "--suspend") suspendDuringScan = true;
    else
    {
        Console.Error.WriteLine($"unknown arg: {a}");
        PrintUsage();
        return 1;
    }
}

string battlePath;
try
{
    battlePath = ResolveBattlePath(target, btlRoot, encounterPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"failed to resolve battle target '{target}': {ex.Message}");
    return 2;
}
if (!File.Exists(battlePath))
{
    Console.Error.WriteLine($"battle bin not found: {battlePath}");
    return 2;
}

string battleId = Path.GetFileNameWithoutExtension(battlePath);
byte[] battleBytes = File.ReadAllBytes(battlePath);
BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBytes);
CameraEstablishingShot? e = setup.Establishing;
if (e == null || !e.HasRef || !e.HasPolar)
{
    Console.Error.WriteLine($"battle {battleId} has no simple establishing ref+polar.");
    Console.Error.WriteLine($"notes: {string.Join("; ", setup.Notes)}");
    return 3;
}

(float eyeX, float eyeY, float eyeZ) = BattleCameraSetup_File.PolarToEye(
    e.RefX, e.RefY, e.RefZ,
    e.PolarHorizontalAngle, e.PolarElevationAngle, e.PolarDistance);

var look = Normalize((e.RefX - eyeX, e.RefY - eyeY, e.RefZ - eyeZ));
var eyeBack = Normalize((eyeX - e.RefX, eyeY - e.RefY, eyeZ - e.RefZ));

var needles = new List<FloatNeedle>
{
    new("refSetPos.xyz", new[] { e.RefX, e.RefY, e.RefZ }, "chunk0 float-pool look-at point"),
    new("camEye.xyz", new[] { eyeX, eyeY, eyeZ }, "derived from IDA-proven camSetPolar"),
    new("polar.h/e/d", new[] { e.PolarHorizontalAngle, e.PolarElevationAngle, e.PolarDistance }, "camSetPolar args in script order"),
    new("lookDir.xyz", new[] { look.X, look.Y, look.Z }, "normalized ref-eye direction, possible view matrix row/column"),
    new("eyeMinusRefDir.xyz", new[] { eyeBack.X, eyeBack.Y, eyeBack.Z }, "opposite direction, possible camera forward/back vector"),
};

Process? proc = pidArg.HasValue
    ? Process.GetProcessById(pidArg.Value)
    : Process.GetProcessesByName("FFX").OrderByDescending(p => p.StartTimeSafe()).FirstOrDefault();
if (proc == null)
{
    Console.Error.WriteLine("FFX.exe process not found. Start the game, enter/force a battle, then run this tool.");
    return 4;
}

ProcessAccess access = ProcessAccess.QueryInformation | ProcessAccess.VirtualMemoryRead;
if (suspendDuringScan) access |= ProcessAccess.SuspendResume;
IntPtr handle = Native.OpenProcess(access, false, proc.Id);
if (handle == IntPtr.Zero)
{
    Console.Error.WriteLine($"OpenProcess failed for PID {proc.Id}: 0x{Marshal.GetLastWin32Error():X8}");
    return 5;
}

var moduleRanges = GetModuleRanges(proc);
var matches = new List<RuntimeCameraMatch>();
var matchCounts = needles.ToDictionary(n => n.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
var regions = 0;
var scannedBytes = 0L;
bool suspended = false;

try
{
    if (suspendDuringScan)
    {
        int suspendStatus = Native.NtSuspendProcess(handle);
        if (suspendStatus != 0)
        {
            Console.Error.WriteLine($"NtSuspendProcess failed for PID {proc.Id}: 0x{suspendStatus:X8}");
            return 6;
        }
        suspended = true;
    }

    foreach (MemoryRegion region in EnumerateReadableRegions(handle))
    {
        if (scanPrivateOnly && region.Type != MemoryType.Private) continue;
        if (region.Size <= 0 || region.Size > MaxRegionRead) continue;
        regions++;
        scannedBytes += region.Size;
        ScanRegion(handle, region, needles, matches, matchCounts, moduleRanges, tolerance, perNeedle);
        if (matchCounts.Values.All(c => c >= perNeedle)) break;
    }
}
finally
{
    if (suspended)
    {
        int resumeStatus = Native.NtResumeProcess(handle);
        if (resumeStatus != 0)
        {
            Console.Error.WriteLine($"WARNING: NtResumeProcess failed for PID {proc.Id}: 0x{resumeStatus:X8}. Resume the process manually before continuing.");
        }
    }
    Native.CloseHandle(handle);
}

matches = matches
    .OrderBy(m => m.Needle)
    .ThenByDescending(m => m.Score)
    .ThenBy(m => m.Address)
    .Take(limit)
    .ToList();

var report = new RuntimeCameraScoutReport(
    battleId,
    battlePath,
    proc.Id,
    proc.ProcessName,
    suspendDuringScan ? "read-only runtime RAM scan; process suspended during scan" : "read-only runtime RAM scan",
    new CameraOfflineSeed(
        e.RefX, e.RefY, e.RefZ,
        eyeX, eyeY, eyeZ,
        e.PolarHorizontalAngle, e.PolarElevationAngle, e.PolarDistance,
        look.X, look.Y, look.Z),
    tolerance,
    regions,
    scannedBytes,
    matches);

Console.WriteLine("BattleCameraRuntimeScout — read-only RAM scan");
Console.WriteLine($"  battle          : {battleId}");
Console.WriteLine($"  process         : {proc.ProcessName} PID {proc.Id}");
Console.WriteLine($"  scan mode       : {(suspendDuringScan ? "suspended process during scan" : "live process")}");
Console.WriteLine($"  ref             : ({e.RefX:0.###}, {e.RefY:0.###}, {e.RefZ:0.###})");
Console.WriteLine($"  eye             : ({eyeX:0.###}, {eyeY:0.###}, {eyeZ:0.###})");
Console.WriteLine($"  polar h/e/dist  : {e.PolarHorizontalAngle:0.###} / {e.PolarElevationAngle:0.###} / {e.PolarDistance:0.###}");
Console.WriteLine($"  tolerance       : +/- {tolerance:0.###}");
Console.WriteLine($"  regions/bytes   : {regions} / {scannedBytes:N0}");
Console.WriteLine($"  matches         : {matches.Count}");
Console.WriteLine($"  per-needle      : {string.Join(" ", matchCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
foreach (RuntimeCameraMatch m in matches.Take(40))
{
    string rva = m.ModuleRva.HasValue ? $"+0x{m.ModuleRva.Value:X}" : "";
    Console.WriteLine($"  {m.Needle,-20} addr=0x{m.Address:X8} {m.ModuleName}{rva} score={m.Score:0.###} values={m.ValuesLabel}");
}
if (matches.Count > 40) Console.WriteLine($"  ... +{matches.Count - 40} more");

if (!string.IsNullOrWhiteSpace(jsonOut))
{
    string? dir = Path.GetDirectoryName(jsonOut);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

Console.WriteLine("VERDICT: lab-only. Matches are candidates; confirm by live watch/writepoint before treating any address as the camera owner.");
return 0;

static string ResolveBattlePath(string target, string btlRoot, string encounterPath)
{
    if (File.Exists(target)) return Path.GetFullPath(target);
    if (target.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
    {
        string[] parts = target.Split(':');
        if (parts.Length != 4)
            throw new ArgumentException("route target must be route:<field>:<group>:<formation>");
        int field = ParseInt(parts[1]);
        int group = ParseInt(parts[2]);
        int formation = ParseInt(parts[3]);
        if (!File.Exists(encounterPath))
            throw new FileNotFoundException("encounter btl.bin not found", encounterPath);

        EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
        EncounterTable_Entry? table = enc.Tables.FirstOrDefault(t => t.TableIndex == field)
            ?? enc.Tables.FirstOrDefault(t => t.Id == field);
        if (table == null)
        {
            string examples = string.Join(", ", enc.Tables.Take(8).Select(t => $"{t.TableIndex}/id{t.Id}:{t.Map}"));
            throw new InvalidOperationException($"field/table {field} not found by TableIndex or Id. Examples: {examples}");
        }
        EncounterTable_Group groupRow = table.Groups.FirstOrDefault(g => g.GroupIndex == group)
            ?? throw new InvalidOperationException($"group {group} not found in field/table {field} resolved to table {table.TableIndex}/id{table.Id}:{table.Map}.");
        EncounterTable_Formation formationRow = groupRow.Formations.FirstOrDefault(f => f.FormationId == formation)
            ?? throw new InvalidOperationException($"formation {formation} not found in table {table.TableIndex}/id{table.Id}:{table.Map}, group {group}.");
        return Path.Combine(btlRoot, formationRow.BattleId, formationRow.BattleId + ".bin");
    }

    string id = Path.GetFileNameWithoutExtension(target);
    return Path.Combine(btlRoot, id, id + ".bin");
}

static void ScanRegion(
    IntPtr process,
    MemoryRegion region,
    IReadOnlyList<FloatNeedle> needles,
    List<RuntimeCameraMatch> matches,
    Dictionary<string, int> matchCounts,
    IReadOnlyList<ModuleRange> modules,
    float tolerance,
    int perNeedle)
{
    long remaining = region.Size;
    long current = region.BaseAddress;
    byte[] carry = Array.Empty<byte>();

    while (remaining > 0 && matchCounts.Values.Any(c => c < perNeedle))
    {
        int want = (int)Math.Min(ChunkSize, remaining);
        byte[] buf = new byte[want + carry.Length];
        if (carry.Length > 0) Buffer.BlockCopy(carry, 0, buf, 0, carry.Length);

        if (!ReadProcessMemory(process, new IntPtr(current), buf, carry.Length, want, out int got) || got <= 0)
        {
            break;
        }

        int len = carry.Length + got;
        long bufBase = current - carry.Length;
        for (int off = 0; off + 12 <= len; off += 4)
        {
            float a = BitConverter.ToSingle(buf, off);
            float b = BitConverter.ToSingle(buf, off + 4);
            float c = BitConverter.ToSingle(buf, off + 8);
            if (!Finite(a) || !Finite(b) || !Finite(c)) continue;

            foreach (FloatNeedle needle in needles)
            {
                if (matchCounts[needle.Name] >= perNeedle) continue;
                float da = Math.Abs(a - needle.Values[0]);
                float db = Math.Abs(b - needle.Values[1]);
                float dc = Math.Abs(c - needle.Values[2]);
                float max = Math.Max(da, Math.Max(db, dc));
                if (max <= tolerance)
                {
                    long address = bufBase + off;
                    ModuleRange? mod = modules.FirstOrDefault(m => address >= m.Base && address < m.Base + m.Size);
                    matches.Add(new RuntimeCameraMatch(
                        needle.Name,
                        address,
                        mod?.Name ?? RegionTypeLabel(region.Type),
                        mod == null ? null : address - mod.Base,
                        max,
                        $"{a:0.###},{b:0.###},{c:0.###}",
                        needle.Note));
                    matchCounts[needle.Name]++;
                    if (matchCounts.Values.All(c => c >= perNeedle)) return;
                }
            }
        }

        int carryLen = Math.Min(8, len);
        carry = new byte[carryLen];
        Buffer.BlockCopy(buf, len - carryLen, carry, 0, carryLen);
        current += got;
        remaining -= got;
    }
}

static IEnumerable<MemoryRegion> EnumerateReadableRegions(IntPtr process)
{
    long addr = 0x10000;
    const long maxUser = 0x7FFF0000;
    while (addr < maxUser)
    {
        if (Native.VirtualQueryEx(process, new IntPtr(addr), out MEMORY_BASIC_INFORMATION mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
        {
            addr += 0x10000;
            continue;
        }

        long baseAddress = mbi.BaseAddress.ToInt64();
        long size = mbi.RegionSize.ToInt64();
        if (size <= 0)
        {
            addr += 0x10000;
            continue;
        }

        if (mbi.State == MemoryState.Commit && IsReadable(mbi.Protect))
        {
            yield return new MemoryRegion(baseAddress, size, mbi.Protect, mbi.Type);
        }

        long next = baseAddress + size;
        addr = next > addr ? next : addr + 0x10000;
    }
}

static IReadOnlyList<ModuleRange> GetModuleRanges(Process process)
{
    var ranges = new List<ModuleRange>();
    foreach (ProcessModule module in process.Modules)
    {
        ranges.Add(new ModuleRange(
            module.ModuleName,
            module.BaseAddress.ToInt64(),
            module.ModuleMemorySize));
    }
    return ranges;
}

static bool IsReadable(MemoryProtect protect)
{
    if ((protect & MemoryProtect.Guard) != 0) return false;
    if ((protect & MemoryProtect.NoAccess) != 0) return false;
    MemoryProtect p = protect & ~(MemoryProtect.Guard | MemoryProtect.NoCache | MemoryProtect.WriteCombine);
    return p is MemoryProtect.ReadOnly
        or MemoryProtect.ReadWrite
        or MemoryProtect.WriteCopy
        or MemoryProtect.ExecuteRead
        or MemoryProtect.ExecuteReadWrite
        or MemoryProtect.ExecuteWriteCopy;
}

static bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int offset, int count, out int read)
{
    byte[] tmp = offset == 0 ? buffer : new byte[count];
    bool ok = Native.ReadProcessMemory(process, address, tmp, count, out IntPtr bytesRead);
    read = bytesRead.ToInt32();
    if (ok && offset != 0 && read > 0) Buffer.BlockCopy(tmp, 0, buffer, offset, read);
    return ok;
}

static (float X, float Y, float Z) Normalize((float X, float Y, float Z) v)
{
    double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    if (len < 1e-6) return (0, 0, 0);
    return ((float)(v.X / len), (float)(v.Y / len), (float)(v.Z / len));
}

static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

static int ParseInt(string value)
{
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return int.Parse(value, CultureInfo.InvariantCulture);
}

static string RegionTypeLabel(MemoryType type) => type switch
{
    MemoryType.Image => "MEM_IMAGE",
    MemoryType.Mapped => "MEM_MAPPED",
    MemoryType.Private => "MEM_PRIVATE",
    _ => $"MEM_0x{(uint)type:X}",
};

static void PrintUsage()
{
    Console.WriteLine("usage:");
    Console.WriteLine("  BattleCameraRuntimeScout <battleId|battle.bin|route:f:g:fo> [--btl-root <path>] [--encounter <btl.bin>] [--pid <pid>] [--tolerance <float>] [--limit <n>] [--per-needle <n>] [--private-only] [--suspend] [--json <out.json>]");
    Console.WriteLine();
    Console.WriteLine("examples:");
    Console.WriteLine("  dotnet run --project RuntimeTools/BattleCameraRuntimeScout/BattleCameraRuntimeScout.csproj -c Release -- azit03_00 --json work/camera_ram/azit03_00.json");
    Console.WriteLine("  dotnet run --project RuntimeTools/BattleCameraRuntimeScout/BattleCameraRuntimeScout.csproj -c Release -- route:363:0:0 --pid 1234 --json work/camera_ram/current_route.json");
    Console.WriteLine("  dotnet run --project RuntimeTools/BattleCameraRuntimeScout/BattleCameraRuntimeScout.csproj -c Release -- bika00_10 --tolerance 0.01 --private-only");
}

[Flags]
enum ProcessAccess : uint
{
    QueryInformation = 0x0400,
    VirtualMemoryRead = 0x0010,
    SuspendResume = 0x0800,
}

enum MemoryState : uint
{
    Commit = 0x1000,
}

[Flags]
enum MemoryProtect : uint
{
    NoAccess = 0x01,
    ReadOnly = 0x02,
    ReadWrite = 0x04,
    WriteCopy = 0x08,
    Execute = 0x10,
    ExecuteRead = 0x20,
    ExecuteReadWrite = 0x40,
    ExecuteWriteCopy = 0x80,
    Guard = 0x100,
    NoCache = 0x200,
    WriteCombine = 0x400,
}

enum MemoryType : uint
{
    Private = 0x20000,
    Mapped = 0x40000,
    Image = 0x1000000,
}

[StructLayout(LayoutKind.Sequential)]
struct MEMORY_BASIC_INFORMATION
{
    public IntPtr BaseAddress;
    public IntPtr AllocationBase;
    public MemoryProtect AllocationProtect;
    public IntPtr RegionSize;
    public MemoryState State;
    public MemoryProtect Protect;
    public MemoryType Type;
}

sealed record FloatNeedle(string Name, float[] Values, string Note);
sealed record MemoryRegion(long BaseAddress, long Size, MemoryProtect Protect, MemoryType Type);
sealed record ModuleRange(string Name, long Base, int Size);
sealed record CameraOfflineSeed(
    float RefX, float RefY, float RefZ,
    float EyeX, float EyeY, float EyeZ,
    float HorizontalDeg, float ElevationDeg, float Distance,
    float LookX, float LookY, float LookZ);
sealed record RuntimeCameraMatch(
    string Needle,
    long Address,
    string ModuleName,
    long? ModuleRva,
    float Score,
    string ValuesLabel,
    string Note);
sealed record RuntimeCameraScoutReport(
    string BattleId,
    string BattlePath,
    int ProcessId,
    string ProcessName,
    string Mode,
    CameraOfflineSeed Seed,
    float Tolerance,
    int RegionsScanned,
    long BytesScanned,
    IReadOnlyList<RuntimeCameraMatch> Matches);

static class ProcessExtensions
{
    public static DateTime StartTimeSafe(this Process process)
    {
        try { return process.StartTime; }
        catch { return DateTime.MinValue; }
    }
}

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(ProcessAccess access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION buffer, uint length);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadProcessMemory")]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out IntPtr bytesRead);

    [DllImport("ntdll.dll")]
    public static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    public static extern int NtResumeProcess(IntPtr processHandle);
}
