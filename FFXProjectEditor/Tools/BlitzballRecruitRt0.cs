using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the blitzball RECRUITMENT patcher (player-id immediate in a field event .ebp)
    // is byte-faithful:
    //   1) site detection: the known recruits of guad0000 (Giera 0x1F, Auda 0x22, Nav 0x21) are found;
    //   2) no-edit Read->Write == original;
    //   3) mutation isolation: re-pointing one recruit changes ONLY that recruit's id byte(s), same length,
    //      and the event re-reads the new id at the same sites.
    // Run: FFXProjectEditor.exe --blitzball-recruit-rt0 [guad0000.ebp]
    internal static class BlitzballRecruitRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== Blitzball RECRUITMENT (.ebp player-id immediate) RT0 ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            Event_File ev;
            try { ev = Event_File.Read("guad0000", orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            // 1) site detection
            List<BlitzballRecruitSite> sites = BlitzballRecruit_File.FindSites(ev);
            var ids = sites.Select(s => s.PlayerId).Distinct().OrderBy(x => x).ToList();
            Console.WriteLine($"recruit sites : {sites.Count} (ids: {string.Join(", ", ids.Select(i => $"0x{i:X2}"))})");
            byte[] expect = { 0x1F, 0x21, 0x22 }; // Giera / Nav / Auda
            if (!expect.All(e => ids.Contains(e)))
            {
                Console.WriteLine($"VERDICT: FAIL - expected ids {string.Join(",", expect.Select(e => $"0x{e:X2}"))} not all found.");
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

            // 3) mutation isolation: re-point Giera (0x1F) -> Wakka (0x33), patch both branches
            const byte oldId = 0x1F, newId = 0x33;
            var ev2 = Event_File.Read("guad0000", orig);
            int chunk0Off = ev2.Chunks[0].Offset; // = 0x40
            int[] targetFileOffsets = BlitzballRecruit_File.FindSites(ev2)
                .Where(s => s.PlayerId == oldId)
                .Select(s => chunk0Off + s.ScriptOffset)
                .ToArray();
            int patched = BlitzballRecruit_File.SetRecruit(ev2, oldId, newId);
            byte[] mutated = ev2.Write();

            Console.WriteLine($"patched sites : {patched} (file offsets: {string.Join(", ", targetFileOffsets.Select(o => $"0x{o:X}"))})");
            if (mutated.Length != orig.Length)
            {
                Console.WriteLine($"VERDICT: FAIL - mutation changed length {orig.Length} -> {mutated.Length}.");
                return 1;
            }
            var diffs = new List<int>();
            for (int i = 0; i < orig.Length; i++)
                if (orig[i] != mutated[i]) diffs.Add(i);

            bool isolated = diffs.Count == patched
                            && diffs.All(d => targetFileOffsets.Contains(d))
                            && diffs.All(d => orig[d] == oldId && mutated[d] == newId);
            // re-read finds the new id at those sites, old id gone
            var reSites = BlitzballRecruit_File.FindSites(Event_File.Read("guad0000", mutated));
            bool reRead = reSites.Any(s => s.PlayerId == newId) && reSites.All(s => s.PlayerId != oldId);

            Console.WriteLine($"diffs         : {diffs.Count} byte(s) @ {string.Join(", ", diffs.Select(d => $"0x{d:X}"))}");
            Console.WriteLine($"re-read       : new id 0x{newId:X2} present={reSites.Any(s => s.PlayerId == newId)}, old 0x{oldId:X2} gone={reSites.All(s => s.PlayerId != oldId)}");
            if (isolated && reRead)
            {
                Console.WriteLine("VERDICT: PASS - recruit re-point isolated to the player-id byte(s); event re-reads the new recruit.");
                return 0;
            }
            Console.WriteLine("VERDICT: FAIL - mutation not isolated / re-read mismatch.");
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
