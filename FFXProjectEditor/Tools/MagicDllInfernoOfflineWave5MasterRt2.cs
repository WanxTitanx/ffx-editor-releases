using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Orchestrates offline inferno waves 5/5b-prep/5c without launching FFX.
    /// </summary>
    internal static class MagicDllInfernoOfflineWave5MasterRt2
    {
        const string DefaultWorkRoot = @"work";

        public static int Run(string[] args)
        {
            string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
            string workRoot = Path.IsPathRooted(ArgValue(args, "--work") ?? DefaultWorkRoot)
                ? ArgValue(args, "--work")!
                : Path.Combine(repoRoot, ArgValue(args, "--work") ?? DefaultWorkRoot);
            string wave5Dir = Path.Combine(workRoot, "magic_dll_logical_decompile_wave2", "wave5");
            bool skipWave3 = HasFlag(args, "--skip-wave3");
            bool skipWave4 = HasFlag(args, "--skip-wave4");

            Console.WriteLine("=== INFERNO OFFLINE MASTER (Wave 5 suite) ===");
            Console.WriteLine($"repo : {repoRoot}");
            Console.WriteLine();

            if (!skipWave3 && !File.Exists(Path.Combine(workRoot, "magic_dll_logical_decompile_wave2", "wave3_subfamily_clusters.json")))
            {
                Console.WriteLine(">> Wave 3 classify (missing artifacts)...");
                int rc = MagicDllClassifyWave3Rt2.Run(Prepend(args, "--wave2", Path.Combine(workRoot, "magic_dll_logical_decompile_wave2")));
                if (rc != 0) return rc;
            }

            if (!skipWave4 && !File.Exists(Path.Combine(workRoot, "magic_dll_logical_decompile_wave2", "wave4", "wave4_data_phyre_inventory.json")))
            {
                Console.WriteLine(">> Wave 4 deep corpus (missing artifacts)...");
                int rc = MagicDllDeepCorpusWave4Rt2.Run(Prepend(args, "--wave2", Path.Combine(workRoot, "magic_dll_logical_decompile_wave2")));
                if (rc != 0) return rc;
            }

            Console.WriteLine(">> Wave 5 KeThRes corpus...");
            int w5 = MagicDllKeThResCorpusWave5Rt2.Run(Prepend(args, "--wave2", Path.Combine(workRoot, "magic_dll_logical_decompile_wave2")));
            if (w5 != 0) return w5;

            Console.WriteLine(">> Wave 5c offline runtime dumps (live vs PE, no FFX)...");
            int w5c = MagicDllOfflineRuntimeDiffWave5cRt2.Run(Prepend(args, "--work", workRoot));
            if (w5c != 0 && w5c != 2)
                return w5c;

            WriteMasterManifest(wave5Dir, workRoot);
            Console.WriteLine();
            Console.WriteLine("VERDICT: PASS — inferno offline suite complete (Wave 5 + 5c; Wave 5b IDA = scripts/ida_wave5_exe_ppp_batch.py)");
            Console.WriteLine($"manifest: {Path.Combine(wave5Dir, "INFERNO_OFFLINE_MANIFEST.md")}");
            return 0;
        }

        static void WriteMasterManifest(string wave5Dir, string workRoot)
        {
            Directory.CreateDirectory(wave5Dir);
            var sb = new StringBuilder();
            sb.AppendLine("# Inferno offline manifest — Wave 5 suite");
            sb.AppendLine();
            sb.AppendLine($"- **Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- **Work root:** `{workRoot}`");
            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            foreach (string rel in new[]
            {
                "magic_dll_logical_decompile_wave2/wave5/wave5_kethres_corpus.json",
                "magic_dll_logical_decompile_wave2/wave5/wave5_summary.json",
                "magic_dll_logical_decompile_wave2/wave5/wave5_vec4_outliers.json",
                "magic_dll_logical_decompile_wave2/wave5/WAVE5_KETHRES_CORPUS.md",
                "magic_dll_logical_decompile_wave2/wave5/wave5c_offline_runtime_diff.json",
                "reverse/ida/exports/wave5_exe_ppp_batch.json",
            })
            {
                string full = Path.Combine(workRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                sb.AppendLine($"- `{rel}` — {(File.Exists(full) ? "OK" : "pending")}");
            }
            File.WriteAllText(Path.Combine(wave5Dir, "INFERNO_OFFLINE_MANIFEST.md"), sb.ToString(), new UTF8Encoding(false));
        }

        static string[] Prepend(string[] args, string key, string value)
        {
            var list = new List<string> { key, value };
            list.AddRange(args);
            return list.ToArray();
        }

        static bool HasFlag(string[] args, string flag) =>
            args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
