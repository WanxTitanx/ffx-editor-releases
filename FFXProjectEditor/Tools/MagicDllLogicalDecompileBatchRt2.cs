using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave-1 logical decompile: stratified 20% sample per family A/B/C/D (~116/581 DLLs).
    /// </summary>
    internal static class MagicDllLogicalDecompileBatchRt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultClassificationCsv = @"scripts\final_581_classification_v2.csv";
        const string DefaultOutputDir = @"work\magic_dll_logical_decompile_wave1";
        const double DefaultSampleFraction = 0.20;

        public static int Run(string[] args)
        {
            try
            {
                double fraction = double.Parse(ArgValue(args, "--fraction") ?? DefaultSampleFraction.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                bool allCorpus = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string csvPath = ArgValue(args, "--classification") ?? DefaultClassificationCsv;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = FindRepoRoot();

                Console.WriteLine("=== Magic DLL Logical Decompile — Wave 1 ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"csv        : {csvPath}");
                Console.WriteLine($"fraction   : {fraction:P0} per family (unless --all)");
                Console.WriteLine($"output     : {outputDir}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                string csvFull = Path.IsPathRooted(csvPath) ? csvPath : Path.Combine(repoRoot, csvPath);
                if (!File.Exists(csvFull))
                {
                    Console.WriteLine($"FAIL: classification CSV not found: {csvFull}");
                    return 2;
                }

                List<ClassificationRow> rows = LoadClassification(csvFull);
                List<ClassificationRow> targets = allCorpus
                    ? rows.OrderBy(r => r.MagicId).ToList()
                    : PickStratifiedSample(rows, fraction);

                Directory.CreateDirectory(outputDir);
                List<MagicDllLogicalDecompileResult> results = [];
                List<string> failures = [];

                foreach (ClassificationRow row in targets)
                {
                    string dllPath = Path.Combine(magicRoot, row.Dll);
                    if (!File.Exists(dllPath))
                    {
                        failures.Add($"{row.Dll}: missing");
                        continue;
                    }

                    try
                    {
                        MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                        MagicDllLogicalDecompileResult decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                        MagicDllLogicalDecompiler.WritePerDllReport(decoded, outputDir);
                        results.Add(decoded);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{row.Dll}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                WriteCorpusArtifacts(results, rows, targets, failures, outputDir, fraction, allCorpus);
                PrintSummary(results, targets, failures, outputDir);

                bool pass = results.Count >= (allCorpus ? 500 : 100) && failures.Count < results.Count / 4;
                Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: PARTIAL");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static List<ClassificationRow> PickStratifiedSample(List<ClassificationRow> rows, double fraction)
        {
            List<ClassificationRow> sample = [];
            foreach (string familyLetter in new[] { "A", "B", "C", "D" })
            {
                List<ClassificationRow> familyRows = rows
                    .Where(r => r.FamilyLetter == familyLetter)
                    .OrderBy(r => r.MagicId)
                    .ToList();
                int target = Math.Max(1, (int)Math.Ceiling(familyRows.Count * fraction));
                for (int i = 0; i < target; i++)
                {
                    int idx = (i * familyRows.Count) / target;
                    sample.Add(familyRows[idx]);
                }
            }

            return sample.OrderBy(r => r.MagicId).ToList();
        }

        static List<ClassificationRow> LoadClassification(string csvPath)
        {
            List<ClassificationRow> rows = [];
            foreach (string line in File.ReadLines(csvPath).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] parts = SplitCsvLine(line);
                if (parts.Length < 3)
                    continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int magicId))
                    continue;
                string family = parts[2].Trim();
                string letter = family switch
                {
                    "A" => "A",
                    "B" => "B",
                    "C" or "C_heavy" or "C_light" => "C",
                    "D" => "D",
                    _ => family.Length > 0 ? family[0].ToString() : "?"
                };
                rows.Add(new ClassificationRow(magicId, parts[1].Trim(), family, letter));
            }

            return rows;
        }

        static string[] SplitCsvLine(string line)
        {
            List<string> parts = [];
            StringBuilder current = new();
            bool quoted = false;
            foreach (char ch in line)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (ch == ',' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            parts.Add(current.ToString());
            return parts.ToArray();
        }

        static void WriteCorpusArtifacts(
            List<MagicDllLogicalDecompileResult> results,
            List<ClassificationRow> allRows,
            List<ClassificationRow> targets,
            List<string> failures,
            string outputDir,
            double fraction,
            bool allCorpus)
        {
            string samplePath = Path.Combine(outputDir, "wave1_sample_manifest.json");
            var manifest = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                mode = allCorpus ? "all" : "stratified_sample",
                fraction,
                targetCount = targets.Count,
                successCount = results.Count,
                failureCount = failures.Count,
                targets = targets.Select(t => new { t.MagicId, t.Dll, t.Family, t.FamilyLetter }),
                failures
            };
            File.WriteAllText(samplePath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            string csvPath = Path.Combine(outputDir, "wave1_logical_decompile_summary.csv");
            List<string> csvLines =
            [
                "magic_id,dll,family,slot_kind,slot0_hash,slot1_hash,active_code,stub_slots,proven_slots,partial_slots,engine_strings"
            ];
            foreach (MagicDllLogicalDecompileResult r in results.OrderBy(r => r.MagicId))
            {
                int proven = r.Slots.Count(s => s.MatchLevel == "proven_fingerprint");
                int partial = r.Slots.Count(s => s.MatchLevel == "partial_fingerprint");
                csvLines.Add(string.Join(",",
                    r.MagicIdText,
                    r.FileName,
                    r.Family,
                    Csv(r.SlotKindSignature),
                    r.Slot0CodeSha256,
                    r.Slot1CodeSha256,
                    r.ActiveCodeSlots.ToString(CultureInfo.InvariantCulture),
                    r.StubSlots.ToString(CultureInfo.InvariantCulture),
                    proven.ToString(CultureInfo.InvariantCulture),
                    partial.ToString(CultureInfo.InvariantCulture),
                    r.EngineStrings.Count.ToString(CultureInfo.InvariantCulture)));
            }

            File.WriteAllText(csvPath, string.Join(Environment.NewLine, csvLines) + Environment.NewLine);

            string mdPath = Path.Combine(outputDir, "MAGIC_DLL_LOGICAL_DECOMPILE_WAVE1.md");
            File.WriteAllText(mdPath, BuildMasterMarkdown(results, allRows, targets, failures, fraction, allCorpus));
        }

        static string BuildMasterMarkdown(
            List<MagicDllLogicalDecompileResult> results,
            List<ClassificationRow> allRows,
            List<ClassificationRow> targets,
            List<string> failures,
            double fraction,
            bool allCorpus)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL — Logical Decompile Wave 1");
            sb.AppendLine();
            sb.AppendLine($"Generated UTC: `{DateTimeOffset.UtcNow:O}` · Lane: **Jarvis-MAGIC**");
            sb.AppendLine();
            sb.AppendLine("## Scope");
            sb.AppendLine();
            sb.AppendLine($"- Corpus classified: **{allRows.Count}** DLLs (A/B/C/D)");
            sb.AppendLine($"- This wave: **{(allCorpus ? "full corpus" : $"{fraction:P0} stratified per family")}** → **{targets.Count}** targets, **{results.Count}** decoded");
            sb.AppendLine("- Method: PE overlay slot scan + host-offset fingerprint + family template pseudocode (no Hex-Rays)");
            sb.AppendLine();
            sb.AppendLine("## Per-family coverage this wave");
            sb.AppendLine();
            sb.AppendLine("| Family | Corpus | Wave targets | Decoded | Proven fingerprint ≥1 slot |");
            sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (string fam in new[] { "A", "B", "C", "D" })
            {
                int corpus = allRows.Count(r => r.FamilyLetter == fam);
                int wave = targets.Count(t => t.FamilyLetter == fam);
                var decoded = results.Where(r => FamilyLetter(r.Family) == fam).ToList();
                int withProven = decoded.Count(r => r.HasProvenFingerprint);
                sb.AppendLine($"| **{fam}** | {corpus} | {wave} | {decoded.Count} | {withProven} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Slot0 code clusters (top hashes per family)");
            sb.AppendLine();
            foreach (string fam in new[] { "A", "B", "C", "D" })
            {
                sb.AppendLine($"### Family {fam}");
                var clusters = results
                    .Where(r => FamilyLetter(r.Family) == fam && !string.IsNullOrEmpty(r.Slot0CodeSha256))
                    .GroupBy(r => r.Slot0CodeSha256)
                    .OrderByDescending(g => g.Count())
                    .Take(8);
                foreach (var g in clusters)
                    sb.AppendLine($"- `{g.Key[..16]}…` × **{g.Count()}** (e.g. {string.Join(", ", g.Take(5).Select(x => x.MagicIdText))})");
                sb.AppendLine();
            }

            if (failures.Count > 0)
            {
                sb.AppendLine("## Failures");
                sb.AppendLine();
                foreach (string f in failures.Take(20))
                    sb.AppendLine($"- {f}");
                sb.AppendLine();
            }

            sb.AppendLine("## Next wave (Hex-Rays)");
            sb.AppendLine();
            sb.AppendLine("- Pick 1 representative per slot0 hash cluster → IDA decompile slots 0/1/3/4");
            sb.AppendLine("- Validate `proven_fingerprint` vs `template_only` rows from this wave");
            sb.AppendLine("- Promote host-offset hits to named callbacks in `.i64` + `docs/reverse/`");
            return sb.ToString();
        }

        static void PrintSummary(List<MagicDllLogicalDecompileResult> results, List<ClassificationRow> targets, List<string> failures, string outputDir)
        {
            Console.WriteLine($"decoded : {results.Count}/{targets.Count}");
            Console.WriteLine($"proven fingerprint (≥1 slot): {results.Count(r => r.HasProvenFingerprint)}");
            foreach (string fam in new[] { "A", "B", "C", "D" })
            {
                int n = results.Count(r => FamilyLetter(r.Family) == fam);
                int proven = results.Count(r => FamilyLetter(r.Family) == fam && r.HasProvenFingerprint);
                Console.WriteLine($"  family {fam}: {n} decoded, {proven} with proven slot fingerprint");
            }

            if (failures.Count > 0)
                Console.WriteLine($"failures: {failures.Count} (see manifest)");
            Console.WriteLine($"output: {outputDir}");
        }

        static string FamilyLetter(string familyEnum) => familyEnum switch
        {
            _ when familyEnum.StartsWith("A_", StringComparison.Ordinal) => "A",
            _ when familyEnum.StartsWith("B_", StringComparison.Ordinal) => "B",
            _ when familyEnum.StartsWith("C_", StringComparison.Ordinal) => "C",
            _ when familyEnum.StartsWith("D_", StringComparison.Ordinal) => "D",
            _ => "?"
        };

        static string FindRepoRoot()
        {
            return FindRepoRootPublic();
        }

        public static string FindRepoRootPublic()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8; i++)
            {
                if (File.Exists(Path.Combine(dir, "PORT_STATUS.md")))
                    return dir;
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                    break;
                dir = parent;
            }

            return Directory.GetCurrentDirectory();
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

        static string Csv(string value)
        {
            value ??= string.Empty;
            if (value.Contains('"') || value.Contains(','))
                return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return value;
        }

        sealed record ClassificationRow(int MagicId, string Dll, string Family, string FamilyLetter);
    }
}
