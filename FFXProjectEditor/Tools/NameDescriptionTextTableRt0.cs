using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves NameDescriptionTextTable_File.WriteIdentity() is a byte-faithful
    // preserve-only writer (no-edit Read -> WriteIdentity == original) for item_txt.bin and its
    // siblings. The family's editing Write(decoder) overload REPACKS the string pool (dedup +
    // offset reorder), so it is not raw byte-identical; WriteIdentity instead clones the captured
    // OriginalBytes and only re-stamps the four proven u16 text-offset scalars per entry, leaving
    // keys / padding / header / string pool verbatim. A no-edit save is byte-identical by
    // construction. Run via: FFXProjectEditor.exe --namedescriptiontexttable-rt0 [item_txt.bin]
    internal static class NameDescriptionTextTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== NameDescriptionTextTable_File (item_txt.bin) RT0 (no-edit Read->WriteIdentity byte-identity) ===");
            Console.WriteLine($"file : {path}");

            byte[]? orig = null;
            string usedPath = path;
            NameDescriptionTextTable_File? table = null;

            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                byte[] bytes;
                try { bytes = File.ReadAllBytes(candidate); }
                catch (Exception ex) { Console.WriteLine($"READ FILE FAILED ({candidate}): {ex.Message}"); continue; }

                try
                {
                    table = NameDescriptionTextTable_File.Read(bytes, FfxEncoding.UsDecoder);
                    orig = bytes;
                    usedPath = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"READ THREW ({candidate}): {ex.Message}");
                }
            }

            if (table is null || orig is null)
            {
                Console.WriteLine("VERDICT: blocked - could not read item_txt.bin from the primary path or any sibling language dir (jppc / inpc / new_uspc).");
                return 2;
            }

            if (!ReferenceEquals(usedPath, path))
                Console.WriteLine($"used : {usedPath} (sibling fallback)");

            byte[] re;
            try { re = table.WriteIdentity(); }
            catch (Exception ex) { Console.WriteLine($"WRITE THREW: {ex.Message}"); return 2; }

            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {table.EntryCount}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - name/description text table no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // The disk path lives under .../master/<lang>/battle/kernel/item_txt.bin. If the primary
        // path can't be read or Read throws, try the same relative file under each sibling
        // language directory.
        static IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;

            string[] langs = { "new_uspc", "jppc", "inpc" };
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                yield break;

            foreach (string lang in langs)
            {
                string? swapped = SwapLanguageDir(path, lang);
                if (swapped != null && !string.Equals(swapped, path, StringComparison.OrdinalIgnoreCase))
                    yield return swapped;
            }
        }

        static string? SwapLanguageDir(string path, string newLang)
        {
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);

            int masterIndex = Array.FindIndex(parts, p => string.Equals(p, "master", StringComparison.OrdinalIgnoreCase));
            if (masterIndex < 0 || masterIndex + 1 >= parts.Length)
                return null;

            parts[masterIndex + 1] = newLang;
            return string.Join(Path.DirectorySeparatorChar, parts);
        }
    }
}
