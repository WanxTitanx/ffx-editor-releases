using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Modules.BukiGetRewards
{
    internal sealed class BukiGetParserEvidenceIndex
    {
        static readonly Regex BukiRowRegex = new(@"buki_get #(?<row>\d+)", RegexOptions.Compiled);
        static readonly Regex TreasureIndexRegex = new(@"\[(?<treasure>[0-9A-Fa-f]+)h\]\);\s*$", RegexOptions.Compiled);
        static readonly Regex MsgWindowRegex = new(@"msgWindow=(?<msg>[^,]+),", RegexOptions.Compiled);
        static readonly Regex OffsetLabelRegex = new(@"^(?<offset>[0-9A-Fa-f]+)(?:\s+(?<label>[A-Za-z][A-Za-z0-9_]*:))?", RegexOptions.Compiled);

        public string SourcePath { get; init; } = "";
        public string StatusSummary { get; init; } = "Old txt parser event evidence was not loaded.";
        public int TotalGearEvidence { get; init; }
        public IReadOnlyDictionary<int, IReadOnlyList<BukiGetParserEvidenceRow>> ByBukiRow { get; init; } =
            new Dictionary<int, IReadOnlyList<BukiGetParserEvidenceRow>>();

        public static BukiGetParserEvidenceIndex LoadDefault()
        {
            string sourcePath = PortablePathResolver.FirstExistingFile(
                Environment.GetEnvironmentVariable("FFX_EVENT_SCRIPT_EVIDENCE"),
                PortablePathResolver.BundledPath("data", "evidence", "eventScriptOutput.txt")) ?? string.Empty;
            if (!File.Exists(sourcePath))
            {
                return new BukiGetParserEvidenceIndex
                {
                    SourcePath = sourcePath,
                    StatusSummary = "Old txt parser eventScriptOutput.txt was not found; parser-backed event refs are unavailable."
                };
            }

            try
            {
                Dictionary<int, List<BukiGetParserEvidenceRow>> mutableRows = [];
                int total = 0;
                int lineNumber = 0;

                foreach (string line in File.ReadLines(sourcePath))
                {
                    lineNumber++;
                    if (!line.Contains("Common.obtainTreasure", StringComparison.Ordinal) ||
                        !line.Contains("Gear: buki_get #", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    BukiGetParserEvidenceRow? row = TryParseLine(sourcePath, lineNumber, line);
                    if (row == null)
                        continue;

                    if (!mutableRows.TryGetValue(row.BukiRow, out List<BukiGetParserEvidenceRow>? entries))
                    {
                        entries = [];
                        mutableRows[row.BukiRow] = entries;
                    }

                    entries.Add(row);
                    total++;
                }

                Dictionary<int, IReadOnlyList<BukiGetParserEvidenceRow>> byRow = mutableRows
                    .OrderBy(pair => pair.Key)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlyList<BukiGetParserEvidenceRow>)pair.Value
                            .OrderBy(evidence => evidence.SourceLineNumber)
                            .ToList());

                string status = total == 0
                    ? "Old txt parser loaded, but no Common.obtainTreasure -> Gear: buki_get refs were found."
                    : $"Old txt parser loaded {total} Common.obtainTreasure gear refs across {byRow.Count} buki_get rows.";

                return new BukiGetParserEvidenceIndex
                {
                    SourcePath = sourcePath,
                    TotalGearEvidence = total,
                    ByBukiRow = byRow,
                    StatusSummary = status
                };
            }
            catch (Exception ex)
            {
                return new BukiGetParserEvidenceIndex
                {
                    SourcePath = sourcePath,
                    StatusSummary = $"Old txt parser evidence scan failed: {ex.Message}"
                };
            }
        }

        public IReadOnlyList<BukiGetParserEvidenceRow> GetRows(int bukiRow)
        {
            return ByBukiRow.TryGetValue(bukiRow, out IReadOnlyList<BukiGetParserEvidenceRow>? rows)
                ? rows
                : [];
        }

        static BukiGetParserEvidenceRow? TryParseLine(string sourcePath, int lineNumber, string line)
        {
            Match bukiRowMatch = BukiRowRegex.Match(line);
            if (!bukiRowMatch.Success || !int.TryParse(bukiRowMatch.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bukiRow))
                return null;

            Match treasureIndexMatch = TreasureIndexRegex.Match(line);
            int? treasureIndex = null;
            if (treasureIndexMatch.Success &&
                int.TryParse(treasureIndexMatch.Groups["treasure"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedTreasureIndex))
            {
                treasureIndex = parsedTreasureIndex;
            }

            Match msgMatch = MsgWindowRegex.Match(line);
            string msgWindow = msgMatch.Success ? msgMatch.Groups["msg"].Value.Trim() : "msgWindow unknown";

            string scriptOffset = BuildScriptOffsetLabel(line);
            string parserSummary = BuildParserSummary(line);

            return new BukiGetParserEvidenceRow
            {
                BukiRow = bukiRow,
                TreasureIndex = treasureIndex,
                SourceLineNumber = lineNumber,
                SourcePath = sourcePath,
                ScriptOffset = scriptOffset,
                MsgWindow = msgWindow,
                ParserSummary = parserSummary
            };
        }

        static string BuildScriptOffsetLabel(string line)
        {
            Match offsetMatch = OffsetLabelRegex.Match(line);
            if (!offsetMatch.Success)
                return "offset unknown";

            string offset = offsetMatch.Groups["offset"].Value.ToUpperInvariant();
            string label = offsetMatch.Groups["label"].Success
                ? offsetMatch.Groups["label"].Value.TrimEnd(':')
                : "";

            return string.IsNullOrWhiteSpace(label)
                ? $"0x{offset}"
                : $"0x{offset} {label}";
        }

        static string BuildParserSummary(string line)
        {
            const string marker = "treasure=";
            int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
            string summary = markerIndex >= 0
                ? line[(markerIndex + marker.Length)..].Trim()
                : line.Trim();

            if (summary.EndsWith(");", StringComparison.Ordinal))
                summary = summary[..^2].Trim();

            return summary;
        }
    }

    internal sealed class BukiGetParserEvidenceRow
    {
        public required int BukiRow { get; init; }
        public int? TreasureIndex { get; init; }
        public required int SourceLineNumber { get; init; }
        public required string SourcePath { get; init; }
        public required string ScriptOffset { get; init; }
        public required string MsgWindow { get; init; }
        public required string ParserSummary { get; init; }

        public string TreasureIndexLabel => TreasureIndex.HasValue
            ? $"takara #{TreasureIndex.Value:D3} / 0x{TreasureIndex.Value:X}"
            : "takara index unknown";

        public string DetailLabel =>
            $"line {SourceLineNumber}, {ScriptOffset}, {MsgWindow}, {TreasureIndexLabel}: {ParserSummary}";
    }
}
