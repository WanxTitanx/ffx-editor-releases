// WHY: A baseline is one immutable header; any possible publication survives errors as local evidence.
// MAINT: Caller owns the root lease. Journal timestamps may change for this one addition; existing
// reserved files, root metadata, exact names and the new header must still agree before success.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Diagnostics;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryBaseline
{
    private static Outcome PublishEpoch(D root, I.Snapshot before, Status successState)
    {
        if (Refusal(before) is { } refused) return new(refused);
        R.Epoch epoch = ComposeEpoch(before, Guid.NewGuid(), DateTimeOffset.UtcNow);
        byte[] bytes = R.Encode(epoch);
        string leaf = R.EpochLeaf(epoch.Ordinal, epoch.Scope);
        string? publishedPath = null;
        try
        {
            using var journal = root.OpenExistingChild(R.JournalLeaf);
            if (!H.SameObservation(before, I.Capture(root))) return new(Status.RescanRequired);
            D.Publication receipt = journal.PublishNew(leaf, bytes);
            publishedPath = receipt.FullPath; // Keep through EVERY subsequent read/capture/history failure.
            D.Snapshot header = journal.ReadSnapshot(leaf, R.MaximumRecordBytes)
                ?? throw new IOException("Published recovery header disappeared.");
            if (header.Identity != receipt.Identity || header.Bytes.Length != receipt.Length ||
                !header.Bytes.SequenceEqual(bytes) ||
                Convert.ToHexString(SHA256.HashData(header.Bytes)) != receipt.Sha256)
                throw new IOException("Published recovery header differs from its receipt.");
            I.Snapshot after = I.Capture(root);
            VerifyAddition(before, after, epoch, header);
            H.Analysis reopened = H.Read(root);
            if (reopened.State != H.Status.Healthy || !H.SameObservation(after, reopened.Inspection))
                throw new IOException("New recovery baseline did not reopen with the verified inventory.");
            DebugLog.Info("LinuxRecovery.Baseline", "Verified a new epoch; old uncertainty remains acknowledged history.");
            return new(successState, publishedPath);
        }
        catch (LinuxPublicationUncertainException error)
        {
            return Uncertain(error.PossiblyPublishedPath, error);
        }
        catch (Exception error) when (publishedPath is not null)
        {
            return Uncertain(publishedPath, error);
        }
    }

    private static Outcome Uncertain(string path, Exception error)
    {
        DebugLog.Error("LinuxRecovery.Baseline", "New epoch is uncertain; no retained evidence was removed.", error);
        return new(Status.RecoveryRequired, path, error.Message);
    }

    private static void VerifyAddition(I.Snapshot before, I.Snapshot after, R.Epoch epoch, D.Snapshot header)
    {
        string leaf = R.EpochLeaf(epoch.Ordinal, epoch.Scope);
        if (before.Journal is not { } oldJournal || after.Journal is not { } newJournal ||
            before.RootPath != after.RootPath || before.Root != after.Root ||
            oldJournal.File.Identity != newJournal.File.Identity ||
            oldJournal.Mode != newJournal.Mode || oldJournal.OwnerId != newJournal.OwnerId ||
            oldJournal.LinkCount != newJournal.LinkCount ||
            !before.RootNames.SequenceEqual(after.RootNames, StringComparer.Ordinal) ||
            !before.JournalNames.Append(leaf).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(after.JournalNames, StringComparer.Ordinal))
            throw new IOException("Recovery directories differ from the single planned header addition.");
        I.Entry[] remaining = after.Entries.Where(entry =>
            entry.Space != I.EntrySpace.Journal || entry.Leaf != leaf).ToArray();
        I.Entry? added = after.Entries.SingleOrDefault(entry =>
            entry.Space == I.EntrySpace.Journal && entry.Leaf == leaf);
        if (added is null || added.Observation != header.Observation ||
            I.Fingerprint(remaining) != before.Digest ||
            after.HeadSelection.Issue != I.HeadIssue.None || after.HeadSelection.Head is not { } head ||
            head.Epoch != epoch || head.Leaf != leaf ||
            head.Source.Sha256 != Convert.ToHexString(SHA256.HashData(header.Bytes)))
            throw new IOException("Recovery files differ from the single verified header addition.");
    }
}
