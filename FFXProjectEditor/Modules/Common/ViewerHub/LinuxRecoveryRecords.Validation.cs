// WHY: A parsed recovery record must remain a finite, coherent observation, never a write authority.
// MAINT: Live adapters still verify backup/target provenance, root ownership and each inode by descriptor.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryRecords
{
    internal static string EpochLeaf(ulong ordinal, Guid scope) =>
        "epoch-" + Number(ordinal) + "-" + Id(scope) + ".json";

    internal static string IntentLeaf(Guid scope, ulong sequence, Guid operation) =>
        OperationStem(scope, sequence, operation) + ".intent.json";

    internal static string CompletionLeaf(Guid scope, ulong sequence, Guid operation) =>
        OperationStem(scope, sequence, operation) + ".complete.json";

    internal static string RetainedLeaf(Guid scope, Guid operation, int stepIndex)
    {
        Need(stepIndex is 0 or 1, "Invalid retained step index.");
        return RetentionPrefix + Id(scope) + "-" + Id(operation) + "-" +
            stepIndex.ToString(CultureInfo.InvariantCulture) + ".bin";
    }

    private static string OperationStem(Guid scope, ulong sequence, Guid operation) =>
        Id(scope) + "-" + Number(sequence) + "-" + Id(operation);

    private static string Id(Guid id)
    {
        Need(id != Guid.Empty, "Empty recovery identifier.");
        return id.ToString("N");
    }

    private static string Number(ulong number)
    {
        Need(number > 0, "Recovery order must be positive.");
        return number.ToString("D20", CultureInfo.InvariantCulture);
    }

    internal static void Validate(Epoch value)
    {
        Need(value is not null, "Missing epoch.");
        Need(value.Version == FormatVersion, "Unsupported recovery version.");
        Validate(value.Root);
        _ = EpochLeaf(value.Ordinal, value.Scope);
        Hash(value.CarryOverSha256);
        Need(value.CarryOverBytes >= 0 && value.CarryOverBytes <= LinuxRecoveryBudget.MaximumBytes,
            "Invalid carry-over byte count.");
        Need(value.CarryOverEntries >= 0 && value.CarryOverEntries <= LinuxRecoveryBudget.MaximumEntries,
            "Invalid carry-over entry count.");
        Need(value.CreatedUtc.Offset == TimeSpan.Zero, "Recovery display time must be UTC.");
        if (value.Previous is { } previous)
        {
            Validate(previous.Identity);
            Hash(previous.Sha256);
            _ = EpochLeaf(previous.Ordinal, previous.Scope);
            Need(previous.Ordinal < value.Ordinal && previous.Scope != value.Scope,
                "Previous head must precede the current scope.");
        }
        else Need(value.Ordinal == 1, "An initial baseline starts at ordinal one.");
    }

    internal static void Validate(Intent value)
    {
        Need(value is not null, "Missing intent.");
        Need(value.Version == FormatVersion, "Unsupported recovery version.");
        _ = IntentLeaf(value.Scope, value.Sequence, value.Operation);
        Need(Enum.IsDefined(value.Kind), "Unknown recovery operation.");
        Need((value.Kind == OperationKind.MagicSave) == (value.Second is not null),
            "Recovery operation has the wrong step count.");
        ValidateStep(value, value.First, 0);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var expectedIdentities = new HashSet<Identity>();
        AddNames(value.First);
        if (value.Second is { } second)
        {
            ValidateStep(value, second, 1);
            AddNames(second);
            Need(value.First.TargetLeaf == second.TargetLeaf + ".bak",
                "Magic save must declare backup first and target second.");
        }
        if (value.Kind == OperationKind.MagicRestore)
            Need(value.First.Expected is not null, "Restore requires an observed target.");

        void AddNames(Step step)
        {
            Need(names.Add(step.TargetLeaf), "Repeated recovery target.");
            if (step.Expected is { } expected)
                Need(expectedIdentities.Add(expected.Identity), "Distinct targets cannot share an expected inode.");
            if (step.RetentionLeaf is { } retained)
                Need(names.Add(retained), "Repeated recovery retention leaf.");
        }
    }

    internal static void Validate(Completion value)
    {
        Need(value is not null, "Missing completion.");
        Need(value.Version == FormatVersion, "Unsupported recovery version.");
        _ = CompletionLeaf(value.Scope, value.Sequence, value.Operation);
        Hash(value.IntentSha256);
        var identities = new HashSet<Identity>();
        AddReceipt(value.First);
        if (value.Second is { } second) AddReceipt(second);

        void AddReceipt(StepReceipt receipt)
        {
            ValidateReceipt(receipt);
            Need(identities.Add(receipt.Target.Identity), "Completion reuses a target inode.");
            if (receipt.Retained is { } retained)
                Need(identities.Add(retained.Identity), "Completion reuses a retained inode.");
        }
    }

    private static void ValidateStep(Intent intent, Step step, int index)
    {
        Need(step is not null, "Missing recovery step.");
        PublicLeaf(step.TargetLeaf);
        Need(step.PlannedLength >= 0 && step.PlannedLength <= MaximumPayloadBytes,
            "Invalid planned payload length.");
        Hash(step.PlannedSha256);
        if (step.Expected is { } expected)
        {
            Validate(expected);
            Need(step.RetentionLeaf == RetainedLeaf(intent.Scope, intent.Operation, index),
                "Replacement requires its exact retained leaf.");
        }
        else Need(step.RetentionLeaf is null, "Create step cannot displace a payload.");
    }

    private static void ValidateReceipt(StepReceipt value)
    {
        Need(value is not null, "Missing step receipt.");
        Validate(value.Target);
        Need((value.Target.Stamp.Mode & 0xFFF) == 0x180, "New payload must be private0600.");
        if (value.Retained is { } retained)
        {
            Validate(retained);
            Need(retained.Identity != value.Target.Identity, "Exchange receipts alias one inode.");
        }
    }

    private static void Validate(Artifact value)
    {
        Need(value is not null, "Missing artifact reference.");
        Validate(value.Identity);
        Need(value.Length >= 0 && value.Length <= MaximumPayloadBytes, "Invalid artifact length.");
        Hash(value.Sha256);
        Stamp stamp = value.Stamp;
        Need(stamp is not null, "Missing artifact stamp.");
        Need((stamp.Mode & 0xF000) == 0x8000 && (stamp.Mode & 0xE12) == 0 &&
            (stamp.Mode & 0x100) != 0 && stamp.Links == 1,
            "Invalid observed artifact mode or link count.");
        Need(stamp.ModifiedNanos < 1_000_000_000 && stamp.ChangedNanos < 1_000_000_000,
            "Invalid nanosecond stamp.");
    }

    private static void Validate(Identity value)
    {
        Need(value is not null && value.Backend == "Linux" && value.Inode != 0 && value.Mount != 0,
            "Recovery identity must be a tagged Linux inode.");
    }

    private static void PublicLeaf(string leaf)
    {
        // UTF8 cannot fit255bytes if its UTF16 representation exceeds255code units. Reject
        // before the shared lexical helper can Split a caller-supplied, arbitrarily long string.
        Need(leaf is not null && leaf.Length is > 0 and <= 255, "Invalid recovery leaf length.");
        try { LinuxOutputFileSystem.ValidateLeaf(leaf); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid recovery leaf.", error); }
        Need(!leaf.StartsWith(JournalLeaf, StringComparison.OrdinalIgnoreCase) &&
            !leaf.StartsWith(RetentionPrefix, StringComparison.OrdinalIgnoreCase),
            "Reserved recovery leaf cannot be a public output.");
    }

    private static void Hash(string value)
    {
        Need(value is not null && value.Length == 64, "Invalid recovery SHA256.");
        foreach (char c in value)
            Need(c is >= '0' and <= '9' or >= 'A' and <= 'F', "SHA256 must be canonical uppercase.");
    }

    private static void MatchStep(Step planned, StepReceipt receipt)
    {
        Need(receipt.Target.Length == planned.PlannedLength &&
            receipt.Target.Sha256 == planned.PlannedSha256, "Published target differs from intent.");
        if (planned.Expected is { } original)
        {
            Artifact? retained = receipt.Retained;
            Need(retained is not null && retained.Identity == original.Identity &&
                retained.Length == original.Length && retained.Sha256 == original.Sha256,
                "Retained receipt differs from the displaced observation.");
            Need(retained!.Stamp.Mode == original.Stamp.Mode &&
                retained.Stamp.Owner == original.Stamp.Owner &&
                retained.Stamp.ModifiedSeconds == original.Stamp.ModifiedSeconds &&
                retained.Stamp.ModifiedNanos == original.Stamp.ModifiedNanos,
                "Displaced inode metadata changed outside its expected ctime boundary.");
        }
        else Need(receipt.Retained is null, "Create receipt cannot claim displaced bytes.");
    }

    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
