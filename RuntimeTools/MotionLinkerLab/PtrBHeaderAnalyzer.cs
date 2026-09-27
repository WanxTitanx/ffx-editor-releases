using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MotionLinkerLab;

public static class MgrpPtrBHeaderAnalyzer
{
    public static PtrBHeaderReport Analyze(
        string ps2Root,
        string outputRoot,
        string monsterId,
        int slot,
        int recordIndex,
        IReadOnlyList<int> focusGroups,
        int headerByteCount = 24)
    {
        if (headerByteCount < 24 || headerByteCount % 4 != 0)
        {
            throw new ArgumentException("headerByteCount must be a multiple of 4 and at least 24 bytes.", nameof(headerByteCount));
        }

        var monsterNumber = int.Parse(monsterId[1..]);
        var monByte = monsterNumber & 0xFF;
        var path = Path.Combine(ps2Root, "chr", "mon", monsterId, "mot", $"resident{slot}.mgrp");
        var file = MgrpParser.Parse(path, slot, monByte);
        if (!file.IsNonEmpty || !File.Exists(path))
        {
            throw new InvalidOperationException($"Resident file is missing or empty: {path}");
        }

        var record = file.Records.FirstOrDefault(item => item.Index == recordIndex)
            ?? throw new InvalidOperationException($"Record {recordIndex} not found in {path}.");
        var bytes = File.ReadAllBytes(path);
        var segments = MgrpCodecPhase2Probe.BuildSegments(bytes, record)
            .ToDictionary(item => item.Label, StringComparer.OrdinalIgnoreCase);
        var focus = focusGroups.Count == 0
            ? new HashSet<int>(record.Groups.Select(item => item.Index))
            : new HashSet<int>(focusGroups);

        var rows = record.Groups
            .Select(group => AnalyzeGroup(group, segments, focus.Contains(group.Index), headerByteCount))
            .ToArray();
        var csvPath = Path.Combine(outputRoot, $"{monsterId}_slot{slot}_record{recordIndex}_ptrb-header.csv");
        WriteCsv(csvPath, rows);

        var summaries = BuildFieldSummaries(rows);
        var header24Count = rows.Count(item => item.Has24ByteHeaderShape);
        var ptrAMatchCount = rows.Count(item => item.PtrASecondWordMatchesPtrBFirstWord);
        var nonZeroLengthWords = rows.Count(item => item.LengthWordAt16 is > 0);

        return new PtrBHeaderReport(
            DateTimeOffset.UtcNow,
            monsterId,
            slot,
            recordIndex,
            path,
            record.ChannelCount,
            record.GroupCount,
            record.OffA,
            record.OffB,
            record.FirstOffX,
            focus.Order().ToArray(),
            headerByteCount,
            rows.Length,
            header24Count,
            ptrAMatchCount,
            nonZeroLengthWords,
            "partial_structural_ptrb_header24_candidate_no_decoder_promotion",
            "Header 24B is a strong local ptrB boundary in this record, but field semantics remain partial.",
            csvPath,
            summaries,
            rows);
    }

    private static PtrBHeaderRow AnalyzeGroup(MgrpGroupInfo group, IReadOnlyDictionary<string, ByteSegment> segments, bool isFocus, int headerByteCount)
    {
        var ptrALabel = $"g{group.Index}.ptrA";
        var ptrBLabel = $"g{group.Index}.ptrB";
        segments.TryGetValue(ptrALabel, out var ptrA);
        segments.TryGetValue(ptrBLabel, out var ptrB);

        var ptrAU16 = ptrA is null ? Array.Empty<ushort>() : ReadU16Prefix(ptrA.Bytes, 8);
        var ptrBU16 = ptrB is null ? Array.Empty<ushort>() : ReadU16Prefix(ptrB.Bytes, headerByteCount);
        var ptrBU32 = ptrB is null ? Array.Empty<uint>() : ReadU32Prefix(ptrB.Bytes, headerByteCount);
        int? lengthWord = ptrBU32.Length > 4 ? checked((int)ptrBU32[4]) : null;
        var ptrBSegmentLength = ptrB?.Length ?? 0;
        int? segmentLengthMinusLengthWord = lengthWord.HasValue ? ptrBSegmentLength - lengthWord.Value : null;

        return new PtrBHeaderRow(
            group.Index,
            isFocus,
            group.Count,
            group.PtrA,
            group.PtrB,
            checked((int)(group.PtrB - group.PtrA)),
            ptrA?.Length ?? 0,
            ptrAU16,
            ptrA is not null && ptrA.Bytes.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(ptrA.Bytes.AsSpan(4, 4)) == 0x77777777,
            ptrB?.Length ?? 0,
            ptrBU16,
            ptrBU32,
            ptrBU16.Length >= 8 && ptrBU16[4] == 24 && ptrBU16[5] == 0 && ptrBU16[7] == 0,
            ptrB is null ? null : ptrB.Start + 24,
            ptrB is null ? "" : Convert.ToHexString(ptrB.Bytes.Skip(24).Take(24).ToArray()),
            ptrAU16.Length > 1 && ptrBU16.Length > 0 && ptrAU16[1] == ptrBU16[0],
            lengthWord,
            segmentLengthMinusLengthWord);
    }

