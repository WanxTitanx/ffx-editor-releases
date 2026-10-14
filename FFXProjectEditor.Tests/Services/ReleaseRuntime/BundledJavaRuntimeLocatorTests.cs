using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Services.ReleaseRuntime;
using Xunit;

namespace FFXProjectEditor.Tests.Services.ReleaseRuntime;

public sealed class BundledJavaRuntimeLocatorTests
{
    [Fact]
    public void Locate_PrefersPrivateRuntimeUnderApplicationBase()
    {
        string appBase = Root("app");
        string bundledJava = Path.Combine(appBase, "runtimes", "java", "bin", "javaw.exe");
        string javaHomeJava = Path.Combine(Root("jdk"), "bin", "javaw.exe");
        FakeJavaRuntimeEnvironment environment = new();
        environment.AddFile(bundledJava);
        environment.AddFile(javaHomeJava);
        environment.SetVariable("JAVA_HOME", Path.GetDirectoryName(Path.GetDirectoryName(javaHomeJava)!)!);

        BundledJavaRuntimeLocator locator = CreateLocator(
            appBase,
            environment,
            allowJavaHome: true,
            allowPath: true);

        JavaRuntimeLocation? result = locator.Locate();

        Assert.NotNull(result);
        Assert.Equal(JavaRuntimeSource.BundledPrivate, result.Source);
        Assert.True(result.IsPrivateRuntime);
        Assert.Equal(Path.GetFullPath(bundledJava), result.ExecutablePath);
    }

    [Fact]
    public void Locate_FindsOneLevelVersionedPrivateRuntime()
    {
        string appBase = Root("app-versioned");
        string javaRoot = Path.Combine(appBase, "runtimes", "java");
        string versionRoot = Path.Combine(javaRoot, "jdk-21.0.11");
        string java = Path.Combine(versionRoot, "bin", "java.exe");
        FakeJavaRuntimeEnvironment environment = new();
        environment.AddDirectory(javaRoot, versionRoot);
        environment.AddFile(java);

        JavaRuntimeLocation? result = CreateLocator(appBase, environment).Locate();

        Assert.NotNull(result);
        Assert.Equal(JavaRuntimeSource.BundledPrivate, result.Source);
        Assert.Equal(Path.GetFullPath(java), result.ExecutablePath);
        Assert.Equal(Path.GetFullPath(versionRoot), result.RuntimeRoot);
    }

    [Fact]
    public void Locate_DoesNotUseJavaHomeOrPathUnlessExplicitlyEnabled()
    {
        string appBase = Root("app-no-fallback");
        string javaHome = Root("jdk-no-fallback");
        string javaHomeExe = Path.Combine(javaHome, "bin", "java.exe");
        string pathDir = Root("path-no-fallback");
        string pathExe = Path.Combine(pathDir, "java.exe");
        FakeJavaRuntimeEnvironment environment = new();
        environment.AddFile(javaHomeExe);
        environment.AddFile(pathExe);
        environment.SetVariable("JAVA_HOME", javaHome);
        environment.SetVariable("PATH", pathDir);

        JavaRuntimeLocation? result = CreateLocator(appBase, environment).Locate();

        Assert.Null(result);
    }

    [Fact]
    public void Locate_UsesJavaHomeBeforePathWhenBothFallbacksAreEnabled()
    {
        string appBase = Root("app-java-home");
        string javaHome = Root("jdk-java-home");
        string javaHomeExe = Path.Combine(javaHome, "bin", "javaw.exe");
        string pathDir = Root("path-java-home");
        string pathExe = Path.Combine(pathDir, "javaw.exe");
        FakeJavaRuntimeEnvironment environment = new();
        environment.AddFile(javaHomeExe);
        environment.AddFile(pathExe);
        environment.SetVariable("JAVA_HOME", javaHome);
        environment.SetVariable("PATH", pathDir);

        JavaRuntimeLocation? result = CreateLocator(
            appBase,
            environment,
            allowJavaHome: true,
            allowPath: true).Locate();

        Assert.NotNull(result);
        Assert.Equal(JavaRuntimeSource.JavaHome, result.Source);
        Assert.False(result.IsPrivateRuntime);
        Assert.Equal(Path.GetFullPath(javaHomeExe), result.ExecutablePath);
    }

    [Fact]
    public void Locate_UsesOnlyRootedPathEntriesWhenPathFallbackIsEnabled()
    {
        string appBase = Root("app-path");
        string pathDir = Root("path-valid");
        string pathExe = Path.Combine(pathDir, "java.exe");
        FakeJavaRuntimeEnvironment environment = new();
        environment.AddFile(Path.GetFullPath(Path.Combine("relative-java", "javaw.exe")));
        environment.AddFile(pathExe);
        environment.SetVariable("PATH", $"relative-java{Path.PathSeparator}{pathDir}");

        JavaRuntimeLocation? result = CreateLocator(
            appBase,
            environment,
            allowPath: true).Locate();

        Assert.NotNull(result);
        Assert.Equal(JavaRuntimeSource.Path, result.Source);
        Assert.Equal(Path.GetFullPath(pathExe), result.ExecutablePath);
    }

    [Fact]
    public void Locate_IgnoresRelativeJavaHomeEvenWhenFallbackIsEnabled()
    {
        string appBase = Root("app-relative-home");
        FakeJavaRuntimeEnvironment environment = new();
        environment.SetVariable("JAVA_HOME", "relative-jdk");
        environment.AddFile(Path.GetFullPath(Path.Combine("relative-jdk", "bin", "java.exe")));

        JavaRuntimeLocation? result = CreateLocator(
            appBase,
            environment,
            allowJavaHome: true).Locate();

        Assert.Null(result);
    }

    private static BundledJavaRuntimeLocator CreateLocator(
        string appBase,
        FakeJavaRuntimeEnvironment environment,
        bool allowJavaHome = false,
        bool allowPath = false) =>
        new(
            new BundledJavaRuntimeLocatorOptions
            {
                BaseDirectory = appBase,
                AllowJavaHomeFallback = allowJavaHome,
                AllowPathFallback = allowPath,
            },
            environment);

    private static string Root(string name) =>
        Path.Combine(Path.GetTempPath(), "ffx-java-locator-tests", name);

    private sealed class FakeJavaRuntimeEnvironment : IJavaRuntimeEnvironment
    {
        private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> variables = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path) => files.Add(Path.GetFullPath(path));

        public void AddDirectory(string parent, string child)
        {
            string fullParent = Path.GetFullPath(parent);
            if (!directories.TryGetValue(fullParent, out List<string>? children))
            {
                children = [];
                directories[fullParent] = children;
            }
            children.Add(Path.GetFullPath(child));
        }

        public void SetVariable(string name, string? value) => variables[name] = value;

        public bool FileExists(string path) => files.Contains(Path.GetFullPath(path));

        public IEnumerable<string> EnumerateDirectories(string path) =>
            directories.TryGetValue(Path.GetFullPath(path), out List<string>? children)
                ? children.ToArray()
                : [];

        public string? GetEnvironmentVariable(string name) =>
            variables.TryGetValue(name, out string? value) ? value : null;
    }
}
