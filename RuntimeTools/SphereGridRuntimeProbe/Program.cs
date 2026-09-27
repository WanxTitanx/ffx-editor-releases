using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SphereGridRuntimeProbe;

internal static class Program
{
    const uint ProcessVmRead = 0x0010;
    const uint ProcessQueryInformation = 0x0400;
    const long IdaImageBase = 0x0040_0000;
    const long StaticSpherePointerVar = 0x01A8_60E4;
    const long StaticPanelPointerVar = 0x01A8_60E0;
    const long StaticGridRuntimeArray = 0x0112_EC7C;
    const int GridRuntimeArraySize = 0x1320;
    const int ContentsPayloadOffset = 0x08;

    static int Main(string[] args)
    {
        string repoRoot = ResolveRepoRoot();
        string masterRoot = args.Length > 0
            ? args[0]
            : @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";

        Process? process = Process.GetProcessesByName("FFX").FirstOrDefault();
        if (process == null)
        {
            Console.Error.WriteLine("FFX.exe process not found.");
            return 2;
        }

        IntPtr handle = Native.OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            Console.Error.WriteLine($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
            return 3;
        }

        try
        {
            long runtimeBase = process.MainModule?.BaseAddress.ToInt64()
                ?? throw new InvalidOperationException("Could not resolve FFX.exe base address.");
            long delta = runtimeBase - IdaImageBase;
            List<CorpusFile> corpus = LoadCorpus(masterRoot);
            List<ContentPair> contentPairs = LoadContentPairs(masterRoot);

            var result = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                process = new
                {
                    process.Id,
                    process.ProcessName,
                    baseAddress = Hex(runtimeBase),
                    idaImageBase = Hex(IdaImageBase),
                    delta = Hex(delta),
                    path = process.MainModule?.FileName
                },
                corpusRoot = masterRoot,
                probes = new[]
                {
                    ProbeBuffer(handle, corpus, "sphere.bin", StaticSpherePointerVar, delta),
                    ProbeBuffer(handle, corpus, "panel.bin", StaticPanelPointerVar, delta)
                },
                gridRuntimeArray = ProbeGridRuntimeArray(handle, contentPairs, StaticGridRuntimeArray, delta)
            };

            string outputDir = Path.Combine(repoRoot, "work", "reverse");
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "spheregrid_runtime_buffer_probe.json");
            File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(outputPath));
            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    static object ProbeBuffer(IntPtr processHandle, IReadOnlyList<CorpusFile> corpus, string family, long staticPointerVar, long delta)
    {
        long runtimePointerVar = staticPointerVar + delta;
        uint bufferPointer = ReadU32(processHandle, runtimePointerVar);

        List<object> matches = new();
        foreach (CorpusFile file in corpus.Where(item => item.Family.Equals(family, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                byte[] memory = ReadMemory(processHandle, bufferPointer, file.Bytes.Length);
                bool same = memory.SequenceEqual(file.Bytes);
                matches.Add(new
                {
                    file = file.Path,
                    length = file.Bytes.Length,
                    same,
                    memorySha256 = Sha(memory),
                    file.Sha256
                });
            }
            catch (Exception ex)
            {
                matches.Add(new
                {
                    file = file.Path,
                    length = file.Bytes.Length,
                    same = false,
                    error = ex.Message
                });
                break;
            }
        }

        return new
        {
            family,
            staticPointerVar = Hex(staticPointerVar),
            runtimePointerVar = Hex(runtimePointerVar),
            bufferPointer = Hex(bufferPointer),
            exactMatches = matches.Where(item => (bool)item.GetType().GetProperty("same")!.GetValue(item)!).ToArray(),
            sampleNonMatches = matches.Where(item => !(bool)item.GetType().GetProperty("same")!.GetValue(item)!).Take(5).ToArray()
        };
    }

    static List<CorpusFile> LoadCorpus(string masterRoot)
    {
        if (!Directory.Exists(masterRoot))
            throw new DirectoryNotFoundException(masterRoot);

        return Directory.EnumerateFiles(masterRoot, "*.bin", SearchOption.AllDirectories)
            .Where(path =>
            {
                string name = Path.GetFileName(path);
                return name.Equals("sphere.bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("panel.bin", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                return new CorpusFile(Path.GetFileName(path), path, bytes, Sha(bytes));
            })
            .ToList();
    }

    static List<ContentPair> LoadContentPairs(string masterRoot)
    {
        var result = new List<ContentPair>();
        foreach (string localeDir in Directory.EnumerateDirectories(masterRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string locale = Path.GetFileName(localeDir);
            string abmapDir = Path.Combine(localeDir, "menu", "abmap");
            foreach (var pair in new[]
            {
                new { Kind = "original", Layout = "dat01.dat", Contents = "dat09.dat" },
                new { Kind = "standard", Layout = "dat02.dat", Contents = "dat10.dat" },
                new { Kind = "expert", Layout = "dat03.dat", Contents = "dat11.dat" }
            })
            {
                string layoutPath = Path.Combine(abmapDir, pair.Layout);
                string contentsPath = Path.Combine(abmapDir, pair.Contents);
                if (!File.Exists(layoutPath) || !File.Exists(contentsPath))
                    continue;

                byte[] layout = File.ReadAllBytes(layoutPath);
                byte[] contents = File.ReadAllBytes(contentsPath);
                if (layout.Length < 0x10 || contents.Length < ContentsPayloadOffset)
                    continue;

                ushort nodeCount = BitConverter.ToUInt16(layout, 0x04);
                if (contents.Length < ContentsPayloadOffset + nodeCount)
                    continue;

                byte[] payload = contents.Skip(ContentsPayloadOffset).Take(nodeCount).ToArray();
                result.Add(new ContentPair(locale, pair.Kind, layoutPath, contentsPath, nodeCount, payload, Sha(payload)));
            }
        }

        return result;
    }

    static object ProbeGridRuntimeArray(IntPtr processHandle, IReadOnlyList<ContentPair> contentPairs, long staticArrayAddress, long delta)
    {
        long runtimeAddress = staticArrayAddress + delta;
        byte[] raw = ReadMemory(processHandle, runtimeAddress, GridRuntimeArraySize);
        byte[] lowBytes = new byte[raw.Length / 2];
        byte[] highBytes = new byte[raw.Length / 2];
        for (int i = 0; i < lowBytes.Length; i++)
        {
            lowBytes[i] = raw[i * 2];
            highBytes[i] = raw[i * 2 + 1];
        }

        var matches = new List<object>();
        foreach (ContentPair pair in contentPairs)
        {
            byte[] runtimeSlice = lowBytes.Take(pair.NodeCount).ToArray();
            int equal = runtimeSlice.Zip(pair.Payload, (left, right) => left == right ? 1 : 0).Sum();
            matches.Add(new
            {
                pair.Locale,
                pair.Kind,
                pair.LayoutPath,
                pair.ContentsPath,
                pair.NodeCount,
                exact = runtimeSlice.SequenceEqual(pair.Payload),
                equalBytes = equal,
                pair.PayloadSha256,
                runtimePayloadSha256 = Sha(runtimeSlice)
            });
        }

        return new
        {
            staticArrayAddress = Hex(staticArrayAddress),
            runtimeAddress = Hex(runtimeAddress),
            size = GridRuntimeArraySize,
            lowByteNonZero = lowBytes.Count(value => value != 0),
            highByteNonZero = highBytes.Count(value => value != 0),
            exactMatches = matches.Where(item => (bool)item.GetType().GetProperty("exact")!.GetValue(item)!).ToArray(),
            rankedMatches = matches
                .OrderByDescending(item => (int)item.GetType().GetProperty("equalBytes")!.GetValue(item)!)
                .Take(6)
                .ToArray()
        };
    }

    static byte[] ReadMemory(IntPtr processHandle, long address, int size)
    {
        if (address == 0)
            throw new InvalidOperationException("runtime pointer is null");

        byte[] buffer = new byte[size];
        if (!Native.ReadProcessMemory(processHandle, new IntPtr(address), buffer, size, out IntPtr read)
            || read.ToInt64() != size)
        {
            throw new InvalidOperationException(
                $"ReadProcessMemory failed at {Hex(address)} size={size} read={read.ToInt64()} err={Marshal.GetLastWin32Error()}");
        }

        return buffer;
    }

    static uint ReadU32(IntPtr processHandle, long address)
    {
        byte[] bytes = ReadMemory(processHandle, address, 4);
        return BitConverter.ToUInt32(bytes, 0);
    }

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static string Hex(long value) => $"0x{value:X8}";

    static string Hex(uint value) => $"0x{value:X8}";

    static string ResolveRepoRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FFXProjectEditor.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    sealed record CorpusFile(string Family, string Path, byte[] Bytes, string Sha256);

    sealed record ContentPair(
        string Locale,
        string Kind,
        string LayoutPath,
        string ContentsPath,
        ushort NodeCount,
        byte[] Payload,
        string PayloadSha256);

    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
