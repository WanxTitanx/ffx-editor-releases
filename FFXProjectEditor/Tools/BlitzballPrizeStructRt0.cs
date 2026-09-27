using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the blitzball PRIZE STRUCTURE patcher (prize-index 2-byte immediate in bltz0200.ebp)
    // is byte-faithful:
    //   1) site detection: finds the constant prize-index assignment sites across the 4 prize vars (0x30..0x33);
    //   2) no-edit Read->Write == original;
    //   3) mutation isolation: changing one prize index changes ONLY that site's 2 immediate bytes, same length,
    //      and the script re-reads the new prize index at that site.
    // Run: FFXProjectEditor.exe --blitzball-prizestruct-rt0 [bltz0200.ebp]
    internal static class BlitzballPrizeStructRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== Blitzball PRIZE STRUCTURE (bltz0200 prize-index immediate) RT0 ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            Event_File ev;
            try { ev = Event_File.Read(BlitzballPrizeStructure_File.EventId, orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            // 1) site detection + breakdown
            List<BlitzballPrizeSite> sites = BlitzballPrizeStructure_File.FindSites(ev);
            foreach (BlitzballPrizeVar v in Enum.GetValues<BlitzballPrizeVar>())
            {
                int n = sites.Count(s => s.Var == v);
                Console.WriteLine($"  {BlitzballPrizeStructure_File.VarLabel(v),-22} : {n} site(s)");
            }
            Console.WriteLine($"prize sites   : {sites.Count}");
            if (sites.Count < 50)
            {
                Console.WriteLine($"VERDICT: FAIL - expected many prize-index sites (var ids 0x30..0x33), found {sites.Count}.");
                return 1;
            }

            // 2) no-edit round-trip
            byte[] noEdit = ev.Write();
            Console.WriteLine($"len orig / re : {orig.Length} / {noEdit.Length}");
            if (!(noEdit.Length == orig.Length && noEdit.AsSpan().SequenceEqual(orig)))
            {
                int d = FirstDiff(orig, noEdit);
                Console.WriteLine($"VERDICT: DRIFT on no-edit save @0x{d:X}");
                return 1;
            }

            // 3) mutation isolation: change one site's prize index to a distinct value
            var ev2 = Event_File.Read(BlitzballPrizeStructure_File.EventId, orig);
            int chunk0Off = ev2.Chunks[0].Offset; // = 0x40
            List<BlitzballPrizeSite> sites2 = BlitzballPrizeStructure_File.FindSites(ev2);
            // pick a non-zero array site so the change is meaningful and the value differs
            BlitzballPrizeSite target = sites2.FirstOrDefault(s => s.IsArray && s.PrizeIndex != 0) ?? sites2[0];
            ushort oldVal = target.PrizeIndex;
            ushort newVal = (ushort)(oldVal == 0x0099 ? 0x0042 : 0x0099);
            int targetFileOffset = chunk0Off + target.ValueOffset;

            BlitzballPrizeStructure_File.SetPrizeIndexAt(ev2, target.ValueOffset, newVal);
            byte[] mutated = ev2.Write();

            Console.WriteLine($"target site   : {BlitzballPrizeStructure_File.VarLabel(target.Var)} {BlitzballPrizeStructure_File.SlotLabel(target.Slot)} @file 0x{targetFileOffset:X} : 0x{oldVal:X4} -> 0x{newVal:X4}");
            if (mutated.Length != orig.Length)
            {
                Console.WriteLine($"VERDICT: FAIL - mutation changed length {orig.Length} -> {mutated.Length}.");
                return 1;
            }
            var diffs = new List<int>();
            for (int i = 0; i < orig.Length; i++)
                if (orig[i] != mutated[i]) diffs.Add(i);

            bool isolated = diffs.Count >= 1 && diffs.Count <= 2
                            && diffs.All(d => d == targetFileOffset || d == targetFileOffset + 1);

            ushort reReadVal = BlitzballPrizeStructure_File
                .FindSites(Event_File.Read(BlitzballPrizeStructure_File.EventId, mutated))
                .First(s => s.ValueOffset == target.ValueOffset).PrizeIndex;

            Console.WriteLine($"diffs         : {diffs.Count} byte(s) @ {string.Join(", ", diffs.Select(d => $"0x{d:X}"))}");
            Console.WriteLine($"re-read       : site now = 0x{reReadVal:X4} (expected 0x{newVal:X4})");
            if (!(isolated && reReadVal == newVal))
            {
                Console.WriteLine("VERDICT: FAIL - prize-index mutation not isolated / re-read mismatch.");
                return 1;
            }
            Console.WriteLine("prize-index   : OK (isolated + re-read).");

            // 4) roll-threshold (ODDS) detection + isolation
            var ev3 = Event_File.Read(BlitzballPrizeStructure_File.EventId, orig);
            List<BlitzballRollThresholdSite> thresholds = BlitzballPrizeStructure_File.FindRollThresholds(ev3);
            int upper = thresholds.Count(t => t.IsUpperBound);
            Console.WriteLine($"threshold sites : {thresholds.Count} ({upper} upper '<=' / {thresholds.Count - upper} lower '>=')");
            if (thresholds.Count < 20)
            {
                Console.WriteLine($"VERDICT: FAIL - expected the prize-switch roll thresholds, found {thresholds.Count}.");
                return 1;
            }

            BlitzballRollThresholdSite tt = thresholds.First(t => t.IsUpperBound && t.Threshold is > 0 and < 100);
            ushort tOld = tt.Threshold;
            ushort tNew = (ushort)(tOld == 50 ? 60 : 50);
            int tFileOffset = ev3.Chunks[0].Offset + tt.ValueOffset;
            BlitzballPrizeStructure_File.SetThresholdAt(ev3, tt.ValueOffset, tNew);
            byte[] tMutated = ev3.Write();

            var tDiffs = new List<int>();
            for (int i = 0; i < orig.Length; i++)
                if (orig[i] != tMutated[i]) tDiffs.Add(i);
            bool tIsolated = tMutated.Length == orig.Length && tDiffs.Count >= 1 && tDiffs.Count <= 2
                             && tDiffs.All(d => d == tFileOffset || d == tFileOffset + 1);
            ushort tReRead = BlitzballPrizeStructure_File
                .FindRollThresholds(Event_File.Read(BlitzballPrizeStructure_File.EventId, tMutated))
                .First(t => t.ValueOffset == tt.ValueOffset).Threshold;

            Console.WriteLine($"threshold edit  : @file 0x{tFileOffset:X} '<= {tOld}' -> '<= {tNew}'; diffs={tDiffs.Count}; re-read={tReRead}");
            if (!(tIsolated && tReRead == tNew))
            {
                Console.WriteLine("VERDICT: FAIL - threshold mutation not isolated / re-read mismatch.");
                return 1;
            }

            // 5) DRAW structure (RE-grounded): each prize/threshold site belongs to one GetRandomInRange roll
            //    switch. This is the grouping unit behind the editor's League/Tournament -> placement -> draw tree.
            List<int> starts = BlitzballPrizeStructure_File.FindRollSwitchStarts(ev);
            var drawsUsed = new HashSet<int>();
            int orphan = 0;
            foreach (BlitzballPrizeSite s in sites)
            {
                int d = BlitzballPrizeStructure_File.DrawStartFor(starts, s.ValueOffset);
                if (d < 0) orphan++; else drawsUsed.Add(d);
            }
            Console.WriteLine($"roll switches   : {starts.Count} GetRandomInRange call(s); prize draws used: {drawsUsed.Count}; orphan prize sites (pre-first-roll): {orphan}");
            // Sanity: many distinct draws, (almost) every prize site mapped. (real bltz0200: 68 draws, 1 orphan.)
            if (drawsUsed.Count < 40 || orphan > 4)
            {
                Console.WriteLine($"VERDICT: FAIL - draw model looks wrong (draws={drawsUsed.Count}, orphan={orphan}).");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - prize-index AND roll-threshold edits both isolated; script re-reads both; draw model holds.");
            return 0;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }
    }
}
