using System.Buffers.Binary;
using System.Text;

namespace MotionLinkerLab;

public static class MgrpCodecProbe
{
    public static CodecProbeReport Run(string ps2Root, IReadOnlyList<string> monsterIds)
    {
        var monsters = monsterIds
            .Select(monsterId => ProbeMonster(ps2Root, monsterId))
            .ToArray();
        var records = monsters.SelectMany(item => item.Records).ToArray();

        var floatGte10 = records.Count(item => item.Float32.BestPlausibleRate >= 0.10);
        var floatGte05 = records.Count(item => item.Float32.BestPlausibleRate >= 0.05);
        var int16GridCleanCount = records.Count(item => item.Int16.CleanChannelGridCandidate);
        var topSmallestThreePerMonster = monsters
            .Select(item => item.TopSmallestThreeCandidates.FirstOrDefault())
            .Where(item => item is not null)
            .Cast<SmallestThreeCandidate>()
            .ToArray();
        var topEulerFixedPerMonster = monsters
            .Select(item => item.TopEulerFixedCandidates.FirstOrDefault())
            .Where(item => item is not null)
            .Cast<EulerFixedCandidate>()
            .ToArray();
        var bestSmallestThree = topSmallestThreePerMonster.OrderByDescending(item => item.Score).FirstOrDefault();
        var bestEulerFixed = topEulerFixedPerMonster.OrderByDescending(item => item.Score).FirstOrDefault();
        var smallestScoreGte75 = topSmallestThreePerMonster.Count(item => item.Score >= 0.75);
        var eulerScoreGte75 = topEulerFixedPerMonster.Count(item => item.Score >= 0.75);

        var floatVerdict = floatGte10 <= Math.Max(2, records.Length / 20)
            ? "rejected_float32_sparse_plausibility_hits"
            : "not_rejected_needs_manual_review";
        var int16Verdict = int16GridCleanCount <= records.Length / 4
            ? "not_clean_int16_frame_grid_sparse_alignment_hits"
            : "some_int16_channel_grid_candidates_exist";
        var smallestVerdict = bestSmallestThree is { ValidRate: >= 0.90, AverageAbsDot: >= 0.85, Score: >= 0.75 }
            ? "candidate_but_not_proved_smallest_three"
            : "no_strong_smallest_three_candidate";
        var eulerVerdict = bestEulerFixed is { AverageDelta: <= 0.35, Score: >= 0.70 } && eulerScoreGte75 >= 3
            ? "candidate_but_not_proved_fixed_signed_triples"
            : "no_strong_fixed_triple_candidate";

        return new CodecProbeReport(
            DateTimeOffset.UtcNow,
            ps2Root,
            monsters.Length,
            records.Length,
            floatGte10,
            floatGte05,
            int16GridCleanCount,
            smallestScoreGte75,
            eulerScoreGte75,
            "blocked_honest_signed_quantized_bitpacked_still_best_description",
            floatVerdict,
            int16Verdict,
            smallestVerdict,
            eulerVerdict,
            monsters);
    }

