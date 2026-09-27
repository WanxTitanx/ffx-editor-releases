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
    /// Headless RT2 prep for magic_0084 (family A) and magic_0098 (family C):
    /// scan DLL for candidate floats/vec4, emit patch plans + patched output DLLs (lab only).
    /// </summary>
    internal static class MagicDllRt2VisualPrepRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\magic_rt2_visual_prep";

        static readonly int[] TargetIds = [84, 98];

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                Console.WriteLine("=== Magic DLL RT2 Visual PREP (0084 + 0098) ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"output     : {outputDir}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                List<Rt2PrepReport> reports = [];
                int pass = 0;

                foreach (int magicId in TargetIds)
                {
                    string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                    if (!File.Exists(dllPath))
                    {
                        Console.WriteLine($"FAIL: missing {dllPath}");
                        continue;
                    }

                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                    MagicDllFamilyClassification family = MagicDllFamilyClassifier.Classify(inspection);
                    MagicDllLogicalDecompileResult logical = MagicDllLogicalDecompiler.Decompile(inspection);

                    List<Vec4Hit> vec4Hits = FindWhiteVec4Candidates(inspection);
                    List<FloatHit> scaleHits = FindScaleCandidates(inspection);
                    List<FloatHit> accelHits = FindStringNeighborFloats(inspection, "pppAccele", 256);

                    MagicDllPatchPlan plan = new() { BytePatches = [] };
                    string spellDir = Path.Combine(outputDir, $"magic_{magicId:D4}");
                    Directory.CreateDirectory(spellDir);

                    if (magicId == 84)
                    {
                        Vec4Hit? color = vec4Hits.FirstOrDefault();
                        if (color != null)
                        {
                            plan.BytePatches.Add(new MagicDllBytePatch
                            {
                                FileOffset = color.FileOffset,
                                Rva = color.Rva,
                                Hex = FloatToHex(1f, 0f, 0f, 1f),
                                Note = "RT2-0084-01: white vec4 -> red (candidate color)"
                            });
                        }

                        FloatHit? scale = scaleHits.FirstOrDefault(h => Math.Abs(h.Value - 1f) < 0.001f)
                            ?? scaleHits.FirstOrDefault();
                        if (scale != null)
                        {
                            plan.BytePatches.Add(new MagicDllBytePatch
                            {
                                FileOffset = scale.FileOffset,
                                Rva = scale.Rva,
                                Hex = FloatToHex(2f),
                                Note = "RT2-0084-02: scale 1.0 -> 2.0 (candidate draw scale)"
                            });
                        }
                    }
                    else if (magicId == 98)
                    {
                        FloatHit? accel = accelHits.FirstOrDefault(h => h.Value > 0f && h.Value <= 4f)
                            ?? accelHits.FirstOrDefault();
                        if (accel != null)
                        {
                            float doubled = accel.Value * 2f;
                            plan.BytePatches.Add(new MagicDllBytePatch
                            {
                                FileOffset = accel.FileOffset,
                                Rva = accel.Rva,
                                Hex = FloatToHex(doubled),
                                Note = $"RT2-0098-01: pppAccele neighbor {accel.Value:G} -> {doubled:G}"
                            });
                        }
                    }

                    string planPath = Path.Combine(spellDir, "rt2_patch_plan.json");
                    File.WriteAllText(planPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

                    string patchedPath = Path.Combine(spellDir, $"magic_{magicId:D4}_rt2_lab.dll");
                    bool applied = false;
                    string applySummary = "no patches";
                    if (plan.BytePatches.Count > 0)
                    {
                        MagicDllCompileResult compile = MagicDllDecompiler.ApplyPatchPlan(dllPath, planPath, patchedPath);
                        applied = compile.Pass;
                        applySummary = compile.Summary;
                    }

                    string mdPath = Path.Combine(spellDir, "RT2_PREP.md");
                    File.WriteAllText(mdPath, BuildMarkdown(magicId, inspection, family, logical, vec4Hits, scaleHits, accelHits, plan, patchedPath, applied, applySummary));

                    bool spellPass = plan.BytePatches.Count > 0 && applied;
                    if (spellPass)
                        pass++;

                    reports.Add(new Rt2PrepReport(magicId, family.Family.ToString(), plan.BytePatches.Count, applied, planPath, patchedPath, mdPath));
                    Console.WriteLine($"magic_{magicId:D4}: family={family.Family} patches={plan.BytePatches.Count} applied={applied}");
                }

                string summaryPath = Path.Combine(outputDir, "RT2_VISUAL_PREP_SUMMARY.md");
                File.WriteAllText(summaryPath, BuildSummary(reports));

                Console.WriteLine($"summary: {summaryPath}");
                Console.WriteLine(pass == TargetIds.Length
                    ? "VERDICT: PASS — patch plans + lab DLLs for 0084 and 0098"
                    : $"VERDICT: PARTIAL — {pass}/{TargetIds.Length} spells ready (check vec4/float hits)");
                return pass == TargetIds.Length ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static List<Vec4Hit> FindWhiteVec4Candidates(MagicDllInspection inspection)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            List<Vec4Hit> hits = [];
            foreach (MagicDllSection section in inspection.Sections.Where(s => !s.Name.Equals(".text", StringComparison.OrdinalIgnoreCase)))
            {
                int start = Math.Max(0, section.RawPointer);
                int end = Math.Min(bytes.Length, section.RawPointer + section.RawSize);
                for (int offset = start; offset <= end - 16; offset += 4)
                {
                    if (!IsOne(bytes, offset) || !IsOne(bytes, offset + 4) || !IsOne(bytes, offset + 8) || !IsOne(bytes, offset + 12))
                        continue;
                    hits.Add(new Vec4Hit(offset, TryRva(inspection, offset), section.Name, 1f, 1f, 1f, 1f));
                }
            }

            return hits.OrderBy(h => h.FileOffset).Take(32).ToList();
        }

        static List<FloatHit> FindScaleCandidates(MagicDllInspection inspection)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            List<FloatHit> hits = [];
            foreach (MagicDllSection section in inspection.Sections)
            {
                int start = Math.Max(0, section.RawPointer);
                int end = Math.Min(bytes.Length, section.RawPointer + section.RawSize);
                for (int offset = start; offset <= end - 4; offset += 4)
                {
                    float v = BitConverter.ToSingle(bytes, offset);
                    if (v is < 0.25f or > 8f)
                        continue;
                    if (Math.Abs(v - MathF.Round(v)) > 0.001f && v is not (>= 0.9f and <= 1.1f))
                        continue;
                    hits.Add(new FloatHit(offset, TryRva(inspection, offset), section.Name, v, "scale/timer candidate"));
                }
            }

            return hits
                .GroupBy(h => h.FileOffset)
                .Select(g => g.First())
                .OrderByDescending(h => Math.Abs(h.Value - 1f) < 0.01f ? 10 : 0)
                .ThenBy(h => h.FileOffset)
                .Take(48)
                .ToList();
        }

        static List<FloatHit> FindStringNeighborFloats(MagicDllInspection inspection, string anchor, int windowBytes)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            List<FloatHit> hits = [];
            foreach (MagicDllAsciiString s in inspection.Strings.Where(s => s.Value.Contains(anchor, StringComparison.OrdinalIgnoreCase)))
            {
                int center = s.FileOffset;
                int start = Math.Max(0, center - windowBytes);
                int end = Math.Min(bytes.Length, center + windowBytes);
                for (int offset = start; offset <= end - 4; offset += 4)
                {
                    float v = BitConverter.ToSingle(bytes, offset);
                    if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 10000f)
                        continue;
                    hits.Add(new FloatHit(offset, TryRva(inspection, offset), s.Section, v, $"near '{anchor}' @0x{s.FileOffset:X}"));
                }
            }

            return hits
                .GroupBy(h => h.FileOffset)
                .Select(g => g.First())
                .OrderBy(h => h.FileOffset)
                .Take(64)
                .ToList();
        }

        static bool IsOne(byte[] bytes, int offset) =>
            offset + 4 <= bytes.Length && BitConverter.ToSingle(bytes, offset) is >= 0.99f and <= 1.01f;

        static int TryRva(MagicDllInspection inspection, int fileOffset)
        {
            foreach (MagicDllSection section in inspection.Sections)
            {
                int start = section.RawPointer;
                int end = start + section.RawSize;
                if (fileOffset >= start && fileOffset < end)
                    return section.VirtualAddress + (fileOffset - start);
            }

            return 0;
        }

        static string FloatToHex(params float[] values) =>
            string.Join(" ", values.SelectMany(v => BitConverter.GetBytes(v).Select(b => b.ToString("X2", CultureInfo.InvariantCulture))));

        static string BuildMarkdown(
            int magicId,
            MagicDllInspection inspection,
            MagicDllFamilyClassification family,
            MagicDllLogicalDecompileResult logical,
            IReadOnlyList<Vec4Hit> vec4,
            IReadOnlyList<FloatHit> scale,
            IReadOnlyList<FloatHit> accel,
            MagicDllPatchPlan plan,
            string patchedPath,
            bool applied,
            string applySummary)
        {
            StringBuilder sb = new();
            sb.AppendLine($"# RT2 Visual Prep — magic_{magicId:D4}");
            sb.AppendLine();
            sb.AppendLine($"- Family: `{family.Family}` ({family.Method})");
            sb.AppendLine($"- SHA256: `{inspection.Sha256}`");
            sb.AppendLine($"- Logical slots: {logical.Slots.Count(s => !s.IsStub)} code / {logical.Slots.Count(s => s.IsStub)} stub");
            sb.AppendLine($"- Patches planned: **{plan.BytePatches.Count}**");
            sb.AppendLine($"- Lab DLL applied: **{applied}** — {applySummary}");
            sb.AppendLine($"- Output: `{patchedPath}`");
            sb.AppendLine();
            sb.AppendLine("## Planned byte patches");
            sb.AppendLine();
            foreach (MagicDllBytePatch p in plan.BytePatches)
                sb.AppendLine($"- `@0x{p.FileOffset:X}` RVA `0x{p.Rva:X}` — {p.Note} — `{p.Hex}`");
            sb.AppendLine();
            sb.AppendLine("## Top white vec4 hits (0084 color hunt)");
            sb.AppendLine();
            foreach (Vec4Hit h in vec4.Take(8))
                sb.AppendLine($"- `@0x{h.FileOffset:X}` RVA `0x{h.Rva:X}` [{h.Section}] ({h.R},{h.G},{h.B},{h.A})");
            sb.AppendLine();
            sb.AppendLine("## Top scale float hits");
            sb.AppendLine();
            foreach (FloatHit h in scale.Take(8))
                sb.AppendLine($"- `@0x{h.FileOffset:X}` RVA `0x{h.Rva:X}` = `{h.Value:G}` — {h.Note}");
            sb.AppendLine();
            sb.AppendLine("## Floats near pppAccele (0098)");
            sb.AppendLine();
            foreach (FloatHit h in accel.Take(12))
                sb.AppendLine($"- `@0x{h.FileOffset:X}` RVA `0x{h.Rva:X}` = `{h.Value:G}` — {h.Note}");
            sb.AppendLine();
            sb.AppendLine("## In-game (human RT2)");
            sb.AppendLine();
            sb.AppendLine("1. Backup `magic_####.dll`");
            sb.AppendLine("2. Copy lab DLL from this folder → `magicFiles\\FFX\\`");
            sb.AppendLine("3. Trigger spell in battle; fill RT2 table in `FFX_MAGIC_RT2_VISUAL_0084_0098_2026-06-14.md`");
            return sb.ToString();
        }

        static string BuildSummary(IReadOnlyList<Rt2PrepReport> reports)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic RT2 Visual Prep — Summary");
            sb.AppendLine();
            sb.AppendLine($"Generated: `{DateTimeOffset.UtcNow:O}` · Lane **Jarvis-MAGIC**");
            sb.AppendLine();
            foreach (Rt2PrepReport r in reports)
            {
                sb.AppendLine($"## magic_{r.MagicId:D4}");
                sb.AppendLine($"- Family: `{r.Family}`");
                sb.AppendLine($"- Patches: {r.PatchCount} · Applied: {r.Applied}");
                sb.AppendLine($"- Plan: `{r.PlanPath}`");
                sb.AppendLine($"- Lab DLL: `{r.PatchedDll}`");
                sb.AppendLine($"- Report: `{r.ReportPath}`");
                sb.AppendLine();
            }

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

        sealed record Vec4Hit(int FileOffset, int Rva, string Section, float R, float G, float B, float A);
        sealed record FloatHit(int FileOffset, int Rva, string Section, float Value, string Note);
        sealed record Rt2PrepReport(int MagicId, string Family, int PatchCount, bool Applied, string PlanPath, string PatchedDll, string ReportPath);
    }
}
