using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SphereGridRt2Lab;

internal static class Program
{
    const uint ProcessVmRead = 0x0010;
    const uint ProcessVmWrite = 0x0020;
    const uint ProcessVmOperation = 0x0008;
    const uint ProcessQueryInformation = 0x0400;
    const long IdaImageBase = 0x0040_0000;
    const long StaticPanelPointerVar = 0x01A8_60E0;
    const int LocalizedHeaderLength = 0x14;
    const int LearnedMoveOffset = 0x12;

    static int Main(string[] args)
    {
        Rt2Options options = Rt2Options.Parse(args);
        string repoRoot = ResolveRepoRoot();
        string reportPath = Path.Combine(repoRoot, "work", "reverse", "spheregrid_rt2_live_report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        Process? process = Process.GetProcessesByName("FFX").FirstOrDefault();
        if (process == null)
        {
            Console.Error.WriteLine("FFX.exe process not found.");
            return 2;
        }

        IntPtr handle = Native.OpenProcess(
            ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryInformation,
            false,
            process.Id);
        if (handle == IntPtr.Zero)
        {
            Console.Error.WriteLine($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
            return 3;
        }

        bool mutated = false;
        ushort originalValue = 0;
        long learnedMoveAddress = 0;
        Rt2Report? report = null;

        try
        {
            long runtimeBase = process.MainModule?.BaseAddress.ToInt64()
                ?? throw new InvalidOperationException("Could not resolve FFX.exe base address.");
            long delta = runtimeBase - IdaImageBase;
            long runtimePanelPointerVar = StaticPanelPointerVar + delta;
            uint panelPointer = ReadU32(handle, runtimePanelPointerVar);

            byte[] panelFile = File.ReadAllBytes(options.PanelPath);
            if (!TryReadLocalizedHeader(panelFile, out LocalizedHeader header))
                throw new InvalidDataException($"Bad panel localized header: {options.PanelPath}");
            if (options.EntryIndex < header.MinIndex || options.EntryIndex > header.MaxIndex)
                throw new ArgumentOutOfRangeException(nameof(options.EntryIndex), "Entry index is outside panel header bounds.");

            byte[] runtimePanel = ReadMemory(handle, panelPointer, panelFile.Length);
            string runtimeSha = Sha(runtimePanel);
            string fileSha = Sha(panelFile);
            if (!runtimePanel.SequenceEqual(panelFile))
                throw new InvalidOperationException("Runtime panel buffer does not match the supplied panel.bin exactly.");

            int relativeIndex = options.EntryIndex - header.MinIndex;
            int fileEntryOffset = LocalizedHeaderLength + relativeIndex * header.EntryLength;
            int fileFieldOffset = fileEntryOffset + LearnedMoveOffset;
            learnedMoveAddress = panelPointer + fileFieldOffset;
            originalValue = ReadU16(handle, learnedMoveAddress);

            if (options.ExpectedOriginal.HasValue && originalValue != options.ExpectedOriginal.Value)
            {
                throw new InvalidOperationException(
                    $"Expected original {Hex(options.ExpectedOriginal.Value)}, got {Hex(originalValue)} at {Hex(learnedMoveAddress)}.");
            }

            report = new Rt2Report
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Verdict = options.RestoreOnly ? "RESTORE_ONLY_PENDING" : "MUTATED_HELD_THEN_RESTORED",
                Process = new Rt2Process
                {
                    Id = process.Id,
                    ProcessName = process.ProcessName,
                    Path = process.MainModule?.FileName ?? string.Empty,
                    BaseAddress = Hex(runtimeBase),
                    IdaImageBase = Hex(IdaImageBase),
                    Delta = Hex(delta)
                },
                Panel = new Rt2Panel
                {
                    Path = options.PanelPath,
                    RuntimePointerVar = Hex(runtimePanelPointerVar),
                    BufferPointer = Hex(panelPointer),
                    RuntimeSha256 = runtimeSha,
                    FileSha256 = fileSha,
                    HeaderMinIndex = header.MinIndex,
                    HeaderMaxIndex = header.MaxIndex,
                    HeaderEntryLength = header.EntryLength
                },
                Mutation = new Rt2Mutation
                {
                    Field = "panel.bin LearnedMove +0x12",
                    EntryIndex = options.EntryIndex,
                    EntryOffset = Hex(fileEntryOffset),
                    FileFieldOffset = Hex(fileFieldOffset),
                    RuntimeAddress = Hex(learnedMoveAddress),
                    OriginalValue = Hex(originalValue),
                    MutatedValue = Hex(options.NewValue),
                    ExpectedObservation = options.ExpectedObservation,
                    HoldMilliseconds = options.HoldMilliseconds,
                    MutatedVerified = false,
                    Restored = false,
                    RestoredValue = string.Empty
                }
            };

            WriteU16(handle, learnedMoveAddress, options.NewValue);
            ushort verifiedMutation = ReadU16(handle, learnedMoveAddress);
            if (verifiedMutation != options.NewValue)
                throw new InvalidOperationException($"Mutation verify failed: got {Hex(verifiedMutation)}.");

            report.Mutation.MutatedVerified = true;
            if (options.RestoreOnly)
            {
                report.Verdict = "RESTORE_ONLY_WRITTEN";
                report.Mutation.Restored = true;
                report.Mutation.RestoredValue = Hex(verifiedMutation);
                WriteReport(reportPath, report);
                Console.WriteLine($"RESTORE_ONLY {Hex(learnedMoveAddress)} -> {Hex(verifiedMutation)}");
                return 0;
            }

            mutated = true;
            WriteReport(reportPath, report);
            Console.WriteLine($"MUTATED {Hex(learnedMoveAddress)} {Hex(originalValue)} -> {Hex(options.NewValue)} hold={options.HoldMilliseconds}ms");
            Console.Out.Flush();

            Thread.Sleep(options.HoldMilliseconds);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (report == null)
            {
                report = new Rt2Report
                {
                    GeneratedUtc = DateTimeOffset.UtcNow,
                    Verdict = "FAILED",
                    Error = ex.Message,
                    Process = new Rt2Process { Id = process.Id, ProcessName = process.ProcessName },
                    Panel = new Rt2Panel { Path = options.PanelPath },
                    Mutation = new Rt2Mutation
                    {
                        Field = "panel.bin LearnedMove +0x12",
                        EntryIndex = options.EntryIndex,
                        MutatedValue = Hex(options.NewValue),
                        ExpectedObservation = options.ExpectedObservation,
                        HoldMilliseconds = options.HoldMilliseconds
                    }
                };
            }

            report.Error = ex.Message;
            report.Verdict = mutated ? "FAILED_AFTER_MUTATION" : "FAILED_BEFORE_MUTATION";
            WriteReport(reportPath, report);
            return 4;
        }
        finally
        {
            try
            {
                if (mutated)
                {
                    WriteU16(handle, learnedMoveAddress, originalValue);
                    ushort restored = ReadU16(handle, learnedMoveAddress);
                    if (report != null)
                    {
                        report.GeneratedUtc = DateTimeOffset.UtcNow;
                        report.Mutation.Restored = restored == originalValue;
                        report.Mutation.RestoredValue = Hex(restored);
                        if (string.IsNullOrWhiteSpace(report.Error))
                            report.Verdict = restored == originalValue ? "RESTORED" : "RESTORE_VERIFY_FAILED";
                        WriteReport(reportPath, report);
                    }

                    Console.WriteLine($"RESTORED {Hex(learnedMoveAddress)} -> {Hex(restored)}");
                }
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }
    }

    static bool TryReadLocalizedHeader(byte[] bytes, out LocalizedHeader header)
    {
        header = new LocalizedHeader();
        if (bytes.Length < LocalizedHeaderLength)
            return false;

        ushort minIndex = BitConverter.ToUInt16(bytes, 0x08);
        ushort maxIndex = BitConverter.ToUInt16(bytes, 0x0A);
        ushort entryLength = BitConverter.ToUInt16(bytes, 0x0C);
        ushort dataLength = BitConverter.ToUInt16(bytes, 0x0E);
        if (maxIndex < minIndex || entryLength == 0)
            return false;

        header = new LocalizedHeader
        {
            MinIndex = minIndex,
            MaxIndex = maxIndex,
            EntryLength = entryLength,
            DataLength = dataLength
        };
        return true;
    }

    static byte[] ReadMemory(IntPtr processHandle, long address, int size)
    {
        byte[] buffer = new byte[size];
        if (!Native.ReadProcessMemory(processHandle, new IntPtr(address), buffer, size, out IntPtr read)
            || read.ToInt64() != size)
        {
            throw new InvalidOperationException(
                $"ReadProcessMemory failed at {Hex(address)} size={size} read={read.ToInt64()} err={Marshal.GetLastWin32Error()}");
        }

        return buffer;
    }

    static uint ReadU32(IntPtr processHandle, long address) =>
        BitConverter.ToUInt32(ReadMemory(processHandle, address, 4));

    static ushort ReadU16(IntPtr processHandle, long address) =>
        BitConverter.ToUInt16(ReadMemory(processHandle, address, 2));

    static void WriteU16(IntPtr processHandle, long address, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (!Native.WriteProcessMemory(processHandle, new IntPtr(address), bytes, bytes.Length, out IntPtr written)
            || written.ToInt64() != bytes.Length)
        {
            throw new InvalidOperationException(
                $"WriteProcessMemory failed at {Hex(address)} size={bytes.Length} written={written.ToInt64()} err={Marshal.GetLastWin32Error()}");
        }
    }

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

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static string Hex(long value) => "0x" + value.ToString("X8");

    static string Hex(ushort value) => "0x" + value.ToString("X4");

    static void WriteReport(string path, Rt2Report report)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed class Rt2Options
    {
        public string PanelPath { get; set; } = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\new_uspc\battle\kernel\panel.bin";
        public int EntryIndex { get; set; } = 54;
        public ushort? ExpectedOriginal { get; set; } = 0x3012;
        public ushort NewValue { get; set; } = 0x3011;
        public int HoldMilliseconds { get; set; } = 45_000;
        public string ExpectedObservation { get; set; } = "Selected Armor Break ability node should display Magic Break while mutation is held, then return after restore.";
        public bool RestoreOnly { get; set; }

        public static Rt2Options Parse(string[] args)
        {
            var options = new Rt2Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next()
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"Missing value for {arg}");
                    return args[++i];
                }

                switch (arg)
                {
                    case "--panel":
                        options.PanelPath = Next();
                        break;
                    case "--index":
                        options.EntryIndex = ParseInt(Next());
                        break;
                    case "--expected":
                        string expected = Next();
                        options.ExpectedOriginal = expected.Equals("any", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : unchecked((ushort)ParseInt(expected));
                        break;
                    case "--new":
                        options.NewValue = unchecked((ushort)ParseInt(Next()));
                        break;
                    case "--hold-ms":
                        options.HoldMilliseconds = ParseInt(Next());
                        break;
                    case "--expected-observation":
                        options.ExpectedObservation = Next();
                        break;
                    case "--restore-only":
                        options.RestoreOnly = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (options.HoldMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(options.HoldMilliseconds));
            return options;
        }

        static int ParseInt(string value)
        {
            value = value.Replace("_", string.Empty, StringComparison.Ordinal);
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(value[2..], 16)
                : int.Parse(value);
        }
    }

    sealed class LocalizedHeader
    {
        public ushort MinIndex { get; set; }
        public ushort MaxIndex { get; set; }
        public ushort EntryLength { get; set; }
        public ushort DataLength { get; set; }
    }

    sealed class Rt2Report
    {
        public DateTimeOffset GeneratedUtc { get; set; }
        public string Verdict { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public Rt2Process Process { get; set; } = new();
        public Rt2Panel Panel { get; set; } = new();
        public Rt2Mutation Mutation { get; set; } = new();
    }

    sealed class Rt2Process
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string BaseAddress { get; set; } = string.Empty;
        public string IdaImageBase { get; set; } = string.Empty;
        public string Delta { get; set; } = string.Empty;
    }

    sealed class Rt2Panel
    {
        public string Path { get; set; } = string.Empty;
        public string RuntimePointerVar { get; set; } = string.Empty;
        public string BufferPointer { get; set; } = string.Empty;
        public string RuntimeSha256 { get; set; } = string.Empty;
        public string FileSha256 { get; set; } = string.Empty;
        public int HeaderMinIndex { get; set; }
        public int HeaderMaxIndex { get; set; }
        public int HeaderEntryLength { get; set; }
    }

    sealed class Rt2Mutation
    {
        public string Field { get; set; } = string.Empty;
        public int EntryIndex { get; set; }
        public string EntryOffset { get; set; } = string.Empty;
        public string FileFieldOffset { get; set; } = string.Empty;
        public string RuntimeAddress { get; set; } = string.Empty;
        public string OriginalValue { get; set; } = string.Empty;
        public string MutatedValue { get; set; } = string.Empty;
        public string ExpectedObservation { get; set; } = string.Empty;
        public int HoldMilliseconds { get; set; }
        public bool MutatedVerified { get; set; }
        public bool Restored { get; set; }
        public string RestoredValue { get; set; } = string.Empty;
    }

    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            [Out] byte[] buffer,
            int size,
            out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            out IntPtr bytesWritten);
    }
}
