using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves SphereGrid_File.WriteIdentity (sphere-type table, sphere.bin) is a byte-faithful
    // preserve-only writer (no-edit Read -> WriteIdentity == original). The production UI path
    // (WriteSphereTypes) is a LOSSY string-pool rebuild, so a SEPARATE preserve-only WriteIdentity exists
    // for this gate: it clones the original file bytes and only re-stamps the authorable scalar fields
    // (Behavior/Activates/Range/SpecialRole/Reserved0x0E) in place; the string-offset/key
    // fields and the trailing string pool are preserved verbatim, so a no-edit save is byte-identical.
    // Run via: FFXProjectEditor.exe --spheregrid-rt0 [sphere.bin]
    internal static class SphereGridRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== SphereGrid_File (sphere.bin sphere-type table) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");

            string? resolved = ResolveExisting(path);
            if (resolved == null)
            {
                Console.WriteLine("NOT FOUND (tried sibling language dirs: jppc / inpc / new_uspc)");
                return 2;
            }
            if (!string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"using: {resolved}");

            byte[] orig = File.ReadAllBytes(resolved);

            SphereGridSphereTypeTable table;
            try
            {
                table = SphereGrid_File.ReadSphereTypes(resolved, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"READ THREW: {ex.Message}");
                return 2;
            }

            byte[] re;
            try
            {
                re = SphereGrid_File.WriteIdentity(table);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WRITE THREW: {ex.Message}");
                return 2;
            }

            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {table.Entries.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - sphere-type no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // If the given path is missing, try the same relative file under sibling language dirs.
        static string? ResolveExisting(string path)
        {
            if (File.Exists(path))
                return path;

            string[] langs = { "jppc", "inpc", "new_uspc" };
            foreach (string lang in langs)
            {
                foreach (string other in langs)
                {
                    string candidate = path.Replace(
                        $"\\{lang}\\",
                        $"\\{other}\\",
                        StringComparison.OrdinalIgnoreCase);
                    if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }
    }
}