    private static MonsterCodecProbe ProbeMonster(string ps2Root, string monsterId)
    {
        var monByte = int.Parse(monsterId[1..]) & 0xFF;
        var motionRoot = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot");
        var records = new List<RecordCodecProbe>();
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
                records.Add(ProbeRecord(monsterId, slot, path, bytes, record));
            }
        }

        var topSmallestThree = records
            .SelectMany(item => item.SmallestThreeCandidates)
            .OrderByDescending(item => item.Score)
            .Take(8)
            .ToArray();
        var topEulerFixed = records
            .SelectMany(item => item.EulerFixedCandidates)
            .OrderByDescending(item => item.Score)
            .Take(8)
            .ToArray();

        return new MonsterCodecProbe(
            monsterId,
            motionRoot,
            nonEmptyResidentCount,
            records.Count,
            records.ToArray(),
            topSmallestThree,
            topEulerFixed);
    }

    private static RecordCodecProbe ProbeRecord(string monsterId, int slot, string path, byte[] bytes, MgrpRecordInfo record)
    {
        var bulkStart = 16;
        var bulkEnd = checked((int)record.OffB);
        var bulk = Slice(bytes, bulkStart, bulkEnd);
        var channelRegion = record.FirstOffX.HasValue
            ? Slice(bytes, checked((int)record.FirstOffX.Value), checked((int)record.OffA))
            : Array.Empty<byte>();

        var segments = BuildSegments(bytes, record).ToArray();
        var payloadHeader = PayloadHeaderProbe.Parse(bulk, bulkStart);
        var segmentSummaries = segments.Select(segment => SegmentSummary.FromSegment(segment)).ToArray();

        var candidatesSource = segments
            .Where(segment => segment.Length >= 16)
            .Append(new ByteSegment("bulk", bulkStart, bulkEnd, bulk))
            .ToArray();

        var smallestThreeCandidates = candidatesSource
            .SelectMany(segment => HypothesisProbe.ProbeSmallestThree(monsterId, slot, record.Index, segment))
            .OrderByDescending(item => item.Score)
            .Take(12)
            .ToArray();

        var eulerFixedCandidates = candidatesSource
            .SelectMany(segment => HypothesisProbe.ProbeEulerFixed(monsterId, slot, record.Index, segment))
            .OrderByDescending(item => item.Score)
            .Take(12)
            .ToArray();

        return new RecordCodecProbe(
            monsterId,
            slot,
            path,
            record.Index,
            record.ChannelCount,
            record.GroupCount,
            record.OffA,
            record.OffB,
            record.FirstOffX,
            bulkStart,
            bulkEnd,
            bulk.Length,
            ByteStats.FromBytes(bulk),
            ByteStats.FromBytes(channelRegion),
            payloadHeader,
            FloatProbe.Analyze(bulk),
            Int16Probe.Analyze(bulk, record.ChannelCount),
            PeriodicityProbe.Analyze(bulk),
            segmentSummaries,
            smallestThreeCandidates,
            eulerFixedCandidates);
    }

    private static IEnumerable<ByteSegment> BuildSegments(byte[] bytes, MgrpRecordInfo record)
    {
        var starts = new SortedDictionary<int, List<string>>();
        foreach (var group in record.Groups)
        {
            AddStart(group.PtrA, $"g{group.Index}.ptrA");
            AddStart(group.PtrB, $"g{group.Index}.ptrB");
        }

        var offsets = starts.Keys.Where(offset => offset >= 16 && offset < record.OffB).Order().ToArray();
        for (var i = 0; i < offsets.Length; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Length ? offsets[i + 1] : checked((int)record.OffB);
            if (end <= start)
            {
                continue;
            }

            var label = string.Join("+", starts[start]);
            yield return new ByteSegment(label, start, end, Slice(bytes, start, end));
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
}

public static class PayloadHeaderProbe
{
    public static PayloadHeaderSummary Parse(byte[] bulk, int absoluteStart)
    {
        var u16 = new List<ushort>();
        for (var offset = 0; offset + 2 <= Math.Min(bulk.Length, 32); offset += 2)
        {
            u16.Add(BinaryPrimitives.ReadUInt16LittleEndian(bulk.AsSpan(offset, 2)));
        }

        uint? dwordAt4 = bulk.Length >= 8 ? BinaryPrimitives.ReadUInt32LittleEndian(bulk.AsSpan(4, 4)) : null;
        var sentinelOffsets = new List<int>();
        for (var offset = 0; offset + 4 <= bulk.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bulk.AsSpan(offset, 4)) == 0x77777777)
            {
                sentinelOffsets.Add(absoluteStart + offset);
            }
        }

        var looksLikeKnownHeader =
            u16.Count >= 6
            && u16[0] == 0
            && dwordAt4 == 0x77777777
            && u16[1] == u16[4];

        return new PayloadHeaderSummary(
            looksLikeKnownHeader,
            u16.Take(12).ToArray(),
            dwordAt4,
            sentinelOffsets.ToArray(),
            HexPrefix(bulk, 32));
    }

    private static string HexPrefix(byte[] bytes, int count) =>
        Convert.ToHexString(bytes.Take(count).ToArray());
}

