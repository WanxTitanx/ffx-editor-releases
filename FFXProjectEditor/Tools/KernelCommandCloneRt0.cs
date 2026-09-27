using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Modules.BattleKernel.Commands;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Proves clone preserves every stat-sheet field through the GUI Unwrap path and WriteList reload.
    /// Run: FFXProjectEditor.exe --kernelcmd-clone-rt0 [path\to\command.bin] [donorIndex]
    /// </summary>
    internal static class KernelCommandCloneRt0
    {
        const int AbilitySliceOffset = 0x10;
        const int AbilitySliceLength = 0x50; // AbilityInfo + ExtraCommandInfo in a 0x60 command row

        public static int Run(string? path, int donorIndex)
        {
            path ??= @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";
            Console.WriteLine("=== Kernel command CLONE fidelity RT0 ===");
            Console.WriteLine($"file : {path}");
            Console.WriteLine($"donor: #{donorIndex}");

            if (!File.Exists(path))
            {
                Console.WriteLine("SKIP — file not found.");
                return 1;
            }

            byte[] orig = File.ReadAllBytes(path);
            List<Ability_Command> list = Ability_Command.ReadList(orig, hasExtraInfo: true);
            if (donorIndex < 0 || donorIndex >= list.Count)
            {
                Console.WriteLine($"FAIL — donor index out of range (count={list.Count}).");
                return 1;
            }

            Ability_Command donor = list[donorIndex];
            int fail = 0;
            fail += Check("CloneDeep (direct)", donor, () => donor.CloneDeep(hasExtraInfo: true));

            Ability_Command unwrapped = KernelCommands_Wrapper.Wrap(donor).Unwrap();
            fail += Check("CloneDeep (via Wrap/Unwrap donor)", donor, () => unwrapped.CloneDeep(hasExtraInfo: true));

            List<Ability_Command> guiList = list.Select(c => KernelCommands_Wrapper.Wrap(c).Unwrap()).ToList();
            KernelCommandListMutator.AppendClone(guiList, donorIndex, hasExtraInfo: true);
            Ability_Command inMemoryClone = guiList[^1];
            fail += CompareAbilitySlice("In-memory AppendClone (no WriteList reload)", donor, inMemoryClone);

            byte[] written = Ability_Command.WriteList(guiList, hasExtraInfo: true);
            Ability_Command reloaded = Ability_Command.ReadList(written, hasExtraInfo: true)[^1];
            fail += CompareAbilitySlice("WriteList reload (AppendClone path)", donor, reloaded);
            fail += CompareFields("WriteList reload fields", donor, reloaded);

            Console.WriteLine(fail == 0
                ? "VERDICT: PASS — clone preserves stat sheet through GUI path."
                : $"VERDICT: FAIL — {fail} check(s) drifted.");
            return fail == 0 ? 0 : 1;
        }

        static int Check(string label, Ability_Command donor, Func<Ability_Command> cloneFactory)
        {
            Ability_Command clone = cloneFactory();
            clone.NameScriptBytes = Ability_Command.CloneScriptBytes(donor.NameScriptBytes)!;
            return CompareAbilitySlice(label, donor, clone);
        }

        static int CompareFields(string label, Ability_Command donor, Ability_Command clone)
        {
            int fail = 0;
            if (donor.CostOverdrive != clone.CostOverdrive)
            {
                Console.WriteLine($"  {label}: CostOverdrive donor={donor.CostOverdrive} clone={clone.CostOverdrive}");
                fail++;
            }
            if (donor.SubMenuCategorization != clone.SubMenuCategorization)
            {
                Console.WriteLine($"  {label}: SubMenu donor={donor.SubMenuCategorization} clone={clone.SubMenuCategorization}");
                fail++;
            }
            if (donor.ExtraInfo?.OrderingIndexInMenu != clone.ExtraInfo?.OrderingIndexInMenu)
            {
                Console.WriteLine($"  {label}: Extra.Order donor={donor.ExtraInfo?.OrderingIndexInMenu} clone={clone.ExtraInfo?.OrderingIndexInMenu}");
                fail++;
            }
            return fail;
        }

        static int CompareAbilitySlice(string label, Ability_Command donor, Ability_Command clone)
        {
            byte[] donorSlice = ExtractAbilitySlice(donor);
            byte[] cloneSlice = ExtractAbilitySlice(clone);
            if (donorSlice.AsSpan().SequenceEqual(cloneSlice))
            {
                Console.WriteLine($"  {label}: PASS (ability slice 0x{AbilitySliceLength:X})");
                return 0;
            }

            int d = 0;
            while (d < AbilitySliceLength && donorSlice[d] == cloneSlice[d]) d++;
            Console.WriteLine($"  {label}: DRIFT @row+0x{AbilitySliceOffset + d:X2} (donor 0x{donorSlice[d]:X2} vs clone 0x{cloneSlice[d]:X2})");
            return 1;
        }

        static byte[] ExtractAbilitySlice(Ability_Command command)
        {
            byte[] file = Ability_Command.WriteList(new List<Ability_Command> { command }, hasExtraInfo: true);
            EntryListFile listFile = EntryListFile.Unpack(file);
            byte[] slice = new byte[AbilitySliceLength];
            Array.Copy(listFile.FirstFile, AbilitySliceOffset, slice, 0, AbilitySliceLength);
            return slice;
        }
    }
}
