using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Linux replacement preserves both observed artifacts ──
// Real ext-family fixtures and deterministic syscall-window interference, not an inode-CAS mock.
// MAINT: ordinary same-UID writers can ignore flock. Uncertainty must preserve evidence, not
// promise that the visible target stayed old or that automatic pathname cleanup is safe.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed partial class LinuxRetainedReplacementTests
{
    [Fact]
    public void Replacement_ReturnsExactNewAndRetainedReceipts_AcrossRepeatedExchanges()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var lease = output.AcquireWriteLock(TimeSpan.Zero);
        output.PublishNew("target.bin", new byte[] { 1, 2, 3 });
        for (int index = 0; index < 4; index++)
        {
            var prior = output.ReadSnapshot("target.bin")!;
            byte[] bytes = index == 3 ? Array.Empty<byte>() : new byte[] { (byte)(index + 4) };
            string retention = $"retained-{index}.bin";
            var result = output.ReplaceRetainingDisplaced("target.bin", prior, retention, bytes);
            Assert.Equal(Path.Combine(fixture.Root, "target.bin"), result.Target.FullPath);
            Assert.Equal(Path.Combine(fixture.Root, retention), result.Retained.FullPath);
            Assert.Equal(bytes, File.ReadAllBytes(result.Target.FullPath));
            Assert.Equal(prior.Bytes.ToArray(), File.ReadAllBytes(result.Retained.FullPath));
            Assert.Equal(prior.Identity, result.Retained.Identity);
            Assert.NotEqual(result.Target.Identity, result.Retained.Identity);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Target.Sha256);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(prior.Bytes)), result.Retained.Sha256);
            Assert.Equal(prior.Observation.File.Length, result.Retained.Length);
            Assert.Equal(bytes.Length, result.Target.Length);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.Target.FullPath));
            Assert.Equal(prior.Observation.File.ModifiedSeconds, output.ReadSnapshot(retention)!.Observation.File.ModifiedSeconds);
            Assert.Equal(prior.Observation.File.ModifiedNanoseconds, output.ReadSnapshot(retention)!.Observation.File.ModifiedNanoseconds);
        }
    }

    [Fact]
    public void CallerBytesAndSnapshot_AreOwned_AndLinuxCaseDistinctRetentionIsAllowed()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        byte[] replacement = { 2, 3 };
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage == "replace-before-link") replacement.AsSpan().Fill(9);
        };
        try { output.ReplaceRetainingDisplaced("target.bin", prior, "TARGET.BIN", replacement); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 2, 3 }, File.ReadAllBytes(Path.Combine(fixture.Root, "target.bin")));
        Assert.Equal(new byte[] { 1 }, prior.Bytes.ToArray());
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fixture.Root, "TARGET.BIN")));
    }

    [Theory]
    [InlineData("target.bin", "target.bin")]
    [InlineData("../target.bin", "retained.bin")]
    [InlineData("target.bin", "nested/retained.bin")]
    [InlineData("target.bin", "bad\\leaf")]
    [InlineData("target.bin", "bad\0leaf")]
    public void InvalidLeaves_AreRejectedBeforeMutation(string target, string retention)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        Assert.Throws<ArgumentException>(() => output.ReplaceRetainingDisplaced(target, prior, retention, new byte[] { 2 }));
        Assert.Single(Directory.GetFiles(fixture.Root));
        Assert.Equal(new byte[] { 1 }, output.ReadSnapshot("target.bin")!.Bytes.ToArray());
    }

    [Fact]
    public void OversizedPayloadOrNullSnapshot_IsRejectedBeforeMutation()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        Assert.Throws<ArgumentNullException>(() => output.ReplaceRetainingDisplaced("target.bin", null!, "retained.bin", new byte[] { 2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin",
            new byte[LinuxOwnedOutputDirectory.MaximumBytes + 1]));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("metadata")]
    [InlineData("different-inode")]
    [InlineData("missing")]
    public void StaleOrDifferentSnapshot_IsRefusedWithoutPublishingRetention(string change)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        if (change == "bytes") File.WriteAllBytes(fixture.Path("target.bin"), new byte[] { 3 });
        if (change == "metadata") File.SetLastWriteTimeUtc(fixture.Path("target.bin"), DateTime.UtcNow.AddMinutes(1));
        if (change is "different-inode" or "missing") File.Move(fixture.Path("target.bin"), fixture.Path("original.bin"));
        if (change == "different-inode") output.PublishNew("target.bin", new byte[] { 1 });
        Assert.ThrowsAny<IOException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
        Assert.False(File.Exists(fixture.Path("retained.bin")));
        output.PublishNew("still-usable.bin", new byte[] { 4 });
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    [InlineData("fifo")]
    [InlineData("directory")]
    public void RetentionCollision_PreservesBothInputs_AndDoesNotFaultTheCapability(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        output.PublishNew("unrelated.bin", new byte[] { 7 });
        var prior = output.ReadSnapshot("target.bin")!;
        if (kind == "regular") output.PublishNew("retained.bin", new byte[] { 8 });
        if (kind == "symlink") File.CreateSymbolicLink(fixture.Path("retained.bin"), fixture.Path("unrelated.bin"));
        if (kind == "hardlink") fixture.Run("ln", fixture.Path("unrelated.bin"), fixture.Path("retained.bin"));
        if (kind == "fifo") fixture.Run("mkfifo", fixture.Path("retained.bin"));
        if (kind == "directory") TestDirectory.CreatePrivate(fixture.Path("retained.bin"));
        var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() =>
            output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
        Assert.Equal(17, error.Errno);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path("target.bin")));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Path("unrelated.bin")));
        output.PublishNew("still-usable.bin", new byte[] { 4 });
    }

    [Theory]
    [InlineData("replace-linked", false)]
    [InlineData("replace-exchanged", true)]
    public void InjectedIoFailure_PreservesPossibleArtifacts_AndFaultsTheCapability(string hook, bool exchanged)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook) return;
            reached = true;
            throw new IOException("Injected post-publication I/O failure.");
        };
        try
        {
            var error = Assert.Throws<LinuxReplacementUncertainException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            Assert.Equal(exchanged, error.ExchangeCompleted);
            Assert.Equal(fixture.Path("target.bin"), error.TargetPath);
            Assert.Equal(fixture.Path("retained.bin"), error.RetentionPath);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { exchanged ? (byte)2 : (byte)1 }, File.ReadAllBytes(fixture.Path("target.bin")));
        Assert.Equal(new byte[] { exchanged ? (byte)1 : (byte)2 }, File.ReadAllBytes(fixture.Path("retained.bin")));
        Assert.Throws<InvalidOperationException>(() => output.ReadSnapshot("target.bin"));
        Assert.Throws<InvalidOperationException>(() => output.PublishNew("must-not-write.bin", new byte[] { 9 }));
    }

    [Theory]
    [InlineData("target.bin")]
    [InlineData("retained.bin")]
    public void LastSyscallWindowSwap_IsNotInodeCas_AndEveryDisplacedObjectIsPreserved(string changedLeaf)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        output.PublishNew("interloper.bin", new byte[] { 7 });
        var prior = output.ReadSnapshot("target.bin")!;
        string changedPath = fixture.Path(changedLeaf), savedPath = fixture.Path("saved-observed.bin");
        string interloperPath = fixture.Path("interloper.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "replace-before-exchange") return;
            reached = true;
            File.Move(changedPath, savedPath);
            File.Move(interloperPath, changedPath);
        };
        try
        {
            var error = Assert.Throws<LinuxReplacementUncertainException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            Assert.True(error.ExchangeCompleted); // Proves the postcondition, not a precondition-only rejection.
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { changedLeaf == "target.bin" ? (byte)2 : (byte)7 }, File.ReadAllBytes(fixture.Path("target.bin")));
        Assert.Equal(new byte[] { changedLeaf == "target.bin" ? (byte)7 : (byte)1 }, File.ReadAllBytes(fixture.Path("retained.bin")));
        Assert.Equal(new byte[] { changedLeaf == "target.bin" ? (byte)1 : (byte)2 }, File.ReadAllBytes(fixture.Path("saved-observed.bin")));
    }

    [Theory]
    [InlineData("replace-before-link", false)]
    [InlineData("replace-linked", true)]
    [InlineData("replace-exchanged", true)]
    public void RootRename_DoesNotWriteTheSubstitutedDirectory(string hook, bool linked)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string selected = fixture.Path("selected"), moved = fixture.Path("moved");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(selected);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook) return;
            reached = true;
            Directory.Move(selected, moved);
            TestDirectory.CreatePrivate(selected);
        };
        try
        {
            var error = Assert.ThrowsAny<IOException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            if (linked) Assert.IsType<LinuxReplacementUncertainException>(error);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Empty(Directory.GetFileSystemEntries(selected));
        Assert.Equal(linked ? 2 : 1, Directory.GetFiles(moved).Length);
        Assert.Equal(new byte[] { hook == "replace-exchanged" ? (byte)2 : (byte)1 }, File.ReadAllBytes(System.IO.Path.Combine(moved, "target.bin")));
    }

    [Fact]
    public void SuccessAndCollisionLoops_ReleaseDescriptorsWithoutRemovingRetainedData()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 32; index++)
        {
            var prior = output.ReadSnapshot("target.bin")!;
            string leaf = $"retained-{index}.bin";
            output.ReplaceRetainingDisplaced("target.bin", prior, leaf, new byte[] { (byte)index });
            var current = output.ReadSnapshot("target.bin")!;
            Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", current, leaf, new byte[] { 9 }));
        }
        int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        Assert.True(after <= before + 4, $"Descriptors: {before} -> {after}");
        Assert.Equal(33, Directory.GetFiles(fixture.Root).Length);
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (LinuxReadFileSystem.IsSupported) return true;
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOwnedOutputDirectory.OpenOrCreate("/tmp/ffx-replace-refusal"));
        return false;
    }

    [SupportedOSPlatform("linux")]
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = TestDirectory.CreatePrivate(System.IO.Path.Combine(TestDataPaths.RepoRoot,
            "work", "linux-replacement-tests", Guid.NewGuid().ToString("N"))).FullName;
        internal string Path(string leaf) => System.IO.Path.Combine(Root, leaf);
        internal void Run(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(5000)) { child.Kill(); throw new TimeoutException("Fixture helper timed out."); }
            Assert.Equal(0, child.ExitCode);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
