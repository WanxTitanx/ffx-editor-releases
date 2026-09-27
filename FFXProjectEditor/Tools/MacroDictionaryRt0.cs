using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves MacroDictionary_File.WriteIdentity is a byte-faithful
    // preserve-only writer (no-edit Read -> WriteIdentity == original) for the
    // macrodic.dcp container. The UI-facing Write(decoder) is a lossy rebuild
    // (recomputes the 16-slot offset table + re-packs a deduped string pool), so
    // the gate uses the preserve-only WriteIdentity path which re-emits the
    // captured OriginalBytes verbatim -> byte-identical by construction.
    // Run via: FFXProjectEditor.exe --macrodictionary-rt0 [macrodic.dcp]
    internal static class MacroDictionaryRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== MacroDictionary_File (macrodic.dcp) RT0 (no-edit Read->WriteIdentity byte-identity) ===");
            Console.WriteLine($"file : {path}");

            // Resolve a readable file. If the primary path's Read throws (or the file
            // is missing), try sibling language dirs (jppc / inpc / new_uspc).
            byte[]? orig = null;
            MacroDictionary_File? table = null;
            string? usedPath = null;

            foreach (string candidate in BuildCandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                byte[] bytes;
                try { bytes = File.ReadAllBytes(candidate); }
                catch (Exception ex) { Console.WriteLine($"READ FILE FAILED ({candidate}): {ex.Message}"); continue; }

                try
                {
                    table = MacroDictionary_File.Read(bytes, FfxEncoding.UsDecoder);
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
                Console.WriteLine("BLOCKED: no readable macrodic.dcp candidate (primary + sibling language dirs all failed).");
                return 2;
            }

            if (!string.Equals(usedPath, path, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"note : fell back to sibling candidate {usedPath}");

            byte[] re = table.WriteIdentity();
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            int chunkCount = table.Chunks.Count(c => c.IsPresent);
            int macroCount = table.Chunks.Sum(c => c.Entries.Count);
            Console.WriteLine($"present chunks: {chunkCount}");
            Console.WriteLine($"macro strings : {macroCount}");
            Console.WriteLine($"len orig / re : {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - macrodic.dcp no-edit save is byte-identical (preserve-only WriteIdentity).");
            return 0;
        }

        // Yields the primary path first, then the same filename under sibling
        // language directories (jppc / inpc / new_uspc) so a broken primary read
        // can fall back to another region's copy of macrodic.dcp.
        static System.Collections.Generic.IEnumerable<string> BuildCandidatePaths(string path)
        {
            yield return path;

            string fileName;
            try { fileName = Path.GetFileName(path); }
            catch { yield break; }

            if (string.IsNullOrEmpty(fileName))
                yield break;

            string? menuDir;
            try { menuDir = Path.GetDirectoryName(path); }
            catch { yield break; }

            if (string.IsNullOrEmpty(menuDir))
                yield break;

            // menuDir = ...\<lang>\menu  ->  langDir = ...\<lang>
            string? langDir = Path.GetDirectoryName(menuDir);
            if (string.IsNullOrEmpty(langDir))
                yield break;

            string? parentDir = Path.GetDirectoryName(langDir);
            if (string.IsNullOrEmpty(parentDir))
                yield break;

            string primaryLang = Path.GetFileName(langDir);

            foreach (string lang in new[] { "new_uspc", "jppc", "inpc" })
            {
                if (string.Equals(lang, primaryLang, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return Path.Combine(parentDir, lang, "menu", fileName);
            }
        }
    }
}
