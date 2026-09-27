using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MotionLinkerLab;

public static class MgrpGroupChannelMapper
{
    public static GroupChannelMapReport Map(string ps2Root, string outputRoot, IReadOnlyList<string> monsterIds)
    {
        var groupRows = new List<GroupMapRow>();
        var channelRows = new List<ChannelMapRow>();
        var recordSummaries = new List<GroupChannelRecordSummary>();

        foreach (var monsterId in monsterIds)
        {
            var monsterNumber = int.Parse(monsterId[1..]);
            var monByte = monsterNumber & 0xFF;
            var motionRoot = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot");

            for (var slot = 0; slot < 4; slot++)
            {
                var path = Path.Combine(motionRoot, $"resident{slot}.mgrp");
                var file = MgrpParser.Parse(path, slot, monByte);
                if (!file.IsNonEmpty || !File.Exists(path))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(path);
                foreach (var record in file.Records)
                {
                    var segments = MgrpCodecPhase2Probe.BuildSegments(bytes, record)
                        .ToDictionary(item => item.Label, StringComparer.OrdinalIgnoreCase);

                    foreach (var group in record.Groups)
                    {
                        groupRows.Add(BuildGroupRow(monsterId, slot, record, group, segments));
                    }

                    foreach (var channel in record.Channels)
                    {
                        channelRows.Add(BuildChannelRow(monsterId, slot, record, channel));
                    }

                    recordSummaries.Add(BuildRecordSummary(monsterId, slot, record, segments));
                }
            }
        }

        var groupCsvPath = Path.Combine(outputRoot, "mgrp-group-map.csv");
        var channelCsvPath = Path.Combine(outputRoot, "mgrp-channel-map.csv");
        WriteGroupCsv(groupCsvPath, groupRows);
        WriteChannelCsv(channelCsvPath, channelRows);

        var channelCountEqualsGroupCountRecords = recordSummaries.Count(item => item.ChannelCountEqualsGroupCount);
        var decisionBand = channelCountEqualsGroupCountRecords == recordSummaries.Count
            ? "structural_simple_count_correlation_needs_semantic_proof"
            : "blocked_no_simple_group_channel_count_correlation";

        return new GroupChannelMapReport(
            DateTimeOffset.UtcNow,
            ps2Root,
            monsterIds.ToArray(),
            monsterIds.Count,
            recordSummaries.Count,
            groupRows.Count,
            channelRows.Count,
            channelCountEqualsGroupCountRecords,
            groupRows.Count(item => item.OrdinalChannelIndex.HasValue),
            decisionBand,
            "Group/channel metadata is mapped, but no direct remap is proved.",
            groupCsvPath,
            channelCsvPath,
            recordSummaries.ToArray(),
            groupRows.ToArray(),
            channelRows.ToArray());
    }

