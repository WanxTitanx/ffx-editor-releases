// WHY: Native reopen must distinguish healthy history from interrupted/changed state without replay.
// MAINT: These use private ext-family fixtures and simulated header bindings, not remount or ACK UI proof.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using F = FFXProjectEditor.Tests.ViewerHub.LinuxRecoveryHistoryFixture;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryHistoryTests : IDisposable
{
    private readonly F _fixture = new();
    private static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(L.IsSupported);
        return true;
    }

    private H.Analysis Read()
    {
        using var root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        return H.Read(root);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyRoot_NeedsGenesisWithoutCreatingAnyRecord(bool journalExists)
    {
        if (!Native()) return;
        if (journalExists)
        {
            using var root = _fixture.Open();
            using var journal = root.OpenOrCreateChild(R.JournalLeaf);
        }
        string[] before = Directory.GetFileSystemEntries(_fixture.RootPath);
        H.Analysis result = Read();
        Assert.Equal(H.Status.EmptyNeedsGenesis, result.State);
        Assert.Null(result.NextSequence);
        Assert.Equal(before, Directory.GetFileSystemEntries(_fixture.RootPath));
    }

    [Fact]
    public void NonemptyRootWithoutHead_NeedsExplicitBaseline()
    {
        if (!Native()) return;
        File.WriteAllBytes(_fixture.At("user-export.bin"), new byte[] { 1 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("user-export.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(H.Status.NonemptyNeedsExplicitBaseline, Read().State);
        Assert.False(Directory.Exists(_fixture.At(R.JournalLeaf)));
    }

    [Fact]
    public void MalformedHead_IsNotMistakenForEmptyFirstUse()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        File.WriteAllText(_fixture.RecordPath("epoch-invalid.json"), "{");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath("epoch-invalid.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        H.Analysis result = Read();
        Assert.Equal(H.Status.InvalidHead, result.State);
        Assert.Null(result.NextSequence);
    }

    [Fact]
    public void FreshHealthyCapabilities_KeepValueEqualityAndDoNotRequireAcknowledgement()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        H.Analysis first = Read();
        Assert.Equal(H.Status.Healthy, first.State);
        Assert.Equal((ulong)1, first.NextSequence);
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 10; index++)
        {
            H.Analysis next = Read();
            Assert.Equal(H.Status.Healthy, next.State);
            Assert.True(H.SameObservation(first.Inspection, next.Inspection));
        }
        Assert.Equal(before, Directory.GetFileSystemEntries("/proc/self/fd").Length);
        using var root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        _ = H.Read(root);
        _ = root.ReadDirectoryMetadata(); // Reader did not dispose the caller's root/lease.
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompletedCreate_HistoricalPublicTargetMayChangeOrDisappear(bool remove)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation();
        if (remove) File.Delete(_fixture.At(op.Intent.First.TargetLeaf));
        else File.WriteAllBytes(_fixture.At(op.Intent.First.TargetLeaf), new byte[] { 0xFF });
        H.Analysis result = Read();
        Assert.Equal(H.Status.Healthy, result.State);
        Assert.Equal((ulong)2, result.NextSequence);
        Assert.Equal(1, result.CompletedOperations);
        Assert.Equal(!remove, File.Exists(_fixture.At(op.Intent.First.TargetLeaf)));
    }

    [Theory]
    [InlineData("SidecarEdit")] [InlineData("MagicSave")] [InlineData("MagicRestore")]
    public void CompletedReplacement_ValidatesPostExchangeRetainedReceipts(string operationKind)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation(kind: Enum.Parse<R.OperationKind>(operationKind), replace: true);
        H.Analysis result = Read();
        Assert.Equal(H.Status.Healthy, result.State);
        Assert.Equal(1, result.CompletedOperations);
        Assert.Equal((ulong)2, result.NextSequence);
        Assert.Equal(0x1A4, op.Completion!.First.Retained!.Stamp.Mode & 0xFFF);
        Assert.Equal(0x180, op.Completion.First.Target.Stamp.Mode & 0xFFF);
        Assert.Equal(new byte[] { 0x31 }, File.ReadAllBytes(_fixture.At(op.Intent.First.RetentionLeaf!)));
    }

    [Fact]
    public void TwoCompletedSequences_AdvanceOnlyTheirCheckedPrefix()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        _fixture.AddOperation(1);
        _fixture.AddOperation(2);
        H.Analysis result = Read();
        Assert.Equal(H.Status.Healthy, result.State);
        Assert.Equal((ulong)3, result.NextSequence);
        Assert.Equal(2, result.CompletedOperations);
    }

    [Theory]
    [InlineData("intent-only", "IncompleteOperation")]
    [InlineData("backup-only", "IncompleteOperation")]
    [InlineData("orphan-completion", "OrphanCompletion")]
    [InlineData("bad-completion-hash", "InvalidActiveRecord")]
    [InlineData("raw-intent-whitespace", "InvalidActiveRecord")]
    [InlineData("malformed-intent", "InvalidActiveRecord")]
    [InlineData("uppercase-intent", "InvalidActiveRecord")]
    [InlineData("unknown-control", "InvalidActiveRecord")]
    [InlineData("gap", "SequenceConflict")]
    [InlineData("overflow", "SequenceConflict")]
    [InlineData("duplicate-operation", "OperationConflict")]
    [InlineData("duplicate-sequence", "SequenceConflict")]
    public void InconsistentActiveHistory_RequiresRecoveryWithoutReplaying(string kind, string expected)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op;
        if (kind is "gap" or "overflow")
            op = _fixture.AddOperation(kind == "gap" ? 2UL : ulong.MaxValue);
        else if (kind == "intent-only") op = _fixture.AddOperation(executeSteps: 0, complete: false);
        else if (kind == "backup-only") op = _fixture.AddOperation(kind: R.OperationKind.MagicSave,
            executeSteps: 1, complete: false);
        else op = _fixture.AddOperation();
        string intentPath = _fixture.RecordPath(op.IntentLeaf);
        if (kind == "orphan-completion") File.Delete(intentPath);
        if (kind == "bad-completion-hash")
            File.WriteAllBytes(_fixture.RecordPath(op.CompletionLeaf),
                R.Encode(op.Completion! with { IntentSha256 = new string('B', 64) }));
        if (kind == "raw-intent-whitespace")
        {
            File.WriteAllText(intentPath, " " + File.ReadAllText(intentPath));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(intentPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (kind == "malformed-intent") File.WriteAllText(intentPath, "{");
        if (kind == "uppercase-intent") File.Move(intentPath, _fixture.RecordPath(op.IntentLeaf.ToUpperInvariant()));
        if (kind == "unknown-control")
        {
            File.WriteAllText(_fixture.RecordPath(F.ScopeA.ToString("N") + "-unknown"), "{}");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(F.ScopeA.ToString("N") + "-unknown"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (kind == "duplicate-operation") _fixture.AddOperation(2, op.Intent.Operation);
        if (kind == "duplicate-sequence")
        {
            F.Operation other = _fixture.AddOperation(2);
            R.Intent changedIntent = other.Intent with { Sequence = 1 };
            byte[] exact = R.Encode(changedIntent);
            R.Completion changedCompletion = other.Completion! with { Sequence = 1, IntentSha256 = F.Sha(exact) };
            File.Delete(_fixture.RecordPath(other.IntentLeaf));
            File.Delete(_fixture.RecordPath(other.CompletionLeaf));
            File.WriteAllBytes(_fixture.RecordPath(R.IntentLeaf(F.ScopeA, 1, other.Intent.Operation)), exact);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(R.IntentLeaf(F.ScopeA, 1, other.Intent.Operation)), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllBytes(_fixture.RecordPath(R.CompletionLeaf(F.ScopeA, 1, other.Intent.Operation)),
                R.Encode(changedCompletion));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(R.CompletionLeaf(F.ScopeA, 1, other.Intent.Operation)), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        string[] before = Directory.GetFiles(_fixture.RootPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] hashes = before.Select(path => F.Sha(File.ReadAllBytes(path))).ToArray();
        H.Analysis result = Read();
        Assert.Equal(expected, result.State.ToString());
        Assert.Null(result.NextSequence);
        Assert.Equal(before, Directory.GetFiles(_fixture.RootPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray());
        Assert.Equal(hashes, before.Select(path => F.Sha(File.ReadAllBytes(path))));
        if (kind == "backup-only") Assert.False(File.Exists(_fixture.At(op.Intent.Second!.TargetLeaf)));
    }

    [Theory]
    [InlineData("bytes")] [InlineData("missing")] [InlineData("inode")] [InlineData("safe-mode")]
    public void ChangedActiveRetention_RefusesStoredReceiptAsFreshProof(string change)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation(replace: true);
        string leaf = op.Intent.First.RetentionLeaf!;
        string path = _fixture.At(leaf);
        if (change == "bytes") File.WriteAllBytes(path, new byte[] { 0x41 });
        if (change == "missing") File.Delete(path);
        if (change == "inode")
        {
            byte[] original = File.ReadAllBytes(path);
            File.Move(path, _fixture.At("original-retained-inode"));
            File.WriteAllBytes(path, original);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (change == "safe-mode")
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        H.Analysis result = Read();
        Assert.Equal(H.Status.RetainedArtifactChanged, result.State);
        Assert.Equal(leaf, result.ProblemLeaf);
        Assert.Null(result.NextSequence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsafeRetention_RefusesWithoutFollowingOrRepairing(bool hardlink)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation(replace: true);
        string path = _fixture.At(op.Intent.First.RetentionLeaf!);
        string sentinel = _fixture.At("sentinel");
        File.WriteAllBytes(sentinel, new byte[] { 0xEE });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(sentinel, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Delete(path);
        if (hardlink) Assert.Equal(0, Link(sentinel, path));
        else File.CreateSymbolicLink(path, sentinel);
        Assert.ThrowsAny<IOException>(() => Read());
        Assert.Equal(new byte[] { 0xEE }, File.ReadAllBytes(sentinel));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void OrphanCurrentRetention_IsNotAFreeOrHistoricalEntry()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        string leaf = R.RetainedLeaf(F.ScopeA, Guid.NewGuid(), 0);
        File.WriteAllBytes(_fixture.At(leaf), new byte[] { 7 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(leaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        H.Analysis result = Read();
        Assert.Equal(H.Status.UnexpectedRetention, result.State);
        Assert.Equal(leaf, result.ProblemLeaf);
        Assert.True(result.Inspection.Usage.HistoryBytes > 1);
    }

    [Theory]
    [InlineData("bytes")] [InlineData("missing")] [InlineData("name")]
    public void AcknowledgedHistoricalInventory_StillDetectsChanges(string change)
    {
        if (!Native()) return;
        using (var root = _fixture.Open())
        using (var journal = root.OpenOrCreateChild(R.JournalLeaf))
            journal.PublishNew("unknown-old", new byte[] { (byte)'{' });
        _fixture.Baseline(F.ScopeA);
        Assert.Equal(H.Status.Healthy, Read().State); // Historical malformed body need not parse.
        string old = _fixture.RecordPath("unknown-old");
        if (change == "bytes") File.WriteAllBytes(old, new byte[] { (byte)'}' });
        if (change == "missing") File.Delete(old);
        if (change == "name") File.Move(old, _fixture.RecordPath("unknown-renamed"));
        Assert.Equal(H.Status.CarryOverChanged, Read().State);
    }

    [Fact]
    public void NewBaseline_UsesCurrentMetadataWithoutReapplyingOldEmbeddedBindingsOrBadBodies()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA, changedBinding: true);
        F.Operation old = _fixture.AddOperation();
        File.WriteAllText(_fixture.RecordPath(old.IntentLeaf), "{");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(old.IntentLeaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(H.Status.RootBindingChanged, Read().State);
        _fixture.Baseline(F.ScopeB); // Synthetic explicit-baseline fixture, not product ACK proof.
        H.Analysis result = Read();
        Assert.Equal(H.Status.Healthy, result.State);
        Assert.Equal((ulong)1, result.NextSequence);
        Assert.Equal(0, result.CompletedOperations);
        Assert.True(result.Inspection.Usage.ManagedEntries >= 4);
        Assert.Equal("{", File.ReadAllText(_fixture.RecordPath(old.IntentLeaf)));
    }


    [Fact]
    public void HighestMismatchedHead_CannotFallBackToOlderHealthyBinding()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        Assert.Equal(H.Status.Healthy, Read().State);
        _fixture.Baseline(F.ScopeB, changedBinding: true);
        H.Analysis result = Read();
        Assert.Equal(H.Status.RootBindingChanged, result.State);
        Assert.Equal(F.ScopeB, result.Inspection.HeadSelection.Head!.Epoch.Scope);
        Assert.Null(result.NextSequence);
    }

    [Fact]
    public void OversizedHistory_IsAccountedBeforeReadingReservedPayloadBytes()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        _ = Read();
        using (var file = new FileStream(_fixture.At(".spira-retained-oversized"),
            FileMode.CreateNew, FileAccess.Write))
            file.SetLength(LinuxRecoveryBudget.MaximumBytes + 1);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(".spira-retained-oversized"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        long before = GC.GetAllocatedBytesForCurrentThread();
        H.Analysis result = Read();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(H.Status.HistoryOverBudget, result.State);
        Assert.True(allocated < 1024 * 1024, "Bounded metadata path allocated " + allocated);
    }

    [Fact]
    public void LaterRetainedReadChangingEarlierMetadata_RefusesFinalHandoff()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation(kind: R.OperationKind.MagicSave, replace: true);
        string first = _fixture.At(op.Intent.First.RetentionLeaf!);
        string second = _fixture.At(op.Intent.Second!.RetentionLeaf!);
        bool changed = false;
        D.BeforeOperationForTests = (operation, path) =>
        {
            if (changed || operation != "snapshot-open" || path != second) return;
            changed = true;
            File.SetLastWriteTimeUtc(first, new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        };
        try { Assert.ThrowsAny<IOException>(() => Read()); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(changed);
        Assert.Equal(new byte[] { 0x31 }, File.ReadAllBytes(first));
    }

    [Fact]
    public void RepeatedInvalidRecordReads_ReleaseOwnedJournalWithoutMutatingInput()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation();
        File.WriteAllText(_fixture.RecordPath(op.IntentLeaf), "{");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(op.IntentLeaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(H.Status.InvalidActiveRecord, Read().State);
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 10; index++)
            Assert.Equal(H.Status.InvalidActiveRecord, Read().State);
        Assert.Equal(before, Directory.GetFileSystemEntries("/proc/self/fd").Length);
        Assert.Equal("{", File.ReadAllText(_fixture.RecordPath(op.IntentLeaf)));
    }


    [Fact]
    public void StoredRetainedHash_IsRecheckedAgainstFreshPayloadBytes()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation(replace: true);
        var changedFirst = op.Intent.First with
        { Expected = op.Intent.First.Expected! with { Sha256 = new string('A', 64) } };
        R.Intent changedIntent = op.Intent with { First = changedFirst };
        byte[] exact = R.Encode(changedIntent);
        R.Completion changedCompletion = op.Completion! with
        {
            IntentSha256 = F.Sha(exact),
            First = op.Completion.First with
            { Retained = op.Completion.First.Retained! with { Sha256 = new string('A', 64) } }
        };
        R.ValidateCompletion(op.IntentLeaf, exact, changedCompletion); // Internally consistent STORED hashes.
        File.WriteAllBytes(_fixture.RecordPath(op.IntentLeaf), exact);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(op.IntentLeaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllBytes(_fixture.RecordPath(op.CompletionLeaf), R.Encode(changedCompletion));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(op.CompletionLeaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        H.Analysis result = Read();
        Assert.Equal(H.Status.RetainedArtifactChanged, result.State);
        Assert.Equal(new byte[] { 0x31 }, File.ReadAllBytes(_fixture.At(op.Intent.First.RetentionLeaf!)));
    }

    [Theory]
    [InlineData(0)] [InlineData(8193)]
    public void InvalidActiveRecordSize_IsRejectedBeforeOpeningPayloadSnapshot(int size)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        F.Operation op = _fixture.AddOperation();
        string path = _fixture.RecordPath(op.IntentLeaf);
        File.WriteAllBytes(path, new byte[size]);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        bool attempted = false;
        D.BeforeOperationForTests = (operation, file) =>
        {
            if (operation == "snapshot-open" && file == path) attempted = true;
        };
        try { Assert.Equal(H.Status.InvalidActiveRecord, Read().State); }
        finally { D.BeforeOperationForTests = null; }
        Assert.False(attempted);
        Assert.Equal(size, new FileInfo(path).Length);
    }

    [Fact]
    public void MissingPredecessor_IsContextNotARecursivelyRequiredChain()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        I.Head old;
        using (var root = _fixture.Open())
        using (var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5)))
            old = I.Capture(root).HeadSelection.Head!;
        // Construct a structurally valid context-only predecessor absent from the fresh baseline.
        // This is a reducer fixture, not an acknowledgement writer's authority or publication proof.
        File.Delete(_fixture.RecordPath(old.Leaf));
        I.InventoryDigest carry = F.Carry(Array.Empty<I.Entry>());
        R.Epoch next = old.Epoch with
        {
            Ordinal = 2, Scope = F.ScopeB,
            Previous = new(1, F.ScopeA, old.Source.Identity, old.Source.Sha256),
            CarryOverSha256 = carry.Sha256, CarryOverBytes = carry.Bytes, CarryOverEntries = carry.Entries
        };
        File.WriteAllBytes(_fixture.RecordPath(R.EpochLeaf(2, F.ScopeB)), R.Encode(next));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(R.EpochLeaf(2, F.ScopeB)), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(H.Status.Healthy, Read().State);
        Assert.Single(Directory.GetFiles(_fixture.At(R.JournalLeaf)));
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string created);

    public void Dispose() => _fixture.Dispose();
}
