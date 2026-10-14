// WHY: An in-flight operation must match its exact LIVE prefix without treating its incomplete intent as reopened history.
// MAINT: Only the directory just mutated may change length/mtime/ctime. All other names, artifacts and stamps stay pinned.
// These finite observations cooperate with native expected-snapshot checks; they do not make rename an inode-CAS or two-file transaction.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryExecutor
{
    private static void VerifyPrefix(Attempt value, bool rootMayChange, bool journalMayChange)
    {
        I.Snapshot initial = value.Initial.Inspection;
        I.Snapshot prior = value.LastInspection;
        I.Snapshot now = I.Capture(value.Root);
        if (now.RootPath != initial.RootPath || now.HeadSelection != initial.HeadSelection ||
            !SameDirectory(prior.Root, now.Root, rootMayChange) ||
            prior.Journal is not { } oldJournal || now.Journal is not { } newJournal ||
            !SameDirectory(oldJournal, newJournal, journalMayChange))
            throw new IOException("Recovery head or directory metadata changed outside the declared write boundary.");

        var rootNames = initial.RootNames.ToHashSet(StringComparer.Ordinal);
        var journalNames = initial.JournalNames.ToHashSet(StringComparer.Ordinal);
        var additions = new Dictionary<(I.EntrySpace Space, string Leaf), D.Snapshot>();
        if (value.IntentSnapshot is { } intent)
        {
            journalNames.Add(value.IntentLeaf);
            additions.Add((I.EntrySpace.Journal, value.IntentLeaf), intent);
        }
        if (value.CompletionSnapshot is { } completion)
        {
            journalNames.Add(value.CompletionLeaf);
            additions.Add((I.EntrySpace.Journal, value.CompletionLeaf), completion);
        }
        for (int index = 0; index < value.CompletedSteps; index++)
        {
            LiveStep step = value.Prepared.Steps[index];
            VerifiedStep done = value.Done[index] ??
                throw new InvalidOperationException("A live step advanced without a verified receipt.");
            rootNames.Add(step.Leaf);
            if (step.Record.RetentionLeaf is { } leaf)
            {
                rootNames.Add(leaf);
                additions.Add((I.EntrySpace.Root, leaf), done.Retained ??
                    throw new InvalidOperationException("A replacement lost its verified displaced snapshot."));
            }
        }
        if (!rootNames.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(now.RootNames, StringComparer.Ordinal) ||
            !journalNames.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(now.JournalNames, StringComparer.Ordinal))
            throw new IOException("Recovery names differ from the admitted operation prefix.");
        var oldEntries = now.Entries.Where(entry => !additions.ContainsKey((entry.Space, entry.Leaf))).ToArray();
        if (I.Fingerprint(oldEntries) != initial.Digest)
            throw new IOException("Previously observed reserved history changed during the live operation.");
        foreach (var addition in additions)
        {
            I.Entry? observed = now.Entries.SingleOrDefault(entry =>
                entry.Space == addition.Key.Space && entry.Leaf == addition.Key.Leaf);
            if (observed is null || observed.Observation != addition.Value.Observation)
                throw new IOException("A new reserved artifact differs from its verified receipt.");
        }

        // Existing public versions are NOT part of historical replay, but they ARE part of this current attempt.
        if (value.IntentSnapshot is { } verifiedIntent)
            VerifySame(value.Journal, value.IntentLeaf, verifiedIntent);
        if (value.CompletionSnapshot is { } verifiedCompletion)
            VerifySame(value.Journal, value.CompletionLeaf, verifiedCompletion);
        for (int index = 0; index < value.Prepared.Steps.Length; index++)
        {
            LiveStep step = value.Prepared.Steps[index];
            if (index < value.CompletedSteps)
            {
                VerifiedStep done = value.Done[index]!;
                VerifySame(value.Root, step.Leaf, done.Target);
                if (done.Retained is { } retained) VerifySame(value.Root, step.Record.RetentionLeaf!, retained);
            }
            else VerifySame(value.Root, step.Leaf, step.Expected);
        }
        if (value.Prepared.Dependency is { } dependency)
            VerifySame(value.Root, dependency.Leaf, dependency.Snapshot);
        I.Snapshot afterReads = I.Capture(value.Root);
        if (!H.SameObservation(now, afterReads))
            throw new IOException("Recovery inventory changed during the live prefix verification.");
        value.LastInspection = afterReads;
    }

    private static bool SameDirectory(O expected, O actual, bool allowMutation)
    {
        if (allowMutation)
            actual = actual with { File = actual.File with {
                Length = expected.File.Length,
                ModifiedSeconds = expected.File.ModifiedSeconds,
                ModifiedNanoseconds = expected.File.ModifiedNanoseconds,
                ChangedSeconds = expected.File.ChangedSeconds,
                ChangedNanoseconds = expected.File.ChangedNanoseconds } };
        return expected == actual; // Identity/mount/type/owner/mode/nlink NEVER receive an allowance.
    }

    private static void VerifySame(D directory, string leaf, D.Snapshot? expected)
    {
        if (expected is null)
        {
            if (directory.ReadFileMetadata(leaf) is not null)
                throw new IOException("An unexecuted create target is no longer absent.");
            return;
        }
        D.Snapshot actual = directory.ReadSnapshot(leaf, expected.Bytes.Length)
            ?? throw new IOException("A live output or dependency disappeared.");
        if (actual.Observation != expected.Observation || !actual.Bytes.SequenceEqual(expected.Bytes))
            throw new IOException("A live output or dependency changed identity, metadata or bytes.");
    }

    private static D.Snapshot VerifyPublication(D directory, string leaf, ReadOnlySpan<byte> bytes,
        D.Publication receipt, bool requirePrivate)
    {
        D.Snapshot actual = directory.ReadSnapshot(leaf, bytes.Length)
            ?? throw new IOException("Published artifact disappeared before receipt verification.");
        if (receipt.FullPath != Path.Combine(directory.FullPath, leaf) ||
            actual.Identity != receipt.Identity || actual.Bytes.Length != receipt.Length ||
            !actual.Bytes.SequenceEqual(bytes) || Sha(actual.Bytes) != receipt.Sha256 ||
            (requirePrivate && (actual.Observation.Mode & 0xFFF) != 0x180))
            throw new IOException("Published artifact differs from its exact live receipt or private mode.");
        return actual;
    }

    private static void VerifyDisplaced(D.Snapshot original, D.Snapshot retained)
    {
        var comparable = retained.Observation with {
            File = retained.Observation.File with {
                ChangedSeconds = original.Observation.File.ChangedSeconds,
                ChangedNanoseconds = original.Observation.File.ChangedNanoseconds } };
        if (comparable != original.Observation || !retained.Bytes.SequenceEqual(original.Bytes))
            throw new IOException("Retained bytes or metadata differ outside the native exchange ctime allowance.");
    }
}
