// ============================================================================
// PrivateJavaRuntime - package-private FFXED launch contract
// PURPOSE : resolve the Windows-only Java/JAR pair from the installed Studio
//           tree and construct a tokenized ProcessStartInfo without launching it.
// WHY     : PATH, workspace, ModsRoot, picker, and repair fallbacks can execute
//           an unreviewed binary or JAR. Linux must refuse before path probing.
// MAINT   : FFXED remains an owner-accepted-risk Windows payload. Do not enable
//           it on Linux until redistribution authority and runtime proof exist.
// ============================================================================

using FFXProjectEditor.Modules.Common.ViewerHub;
using System;
using System.Diagnostics;
using System.IO;

namespace FFXProjectEditor.Services.Tools;

internal enum PrivateJavaHostPlatform
{
    Windows,
    Linux,
    Unsupported,
}

internal enum PrivateJavaLaunchStatus
{
    Ready,
    OptionalUnavailable,
    AppBaseInvalid,
    PrivateJavaInvalid,
    FfxedJarInvalid,
    WorkingDirectoryInvalid,
}

internal sealed record PrivateJavaLaunchResult(
    PrivateJavaLaunchStatus Status,
    string ReasonCode,
    ProcessStartInfo? StartInfo);

internal static class PrivateJavaRuntime
{
    private static readonly string[] JavaRelativePath = ["runtime", "java", "bin", "java.exe"];
    private static readonly string[] FfxedRelativePath = ["ExternalLibs", "FFXED", "FFXED.jar"];
    private static readonly string[] JavaOptionInjectionVariables =
        ["JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS"];

    internal static PrivateJavaLaunchResult CreateFfxedLaunch()
    {
        PrivateJavaHostPlatform platform = OperatingSystem.IsWindows()
            ? PrivateJavaHostPlatform.Windows
            : OperatingSystem.IsLinux()
                ? PrivateJavaHostPlatform.Linux
                : PrivateJavaHostPlatform.Unsupported;

        // WHY: no environment or filesystem call belongs on the unsupported-platform branch.
        // This keeps ambient Java and repository JARs irrelevant on Linux by construction.
        if (platform != PrivateJavaHostPlatform.Windows)
            return Failure(PrivateJavaLaunchStatus.OptionalUnavailable, "OPTIONAL_UNAVAILABLE");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string stateRoot = string.IsNullOrWhiteSpace(localAppData)
            ? string.Empty
            : Path.Combine(localAppData, "FFXProjectEditor");
        return CreateFfxedLaunch(platform, AppContext.BaseDirectory, stateRoot);
    }

    internal static PrivateJavaLaunchResult CreateFfxedLaunch(
        PrivateJavaHostPlatform platform,
        string appBaseDirectory,
        string appOwnedStateRoot)
    {
        if (platform != PrivateJavaHostPlatform.Windows)
            return Failure(PrivateJavaLaunchStatus.OptionalUnavailable, "OPTIONAL_UNAVAILABLE");

        if (!TryNormalizeAbsoluteDirectory(appBaseDirectory, out string appBase))
            return Failure(PrivateJavaLaunchStatus.AppBaseInvalid, "APP_BASE_INVALID");

        string? javaPath = ResolveAppPrivateFile(appBase, JavaRelativePath);
        if (javaPath == null)
            return Failure(PrivateJavaLaunchStatus.PrivateJavaInvalid, "PRIVATE_JAVA_INVALID");

        string? jarPath = ResolveAppPrivateFile(appBase, FfxedRelativePath);
        if (jarPath == null)
            return Failure(PrivateJavaLaunchStatus.FfxedJarInvalid, "PRIVATE_FFXED_INVALID");

        if (!TryPrepareWorkingDirectory(appOwnedStateRoot, out string workingDirectory))
            return Failure(PrivateJavaLaunchStatus.WorkingDirectoryInvalid, "APP_STATE_INVALID");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        // Java consumes these ambient variables before the explicit -jar token and accepts options
        // such as -javaagent. Remove only the three JVM injection channels; locale, display, and
        // other environment inherited by the GUI runtime must remain available.
        foreach (string variable in JavaOptionInjectionVariables)
            startInfo.Environment.Remove(variable);
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jarPath);

