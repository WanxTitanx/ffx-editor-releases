using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Offline analyze + orange-bolt patch on <c>ppp_dataA</c> / KeThRes static PPP surfaces.
    /// RT2 in-game only when deploying patched <c>magic_0716.dll</c>.
    /// </summary>
    internal static class ThundaFiraKeThResPatchRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\thundafira_kethres_patch";

        public static int Run(string[] args)
        {
            try
            {
                bool analyzeOnly = args.Any(a => a.Equals("--analyze", StringComparison.OrdinalIgnoreCase));
                bool includeData = args.Any(a => a.Equals("--include-data", StringComparison.OrdinalIgnoreCase));
                bool patch = args.Any(a => a.Equals("--patch-orange", StringComparison.OrdinalIgnoreCase));
                if (!analyzeOnly && !patch)
                    patch = true;

                int sourceId = int.Parse(ArgValue(args, "--source-id") ?? "94");
                int outputId = int.Parse(ArgValue(args, "--output-id") ?? "716");
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                int maxPatches = int.Parse(ArgValue(args, "--max-patches") ?? "24", System.Globalization.CultureInfo.InvariantCulture);
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                string sourceDll = Path.Combine(magicRoot, $"magic_{sourceId:D4}.dll");
                if (!File.Exists(sourceDll))
                {
                    Console.WriteLine($"FAIL: {sourceDll} missing");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                string outDll = Path.Combine(outputDir, $"magic_{outputId:D4}_kethres_orange.dll");
                string analyzeJson = Path.Combine(outputDir, $"magic_{sourceId:D4}_kethres_analyze.json");
                string patchMd = Path.Combine(outputDir, $"magic_{outputId:D4}_KETHRES_PATCH.md");

                Console.WriteLine("=== ThundaFira KeThRes PPP PATCH (offline) ===");
                Console.WriteLine($"source  : {sourceDll}");
                Console.WriteLine($"mode    : {(analyzeOnly ? "analyze" : "patch-orange")}");
                Console.WriteLine($"out dir : {outputDir}");

                Console.WriteLine($"scope   : {(includeData ? "ppp+data" : "ppp-only (dataA+blob)")}");

                MagicDllInspection inspection = MagicDllDecompiler.Inspect(sourceDll, repoRoot);
                MagicDllKeThResPppPatch.AnalyzeResult analysis =
                    MagicDllKeThResPppPatch.Analyze(inspection, includeDataSection: includeData);
                MagicDllKeThResPppPatch.WriteAnalyzeJson(analysis, analyzeJson);

                Console.WriteLine($"primary : {analysis.PrimaryResource}");
                if (analysis.DataA != null)
                    Console.WriteLine($"dataA   : va=0x{analysis.DataA.ImageVa:X} off=0x{analysis.DataA.FileOffset:X} len={analysis.DataA.Length}");
                Console.WriteLine($"candidates: {analysis.Candidates.Count}");
                foreach (MagicDllKeThResPppPatch.PatchCandidate c in analysis.Candidates.Take(8))
                    Console.WriteLine($"  [{c.Score,4:F0}] 0x{c.FileOffset:X} {c.Kind} — {c.Note}");

                foreach (string n in analysis.Notes)
                    Console.WriteLine($"note: {n}");

                Console.WriteLine($"wrote: {analyzeJson}");

                if (analyzeOnly)
                {
                    Console.WriteLine(analysis.Candidates.Count > 0 ? "VERDICT: PASS (analyze)" : "VERDICT: PASS (no static candidates)");
                    return 0;
                }

                MagicDllKeThResPppPatch.PatchResult result =
                    MagicDllKeThResPppPatch.ApplyOrangeBoltPatch(inspection, outDll, maxPatches, includeDataSection: includeData);
                MagicDllKeThResPppPatch.WritePatchMarkdown(result, patchMd);

                Console.WriteLine($"patched : {result.Applied.Count} sites");
                Console.WriteLine($"output  : {outDll}");
                Console.WriteLine($"sha     : {result.OutputSha256[..16]}…");
                Console.WriteLine($"wrote   : {patchMd}");
                Console.WriteLine(result.Applied.Count > 0 ? "VERDICT: PASS (patch written — RT2 in-game pending)" : "VERDICT: PARTIAL (no static sites patched)");
                return result.Applied.Count > 0 ? 0 : 3;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
