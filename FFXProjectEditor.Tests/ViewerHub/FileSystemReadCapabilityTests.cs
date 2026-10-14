using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Retained read capability regressions ────────────────────────────────────────
// These tests exercise real filesystem objects. A successful open owns the exact object selected
// by the kernel; it does not promise immutable bytes or continued membership beneath a pathname.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class FileSystemReadCapabilityTests
{
    [Fact]
    public void OpenedLeaf_RemainsReadableAfterDirectoryCloseAndPathReplacement()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "original");
        string root = Path.GetDirectoryName(source)!;

        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var directory));
        FileSystemReparseGuard.VerifiedReadFile? retained;
        using (directory)
        {
            Assert.Equal(
                FileSystemReparseGuard.VerifiedOpenResult.Success,
                FileSystemReparseGuard.TryOpenVerifiedRead(directory!, "leaf.bin", out retained));
        }

        using (retained)
        {
            FileSystemReparseGuard.FileIdentity originalIdentity = retained!.Identity;
            File.Move(source, source + ".old");
            File.WriteAllText(source, "replacement");

            AssertRead(retained, "original");
            Assert.Equal(
                FileSystemReparseGuard.VerifiedOpenResult.Success,
                FileSystemReparseGuard.TryOpenVerifiedRead(root, source, out var replacement));
            using (replacement)
                Assert.NotEqual(originalIdentity, replacement!.Identity);
        }
    }

    [Fact]
    public void RetainedDirectory_RenameUsesOriginalObjectOnLinux_AndWindowsRejectsFinalPathDrift()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "original");
        string root = Path.GetDirectoryName(source)!;
        string movedRoot = sandbox.PathFor("retained-root");

        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var directory));
        using (directory)
        {
            Directory.Move(root, movedRoot);
            sandbox.Write("root/leaf.bin", "replacement");

            FileSystemReparseGuard.VerifiedOpenResult result =
                FileSystemReparseGuard.TryOpenVerifiedRead(directory!, "leaf.bin", out var opened);
            using (opened)
            {
                if (OperatingSystem.IsLinux())
                {
                    Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Success, result);
                    AssertRead(opened!, "original");
                }
                else
                {
                    Assert.True(OperatingSystem.IsWindows(), "Only Linux x64 and Windows are supported.");
                    Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected, result);
                    Assert.Null(opened);
                }
            }
        }
    }

    [Theory]
    [InlineData(LinkAttack.LinkedRoot)]
    [InlineData(LinkAttack.LinkedAncestor)]
    [InlineData(LinkAttack.LinkedChildDirectory)]
    [InlineData(LinkAttack.LinkedLeafExistingTarget)]
    [InlineData(LinkAttack.LinkedLeafMissingTarget)]
    public void LinkedPathComponents_AreRejectedWithoutReadingOutsideBytes(LinkAttack attack)
    {
        using var sandbox = new OwnedReadFixture();
        string outsideFile = sandbox.Write("outside/secret.bin", "outside-secret");
        string trustedRoot = sandbox.CreateDirectory("trusted");
        string candidate;

        switch (attack)
        {
            case LinkAttack.LinkedRoot:
                trustedRoot = sandbox.LinkDirectory("linked-root", Path.GetDirectoryName(outsideFile)!);
                candidate = Path.Combine(trustedRoot, "secret.bin");
                break;
            case LinkAttack.LinkedAncestor:
                string actualParent = sandbox.CreateDirectory("actual-parent");
                sandbox.Write("actual-parent/trusted/secret.bin", "outside-secret");
                string linkedAncestor = sandbox.LinkDirectory("linked-ancestor", actualParent);
                trustedRoot = Path.Combine(linkedAncestor, "trusted");
                candidate = Path.Combine(trustedRoot, "secret.bin");
                break;
            case LinkAttack.LinkedChildDirectory:
                string linkedChild = sandbox.LinkDirectory(
                    "trusted/linked-child",
                    Path.GetDirectoryName(outsideFile)!);
                candidate = Path.Combine(linkedChild, "secret.bin");
                break;
            case LinkAttack.LinkedLeafExistingTarget:
                candidate = sandbox.LinkFile("trusted/leaf.bin", outsideFile);
                break;
            case LinkAttack.LinkedLeafMissingTarget:
                candidate = sandbox.LinkFile("trusted/missing.bin", sandbox.PathFor("absent/secret.bin"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(attack));
        }

        FileSystemReparseGuard.VerifiedOpenResult result =
            FileSystemReparseGuard.TryOpenVerifiedRead(trustedRoot, candidate, out var opened);

        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected, result);
        Assert.Null(opened);
    }

    [Fact]
    public void MissingDescendants_AreNotFound_WhileMalformedAndNonFileInputsAreRejected()
    {
        using var sandbox = new OwnedReadFixture();
        string root = sandbox.CreateDirectory("root");
        string sibling = sandbox.Write("root-sibling/secret.bin", "outside-secret");
        string directoryCandidate = sandbox.CreateDirectory("root/as-directory.bin");

        AssertOpenResult(root, Path.Combine(root, "missing.bin"),
            FileSystemReparseGuard.VerifiedOpenResult.NotFound);
        AssertOpenResult(root, Path.Combine(root, "missing", "leaf.bin"),
            FileSystemReparseGuard.VerifiedOpenResult.NotFound);
        AssertOpenResult("relative-root", "relative-root/leaf.bin",
            FileSystemReparseGuard.VerifiedOpenResult.Rejected);
        AssertOpenResult(root, "relative-leaf.bin",
            FileSystemReparseGuard.VerifiedOpenResult.Rejected);
        AssertOpenResult(root, sibling,
            FileSystemReparseGuard.VerifiedOpenResult.Rejected);
        AssertOpenResult(root, directoryCandidate,
            FileSystemReparseGuard.VerifiedOpenResult.Rejected);
        Assert.False(FileSystemReparseGuard.TryOpenVerifiedDirectory("relative-root", out var directory));
        Assert.Null(directory);
    }

    [Fact]
    public void Identity_IsStableForSameObject_AndTracksHostCaseRules()
    {
        using var sandbox = new OwnedReadFixture();
        string lower = sandbox.Write("root/case.bin", "lower");
        string root = Path.GetDirectoryName(lower)!;

        using FileSystemReparseGuard.VerifiedReadFile first = Open(root, lower);
        using FileSystemReparseGuard.VerifiedReadFile second = Open(root, lower);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(
            OperatingSystem.IsLinux()
                ? FileSystemReparseGuard.FileIdentityKind.Linux
                : FileSystemReparseGuard.FileIdentityKind.Windows,
            first.Identity.Kind);

        if (OperatingSystem.IsLinux())
        {
            string upper = sandbox.Write("root/CASE.bin", "upper");
            using FileSystemReparseGuard.VerifiedReadFile distinct = Open(root, upper);
            Assert.NotEqual(first.Identity, distinct.Identity);
            AssertRead(first, "lower");
            AssertRead(distinct, "upper");
        }
        else
        {
            Assert.True(OperatingSystem.IsWindows(), "Only Linux x64 and Windows are supported.");
            using FileSystemReparseGuard.VerifiedReadFile same = Open(root, Path.Combine(root, "CASE.bin"));
            Assert.Equal(first.Identity, same.Identity);
            AssertRead(same, "lower");
        }
    }

    [Fact]
    public void DisposedDirectoryRefusesRelativeOpen_AndDisposedReadReportsObjectDisposed()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "bytes");
        string root = Path.GetDirectoryName(source)!;

        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var directory));
        Assert.Equal(
            FileSystemReparseGuard.VerifiedOpenResult.Success,
            FileSystemReparseGuard.TryOpenVerifiedRead(directory!, "leaf.bin", out var opened));
        directory!.Dispose();

        Assert.Equal(
            FileSystemReparseGuard.VerifiedOpenResult.Rejected,
            FileSystemReparseGuard.TryOpenVerifiedRead(directory, "leaf.bin", out var afterDispose));
        Assert.Null(afterDispose);

        opened!.Dispose();
        Assert.Throws<ObjectDisposedException>(() => opened.Stream.ReadByte());
    }

    [Fact]
    public void RepeatedSuccessfulAndRefusedOpens_DoNotLeakLinuxDescriptors()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "bytes");
        string root = Path.GetDirectoryName(source)!;
        string outside = sandbox.Write("outside/secret.bin", "secret");
        string linked = sandbox.LinkFile("root/linked.bin", outside);
        int before = OperatingSystem.IsLinux() ? CountLinuxFileDescriptors() : 0;

        for (int index = 0; index < 128; index++)
        {
            using FileSystemReparseGuard.VerifiedReadFile opened = Open(root, source);
            Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
                FileSystemReparseGuard.TryOpenVerifiedRead(root, linked, out var refused));
            Assert.Null(refused);
        }

        if (OperatingSystem.IsLinux())
        {
            int after = CountLinuxFileDescriptors();
            Assert.True(after <= before + 3, $"File descriptor count grew from {before} to {after}.");
        }
        else
        {
            Assert.True(OperatingSystem.IsWindows(), "Only Linux x64 and Windows are supported.");
            Assert.True(File.Exists(source));
        }
    }

    [Fact]
    public void ReadHooks_KeepExistingOperationNames()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "bytes");
        string root = Path.GetDirectoryName(source)!;
        var operations = new List<string>();
        FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, _) => operations.Add(operation);

        try
        {
            using FileSystemReparseGuard.VerifiedReadFile direct = Open(root, source);
            Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var directory));
            using (directory)
            {
                Assert.Equal(
                    FileSystemReparseGuard.VerifiedOpenResult.Success,
                    FileSystemReparseGuard.TryOpenVerifiedRead(directory!, "leaf.bin", out var relative));
                relative!.Dispose();
            }
        }
        finally
        {
            FileSystemReparseGuard.BeforeHandleOperationForTests = null;
        }

        Assert.Equal(new[] { "open-read", "open-read-relative" }, operations);
    }

    [Fact]
    public void LinuxReadDirectory_DoesNotEnableMutationOrStrictStagingReads()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.True(OperatingSystem.IsWindows(), "Only Linux x64 and Windows are supported.");
            return;
        }

        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "bytes");
        string root = Path.GetDirectoryName(source)!;
        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var directory));
        using (directory)
        {
            Assert.Equal(
                FileSystemReparseGuard.VerifiedOpenResult.Rejected,
                FileSystemReparseGuard.TryOpenVerifiedMutationRead(directory!, "leaf.bin", out var mutation));
            Assert.Null(mutation);
            Assert.Equal(
                FileSystemReparseGuard.VerifiedOpenResult.Rejected,
                FileSystemReparseGuard.TryOpenVerifiedStagingRead(directory!, "leaf.bin", out var staging));
            Assert.Null(staging);
        }
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("create")]
    [InlineData("promote")]
    [InlineData("promote-unchanged")]
    [InlineData("identity")]
    [InlineData("delete")]
    public void UnsupportedMutationEntries_RefuseBeforeCallingWindowsLibraries(string operation)
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "preserved");
        string root = Path.GetDirectoryName(source)!;
        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var openedDirectory));
        using var directory = openedDirectory!;
        // On Windows, deliberately tag a real owned handle as foreign. On Linux, exercise the
        // genuine read capability. Neither is an authorized input to the Windows mutation backend.
        var refused = OperatingSystem.IsWindows()
            ? new FileSystemReparseGuard.VerifiedDirectory(directory.Handle, root, FileSystemReparseGuard.FileIdentityKind.Linux)
            : directory;
        string temporaryPath = sandbox.Write("root/temporary.bin", "temporary");
        using var temporary = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        using var read = Open(root, source);
        string[] before = Directory.GetFileSystemEntries(root).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        switch (operation)
        {
            case "lock": Assert.Throws<PlatformNotSupportedException>(() => FileSystemReparseGuard.OpenOrCreateExclusiveFile(refused, "lock.bin")); break;
            case "create": Assert.Throws<PlatformNotSupportedException>(() => FileSystemReparseGuard.CreateNewVerifiedFile(refused, "created.bin")); break;
            case "promote": Assert.Throws<PlatformNotSupportedException>(() => FileSystemReparseGuard.PromoteOpenedFile(refused, temporary, "leaf.bin")); break;
            case "promote-unchanged": Assert.Throws<PlatformNotSupportedException>(() => FileSystemReparseGuard.PromoteOpenedFileIfUnchanged(
                refused, temporary, "leaf.bin", FileSystemReparseGuard.VerifiedOpenResult.Success, read.Identity)); break;
            case "identity": Assert.False(FileSystemReparseGuard.IsOpenedFileIdentityCurrent(refused, "leaf.bin", read.Identity)); break;
            case "delete": Assert.False(FileSystemReparseGuard.TryDeleteVerifiedFile(refused, "leaf.bin", read.Identity)); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        Assert.Equal(before, Directory.GetFileSystemEntries(root).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        // Windows ReadAllText uses FileShare.Read, incompatible with our still-open writable
        // fixture. Close it after the refused operations, before checking preserved disk bytes.
        temporary.Dispose();
        Assert.Equal("preserved", File.ReadAllText(source));
        Assert.Equal("temporary", File.ReadAllText(temporaryPath));
    }

    [Fact]
    public void DeleteOpenedFile_UsesOnlyTheWindowsBackendAndPreservesLinuxBytes()
    {
        using var sandbox = new OwnedReadFixture();
        string path = sandbox.Write("root/keep.bin", "preserved");
        if (OperatingSystem.IsWindows())
        {
            using var directory = FileSystemReparseGuard.OpenOrCreateVerifiedDirectory(Path.GetDirectoryName(path)!);
            using (var owned = FileSystemReparseGuard.CreateNewVerifiedFile(directory, "delete-me.bin"))
                FileSystemReparseGuard.DeleteOpenedFile(owned);
            Assert.False(File.Exists(Path.Combine(directory.FullPath, "delete-me.bin")));
        }
        else
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            Assert.Throws<PlatformNotSupportedException>(() => FileSystemReparseGuard.DeleteOpenedFile(file));
        }
        Assert.Equal("preserved", File.ReadAllText(path));
    }

    [Fact]
    public void ForeignDirectoryKind_CannotDispatchToTheCurrentReadBackend()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("root/leaf.bin", "preserved");
        Assert.True(FileSystemReparseGuard.TryOpenVerifiedDirectory(Path.GetDirectoryName(source)!, out var opened));
        using var directory = opened!;
        var wrongKind = OperatingSystem.IsWindows()
            ? FileSystemReparseGuard.FileIdentityKind.Linux : FileSystemReparseGuard.FileIdentityKind.Windows;
        var foreign = new FileSystemReparseGuard.VerifiedDirectory(directory.Handle, directory.FullPath, wrongKind);
        Assert.NotEqual(directory.Kind, foreign.Kind);
        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
            FileSystemReparseGuard.TryOpenVerifiedRead(foreign, "leaf.bin", out var read));
        Assert.Null(read);
        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
            FileSystemReparseGuard.TryOpenVerifiedMutationRead(foreign, "leaf.bin", out var mutation));
        Assert.Null(mutation);
        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
            FileSystemReparseGuard.TryOpenVerifiedStagingRead(foreign, "leaf.bin", out var staging));
        Assert.Null(staging);
        Assert.Equal("preserved", File.ReadAllText(source));
    }

    [Fact]
    public async Task StudioServer_ExactFileRouteUsesTheGuardedReadCapability()
    {
        using var sandbox = new OwnedReadFixture();
        string source = sandbox.Write("exact/overlay.json", "{\"source\":\"exact\"}");
        var server = new StudioWebServer();

        try
        {
            Assert.True(server.TryMapExactFile("/overlay.json", source));
            Assert.True(server.Start(0), server.Status);
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"),
            };

            using HttpResponseMessage response = await client.GetAsync("/overlay.json");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"source\":\"exact\"}", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            server.Stop();
        }
    }

    private static FileSystemReparseGuard.VerifiedReadFile Open(string root, string path)
    {
        Assert.Equal(
            FileSystemReparseGuard.VerifiedOpenResult.Success,
            FileSystemReparseGuard.TryOpenVerifiedRead(root, path, out var opened));
        return Assert.IsType<FileSystemReparseGuard.VerifiedReadFile>(opened);
    }

    private static void AssertOpenResult(
        string root,
        string path,
        FileSystemReparseGuard.VerifiedOpenResult expected)
    {
        Assert.Equal(expected, FileSystemReparseGuard.TryOpenVerifiedRead(root, path, out var opened));
        Assert.Null(opened);
    }

    private static void AssertRead(FileSystemReparseGuard.VerifiedReadFile opened, string expected)
    {
        opened.Stream.Position = 0;
        using var reader = new StreamReader(opened.Stream, leaveOpen: true);
        Assert.Equal(expected, reader.ReadToEnd());
    }

    private static int CountLinuxFileDescriptors() =>
        Directory.EnumerateFileSystemEntries("/proc/self/fd").Count();

    public enum LinkAttack
    {
        LinkedRoot,
        LinkedAncestor,
        LinkedChildDirectory,
        LinkedLeafExistingTarget,
        LinkedLeafMissingTarget,
    }

    private sealed class OwnedReadFixture : IDisposable
    {
        private readonly List<string> _links = new();

        internal OwnedReadFixture() => Root = Directory.CreateTempSubdirectory("ffx-read-cap-").FullName;

        internal string Root { get; }

        internal string PathFor(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

        internal string CreateDirectory(string relative) => Directory.CreateDirectory(PathFor(relative)).FullName;

        internal string Write(string relative, string content)
        {
            string path = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        internal string LinkDirectory(string relative, string target)
        {
            string link = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (
                OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
            {
                RunMklink("/J", link, target);
            }
            _links.Add(link);
            return link;
        }

        internal string LinkFile(string relative, string target)
        {
            string link = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (
                OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
            {
                RunMklink(null, link, target);
            }
            _links.Add(link);
            return link;
        }

        public void Dispose()
        {
            for (int index = _links.Count - 1; index >= 0; index--)
            {
                try { File.Delete(_links[index]); } catch { }
                try { Directory.Delete(_links[index]); } catch { }
            }
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static void RunMklink(string? kind, string link, string target)
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            if (kind != null)
                startInfo.ArgumentList.Add(kind);
            startInfo.ArgumentList.Add(link);
            startInfo.ArgumentList.Add(target);
            using Process process = Process.Start(startInfo)!;
            Assert.True(process.WaitForExit(5000), "mklink timed out");
            Assert.Equal(0, process.ExitCode);
        }
    }
}
