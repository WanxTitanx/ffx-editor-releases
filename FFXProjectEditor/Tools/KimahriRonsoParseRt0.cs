using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    /// <summary>Dump Kimahri Lancet + Ronso Rage OD ring stats from command.bin.</summary>
    internal static class KimahriRonsoParseRt0
    {
        static readonly int[] DefaultRonsoIds = KimahriExtendedCommandWriter.RonsoRageDonorIds;

        public static int Run(string? commandBinPath)
        {
            commandBinPath ??= FindDefaultCommandBin();
            if (commandBinPath == null || !File.Exists(commandBinPath))
            {
                Console.Error.WriteLine("command.bin not found. Pass path as 2nd arg.");
                return 1;
            }

            byte[] bytes = File.ReadAllBytes(commandBinPath);
            List<Ability_Command> list = Ability_Command.ReadList(bytes, hasExtraInfo: true);
            Console.WriteLine($"=== Kimahri / Ronso Rage parse ===");
            Console.WriteLine($"file : {commandBinPath}");
            Console.WriteLine($"rows : {list.Count}");
            Console.WriteLine();

            DumpSection("Kimahri per-char (AbiMap 0..95)", list, [32]);
            DumpSection("Ronso Rage menu opener", list, [282]);
            DumpSection("Ronso Rage ring (Lancet-learned, ids 104–115)", list, DefaultRonsoIds);

            List<int> extraKimahri = [];
            for (int i = 0; i < list.Count; i++)
            {
                if (i is 32 or >= 104 and <= 115 or 282)
                    continue;
                if (list[i].CharacterUser == Character_Enum.Kimahri)
                    extraKimahri.Add(i);
            }

            if (extraKimahri.Count > 0)
                DumpSection("Other rows with CharacterUser = Kimahri", list, extraKimahri.ToArray());

            if (list.Count > KimahriExtendedCommandWriter.VanillaRowCount)
            {
                int first = KimahriExtendedCommandWriter.VanillaRowCount;
                int[] grown = Enumerable.Range(first, list.Count - first).ToArray();
                DumpSection($"Appended rows (#{first}..#{list.Count - 1})", list, grown);
            }

            if (list.Count >= KimahriExtendedCommandWriter.VanillaRowCount + KimahriExtendedCommandWriter.ExpectedAppendCount)
            {
                int start = list.Count - KimahriExtendedCommandWriter.ExpectedAppendCount;
                int[] ids = Enumerable.Range(start, KimahriExtendedCommandWriter.ExpectedAppendCount).ToArray();
                DumpSection(
                    $"Kimahri Blue Mage pack (#{start}..#{list.Count - 1})",
                    list,
                    ids);
            }

            return 0;
        }

        static void DumpSection(string title, List<Ability_Command> list, int[] ids)
        {
            Console.WriteLine($"--- {title} ---");
            Console.WriteLine(
                "ID | Name | MP | OD | Pwr | Acc | Hits | Formula | DmgType | Target | SubMenu | Elements | Status/Special");
            Console.WriteLine(new string('-', 120));

            foreach (int id in ids)
            {
                if (id < 0 || id >= list.Count)
                {
                    Console.WriteLine($"{id,3} | (missing — file has only {list.Count} rows)");
                    continue;
                }

                Ability_Command c = list[id];
                string name = DecodeName(c, id);
                string formula = c.DamageFormula.ToString();
                string dmg = DescribeDamage(c);
                string target = DescribeTarget(c);
                string elements = DescribeElements(c);
                string status = DescribeStatus(c);
                string special = DescribeSpecial(c);

                Console.WriteLine(
                    $"{id,3} | {name,-16} | {c.CostMp,2} | {c.CostOverdrive,3} | {c.AttackPower,3} | {c.AttackAccuracy,3} | {c.HitCount,4} | {formula,-18} | {dmg,-7} | {target,-6} | {c.SubMenuCategorization,7} | {elements,-8} | {status}{special}");
            }

            Console.WriteLine();
        }

        static string FindDefaultCommandBin()
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "work", "nul_ward_pack", "command.bin"),
                Path.Combine(Directory.GetCurrentDirectory(), "work", "nul_ward_pack", "command.bin"),
                @"D:\FFX Extracted\FFX\new_uspc\battle\kernel\command.bin",
            ];

            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full))
                    return full;
            }

            return candidates[1];
        }

        static string DecodeName(Ability_Command c, int id)
        {
            try
            {
                string decoded = FfxEncoding.DecodeScript(c.NameScriptBytes).GetString(FfxEncoding.UsDecoder);
                if (!string.IsNullOrWhiteSpace(decoded))
                    return decoded;
            }
            catch { /* fall through */ }

            return CommandCharacter_Dictionary.Instance.TryGetValue((ushort)id, out string? dictName)
                ? dictName
                : $"#{id}";
        }

        static string DescribeDamage(Ability_Command c)
        {
            if (c.FlagDamageHeals)
                return "Heal";
            if (c.FlagDamageMagical && c.FlagDamagePhysical)
                return "Mixed";
            if (c.FlagDamageMagical)
                return "Magic";
            if (c.FlagDamagePhysical)
                return "Phys";
            return "None";
        }

        static string DescribeTarget(Ability_Command c)
        {
            List<string> parts = [];
            if (c.FlagTargetSelfOnly) parts.Add("Self");
            else if (c.FlagTargetMulti) parts.Add("Multi");
            else parts.Add("Single");
            if (c.FlagTargetEnemies) parts.Add("Foe");
            if (c.FlagTargetEitherTeam) parts.Add("Either");
            if (c.FlagTargetDead) parts.Add("Dead");
            return string.Join("+", parts);
        }

        static string DescribeElements(Ability_Command c)
        {
            List<string> e = [];
            if (c.FlagElementFire) e.Add("Fire");
            if (c.FlagElementBlizzard) e.Add("Ice");
            if (c.FlagElementThunder) e.Add("Thunder");
            if (c.FlagElementWater) e.Add("Water");
            if (c.FlagElementHoly) e.Add("Holy");
            if (c.FlagElementDark) e.Add("Dark");
            return e.Count == 0 ? "—" : string.Join(",", e);
        }

        static string DescribeStatus(Ability_Command c)
        {
            List<string> s = [];
            void Add(string label, byte chance, byte? turns = null)
            {
                if (chance == 0)
                    return;
                s.Add(turns.HasValue ? $"{label} {chance}%/{turns}t" : $"{label} {chance}%");
            }

            Add("Petrify", c.StatusChance.Petrify);
            Add("Poison", c.StatusChance.Poison);
            Add("Sleep", c.StatusChance.Sleep, c.StatusDuration.Sleep);
            Add("Silence", c.StatusChance.Silence, c.StatusDuration.Silence);
            Add("Dark", c.StatusChance.Darkness, c.StatusDuration.Darkness);
            Add("Slow", c.StatusChance.Slow, c.StatusDuration.Slow);
            Add("Zombie", c.StatusChance.Zombie);
            Add("Regen", c.StatusChance.Regen, c.StatusDuration.Regen);
            Add("Haste", c.StatusChance.Haste, c.StatusDuration.Haste);
            Add("Shell", c.StatusChance.Shell, c.StatusDuration.Shell);
            Add("Protect", c.StatusChance.Protect, c.StatusDuration.Protect);
            Add("Reflect", c.StatusChance.Reflect, c.StatusDuration.Reflect);
            Add("BreakPwr", c.StatusChance.BreakPower);
            Add("BreakMag", c.StatusChance.BreakMagic);
            Add("BreakArm", c.StatusChance.BreakArmor);
            Add("BreakMnt", c.StatusChance.BreakMental);
            Add("Confuse", c.StatusChance.Confuse);
            Add("Berserk", c.StatusChance.Berserk);

            if (c.FlagStatusScan) s.Add("Scan");
            if (c.FlagStatusShield) s.Add("Shield");
            if (c.FlagStatusAutoLife) s.Add("AutoLife");
            if (c.FlagStatusDoom) s.Add("Doom");

            return s.Count == 0 ? "" : string.Join("; ", s);
        }

        static string DescribeSpecial(Ability_Command c)
        {
            List<string> s = [];
            if (c.FlagMisc2DelayS) s.Add("DelayS");
            if (c.FlagMisc2DelayL) s.Add("DelayL");
            if (c.FlagMisc3Piercing) s.Add("Pierce");
            if (c.FlagMisc4RunOffScreen) s.Add("OffScreen");
            if (c.FlagMisc4AeonOverdrive) s.Add("AeonOD");
            if (c.OverdriveCategory != 0) s.Add($"ODcat={c.OverdriveCategory}");
            if (c.FlagDamageBreaksDamageLimit) s.Add("BDL");
            return s.Count == 0 ? "" : " | " + string.Join(",", s);
        }
    }
}