public static class FloatProbe
{
    public static FloatProbeSummary Analyze(byte[] bytes)
    {
        var phases = Enumerable.Range(0, 4)
            .Select(phase => AnalyzePhase(bytes, phase))
            .ToArray();
        var best = phases.OrderByDescending(item => item.PlausibleRate).FirstOrDefault()
            ?? new FloatPhaseSummary(0, 0, 0, 0, 0, null, null);
        return new FloatProbeSummary(best.PlausibleRate, best.Phase, phases);
    }

    private static FloatPhaseSummary AnalyzePhase(byte[] bytes, int phase)
    {
        var count = 0;
        var finite = 0;
        var plausible = 0;
        var zero = 0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        for (var offset = phase; offset + 4 <= bytes.Length; offset += 4)
        {
            count++;
            var value = BitConverter.ToSingle(bytes, offset);
            if (!float.IsFinite(value))
            {
                continue;
            }

            finite++;
            if (value == 0)
            {
                zero++;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
            var abs = Math.Abs(value);
            if (abs is >= 0.0001f and <= 1000f)
            {
                plausible++;
            }
        }

        return new FloatPhaseSummary(
            phase,
            count,
            count == 0 ? 0 : (double)finite / count,
            count == 0 ? 0 : (double)plausible / count,
            count == 0 ? 0 : (double)zero / count,
            finite == 0 ? null : min,
            finite == 0 ? null : max);
    }
}

public static class Int16Probe
{
    public static Int16ProbeSummary Analyze(byte[] bytes, int channelCount)
    {
        var phases = Enumerable.Range(0, 2)
            .Select(phase => AnalyzePhase(bytes, phase))
            .ToArray();

        var remainders = new[]
        {
            new GridRemainder("channels_x_i16", channelCount * 2, channelCount == 0 ? null : bytes.Length % (channelCount * 2)),
            new GridRemainder("channels_x_vec3_i16", channelCount * 6, channelCount == 0 ? null : bytes.Length % (channelCount * 6)),
            new GridRemainder("channels_x_quat_i16", channelCount * 8, channelCount == 0 ? null : bytes.Length % (channelCount * 8)),
            new GridRemainder("channels_x_rot_trans_i16", channelCount * 12, channelCount == 0 ? null : bytes.Length % (channelCount * 12)),
        };

        var cleanGrid = remainders.Any(item => item.Remainder == 0 && item.Divisor > 0);
        var bestPhase = phases.OrderByDescending(item => item.AbsUnder8192Rate).First();
        return new Int16ProbeSummary(cleanGrid, bestPhase.Phase, phases, remainders);
    }

    private static Int16PhaseSummary AnalyzePhase(byte[] bytes, int phase)
    {
        var values = new List<short>();
        for (var offset = phase; offset + 2 <= bytes.Length; offset += 2)
        {
            values.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2)));
        }

        if (values.Count == 0)
        {
            return new Int16PhaseSummary(phase, 0, 0, 0, 0, 0, Array.Empty<short>());
        }

        return new Int16PhaseSummary(
            phase,
            values.Count,
            values.Count(item => item == 0) / (double)values.Count,
            values.Count(item => Math.Abs((int)item) <= 1024) / (double)values.Count,
            values.Count(item => Math.Abs((int)item) <= 8192) / (double)values.Count,
            values.Average(item => Math.Abs((double)item)),
            values.Take(24).ToArray());
    }
}

