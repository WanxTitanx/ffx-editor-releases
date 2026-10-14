using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Text
{
    public enum TextTokenKind
    {
        PlainText,
        FormatNamed,
        FormatDecodeOnly,
        PlaceholderCharacterName,
        RawControl,
        UnknownAngleToken,
        UnresolvedMarker,
        Malformed
    }

    public enum TextTokenSafety
    {
        EncodeSafe,
        DecodeOnly,
        RawPreserve,
        UnknownRisk
    }

    public readonly record struct TextTokenKindCount(TextTokenKind Kind, int Count);

    public readonly record struct TextTokenSafetyCount(TextTokenSafety Safety, int Count);

    public sealed record TextTokenExample(
        TextTokenKind Kind,
        TextTokenSafety Safety,
        string Sample,
        int Count);

    public sealed record TextTokenInventory(
        int TotalTokenCount,
        IReadOnlyList<TextTokenKindCount> KindCounts,
        IReadOnlyList<TextTokenSafetyCount> SafetyCounts,
        IReadOnlyList<TextTokenExample> Examples);

    public enum TextWriteIssueStage
    {
        Decode,
        Encode,
        Preserve
    }

    public enum TextWriteIssueCode
    {
        DecodeOnlyTokenPresent,
        RawControlTokenPresent,
        UnknownAngleTokenPresent,
        UnresolvedMarkerPresent,
        MalformedTokenPresent,
        TabCharacterPresent,
        EmbeddedNulPresent
    }

    public sealed record TextValidationIssue(
        TextWriteIssueStage Stage,
        TextWriteIssueCode Code,
        TextTokenKind Kind,
        TextTokenSafety Safety,
        string Token,
        string Reason);

    public sealed record TextWriteValidationResult(
        bool CanReencode,
        TextWriteIssueCode? PrimaryIssueCode,
        TextTokenInventory Inventory,
        IReadOnlyList<TextValidationIssue> Issues,
        string Summary);

    public enum TextRoundTripDecisionKind
    {
        ReuseOriginalBytes,
        ReencodeAllowed,
        Blocked
    }

    public sealed record TextRoundTripDecision(
        TextRoundTripDecisionKind Decision,
        string Reason,
        TextWriteValidationResult Validation);
}
