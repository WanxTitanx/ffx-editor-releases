using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Shop;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves ShopGearCatalog_File.Write is a byte-faithful preserve-only writer
    // (no-edit Read -> Write == original). shop_arms.bin is a fixed-stride equipment table:
    // Write clones the original bytes and only re-stamps the clearly-fixed scalar fields of each
    // EquipmentStruct in place (header + lossy presence byte stay preserved), so a no-edit save
    // is byte-identical by construction.
    // Run via: FFXProjectEditor.exe --shopgearcatalog-rt0 [shop_arms.bin]
    internal static class ShopGearCatalogRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== ShopGearCatalog_File (shop_arms.bin) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");

            byte[]? orig = null;
            string? resolvedPath = null;

            foreach (string candidate in EnumerateCandidatePaths(path))
            {
                if (!File.Exists(candidate))
                    continue;

                byte[] candidateBytes = File.ReadAllBytes(candidate);
                try
                {
                    // Probe: ensure Read succeeds for this candidate before committing to it.
                    ShopGearCatalog_File.Read(candidateBytes);
                    orig = candidateBytes;
                    resolvedPath = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"READ THREW for {candidate}: {ex.Message}");
                }
            }

            if (orig is null || resolvedPath is null)
            {
                Console.WriteLine("VERDICT: blocked - no readable shop_arms.bin found (tried sibling language dirs).");
                return 2;
            }

            if (!string.Equals(resolvedPath, path, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"resolved via sibling dir : {resolvedPath}");

            ShopGearCatalog table;
            try { table = ShopGearCatalog_File.Read(orig); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = ShopGearCatalog_File.Write(table);
            bool identical = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);

            Console.WriteLine($"entries      : {table.EntriesByIndex.Count}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!identical)
            {
                int firstDiff = 0, n = Math.Min(orig.Length, re.Length);
                while (firstDiff < n && orig[firstDiff] == re[firstDiff]) firstDiff++;
                Console.WriteLine($"VERDICT: DRIFT @0x{firstDiff:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - shop_arms.bin no-edit save is byte-identical (preserve-only writer).");
            return 0;
        }

        // If the given path is missing/unreadable, try the same relative file under the sibling
        // language dirs (jppc / inpc / new_uspc).
        static System.Collections.Generic.IEnumerable<string> EnumerateCandidatePaths(string path)
        {
            yield return path;

            string fileName = Path.GetFileName(path);
            string? kernelDir = Path.GetDirectoryName(path);          // .../<lang>/battle/kernel
            string? battleDir = kernelDir is null ? null : Path.GetDirectoryName(kernelDir);
            string? langDir = battleDir is null ? null : Path.GetDirectoryName(battleDir);
            string? masterDir = langDir is null ? null : Path.GetDirectoryName(langDir);

            if (masterDir is null || langDir is null || battleDir is null || kernelDir is null)
                yield break;

            string battleRel = Path.GetFileName(battleDir);           // "battle"
            string kernelRel = Path.GetFileName(kernelDir);           // "kernel"

            foreach (string lang in new[] { "jppc", "inpc", "new_uspc" })
            {
                string candidate = Path.Combine(masterDir, lang, battleRel, kernelRel, fileName);
                if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                    yield return candidate;
            }
        }
    }
}
