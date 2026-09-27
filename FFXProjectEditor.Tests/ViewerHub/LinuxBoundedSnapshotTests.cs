using System;
using System.IO;
using System.Runtime.Versioning;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Descriptor-bound small snapshot allocation ──
// An inventory size check is not an allocation boundary: a file may grow before its next open.
// MAINT: custom limits must reach the retained byte reader without changing default64MiB behavior.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxBoundedSnapshotTests
{
    [Theory]
    [InlineData(8192, 8192, true)]
    [InlineData(8193, 8192, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    public void Limit_AppliesToTheOpenedObject_AndDefaultReadIsPreserved(int length, int limit, bool accepted)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        byte[] content = new byte[length];
        if (length > 0) content[^1] = 7;
        output.PublishNew("receipt.json", content);
        if (accepted) Assert.Equal(content, output.ReadSnapshot("receipt.json", limit)!.Bytes.ToArray());
        else Assert.Throws<IOException>(() => output.ReadSnapshot("receipt.json", limit));
        Assert.Equal(content, output.ReadSnapshot("receipt.json")!.Bytes.ToArray());
        Assert.Null(output.ReadSnapshot("absent.json", limit));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(LinuxOwnedOutputDirectory.MaximumBytes + 1)]
    public void UnsupportedLimits_AreRejectedWithoutIo(int limit)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.Throws<ArgumentOutOfRangeException>(() => output.ReadSnapshot("absent.json", limit));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public void GrowthAfterMetadataCheck_DoesNotAllocateTheLargerPayload()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("receipt.json", new byte[1024]);
        Assert.Equal(1024, output.ReadFileMetadata("receipt.json")!.Value.File.Length);
        using (var file = new FileStream(fixture.Path, FileMode.Open, FileAccess.Write)) file.SetLength(32 * 1024 * 1024);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<IOException>(() => output.ReadSnapshot("receipt.json", 8192));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024 * 1024, $"Bounded snapshot allocation was {allocated} bytes.");
        Assert.Equal(32 * 1024 * 1024, output.ReadFileMetadata("receipt.json")!.Value.File.Length);
    }

    [Theory]
    [InlineData("grow")]
    [InlineData("shrink")]
    [InlineData("replace")]
    public void LateDrift_IsRejectedUnderCustomLimit(string drift)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("receipt.json", new byte[16]);
        string path = fixture.Path, saved = path + ".saved";
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "snapshot-open") return;
            reached = true;
            if (drift == "replace") { File.Move(path, saved); File.WriteAllBytes(path, new byte[16]); }
            else { using var file = new FileStream(path, FileMode.Open, FileAccess.Write); file.SetLength(drift == "grow" ? 4096 : 1); }
        };
        try { Assert.ThrowsAny<IOException>(() => output.ReadSnapshot("receipt.json", 32)); Assert.True(reached); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
    }

    [Fact]
    public void RepeatedRefusals_ReleaseDescriptors_AndDisposedCapabilityIsRejected()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        try
        {
            output.PublishNew("receipt.json", new byte[128]);
            int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
            for (int index = 0; index < 32; index++) Assert.Throws<IOException>(() => output.ReadSnapshot("receipt.json", 64));
            int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
            Assert.True(after <= before + 4, $"Descriptors: {before} -> {after}");
        }
        finally { output.Dispose(); }
        Assert.Throws<ObjectDisposedException>(() => output.ReadSnapshot("receipt.json", 8192));
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (LinuxReadFileSystem.IsSupported) return true;
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOwnedOutputDirectory.OpenOrCreate("/tmp/ffx-bounded-read-refusal"));
        return false;
    }

    [SupportedOSPlatform("linux")]
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = TestDirectory.CreatePrivate(System.IO.Path.Combine(TestDataPaths.RepoRoot,
            "work", "linux-bounded-read-tests", Guid.NewGuid().ToString("N"))).FullName;
        internal string Path => System.IO.Path.Combine(Root, "receipt.json");
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
