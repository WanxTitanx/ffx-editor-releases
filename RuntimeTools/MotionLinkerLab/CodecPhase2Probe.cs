using System.Buffers.Binary;

namespace MotionLinkerLab;

public static class MgrpCodecPhase2Probe
{
    internal static readonly KnownScale[] KnownScales =
    [
        new("identity", 1.0),
        new("i32", 1.0 / 32.0),
        new("i64", 1.0 / 64.0),
        new("i128", 1.0 / 128.0),
        new("i256", 1.0 / 256.0),
        new("i512", 1.0 / 512.0),
        new("i1024", 1.0 / 1024.0),
        new("i2048", 1.0 / 2048.0),
        new("i4096", 1.0 / 4096.0),
        new("i8192", 1.0 / 8192.0),
        new("i16384", 1.0 / 16384.0),
        new("half_pi", Math.PI / 2.0),
        new("pi", Math.PI),
        new("two_pi", Math.PI * 2.0),
    ];

    public static CodecPhase2Report Run(string ps2Root, IReadOnlyList<string> monsterIds)
    {
        var monsters = monsterIds
            .Select(monsterId => ProbeMonster(ps2Root, monsterId))
            .ToArray();
        var records = monsters.SelectMany(item => item.Records).ToArray();
        var topCandidates = records
            .SelectMany(item => item.TopCandidates)
            .OrderByDescending(item => item.Score)
            .Take(48)
            .ToArray();

        var candidateCount = records.Sum(item => item.CandidateCount);
        var strongCandidates = records.Sum(item => item.StrongCandidateCount);
        var bindPosePresentCount = monsters.Count(item => item.BindPose.Exists && item.BindPose.ValidNodeCount > 0);
        var verdict = strongCandidates > 0
            ? "substream_boundaries_ranked_but_codec_semantics_still_blocked"
            : "substream_probe_did_not_promote_decode";

        return new CodecPhase2Report(
            DateTimeOffset.UtcNow,
            ps2Root,
            monsters.Length,
            records.Length,
            records.Sum(item => item.SegmentCount),
            candidateCount,
            strongCandidates,
            bindPosePresentCount,
            "blocked_honest_substreams_ranked_no_modelviewer_promotion",
            verdict,
            topCandidates,
            monsters);
    }

    private static MonsterCodecPhase2 ProbeMonster(string ps2Root, string monsterId)
    {
        var monsterNumber = int.Parse(monsterId[1..]);
        var monByte = monsterNumber & 0xFF;
        var monsterRoot = Path.Combine(ps2Root, "chr", "mon", monsterId);
        var chrPath = Path.Combine(monsterRoot, "mdl", $"{monsterId}.chr");
        var bindPose = ChrBindPoseReader.Parse(chrPath);
        var motionRoot = Path.Combine(monsterRoot, "mot");

        var records = new List<RecordCodecPhase2>();
        var nonEmptyResidentCount = 0;
        for (var slot = 0; slot < 4; slot++)
        {
            var path = Path.Combine(motionRoot, $"resident{slot}.mgrp");
            var file = MgrpParser.Parse(path, slot, monByte);
            if (!file.IsNonEmpty || !File.Exists(path))
            {
                continue;
            }

            nonEmptyResidentCount++;
            var bytes = File.ReadAllBytes(path);
            foreach (var record in file.Records)
            {
                records.Add(ProbeRecord(monsterId, slot, path, bytes, record, bindPose));
            }
        }

        return new MonsterCodecPhase2(
            monsterId,
            chrPath,
            motionRoot,
            bindPose,
            nonEmptyResidentCount,
            records.Count,
            records
                .SelectMany(item => item.TopCandidates)
                .OrderByDescending(item => item.Score)
                .Take(16)
                .ToArray(),
            records.ToArray());
    }

