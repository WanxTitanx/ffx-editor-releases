using FFXProjectEditor.FfxLib.Monster;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    // Headless RT0 audit for the Monster writer (backlog #4 follow-up): for each monster_*.bin,
    // Monster_File.Read(x).Write() must equal x. Measures how many monsters DRIFT on a no-edit save
    // and localizes it (size drift = StatSheet text rebuild / padding; same-size byte drift = content).
    // Runs via the editor entry: FFXProjectEditor.exe --monster-rt0 [monsterRoot]
    internal static class MonsterRt0
    {
        public static int Run(string root)
        {
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"monster root not found: {root}");
                return 2;
            }

            var files = Directory.EnumerateFiles(root, "m*.bin", SearchOption.AllDirectories)
                .Where(p => { string n = Path.GetFileNameWithoutExtension(p); return n.Length == 4 && n[0] == 'm' && n.Skip(1).All(char.IsDigit); })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = 0, rt0 = 0, sizeDrift = 0, contentDrift = 0, readFail = 0;
            var examples = new System.Collections.Generic.List<string>();

            foreach (string path in files)
            {
                byte[] orig = File.ReadAllBytes(path);
                total++;
                byte[] re;
                try { re = Monster_File.Read(orig).Write(); }
                catch (Exception ex) { readFail++; if (examples.Count < 12) examples.Add($"{Name(path)}: Read/Write threw {ex.GetType().Name}"); continue; }

                if (re.AsSpan().SequenceEqual(orig)) { rt0++; continue; }

                if (re.Length != orig.Length)
                {
                    sizeDrift++;
                    if (examples.Count < 12) examples.Add($"{Name(path)}: SIZE drift orig={orig.Length} re={re.Length} (text/padding)");
                }
                else
                {
                    contentDrift++;
                    int firstDiff = 0; while (firstDiff < orig.Length && orig[firstDiff] == re[firstDiff]) firstDiff++;
                    if (examples.Count < 12) examples.Add($"{Name(path)}: CONTENT drift @0x{firstDiff:X} (same length)");
                }
            }

            Console.WriteLine("=== Monster_File RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"monster root      : {root}");
            Console.WriteLine($"monsters          : {total}");
            Console.WriteLine($"RT0 byte-identical: {rt0}/{total}");
            Console.WriteLine($"size drift         : {sizeDrift}  (StatSheet text rebuild / alignment padding)");
            Console.WriteLine($"content drift      : {contentDrift}  (same length, bytes differ)");
            Console.WriteLine($"read/write threw   : {readFail}");
            if (examples.Count > 0)
            {
                Console.WriteLine("examples:");
                foreach (var e in examples) Console.WriteLine("  " + e);
            }
            Console.WriteLine(rt0 == total
                ? "VERDICT: PASS — Monster no-edit save is byte-identical across the corpus."
                : $"VERDICT: DRIFT — {total - rt0}/{total} monsters change on a no-edit save (see FFX_MONSTER_WRITER_AUDIT).");
            return rt0 == total ? 0 : 1;
        }

        static string Name(string p) => Path.GetFileNameWithoutExtension(p);
    }
}
