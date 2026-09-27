using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>Offline reloc map for KeThRes PPP — safe patch gates before redeploy.</summary>
    internal static class ThundaFiraKeThResRelocRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\thundafira_kethres_reloc";

        public static int Run(string[] args)
        {
            try
            {
                int magicId = int.Parse(ArgValue(args, "--magic-id") ?? "94");
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                string dll = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                if (!File.Exists(dll))
                {
                    Console.WriteLine($"FAIL: {dll} missing");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                string jsonPath = Path.Combine(outputDir, $"magic_{magicId:D4}_kethres_reloc.json");
                string mdPath = Path.Combine(outputDir, $"magic_{magicId:D4}_KETHRES_RELOC.md");

                Console.WriteLine("=== ThundaFira KeThRes RELOC map (offline) ===");
                Console.WriteLine($"dll : {dll}");
                Console.WriteLine($"out : {outputDir}");

                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dll, repoRoot);
                MagicDllKeThResRelocAnalyzer.AnalyzeResult result = MagicDllKeThResRelocAnalyzer.Analyze(inspection);
                MagicDllKeThResRelocAnalyzer.WriteJson(result, jsonPath);
                MagicDllKeThResRelocAnalyzer.WriteMarkdown(result, mdPath);

                Console.WriteLine($"tags: {result.Tags.Count}");
                Console.WriteLine($"allowed vec4 patches: {result.PatchGates.Count(g => g.Allowed)}");
                foreach (MagicDllKeThResRelocAnalyzer.PatchGateVerdict g in result.PatchGates.Where(g => g.Allowed))
                    Console.WriteLine($"  OK 0x{g.FileOffset:X} dataA+0x{g.DataARel:X}");
                foreach (string n in result.Notes)
                    Console.WriteLine($"note: {n}");
                Console.WriteLine($"wrote: {jsonPath}");
                Console.WriteLine($"wrote: {mdPath}");
                Console.WriteLine("VERDICT: PASS");
                return 0;
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
