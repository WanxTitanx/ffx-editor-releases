using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed class TextScriptTokenInventory
    {
        public required int FormatTokenCount { get; init; }
        public required int ReadOnlyFormatTokenCount { get; init; }
        public required int CharacterNameTokenCount { get; init; }
        public required int RawControlTokenCount { get; init; }
        public int GlyphTokenCount { get; init; }
        public required int UnknownAngleTokenCount { get; init; }
        public required int UnresolvedByteMarkerCount { get; init; }
        public required int UnresolvedControlMarkerCount { get; init; }
        public required int MalformedAngleTokenCount { get; init; }
        public required int StrayClosingBracketCount { get; init; }
        public required int TabCharacterCount { get; init; }
        public required IReadOnlyDictionary<string, int> ExactFormatTokenCounts { get; init; }
        public required IReadOnlyDictionary<string, int> ExactReadOnlyFormatTokenCounts { get; init; }
        public required IReadOnlyDictionary<string, int> ExactCharacterNameTokenCounts { get; init; }
        public required IReadOnlyDictionary<string, int> ExactRawControlTokenCounts { get; init; }
        public IReadOnlyDictionary<string, int> ExactGlyphTokenCounts { get; init; } = new Dictionary<string, int>();
        public required IReadOnlyDictionary<string, int> ExactUnknownAngleTokenCounts { get; init; }
        public required IReadOnlyDictionary<string, int> ExactUnresolvedTokenCounts { get; init; }
        public required IReadOnlyDictionary<string, int> ExactMalformedAngleTokenCounts { get; init; }

        public bool HasWriteBlockingIssues =>
            ReadOnlyFormatTokenCount > 0
            || RawControlTokenCount > 0
            || UnknownAngleTokenCount > 0
            || UnresolvedByteMarkerCount > 0
            || UnresolvedControlMarkerCount > 0
            || MalformedAngleTokenCount > 0
            || StrayClosingBracketCount > 0
            || TabCharacterCount > 0;

        public string BuildReadOnlySummary()
        {
            List<string> parts = [];

            if (FormatTokenCount > 0)
                parts.Add($"format tokens [{BuildTopCountsSummary(ExactFormatTokenCounts, 4)}]");

            if (ReadOnlyFormatTokenCount > 0)
                parts.Add($"decode-only format tokens [{BuildTopCountsSummary(ExactReadOnlyFormatTokenCounts, 4)}]");

            if (CharacterNameTokenCount > 0)
                parts.Add($"character-name tokens [{BuildTopCountsSummary(ExactCharacterNameTokenCounts, 4)}]");

            if (RawControlTokenCount > 0)
                parts.Add($"raw controls [{BuildTopCountsSummary(ExactRawControlTokenCounts, 4)}]");

            if (UnknownAngleTokenCount > 0)
                parts.Add($"unknown angle tokens [{BuildTopCountsSummary(ExactUnknownAngleTokenCounts, 4)}]");

            if (UnresolvedByteMarkerCount > 0 || UnresolvedControlMarkerCount > 0)
                parts.Add($"unresolved markers [{BuildTopCountsSummary(ExactUnresolvedTokenCounts, 4)}]");

            if (MalformedAngleTokenCount > 0)
                parts.Add($"malformed angle tokens [{BuildTopCountsSummary(ExactMalformedAngleTokenCounts, 3)}]");

            if (StrayClosingBracketCount > 0)
                parts.Add($"stray '>' x{StrayClosingBracketCount}");

            if (TabCharacterCount > 0)
                parts.Add($"tab chars x{TabCharacterCount}");

            return parts.Count == 0
                ? "No angle-token or control-script issues detected."
                : string.Join(" · ", parts);
        }

        public string BuildWriteBlockerMessage()
        {
            List<string> blockers = [];

            if (MalformedAngleTokenCount > 0)
                blockers.Add($"malformed angle tokens [{BuildTopCountsSummary(ExactMalformedAngleTokenCounts, 3)}]");

            if (StrayClosingBracketCount > 0)
                blockers.Add($"unexpected '>' x{StrayClosingBracketCount}");

            if (TabCharacterCount > 0)
                blockers.Add($"tab chars x{TabCharacterCount}");

            if (ReadOnlyFormatTokenCount > 0)
                blockers.Add($"decode-only format tokens [{BuildTopCountsSummary(ExactReadOnlyFormatTokenCounts, 4)}]");

            if (UnresolvedByteMarkerCount > 0 || UnresolvedControlMarkerCount > 0)
                blockers.Add($"unresolved markers [{BuildTopCountsSummary(ExactUnresolvedTokenCounts, 4)}]");

            if (RawControlTokenCount > 0)
                blockers.Add($"raw controls [{BuildTopCountsSummary(ExactRawControlTokenCounts, 4)}]");

            if (UnknownAngleTokenCount > 0)
                blockers.Add($"unknown angle tokens [{BuildTopCountsSummary(ExactUnknownAngleTokenCounts, 4)}]");

            return blockers.Count == 0
                ? "No write-blocking token issues were detected."
                : $"Text contains non-writable token forms: {string.Join("; ", blockers)}.";
        }

        static string BuildTopCountsSummary(IReadOnlyDictionary<string, int> counts, int maxItems)
        {
            if (counts.Count == 0)
                return "none";

            return string.Join(
                ", ",
                counts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(maxItems)
                    .Select(pair => $"{pair.Key} x{pair.Value}"));
        }
    }

    public static class TextScriptInspection_Util
    {
        static readonly HashSet<string> FormatTokens = [.. FfxEncoding.FormatCodes.Values];
        static readonly HashSet<string> ReadOnlyFormatTokens = [.. FfxEncoding.ReadOnlyFormatCodes.Values];
        static readonly HashSet<string> CharacterNameTokens = [.. FfxEncoding.CharacterNameCodes.Values];
        static readonly HashSet<string> RawControlTokens = [.. FfxEncoding.ControlDecoder.Values];

        public static TextScriptTokenInventory Analyze(string? text)
        {
            string normalized = TextBinary_Util.NormalizeText(text);

            int formatTokenCount = 0;
            int readOnlyFormatTokenCount = 0;
            int characterNameTokenCount = 0;
            int rawControlTokenCount = 0;
            int glyphTokenCount = 0;
            int unknownAngleTokenCount = 0;
            int unresolvedByteMarkerCount = 0;
            int unresolvedControlMarkerCount = 0;
            int malformedAngleTokenCount = 0;
            int strayClosingBracketCount = 0;
            int tabCharacterCount = 0;

            Dictionary<string, int> exactFormatTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactReadOnlyFormatTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactCharacterNameTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactRawControlTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactGlyphTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactUnknownAngleTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactUnresolvedTokenCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> exactMalformedAngleTokenCounts = new(StringComparer.Ordinal);

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '\t')
                {
                    tabCharacterCount++;
                    continue;
                }

                if (c == '>')
                {
                    strayClosingBracketCount++;
                    continue;
                }

                if (c != '<')
                    continue;

                int endIndex = normalized.IndexOf('>', i + 1);
                if (endIndex <= i)
                {
                    string malformed = normalized[i..];
                    malformedAngleTokenCount++;
                    Increment(exactMalformedAngleTokenCounts, malformed);
                    break;
                }

                string token = normalized[i..(endIndex + 1)];
                if (FormatTokens.Contains(token))
                {
                    formatTokenCount++;
                    Increment(exactFormatTokenCounts, token);
                }
                else if (ReadOnlyFormatTokens.Contains(token))
                {
                    readOnlyFormatTokenCount++;
                    Increment(exactReadOnlyFormatTokenCounts, token);
                }
                else if (CharacterNameTokens.Contains(token))
                {
                    characterNameTokenCount++;
                    Increment(exactCharacterNameTokenCounts, token);
                }
                else if (token.StartsWith("<MISS:", StringComparison.Ordinal))
                {
                    unresolvedByteMarkerCount++;
                    Increment(exactUnresolvedTokenCounts, token);
                }
                else if (token.StartsWith("<C_ERROR:", StringComparison.Ordinal))
                {
                    unresolvedControlMarkerCount++;
                    Increment(exactUnresolvedTokenCounts, token);
                }
                else if (IsGlyphToken(token))
                {
                    // 2-byte glyph token (<FTCX:n>, <FONTk:n>): a writable alternate-font kanji. Not blocking.
                    glyphTokenCount++;
                    Increment(exactGlyphTokenCounts, token);
                }
                else if (RawControlTokens.Contains(token) || token.StartsWith("<C", StringComparison.Ordinal))
                {
                    rawControlTokenCount++;
                    Increment(exactRawControlTokenCounts, token);
                }
                else
                {
                    unknownAngleTokenCount++;
                    Increment(exactUnknownAngleTokenCounts, token);
                }

                i = endIndex;
            }

            return new TextScriptTokenInventory
            {
                FormatTokenCount = formatTokenCount,
                ReadOnlyFormatTokenCount = readOnlyFormatTokenCount,
                CharacterNameTokenCount = characterNameTokenCount,
                RawControlTokenCount = rawControlTokenCount,
                GlyphTokenCount = glyphTokenCount,
                UnknownAngleTokenCount = unknownAngleTokenCount,
                UnresolvedByteMarkerCount = unresolvedByteMarkerCount,
                UnresolvedControlMarkerCount = unresolvedControlMarkerCount,
                MalformedAngleTokenCount = malformedAngleTokenCount,
                StrayClosingBracketCount = strayClosingBracketCount,
                TabCharacterCount = tabCharacterCount,
                ExactFormatTokenCounts = exactFormatTokenCounts,
                ExactReadOnlyFormatTokenCounts = exactReadOnlyFormatTokenCounts,
                ExactCharacterNameTokenCounts = exactCharacterNameTokenCounts,
                ExactRawControlTokenCounts = exactRawControlTokenCounts,
                ExactGlyphTokenCounts = exactGlyphTokenCounts,
                ExactUnknownAngleTokenCounts = exactUnknownAngleTokenCounts,
                ExactUnresolvedTokenCounts = exactUnresolvedTokenCounts,
                ExactMalformedAngleTokenCounts = exactMalformedAngleTokenCounts
            };
        }

        // True for a writable 2-byte glyph token like "<FTCX:5>" or "<FONT0:123>" (alternate-font kanji).
        static bool IsGlyphToken(string token)
        {
            if (token.Length < 4 || token[0] != '<' || token[^1] != '>')
                return false;

            int colon = token.IndexOf(':');
            if (colon <= 1 || colon >= token.Length - 2)
                return false;

            string prefix = token.Substring(1, colon - 1);
            string numberText = token.Substring(colon + 1, token.Length - colon - 2);
            return numberText.Length > 0
                && int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out int idx)
                && FfxEncoding.TryEncodeGlyph(prefix, idx, out _, out _);
        }

        static void Increment(Dictionary<string, int> counts, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;

            counts[token] = counts.TryGetValue(token, out int current)
                ? current + 1
                : 1;
        }
    }
}
