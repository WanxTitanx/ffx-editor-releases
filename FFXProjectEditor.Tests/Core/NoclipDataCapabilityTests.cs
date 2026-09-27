using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class NoclipDataCapabilityTests
{
    [Fact]
    public void RequiredDirectoryManifest_MatchesTheCdnCoreSet_EnrichedSetIsOptional()
    {
        Assert.Equal(
            new[] { "0c", "0d", "0e", "11", "13", "1a", "1c", "1d", "1e", "20", "21", "22" },
            NoclipDataCapability.RequiredDirectoryNames);
        Assert.Equal(
            new[] { "10", "14", "1f", "28" },
            NoclipDataCapability.EnrichedDirectoryNames);
    }

    [Fact]
    public void Validate_AcceptsCompleteLocalData_AndRejectsCorruptCriticalFile()
    {
        string root = CreateFixture(valid: true);
        try
        {
            NoclipDataCapability.Report valid = NoclipDataCapability.Validate(root);
            Assert.True(valid.Ready, string.Join(",", valid.Issues));

            File.WriteAllBytes(Path.Combine(root, "data", "FinalFantasyX", "common_textures.bin"), new byte[80]);
            NoclipDataCapability.Report corrupt = NoclipDataCapability.Validate(root);
            Assert.False(corrupt.Ready);
            Assert.Contains("CRITICAL_FILE_INVALID:common_textures.bin", corrupt.Issues);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PublicSurface_ExposesValidationOnly_AndCannotMutateTheSelectedRoot()
    {
        string[] publicOperations = typeof(NoclipDataCapability)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { nameof(NoclipDataCapability.Validate) }, publicOperations);
    }

    [Fact]
    public void Validate_RejectsReparsePointsAtTheSelectedRootAndItsAncestors()
    {
        string sandbox = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot,
            "work",
            "ffx-noclip-reparse-root-" + Guid.NewGuid().ToString("N"));
        string actualParent = Path.Combine(sandbox, "actual-parent");
        string actualRoot = Path.Combine(actualParent, "noclip");
        string linkedAncestor = Path.Combine(sandbox, "linked-ancestor");
        string linkedRoot = Path.Combine(sandbox, "linked-root");
        CreateFixtureAt(actualRoot, valid: true);
        CreateDirectoryLink(linkedAncestor, actualParent);
        CreateDirectoryLink(linkedRoot, actualRoot);

        try
        {
            NoclipDataCapability.Report ancestorReport = NoclipDataCapability.Validate(
                Path.Combine(linkedAncestor, "noclip"));
            NoclipDataCapability.Report rootReport = NoclipDataCapability.Validate(linkedRoot);

            Assert.False(ancestorReport.Ready);
            Assert.Contains(ancestorReport.Issues, issue =>
                issue.StartsWith("REPARSE_POINT_REJECTED:", StringComparison.Ordinal));
            Assert.False(rootReport.Ready);
            Assert.Contains(rootReport.Issues, issue =>
                issue.StartsWith("REPARSE_POINT_REJECTED:", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryLink(linkedRoot);
            DeleteDirectoryLink(linkedAncestor);
            try { Directory.Delete(sandbox, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Validate_RejectsReparseRequiredDirectoriesAndCriticalFiles()
    {
        string root = CreateFixture(valid: true);
        string ffx = Path.Combine(root, "data", "FinalFantasyX");
        string externalDirectory = root + "-external-directory";
        string externalCriticalDirectory = root + "-external-critical";
        string requiredDirectory = Path.Combine(ffx, "0e");
        string criticalFile = Path.Combine(ffx, "common_textures.bin");
        Directory.Delete(requiredDirectory, recursive: true);
        Directory.CreateDirectory(externalDirectory);
        Directory.CreateDirectory(externalCriticalDirectory);
        CreateDirectoryLink(requiredDirectory, externalDirectory);
        File.Delete(criticalFile);
        // A directory junction deliberately named like the critical file is a portable
        // no-admin Windows reparse fixture. Validation must reject the node before File.Exists.
        CreateDirectoryLink(criticalFile, externalCriticalDirectory);

        try
        {
            NoclipDataCapability.Report report = NoclipDataCapability.Validate(root);

            Assert.False(report.Ready);
            Assert.Contains(report.Issues, issue =>
                issue == "REPARSE_POINT_REJECTED:0e");
            Assert.Contains(report.Issues, issue =>
                issue == "REPARSE_POINT_REJECTED:common_textures.bin");
        }
        finally
        {
            DeleteDirectoryLink(criticalFile);
            DeleteDirectoryLink(requiredDirectory);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(externalDirectory, recursive: true); } catch { }
            try { Directory.Delete(externalCriticalDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Validate_RejectsMissingOrEmptyCoreDirectories()
    {
        string root = CreateFixture(valid: true);
        string data = Path.Combine(root, "data", "FinalFantasyX");
        try
        {
            Directory.Delete(Path.Combine(data, "13"), recursive: true);
            File.Delete(Path.Combine(data, "22", "0000.bin"));

            NoclipDataCapability.Report report = NoclipDataCapability.Validate(root);

            Assert.False(report.Ready);
            Assert.Contains("REQUIRED_DIRECTORY_MISSING:13", report.Issues);
            Assert.Contains("REQUIRED_DIRECTORY_EMPTY:22", report.Issues);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Validate_EnrichedDirectoriesAreInformationalAndNeverBlock()
    {
        string root = CreateFixture(valid: true);
        string data = Path.Combine(root, "data", "FinalFantasyX");
        try
        {
            // The fixture only creates core dirs; enriched absence is reported but never blocks.
            NoclipDataCapability.Report report = NoclipDataCapability.Validate(root);

            Assert.True(report.Ready, string.Join(",", report.Issues));
            Assert.Equal(new[] { "10", "14", "1f", "28" }, report.MissingEnriched);

            Directory.CreateDirectory(Path.Combine(data, "10"));
            File.WriteAllBytes(Path.Combine(data, "10", "0001.bin"), new byte[] { 0x01 });
            report = NoclipDataCapability.Validate(root);
            Assert.Equal(new[] { "14", "1f", "28" }, report.MissingEnriched);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ServeBinding_AcceptsRootA_AndRejectsRootBAfterSelectionChanges()
    {
        string rootA = CreateFixture(valid: true);
        string rootB = CreateFixture(valid: true);
        string sandbox = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot,
            "work",
            "ffx-noclip-serve-binding-" + Guid.NewGuid().ToString("N"));
        string serveDir = rootA;
        string? dataLink = null;

        try
        {
            Assert.True(NoclipDataCapability.Validate(rootA).Ready);
            Assert.True(NoclipDataCapability.Validate(rootB).Ready);
            Assert.True(ViewerHubService.IsNoclipServeDirBoundToRoot(rootA, rootA));
            Assert.False(ViewerHubService.IsNoclipServeDirBoundToRoot(
                Path.Combine(sandbox, "missing-serve"),
                rootA));

            if (OperatingSystem.IsWindows())
            {
                serveDir = Path.Combine(sandbox, "serve");
                Directory.CreateDirectory(serveDir);
                dataLink = Path.Combine(serveDir, "data");
                RunMklink("/J", dataLink, Path.Combine(rootA, "data"));
            }

            Assert.True(ViewerHubService.IsNoclipServeDirBoundToRoot(serveDir, rootA));
            Assert.False(ViewerHubService.IsNoclipServeDirBoundToRoot(serveDir, rootB));
        }
        finally
        {
            if (dataLink != null)
                DeleteDirectoryLink(dataLink);
            try { Directory.Delete(sandbox, recursive: true); } catch { }
            try { Directory.Delete(rootA, recursive: true); } catch { }
            try { Directory.Delete(rootB, recursive: true); } catch { }
        }
    }

    private static string CreateFixture(bool valid)
    {
        string root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx-noclip-capability-" + Guid.NewGuid().ToString("N"));
        CreateFixtureAt(root, valid);
        return root;
    }

    private static void CreateFixtureAt(string root, bool valid)
    {
        string data = Path.Combine(root, "data", "FinalFantasyX");
        foreach (string directory in NoclipDataCapability.RequiredDirectoryNames)
        {
            string path = Path.Combine(data, directory);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "0000.bin"), new byte[] { 0x01 });
        }

        WriteCritical(Path.Combine(data, "common_textures.bin"), 0x3C, valid);
        WriteCritical(Path.Combine(data, "screen_shatter.bin"), 0x3C, true);
        WriteCritical(Path.Combine(data, "env_map_texture.bin"), 0x18, true);
    }

    private static void WriteCritical(string file, int field, bool valid)
    {
        byte[] bytes = new byte[128];
        BitConverter.GetBytes(valid ? 96u : 0u).CopyTo(bytes, field);
        File.WriteAllBytes(file, bytes);
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (
            OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
        {
            RunMklink("/J", link, target);
        }
    }

    private static void RunMklink(string? kind, string link, string target)
    {
        string workingDirectory = Environment.CurrentDirectory;
        Assert.True(
            TryGetPathAttributes(target, out FileAttributes targetAttributes, out _) &&
            (targetAttributes & FileAttributes.Directory) != 0,
            "mklink target must be an existing directory before invocation. " +
            BuildMklinkDiagnostic(link, target, workingDirectory, null, null, null));
        bool linkExists = TryGetPathAttributes(link, out _, out string? linkError);
        Assert.True(
            !linkExists && linkError is nameof(FileNotFoundException) or nameof(DirectoryNotFoundException),
            "mklink link path must be absent before invocation. " +
            BuildMklinkDiagnostic(link, target, workingDirectory, null, null, null));

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        if (kind != null)
            startInfo.ArgumentList.Add(kind);
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) => AppendLine(stdout, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(stderr, args.Data);

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"cmd.exe could not start for mklink: {ex.GetType().Name}: {ex.Message}. " +
                BuildMklinkDiagnostic(link, target, workingDirectory, null, null, null));
            return;
        }
        Assert.True(
            started,
            "cmd.exe refused to start for mklink. " +
            BuildMklinkDiagnostic(link, target, workingDirectory, null, null, null));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit(5000);
        string? killError = null;
        bool exitedAfterKill = exited;
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                exitedAfterKill = process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                killError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        if (exitedAfterKill)
            process.WaitForExit(); // Drain both redirected streams after process termination.

        string diagnostic = BuildMklinkDiagnostic(
            link,
            target,
            workingDirectory,
            exitedAfterKill ? process.ExitCode : null,
            Snapshot(stdout),
            Snapshot(stderr));
        Assert.True(
            exited,
            $"mklink timed out and was killed (terminated={exitedAfterKill}, killError={killError ?? "<none>"}). {diagnostic}");
        Assert.True(process.ExitCode == 0, $"mklink failed. {diagnostic}");
    }

    private static bool TryGetPathAttributes(
        string path,
        out FileAttributes attributes,
        out string? error)
    {
        try
        {
            attributes = File.GetAttributes(path);
            error = null;
            return true;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            error = ex.GetType().Name;
            return false;
        }
        catch (Exception ex)
        {
            attributes = default;
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static string DescribePath(string path)
    {
        bool exists = TryGetPathAttributes(path, out FileAttributes attributes, out string? error);
        return $"path='{path}', exists={exists}, attributes={(exists ? attributes : "<unavailable>")}, error={error ?? "<none>"}";
    }

    private static string BuildMklinkDiagnostic(
        string link,
        string target,
        string workingDirectory,
        int? exitCode,
        string? stdout,
        string? stderr) =>
        $"exit={exitCode?.ToString() ?? "<not-started>"}; cwd='{workingDirectory}'; " +
        $"link=[{DescribePath(link)}]; target=[{DescribePath(target)}]; " +
        $"stdout={FormatProcessOutput(stdout)}; stderr={FormatProcessOutput(stderr)}";

    private static void AppendLine(StringBuilder destination, string? line)
    {
        if (line == null)
            return;
        lock (destination)
            destination.AppendLine(line);
    }

    private static string Snapshot(StringBuilder source)
    {
        lock (source)
            return source.ToString();
    }

    private static string FormatProcessOutput(string? output) =>
        string.IsNullOrWhiteSpace(output) ? "<empty>" : "'" + output.Trim() + "'";

    private static void DeleteDirectoryLink(string link)
    {
        try { Directory.Delete(link); } catch { }
    }
}
