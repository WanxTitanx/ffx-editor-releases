// BattleWorldToScreenScout
//
// Lab-only RAM scanner for Aurora/Battle Sandbox overlay research. It uses
// manually tagged world->screen pairs from a live battle screenshot and scans
// FFX.exe memory for 4x4 matrices that project those points. Read-only.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

const int DefaultWidth = 2560;
const int DefaultHeight = 1440;
const int DefaultChunkSize = 4 * 1024 * 1024;
const long DefaultMaxRegion = 64L * 1024 * 1024;

string? pointsPath = null;
string? jsonOut = null;
int? pidArg = null;
int width = DefaultWidth;
int height = DefaultHeight;
int limit = 40;
int step = 16;
float maxAbsMatrixValue = 100000f;
float maxMeanError = 220f;
bool scanPrivateOnly = false;
bool scanImageOnly = false;
bool suspendDuringScan = false;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a is "-h" or "--help" or "help")
    {
        PrintUsage();
        return 0;
    }
    if (a == "--points" && i + 1 < args.Length) pointsPath = args[++i];
    else if (a == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (a == "--pid" && i + 1 < args.Length) pidArg = ParseInt(args[++i]);
    else if (a == "--width" && i + 1 < args.Length) width = ParseInt(args[++i]);
    else if (a == "--height" && i + 1 < args.Length) height = ParseInt(args[++i]);
    else if (a == "--limit" && i + 1 < args.Length) limit = ParseInt(args[++i]);
    else if (a == "--step" && i + 1 < args.Length) step = Math.Max(4, ParseInt(args[++i]));
    else if (a == "--max-mean-error" && i + 1 < args.Length) maxMeanError = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a == "--max-matrix-abs" && i + 1 < args.Length) maxAbsMatrixValue = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a == "--private-only") scanPrivateOnly = true;
    else if (a == "--image-only") scanImageOnly = true;
    else if (a == "--suspend") suspendDuringScan = true;
    else
    {
        Console.Error.WriteLine($"unknown arg: {a}");
        PrintUsage();
        return 1;
    }
}

if (string.IsNullOrWhiteSpace(pointsPath))
{
    Console.Error.WriteLine("missing --points <csv>");
    PrintUsage();
    return 1;
}

List<TaggedPoint> points;
try
{
    points = LoadPoints(pointsPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"failed to read points '{pointsPath}': {ex.Message}");
    return 2;
}

if (points.Count < 4)
{
    Console.Error.WriteLine($"need at least 4 point pairs, got {points.Count}");
    return 2;
}

Process? proc = pidArg.HasValue
    ? Process.GetProcessById(pidArg.Value)
    : Process.GetProcessesByName("FFX").OrderByDescending(p => p.StartTimeSafe()).FirstOrDefault();
if (proc == null)
{
    Console.Error.WriteLine("FFX.exe process not found.");
    return 3;
}

ProcessAccess access = ProcessAccess.QueryInformation | ProcessAccess.VirtualMemoryRead;
if (suspendDuringScan) access |= ProcessAccess.SuspendResume;
IntPtr handle = Native.OpenProcess(access, false, proc.Id);
if (handle == IntPtr.Zero)
{
    Console.Error.WriteLine($"OpenProcess failed for PID {proc.Id}: 0x{Marshal.GetLastWin32Error():X8}");
    return 4;
}

var modules = GetModuleRanges(proc);
var best = new TopList<ScoredMatrix>(Math.Max(1, limit));
int regions = 0;
long scannedBytes = 0;
long matricesTested = 0;
bool suspended = false;

try
{
    if (suspendDuringScan)
    {
        int st = Native.NtSuspendProcess(handle);
        if (st != 0)
        {
            Console.Error.WriteLine($"NtSuspendProcess failed for PID {proc.Id}: 0x{st:X8}");
            return 5;
        }
        suspended = true;
    }

    foreach (MemoryRegion region in EnumerateReadableRegions(handle))
    {
        if (scanPrivateOnly && region.Type != MemoryType.Private) continue;
        if (scanImageOnly && region.Type != MemoryType.Image) continue;
        if (region.Size <= 0 || region.Size > DefaultMaxRegion) continue;
        regions++;
        scannedBytes += region.Size;
        ScanRegion(handle, region, modules, points, width, height, step, maxAbsMatrixValue, maxMeanError, best, ref matricesTested);
    }
}
finally
{
    if (suspended)
    {
        int st = Native.NtResumeProcess(handle);
        if (st != 0)
            Console.Error.WriteLine($"WARNING: NtResumeProcess failed for PID {proc.Id}: 0x{st:X8}.");
    }
    Native.CloseHandle(handle);
}

