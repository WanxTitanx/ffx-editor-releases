using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Real private-output objects, not mocked pathname safety ──
// Test hooks make observed rename/write failures deterministic. They do not prove exclusion
// against arbitrary same-UID writers. Every filesystem mutation stays in a new owned fixture.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxOwnedOutputDirectoryTests
{
    [Fact]
    public void OutputAbi_RequiresOwnershipFields_AndRejectsUnsupportedFilesystem()
    {
        Assert.Equal(120, Marshal.SizeOf<LinuxOutputFileSystem.StatFsBuffer>());
        Assert.Equal(16, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>("LinkCount").ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>("OwnerId").ToInt32());
        Assert.Equal(0x13CFU, LinuxOutputFileSystem.RequiredMask);
        Assert.Equal(0x410002UL | LinuxReadFileSystem.OpenCloseOnExec,
            LinuxOutputFileSystem.AnonymousFileFlags);
        LinuxOutputFileSystem.RequireExtFileSystem(new() { Type = 0xEF53 });
        Assert.Throws<IOException>(() => LinuxOutputFileSystem.RequireExtFileSystem(new() { Type = 0x01021994 }));
        var value = new LinuxReadFileSystem.StatxBuffer
        {
            Mask = LinuxOutputFileSystem.RequiredMask, Mode = 0x8180, OwnerId = 1000,
            LinkCount = 0, Inode = 1, MountId = 2,
        };
        var decoded = LinuxOutputFileSystem.DecodeOutput(value, LinuxReadFileSystem.RegularFileType);
        Assert.Equal(1000U, decoded.OwnerId);
        Assert.Equal(0U, decoded.LinkCount);
        Assert.Equal(0x8180, decoded.Mode);
        foreach (uint bit in new uint[] { 2, 4, 8 })
        {
            var missing = value;
            missing.Mask &= ~bit;
            Assert.Throws<IOException>(() => LinuxOutputFileSystem.DecodeOutput(missing, LinuxReadFileSystem.RegularFileType));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/a.bin")]
    [InlineData("/absolute")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void LeafPolicy_RefusesAmbiguousNamesOnEveryHost(string leaf) =>
        Assert.Throws<ArgumentException>(() => LinuxOutputFileSystem.ValidateLeaf(leaf));

    [Fact]
    public void LeafPolicy_RefusesMalformedUtf16AndUtf8ByteOverflow()
    {
        Assert.Throws<ArgumentException>(() => LinuxOutputFileSystem.ValidateLeaf(new string('\uD800', 1)));
        Assert.Throws<ArgumentException>(() => LinuxOutputFileSystem.ValidateLeaf(new string('界', 86)));
        LinuxOutputFileSystem.ValidateLeaf(new string('界', 85));
    }

    [Fact]
    public void PublishNew_CreatesExactOwnedBytesAndIdentity_ReadsOwnedSnapshot()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = Path.Combine(fixture.Root, "nested", "output");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        byte[] original = Encoding.UTF8.GetBytes("owned bytes, including \0 tail");
        byte[] expected = original.ToArray();
        var published = output.PublishNew("one.bin", original);
        original.AsSpan().Fill(0xEE);
        Assert.Equal(Path.Combine(root, "one.bin"), published.FullPath);
        Assert.Equal(expected.Length, published.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), published.Sha256);
        Assert.Equal(expected, File.ReadAllBytes(published.FullPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(published.FullPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(root)!));
        var snapshot = Assert.IsType<LinuxOwnedOutputDirectory.Snapshot>(output.ReadSnapshot("one.bin"));
        Assert.Equal(expected, snapshot.Bytes.ToArray());
        Assert.Equal(published.Identity, snapshot.Identity);
        Assert.Equal(1U, snapshot.Observation.LinkCount);
        Assert.Equal(LinuxOutputFileSystem.EffectiveUserId, snapshot.Observation.OwnerId);
        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Success,
            FileSystemReparseGuard.TryOpenVerifiedRead(root, published.FullPath, out var verified));
        using (verified) Assert.Equal(published.Identity, verified!.Identity);
        output.PublishNew("empty.bin", ReadOnlySpan<byte>.Empty);
        Assert.Empty(output.ReadSnapshot("empty.bin")!.Bytes.ToArray());
        Assert.Null(output.ReadSnapshot("absent.bin"));
        // The returned snapshot owns bytes; a later filesystem write must not alter it.
        File.WriteAllText(published.FullPath, "changed later");
        Assert.Equal(expected, snapshot.Bytes.ToArray());
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    public void PublishNew_CollisionNeverClobbersExistingObject(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string outside = fixture.Write("unrelated.bin", "keep outside");
        string leaf = Path.Combine(root, "occupied.bin");
        if (kind == "regular") File.WriteAllText(leaf, "keep existing");
        else if (kind == "symlink") File.CreateSymbolicLink(leaf, outside);
        else fixture.Run("ln", outside, leaf);
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() =>
            output.PublishNew("occupied.bin", new byte[] { 1, 2, 3 }));
        Assert.Equal(17, error.Errno);
        Assert.Equal("keep outside", File.ReadAllText(outside));
        Assert.Equal(kind == "regular" ? "keep existing" : "keep outside", File.ReadAllText(leaf));
        if (kind == "symlink") Assert.Equal(outside, new FileInfo(leaf).LinkTarget);
        output.PublishNew("different.bin", new byte[] { 4 }); // EEXIST did not poison the capability.
    }

    [Fact]
    public async Task ConcurrentPublish_ExactlyOneCreateWins()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        using var first = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        using var second = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        async Task<bool> Attempt(LinuxOwnedOutputDirectory directory, byte value) => await Task.Run(() =>
        {
            try { directory.PublishNew("winner.bin", new[] { value }); return true; }
            catch (LinuxReadFileSystem.LinuxNativeIOException ex) when (ex.Errno == 17) { return false; }
        });
        bool[] results = await Task.WhenAll(Attempt(first, 1), Attempt(second, 2));
        Assert.Single(results.Where(won => won));
        Assert.Contains(File.ReadAllBytes(Path.Combine(root, "winner.bin"))[0], new byte[] { 1, 2 });
        Assert.Single(System.IO.Directory.GetFiles(root));
    }

    [Fact]
    public void Factory_RefusesLinkedAncestors_AndLeavesExistingPermissionsAlone()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string outside = fixture.Directory("outside");
        string link = Path.Combine(fixture.Root, "link");
        System.IO.Directory.CreateSymbolicLink(link, outside);
        Assert.ThrowsAny<IOException>(() => LinuxOwnedOutputDirectory.OpenOrCreate(Path.Combine(link, "new")));
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(outside));
        UnixFileMode unsafeMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(outside, unsafeMode);
        Assert.Throws<IOException>(() => LinuxOwnedOutputDirectory.OpenOrCreate(outside));
        Assert.Equal(unsafeMode, File.GetUnixFileMode(outside));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc/ffx-refuse")]
    [InlineData("/proc/ffx-refuse")]
    [InlineData("relative")]
    [InlineData("C:\\Windows\\output")]
    [InlineData("/tmp/../ffx-refuse")]
    public void Factory_RefusesUnsafeRootsBeforeCreating(string path)
    {
        if (!CanRunNative()) return;
        Assert.ThrowsAny<ArgumentException>(() => LinuxOwnedOutputDirectory.OpenOrCreate(path));
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("directory")]
    [InlineData("fifo")]
    [InlineData("oversized")]
    [InlineData("hardlink")]
    public void Snapshot_RefusesSpecialLinkedOrOversizedFilesWithoutBlocking(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string path = Path.Combine(root, "input");
        if (kind == "directory") TestDirectory.CreatePrivate(path);
        else if (kind == "symlink") File.CreateSymbolicLink(path, fixture.Write("outside", "keep"));
        else if (kind == "hardlink") fixture.Run("ln", fixture.Write("outside", "keep"), path);
        else if (kind == "fifo") fixture.Run("mkfifo", path);
        else
        {
            using (var stream = File.Create(path)) stream.SetLength(LinuxOwnedOutputDirectory.MaximumBytes + 1);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        var watch = Stopwatch.StartNew();
        var error = Assert.ThrowsAny<IOException>(() => output.ReadSnapshot("input"));
        if (kind == "oversized") Assert.Contains("64 MiB", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Snapshot_RejectsObservedReplacementOrInPlaceRewrite(bool replace)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string path = fixture.Write("output/input", "original");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "snapshot-open") return;
            if (replace) File.Move(path, path + ".retained");
            File.WriteAllText(path, "replaced");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        };
        try { Assert.Throws<IOException>(() => output.ReadSnapshot("input")); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal("replaced", File.ReadAllText(path));
        if (replace) Assert.Equal("original", File.ReadAllText(path + ".retained"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryRename_IsRefusedBeforeLink_OrUncertainAfterLink(bool afterLink)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string moved = Path.Combine(fixture.Root, "moved");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != (afterLink ? "create-linked" : "create-before-link")) return;
            System.IO.Directory.Move(root, moved);
            TestDirectory.CreatePrivate(root);
            File.WriteAllText(Path.Combine(root, "file.bin"), "unrelated replacement");
        };
        try
        {
            if (afterLink)
            {
                var error = Assert.Throws<LinuxPublicationUncertainException>(() => output.PublishNew("file.bin", new byte[] { 7 }));
                Assert.Equal(Path.Combine(root, "file.bin"), error.PossiblyPublishedPath);
                Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(moved, "file.bin")));
                Assert.Throws<InvalidOperationException>(() => output.PublishNew("again.bin", new byte[] { 8 }));
            }
            else
            {
                Assert.Throws<IOException>(() => output.PublishNew("file.bin", new byte[] { 7 }));
                Assert.Empty(System.IO.Directory.GetFileSystemEntries(moved));
            }
            Assert.Equal("unrelated replacement", File.ReadAllText(Path.Combine(root, "file.bin")));
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
    }

    [Fact]
    public void PostLinkIoFailure_PreservesFileAndFaultsCapability()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage == "create-linked") throw new IOException("injected post-link pre-fsync failure");
        };
        try { Assert.Throws<LinuxPublicationUncertainException>(() => output.PublishNew("uncertain.bin", new byte[] { 9 })); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(root, "uncertain.bin")));
        Assert.Throws<InvalidOperationException>(() => output.PublishNew("again.bin", new byte[] { 8 }));
    }

    // ── Descriptor oracle controls ──
    // Raw proc targets retain deleted/anonymous names. Held handles below make the positive
    // leak detector deterministic; an unrelated prefix is not ownership of this fixture.
    [Theory]
    [InlineData("regular")]
    [InlineData("unlinked")]
    [InlineData("directory")]
    [InlineData("anonymous")]
    [InlineData("deleted-directory")]
    public void FixtureDescriptorOracle_ObservesEveryOwnedHandleUntilDisposed(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("probe");
        fixture.Write("probe/held.bin", "keep descriptor");
        string child = fixture.Directory("probe/child");
        using var directory = LinuxReadFileSystem.OpenRoot(root);
        var before = LinuxFixtureDescriptors.Capture(fixture.Root);
        Assert.Single(before);
        using var held = kind switch
        {
            "regular" or "unlinked" => LinuxReadFileSystem.OpenRead(directory, "held.bin"),
            "directory" or "deleted-directory" => LinuxReadFileSystem.OpenRoot(child),
            "anonymous" => LinuxOutputFileSystem.CreateAnonymous(directory),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (kind == "unlinked") File.Delete(Path.Combine(root, "held.bin"));
        if (kind == "deleted-directory") System.IO.Directory.Delete(child);
        var during = LinuxFixtureDescriptors.Capture(fixture.Root);
        Assert.Equal(before.Length + 1, during.Length);
        var added = Assert.Single(during.Where(value =>
            value.Number == held.DangerousGetHandle().ToInt32()));
        Assert.StartsWith(root + "/", added.Target, StringComparison.Ordinal);
        if (kind is "unlinked" or "anonymous" or "deleted-directory")
            Assert.EndsWith(" (deleted)", added.Target);
        held.Dispose();
        Assert.Equal(before, LinuxFixtureDescriptors.Capture(fixture.Root));
        directory.Dispose();
        Assert.Empty(LinuxFixtureDescriptors.Capture(fixture.Root));
    }

    [Fact]
    public void FixtureDescriptorOracle_DoesNotCountANeighboringPrefix()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("probe");
        string neighbor = fixture.Directory("probe-neighbor");
        fixture.Write("probe-neighbor/held.bin", "outside selected root");
        using var owned = LinuxReadFileSystem.OpenRoot(root);
        var before = LinuxFixtureDescriptors.Capture(root);
        Assert.Single(before);
        using var otherDirectory = LinuxReadFileSystem.OpenRoot(neighbor);
        using var otherFile = LinuxReadFileSystem.OpenRead(otherDirectory, "held.bin");
        Assert.Equal(before, LinuxFixtureDescriptors.Capture(root));
        owned.Dispose();
        Assert.Empty(LinuxFixtureDescriptors.Capture(root));
    }

    [Theory]
    [InlineData("/fixture", true)]
    [InlineData("/fixture/child", true)]
    [InlineData("/fixture/file (deleted)", true)]
    [InlineData("/fixture (deleted)", true)]
    [InlineData("/fixture-neighbor/child", false)]
    [InlineData("/fixtures", false)]
    [InlineData("anon_inode:[eventpoll]", false)]
    public void FixtureDescriptorOracle_UsesExactRootBoundaryAndRawDeletedTargets(
        string target, bool expected) =>
        Assert.Equal(expected, LinuxFixtureDescriptors.IsWithinRoot("/fixture", target));

    [Fact]
    public void RepeatedSuccessAndRefusal_DoNotLeakDescriptors_AndDisposeIsEnforced()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        output.PublishNew("occupied.bin", new byte[] { 0 });
        // Late runtime assembly loads are not output leaks. Measure only this unmoved fixture,
        // require the held root to be visible, and allow no extra descriptor after any cycle.
        var before = LinuxFixtureDescriptors.Capture(fixture.Root);
        Assert.Single(before);
        for (int i = 0; i < 64; i++)
        {
            output.PublishNew($"{i}.bin", new byte[] { (byte)i });
            Assert.NotNull(output.ReadSnapshot($"{i}.bin"));
            Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() => output.PublishNew("occupied.bin", new byte[] { 4 }));
        }
        Assert.Equal(before, LinuxFixtureDescriptors.Capture(fixture.Root));
        output.Dispose();
        Assert.Empty(LinuxFixtureDescriptors.Capture(fixture.Root));
        Assert.Throws<ObjectDisposedException>(() => output.PublishNew("disposed.bin", new byte[] { 1 }));
        Assert.Throws<ObjectDisposedException>(() => output.ReadSnapshot("occupied.bin"));
    }

    [Fact]
    public void PublishNew_OversizedCaptureIsRefusedBeforeCreatingAnyLeaf()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        var bytes = new byte[LinuxOwnedOutputDirectory.MaximumBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => output.PublishNew("too-large.bin", bytes));
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void PublishNew_CapturesCallerBufferBeforePublicationHook()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        byte[] caller = { 1, 2, 3 };
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage == "create-before-link") caller.AsSpan().Fill(9);
        };
        try { output.PublishNew("captured.bin", caller); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 9, 9, 9 }, caller);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(root, "captured.bin")));
    }

    [Fact]
    public void Snapshot_RejectsSharedWritePermissions_ThenReadsUnchangedPrivateFile()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string path = fixture.Write("output/file.bin", "unchanged");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);
        Assert.Throws<IOException>(() => output.ReadSnapshot("file.bin"));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal("unchanged", Encoding.UTF8.GetString(output.ReadSnapshot("file.bin")!.Bytes));
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    public void PostLinkReplacement_IsUncertainAndPreservesBothObjects(string replacement)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = fixture.Directory("output");
        string outside = fixture.Write("outside", "unrelated");
        string path = Path.Combine(root, "file.bin");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "create-linked") return;
            File.Move(path, path + ".retained");
            if (replacement == "symlink") File.CreateSymbolicLink(path, outside);
            else if (OperatingSystem.IsLinux()) fixture.Run("ln", outside, path);
            else throw new PlatformNotSupportedException("This hook requires Linux.");
        };
        try { Assert.Throws<LinuxPublicationUncertainException>(() => output.PublishNew("file.bin", new byte[] { 2 })); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal("unrelated", File.ReadAllText(outside));
        Assert.Equal("unrelated", File.ReadAllText(path));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path + ".retained"));
        Assert.Throws<InvalidOperationException>(() => output.PublishNew("again.bin", new byte[] { 3 }));
    }

    [Fact]
    public void CandidateFilesystem_IsValidatedBeforeCreatingDirectories()
    {
        if (!CanRunNative()) return;
        // /tmp is tmpfs on the validation host, but may be ext4 elsewhere. Both branches
        // perform real assertions; no platform gets a vacuous return for this filesystem gate.
        string fixture = System.IO.Directory.CreateTempSubdirectory("ffx-output-filesystem-").FullName;
        try
        {
            using var directory = LinuxReadFileSystem.OpenRoot(fixture);
            Exception? unsupported = Record.Exception(() => LinuxOutputFileSystem.VerifyFileSystem(directory));
            string requested = Path.Combine(fixture, "child");
            if (unsupported == null)
            {
                using var output = LinuxOwnedOutputDirectory.OpenOrCreate(requested);
                output.PublishNew("real.bin", new byte[] { 1 });
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(requested, "real.bin")));
            }
            else
            {
                Assert.IsType<IOException>(unsupported); // A syscall failure is not an unsupported-filesystem result.
                Assert.Throws<IOException>(() => LinuxOwnedOutputDirectory.OpenOrCreate(requested));
                Assert.Empty(System.IO.Directory.GetFileSystemEntries(fixture));
            }
        }
        finally { System.IO.Directory.Delete(fixture, recursive: true); }
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (LinuxReadFileSystem.IsSupported) return true;
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOwnedOutputDirectory.OpenOrCreate("/tmp/ffx-never-created"));
        return false;
    }

    [SupportedOSPlatform("linux")]
    private sealed class Fixture : IDisposable
    {
        // /tmp may be tmpfs, deliberately outside the supported output contract. Keep these
        // owned tests on the checkout's local filesystem, never beside private game inputs.
        internal string Root { get; } = TestDirectory.CreatePrivate(Path.Combine(
            TestDataPaths.RepoRoot, "work", "linux-output-tests", Guid.NewGuid().ToString("N"))).FullName;
        internal string Directory(string name) => TestDirectory.CreatePrivate(Path.Combine(Root, name)).FullName;
        internal string Write(string relative, string text)
        {
            string path = Path.Combine(Root, relative);
            TestDirectory.CreatePrivate(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return path;
        }
        internal void Run(string command, params string[] arguments)
        {
            var info = new ProcessStartInfo(command) { UseShellExecute = false };
            foreach (string value in arguments) info.ArgumentList.Add(value);
            using var process = Process.Start(info)!;
            Assert.True(process.WaitForExit(5000), command + " timed out");
            Assert.Equal(0, process.ExitCode);
        }
        public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
    }
}
