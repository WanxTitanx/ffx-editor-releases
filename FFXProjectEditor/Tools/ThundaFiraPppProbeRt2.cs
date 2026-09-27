using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Map Thundaga Family D PPP resources (magic_0094) + color candidates for ThundaFira bolt RT2.
    /// </summary>
    internal static class ThundaFiraPppProbeRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultOutputDir = @"work\thundafira_ppp_probe";

        static readonly int[] CompareIds = [86, 94, 95];

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                Console.WriteLine("=== ThundaFira PPP PROBE (Family D / Thundaga) ===");
                Console.WriteLine($"magic : {magicRoot}");
                Console.WriteLine($"ps3   : {ps3Root}");
                Console.WriteLine($"out   : {outputDir}");

                Directory.CreateDirectory(outputDir);
                var reports = new List<object>();

                foreach (int magicId in CompareIds)
                {
                    string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                    if (!File.Exists(dllPath))
                    {
                        Console.WriteLine($"skip magic_{magicId:D4}: DLL missing");
                        continue;
                    }

                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                    MagicDllFamilyClassification family = MagicDllFamilyClassifier.Classify(inspection);
                    MagicDllLogicalDecompileResult logical = MagicDllLogicalDecompiler.Decompile(inspection);
                    IReadOnlyList<MagicDllPppColorCandidateScanner.ColorCandidate> colors =
                        MagicDllPppColorCandidateScanner.Scan(inspection, maxResults: 24);

                    string[] pppThunder = inspection.Strings
                        .Select(s => s.Value)
                        .Where(v => v.StartsWith("pppKeTh", StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(v => v, StringComparer.Ordinal)
                        .ToArray();

                    string phyreFolder = Path.Combine(ps3Root, $"magic_{magicId:D4}");
                    string[] phyreFiles = Directory.Exists(phyreFolder)
                        ? Directory.GetFiles(phyreFolder, "*.dds.phyre", SearchOption.AllDirectories)
                            .Select(Path.GetFileName)
                            .Where(n => n != null)
                            .Cast<string>()
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                        : [];

                    Console.WriteLine($"magic_{magicId:D4}: family={family.Family} pppKeTh={pppThunder.Length} colors={colors.Count} phyre={phyreFiles.Length}");
                    foreach (MagicDllPppColorCandidateScanner.ColorCandidate c in colors.Take(5))
                        Console.WriteLine($"  {MagicDllPppColorCandidateScanner.FormatCandidateLine(c)}");

                    reports.Add(new
                    {
                        magicId,
                        family = family.Family.ToString(),
                        slotKind = logical.SlotKindSignature,
                        pppThunder,
                        topColors = colors.Select(c => new
                        {
                            fileOffset = c.FileOffset,
                            fileOffsetHex = $"0x{c.FileOffset:X}",
                            rva = c.RvaHex,
                            c.R,
                            c.G,
                            c.B,
                            c.A,
                            c.Score,
                            c.Note
                        }),
                        phyreFiles,
                        phyreHeuristic = BuildPhyreHeuristic(phyreFiles)
                    });
                }

                string[] anim1Only = DiffPhyre(
                    Path.Combine(ps3Root, "magic_0094"),
                    Path.Combine(ps3Root, "magic_0095"));

                var payload = new
                {
                    compareIds = CompareIds,
                    anim1OnlyPhyre = anim1Only,
                    note = "Anim1 bolt phyre smoke: recolor anim1OnlyPhyre on magic_0716. DLL: single-offset --ppp-color-test only."
                };

                string jsonPath = Path.Combine(outputDir, "thundafira_ppp_probe.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { reports, payload }, new JsonSerializerOptions { WriteIndented = true }));

                string mdPath = Path.Combine(outputDir, "THUNDAFIRA_PPP_PROBE.md");
                File.WriteAllText(mdPath, BuildMarkdown(reports, anim1Only, payload.note));

                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine($"md  : {mdPath}");
                Console.WriteLine(anim1Only.Length > 0
                    ? $"Anim1-only phyre vs 95: {string.Join(", ", anim1Only)}"
                    : "Anim1-only phyre: (none by filename — check dims)");
                Console.WriteLine("VERDICT: PASS — review top color offsets; RT2 one offset at a time with --thundafira-ppp-color-test");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static object[] BuildPhyreHeuristic(string[] files)
        {
            var rows = new List<object>();
            foreach (string file in files)
            {
                string? dims = ExtractDims(file);
                string? guess = dims switch
                {
                    "128_64" => "pppKeThRes64?",
                    "128_128" => "pppKeThRes32x4?",
                    "512_256" => "pppKeThRes64x4?",
                    "256_128" => "burst/flash",
                    _ => null
                };
                rows.Add(new { file, dims, guess });
            }

            return rows.ToArray();
        }

        static string[] DiffPhyre(string anim1Folder, string anim2Folder)
        {
            if (!Directory.Exists(anim1Folder) || !Directory.Exists(anim2Folder))
                return [];

            HashSet<string> a2 = Directory.GetFiles(anim2Folder, "*.dds.phyre", SearchOption.AllDirectories)
                .Select(f => NormalizePhyreKey(Path.GetFileName(f)!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Directory.GetFiles(anim1Folder, "*.dds.phyre", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Cast<string>()
                .Where(n => !a2.Contains(NormalizePhyreKey(n)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string NormalizePhyreKey(string fileName)
        {
            int idx = fileName.IndexOf("_0_0_", StringComparison.Ordinal);
            return idx >= 0 ? fileName[idx..] : fileName;
        }

        static string? ExtractDims(string fileName)
        {
            int idx = fileName.IndexOf("_0_0_", StringComparison.Ordinal);
            if (idx < 0)
                return null;
            string tail = fileName[(idx + 5)..];
            int dot = tail.IndexOf(".dds.phyre", StringComparison.OrdinalIgnoreCase);
            return dot > 0 ? tail[..dot] : null;
        }

        static string BuildMarkdown(List<object> reports, string[] anim1Only, string note)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ThundaFira PPP Probe — Family D Thundaga");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z · Lane Jarvis-MAGIC");
            sb.AppendLine();
            sb.AppendLine(note);
            sb.AppendLine();
            sb.AppendLine("## Anim1-only phyre (0094 vs 0095 filename tail)");
            foreach (string f in anim1Only)
                sb.AppendLine($"- `{f}`");
            if (anim1Only.Length == 0)
                sb.AppendLine("- _(none — compare dims manually)_");
            sb.AppendLine();
            sb.AppendLine("## Reports");
            sb.AppendLine("See `thundafira_ppp_probe.json` for PPP strings, color candidates, phyre lists.");
            return sb.ToString();
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
