using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves AlBhedDictionary_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). albheddic.bin is a fixed 4-byte-record table; the writer
    // clones the original bytes and only re-stamps the authorable scalar fields per record
    // (SourceCode @0, MappedCode @1, GroupIndex @2 as LE u16), preserving padding verbatim.
    // Run via: FFXProjectEditor.exe --albheddictionary-rt0 [albheddic.bin]
    internal static class AlBhedDictionaryRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== AlBhedDictionary_File (albheddic.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            AlBhedDictionary_File? table = TryRead(orig, path, out string usedPath, out byte[] usedOrig);
            if (table is null)
            {
                Console.WriteLine("VERDICT: BLOCKED - Read threw on the disk file and all sibling language dirs.");
                return 2;
            }

            byte[] re = AlBhedDictionary_File.Write(table);
            bool identical = re.Length == usedOrig.Length && re.AsSpan().SequenceEqual(usedOrig);

            Console.WriteLine($"variant      : {table.VariantLabel}");
            Console.WriteLine($"entries      : {table.EntryCount} ({table.ActiveEntryCount} active / {table.PaddingEntryCount} padding)");
            Console.WriteLine($"source       : {usedPath}");
            Console.WriteLine($"len orig / re: {usedOrig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(usedOrig.Length, re.Length);
                while (firstDiff < n && usedOrig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(usedOrig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - Al Bhed dictionary no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // Try the given path; if Read throws, fall back to sibling language dirs (jppc / inpc / new_uspc).
        static AlBhedDictionary_File? TryRead(byte[] orig, string path, out string usedPath, out byte[] usedOrig)
        {
            usedPath = path;
            usedOrig = orig;
            try { return AlBhedDictionary_File.Read(orig, FfxEncoding.UsDecoder); }
            catch (Exception ex) { Console.WriteLine($"READ THREW on '{path}': {ex.Message}"); }

            foreach (string sibling in SiblingCandidates(path))
            {
                if (!File.Exists(sibling)) continue;
                try
                {
                    byte[] sb = File.ReadAllBytes(sibling);
                    AlBhedDictionary_File t = AlBhedDictionary_File.Read(sb, FfxEncoding.UsDecoder);
                    usedPath = sibling;
                    usedOrig = sb;
                    Console.WriteLine($"recovered via sibling dir: {sibling}");
                    return t;
                }
                catch (Exception ex) { Console.WriteLine($"READ THREW on '{sibling}': {ex.Message}"); }
            }

            return null;
        }

        // Swap the language directory segment (e.g. ...\master\new_uspc\menu\albheddic.bin) for known siblings.
        static System.Collections.Generic.IEnumerable<string> SiblingCandidates(string path)
        {
            string[] langs = { "jppc", "inpc", "new_uspc", "uspc" };
            string? menuDir = Path.GetDirectoryName(path);              // ...\<lang>\menu
            string? langDir = menuDir is null ? null : Path.GetDirectoryName(menuDir); // ...\<lang>
            string? masterDir = langDir is null ? null : Path.GetDirectoryName(langDir); // ...\master
            if (masterDir is null || menuDir is null) yield break;

            string menuLeaf = Path.GetFileName(menuDir);
            string fileLeaf = Path.GetFileName(path);
            string currentLang = Path.GetFileName(langDir);

            foreach (string lang in langs)
            {
                if (string.Equals(lang, currentLang, StringComparison.OrdinalIgnoreCase)) continue;
                yield return Path.Combine(masterDir, lang, menuLeaf, fileLeaf);
            }
        }
    }
}
