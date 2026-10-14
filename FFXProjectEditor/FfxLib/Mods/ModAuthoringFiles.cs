using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Mods;

internal static class ModAuthoringFiles
{
    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static byte[] Read(string path, int maximum)
    {
        RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("File exceeds the contract size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static void RejectLinks(string path)
    {
        if (path.Split(new[] { '/', '\\' }).Any(part => part is "." or ".."))
            throw new InvalidDataException("Dot segments are not supported in authoring paths.");
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget != null || new DirectoryInfo(current).LinkTarget != null ||
                ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new InvalidDataException("Linked authoring paths are not supported.");
    }

    internal static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
