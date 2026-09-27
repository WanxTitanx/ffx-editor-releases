using System.Globalization;
using System.Text;

namespace MotionLinkerLab;

public static class MgrpTripleDumper
{
    public static TripleDumpReport Dump(string ps2Root, string outputRoot, TripleDumpRequest request)
    {
        var monsterNumber = int.Parse(request.MonsterId[1..]);
        var monByte = monsterNumber & 0xFF;
        var path = Path.Combine(ps2Root, "chr", "mon", request.MonsterId, "mot", $"resident{request.Slot}.mgrp");
        var file = MgrpParser.Parse(path, request.Slot, monByte);
        if (!file.IsNonEmpty || !File.Exists(path))
        {
            throw new InvalidOperationException($"Resident file is missing or empty: {path}");
        }

        var record = file.Records.FirstOrDefault(item => item.Index == request.RecordIndex)
            ?? throw new InvalidOperationException($"Record {request.RecordIndex} not found in {path}.");
        var bytes = File.ReadAllBytes(path);
        var segment = MgrpCodecPhase2Probe.BuildSegments(bytes, record)
            .FirstOrDefault(item => string.Equals(item.Label, request.SegmentLabel, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Segment '{request.SegmentLabel}' not found in {path}.");

        if (request.LocalByteSkip < 0 || request.LocalByteSkip > segment.Length - 1)
        {
            throw new InvalidOperationException($"Local skip {request.LocalByteSkip} is outside segment '{segment.Label}'.");
        }

        var localBytes = segment.Bytes.Skip(request.LocalByteSkip).ToArray();
        var triples = MgrpCodecPhase2Probe
            .DecodeSignedTriples(localBytes, request.BitOffset, request.BitsPerComponent, request.MaxSamples)
            .ToArray();
        if (triples.Length == 0)
        {
            throw new InvalidOperationException("No triples decoded from the selected candidate.");
        }

        Directory.CreateDirectory(outputRoot);
        var stem = BuildStem(request);
        var csvPath = Path.Combine(outputRoot, $"{stem}.triples.csv");
        WriteCsv(csvPath, triples);

        var stats = TripleStats.FromTriples(triples);
        return new TripleDumpReport(
            DateTimeOffset.UtcNow,
            request,
            path,
            segment.Start,
            segment.End,
            segment.Length,
            segment.Start + request.LocalByteSkip,
            Phase2SegmentSummary.FromSegment(segment),
            triples.Length,
            stats,
            csvPath);
    }

    private static string BuildStem(TripleDumpRequest request)
    {
        var raw = $"{request.MonsterId}_slot{request.Slot}_record{request.RecordIndex}_{request.SegmentLabel}_skip{request.LocalByteSkip}_b{request.BitsPerComponent}_bit{request.BitOffset}";
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        }

        return builder.ToString();
    }

    private static void WriteCsv(string path, IReadOnlyList<double[]> triples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sampleIndex,x,y,z,deltaFromPrevious,magnitude");
        double[]? previous = null;
        for (var index = 0; index < triples.Count; index++)
        {
            var triple = triples[index];
            var delta = previous is null
                ? 0
                : Math.Sqrt(
                    Math.Pow(triple[0] - previous[0], 2)
                    + Math.Pow(triple[1] - previous[1], 2)
                    + Math.Pow(triple[2] - previous[2], 2));
            var magnitude = Math.Sqrt(triple[0] * triple[0] + triple[1] * triple[1] + triple[2] * triple[2]);

            builder.Append(index.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(triple[0].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(triple[1].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(triple[2].ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(delta.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(magnitude.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine();
            previous = triple;
        }

        File.WriteAllText(path, builder.ToString());
    }
}

public sealed record TripleDumpRequest(
    string MonsterId,
    int Slot,
    int RecordIndex,
    string SegmentLabel,
    int LocalByteSkip,
    int BitsPerComponent,
    int BitOffset,
    int MaxSamples);

public sealed record TripleDumpReport(
    DateTimeOffset GeneratedAtUtc,
    TripleDumpRequest Request,
    string MgrpPath,
    int SegmentStart,
    int SegmentEnd,
    int SegmentLength,
    int AbsoluteDataStart,
    Phase2SegmentSummary SegmentSummary,
    int SampleCount,
    TripleStats Stats,
    string CsvPath);