    private static PtrBFieldSummary[] BuildFieldSummaries(IReadOnlyList<PtrBHeaderRow> rows)
    {
        var summaries = new List<PtrBFieldSummary>();
        var fieldCount = rows.Count == 0 ? 0 : rows.Max(item => item.PtrBHeaderU16.Length);
        for (var index = 0; index < fieldCount; index++)
        {
            var values = rows
                .Where(item => item.PtrBHeaderU16.Length > index)
                .Select(item => item.PtrBHeaderU16[index])
                .GroupBy(item => item)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key)
                .Select(item => new PtrBFieldValue(item.Key, item.Count()))
                .ToArray();
            summaries.Add(new PtrBFieldSummary($"u16_{index}", values.Length == 1, values));
        }

        return summaries.ToArray();
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

    private static void WriteCsv(string path, IReadOnlyList<PtrBHeaderRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        var u16FieldCount = rows.Count == 0 ? 12 : Math.Max(12, rows.Max(item => item.PtrBHeaderU16.Length));
        var u32FieldCount = rows.Count == 0 ? 6 : Math.Max(6, rows.Max(item => item.PtrBHeaderU32.Length));
        builder.Append("groupIndex,isFocus,groupCount,ptrA,ptrB,ptrGap,ptrALength,ptrAHas77777777AtPlus4,ptrASecondWordMatchesPtrBFirstWord,ptrBLength");
        for (var index = 0; index < u16FieldCount; index++)
        {
            builder.Append($",u16_{index}");
        }

        for (var index = 0; index < u32FieldCount; index++)
        {
            builder.Append($",u32_{index}");
        }

        builder.AppendLine(",has24ByteHeaderShape,dataStartCandidate,firstDataHexAfter24,lengthWordAt16,segmentLengthMinusLengthWord");
        foreach (var row in rows)
        {
            Append(builder, row.GroupIndex);
            Append(builder, row.IsFocusGroup);
            Append(builder, row.GroupCount);
            Append(builder, row.PtrA);
            Append(builder, row.PtrB);
            Append(builder, row.PtrGap);
            Append(builder, row.PtrASegmentLength);
            Append(builder, row.PtrAHas77777777AtPlus4);
            Append(builder, row.PtrASecondWordMatchesPtrBFirstWord);
            Append(builder, row.PtrBSegmentLength);
            for (var index = 0; index < u16FieldCount; index++)
            {
                Append(builder, row.PtrBHeaderU16.ElementAtOrDefault(index));
            }

            for (var index = 0; index < u32FieldCount; index++)
            {
                Append(builder, row.PtrBHeaderU32.ElementAtOrDefault(index));
            }

            Append(builder, row.Has24ByteHeaderShape);
            Append(builder, row.PtrBDataStartCandidate);
            Append(builder, row.FirstDataHexAfter24);
            Append(builder, row.LengthWordAt16);
            AppendLast(builder, row.SegmentLengthMinusLengthWord);
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

public sealed record PtrBHeaderReport(
    DateTimeOffset GeneratedAtUtc,
    string MonsterId,
    int Slot,
    int RecordIndex,
    string MgrpPath,
    ushort RecordChannelCount,
    ushort RecordGroupCount,
    uint OffA,
    uint OffB,
    uint? FirstOffX,
    int[] FocusGroups,
    int HeaderByteCount,
    int RowCount,
    int Header24ShapeCount,
    int PtrASecondWordMatchesPtrBFirstWordCount,
    int NonZeroLengthWordCount,
    string DecisionBand,
    string Verdict,
    string CsvPath,
    PtrBFieldSummary[] FieldSummaries,
    PtrBHeaderRow[] Rows);

public sealed record PtrBHeaderRow(
    int GroupIndex,
    bool IsFocusGroup,
    uint GroupCount,
    uint PtrA,
    uint PtrB,
    int PtrGap,
    int PtrASegmentLength,
    ushort[] PtrAHeaderU16,
    bool PtrAHas77777777AtPlus4,
    int PtrBSegmentLength,
    ushort[] PtrBHeaderU16,
    uint[] PtrBHeaderU32,
    bool Has24ByteHeaderShape,
    int? PtrBDataStartCandidate,
    string FirstDataHexAfter24,
    bool PtrASecondWordMatchesPtrBFirstWord,
    int? LengthWordAt16,
    int? SegmentLengthMinusLengthWord);

public sealed record PtrBFieldSummary(string Field, bool IsConstantAcrossRecord, PtrBFieldValue[] Values);

public sealed record PtrBFieldValue(ushort Value, int Count);