    private static RecordCodecPhase2 ProbeRecord(
        string monsterId,
        int slot,
        string path,
        byte[] bytes,
        MgrpRecordInfo record,
        ChrBindPoseSummary bindPose)
    {
        var segments = BuildSegments(bytes, record).ToArray();
        var candidatesSource = segments
            .Where(segment => segment.Length >= 12)
            .ToArray();

        var allCandidates = new List<Phase2Candidate>();
        foreach (var segment in candidatesSource)
        {
            foreach (var candidate in ProbeSegment(monsterId, slot, record.Index, segment, bindPose))
            {
                allCandidates.Add(candidate);
            }
        }

        var topCandidates = allCandidates
            .OrderByDescending(item => item.Score)
            .Take(16)
            .ToArray();

        return new RecordCodecPhase2(
            monsterId,
            slot,
            path,
            record.Index,
            record.ChannelCount,
            record.GroupCount,
            record.OffA,
            record.OffB,
            record.FirstOffX,
            segments.Length,
            allCandidates.Count,
            allCandidates.Count(IsStrongCandidate),
            segments.Select(Phase2SegmentSummary.FromSegment).ToArray(),
            topCandidates);
    }

    private static IEnumerable<Phase2Candidate> ProbeSegment(
        string monsterId,
        int slot,
        int recordIndex,
        ByteSegment segment,
        ChrBindPoseSummary bindPose)
    {
        foreach (var localSkip in BuildSkipCandidates(segment.Length))
        {
            var localBytes = Slice(segment.Bytes, localSkip, segment.Bytes.Length);
            var absoluteStart = segment.Start + localSkip;
            foreach (var bitsPerComponent in Enumerable.Range(8, 9))
            {
                var sampleBits = bitsPerComponent * 3;
                for (var bitOffset = 0; bitOffset < 8; bitOffset++)
                {
                    var triples = DecodeSignedTriples(localBytes, bitOffset, bitsPerComponent, 96).ToArray();
                    if (triples.Length < 4)
                    {
                        continue;
                    }

                    var stats = TripleStats.FromTriples(triples);
                    if (stats.MeanAxisRange <= 0.000001)
                    {
                        continue;
                    }

                    var smoothScore = 1.0 / (1.0 + stats.AverageDelta);
                    var variationScore = Clamp01(stats.MeanAxisRange / 0.20);
                    var saturationScore = 1.0 - stats.SaturatedComponentRate;
                    var sampleScore = Clamp01(triples.Length / 24.0);
                    var skipScore = localSkip == 0
                        ? 0.72
                        : localSkip % 4 == 0
                            ? 1.0
                            : 0.88;
                    var structuralScore = smoothScore
                        * (0.35 + variationScore * 0.65)
                        * (0.55 + saturationScore * 0.45)
                        * sampleScore
                        * skipScore;

                    var fit = BindFitSummary.From(bindPose, stats);
                    var score = structuralScore * 0.80 + (fit?.FitScore ?? 0.35) * 0.20;

                    yield return new Phase2Candidate(
                        monsterId,
                        slot,
                        recordIndex,
                        segment.Label,
                        segment.Start,
                        segment.End,
                        localSkip,
                        absoluteStart,
                        bitsPerComponent,
                        bitOffset,
                        sampleBits,
                        triples.Length,
                        stats.AverageDelta,
                        stats.MeanVectorMagnitude,
                        stats.MeanAbsComponent,
                        stats.MeanAxisRange,
                        stats.SaturatedComponentRate,
                        structuralScore,
                        fit?.FitScore,
                        score,
                        fit?.BestScale,
                        fit?.BestKnownScaleName,
                        fit?.BestKnownScaleRelativeError,
                        stats.Axes);
                }
            }
        }
    }

    private static bool IsStrongCandidate(Phase2Candidate candidate) =>
        candidate.Score >= 0.60
        && candidate.StructuralScore >= 0.55
        && candidate.SampleCount >= 8
        && candidate.MeanAxisRange >= 0.015
        && candidate.SaturatedComponentRate <= 0.35;

    private static int[] BuildSkipCandidates(int segmentLength)
    {
        var maxSkip = Math.Min(32, segmentLength - 12);
        if (maxSkip < 0)
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(0, maxSkip + 1).ToArray();
    }

