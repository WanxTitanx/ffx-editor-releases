using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class ProcessMagicModuleResolver
{
    const uint Th32CsSnapModule = 0x00000008;
    const uint Th32CsSnapModule32 = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ModuleEntry32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Module32FirstW(IntPtr hSnapshot, ref ModuleEntry32W lpme);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Module32NextW(IntPtr hSnapshot, ref ModuleEntry32W lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    public sealed record MagicModule(int MagicId, uint Base, uint Size, string Name);

    public static IReadOnlyList<MagicModule> ListMagicModules()
    {
        var found = new List<MagicModule>();
        foreach (Process p in Process.GetProcessesByName("FFX"))
        {
            try
            {
                foreach (MagicModule m in EnumerateMagicModules((uint)p.Id))
                    found.Add(m);
            }
            catch { /* ignore */ }
        }
        return found;
    }

    public static string ListMagicDlls() =>
        string.Join(" | ", ListMagicModules().Select(m => $"{m.Name}@0x{m.Base:X8}+0x{m.Size:X}"));

    public static MagicModule? TryFindMagicModule(int magicId)
    {
        string want = $"magic_{magicId:D4}.dll";
        return ListMagicModules().FirstOrDefault(m => m.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
    }

    public static uint? TryFindMagicBase(int magicId) => TryFindMagicModule(magicId)?.Base;

    static IEnumerable<MagicModule> EnumerateMagicModules(uint pid)
    {
        IntPtr snap = CreateToolhelp32Snapshot(Th32CsSnapModule | Th32CsSnapModule32, pid);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            yield break;

        try
        {
            var me = new ModuleEntry32W { dwSize = (uint)Marshal.SizeOf<ModuleEntry32W>() };
            if (!Module32FirstW(snap, ref me))
                yield break;

            do
            {
                if (!me.szModule.StartsWith("magic_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!me.szModule.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!int.TryParse(me.szModule.AsSpan(6, 4), out int magicId))
                    continue;
                yield return new MagicModule(magicId, (uint)me.modBaseAddr, me.modBaseSize, me.szModule);
            }
            while (Module32NextW(snap, ref me));
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    public static string DescribeFfxProcesses()
    {
        var sb = new StringBuilder();
        foreach (Process p in Process.GetProcessesByName("FFX"))
            sb.AppendLine($"FFX pid={p.Id} mainModule={p.MainModule?.FileName ?? "?"}");
        return sb.Length == 0 ? "(no FFX process)" : sb.ToString().TrimEnd();
    }
}
