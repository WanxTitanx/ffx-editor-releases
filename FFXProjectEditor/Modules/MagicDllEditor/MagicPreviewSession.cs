using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Modules.Common.ViewerHub;

namespace FFXProjectEditor.Modules.MagicDllEditor;

/// <summary>
/// Publishes the PC DLL's shared PPP resource data, with explicit resource/handler metadata.
/// NoClip cannot execute or sniff PE/x86 code as a PS2 overlay. Only the .data payload is served;
/// neither the installed DLL nor the selected NoClip extraction is written.
/// </summary>
internal sealed class MagicPreviewSession : IDisposable
{
    private readonly string directory;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Revision { get; private set; } = string.Empty;

    public MagicPreviewSession(string viewerDataRoot)
    {
        directory = Path.GetFullPath(Path.Combine(viewerDataRoot, "magic-preview", Id));
        if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(directory))
            throw new IOException("Magic preview staging contains a reparse point.");
        Directory.CreateDirectory(directory);
    }

    public void Publish(MagicDllFile file, byte[] workingBytes, string label, string[]? textureRoots = null)
    {
        if (file.MagicId < 0 || file.MagicId > ushort.MaxValue || file.Roots.Count == 0)
            throw new InvalidDataException("The DLL has no supported PPP particle resource.");
        if (file.DataSectionRawPtr < 0 || file.DataSectionSize < 84 ||
            file.DataSectionRawPtr > workingBytes.Length - file.DataSectionSize)
            throw new InvalidDataException("The DLL data section is outside the working bytes.");

        byte[] data = workingBytes.AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray();
        var resources = FindResources(data, file.Roots.Select(r => r.RootAbs).ToArray());
        if (resources.Count != file.Roots.Count)
            throw new InvalidDataException("Could not match the PPP roots to their texture/resource headers.");
        var names = MagicFpHandlerTable.TryExtractFromDll(file)?.Names;
        if (names == null || names.Count == 0)
            throw new InvalidDataException("The DLL has no readable PPP handler table.");

        string workingRevision = Convert.ToHexString(SHA256.HashData(workingBytes)).ToLowerInvariant();
        string binaryName = workingRevision + ".bin";
        var textures = new List<object>();
        var textureWarnings = new List<string>();
        var selectedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string textureRoot in textureRoots ?? Array.Empty<string>())
        {
            string folder = Path.Combine(textureRoot, $"magic_{file.MagicId:D4}", "tex", "d3d11");
            if (!Directory.Exists(folder) || FileSystemReparseGuard.ContainsReparsePointInExistingChain(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.dds.phyre"))
                selectedTextures[Path.GetFileName(path)] = path; // project overrides extraction, per texture
        }
        var revisionParts = new List<string> { workingRevision };
        foreach (var pair in selectedTextures.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryTextureKey(pair.Key, out int tbp, out int psm, out int width, out int height)) continue;
            if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(pair.Value)) continue;
            var decode = Ps3MagicTextureReader.Decode(pair.Value);
            try
            {
                if (!decode.Ok || decode.Bgra == null || decode.Width <= 0 || decode.Height <= 0 ||
                    (long)decode.Width * decode.Height * 4 != decode.Bgra.Length)
                {
                    textureWarnings.Add(pair.Key);
                    continue;
                }
                byte[] rgba = (byte[])decode.Bgra.Clone();
                for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i+2]) = (rgba[i+2], rgba[i]);
                string hash = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant();
                string name = hash + ".rgba.bin";
                if (!File.Exists(Path.Combine(directory, name))) WriteAtomic(name, rgba);
                revisionParts.Add($"{pair.Key}:{hash}");
                textures.Add(new { tbp, psm, sourceWidth = width, sourceHeight = height,
                    width = decode.Width, height = decode.Height, name = pair.Key,
                    url = $"/viewer-data/magic-preview/{Id}/{name}" });
            }
            finally { decode.Bitmap?.Dispose(); }
        }
        string revision = textures.Count == 0 ? workingRevision : Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", revisionParts)))).ToLowerInvariant();
        // Content-addressed files keep an in-flight browser read consistent with its manifest.
        // They belong exclusively to this session and are removed together on close.
        if (!File.Exists(Path.Combine(directory, binaryName)))
            WriteAtomic(binaryName, data);
        var manifest = new
        {
            format = "ffx-pc-magic-v1", revision, workingRevision, magicId = file.MagicId, label,
            binaryUrl = $"/viewer-data/magic-preview/{Id}/{binaryName}",
            textures, textureWarnings,
            headers = resources.Select(r => r.Header).Distinct().ToArray(),
            particleIndex = resources[0].ParticleIndex,
            rootCount = file.Roots.Count,
            handlerNames = Enumerable.Range(0, names.Keys.Max() + 1)
                .Select(i => names.TryGetValue(i, out string? name) ? name : null).ToArray(),
            usedHandlers = file.Roots.SelectMany(r => r.HandlerIndicesUsed).Distinct().Order().ToArray(),
        };
        WriteAtomic("current.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
        Revision = revision;
    }

    internal static bool TryTextureKey(string name, out int tbp, out int psm, out int width, out int height)
    {
        tbp = psm = width = height = 0;
        string[] parts = name.Split('.')[0].Split('_');
        return parts.Length == 6 && int.TryParse(parts[0], out tbp) && tbp is >= 0 and <= 16383 &&
            int.TryParse(parts[1], out psm) && psm is >= 0 and <= 63 &&
            parts[2] == "0" && parts[3] == "0" &&
            int.TryParse(parts[4], out width) && width is > 0 and <= 8192 &&
            int.TryParse(parts[5], out height) && height is > 0 and <= 8192;
    }

    // These are the resource-header links consumed by NoClip's FFX texture/particle reader:
    // header+0x3c -> resource data; data+0x20 -> particle table; data+0x50 -> count.
    // Require a unique chain to an already validated PPP root; never guess a header by ID.
    internal static IReadOnlyList<(int Header, int ParticleIndex)> FindResources(byte[] data, int[] roots)
    {
        var result = new List<(int, int)>();
        foreach (int root in roots)
        {
            var candidates = new List<(int, int)>();
            for (int header = 0; header <= data.Length - 84; header += 4)
            {
                long start = header + (long)BitConverter.ToUInt32(data, header + 60);
                if (start < header + 64 || start > data.Length - 84) continue;
                int ds = (int)start;
                int count = BitConverter.ToUInt16(data, ds + 80);
                long table = start + BitConverter.ToUInt32(data, ds + 32);
                if (count is < 1 or > 64 || table < start + 84 || table > data.Length - 4L * count)
                    continue;
                for (int i = 0; i < count; i++)
                    if (start + BitConverter.ToUInt32(data, (int)table + 4 * i) == root)
                        candidates.Add((header, i));
            }
            if (candidates.Count != 1) return Array.Empty<(int, int)>();
            result.Add(candidates[0]);
        }
        return result;
    }

    private void WriteAtomic(string name, byte[] bytes)
    {
        if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(directory))
            throw new IOException("Magic preview staging contains a reparse point.");
        string path = Path.Combine(directory, name), temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory) && !FileSystemReparseGuard.ContainsReparsePointInExistingChain(directory))
            Directory.Delete(directory, recursive: true);
    }
}
