using System;
using System.IO;
using FFXProjectEditor.FfxLib.WeaponNames;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves WeaponNameTable_File.WriteIdentity is a byte-faithful preserve-only writer
    // (no-edit Read -> WriteIdentity == original) for w_name.bin (weapon/armor name table).
    //
    // The family's regular Write is intentionally LOSSY for byte-identity: it rebuilds the string pool
    // with dedup and re-packs the text offsets, so a no-edit save can drift even though it stays
    // model-correct. WriteIdentity clones the verbatim source bytes and re-stamps only the fixed
    // per-entry scalar fields (the 7 model words + the final word) back into their original slots, so
    // an unedited table round-trips byte-for-byte. This gate calls WriteIdentity.
    //
    // Run via: FFXProjectEditor.exe --weaponnametable-rt0 [w_name.bin]
    internal static class WeaponNameTableRt0
    {
        // Sibling language dirs to retry when the supplied/primary file fails to Read.
        static readonly string[] LanguageDirs = { "new_uspc", "jppc", "inpc" };

        public static int Run(string path)
        {
            Console.WriteLine("=== WeaponNameTable_File (w_name.bin) RT0 (no-edit Read->WriteIdentity byte-identity) ===");
            Console.WriteLine($"file : {path}");

            if (!TryReadTable(path, out byte[] orig, out WeaponNameTable_File? table, out string? error))
            {
                // The primary path failed (missing or threw). Try the sibling language dirs.
                foreach (string candidate in EnumerateSiblingCandidates(path))
                {
                    Console.WriteLine($"retry: {candidate}");
                    if (TryReadTable(candidate, out orig, out table, out error))
                    {
                        Console.WriteLine($"file : {candidate} (sibling fallback)");
                        break;
                    }
                }
            }

            if (table is null)
            {
                Console.WriteLine($"READ FAILED (all candidates): {error}");
                return 2;
            }

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

            Console.WriteLine("VERDICT: PASS - weapon-name no-edit save is byte-identical (preserve-only WriteIdentity).");
            return 0;
        }

        static bool TryReadTable(string path, out byte[] orig, out WeaponNameTable_File? table, out string? error)
        {
            orig = Array.Empty<byte>();
            table = null;
            error = null;

            if (!File.Exists(path))
            {
                error = "NOT FOUND";
                return false;
            }

            try
            {
                orig = File.ReadAllBytes(path);
                table = WeaponNameTable_File.Read(orig, FfxEncoding.UsDecoder);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                table = null;
                return false;
            }
        }

        // Given a path like ...\master\new_uspc\battle\kernel\w_name.bin, yield the same relative file
        // under the other language dirs (jppc / inpc / new_uspc) so a broken primary can be recovered.
        static System.Collections.Generic.IEnumerable<string> EnumerateSiblingCandidates(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { yield break; }

            string normalized = full.Replace('/', '\\');
            foreach (string dir in LanguageDirs)
            {
                string segment = $"\\{dir}\\";
                int idx = normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    continue;

                string prefix = normalized.Substring(0, idx + 1);
                string suffix = normalized.Substring(idx + segment.Length);
                foreach (string other in LanguageDirs)
                {
                    string candidate = prefix + other + "\\" + suffix;
                    if (!string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                        yield return candidate;
                }

                yield break;
            }
        }
    }
}
