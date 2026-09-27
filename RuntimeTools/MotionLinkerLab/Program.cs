using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionLinkerLab;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string DefaultPs2Root = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc";
    private const string DefaultPs3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data";
    private const string DefaultOutput = @"work\motion_linker_lab";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());

            return command switch
            {
                "manifest" => RunManifest(options),
                "codec-probe" => RunCodecProbe(options),
                "codec-phase2" => RunCodecPhase2(options),
                "dump-triples" => RunDumpTriples(options),
                "dump-grid" => RunDumpGrid(options),
                "ptrb-header" => RunPtrBHeader(options),
                "group-channel-map" => RunGroupChannelMap(options),
                "grouping-rank" => RunGroupingRank(options),
                "motion-semantics" => RunMotionSemantics(options),
                "remap-attack" => RunRemapAttack(options),
                "timing-law" => RunTimingLaw(options),
                "decode-candidate" => RunDecodeCandidate(options),
                "unsafe-lab-package" => RunUnsafeLabPackage(options),
                "cross-corpus-regression" => RunCrossCorpusRegression(options),
                "smoke" => RunSmoke(options),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int RunSmoke(IReadOnlyDictionary<string, string> options)
    {
        var smokeOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
        {
            ["monsters"] = options.TryGetValue("monsters", out var monsters) ? monsters : "m018,m020,m106,m162,m211",
        };

        return RunManifest(smokeOptions);
    }

    private static int RunManifest(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var ps3Root = Path.GetFullPath(options.TryGetValue("ps3-root", out var rawPs3Root) ? rawPs3Root : DefaultPs3Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : DefaultOutput);

        var monsters = ResolveMonsterIds(ps2Root, options);
        if (monsters.Count == 0)
        {
            throw new InvalidOperationException("No monsters selected. Use --monsters m018,m211 or ensure the PS2 root exists.");
        }

        Directory.CreateDirectory(outputRoot);

        var manifests = new List<MonsterMotionManifest>();
        foreach (var monsterId in monsters)
        {
            var manifest = MonsterMotionLinker.Build(ps2Root, ps3Root, monsterId);
            manifests.Add(manifest);

            var manifestPath = Path.Combine(outputRoot, $"{monsterId}.motion-link.json");
            WriteJson(manifestPath, manifest);
        }

        var index = new MotionLinkerIndex(
            DateTimeOffset.UtcNow,
            ps2Root,
            ps3Root,
            manifests.Count,
            manifests.Count(item => item.Ps2.Motion.NonEmptyResidentCount > 0),
            manifests.Count(item => item.Ps3.HasEmbeddedAnimationClip),
            manifests.Select(item => new MotionLinkerIndexEntry(
                item.MonsterId,
                item.Linkage.IdentityStatus,
                item.Linkage.MotionReadiness,
                item.Ps2.Motion.NonEmptyResidentCount,
                item.Ps3.EmbeddedAnimationClipCount,
                Path.Combine(outputRoot, $"{item.MonsterId}.motion-link.json"))).ToArray());

        var indexPath = Path.Combine(outputRoot, "index.motion-link.json");
        WriteJson(indexPath, index);

        PrintJson(new
        {
            indexPath,
            index.MonsterCount,
            index.WithPs2ResidentMotion,
            index.WithPs3EmbeddedAnimation,
            monsters = manifests.Select(item => new
            {
                item.MonsterId,
                item.Linkage.IdentityStatus,
                item.Linkage.MotionReadiness,
                item.Ps2.Motion.NonEmptyResidentCount,
                item.Ps3.EmbeddedAnimationClipCount,
            }),
        });

        return 0;
    }

    private static int RunCodecProbe(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\motion_codec_probe");
        var monsters = options.ContainsKey("monsters")
            ? ResolveMonsterIds(ps2Root, options)
            : new[] { "m211", "m162", "m106" };

        Directory.CreateDirectory(outputRoot);

        var report = MgrpCodecProbe.Run(ps2Root, monsters);
        var reportPath = Path.Combine(outputRoot, "mgrp-codec-probe.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.MonsterCount,
            report.RecordCount,
            report.Float32RecordsGte10,
            report.Float32RecordsGte05,
            report.Int16CleanGridRecords,
            report.SmallestThreeTopScoreGte75,
            report.EulerFixedTopScoreGte75,
            report.DecisionBand,
            report.Float32Verdict,
            report.Int16Verdict,
            report.SmallestThreeVerdict,
            report.EulerFixedVerdict,
            monsters = report.Monsters.Select(item => new
            {
                item.MonsterId,
                item.NonEmptyResidentCount,
                item.RecordCount,
                BestSmallestThree = item.TopSmallestThreeCandidates.FirstOrDefault(),
                BestEulerFixed = item.TopEulerFixedCandidates.FirstOrDefault(),
            }),
        });

        return 0;
    }

    private static int RunCodecPhase2(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\motion_codec_phase2");
        var monsters = options.ContainsKey("monsters")
            ? ResolveMonsterIds(ps2Root, options)
            : new[] { "m211", "m162", "m106", "m064", "m046", "m130" };

        Directory.CreateDirectory(outputRoot);

        var report = MgrpCodecPhase2Probe.Run(ps2Root, monsters);
        var reportPath = Path.Combine(outputRoot, "mgrp-codec-phase2.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.MonsterCount,
            report.RecordCount,
            report.SegmentCount,
            report.CandidateCount,
            report.StrongSubstreamCandidates,
            report.BindPosePresentCount,
            report.DecisionBand,
            report.Phase2Verdict,
            topCandidates = report.TopCandidates.Take(8).Select(item => new
            {
                item.MonsterId,
                item.Slot,
                item.RecordIndex,
                item.SegmentLabel,
                item.AbsoluteStart,
                item.LocalByteSkip,
                item.BitsPerComponent,
                item.BitOffset,
                item.SampleCount,
                item.StructuralScore,
                item.BindFitScore,
                item.Score,
                item.BestKnownScaleName,
            }),
        });

        return 0;
    }

    private static int RunDumpTriples(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\motion_codec_candidate_dumps");

        var request = new TripleDumpRequest(
            NormalizeMonsterId(RequireOption(options, "monster")),
            ParseIntOption(options, "slot"),
            ParseIntOption(options, "record"),
            RequireOption(options, "segment"),
            ParseIntOption(options, "skip"),
            ParseIntOption(options, "bits"),
            ParseIntOption(options, "bit-offset"),
            options.TryGetValue("samples", out var rawSamples) ? int.Parse(rawSamples) : 128);

        var report = MgrpTripleDumper.Dump(ps2Root, outputRoot, request);
        var reportPath = Path.Combine(
            outputRoot,
            $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_{request.SegmentLabel.Replace('+', '_')}_skip{request.LocalByteSkip}_b{request.BitsPerComponent}_bit{request.BitOffset}.triple-dump.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.CsvPath,
            report.SampleCount,
            report.AbsoluteDataStart,
            report.Stats.AverageDelta,
            report.Stats.MeanVectorMagnitude,
            report.Stats.MeanAxisRange,
        });

        return 0;
    }

    private static int RunDumpGrid(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\motion_codec_candidate_dumps_grid");

        var request = new TripleDumpGridRequest(
            NormalizeMonsterId(RequireOption(options, "monster")),
            ParseIntOption(options, "slot"),
            ParseIntOption(options, "record"),
            ParseStringList(RequireOption(options, "segments")),
            ParseIntList(RequireOption(options, "skips")),
            ParseIntList(options.TryGetValue("bits", out var rawBits) ? rawBits : "12"),
            ParseIntList(options.TryGetValue("bit-offsets", out var rawBitOffsets) ? rawBitOffsets : "0,1,2,3,4,5,6,7"),
            options.TryGetValue("samples", out var rawSamples) ? int.Parse(rawSamples) : 128);

        var report = MgrpTripleDumpGrid.Dump(ps2Root, outputRoot, request);
        var reportPath = Path.Combine(outputRoot, $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_dump-grid-report.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.GridCsvPath,
            report.RequestCount,
            report.SuccessCount,
            report.FailureCount,
            report.DecisionBand,
            top = report.Rows
                .Where(item => item.Success)
                .OrderBy(item => item.AverageDelta)
                .Take(12)
                .Select(item => new
                {
                    item.SegmentLabel,
                    item.Skip,
                    item.Bits,
                    item.BitOffset,
                    item.SampleCount,
                    item.AverageDelta,
                    item.MeanAxisRange,
                    item.OutputStem,
                }),
        });

        return 0;
    }

    private static int RunPtrBHeader(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_ptrb_header_analysis");
        var monsterId = NormalizeMonsterId(options.TryGetValue("monster", out var rawMonster) ? rawMonster : "m046");
        var slot = options.TryGetValue("slot", out var rawSlot) ? int.Parse(rawSlot) : 1;
        var recordIndex = options.TryGetValue("record", out var rawRecord) ? int.Parse(rawRecord) : 0;
        var focusGroups = options.TryGetValue("groups", out var rawGroups)
            ? rawGroups
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToArray()
            : Array.Empty<int>();
        var headerBytes = options.TryGetValue("header-bytes", out var rawHeaderBytes) ? int.Parse(rawHeaderBytes) : 24;

        Directory.CreateDirectory(outputRoot);
        var report = MgrpPtrBHeaderAnalyzer.Analyze(ps2Root, outputRoot, monsterId, slot, recordIndex, focusGroups, headerBytes);
        var reportPath = Path.Combine(outputRoot, $"{monsterId}_slot{slot}_record{recordIndex}_ptrb-header.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.CsvPath,
            report.MonsterId,
            report.Slot,
            report.RecordIndex,
            report.HeaderByteCount,
            report.RecordChannelCount,
            report.RecordGroupCount,
            report.RowCount,
            report.Header24ShapeCount,
            report.PtrASecondWordMatchesPtrBFirstWordCount,
            report.NonZeroLengthWordCount,
            report.DecisionBand,
            focusRows = report.Rows
                .Where(item => item.IsFocusGroup)
                .Select(item => new
                {
                    item.GroupIndex,
                    item.PtrBSegmentLength,
                    item.PtrBHeaderU16,
                    item.PtrASecondWordMatchesPtrBFirstWord,
                    item.LengthWordAt16,
                    item.SegmentLengthMinusLengthWord,
                }),
        });

        return 0;
    }

    private static int RunGroupChannelMap(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_group_channel_map");
        var monsters = options.ContainsKey("monsters")
            ? ResolveMonsterIds(ps2Root, options)
            : new[] { "m046", "m211", "m106", "m162" };

        Directory.CreateDirectory(outputRoot);
        var report = MgrpGroupChannelMapper.Map(ps2Root, outputRoot, monsters);
        var reportPath = Path.Combine(outputRoot, "mgrp-group-channel-map.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.GroupCsvPath,
            report.ChannelCsvPath,
            report.MonsterCount,
            report.RecordCount,
            report.GroupRowCount,
            report.ChannelRowCount,
            report.ChannelCountEqualsGroupCountRecords,
            report.OrdinalGroupChannelRows,
            report.DecisionBand,
            records = report.Records.Select(item => new
            {
                item.MonsterId,
                item.Slot,
                item.RecordIndex,
                item.ChannelCount,
                item.GroupCount,
                item.ChannelCountEqualsGroupCount,
                item.GroupCountValues,
                item.ChannelCarryFlagOneCount,
                item.GroupsWithPtrBHeader24Shape,
                item.GroupsWithNonZeroLengthWord,
            }),
        });

        return 0;
    }

    private static int RunGroupingRank(IReadOnlyDictionary<string, string> options)
    {
        var inputRoot = Path.GetFullPath(options.TryGetValue("input", out var rawInput) ? rawInput : @"work\motion_codec_candidate_dumps_phase4");
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_frame_channel_separation");

        Directory.CreateDirectory(outputRoot);
        var report = MgrpDumpGroupingRanker.Analyze(inputRoot, outputRoot);
        var reportPath = Path.Combine(outputRoot, "mgrp-frame-channel-grouping-rank.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.CsvPath,
            report.InputRoot,
            report.DumpCount,
            report.ClearImprovementDumpCount,
            report.DecisionBand,
            dumps = report.Dumps.Select(item => new
            {
                item.DumpName,
                item.SampleCount,
                item.LinearAverageDelta,
                item.BestStrategyName,
                item.BestAverageDelta,
                item.BestImprovementRate,
                item.BestComparisonCount,
                item.DecisionBand,
            }),
        });

        return 0;
    }

    private static int RunMotionSemantics(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var inputRoot = Path.GetFullPath(options.TryGetValue("input", out var rawInput) ? rawInput : @"work\motion_codec_candidate_dumps_phase4");
        var groupingReportPath = Path.GetFullPath(options.TryGetValue("grouping-report", out var rawGrouping)
            ? rawGrouping
            : @"work\mgrp_frame_channel_separation\mgrp-frame-channel-grouping-rank.json");
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_motion_semantics");

        Directory.CreateDirectory(outputRoot);
        var report = MgrpMotionSemanticsAnalyzer.Analyze(ps2Root, inputRoot, groupingReportPath, outputRoot);
        var reportPath = Path.Combine(outputRoot, "mgrp-motion-semantics-report.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.Phase6CsvPath,
            report.Phase7CsvPath,
            report.Phase8CsvPath,
            report.Phase9CsvPath,
            report.Phase10CsvPath,
            report.DumpCount,
            report.Phase6DecisionBand,
            report.Phase7DecisionBand,
            report.Phase8DecisionBand,
            report.Phase9DecisionBand,
            report.Phase10DecisionBand,
            phase6 = report.Phase6Rows.Select(item => new
            {
                item.DumpName,
                item.BestGroupingStrategy,
                item.GroupedImprovementRate,
                item.BestMode,
                item.DecisionBand,
            }),
            phase10 = report.Phase10Rows.Select(item => new
            {
                item.DumpName,
                item.HeaderU16_0,
                item.HeaderLengthWord,
                item.PossibleTripleCount,
                item.DecisionBand,
            }),
        });

        return 0;
    }

    private static int RunRemapAttack(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_remap_attack");
        var monsters = options.ContainsKey("monsters")
            ? ResolveMonsterIds(ps2Root, options)
            : new[] { "m046", "m064", "m130", "m016", "m106", "m162", "m211" };

        Directory.CreateDirectory(outputRoot);
        var report = MgrpRemapAttacker.Analyze(ps2Root, outputRoot, monsters);
        var reportPath = Path.Combine(outputRoot, "mgrp-remap-attack.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.PolicyCsvPath,
            report.CandidateCsvPath,
            report.MonsterCount,
            report.RecordCount,
            report.DecisionBand,
            policies = report.PolicySummaries.Select(item => new
            {
                item.PolicyName,
                item.CandidateCount,
                item.InRangeRate,
                item.UniqueRate,
                item.CoherentMonsterCount,
                item.DecisionBand,
            }),
        });

        return 0;
    }

    private static int RunTimingLaw(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_timing_count_law");
        var monsters = options.ContainsKey("monsters")
            ? ResolveMonsterIds(ps2Root, options)
            : new[] { "m046", "m064", "m130", "m016", "m106", "m162", "m211" };

        Directory.CreateDirectory(outputRoot);
        var report = MgrpTimingLawAnalyzer.Analyze(ps2Root, outputRoot, monsters);
        var reportPath = Path.Combine(outputRoot, "mgrp-timing-count-law.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.CsvPath,
            report.MonsterCount,
            report.SegmentCount,
            report.U16_0PossibleFrameMonsters,
            report.U16_8LengthWordMonsters,
            report.DecisionBand,
            topPatterns = report.Patterns.Take(12),
        });

        return 0;
    }

    private static int RunDecodeCandidate(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_decode_candidate_v0");

        var request = new DecodeCandidateRequest(
            NormalizeMonsterId(RequireOption(options, "monster")),
            ParseIntOption(options, "slot"),
            ParseIntOption(options, "record"),
            RequireOption(options, "segment"),
            ParseIntOption(options, "skip"),
            ParseIntOption(options, "bits"),
            ParseIntOption(options, "bit-offset"),
            ParseIntOption(options, "stride"),
            options.TryGetValue("scale", out var rawScale) ? rawScale : "identity",
            options.TryGetValue("samples", out var rawSamples) ? int.Parse(rawSamples) : 256);

        Directory.CreateDirectory(outputRoot);
        var report = MgrpDecodeCandidateV0.Decode(ps2Root, outputRoot, request);
        var reportPath = Path.Combine(outputRoot, $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_{request.SegmentLabel.Replace('+', '_')}_decode-candidate-v0.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.RawTriplesCsvPath,
            report.GroupedCurvesCsvPath,
            report.MetadataCsvPath,
            report.SampleCount,
            report.ChannelCount,
            report.FrameCount,
            report.DecisionBand,
        });

        return 0;
    }

    private static int RunUnsafeLabPackage(IReadOnlyDictionary<string, string> options)
    {
        var ps2Root = Path.GetFullPath(options.TryGetValue("ps2-root", out var rawPs2Root) ? rawPs2Root : DefaultPs2Root);
        var dumpRoot = Path.GetFullPath(options.TryGetValue("dumps", out var rawDumps) ? rawDumps : @"work\motion_codec_candidate_dumps_phase4");
        var semanticsReportPath = Path.GetFullPath(options.TryGetValue("semantics-report", out var rawSemantics)
            ? rawSemantics
            : @"work\mgrp_motion_semantics\mgrp-motion-semantics-report.json");
        var manifestRoot = Path.GetFullPath(options.TryGetValue("manifest-root", out var rawManifest) ? rawManifest : @"work\motion_linker_lab_all");
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_unsafe_lab_package");

        Directory.CreateDirectory(outputRoot);
        var report = MgrpUnsafeLabPackager.Create(ps2Root, dumpRoot, semanticsReportPath, manifestRoot, outputRoot);
        var reportPath = Path.Combine(outputRoot, "unsafe-lab-package-report.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.PoseCsvPath,
            report.PlaybackCsvPath,
            report.ModelViewerStatusCsvPath,
            report.ExportJsonPath,
            report.ExportCurveCsvPath,
            report.GltfPaths,
            report.CandidateCount,
            report.DecisionBand,
            candidates = report.Candidates.Select(item => new
            {
                item.MonsterId,
                item.DumpName,
                item.GroupingStrategy,
                item.UnsafeChannelCount,
                item.FrameCount,
                item.DurationSeconds,
                item.DecisionBand,
            }),
        });

        return 0;
    }

    private static int RunCrossCorpusRegression(IReadOnlyDictionary<string, string> options)
    {
        var phase2ReportPath = Path.GetFullPath(options.TryGetValue("phase2-report", out var rawPhase2)
            ? rawPhase2
            : @"work\motion_codec_phase2_all\mgrp-codec-phase2.json");
        var outputRoot = Path.GetFullPath(options.TryGetValue("output", out var rawOutput) ? rawOutput : @"work\mgrp_cross_corpus_regression");

        Directory.CreateDirectory(outputRoot);
        var report = MgrpCrossCorpusRegression.Analyze(phase2ReportPath, outputRoot);
        var reportPath = Path.Combine(outputRoot, "mgrp-cross-corpus-regression.json");
        WriteJson(reportPath, report);

        PrintJson(new
        {
            reportPath,
            report.CsvPath,
            report.MonsterCount,
            report.ClassifiedMonsterCount,
            report.DecisionBand,
            families = report.FamilyCounts,
        });

        return 0;
    }

    private static IReadOnlyList<string> ResolveMonsterIds(string ps2Root, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("monsters", out var rawMonsters))
        {
            return rawMonsters
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeMonsterId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var monRoot = Path.Combine(ps2Root, "chr", "mon");
        if (!Directory.Exists(monRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(monRoot, "m???")
            .Select(path => Path.GetFileName(path) ?? "")
            .Where(id => id.Length == 4)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeMonsterId(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        if (value.StartsWith('m') && value.Length == 4)
        {
            return value;
        }

        if (int.TryParse(value.TrimStart('m'), out var id) && id >= 0 && id <= 999)
        {
            return $"m{id:000}";
        }

        throw new ArgumentException($"Invalid monster id '{raw}'. Use m018 or 18.");
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required option '--{name}'.");

    private static int ParseIntOption(IReadOnlyDictionary<string, string> options, string name) =>
        int.TryParse(RequireOption(options, name), out var value)
            ? value
            : throw new ArgumentException($"Option '--{name}' must be an integer.");

    private static string[] ParseStringList(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int[] ParseIntList(string raw) =>
        ParseStringList(raw).Select(int.Parse).ToArray();

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected token '{token}'. Expected --name value pairs.");
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for option '{token}'.");
            }

            options[token[2..]] = args[++i];
        }

        return options;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void PrintJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MotionLinkerLab commands:");
        Console.WriteLine("  manifest [--ps2-root <jppc>] [--ps3-root <ps3data>] [--output <dir>] [--monsters m018,m211]");
        Console.WriteLine("  codec-probe [--ps2-root <jppc>] [--output <dir>] [--monsters m211,m162,m106]");
        Console.WriteLine("  codec-phase2 [--ps2-root <jppc>] [--output <dir>] [--monsters m211,m162,m106]");
        Console.WriteLine("  dump-triples --monster m046 --slot 1 --record 0 --segment g5.ptrB --skip 24 --bits 12 --bit-offset 6 [--samples 128] [--output <dir>]");
        Console.WriteLine("  dump-grid --monster m046 --slot 1 --record 0 --segments g5.ptrB,g6.ptrB --skips 16,20,24 --bits 12 --bit-offsets 0,1,2,3,4,5,6,7 [--samples 128] [--output <dir>]");
        Console.WriteLine("  ptrb-header [--ps2-root <jppc>] [--output <dir>] [--monster m046] [--slot 1] [--record 0] [--groups 5,6,15,18,19] [--header-bytes 96]");
        Console.WriteLine("  group-channel-map [--ps2-root <jppc>] [--output <dir>] [--monsters m046,m211,m106,m162]");
        Console.WriteLine("  grouping-rank [--input work\\motion_codec_candidate_dumps_phase4] [--output work\\mgrp_frame_channel_separation]");
        Console.WriteLine("  motion-semantics [--ps2-root <jppc>] [--input work\\motion_codec_candidate_dumps_phase4] [--grouping-report work\\mgrp_frame_channel_separation\\mgrp-frame-channel-grouping-rank.json] [--output work\\mgrp_motion_semantics]");
        Console.WriteLine("  remap-attack [--ps2-root <jppc>] [--output work\\mgrp_remap_attack] [--monsters m046,m064,m130]");
        Console.WriteLine("  timing-law [--ps2-root <jppc>] [--output work\\mgrp_timing_count_law] [--monsters m046,m064,m130]");
        Console.WriteLine("  decode-candidate --monster m064 --slot 1 --record 0 --segment g4.ptrB --skip 28 --bits 12 --bit-offset 7 --stride 2 [--scale identity] [--samples 256] [--output <dir>]");
        Console.WriteLine("  unsafe-lab-package [--ps2-root <jppc>] [--dumps work\\motion_codec_candidate_dumps_phase4] [--semantics-report work\\mgrp_motion_semantics\\mgrp-motion-semantics-report.json] [--manifest-root work\\motion_linker_lab_all] [--output work\\mgrp_unsafe_lab_package]");
        Console.WriteLine("  cross-corpus-regression [--phase2-report work\\motion_codec_phase2_all\\mgrp-codec-phase2.json] [--output work\\mgrp_cross_corpus_regression]");
        Console.WriteLine("  smoke [--ps2-root <jppc>] [--ps3-root <ps3data>] [--output <dir>] [--monsters m018,m020,m106,m162,m211]");
    }
}

public static class MonsterMotionLinker
{
    public static MonsterMotionManifest Build(string ps2Root, string ps3Root, string monsterId)
    {
        var monsterNumber = int.Parse(monsterId[1..]);
        var monByte = monsterNumber & 0xFF;

        var ps2MonsterRoot = Path.Combine(ps2Root, "chr", "mon", monsterId);
        var ps3MonsterRoot = Path.Combine(ps3Root, "chr", "mon", monsterId);

        var chrPath = Path.Combine(ps2MonsterRoot, "mdl", $"{monsterId}.chr");
        var chr = ChrParser.Parse(chrPath, monByte);

        var motionRoot = Path.Combine(ps2MonsterRoot, "mot");
        var residents = Enumerable.Range(0, 4)
            .Select(slot => MgrpParser.Parse(Path.Combine(motionRoot, $"resident{slot}.mgrp"), slot, monByte))
            .ToArray();
        var ps2Motion = new Ps2MotionInfo(motionRoot, residents, residents.Count(item => item.IsNonEmpty));

        var ahPath = Path.Combine(ps3MonsterRoot, $"{monsterId}.ahwin32");
        var daePath = Path.Combine(ps3MonsterRoot, "mdl", "d3d11", $"{monsterId}.dae.phyre");
        var texturePath = Path.Combine(ps3MonsterRoot, "tex", "d3d11", $"{monsterId}.dds.phyre");
        var ps3 = Ps3AssetParser.Parse(ps3MonsterRoot, ahPath, daePath, texturePath);

        var linkage = BuildLinkage(monsterId, monByte, chr, ps2Motion, ps3);

        return new MonsterMotionManifest(
            DateTimeOffset.UtcNow,
            monsterId,
            monsterNumber,
            monByte,
            new RootInfo(ps2Root, ps3Root),
            new Ps2Info(ps2MonsterRoot, chr, ps2Motion),
            ps3,
            linkage,
            BuildNotes(ps2Motion, ps3, linkage));
    }

    private static LinkageInfo BuildLinkage(string monsterId, int monByte, ChrInfo chr, Ps2MotionInfo motion, Ps3AssetInfo ps3)
    {
        var nonEmptyResidents = motion.Residents.Where(item => item.IsNonEmpty).ToArray();
        var records = nonEmptyResidents.SelectMany(item => item.Records).ToArray();
        var recordMonIds = records
            .Where(item => item.SubIdMonByte.HasValue)
            .Select(item => item.SubIdMonByte!.Value)
            .Distinct()
            .Order()
            .ToArray();
        var markerMonIds = records
            .SelectMany(item => item.Channels)
            .Where(item => item.MarkerMonByte.HasValue)
            .Select(item => item.MarkerMonByte!.Value)
            .Distinct()
            .Order()
            .ToArray();

        var identityStatus =
            chr.Exists && ps3.Model.Exists && records.Any(item => item.SubIdMonByte == monByte)
                ? "proved_ps2_chr_mgrp_ps3_asset_identity"
                : chr.Exists && records.Any(item => item.SubIdMonByte == monByte)
                    ? "proved_ps2_chr_mgrp_identity"
                    : chr.Exists && ps3.Model.Exists
                        ? "structural_chr_ps3_asset_identity"
                        : "blocked_missing_required_asset";

        string motionReadiness;
        if (ps3.HasEmbeddedAnimationClip)
        {
            motionReadiness = nonEmptyResidents.Length > 0
                ? "ps3_embedded_clip_plus_ps2_mgrp_codec_blocked"
                : "ps3_embedded_clip_control_positive";
        }
        else if (nonEmptyResidents.Length > 0)
        {
            motionReadiness = "ps2_mgrp_codec_blocked";
        }
        else
        {
            motionReadiness = "no_resident_skeletal_motion_found";
        }

        var viewerReadiness =
            ps3.Model.Exists && (ps3.HasEmbeddedAnimationClip || nonEmptyResidents.Length > 0)
                ? "manifest_ready_animation_decode_or_exporter_needed"
                : ps3.Model.Exists
                    ? "static_model_ready_motion_unavailable"
                    : "blocked_missing_ps3_model";

        return new LinkageInfo(
            identityStatus,
            motionReadiness,
            viewerReadiness,
            "codec_packed_at_offset_16_and_jointidx_to_boneid_remap_remain_blocked",
            recordMonIds,
            markerMonIds);
    }

    private static string[] BuildNotes(Ps2MotionInfo motion, Ps3AssetInfo ps3, LinkageInfo linkage)
    {
        var notes = new List<string>
        {
            "Read-only manifest generated from Claude/Codex 2026-06-02 .mgrp hierarchy findings.",
            "This tool does not decode packed keyframes and does not write game assets.",
        };

        if (motion.NonEmptyResidentCount > 0)
        {
            notes.Add(".mgrp resident motion exists structurally, but animation curves remain blocked until codec decode.");
        }

        if (ps3.HasEmbeddedAnimationClip)
        {
            notes.Add("PS3 Phyre asset contains PAnimationClip text marker; treat as exporter/control-positive lane.");
        }

        notes.Add($"Identity status: {linkage.IdentityStatus}.");
        return notes.ToArray();
    }
}

public static class ChrParser
{
    public static ChrInfo Parse(string path, int monByte)
    {
        if (!File.Exists(path))
        {
            return ChrInfo.Missing(path);
        }

        var bytes = File.ReadAllBytes(path);
        uint? version = bytes.Length >= 8 ? ReadU32(bytes, 4) : null;
        uint? nodeTableOffset = bytes.Length >= 0x24 ? ReadU32(bytes, 0x20) : null;
        uint? nodeCount = bytes.Length >= 0x28 ? ReadU32(bytes, 0x24) : null;
        int? approxBoneCount = nodeCount.HasValue && nodeCount.Value > 0 ? (int)nodeCount.Value - 1 : null;
        var markerOffsets = FindIdentityMarkers(bytes, monByte).Take(16).ToArray();

        return new ChrInfo(
            path,
            true,
            bytes.LongLength,
            Sha256(path),
            version,
            nodeTableOffset,
            nodeCount,
            approxBoneCount,
            markerOffsets);
    }

    private static IEnumerable<long> FindIdentityMarkers(byte[] bytes, int monByte)
    {
        for (var index = 0; index <= bytes.Length - 4; index++)
        {
            if (bytes[index + 1] == 0x10 && bytes[index + 2] == monByte && bytes[index + 3] == 0x10)
            {
                yield return index;
            }
        }
    }

    private static uint ReadU32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public static class MgrpParser
{
    public static MgrpFileInfo Parse(string path, int slot, int expectedMonByte)
    {
        if (!File.Exists(path))
        {
            return MgrpFileInfo.Missing(path, slot);
        }

        var bytes = File.ReadAllBytes(path);
        var warnings = new List<string>();

        if (bytes.Length < 16)
        {
            warnings.Add("file_shorter_than_16_byte_header");
            return new MgrpFileInfo(path, slot, true, bytes.LongLength, false, null, null, null, false, Array.Empty<MgrpRecordInfo>(), warnings.ToArray());
        }

        var zero0 = ReadU32(bytes, 0x00);
        var recordCount = ReadU32(bytes, 0x04);
        var zero8 = ReadU32(bytes, 0x08);
        var payloadEnd = ReadU32(bytes, 0x0C);
        var sizeLawOk = bytes.LongLength == payloadEnd + recordCount * 20L;
        if (zero0 != 0 || zero8 != 0)
        {
            warnings.Add("header_zero_fields_not_zero");
        }

        if (!sizeLawOk)
        {
            warnings.Add("size_law_failed_filesize_ne_payloadEnd_plus_records20");
        }

        var records = new List<MgrpRecordInfo>();
        if (payloadEnd <= bytes.Length && payloadEnd + recordCount * 20L <= bytes.Length)
        {
            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                records.Add(ParseRecord(bytes, (int)payloadEnd + recordIndex * 20, recordIndex, expectedMonByte, warnings));
            }
        }
        else if (recordCount > 0)
        {
            warnings.Add("record_table_out_of_bounds");
        }

        var isEmptyStub = bytes.Length == 16 && recordCount == 0 && payloadEnd == 16;
        return new MgrpFileInfo(
            path,
            slot,
            true,
            bytes.LongLength,
            recordCount > 0,
            recordCount,
            payloadEnd,
            sizeLawOk,
            isEmptyStub,
            records.ToArray(),
            warnings.ToArray());
    }

    private static MgrpRecordInfo ParseRecord(byte[] bytes, int offset, int recordIndex, int expectedMonByte, List<string> warnings)
    {
        var f0 = ReadU32(bytes, offset);
        var subid = ReadU32(bytes, offset + 0x04);
        var channelCount = ReadU16(bytes, offset + 0x08);
        var groupCount = ReadU16(bytes, offset + 0x0A);
        var offA = ReadU32(bytes, offset + 0x0C);
        var offB = ReadU32(bytes, offset + 0x10);
        var subIdMonByte = (int)(subid & 0xFF);

        var groups = new List<MgrpGroupInfo>();
        if (IsRange(bytes, offB, groupCount * 16L))
        {
            for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                var groupOffset = (int)offB + groupIndex * 16;
                groups.Add(new MgrpGroupInfo(
                    groupIndex,
                    ReadU32(bytes, groupOffset),
                    ReadU32(bytes, groupOffset + 4),
                    ReadU32(bytes, groupOffset + 8),
                    ReadU32(bytes, groupOffset + 12)));
            }
        }
        else if (groupCount > 0)
        {
            warnings.Add($"record_{recordIndex}_group_table_out_of_bounds");
        }

        var channels = new List<MgrpChannelInfo>();
        if (IsRange(bytes, offA, channelCount * 16L))
        {
            for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var channelOffset = (int)offA + channelIndex * 16;
                var marker = ReadU32(bytes, channelOffset);
                channels.Add(new MgrpChannelInfo(
                    channelIndex,
                    marker,
                    (int)((marker >> 16) & 0xFF),
                    ReadU16(bytes, channelOffset + 4),
                    ReadU16(bytes, channelOffset + 6),
                    ReadU32(bytes, channelOffset + 8),
                    ReadU32(bytes, channelOffset + 12)));
            }
        }
        else if (channelCount > 0)
        {
            warnings.Add($"record_{recordIndex}_channel_table_out_of_bounds");
        }

        uint? firstOffX = channels.Count > 0 ? channels[0].OffX : null;
        var groupToFirstOffXOk = firstOffX.HasValue && offB + groupCount * 16L == firstOffX.Value;
        var channelRegionLawOk = firstOffX.HasValue && firstOffX.Value + channelCount * 12L == offA;
        var allChannelTagsAre000A = channels.Count == channelCount && channels.All(item => item.Tag == 0x000A);
        var groupPointersAbsoluteAndInRange = groups.Count == groupCount && groups.All(item =>
            item.PtrA < bytes.LongLength && item.PtrB < bytes.LongLength);
        var subIdMatchesExpected = subIdMonByte == expectedMonByte || subid < 0x1000;

        return new MgrpRecordInfo(
            recordIndex,
            f0,
            subid,
            subIdMonByte,
            subIdMatchesExpected,
            channelCount,
            groupCount,
            offA,
            offB,
            firstOffX,
            groupToFirstOffXOk,
            channelRegionLawOk,
            allChannelTagsAre000A,
            groupPointersAbsoluteAndInRange,
            groups.ToArray(),
            channels.ToArray());
    }

    private static bool IsRange(byte[] bytes, uint offset, long length) =>
        offset <= bytes.LongLength && length >= 0 && offset + length <= bytes.LongLength;

    private static ushort ReadU16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static uint ReadU32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}