public static class PeriodicityProbe
{
    public static PeriodicitySummary Analyze(byte[] bytes)
    {
        var scores = new List<PeriodScore>();
        for (var period = 1; period <= 24; period++)
        {
            if (bytes.Length <= period)
            {
                continue;
            }

            var matches = 0;
            for (var index = period; index < bytes.Length; index++)
            {
                if (bytes[index] == bytes[index - period])
                {
                    matches++;
                }
            }

            scores.Add(new PeriodScore(period, matches / (double)(bytes.Length - period)));
        }

        return new PeriodicitySummary(scores.OrderByDescending(item => item.MatchRate).Take(8).ToArray());
    }
}

public static class HypothesisProbe
{
    public static IEnumerable<SmallestThreeCandidate> ProbeSmallestThree(
        string monsterId,
        int slot,
        int recordIndex,
        ByteSegment segment)
    {
        foreach (var bitsPerComponent in Enumerable.Range(8, 9))
        {
            var sampleBits = 2 + bitsPerComponent * 3;
            for (var bitOffset = 0; bitOffset < 8; bitOffset++)
            {
                var decoded = DecodeSmallestThree(segment.Bytes, bitOffset, bitsPerComponent, 128).ToArray();
                if (decoded.Length < 4)
                {
                    continue;
                }

                var valid = decoded.Where(item => item.Valid).ToArray();
                var validRate = valid.Length / (double)decoded.Length;
                var averageAbsDot = AverageAbsDot(valid.Select(item => item.Components).ToArray());
                var score = validRate * averageAbsDot * Math.Min(1.0, decoded.Length / 16.0);

                yield return new SmallestThreeCandidate(
                    monsterId,
                    slot,
                    recordIndex,
                    segment.Label,
                    segment.Start,
                    segment.End,
                    bitsPerComponent,
                    bitOffset,
                    sampleBits,
                    decoded.Length,
                    validRate,
                    averageAbsDot,
                    score);
            }
        }
    }

    public static IEnumerable<EulerFixedCandidate> ProbeEulerFixed(
        string monsterId,
        int slot,
        int recordIndex,
        ByteSegment segment)
    {
        foreach (var bitsPerComponent in Enumerable.Range(8, 9))
        {
            var sampleBits = bitsPerComponent * 3;
            for (var bitOffset = 0; bitOffset < 8; bitOffset++)
            {
                var triples = DecodeSignedTriples(segment.Bytes, bitOffset, bitsPerComponent, 128).ToArray();
                if (triples.Length < 4)
                {
                    continue;
                }

                var averageDelta = AverageDelta(triples);
                var score = 1.0 / (1.0 + averageDelta);

                yield return new EulerFixedCandidate(
                    monsterId,
                    slot,
                    recordIndex,
                    segment.Label,
                    segment.Start,
                    segment.End,
                    bitsPerComponent,
                    bitOffset,
                    sampleBits,
                    triples.Length,
                    averageDelta,
                    score);
            }
        }
    }

    private static IEnumerable<DecodedQuaternion> DecodeSmallestThree(byte[] bytes, int bitOffset, int bitsPerComponent, int maxSamples)
    {
        var reader = new LsbBitReader(bytes, bitOffset);
        var maxAbs = (1 << (bitsPerComponent - 1)) - 1;

        for (var sample = 0; sample < maxSamples; sample++)
        {
            if (reader.RemainingBits < 2 + bitsPerComponent * 3)
            {
                yield break;
            }

            var missingIndex = (int)reader.ReadUnsigned(2);
            var values = new double[4];
            var sumSq = 0.0;
            var cursor = 0;
            for (var component = 0; component < 4; component++)
            {
                if (component == missingIndex)
                {
                    continue;
                }

                var signed = reader.ReadSigned(bitsPerComponent);
                var normalized = signed / (double)maxAbs;
                values[component] = normalized;
                sumSq += normalized * normalized;
                cursor++;
            }

            var valid = missingIndex is >= 0 and < 4 && sumSq <= 1.0;
            if (valid)
            {
                values[missingIndex] = Math.Sqrt(Math.Max(0, 1.0 - sumSq));
            }

            _ = cursor;
            yield return new DecodedQuaternion(values, valid);
        }
    }

