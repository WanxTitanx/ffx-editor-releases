using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MotionLinkerLab;

public static class MgrpUnsafeLabPackager
{
    private const double UnsafeFrameRate = 30.0;

    public static UnsafeLabPackageReport Create(
        string ps2Root,
        string dumpRoot,
        string semanticsReportPath,
        string manifestRoot,
        string outputRoot)
    {
        var semantics = JsonSerializer.Deserialize<MotionSemanticsReport>(File.ReadAllText(semanticsReportPath))
            ?? throw new InvalidOperationException($"Could not read semantics report: {semanticsReportPath}");
        var candidateRows = semantics.Phase6Rows
            .Where(item => item.DecisionBand == "partial_grouped_absolute_candidate")
            .ToArray();

        var candidates = new List<UnsafeMotionCandidate>();
        foreach (var row in candidateRows)
        {
            var dumpReportPath = Path.Combine(dumpRoot, $"{row.DumpName}.triple-dump.json");
            var dump = JsonSerializer.Deserialize<TripleDumpReport>(File.ReadAllText(dumpReportPath))
                ?? throw new InvalidOperationException($"Could not read dump report: {dumpReportPath}");
            var samples = ReadSamples(dump.CsvPath);
            candidates.Add(BuildCandidate(ps2Root, row, dump, samples, outputRoot));
        }

        var poseCsv = Path.Combine(outputRoot, "unsafe-candidate-pose.csv");
        var playbackCsv = Path.Combine(outputRoot, "unsafe-candidate-playback.csv");
        var statusCsv = Path.Combine(outputRoot, "modelviewer-readonly-status.csv");
        var exportJson = Path.Combine(outputRoot, "unsafe_candidate_motion.json");
        var exportCurves = Path.Combine(outputRoot, "unsafe_candidate_motion_curves.csv");

        WritePoseCsv(poseCsv, candidates);
        WritePlaybackCsv(playbackCsv, candidates);
        WriteModelViewerStatusCsv(statusCsv, manifestRoot, candidates);
        WriteExportCurvesCsv(exportCurves, candidates);
        File.WriteAllText(exportJson, JsonSerializer.Serialize(new UnsafeMotionExport(
            DateTimeOffset.UtcNow,
            "unsafe_candidate_motion",
            "lab_only_no_game_asset_write_no_decoder_promotion",
            UnsafeFrameRate,
            candidates.ToArray()), new JsonSerializerOptions { WriteIndented = true }));

        var gltfPaths = candidates.Select(candidate => WriteGltfExtras(outputRoot, candidate)).ToArray();

        return new UnsafeLabPackageReport(
            DateTimeOffset.UtcNow,
            dumpRoot,
            semanticsReportPath,
            manifestRoot,
            candidates.Count,
            "unsafe_partial_lab_artifacts_no_decoder_no_modelviewer_promotion",
            poseCsv,
            playbackCsv,
            statusCsv,
            exportJson,
            exportCurves,
            gltfPaths,
            candidates.ToArray());
    }

    private static UnsafeMotionCandidate BuildCandidate(
        string ps2Root,
        Phase6DeltaModeRow row,
        TripleDumpReport dump,
        IReadOnlyList<TripleSample> samples,
        string outputRoot)
    {
        var stride = row.BestGroupingStrategy.StartsWith("stride_", StringComparison.OrdinalIgnoreCase)
            ? row.BestStrategyParameterSafe()
            : 1;
        stride = Math.Max(1, stride);
        var monsterId = row.MonsterId;
        var chr = ChrBindPoseReader.Parse(Path.Combine(ps2Root, "chr", "mon", monsterId, "mdl", $"{monsterId}.chr"));
        var unsafeChannelCount = Math.Min(stride, Math.Max(1, chr.ValidNodeCount));
        var frames = samples
            .Select((sample, index) => new UnsafeMotionSample(
                index / stride,
                index % stride,
                (index / stride) / UnsafeFrameRate,
                index % unsafeChannelCount,
                sample.X,
                sample.Y,
                sample.Z))
            .ToArray();
        var frameCount = frames.Length == 0 ? 0 : frames.Max(item => item.FrameIndex) + 1;
        var pose = frames
            .Where(item => item.FrameIndex == 0)
            .GroupBy(item => item.UnsafeNodeIndex)
            .Select(group => group.First())
            .OrderBy(item => item.UnsafeNodeIndex)
            .ToArray();

        return new UnsafeMotionCandidate(
            monsterId,
            row.DumpName,
            dump.Request.SegmentLabel,
            row.BestGroupingStrategy,
            stride,
            unsafeChannelCount,
            chr.NodeCount,
            chr.ValidNodeCount,
            frameCount,
            frameCount <= 1 ? 0 : (frameCount - 1) / UnsafeFrameRate,
            "unsafe_ordinal_node_assignment",
            "unsafe_partial_pose_and_playback_candidate",
            pose,
            frames);
    }

