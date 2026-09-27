using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Treasure;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves Treasure_File.ReadTable -> WriteTable is byte-faithful
    // (no-edit round-trip == original). takara.bin is an indexed fixed table:
    // 0x14-byte header + 498 fully-decoded 4-byte entries (Kind/Quantity/Type u16 LE).
    // Run via: FFXProjectEditor.exe --treasure-rt0 [takara.bin]
    internal static class TreasureRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== Treasure_File (takara.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            TreasureTable table;
            try { table = Treasure_File.ReadTable(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = Treasure_File.WriteTable(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"header       : 0x14 bytes");
            Console.WriteLine($"index range  : {table.Header.MinIndex}..{table.Header.MaxIndex}");
            Console.WriteLine($"entry length : 0x{table.Header.EntryLength:X2}");
            Console.WriteLine($"payload bytes: 0x{table.Header.TotalDataLength:X4}");
            Console.WriteLine($"entries      : {table.Entries.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            if (!ProbeSinglePayloadMutation(table, orig, out string mutationSummary))
            {
                Console.WriteLine($"mutation    : {mutationSummary}");
                return 1;
            }

            Console.WriteLine($"mutation    : {mutationSummary}");
            Console.WriteLine("VERDICT: PASS - treasure no-edit save is byte-identical (0x14 header + payload round-trip).");
            return 0;
        }

        static bool ProbeSinglePayloadMutation(TreasureTable table, byte[] originalBytes, out string summary)
        {
            if (table.Entries.Count == 0)
            {
                summary = "BLOCKED - table has no entries to mutate.";
                return false;
            }

            int index = Math.Min(331, table.Entries.Count - 1);
            var entries = table.Entries
                .Select(entry => new Treasure_Entry
                {
                    Kind = entry.Kind,
                    Quantity = entry.Quantity,
                    ItemId = entry.ItemId
                })
                .ToList();

            entries[index].Quantity = (byte)(entries[index].Quantity == byte.MaxValue
                ? 0
                : entries[index].Quantity + 1);

            byte[] mutated = Treasure_File.WriteTable(new TreasureTable
            {
                OriginalBytes = table.OriginalBytes,
                Header = table.Header,
                Entries = entries
            });

            if (mutated.Length != originalBytes.Length)
            {
                summary = $"FAIL - mutation changed file length {originalBytes.Length} -> {mutated.Length}.";
                return false;
            }

            int expectedOffset = 0x14 + (index * Treasure_Entry.LENGTH) + 1;
            int diffCount = 0;
            int firstDiff = -1;
            int lastDiff = -1;
            for (int i = 0; i < originalBytes.Length; i++)
            {
                if (originalBytes[i] == mutated[i])
                    continue;

                diffCount++;
                if (firstDiff < 0)
                    firstDiff = i;
                lastDiff = i;
            }

            if (diffCount == 1 && firstDiff == expectedOffset)
            {
                summary = $"PASS - controlled entry #{index} quantity edit touched payload only @0x{expectedOffset:X4}; header unchanged.";
                return true;
            }

            summary = $"FAIL - expected one payload diff @0x{expectedOffset:X4}, got {diffCount} diff(s) from 0x{firstDiff:X4} to 0x{lastDiff:X4}.";
            return false;
        }
    }
}
