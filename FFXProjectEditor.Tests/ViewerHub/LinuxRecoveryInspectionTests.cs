// WHY: Structural recovery inspection must consume real retained Linux capabilities without writes.
// MAINT: Headers are structurally valid synthetic controls, not a claim of healthy active history.
// All fixtures are private and caller-owned; Linux-only bodies are not Windows runtime evidence.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(RepoWorkDir(),
        "linux-recovery-inspect-" + Guid.NewGuid().ToString("N"));
    public LinuxRecoveryInspectionTests() => TestDirectory.CreatePrivate(_root);
    private string At(string name) => Path.Combine(_root, name);
    private static readonly Guid ScopeA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ScopeB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string RetainedA = ".spira-retained-a.bin";
    private const string RetainedB = ".spira-retained-b.bin";

    private string Journal()
    {
        string path = At(R.JournalLeaf);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        TestDirectory.CreatePrivate(path);
        return path;
    }

    private I.Snapshot Capture()
    {
        using var root = D.OpenExisting(_root);
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        return I.Capture(root);
    }

    private R.Identity RootIdentity()
    {
        using var root = D.OpenExisting(_root);
        var value = root.ReadDirectoryMetadata().File.Identity;
        return new("Linux", ((ulong)value.DeviceMajor << 32) | value.DeviceMinor,
            value.Inode, value.MountId);
    }

    private R.Epoch Epoch(ulong ordinal = 1, Guid? scope = null, R.Identity? binding = null,
        R.PreviousHead? previous = null) => new(
        R.FormatVersion, binding ?? RootIdentity(), ordinal, scope ?? ScopeA, previous,
        new string('A', 64), 0, 0, // Structural-only fixture: not a healthy carry-over claim.
        new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));

    private string WriteHeader(R.Epoch value)
    {
        string path = Path.Combine(Journal(), R.EpochLeaf(value.Ordinal, value.Scope));
        File.WriteAllBytes(path, R.Encode(value));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    private static R.PreviousHead Previous(I.Head head) =>
        new(head.Epoch.Ordinal, head.Epoch.Scope, head.Source.Identity, head.Source.Sha256);
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


    [Fact]
    public void MissingJournal_StaysMissingAndNoHeadGrantsNoWritePermission()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        var before = Directory.GetFileSystemEntries(_root);
        I.Snapshot snapshot = Capture();
        Assert.Null(snapshot.Journal);
        Assert.True(snapshot.ObservedEmptyRoot);
        Assert.Null(snapshot.HeadSelection.Head);
        Assert.Equal(I.HeadIssue.None, snapshot.HeadSelection.Issue);
        Assert.Null(snapshot.HeadRootMatches);
        Assert.Equal(0, snapshot.Usage.HistoryBytes);
        Assert.Equal(before, Directory.GetFileSystemEntries(_root));
        Assert.False(Directory.Exists(At(R.JournalLeaf)));
        Assert.DoesNotContain(typeof(I.Snapshot).GetProperties(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public), property =>
                property.Name is "Ready" or "CanWrite" or "CanAcknowledge");
    }

    [Fact]
    public void PublicNames_CountWithoutFollowingLinksOrHashingTheirContents()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        TestDirectory.CreatePrivate(At("public-directory"));
        File.WriteAllBytes(At("export.bin"), new byte[] { 0x11 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At("export.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.CreateSymbolicLink(At("public-link"), At("export.bin"));
        I.Snapshot snapshot = Capture();
        Assert.False(snapshot.ObservedEmptyRoot);
        Assert.Equal(3, snapshot.Usage.RootEntries);
        Assert.Equal(0, snapshot.Usage.ManagedEntries);
        Assert.Equal(new[] { "export.bin", "public-directory", "public-link" }, snapshot.RootNames);
        Assert.Equal(new byte[] { 0x11 }, File.ReadAllBytes(At("export.bin")));
        Assert.NotNull(new FileInfo(At("public-link")).LinkTarget);
        Assert.False(Directory.Exists(At(R.JournalLeaf)));
    }

    [Fact]
    public void UnknownAndZeroByteHistory_CountEvenWhenBodiesAreNotRecords()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        File.WriteAllBytes(At(".SPIRA-RETAINED-unknown"), new byte[] { 1, 2, 3 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At(".SPIRA-RETAINED-unknown"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string journal = Journal();
        File.WriteAllText(Path.Combine(journal, "old-malformed.intent.json"), "{");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(journal, "old-malformed.intent.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllBytes(Path.Combine(journal, "zero"), Array.Empty<byte>());
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(journal, "zero"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        I.Snapshot snapshot = Capture();
        Assert.Equal(4, snapshot.Usage.HistoryBytes);
        Assert.Equal(3, snapshot.Usage.ManagedEntries);
        Assert.Equal(2, snapshot.Usage.RootEntries);
        Assert.Equal(2, snapshot.Usage.JournalEntries);
        Assert.Equal(I.HeadIssue.None, snapshot.HeadSelection.Issue);
        Assert.Null(snapshot.HeadSelection.Head);
        Assert.False(snapshot.ObservedEmptyRoot);
        Assert.Equal("{", File.ReadAllText(Path.Combine(journal, "old-malformed.intent.json")));
        Assert.Equal(0, new FileInfo(Path.Combine(journal, "zero")).Length);
    }

    [Fact]
    public void EmptyPrivateJournal_IsDistinctFromMissingAndDoesNotCreateGenesis()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        Journal();
        I.Snapshot snapshot = Capture();
        Assert.NotNull(snapshot.Journal);
        Assert.True(snapshot.ObservedEmptyRoot);
        Assert.Equal(1, snapshot.Usage.RootEntries);
        Assert.Empty(snapshot.JournalNames);
        Assert.Empty(Directory.GetFiles(At(R.JournalLeaf)));
    }

    [Fact]
    public void HighestHead_IsSelectedAcrossBindingsAndUsesCurrentHeaderIdentity()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        WriteHeader(Epoch());
        I.Head first = Capture().HeadSelection.Head!;
        R.Identity different = RootIdentity() with { Mount = RootIdentity().Mount + 1 };
        string path = WriteHeader(Epoch(2, ScopeB, different, Previous(first)));
        byte[] bytes = File.ReadAllBytes(path);
        I.Snapshot snapshot = Capture();
        I.Head highest = Assert.IsType<I.Head>(snapshot.HeadSelection.Head);
        Assert.Equal((ulong)2, highest.Epoch.Ordinal);
        Assert.Equal(ScopeB, highest.Epoch.Scope);
        Assert.False(snapshot.HeadRootMatches);
        Assert.NotEqual(highest.Epoch.Root, highest.Source.Identity);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), highest.Source.Sha256);
        Assert.Equal(bytes.Length, highest.Source.Length);
        using var root = D.OpenExisting(_root);
        using var journal = root.OpenExistingChild(R.JournalLeaf);
        var actual = journal.ReadSnapshot(Path.GetFileName(path), 8192)!.Observation;
        Assert.Equal(actual.File.Identity.Inode, highest.Source.Identity.Inode);
        Assert.Equal(actual.File.Identity.MountId, highest.Source.Identity.Mount);
        Assert.Equal(actual.Mode, highest.Source.Stamp.Mode);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DuplicateOrdinalOrScope_BlocksSelection(bool sameScope)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        WriteHeader(Epoch());
        R.PreviousHead? previous = sameScope
            ? new(1, ScopeB, RootIdentity(), new string('A', 64)) : null;
        WriteHeader(Epoch(sameScope ? 2UL : 1UL, sameScope ? ScopeA : ScopeB,
            previous: previous));
        I.Snapshot snapshot = Capture();
        Assert.Null(snapshot.HeadSelection.Head);
        Assert.Equal(sameScope ? I.HeadIssue.DuplicateScope : I.HeadIssue.DuplicateOrdinal,
            snapshot.HeadSelection.Issue);
        Assert.Equal(2, snapshot.Usage.ManagedEntries);
    }

    [Fact]
    public void MissingPredecessor_DoesNotRequireRecursiveHistoricalChain()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        // Context is deliberately absent on disk. This does not manufacture a completed operation.
        var previous = new R.PreviousHead(1, ScopeB, RootIdentity(), new string('A', 64));
        WriteHeader(Epoch(2, ScopeA, previous: previous));
        I.Snapshot snapshot = Capture();
        Assert.Equal(I.HeadIssue.None, snapshot.HeadSelection.Issue);
        Assert.Equal((ulong)2, snapshot.HeadSelection.Head!.Epoch.Ordinal);
        Assert.True(snapshot.HeadRootMatches);
        Assert.Single(snapshot.JournalNames);
    }

    [Theory]
    [InlineData("json")] [InlineData("name")] [InlineData("uppercase")]
    [InlineData("empty")] [InlineData("oversized")]
    public void InvalidHeader_DoesNotHideBehindAnotherValidCandidate(string kind)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        WriteHeader(Epoch());
        R.Epoch other = Epoch(1, ScopeB);
        string name = R.EpochLeaf(other.Ordinal, other.Scope);
        byte[] content = R.Encode(other);
        if (kind == "json") content = new byte[] { (byte)'{' };
        if (kind == "name") name = "epoch-not-canonical.json";
        if (kind == "uppercase") name = name.ToUpperInvariant();
        if (kind == "empty") content = Array.Empty<byte>();
        if (kind == "oversized") content = new byte[8193];
        File.WriteAllBytes(Path.Combine(Journal(), name), content);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(Journal(), name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        I.Snapshot snapshot = Capture();
        Assert.Equal(I.HeadIssue.InvalidHeader, snapshot.HeadSelection.Issue);
        Assert.Null(snapshot.HeadSelection.Head);
        Assert.Equal(2, snapshot.Usage.ManagedEntries);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(Journal(), name)));
    }

    [Theory]
    [InlineData("root-link")] [InlineData("journal-link")] [InlineData("retained-directory")]
    [InlineData("journal-file")] [InlineData("nonprivate-journal")]
    [InlineData("hardlink")] [InlineData("journal-alias")]
    [InlineData("journal-prefix")] [InlineData("journal-prefix-sibling")]
    public void UnsafeReservedObjects_RefuseWithoutRepair(string kind)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        string sentinel = At("sentinel");
        File.WriteAllBytes(sentinel, new byte[] { 0x99 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(sentinel, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string? observedModePath = null;
        string? aliasSentinel = null;
        if (kind == "root-link") File.CreateSymbolicLink(At(RetainedA), sentinel);
        if (kind == "journal-link") File.CreateSymbolicLink(Path.Combine(Journal(), "entry"), sentinel);
        if (kind == "retained-directory") TestDirectory.CreatePrivate(At(RetainedA));
        if (kind == "journal-file") File.WriteAllBytes(At(R.JournalLeaf), new byte[] { 7 });
        if (kind == "nonprivate-journal")
        {
            observedModePath = At(R.JournalLeaf);
            Directory.CreateDirectory(observedModePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            // Set ONLY this private test fixture: the runner umask077 would strip group bits
            // during mkdir, so explicitly construct the unsafe input without repairing anything.
            File.SetUnixFileMode(observedModePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        }
        if (kind == "hardlink") Assert.Equal(0, Link(sentinel, At(RetainedA)));
        if (kind == "journal-alias") TestDirectory.CreatePrivate(At(".Spira-Recovery"));
        if (kind is "journal-prefix" or "journal-prefix-sibling")
        {
            observedModePath = At(".spira-recovery-extra");
            TestDirectory.CreatePrivate(observedModePath);
            aliasSentinel = Path.Combine(observedModePath, "sentinel");
            File.WriteAllBytes(aliasSentinel, new byte[] { 0xAC });
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(aliasSentinel, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (kind == "journal-prefix-sibling") Journal();
        }
        UnixFileMode? mode = observedModePath == null ? null : File.GetUnixFileMode(observedModePath);
        string[] beforeNames = Directory.GetFileSystemEntries(_root).OrderBy(x => x).ToArray();
        Assert.ThrowsAny<IOException>(() => Capture());
        Assert.Equal(new byte[] { 0x99 }, File.ReadAllBytes(sentinel));
        Assert.Equal(beforeNames, Directory.GetFileSystemEntries(_root).OrderBy(x => x).ToArray());
        if (mode != null) Assert.Equal(mode.Value, File.GetUnixFileMode(observedModePath!));
        if (aliasSentinel != null)
            Assert.Equal(new byte[] { 0xAC }, File.ReadAllBytes(aliasSentinel));
    }

    [Fact]
    public void OversizedReservedPayload_IsAccountedWithoutReadingItsBytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        _ = Capture(); // Warm JIT/encoder without modifying any inventory.
        long length = LinuxRecoveryBudget.MaximumBytes + 1;
        using (var file = new FileStream(At(RetainedA), FileMode.CreateNew, FileAccess.Write))
            file.SetLength(length); // Sparse private fixture; not a half-gigabyte payload allocation.
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At(RetainedA), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        long before = GC.GetAllocatedBytesForCurrentThread();
        I.Snapshot snapshot = Capture();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(length, snapshot.Usage.HistoryBytes);
        Assert.Equal(1, snapshot.Usage.ManagedEntries);
        Assert.True(allocated < 1024 * 1024, "Metadata-only inspection allocated " + allocated);
        Assert.Equal(length, new FileInfo(At(RetainedA)).Length);
    }

    [Fact]
    public void EarlierReservedLeafChangingDuringLaterObservation_RefusesAggregateSnapshot()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        File.WriteAllBytes(At(RetainedA), new byte[] { 1 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At(RetainedA), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllBytes(At(RetainedB), new byte[] { 2 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At(RetainedB), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        bool changed = false;
        D.BeforeOperationForTests = (operation, path) =>
        {
            if (changed || operation != "metadata-before-named" || path != At(RetainedB)) return;
            changed = true;
            File.SetLastWriteTimeUtc(At(RetainedA), new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        };
        try { Assert.ThrowsAny<IOException>(() => Capture()); }
        finally { D.BeforeOperationForTests = null; }
        Assert.True(changed);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(At(RetainedA)));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(At(RetainedB)));
    }

    [Fact]
    public void FreshCapabilities_ObserveSameHeadAndHistoryWithoutMutationOrRetainedFd()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        string header = WriteHeader(Epoch());
        byte[] before = File.ReadAllBytes(header);
        I.Snapshot first = Capture();
        int fdBefore = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 10; index++)
        {
            I.Snapshot next = Capture();
            Assert.Equal(first.Digest, next.Digest);
            Assert.Equal(first.HeadSelection, next.HeadSelection);
            Assert.Equal(first.Root, next.Root);
            Assert.Equal(first.Journal, next.Journal);
        }
        Assert.Equal(fdBefore, Directory.GetFileSystemEntries("/proc/self/fd").Length);
        Assert.Equal(before, File.ReadAllBytes(header));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<I.Entry>)first.Entries).Add(first.Entries[0]));
        using var root = D.OpenExisting(_root);
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        _ = I.Capture(root);
        _ = root.ReadDirectoryMetadata(); // Capture did not dispose its caller's capability.
    }

    [Fact]
    public void FailedJournalRead_ClosesOwnedChildWithoutDisposingTheCallerRoot()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        File.WriteAllBytes(At("sentinel"), new byte[] { 7 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At("sentinel"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string bad = Path.Combine(Journal(), "linked-control");
        File.CreateSymbolicLink(bad, At("sentinel"));
        using var root = D.OpenExisting(_root);
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        Assert.ThrowsAny<IOException>(() => I.Capture(root));
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 10; index++)
            Assert.ThrowsAny<IOException>(() => I.Capture(root));
        Assert.Equal(before, Directory.GetFileSystemEntries("/proc/self/fd").Length);
        _ = root.ReadDirectoryMetadata();
        Assert.NotNull(new FileInfo(bad).LinkTarget);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(At("sentinel")));
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string created);

    public void Dispose()
    {
        D.BeforeOperationForTests = null;
        Directory.Delete(_root, recursive: true);
    }
}
