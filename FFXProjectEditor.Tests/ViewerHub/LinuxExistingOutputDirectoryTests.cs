// WHY: Recovery inspection must not create missing roots or journal children.
// MAINT: These Linux ext-family fixtures prove readonly opens, not immutable namespaces or Windows behavior.
using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using N = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxExistingOutputDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(RepoWorkDir(), "linux-inspect-open-" + Guid.NewGuid().ToString("N"));
    public LinuxExistingOutputDirectoryTests() => TestDirectory.CreatePrivate(_root);
    private string At(string name) => Path.Combine(_root, name);
    public void Dispose()
    {
        O.BeforeOperationForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


    [Fact]
    public void ExistingRoot_ReturnsObservedBytesWithoutChangingDirectory()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
File.WriteAllBytes(At("saved.bin"), new byte[] { 7 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At("saved.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var raw = L.OpenRoot(_root);
        var before = N.Observe(raw, L.DirectoryType);
        using var root = O.OpenExisting(_root);
        Assert.Equal(new byte[] { 7 }, root.ReadSnapshot("saved.bin")!.Bytes.ToArray());
        Assert.Equal(before, root.ReadDirectoryMetadata());
        Assert.Equal(before, N.Observe(raw, L.DirectoryType));
        Assert.Single(Directory.GetFiles(_root));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void MissingRoot_DoesNotCreateIt()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string missing = At("missing");
        Assert.ThrowsAny<IOException>(() => { using var ignored = O.OpenExisting(missing); });
        Assert.False(Directory.Exists(missing));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void MissingAncestors_DoNotCreateAnyDirectory()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string firstMissing = At("missing-parent");
        Assert.ThrowsAny<IOException>(() =>
        {
            using var ignored = O.OpenExisting(Path.Combine(firstMissing, "nested", "output"));
        });
        Assert.False(Directory.Exists(firstMissing));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void ExistingChild_ReturnsReadablePrivateChild()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string childPath = At("journal");
        TestDirectory.CreatePrivate(childPath);
File.WriteAllBytes(Path.Combine(childPath, "record.json"), new byte[] { 3 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(childPath, "record.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var root = O.OpenExisting(_root);
        using var child = root.OpenExistingChild("journal");
        Assert.Equal(childPath, child.FullPath);
        Assert.Equal(new byte[] { 3 }, child.ReadSnapshot("record.json", 8192)!.Bytes.ToArray());
        var observation = child.ReadDirectoryMetadata();
        Assert.Equal(N.EffectiveUserId, observation.OwnerId);
        Assert.Equal(0x1C0, observation.Mode & 0xFFF);
        Assert.Single(Directory.GetFiles(childPath));
    }

    [Fact]
    public void MissingChild_DoesNotCreateIt()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        using var root = O.OpenExisting(_root);
        Assert.ThrowsAny<IOException>(() => { using var ignored = root.OpenExistingChild("missing-journal"); });
        Assert.False(Directory.Exists(At("missing-journal")));
        Assert.Empty(root.ListChildNames());
    }

    [Fact]
    public void SymlinkRoot_IsRefusedWithoutTouchingItsTarget()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        TestDirectory.CreatePrivate(At("actual"));
File.WriteAllBytes(At("actual/keep.bin"), new byte[] { 8 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At("actual/keep.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.CreateSymbolicLink(At("alias"), At("actual"));
        Assert.ThrowsAny<IOException>(() => { using var ignored = O.OpenExisting(At("alias")); });
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(At("actual/keep.bin")));
        Assert.Single(Directory.GetFileSystemEntries(At("actual")));
    }

    [Fact]
    public void SymlinkChild_IsRefusedWithoutTouchingItsTarget()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        TestDirectory.CreatePrivate(At("actual"));
File.WriteAllBytes(At("actual/keep.bin"), new byte[] { 8 });
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(At("actual/keep.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.CreateSymbolicLink(At("alias"), At("actual"));
        using var root = O.OpenExisting(_root);
        Assert.ThrowsAny<IOException>(() => { using var ignored = root.OpenExistingChild("alias"); });
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(At("actual/keep.bin")));
        Assert.Single(Directory.GetFileSystemEntries(At("actual")));
    }

    [Fact]
    public void NonPrivateChild_IsRefusedWithoutChangingMode()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string childPath = At("public-child");
        TestDirectory.CreatePrivate(childPath);
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(childPath, mode);
        using var root = O.OpenExisting(_root);
        Assert.ThrowsAny<IOException>(() => { using var ignored = root.OpenExistingChild("public-child"); });
        Assert.Equal(mode, File.GetUnixFileMode(childPath));
        Assert.Empty(Directory.GetFileSystemEntries(childPath));
    }

    [Fact]
    public void SharedWritableRoot_IsRefusedWithoutChangingMode()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        string directory = At("shared");
        TestDirectory.CreatePrivate(directory);
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(directory, mode);
        Assert.ThrowsAny<IOException>(() => { using var ignored = O.OpenExisting(directory); });
        Assert.Equal(mode, File.GetUnixFileMode(directory));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public void Observation_RefusesAChangedNamedRoot()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        TestDirectory.CreatePrivate(At("owned"));
        using var root = O.OpenExisting(At("owned"));
        Directory.Move(At("owned"), At("moved"));
        TestDirectory.CreatePrivate(At("owned"));
        Assert.ThrowsAny<IOException>(() => { _ = root.ReadDirectoryMetadata(); });
        Assert.Empty(Directory.GetFileSystemEntries(At("owned")));
        Assert.Empty(Directory.GetFileSystemEntries(At("moved")));
    }

    [Fact]
    public void Observation_RefusesDisposedRoot()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        var root = O.OpenExisting(_root);
        root.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = root.ReadDirectoryMetadata(); });
    }

    [Fact]
    public void Observation_RefusesDisposedParentForChild()
    {
        if (!OperatingSystem.IsLinux() || !L.IsSupported) return;
        TestDirectory.CreatePrivate(At("child"));
        var parent = O.OpenExisting(_root);
        using var child = parent.OpenExistingChild("child");
        parent.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = child.ReadDirectoryMetadata(); });
    }
}
