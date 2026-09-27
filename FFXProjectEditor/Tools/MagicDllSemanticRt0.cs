using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class MagicDllSemanticRt0
    {
        const string DefaultOutputDir = @"work\magic_dll_semantics_rt0";

        public static int Run(string[] args)
        {
            try
            {
                string ffxRoot = args.Length > 1 ? args[1] : MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
                string ffx2Root = args.Length > 2 ? args[2] : MagicDllSemanticAnalyzer.DefaultFfx2MagicFilesRoot;
                string outputDir = args.Length > 3 ? args[3] : DefaultOutputDir;
                string repoRoot = Directory.GetCurrentDirectory();

                Console.WriteLine("=== Magic DLL Semantic RE RT0 ===");
                Console.WriteLine($"FFX root : {ffxRoot}");
                Console.WriteLine($"FFX-2 root: {ffx2Root}");
                Console.WriteLine($"output  : {outputDir}");

                MagicDllCorpusSemanticAnalysis ffx = MagicDllSemanticAnalyzer.AnalyzeRoot("FFX", ffxRoot, repoRoot, attachFfxOverlayEvidence: true);
                MagicDllCorpusSemanticAnalysis? ffx2 = Directory.Exists(ffx2Root)
                    ? MagicDllSemanticAnalyzer.AnalyzeRoot("FFX-2", ffx2Root, repoRoot, attachFfxOverlayEvidence: false)
                    : null;

                MagicDllSemanticReportFiles files = MagicDllSemanticAnalyzer.WriteReport(ffx, ffx2, outputDir);

                Console.WriteLine($"FFX DLLs inspected : {ffx.InspectedDlls}/{ffx.TotalDlls}");
                Console.WriteLine($"FFX overlay evidence: {ffx.WithOverlayEvidence}");
                Console.WriteLine($"FFX top signatures : {string.Join(" | ", ffx.SignatureStats.Take(5).Select(s => $"{s.Count}x {s.Signature}"))}");
                if (ffx2 != null)
                {
                    Console.WriteLine($"FFX-2 DLLs inspected: {ffx2.InspectedDlls}/{ffx2.TotalDlls}");
                    Console.WriteLine($"FFX-2 overlay evidence: {ffx2.WithOverlayEvidence} (expected 0 until a FFX-2 overlay CSV is generated)");
                }
                Console.WriteLine($"json : {files.JsonPath}");
                Console.WriteLine($"dlls : {files.DllCsvPath}");
                Console.WriteLine($"slots: {files.SlotCsvPath}");
                Console.WriteLine($"host : {files.HostCsvPath}");
                Console.WriteLine($"md   : {files.MarkdownPath}");

                bool pass = ffx.InspectedDlls > 0
                    && ffx.WithOverlayEvidence > 0
                    && ffx.HostFieldRoles.Any(r => r.CandidateName.Contains("sub80CD60", StringComparison.OrdinalIgnoreCase))
                    && File.Exists(files.MarkdownPath);

                Console.WriteLine(pass
                    ? "VERDICT: PASS - semantic corpus report generated with FFX overlay/host role candidates."
                    : "VERDICT: FAIL - semantic report missing required FFX evidence.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}
