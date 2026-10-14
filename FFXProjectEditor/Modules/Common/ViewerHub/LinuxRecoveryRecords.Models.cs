// WHY: Recovery files need bounded, explicit immutable shapes rather than executable journal commands.
// MAINT: These DTOs carry observations only. Deserializing one never acquires a filesystem capability.
using System;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryRecords
{
    internal const int FormatVersion = 1;
    internal const int MaximumRecordBytes = 8192;
    internal const int MaximumPayloadBytes = 64 * 1024 * 1024;
    internal const string JournalLeaf = ".spira-recovery";
    internal const string RetentionPrefix = ".spira-retained-";

    internal enum OperationKind { MagicSave, MagicRestore, SidecarEdit }

    internal sealed record Identity(
        [property: JsonRequired] string Backend,
        [property: JsonRequired] ulong Device,
        [property: JsonRequired] ulong Inode,
        [property: JsonRequired] ulong Mount);

    internal sealed record Stamp(
        [property: JsonRequired] ushort Mode,
        [property: JsonRequired] uint Owner,
        [property: JsonRequired] uint Links,
        [property: JsonRequired] long ModifiedSeconds,
        [property: JsonRequired] uint ModifiedNanos,
        [property: JsonRequired] long ChangedSeconds,
        [property: JsonRequired] uint ChangedNanos);

    internal sealed record Artifact(
        [property: JsonRequired] Identity Identity,
        [property: JsonRequired] long Length,
        [property: JsonRequired] string Sha256,
        [property: JsonRequired] Stamp Stamp);

    internal sealed record PreviousHead(
        [property: JsonRequired] ulong Ordinal,
        [property: JsonRequired] Guid Scope,
        [property: JsonRequired] Identity Identity,
        [property: JsonRequired] string Sha256);

    internal sealed record Epoch(
        [property: JsonRequired] int Version,
        [property: JsonRequired] Identity Root,
        [property: JsonRequired] ulong Ordinal,
        [property: JsonRequired] Guid Scope,
        [property: JsonRequired] PreviousHead? Previous,
        [property: JsonRequired] string CarryOverSha256,
        [property: JsonRequired] long CarryOverBytes,
        [property: JsonRequired] int CarryOverEntries,
        [property: JsonRequired] DateTimeOffset CreatedUtc);

    internal sealed record Step(
        [property: JsonRequired] string TargetLeaf,
        [property: JsonRequired] Artifact? Expected,
        [property: JsonRequired] int PlannedLength,
        [property: JsonRequired] string PlannedSha256,
        [property: JsonRequired] string? RetentionLeaf);

    internal sealed record Intent(
        [property: JsonRequired] int Version,
        [property: JsonRequired] Guid Scope,
        [property: JsonRequired] ulong Sequence,
        [property: JsonRequired] Guid Operation,
        [property: JsonRequired] OperationKind Kind,
        [property: JsonRequired] Step First,
        [property: JsonRequired] Step? Second);

    internal sealed record StepReceipt(
        [property: JsonRequired] Artifact Target,
        [property: JsonRequired] Artifact? Retained);

    internal sealed record Completion(
        [property: JsonRequired] int Version,
        [property: JsonRequired] Guid Scope,
        [property: JsonRequired] ulong Sequence,
        [property: JsonRequired] Guid Operation,
        [property: JsonRequired] string IntentSha256,
        [property: JsonRequired] StepReceipt First,
        [property: JsonRequired] StepReceipt? Second);
}
