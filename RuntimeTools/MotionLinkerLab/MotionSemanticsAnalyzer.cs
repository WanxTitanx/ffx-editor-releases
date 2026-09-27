using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MotionLinkerLab;

public static class MgrpMotionSemanticsAnalyzer
{
    private static readonly SemanticsScale[] Scales =
    [
        new("identity", 1.0),
        new("i32", 1.0 / 32.0),
        new("i64", 1.0 / 64.0),
        new("i128", 1.0 / 128.0),
        new("i256", 1.0 / 256.0),
        new("half_pi", Math.PI / 2.0),
        new("pi", Math.PI),
        new("two_pi", Math.PI * 2.0),
    ];

    public static MotionSemanticsReport Analyze(string ps2Root, string inputRoot, string groupingReportPath, string outputRoot)
    {
        if (!Directory.Exists(inputRoot))
        {
            throw new DirectoryNotFoundException($"Input dump directory not found: {inputRoot}");
        }

        var grouping = JsonSerializer.Deserialize<DumpGroupingReport>(File.ReadAllText(groupingReportPath))
            ?? throw new InvalidOperationException($"Could not read grouping report: {groupingReportPath}");
        var groupingByDump = grouping.Dumps.ToDictionary(item => item.DumpName, StringComparer.OrdinalIgnoreCase);
        var dumps = Directory.EnumerateFiles(inputRoot, "*.triple-dump.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadDump(path, groupingByDump))
            .ToArray();

        var phase6 = dumps.Select(BuildPhase6Row).ToArray();
        var phase7 = dumps.SelectMany(BuildPhase7Rows).ToArray();
        var phase8 = dumps.Select(dump => BuildPhase8Row(ps2Root, dump)).ToArray();
        var phase9 = dumps.SelectMany(dump => BuildPhase9Rows(ps2Root, dump)).ToArray();
        var phase10 = dumps.Select(BuildPhase10Row).ToArray();

        var phase6Csv = Path.Combine(outputRoot, "phase6-delta-vs-absolute.csv");
        var phase7Csv = Path.Combine(outputRoot, "phase7-quantization-scale.csv");
        var phase8Csv = Path.Combine(outputRoot, "phase8-rot-trans-intermediate.csv");
        var phase9Csv = Path.Combine(outputRoot, "phase9-remap-matrix.csv");
        var phase10Csv = Path.Combine(outputRoot, "phase10-timing-hypothesis.csv");
        WritePhase6Csv(phase6Csv, phase6);
        WritePhase7Csv(phase7Csv, phase7);
        WritePhase8Csv(phase8Csv, phase8);
        WritePhase9Csv(phase9Csv, phase9);
        WritePhase10Csv(phase10Csv, phase10);

        return new MotionSemanticsReport(
            DateTimeOffset.UtcNow,
            ps2Root,
            inputRoot,
            groupingReportPath,
            dumps.Length,
            phase6.Count(item => item.DecisionBand.StartsWith("partial", StringComparison.Ordinal)),
            "partial_limited_to_grouped_candidates_no_m046_promotion",
            "partial_scales_tabulated_no_single_scale_proved",
            "partial_rotation_or_intermediate_candidate_translation_not_proved",
            "blocked_no_safe_jointidx_to_boneid_remap",
            "partial_counts_hypothesized_no_duration_proof",
            phase6Csv,
            phase7Csv,
            phase8Csv,
            phase9Csv,
            phase10Csv,
            phase6,
            phase7,
            phase8,
            phase9,
            phase10);
    }

    private static LoadedDump LoadDump(string reportPath, IReadOnlyDictionary<string, DumpGroupingResult> groupingByDump)
    {
        var dump = JsonSerializer.Deserialize<TripleDumpReport>(File.ReadAllText(reportPath))
            ?? throw new InvalidOperationException($"Could not read dump report: {reportPath}");
        var dumpName = Path.GetFileNameWithoutExtension(reportPath).Replace(".triple-dump", "", StringComparison.OrdinalIgnoreCase);
        groupingByDump.TryGetValue(dumpName, out var grouping);
        var samples = ReadSamples(dump.CsvPath);
        return new LoadedDump(dumpName, reportPath, dump, grouping, samples);
    }

    private static Phase6DeltaModeRow BuildPhase6Row(LoadedDump dump)
    {
        var linear = AverageAdjacentDelta(dump.Samples);
        var grouped = dump.Grouping?.BestAverageDelta ?? linear;
        var improvement = dump.Grouping?.BestImprovementRate ?? 0;
        var cumulative = CumulativeStats(dump.Samples);
        var reset = ResetCumulativeStats(dump.Samples, dump.Grouping);
        var bestMode = improvement >= 0.15
            ? "absolute_grouped_interleave_candidate"
            : cumulative.FinalDrift < linear
                ? "relative_cumulative_weak_candidate"
                : "absolute_linear_baseline";
        var decision = improvement >= 0.15
            ? "partial_grouped_absolute_candidate"
            : "blocked_no_delta_mode_promotion";

        return new Phase6DeltaModeRow(
            dump.DumpName,
            dump.Report.Request.MonsterId,
            dump.Report.Request.SegmentLabel,
            dump.Report.Request.LocalByteSkip,
            dump.Report.Request.BitsPerComponent,
            dump.Report.Request.BitOffset,
            dump.Samples.Length,
            linear,
            dump.Grouping?.BestStrategyName ?? "linear",
            grouped,
            improvement,
            cumulative.FinalDrift,
            cumulative.AxisRangeMean,
            reset.FinalDrift,
            reset.AxisRangeMean,
            bestMode,
            decision);
    }

    private static IEnumerable<Phase7ScaleRow> BuildPhase7Rows(LoadedDump dump)
    {
        foreach (var scale in Scales)
        {
            var meanMagnitude = dump.Report.Stats.MeanVectorMagnitude * scale.Value;
            var meanAxisRange = dump.Report.Stats.MeanAxisRange * scale.Value;
            var angularPlausibility = meanAxisRange <= Math.PI * 2.25 && meanMagnitude <= Math.PI * 1.5;
            yield return new Phase7ScaleRow(
                dump.DumpName,
                dump.Report.Request.MonsterId,
                scale.Name,
                scale.Value,
                meanMagnitude,
                meanAxisRange,
                angularPlausibility,
                angularPlausibility ? "partial_plausible_numeric_scale" : "unsafe_out_of_expected_angular_range");
        }
    }

    private static Phase8SemanticRow BuildPhase8Row(string ps2Root, LoadedDump dump)
    {
        var chrPath = Path.Combine(ps2Root, "chr", "mon", dump.Report.Request.MonsterId, "mdl", $"{dump.Report.Request.MonsterId}.chr");
        var bind = ChrBindPoseReader.Parse(chrPath);
        var identityAngularPlausible = dump.Report.Stats.MeanAxisRange <= Math.PI * 2.25
            && dump.Report.Stats.MeanVectorMagnitude <= Math.PI * 1.5;
        double? bindRatio = bind.MeanVectorMagnitude <= 0 ? null : dump.Report.Stats.MeanVectorMagnitude / bind.MeanVectorMagnitude;
        var classification = identityAngularPlausible
            ? "rotation_or_intermediate_candidate"
            : "intermediate_or_scaled_translation_unknown";

        return new Phase8SemanticRow(
            dump.DumpName,
            dump.Report.Request.MonsterId,
            bind.Exists,
            bind.NodeCount,
            bind.ValidNodeCount,
            bind.MeanVectorMagnitude,
            dump.Report.Stats.MeanVectorMagnitude,
            dump.Report.Stats.MeanAxisRange,
            bindRatio,
            classification,
            "partial_no_anatomical_remap_proof");
    }

    private static IEnumerable<Phase9RemapRow> BuildPhase9Rows(string ps2Root, LoadedDump dump)
    {
        var monsterId = dump.Report.Request.MonsterId;
        var monByte = int.Parse(monsterId[1..]) & 0xFF;
        var path = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot", $"resident{dump.Report.Request.Slot}.mgrp");
        var file = MgrpParser.Parse(path, dump.Report.Request.Slot, monByte);
        var record = file.Records.First(item => item.Index == dump.Report.Request.RecordIndex);
        var chrPath = Path.Combine(ps2Root, "chr", "mon", monsterId, "mdl", $"{monsterId}.chr");
        var bind = ChrBindPoseReader.Parse(chrPath);
        var approxBoneCount = Math.Max(0, (int)(bind.NodeCount ?? 0) - 1);

        foreach (var channel in record.Channels)
        {
            var candidateNode = channel.Index < bind.ValidNodeCount ? channel.Index : null as int?;
            var status = record.ChannelCount <= approxBoneCount && candidateNode.HasValue
                ? "unsafe_ordinal_candidate_only"
                : candidateNode.HasValue
                    ? "unsafe_ordinal_over_count_mismatch"
                    : "blocked_no_candidate";
            yield return new Phase9RemapRow(
                dump.DumpName,
                monsterId,
                channel.Index,
                channel.Marker,
                channel.CarryFlag,
                record.ChannelCount,
                record.GroupCount,
                bind.NodeCount,
                approxBoneCount,
                candidateNode,
                status);
        }
    }

    private static Phase10TimingRow BuildPhase10Row(LoadedDump dump)
    {
        var header = dump.Report.SegmentSummary.FirstU16Values;
        var headerU16_0 = header.Length > 0 ? header[0] : null as ushort?;
        var headerLengthWord = header.Length > 8 ? header[8] : null as ushort?;
        var dataBytes = Math.Max(0, dump.Report.SegmentLength - dump.Report.Request.LocalByteSkip);
        var usableBits = Math.Max(0, dataBytes * 8 - dump.Report.Request.BitOffset);
        var possibleTriples = usableBits / (dump.Report.Request.BitsPerComponent * 3);
        var sampleCapLimited = possibleTriples > dump.Report.SampleCount;
        var decision = headerU16_0.HasValue || headerLengthWord.HasValue
            ? "partial_count_fields_exist_no_duration"
            : "blocked_no_timing_field";

        return new Phase10TimingRow(
            dump.DumpName,
            dump.Report.Request.MonsterId,
            dump.Report.Request.SegmentLabel,
            dump.Report.SegmentLength,
            dump.Report.Request.LocalByteSkip,
            dump.Report.Request.BitsPerComponent,
            dump.Report.Request.BitOffset,
            dump.Report.SampleCount,
            possibleTriples,
            sampleCapLimited,
            headerU16_0,
            header.Length > 3 ? header[3] : null as ushort?,
            headerLengthWord,
            decision);
    }

    private static TripleSample[] ReadSamples(string csvPath)
    {
        var samples = new List<TripleSample>();
        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            samples.Add(new TripleSample(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture)));
        }

