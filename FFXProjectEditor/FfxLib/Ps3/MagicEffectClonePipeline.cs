using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.Magic;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Deploy dedicated magic_#### clones: ps3data via ff10-file-loader + runtime DLL.</summary>
    public static class MagicEffectClonePipeline
    {
        public const string VirtualPs3MagicRoot = "FFX_Data/GameData/PS3Data/magic";

        public static string ResolveModsPs3MagicFolder(string gameInstallRoot, int magicId) =>
            Path.Combine(gameInstallRoot, "data", "mods", VirtualPs3MagicRoot, $"magic_{magicId:D4}");

        public static MagicEffectCloneDeployResult DeployClone(
            int sourceMagicId,
            int targetMagicId,
            string ps3ExtractMagicRoot,
            string gameInstallRoot,
            string magicDllRoot,
            bool applyPrismTextureRecolor = false,
            bool applyDrasticColorRecolor = false,
            string? selectedSourceDll = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ps3ExtractMagicRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(gameInstallRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(magicDllRoot);
            if (sourceMagicId is < 0 or > 9999)
                throw new ArgumentOutOfRangeException(nameof(sourceMagicId));
            if (targetMagicId is < 0 or > 9999)
                throw new ArgumentOutOfRangeException(nameof(targetMagicId));
            if (sourceMagicId == targetMagicId)
                throw new ArgumentException("Source and target Magic IDs must be different.", nameof(targetMagicId));

            string sourceFolder = Path.Combine(ps3ExtractMagicRoot, $"magic_{sourceMagicId:D4}");
            string targetModsFolder = ResolveModsPs3MagicFolder(gameInstallRoot, targetMagicId);
            string sourceDll = ResolveSelectedSourceDll(magicDllRoot, sourceMagicId, selectedSourceDll);
            string targetDll = Path.Combine(magicDllRoot, $"magic_{targetMagicId:D4}.dll");

            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException($"ps3data source missing: {sourceFolder}");
            if (!File.Exists(sourceDll))
                throw new FileNotFoundException($"Source DLL missing: {sourceDll}");

            // This command is reachable from a root selected by the user and that root can be
            // the live magicFiles\FFX directory. An occupied destination is therefore never an
            // implicit update target. Check both destinations before creating any output.
            ThrowIfDestinationExists(targetModsFolder, "mods payload folder");
            ThrowIfMagicIdExists(magicDllRoot, targetMagicId);

            string transactionId = Guid.NewGuid().ToString("N");
            string stagedModsFolder = targetModsFolder + $".ffxms-staging-{transactionId}";
            string stagedDll = targetDll + $".ffxms-staging-{transactionId}";
            bool dllCommitted = false;
            byte[]? stagedDllHash = null;

            string? recoloredTexture = null;
            int recoloredTextureCount = 0;
            int textureCount = 0;
            try
            {
                CopyDirectory(sourceFolder, stagedModsFolder, overwrite: false);
                Directory.CreateDirectory(Path.GetDirectoryName(targetDll)!);
                byte[] rewrittenDll = MagicDllNameRewriter.RewriteMagicNameStrings(
                    File.ReadAllBytes(sourceDll),
                    sourceMagicId,
                    targetMagicId);
                stagedDllHash = SHA256.HashData(rewrittenDll);
                using (var stream = new FileStream(
                    stagedDll,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.WriteThrough))
                {
                    stream.Write(rewrittenDll, 0, rewrittenDll.Length);
                    stream.Flush(flushToDisk: true);
                }
                if (!FilesMatch(stagedDllHash, stagedDll))
                    throw new IOException("Staged Magic DLL hash does not match the safely rewritten bytes.");

                if (applyDrasticColorRecolor)
                {
                    Ps3MagicColorTransform transform = Ps3MagicColorTransform.DrasticMagenta;
                    foreach (string texture in Directory.EnumerateFiles(stagedModsFolder, "*.dds.phyre", SearchOption.AllDirectories)
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        string backup = texture + ".backup_drastic";
                        File.Copy(texture, backup, overwrite: false);
                        Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);
                        recoloredTexture ??= Path.Combine(
                            targetModsFolder,
                            Path.GetRelativePath(stagedModsFolder, texture));
                        recoloredTextureCount++;
                    }
                }
                else if (applyPrismTextureRecolor)
                {
                    Ps3MagicColorTransform transform = Ps3MagicColorTransform.PrismViolet;
                    foreach (string texture in Directory.EnumerateFiles(stagedModsFolder, "*.dds.phyre", SearchOption.AllDirectories)
                                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        string backup = texture + ".backup_prism";
                        File.Copy(texture, backup, overwrite: false);
                        Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);
                        recoloredTexture ??= Path.Combine(
                            targetModsFolder,
                            Path.GetRelativePath(stagedModsFolder, texture));
                        recoloredTextureCount++;
                    }
                }

                textureCount = Directory.EnumerateFiles(
                    stagedModsFolder,
                    "*.dds.phyre",
                    SearchOption.AllDirectories).Count();

                // Re-check at commit time to close the ordinary check/copy race. File.Move and
                // Directory.Move also fail closed if another process creates a target afterward.
                ThrowIfDestinationExists(targetModsFolder, "mods payload folder");
                ThrowIfMagicIdExists(magicDllRoot, targetMagicId);
                File.Move(stagedDll, targetDll);
                dllCommitted = true;
                Directory.Move(stagedModsFolder, targetModsFolder);
            }
            catch
            {
                // The target did not exist before this transaction. If the second commit fails,
                // remove only the DLL that this transaction just created, and only while its hash
                // still matches the staged rewritten bytes.
                if (dllCommitted && stagedDllHash != null &&
                    File.Exists(targetDll) && FilesMatch(stagedDllHash, targetDll))
                    File.Delete(targetDll);
                throw;
            }
            finally
            {
                if (File.Exists(stagedDll))
                    File.Delete(stagedDll);
                if (Directory.Exists(stagedModsFolder))
                    Directory.Delete(stagedModsFolder, recursive: true);
            }

            // Drastic mode: texture recolor only. Bulk DLL vec4 patch crashed in-game RT2 2026-06-14.

            return new MagicEffectCloneDeployResult(
                sourceMagicId,
                targetMagicId,
                sourceFolder,
                targetModsFolder,
                sourceDll,
                targetDll,
                textureCount,
                recoloredTexture,
                recoloredTextureCount,
                0,
                true,
                true);
        }

        private static void ThrowIfDestinationExists(string path, string label)
        {
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException($"Cannot deploy because the target {label} already exists: {path}");
        }

        private static string ResolveSelectedSourceDll(
            string magicDllRoot,
            int sourceMagicId,
            string? selectedSourceDll)
        {
            string fullRoot = Path.GetFullPath(magicDllRoot);
            string candidate = string.IsNullOrWhiteSpace(selectedSourceDll)
                ? Path.Combine(fullRoot, $"magic_{sourceMagicId:D4}.dll")
                : Path.GetFullPath(selectedSourceDll);

            if (!string.Equals(
                    Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar),
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected source DLL must be a direct child of the selected magicFiles\\FFX root.");

            if (!TryParseMagicDllName(candidate, out int parsedId) || parsedId != sourceMagicId)
                throw new InvalidDataException($"The selected source DLL does not own Magic id {sourceMagicId}: {candidate}");

            return candidate;
        }

        private static void ThrowIfMagicIdExists(string magicDllRoot, int magicId)
        {
            if (!Directory.Exists(magicDllRoot))
                return;

            string? existing = Directory.EnumerateFiles(magicDllRoot, "*.dll", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => TryParseMagicDllName(path, out int parsedId) && parsedId == magicId);
            if (existing != null)
                throw new IOException($"Cannot deploy because Magic id {magicId} already exists: {existing}");
        }

        private static bool TryParseMagicDllName(string path, out int magicId)
        {
            magicId = -1;
            if (!string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                return false;

            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("magic_", StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(
                       name.AsSpan("magic_".Length),
                       System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out magicId) &&
                   magicId is >= 0 and <= 9999;
        }

        private static bool FilesMatch(byte[] expectedHash, string path)
        {
            byte[] actualHash = SHA256.HashData(File.ReadAllBytes(path));
            return expectedHash.AsSpan().SequenceEqual(actualHash);
        }

        public static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase));

            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string dest = file.Replace(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase);
                string? parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.Copy(file, dest, overwrite);
            }
        }
    }

    public sealed record MagicEffectCloneDeployResult(
        int SourceMagicId,
        int TargetMagicId,
        string SourcePs3Folder,
        string DeployedPs3Folder,
        string SourceDll,
        string TargetDll,
        int DeployedTextureCount,
        string? RecoloredTexturePath,
        int RecoloredTextureCount = 0,
        int DllColorPatchCount = 0,
        bool TargetDllExists = false,
        bool ModsFolderExists = false)
    {
        public bool Pass => ModsFolderExists && TargetDllExists && DeployedTextureCount > 0;
    }
}
