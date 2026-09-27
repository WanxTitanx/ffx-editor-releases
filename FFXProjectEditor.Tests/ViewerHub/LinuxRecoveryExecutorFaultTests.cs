// WHY: Once an intent or native linked artifact is possible, every later fault must preserve evidence.
// MAINT: Hooks mutate only GUID-private fixtures and are reset by IDisposable even after assertion failure.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using E = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryExecutor;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryExecutorFaultTests : IDisposable
{
    private readonly LinuxRecoveryHistoryFixture _fixture = new();
    private delegate D.Snapshot VerifyPublicationDelegate(D directory, string leaf,
        ReadOnlySpan<byte> bytes, D.Publication receipt, bool requirePrivate);

    public void Dispose()
    {
        E.BeforePhaseForTests = null;
        E.OperationIdForTests = null;
        D.BeforeOperationForTests = null;
        _fixture.Dispose();
    }

    [Fact]
    public void FailureAfterVerifiedIntent_PreservesIntentOnlyAndReopensIncomplete()
    {
        if (!Native()) return;
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "intent-verified") throw new IOException("fault after intent");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Single(IntentPaths());
        Assert.Empty(CompletionPaths());
        Assert.False(File.Exists(_fixture.At("copy.bin.bak")));
        Assert.False(File.Exists(_fixture.At("copy.bin")));
        AssertLocation(outcome, IntentPaths().Single(), E.LocationRole.Intent, E.LocationEvidence.LastVerified);
        E.BeforePhaseForTests = null;
        Assert.Equal(H.Status.IncompleteOperation, ReadHistory().State);
    }

    [Fact]
    public void FailureAfterStepZero_PreservesBackupOnlyWithoutRetryRollbackOrCompletion()
    {
        if (!Native()) return;
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "step-0-verified") throw new IOException("fault after backup");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1, 2 }, new byte[] { 9 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        Assert.False(File.Exists(_fixture.At("copy.bin")));
        Assert.Empty(CompletionPaths());
        AssertLocation(outcome, _fixture.At("copy.bin.bak"), E.LocationRole.Target,
            E.LocationEvidence.LastVerified);
        E.BeforePhaseForTests = null;
        Assert.Equal(H.Status.IncompleteOperation, ReadHistory().State);
    }

    [Theory]
    [InlineData("create-linked", null)]
    [InlineData("replace-linked", false)]
    [InlineData("replace-exchanged", true)]
    public void NativeLinkedFaults_ReportExactPossibleNamesAndExchangeState(string stage, bool? exchanged)
    {
        if (!Native()) return;
        byte[] oldTarget = { 1 };
        byte[] oldBackup = { 2 };
        if (stage != "create-linked")
        {
            Seed("copy.bin", oldTarget, mode0644: true);
            Seed("copy.bin.bak", oldBackup, mode0644: true);
            _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        }
        string target = _fixture.At("copy.bin.bak");
        string retention = string.Empty;
        D.BeforeOperationForTests = (observedStage, path) =>
        {
            bool selected = observedStage == stage && (stage switch
            {
                "create-linked" => path == target,
                "replace-linked" => Path.GetFileName(path).StartsWith(R.RetentionPrefix,
                    StringComparison.Ordinal),
                "replace-exchanged" => path == target,
                _ => false,
            });
            if (selected) throw new IOException("native linked fault");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 3 }, new byte[] { 4 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(exchanged, outcome.ExchangeReportedCompleted);
        AssertLocation(outcome, target, E.LocationRole.Target, E.LocationEvidence.Possible);
        if (stage == "create-linked")
        {
            Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(target));
            Assert.False(File.Exists(_fixture.At("copy.bin")));
        }
        else
        {
            retention = outcome.Locations.Single(location => location.Role == E.LocationRole.Retention &&
                location.Step == 0).Path;
            Assert.True(File.Exists(retention));
            AssertLocation(outcome, retention, E.LocationRole.Retention, E.LocationEvidence.Possible);
            Assert.Equal(stage == "replace-exchanged" ? oldTarget : oldBackup, File.ReadAllBytes(target));
            Assert.Equal(stage == "replace-exchanged" ? oldBackup : oldTarget, File.ReadAllBytes(retention));
        }
        Assert.Single(IntentPaths());
        Assert.Empty(CompletionPaths());
    }

    [Fact]
    public void SnapshotFailureAfterNativeReceipt_PreservesPublishedTargetAsPossible()
    {
        if (!Native()) return;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" && path == _fixture.At("copy.bin.bak"))
                throw new IOException("receipt reread fault");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        AssertLocation(outcome, _fixture.At("copy.bin.bak"), E.LocationRole.Target,
            E.LocationEvidence.Possible);
        Assert.Empty(CompletionPaths());

        D.BeforeOperationForTests = null;
        using var intentFixture = new LinuxRecoveryHistoryFixture();
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "snapshot-open" && path.EndsWith(".intent.json", StringComparison.Ordinal))
                throw new IOException("intent receipt reread fault");
        };
        E.Outcome intentOutcome = ExecuteChecked(intentFixture.RootPath, E.Command.Save(
            intentFixture.At("copy.bin"), null, null, new byte[] { 3 }, new byte[] { 4 }));
        string intentPath = Directory.GetFiles(intentFixture.At(R.JournalLeaf), "*.intent.json").Single();
        Assert.Equal(E.Status.RecoveryRequired, intentOutcome.State);
        AssertLocation(intentOutcome, intentPath, E.LocationRole.Intent, E.LocationEvidence.Possible);
    }

    [Fact]
    public void CompletionLinkedFault_IsUncertainEvenWhenFreshReopenCanProveHealthy()
    {
        if (!Native()) return;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "create-linked" && path.EndsWith(".complete.json", StringComparison.Ordinal))
                throw new IOException("completion linked fault");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Single(CompletionPaths());
        AssertLocation(outcome, CompletionPaths().Single(), E.LocationRole.Completion,
            E.LocationEvidence.Possible);
        D.BeforeOperationForTests = null;
        Assert.Equal(H.Status.Healthy, ReadHistory().State);
    }

    [Theory]
    [InlineData("completion-snapshot")]
    [InlineData("post-completion-inventory")]
    [InlineData("post-history-hook")]
    public void PostCompletionFailures_ReturnRecoveryWhileActualHistoryIsReadFresh(string variant)
    {
        if (!Native()) return;
        bool completionLinked = false;
        D.BeforeOperationForTests = (stage, path) =>
        {
            if (stage == "create-linked" && path.EndsWith(".complete.json", StringComparison.Ordinal))
                completionLinked = true;
            if (completionLinked && variant == "completion-snapshot" && stage == "snapshot-open" &&
                path.EndsWith(".complete.json", StringComparison.Ordinal))
                throw new IOException("completion snapshot fault");
            if (completionLinked && variant == "post-completion-inventory" &&
                stage == "inventory-before-read")
                throw new IOException("post-completion inventory fault");
        };
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (variant == "post-history-hook" && phase == "history-reopened")
                throw new IOException("post-history hook fault");
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Null(outcome.Receipt);
        Assert.Single(CompletionPaths());
        D.BeforeOperationForTests = null;
        E.BeforePhaseForTests = null;
        Assert.Equal(H.Status.Healthy, ReadHistory().State);
    }

    [Theory]
    [InlineData("before-intent")]
    [InlineData("after-intent")]
    public void CancellationBeforeVersusAfterIntent_MapsToCleanOrRecoveryState(string variant)
    {
        if (!Native()) return;
        using var cancellation = new CancellationTokenSource();
        if (variant == "before-intent") cancellation.Cancel();
        else E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "intent-verified") cancellation.Cancel();
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }), cancellation.Token);

        Assert.Equal(variant == "before-intent" ? E.Status.CancelledBeforeIntent : E.Status.RecoveryRequired,
            outcome.State);
        Assert.Equal(variant == "before-intent" ? 0 : 1, IntentPaths().Length);
        Assert.Empty(CompletionPaths());
        Assert.False(File.Exists(_fixture.At("copy.bin")));
    }

    [Theory]
    [InlineData("head-bytes")]
    [InlineData("history-metadata")]
    [InlineData("history-name")]
    [InlineData("root-directory")]
    [InlineData("journal-directory")]
    public void PrefixDriftAfterIntent_IsRefusedWithoutExecutingFirstPayload(string variant)
    {
        if (!Native()) return;
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        string head = HeadPath();
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase != "intent-verified") return;
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Linux recovery callback requires Linux.");
            if (variant == "head-bytes") File.WriteAllBytes(head, new byte[] { 0x7B });
            if (variant == "history-metadata")
                File.SetUnixFileMode(head, UnixFileMode.UserRead);
            if (variant == "history-name")
                File.WriteAllBytes(Path.Combine(_fixture.At(R.JournalLeaf), "foreign.bin"), new byte[] { 1 });
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(_fixture.At(R.JournalLeaf), "foreign.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (variant == "root-directory")
                Directory.SetLastWriteTimeUtc(_fixture.RootPath, DateTime.UtcNow.AddMinutes(-5));
            if (variant == "journal-directory")
                Directory.SetLastWriteTimeUtc(_fixture.At(R.JournalLeaf), DateTime.UtcNow.AddMinutes(-5));
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.False(File.Exists(_fixture.At("copy.bin.bak")));
        Assert.False(File.Exists(_fixture.At("copy.bin")));
        Assert.Empty(CompletionPaths());
    }

    [Fact]
    public void FutureTargetChangedAfterBackup_IsDetectedBeforeSecondStep()
    {
        if (!Native()) return;
        SeedExistingSave();
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "step-0-verified") File.WriteAllBytes(_fixture.At("copy.bin"), new byte[] { 0xEE });
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 0 }, new byte[] { 9 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        Assert.Equal(new byte[] { 0xEE }, File.ReadAllBytes(_fixture.At("copy.bin")));
        Assert.Empty(CompletionPaths());
    }

    [Theory]
    [InlineData("verified-target")]
    [InlineData("verified-retention")]
    public void FirstVerifiedTargetOrRetentionChangedBeforeSecondStep_IsDetected(string variant)
    {
        if (!Native()) return;
        SeedExistingSave();
        Guid operation = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        E.OperationIdForTests = operation;
        string retention = _fixture.At(R.RetainedLeaf(LinuxRecoveryHistoryFixture.ScopeA, operation, 0));
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase != "step-0-verified") return;
            File.WriteAllBytes(variant == "verified-target" ? _fixture.At("copy.bin.bak") : retention,
                new byte[] { 0xED });
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 0 }, new byte[] { 9 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(_fixture.At("copy.bin")));
        Assert.Empty(CompletionPaths());
    }

    [Fact]
    public void MissingCreateTargetAppearingAfterIntent_IsDetectedBeforePublication()
    {
        if (!Native()) return;
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "before-step-0") File.WriteAllBytes(_fixture.At("copy.bin.bak"), new byte[] { 0xAB });
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 1 }, new byte[] { 2 }));

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(new byte[] { 0xAB }, File.ReadAllBytes(_fixture.At("copy.bin.bak")));
        Assert.False(File.Exists(_fixture.At("copy.bin")));
        Assert.Empty(CompletionPaths());
    }

    [Fact]
    public void NativeExchangeCtimeAllowance_DoesNotPermitLaterCtimeOnlyDrift()
    {
        if (!Native()) return;
        SeedExistingSave();
        bool ctimeChanged = false;
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase != "step-0-verified") return;
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Linux recovery callback requires Linux.");
            string path = _fixture.At("copy.bin.bak");
            UnixFileMode finalMode = File.GetUnixFileMode(path);
            D.Snapshot before = Snapshot("copy.bin.bak");
            for (int attempt = 0; attempt < 100 && !ctimeChanged; attempt++)
            {
                File.SetUnixFileMode(path, finalMode | UnixFileMode.GroupRead);
                File.SetUnixFileMode(path, finalMode);
                D.Snapshot after = Snapshot("copy.bin.bak");
                ctimeChanged = before.Observation.File.ChangedSeconds != after.Observation.File.ChangedSeconds ||
                    before.Observation.File.ChangedNanoseconds != after.Observation.File.ChangedNanoseconds;
                if (!ctimeChanged) Thread.SpinWait(10_000);
            }
        };

        E.Outcome outcome = ExecuteChecked(Save(new byte[] { 0 }, new byte[] { 9 }));

        Assert.True(ctimeChanged);
        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Empty(CompletionPaths());
    }

    [Theory]
    [InlineData("save-current-target")]
    [InlineData("restore-read-dependency")]
    public void FinalLiveGate_RejectsCurrentPublicOrRestoreDependencyDriftAfterHealthyHistory(string variant)
    {
        if (!Native()) return;
        E.Command command;
        string mutated;
        if (variant == "save-current-target")
        {
            command = Save(new byte[] { 1 }, new byte[] { 2 });
            mutated = _fixture.At("copy.bin");
        }
        else
        {
            Seed("copy.bin", new byte[] { 1 }, mode0644: true);
            Seed("copy.bin.bak", new byte[] { 2 }, mode0644: true);
            D.Snapshot backup = Snapshot("copy.bin.bak");
            _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
            command = E.Command.RestoreLastSave(_fixture.At("copy.bin"), _fixture.At("copy.bin"), null, null,
                LinuxRecoveryHistoryFixture.Sha(new byte[] { 1 }), LinuxRecoveryHistoryFixture.Sha(new byte[] { 2 }),
                backup.Identity);
            mutated = _fixture.At("copy.bin.bak");
        }
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase == "history-reopened") File.WriteAllBytes(mutated, new byte[] { 0xFE });
        };

        E.Outcome outcome = ExecuteChecked(command);

        Assert.Equal(E.Status.RecoveryRequired, outcome.State);
        Assert.Equal(H.Status.Healthy, outcome.LastHistoryState);
        Assert.Null(outcome.Receipt);
        Assert.Equal(new byte[] { 0xFE }, File.ReadAllBytes(mutated));
        E.BeforePhaseForTests = null;
        Assert.Equal(H.Status.Healthy, ReadHistory().State);
    }

    [Fact]
    public void PublicationVerifier_RejectsForgedPlannedHashWhenFreshBytesDiffer()
    {
        if (!Native()) return;
        byte[] actualBytes = { 9 };
        byte[] plannedBytes = { 1 };
        Seed("artifact.bin", actualBytes);
        using D root = _fixture.Open();
        D.Snapshot actual = root.ReadSnapshot("artifact.bin")!;
        var forged = new D.Publication(_fixture.At("artifact.bin"), actual.Identity,
            LinuxRecoveryHistoryFixture.Sha(plannedBytes), plannedBytes.Length);
        MethodInfo method = typeof(E).GetMethod("VerifyPublication",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The security helper is absent.");
        var verify = (VerifyPublicationDelegate)method.CreateDelegate(typeof(VerifyPublicationDelegate));

        Assert.Throws<IOException>(() =>
            verify(root, "artifact.bin", plannedBytes, forged, requirePrivate: true));
    }

    private E.Command Save(byte[] original, byte[] working) =>
        E.Command.Save(_fixture.At("copy.bin"), null, null, original, working);

    private E.Outcome ExecuteChecked(E.Command command, CancellationToken cancellationToken = default)
        => ExecuteChecked(_fixture.RootPath, command, cancellationToken);

    private static E.Outcome ExecuteChecked(string rootPath, E.Command command,
        CancellationToken cancellationToken = default)
    {
        Assert.Empty(LinuxFixtureDescriptors.Capture(rootPath));
        try { return E.Execute(command, cancellationToken); }
        finally { Assert.Empty(LinuxFixtureDescriptors.Capture(rootPath)); }
    }

    private void SeedExistingSave()
    {
        Seed("copy.bin", new byte[] { 1 }, mode0644: true);
        Seed("copy.bin.bak", new byte[] { 2 }, mode0644: true);
        _fixture.Baseline(LinuxRecoveryHistoryFixture.ScopeA);
    }

    private void Seed(string leaf, byte[] bytes, bool mode0644 = false)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux recovery fixture requires Linux.");
        using D root = _fixture.Open();
        root.PublishNew(leaf, bytes);
        if (mode0644)
            File.SetUnixFileMode(_fixture.At(leaf), UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    private D.Snapshot Snapshot(string leaf)
    {
        using D root = _fixture.Open();
        return root.ReadSnapshot(leaf)!;
    }

    private H.Analysis ReadHistory()
    {
        using D root = _fixture.Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        return H.Read(root);
    }

    private string HeadPath() => Directory.GetFiles(_fixture.At(R.JournalLeaf), "epoch-*.json").Single();
    private string[] IntentPaths() => Files("*.intent.json");
    private string[] CompletionPaths() => Files("*.complete.json");
    private string[] Files(string pattern)
    {
        string journal = _fixture.At(R.JournalLeaf);
        return Directory.Exists(journal) ? Directory.GetFiles(journal, pattern) : Array.Empty<string>();
    }

    private static void AssertLocation(E.Outcome outcome, string path, E.LocationRole role,
        E.LocationEvidence evidence) => Assert.Contains(outcome.Locations, location =>
            location.Path == path && location.Role == role && location.Evidence == evidence);

    private static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(LinuxReadFileSystem.IsSupported);
        return true;
    }
}