public static class Ps3AssetParser
{
    public static Ps3AssetInfo Parse(string root, string ahPath, string daePath, string texturePath)
    {
        var ah = FileInfoSnapshot.FromPath(ahPath);
        var model = FileInfoSnapshot.FromPath(daePath);
        var texture = FileInfoSnapshot.FromPath(texturePath);
        var embeddedAnimationClipCount = model.Exists ? CountAsciiOccurrences(daePath, "PAnimationClip") : 0;
        var skeletonNodeMarkerCount = model.Exists ? CountAsciiOccurrences(daePath, "SkeletonNode_") : 0;

        return new Ps3AssetInfo(
            root,
            ah,
            model,
            texture,
            embeddedAnimationClipCount,
            skeletonNodeMarkerCount,
            embeddedAnimationClipCount > 0);
    }

    private static int CountAsciiOccurrences(string path, string needle)
    {
        var bytes = File.ReadAllBytes(path);
        var pattern = Encoding.ASCII.GetBytes(needle);
        var count = 0;

        for (var index = 0; index <= bytes.Length - pattern.Length; index++)
        {
            var matched = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (bytes[index + i] != pattern[i])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                count++;
            }
        }

        return count;
    }
}

public sealed record MotionLinkerIndex(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    string Ps3Root,
    int MonsterCount,
    int WithPs2ResidentMotion,
    int WithPs3EmbeddedAnimation,
    MotionLinkerIndexEntry[] Entries);

