// WHY: Baselines must require fresh confirmation while preserving every uncertain native artifact.
// MAINT: All writes/faults below target fresh private fixtures. No game/export/profile data is used.
// Synthetic old headers model interrupted/binding-changed history, not an actual remount or UI click.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBaseline;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using F = FFXProjectEditor.Tests.ViewerHub.LinuxRecoveryHistoryFixture;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryBaselineTests : IDisposable
{
    private readonly F _fixture = new();
    private static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(L.IsSupported);
        return true;
    }
    private B.Proposal Inspect() => B.Inspect(_fixture.RootPath);
    private H.Analysis Read()
    {
        using var root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        return H.Read(root);
    }
    private B.Outcome Genesis()
    {
        using var root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        B.Outcome result = B.EnsureGenesis(root);
        _ = root.ReadDirectoryMetadata(); // Baseline did not consume caller root/lease.
        return result;
    }
    private void Journal()
    {
        using var root = _fixture.Open();
        using var journal = root.OpenOrCreateChild(R.JournalLeaf);
    }
    private SortedDictionary<string, string> Files() => new(Directory.GetFiles(_fixture.RootPath, "*",
        SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(_fixture.RootPath, path),
            path => F.Sha(File.ReadAllBytes(path)), StringComparer.Ordinal), StringComparer.Ordinal);
    private static void SameFiles(SortedDictionary<string, string> expected,
        SortedDictionary<string, string> actual) => Assert.Equal(expected.ToArray(), actual.ToArray());

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Inspection_IsExistingOnlyStableAndDoesNotCreateHistory(bool journalExists)
    {
        if (!Native()) return;
        if (journalExists) Journal();
        B.Proposal first = Inspect();
        Assert.Equal(journalExists, first.Observation.Journal is not null);
        Assert.Empty(Files());
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
        for (int n = 0; n < 10; n++)
        {
            B.Proposal next = Inspect();
            Assert.Equal(first.Token, next.Token);
            Assert.True(H.SameObservation(first.Observation, next.Observation));
        }
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
        Assert.Equal(journalExists, Directory.Exists(_fixture.At(R.JournalLeaf)));
    }

    [Fact]
    public void Inspection_MissingRootIsNotCreated()
    {
        if (!Native()) return;
        string missing = _fixture.At("missing");
        Assert.ThrowsAny<IOException>(() => B.Inspect(missing));
        Assert.False(Directory.Exists(missing));
    }

    [Theory]
    [InlineData(".spira-recovery")] [InlineData(".SPIRA-RECOVERY")]
    [InlineData(".spira-recovery-old")] [InlineData(".spira-recovery/child")]
    public void ReservedJournalLocation_IsNeverAnOutputRoot(string relative)
    {
        if (!Native()) return;
        string path = _fixture.At(relative);
        TestDirectory.CreatePrivate(path);
        Assert.Throws<ArgumentException>(() => B.Inspect(path));
        using var root = D.OpenExisting(path);
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        Assert.Throws<ArgumentException>(() => B.EnsureGenesis(root));
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyGenesis_PublishesOneHeaderAndFreshReopenNeedsNoAcknowledgement(bool journalExists)
    {
        if (!Native()) return;
        if (journalExists) Journal();
        B.Outcome result = Genesis();
        Assert.Equal(B.Status.GenesisCreated, result.State);
        Assert.NotNull(result.HeaderPath);
        Assert.True(File.Exists(result.HeaderPath));
        H.Analysis reopened = Read();
        Assert.Equal(H.Status.Healthy, reopened.State);
        Assert.Equal((ulong)1, reopened.NextSequence);
        R.Epoch epoch = reopened.Inspection.HeadSelection.Head!.Epoch;
        Assert.Null(epoch.Previous);
        Assert.Equal(0, epoch.CarryOverBytes);
        Assert.Equal(F.Carry(Array.Empty<I.Entry>()).Sha256, epoch.CarryOverSha256);
        var before = Files();
        Assert.Equal(B.Status.AlreadyInitialized, Genesis().State); // Observation, not write permission.
        SameFiles(before, Files());
    }

    [Theory]
    [InlineData("public")] [InlineData("journal")] [InlineData("retained")]
    public void NonemptyRoot_NeverGetsAutomaticGenesis(string kind)
    {
        if (!Native()) return;
        if (kind == "journal") Journal();
        string path = kind == "journal" ? _fixture.RecordPath("old-unknown") :
            _fixture.At(kind == "retained" ? ".spira-retained-old" : "user.bin");
        File.WriteAllText(path, "{");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var before = Files();
        Assert.Equal(B.Status.NeedsAcknowledgement, Genesis().State);
        SameFiles(before, Files());
        Assert.Equal(kind == "journal", Directory.Exists(_fixture.At(R.JournalLeaf)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Genesis_ReobservesEmptyStateAfterJournalAcquisition(bool addInsideJournal)
    {
        if (!Native()) return;
        bool inserted = false;
        D.BeforeOperationForTests = (op, path) =>
        {
            if (inserted || op != "child-created" || path != _fixture.At(R.JournalLeaf)) return;
            inserted = true;
            File.WriteAllText(addInsideJournal ? _fixture.RecordPath("foreign") : _fixture.At("foreign"), "keep");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(addInsideJournal ? _fixture.RecordPath("foreign") : _fixture.At("foreign"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        };
        B.Outcome result;
        try { result = Genesis(); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(inserted);
        Assert.Equal(B.Status.NeedsAcknowledgement, result.State);
        Assert.Single(Files());
        Assert.Empty(Directory.GetFiles(_fixture.At(R.JournalLeaf), "epoch-*"));
    }

    [Fact]
    public void ExplicitAbsentJournal_RequiresNewPresentProposalBeforePublishing()
    {
        if (!Native()) return;
        File.WriteAllText(_fixture.At("user.bin"), "keep");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("user.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        B.Proposal absent = Inspect();
        Assert.Equal(B.Status.RescanRequired, B.Acknowledge(absent).State);
        Assert.True(Directory.Exists(_fixture.At(R.JournalLeaf)));
        Assert.Empty(Directory.GetFiles(_fixture.At(R.JournalLeaf)));
        Assert.Equal(B.Status.RescanRequired, B.Acknowledge(absent).State); // Old absence token is stale.
        B.Proposal present = Inspect();
        Assert.NotEqual(absent.Token, present.Token);
        Assert.Equal(B.Status.BaselineCreated, B.Acknowledge(present).State);
        Assert.Equal("keep", File.ReadAllText(_fixture.At("user.bin")));
        Assert.Equal(H.Status.Healthy, Read().State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitAbsentJournal_CannotAutomaticallyAcknowledgeConcurrentAdoption(bool withData)
    {
        if (!Native()) return;
        File.WriteAllText(_fixture.At("user.bin"), "keep");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("user.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        B.Proposal proposal = Inspect();
        bool adopted = false;
        D.BeforeOperationForTests = (op, path) =>
        {
            if (adopted || op != "child-before-open" || path != _fixture.At(R.JournalLeaf)) return;
            adopted = true;
            TestDirectory.CreatePrivate(path);
            if (withData) File.WriteAllText(_fixture.RecordPath("foreign"), "{");
            if (withData && OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath("foreign"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        };
        B.Outcome result;
        try { result = B.Acknowledge(proposal); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(adopted);
        Assert.Equal(B.Status.RescanRequired, result.State);
        Assert.Null(result.HeaderPath);
        Assert.Empty(Directory.GetFiles(_fixture.At(R.JournalLeaf), "epoch-*"));
        Assert.Equal(withData, File.Exists(_fixture.RecordPath("foreign")));
        Assert.Equal(B.Status.RescanRequired, B.Acknowledge(proposal).State);
        Assert.Equal(B.Status.BaselineCreated, B.Acknowledge(Inspect()).State);
        Assert.Equal(H.Status.Healthy, Read().State);
        if (withData) Assert.Equal("{", File.ReadAllText(_fixture.RecordPath("foreign")));
    }

    [Theory]
    [InlineData("public-name")] [InlineData("reserved-bytes")] [InlineData("reserved-name")]
    [InlineData("head-bytes")] [InlineData("head-advance")]
    [InlineData("root-stamp")] [InlineData("journal-stamp")]
    public void StaleProposal_RefusesBeforeAnyHeaderPublication(string change)
    {
        if (!Native()) return;
        File.WriteAllText(_fixture.At(".spira-retained-old"), "a");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(".spira-retained-old"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _fixture.Baseline(F.ScopeA);
        B.Proposal old = Inspect();
        if (change == "public-name") File.WriteAllText(_fixture.At("new-public"), "keep");
        if (change == "reserved-bytes") File.WriteAllText(_fixture.At(".spira-retained-old"), "b");
        if (change == "reserved-name")
            File.Move(_fixture.At(".spira-retained-old"), _fixture.At(".spira-retained-renamed"));
        if (change == "head-bytes")
        {
            string head = _fixture.RecordPath(old.Observation.HeadSelection.Head!.Leaf);
            File.WriteAllText(head, " " + File.ReadAllText(head));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(head, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (change == "head-advance") _fixture.Baseline(F.ScopeB);
        if (change == "root-stamp" || change == "journal-stamp")
            Directory.SetLastWriteTimeUtc(change == "root-stamp" ? _fixture.RootPath : _fixture.At(R.JournalLeaf),
                new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = Files();
        Assert.NotEqual(old.Token, Inspect().Token);
        B.Outcome result = B.Acknowledge(old);
        Assert.Equal(B.Status.RescanRequired, result.State);
        Assert.Null(result.HeaderPath);
        SameFiles(before, Files());
    }

    [Fact]
    public void FreshRootDescriptor_RejectsProposalForReplacedPath()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        B.Proposal old = Inspect();
        string moved = _fixture.RootPath + "-original";
        Assert.False(Directory.Exists(moved));
        Directory.Move(_fixture.RootPath, moved);
        try
        {
            TestDirectory.CreatePrivate(_fixture.RootPath);
            Assert.Equal(B.Status.RescanRequired, B.Acknowledge(old).State);
            Assert.Empty(Directory.GetFileSystemEntries(_fixture.RootPath));
            Assert.Single(Directory.GetFiles(Path.Combine(moved, R.JournalLeaf)));
        }
        finally
        {
            Directory.Delete(_fixture.RootPath); // Only newly created, asserted-empty fixture directory.
            Directory.Move(moved, _fixture.RootPath);
        }
    }

    [Theory]
    [InlineData("binding")] [InlineData("backup-only")] [InlineData("bad-intent")]
    [InlineData("missing-retention")] [InlineData("changed-retention")] [InlineData("old-carry-change")]
    public void ExplicitBaseline_AcknowledgesUnhealthyHistoryWithoutReplayingOrResettingItsOccupancy(string kind)
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA, changedBinding: kind == "binding");
        F.Operation old = _fixture.AddOperation(kind: R.OperationKind.MagicSave, replace: true,
            executeSteps: kind == "backup-only" ? 1 : 2, complete: kind != "backup-only");
        if (kind == "bad-intent") File.WriteAllText(_fixture.RecordPath(old.IntentLeaf), "{");
        if (kind == "missing-retention") File.Delete(_fixture.At(old.Intent.First.RetentionLeaf!));
        if (kind == "changed-retention")
        {
            File.WriteAllText(_fixture.At(old.Intent.First.RetentionLeaf!), "changed");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(old.Intent.First.RetentionLeaf!), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (kind == "old-carry-change")
        {
            _fixture.Baseline(F.ScopeB);
            File.WriteAllText(_fixture.At(old.Intent.First.RetentionLeaf!), "changed");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(old.Intent.First.RetentionLeaf!), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Assert.NotEqual(H.Status.Healthy, Read().State);
        B.Proposal proposal = Inspect(); // Independent of the ordinary history-health gate.
        I.Head previous = proposal.Observation.HeadSelection.Head!;
        var before = Files();
        B.Outcome result = B.Acknowledge(proposal);
        Assert.Equal(B.Status.BaselineCreated, result.State);
        Assert.NotNull(result.HeaderPath);
        var after = Files();
        Assert.True(after.Remove(Path.GetRelativePath(_fixture.RootPath, result.HeaderPath!)));
        SameFiles(before, after); // Old bytes/names unchanged, even incomplete or malformed.
        H.Analysis reopened = Read();
        Assert.Equal(H.Status.Healthy, reopened.State);
        Assert.Equal(0, reopened.CompletedOperations);
        Assert.Equal((ulong)1, reopened.NextSequence);
        I.Head current = reopened.Inspection.HeadSelection.Head!;
        Assert.True(reopened.Inspection.HeadRootMatches);
        Assert.Equal(previous.Epoch.Ordinal + 1, current.Epoch.Ordinal);
        Assert.NotEqual(previous.Epoch.Scope, current.Epoch.Scope);
        Assert.Equal(previous.Source.Identity, current.Epoch.Previous!.Identity);
        Assert.Equal(previous.Source.Sha256, current.Epoch.Previous.Sha256);
        Assert.NotEqual(previous.Epoch.Root, current.Epoch.Previous.Identity); // Header inode, not root inode.
        Assert.Equal(F.Carry(proposal.Observation.Entries.ToArray()).Sha256, current.Epoch.CarryOverSha256);
        Assert.Equal(proposal.Observation.Digest.Bytes, current.Epoch.CarryOverBytes);
        Assert.Equal(proposal.Observation.Digest.Entries, current.Epoch.CarryOverEntries);
        Assert.True(reopened.Inspection.Usage.HistoryBytes > proposal.Observation.Usage.HistoryBytes);
        Assert.Equal(proposal.Observation.Usage.ManagedEntries + 1, reopened.Inspection.Usage.ManagedEntries);
        if (kind == "backup-only")
            Assert.Equal(new byte[] { 0x32 }, File.ReadAllBytes(_fixture.At(old.Intent.Second!.TargetLeaf)));
    }

    [Theory]
    [InlineData("malformed")] [InlineData("duplicate-ordinal")] [InlineData("duplicate-scope")]
    public void UnsafeStructuralHead_IsNotAnAcknowledgementEscape(string kind)
    {
        if (!Native()) return;
        R.Epoch first = _fixture.Baseline(F.ScopeA);
        I.Head old = Inspect().Observation.HeadSelection.Head!;
        if (kind == "malformed")
        {
            File.WriteAllText(_fixture.RecordPath("epoch-invalid.json"), "{");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath("epoch-invalid.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        else
        {
            R.Epoch extra = kind == "duplicate-ordinal" ? first with { Scope = F.ScopeB } :
                first with { Ordinal = 2, Previous = new(1, F.ScopeB, old.Source.Identity, old.Source.Sha256) };
            File.WriteAllBytes(_fixture.RecordPath(R.EpochLeaf(extra.Ordinal, extra.Scope)), R.Encode(extra));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(R.EpochLeaf(extra.Ordinal, extra.Scope)), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        B.Proposal proposal = Inspect();
        Assert.NotEqual(I.HeadIssue.None, proposal.Observation.HeadSelection.Issue);
        var before = Files();
        Assert.Equal(B.Status.InvalidHead, B.Acknowledge(proposal).State);
        Assert.Equal(B.Status.InvalidHead, Genesis().State);
        SameFiles(before, Files());
    }

    [Fact]
    public void MissingJournal_ReservesItsRootDirectorySlotBeforeCreatingIt()
    {
        if (!Native()) return;
        for (int n = 0; n < LinuxRecoveryBudget.MaximumDirectoryEntries; n++)
            File.WriteAllBytes(_fixture.At("user-" + n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)),
                Array.Empty<byte>());
        B.Proposal proposal = Inspect();
        Assert.Equal(LinuxRecoveryBudget.MaximumDirectoryEntries, proposal.Observation.Usage.RootEntries);
        Assert.Equal(B.Status.CapacityExceeded, B.Acknowledge(proposal).State);
        Assert.False(Directory.Exists(_fixture.At(R.JournalLeaf)));
        Assert.Equal(LinuxRecoveryBudget.MaximumDirectoryEntries, Directory.GetFiles(_fixture.RootPath).Length);
    }

    [Fact]
    public void OversizedOldHistory_RefusesWithoutAllocatingItsPayloadOrCreatingAHeader()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        _ = Inspect(); // Warm token/resources before measuring the sparse metadata path.
        string path = _fixture.At(".spira-retained-large");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            file.SetLength(LinuxRecoveryBudget.MaximumBytes + 1);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        B.Proposal proposal = Inspect();
        bool readPayload = false;
        D.BeforeOperationForTests = (op, p) => { if (op == "snapshot-open" && p == path) readPayload = true; };
        long before = GC.GetAllocatedBytesForCurrentThread();
        B.Outcome result;
        try { result = B.Acknowledge(proposal); }
        finally { D.BeforeOperationForTests = null; }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(B.Status.CapacityExceeded, result.State);
        Assert.False(readPayload);
        Assert.True(allocated < 1024 * 1024, "Metadata-only ACK allocated " + allocated);
        Assert.Single(Directory.GetFiles(_fixture.At(R.JournalLeaf)));
        Assert.Equal(LinuxRecoveryBudget.MaximumBytes + 1, new FileInfo(path).Length);
    }

    [Fact]
    public void ExhaustedOrdinal_IsRefusedWithoutWrappingOrWriting()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        B.Proposal before = Inspect();
        I.Head old = before.Observation.HeadSelection.Head!;
        var carry = F.Carry(before.Observation.Entries.ToArray());
        var last = old.Epoch with { Ordinal = ulong.MaxValue, Scope = F.ScopeB,
            Previous = new(old.Epoch.Ordinal, old.Epoch.Scope, old.Source.Identity, old.Source.Sha256),
            CarryOverSha256 = carry.Sha256, CarryOverBytes = carry.Bytes, CarryOverEntries = carry.Entries };
        File.WriteAllBytes(_fixture.RecordPath(R.EpochLeaf(last.Ordinal, last.Scope)), R.Encode(last));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.RecordPath(R.EpochLeaf(last.Ordinal, last.Scope)), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var files = Files();
        Assert.Equal(B.Status.OrdinalExhausted, B.Acknowledge(Inspect()).State);
        SameFiles(files, Files());
    }

    [Fact]
    public void ChangedObservationAfterConfirmation_IsRecapturedBeforePublishing()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        B.Proposal proposal = Inspect();
        int opens = 0;
        bool changed = false;
        D.BeforeOperationForTests = (op, path) =>
        {
            if (op != "child-opened" || path != _fixture.At(R.JournalLeaf) || ++opens != 2) return;
            changed = true;
            File.WriteAllText(_fixture.At("late-public"), "keep");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("late-public"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        };
        B.Outcome result;
        try { result = B.Acknowledge(proposal); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(changed);
        Assert.Equal(B.Status.RescanRequired, result.State);
        Assert.Null(result.HeaderPath);
        Assert.Single(Directory.GetFiles(_fixture.At(R.JournalLeaf)));
    }

    [Theory]
    [InlineData("linked")] [InlineData("after-return-snapshot")]
    [InlineData("after-return-inventory")] [InlineData("history-reopen")]
    [InlineData("foreign-addition")] [InlineData("root-stamp")] [InlineData("old-reserved-change")]
    public void FailureAfterPossibleHeaderPublication_PreservesLocationAndNeverClaimsAcknowledgement(string fault)
    {
        if (!Native()) return;
        File.WriteAllText(_fixture.At(".spira-retained-old"), "a");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(".spira-retained-old"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _fixture.Baseline(F.ScopeA);
        B.Proposal proposal = Inspect();
        string? published = null;
        int rootInventories = 0;
        bool injected = false;
        D.BeforeOperationForTests = (op, path) =>
        {
            if (op == "create-linked" && Path.GetFileName(path).StartsWith("epoch-", StringComparison.Ordinal))
            {
                published = path;
                if (fault == "linked") { injected = true; throw new IOException("injected linked fault"); }
                if (fault == "foreign-addition") { injected = true; File.WriteAllText(_fixture.RecordPath("foreign"), "keep"); }
                if (fault == "root-stamp")
                {
                    injected = true;
                    Directory.SetLastWriteTimeUtc(_fixture.RootPath,
                        new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                }
                if (fault == "old-reserved-change") { injected = true; File.WriteAllText(_fixture.At(".spira-retained-old"), "b"); }
            }
            if (published is null) return;
            if (fault == "after-return-snapshot" && op == "snapshot-open" && path == published)
            { injected = true; throw new IOException("injected after publication returned"); }
            if (op == "inventory-before-read" && path == _fixture.RootPath)
            {
                rootInventories++;
                if (fault == "after-return-inventory" && rootInventories == 1 ||
                    fault == "history-reopen" && rootInventories == 3)
                { injected = true; throw new IOException("injected later inventory failure"); }
            }
        };
        B.Outcome result;
        try { result = B.Acknowledge(proposal); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(injected);
        Assert.Equal(B.Status.RecoveryRequired, result.State);
        Assert.Equal(published, result.HeaderPath);
        Assert.NotNull(result.Detail);
        Assert.True(File.Exists(published));
        Assert.Equal(2, Directory.GetFiles(_fixture.At(R.JournalLeaf), "epoch-*").Length);
        Assert.True(File.Exists(_fixture.RecordPath(proposal.Observation.HeadSelection.Head!.Leaf)));
        if (fault == "foreign-addition") Assert.Equal("keep", File.ReadAllText(_fixture.RecordPath("foreign")));
        Assert.Equal(fault == "old-reserved-change" ? "b" : "a", File.ReadAllText(_fixture.At(".spira-retained-old")));
    }

    [Fact]
    public void FailureBeforeLink_DoesNotInventPossibleHeaderPublication()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        B.Proposal proposal = Inspect();
        var before = Files();
        bool injected = false;
        D.BeforeOperationForTests = (op, path) =>
        {
            if (op != "create-before-link" || !Path.GetFileName(path).StartsWith("epoch-", StringComparison.Ordinal)) return;
            injected = true;
            throw new IOException("injected before link");
        };
        try { Assert.ThrowsAny<IOException>(() => B.Acknowledge(proposal)); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(injected);
        SameFiles(before, Files());
    }

    [Fact]
    public void PublicBytesNotDisplayedByProposal_AreNotDeclaredUnchangedOrRestorable()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        string path = _fixture.At("user.bin");
        File.WriteAllText(path, "before");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        B.Proposal proposal = Inspect();
        File.WriteAllText(path, "after");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(proposal.Token, Inspect().Token); // Token binds public names, not their bytes.
        Assert.Equal(B.Status.BaselineCreated, B.Acknowledge(proposal).State);
        Assert.Equal("after", File.ReadAllText(path));
        Assert.Equal(0, Read().CompletedOperations); // No fabricated save/restore receipt.
    }

    [Fact]
    public void RepeatedAcknowledgements_CloseTheirOwnedDescriptorsWithoutDeletingHistory()
    {
        if (!Native()) return;
        _fixture.Baseline(F.ScopeA);
        Assert.Equal(B.Status.BaselineCreated, B.Acknowledge(Inspect()).State);
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
        for (int n = 0; n < 8; n++)
            Assert.Equal(B.Status.BaselineCreated, B.Acknowledge(Inspect()).State);
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
        Assert.Equal(10, Directory.GetFiles(_fixture.At(R.JournalLeaf)).Length);
        Assert.Equal(H.Status.Healthy, Read().State);
    }

    [Fact]
    public void CancelledAcknowledgement_DoesNotCreateJournalOrHeader()
    {
        if (!Native()) return;
        File.WriteAllText(_fixture.At("user.bin"), "keep");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("user.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        B.Proposal proposal = Inspect();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => B.Acknowledge(proposal, cancelled.Token));
        Assert.False(Directory.Exists(_fixture.At(R.JournalLeaf)));
        Assert.Equal("keep", File.ReadAllText(_fixture.At("user.bin")));
    }

    public void Dispose() => _fixture.Dispose();
}
