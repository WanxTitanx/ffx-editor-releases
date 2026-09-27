using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class ThundaFiraKeThResDumpRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultOutputDir = @"work\thundafira_kethres_dump";

        public static int Run(string[] args)
        {
            try
            {
                int magicId = int.Parse(ArgValue(args, "--magic-id") ?? "94");
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                if (!File.Exists(dllPath))
                {
                    Console.WriteLine($"FAIL: {dllPath} missing");
                    return 2;
                }

                Console.WriteLine("=== ThundaFira KeThRes DUMP (static PE) ===");
                Console.WriteLine($"dll : {dllPath}");
                Console.WriteLine($"out : {outputDir}");

                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                MagicDllKeThResDumper.MagicDllKeThResDumpResult result =
                    MagicDllKeThResDumper.Dump(inspection, outputDir, ps3Root);

                Console.WriteLine($"regions: {result.Regions.Count}");
                foreach (MagicDllKeThResDumper.DumpRegion r in result.Regions)
                {
                    string off = r.FileOffset >= 0 ? $"0x{r.FileOffset:X}" : "-";
                    Console.WriteLine($"  {r.Id}.bin  va=0x{r.ImageVa:X} off={off} size={r.DumpedSize} nz={r.NonZeroBytes}");
                }

                Console.WriteLine($"wrote: {Path.Combine(outputDir, "kethres_dump_manifest.json")}");
                Console.WriteLine($"wrote: {Path.Combine(outputDir, "KETHRES_DUMP.md")}");
                Console.WriteLine("NOTE: full 1MiB runtime blob needs in-game capture after cast (host+2840).");
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
