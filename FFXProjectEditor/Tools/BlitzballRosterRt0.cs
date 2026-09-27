using System;
using System.IO;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the .ebp ATEL eventData-variable writer (blitzball roster stats/growth in
    // bltz0002.ebp, vars 0x126..0x12E) is byte-faithful:
    //   1) no-edit Read->Write == original   (container round-trip; subset of --event-rt0, re-asserted here);
    //   2) decode sanity                      (60 players x 8 stats; all growthType in {-1,0,1,2,3});
    //   3) mutation isolation                 (a single-float patch stays within the 4 target bytes at the
    //                                          predicted file offset; header/other chunks untouched).
    // Run: FFXProjectEditor.exe --blitzball-roster-rt0 [bltz0002.ebp]
    internal static class BlitzballRosterRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== Blitzball roster (.ebp ATEL eventData var) RT0 ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            Event_File ev;
            try { ev = Event_File.Read(BlitzballRoster_File.EventId, orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            // 1) no-edit round-trip == original
            byte[] noEdit = ev.Write();
            Console.WriteLine($"len orig / re : {orig.Length} / {noEdit.Length}");
            if (!(noEdit.Length == orig.Length && noEdit.AsSpan().SequenceEqual(orig)))
            {
                Console.WriteLine($"VERDICT: DRIFT on no-edit save @0x{FirstDiff(orig, noEdit):X}");
                return 1;
            }

            // 2) decode sanity
            BlitzballStatGrowth[,] grid = BlitzballRoster_File.ReadAll(ev);
            BlitzballStatGrowth hp0 = grid[0, 0];
            Console.WriteLine($"players x stats : {BlitzballRoster_File.PlayerCount} x {BlitzballRoster_File.Stats.Count}");
            Console.WriteLine($"player0 HP (a,b,c,gt) = ({hp0.A}, {hp0.B}, {hp0.C}, {hp0.GrowthType})  [expect 70, 30, ~0.711, 3]");
            int bad = 0;
            for (int s = 0; s < BlitzballRoster_File.Stats.Count; s++)
                for (int p = 0; p < BlitzballRoster_File.PlayerCount; p++)
                {
                    float gt = grid[p, s].GrowthType;
                    if (gt != -1f && gt != 0f && gt != 1f && gt != 2f && gt != 3f) bad++;
                }
            Console.WriteLine($"growthType valid : {(bad == 0 ? "ALL 480" : bad + " INVALID")}");
            if (bad != 0) { Console.WriteLine("VERDICT: FAIL - growthType decode invalid (offset chain wrong)."); return 1; }

            // 3) mutation isolation: patch player0 HP 'a' (var 0x126, element 0) by +1.0
            int varId = BlitzballRoster_File.Stats[0].VarId; // 0x126 (HP)
            int expectedOffset = ev.EventDataElementFileOffset(varId, 0);
            Event_File ev2 = Event_File.Read(BlitzballRoster_File.EventId, orig);
            float baseA = BitConverter.ToSingle(ev2.ReadEventDataElement(varId, 0), 0);
            ev2.PatchEventDataElement(varId, 0, BitConverter.GetBytes(baseA + 1.0f));
            byte[] mutated = ev2.Write();

            if (mutated.Length != orig.Length)
            {
                Console.WriteLine($"VERDICT: FAIL - mutation changed length {orig.Length} -> {mutated.Length}.");
                return 1;
            }
            int diffCount = 0, first = -1, last = -1;
            for (int i = 0; i < orig.Length; i++)
                if (orig[i] != mutated[i]) { diffCount++; if (first < 0) first = i; last = i; }

            Console.WriteLine($"mutation : {diffCount} byte(s) changed @0x{(first < 0 ? 0 : first):X}..0x{(last < 0 ? 0 : last):X}; target element @0x{expectedOffset:X}+4");
            if (diffCount >= 1 && first >= expectedOffset && last <= expectedOffset + 3)
            {
                Console.WriteLine("VERDICT: PASS - no-edit byte-identical + single-float patch isolated to the 4 target bytes at predicted offset.");
                return 0;
            }
            Console.WriteLine("VERDICT: FAIL - mutation not isolated to the predicted element bytes.");
            return 1;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }
    }
}