var matches = best.Items.OrderBy(m => m.MeanError).ThenBy(m => m.Address).ToList();
var report = new WorldToScreenScoutReport(
    DateTimeOffset.Now,
    "lab-only; point tags are approximate and candidates require live confirmation",
    proc.Id,
    proc.ProcessName,
    width,
    height,
    points,
    suspendDuringScan,
    scanPrivateOnly,
    scanImageOnly,
    step,
    regions,
    scannedBytes,
    matricesTested,
    matches);

Console.WriteLine("BattleWorldToScreenScout — read-only RAM matrix scan");
Console.WriteLine($"  process        : {proc.ProcessName} PID {proc.Id}");
Console.WriteLine($"  viewport       : {width}x{height}");
Console.WriteLine($"  points         : {points.Count} ({string.Join(", ", points.Select(p => p.Label))})");
Console.WriteLine($"  scan mode      : {(suspendDuringScan ? "suspended process during scan" : "live process")}");
Console.WriteLine($"  filter         : privateOnly={scanPrivateOnly} imageOnly={scanImageOnly} step={step} maxMeanError={maxMeanError:0.###}");
Console.WriteLine($"  regions/bytes  : {regions} / {scannedBytes:N0}");
Console.WriteLine($"  matrices       : {matricesTested:N0}");
Console.WriteLine($"  matches        : {matches.Count}");

