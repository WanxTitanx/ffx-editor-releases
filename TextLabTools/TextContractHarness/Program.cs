using FFXProjectEditor.FfxLib.Text;
using System;
using System.Collections.Generic;

List<SmokeCase> smokeCases =
[
    new(
        "plain-text-reencode",
        CurrentText: "Hello Spira",
        OriginalDecodedText: "Hello",
        ExpectedDecision: TextRoundTripDecisionKind.ReencodeAllowed,
        ExpectedCanReencode: true),
    new(
        "named-format-reencode",
        CurrentText: "<W>Stay alert</>",
        OriginalDecodedText: "Stay alert",
        ExpectedDecision: TextRoundTripDecisionKind.ReencodeAllowed,
        ExpectedCanReencode: true),
    new(
        "placeholder-reencode",
        CurrentText: "<TIDUS> is ready",
        OriginalDecodedText: "Someone is ready",
        ExpectedDecision: TextRoundTripDecisionKind.ReencodeAllowed,
        ExpectedCanReencode: true),
    new(
        "decode-only-reuse",
        CurrentText: "<FMT52>Old display form",
        OriginalDecodedText: "<FMT52>Old display form",
        ExpectedDecision: TextRoundTripDecisionKind.ReuseOriginalBytes,
        ExpectedCanReencode: false),
    new(
        "raw-control-blocked",
        CurrentText: "Alert <C11>",
        OriginalDecodedText: "Alert",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "unresolved-blocked",
        CurrentText: "Broken <MISS:171>",
        OriginalDecodedText: "Broken",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "unknown-angle-blocked",
        CurrentText: "<BTL-PFX:42>Danger",
        OriginalDecodedText: "Danger",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "malformed-blocked",
        CurrentText: "oops>",
        OriginalDecodedText: "oops",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "unterminated-blocked",
        CurrentText: "<C11",
        OriginalDecodedText: string.Empty,
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "tab-blocked",
        CurrentText: "Hello\tSpira",
        OriginalDecodedText: "Hello Spira",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false),
    new(
        "nul-blocked",
        CurrentText: "Hello\0Spira",
        OriginalDecodedText: "Hello Spira",
        ExpectedDecision: TextRoundTripDecisionKind.Blocked,
        ExpectedCanReencode: false)
];

Console.WriteLine("Pt16 Slice 1 Text Contract Harness");
Console.WriteLine();

int failures = 0;
foreach (SmokeCase smokeCase in smokeCases)
{
    TextRoundTripDecision decision = TextWriteValidation_Util.DecideRoundTrip(smokeCase.CurrentText, smokeCase.OriginalDecodedText);
    bool passed = decision.Decision == smokeCase.ExpectedDecision
        && decision.Validation.CanReencode == smokeCase.ExpectedCanReencode;

    Console.WriteLine($"CASE {smokeCase.Name}");
    Console.WriteLine($"  Decision: {decision.Decision}");
    Console.WriteLine($"  CanReencode: {decision.Validation.CanReencode}");
    Console.WriteLine($"  Summary: {decision.Validation.Summary}");
    Console.WriteLine($"  Inventory: {RenderInventory(decision.Validation.Inventory)}");
    Console.WriteLine($"  Result: {(passed ? "PASS" : "FAIL")}");
    Console.WriteLine();

    if (!passed)
    {
        failures++;
    }
}

Console.WriteLine($"Smoke summary: {smokeCases.Count - failures} passed / {smokeCases.Count} total");
Environment.ExitCode = failures == 0 ? 0 : 1;

static string RenderInventory(TextTokenInventory inventory)
{
    List<string> parts = [];

    foreach (TextTokenKindCount kindCount in inventory.KindCounts)
    {
        parts.Add($"{kindCount.Kind} x{kindCount.Count}");
    }

    foreach (TextTokenSafetyCount safetyCount in inventory.SafetyCounts)
    {
        parts.Add($"{safetyCount.Safety} x{safetyCount.Count}");
    }

    return parts.Count == 0
        ? "none"
        : string.Join(", ", parts);
}

sealed record SmokeCase(
    string Name,
    string CurrentText,
    string OriginalDecodedText,
    TextRoundTripDecisionKind ExpectedDecision,
    bool ExpectedCanReencode);
