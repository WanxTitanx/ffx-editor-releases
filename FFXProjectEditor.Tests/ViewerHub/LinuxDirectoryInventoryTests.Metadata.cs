using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Accounting observations are metadata, not payload snapshots ──
// Large files must be measured without allocating their content, while unsafe or changed leaves
// still fail closed. MAINT: these observations neither hash data nor authorize later deletion.
public sealed partial class LinuxDirectoryInventoryTests
{
    [Fact]
    public void Metadata_ObservesLargeSparseFilesWithoutThePayloadSnapshotLimit()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        fixture.Create("large.bin", 1);
        long length = LinuxOwnedOutputDirectory.MaximumBytes + 1L;
        using (var file = new FileStream(fixture.Path("large.bin"), FileMode.Open, FileAccess.Write)) file.SetLength(length);
        Assert.Null(output.ReadFileMetadata("absent.bin"));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var metadata = output.ReadFileMetadata("large.bin")!.Value;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(length, metadata.File.Length);
        Assert.True(allocated < 1024 * 1024, $"Metadata-only allocation unexpectedly large: {allocated}");
        Assert.Throws<IOException>(() => output.ReadSnapshot("large.bin"));
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    [InlineData("fifo")]
    [InlineData("directory")]
    [InlineData("unsafe-mode")]
    public void Metadata_RejectsSpecialLinkedOrUnsafeFiles(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        fixture.Create("unrelated.bin", 7);
        if (kind == "symlink") File.CreateSymbolicLink(fixture.Path("target.bin"), fixture.Path("unrelated.bin"));
        if (kind == "hardlink") fixture.Run("ln", fixture.Path("unrelated.bin"), fixture.Path("target.bin"));
        if (kind == "fifo") fixture.Run("mkfifo", fixture.Path("target.bin"));
        if (kind == "directory") TestDirectory.CreatePrivate(fixture.Path("target.bin"));
        if (kind == "unsafe-mode")
        {
            fixture.Create("target.bin", 1);
            File.SetUnixFileMode(fixture.Path("target.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);
        }
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.ThrowsAny<IOException>(() => output.ReadFileMetadata("target.bin"));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Path("unrelated.bin")));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("same-bytes-new-inode")]
    [InlineData("missing")]
    public void Metadata_RefusesRetainedOrNamedDrift_AndDoesNotMisclassifyLateMissing(string drift)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        fixture.Create("target.bin", 1);
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        string target = fixture.Path("target.bin"), saved = fixture.Path("saved.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "metadata-open") return;
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            reached = true;
            if (drift == "bytes") File.WriteAllBytes(target, new byte[] { 2 });
            else File.Move(target, saved);
            if (drift == "same-bytes-new-inode") fixture.Create("target.bin", 1);
        };
        try { Assert.ThrowsAny<IOException>(() => output.ReadFileMetadata("target.bin")); Assert.True(reached); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        if (drift != "bytes") Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_LastNamedObservationRejectsLateSameByteSubstitutionOrMissing(bool missing)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        fixture.Create("target.bin", 1);
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        string target = fixture.Path("target.bin"), saved = fixture.Path("saved.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "metadata-before-named") return;
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            reached = true;
            File.Move(target, saved);
            if (!missing) fixture.Create("target.bin", 1);
        };
        try { Assert.ThrowsAny<IOException>(() => output.ReadFileMetadata("target.bin")); Assert.True(reached); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(saved));
    }

    [Fact]
    public void InventoryAndMetadata_RespectDisposedAndUncertainPublicationLifecycle()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        var disposed = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.ListChildNames());
        Assert.Throws<ObjectDisposedException>(() => disposed.ReadFileMetadata("absent.bin"));
        using var faulted = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage == "create-linked") throw new IOException("Injected uncertainty.");
        };
        try { Assert.Throws<LinuxPublicationUncertainException>(() => faulted.PublishNew("retained.bin", new byte[] { 1 })); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Throws<InvalidOperationException>(() => faulted.ListChildNames());
        Assert.Throws<InvalidOperationException>(() => faulted.ReadFileMetadata("retained.bin"));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path("retained.bin")));
    }

    [Fact]
    public void RepeatedSuccessAndRefusals_DoNotLeakDescriptors()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        fixture.Create("a.bin", 1);
        fixture.Create("b.bin", 2);
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 32; index++)
        {
            Assert.Equal(2, output.ListChildNames().Count);
            Assert.NotNull(output.ReadFileMetadata("a.bin"));
            Assert.Null(output.ReadFileMetadata("absent.bin"));
            Assert.Throws<IOException>(() => output.ListChildNames(1));
        }
        int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        Assert.True(after <= before + 4, $"Descriptors: {before} -> {after}");
    }
}
