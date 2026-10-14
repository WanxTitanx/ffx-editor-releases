using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps3;

public sealed record NativeBossTextureTint(string Path, int Levels, int Blocks);
public sealed record NativeBossVisualTint(int MagicId, int SpriteColors,
    IReadOnlyList<NativeBossTextureTint> Textures, bool ExecutableCodePreserved);

/// <summary>Tint only owned clones. Reuse the proved Phyre texture-buffer codec
/// and WD3 sprite locations; never scan arbitrary DLL floats for possible colors.</summary>
public static class SpiraNativeBossVisuals
{
    public static bool HasSameExecutableCode(string donorDll, string clonedDll)
    {
        byte[] source = File.ReadAllBytes(donorDll), target = File.ReadAllBytes(clonedDll);
        var sections = MagicDllDecompiler.Inspect(donorDll).Sections.Where(section => section.Name == ".text").ToArray();
        return sections.Length != 0 && sections.All(section => source.AsSpan(section.RawPointer, section.RawSize)
            .SequenceEqual(target.AsSpan(section.RawPointer, section.RawSize)));
    }

    public static NativeBossVisualTint Tint(string ownedDll, string ownedAssetFolder, bool holy)
        => TintPalette(ownedDll, ownedAssetFolder, holy ? 1.0f : 0.58f,
            holy ? 0.82f : 0.16f, holy ? 0.45f : 0.88f);

    public static NativeBossVisualTint TintPalette(string ownedDll, string ownedAssetFolder,
        float red, float green, float blue)
        => TintPalette(ownedDll, ownedAssetFolder, red, green, blue, null);

    /// <summary>Opt-in preservation of native partial-mip textures. The strict
    /// texture writer remains unchanged; unsupported payloads are never edited.</summary>
    public static NativeBossVisualTint TintPalette(string ownedDll, string ownedAssetFolder,
        float red, float green, float blue, ICollection<string>? preservedPartialMipTextures)
    {
        if (new[] { red, green, blue }.Any(c => !float.IsFinite(c) || c < 0 || c > 1))
            throw new InvalidDataException("Unsupported visual palette.");
        byte[] before = File.ReadAllBytes(ownedDll);
        var inspection = MagicDllDecompiler.Inspect(ownedDll);
        int sprites = 0;
        if (before.AsSpan().IndexOf(new byte[] { (byte)'W', (byte)'D', (byte)'3', 1 }) >= 0)
        {
            var recolor = Wd3StreamRecolor.ApplyToAllStreams(ownedDll, ownedDll, red, green, blue, 1);
            byte[] tinted = File.ReadAllBytes(ownedDll);
            // Keep the original opacity/fade byte at every proved sprite-color site.
            foreach (var patch in recolor.Patches) tinted[patch.FileOffset + 3] = before[patch.FileOffset + 3];
            File.WriteAllBytes(ownedDll, tinted);
            sprites = recolor.PatchCount;
        }
        byte[] after = File.ReadAllBytes(ownedDll);
        bool codePreserved = inspection.Sections.Where(section => section.Name == ".text")
            .All(section => before.AsSpan(section.RawPointer, section.RawSize)
                .SequenceEqual(after.AsSpan(section.RawPointer, section.RawSize)));
        if (!codePreserved) throw new InvalidDataException("Tint changed executable code.");
        var textures = new List<NativeBossTextureTint>();
        int foundTextures = 0;
        foreach (string path in Directory.EnumerateFiles(ownedAssetFolder, "*.dds.phyre", SearchOption.AllDirectories))
        {
            foundTextures++;
            byte[]? originalTexture = preservedPartialMipTextures is null ? null : File.ReadAllBytes(path);
            try { textures.Add(TintTextureMipChain(path, red, green, blue)); }
            catch (InvalidDataException error) when (preservedPartialMipTextures is not null
                && error.Message == "Texture does not contain an exact full mip chain.")
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(originalTexture))
                    throw new InvalidDataException("Unsupported texture was modified before rejection.", error);
                preservedPartialMipTextures.Add(path);
            }
        }
        if (foundTextures == 0) throw new InvalidDataException("The visual donor has no texture payload.");
        string name = Path.GetFileNameWithoutExtension(ownedDll);
        return new(int.Parse(name.AsSpan("magic_".Length)), sprites, textures, codePreserved);
    }

    static NativeBossTextureTint TintTextureMipChain(string path, float red, float green, float blue)
    {
        if (!Ps3MagicTextureWriter.TryReadMip0Layout(path, out var layout, out string error) || layout is null)
            throw new InvalidDataException(error);
        byte[] source = File.ReadAllBytes(path);
        byte[] result = TintDxt5MipChain(source, layout.BufferStart, layout.Width, layout.Height,
            layout.Format, red, green, blue, out int levels, out int blocks);
        File.WriteAllBytes(path, result);
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(result)) throw new IOException("Texture readback failed.");
        return new(path, levels, blocks);
    }

    /// <summary>Reviewed donors all use DXT5 and have an exact full mip chain.
    /// Change RGB565 endpoints only: alpha blocks, pixel selectors, Phyre metadata,
    /// dimensions and file size remain byte-identical. Reject unproved layouts.</summary>
    public static byte[] TintDxt5MipChain(byte[] source, int payloadStart, int width, int height,
        string format, float red, float green, float blue, out int levels, out int blocks)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (format != "DXT5" || width < 1 || height < 1 || width > 8192 || height > 8192
            || payloadStart < 0 || payloadStart > source.Length
            || new[] { red, green, blue }.Any(c => !float.IsFinite(c) || c < 0 || c > 1))
            throw new InvalidDataException("Unsupported tint layout/palette.");
        int expected = 0, w = width, h = height;
        while (true)
        {
            expected = checked(expected + checked(((w + 3) / 4) * ((h + 3) / 4) * 16));
            if (w == 1 && h == 1) break;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        if (source.Length - payloadStart != expected)
            throw new InvalidDataException("Texture does not contain an exact full mip chain.");
        byte[] result = (byte[])source.Clone();
        levels = 0; blocks = 0;
        int position = payloadStart;
        w = width; h = height;
        while (true)
        {
            int count = ((w + 3) / 4) * ((h + 3) / 4);
            for (int block = 0; block < count; block++)
            {
                int color = position + block * 16 + 8;
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    int offset = color + endpoint * 2;
                    ushort old = (ushort)(source[offset] | source[offset + 1] << 8);
                    float light = Math.Max(((old >> 11) & 31) / 31f,
                        Math.Max(((old >> 5) & 63) / 63f, (old & 31) / 31f));
                    ushort tinted = (ushort)(((int)Math.Round(light * red * 31) << 11)
                        | ((int)Math.Round(light * green * 63) << 5) | (int)Math.Round(light * blue * 31));
                    result[offset] = (byte)tinted; result[offset + 1] = (byte)(tinted >> 8);
                }
            }
            blocks += count; levels++; position += count * 16;
            if (w == 1 && h == 1) break;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        return result;
    }
}