    internal static IEnumerable<ByteSegment> BuildSegments(byte[] bytes, MgrpRecordInfo record)
    {
        var starts = new SortedDictionary<int, List<string>>();
        foreach (var group in record.Groups)
        {
            AddStart(group.PtrA, $"g{group.Index}.ptrA");
            AddStart(group.PtrB, $"g{group.Index}.ptrB");
        }

        var payloadEnd = Math.Min(bytes.Length, checked((int)record.OffB));
        var offsets = starts.Keys.Where(offset => offset >= 16 && offset < payloadEnd).Order().ToArray();
        for (var i = 0; i < offsets.Length; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Length ? offsets[i + 1] : payloadEnd;
            if (end <= start)
            {
                continue;
            }

            yield return new ByteSegment(string.Join("+", starts[start]), start, end, Slice(bytes, start, end));
        }

        void AddStart(uint offset, string label)
        {
            if (offset >= bytes.Length)
            {
                return;
            }

            var intOffset = checked((int)offset);
            if (!starts.TryGetValue(intOffset, out var labels))
            {
                labels = new List<string>();
                starts[intOffset] = labels;
            }

            labels.Add(label);
        }
    }

    internal static IEnumerable<double[]> DecodeSignedTriples(byte[] bytes, int bitOffset, int bitsPerComponent, int maxSamples)
    {
        var reader = new LsbBitReader(bytes, bitOffset);
        var maxAbs = (1 << (bitsPerComponent - 1)) - 1;

        for (var sample = 0; sample < maxSamples; sample++)
        {
            if (reader.RemainingBits < bitsPerComponent * 3)
            {
                yield break;
            }

            yield return
            [
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
            ];
        }
    }

    private static byte[] Slice(byte[] bytes, int start, int end)
    {
        if (start < 0 || end < start || end > bytes.Length)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[end - start];
        Array.Copy(bytes, start, result, 0, result.Length);
        return result;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}

public static class ChrBindPoseReader
{
    public static ChrBindPoseSummary Parse(string path)
    {
        if (!File.Exists(path))
        {
            return ChrBindPoseSummary.Missing(path);
        }

        var bytes = File.ReadAllBytes(path);
        uint? version = bytes.Length >= 8 ? ReadU32(bytes, 4) : null;
        uint? nodeTableOffset = bytes.Length >= 0x24 ? ReadU32(bytes, 0x20) : null;
        uint? nodeCount = bytes.Length >= 0x28 ? ReadU32(bytes, 0x24) : null;
        var nodes = new List<ChrBindNode>();

        if (nodeTableOffset.HasValue && nodeCount.HasValue)
        {
            var start = checked((int)nodeTableOffset.Value);
            var count = (int)Math.Min(nodeCount.Value, 256);
            if (start >= 0 && start + count * 16 <= bytes.Length)
            {
                for (var index = 0; index < count; index++)
                {
                    var offset = start + index * 16;
                    var vector = new[]
                    {
                        BitConverter.ToSingle(bytes, offset + 4),
                        BitConverter.ToSingle(bytes, offset + 8),
                        BitConverter.ToSingle(bytes, offset + 12),
                    };

                    if (vector.All(float.IsFinite))
                    {
                        nodes.Add(new ChrBindNode(
                            index,
                            bytes[offset],
                            bytes[offset + 1],
                            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2)),
                            vector[0],
                            vector[1],
                            vector[2],
                            Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2])));
                    }
                }
            }
        }

        var axisStats = AxisStats.FromVectors(nodes.Select(item => new[] { item.X, item.Y, item.Z }).ToArray());
        var meanMagnitude = nodes.Count == 0 ? 0 : nodes.Average(item => item.Magnitude);

        return new ChrBindPoseSummary(
            path,
            true,
            bytes.LongLength,
            version,
            nodeTableOffset,
            nodeCount,
            nodes.Count,
            nodes.Count(item => item.Flag == 0x40),
            meanMagnitude,
            axisStats.Length == 0 ? 0 : axisStats.Average(item => item.MeanAbs),
            axisStats.Length == 0 ? 0 : axisStats.Average(item => item.Range),
            axisStats,
            nodes.ToArray());
    }

    private static uint ReadU32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}

