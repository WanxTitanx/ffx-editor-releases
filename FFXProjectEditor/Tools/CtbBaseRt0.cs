using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves CtbBase_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). ctb_base.bin is a fixed-stride indexed table; the writer clones
    // the original bytes and only re-stamps the authorable TickSpeed/IcvBonus per entry.
    // Run via: FFXProjectEditor.exe --ctbbase-rt0 [ctb_base.bin]
    internal static class CtbBaseRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== CtbBase_File (ctb_base.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            CtbBaseTable table;
            try { table = CtbBase_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = CtbBase_File.Write(table);
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

            Console.WriteLine("VERDICT: PASS - ctb-base no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }
    }
}
