// WHY: Hex compatibility must preserve the mapped-root NO_XDEV boundary and direct-root reads.
// MAINT: Private fixtures only; never mount, bind-mount or change real extraction permissions.
using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class NoclipHexDataReadBoundaryTests
{
    [Fact]
    public void Native_DirectMappedParentSupportsCaseCompatibleRead()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "hex-direct-" + Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "ABCD.bin"), new byte[] { 7 });
            G.VerifiedOpenResult result = NoclipHexDataRead.TryOpen(
                root, Path.Combine(root, "abcd.bin"), "/data/FinalFantasyX/11/abcd.bin", out var opened);
            using (opened)
            {
                Assert.Equal(G.VerifiedOpenResult.Success, result);
                Assert.NotNull(opened);
                Assert.Equal(7, opened.Stream.ReadByte());
            }
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Native_OriginalMappedRootCannotBeBypassedAcrossMounts()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        // This tests an existing Linux mount, not a mount created or changed by the test.
        Assert.True(Directory.Exists("/dev/shm"), "Native boundary test requires existing /dev/shm.");
        using var mapped = L.OpenRoot("/");
        using var shared = L.OpenRoot("/dev/shm");
        Assert.NotEqual(L.Observe(mapped, L.DirectoryType).Identity.MountId,
            L.Observe(shared, L.DirectoryType).Identity.MountId);
        string root = Path.Combine("/dev/shm", "ffx-hex-cross-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(root));
        TestDirectory.CreatePrivate(root);
        try
        {
            string existing = Path.Combine(root, "ABCD.bin");
            File.WriteAllBytes(existing, new byte[] { 9 });
            G.VerifiedOpenResult result = NoclipHexDataRead.TryOpen(
                "/", Path.Combine(root, "abcd.bin"), "/data/FinalFantasyX/11/abcd.bin", out var opened);
            using (opened)
            {
                Assert.Equal(G.VerifiedOpenResult.Rejected, result);
                Assert.Null(opened);
            }
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(existing));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