public sealed record CodecPhase2Report(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    int MonsterCount,
    int RecordCount,
    int SegmentCount,
    int CandidateCount,
    int StrongSubstreamCandidates,
    int BindPosePresentCount,
    string DecisionBand,
    string Phase2Verdict,
    Phase2Candidate[] TopCandidates,
    MonsterCodecPhase2[] Monsters);

public sealed record MonsterCodecPhase2(
    string MonsterId,
    string ChrPath,
    string MotionRoot,
    ChrBindPoseSummary BindPose,
    int NonEmptyResidentCount,
    int RecordCount,
    Phase2Candidate[] TopCandidates,
    RecordCodecPhase2[] Records);

public sealed record RecordCodecPhase2(
    string MonsterId,
    int Slot,
    string Path,
    int RecordIndex,
    ushort ChannelCount,
    ushort GroupCount,
    uint OffA,
    uint OffB,
    uint? FirstOffX,
    int SegmentCount,
    int CandidateCount,
    int StrongCandidateCount,
    Phase2SegmentSummary[] Segments,
    Phase2Candidate[] TopCandidates);

public sealed record Phase2SegmentSummary(
    string Label,
    int Start,
    int End,
    int Length,
    ushort[] FirstU16Values,
    bool Has77777777AtPlus4,
    int[] Sentinel77777777AbsoluteOffsets,
    string HexPrefix)
{
    public static Phase2SegmentSummary FromSegment(ByteSegment segment)
    {
        var u16 = new List<ushort>();
        for (var offset = 0; offset + 2 <= Math.Min(segment.Bytes.Length, 24); offset += 2)
        {
            u16.Add(BinaryPrimitives.ReadUInt16LittleEndian(segment.Bytes.AsSpan(offset, 2)));
        }

        var sentinels = new List<int>();
        for (var offset = 0; offset + 4 <= segment.Bytes.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(segment.Bytes.AsSpan(offset, 4)) == 0x77777777)
            {
                sentinels.Add(segment.Start + offset);
            }
        }

        var hasSentinelAtPlus4 = segment.Bytes.Length >= 8
            && BinaryPrimitives.ReadUInt32LittleEndian(segment.Bytes.AsSpan(4, 4)) == 0x77777777;

        return new Phase2SegmentSummary(
            segment.Label,
            segment.Start,
            segment.End,
            segment.Length,
            u16.ToArray(),
            hasSentinelAtPlus4,
            sentinels.Take(12).ToArray(),
            Convert.ToHexString(segment.Bytes.Take(48).ToArray()));
    }
}

public sealed record Phase2Candidate(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int SegmentStart,
    int SegmentEnd,
    int LocalByteSkip,
    int AbsoluteStart,
    int BitsPerComponent,
    int BitOffset,
    int SampleBits,
    int SampleCount,
    double AverageDelta,
    double MeanVectorMagnitude,
    double MeanAbsComponent,
    double MeanAxisRange,
    double SaturatedComponentRate,
    double StructuralScore,
    double? BindFitScore,
    double Score,
    double? BestScale,
    string? BestKnownScaleName,
    double? BestKnownScaleRelativeError,
    AxisStats[] Axes);

public sealed record ChrBindPoseSummary(
    string Path,
    bool Exists,
    long? Length,
    uint? Version,
    uint? NodeTableOffset,
    uint? NodeCount,
    int ValidNodeCount,
    int Flag40NodeCount,
    double MeanVectorMagnitude,
    double MeanAbsComponent,
    double MeanAxisRange,
    AxisStats[] Axes,
    ChrBindNode[] Nodes)
{
    public static ChrBindPoseSummary Missing(string path) =>
        new(path, false, null, null, null, null, 0, 0, 0, 0, 0, Array.Empty<AxisStats>(), Array.Empty<ChrBindNode>());
}

public sealed record ChrBindNode(
    int TableIndex,
    byte NodeIndex,
    byte Flag,
    ushort RawU16,
    double X,
    double Y,
    double Z,
    double Magnitude);

