using FFXProjectEditor.FfxLib.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FFXProjectEditor.Services;

/// <summary>
/// Shared source policy: workspace, extracted source tree, then the installed VBF.
/// Archive fallback reads only the requested entry and never creates loose files
/// or modifies the installation as a side effect of browsing.
/// </summary>
internal static class GameAssetSourceResolver
{
    sealed record Index(long Length, DateTime Modified, VbfArchive Archive, Dictionary<string, VbfEntry> Entries);
    static readonly object gate = new();
    static readonly Dictionary<string, Index> indexes = new(StringComparer.Ordinal);

    public static GameAssetSource? ResolveMasterFile(string relativePath, string? projectPath,
        string? ffxPs2Root, string? gameInstallRoot = null)
    {
        string virtualPath = VbfArchive.NormalizePath(relativePath);
        string physical = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string? loose = PortablePathResolver.FirstExistingFile(
            projectPath == null ? null : Path.Combine(projectPath, physical),
            ffxPs2Root == null ? null : Path.Combine(ffxPs2Root, "ffx", "master", physical));
        if (loose != null) return GameAssetSource.FromLoose(loose, virtualPath);
        return ResolveArchiveFile("ffx_ps2/ffx/master/" + virtualPath, "FFX_Data.vbf", gameInstallRoot, virtualPath);
    }

    public static GameAssetSource? ResolveArchiveFile(string entryPath, string archiveName = "FFX_Data.vbf",
        string? gameInstallRoot = null, string? workspaceRelativePath = null)
    {
        if (archiveName is not ("FFX_Data.vbf" or "FFX2_Data.vbf" or "metamenu.vbf"))
            throw new ArgumentException("Unsupported game archive.", nameof(archiveName));
        entryPath = VbfArchive.NormalizePath(entryPath);
        string? root = gameInstallRoot ?? PortablePathResolver.GameInstallRoot;
        if (string.IsNullOrWhiteSpace(root)) return null;
        string? archivePath = PortablePathResolver.FirstExistingFile(Path.Combine(root, "data", archiveName));
        if (archivePath == null) return null;
        var info = new FileInfo(archivePath);
        Index index;
        lock (gate)
        {
            if (!indexes.TryGetValue(archivePath, out index!) || index.Length != info.Length || index.Modified != info.LastWriteTimeUtc)
            {
                var archive = VbfArchive.Open(archivePath);
                index = new Index(info.Length, info.LastWriteTimeUtc, archive,
                    archive.Entries.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase));
                // Bound metadata retention when switching between installations.
                if (indexes.Count >= 4) indexes.Clear();
                indexes[archivePath] = index;
            }
        }
        return index.Entries.TryGetValue(entryPath, out var entry)
            ? GameAssetSource.FromArchive(index.Archive, entry, workspaceRelativePath ?? entryPath)
            : null;
    }
}

internal sealed class GameAssetSource
{
    readonly VbfArchive? archive;
    readonly VbfEntry? entry;
    public string? LoosePath { get; }
    public string RelativePath { get; }
    public bool IsArchive => archive != null;
    public string Description => LoosePath ?? archive!.FilePath + "!/" + entry!.Path;

    GameAssetSource(string? loosePath, string relativePath, VbfArchive? archive = null, VbfEntry? entry = null)
    { LoosePath = loosePath; RelativePath = relativePath; this.archive = archive; this.entry = entry; }

    internal static GameAssetSource FromLoose(string path, string relative) => new(path, relative);
    internal static GameAssetSource FromArchive(VbfArchive archive, VbfEntry entry, string relative) => new(null, relative, archive, entry);

    public byte[] ReadAllBytes(CancellationToken token = default)
    {
        const int limit = 128 * 1024 * 1024;
        if (LoosePath != null)
        {
            VbfSafeFileSystem.RejectLinks(LoosePath);
            using var input = new FileStream(LoosePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > limit) throw new InvalidDataException("This asset exceeds the in-memory editor limit.");
            byte[] bytes = new byte[(int)input.Length];
            token.ThrowIfCancellationRequested(); input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new IOException("Asset changed while it was being read.");
            return bytes;
        }
        long size = entry!.Size;
        if (size > limit) throw new InvalidDataException("This asset exceeds the in-memory editor limit; use streaming extraction.");
        using var output = new MemoryStream((int)size);
        CopyTo(output, token);
        return output.ToArray();
    }

    public void CopyTo(Stream output, CancellationToken token = default)
    {
        if (archive != null && entry != null) { archive.ExtractTo(entry, output, token); return; }
        VbfSafeFileSystem.RejectLinks(LoosePath!);
        using var input = new FileStream(LoosePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[VbfArchive.BlockSize];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
    }

    public string? EditablePath(string? workspace)
    {
        if (LoosePath != null) return LoosePath;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) return null;
        return VbfSafeFileSystem.Destination(workspace, RelativePath);
    }
}
