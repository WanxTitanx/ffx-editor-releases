// ============================================================================
// BundledJavaRuntimeLocator — deterministic Java executable discovery
// PURPOSE : prefer a private JRE below runtimes/java and expose optional,
//           explicitly enabled JAVA_HOME/PATH fallbacks.
// WHY     : the FFXED bridge must not depend on developer-machine paths or run
//           an arbitrary PATH executable unless the caller opts into fallback.
// MAINT   : keep discovery shallow and side-effect free. This class locates;
//           launch policy and JAR hash verification belong to the caller.
// ============================================================================

using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public enum JavaRuntimeSource
{
    BundledPrivate,
    JavaHome,
    Path,
}

public sealed record JavaRuntimeLocation(
    string ExecutablePath,
    string RuntimeRoot,
    JavaRuntimeSource Source,
    bool IsPrivateRuntime);

public sealed class BundledJavaRuntimeLocatorOptions
{
    public string BaseDirectory { get; init; } = AppContext.BaseDirectory;
    public bool AllowJavaHomeFallback { get; init; }
    public bool AllowPathFallback { get; init; }
}

public interface IJavaRuntimeEnvironment
{
    bool FileExists(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    string? GetEnvironmentVariable(string name);
}

public sealed class SystemJavaRuntimeEnvironment : IJavaRuntimeEnvironment
{
    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}

public sealed class BundledJavaRuntimeLocator
{
    private static readonly string[] ExecutableNames = ["javaw.exe", "java.exe"];

    private readonly BundledJavaRuntimeLocatorOptions options;
    private readonly IJavaRuntimeEnvironment environment;
    private readonly string privateRuntimeRoot;

    public BundledJavaRuntimeLocator()
        : this(new BundledJavaRuntimeLocatorOptions(), new SystemJavaRuntimeEnvironment())
    {
    }

    public BundledJavaRuntimeLocator(
        BundledJavaRuntimeLocatorOptions options,
        IJavaRuntimeEnvironment environment)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        if (string.IsNullOrWhiteSpace(options.BaseDirectory) || !Path.IsPathRooted(options.BaseDirectory))
        {
            throw new ArgumentException("Java runtime base directory must be an absolute path.", nameof(options));
        }

        string baseDirectory = Path.GetFullPath(options.BaseDirectory);
        privateRuntimeRoot = Path.Combine(baseDirectory, "runtimes", "java");
    }

    public JavaRuntimeLocation? Locate()
    {
        JavaRuntimeLocation? location = LocatePrivateRuntime()
            ?? (options.AllowJavaHomeFallback ? LocateJavaHome() : null)
            ?? (options.AllowPathFallback ? LocateOnPath() : null);

        if (location == null)
        {
            DebugLog.Warn("ReleaseRuntime.JavaLocator", "No allowed Java runtime was found.");
        }
        else
        {
            DebugLog.Info(
                "ReleaseRuntime.JavaLocator",
                $"source={location.Source} executable={Path.GetFileName(location.ExecutablePath)}");
        }

        return location;
    }

    private JavaRuntimeLocation? LocatePrivateRuntime()
    {
        JavaRuntimeLocation? direct = LocateInsideRoot(
            privateRuntimeRoot,
            JavaRuntimeSource.BundledPrivate,
            isPrivateRuntime: true);
        if (direct != null)
        {
            return direct;
        }

        foreach (string child in environment.EnumerateDirectories(privateRuntimeRoot)
                     .Select(Path.GetFullPath)
                     .Where(path => IsSameOrUnder(path, privateRuntimeRoot))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            JavaRuntimeLocation? versioned = LocateInsideRoot(
                child,
                JavaRuntimeSource.BundledPrivate,
                isPrivateRuntime: true);
            if (versioned != null)
            {
                return versioned;
            }
        }

        return null;
    }

    private JavaRuntimeLocation? LocateJavaHome()
    {
        string? rawHome = environment.GetEnvironmentVariable("JAVA_HOME");
        string? home = NormalizeEnvironmentPath(rawHome);
        return home == null
            ? null
            : LocateInsideRoot(home, JavaRuntimeSource.JavaHome, isPrivateRuntime: false);
    }

    private JavaRuntimeLocation? LocateOnPath()
    {
        string? rawPath = environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        foreach (string segment in rawPath.Split(Path.PathSeparator, StringSplitOptions.TrimEntries))
        {
            string? directory = NormalizeEnvironmentPath(segment);
            if (directory == null)
            {
                continue;
            }

            foreach (string executableName in ExecutableNames)
            {
                string candidate = Path.Combine(directory, executableName);
                if (environment.FileExists(candidate))
                {
                    return new JavaRuntimeLocation(
                        Path.GetFullPath(candidate),
                        directory,
                        JavaRuntimeSource.Path,
                        IsPrivateRuntime: false);
                }
            }
        }

        return null;
    }

    private JavaRuntimeLocation? LocateInsideRoot(
        string runtimeRoot,
        JavaRuntimeSource source,
        bool isPrivateRuntime)
    {
        string fullRoot = Path.GetFullPath(runtimeRoot);
        if (isPrivateRuntime && !IsSameOrUnder(fullRoot, privateRuntimeRoot))
        {
            return null;
        }

        foreach (string executableName in ExecutableNames)
        {
            string candidate = Path.Combine(fullRoot, "bin", executableName);
            if (!environment.FileExists(candidate))
            {
                continue;
            }

            string fullCandidate = Path.GetFullPath(candidate);
            if (!IsSameOrUnder(fullCandidate, fullRoot))
            {
                continue;
            }

            return new JavaRuntimeLocation(fullCandidate, fullRoot, source, isPrivateRuntime);
        }

        return null;
    }

    private static string? NormalizeEnvironmentPath(string? raw)
    {
        string value = (raw ?? string.Empty).Trim().Trim('"');
        if (value.Length == 0 || !Path.IsPathRooted(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
