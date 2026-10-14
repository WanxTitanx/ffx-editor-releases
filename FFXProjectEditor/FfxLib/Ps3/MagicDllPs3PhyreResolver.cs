using FFXProjectEditor.Services;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Resolve the first inspectable Phyre package under a magic_#### PS3 folder.</summary>
    public static class MagicDllPs3PhyreResolver
    {
        public static string? ResolveFirstPhyrePackage(int magicId, string? ps3MagicRoot = null)
        {
            string folder = ResolveMagicFolder(magicId, ps3MagicRoot);
            if (!Directory.Exists(folder))
                return null;

            string? dds = Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(dds))
                return dds;

            return Directory.EnumerateFiles(folder, "*.phyre", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static string ResolveMagicFolder(int magicId, string? ps3MagicRoot = null)
        {
            string? root = ps3MagicRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                string? ps3 = PortablePathResolver.Ps3DataRoot;
                root = string.IsNullOrWhiteSpace(ps3) ? null : Path.Combine(ps3, "magic");
            }

            return string.IsNullOrWhiteSpace(root)
                ? string.Empty
                : Path.Combine(root, $"magic_{magicId:D4}");
        }
    }
}