    private static IEnumerable<double[]> DecodeSignedTriples(byte[] bytes, int bitOffset, int bitsPerComponent, int maxSamples)
    {
        var reader = new LsbBitReader(bytes, bitOffset);
        var maxAbs = (1 << (bitsPerComponent - 1)) - 1;

        for (var sample = 0; sample < maxSamples; sample++)
        {
            if (reader.RemainingBits < bitsPerComponent * 3)
            {
                yield break;
            }

            yield return new[]
            {
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
                reader.ReadSigned(bitsPerComponent) / (double)maxAbs,
            };
        }
    }

    private static double AverageAbsDot(IReadOnlyList<double[]> quaternions)
    {
        if (quaternions.Count < 2)
        {
            return 0;
        }

        var total = 0.0;
        var count = 0;
        for (var i = 1; i < quaternions.Count; i++)
        {
            var dot = 0.0;
            for (var component = 0; component < 4; component++)
            {
                dot += quaternions[i - 1][component] * quaternions[i][component];
            }

            total += Math.Abs(dot);
            count++;
        }

        return count == 0 ? 0 : total / count;
    }

    private static double AverageDelta(IReadOnlyList<double[]> triples)
    {
        if (triples.Count < 2)
        {
            return double.PositiveInfinity;
        }

        var total = 0.0;
        var count = 0;
        for (var i = 1; i < triples.Count; i++)
        {
            var dx = triples[i][0] - triples[i - 1][0];
            var dy = triples[i][1] - triples[i - 1][1];
            var dz = triples[i][2] - triples[i - 1][2];
            total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            count++;
        }

        return total / count;
    }
}

public sealed class LsbBitReader
{
    private readonly byte[] _bytes;
    private int _bitPosition;

    public LsbBitReader(byte[] bytes, int bitOffset)
    {
        _bytes = bytes;
        _bitPosition = bitOffset;
    }

    public int RemainingBits => _bytes.Length * 8 - _bitPosition;

    public uint ReadUnsigned(int bitCount)
    {
        uint value = 0;
        for (var i = 0; i < bitCount; i++)
        {
            var byteIndex = _bitPosition >> 3;
            var bitIndex = _bitPosition & 7;
            var bit = (_bytes[byteIndex] >> bitIndex) & 1;
            value |= (uint)(bit << i);
            _bitPosition++;
        }

        return value;
    }

    public int ReadSigned(int bitCount)
    {
        var raw = ReadUnsigned(bitCount);
        var signBit = 1u << (bitCount - 1);
        if ((raw & signBit) == 0)
        {
            return (int)raw;
        }

        var mask = (1u << bitCount) - 1;
        return -((int)((~raw + 1) & mask));
    }
}

public sealed record CodecProbeReport(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    int MonsterCount,
    int RecordCount,
    int Float32RecordsGte10,
    int Float32RecordsGte05,
    int Int16CleanGridRecords,
    int SmallestThreeTopScoreGte75,
    int EulerFixedTopScoreGte75,
    string DecisionBand,
    string Float32Verdict,
    string Int16Verdict,
    string SmallestThreeVerdict,
    string EulerFixedVerdict,
    MonsterCodecProbe[] Monsters);

public sealed record MonsterCodecProbe(
    string MonsterId,
    string MotionRoot,
    int NonEmptyResidentCount,
    int RecordCount,
    RecordCodecProbe[] Records,
    SmallestThreeCandidate[] TopSmallestThreeCandidates,
    EulerFixedCandidate[] TopEulerFixedCandidates);

