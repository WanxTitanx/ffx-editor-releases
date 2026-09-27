using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    internal readonly record struct MagicOverrideResult(
        bool Success,
        string Message,
        string? RequestPath = null,
        string? StagedPath = null,
        string? Sha256 = null,
        StudioWebServer.ExactMemoryLease? MemoryLease = null);

    /// <summary>
    /// Owns copy-only Magic previews: native Linux uses an opaque exact-memory lease; the existing
    /// Windows path stages beneath app-owned viewer data. Selected NoClip files are never written.
    /// </summary>
    internal static partial class MagicOverrideService
    {
        public const string BackupSuffix = ".magic3d.bak";

        private static readonly ConcurrentDictionary<
            string,
            FileSystemReparseGuard.FileIdentity> ActiveStagedOverrides =
                new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Directory 11/ (Magic) in selected NoClip data, or null when unavailable.</summary>
        public static string? ResolveMagicDir()
        {
            string? root = NoclipLocator.Find();
            if (root == null)
                return null;
            string directory = Path.Combine(root, "data", "FinalFantasyX", "11");
            return Directory.Exists(directory) ? directory : null;
        }

        /// <summary>
        /// Stages an immutable, uniquely owned preview file. The selected vanilla bin is verified
        /// through a handle first; all mutations then occur relative to a verified app directory.
        /// </summary>
        internal static MagicOverrideResult StageMagicOverride(
            int magicId,
            byte[] customBytes,
            string magicDirectory,
            string viewerDataRoot,
            StudioWebServer? server = null)
        {
            if (customBytes == null || customBytes.Length == 0)
                return new MagicOverrideResult(false, Strings.U_Md_OverrideEmptyDll);
            if (magicId is < 0 or > ushort.MaxValue)
                return new MagicOverrideResult(
                    false,
                    string.Format(Strings.U_Md_OverrideBinMissing, magicId));

            if (OperatingSystem.IsLinux())
                return StageNativeMagicOverride(magicId, customBytes, magicDirectory, server);

            string? stagedPath = null;
            string? stagedName = null;
            FileStream? staged = null;
            FileSystemReparseGuard.FileIdentity? stagedIdentity = null;
            FileSystemReparseGuard.VerifiedDirectory? ownedDirectory = null;
            try
            {
                string selectedMagicDirectory = Path.GetFullPath(magicDirectory);
                string selectedTarget = Path.Combine(selectedMagicDirectory, $"{magicId:X4}.bin");
                FileSystemReparseGuard.VerifiedOpenResult selectedResult =
                    FileSystemReparseGuard.TryOpenVerifiedRead(
                        selectedMagicDirectory,
                        selectedTarget,
                        out FileSystemReparseGuard.VerifiedReadFile? selected);
                if (selectedResult == FileSystemReparseGuard.VerifiedOpenResult.NotFound &&
                    TryFetchViaLoopbackProbe(
                        server, $"/data/FinalFantasyX/11/{magicId:x4}.bin"))
                {
                    // The managed bootstrap seeds only one file per directory — the loopback probe
                    // fetched and cached this bin, so re-open before refusing.
                    selected?.Dispose();
                    selectedResult = FileSystemReparseGuard.TryOpenVerifiedRead(
                        selectedMagicDirectory,
                        selectedTarget,
                        out selected);
                }
                if (selectedResult != FileSystemReparseGuard.VerifiedOpenResult.Success || selected == null)
                {
                    selected?.Dispose();
                    return new MagicOverrideResult(
                        false,
                        string.Format(Strings.U_Md_OverrideBinMissing, magicId));
                }
                selected.Dispose();

                string stagingDirectory = ResolveStagingDirectory(viewerDataRoot);
                ownedDirectory = FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(
                    stagingDirectory);

                string finalName = $"{magicId:X4}.{Guid.NewGuid():N}.bin";
                stagedName = finalName;
                string temporaryName = $".{Guid.NewGuid():N}.tmp";
                stagedPath = Path.Combine(stagingDirectory, finalName);
                staged = FileSystemReparseGuard.CreateNewVerifiedFile(ownedDirectory, temporaryName);
                staged.Write(customBytes, 0, customBytes.Length);
                staged.Flush(flushToDisk: true);
                FileSystemReparseGuard.FileIdentity identity =
                    FileSystemReparseGuard.PromoteOpenedFile(ownedDirectory, staged, finalName);
                stagedIdentity = identity;
                staged.Dispose();
                staged = null;

                using (FileSystemReparseGuard.VerifiedReadFile verified = OpenStagedRead(
                    ownedDirectory,
                    finalName,
                    identity))
                {
                    string actualHash = Convert.ToHexString(SHA256.HashData(verified.Stream));
                    string expectedHash = Convert.ToHexString(SHA256.HashData(customBytes));
                    if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                        throw new IOException("Magic preview staging hash mismatch.");

                    if (!ActiveStagedOverrides.TryAdd(stagedPath, identity))
                        throw new IOException("Magic preview ownership collision.");

                    string requestPath = $"/data/FinalFantasyX/11/{magicId:x4}.bin";
                    DebugLog.Info(
                        "Magic.Override",
                        $"Staged 11/{magicId:X4}.bin ({customBytes.Length} bytes, sha256={expectedHash}); selected data unchanged.");
                    return new MagicOverrideResult(
                        true,
                        string.Format(Strings.U_Md_OverrideApplied, magicId),
                        requestPath,
                        stagedPath,
                        expectedHash);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException or
                PlatformNotSupportedException)
            {
                if (staged != null)
                {
                    try { FileSystemReparseGuard.DeleteOpenedFile(staged); } catch { }
                    staged.Dispose();
                }
                if (stagedPath != null && stagedName != null &&
                    ownedDirectory != null && stagedIdentity is { } identity)
                {
                    _ = ActiveStagedOverrides.TryRemove(
                        new KeyValuePair<string, FileSystemReparseGuard.FileIdentity>(stagedPath, identity));
                    _ = FileSystemReparseGuard.TryDeleteVerifiedFile(
                        ownedDirectory,
                        stagedName,
                        identity);
                }

                DebugLog.Error(
                    "Magic.Override",
                    $"Failed to stage 11/{magicId:X4}.bin: {ex.Message}",
                    ex);
                return new MagicOverrideResult(
                    false,
                    string.Format(Strings.U_Md_OverrideBinMissing, magicId));
            }
            finally
            {
                ownedDirectory?.Dispose();
            }
        }

        // ── Preview ownership funnel ───────────────────────────────────────────────
        // Memory is already mapped by the producer. These checks must not add another owner,
        // and a same-looking request path on a different server is not an ownership proof.
        internal static bool TryRegisterPreview(StudioWebServer? server, MagicOverrideResult result)
        {
            if (server is not { IsRunning: true } || !result.Success ||
                string.IsNullOrWhiteSpace(result.RequestPath))
                return false;
            if (result.MemoryLease is { } lease)
            {
                return result.StagedPath == null &&
                    string.Equals(result.RequestPath, lease.RequestPath, StringComparison.Ordinal) &&
                    lease.IsActive && lease.IsOwnedBy(server);
            }
            return !string.IsNullOrWhiteSpace(result.StagedPath) &&
                server.TryMapExactFile(result.RequestPath, result.StagedPath);
        }

        // A supplied memory capability is sufficient authority to RELEASE itself, even if a
        // malformed result cannot register or the caller's current server changed. Never follow
        // a mixed StagedPath into the filesystem. True means that memory cleanup was handled.
        internal static bool TryReleasePreview(
            string viewerDataRoot, StudioWebServer? server, MagicOverrideResult result)
        {
            if (result.MemoryLease is { } lease)
            {
                lease.Dispose();
                DebugLog.Info("Magic.Preview", "Released the supplied exact-memory preview lease.");
                return true;
            }
            if (string.IsNullOrWhiteSpace(result.StagedPath))
                return false;
            if (server != null && !string.IsNullOrWhiteSpace(result.RequestPath))
                _ = server.TryUnmapExactFile(result.RequestPath, result.StagedPath);
            return TryDeleteStagedOverride(viewerDataRoot, result.StagedPath);
        }

        /// <summary>Deletes only a file registered to this process with the same file identity.</summary>
        internal static bool TryDeleteStagedOverride(string viewerDataRoot, string stagedPath)
        {
            try
            {
                string stagingDirectory = ResolveStagingDirectory(viewerDataRoot);
                string canonicalPath = Path.GetFullPath(stagedPath);
                if (!IsDirectCanonicalStagingChild(stagingDirectory, canonicalPath) ||
                    !ActiveStagedOverrides.TryGetValue(canonicalPath, out var identity))
                    return false;

                if (!FileSystemReparseGuard.TryDeleteVerifiedFile(
                        stagingDirectory,
                        canonicalPath,
                        identity))
                    return false;

                return ActiveStagedOverrides.TryRemove(
                    new KeyValuePair<string, FileSystemReparseGuard.FileIdentity>(canonicalPath, identity));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                DebugLog.Error(
                    "Magic.Override",
                    $"Owned staging cleanup failed {stagedPath}: {ex.Message}",
                    ex);
                return false;
            }
        }

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>
        /// Compatibility entry point for developer builds. It stages instead of modifying selected
        /// data; product UI consumes the richer result to map an exact in-process route.
        /// </summary>
        public static string ApplyMagicOverride(int magicId, byte[] customBytes)
        {
            string? magicDirectory = ResolveMagicDir();
            if (magicDirectory == null)
                return Strings.U_Md_OverrideNoNoclip;
            MagicOverrideResult result = StageMagicOverride(
                magicId,
                customBytes,
                magicDirectory,
                ViewerHubService.ViewerDataRoot);
            return result.Message;
        }

        /// <summary>
        /// Explicitly recovers safe legacy backups produced by older editor versions. Normal preview
        /// staging never creates these files and never changes the selected extraction.
        /// </summary>
        public static int RestoreAllOverrides(out string detail)
        {
            string? magicDirectory = ResolveMagicDir();
            if (string.IsNullOrWhiteSpace(magicDirectory))
            {
                detail = "Nenhum override de magic ativo.";
                return 0;
            }

            int restored = 0;
            var restoredNames = new List<string>();
            try
            {
                string root = Path.GetFullPath(magicDirectory);
                if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(root, out var verifiedDirectory) ||
                    verifiedDirectory == null)
                {
                    detail = "Nenhum override de magic ativo.";
                    return 0;
                }
                verifiedDirectory.Dispose();

                foreach (string backupPath in Directory.EnumerateFiles(
                    root,
                    "*.bin" + BackupSuffix,
                    SearchOption.TopDirectoryOnly))
                {
                    string targetName = Path.GetFileName(backupPath)[..^BackupSuffix.Length];
                    if (!IsCanonicalMagicFileName(targetName))
                        continue;
                    string targetPath = Path.Combine(root, targetName);
                    if (FileSystemReparseGuard.TryOpenVerifiedRead(root, backupPath, out var backup) !=
                            FileSystemReparseGuard.VerifiedOpenResult.Success || backup == null)
                        continue;
                    using (backup)
                    {
                        byte[] content;
                        using (var buffer = new MemoryStream())
                        {
                            backup.Stream.CopyTo(buffer);
                            content = buffer.ToArray();
                        }
                        WriteOrReplaceVerified(root, targetName, content);
                    }
                    _ = FileSystemReparseGuard.TryDeleteVerifiedFile(root, backupPath);
                    restored++;
                    restoredNames.Add(targetName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                DebugLog.Error("Magic.Override", $"Legacy restore failed: {ex.Message}", ex);
            }

            detail = restored == 0
                ? "Nenhum override de magic ativo."
                : $"{restored} magic(s) restaurado(s): {string.Join(", ", restoredNames)}";
            return restored;
        }
#endif

        private static FileSystemReparseGuard.VerifiedReadFile OpenStagedRead(
            FileSystemReparseGuard.VerifiedDirectory stagingDirectory,
            string stagedName,
            FileSystemReparseGuard.FileIdentity expectedIdentity)
        {
            FileSystemReparseGuard.VerifiedOpenResult result =
                FileSystemReparseGuard.TryOpenVerifiedRead(stagingDirectory, stagedName, out var verified);
            if (result != FileSystemReparseGuard.VerifiedOpenResult.Success || verified == null ||
                verified.Identity != expectedIdentity)
            {
                verified?.Dispose();
                throw new IOException("The staged Magic preview identity changed before verification.");
            }
            return verified;
        }

#if FFX_INCLUDE_DEVTOOLS
        private static void WriteOrReplaceVerified(string root, string destinationName, byte[] content)
        {
            using FileSystemReparseGuard.VerifiedDirectory directory =
                FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(root);
            using FileStream temporary = FileSystemReparseGuard.CreateNewVerifiedFile(
                directory,
                $".{Guid.NewGuid():N}.tmp");
            temporary.Write(content, 0, content.Length);
            temporary.Flush(flushToDisk: true);
            _ = FileSystemReparseGuard.PromoteOpenedFile(directory, temporary, destinationName);
        }
#endif

        /// <summary>
        /// Probes the running loopback server for a bin absent from the local root. The managed
        /// bootstrap seeds only one file per directory; its fetch-through downloads and caches the
        /// file on demand — a 2xx both proves the id is inside the extraction and materializes it
        /// on disk. Roots without fetch-through (user-owned extractions) simply 404 and the caller
        /// keeps the existing missing-bin refusal.
        /// </summary>
        private static bool TryFetchViaLoopbackProbe(StudioWebServer? server, string requestPath)
        {
            if (server is not { IsRunning: true })
                return false;
            try
            {
                // Timeout must outlast the server's own 60s CDN fetch bound so a slow download is
                // not misreported as missing.
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                using HttpResponseMessage response = client.GetAsync(
                    $"http://127.0.0.1:{server.Port}{requestPath}",
                    HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                OperationCanceledException or InvalidOperationException)
            {
                return false;
            }
        }

        private static string ResolveStagingDirectory(string viewerDataRoot) =>
            Path.GetFullPath(Path.Combine(viewerDataRoot, "magic-overrides", "11"));

        private static bool IsDirectCanonicalStagingChild(string root, string candidate)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetDirectoryName(candidate), root, comparison) &&
                IsCanonicalStagedMagicFileName(Path.GetFileName(candidate));
        }

#if FFX_INCLUDE_DEVTOOLS
        private static bool IsCanonicalMagicFileName(string fileName) =>
            fileName.Length == 8 &&
            fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(
                fileName.AsSpan(0, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out _);
#endif

        private static bool IsCanonicalStagedMagicFileName(string fileName)
        {
            if (!fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                return false;
            string stem = Path.GetFileNameWithoutExtension(fileName);
            return stem.Length == 37 && stem[4] == '.' &&
                ushort.TryParse(
                    stem.AsSpan(0, 4),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out _) &&
                Guid.TryParseExact(stem.AsSpan(5), "N", out _);
        }
    }
}
