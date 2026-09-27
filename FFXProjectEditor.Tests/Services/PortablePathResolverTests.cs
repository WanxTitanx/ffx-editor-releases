using System;
using System.Diagnostics;
using System.IO;
using FFXProjectEditor.Services;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.Services;

/// <summary>
/// Security contracts for product path discovery. These tests keep developer-machine
/// paths, traversal, and Windows reparse points outside the portable runtime boundary.
/// </summary>
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class PortablePathResolverTests
{
    [Fact]
    public void BundledPath_ExistingDescendant_ReturnsCanonicalPath()
    {
        string relativeRoot = $"portable-path-{Guid.NewGuid():N}";
        string root = Path.Combine(AppContext.BaseDirectory, relativeRoot);
        string file = Path.Combine(root, "tools", "tool.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
        try
        {
            Assert.Equal(
                Path.GetFullPath(file),
                PortablePathResolver.BundledPath(relativeRoot, "tools", "tool.bin"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledPath_MissingTarget_ReturnsNull()
    {
        Assert.Null(PortablePathResolver.BundledPath($"missing-{Guid.NewGuid():N}", "tool.bin"));
    }

    [Fact]
    public void BundledPath_DotDotEscape_ReturnsNull()
    {
        string parent = Directory.GetParent(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
        string leaf = $"portable-escape-{Guid.NewGuid():N}.bin";
        string escaped = Path.Combine(parent, leaf);
        File.WriteAllText(escaped, "outside");
        try
        {
            Assert.Null(PortablePathResolver.BundledPath("..", leaf));
        }
        finally
        {
            File.Delete(escaped);
        }
    }

    [Fact]
    public void BundledPath_RootedSegment_ReturnsNull()
    {
        string root = MakeTempDirectory();
        string file = Path.Combine(root, "outside.bin");
        File.WriteAllText(file, "outside");
        try
        {
            Assert.Null(PortablePathResolver.BundledPath(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledPath_RootedSegmentAfterSafeSegment_ReturnsNull()
    {
        string root = MakeTempDirectory();
        string file = Path.Combine(root, "outside.bin");
        File.WriteAllText(file, "outside");
        try
        {
            Assert.Null(PortablePathResolver.BundledPath("safe", file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledPath_PrefixSiblingEscape_ReturnsNull()
    {
        string basePath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string sibling = basePath + $"-sibling-{Guid.NewGuid():N}";
        string file = Path.Combine(sibling, "outside.bin");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(file, "outside");
        try
        {
            string relative = Path.GetRelativePath(basePath, file);
            Assert.Null(PortablePathResolver.BundledPath(relative));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Theory]
    [InlineData("FFX-3")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    public void MagicFilesRoot_InvalidGame_ReturnsNull(string game)
    {
        Assert.Null(PortablePathResolver.MagicFilesRoot(game));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../sfx-sibling")]
    [InlineData("C:\\outside")]
    [InlineData("us/subdir")]
    public void AudioSfxRoot_InvalidLocale_ReturnsNull(string locale)
    {
        Assert.Null(PortablePathResolver.AudioSfxRoot(locale));
    }

    [Fact]
    public void ResolveGameInstallRoot_ConfiguredPathAvoidsFailingDiscovery()
    {
        string safe = MakeTempDirectory();
        try
        {
            bool called = false;
            string? resolved = PortablePathResolver.ResolveGameInstallRoot(
                safe,
                null,
                () =>
                {
                    called = true;
                    throw new IOException("discovery should not run");
                });

            Assert.Equal(Path.GetFullPath(safe), resolved);
            Assert.False(called);
        }
        finally
        {
            Directory.Delete(safe, recursive: true);
        }
    }

    [Fact]
    public void ResolveGameInstallRoot_DiscoveryIoFailure_ReturnsNull()
    {
        Assert.Null(PortablePathResolver.ResolveGameInstallRoot(
            null,
            null,
            () => throw new IOException("unavailable Steam library")));
    }

    [Fact]
    public void ExtractionProperties_DoNotUseKnownDeveloperDriveFallbacks()
    {
        string? savedPs2Env = Environment.GetEnvironmentVariable("FFX_PS2_ROOT");
        string? savedPs3Env = Environment.GetEnvironmentVariable("FFX_PS3DATA_ROOT");
        string? savedPs2Override = Project_Service.FfxPs2RootOverride;
        string? savedPs3Override = Project_Service.Ps3DataRootOverride;
        string? savedProject = Project_Service.Instance.ProjectPath;
        try
        {
            Environment.SetEnvironmentVariable("FFX_PS2_ROOT", null);
            Environment.SetEnvironmentVariable("FFX_PS3DATA_ROOT", null);
            Project_Service.FfxPs2RootOverride = null;
            Project_Service.Ps3DataRootOverride = null;
            Project_Service.Instance.ProjectPath = null;

            Assert.Null(PortablePathResolver.FfxPs2Root);
            Assert.Null(PortablePathResolver.Ps3DataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFX_PS2_ROOT", savedPs2Env);
            Environment.SetEnvironmentVariable("FFX_PS3DATA_ROOT", savedPs3Env);
            Project_Service.FfxPs2RootOverride = savedPs2Override;
            Project_Service.Ps3DataRootOverride = savedPs3Override;
            Project_Service.Instance.ProjectPath = savedProject;
        }
    }

    [Fact]
    public void ExtractionProperties_DeriveOnlyFromTheLoadedProjectTree()
    {
        string root = MakeTempDirectory();
        string ffxPs2 = Path.Combine(root, "ffx_ps2");
        string master = Path.Combine(ffxPs2, "ffx", "master");
        string ps3Data = Path.Combine(root, "ffx_data", "gamedata", "ps3data");
        Directory.CreateDirectory(master);
        Directory.CreateDirectory(ps3Data);

        string? savedPs2Env = Environment.GetEnvironmentVariable("FFX_PS2_ROOT");
        string? savedPs3Env = Environment.GetEnvironmentVariable("FFX_PS3DATA_ROOT");
        string? savedPs2Override = Project_Service.FfxPs2RootOverride;
        string? savedPs3Override = Project_Service.Ps3DataRootOverride;
        string? savedProject = Project_Service.Instance.ProjectPath;
        try
        {
            Environment.SetEnvironmentVariable("FFX_PS2_ROOT", null);
            Environment.SetEnvironmentVariable("FFX_PS3DATA_ROOT", null);
            Project_Service.FfxPs2RootOverride = null;
            Project_Service.Ps3DataRootOverride = null;
            Project_Service.Instance.ProjectPath = master;

            Assert.Equal(Path.GetFullPath(ffxPs2), PortablePathResolver.FfxPs2Root);
            Assert.Equal(Path.GetFullPath(ps3Data), PortablePathResolver.Ps3DataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFX_PS2_ROOT", savedPs2Env);
            Environment.SetEnvironmentVariable("FFX_PS3DATA_ROOT", savedPs3Env);
            Project_Service.FfxPs2RootOverride = savedPs2Override;
            Project_Service.Ps3DataRootOverride = savedPs3Override;
            Project_Service.Instance.ProjectPath = savedProject;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingDirectory_SkipsRelativeMissingAndReturnsSafeAbsoluteCandidate()
    {
        string safe = MakeTempDirectory();
        try
        {
            Assert.Equal(
                Path.GetFullPath(safe),
                PortablePathResolver.FirstExistingDirectory(
                    ".",
                    Path.Combine(safe, "missing"),
                    safe));
        }
        finally
        {
            Directory.Delete(safe, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingFile_SkipsRelativeMissingAndReturnsSafeAbsoluteCandidate()
    {
        string root = MakeTempDirectory();
        string safe = Path.Combine(root, "safe.bin");
        File.WriteAllText(safe, "safe");
        try
        {
            Assert.Equal(
                Path.GetFullPath(safe),
                PortablePathResolver.FirstExistingFile(
                    "relative.bin",
                    Path.Combine(root, "missing.bin"),
                    safe));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingDirectory_SkipsJunctionAndReturnsSafeCandidate()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = MakeTempDirectory();
        string outside = MakeTempDirectory();
        string junction = Path.Combine(root, "redirected");
        string safe = Path.Combine(root, "safe");
        Directory.CreateDirectory(safe);
        CreateDirectoryLink(junction, outside);
        try
        {
            Assert.Equal(
                Path.GetFullPath(safe),
                PortablePathResolver.FirstExistingDirectory(junction, safe));
        }
        finally
        {
            DeleteDirectoryLink(junction);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingFile_SkipsJunctionAncestorAndReturnsSafeCandidate()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = MakeTempDirectory();
        string outside = MakeTempDirectory();
        string junction = Path.Combine(root, "redirected");
        string unsafeFile = Path.Combine(outside, "tool.bin");
        string safeFile = Path.Combine(root, "safe.bin");
        File.WriteAllText(unsafeFile, "outside");
        File.WriteAllText(safeFile, "safe");
        CreateDirectoryLink(junction, outside);
        try
        {
            Assert.Equal(
                Path.GetFullPath(safeFile),
                PortablePathResolver.FirstExistingFile(
                    Path.Combine(junction, "tool.bin"),
                    safeFile));
        }
        finally
        {
            DeleteDirectoryLink(junction);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void BundledPath_RejectsJunctionAncestor()
    {
        if (!OperatingSystem.IsWindows()) return;
        string relativeRoot = $"portable-path-{Guid.NewGuid():N}";
        string root = Path.Combine(AppContext.BaseDirectory, relativeRoot);
        string outside = MakeTempDirectory();
        string junction = Path.Combine(root, "redirected");
        string outsideFile = Path.Combine(outside, "tool.bin");
        Directory.CreateDirectory(root);
        File.WriteAllText(outsideFile, "outside");
        CreateDirectoryLink(junction, outside);
        try
        {
            Assert.Null(PortablePathResolver.BundledPath(relativeRoot, "redirected", "tool.bin"));
        }
        finally
        {
            DeleteDirectoryLink(junction);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingDirectory_HandleVerificationRejectsPostScanJunctionSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = MakeTempDirectory();
        string outside = MakeTempDirectory();
        string candidate = Path.Combine(root, "candidate");
        string moved = Path.Combine(root, "candidate-original");
        Directory.CreateDirectory(candidate);
        try
        {
            PortablePathResolver.BeforeVerifiedOpenForTests = (operation, path) =>
            {
                if (operation != "directory" || !string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                    return;
                PortablePathResolver.BeforeVerifiedOpenForTests = null;
                Directory.Move(candidate, moved);
                CreateDirectoryLink(candidate, outside);
            };

            Assert.Null(PortablePathResolver.FirstExistingDirectory(candidate));
        }
        finally
        {
            PortablePathResolver.BeforeVerifiedOpenForTests = null;
            DeleteDirectoryLink(candidate);
            if (Directory.Exists(moved)) Directory.Delete(moved, recursive: true);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void FirstExistingFile_HandleVerificationRejectsPostScanJunctionSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = MakeTempDirectory();
        string outside = MakeTempDirectory();
        string candidateDirectory = Path.Combine(root, "candidate");
        string movedDirectory = Path.Combine(root, "candidate-original");
        string candidate = Path.Combine(candidateDirectory, "tool.bin");
        string outsideFile = Path.Combine(outside, "tool.bin");
        Directory.CreateDirectory(candidateDirectory);
        File.WriteAllText(candidate, "original");
        File.WriteAllText(outsideFile, "outside");
        try
        {
            PortablePathResolver.BeforeVerifiedOpenForTests = (operation, path) =>
            {
                if (operation != "file" || !string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                    return;
                PortablePathResolver.BeforeVerifiedOpenForTests = null;
                Directory.Move(candidateDirectory, movedDirectory);
                CreateDirectoryLink(candidateDirectory, outside);
            };

            Assert.Null(PortablePathResolver.FirstExistingFile(candidate));
        }
        finally
        {
            PortablePathResolver.BeforeVerifiedOpenForTests = null;
            DeleteDirectoryLink(candidateDirectory);
            if (Directory.Exists(movedDirectory)) Directory.Delete(movedDirectory, recursive: true);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string MakeTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FFXProjectEditor.Tests",
            "PortablePathResolver",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(link);
            startInfo.ArgumentList.Add(target);
            using Process process = Process.Start(startInfo)!;
            Assert.True(process.WaitForExit(5000), "mklink /J timed out");
            Assert.Equal(0, process.ExitCode);
        }
    }

    private static void DeleteDirectoryLink(string link)
    {
        if (!Directory.Exists(link)) return;
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Directory.Delete(link, recursive: false);
    }
}
