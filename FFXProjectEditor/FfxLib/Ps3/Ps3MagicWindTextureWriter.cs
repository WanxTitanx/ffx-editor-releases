using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ps3;

/// <summary>
/// Wind authoring for the reviewed PC DXT5 textures: preserves the Phyre header and
/// every mip's dimensions. Rejects any trailing data not accounted for by a complete
/// DXT5 mip chain. Unlike a mip0-only tint, distant mips cannot retain orange flames.
/// </summary>
internal static class Ps3MagicWindTextureWriter
{
    internal static void CustomizeClone(string cloneFolder,string neutralFolder,double red,double green,double blue,double alpha)
    {
        // Targets are dedicated copies. Neutral replacements always
        // come from the original folder so traversal order cannot double-tint them.
        var replacements=new Dictionary<string,string> {
            ["14112_19_0_0_128_256.dds.phyre"]="13312_19_0_0_128_256.dds.phyre",
            ["14240_19_0_0_512_256.dds.phyre"]="15040_19_0_0_256_256.dds.phyre",
            ["14752_19_0_0_256_256.dds.phyre"]="15040_19_0_0_256_256.dds.phyre"
        };
        foreach (string target in Directory.GetFiles(Path.Combine(cloneFolder,"tex","d3d11"),"*.dds.phyre"))
        {
            string? neutral=replacements.TryGetValue(Path.GetFileName(target),out string? name)
                ? Path.Combine(neutralFolder,"tex","d3d11",name) : null;
            Write(target,neutral,red,green,blue,alpha);
        }
    }

    internal static int[] LevelSizes(int width, int height, long payloadLength)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192 ||
            (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            throw new InvalidDataException("Wind texture dimensions must be bounded powers of two.");
        var sizes = new List<int>();
        do
        {
            sizes.Add(checked(Math.Max(1, width / 4) * Math.Max(1, height / 4) * 16));
            if (width == 1 && height == 1) break;
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        } while (true);
        long sum = 0; foreach (int size in sizes) sum += size;
        if (sum != payloadLength) throw new InvalidDataException("Texture tail is not exactly one complete DXT5 mip chain.");
        return sizes.ToArray();
    }

    internal static void Write(string target, string? neutralSource, double red, double green, double blue, double alpha)
    {
        foreach (double factor in new[] { red, green, blue, alpha })
            if (!double.IsFinite(factor) || factor is <= 0 or > 2)
                throw new ArgumentOutOfRangeException(nameof(red));
        if (alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        var (bytes, layout, levels) = Read(target);
        byte[] output = (byte[])bytes.Clone();
        if (neutralSource != null)
        {
            var (neutral, donor, donorLevels) = Read(neutralSource);
            if (layout.Height != donor.Height || (layout.Width != donor.Width && layout.Width != donor.Width * 2))
                throw new InvalidDataException("Neutral atlas must have the same size or exactly half the target width.");
            int dst = layout.BufferStart, src = donor.BufferStart, w = layout.Width, h = layout.Height,
                donorWidth = donor.Width;
            for (int level = 0; level < levels.Length; level++)
            {
                int donorLevel = Math.Min(level, donorLevels.Length - 1);
                int columns = Math.Max(1, w / 4), rows = Math.Max(1, h / 4), donorColumns = Math.Max(1, donorWidth / 4);
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                        Array.Copy(neutral, src + (y * donorColumns + x % donorColumns) * 16,
                            output, dst + (y * columns + x) * 16, 16);
                dst += levels[level];
                if (level < donorLevels.Length - 1) src += donorLevels[donorLevel];
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                donorWidth = Math.Max(1, donorWidth / 2);
            }
        }
        for (int block = layout.BufferStart; block < output.Length; block += 16)
        {
            byte a = output[block], b = output[block + 1];
            output[block] = Scale(a, alpha); output[block + 1] = Scale(b, alpha);
            // DXT5 switches interpolation mode at a0 <= a1; preserve that relation.
            if (a > b && output[block] <= output[block + 1])
                output[block] = (byte)Math.Min(255, output[block + 1] + 1);
            for (int endpoint = block + 8; endpoint <= block + 10; endpoint += 2)
            {
                ushort rgb = BitConverter.ToUInt16(output, endpoint);
                double luminance = .2126 * ((rgb >> 11) & 31) / 31 + .7152 * ((rgb >> 5) & 63) / 63 + .0722 * (rgb & 31) / 31;
                int r = (int)Math.Round(Math.Clamp(luminance * red, 0, 1) * 31);
                int g = (int)Math.Round(Math.Clamp(luminance * green, 0, 1) * 63);
                int bl = (int)Math.Round(Math.Clamp(luminance * blue, 0, 1) * 31);
                ushort color = (ushort)((r << 11) | (g << 5) | bl);
                output[endpoint] = (byte)color; output[endpoint + 1] = (byte)(color >> 8);
            }
        }
        if (!bytes.AsSpan(0, layout.BufferStart).SequenceEqual(output.AsSpan(0, layout.BufferStart)))
            throw new InvalidDataException("Texture header changed.");
        File.WriteAllBytes(target, output);
        if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(output)) throw new IOException("Texture readback differs.");
    }

    private static (byte[], Ps3PhyreMip0Layout, int[]) Read(string path)
    {
        if (!Ps3MagicTextureWriter.TryReadMip0Layout(path, out var layout, out string note) || layout == null || layout.Format != "DXT5")
            throw new InvalidDataException("Wind requires a reviewed DXT5 texture: " + note);
        byte[] bytes = File.ReadAllBytes(path);
        return (bytes, layout, LevelSizes(layout.Width, layout.Height, bytes.Length - layout.BufferStart));
    }

    private static byte Scale(byte value, double factor) => (byte)Math.Clamp((int)Math.Round(value * factor), 0, 255);
}
