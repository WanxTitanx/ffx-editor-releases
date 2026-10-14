using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves MixTable_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). prepare.bin is the item-mix combo table; the writer clones the
    // original bytes and only re-stamps the decoded per-origin result slots.
    // Run via: FFXProjectEditor.exe --mixtable-rt0 [prepare.bin]
    internal static class MixTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== MixTable_File (prepare.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            MixTable table;
            try { table = MixTable_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = MixTable_File.Write(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"origins      : {table.Entries.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - mix-table no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }
    }
}
