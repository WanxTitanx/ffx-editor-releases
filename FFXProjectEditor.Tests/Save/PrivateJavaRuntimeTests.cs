using FFXProjectEditor.Services.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace FFXProjectEditor.Tests.Save;

// Environment variables are process-global. Disable parallel execution so the injection fixture
// cannot leak into another process-launch contract while the parent environment is being seeded.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PrivateJavaEnvironmentTestCollection
{
    public const string Name = "Private Java environment tests";
}

// ── Private FFXED runtime boundary ────────────────────────────────────────────
// These tests exercise the resolver directly without starting Java. The product action remains
// explicit, while platform refusal and package-relative trust decisions stay deterministic.
[Collection(PrivateJavaEnvironmentTestCollection.Name)]
public sealed class PrivateJavaRuntimeTests
{
    [Fact]
    public void Linux_ReturnsOptionalUnavailableBeforeInspectingPaths()
    {
        PrivateJavaLaunchResult result = PrivateJavaRuntime.CreateFfxedLaunch(
            PrivateJavaHostPlatform.Linux,
            appBaseDirectory: null!,
            appOwnedStateRoot: null!);

        Assert.Equal(PrivateJavaLaunchStatus.OptionalUnavailable, result.Status);
        Assert.Equal("OPTIONAL_UNAVAILABLE", result.ReasonCode);
        Assert.Null(result.StartInfo);
    }

    [Fact]
    public void Windows_ResolvesOnlyTheTwoExpectedAppPrivateFiles()
    {
        using var fixture = new PrivateJavaFixture();
        fixture.CreateCompleteLayout();

        PrivateJavaLaunchResult result = fixture.Resolve();

        Assert.Equal(PrivateJavaLaunchStatus.Ready, result.Status);
        ProcessStartInfo startInfo = Assert.IsType<ProcessStartInfo>(result.StartInfo);
        Assert.Equal(Path.GetFullPath(fixture.JavaPath), startInfo.FileName);
        Assert.Equal(Path.GetFullPath(fixture.WorkingDirectory), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.CreateNoWindow);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(new[] { "-jar", Path.GetFullPath(fixture.JarPath) }, startInfo.ArgumentList);
    }

    [Fact]
    public void Windows_ProcessSpecRemovesJavaOptionInjectionWithoutClearingUnrelatedEnvironment()
    {
        using var fixture = new PrivateJavaFixture();
        fixture.CreateCompleteLayout();
        string[] injectionVariables = ["JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS"];
        const string unrelatedVariable = "FFX_PRIVATE_JAVA_ENV_PRESERVE_TEST";
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string variable in injectionVariables)
            previous[variable] = Environment.GetEnvironmentVariable(variable);
        previous[unrelatedVariable] = Environment.GetEnvironmentVariable(unrelatedVariable);