foreach (ScoredMatrix m in matches.Take(limit))
{
    string rva = m.ModuleRva.HasValue ? $"+0x{m.ModuleRva.Value:X}" : "";
    Console.WriteLine($"  addr=0x{m.Address:X8} {m.ModuleName}{rva} {m.Convention} mean={m.MeanError:0.##} max={m.MaxError:0.##} spread={m.ProjectedSpread:0.##}");
    foreach (ProjectionHit h in m.Hits)
        Console.WriteLine($"    {h.Label,-8} target=({h.TargetX:0.#},{h.TargetY:0.#}) pred=({h.ScreenX:0.#},{h.ScreenY:0.#}) err={h.Error:0.#} ndc=({h.NdcX:0.###},{h.NdcY:0.###},{h.NdcZ:0.###}) w={h.W:0.###}");
    Console.WriteLine("    matrix=" + string.Join(",", m.Matrix.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
}

if (!string.IsNullOrWhiteSpace(jsonOut))
{
    string? dir = Path.GetDirectoryName(jsonOut);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

Console.WriteLine("VERDICT: candidates only. Confirm with a second camera/frame or a live watchpoint before promoting to Aurora runtime owner.");
return 0;

static List<TaggedPoint> LoadPoints(string path)
{
    var points = new List<TaggedPoint>();
    foreach (string raw in File.ReadLines(path))
    {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
        string[] p = line.Split(',', StringSplitOptions.TrimEntries);
        if (p.Length < 6) throw new InvalidOperationException($"bad point line: {line}");
        if (string.Equals(p[0], "label", StringComparison.OrdinalIgnoreCase)) continue;
        float weight = p.Length >= 7 ? float.Parse(p[6], CultureInfo.InvariantCulture) : 1f;
        points.Add(new TaggedPoint(
            p[0],
            float.Parse(p[1], CultureInfo.InvariantCulture),
            float.Parse(p[2], CultureInfo.InvariantCulture),
            float.Parse(p[3], CultureInfo.InvariantCulture),
            float.Parse(p[4], CultureInfo.InvariantCulture),
            float.Parse(p[5], CultureInfo.InvariantCulture),
            weight));
    }
    return points;
}

static void ScanRegion(
    IntPtr process,
    MemoryRegion region,
    IReadOnlyList<ModuleRange> modules,
    IReadOnlyList<TaggedPoint> points,
    int width,
    int height,
    int step,
    float maxAbsMatrixValue,
    float maxMeanError,
    TopList<ScoredMatrix> best,
    ref long matricesTested)
{
    long remaining = region.Size;
    long current = region.BaseAddress;
    byte[] carry = Array.Empty<byte>();

    while (remaining > 0)
    {
        int want = (int)Math.Min(DefaultChunkSize, remaining);
        byte[] buf = new byte[want + carry.Length];
        if (carry.Length > 0) Buffer.BlockCopy(carry, 0, buf, 0, carry.Length);

        if (!ReadProcessMemory(process, new IntPtr(current), buf, carry.Length, want, out int got) || got <= 0)
            break;

        int len = carry.Length + got;
        long bufBase = current - carry.Length;
        int alignedStep = Math.Max(4, step);
        float[] m = new float[16];
        for (int off = 0; off + 64 <= len; off += alignedStep)
        {
            bool ok = true;
            float maxAbs = 0;
            for (int i = 0; i < 16; i++)
            {
                float v = BitConverter.ToSingle(buf, off + i * 4);
                if (!Finite(v)) { ok = false; break; }
                maxAbs = Math.Max(maxAbs, Math.Abs(v));
                m[i] = v;
            }
            if (!ok || maxAbs < 0.0001f || maxAbs > maxAbsMatrixValue) continue;
            matricesTested++;

            long address = bufBase + off;
            TryScore(address, region, modules, m, "row-vector", points, width, height, maxMeanError, best, rowVector: true, flipY: true);
            TryScore(address, region, modules, m, "row-vector/noYflip", points, width, height, maxMeanError, best, rowVector: true, flipY: false);
            TryScore(address, region, modules, m, "column-vector", points, width, height, maxMeanError, best, rowVector: false, flipY: true);
            TryScore(address, region, modules, m, "column-vector/noYflip", points, width, height, maxMeanError, best, rowVector: false, flipY: false);
        }

        int carryLen = Math.Min(63, len);
        carry = new byte[carryLen];
        Buffer.BlockCopy(buf, len - carryLen, carry, 0, carryLen);
        current += got;
        remaining -= got;
    }
}

static void TryScore(
    long address,
    MemoryRegion region,
    IReadOnlyList<ModuleRange> modules,
    ReadOnlySpan<float> m,
    string convention,
    IReadOnlyList<TaggedPoint> points,
    int width,
    int height,
    float maxMeanError,
    TopList<ScoredMatrix> best,
    bool rowVector,
    bool flipY)
{
    var hits = new List<ProjectionHit>(points.Count);
    double weightedErr = 0;
    double totalWeight = 0;
    float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
    float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
    float? sign = null;

    foreach (TaggedPoint p in points)
    {
        Clip c = rowVector ? ProjectRow(m, p.WorldX, p.WorldY, p.WorldZ) : ProjectColumn(m, p.WorldX, p.WorldY, p.WorldZ);
        if (!Finite(c.X) || !Finite(c.Y) || !Finite(c.Z) || !Finite(c.W) || Math.Abs(c.W) < 1e-5f) return;
        float s = Math.Sign(c.W);
        sign ??= s;
        if (s != 0 && sign.Value != 0 && s != sign.Value) return;
        float ndcX = c.X / c.W;
        float ndcY = c.Y / c.W;
        float ndcZ = c.Z / c.W;
        if (!Finite(ndcX) || !Finite(ndcY) || !Finite(ndcZ)) return;
        if (Math.Abs(ndcX) > 4 || Math.Abs(ndcY) > 4 || Math.Abs(ndcZ) > 10) return;

        float sx = (ndcX + 1f) * 0.5f * width;
        float sy = flipY ? (1f - ndcY) * 0.5f * height : (ndcY + 1f) * 0.5f * height;
        if (!Finite(sx) || !Finite(sy)) return;
        if (sx < -width || sx > width * 2 || sy < -height || sy > height * 2) return;

        float err = Distance(sx, sy, p.ScreenX, p.ScreenY);
        weightedErr += err * Math.Max(0.01f, p.Weight);
        totalWeight += Math.Max(0.01f, p.Weight);
        minX = Math.Min(minX, sx); minY = Math.Min(minY, sy);
        maxX = Math.Max(maxX, sx); maxY = Math.Max(maxY, sy);
        hits.Add(new ProjectionHit(p.Label, p.ScreenX, p.ScreenY, sx, sy, err, ndcX, ndcY, ndcZ, c.W));
    }

    if (hits.Count != points.Count || totalWeight <= 0) return;
    float mean = (float)(weightedErr / totalWeight);
    float maxErr = hits.Max(h => h.Error);
    float spread = Distance(minX, minY, maxX, maxY);
    if (spread < 120f) return;
    if (mean > maxMeanError && !best.WouldAccept(mean)) return;

    ModuleRange? mod = modules.FirstOrDefault(r => address >= r.Base && address < r.Base + r.Size);
    best.Add(new ScoredMatrix(
        address,
        mod?.Name ?? RegionTypeLabel(region.Type),
        mod == null ? null : address - mod.Base,
        convention,
        mean,
        maxErr,
        spread,
        m.ToArray(),
        hits));
}

static Clip ProjectRow(ReadOnlySpan<float> m, float x, float y, float z)
{
    return new Clip(
        x * m[0] + y * m[4] + z * m[8] + m[12],
        x * m[1] + y * m[5] + z * m[9] + m[13],
        x * m[2] + y * m[6] + z * m[10] + m[14],
        x * m[3] + y * m[7] + z * m[11] + m[15]);
}

static Clip ProjectColumn(ReadOnlySpan<float> m, float x, float y, float z)
{
    return new Clip(
        m[0] * x + m[1] * y + m[2] * z + m[3],
        m[4] * x + m[5] * y + m[6] * z + m[7],
        m[8] * x + m[9] * y + m[10] * z + m[11],
        m[12] * x + m[13] * y + m[14] * z + m[15]);
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
            yield return new MemoryRegion(baseAddress, size, mbi.Protect, mbi.Type);

        long next = baseAddress + size;
        addr = next > addr ? next : addr + 0x10000;
    }
}

static IReadOnlyList<ModuleRange> GetModuleRanges(Process process)
{
    var ranges = new List<ModuleRange>();
    foreach (ProcessModule module in process.Modules)
        ranges.Add(new ModuleRange(module.ModuleName, module.BaseAddress.ToInt64(), module.ModuleMemorySize));
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

static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
static float Distance(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

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
    Console.WriteLine("  BattleWorldToScreenScout --points <csv> [--pid <pid>] [--width 2560] [--height 1440] [--step 16] [--limit 40] [--max-mean-error 220] [--private-only|--image-only] [--suspend] [--json <out.json>]");
    Console.WriteLine();
    Console.WriteLine("CSV columns:");
    Console.WriteLine("  label,worldX,worldY,worldZ,screenX,screenY,weight");
}

sealed class TopList<T>(int capacity) where T : ScoredMatrix
{
    private readonly List<T> _items = new();
    public IReadOnlyList<T> Items => _items;
    public bool WouldAccept(float meanError) => _items.Count < capacity || meanError < _items.Max(i => i.MeanError);
    public void Add(T item)
    {
        if (_items.Count < capacity)
        {
            _items.Add(item);
            return;
        }
        int worst = 0;
        for (int i = 1; i < _items.Count; i++)
            if (_items[i].MeanError > _items[worst].MeanError) worst = i;
        if (item.MeanError < _items[worst].MeanError)
            _items[worst] = item;
    }
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

sealed record MemoryRegion(long BaseAddress, long Size, MemoryProtect Protect, MemoryType Type);
sealed record ModuleRange(string Name, long Base, int Size);
sealed record Clip(float X, float Y, float Z, float W);
sealed record TaggedPoint(string Label, float WorldX, float WorldY, float WorldZ, float ScreenX, float ScreenY, float Weight);
sealed record ProjectionHit(string Label, float TargetX, float TargetY, float ScreenX, float ScreenY, float Error, float NdcX, float NdcY, float NdcZ, float W);
record ScoredMatrix(long Address, string ModuleName, long? ModuleRva, string Convention, float MeanError, float MaxError, float ProjectedSpread, float[] Matrix, IReadOnlyList<ProjectionHit> Hits);
sealed record WorldToScreenScoutReport(
    DateTimeOffset GeneratedAt,
    string Status,
    int ProcessId,
    string ProcessName,
    int Width,
    int Height,
    IReadOnlyList<TaggedPoint> Points,
    bool Suspended,
    bool PrivateOnly,
    bool ImageOnly,
    int Step,
    int RegionsScanned,
    long BytesScanned,
    long MatricesTested,
    IReadOnlyList<ScoredMatrix> Matches);

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
