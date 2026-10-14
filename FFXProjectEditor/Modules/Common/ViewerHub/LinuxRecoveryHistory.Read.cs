// WHY: Reopen must validate the active epoch without replaying history or requiring old live target versions.
// MAINT: Caller holds the root lease. Read pairs sequentially; release child handles on every exit.
// Final recapture compares VALUES, not fresh collection references. This is finite, not atomic.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Diagnostics;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryHistory
{
    internal static Analysis Read(D root)
    {
        ArgumentNullException.ThrowIfNull(root);
        I.Snapshot initial = I.Capture(root);
        Analysis result = Evaluate(root, initial);
        if (!SameObservation(initial, I.Capture(root)))
            throw new IOException("Recovery history changed during its bounded validation.");
        DebugLog.Info("LinuxRecovery.History",
            $"Observed history state={result.State}; completed operations={result.CompletedOperations}.");
        return result;
    }

    private static Analysis Evaluate(D root, I.Snapshot initial)
    {
        Analysis Refused(Status state, string? leaf = null) => new(state, initial, null, 0, leaf);
        if (initial.HeadSelection.Issue != I.HeadIssue.None)
            return Refused(Status.InvalidHead);
        if (initial.HeadSelection.Head is not { } head)
            return Refused(initial.ObservedEmptyRoot
                ? Status.EmptyNeedsGenesis : Status.NonemptyNeedsExplicitBaseline);
        if (initial.HeadRootMatches != true)
            return Refused(Status.RootBindingChanged, head.Leaf);
        if (initial.Usage.HistoryBytes > LinuxRecoveryBudget.MaximumBytes ||
            initial.Usage.ManagedEntries > LinuxRecoveryBudget.MaximumEntries ||
            initial.Usage.RootEntries > LinuxRecoveryBudget.MaximumDirectoryEntries ||
            initial.Usage.JournalEntries > LinuxRecoveryBudget.MaximumDirectoryEntries)
            return Refused(Status.HistoryOverBudget);

        string scope = head.Epoch.Scope.ToString("N");
        var carryOver = initial.Entries.Where(entry =>
            !(entry.Space == I.EntrySpace.Journal && entry.Leaf == head.Leaf) &&
            !IsCurrentJournal(entry, scope) && !IsCurrentRetention(entry, scope)).ToArray();
        I.InventoryDigest carry = CarryOverFingerprint(carryOver);
        if (carry.Sha256 != head.Epoch.CarryOverSha256 ||
            carry.Bytes != head.Epoch.CarryOverBytes || carry.Entries != head.Epoch.CarryOverEntries)
            return Refused(Status.CarryOverChanged);

        var active = initial.Entries.Where(entry => IsCurrentJournal(entry, scope))
            .ToDictionary(entry => entry.Leaf, StringComparer.Ordinal);
        foreach (I.Entry entry in active.Values)
            if (!entry.Leaf.EndsWith(".intent.json", StringComparison.Ordinal) &&
                !entry.Leaf.EndsWith(".complete.json", StringComparison.Ordinal))
                return Refused(Status.InvalidActiveRecord, entry.Leaf);
        var retained = initial.Entries.Where(entry => IsCurrentRetention(entry, scope))
            .ToDictionary(entry => entry.Leaf, StringComparer.Ordinal);
        var usedRecords = new HashSet<string>(StringComparer.Ordinal);
        var usedRetained = new HashSet<string>(StringComparer.Ordinal);
        var operations = new HashSet<Guid>();
        ulong nextSequence = 1;
        int completeCount = 0;
        using var journal = root.OpenExistingChild(R.JournalLeaf);
        foreach (I.Entry entry in active.Values
            .Where(entry => entry.Leaf.EndsWith(".intent.json", StringComparison.Ordinal))
            .OrderBy(entry => entry.Leaf, StringComparer.Ordinal))
        {
            string problem = entry.Leaf;
            R.Intent intent;
            R.Completion completion;
            try
            {
                D.Snapshot intentBytes = ReadRecord(journal, entry);
                intent = R.ReadIntent(entry.Leaf, intentBytes.Bytes);
                if (intent.Scope != head.Epoch.Scope)
                    return Refused(Status.InvalidActiveRecord, entry.Leaf);
                if (intent.Sequence != nextSequence)
                    return Refused(Status.SequenceConflict, entry.Leaf);
                if (!operations.Add(intent.Operation))
                    return Refused(Status.OperationConflict, entry.Leaf);
                problem = R.CompletionLeaf(intent.Scope, intent.Sequence, intent.Operation);
                if (!active.TryGetValue(problem, out I.Entry? completionEntry))
                    return Refused(Status.IncompleteOperation, entry.Leaf);
                D.Snapshot completionBytes = ReadRecord(journal, completionEntry);
                completion = R.ReadCompletion(problem, completionBytes.Bytes);
                R.ValidateCompletion(entry.Leaf, intentBytes.Bytes, completion);
            }
            catch (InvalidDataException)
            {
                return Refused(Status.InvalidActiveRecord, problem);
            }

            string? changed = VerifyRetainedStep(root, retained, usedRetained, intent.First, completion.First);
            if (changed is null && intent.Second is { } second)
                changed = VerifyRetainedStep(root, retained, usedRetained, second, completion.Second!);
            if (changed is not null)
                return Refused(Status.RetainedArtifactChanged, changed);
            usedRecords.Add(entry.Leaf);
            usedRecords.Add(problem);
            try { nextSequence = checked(nextSequence + 1); }
            catch (OverflowException) { return Refused(Status.SequenceConflict, entry.Leaf); }
            completeCount++;
        }

        foreach (I.Entry entry in active.Values)
            if (!usedRecords.Contains(entry.Leaf))
                return Refused(Status.OrphanCompletion, entry.Leaf);
        foreach (I.Entry entry in retained.Values)
            if (!usedRetained.Contains(entry.Leaf))
                return Refused(Status.UnexpectedRetention, entry.Leaf);
        return new(Status.Healthy, initial, nextSequence, completeCount, null);
    }

    private static D.Snapshot ReadRecord(D journal, I.Entry entry)
    {
        if (entry.Observation.File.Length is < 1 or > R.MaximumRecordBytes)
            throw new InvalidDataException("Invalid active recovery record length.");
        D.Snapshot snapshot = journal.ReadSnapshot(entry.Leaf, R.MaximumRecordBytes)
            ?? throw new IOException("Observed recovery record disappeared.");
        if (snapshot.Observation != entry.Observation)
            throw new IOException("Recovery record changed after initial inspection.");
        return snapshot;
    }

    private static string? VerifyRetainedStep(D root, IReadOnlyDictionary<string, I.Entry> retained,
        ISet<string> used, R.Step step, R.StepReceipt receipt)
    {
        if (step.RetentionLeaf is not { } leaf) return null;
        if (receipt.Retained is not { } expected || !retained.TryGetValue(leaf, out I.Entry? entry) ||
            !used.Add(leaf) || ObservedArtifact(entry.Observation, expected.Sha256) != expected)
            return leaf; // Metadata rejection precedes any potentially oversized payload allocation.
        D.Snapshot snapshot = root.ReadSnapshot(leaf, R.MaximumPayloadBytes)
            ?? throw new IOException("Observed retained recovery payload disappeared.");
        if (snapshot.Observation != entry.Observation)
            throw new IOException("Retained recovery payload changed during validation.");
        string sha = Convert.ToHexString(SHA256.HashData(snapshot.Bytes));
        return ObservedArtifact(snapshot.Observation, sha) == expected ? null : leaf;
    }
}
