using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Customization;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves Customization_File gear/aeon writers are byte-faithful
    // (no-edit Read -> Write == original). Both kaizou.bin (gear) and sum_grow.bin (aeon) are
    // fixed-stride indexed tables fully decoded into 8-byte entries; the writer preserves the
    // header prefix/tail and re-emits the same entry bytes, so the round-trip is byte-identical.
    // Run via: FFXProjectEditor.exe --customization-rt0 [kaizou.bin] [sum_grow.bin]
    internal static class CustomizationRt0
    {
        public static int Run(string gearPath, string aeonPath)
        {
            Console.WriteLine("=== Customization_File (kaizou.bin / sum_grow.bin) RT0 (no-edit Read->Write byte-identity) ===");

            int gear = RunGear(gearPath);
            int aeon = RunAeon(aeonPath);

            if (gear == 2 || aeon == 2)
            {
                Console.WriteLine("VERDICT: NOT FOUND - one or both customization files are missing.");
                return 2;
            }

            if (gear != 0 || aeon != 0)
            {
                Console.WriteLine("VERDICT: DRIFT - at least one customization writer is not byte-faithful.");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - gear+aeon customization no-edit save is byte-identical (preserve-only writers).");
            return 0;
        }

        static int RunGear(string path)
        {
            Console.WriteLine($"gear file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("  NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            CustomizationTable<GearCustomizationEntry> table;
            try { table = Customization_File.ReadGear(orig); }
            catch (Exception ex) { Console.WriteLine($"  READ THREW: {ex.Message}"); return 2; }

            byte[] re = Customization_File.WriteGear(table);
            return Report("gear", orig, re, table.Entries.Count);
        }

        static int RunAeon(string path)
        {
            Console.WriteLine($"aeon file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("  NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            CustomizationTable<AeonCustomizationEntry> table;
            try { table = Customization_File.ReadAeon(orig); }
            catch (Exception ex) { Console.WriteLine($"  READ THREW: {ex.Message}"); return 2; }

            byte[] re = Customization_File.WriteAeon(table);
            return Report("aeon", orig, re, table.Entries.Count);
        }

        static int Report(string label, byte[] orig, byte[] re, int entryCount)
        {
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);
            Console.WriteLine($"  {label} entries: {entryCount}; len orig / re: {orig.Length} / {re.Length}");
            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"  {label} DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }
            Console.WriteLine($"  {label} PASS - byte-identical.");
            return 0;
        }
    }
}