    private static int BestStrategyParameterSafe(this Phase6DeltaModeRow row)
    {
        var digits = new string(row.BestGroupingStrategy.SkipWhile(ch => !char.IsDigit(ch)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 1;
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

    private static void WritePoseCsv(string path, IReadOnlyList<UnsafeMotionCandidate> candidates)
    {
        var builder = BeginCsv(path, "monsterId,dumpName,unsafeNodeIndex,channelIndex,x,y,z,decisionBand");
        foreach (var candidate in candidates)
        {
            foreach (var pose in candidate.PoseSamples)
            {
                AppendRow(builder, candidate.MonsterId, candidate.DumpName, pose.UnsafeNodeIndex, pose.ChannelIndex, pose.X, pose.Y, pose.Z, candidate.DecisionBand);
            }
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePlaybackCsv(string path, IReadOnlyList<UnsafeMotionCandidate> candidates)
    {
        var builder = BeginCsv(path, "monsterId,dumpName,frameIndex,timeSeconds,channelIndex,unsafeNodeIndex,x,y,z,decisionBand");
        foreach (var candidate in candidates)
        {
            foreach (var frame in candidate.Samples)
            {
                AppendRow(builder, candidate.MonsterId, candidate.DumpName, frame.FrameIndex, frame.TimeSeconds, frame.ChannelIndex, frame.UnsafeNodeIndex, frame.X, frame.Y, frame.Z, candidate.DecisionBand);
            }
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteModelViewerStatusCsv(string path, string manifestRoot, IReadOnlyList<UnsafeMotionCandidate> candidates)
    {
        var candidateSet = candidates.Select(item => item.MonsterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = BeginCsv(path, "monsterId,motionReadiness,embeddedClipCount,nonEmptyResidentCount,labStatus,sourceManifest");
        var indexPath = Path.Combine(manifestRoot, "index.motion-link.json");
        if (!File.Exists(indexPath))
        {
            File.WriteAllText(path, builder.ToString());
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
        foreach (var entry in doc.RootElement.GetProperty("Entries").EnumerateArray())
        {
            var monsterId = entry.GetProperty("MonsterId").GetString() ?? "";
            var motionReadiness = entry.GetProperty("MotionReadiness").GetString() ?? "";
            var embedded = entry.GetProperty("EmbeddedAnimationClipCount").GetInt32();
            var nonEmpty = entry.GetProperty("NonEmptyResidentCount").GetInt32();
            var manifestPath = entry.GetProperty("ManifestPath").GetString() ?? "";
            var status = candidateSet.Contains(monsterId)
                ? "unsafe_lab_candidate_motion"
                : embedded > 0
                    ? "ps3_clip_control_positive"
                    : nonEmpty > 0
                        ? "ps2_codec_blocked_or_phase2_candidate"
                        : "static_or_no_resident_motion";
            AppendRow(builder, monsterId, motionReadiness, embedded, nonEmpty, status, manifestPath);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteExportCurvesCsv(string path, IReadOnlyList<UnsafeMotionCandidate> candidates)
    {
        var builder = BeginCsv(path, "monsterId,dumpName,curveName,timeSeconds,unsafeNodeIndex,x,y,z");
        foreach (var candidate in candidates)
        {
            foreach (var sample in candidate.Samples)
            {
                AppendRow(builder, candidate.MonsterId, candidate.DumpName, $"unsafe_node_{sample.UnsafeNodeIndex:00}", sample.TimeSeconds, sample.UnsafeNodeIndex, sample.X, sample.Y, sample.Z);
            }
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static string WriteGltfExtras(string outputRoot, UnsafeMotionCandidate candidate)
    {
        var path = Path.Combine(outputRoot, $"{SanitizeStem(candidate.DumpName)}_unsafe_candidate_motion.gltf");
        var nodes = Enumerable.Range(0, candidate.UnsafeChannelCount)
            .Select(index => new Dictionary<string, object?>
            {
                ["name"] = $"unsafe_node_{index:00}",
                ["extras"] = new Dictionary<string, object?>
                {
                    ["source"] = "MGRP unsafe ordinal candidate",
                    ["nodeIndex"] = index,
                },
            })
            .ToArray();
        var gltf = new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object?>
            {
                ["version"] = "2.0",
                ["generator"] = "MotionLinkerLab unsafe-lab-package",
                ["extras"] = new Dictionary<string, object?>
                {
                    ["decisionBand"] = candidate.DecisionBand,
                    ["warning"] = "unsafe candidate motion; no real MGRP decoder, remap, or timing proof",
                },
            },
            ["scene"] = 0,
            ["scenes"] = new[]
            {
                new Dictionary<string, object?> { ["name"] = "unsafe_candidate_motion", ["nodes"] = Enumerable.Range(0, candidate.UnsafeChannelCount).ToArray() },
            },
            ["nodes"] = nodes,
            ["extras"] = new Dictionary<string, object?>
            {
                ["monsterId"] = candidate.MonsterId,
                ["dumpName"] = candidate.DumpName,
                ["frameRate"] = UnsafeFrameRate,
                ["samples"] = candidate.Samples.Take(256).ToArray(),
            },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(gltf, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string SanitizeStem(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        }

        return builder.ToString();
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

public sealed record UnsafeLabPackageReport(
    DateTimeOffset GeneratedAtUtc,
    string DumpRoot,
    string SemanticsReportPath,
    string ManifestRoot,
    int CandidateCount,
    string DecisionBand,
    string PoseCsvPath,
    string PlaybackCsvPath,
    string ModelViewerStatusCsvPath,
    string ExportJsonPath,
    string ExportCurveCsvPath,
    string[] GltfPaths,
    UnsafeMotionCandidate[] Candidates);

public sealed record UnsafeMotionExport(
    DateTimeOffset GeneratedAtUtc,
    string PackageKind,
    string DecisionBand,
    double AssumedFrameRate,
    UnsafeMotionCandidate[] Candidates);

public sealed record UnsafeMotionCandidate(
    string MonsterId,
    string DumpName,
    string SegmentLabel,
    string GroupingStrategy,
    int GroupingStride,
    int UnsafeChannelCount,
    uint? ChrNodeCount,
    int ValidNodeCount,
    int FrameCount,
    double DurationSeconds,
    string RemapPolicy,
    string DecisionBand,
    UnsafeMotionSample[] PoseSamples,
    UnsafeMotionSample[] Samples);

public sealed record UnsafeMotionSample(
    int FrameIndex,
    int ChannelIndex,
    double TimeSeconds,
    int UnsafeNodeIndex,
    double X,
    double Y,
    double Z);
