using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves EncounterTable_File.Rebuild() is a STRUCTURAL rebuild that is
    //   (1) RT0 byte-identical on a no-edit round-trip (Read(x).Rebuild() == x), and
    //   (2) grow-capable: adding a formation to a group survives Rebuild()->Read() with the new
    //       formation present, the file still parsing, and still exposing exactly 2 chunks.
    // Run via: FFXProjectEditor.exe --encounter-rebuild-rt0 [btl.bin]
    internal static class EncounterRebuildRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== EncounterTable_File STRUCTURAL Rebuild RT0 + grow ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            EncounterTable_File table;
            try { table = EncounterTable_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            Console.WriteLine($"tables       : {table.Tables.Count}");
            Console.WriteLine($"groups       : {table.Tables.Sum(t => t.Groups.Count)}");
            Console.WriteLine($"formations   : {table.Tables.Sum(t => t.Groups.Sum(g => g.Formations.Count))}");
            Console.WriteLine($"chunk0Start  : 0x{table.Chunk0Start:X}");
            Console.WriteLine($"chunk1Start  : 0x{table.Chunk1Start:X}");

            // -------- (1) RT0: structural rebuild with no edits must be byte-identical. --------
            byte[] re = table.Rebuild();
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"RT0 VERDICT: DRIFT @0x{firstDiff:X} " +
                    $"(orig=0x{(firstDiff < orig.Length ? orig[firstDiff] : 0):X2} " +
                    $"re=0x{(firstDiff < re.Length ? re[firstDiff] : 0):X2}, " +
                    $"length {(orig.Length == re.Length ? "same" : "differs")})");
                DumpContext(orig, re, firstDiff);
                return 1;
            }
            Console.WriteLine("RT0 VERDICT: PASS - structural no-edit Rebuild() is byte-identical.");

            // -------- (2) grow-test: add 1 formation to the first decoded group. --------
            // Pick the first table that actually has a group with at least one formation (skip empty/system).
            EncounterTable_Entry? growTable = table.Tables.FirstOrDefault(t => t.Groups.Count > 0);
            if (growTable is null)
            {
                Console.WriteLine("GROW VERDICT: SKIP - no table with a group to grow.");
                return 1;
            }
            EncounterTable_Group growGroup = growTable.Groups[0];
            int growTableIndex = growTable.TableIndex;
            int beforeFormations = growGroup.Formations.Count;

            growGroup.Formations.Add(new EncounterTable_Formation
            {
                FormationId = 0x7F,
                Weight = 0x01,
                BattleId = $"{growTable.Map}_99"
            });
            int afterExpected = beforeFormations + 1;

            byte[] grown = table.Rebuild();
            Console.WriteLine($"grow: table[{growTableIndex}] group[0] formations {beforeFormations} -> {afterExpected}; " +
                $"len {orig.Length} -> {grown.Length} (+{grown.Length - orig.Length})");

            EncounterTable_File reread;
            try { reread = EncounterTable_File.Read(grown); }
            catch (Exception ex) { Console.WriteLine($"GROW VERDICT: FAIL - reread threw: {ex.Message}"); return 1; }

            // a) still parses with the same table count
            if (reread.Tables.Count != table.Tables.Count)
            {
                Console.WriteLine($"GROW VERDICT: FAIL - table count changed {table.Tables.Count} -> {reread.Tables.Count}.");
                return 1;
            }

            // b) the grown group has +1 formation
            EncounterTable_Entry? rt = reread.Tables.FirstOrDefault(t => t.TableIndex == growTableIndex);
            if (rt is null || rt.Groups.Count == 0)
            {
                Console.WriteLine("GROW VERDICT: FAIL - grown table/group missing after reread.");
                return 1;
            }
            int rereadFormations = rt.Groups[0].Formations.Count;
            if (rereadFormations != afterExpected)
            {
                Console.WriteLine($"GROW VERDICT: FAIL - group[0] formations {rereadFormations}, expected {afterExpected}.");
                return 1;
            }

            // c) still exactly 2 chunks (chunk0Start>0, chunk1Start>chunk0Start, end>chunk1Start)
            int c0 = reread.Chunk0Start, c1 = reread.Chunk1Start;
            int end = grown[0x0C] | (grown[0x0D] << 8) | (grown[0x0E] << 16) | (grown[0x0F] << 24);
            int chunkCount = grown[0x00] | (grown[0x01] << 8) | (grown[0x02] << 16) | (grown[0x03] << 24);
            bool twoChunks = chunkCount == 2 && c0 > 0 && c1 > c0 && end > c1;
            Console.WriteLine($"grown chunks : count={chunkCount} c0=0x{c0:X} c1=0x{c1:X} end=0x{end:X}");
            if (!twoChunks)
            {
                Console.WriteLine("GROW VERDICT: FAIL - grown file does not expose exactly 2 valid chunks.");
                return 1;
            }

            // d) sanity: the new formation we appended is actually present in the grown group.
            bool appendedPresent = rt.Groups[0].Formations.Any(f => f.FormationId == 0x7F && f.Weight == 0x01);
            if (!appendedPresent)
            {
                Console.WriteLine("GROW VERDICT: FAIL - appended formation (id=0x7F w=1) not found after reread.");
                return 1;
            }

            Console.WriteLine("GROW VERDICT: PASS - +1 formation survives Rebuild()->Read(), parses, 2 chunks intact.");
            Console.WriteLine("VERDICT: PASS - structural Rebuild RT0 byte-identical AND grow valid.");
            return 0;
        }

        private static void DumpContext(byte[] a, byte[] b, int at)
        {
            int start = Math.Max(0, at - 8);
            int endA = Math.Min(a.Length, at + 8);
            int endB = Math.Min(b.Length, at + 8);
            Console.WriteLine($"  orig @0x{start:X}: " + string.Join(' ', a[start..endA].Select(x => x.ToString("X2"))));
            Console.WriteLine($"  re   @0x{start:X}: " + string.Join(' ', b[start..endB].Select(x => x.ToString("X2"))));
        }
    }
}
