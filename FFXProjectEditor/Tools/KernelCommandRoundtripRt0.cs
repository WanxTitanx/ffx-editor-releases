using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Modules.BattleKernel.Commands;

namespace FFXProjectEditor.Tools
{
    // Headless gate for the EXACT path the GUI uses when you open Battle Commands / Items / Monster
    // Commands 1/2: ReadList -> KernelCommands_Wrapper.Wrap -> Unwrap -> WriteList. The AbilityCommandLab
    // gate only proves ReadList->WriteList (the direct path) and so MISSED a real crash: the wrapper had
    // stale field names (unusedText1/2*) + no Original*Offset, so PropertyUtil.CopyProperties dropped those
    // fields on Wrap/Unwrap -> WriteList lost the preserve-only state, fell to the append rebuild, and NPE'd
    // on monmagic's empty JP-only text fields the moment the module was opened.
    //
    // This gate asserts: the wrapper round-trip (a) does not throw and (b) is byte-identical to the direct
    // write. Run via: FFXProjectEditor.exe --kernelcmd-roundtrip [kernelDir]
    internal static class KernelCommandRoundtripRt0
    {
        public static int Run(string kernelDir)
        {
            Console.WriteLine("=== Kernel command WRAP/UNWRAP round-trip RT0 (the GUI open/save path) ===");
            Console.WriteLine($"dir : {kernelDir}");

            // (file, hasExtraInfo). Command/Item carry the 0x60 extra block; MonMagic does not (0x5C).
            (string file, bool hasExtra)[] targets =
            {
                ("command.bin", true),
                ("item.bin", true),
                ("monmagic1.bin", false),
                ("monmagic2.bin", false),
            };

            int fail = 0;
            foreach ((string file, bool hasExtra) in targets)
            {
                string path = Path.Combine(kernelDir, file);
                if (!File.Exists(path)) { Console.WriteLine($"  {file,-14}: SKIP (not found)"); continue; }

                byte[] orig;
                try { orig = File.ReadAllBytes(path); }
                catch (Exception ex) { Console.WriteLine($"  {file,-14}: READ FAIL ({ex.Message})"); fail++; continue; }

                try
                {
                    List<Ability_Command> list = Ability_Command.ReadList(orig, hasExtra);
                    byte[] direct = Ability_Command.WriteList(list, hasExtra);

                    // The exact GUI transform: every command goes through the Avalonia wrapper and back.
                    List<Ability_Command> roundTripped = list
                        .Select(c => KernelCommands_Wrapper.Wrap(c).Unwrap())
                        .ToList();
                    byte[] viaWrapper = Ability_Command.WriteList(roundTripped, hasExtra);

                    bool identical = direct.Length == viaWrapper.Length && direct.AsSpan().SequenceEqual(viaWrapper);
                    if (!identical)
                    {
                        int d = 0, n = Math.Min(direct.Length, viaWrapper.Length);
                        while (d < n && direct[d] == viaWrapper[d]) d++;
                        Console.WriteLine($"  {file,-14}: DRIFT @0x{d:X} (direct {direct.Length}B vs wrapper {viaWrapper.Length}B) — wrapper round-trip is lossy");
                        fail++;
                    }
                    else
                    {
                        Console.WriteLine($"  {file,-14}: PASS ({list.Count} cmds, {direct.Length}B, wrapper round-trip byte-identical)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {file,-14}: THREW {ex.GetType().Name}: {ex.Message} (this is the open-module crash)");
                    fail++;
                }
            }

            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - every kernel command file survives the GUI Wrap/Unwrap/Write path byte-identically."
                : $"VERDICT: FAIL - {fail} file(s) broke the wrapper round-trip.");
            return fail == 0 ? 0 : 1;
        }
    }
}
