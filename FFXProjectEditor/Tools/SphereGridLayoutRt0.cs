using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves SphereGrid_File.WriteLayout (the TOPOLOGY/LAYOUT writer for the abmap grid
    // files dat01/dat02/dat03 = Original/Standard/Expert) is byte-faithful: a no-edit
    // ReadLayout -> WriteLayout re-emits the original file byte-for-byte.
    //
    // This is the proof that "build a sphere grid from scratch" is unlocked: WriteLayout serializes the
    // full layout struct (header + cluster table + node table with positions + link table) purely from
    // the in-memory model. If read->write is byte-identical on all three shipped grids, the serializer
    // is correct and authoring a new grid = constructing a SphereGridLayoutFile + serializing.
    //
    // Run via: FFXProjectEditor.exe --spheregrid-layout-rt0 [abmapDir]
    //   abmapDir defaults to the jppc abmap folder; the gate checks dat01/dat02/dat03 (whichever exist).
    internal static class SphereGridLayoutRt0
    {
        static readonly string[] Grids =
        {
            "dat01", // Original Sphere Grid
            "dat02", // Standard Sphere Grid
            "dat03", // Expert Sphere Grid
        };

        public static int Run(string abmapDir)
        {
            Console.WriteLine("=== SphereGrid_File.WriteLayout (abmap dat01/02/03 topology) RT0 (no-edit ReadLayout->WriteLayout byte-identity) ===");
            Console.WriteLine($"dir : {abmapDir}");

            string? resolved = ResolveDir(abmapDir);
            if (resolved == null)
            {
                Console.WriteLine("NOT FOUND (tried sibling language dirs: jppc / uspc / inpc / new_uspc)");
                return 2;
            }
            if (!string.Equals(resolved, abmapDir, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"using: {resolved}");

            int checkedCount = 0;
            int worstExit = 0;

            foreach (string grid in Grids)
            {
                string layoutPath = Path.Combine(resolved, grid + ".dat");
                if (!File.Exists(layoutPath))
                {
                    Console.WriteLine($"[{grid}] skip (not found)");
                    continue;
                }

                byte[] orig = File.ReadAllBytes(layoutPath);

                // Some language dirs ship 1-byte placeholder stubs for grids that only exist in jppc
                // (e.g. uspc dat02/dat03 reuse the jppc Standard/Expert layouts). A file smaller than the
                // 0x10 header is not a real layout; skip it so the verdict reflects only real grids.
                if (orig.Length < 0x10)
                {
                    Console.WriteLine($"[{grid}] skip (placeholder stub, {orig.Length} byte(s) < 0x10 header)");
                    continue;
                }

                checkedCount++;

                SphereGridLayoutFile layout;
                try
                {
                    // Contents file (dat09/10/11) is only used for per-node ContentIndex; WriteLayout does
                    // not touch contents, so a missing/empty contents file is fine for this gate. Pass the
                    // layout path itself as the contents path when no contents file is present so the reader
                    // never throws on a missing file.
                    string contentsPath = GuessContentsPath(resolved, grid);
                    if (!File.Exists(contentsPath))
                        contentsPath = layoutPath;

                    layout = SphereGrid_File.ReadLayout(layoutPath, contentsPath, grid);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{grid}] READ THREW: {ex.Message}");
                    worstExit = Math.Max(worstExit, 2);
                    continue;
                }

                byte[] re;
                try
                {
                    re = SphereGrid_File.WriteLayout(layout);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{grid}] WRITE THREW: {ex.Message}");
                    worstExit = Math.Max(worstExit, 2);
                    continue;
                }

                bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

                Console.WriteLine($"[{grid}] clusters={layout.ClusterCount} nodes={layout.NodeCount} links={layout.LinkCount} len orig/re={orig.Length}/{re.Length}");

                if (!identical)
                {
                    int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                    while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                    Console.WriteLine($"[{grid}] DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                    worstExit = Math.Max(worstExit, 1);
                }
                else
                {
                    Console.WriteLine($"[{grid}] PASS - layout no-edit ReadLayout->WriteLayout is byte-identical.");
                }
            }

            if (checkedCount == 0)
            {
                Console.WriteLine("VERDICT: NO GRIDS FOUND (expected dat01/dat02/dat03 in the abmap dir).");
                return 2;
            }

            if (worstExit == 0)
                Console.WriteLine($"VERDICT: PASS - all {checkedCount} grid(s) round-trip byte-identical (WriteLayout proven).");
            else if (worstExit == 1)
                Console.WriteLine("VERDICT: DRIFT - at least one grid did not round-trip byte-identical.");
            else
                Console.WriteLine("VERDICT: ERROR - read/write threw on at least one grid.");

            return worstExit;
        }

        // Contents (per-node content byte) lives in dat09/dat10/dat11 for dat01/dat02/dat03 respectively.
        static string GuessContentsPath(string dir, string grid)
        {
            string contents = grid switch
            {
                "dat01" => "dat09",
                "dat02" => "dat10",
                "dat03" => "dat11",
                _ => grid,
            };
            return Path.Combine(dir, contents + ".dat");
        }

        // If the given dir is missing, try the same relative dir under sibling language folders.
        static string? ResolveDir(string dir)
        {
            if (Directory.Exists(dir))
                return dir;

            string[] langs = { "jppc", "uspc", "inpc", "new_uspc" };
            foreach (string lang in langs)
            {
                foreach (string other in langs)
                {
                    string candidate = dir.Replace(
                        $"\\{lang}\\",
                        $"\\{other}\\",
                        StringComparison.OrdinalIgnoreCase);
                    if (!string.Equals(candidate, dir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }
    }
}
