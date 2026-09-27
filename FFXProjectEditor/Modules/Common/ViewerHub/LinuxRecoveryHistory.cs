// WHY: History health and carry-over are finite observations, never reusable write/restore authority.
// MAINT: The inventory digest and carry-over digest have distinct domains. No persisted instruction runs.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryHistory
{
    internal enum Status
    {
        Healthy, EmptyNeedsGenesis, NonemptyNeedsExplicitBaseline, InvalidHead, RootBindingChanged,
        HistoryOverBudget, CarryOverChanged, InvalidActiveRecord, IncompleteOperation,
        OrphanCompletion, SequenceConflict, OperationConflict, RetainedArtifactChanged,
        UnexpectedRetention
    }

    internal sealed record Analysis(Status State, I.Snapshot Inspection, ulong? NextSequence,
        int CompletedOperations, string? ProblemLeaf);

    internal static I.InventoryDigest CarryOverFingerprint(IReadOnlyList<I.Entry> entries)
    {
        I.InventoryDigest inventory = I.Fingerprint(entries);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] domain = Encoding.ASCII.GetBytes("SPIRA-REFORGE/LinuxRecovery/CarryOver/v1");
        Span<byte> scalar = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(scalar, (ulong)domain.Length);
        hash.AppendData(scalar);
        hash.AppendData(domain);
        hash.AppendData(Convert.FromHexString(inventory.Sha256)); // Fixed SHA256 width,32bytes.
        BinaryPrimitives.WriteInt64LittleEndian(scalar, inventory.Bytes);
        hash.AppendData(scalar);
        BinaryPrimitives.WriteUInt64LittleEndian(scalar, (ulong)inventory.Entries);
        hash.AppendData(scalar);
        return inventory with { Sha256 = Convert.ToHexString(hash.GetHashAndReset()) };
    }

    internal static bool SameObservation(I.Snapshot left, I.Snapshot right) =>
        left.RootPath == right.RootPath && left.Root == right.Root && left.Journal == right.Journal &&
        left.Digest == right.Digest && left.HeadSelection == right.HeadSelection &&
        left.RootNames.SequenceEqual(right.RootNames, StringComparer.Ordinal) &&
        left.JournalNames.SequenceEqual(right.JournalNames, StringComparer.Ordinal);

    private static bool IsCurrentJournal(I.Entry entry, string scope) =>
        entry.Space == I.EntrySpace.Journal &&
        entry.Leaf.StartsWith(scope + "-", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentRetention(I.Entry entry, string scope) =>
        entry.Space == I.EntrySpace.Root &&
        entry.Leaf.StartsWith(R.RetentionPrefix + scope + "-", StringComparison.OrdinalIgnoreCase);

    // Keep this local conversion explicit: existing inspection helpers stay private/frozen.
    // Identities are tagged observations, not authentication or a permanent mount identity.
    private static R.Artifact ObservedArtifact(O observation, string sha256) => new(
        new("Linux", ((ulong)observation.File.Identity.DeviceMajor << 32) |
            observation.File.Identity.DeviceMinor, observation.File.Identity.Inode,
            observation.File.Identity.MountId),
        observation.File.Length, sha256,
        new(observation.Mode, observation.OwnerId, observation.LinkCount,
            observation.File.ModifiedSeconds, observation.File.ModifiedNanoseconds,
            observation.File.ChangedSeconds, observation.File.ChangedNanoseconds));
}