        return new PrivateJavaLaunchResult(PrivateJavaLaunchStatus.Ready, "READY", startInfo);
    }

    /// <summary>
    /// Resolves one regular app-private file through the shared verified-read boundary. Every
    /// segment must be a canonical leaf, so rooted paths and traversal are rejected before I/O.
    /// </summary>
    internal static string? ResolveAppPrivateFile(string appBaseDirectory, params string[] segments)
    {
        if (!TryNormalizeAbsoluteDirectory(appBaseDirectory, out string appBase) ||
            segments == null || segments.Length == 0)
            return null;

        try
        {
            string candidate = appBase;
            foreach (string? segment in segments)
            {
                if (!IsCanonicalLeaf(segment))
                    return null;
                candidate = Path.Combine(candidate, segment);
            }

            candidate = Path.GetFullPath(candidate);
            string relative = Path.GetRelativePath(appBase, candidate);
            if (!IsStrictDescendant(relative) ||
                FileSystemReparseGuard.ContainsReparsePointInExistingChain(candidate))
                return null;

            FileSystemReparseGuard.VerifiedOpenResult openResult =
                FileSystemReparseGuard.TryOpenVerifiedRead(appBase, candidate, out var verified);
            using (verified)
            {
                return openResult == FileSystemReparseGuard.VerifiedOpenResult.Success && verified != null
                    ? Path.GetFullPath(verified.FullPath)
                    : null;
            }
        }
        catch (Exception error) when (IsPathFailure(error))
        {
            return null;
        }
    }

    private static bool TryPrepareWorkingDirectory(string appOwnedStateRoot, out string workingDirectory)
    {
        workingDirectory = string.Empty;
        if (!TryNormalizeAbsolute(appOwnedStateRoot, out string stateRoot))
            return false;

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(stateRoot, "ffxed"));
            string relative = Path.GetRelativePath(stateRoot, candidate);
            if (!IsStrictDescendant(relative) ||
                FileSystemReparseGuard.ContainsReparsePointInExistingChain(candidate))
                return false;

            if (OperatingSystem.IsWindows())
            {
                // The NT capability walk creates each missing segment with OBJ_DONT_REPARSE and
                // keeps the final directory stable during validation.
                using FileSystemReparseGuard.VerifiedDirectory verified =
                    FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(candidate);
                workingDirectory = verified.FullPath;
                return true;
            }

            // Tests exercise the Windows launch shape on Linux without pretending that Linux can
            // launch it. Existing Linux openat2 primitives still validate the fixture directory.
            Directory.CreateDirectory(candidate);
            if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(candidate, out var linuxVerified) ||
                linuxVerified == null)
                return false;
            using (linuxVerified)
            {
                workingDirectory = linuxVerified.FullPath;
                return true;
            }
        }
        catch (Exception error) when (IsPathFailure(error))
        {
            return false;
        }
    }

    private static bool TryNormalizeAbsoluteDirectory(string? value, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryNormalizeAbsolute(value, out string candidate) ||
            !Directory.Exists(candidate) ||
            FileSystemReparseGuard.ContainsReparsePointInExistingChain(candidate))
            return false;

        if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(candidate, out var verified) ||
            verified == null)
            return false;
        using (verified)
        {
            fullPath = verified.FullPath;
            return true;
        }
    }

    private static bool TryNormalizeAbsolute(string? value, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            return false;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            return !string.IsNullOrWhiteSpace(fullPath);
        }
        catch (Exception error) when (IsPathFailure(error))
        {
            return false;
        }
    }

    private static bool IsCanonicalLeaf(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not "." and not ".." &&
        !Path.IsPathRooted(value) &&
        value.IndexOfAny(['\\', '/', ':', '\0']) < 0 &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal);

    private static bool IsStrictDescendant(string relative) =>
        !string.IsNullOrWhiteSpace(relative) &&
        !Path.IsPathRooted(relative) &&
        relative is not "." and not ".." &&
        !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static bool IsPathFailure(Exception error) =>
        error is ArgumentException or NotSupportedException or PathTooLongException or
            IOException or UnauthorizedAccessException;

    private static PrivateJavaLaunchResult Failure(
        PrivateJavaLaunchStatus status,
        string reasonCode) => new(status, reasonCode, null);
}
