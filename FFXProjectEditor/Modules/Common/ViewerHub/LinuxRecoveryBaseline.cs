// WHY: Explicit baseline acknowledgement is separate from normal history health and save provenance.
// MAINT: Proposal tokens detect stale observations; they are neither secrets nor reusable write authority.
// No consumer calls this API yet. Callers of EnsureGenesis already hold the root cooperative lease.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryBaseline
{
    internal enum Status
    {
        BaselineCreated, GenesisCreated, RescanRequired, NeedsAcknowledgement,
        InvalidHead, AlreadyInitialized, CapacityExceeded, OrdinalExhausted, RecoveryRequired
    }

    internal sealed class Proposal
    {
        internal Proposal(I.Snapshot observation)
        {
            Observation = observation;
            Token = ComputeToken(observation);
        }
        internal I.Snapshot Observation { get; }
        internal string Token { get; }
    }

    internal sealed record Outcome(Status State, string? HeaderPath = null, string? Detail = null);
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromSeconds(5);

    internal static Proposal Inspect(string rootPath, CancellationToken cancellationToken = default)
    {
        CheckRootLocation(rootPath);
        using var root = D.OpenExisting(rootPath);
        using var lease = root.AcquireWriteLock(LeaseTimeout, cancellationToken);
        return new(I.Capture(root)); // No history-health predicate or mkdir in an inspection.
    }

    internal static Outcome Acknowledge(Proposal expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        CheckRootLocation(expected.Observation.RootPath);
        using var root = D.OpenExisting(expected.Observation.RootPath);
        using var lease = root.AcquireWriteLock(LeaseTimeout, cancellationToken);
        I.Snapshot current = I.Capture(root);
        if (!string.Equals(expected.Token, ComputeToken(current), StringComparison.Ordinal))
            return new(Status.RescanRequired);
        if (Refusal(current) is { } refused) return new(refused);
        if (current.Journal is null)
        {
            // Open-or-create cannot prove it created, rather than adopted, a racing directory.
            // Always return to inspection; never exempt an assumed self-created metadata delta.
            using var setup = root.OpenOrCreateChild(R.JournalLeaf);
            return new(Status.RescanRequired);
        }
        return PublishEpoch(root, current, Status.BaselineCreated);
    }

    internal static Outcome EnsureGenesis(D root)
    {
        ArgumentNullException.ThrowIfNull(root);
        CheckRootLocation(root.FullPath);
        I.Snapshot current = I.Capture(root);
        if (current.HeadSelection.Issue != I.HeadIssue.None) return new(Status.InvalidHead);
        if (current.HeadSelection.Head is not null) return new(Status.AlreadyInitialized);
        if (!current.ObservedEmptyRoot) return new(Status.NeedsAcknowledgement);
        if (Refusal(current) is { } refused) return new(refused);
        if (current.Journal is null)
        {
            using var setup = root.OpenOrCreateChild(R.JournalLeaf);
            current = I.Capture(root); // Fresh EMPTY observation, never automatic nonempty ACK.
            if (current.HeadSelection.Issue != I.HeadIssue.None) return new(Status.InvalidHead);
            if (current.HeadSelection.Head is not null) return new(Status.AlreadyInitialized);
            if (!current.ObservedEmptyRoot) return new(Status.NeedsAcknowledgement);
        }
        return PublishEpoch(root, current, Status.GenesisCreated);
    }

    private static Status? Refusal(I.Snapshot observation)
    {
        if (observation.HeadSelection.Issue != I.HeadIssue.None) return Status.InvalidHead;
        if (observation.HeadSelection.Head?.Epoch.Ordinal == ulong.MaxValue) return Status.OrdinalExhausted;
        try { _ = Admission(observation); }
        catch (InvalidDataException) { return Status.CapacityExceeded; }
        return null;
    }

    internal static LinuxRecoveryBudget.Reservation Admission(I.Snapshot observation)
    {
        var usage = observation.Usage;
        try
        {
            if (observation.Journal is null)
                usage = usage with { RootEntries = checked(usage.RootEntries + 1) };
        }
        catch (OverflowException error)
        { throw new InvalidDataException("Recovery journal root-slot accounting overflowed.", error); }
        return LinuxRecoveryBudget.ForAcknowledgement(usage);
    }

    internal static R.Epoch ComposeEpoch(I.Snapshot before, Guid scope, DateTimeOffset createdUtc)
    {
        if (before.HeadSelection.Issue != I.HeadIssue.None || scope == Guid.Empty)
            throw new InvalidDataException("An epoch requires a safe unique head and fresh nonempty scope.");
        string id = scope.ToString("N");
        if (before.Entries.Any(entry => entry.Space == I.EntrySpace.Journal
            ? entry.Leaf.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase) ||
              (entry.Leaf.StartsWith("epoch-", StringComparison.OrdinalIgnoreCase) &&
               entry.Leaf.EndsWith("-" + id + ".json", StringComparison.OrdinalIgnoreCase))
            : entry.Leaf.StartsWith(R.RetentionPrefix + id + "-", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The proposed recovery scope already occurs in observed history.");
        I.Head? old = before.HeadSelection.Head;
        var observed = before.Root.File.Identity;
        var root = new R.Identity("Linux", ((ulong)observed.DeviceMajor << 32) | observed.DeviceMinor,
            observed.Inode, observed.MountId);
        ulong ordinal;
        try { ordinal = old is null ? 1 : checked(old.Epoch.Ordinal + 1); }
        catch (OverflowException error) { throw new InvalidDataException("Recovery ordinal exhausted.", error); }
        var carry = H.CarryOverFingerprint(before.Entries);
        var epoch = new R.Epoch(R.FormatVersion, root, ordinal, scope, old is null ? null :
            new(old.Epoch.Ordinal, old.Epoch.Scope, old.Source.Identity, old.Source.Sha256),
            carry.Sha256, carry.Bytes, carry.Entries, createdUtc);
        R.Validate(epoch);
        return epoch;
    }

    private static void CheckRootLocation(string path)
    {
        string normalized = LinuxReadFileSystem.NormalizeAbsoluteRoot(path);
        if (normalized.Split('/').Any(part => part.StartsWith(R.JournalLeaf, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A recovery journal cannot be selected as an output root.", nameof(path));
    }
}
