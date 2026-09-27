// WHY: The LIVE coordinator must derive and finish exact native operations under one root lease.
// MAINT: All files are private fixtures; no game, source tree, corpus, or user export is a target.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBaseline;
using Budget = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBudget;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using E = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryExecutor;
using F = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryExecutorTests : IDisposable
{
    private readonly LinuxRecoveryHistoryFixture _fixture = new();

    public void Dispose()
    {
        E.BeforePhaseForTests = null;
        E.OperationIdForTests = null;
        D.BeforeOperationForTests = null;
        _fixture.Dispose();
    }

    [Fact]
    public void EmptyGenesisSave_PublishesFreshPrivateReceiptsAndLeavesExternalSourceUnchanged()
    {
        if (!Native()) return;
        byte[] original = { 0x10, 0x11 };
        byte[] working = { 0x20, 0x21, 0x22 };
        string source = ExternalSource(original);
        try
        {
            E.Command command = E.Command.Save(_fixture.At("copy.bin"), source, null, original, working);
            original[0] = 0xEE;
            working[0] = 0xEF;

            E.Outcome outcome = ExecuteChecked(_fixture.RootPath, command);

            Assert.Equal(E.Status.Completed, outcome.State);
            Assert.Equal(H.Status.Healthy, outcome.LastHistoryState);
            Assert.NotNull(outcome.Receipt);
            R.Intent intent = outcome.Receipt!.Intent;
            R.Completion completion = outcome.Receipt.Completion;
            Assert.Equal(R.OperationKind.MagicSave, intent.Kind);
            Assert.Equal(1UL, intent.Sequence);
            Assert.Equal("copy.bin.bak", intent.First.TargetLeaf);
            Assert.Null(intent.First.Expected);
            Assert.Equal(LinuxRecoveryHistoryFixture.Sha(new byte[] { 0x10, 0x11 }), intent.First.PlannedSha256);
            Assert.Equal("copy.bin", intent.Second!.TargetLeaf);
            Assert.Null(intent.Second.Expected);
            Assert.Equal(LinuxRecoveryHistoryFixture.Sha(new byte[] { 0x20, 0x21, 0x22 }), intent.Second.PlannedSha256);
            Assert.Equal(new byte[] { 0x10, 0x11 }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
            Assert.Equal(new byte[] { 0x20, 0x21, 0x22 }, File.ReadAllBytes(_fixture.At("copy.bin")));
            Assert.Equal(new byte[] { 0x10, 0x11 }, File.ReadAllBytes(source));
            Assert.Equal(0x180, completion.First.Target.Stamp.Mode & 0xFFF);
            Assert.Equal(0x180, completion.Second!.Target.Stamp.Mode & 0xFFF);
            AssertPersistedReceipt(outcome.Receipt);
            using D root = _fixture.Open();
            using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
            H.Analysis history = H.Read(root);
            Assert.Equal(H.Status.Healthy, history.State);
            Assert.Equal(2UL, history.NextSequence);
            Assert.Equal(1, history.CompletedOperations);
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void ExistingSave_ReplacesBackupFirstWithExactOldTargetAndPreservesDisplaced0644()
    {
        if (!Native()) return;
        byte[] oldTarget = { 1, 2, 3 };
        byte[] oldBackup = { 4, 5 };
        byte[] working = { 9, 8, 7, 6 };
        Seed(_fixture.RootPath, "copy.bin", oldTarget, mode0644: true);
        Seed(_fixture.RootPath, "copy.bin.bak", oldBackup, mode0644: true);
        D.Snapshot targetBefore = Snapshot(_fixture.RootPath, "copy.bin");
        D.Snapshot backupBefore = Snapshot(_fixture.RootPath, "copy.bin.bak");
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        var order = new List<string>();
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "replace-before-link") order.Add(Path.GetFileName(path));
        };

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.Save(_fixture.At("copy.bin"), null, null, new byte[] { 0 }, working));

        Assert.Equal(new[] { "copy.bin.bak", "copy.bin" }, order);
        Assert.Equal(E.Status.Completed, outcome.State);
        Assert.Equal(oldTarget, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        Assert.Equal(working, File.ReadAllBytes(_fixture.At("copy.bin")));
        R.Intent intent = outcome.Receipt!.Intent;
        Assert.Equal(LinuxRecoveryHistoryFixture.Artifact(backupBefore), intent.First.Expected);
        Assert.Equal(LinuxRecoveryHistoryFixture.Artifact(targetBefore), intent.Second!.Expected);
        Assert.Equal(LinuxRecoveryHistoryFixture.Sha(oldTarget), intent.First.PlannedSha256);
        Assert.Equal(0x180, outcome.Receipt.Completion.First.Target.Stamp.Mode & 0xFFF);
        Assert.Equal(0x1A4, outcome.Receipt.Completion.First.Retained!.Stamp.Mode & 0xFFF);
        Assert.Equal(0x180, outcome.Receipt.Completion.Second!.Target.Stamp.Mode & 0xFFF);
        Assert.Equal(0x1A4, outcome.Receipt.Completion.Second.Retained!.Stamp.Mode & 0xFFF);
        Assert.Equal(oldBackup, File.ReadAllBytes(_fixture.At(intent.First.RetentionLeaf!)));
        Assert.Equal(oldTarget, File.ReadAllBytes(_fixture.At(intent.Second.RetentionLeaf!)));
        AssertPersistedReceipt(outcome.Receipt);
    }

    [Fact]
    public void FreshReopen_IgnoresDisappearedPriorPublicVersionWithoutRedundantAcknowledgement()
    {
        if (!Native()) return;
        E.Outcome first = ExecuteChecked(_fixture.RootPath,
            E.Command.Save(_fixture.At("copy.bin"), null, null, new byte[] { 1 }, new byte[] { 2 }));
        File.Delete(_fixture.At("copy.bin"));

        E.Outcome second = ExecuteChecked(_fixture.RootPath,
            E.Command.Save(_fixture.At("copy.bin"), null, null, new byte[] { 3 }, new byte[] { 4 }));

        Assert.Equal(E.Status.Completed, first.State);
        Assert.Equal(E.Status.Completed, second.State);
        Assert.Equal(2UL, second.Receipt!.Intent.Sequence);
        Assert.Single(Directory.EnumerateFiles(_fixture.At(R.JournalLeaf), "epoch-*.json"));
        using D root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        H.Analysis history = H.Read(root);
        Assert.Equal(H.Status.Healthy, history.State);
        Assert.Equal(3UL, history.NextSequence);
        Assert.Equal(2, history.CompletedOperations);
    }

    [Fact]
    public void ExistingOperationIdCollision_RefusesBeforeASecondIntent()
    {
        if (!Native()) return;
        E.Outcome first = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 239, 0, 1, 2, 3, null, null));
        Guid collision = first.Receipt!.Intent.Operation;
        int intentsBefore = IntentPaths(_fixture.RootPath).Length;
        E.OperationIdForTests = collision;

        E.Outcome second = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 240, 0, 1, 2, 3, null, null));

        Assert.Equal(E.Status.RefusedBeforeIntent, second.State);
        Assert.Null(second.Receipt);
        Assert.Equal(intentsBefore, IntentPaths(_fixture.RootPath).Length);
    }

    [Fact]
    public void RestoreSuccess_UsesFreshBackupProofAndKeepsBackupAsReadDependency()
    {
        if (!Native()) return;
        byte[] targetBytes = { 1, 2, 3 };
        byte[] backupBytes = { 7, 8, 9 };
        Seed(_fixture.RootPath, "copy.bin", targetBytes, mode0644: true);
        Seed(_fixture.RootPath, "copy.bin.bak", backupBytes, mode0644: true);
        D.Snapshot target = Snapshot(_fixture.RootPath, "copy.bin");
        D.Snapshot backup = Snapshot(_fixture.RootPath, "copy.bin.bak");
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath, E.Command.RestoreLastSave(
            _fixture.At("copy.bin"), _fixture.At("copy.bin"), null, null,
            LinuxRecoveryHistoryFixture.Sha(targetBytes), LinuxRecoveryHistoryFixture.Sha(backupBytes), backup.Identity));

        Assert.Equal(E.Status.Completed, outcome.State);
        Assert.Equal(backupBytes, File.ReadAllBytes(_fixture.At("copy.bin")));
        Assert.Equal(backupBytes, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        Assert.Equal(backup.Identity, Snapshot(_fixture.RootPath, "copy.bin.bak").Identity);
        Assert.Equal(LinuxRecoveryHistoryFixture.Artifact(target), outcome.Receipt!.Intent.First.Expected);
        Assert.Equal(targetBytes, File.ReadAllBytes(_fixture.At(outcome.Receipt.Intent.First.RetentionLeaf!)));
        Assert.Contains(outcome.Locations, location => location.Role == E.LocationRole.ReadDependency &&
            location.Path == _fixture.At("copy.bin.bak") && location.Evidence == E.LocationEvidence.LastVerified);
    }

    [Theory]
    [InlineData("target-sha")]
    [InlineData("backup-new-inode")]
    [InlineData("backup-same-inode-new-bytes")]
    [InlineData("backup-missing")]
    public void RestoreProofMismatch_RefusesBeforeIntentAndEarlyIdentityCasesSkipTargetSnapshot(string variant)
    {
        if (!Native()) return;
        byte[] targetBytes = { 1, 2, 3 };
        byte[] backupBytes = { 4, 5, 6 };
        Seed(_fixture.RootPath, "copy.bin", targetBytes, mode0644: true);
        Seed(_fixture.RootPath, "copy.bin.bak", backupBytes, mode0644: true);
        D.Snapshot backup = Snapshot(_fixture.RootPath, "copy.bin.bak");
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        string targetSha = variant == "target-sha" ? new string('0', 64) :
            LinuxRecoveryHistoryFixture.Sha(targetBytes);
        E.Command command = E.Command.RestoreLastSave(_fixture.At("copy.bin"), _fixture.At("copy.bin"),
            null, null, targetSha, LinuxRecoveryHistoryFixture.Sha(backupBytes), backup.Identity);
        if (variant == "backup-new-inode")
        {
            // Create while the proven inode is still named, then overwrite-rename. This prevents
            // immediate inode reuse from making a delete/create oracle nondeterministic.
            Seed(_fixture.RootPath, "replacement-backup.bin", backupBytes);
            File.Move(_fixture.At("replacement-backup.bin"), _fixture.At("copy.bin.bak"), overwrite: true);
            Assert.NotEqual(backup.Identity, Snapshot(_fixture.RootPath, "copy.bin.bak").Identity);
        }
        else if (variant == "backup-same-inode-new-bytes")
        {
            File.WriteAllBytes(_fixture.At("copy.bin.bak"), new byte[] { 9, 9, 9 });
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("copy.bin.bak"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        else if (variant == "backup-missing") File.Delete(_fixture.At("copy.bin.bak"));
        int targetSnapshots = 0;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" && path == _fixture.At("copy.bin")) targetSnapshots++;
        };

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath, command);

        Assert.Equal(E.Status.RefusedBeforeIntent, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Empty(IntentPaths(_fixture.RootPath));
        Assert.Equal(targetBytes, File.ReadAllBytes(_fixture.At("copy.bin")));
        if (variant is "backup-new-inode" or "backup-missing") Assert.Equal(0, targetSnapshots);
    }

    [Fact]
    public void SidecarMerge_PreservesUnknownFieldsAndOtherSlotsAndAdmitsExactFinalBytes()
    {
        if (!Native()) return;
        byte[] existing = System.Text.Encoding.UTF8.GetBytes(
            "{\"unknown\":{\"keep\":true},\"actors\":{\"0\":{\"heading\":4}}}");
        Seed(_fixture.RootPath, "239.json", existing, mode0644: true);
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        Budget.Usage usage;
        using (D before = _fixture.Open())
        using (IDisposable lease = before.AcquireWriteLock(TimeSpan.FromSeconds(5)))
            usage = H.Read(before).Inspection.Usage;

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 239, 5, 1, 2, 3, 0.5, 1.25));

        Assert.Equal(E.Status.Completed, outcome.State);
        byte[] finalBytes = File.ReadAllBytes(_fixture.At("239.json"));
        JsonNode root = JsonNode.Parse(finalBytes)!;
        Assert.True((bool)root["unknown"]!["keep"]!);
        Assert.Equal(4, (double)root["actors"]!["0"]!["heading"]!);
        Assert.Equal(1, (double)root["actors"]!["5"]!["position"]![0]!);
        R.Intent intent = outcome.Receipt!.Intent;
        Assert.Equal(finalBytes.Length, intent.First.PlannedLength);
        Assert.Equal(LinuxRecoveryHistoryFixture.Sha(finalBytes), intent.First.PlannedSha256);
        Budget.Reservation admitted = Budget.ForOperation(usage, intent);
        Assert.True(admitted.PeakBytes <= Budget.MaximumBytes);
    }

    [Fact]
    public async Task EightParallelWriters_OnOnePreexistingRootSerializeWithoutLostSlotsOrPerProcessRoots()
    {
        if (!Native()) return;
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        Task<E.Outcome>[] writes = Enumerable.Range(0, 8).Select(slot => Task.Run(() =>
            E.Execute(E.Command.EditSidecar(_fixture.RootPath, 239, slot,
                slot, slot + 0.25, slot + 0.5, null, null)))).ToArray();

        E.Outcome[] outcomes = await Task.WhenAll(writes);

        Assert.All(outcomes, outcome => Assert.Equal(E.Status.Completed, outcome.State));
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
        JsonNode json = JsonNode.Parse(File.ReadAllBytes(_fixture.At("239.json")))!;
        for (int slot = 0; slot < 8; slot++)
            Assert.Equal(slot, (double)json["actors"]![slot.ToString()]!["position"]![0]!);
        using D root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        H.Analysis history = H.Read(root);
        Assert.Equal(H.Status.Healthy, history.State);
        Assert.Equal(8, history.CompletedOperations);
        Assert.Equal(9UL, history.NextSequence);
        Assert.Single(Directory.EnumerateFiles(_fixture.At(R.JournalLeaf), "epoch-*.json"));
        Assert.DoesNotContain(Directory.EnumerateDirectories(_fixture.RootPath), path =>
            Path.GetFileName(path).Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("uninitialized-or-invalid")]
    [InlineData("binding")]
    [InlineData("carry")]
    [InlineData("incomplete")]
    [InlineData("orphan")]
    [InlineData("changed-retention")]
    public void UnhealthyHistory_RefusesBeforeIntentWithoutUsingOldRecordsAsAuthority(string variant)
    {
        if (!Native()) return;
        if (variant == "uninitialized-or-invalid")
        {
            Seed(_fixture.RootPath, "occupied.bin", new byte[] { 1 });
            E.Outcome nonempty = ExecuteChecked(_fixture.RootPath,
                E.Command.EditSidecar(_fixture.RootPath, 239, 0, 1, 2, 3, null, null));
            Assert.Equal(E.Status.NeedsInspection, nonempty.State);
            Assert.Equal(H.Status.NonemptyNeedsExplicitBaseline, nonempty.LastHistoryState);
            Assert.Empty(IntentPaths(_fixture.RootPath));

            using var invalid = new LinuxRecoveryHistoryFixture();
            using (D root = invalid.Open()) using (D journal = root.OpenOrCreateChild(R.JournalLeaf))
                journal.PublishNew("epoch-1-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json", new byte[] { 0x7B });
            E.Outcome malformed = ExecuteChecked(invalid.RootPath,
                E.Command.EditSidecar(invalid.RootPath, 239, 0, 1, 2, 3, null, null));
            Assert.Equal(E.Status.NeedsInspection, malformed.State);
            Assert.Equal(H.Status.InvalidHead, malformed.LastHistoryState);
            Assert.Empty(IntentPaths(invalid.RootPath));

            using var maxSequence = new LinuxRecoveryHistoryFixture();
            maxSequence.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
            R.Intent maxIntent = LinuxRecoveryRecordsTests.Intent() with
            {
                Scope = LinuxRecoveryHistoryFixture.ScopeA,
                Sequence = ulong.MaxValue,
                Operation = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            };
            using (D root = maxSequence.Open()) using (D journal = root.OpenExistingChild(R.JournalLeaf))
                journal.PublishNew(R.IntentLeaf(maxIntent.Scope, maxIntent.Sequence, maxIntent.Operation),
                    R.Encode(maxIntent));
            E.Outcome maxRefusal = ExecuteChecked(maxSequence.RootPath,
                E.Command.EditSidecar(maxSequence.RootPath, 239, 0, 1, 2, 3, null, null));
            Assert.Equal(E.Status.NeedsInspection, maxRefusal.State);
            Assert.Equal(H.Status.SequenceConflict, maxRefusal.LastHistoryState);
            Assert.Single(IntentPaths(maxSequence.RootPath));
            return;
        }

        if (variant == "binding") _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA, changedBinding: true);
        else
        {
            _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
            if (variant == "carry") SeedJournal(_fixture.RootPath, "foreign-carry.bin", new byte[] { 1 });
            if (variant == "incomplete") _fixture.AddOperation(complete: false);
            if (variant == "orphan")
            {
                LinuxRecoveryHistoryFixture.Operation operation = _fixture.AddOperation();
                File.Delete(_fixture.RecordPath(operation.IntentLeaf));
            }
            if (variant == "changed-retention")
            {
                LinuxRecoveryHistoryFixture.Operation operation = _fixture.AddOperation(replace: true);
                File.WriteAllBytes(_fixture.At(operation.Intent.First.RetentionLeaf!), new byte[] { 0xFF });
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At(operation.Intent.First.RetentionLeaf!), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        H.Status expected = variant switch
        {
            "binding" => H.Status.RootBindingChanged,
            "carry" => H.Status.CarryOverChanged,
            "incomplete" => H.Status.IncompleteOperation,
            "orphan" => H.Status.OrphanCompletion,
            "changed-retention" => H.Status.RetainedArtifactChanged,
            _ => throw new InvalidOperationException(variant),
        };
        int before = IntentPaths(_fixture.RootPath).Length;

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 239, 0, 1, 2, 3, null, null));

        Assert.Equal(E.Status.NeedsInspection, outcome.State);
        Assert.Equal(expected, outcome.LastHistoryState);
        Assert.Null(outcome.Receipt);
        Assert.Equal(before, IntentPaths(_fixture.RootPath).Length);
    }

    [Fact]
    public void ExplicitAcknowledgement_IsSeparateNeedsFreshTokenAndNeverRetriesPendingCommand()
    {
        if (!Native()) return;
        byte[] occupied = { 0x41 };
        Seed(_fixture.RootPath, "occupied.bin", occupied);
        E.Command pending = E.Command.EditSidecar(_fixture.RootPath, 239, 0, 1, 2, 3, null, null);

        E.Outcome refused = ExecuteChecked(_fixture.RootPath, pending);
        B.Proposal absentJournal = B.Inspect(_fixture.RootPath);
        B.Outcome setup = B.Acknowledge(absentJournal);
        B.Proposal presentJournal = B.Inspect(_fixture.RootPath);
        B.Outcome accepted = B.Acknowledge(presentJournal);

        Assert.Equal(E.Status.NeedsInspection, refused.State);
        Assert.Equal(B.Status.RescanRequired, setup.State);
        Assert.Equal(B.Status.BaselineCreated, accepted.State);
        Assert.Equal(occupied, File.ReadAllBytes(_fixture.At("occupied.bin")));
        Assert.False(File.Exists(_fixture.At("239.json")));
        Assert.Empty(IntentPaths(_fixture.RootPath));
        using (D root = _fixture.Open())
        using (IDisposable lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5)))
            Assert.Equal(H.Status.Healthy, H.Read(root).State);
        Assert.Empty(LinuxFixtureDescriptors.Capture(_fixture.RootPath));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("managed")]
    [InlineData("root")]
    [InlineData("journal-combined")]
    public void MetadataFloorCapacityRefusal_HappensBeforeTargetSnapshotOrIntent(string dimension)
    {
        if (!Native()) return;
        PrepareCapacityFixture(dimension);
        int targetSnapshots = 0;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" &&
                (path == _fixture.At("copy.bin") || path == _fixture.At("copy.bin.bak")))
                targetSnapshots++;
        };

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.Save(_fixture.At("copy.bin"), null, null, new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.CapacityExceeded, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Equal(0, targetSnapshots);
        Assert.Empty(IntentPaths(_fixture.RootPath));
        if (dimension == "bytes")
        {
            Assert.Equal(new byte[] { 0x31 }, File.ReadAllBytes(_fixture.At("copy.bin")));
            Assert.Equal(new byte[] { 0x32 }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        }
        else
        {
            Assert.False(File.Exists(_fixture.At("copy.bin")));
            Assert.False(File.Exists(_fixture.At("copy.bin.bak")));
        }
    }

    [Fact]
    public void SidecarExactCapacityRefusal_AfterMetadataFloorFits_PreservesOriginalWithoutIntent()
    {
        if (!Native()) return;
        byte[] original = System.Text.Encoding.UTF8.GetBytes(
            "{\"unknown\":\"" + new string('x', 32 * 1024) + "\",\"actors\":{}}");
        Seed(_fixture.RootPath, "239.json", original);
        long carryoverLength = checked(Budget.MaximumBytes - original.Length -
            4L * R.MaximumRecordBytes - 1024);
        Assert.True(carryoverLength > 0);
        string sparsePath;
        using (D root = _fixture.Open())
        using (var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5)))
        using (D journal = root.OpenOrCreateChild(R.JournalLeaf))
        {
            sparsePath = Path.Combine(journal.FullPath, "old-sidecar-sparse.bin");
            using (var stream = new FileStream(sparsePath,
                FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(carryoverLength); // Set once BEFORE baseline; no payload allocation/read.
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(sparsePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);

        // Independent admission witness: no executor hook or synthetic healthy DTO prepares this case.
        H.Analysis history;
        using (D root = _fixture.Open())
        using (var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5)))
            history = H.Read(root);
        Assert.Equal(H.Status.Healthy, history.State);
        I.Head head = Assert.IsType<I.Head>(history.Inspection.HeadSelection.Head);
        Assert.InRange(head.Source.Length, 1L, (long)R.MaximumRecordBytes);
        Budget.Usage usage = history.Inspection.Usage;
        Assert.Equal(checked(carryoverLength + head.Source.Length), usage.HistoryBytes);
        long floorPeak = checked(usage.HistoryBytes + original.Length + 3L * R.MaximumRecordBytes);
        byte[] planned = LinuxSidecarJson.Prepare(original, 5, 1, 2, 3, 0.5, 1.25);
        Assert.InRange(planned.Length, 32 * 1024, R.MaximumPayloadBytes);
        Assert.InRange(floorPeak, 0L, Budget.MaximumBytes);
        Assert.True(checked(floorPeak + planned.Length) > Budget.MaximumBytes);
        Assert.True(checked(usage.ManagedEntries + 4) <= Budget.MaximumEntries);
        Assert.True(checked(usage.RootEntries + 1) <= Budget.MaximumDirectoryEntries);
        Assert.True(checked(usage.JournalEntries + 3) <= Budget.MaximumDirectoryEntries);

        int targetSnapshots = 0;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" && path == _fixture.At("239.json")) targetSnapshots++;
        };

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 239, 5, 1, 2, 3, 0.5, 1.25));

        Assert.Equal(H.Status.Healthy, outcome.LastHistoryState);
        Assert.Equal(E.Status.CapacityExceeded, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Equal(1, targetSnapshots); // Real floor passed; exact merged-byte admission refused.
        Assert.Empty(IntentPaths(_fixture.RootPath));
        Assert.Empty(Directory.GetFiles(_fixture.At(R.JournalLeaf), "*.complete.json"));
        Assert.Equal(original, File.ReadAllBytes(_fixture.At("239.json")));
    }

    [Theory]
    [InlineData("copy.bin")]
    [InlineData("copy.bin.bak")]
    public void OversizedExistingPayload_RefusesFromMetadataBeforeAnyTargetSnapshot(string oversizedLeaf)
    {
        if (!Native()) return;
        long length = (long)R.MaximumPayloadBytes + 1;
        using (var stream = new FileStream(_fixture.At(oversizedLeaf), FileMode.CreateNew,
            FileAccess.Write, FileShare.None))
            stream.SetLength(length); // Sparse private fixture; never allocate/read the oversized payload.
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        int snapshots = 0;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" &&
                (path == _fixture.At("copy.bin") || path == _fixture.At("copy.bin.bak")))
                snapshots++;
        };

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.Save(_fixture.At("copy.bin"), null, null, new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RefusedBeforeIntent, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Equal(0, snapshots);
        Assert.Empty(IntentPaths(_fixture.RootPath));
        Assert.Equal(length, new FileInfo(_fixture.At(oversizedLeaf)).Length);
    }

    [Fact]
    public void DeclaredRootAndJournalMetadataChanges_AreAdmittedWithoutWeakeningIdentityModeOrOwner()
    {
        if (!Native()) return;
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        O rootBefore;
        O journalBefore;
        using (D root = _fixture.Open())
        {
            rootBefore = root.ReadDirectoryMetadata();
            using D journal = root.OpenExistingChild(R.JournalLeaf);
            journalBefore = journal.ReadDirectoryMetadata();
        }

        E.Outcome outcome = ExecuteChecked(_fixture.RootPath,
            E.Command.EditSidecar(_fixture.RootPath, 239, 0, 1, 2, 3, null, null));

        Assert.Equal(E.Status.Completed, outcome.State);
        using D reopened = _fixture.Open();
        O rootAfter = reopened.ReadDirectoryMetadata();
        using D reopenedJournal = reopened.OpenExistingChild(R.JournalLeaf);
        O journalAfter = reopenedJournal.ReadDirectoryMetadata();
        Assert.Equal(Invariant(rootBefore), Invariant(rootAfter));
        Assert.Equal(Invariant(journalBefore), Invariant(journalAfter));
    }

    private E.Outcome ExecuteChecked(string rootPath, E.Command command,
        CancellationToken cancellationToken = default)
    {
        Assert.Empty(LinuxFixtureDescriptors.Capture(rootPath));
        try { return E.Execute(command, cancellationToken); }
        finally { Assert.Empty(LinuxFixtureDescriptors.Capture(rootPath)); }
    }

    private void AssertPersistedReceipt(E.CompletedOperation receipt)
    {
        using D root = _fixture.Open();
        using D journal = root.OpenExistingChild(R.JournalLeaf);
        string intentLeaf = R.IntentLeaf(receipt.Intent.Scope, receipt.Intent.Sequence, receipt.Intent.Operation);
        string completionLeaf = R.CompletionLeaf(receipt.Intent.Scope, receipt.Intent.Sequence, receipt.Intent.Operation);
        D.Snapshot intent = journal.ReadSnapshot(intentLeaf, R.MaximumRecordBytes)!;
        D.Snapshot completion = journal.ReadSnapshot(completionLeaf, R.MaximumRecordBytes)!;
        Assert.Equal(receipt.Intent, R.ReadIntent(intentLeaf, intent.Bytes));
        Assert.Equal(receipt.Completion, R.ReadCompletion(completionLeaf, completion.Bytes));
        R.ValidateCompletion(intentLeaf, intent.Bytes, receipt.Completion);
        Assert.Equal(0x180, intent.Observation.Mode & 0xFFF);
        Assert.Equal(0x180, completion.Observation.Mode & 0xFFF);
    }

    private void PrepareCapacityFixture(string dimension)
    {
        if (dimension == "bytes")
        {
            // Existing bounded targets make a missing floor guard observable before the exact guard.
            Seed(_fixture.RootPath, "copy.bin", new byte[] { 0x31 });
            Seed(_fixture.RootPath, "copy.bin.bak", new byte[] { 0x32 });
        }
        if (dimension == "root")
        {
            for (int index = 0; index < Budget.MaximumDirectoryEntries - 1; index++)
            {
                File.WriteAllBytes(_fixture.At("public-" + index.ToString("D4") + ".bin"), Array.Empty<byte>());
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_fixture.At("public-" + index.ToString("D4") + ".bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        else
        {
            using D root = _fixture.Open();
            using D journal = root.OpenOrCreateChild(R.JournalLeaf);
            if (dimension == "bytes")
            {
                string path = Path.Combine(journal.FullPath, "old-sparse.bin");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.SetLength(Budget.MaximumBytes - 16 * 1024);
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            else
            {
                int entries = dimension == "managed" ? 2044 : 2045;
                for (int index = 0; index < entries; index++)
                {
                    string p2 = Path.Combine(journal.FullPath,
                        "old-" + index.ToString("D4") + ".bin");
                    File.WriteAllBytes(p2, Array.Empty<byte>());
                    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(p2, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
    }

    private static (object Identity, ushort Mode, uint Owner, uint Links) Invariant(O value) =>
        (value.File.Identity, value.Mode, value.OwnerId, value.LinkCount);

    private string ExternalSource(byte[] bytes)
    {
        string path = Path.Combine(Path.GetDirectoryName(_fixture.RootPath)!,
            "linux-recovery-source-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, bytes);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    private static void Seed(string rootPath, string leaf, byte[] bytes, bool mode0644 = false)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux recovery fixture requires Linux.");
        using D root = D.OpenExisting(rootPath);
        root.PublishNew(leaf, bytes);
        if (mode0644)
            File.SetUnixFileMode(Path.Combine(rootPath, leaf), UnixFileMode.UserRead |
                UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    private static void SeedJournal(string rootPath, string leaf, byte[] bytes)
    {
        using D root = D.OpenExisting(rootPath);
        using D journal = root.OpenExistingChild(R.JournalLeaf);
        journal.PublishNew(leaf, bytes);
    }

    private static D.Snapshot Snapshot(string rootPath, string leaf)
    {
        using D root = D.OpenExisting(rootPath);
        return root.ReadSnapshot(leaf, R.MaximumPayloadBytes)!;
    }

    private static string[] IntentPaths(string rootPath)
    {
        string journal = Path.Combine(rootPath, R.JournalLeaf);
        return Directory.Exists(journal)
            ? Directory.GetFiles(journal, "*.intent.json", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    private static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(LinuxReadFileSystem.IsSupported);
        return true;
    }
}
