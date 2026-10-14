// ============================================================================
// PortablePathResolver — fail-closed discovery for product-owned FFX paths
// PURPOSE : resolve configured, discovered, and app-bundled files without any
//           developer-drive fallback or implicit current-directory behavior.
// WHY     : release runtimes must reject traversal and reparse redirection before
//           returning a pathname to Java, FFXED, viewers, or import services.
// MAINT   : returning a verified path is discovery, not a long-lived capability;
//           mutating consumers must still reopen through FileSystemReparseGuard.
// ============================================================================

using FFXProjectEditor.Core;
using FFXProjectEditor.Modules.Common.ViewerHub;
using System;
using System.IO;

namespace FFXProjectEditor.Services
{
    public static class PortablePathResolver
    {
        internal static Action<string, string>? BeforeVerifiedOpenForTests { get; set; }

        public static string? GameInstallRoot => ResolveGameInstallRoot(
            Project_Service.Instance.Path_GameInstallRoot,
            Environment.GetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar),
            () => new GameEnvironmentProbe().DiscoverGameRoot());

        public static string? MasterRoot => FirstExistingDirectory(
            Project_Service.Instance.ProjectPath,
            Environment.GetEnvironmentVariable("FFX_MASTER"));

        public static string? FfxPs2Root => ResolveFfxPs2Root(
            Environment.GetEnvironmentVariable("FFX_PS2_ROOT"),
            Project_Service.FfxPs2RootOverride,
            Project_Service.Instance.ProjectPath);

        public static string? Ps3DataRoot => ResolvePs3DataRoot(
            Environment.GetEnvironmentVariable("FFX_PS3DATA_ROOT"),
            Project_Service.Ps3DataRootOverride,
            Project_Service.Instance.ProjectPath);

        public static string? ExtractedFfxRoot
        {
            get
            {
                string? explicitRoot = FirstExistingDirectory(
                    Environment.GetEnvironmentVariable("FFX_EXTRACTED_ROOT"));
                if (explicitRoot != null)
                    return explicitRoot;

                string? ffxPs2 = FfxPs2Root;
                string? parent = ffxPs2 == null ? null : Directory.GetParent(ffxPs2)?.FullName;
                return FirstExistingDirectory(parent);
            }
        }

        public static string? MagicFilesRoot(string game = "FFX")
        {
            bool isFfx2 = string.Equals(game, "FFX-2", StringComparison.OrdinalIgnoreCase);
            if (!isFfx2 && !string.Equals(game, "FFX", StringComparison.OrdinalIgnoreCase))
                return null;

            string canonicalGame = isFfx2 ? "FFX-2" : "FFX";
            string envName = isFfx2
                ? "FFX2_MAGIC_FILES_ROOT"
                : "FFX_MAGIC_FILES_ROOT";
            string? configured = FirstExistingDirectory(Environment.GetEnvironmentVariable(envName));
            if (configured != null)
                return configured;

            string? gameRoot = GameInstallRoot;
            return gameRoot == null
                ? null
                : FirstExistingDirectoryUnderRoot(gameRoot, "magicFiles", canonicalGame);
        }

        public static string? AudioSfxRoot(string locale = "us")
        {
            if (!IsCanonicalLeaf(locale))
                return null;

            string? root = Ps3DataRoot;
            return root == null
                ? null
                : FirstExistingDirectoryUnderRoot(root, "sound_pc", "sfx", locale);
        }

