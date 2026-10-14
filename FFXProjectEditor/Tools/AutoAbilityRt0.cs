using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves AutoAbility_File.WriteAbilities is a byte-faithful preserve-only writer
    // for the a_ability.bin DATA section (no-edit Read -> WriteAbilities == original a_ability.bin).
    // Read() needs arms_rate.bin only to satisfy the price-table contract; this gate does NOT write
    // arms_rate.bin (that file is owned elsewhere) and does NOT cover WriteAbilitiesAndText (text repack).
    // Run via: FFXProjectEditor.exe --autoability-rt0 [a_ability.bin] [arms_rate.bin]
    internal static class AutoAbilityRt0
    {
        public static int Run(string abilityPath, string pricePath)
        {
            Console.WriteLine("=== AutoAbility_File (a_ability.bin) RT0 (no-edit Read->WriteAbilities byte-identity) ===");
            Console.WriteLine($"ability file : {abilityPath}");
            Console.WriteLine($"price file   : {pricePath}");
            if (!File.Exists(abilityPath)) { Console.WriteLine("a_ability.bin NOT FOUND"); return 2; }
            if (!File.Exists(pricePath)) { Console.WriteLine("arms_rate.bin NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(abilityPath);
            byte[] prices = File.ReadAllBytes(pricePath);

            AutoAbilityTable table;
            try { table = AutoAbility_File.Read(orig, prices); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = AutoAbility_File.WriteAbilities(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {table.Entries.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - auto-ability data-section no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }
    }
}