        try
        {
            foreach (string variable in injectionVariables)
                Environment.SetEnvironmentVariable(variable, "-javaagent:ambient-agent.jar");
            Environment.SetEnvironmentVariable(unrelatedVariable, "preserve-me");

            // Control assertion: a fresh ProcessStartInfo inherits the seeded parent variables.
            var inherited = new ProcessStartInfo();
            foreach (string variable in injectionVariables)
                Assert.Equal("-javaagent:ambient-agent.jar", inherited.Environment[variable]);

            PrivateJavaLaunchResult result = fixture.Resolve();

            ProcessStartInfo startInfo = Assert.IsType<ProcessStartInfo>(result.StartInfo);
            foreach (string variable in injectionVariables)
                Assert.False(startInfo.Environment.ContainsKey(variable));
            Assert.Equal("preserve-me", startInfo.Environment[unrelatedVariable]);
        }
        finally
        {
            foreach ((string variable, string? value) in previous)
                Environment.SetEnvironmentVariable(variable, value);
        }
    }

    [Fact]
    public void Windows_DoesNotUseAmbientJavaOrRepositoryJarWhenPrivateJavaIsMissing()
    {
        using var fixture = new PrivateJavaFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JarPath)!);
        File.WriteAllText(fixture.JarPath, "jar");

        string ambientRoot = Path.Combine(fixture.Root, "ambient");
        Directory.CreateDirectory(ambientRoot);
        File.WriteAllText(Path.Combine(ambientRoot, "java"), "ambient-java");
        File.WriteAllText(Path.Combine(fixture.Root, "FFXED.jar"), "repo-side-jar");

        PrivateJavaLaunchResult result = fixture.Resolve();

        Assert.Equal(PrivateJavaLaunchStatus.PrivateJavaInvalid, result.Status);
        Assert.Equal("PRIVATE_JAVA_INVALID", result.ReasonCode);
        Assert.Null(result.StartInfo);
    }

    [Fact]
    public void Windows_RejectsDirectoryWherePrivateJavaFileIsRequired()
    {
        using var fixture = new PrivateJavaFixture();
        Directory.CreateDirectory(fixture.JavaPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JarPath)!);
        File.WriteAllText(fixture.JarPath, "jar");

        PrivateJavaLaunchResult result = fixture.Resolve();

        Assert.Equal(PrivateJavaLaunchStatus.PrivateJavaInvalid, result.Status);
        Assert.Null(result.StartInfo);
    }

    [Fact]
    public void AppPrivateFileResolver_RejectsTraversalOutsideBase()
    {
        using var fixture = new PrivateJavaFixture();
        string outside = Path.Combine(fixture.Root, "outside.exe");
        File.WriteAllText(outside, "outside");

        Assert.Null(PrivateJavaRuntime.ResolveAppPrivateFile(
            fixture.AppBase,
            "..",
            Path.GetFileName(outside)));
    }

    [Fact]
    public void Windows_RejectsMissingFfxedJarAfterPrivateJavaValidation()
    {
        using var fixture = new PrivateJavaFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JavaPath)!);
        File.WriteAllText(fixture.JavaPath, "private-java");

        PrivateJavaLaunchResult result = fixture.Resolve();

        Assert.Equal(PrivateJavaLaunchStatus.FfxedJarInvalid, result.Status);
        Assert.Equal("PRIVATE_FFXED_INVALID", result.ReasonCode);
        Assert.Null(result.StartInfo);
    }

    [Fact]
    public void Windows_RejectsReparsedStateRootWithoutCreatingOutsideWorkingDirectory()
    {
        using var fixture = new PrivateJavaFixture();
        fixture.CreateCompleteLayout();
        string outside = Path.Combine(fixture.Root, "outside-state");
        Directory.CreateDirectory(outside);

        try
        {
            Directory.CreateSymbolicLink(fixture.StateRoot, outside);
        }
        catch (Exception error) when (OperatingSystem.IsWindows() &&
            error is UnauthorizedAccessException or IOException)
        {
            // Standard Windows accounts may lack symlink creation rights. The shared guard suite
            // covers native Windows reparse rejection without weakening this resolver.
            return;
        }

        PrivateJavaLaunchResult result = fixture.Resolve();

        Assert.Equal(PrivateJavaLaunchStatus.WorkingDirectoryInvalid, result.Status);
        Assert.False(Directory.Exists(Path.Combine(outside, "ffxed")));
        Assert.Null(result.StartInfo);
    }

    [Fact]
    public void AppPrivateFileResolver_RejectsSymlinkToOutsideBaseWhenHostSupportsLinks()
    {
        using var fixture = new PrivateJavaFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JavaPath)!);
        string outside = Path.Combine(fixture.Root, "outside-java.exe");
        File.WriteAllText(outside, "outside");

        try
        {
            File.CreateSymbolicLink(fixture.JavaPath, outside);
        }
        catch (Exception error) when (OperatingSystem.IsWindows() &&
            error is UnauthorizedAccessException or IOException)
        {
            // Standard Windows accounts may lack symlink creation rights. The Windows handle
            // rejection itself is covered by the shared FileSystemReparseGuard test suite.
            return;
        }

        Assert.Null(PrivateJavaRuntime.ResolveAppPrivateFile(
            fixture.AppBase,
            "runtime",
            "java",
            "bin",
            "java.exe"));
    }

    private sealed class PrivateJavaFixture : IDisposable
    {
        internal PrivateJavaFixture()
        {
            Root = Directory.CreateTempSubdirectory("ffx-private-java-").FullName;
            AppBase = Path.Combine(Root, "Studio path 'quoted'");
            StateRoot = Path.Combine(Root, "user state");
            JavaPath = Path.Combine(AppBase, "runtime", "java", "bin", "java.exe");
            JarPath = Path.Combine(AppBase, "ExternalLibs", "FFXED", "FFXED.jar");
            WorkingDirectory = Path.Combine(StateRoot, "ffxed");
            Directory.CreateDirectory(AppBase);
        }

        internal string Root { get; }
        internal string AppBase { get; }
        internal string StateRoot { get; }
        internal string JavaPath { get; }
        internal string JarPath { get; }
        internal string WorkingDirectory { get; }

        internal void CreateCompleteLayout()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JavaPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(JarPath)!);
            File.WriteAllText(JavaPath, "private-java");
            File.WriteAllText(JarPath, "private-jar");
        }

        internal PrivateJavaLaunchResult Resolve() => PrivateJavaRuntime.CreateFfxedLaunch(
            PrivateJavaHostPlatform.Windows,
            AppBase,
            StateRoot);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
