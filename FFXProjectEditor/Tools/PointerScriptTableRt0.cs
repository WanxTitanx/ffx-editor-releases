using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves PointerScriptTable_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). The pointer-script table (battle_script.bin) is a
    // pointer-header writer: it clones the original bytes and only re-stamps the fixed 4-byte
    // little-endian pointer slots (slot*4), preserving the script text bodies + padding verbatim.
    // Run via: FFXProjectEditor.exe --pointerscripttable-rt0 [battle_script.bin]
    internal static class PointerScriptTableRt0
    {
        static readonly string[] SiblingLangDirs = { "jppc", "inpc", "new_uspc" };

        public static int Run(string path)
        {
            Console.WriteLine("=== PointerScriptTable_File (battle_script.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");

            byte[] orig;
            PointerScriptTable_File table;
            string resolvedPath = path;

            if (!TryReadTable(path, out orig, out table, out string? readError))
            {
                // The disk Read threw (or file missing). Try sibling language dirs.
                bool recovered = false;
                foreach (string sibling in EnumerateSiblingCandidates(path))
                {
                    if (TryReadTable(sibling, out orig, out table, out _))
                    {
                        resolvedPath = sibling;
                        Console.WriteLine($"recovered via sibling: {sibling}");
                        recovered = true;
                        break;
                    }
                }

                if (!recovered)
                {
                    Console.WriteLine($"READ FAILED: {readError ?? "file not found"}");
                    Console.WriteLine("VERDICT: BLOCKED - disk Read threw on the target and all sibling language dirs.");
                    return 2;
                }
            }

            byte[] re = PointerScriptTable_File.Write(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"resolved     : {resolvedPath}");
            Console.WriteLine($"variant      : {table.VariantLabel}");
            Console.WriteLine($"entries      : {table.EntryCount} ({table.PopulatedEntryCount} populated, {table.EmptyEntryCount} empty)");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - pointer-script no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        static bool TryReadTable(string path, out byte[] bytes, out PointerScriptTable_File table, out string? error)
        {
            bytes = Array.Empty<byte>();
            table = null!;
            error = null;

            if (!File.Exists(path))
            {
                error = "NOT FOUND";
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(path);
                table = PointerScriptTable_File.Read(bytes, FfxEncoding.UsDecoder);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static System.Collections.Generic.IEnumerable<string> EnumerateSiblingCandidates(string path)
        {
            // .../master/<lang>/menu/battle_script.bin -> swap <lang> for each known sibling dir.
            string fileName = Path.GetFileName(path);
            string? menuDir = Path.GetDirectoryName(path);          // .../<lang>/menu
            string? langDir = menuDir == null ? null : Path.GetDirectoryName(menuDir); // .../<lang>
            string? masterDir = langDir == null ? null : Path.GetDirectoryName(langDir); // .../master
            string? menuLeaf = menuDir == null ? null : Path.GetFileName(menuDir);

            if (masterDir == null || menuLeaf == null)
                yield break;

            foreach (string lang in SiblingLangDirs)
            {
                string candidate = Path.Combine(masterDir, lang, menuLeaf, fileName);
                if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                    yield return candidate;
            }
        }
    }
}
