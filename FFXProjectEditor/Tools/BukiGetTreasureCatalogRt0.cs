using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Treasure;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves BukiGetTreasureCatalog_File.Write is a byte-faithful
    // preserve-only writer (no-edit Read -> Write == original). buki_get.bin is a
    // fixed-table writer: it clones the original bytes and re-stamps only each entry's
    // little-endian RawWords (the exact LE decode of the original entry block), so a
    // no-edit save is byte-identical by construction.
    // Run via: FFXProjectEditor.exe --bukigettreasurecatalog-rt0 [buki_get.bin]
    internal static class BukiGetTreasureCatalogRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== BukiGetTreasureCatalog_File (buki_get.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");

            byte[]? orig = null;
            string? resolvedPath = null;

            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                byte[] bytes = File.ReadAllBytes(candidate);
                try
                {
                    // Probe-read to confirm this candidate parses before committing to it.
                    BukiGetTreasureCatalog_File.Read(bytes);
                    orig = bytes;
                    resolvedPath = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"READ THREW for {candidate}: {ex.Message}");
                }
            }

            if (orig == null || resolvedPath == null)
            {
                Console.WriteLine("VERDICT: BLOCKED - no readable buki_get.bin found (tried path + sibling language dirs).");
                return 2;
            }

            if (resolvedPath != path)
                Console.WriteLine($"resolved : {resolvedPath} (sibling fallback)");

            BukiGetTreasureCatalog catalog;
            try { catalog = BukiGetTreasureCatalog_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = BukiGetTreasureCatalog_File.Write(catalog);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {catalog.EntriesByIndex.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - buki_get no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // Yields the given path first, then the same file under sibling language dirs
        // (jppc / inpc / new_uspc) in case the requested language dir is missing/broken.
        static System.Collections.Generic.IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;

            string[] langDirs = { "jppc", "inpc", "new_uspc" };
            string fileName = Path.GetFileName(path);
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                yield break;

            // dir is .../master/<lang>/battle/kernel ; swap the <lang> segment.
            foreach (string lang in langDirs)
            {
                string? probe = SwapLanguageSegment(dir, lang);
                if (probe == null)
                    continue;

                string candidate = Path.Combine(probe, fileName);
                if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                    yield return candidate;
            }
        }

        static string? SwapLanguageSegment(string dir, string lang)
        {
            string[] knownLangs = { "jppc", "inpc", "new_uspc", "uspc", "depc", "frpc", "itpc", "sppc" };
            string[] parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int langIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (knownLangs.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                {
                    langIndex = i;
                    break;
                }
            }

            if (langIndex < 0)
                return null;

            parts[langIndex] = lang;
            return string.Join(Path.DirectorySeparatorChar, parts);
        }
    }
}
