using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Shop;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves ShopTable_File.WriteIdentity (item_shop.bin, preserve-only) is byte-faithful.
    // Run: FFXProjectEditor.exe --shoptable-rt0 [item_shop.bin]
    internal static class ShopTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== ShopTable_File (item_shop.bin) RT0 (no-edit Read->WriteIdentity byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            ShopTable table;
            try { table = ShopTable_File.Read(orig, ShopTableKind.Item); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = ShopTable_File.WriteIdentity(table);
            bool ok = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);
            Console.WriteLine($"rows : {table.Entries.Count}   len {orig.Length}/{re.Length}");

            if (!ok)
            {
                int d = 0, n = Math.Min(orig.Length, re.Length);
                while (d < n && orig[d] == re[d]) d++;
                Console.WriteLine($"VERDICT: DRIFT @0x{d:X}");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - shop-table no-edit save is byte-identical (preserve-only WriteIdentity).");
            return 0;
        }
    }
}
