// WHY: Recovery metadata must come from retained existing directories, with exact observed header bytes.
// MAINT: Caller holds the cooperative root lease. This helper creates no directory, holds no payload
// cache and returns no Ready/ACK permission. Directory and leaf rechecks are finite, not atomic.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Diagnostics;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryInspection
{
    internal static Snapshot Capture(D root)
    {
        ArgumentNullException.ThrowIfNull(root);
        O rootBefore = root.ReadDirectoryMetadata();
        string[] rootNames = SortedNames(root);
        foreach (string leaf in rootNames)
        {
            if (leaf.StartsWith(R.JournalLeaf, StringComparison.OrdinalIgnoreCase) &&
                leaf != R.JournalLeaf)
                throw new IOException("Noncanonical reserved recovery journal name.");
        }

        D? journal = null;
        O? journalBefore = null;
        try
        {
            string[] journalNames = Array.Empty<string>();
            if (Array.IndexOf(rootNames, R.JournalLeaf) >= 0)
            {
                journal = root.OpenExistingChild(R.JournalLeaf);
                journalBefore = journal.ReadDirectoryMetadata();
                journalNames = SortedNames(journal);
            }

            var entries = new List<Entry>();
            foreach (string leaf in rootNames)
                if (leaf.StartsWith(R.RetentionPrefix, StringComparison.OrdinalIgnoreCase))
                    entries.Add(new(EntrySpace.Root, leaf, RequiredMetadata(root, leaf)));
            foreach (string leaf in journalNames)
                entries.Add(new(EntrySpace.Journal, leaf, RequiredMetadata(journal!, leaf)));
            InventoryDigest digest = Fingerprint(entries);

            var candidates = new List<Head>();
            bool invalidHeader = false;
            foreach (Entry entry in entries)
            {
                if (entry.Space != EntrySpace.Journal ||
                    !entry.Leaf.StartsWith("epoch-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.Observation.File.Length is < 1 or > R.MaximumRecordBytes)
                {
                    invalidHeader = true;
                    continue; // Accounted metadata, no payload allocation for invalid record sizes.
                }

                D.Snapshot bytes = journal!.ReadSnapshot(entry.Leaf, R.MaximumRecordBytes)
                    ?? throw new IOException("Observed recovery header disappeared.");
                if (bytes.Observation != entry.Observation)
                    throw new IOException("Recovery header changed after inventory.");
                try
                {
                    R.Epoch epoch = R.ReadEpoch(entry.Leaf, bytes.Bytes);
                    string sha = Convert.ToHexString(SHA256.HashData(bytes.Bytes));
                    candidates.Add(new(entry.Leaf, epoch, ToArtifact(bytes.Observation, sha)));
                }
                catch (InvalidDataException)
                {
                    invalidHeader = true; // Never choose another head around a malformed control.
                }
            }

            Selection selection = invalidHeader
                ? new(null, HeadIssue.InvalidHeader) : SelectHead(candidates);
            VerifyInventory(root, journal, rootBefore, journalBefore, rootNames, journalNames, entries);
            DebugLog.Info("LinuxRecovery.Inspect",
                $"Observed {entries.Count} reserved files; structural head status={selection.Issue}.");
            return new(root.FullPath, rootBefore, journalBefore, rootNames, journalNames,
                entries, digest, selection);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            DebugLog.Error("LinuxRecovery.Inspect", "Existing recovery inspection was refused.", error);
            throw;
        }
        finally { journal?.Dispose(); }
    }

    private static Selection SelectHead(IReadOnlyList<Head> candidates)
    {
        var ordinals = new HashSet<ulong>();
        var scopes = new HashSet<Guid>();
        Head? highest = null;
        foreach (Head candidate in candidates)
        {
            if (!ordinals.Add(candidate.Epoch.Ordinal))
                return new(null, HeadIssue.DuplicateOrdinal);
            if (!scopes.Add(candidate.Epoch.Scope))
                return new(null, HeadIssue.DuplicateScope);
            // Compare across ALL recorded roots. Binding is a separate observation on Snapshot.
            if (highest == null || candidate.Epoch.Ordinal > highest.Epoch.Ordinal)
                highest = candidate;
        }
        return new(highest, HeadIssue.None);
    }

    private static string[] SortedNames(D directory) =>
        directory.ListChildNames().OrderBy(leaf => leaf, StringComparer.Ordinal).ToArray();

    private static O RequiredMetadata(D directory, string leaf) =>
        directory.ReadFileMetadata(leaf)
        ?? throw new IOException("Observed reserved recovery entry disappeared.");

    private static void VerifyInventory(D root, D? journal, O rootBefore, O? journalBefore,
        string[] rootNames, string[] journalNames, IReadOnlyList<Entry> entries)
    {
        foreach (Entry entry in entries)
            if (RequiredMetadata(entry.Space == EntrySpace.Root ? root : journal!, entry.Leaf) !=
                entry.Observation)
                throw new IOException("Reserved recovery metadata changed during inspection.");
        if (!rootNames.SequenceEqual(SortedNames(root), StringComparer.Ordinal) ||
            root.ReadDirectoryMetadata() != rootBefore)
            throw new IOException("Recovery root inventory changed during inspection.");
        if (journal != null &&
            (!journalNames.SequenceEqual(SortedNames(journal), StringComparer.Ordinal) ||
             journal.ReadDirectoryMetadata() != journalBefore))
            throw new IOException("Recovery journal inventory changed during inspection.");
        // Recheck root after the final journal observation as well; no future atomicity is implied.
        if (root.ReadDirectoryMetadata() != rootBefore)
            throw new IOException("Recovery root changed before inspection handoff.");
    }
}