        public static string? BundledPath(params string[] segments)
        {
            if (segments == null || segments.Length == 0)
                return null;

            try
            {
                string baseDirectory = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(AppContext.BaseDirectory));
                string candidate = baseDirectory;
                foreach (string? segment in segments)
                {
                    if (string.IsNullOrWhiteSpace(segment) || Path.IsPathRooted(segment))
                        return null;
                    candidate = Path.Combine(candidate, segment);
                }

                candidate = Path.GetFullPath(candidate);
                string relative = Path.GetRelativePath(baseDirectory, candidate);
                if (!IsStrictDescendant(relative))
                    return null;

                return FirstExistingFile(candidate) ?? FirstExistingDirectory(candidate);
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                return null;
            }
        }

        public static string? FirstExistingDirectory(params string?[] candidates)
        {
            if (candidates == null)
                return null;

            foreach (string? candidate in candidates)
            {
                if (!TryNormalizeAbsolute(candidate, out string full) || !Directory.Exists(full) ||
                    FileSystemReparseGuard.ContainsReparsePointInExistingChain(full))
                    continue;

                if (!OperatingSystem.IsWindows())
                    return full;

                BeforeVerifiedOpenForTests?.Invoke("directory", full);
                if (FileSystemReparseGuard.TryOpenVerifiedDirectory(
                        full,
                        out FileSystemReparseGuard.VerifiedDirectory? verified) &&
                    verified != null)
                {
                    using (verified)
                        return verified.FullPath;
                }
            }

            return null;
        }

        public static string? FirstExistingFile(params string?[] candidates)
        {
            if (candidates == null)
                return null;

            foreach (string? candidate in candidates)
            {
                if (!TryNormalizeAbsolute(candidate, out string full) || !File.Exists(full) ||
                    FileSystemReparseGuard.ContainsReparsePointInExistingChain(full))
                    continue;

                if (!OperatingSystem.IsWindows())
                    return full;

                string? parent = Path.GetDirectoryName(full);
                BeforeVerifiedOpenForTests?.Invoke("file", full);
                if (parent == null ||
                    FileSystemReparseGuard.TryOpenVerifiedRead(
                        parent,
                        full,
                        out FileSystemReparseGuard.VerifiedReadFile? verified) !=
                        FileSystemReparseGuard.VerifiedOpenResult.Success ||
                    verified == null)
                    continue;

                using (verified)
                    return verified.FullPath;
            }

            return null;
        }

        internal static string? ResolveGameInstallRoot(
            string? configuredRoot,
            string? environmentRoot,
            Func<string?> discoverRoot)
        {
            string? configured = FirstExistingDirectory(configuredRoot, environmentRoot);
            if (configured != null)
                return configured;

            try
            {
                return FirstExistingDirectory(discoverRoot());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        internal static string? ResolveFfxPs2Root(
            string? environmentRoot,
            string? sessionOverride,
            string? projectPath)
        {
            string? explicitRoot = FirstExistingDirectory(environmentRoot, sessionOverride);
            return explicitRoot ?? DeriveFfxPs2RootFromProject(projectPath);
        }

        internal static string? ResolvePs3DataRoot(
            string? environmentRoot,
            string? sessionOverride,
            string? projectPath)
        {
            string? explicitRoot = FirstExistingDirectory(environmentRoot, sessionOverride);
            if (explicitRoot != null)
                return explicitRoot;

            string? ffxPs2Root = DeriveFfxPs2RootFromProject(projectPath);
            string? extractionRoot = ffxPs2Root == null
                ? null
                : Directory.GetParent(ffxPs2Root)?.FullName;
            return extractionRoot == null
                ? null
                : FirstExistingDirectoryUnderRoot(
                    extractionRoot,
                    "ffx_data",
                    "gamedata",
                    "ps3data");
        }

        private static string? DeriveFfxPs2RootFromProject(string? projectPath)
        {
            string? master = FirstExistingDirectory(projectPath);
            if (master == null ||
                !string.Equals(Path.GetFileName(master), "master", StringComparison.OrdinalIgnoreCase))
                return null;

            DirectoryInfo? ffxDirectory = Directory.GetParent(master);
            DirectoryInfo? ffxPs2Directory = ffxDirectory?.Parent;
            if (ffxDirectory == null || ffxPs2Directory == null ||
                !string.Equals(ffxDirectory.Name, "ffx", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ffxPs2Directory.Name, "ffx_ps2", StringComparison.OrdinalIgnoreCase))
                return null;

            return FirstExistingDirectory(ffxPs2Directory.FullName);
        }

        private static string? FirstExistingDirectoryUnderRoot(
            string root,
            params string[] segments)
        {
            try
            {
                string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                string candidate = canonicalRoot;
                foreach (string segment in segments)
                {
                    if (!IsCanonicalLeaf(segment))
                        return null;
                    candidate = Path.Combine(candidate, segment);
                }

                candidate = Path.GetFullPath(candidate);
                string relative = Path.GetRelativePath(canonicalRoot, candidate);
                return IsStrictDescendant(relative)
                    ? FirstExistingDirectory(candidate)
                    : null;
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                return null;
            }
        }

        private static bool TryNormalizeAbsolute(string? candidate, out string full)
        {
            full = string.Empty;
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
                return false;
            try
            {
                full = Path.GetFullPath(candidate);
                return true;
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                return false;
            }
        }

        private static bool IsStrictDescendant(string relative) =>
            !string.IsNullOrWhiteSpace(relative) &&
            !Path.IsPathRooted(relative) &&
            relative != "." &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

        private static bool IsCanonicalLeaf(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value != "." &&
            value != ".." &&
            !Path.IsPathRooted(value) &&
            value.IndexOfAny(new[] { '\\', '/', ':', '\0' }) < 0 &&
            string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal);

        private static bool IsPathException(Exception ex) =>
            ex is ArgumentException or NotSupportedException or PathTooLongException or
                IOException or UnauthorizedAccessException;
    }
}
