using System.Globalization;
using System.Text;

namespace MotionLinkerLab;

public static class MgrpDumpGroupingRanker
{
    public static DumpGroupingReport Analyze(string inputRoot, string outputRoot)
    {
        if (!Directory.Exists(inputRoot))
        {
            throw new DirectoryNotFoundException($"Input dump directory not found: {inputRoot}");
        }

        var dumps = Directory.EnumerateFiles(inputRoot, "*.triples.csv")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(AnalyzeDump)
            .ToArray();
        var clearCount = dumps.Count(item => item.DecisionBand == "partial_grouping_improves_linear_stream");
        var decisionBand = clearCount >= 2
            ? "partial_grouping_signal_repeated_but_semantics_unproved"
            : "blocked_no_repeated_clear_grouping_signal";
        var csvPath = Path.Combine(outputRoot, "mgrp-frame-channel-grouping-rank.csv");
        WriteCsv(csvPath, dumps);

        return new DumpGroupingReport(
            DateTimeOffset.UtcNow,
            inputRoot,
            dumps.Length,
            clearCount,
            decisionBand,
            csvPath,
            dumps);
    }

    private static DumpGroupingResult AnalyzeDump(string csvPath)
    {
        var samples = ReadSamples(csvPath);
        if (samples.Length < 4)
        {
            throw new InvalidOperationException($"Dump has too few samples: {csvPath}");
        }

        var linearAverage = AverageAdjacentDelta(samples);
        var strategies = new List<GroupingStrategyScore>
        {
            new("linear", 1, samples.Length - 1, linearAverage, 0),
        };

        var maxStride = Math.Min(32, Math.Max(2, samples.Length / 2));
        for (var stride = 2; stride <= maxStride; stride++)
        {
            var score = AverageStrideDelta(samples, stride);
            if (score.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"stride_{stride}",
                    stride,
                    score.ComparisonCount,
                    score.AverageDelta,
                    Improvement(linearAverage, score.AverageDelta)));
            }

