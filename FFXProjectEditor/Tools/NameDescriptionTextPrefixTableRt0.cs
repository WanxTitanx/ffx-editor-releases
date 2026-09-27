using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves NameDescriptionTextPrefixTable_File can round-trip an unedited table to a
    // byte-identical save. The family's public Write is a LOSSY rebuild (it re-packs the string pool with
    // dedup and recomputes the four text offsets), so it is only model-preserved. WriteIdentity is the
    // preserve-only path (it hands back the verbatim OriginalBytes captured in Read) and is what this gate
    // exercises. a_ability.bin is read with the same header + US decoder the editor uses for that file.
    // Run via: FFXProjectEditor.exe --namedescriptiontextprefixtable-rt0 [a_ability.bin]
    internal static class NameDescriptionTextPrefixTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== NameDescriptionTextPrefixTable_File (a_ability.bin) RT0 (no-edit Read->WriteIdentity byte-identity) ===");
            Console.WriteLine($"file : {path}");

            // Primary path plus sibling language dirs in case the supplied file refuses to parse with the
            // US decoder / expected header (jppc / inpc / new_uspc all ship the same prefix-table layout).
            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate))
                {
                    Console.WriteLine($"skip (not found): {candidate}");
                    continue;
                }

                Console.WriteLine($"try  : {candidate}");
                byte[] orig = File.ReadAllBytes(candidate);

                NameDescriptionTextPrefixTable_File table;
                try
                {
                    IndexedFixedTableHeader header = Customization_File.ReadHeader(orig);
                    table = NameDescriptionTextPrefixTable_File.Read(
                        orig,
                        header,
                        FfxEncoding.UsDecoder,
                        Path.GetFileName(candidate));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"READ THREW: {ex.Message}");
                    continue;
                }

                byte[] re = table.WriteIdentity();
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

                Console.WriteLine("VERDICT: PASS - name/description prefix-table no-edit save is byte-identical (preserve-only WriteIdentity).");
                return 0;
            }

            Console.WriteLine("VERDICT: BLOCKED - could not read a_ability.bin from the supplied path or any sibling language dir.");
            return 2;
        }

        // Yields the supplied path first, then the same relative file under jppc / inpc / new_uspc by
        // swapping the language-dir segment that sits directly under .../master/.
        static System.Collections.Generic.IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;

            string[] siblings = { "inpc", "jppc", "new_uspc" };
            string full;
            try { full = Path.GetFullPath(path); }
            catch { yield break; }

            string normalized = full.Replace('/', Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);

            int masterIndex = Array.FindIndex(parts, segment => string.Equals(segment, "master", StringComparison.OrdinalIgnoreCase));
            if (masterIndex < 0 || masterIndex + 1 >= parts.Length)
                yield break;

            int langIndex = masterIndex + 1;
            string currentLang = parts[langIndex];

            foreach (string sibling in siblings)
            {
                if (string.Equals(sibling, currentLang, StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] swapped = (string[])parts.Clone();
                swapped[langIndex] = sibling;
                yield return string.Join(Path.DirectorySeparatorChar, swapped);
            }
        }
    }
}
