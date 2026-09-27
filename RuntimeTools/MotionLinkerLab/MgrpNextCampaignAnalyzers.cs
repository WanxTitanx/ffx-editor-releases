using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MotionLinkerLab;

public static class MgrpTripleDumpGrid
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static TripleDumpGridReport Dump(string ps2Root, string outputRoot, TripleDumpGridRequest request)
    {
        Directory.CreateDirectory(outputRoot);
        var rows = new List<TripleDumpGridRow>();
        var requestCount = 0;

        foreach (var segment in request.SegmentLabels)
        {
            foreach (var skip in request.Skips)
            {
                foreach (var bits in request.BitsPerComponent)
                {
                    foreach (var bitOffset in request.BitOffsets)
                    {
                        requestCount++;
                        var single = new TripleDumpRequest(
                            request.MonsterId,
                            request.Slot,
                            request.RecordIndex,
                            segment,
                            skip,
                            bits,
                            bitOffset,
                            request.MaxSamples);
                        try
                        {
                            var dump = MgrpTripleDumper.Dump(ps2Root, outputRoot, single);
                            var outputStem = Path.GetFileNameWithoutExtension(dump.CsvPath).Replace(".triples", "", StringComparison.OrdinalIgnoreCase);
                            var jsonPath = Path.Combine(outputRoot, $"{outputStem}.triple-dump.json");
                            File.WriteAllText(jsonPath, JsonSerializer.Serialize(dump, JsonOptions));
                            rows.Add(new TripleDumpGridRow(
                                request.MonsterId,
                                request.Slot,
                                request.RecordIndex,
                                segment,
                                skip,
                                bits,
                                bitOffset,
                                true,
                                null,
                                dump.SampleCount,
                                dump.AbsoluteDataStart,
                                dump.Stats.AverageDelta,
                                dump.Stats.MeanVectorMagnitude,
                                dump.Stats.MeanAxisRange,
                                dump.Stats.SaturatedComponentRate,
                                outputStem,
                                dump.CsvPath));
                        }
                        catch (Exception exception)
                        {
                            rows.Add(new TripleDumpGridRow(
                                request.MonsterId,
                                request.Slot,
                                request.RecordIndex,
                                segment,
                                skip,
                                bits,
                                bitOffset,
                                false,
                                exception.Message,
                                0,
                                null,
                                null,
                                null,
                                null,
                                null,
                                null,
                                null));
                        }
                    }
                }
            }
        }

        var csvPath = Path.Combine(outputRoot, $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_dump-grid.csv");
        WriteGridCsv(csvPath, rows);
        var successCount = rows.Count(item => item.Success);
        var decision = successCount > 0
            ? "proved_grid_dumps_generated_lab_only"
            : "blocked_no_grid_dumps_generated";

        return new TripleDumpGridReport(
            DateTimeOffset.UtcNow,
            outputRoot,
            request,
            requestCount,
            successCount,
            requestCount - successCount,
            decision,
            csvPath,
            rows.ToArray());
    }

    private static void WriteGridCsv(string path, IReadOnlyList<TripleDumpGridRow> rows)
    {
        var builder = BeginCsv(path, "monsterId,slot,recordIndex,segment,skip,bits,bitOffset,success,error,sampleCount,absoluteDataStart,averageDelta,meanVectorMagnitude,meanAxisRange,saturatedComponentRate,outputStem,csvPath");
        foreach (var row in rows)
        {
            AppendRow(builder, row.MonsterId, row.Slot, row.RecordIndex, row.SegmentLabel, row.Skip, row.Bits, row.BitOffset, row.Success, row.Error, row.SampleCount, row.AbsoluteDataStart, row.AverageDelta, row.MeanVectorMagnitude, row.MeanAxisRange, row.SaturatedComponentRate, row.OutputStem, row.CsvPath);
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

    internal static void AppendRow(StringBuilder builder, params object?[] values)
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

    internal static string FormatCsv(object? value)
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

public static class MgrpRemapAttacker
{
    private static readonly string[] Policies =
    [
        "ordinal",
        "marker_low_byte",
        "marker_high_byte",
        "marker_delta_from_first",
        "sparse_node_order",
        "active_channel_only",
        "group_active_only",
    ];

    public static RemapAttackReport Analyze(string ps2Root, string outputRoot, IReadOnlyList<string> monsterIds)
    {
        var candidates = new List<RemapCandidateRow>();
        var recordCount = 0;

        foreach (var monsterId in monsterIds)
        {
            var monByte = int.Parse(monsterId[1..]) & 0xFF;
            var chrPath = Path.Combine(ps2Root, "chr", "mon", monsterId, "mdl", $"{monsterId}.chr");
            var bind = ChrBindPoseReader.Parse(chrPath);
            var sparseNodes = bind.Nodes
                .Where(item => item.Flag == 0x40 || item.NodeIndex != 0)
                .OrderBy(item => item.TableIndex)
                .ToArray();
            if (sparseNodes.Length == 0)
            {
                sparseNodes = bind.Nodes.OrderBy(item => item.TableIndex).ToArray();
            }

            for (var slot = 0; slot < 4; slot++)
            {
                var path = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot", $"resident{slot}.mgrp");
                var file = MgrpParser.Parse(path, slot, monByte);
                if (!file.IsNonEmpty || !File.Exists(path))
                {
                    continue;
                }

                foreach (var record in file.Records)
                {
                    recordCount++;
                    foreach (var policy in Policies)
                    {
                        foreach (var row in BuildPolicyRows(monsterId, slot, record, policy, bind, sparseNodes))
                        {
                            candidates.Add(row);
                        }
                    }
                }
            }
        }

        var policySummaries = candidates
            .GroupBy(item => item.PolicyName, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPolicySummary(group.Key, group.ToArray()))
            .OrderByDescending(item => item.InRangeRate)
            .ThenByDescending(item => item.UniqueRate)
            .ToArray();
        var best = policySummaries.FirstOrDefault();
        var decision = best is not null && best.CoherentMonsterCount >= 2 && best.InRangeRate >= 0.95 && best.UniqueRate >= 0.70
            ? "partial_candidate_remap_repeated_but_anatomy_unproved"
            : "blocked_no_proved_jointidx_to_boneid_remap";

        var candidateCsv = Path.Combine(outputRoot, "mgrp-remap-candidates.csv");
        var policyCsv = Path.Combine(outputRoot, "mgrp-remap-policy-summary.csv");
        WriteCandidateCsv(candidateCsv, candidates);
        WritePolicyCsv(policyCsv, policySummaries);

        return new RemapAttackReport(
            DateTimeOffset.UtcNow,
            ps2Root,
            monsterIds.ToArray(),
            monsterIds.Count,
            recordCount,
            candidates.Count,
            decision,
            candidateCsv,
            policyCsv,
            policySummaries,
            candidates.ToArray());
    }

    private static IEnumerable<RemapCandidateRow> BuildPolicyRows(
        string monsterId,
        int slot,
        MgrpRecordInfo record,
        string policy,
        ChrBindPoseSummary bind,
        IReadOnlyList<ChrBindNode> sparseNodes)
    {
        var firstMarker = record.Channels.FirstOrDefault()?.Marker ?? 0;
        var activeChannelIndex = 0;
        foreach (var channel in record.Channels)
        {
            int? node = policy switch
            {
                "ordinal" => channel.Index,
                "marker_low_byte" => (int)(channel.Marker & 0xFF),
                "marker_high_byte" => (int)((channel.Marker >> 16) & 0xFF),
                "marker_delta_from_first" => checked((int)((channel.Marker - firstMarker) & 0xFFFF)),
                "sparse_node_order" => channel.Index < sparseNodes.Count ? sparseNodes[channel.Index].TableIndex : null,
                "active_channel_only" => channel.CarryFlag != 0 ? activeChannelIndex++ : null,
                "group_active_only" => channel.Index < record.GroupCount ? channel.Index : null,
                _ => null,
            };
            var inRange = node.HasValue && node.Value >= 0 && node.Value < bind.ValidNodeCount;
            yield return new RemapCandidateRow(
                monsterId,
                slot,
                record.Index,
                policy,
                channel.Index,
                channel.Marker,
                channel.MarkerMonByte,
                channel.CarryFlag,
                record.ChannelCount,
                record.GroupCount,
                bind.NodeCount,
                bind.ValidNodeCount,
                node,
                inRange,
                inRange ? "unsafe_candidate_in_range" : "blocked_out_of_range_or_missing");
        }
    }

    private static RemapPolicySummary BuildPolicySummary(string policy, IReadOnlyList<RemapCandidateRow> rows)
    {
        var candidateCount = rows.Count;
        var inRangeCount = rows.Count(item => item.InRange);
        var monsterGroups = rows.GroupBy(item => item.MonsterId, StringComparer.OrdinalIgnoreCase).ToArray();
        var coherentMonsters = monsterGroups.Count(group =>
        {
            var inRangeRate = group.Count(item => item.InRange) / (double)Math.Max(1, group.Count());
            var uniqueRate = group.Where(item => item.CandidateNodeIndex.HasValue).Select(item => item.CandidateNodeIndex!.Value).Distinct().Count()
                / (double)Math.Max(1, group.Count(item => item.CandidateNodeIndex.HasValue));
            return inRangeRate >= 0.95 && uniqueRate >= 0.70;
        });
        var uniqueRateAll = rows.Where(item => item.CandidateNodeIndex.HasValue).Select(item => $"{item.MonsterId}:{item.Slot}:{item.RecordIndex}:{item.CandidateNodeIndex}").Distinct().Count()
            / (double)Math.Max(1, rows.Count(item => item.CandidateNodeIndex.HasValue));
        var inRangeRateAll = inRangeCount / (double)Math.Max(1, candidateCount);
        var decision = coherentMonsters >= 2 && inRangeRateAll >= 0.95 && uniqueRateAll >= 0.70
            ? "partial_remap_policy_repeated_no_anatomical_proof"
            : "blocked_policy_not_enough_for_promotion";

        return new RemapPolicySummary(policy, candidateCount, inRangeCount, inRangeRateAll, uniqueRateAll, coherentMonsters, decision);
    }

    private static void WriteCandidateCsv(string path, IReadOnlyList<RemapCandidateRow> rows)
    {
        var builder = BeginCsv(path, "monsterId,slot,recordIndex,policyName,channelIndex,marker,markerMonByte,carryFlag,recordChannelCount,recordGroupCount,chrNodeCount,validNodeCount,candidateNodeIndex,inRange,decisionBand");
        foreach (var row in rows)
        {
            MgrpTripleDumpGrid.AppendRow(builder, row.MonsterId, row.Slot, row.RecordIndex, row.PolicyName, row.ChannelIndex, row.Marker, row.MarkerMonByte, row.CarryFlag, row.RecordChannelCount, row.RecordGroupCount, row.ChrNodeCount, row.ValidNodeCount, row.CandidateNodeIndex, row.InRange, row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WritePolicyCsv(string path, IReadOnlyList<RemapPolicySummary> rows)
    {
        var builder = BeginCsv(path, "policyName,candidateCount,inRangeCount,inRangeRate,uniqueRate,coherentMonsterCount,decisionBand");
        foreach (var row in rows)
        {
            MgrpTripleDumpGrid.AppendRow(builder, row.PolicyName, row.CandidateCount, row.InRangeCount, row.InRangeRate, row.UniqueRate, row.CoherentMonsterCount, row.DecisionBand);
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
}

public static class MgrpTimingLawAnalyzer
{
    public static TimingLawReport Analyze(string ps2Root, string outputRoot, IReadOnlyList<string> monsterIds)
    {
        var rows = new List<TimingLawRow>();
        foreach (var monsterId in monsterIds)
        {
            var monByte = int.Parse(monsterId[1..]) & 0xFF;
            for (var slot = 0; slot < 4; slot++)
            {
                var path = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot", $"resident{slot}.mgrp");
                var file = MgrpParser.Parse(path, slot, monByte);
                if (!file.IsNonEmpty || !File.Exists(path))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(path);
                foreach (var record in file.Records)
                {
                    var segments = MgrpCodecPhase2Probe.BuildSegments(bytes, record)
                        .Where(item => item.Label.Contains(".ptrB", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    foreach (var segment in segments)
                    {
                        rows.Add(BuildRow(monsterId, slot, record, segment));
                    }
                }
            }
        }

        var patterns = BuildPatterns(rows);
        var u16_0FrameMonsters = rows
            .Where(item => item.U16_0.HasValue && item.U16_0 == item.PossibleTriplesSkip24B12)
            .Select(item => item.MonsterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var u16_8LengthWordMonsters = rows
            .Where(item => item.U32_4.HasValue && item.LengthResidualFromU32_4 is >= 0 and <= 64)
            .Select(item => item.MonsterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var decision = u16_0FrameMonsters >= 3 || u16_8LengthWordMonsters >= 3
            ? "partial_count_law_repeats_in_three_or_more_monsters"
            : "blocked_no_timing_or_duration_law_proved";

        var csvPath = Path.Combine(outputRoot, "mgrp-timing-count-law.csv");
        WriteCsv(csvPath, rows);

        return new TimingLawReport(
            DateTimeOffset.UtcNow,
            ps2Root,
            monsterIds.ToArray(),
            monsterIds.Count,
            rows.Count,
            u16_0FrameMonsters,
            u16_8LengthWordMonsters,
            decision,
            csvPath,
            patterns,
            rows.ToArray());
    }

    private static TimingLawRow BuildRow(string monsterId, int slot, MgrpRecordInfo record, ByteSegment segment)
    {
        var u16 = ReadU16Prefix(segment.Bytes, 96);
        var u32 = ReadU32Prefix(segment.Bytes, 96);
        var possibleTriplesSkip24 = PossibleTriples(segment.Length, 24, 12, 0);
        var possibleTriplesSkip28 = PossibleTriples(segment.Length, 28, 12, 0);
        var u32_4 = u32.Length > 4 ? checked((int)u32[4]) : null as int?;
        int? lengthResidual = u32_4.HasValue ? segment.Length - u32_4.Value : null;
        var decision = lengthResidual is >= 0 and <= 64
            ? "partial_length_word_candidate"
            : u16.Length > 0
                ? "structural_header_fields_no_duration"
                : "blocked_no_header_fields";

        return new TimingLawRow(
            monsterId,
            slot,
            record.Index,
            segment.Label,
            segment.Length,
            record.ChannelCount,
            record.GroupCount,
            u16.ElementAtOrNull(0),
            u16.ElementAtOrNull(1),
            u16.ElementAtOrNull(3),
            u16.ElementAtOrNull(4),
            u16.ElementAtOrNull(6),
            u16.ElementAtOrNull(8),
            u32_4,
            lengthResidual,
            possibleTriplesSkip24,
            possibleTriplesSkip28,
            possibleTriplesSkip24 / Math.Max(1, (int)record.ChannelCount),
            possibleTriplesSkip24 / Math.Max(1, (int)record.GroupCount),
            decision);
    }

    private static TimingPattern[] BuildPatterns(IReadOnlyList<TimingLawRow> rows)
    {
        return rows
            .GroupBy(item => new
            {
                item.U16_1,
                item.U16_4,
                item.U16_6,
                ResidualBand = item.LengthResidualFromU32_4 switch
                {
                    null => "none",
                    <= 8 => "0_8",
                    <= 24 => "9_24",
                    <= 64 => "25_64",
                    _ => "gt64",
                },
            })
            .Select(group => new TimingPattern(
                $"u16_1={group.Key.U16_1};u16_4={group.Key.U16_4};u16_6={group.Key.U16_6};residual={group.Key.ResidualBand}",
                group.Count(),
                group.Select(item => item.MonsterId).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .OrderByDescending(item => item.RowCount)
            .ThenBy(item => item.Pattern)
            .ToArray();
    }

    private static int PossibleTriples(int segmentLength, int skip, int bits, int bitOffset)
    {
        var dataBytes = Math.Max(0, segmentLength - skip);
        var usableBits = Math.Max(0, dataBytes * 8 - bitOffset);
        return usableBits / (bits * 3);
    }

    private static ushort[] ReadU16Prefix(byte[] bytes, int maxBytes)
    {
        var values = new List<ushort>();
        for (var offset = 0; offset + 2 <= Math.Min(bytes.Length, maxBytes); offset += 2)
        {
            values.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));
        }

        return values.ToArray();
    }

    private static uint[] ReadU32Prefix(byte[] bytes, int maxBytes)
    {
        var values = new List<uint>();
        for (var offset = 0; offset + 4 <= Math.Min(bytes.Length, maxBytes); offset += 4)
        {
            values.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
        }

        return values.ToArray();
    }

    private static void WriteCsv(string path, IReadOnlyList<TimingLawRow> rows)
    {
        var builder = BeginCsv(path, "monsterId,slot,recordIndex,segment,segmentLength,recordChannelCount,recordGroupCount,u16_0,u16_1,u16_3,u16_4,u16_6,u16_8,u32_4,lengthResidualFromU32_4,possibleTriplesSkip24B12,possibleTriplesSkip28B12,triplesPerChannelSkip24,triplesPerGroupSkip24,decisionBand");
        foreach (var row in rows)
        {
            MgrpTripleDumpGrid.AppendRow(builder, row.MonsterId, row.Slot, row.RecordIndex, row.SegmentLabel, row.SegmentLength, row.RecordChannelCount, row.RecordGroupCount, row.U16_0, row.U16_1, row.U16_3, row.U16_4, row.U16_6, row.U16_8, row.U32_4, row.LengthResidualFromU32_4, row.PossibleTriplesSkip24B12, row.PossibleTriplesSkip28B12, row.TriplesPerChannelSkip24, row.TriplesPerGroupSkip24, row.DecisionBand);
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
}

public static class MgrpDecodeCandidateV0
{
    private static readonly Dictionary<string, double> Scales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["identity"] = 1.0,
        ["i32"] = 1.0 / 32.0,
        ["i64"] = 1.0 / 64.0,
        ["i128"] = 1.0 / 128.0,
        ["i256"] = 1.0 / 256.0,
        ["half_pi"] = Math.PI / 2.0,
        ["pi"] = Math.PI,
        ["two_pi"] = Math.PI * 2.0,
    };

    public static DecodeCandidateReport Decode(string ps2Root, string outputRoot, DecodeCandidateRequest request)
    {
        var tripleDump = MgrpTripleDumper.Dump(ps2Root, outputRoot, new TripleDumpRequest(
            request.MonsterId,
            request.Slot,
            request.RecordIndex,
            request.SegmentLabel,
            request.LocalByteSkip,
            request.BitsPerComponent,
            request.BitOffset,
            request.MaxSamples));
        var samples = ReadSamples(tripleDump.CsvPath);
        var scaleValue = Scales.TryGetValue(request.ScaleName, out var knownScale)
            ? knownScale
            : double.Parse(request.ScaleName, CultureInfo.InvariantCulture);
        var channelCount = Math.Max(1, request.Stride);
        var frames = samples
            .Select((sample, index) => new DecodedCurveSample(
                index,
                index / channelCount,
                index % channelCount,
                sample.X * scaleValue,
                sample.Y * scaleValue,
                sample.Z * scaleValue))
            .ToArray();

        var stem = $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_{request.SegmentLabel.Replace('+', '_')}_skip{request.LocalByteSkip}_b{request.BitsPerComponent}_bit{request.BitOffset}_stride{request.Stride}_{request.ScaleName}";
        stem = SanitizeStem(stem);
        var rawCsv = Path.Combine(outputRoot, $"{stem}.raw-triples.csv");
        var curvesCsv = Path.Combine(outputRoot, $"{stem}.grouped-curves.csv");
        var metadataCsv = Path.Combine(outputRoot, $"{stem}.metadata.csv");
        WriteRawCsv(rawCsv, samples, scaleValue);
        WriteCurvesCsv(curvesCsv, frames);
        WriteMetadataCsv(metadataCsv, request, tripleDump, scaleValue, channelCount, frames);

        return new DecodeCandidateReport(
            DateTimeOffset.UtcNow,
            "candidate_decoder_v0_structure_only_no_playback_promotion",
            request,
            tripleDump.MgrpPath,
            tripleDump.SegmentStart,
            tripleDump.SegmentEnd,
            tripleDump.SegmentLength,
            tripleDump.AbsoluteDataStart,
            scaleValue,
            samples.Length,
            channelCount,
            frames.Length == 0 ? 0 : frames.Max(item => item.FrameIndex) + 1,
            rawCsv,
            curvesCsv,
            metadataCsv,
            tripleDump.SegmentSummary,
            frames);
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

    private static void WriteRawCsv(string path, IReadOnlyList<TripleSample> samples, double scale)
    {
        var builder = BeginCsv(path, "sampleIndex,x,y,z,scaledX,scaledY,scaledZ");
        foreach (var sample in samples)
        {
            MgrpTripleDumpGrid.AppendRow(builder, sample.Index, sample.X, sample.Y, sample.Z, sample.X * scale, sample.Y * scale, sample.Z * scale);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteCurvesCsv(string path, IReadOnlyList<DecodedCurveSample> samples)
    {
        var builder = BeginCsv(path, "sampleIndex,frameIndex,channelIndex,x,y,z");
        foreach (var sample in samples)
        {
            MgrpTripleDumpGrid.AppendRow(builder, sample.SampleIndex, sample.FrameIndex, sample.ChannelIndex, sample.X, sample.Y, sample.Z);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteMetadataCsv(string path, DecodeCandidateRequest request, TripleDumpReport dump, double scale, int channelCount, IReadOnlyList<DecodedCurveSample> frames)
    {
        var builder = BeginCsv(path, "key,value");
        MgrpTripleDumpGrid.AppendRow(builder, "decisionBand", "candidate_decoder_v0_structure_only_no_playback_promotion");
        MgrpTripleDumpGrid.AppendRow(builder, "monsterId", request.MonsterId);
        MgrpTripleDumpGrid.AppendRow(builder, "segment", request.SegmentLabel);
        MgrpTripleDumpGrid.AppendRow(builder, "skip", request.LocalByteSkip);
        MgrpTripleDumpGrid.AppendRow(builder, "bits", request.BitsPerComponent);
        MgrpTripleDumpGrid.AppendRow(builder, "bitOffset", request.BitOffset);
        MgrpTripleDumpGrid.AppendRow(builder, "stride", request.Stride);
        MgrpTripleDumpGrid.AppendRow(builder, "scaleName", request.ScaleName);
        MgrpTripleDumpGrid.AppendRow(builder, "scaleValue", scale);
        MgrpTripleDumpGrid.AppendRow(builder, "absoluteDataStart", dump.AbsoluteDataStart);
        MgrpTripleDumpGrid.AppendRow(builder, "segmentLength", dump.SegmentLength);
        MgrpTripleDumpGrid.AppendRow(builder, "sampleCount", frames.Count);
        MgrpTripleDumpGrid.AppendRow(builder, "channelCount", channelCount);
        MgrpTripleDumpGrid.AppendRow(builder, "frameCount", frames.Count == 0 ? 0 : frames.Max(item => item.FrameIndex) + 1);
        File.WriteAllText(path, builder.ToString());
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
}

public static class MgrpCrossCorpusRegression
{
    public static CrossCorpusRegressionReport Analyze(string phase2ReportPath, string outputRoot)
    {
        if (!File.Exists(phase2ReportPath))
        {
            throw new FileNotFoundException("Phase2 report not found.", phase2ReportPath);
        }

        var phase2 = JsonSerializer.Deserialize<CodecPhase2Report>(File.ReadAllText(phase2ReportPath))
            ?? throw new InvalidOperationException($"Could not read phase2 report: {phase2ReportPath}");
        var rows = phase2.Monsters
            .Select(monster => BuildRow(monster))
            .ToArray();
        var familyCounts = rows
            .GroupBy(item => item.Family)
            .OrderByDescending(item => item.Count())
            .Select(item => new FamilyCount(item.Key, item.Count()))
            .ToArray();
        var classified = rows.Count(item => item.Family != "blocked/noise");
        var decision = familyCounts.Any(item => item.Family != "blocked/noise" && item.Count >= 3)
            ? "partial_family_repetition_detected_no_decoder_promotion"
            : "blocked_no_repeated_family_sufficient_for_promotion";
        var csvPath = Path.Combine(outputRoot, "mgrp-cross-corpus-regression.csv");
        WriteCsv(csvPath, rows);

        return new CrossCorpusRegressionReport(
            DateTimeOffset.UtcNow,
            phase2ReportPath,
            phase2.MonsterCount,
            classified,
            decision,
            csvPath,
            familyCounts,
            rows);
    }

    private static CrossCorpusMonsterRow BuildRow(MonsterCodecPhase2 monster)
    {
        var best = monster.TopCandidates.OrderByDescending(item => item.Score).FirstOrDefault();
        if (best is null)
        {
            return new CrossCorpusMonsterRow(monster.MonsterId, 0, null, null, null, null, null, null, 0, 0, 0, "blocked/noise", "blocked_no_candidate");
        }

        var ptrB = best.SegmentLabel.Contains(".ptrB", StringComparison.OrdinalIgnoreCase);
        var family =
            ptrB && best.LocalByteSkip == 24 && best.BitsPerComponent == 12 ? "family_24B_12bit" :
            ptrB && best.LocalByteSkip == 4 && best.BitsPerComponent == 12 ? "family_skip4_short" :
            ptrB && best.BitsPerComponent == 12 && best.LocalByteSkip is 28 or 12 or 16 ? "family_stride_candidate_12bit" :
            best.Score >= 0.60 ? $"family_{best.BitsPerComponent}bit_other" :
            "blocked/noise";
        var decision = best.Score >= 0.60
            ? "structural_phase2_candidate_no_semantics"
            : "blocked_low_phase2_score";

        return new CrossCorpusMonsterRow(
            monster.MonsterId,
            monster.RecordCount,
            best.SegmentLabel,
            best.LocalByteSkip,
            best.BitsPerComponent,
            best.BitOffset,
            best.SampleCount,
            best.SaturatedComponentRate,
            best.StructuralScore,
            best.BindFitScore,
            best.Score,
            family,
            decision);
    }

    private static void WriteCsv(string path, IReadOnlyList<CrossCorpusMonsterRow> rows)
    {
        var builder = BeginCsv(path, "monsterId,recordCount,topSegment,topSkip,topBits,topBitOffset,topSampleCount,topSaturatedComponentRate,topStructuralScore,topBindFitScore,topScore,family,decisionBand");
        foreach (var row in rows)
        {
            MgrpTripleDumpGrid.AppendRow(builder, row.MonsterId, row.RecordCount, row.TopSegment, row.TopSkip, row.TopBits, row.TopBitOffset, row.TopSampleCount, row.TopSaturatedComponentRate, row.TopStructuralScore, row.TopBindFitScore, row.TopScore, row.Family, row.DecisionBand);
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
}

public static class CollectionExtensions
{
    public static ushort? ElementAtOrNull(this IReadOnlyList<ushort> values, int index) =>
        index >= 0 && index < values.Count ? values[index] : null;
}

public sealed record TripleDumpGridRequest(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string[] SegmentLabels,
    int[] Skips,
    int[] BitsPerComponent,
    int[] BitOffsets,
    int MaxSamples);

public sealed record TripleDumpGridReport(
    DateTimeOffset GeneratedAtUtc,
    string OutputRoot,
    TripleDumpGridRequest Request,
    int RequestCount,
    int SuccessCount,
    int FailureCount,
    string DecisionBand,
    string GridCsvPath,
    TripleDumpGridRow[] Rows);

public sealed record TripleDumpGridRow(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int Skip,
    int Bits,
    int BitOffset,
    bool Success,
    string? Error,
    int SampleCount,
    int? AbsoluteDataStart,
    double? AverageDelta,
    double? MeanVectorMagnitude,
    double? MeanAxisRange,
    double? SaturatedComponentRate,
    string? OutputStem,
    string? CsvPath);

public sealed record RemapAttackReport(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    string[] MonsterIds,
    int MonsterCount,
    int RecordCount,
    int CandidateRowCount,
    string DecisionBand,
    string CandidateCsvPath,
    string PolicyCsvPath,
    RemapPolicySummary[] PolicySummaries,
    RemapCandidateRow[] Candidates);

public sealed record RemapPolicySummary(
    string PolicyName,
    int CandidateCount,
    int InRangeCount,
    double InRangeRate,
    double UniqueRate,
    int CoherentMonsterCount,
    string DecisionBand);

public sealed record RemapCandidateRow(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string PolicyName,
    int ChannelIndex,
    uint Marker,
    int? MarkerMonByte,
    ushort CarryFlag,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    uint? ChrNodeCount,
    int ValidNodeCount,
    int? CandidateNodeIndex,
    bool InRange,
    string DecisionBand);

public sealed record TimingLawReport(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    string[] MonsterIds,
    int MonsterCount,
    int SegmentCount,
    int U16_0PossibleFrameMonsters,
    int U16_8LengthWordMonsters,
    string DecisionBand,
    string CsvPath,
    TimingPattern[] Patterns,
    TimingLawRow[] Rows);

public sealed record TimingPattern(string Pattern, int RowCount, int MonsterCount);

public sealed record TimingLawRow(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int SegmentLength,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    ushort? U16_0,
    ushort? U16_1,
    ushort? U16_3,
    ushort? U16_4,
    ushort? U16_6,
    ushort? U16_8,
    int? U32_4,
    int? LengthResidualFromU32_4,
    int PossibleTriplesSkip24B12,
    int PossibleTriplesSkip28B12,
    int TriplesPerChannelSkip24,
    int TriplesPerGroupSkip24,
    string DecisionBand);

public sealed record DecodeCandidateRequest(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int LocalByteSkip,
    int BitsPerComponent,
    int BitOffset,
    int Stride,
    string ScaleName,
    int MaxSamples);

public sealed record DecodeCandidateReport(
    DateTimeOffset GeneratedAtUtc,
    string DecisionBand,
    DecodeCandidateRequest Request,
    string MgrpPath,
    int SegmentStart,
    int SegmentEnd,
    int SegmentLength,
    int AbsoluteDataStart,
    double ScaleValue,
    int SampleCount,
    int ChannelCount,
    int FrameCount,
    string RawTriplesCsvPath,
    string GroupedCurvesCsvPath,
    string MetadataCsvPath,
    Phase2SegmentSummary SegmentSummary,
    DecodedCurveSample[] Samples);

public sealed record DecodedCurveSample(
    int SampleIndex,
    int FrameIndex,
    int ChannelIndex,
    double X,
    double Y,
    double Z);

public sealed record CrossCorpusRegressionReport(
    DateTimeOffset GeneratedAtUtc,
    string Phase2ReportPath,
    int MonsterCount,
    int ClassifiedMonsterCount,
    string DecisionBand,
    string CsvPath,
    FamilyCount[] FamilyCounts,
    CrossCorpusMonsterRow[] Rows);

public sealed record FamilyCount(string Family, int Count);

public sealed record CrossCorpusMonsterRow(
    string MonsterId,
    int RecordCount,
    string? TopSegment,
    int? TopSkip,
    int? TopBits,
    int? TopBitOffset,
    int? TopSampleCount,
    double? TopSaturatedComponentRate,
    double TopStructuralScore,
    double? TopBindFitScore,
    double TopScore,
    string Family,
    string DecisionBand);
