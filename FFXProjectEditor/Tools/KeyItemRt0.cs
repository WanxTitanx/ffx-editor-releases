using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves KeyItem_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). KeyItem (important.bin) is a slot/byte-local writer:
    // it clones the original bytes and only re-stamps the authorable ItemType (0x10) / Number (0x13).
    // Run via: FFXProjectEditor.exe --keyitem-rt0 [important.bin]
    internal static class KeyItemRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== KeyItem_File (important.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            KeyItemTable table;
            try { table = KeyItem_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = KeyItem_File.Write(table);
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

            Console.WriteLine("VERDICT: PASS - key-item no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }
    }
}
