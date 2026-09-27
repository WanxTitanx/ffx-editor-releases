// WHY: Filesystem-independent commands become private executable steps only after fresh domain observations.
// MAINT: Metadata floor checks are arithmetic only; their sizing digests NEVER become persisted intents.
// Actual intent references are built exclusively from freshly verified native snapshots and captured bytes.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryExecutor
{
    private static readonly string SizingDigest = new('0', 64);

    private static PreparedOperation Prepare(Command command, D root, H.Analysis analysis, Guid operation)
    {
        var epoch = analysis.Inspection.HeadSelection.Head!.Epoch;
        ulong sequence = analysis.NextSequence!.Value;
        O? targetMetadata = Metadata(root, command.TargetLeaf);
        O? backupMetadata = command.Kind == R.OperationKind.SidecarEdit ? null :
            Metadata(root, command.TargetLeaf + ".bak");

        if (command.Kind == R.OperationKind.MagicRestore &&
            (!targetMetadata.HasValue || !backupMetadata.HasValue || command.Restore is null ||
             LinuxOutputFileSystem.TagIdentity(backupMetadata.Value.File.Identity) != command.Restore.BackupIdentity))
            throw new InvalidDataException("Restore target or promoted backup is missing or has changed identity.");

        // Reject a provable minimum peak before reading or serializing large, otherwise in-bound files.
        // Sidecar final output is not yet known, so its lower bound is zero; exact admission follows merge.
        R.Step SizeStep(string leaf, O? expected, int planned, int index) => new(leaf,
            expected.HasValue ? Artifact(expected.Value, SizingDigest) : null, planned, SizingDigest,
            expected.HasValue ? R.RetainedLeaf(epoch.Scope, operation, index) : null);
        var first = command.Kind == R.OperationKind.MagicSave
            ? SizeStep(command.TargetLeaf + ".bak", backupMetadata,
                checked((int)(targetMetadata?.File.Length ?? command.Original.Length)), 0)
            : SizeStep(command.TargetLeaf, targetMetadata,
                command.Kind == R.OperationKind.MagicRestore ? checked((int)(backupMetadata?.File.Length ?? 0)) : 0, 0);
        var second = command.Kind == R.OperationKind.MagicSave
            ? SizeStep(command.TargetLeaf, targetMetadata, command.Working.Length, 1) : null;
        RequireCapacity(analysis.Inspection.Usage,
            new(1, epoch.Scope, sequence, operation, command.Kind, first, second));

        D.Snapshot? target = ReadExpected(root, command.TargetLeaf, targetMetadata);
        D.Snapshot? backup = command.Kind == R.OperationKind.SidecarEdit ? null :
            ReadExpected(root, command.TargetLeaf + ".bak", backupMetadata);
        var steps = new LiveStep[command.Kind == R.OperationKind.MagicSave ? 2 : 1];
        ReadDependency? dependency = null;
        if (command.Kind == R.OperationKind.MagicSave)
        {
            steps[0] = MakeStep(command.TargetLeaf + ".bak", backup,
                target is null ? command.Original.ToArray() : target.Bytes.ToArray(), 0);
            steps[1] = MakeStep(command.TargetLeaf, target, command.Working.ToArray(), 1);
        }
        else if (command.Kind == R.OperationKind.MagicRestore)
        {
            RestoreProvenance proof = command.Restore ??
                throw new InvalidOperationException("Restore command lost its captured provenance.");
            if (target is null || backup is null || Sha(target.Bytes) != proof.TargetSha256 ||
                backup.Identity != proof.BackupIdentity || Sha(backup.Bytes) != proof.BackupSha256)
                throw new InvalidDataException("Restore target hash or backup identity/hash no longer matches this document.");
            steps[0] = MakeStep(command.TargetLeaf, target, backup.Bytes.ToArray(), 0);
            dependency = new(command.TargetLeaf + ".bak", backup);
        }
        else
        {
            SidecarValues value = command.Sidecar ??
                throw new InvalidOperationException("Sidecar command lost its captured values.");
            byte[] bytes = LinuxSidecarJson.Prepare(target is null ? null : target.Bytes.ToArray(),
                value.Slot, value.X, value.Y, value.Z, value.Heading, value.Scale);
            steps[0] = MakeStep(command.TargetLeaf, target, bytes, 0);
        }

        var intent = new R.Intent(1, epoch.Scope, sequence, operation, command.Kind, steps[0].Record,
            steps.Length == 2 ? steps[1].Record : null);
        R.Validate(intent);
        return new(intent, steps, dependency);

        LiveStep MakeStep(string leaf, D.Snapshot? expected, byte[] bytes, int index)
        {
            CheckPayload(bytes.Length);
            return new(leaf, expected, bytes, new(leaf, expected is null ? null : Artifact(expected),
                bytes.Length, Sha(bytes), expected is null ? null : R.RetainedLeaf(epoch.Scope, operation, index)));
        }
    }

    private static O? Metadata(D root, string leaf)
    {
        O? value = root.ReadFileMetadata(leaf);
        if (value.HasValue && value.Value.File.Length > R.MaximumPayloadBytes)
            throw new InvalidDataException("Existing output exceeds the 64 MiB snapshot limit.");
        return value;
    }

    private static D.Snapshot? ReadExpected(D root, string leaf, O? metadata)
    {
        D.Snapshot? value = root.ReadSnapshot(leaf, R.MaximumPayloadBytes);
        if (value?.Observation != metadata)
            throw new IOException("Output changed between metadata admission and its bounded snapshot.");
        return value;
    }

    private static void RequireCapacity(LinuxRecoveryBudget.Usage observed, R.Intent intent)
    {
        R.Validate(intent); // Keep malformed domain plans distinct from the arithmetic-capacity refusal.
        try { _ = LinuxRecoveryBudget.ForOperation(observed, intent); }
        catch (InvalidDataException error) { throw new CapacityRefusalException(error.Message, error); }
    }

    private sealed class CapacityRefusalException : IOException
    {
        internal CapacityRefusalException(string message, Exception cause) : base(message, cause) { }
    }

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static R.Artifact Artifact(D.Snapshot value) => Artifact(value.Observation, Sha(value.Bytes));
    private static R.Artifact Artifact(O value, string sha)
    {
        var id = value.File.Identity;
        return new(new("Linux", ((ulong)id.DeviceMajor << 32) | id.DeviceMinor, id.Inode, id.MountId),
            value.File.Length, sha, new(value.Mode, value.OwnerId, value.LinkCount,
                value.File.ModifiedSeconds, value.File.ModifiedNanoseconds,
                value.File.ChangedSeconds, value.File.ChangedNanoseconds));
    }
}
