using System.Diagnostics;
using System.Runtime.InteropServices;

// AbilityRuntimeProbe — RT2 probe do a_ability na RAM do FFX.exe (HD Remaster, x86, ASLR).
// Espelha RuntimeTools/SphereGridRuntimeProbe (mecanica RPM aprovada).
// Caminho: dword_112A944 -> buffer a_ability; entry[i] = base + 0x14 + i*0x6C.
// Mascaras OR-acumuladas no char struct unk_113205C (stride 148): word37@0x4A(<-0x62), word38@0x4C(<-0x64),
//   word39@0x4E(<-word@0x66, cujo byte ALTO e o campo +0x67).
//
// MODOS (READ-only por padrao; escrita exige flag explicita):
//   (sem args) | read   -> dump a_ability (+0x67 hist, word@0x66 hist) + char structs (w37/w38/w39 por slot)
//   arm [hex]           -> ESCREVE +0x67 = hex (default 0x80) em todas as 134 entries; verifica read-back
//   restore             -> ESCREVE +0x67 = 0x00 em todas as entries; verifica read-back
// RT2: arm -> (usuario entra em batalha / re-equipa) -> read (w39 deve virar 0x__00) -> restore.

namespace AbilityRuntimeProbe;

internal static class Program
{
    const uint PROCESS_VM_READ = 0x0010;
    const uint PROCESS_VM_WRITE = 0x0020;
    const uint PROCESS_VM_OPERATION = 0x0008;
    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const long IdaImageBase = 0x0040_0000;
    const long AbilityPointerVar = 0x0112_A944;
    const long CharArrayVar = 0x0113_205C;
    const int CharStride = 148;
    const int HeaderLen = 0x14;
    const int EntryLen = 0x6C;
    const int FieldOff = 0x67; // alvo: byte +0x67 (hi-byte da word@0x66)

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "read";
        bool write = mode is "arm" or "restore";

        Process? proc = Process.GetProcessesByName("FFX").FirstOrDefault();
        if (proc == null) { Console.Error.WriteLine("FFX.exe nao encontrado."); return 2; }

        uint access = PROCESS_VM_READ | PROCESS_QUERY_INFORMATION;
        if (write) access |= PROCESS_VM_WRITE | PROCESS_VM_OPERATION;
        IntPtr h = Native.OpenProcess(access, false, proc.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine($"OpenProcess falhou err={Marshal.GetLastWin32Error()}"); return 3; }

        try
        {
            long baseAddr = proc.MainModule!.BaseAddress.ToInt64();
            long delta = baseAddr - IdaImageBase;
            Console.WriteLine($"pid={proc.Id} base=0x{baseAddr:X} delta=0x{delta:X} mode={mode}");

            uint bufBase = ReadU32(h, AbilityPointerVar + delta);
            Console.WriteLine($"a_ability buffer=0x{bufBase:X}");
            if (bufBase == 0) { Console.WriteLine(">>> NULL: a_ability nao carregada. Entre numa batalha / carregue save."); return 0; }

            byte[] hdr = ReadMem(h, bufBase, HeaderLen);
            int min = U16(hdr, 0x08), max = U16(hdr, 0x0A), elen = U16(hdr, 0x0C);
            int cnt = max + 1 - min;
            Console.WriteLine($"HEADER: min={min} max={max} entryLen=0x{elen:X} count={cnt}");
            if (elen != EntryLen) { Console.WriteLine($">>> entryLen inesperado 0x{elen:X}; abortando."); return 0; }

            if (mode is "arm" or "restore")
            {
                byte val = mode == "restore" ? (byte)0x00
                    : (byte)(args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x80);
                Console.WriteLine($">>> {(mode == "restore" ? "RESTORE" : "ARM")}: escrevendo +0x{FieldOff:X2} = 0x{val:X2} em {cnt} entries...");
                int ok = 0;
                for (int i = 0; i < cnt; i++)
                    if (WriteByte(h, bufBase + HeaderLen + (long)i * elen + FieldOff, val)) ok++;
                Console.WriteLine($"    escritas OK: {ok}/{cnt}");
            }

            // ---- leitura: +0x67 hist + word@0x66 hist ----
            var h67 = new SortedDictionary<int, int>();
            var w66 = new Dictionary<int, int>();
            var nz = new List<string>();
            for (int i = 0; i < cnt; i++)
            {
                byte[] e = ReadMem(h, bufBase + HeaderLen + (long)i * elen, EntryLen);
                int b67 = e[FieldOff], word66 = U16(e, 0x66);
                h67[b67] = h67.GetValueOrDefault(b67) + 1;
                w66[word66] = w66.GetValueOrDefault(word66) + 1;
                if (b67 != 0) nz.Add($"idx{i}=0x{b67:X2}");
            }
            Console.WriteLine("+0x67 byte histograma:");
            foreach (var kv in h67) Console.WriteLine($"  0x{kv.Key:X2} x{kv.Value}");
            Console.WriteLine("word@0x66 histograma (top 14):");
            foreach (var kv in w66.OrderByDescending(k => k.Value).Take(14)) Console.WriteLine($"  0x{kv.Key:X4} x{kv.Value}");

            // ---- char structs: word[37/38/39] = agregados de 0x62/0x64/0x66 ----
            long charArr = CharArrayVar + delta;
            Console.WriteLine($"char structs @0x{charArr:X} (stride {CharStride}):");
            Console.WriteLine("  slot  curHP   maxHP   ab45 ab46  w37@4A  w38@4C  w39@4E");
            for (int s = 0; s < 8; s++)
            {
                byte[] c = ReadMem(h, charArr + (long)s * CharStride, CharStride);
                Console.WriteLine($"  {s,4}  {BitConverter.ToUInt32(c, 0x1C),6}  {BitConverter.ToUInt32(c, 0x24),6}  0x{c[0x2D]:X2} 0x{c[0x2E]:X2}  0x{U16(c, 0x4A):X4}  0x{U16(c, 0x4C):X4}  0x{U16(c, 0x4E):X4}");
            }
            return 0;
        }
        finally { Native.CloseHandle(h); }
    }

    static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);

    static byte[] ReadMem(IntPtr h, long addr, int size)
    {
        var buf = new byte[size];
        if (!Native.ReadProcessMemory(h, new IntPtr(addr), buf, size, out IntPtr r) || r.ToInt64() != size)
            throw new InvalidOperationException($"RPM falhou @0x{addr:X} size={size} read={r.ToInt64()} err={Marshal.GetLastWin32Error()}");
        return buf;
    }

    static uint ReadU32(IntPtr h, long addr) => BitConverter.ToUInt32(ReadMem(h, addr, 4), 0);

    static bool WriteByte(IntPtr h, long addr, byte val)
    {
        byte[] b = { val };
        return Native.WriteProcessMemory(h, new IntPtr(addr), b, 1, out IntPtr w) && w.ToInt64() == 1;
    }

    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint a, bool i, int p);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
    }
}