    private static GroupMapRow BuildGroupRow(
        string monsterId,
        int slot,
        MgrpRecordInfo record,
        MgrpGroupInfo group,
        IReadOnlyDictionary<string, ByteSegment> segments)
    {
        var ptrALabel = $"g{group.Index}.ptrA";
        var ptrBLabel = $"g{group.Index}.ptrB";
        segments.TryGetValue(ptrALabel, out var ptrA);
        segments.TryGetValue(ptrBLabel, out var ptrB);
        var ordinalChannel = group.Index < record.Channels.Length ? record.Channels[group.Index] : null;
        var ptrBHeaderU16 = ptrB is null ? Array.Empty<ushort>() : ReadU16Prefix(ptrB.Bytes, 24);
        var ptrBLengthWord = ptrB is null || ptrB.Bytes.Length < 20
            ? null
            : (int?)checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ptrB.Bytes.AsSpan(16, 4)));

        return new GroupMapRow(
            monsterId,
            slot,
            record.Index,
            record.ChannelCount,
            record.GroupCount,
            group.Index,
            group.Count,
            group.PtrA,
            group.PtrB,
            checked((int)(group.PtrB - group.PtrA)),
            ptrA?.Length ?? 0,
            ptrB?.Length ?? 0,
            ptrBHeaderU16.Length >= 8 && ptrBHeaderU16[4] == 24 && ptrBHeaderU16[5] == 0 && ptrBHeaderU16[7] == 0,
            ptrBLengthWord,
            ptrBLengthWord.HasValue ? (ptrB?.Length ?? 0) - ptrBLengthWord.Value : null,
            ordinalChannel?.Index,
            ordinalChannel?.CarryFlag,
            ordinalChannel?.OffX,
            ordinalChannel?.OffY,
            ordinalChannel is null ? null : checked((int)(ordinalChannel.OffY - ordinalChannel.OffX)));
    }

    private static ChannelMapRow BuildChannelRow(string monsterId, int slot, MgrpRecordInfo record, MgrpChannelInfo channel) =>
        new(
            monsterId,
            slot,
            record.Index,
            record.ChannelCount,
            record.GroupCount,
            channel.Index,
            channel.Marker,
            channel.MarkerMonByte,
            channel.CarryFlag,
            channel.Tag,
            channel.OffX,
            channel.OffY,
            checked((int)(channel.OffY - channel.OffX)));

    private static GroupChannelRecordSummary BuildRecordSummary(
        string monsterId,
        int slot,
        MgrpRecordInfo record,
        IReadOnlyDictionary<string, ByteSegment> segments)
    {
        var groupValues = record.Groups.Select(item => item.Count).Distinct().Order().ToArray();
        var carryOne = record.Channels.Count(item => item.CarryFlag == 1);
        var offDeltas = record.Channels
            .Select(item => checked((int)(item.OffY - item.OffX)))
            .GroupBy(item => item)
            .OrderBy(item => item.Key)
            .Select(item => new ValueCount(item.Key, item.Count()))
            .ToArray();
        var ptrBHeader24Count = record.Groups.Count(group =>
        {
            if (!segments.TryGetValue($"g{group.Index}.ptrB", out var ptrB))
            {
                return false;
            }

            var u16 = ReadU16Prefix(ptrB.Bytes, 24);
            return u16.Length >= 8 && u16[4] == 24 && u16[5] == 0 && u16[7] == 0;
        });
        var nonZeroLengthWordCount = record.Groups.Count(group =>
        {
            if (!segments.TryGetValue($"g{group.Index}.ptrB", out var ptrB) || ptrB.Bytes.Length < 20)
            {
                return false;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(ptrB.Bytes.AsSpan(16, 4)) != 0;
        });

        return new GroupChannelRecordSummary(
            monsterId,
            slot,
            record.Index,
            record.ChannelCount,
            record.GroupCount,
            record.ChannelCount == record.GroupCount,
            groupValues,
            carryOne,
            offDeltas,
            ptrBHeader24Count,
            nonZeroLengthWordCount);
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

    private static void WriteGroupCsv(string path, IReadOnlyList<GroupMapRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("monsterId,slot,recordIndex,recordChannelCount,recordGroupCount,groupIndex,groupCount,ptrA,ptrB,ptrGap,ptrALength,ptrBLength,ptrBHeader24Shape,ptrBLengthWordAt16,ptrBLengthMinusLengthWord,ordinalChannelIndex,ordinalChannelCarryFlag,ordinalChannelOffX,ordinalChannelOffY,ordinalChannelOffDelta");
        foreach (var row in rows)
        {
            Append(builder, row.MonsterId);
            Append(builder, row.Slot);
            Append(builder, row.RecordIndex);
            Append(builder, row.RecordChannelCount);
            Append(builder, row.RecordGroupCount);
            Append(builder, row.GroupIndex);
            Append(builder, row.GroupCount);
            Append(builder, row.PtrA);
            Append(builder, row.PtrB);
            Append(builder, row.PtrGap);
            Append(builder, row.PtrALength);
            Append(builder, row.PtrBLength);
            Append(builder, row.PtrBHeader24Shape);
            Append(builder, row.PtrBLengthWordAt16);
            Append(builder, row.PtrBLengthMinusLengthWord);
            Append(builder, row.OrdinalChannelIndex);
            Append(builder, row.OrdinalChannelCarryFlag);
            Append(builder, row.OrdinalChannelOffX);
            Append(builder, row.OrdinalChannelOffY);
            AppendLast(builder, row.OrdinalChannelOffDelta);
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteChannelCsv(string path, IReadOnlyList<ChannelMapRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("monsterId,slot,recordIndex,recordChannelCount,recordGroupCount,channelIndex,marker,markerMonByte,carryFlag,tag,offX,offY,offDelta");
        foreach (var row in rows)
        {
            Append(builder, row.MonsterId);
            Append(builder, row.Slot);
            Append(builder, row.RecordIndex);
            Append(builder, row.RecordChannelCount);
            Append(builder, row.RecordGroupCount);
            Append(builder, row.ChannelIndex);
            Append(builder, row.Marker);
            Append(builder, row.MarkerMonByte);
            Append(builder, row.CarryFlag);
            Append(builder, row.Tag);
            Append(builder, row.OffX);
            Append(builder, row.OffY);
            AppendLast(builder, row.OffDelta);
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
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}

public sealed record GroupChannelMapReport(
    DateTimeOffset GeneratedAtUtc,
    string Ps2Root,
    string[] MonsterIds,
    int MonsterCount,
    int RecordCount,
    int GroupRowCount,
    int ChannelRowCount,
    int ChannelCountEqualsGroupCountRecords,
    int OrdinalGroupChannelRows,
    string DecisionBand,
    string Verdict,
    string GroupCsvPath,
    string ChannelCsvPath,
    GroupChannelRecordSummary[] Records,
    GroupMapRow[] Groups,
    ChannelMapRow[] Channels);

public sealed record GroupChannelRecordSummary(
    string MonsterId,
    int Slot,
    int RecordIndex,
    ushort ChannelCount,
    ushort GroupCount,
    bool ChannelCountEqualsGroupCount,
    uint[] GroupCountValues,
    int ChannelCarryFlagOneCount,
    ValueCount[] ChannelOffDeltaDistribution,
    int GroupsWithPtrBHeader24Shape,
    int GroupsWithNonZeroLengthWord);

public sealed record GroupMapRow(
    string MonsterId,
    int Slot,
    int RecordIndex,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    int GroupIndex,
    uint GroupCount,
    uint PtrA,
    uint PtrB,
    int PtrGap,
    int PtrALength,
    int PtrBLength,
    bool PtrBHeader24Shape,
    int? PtrBLengthWordAt16,
    int? PtrBLengthMinusLengthWord,
    int? OrdinalChannelIndex,
    ushort? OrdinalChannelCarryFlag,
    uint? OrdinalChannelOffX,
    uint? OrdinalChannelOffY,
    int? OrdinalChannelOffDelta);

public sealed record ChannelMapRow(
    string MonsterId,
    int Slot,
    int RecordIndex,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    int ChannelIndex,
    uint Marker,
    int? MarkerMonByte,
    ushort CarryFlag,
    ushort Tag,
    uint OffX,
    uint OffY,
    int OffDelta);

public sealed record ValueCount(int Value, int Count);
