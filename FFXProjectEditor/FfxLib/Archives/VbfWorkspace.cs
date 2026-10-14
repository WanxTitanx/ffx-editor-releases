using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FFXProjectEditor.FfxLib.Archives;

internal sealed record VbfContent(string Path, long Size, VbfArchive? Archive = null,
    VbfEntry? Entry = null, string? LocalPath = null, DateTime Modified = default)
{
    public long PackedSize => Entry?.PackedSize ?? Size;
    public bool IsPending => LocalPath != null;

    internal FileStream OpenLocal()
    {
        VbfSafeFileSystem.RejectLinks(LocalPath!);
        var stream = new FileStream(LocalPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != Size || File.GetLastWriteTimeUtc(LocalPath!) != Modified)
        {
            stream.Dispose();
            throw new IOException("An imported file changed. Add it again: " + LocalPath);
        }
        return stream;
    }
}

/// <summary>Edits are staged; neither imported files nor the opened VBF are written.</summary>
internal sealed class VbfWorkspace
{
    readonly Dictionary<string, VbfContent> files = new(StringComparer.OrdinalIgnoreCase);
    readonly Stack<Dictionary<string, VbfContent?>> undo = new();
    public VbfArchive? Original { get; }
    public IReadOnlyCollection<VbfContent> Files => files.Values;
    public bool HasChanges => undo.Count != 0;

    public VbfWorkspace(VbfArchive? original = null)
    {
        Original = original;
        if (original != null)
            foreach (var entry in original.Entries)
                files.Add(entry.Path, new VbfContent(entry.Path, entry.Size, original, entry));
    }

    public static IReadOnlyList<VbfContent> Import(IEnumerable<string> paths, string folder = "",
        bool folderContents = false, CancellationToken token = default)
    {
        string prefix = string.IsNullOrEmpty(folder) ? "" : VbfArchive.NormalizePath(folder.TrimEnd('/')) + "/";
        var result = new Dictionary<string, VbfContent>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in paths)
        {
            token.ThrowIfCancellationRequested();
            string full = System.IO.Path.GetFullPath(source);
            VbfSafeFileSystem.RejectLinks(full);
            if (Directory.Exists(full))
            {
                string basePath = prefix + (folderContents ? "" : System.IO.Path.GetFileName(full.TrimEnd(System.IO.Path.DirectorySeparatorChar)) + "/");
                var directories = new Stack<string>();
                directories.Push(full);
                while (directories.Count != 0)
                {
                    token.ThrowIfCancellationRequested();
                    string dir = directories.Pop();
                    foreach (string child in Directory.EnumerateFileSystemEntries(dir))
                    {
                        VbfSafeFileSystem.RejectLinks(child);
                        if (Directory.Exists(child)) directories.Push(child);
                        else Add(child, basePath + System.IO.Path.GetRelativePath(full, child));
                    }
                }
            }
            else Add(full, prefix + System.IO.Path.GetFileName(full));
        }
        return result.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();

        void Add(string local, string name)
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(local);
            if (!info.Exists) throw new FileNotFoundException("Import file not found.", local);
            name = VbfArchive.NormalizePath(name);
            if (!result.TryAdd(name, new VbfContent(name, info.Length, LocalPath: local, Modified: info.LastWriteTimeUtc)))
                throw new IOException("Multiple input files map to the same VBF path: " + name);
            if (result.Count > VbfArchive.MaxEntries) throw new IOException("Too many input files.");
        }
    }

    public void Add(IEnumerable<VbfContent> contents)
    {
        var incoming = contents.ToArray();
        var previous = new Dictionary<string, VbfContent?>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in incoming)
        {
            string name = VbfArchive.NormalizePath(content.Path);
            if (name != content.Path || !previous.TryAdd(name, files.GetValueOrDefault(name)))
                throw new ArgumentException("Duplicate or non-normalized import path.");
        }
        if (files.Count + previous.Count(x => x.Value == null) > VbfArchive.MaxEntries)
            throw new IOException("Too many VBF entries.");
        ValidateHierarchy(files.Keys.Concat(incoming.Select(x => x.Path)));
        foreach (var content in incoming) files[content.Path] = content;
        if (previous.Count > 0) undo.Push(previous);
    }

    public IReadOnlyList<VbfContent> Select(IEnumerable<string> paths)
    {
        var selected = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        string[] folders = selected.Where(x => x.EndsWith('/')).ToArray();
        return files.Values.Where(x => selected.Contains(x.Path) ||
            folders.Any(f => x.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public void Remove(IEnumerable<string> paths)
    {
        var previous = Select(paths).ToDictionary(x => x.Path, x => (VbfContent?)x, StringComparer.OrdinalIgnoreCase);
        foreach (string path in previous.Keys) files.Remove(path);
        if (previous.Count > 0) undo.Push(previous);
    }

    public void Rename(string oldPath, string newPath)
    {
        string normalized = VbfArchive.NormalizePath(newPath.TrimEnd('/'));
        var selected = Select(new[] { oldPath });
        if (selected.Count == 0) throw new ArgumentException("Select a file or folder to rename.");
        bool folder = oldPath.EndsWith('/');
        var moved = selected.Select(x => x with { Path = folder ? normalized + "/" + x.Path[oldPath.Length..] : normalized }).ToArray();
        var originalNames = selected.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (moved.Any(x => files.ContainsKey(x.Path) && !originalNames.Contains(x.Path)))
            throw new IOException("The destination already exists.");
        ValidateHierarchy(files.Keys.Where(x => !originalNames.Contains(x)).Concat(moved.Select(x => x.Path)));
        var previous = selected.ToDictionary(x => x.Path, x => (VbfContent?)x, StringComparer.OrdinalIgnoreCase);
        foreach (var item in moved) previous.TryAdd(item.Path, files.GetValueOrDefault(item.Path));
        foreach (var item in selected) files.Remove(item.Path);
        foreach (var item in moved) files[item.Path] = item;
        undo.Push(previous);
    }

    public void Undo()
    {
        if (!undo.TryPop(out var previous)) return;
        foreach (var change in previous)
                if (change.Value == null) files.Remove(change.Key); else files[change.Key] = change.Value;
    }

    internal static void ValidateHierarchy(IEnumerable<string> paths)
    {
        var names = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            for (int slash = name.IndexOf('/'); slash >= 0; slash = name.IndexOf('/', slash + 1))
                if (names.Contains(name[..slash]))
                    throw new IOException("A VBF file conflicts with a folder path: " + name);
    }
}

internal static class VbfSafeFileSystem
{
    internal static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static void RejectLinks(string path)
    {
        for (string? current = System.IO.Path.GetFullPath(path); current != null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and reparse points are not allowed: " + current);
    }

    internal static void EnsureSpace(string destination, long required)
    {
        string full = System.IO.Path.GetFullPath(destination);
        var drive = DriveInfo.GetDrives().Where(x => x.IsReady && full.StartsWith(x.RootDirectory.FullName, PathComparison))
            .OrderByDescending(x => x.RootDirectory.FullName.Length).FirstOrDefault();
        if (drive != null && drive.AvailableFreeSpace < required)
            throw new IOException($"Not enough disk space. Required: {required:N0} bytes; available: {drive.AvailableFreeSpace:N0} bytes.");
    }

    internal static string Destination(string root, string name)
    {
        root = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, VbfArchive.NormalizePath(name).Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, PathComparison)) throw new IOException("Extraction path escapes its destination.");
        RejectLinks(target);
        return target;
    }
}