public sealed record MotionLinkerIndexEntry(
    string MonsterId,
    string IdentityStatus,
    string MotionReadiness,
    int NonEmptyResidentCount,
    int EmbeddedAnimationClipCount,
    string ManifestPath);

public sealed record MonsterMotionManifest(
    DateTimeOffset GeneratedAtUtc,
    string MonsterId,
    int MonsterNumber,
    int MonIdByte,
    RootInfo Roots,
    Ps2Info Ps2,
    Ps3AssetInfo Ps3,
    LinkageInfo Linkage,
    string[] Notes);

public sealed record RootInfo(string Ps2Root, string Ps3Root);

public sealed record Ps2Info(string Root, ChrInfo Chr, Ps2MotionInfo Motion);

public sealed record ChrInfo(
    string Path,
    bool Exists,
    long? Length,
    string? Sha256,
    uint? Version,
    uint? NodeTableOffset,
    uint? NodeCount,
    int? ApproxBoneCount,
    long[] IdentityMarkerOffsets)
{
    public static ChrInfo Missing(string path) =>
        new(path, false, null, null, null, null, null, null, Array.Empty<long>());
}

public sealed record Ps2MotionInfo(
    string Root,
    MgrpFileInfo[] Residents,
    int NonEmptyResidentCount);

public sealed record MgrpFileInfo(
    string Path,
    int Slot,
    bool Exists,
    long? Length,
    bool IsNonEmpty,
    uint? RecordCount,
    uint? PayloadEnd,
    bool? SizeLawOk,
    bool IsEmptyStub,
    MgrpRecordInfo[] Records,
    string[] Warnings)
{
    public static MgrpFileInfo Missing(string path, int slot) =>
        new(path, slot, false, null, false, null, null, null, false, Array.Empty<MgrpRecordInfo>(), Array.Empty<string>());
}

