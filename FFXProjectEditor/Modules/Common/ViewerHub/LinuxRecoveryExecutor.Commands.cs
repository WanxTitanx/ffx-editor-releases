// WHY: Only a live, bounded and privately captured domain command may enter Linux persistence.
// MAINT: This sealed union has three factories, no I/O callback, and accepts no persisted Intent.
// The document adapter holds its own gate while capturing; copying is not an atomic read of a racing array.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using F = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryExecutor
{
    internal readonly record struct SidecarValues(int Slot, double? X, double? Y, double? Z,
        double? Heading, double? Scale);
    internal sealed record RestoreProvenance(string TargetSha256, string BackupSha256, F.FileIdentity BackupIdentity);

    internal sealed class Command
    {
        private readonly byte[] _original;
        private readonly byte[] _working;
        private Command(R.OperationKind kind, string rootPath, string targetLeaf,
            string? sourcePath, string? gameRoot, byte[] original, byte[] working,
            RestoreProvenance? restore, SidecarValues? sidecar)
        {
            Kind = kind; RootPath = rootPath; TargetLeaf = targetLeaf; SourcePath = sourcePath;
            GameRoot = gameRoot; _original = original; _working = working;
            Restore = restore; Sidecar = sidecar;
        }

        internal R.OperationKind Kind { get; }
        internal string RootPath { get; }
        internal string TargetLeaf { get; }
        internal string? SourcePath { get; }
        internal string? GameRoot { get; }
        internal ReadOnlySpan<byte> Original => _original;
        internal ReadOnlySpan<byte> Working => _working;
        internal RestoreProvenance? Restore { get; }
        internal SidecarValues? Sidecar { get; }

        internal static Command Save(string destinationPath, string? sourcePath, string? gameRoot,
            byte[] original, byte[] working)
        {
            ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(working);
            var target = MagicTarget(destinationPath, sourcePath, gameRoot);
            CheckPayload(original.Length); CheckPayload(working.Length); // Both bounds before either copy.
            BeforePhaseForTests?.Invoke("capture-save", target.Root);
            return new(R.OperationKind.MagicSave, target.Root, target.Leaf, sourcePath, gameRoot,
                original.ToArray(), working.ToArray(), null, null);
        }

        internal static Command RestoreLastSave(string targetPath, string lastSavedPath, string? sourcePath,
            string? gameRoot, string targetSha256, string backupSha256, F.FileIdentity backupIdentity)
        {
            var target = MagicTarget(targetPath, sourcePath, gameRoot);
            CheckCommandPath(lastSavedPath);
            if (!string.Equals(targetPath, lastSavedPath, StringComparison.Ordinal) ||
                backupIdentity.Kind != F.FileIdentityKind.Linux)
                throw new ArgumentException("Restore must name the exact last Linux copy owned by this document.");
            var proof = new RestoreProvenance(CanonicalHash(targetSha256), CanonicalHash(backupSha256), backupIdentity);
            return new(R.OperationKind.MagicRestore, target.Root, target.Leaf, sourcePath, gameRoot,
                Array.Empty<byte>(), Array.Empty<byte>(), proof, null);
        }

        internal static Command EditSidecar(string outputRoot, int encounterId, int slot,
            double? x, double? y, double? z, double? heading, double? scale)
        {
            string root = CheckRootPath(outputRoot);
            if (encounterId is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(encounterId));
            if (slot is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(slot));
            foreach (double? value in new[] { x, y, z, heading, scale })
                if (value.HasValue && !double.IsFinite(value.Value))
                    throw new ArgumentException("Sidecar deltas must be finite.");
            return new(R.OperationKind.SidecarEdit, root,
                encounterId.ToString(CultureInfo.InvariantCulture) + ".json", null, null,
                Array.Empty<byte>(), Array.Empty<byte>(), null, new(slot, x, y, z, heading, scale));
        }
    }

    // ── Raw syntax and copy-only domain boundaries ──
    // Validate BEFORE GetDirectoryName/Combine: GetFullPath would erase forbidden dot components.
    // The Linux adapter supplies the captured configured install root; Steam structure remains a fallback.
    private const int MaximumRootBytes = 16384;
    private const int MaximumCommandPathBytes = MaximumRootBytes + 1 + 255;

    private static (string Root, string Leaf) MagicTarget(string path, string? source, string? gameRoot)
    {
        CheckCommandPath(path);
        string root = CheckRootPath(Path.GetDirectoryName(path) ??
            throw new ArgumentException("A Magic copy needs an explicit output directory."));
        string leaf = Path.GetFileName(path);
        CheckPublicLeaf(leaf); CheckPublicLeaf(leaf + ".bak");
        if (source is not null) CheckCommandPath(source);
        if (gameRoot is { Length: > MaximumRootBytes + 1 })
            throw new ArgumentException("Configured game root exceeds its path bound.");
        string? protectedRoot = string.IsNullOrWhiteSpace(gameRoot) ? null :
            LinuxReadFileSystem.NormalizeAbsoluteRoot(gameRoot);
        // The UTF16 pre-bound limits parsing work, but multibyte roots still need the byte bound.
        if (protectedRoot is not null && Encoding.UTF8.GetByteCount(protectedRoot) > MaximumRootBytes)
            throw new ArgumentException("Configured game root exceeds its UTF8 path bound.");
        foreach (string target in new[] { path, path + ".bak" })
        {
            if (string.Equals(target, source, StringComparison.Ordinal) ||
                (protectedRoot is not null && Within(target, protectedRoot)) ||
                target.Split('/').Any(part => string.Equals(part, "SteamLibrary", StringComparison.OrdinalIgnoreCase)) ||
                target.Contains("/steamapps/common/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Magic source and installed-game paths are read-only, including the backup leaf.");
        }
        return (root, leaf);
    }

    private static string CheckRootPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > MaximumRootBytes + 1)
            throw new ArgumentException("Recovery output root exceeds its path bound.");
        string root = LinuxReadFileSystem.NormalizeAbsoluteRoot(path);
        if (Encoding.UTF8.GetByteCount(root) > MaximumRootBytes ||
            root.Split('/').Any(part => part.StartsWith(R.JournalLeaf, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A recovery output root cannot be oversized or inside a recovery journal.");
        return root;
    }

    private static void CheckPublicLeaf(string leaf)
    {
        if (leaf.Length is < 1 or > 255) throw new ArgumentException("Invalid output leaf length.");
        LinuxOutputFileSystem.ValidateLeaf(leaf);
        if (leaf.StartsWith(R.JournalLeaf, StringComparison.OrdinalIgnoreCase) ||
            leaf.StartsWith(R.RetentionPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Recovery metadata cannot be a public write target.");
    }

    private static void CheckCommandPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > MaximumCommandPathBytes)
            throw new ArgumentException("Command path exceeds its byte bound.");
        LinuxReadFileSystem.ValidateAbsolutePath(path); // Bounded UTF16 length before lexical Split.
        if (Encoding.UTF8.GetByteCount(path) > MaximumCommandPathBytes)
            throw new ArgumentException("Command path exceeds its byte bound.");
    }

    private static bool Within(string path, string root) =>
        root == "/" || path == root || path.StartsWith(root + "/", StringComparison.Ordinal);

    private static void CheckPayload(int length)
    {
        if (length > R.MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(length), "Linux payload exceeds 64 MiB.");
    }

    private static string CanonicalHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64) throw new ArgumentException("Restore provenance needs a SHA256.");
        _ = Convert.FromHexString(value);
        return value.ToUpperInvariant();
    }
}
