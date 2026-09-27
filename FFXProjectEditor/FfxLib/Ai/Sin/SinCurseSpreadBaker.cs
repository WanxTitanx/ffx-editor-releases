using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // Offline v0.5 bake — reads a spira-sin-spread manifest and patches mod-folder m###.bin for infected slots.
    public static class SinCurseSpreadBaker
    {
        public const string BackupSuffix = ".spiraforge.bak";

        public sealed record BakeOptions
        {
            public required string SpreadJsonPath { get; init; }
            public string? ModMonRoot { get; init; }
            /// <summary>ffx_ps2 root — used to seed mod copies from vanilla when missing.</summary>
            public string? VanillaFfxPs2Root { get; init; }
            public bool DryRun { get; init; }
            public bool SkipUnmapped { get; init; } = true;
            public bool ApplyPossessedOpener { get; init; }
            public string? ReportPath { get; init; }
        }

        public sealed record SlotBakeResult
        {
            public required string MonsterId { get; init; }
            public string? PresetId { get; init; }
            public required string Outcome { get; init; }
            public string? TargetPath { get; init; }
            public string? Detail { get; init; }
            public int AddedRows { get; init; }
        }

        public sealed record BakeReport
        {
            public required string SpreadPath { get; init; }
            public required string RegionId { get; init; }
            public required int DerivedSeed { get; init; }
            public required IReadOnlyList<SlotBakeResult> Slots { get; init; }
            public required BakeSummary Summary { get; init; }
        }

        public sealed record BakeSummary
        {
            public int Infected { get; init; }
            public int Written { get; init; }
            public int DryRunOk { get; init; }
            public int Skipped { get; init; }
            public int Failed { get; init; }
        }

        public static BakeReport Bake(BakeOptions options)
        {
            SpreadDocument doc = LoadSpread(options.SpreadJsonPath);
            string modRoot = options.ModMonRoot ?? DefaultModMonRoot();
            var results = new List<SlotBakeResult>();
            int written = 0, dryOk = 0, skipped = 0, failed = 0;

            foreach (SpreadAssignment row in doc.Assignments.Where(a => a.Infected))
            {
                string monsterId = NormalizeMonsterId(row.MonsterId);
                string? presetId = row.PresetId;
                if (string.IsNullOrWhiteSpace(presetId))
                {
                    failed++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "failed",
                        Detail = "infected row missing preset_id",
                    });
                    continue;
                }

                SinPresetRecipeResolver.ResolveResult recipeRes = SinPresetRecipeResolver.Resolve(presetId);
                if (!recipeRes.Ok)
                {
                    if (options.SkipUnmapped)
                    {
                        skipped++;
                        results.Add(new SlotBakeResult
                        {
                            MonsterId = monsterId,
                            PresetId = presetId,
                            Outcome = "skipped",
                            Detail = recipeRes.BlockReason,
                        });
                    }
                    else
                    {
                        failed++;
                        results.Add(new SlotBakeResult
                        {
                            MonsterId = monsterId,
                            PresetId = presetId,
                            Outcome = "failed",
                            Detail = recipeRes.BlockReason,
                        });
                    }

                    continue;
                }

                if (!TryResolvePaths(modRoot, options.VanillaFfxPs2Root, monsterId, options.DryRun,
                        out string readPath, out string writePath, out string? pathErr))
                {
                    failed++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "failed",
                        Detail = pathErr,
                    });
                    continue;
                }

                byte[] sourceBytes;
                try { sourceBytes = File.ReadAllBytes(readPath); }
                catch (Exception ex)
                {
                    failed++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "failed",
                        TargetPath = writePath,
                        Detail = ex.Message,
                    });
                    continue;
                }

                SinMonsterEmitResult emit = options.ApplyPossessedOpener
                    ? SinPossessedOpener.TryEmitWithPossessedOpener(sourceBytes, recipeRes.Recipe!)
                    : SinSandboxApplySession.TryEmitInMemory(sourceBytes, recipeRes.Recipe!, modAuthoringBake: true);
                if (!emit.Ok)
                {
                    failed++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "failed",
                        TargetPath = writePath,
                        Detail = emit.Error,
                    });
                    continue;
                }

                if (options.DryRun)
                {
                    dryOk++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "dry-run-ok",
                        TargetPath = writePath,
                        Detail = $"{recipeRes.Source} · read={readPath} · {emit.WorkerResolution}",
                        AddedRows = emit.AddedRows,
                    });
                    continue;
                }

                try
                {
                    string backupPath = writePath + BackupSuffix;
                    if (File.Exists(writePath))
                        File.Copy(writePath, backupPath, overwrite: true);
                    File.WriteAllBytes(writePath, emit.EditedMonster!);
                    written++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "written",
                        TargetPath = writePath,
                        Detail = $"{recipeRes.Source} · backup={Path.GetFileName(backupPath)}",
                        AddedRows = emit.AddedRows,
                    });
                }
                catch (Exception ex)
                {
                    failed++;
                    results.Add(new SlotBakeResult
                    {
                        MonsterId = monsterId,
                        PresetId = presetId,
                        Outcome = "failed",
                        TargetPath = writePath,
                        Detail = $"write failed: {ex.Message}",
                    });
                }
            }

            var report = new BakeReport
            {
                SpreadPath = Path.GetFullPath(options.SpreadJsonPath),
                RegionId = doc.RegionId ?? "",
                DerivedSeed = doc.DerivedSeed,
                Slots = results,
                Summary = new BakeSummary
                {
                    Infected = doc.Assignments.Count(a => a.Infected),
                    Written = written,
                    DryRunOk = dryOk,
                    Skipped = skipped,
                    Failed = failed,
                },
            };

            if (!string.IsNullOrWhiteSpace(options.ReportPath) && !options.DryRun)
                File.WriteAllText(options.ReportPath, ToReportJson(report));

            return report;
        }

        public static string FormatHumanReport(BakeReport report)
        {
            var lines = new List<string>
            {
                $"=== Sin Curse bake — {report.RegionId} ===",
                $"spread={report.SpreadPath}",
                $"derived_seed={report.DerivedSeed}",
                $"infected={report.Summary.Infected}  written={report.Summary.Written}  dry_ok={report.Summary.DryRunOk}  skipped={report.Summary.Skipped}  failed={report.Summary.Failed}",
                "",
            };

            foreach (SlotBakeResult s in report.Slots)
            {
                lines.Add($"  {s.MonsterId,-6} {s.PresetId,-8} {s.Outcome,-12} +{s.AddedRows} rows  {s.Detail}");
                if (!string.IsNullOrEmpty(s.TargetPath))
                    lines.Add($"           → {s.TargetPath}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static string DefaultModMonRoot() =>
            Path.Combine(
                SinCurseSpreadPlanner.FindRepoRoot(),
                "mods", "Spira Reforge", "data", "mods", "ffx_ps2",
                "ffx", "master", "jppc", "battle", "mon");

        static bool TryResolvePaths(
            string modRoot,
            string? vanillaRoot,
            string monsterId,
            bool dryRun,
            out string readPath,
            out string writePath,
            out string? error)
        {
            writePath = Path.Combine(modRoot, "_" + monsterId, monsterId + ".bin");
            if (File.Exists(writePath))
            {
                readPath = writePath;
                error = null;
                return true;
            }

            if (string.IsNullOrWhiteSpace(vanillaRoot))
            {
                readPath = writePath;
                error = $"mod file missing ({writePath}) and no --vanilla-root to read/seed from";
                return false;
            }

            readPath = Path.Combine(
                vanillaRoot, "ffx", "master", "jppc", "battle", "mon", "_" + monsterId, monsterId + ".bin");
            if (!File.Exists(readPath))
            {
                error = $"vanilla monster not found: {readPath}";
                return false;
            }

            if (dryRun)
            {
                error = null;
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
                File.Copy(readPath, writePath, overwrite: false);
                readPath = writePath;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"seed copy failed: {ex.Message}";
                return false;
            }
        }

        static SpreadDocument LoadSpread(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Spread JSON not found: {path}");

            string json = File.ReadAllText(path);
            SpreadDocument? doc = JsonSerializer.Deserialize<SpreadDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Spread JSON deserialized to null");
            if (doc.Assignments.Count == 0)
                throw new InvalidDataException("Spread has no assignments");
            return doc;
        }

        static string ToReportJson(BakeReport report)
        {
            var payload = new
            {
                format = "spira-sin-bake",
                format_version = 1,
                generated_utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                region_id = report.RegionId,
                derived_seed = report.DerivedSeed,
                spread_path = report.SpreadPath,
                summary = new
                {
                    infected = report.Summary.Infected,
                    written = report.Summary.Written,
                    skipped = report.Summary.Skipped,
                    failed = report.Summary.Failed,
                },
                slots = report.Slots.Select(s => new
                {
                    monster_id = s.MonsterId,
                    preset_id = s.PresetId,
                    outcome = s.Outcome,
                    target_path = s.TargetPath,
                    detail = s.Detail,
                    added_rows = s.AddedRows,
                }).ToList(),
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        static string NormalizeMonsterId(string raw)
        {
            string s = raw.Trim().ToLowerInvariant();
            if (s.StartsWith('m'))
                return s;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return $"m{n:D3}";
            return s;
        }

        sealed class SpreadDocument
        {
            [JsonPropertyName("region_id")]
            public string? RegionId { get; set; }

            [JsonPropertyName("derived_seed")]
            public int DerivedSeed { get; set; }

            [JsonPropertyName("assignments")]
            public List<SpreadAssignment> Assignments { get; set; } = new();
        }

        sealed class SpreadAssignment
        {
            [JsonPropertyName("monster_id")]
            public required string MonsterId { get; init; }

            [JsonPropertyName("infected")]
            public bool Infected { get; init; }

            [JsonPropertyName("preset_id")]
            public string? PresetId { get; init; }
        }

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };
    }
}
