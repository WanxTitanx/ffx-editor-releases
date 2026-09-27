using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves BtlTextTable_File.WriteIdentity is a byte-faithful preserve-only writer
    // (no-edit Read -> WriteIdentity == original) for btl_txt.bin (battle text).
    //
    // btl_txt.bin is a header (0x14) + entry-offset table + an OVERLAPPING suffix-shared string pool.
    // The editable UI Write(decoder) is append-only (lossless), so a no-edit save is also byte-identical;
    // but the gate uses the dedicated WriteIdentity() which clones the original bytes verbatim and only
    // re-stamps the four fixed header scalars in place. That keeps the proof independent of the text codec.
    //
    // Run via: FFXProjectEditor.exe --btltexttable-rt0 [btl_txt.bin]
    internal static class BtlTextTableRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== BtlTextTable_File (btl_txt.bin) RT0 (no-edit Read->WriteIdentity byte-identity) ===");

            byte[]? orig = null;
            BtlTextTable_File? table = null;
            string? usedPath = null;
            string? lastError = null;

            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                byte[] bytes;
                try { bytes = File.ReadAllBytes(candidate); }
                catch (Exception ex) { lastError = $"{candidate}: read failed: {ex.Message}"; continue; }

                try
                {
                    table = BtlTextTable_File.Read(bytes, FfxEncoding.UsDecoder);
                    orig = bytes;
                    usedPath = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = $"{candidate}: parse threw: {ex.Message}";
                }
            }

            if (table == null || orig == null || usedPath == null)
            {
                Console.WriteLine($"file : {path}");
                Console.WriteLine(lastError == null ? "NOT FOUND (no candidate path exists)" : $"READ THREW: {lastError}");
                return 2;
            }

            Console.WriteLine($"file : {usedPath}");

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

            Console.WriteLine("VERDICT: PASS - btl_txt.bin no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // Try the supplied path first, then the same relative file under sibling language dirs.
        static System.Collections.Generic.IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;

            string[] langs = { "new_uspc", "jppc", "inpc" };
            foreach (string lang in langs)
            {
                string? swapped = SwapLanguageDir(path, lang);
                if (swapped != null && !string.Equals(swapped, path, StringComparison.OrdinalIgnoreCase))
                    yield return swapped;
            }
        }

        static string? SwapLanguageDir(string path, string targetLang)
        {
            string[] knownLangs = { "new_uspc", "jppc", "inpc", "uspc", "frpc", "gepc", "itpc", "sppc", "krpc", "chpc" };
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i++)
            {
                if (knownLangs.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                {
                    string[] copy = (string[])parts.Clone();
                    copy[i] = targetLang;
                    return string.Join(Path.DirectorySeparatorChar, copy);
                }
            }

            return null;
        }
    }
}