        return samples.ToArray();
    }

    private static MotionCumulativeStats CumulativeStats(IReadOnlyList<TripleSample> samples)
    {
        var cumulative = new List<TripleSample>();
        var x = 0.0;
        var y = 0.0;
        var z = 0.0;
        foreach (var sample in samples)
        {
            x += sample.X;
            y += sample.Y;
            z += sample.Z;
            cumulative.Add(new TripleSample(sample.Index, x, y, z));
        }

        return BuildCumulativeStats(cumulative);
    }

    private static MotionCumulativeStats ResetCumulativeStats(IReadOnlyList<TripleSample> samples, DumpGroupingResult? grouping)
    {
        var parameter = grouping?.BestStrategyParameter ?? samples.Count;
        parameter = Math.Max(1, parameter);
        var cumulative = new List<TripleSample>();
        var x = 0.0;
        var y = 0.0;
        var z = 0.0;
        for (var index = 0; index < samples.Count; index++)
        {
            if (index % parameter == 0)
            {
                x = 0;
                y = 0;
                z = 0;
            }

            x += samples[index].X;
            y += samples[index].Y;
            z += samples[index].Z;
            cumulative.Add(new TripleSample(samples[index].Index, x, y, z));
        }

        return BuildCumulativeStats(cumulative);
    }

    private static MotionCumulativeStats BuildCumulativeStats(IReadOnlyList<TripleSample> samples)
    {
        var first = samples.First();
        var last = samples.Last();
        var rangeX = samples.Max(item => item.X) - samples.Min(item => item.X);
        var rangeY = samples.Max(item => item.Y) - samples.Min(item => item.Y);
        var rangeZ = samples.Max(item => item.Z) - samples.Min(item => item.Z);
        return new MotionCumulativeStats(Distance(first, last), (rangeX + rangeY + rangeZ) / 3.0);
    }

    private static double AverageAdjacentDelta(IReadOnlyList<TripleSample> samples)
    {
        var total = 0.0;
        for (var index = 1; index < samples.Count; index++)
        {
            total += Distance(samples[index - 1], samples[index]);
        }

        return total / (samples.Count - 1);
    }

    private static double Distance(TripleSample a, TripleSample b) =>
        Math.Sqrt(
            Math.Pow(a.X - b.X, 2)
            + Math.Pow(a.Y - b.Y, 2)
            + Math.Pow(a.Z - b.Z, 2));

    private static void WritePhase6Csv(string path, IReadOnlyList<Phase6DeltaModeRow> rows)
    {
        var builder = BeginCsv(path, "dumpName,monsterId,segment,skip,bits,bitOffset,sampleCount,linearAverageDelta,bestGroupingStrategy,groupedAverageDelta,groupedImprovementRate,cumulativeFinalDrift,cumulativeAxisRangeMean,resetFinalDrift,resetAxisRangeMean,bestMode,decisionBand");
        foreach (var row in rows)
        {
            AppendRow(builder, row.DumpName, row.MonsterId, row.SegmentLabel, row.Skip, row.Bits, row.BitOffset, row.SampleCount, row.LinearAverageDelta, row.BestGroupingStrategy, row.GroupedAverageDelta, row.GroupedImprovementRate, row.CumulativeFinalDrift, row.CumulativeAxisRangeMean, row.ResetFinalDrift, row.ResetAxisRangeMean, row.BestMode, row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePhase7Csv(string path, IReadOnlyList<Phase7ScaleRow> rows)
    {
        var builder = BeginCsv(path, "dumpName,monsterId,scaleName,scaleValue,scaledMeanMagnitude,scaledMeanAxisRange,angularPlausible,decisionBand");
        foreach (var row in rows)
        {
            AppendRow(builder, row.DumpName, row.MonsterId, row.ScaleName, row.ScaleValue, row.ScaledMeanMagnitude, row.ScaledMeanAxisRange, row.AngularPlausible, row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePhase8Csv(string path, IReadOnlyList<Phase8SemanticRow> rows)
    {
        var builder = BeginCsv(path, "dumpName,monsterId,bindPoseExists,chrNodeCount,validNodeCount,bindMeanMagnitude,dumpMeanMagnitude,dumpMeanAxisRange,dumpToBindMagnitudeRatio,classification,decisionBand");
        foreach (var row in rows)
        {
            AppendRow(builder, row.DumpName, row.MonsterId, row.BindPoseExists, row.ChrNodeCount, row.ValidNodeCount, row.BindMeanMagnitude, row.DumpMeanMagnitude, row.DumpMeanAxisRange, row.DumpToBindMagnitudeRatio, row.Classification, row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePhase9Csv(string path, IReadOnlyList<Phase9RemapRow> rows)
    {
        var builder = BeginCsv(path, "dumpName,monsterId,channelIndex,marker,carryFlag,recordChannelCount,recordGroupCount,chrNodeCount,approxBoneCount,candidateNodeIndex,status");
        foreach (var row in rows)
        {
            AppendRow(builder, row.DumpName, row.MonsterId, row.ChannelIndex, row.Marker, row.CarryFlag, row.RecordChannelCount, row.RecordGroupCount, row.ChrNodeCount, row.ApproxBoneCount, row.CandidateNodeIndex, row.Status);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePhase10Csv(string path, IReadOnlyList<Phase10TimingRow> rows)
    {
        var builder = BeginCsv(path, "dumpName,monsterId,segment,segmentLength,skip,bits,bitOffset,dumpedSampleCount,possibleTripleCount,sampleCapLimited,headerU16_0,headerU16_3,headerLengthWord,decisionBand");
        foreach (var row in rows)
        {
            AppendRow(builder, row.DumpName, row.MonsterId, row.SegmentLabel, row.SegmentLength, row.Skip, row.Bits, row.BitOffset, row.DumpedSampleCount, row.PossibleTripleCount, row.SampleCapLimited, row.HeaderU16_0, row.HeaderU16_3, row.HeaderLengthWord, row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static StringBuilder BeginCsv(string path, string header)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine(header);
        return builder;
    }

    private static void AppendRow(StringBuilder builder, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(FormatCsv(values[index]));
        }

        builder.AppendLine();
    }

    private static string FormatCsv(object? value)
    {
        if (value is null)
        {
            return "";
        }

        var text = value switch
        {
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}

internal sealed record LoadedDump(
    string DumpName,
    string ReportPath,
    TripleDumpReport Report,
    DumpGroupingResult? Grouping,
    TripleSample[] Samples);

internal sealed record SemanticsScale(string Name, double Value);

internal sealed record MotionCumulativeStats(double FinalDrift, double AxisRangeMean);

public sealed record MotionSemanticsReport(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    string InputRoot,
    string GroupingReportPath,
    int DumpCount,
    int Phase6PartialCount,
    string Phase6DecisionBand,
    string Phase7DecisionBand,
    string Phase8DecisionBand,
    string Phase9DecisionBand,
    string Phase10DecisionBand,
    string Phase6CsvPath,
    string Phase7CsvPath,
    string Phase8CsvPath,
    string Phase9CsvPath,
    string Phase10CsvPath,
    Phase6DeltaModeRow[] Phase6Rows,
    Phase7ScaleRow[] Phase7Rows,
    Phase8SemanticRow[] Phase8Rows,
    Phase9RemapRow[] Phase9Rows,
    Phase10TimingRow[] Phase10Rows);

public sealed record Phase6DeltaModeRow(
    string DumpName,
    string MonsterId,
    string SegmentLabel,
    int Skip,
    int Bits,
    int BitOffset,
    int SampleCount,
    double LinearAverageDelta,
    string BestGroupingStrategy,
    double GroupedAverageDelta,
    double GroupedImprovementRate,
    double CumulativeFinalDrift,
    double CumulativeAxisRangeMean,
    double ResetFinalDrift,
    double ResetAxisRangeMean,
    string BestMode,
    string DecisionBand);

public sealed record Phase7ScaleRow(
    string DumpName,
    string MonsterId,
    string ScaleName,
    double ScaleValue,
    double ScaledMeanMagnitude,
    double ScaledMeanAxisRange,
    bool AngularPlausible,
    string DecisionBand);

public sealed record Phase8SemanticRow(
    string DumpName,
    string MonsterId,
    bool BindPoseExists,
    uint? ChrNodeCount,
    int ValidNodeCount,
    double BindMeanMagnitude,
    double DumpMeanMagnitude,
    double DumpMeanAxisRange,
    double? DumpToBindMagnitudeRatio,
    string Classification,
    string DecisionBand);

public sealed record Phase9RemapRow(
    string DumpName,
    string MonsterId,
    int ChannelIndex,
    uint Marker,
    ushort CarryFlag,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    uint? ChrNodeCount,
    int ApproxBoneCount,
    int? CandidateNodeIndex,
    string Status);

public sealed record Phase10TimingRow(
    string DumpName,
    string MonsterId,
    string SegmentLabel,
    int SegmentLength,
    int Skip,
    int Bits,
    int BitOffset,
    int DumpedSampleCount,
    int PossibleTripleCount,
    bool SampleCapLimited,
    ushort? HeaderU16_0,
    ushort? HeaderU16_3,
    ushort? HeaderLengthWord,
    string DecisionBand);
