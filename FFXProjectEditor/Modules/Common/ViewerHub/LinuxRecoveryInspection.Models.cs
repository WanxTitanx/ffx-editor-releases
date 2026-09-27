// WHY: A recovery inspection reports finite observations without granting write or ACK authority.
// MAINT: Head.Source describes the CURRENT header file, not Epoch.Root; snapshots own no handles.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryInspection
{
    internal enum EntrySpace { Root = 0, Journal = 1 }
    internal enum HeadIssue { None, InvalidHeader, DuplicateOrdinal, DuplicateScope }
    internal sealed record Entry(EntrySpace Space, string Leaf, O Observation);
    internal sealed record InventoryDigest(string Sha256, long Bytes, int Entries);
    internal sealed record Head(string Leaf, R.Epoch Epoch, R.Artifact Source);
    internal sealed record Selection(Head? Head, HeadIssue Issue);

    internal sealed class Snapshot
    {
        internal Snapshot(string rootPath, O root, O? journal, IEnumerable<string> rootNames,
            IEnumerable<string> journalNames, IEnumerable<Entry> entries, InventoryDigest digest,
            Selection selection)
        {
            RootPath = rootPath;
            Root = root;
            Journal = journal;
            RootNames = Array.AsReadOnly(rootNames.ToArray());
            JournalNames = Array.AsReadOnly(journalNames.ToArray());
            Entries = Array.AsReadOnly(entries.ToArray());
            Digest = digest;
            HeadSelection = selection;
            Usage = new(digest.Bytes, digest.Entries, RootNames.Count, JournalNames.Count);
        }

        internal string RootPath { get; }
        internal O Root { get; }
        internal O? Journal { get; }
        internal ReadOnlyCollection<string> RootNames { get; }
        internal ReadOnlyCollection<string> JournalNames { get; }
        internal ReadOnlyCollection<Entry> Entries { get; }
        internal InventoryDigest Digest { get; }
        internal Selection HeadSelection { get; }
        internal LinuxRecoveryBudget.Usage Usage { get; }
        // This distinguishes first-use observations only; it never creates a genesis record.
        internal bool ObservedEmptyRoot => JournalNames.Count == 0 &&
            (RootNames.Count == 0 || (RootNames.Count == 1 && RootNames[0] == R.JournalLeaf));
        internal bool? HeadRootMatches => HeadSelection.Head is { } head
            ? head.Epoch.Root == ToIdentity(Root) : null;
    }
}