public sealed record AxisStats(
    int Axis,
    double Min,
    double Max,
    double Range,
    double MeanAbs)
{
    public static AxisStats[] FromVectors(IReadOnlyList<double[]> vectors)
    {
        if (vectors.Count == 0)
        {
            return Array.Empty<AxisStats>();
        }

        var result = new AxisStats[3];
        for (var axis = 0; axis < 3; axis++)
        {
            var values = vectors.Select(item => item[axis]).ToArray();
            result[axis] = new AxisStats(
                axis,
                values.Min(),
                values.Max(),
                values.Max() - values.Min(),
                values.Average(item => Math.Abs(item)));
        }

        return result;
    }
}

public sealed record TripleStats(
    int SampleCount,
    double AverageDelta,
    double MeanVectorMagnitude,
    double MeanAbsComponent,
    double MeanAxisRange,
    double SaturatedComponentRate,
    AxisStats[] Axes)
{
    public static TripleStats FromTriples(IReadOnlyList<double[]> triples)
    {
        var axes = AxisStats.FromVectors(triples);
        var meanVectorMagnitude = triples.Count == 0
            ? 0
            : triples.Average(item => Math.Sqrt(item[0] * item[0] + item[1] * item[1] + item[2] * item[2]));
        var saturatedComponents = triples.Sum(item => item.Count(component => Math.Abs(component) >= 0.985));
        var totalComponents = triples.Count * 3;

        return new TripleStats(
            triples.Count,
            ComputeAverageDelta(triples),
            meanVectorMagnitude,
            axes.Length == 0 ? 0 : axes.Average(item => item.MeanAbs),
            axes.Length == 0 ? 0 : axes.Average(item => item.Range),
            totalComponents == 0 ? 0 : saturatedComponents / (double)totalComponents,
            axes);
    }

    private static double ComputeAverageDelta(IReadOnlyList<double[]> triples)
    {
        if (triples.Count < 2)
        {
            return double.PositiveInfinity;
        }

        var total = 0.0;
        for (var i = 1; i < triples.Count; i++)
        {
            var dx = triples[i][0] - triples[i - 1][0];
            var dy = triples[i][1] - triples[i - 1][1];
            var dz = triples[i][2] - triples[i - 1][2];
            total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        return total / (triples.Count - 1);
    }
}

public sealed record BindFitSummary(
    double FitScore,
    double BestScale,
    string BestKnownScaleName,
    double BestKnownScaleRelativeError)
{
    public static BindFitSummary? From(ChrBindPoseSummary bindPose, TripleStats stats)
    {
        if (!bindPose.Exists || bindPose.ValidNodeCount == 0 || stats.MeanVectorMagnitude <= 0.000001)
        {
            return null;
        }

        var targetMagnitude = bindPose.MeanVectorMagnitude > 0.000001
            ? bindPose.MeanVectorMagnitude
            : bindPose.MeanAbsComponent;
        if (targetMagnitude <= 0.000001)
        {
            return null;
        }

        var bestScale = targetMagnitude / stats.MeanVectorMagnitude;
        var bestKnown = KnownScale.FindClosest(bestScale);
        var scaleScore = 1.0 / (1.0 + bestKnown.RelativeError);
        var scaledRange = stats.MeanAxisRange * bestScale;
        var rangeScore = bindPose.MeanAxisRange <= 0.000001 || scaledRange <= 0.000001
            ? 0.35
            : 1.0 / (1.0 + Math.Abs(Math.Log(scaledRange / bindPose.MeanAxisRange)));
        var fitScore = Math.Clamp(scaleScore * 0.55 + rangeScore * 0.45, 0.0, 1.0);

        return new BindFitSummary(fitScore, bestScale, bestKnown.Name, bestKnown.RelativeError);
    }
}

public sealed record KnownScale(string Name, double Value)
{
    public static (string Name, double Value, double RelativeError) FindClosest(double value)
    {
        var best = MgrpCodecPhase2Probe.KnownScales
            .Select(item => (item.Name, item.Value, RelativeError: Math.Abs(Math.Log(value / item.Value))))
            .OrderBy(item => item.RelativeError)
            .First();
        return best;
    }
}