            var deinterleaved = AverageDeinterleavedAdjacentDelta(samples, stride);
            if (deinterleaved.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"deinterleave_channels_first_{stride}",
                    stride,
                    deinterleaved.ComparisonCount,
                    deinterleaved.AverageDelta,
                    Improvement(linearAverage, deinterleaved.AverageDelta)));
            }
        }

        var maxWindow = Math.Min(32, Math.Max(2, samples.Length / 2));
        for (var window = 2; window <= maxWindow; window++)
        {
            var score = AverageWindowInternalDelta(samples, window);
            if (score.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"window_{window}_drop_boundaries",
                    window,
                    score.ComparisonCount,
                    score.AverageDelta,
                    Improvement(linearAverage, score.AverageDelta)));
            }

            var block = AverageBlockInternalDelta(samples, window, dropFirstSample: false);
            if (block.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"block_{window}_drop_boundaries",
                    window,
                    block.ComparisonCount,
                    block.AverageDelta,
                    Improvement(linearAverage, block.AverageDelta)));
            }

            var blockDropHeader = AverageBlockInternalDelta(samples, window, dropFirstSample: true);
            if (blockDropHeader.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"block_{window}_drop_header_sample",
                    window,
                    blockDropHeader.ComparisonCount,
                    blockDropHeader.AverageDelta,
                    Improvement(linearAverage, blockDropHeader.AverageDelta)));
            }

            var reset = AverageResetOnLargeJumpDelta(samples, window);
            if (reset.ComparisonCount > 0)
            {
                strategies.Add(new GroupingStrategyScore(
                    $"reset_sentinel_like_window_{window}",
                    window,
                    reset.ComparisonCount,
                    reset.AverageDelta,
                    Improvement(linearAverage, reset.AverageDelta)));
            }
        }

        var top = strategies
            .OrderBy(item => item.AverageDelta)
            .ThenByDescending(item => item.ComparisonCount)
            .Take(16)
            .ToArray();
        var best = top.First(item => item.Name != "linear");
        var hasEnoughComparisons = best.ComparisonCount >= Math.Max(8, samples.Length / 2);
        var clearImprovement = hasEnoughComparisons && best.ImprovementRate >= 0.15;
        var decisionBand = clearImprovement
            ? "partial_grouping_improves_linear_stream"
            : "blocked_grouping_not_clear_enough";

        return new DumpGroupingResult(
            Path.GetFileNameWithoutExtension(csvPath).Replace(".triples", "", StringComparison.OrdinalIgnoreCase),
            csvPath,
            samples.Length,
            linearAverage,
            best.Name,
            best.Parameter,
            best.AverageDelta,
            best.ImprovementRate,
            best.ComparisonCount,
            decisionBand,
            top);
    }

    private static TripleSample[] ReadSamples(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        var samples = new List<TripleSample>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                continue;
            }

            samples.Add(new TripleSample(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture)));
        }

        return samples.ToArray();
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

    private static DeltaScore AverageStrideDelta(IReadOnlyList<TripleSample> samples, int stride)
    {
        var total = 0.0;
        var count = 0;
        for (var index = 0; index + stride < samples.Count; index++)
        {
            total += Distance(samples[index], samples[index + stride]);
            count++;
        }

        return new DeltaScore(count == 0 ? double.PositiveInfinity : total / count, count);
    }

    private static DeltaScore AverageWindowInternalDelta(IReadOnlyList<TripleSample> samples, int window)
    {
        var total = 0.0;
        var count = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            if (index % window == 0)
            {
                continue;
            }

            total += Distance(samples[index - 1], samples[index]);
            count++;
        }

        return new DeltaScore(count == 0 ? double.PositiveInfinity : total / count, count);
    }

    private static DeltaScore AverageDeinterleavedAdjacentDelta(IReadOnlyList<TripleSample> samples, int channelCount)
    {
        var total = 0.0;
        var count = 0;
        for (var channel = 0; channel < channelCount; channel++)
        {
            TripleSample? previous = null;
            for (var index = channel; index < samples.Count; index += channelCount)
            {
                if (previous is not null)
                {
                    total += Distance(previous, samples[index]);
                    count++;
                }

                previous = samples[index];
            }
        }

        return new DeltaScore(count == 0 ? double.PositiveInfinity : total / count, count);
    }

    private static DeltaScore AverageBlockInternalDelta(IReadOnlyList<TripleSample> samples, int blockSize, bool dropFirstSample)
    {
        var total = 0.0;
        var count = 0;
        for (var blockStart = 0; blockStart < samples.Count; blockStart += blockSize)
        {
            var start = dropFirstSample ? blockStart + 2 : blockStart + 1;
            var end = Math.Min(samples.Count, blockStart + blockSize);
            for (var index = start; index < end; index++)
            {
                total += Distance(samples[index - 1], samples[index]);
                count++;
            }
        }

        return new DeltaScore(count == 0 ? double.PositiveInfinity : total / count, count);
    }

    private static DeltaScore AverageResetOnLargeJumpDelta(IReadOnlyList<TripleSample> samples, int window)
    {
        var linear = AverageAdjacentDelta(samples);
        var jumpLimit = linear * 2.5;
        var total = 0.0;
        var count = 0;

        for (var index = 1; index < samples.Count; index++)
        {
            if (index % window == 0)
            {
                continue;
            }

            var delta = Distance(samples[index - 1], samples[index]);
            if (delta > jumpLimit)
            {
                continue;
            }

            total += delta;
            count++;
        }

        return new DeltaScore(count == 0 ? double.PositiveInfinity : total / count, count);
    }

    private static double Distance(TripleSample a, TripleSample b) =>
        Math.Sqrt(
            Math.Pow(a.X - b.X, 2)
            + Math.Pow(a.Y - b.Y, 2)
            + Math.Pow(a.Z - b.Z, 2));

    private static double Improvement(double baseline, double candidate) =>
        baseline <= 0 ? 0 : Math.Max(0, (baseline - candidate) / baseline);

    private static void WriteCsv(string path, IReadOnlyList<DumpGroupingResult> dumps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("dumpName,sampleCount,linearAverageDelta,bestStrategy,bestParameter,bestAverageDelta,bestImprovementRate,bestComparisonCount,decisionBand");
        foreach (var dump in dumps)
        {
            Append(builder, dump.DumpName);
            Append(builder, dump.SampleCount);
            Append(builder, dump.LinearAverageDelta);
            Append(builder, dump.BestStrategyName);
            Append(builder, dump.BestStrategyParameter);
            Append(builder, dump.BestAverageDelta);
            Append(builder, dump.BestImprovementRate);
            Append(builder, dump.BestComparisonCount);
            AppendLast(builder, dump.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void Append(StringBuilder builder, object? value)
    {
        builder.Append(FormatCsv(value));
        builder.Append(',');
    }

    private static void AppendLast(StringBuilder builder, object? value)
    {
        builder.Append(FormatCsv(value));
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
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}

public sealed record DumpGroupingReport(
    DateTimeOffset GeneratedAtUtc,
    string InputRoot,
    int DumpCount,
    int ClearImprovementDumpCount,
    string DecisionBand,
    string CsvPath,
    DumpGroupingResult[] Dumps);

public sealed record DumpGroupingResult(
    string DumpName,
    string CsvPath,
    int SampleCount,
    double LinearAverageDelta,
    string BestStrategyName,
    int BestStrategyParameter,
    double BestAverageDelta,
    double BestImprovementRate,
    int BestComparisonCount,
    string DecisionBand,
    GroupingStrategyScore[] TopStrategies);

public sealed record GroupingStrategyScore(
    string Name,
    int Parameter,
    int ComparisonCount,
    double AverageDelta,
    double ImprovementRate);

public sealed record TripleSample(int Index, double X, double Y, double Z);

public sealed record DeltaScore(double AverageDelta, int ComparisonCount);
