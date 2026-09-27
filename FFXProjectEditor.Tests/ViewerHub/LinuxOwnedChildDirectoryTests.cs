using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Child capabilities remain beneath the retained owner ──
// Real ext-family fixtures exercise native traversal and deterministic rename windows.
// MAINT: parent lifetime/identity remains part of every child operation; no host mount changes.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxOwnedChildDirectoryTests
{
    [Fact]
    public void NestedPrivateChildren_PreserveNamesPermissionsAndRootOwnership()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var child = root.OpenOrCreateChild("Édits");
        using var nested = child.OpenOrCreateChild("session");
        using var lease = nested.AcquireWriteLock(TimeSpan.Zero);
        nested.PublishNew("test.bin", new byte[] { 1, 2 });
        Assert.Equal(Path.Combine(fixture.Root, "Édits", "session"), nested.FullPath);
        Assert.Equal(new byte[] { 1, 2 }, nested.ReadSnapshot("test.bin")!.Bytes.ToArray());
        Assert.Equal(PrivateMode, File.GetUnixFileMode(child.FullPath));
        Assert.Equal(PrivateMode, File.GetUnixFileMode(nested.FullPath));
        using var reopened = root.OpenOrCreateChild("Édits");
        Assert.Equal(child.FullPath, reopened.FullPath);
        Assert.Single(root.ListChildNames());
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escaped")]
    [InlineData("/absolute")]
    [InlineData("one/two")]
    [InlineData("bad\\leaf")]
    public void InvalidChildLeaf_IsRejectedBeforeCreatingAnything(string leaf)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.ThrowsAny<ArgumentException>(() => root.OpenOrCreateChild(leaf));
        Assert.Empty(root.ListChildNames());
    }

    [Fact]
    public void OversizedUtf8Leaf_IsRejectedWithoutMutation()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.ThrowsAny<ArgumentException>(() => root.OpenOrCreateChild(new string('é', 128)));
        Assert.Empty(root.ListChildNames());
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("file")]
    [InlineData("public-directory")]
    [InlineData("shared-writable-directory")]
    public void ExistingUnownedShape_IsNotAdoptedOrChanged(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        string leaf = Path.Combine(fixture.Root, "child");
        if (kind == "symlink") Directory.CreateSymbolicLink(leaf, fixture.Outside);
        if (kind == "file") File.WriteAllText(leaf, "keep");
        if (kind.EndsWith("directory", StringComparison.Ordinal))
        {
            TestDirectory.CreatePrivate(leaf);
            File.SetUnixFileMode(leaf, kind == "public-directory"
                ? PrivateMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                : PrivateMode | UnixFileMode.GroupWrite);
        }
        Assert.ThrowsAny<IOException>(() => root.OpenOrCreateChild("child"));
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(fixture.Outside, "sentinel")));
        if (kind == "file") Assert.Equal("keep", File.ReadAllText(leaf));
        if (kind.EndsWith("directory", StringComparison.Ordinal))
            Assert.NotEqual(PrivateMode, File.GetUnixFileMode(leaf));
        Assert.Single(root.ListChildNames());
    }

    [Fact]
    public void NativeChildOpen_RejectsCrossMountBeforeFilesystemAdoption()
    {
        if (!CanRunNative()) return;
        using var system = LinuxReadFileSystem.OpenRoot("/");
        var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() =>
            LinuxOutputFileSystem.OpenOwnedChildDirectory(system, "proc"));
        Assert.Equal(18, error.Errno); // EXDEV from RESOLVE_NO_XDEV, not a later statfs check.
    }

    [Theory]
    [InlineData("child-before-open", false)]
    [InlineData("child-created", true)]
    [InlineData("child-opened", true)]
    public void ParentRenameDuringAcquisition_RefusesAndPreservesCreatedDirectory(string hook, bool created)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "selected");
        string moved = Path.Combine(fixture.Root, "moved");
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(selected);
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook || reached) return;
            reached = true;
            Directory.Move(selected, moved);
            TestDirectory.CreatePrivate(selected);
        };
        try { Assert.ThrowsAny<IOException>(() => root.OpenOrCreateChild("child")); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.True(reached);
        Assert.Empty(Directory.EnumerateFileSystemEntries(selected));
        Assert.Equal(created, Directory.Exists(Path.Combine(moved, "child")));
        if (created) Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(moved, "child")));
    }

    [Fact]
    public void MovingSameChildUnderSubstitutedParent_StillRefusesLaterWrites()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "selected");
        string moved = Path.Combine(fixture.Root, "moved");
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(selected);
        using var child = root.OpenOrCreateChild("child");
        Directory.Move(selected, moved);
        TestDirectory.CreatePrivate(selected);
        Directory.Move(Path.Combine(moved, "child"), Path.Combine(selected, "child"));
        Assert.ThrowsAny<IOException>(() => child.PublishNew("refused.bin", new byte[] { 1 }));
        Assert.Empty(Directory.EnumerateFileSystemEntries(child.FullPath));
    }

    [Fact]
    public void ParentDisposal_InvalidatesChildWithoutDisposingItsDescriptorTwice()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var child = root.OpenOrCreateChild("child");
        root.Dispose();
        Assert.Throws<ObjectDisposedException>(() => child.ReadSnapshot("missing.bin"));
        child.Dispose();
        child.Dispose();
        Assert.Empty(Directory.EnumerateFileSystemEntries(child.FullPath));
    }

    [Fact]
    public void DepthLimit_IsCheckedBeforeNinthDirectoryIsCreated()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        var roots = new List<LinuxOwnedOutputDirectory> { LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root) };
        try
        {
            for (int depth = 0; depth < 8; depth++) roots.Add(roots[^1].OpenOrCreateChild("level"));
            Assert.Throws<IOException>(() => roots[^1].OpenOrCreateChild("ninth"));
            Assert.Empty(roots[^1].ListChildNames());
        }
        finally { for (int index = roots.Count - 1; index >= 0; index--) roots[index].Dispose(); }
    }

    [Theory]
    [InlineData("child-before-open")]
    [InlineData("child-created")]
    [InlineData("child-opened")]
    public void FailedAcquisition_DoesNotLeakOwnedDescriptors(string hook)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        int CountOwned() => Directory.EnumerateFileSystemEntries("/proc/self/fd")
            .Select(path => new FileInfo(path).LinkTarget)
            .Count(path => path == fixture.Root || path?.StartsWith(fixture.Root + "/", StringComparison.Ordinal) == true);
        int before = CountOwned();
        Assert.True(before >= 1, "The retained root descriptor must be visible to this leak assertion.");
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage == hook) throw new IOException("Injected child acquisition failure.");
        };
        try
        {
            for (int attempt = 0; attempt < 16; attempt++)
                Assert.Throws<IOException>(() => root.OpenOrCreateChild("failed-" + attempt));
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(before, CountOwned());
    }

    [Fact]
    public void ChildReplacementAfterOpen_IsRejectedWithoutDeletingEitherDirectory()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        string selected = Path.Combine(fixture.Root, "child");
        string retained = Path.Combine(fixture.Root, "retained");
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "child-opened") return;
            Directory.Move(selected, retained);
            TestDirectory.CreatePrivate(selected);
        };
        try { Assert.Throws<IOException>(() => root.OpenOrCreateChild("child")); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.True(Directory.Exists(retained));
        Assert.True(Directory.Exists(selected));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void PrivateMode_IsRecheckedBeforeLaterChildWrites()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var root = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var child = root.OpenOrCreateChild("child");
        File.SetUnixFileMode(child.FullPath, PrivateMode | UnixFileMode.GroupRead);
        Assert.Throws<IOException>(() => child.PublishNew("refused.bin", new byte[] { 1 }));
        Assert.Empty(Directory.EnumerateFileSystemEntries(child.FullPath));
    }

    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(LinuxReadFileSystem.IsSupported);
        return true;
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; }
        internal string Outside { get; }
        [SupportedOSPlatform("linux")]
        internal Fixture()
        {
            Root = TestDirectory.CreatePrivate(Path.Combine(TestDataPaths.RepoRoot, "work",
                "linux-child-tests", Guid.NewGuid().ToString("N"))).FullName;
            Outside = TestDirectory.CreatePrivate(Root + "-outside").FullName;
            File.WriteAllText(Path.Combine(Outside, "sentinel"), "unrelated");
        }
        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            Directory.Delete(Outside, recursive: true);
        }
    }
}
