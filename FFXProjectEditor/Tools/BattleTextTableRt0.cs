using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves BattleTextTable_File.Write is a byte-faithful
    // preserve-only writer (no-edit Read -> Write == original). btl_txt.bin is an
    // 8-byte indexed table over a lossy (decode-only) string pool, so Write clones
    // the original byte snapshot and only re-stamps the fixed scalar fields
    // (header words + per-entry word offsets); the string pool is preserved verbatim.
    // Run via: FFXProjectEditor.exe --battletexttable-rt0 [btl_txt.bin]
    internal static class BattleTextTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== BattleTextTable_File (btl_txt.bin) RT0 (no-edit Read->Write byte-identity) ===");

            // Resolve a readable file: try the given path, then sibling language dirs.
            string? resolved = ResolvePath(path, out BattleTextTable_File? table, out byte[]? orig);
            if (resolved is null || table is null || orig is null)
            {
                Console.WriteLine("VERDICT: BLOCKED - could not Read btl_txt.bin from the given path or any sibling language dir.");
                return 2;
            }

            Console.WriteLine($"file : {resolved}");

            byte[] re = BattleTextTable_File.Write(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {table.EntryCount} (index {table.MinIndex:X2}h..{table.MaxIndex:X2}h)");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - battle-text no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // Attempts to read from the provided path; if its Read throws or the file is
        // missing, walks the known sibling language dirs (new_uspc / jppc / inpc).
        static string? ResolvePath(string path, out BattleTextTable_File? table, out byte[]? orig)
        {
            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    byte[] bytes = File.ReadAllBytes(candidate);
                    BattleTextTable_File parsed = BattleTextTable_File.Read(bytes, FfxEncoding.UsDecoder);
                    table = parsed;
                    orig = bytes;
                    return candidate;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"READ failed for {candidate}: {ex.Message}");
                }
            }

            table = null;
            orig = null;
            return null;
        }

        static System.Collections.Generic.IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;

            // Swap the language dir segment if present (…/master/<lang>/battle/kernel/…).
            string[] langs = { "new_uspc", "jppc", "inpc" };
            foreach (string lang in langs)
            {
                string swapped = SwapLanguageDir(path, lang);
                if (!string.Equals(swapped, path, StringComparison.OrdinalIgnoreCase))
                    yield return swapped;
            }
        }

        static string SwapLanguageDir(string path, string targetLang)
        {
            string norm = path.Replace('\\', '/');
            string[] langs = { "new_uspc", "jppc", "inpc" };
            foreach (string lang in langs)
            {
                string token = $"/master/{lang}/";
                int idx = norm.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string replaced = norm.Substring(0, idx) + $"/master/{targetLang}/" + norm.Substring(idx + token.Length);
                    return replaced.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            return path;
        }
    }
}