public sealed record RecordCodecProbe(
    string MonsterId,
    int Slot,
    string Path,
    int RecordIndex,
    int ChannelCount,
    int GroupCount,
    uint OffA,
    uint OffB,
    uint? FirstOffX,
    int BulkStart,
    int BulkEnd,
    int BulkLength,
    ByteStats BulkStats,
    ByteStats ChannelRegionStats,
    PayloadHeaderSummary PayloadHeader,
    FloatProbeSummary Float32,
    Int16ProbeSummary Int16,
    PeriodicitySummary Periodicity,
    SegmentSummary[] Segments,
    SmallestThreeCandidate[] SmallestThreeCandidates,
    EulerFixedCandidate[] EulerFixedCandidates);

public sealed record ByteSegment(string Label, int Start, int End, byte[] Bytes)
{
    public int Length => End - Start;
}

public sealed record SegmentSummary(
    string Label,
    int Start,
    int End,
    int Length,
    ByteStats Stats)
{
    public static SegmentSummary FromSegment(ByteSegment segment) =>
        new(segment.Label, segment.Start, segment.End, segment.Length, ByteStats.FromBytes(segment.Bytes));
}

public sealed record PayloadHeaderSummary(
    bool LooksLikeKnownHeader,
    ushort[] FirstU16Values,
    uint? DwordAtPlus4,
    int[] Sentinel77777777AbsoluteOffsets,
    string HexPrefix);

public sealed record ByteStats(
    int Length,
    double Entropy,
    double ZeroRate,
    double HighBitRate,
    TopByte[] TopBytes,
    string HexPrefix)
{
    public static ByteStats FromBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return new ByteStats(0, 0, 0, 0, Array.Empty<TopByte>(), "");
        }

        var counts = new int[256];
        foreach (var value in bytes)
        {
            counts[value]++;
        }

        var entropy = 0.0;
        foreach (var count in counts.Where(item => item > 0))
        {
            var p = count / (double)bytes.Length;
            entropy -= p * Math.Log2(p);
        }

        var top = counts
            .Select((count, value) => new TopByte($"0x{value:X2}", count, count / (double)bytes.Length))
            .OrderByDescending(item => item.Count)
            .Take(8)
            .ToArray();

        return new ByteStats(
            bytes.Length,
            entropy,
            counts[0] / (double)bytes.Length,
            bytes.Count(item => item >= 0x80) / (double)bytes.Length,
            top,
            Convert.ToHexString(bytes.Take(48).ToArray()));
    }
}

public sealed record TopByte(string Byte, int Count, double Rate);

public sealed record FloatProbeSummary(
    double BestPlausibleRate,
    int BestPhase,
    FloatPhaseSummary[] Phases);

public sealed record FloatPhaseSummary(
    int Phase,
    int Count,
    double FiniteRate,
    double PlausibleRate,
    double ZeroRate,
    double? Min,
    double? Max);

public sealed record Int16ProbeSummary(
    bool CleanChannelGridCandidate,
    int BestPhase,
    Int16PhaseSummary[] Phases,
    GridRemainder[] GridRemainders);

public sealed record Int16PhaseSummary(
    int Phase,
    int Count,
    double ZeroRate,
    double AbsUnder1024Rate,
    double AbsUnder8192Rate,
    double MeanAbs,
    short[] FirstValues);

public sealed record GridRemainder(string Name, int Divisor, int? Remainder);

public sealed record PeriodicitySummary(PeriodScore[] TopPeriods);

public sealed record PeriodScore(int Period, double MatchRate);

public sealed record SmallestThreeCandidate(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int SegmentStart,
    int SegmentEnd,
    int BitsPerComponent,
    int BitOffset,
    int SampleBits,
    int SampleCount,
    double ValidRate,
    double AverageAbsDot,
    double Score);

public sealed record EulerFixedCandidate(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int SegmentStart,
    int SegmentEnd,
    int BitsPerComponent,
    int BitOffset,
    int SampleBits,
    int SampleCount,
    double AverageDelta,
    double Score);

public sealed record DecodedQuaternion(double[] Components, bool Valid);