public sealed record MgrpRecordInfo(
    int Index,
    uint F0,
    uint SubId,
    int? SubIdMonByte,
    bool SubIdMatchesExpectedMonster,
    ushort ChannelCount,
    ushort GroupCount,
    uint OffA,
    uint OffB,
    uint? FirstOffX,
    bool GroupToFirstOffXLawOk,
    bool ChannelRegionLawOk,
    bool AllChannelTagsAre000A,
    bool GroupPointersAbsoluteAndInRange,
    MgrpGroupInfo[] Groups,
    MgrpChannelInfo[] Channels);

public sealed record MgrpGroupInfo(
    int Index,
    uint Zero,
    uint Count,
    uint PtrA,
    uint PtrB);

public sealed record MgrpChannelInfo(
    int Index,
    uint Marker,
    int? MarkerMonByte,
    ushort CarryFlag,
    ushort Tag,
    uint OffX,
    uint OffY);

public sealed record Ps3AssetInfo(
    string Root,
    FileInfoSnapshot AhWin32,
    FileInfoSnapshot Model,
    FileInfoSnapshot Texture,
    int EmbeddedAnimationClipCount,
    int SkeletonNodeMarkerCount,
    bool HasEmbeddedAnimationClip);

public sealed record FileInfoSnapshot(
    string Path,
    bool Exists,
    long? Length,
    string? Sha256)
{
    public static FileInfoSnapshot FromPath(string path)
    {
        if (!File.Exists(path))
        {
            return new FileInfoSnapshot(path, false, null, null);
        }

        using var stream = File.OpenRead(path);
        return new FileInfoSnapshot(path, true, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }
}

public sealed record LinkageInfo(
    string IdentityStatus,
    string MotionReadiness,
    string ViewerReadiness,
    string BlockedFrontier,
    int[] RecordMonIdBytes,
    int[] ChannelMarkerMonIdBytes);
