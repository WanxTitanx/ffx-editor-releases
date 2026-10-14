using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Text
{
    public static class TextWriteValidation_Util
    {
        static readonly HashSet<string> FormatNamedTokens = new(FfxEncoding.FormatCodes.Values, StringComparer.Ordinal);
        static readonly HashSet<string> FormatDecodeOnlyTokens = new(FfxEncoding.ReadOnlyFormatCodes.Values, StringComparer.Ordinal);
        static readonly HashSet<string> PlaceholderCharacterNameTokens = new(FfxEncoding.CharacterNameCodes.Values, StringComparer.Ordinal);

        public static TextWriteValidationResult ValidateForReencode(string? text)
        {
            string normalized = TextBinary_Util.NormalizeText(text);
            List<TextValidationIssue> issues = new();
            Dictionary<TextTokenKind, int> kindCounts = new();
            Dictionary<TextTokenSafety, int> safetyCounts = new();
            Dictionary<(TextTokenKind Kind, TextTokenSafety Safety, string Sample), int> examples = new();

            foreach (TextToken token in Tokenize(normalized))
            {
                Increment(kindCounts, token.Kind);
                Increment(safetyCounts, token.Safety);
                Increment(examples, (token.Kind, token.Safety, token.Text));

                if (token.Safety != TextTokenSafety.EncodeSafe)
                {
                    issues.Add(new TextValidationIssue(
                        TextWriteIssueStage.Encode,
                        MapIssueCode(token),
                        token.Kind,
                        token.Safety,
                        token.Text,
                        BuildIssueReason(token)));
                }
            }

            if (normalized.Contains('\t'))
            {
                issues.Add(new TextValidationIssue(
                    TextWriteIssueStage.Encode,
                    TextWriteIssueCode.TabCharacterPresent,
                    TextTokenKind.PlainText,
                    TextTokenSafety.UnknownRisk,
                    "\\t",
                    "Tab characters are blocked by the current text surface."));
            }

            if (normalized.Contains('\0'))
            {
                issues.Add(new TextValidationIssue(
                    TextWriteIssueStage.Encode,
                    TextWriteIssueCode.EmbeddedNulPresent,
                    TextTokenKind.PlainText,
                    TextTokenSafety.UnknownRisk,
                    "\\0",
                    "Embedded NUL characters are blocked by the current text surface."));
            }

            TextTokenInventory inventory = new(
                TotalTokenCount: kindCounts.Values.Sum(),
                KindCounts: kindCounts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new TextTokenKindCount(pair.Key, pair.Value))
                    .ToArray(),
                SafetyCounts: safetyCounts
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new TextTokenSafetyCount(pair.Key, pair.Value))
                    .ToArray(),
                Examples: examples
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key.Kind)
                    .ThenBy(pair => pair.Key.Sample, StringComparer.Ordinal)
                    .Select(pair => new TextTokenExample(
                        pair.Key.Kind,
                        pair.Key.Safety,
                        pair.Key.Sample,
                        pair.Value))
                    .ToArray());

            bool canReencode = issues.Count == 0;
            string summary = canReencode
                ? "Re-encode is allowed: only EncodeSafe token classes were found."
                : $"Re-encode is blocked: {string.Join("; ", issues.Select(issue => $"{issue.Code} [{issue.Token}]"))}.";

            return new TextWriteValidationResult(
                canReencode,
                issues.FirstOrDefault()?.Code,
                inventory,
                issues,
                summary);
        }

        public static TextRoundTripDecision DecideRoundTrip(string? currentText, string? originalDecodedText)
        {
            string normalizedCurrent = TextBinary_Util.NormalizeText(currentText);
            string normalizedOriginal = TextBinary_Util.NormalizeText(originalDecodedText);
            TextWriteValidationResult validation = ValidateForReencode(normalizedCurrent);

            if (string.Equals(normalizedCurrent, normalizedOriginal, StringComparison.Ordinal))
            {
                return new TextRoundTripDecision(
                    TextRoundTripDecisionKind.ReuseOriginalBytes,
                    "Current text matches the original decoded text, so the original bytes can be preserved without re-encoding.",
                    validation);
            }

            if (validation.CanReencode)
            {
                return new TextRoundTripDecision(
                    TextRoundTripDecisionKind.ReencodeAllowed,
                    "Current text changed and only EncodeSafe token classes were found, so re-encoding is allowed.",
                    validation);
            }

            return new TextRoundTripDecision(
                TextRoundTripDecisionKind.Blocked,
                "Current text changed and contains non-EncodeSafe token classes, so re-encoding must stay blocked.",
                validation);
        }

        static IEnumerable<TextToken> Tokenize(string normalizedText)
        {
            if (string.IsNullOrEmpty(normalizedText))
            {
                yield break;
            }

            StringBuilder plainText = new();

            for (int i = 0; i < normalizedText.Length; i++)
            {
                char current = normalizedText[i];

                if (current == '<')
                {
                    if (TryFlushPlainText(plainText, out TextToken plainToken))
                    {
                        yield return plainToken;
                    }

                    int endIndex = normalizedText.IndexOf('>', i + 1);
                    if (endIndex < 0)
                    {
                        yield return BuildMalformedToken(normalizedText[i..]);
                        yield break;
                    }

                    string tokenText = normalizedText[i..(endIndex + 1)];
                    yield return ClassifyAngleToken(tokenText);
                    i = endIndex;
                    continue;
                }

                if (current == '>')
                {
                    if (TryFlushPlainText(plainText, out TextToken plainToken))
                    {
                        yield return plainToken;
                    }

                    yield return BuildMalformedToken(">");
                    continue;
                }

                plainText.Append(current);
            }

            if (TryFlushPlainText(plainText, out TextToken trailingPlainToken))
            {
                yield return trailingPlainToken;
            }
        }

        static TextToken ClassifyAngleToken(string tokenText)
        {
            if (FormatNamedTokens.Contains(tokenText))
            {
                return new TextToken(tokenText, TextTokenKind.FormatNamed, TextTokenSafety.EncodeSafe);
            }

            if (FormatDecodeOnlyTokens.Contains(tokenText))
            {
                return new TextToken(tokenText, TextTokenKind.FormatDecodeOnly, TextTokenSafety.DecodeOnly);
            }

            if (PlaceholderCharacterNameTokens.Contains(tokenText))
            {
                return new TextToken(tokenText, TextTokenKind.PlaceholderCharacterName, TextTokenSafety.EncodeSafe);
            }

            if (tokenText.StartsWith("<MISS:", StringComparison.Ordinal) || tokenText.StartsWith("<C_ERROR:", StringComparison.Ordinal))
            {
                return new TextToken(tokenText, TextTokenKind.UnresolvedMarker, TextTokenSafety.DecodeOnly);
            }

            if (tokenText.StartsWith("<C", StringComparison.Ordinal))
            {
                return new TextToken(tokenText, TextTokenKind.RawControl, TextTokenSafety.RawPreserve);
            }

            return new TextToken(tokenText, TextTokenKind.UnknownAngleToken, TextTokenSafety.UnknownRisk);
        }

        static TextToken BuildMalformedToken(string tokenText)
        {
            return new TextToken(tokenText, TextTokenKind.Malformed, TextTokenSafety.UnknownRisk);
        }

        static string BuildIssueReason(TextToken token)
        {
            return token.Safety switch
            {
                TextTokenSafety.DecodeOnly => "Known decode/readability form that must not be promoted to re-encode.",
                TextTokenSafety.RawPreserve => "Raw control form that only survives by byte preservation, not by free re-encode.",
                TextTokenSafety.UnknownRisk => "Unknown or malformed token form without a trusted write contract.",
                _ => "Encode-safe token unexpectedly reported as an issue."
            };
        }

        static TextWriteIssueCode MapIssueCode(TextToken token)
        {
            return token.Kind switch
            {
                TextTokenKind.FormatDecodeOnly => TextWriteIssueCode.DecodeOnlyTokenPresent,
                TextTokenKind.RawControl => TextWriteIssueCode.RawControlTokenPresent,
                TextTokenKind.UnknownAngleToken => TextWriteIssueCode.UnknownAngleTokenPresent,
                TextTokenKind.UnresolvedMarker => TextWriteIssueCode.UnresolvedMarkerPresent,
                TextTokenKind.Malformed => TextWriteIssueCode.MalformedTokenPresent,
                _ => TextWriteIssueCode.UnknownAngleTokenPresent
            };
        }

        static bool TryFlushPlainText(StringBuilder plainText, out TextToken token)
        {
            if (plainText.Length == 0)
            {
                token = default;
                return false;
            }

            token = new TextToken(plainText.ToString(), TextTokenKind.PlainText, TextTokenSafety.EncodeSafe);
            plainText.Clear();
            return true;
        }

        static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull
        {
            counts[key] = counts.TryGetValue(key, out int current)
                ? current + 1
                : 1;
        }

        readonly record struct TextToken(string Text, TextTokenKind Kind, TextTokenSafety Safety);
    }
}
