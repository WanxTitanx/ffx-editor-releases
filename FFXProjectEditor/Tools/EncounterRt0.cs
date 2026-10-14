using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves EncounterTable_File.Write() is a byte-faithful slot-only writer
    // (no-edit Read -> Write == original). Run via: FFXProjectEditor.exe --encounter-rt0 [btl.bin]
    internal static class EncounterRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== EncounterTable_File RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            EncounterTable_File table;
            try { table = EncounterTable_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = table.Write();
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"tables       : {table.Tables.Count}");
            Console.WriteLine($"groups       : {table.Tables.Sum(t => t.Groups.Count)}");
            Console.WriteLine($"formations   : {table.Tables.Sum(t => t.Groups.Sum(g => g.Formations.Count))}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - encounter-table no-edit save is byte-identical (slot-only writer).");
            return 0;
        }
    }
}
