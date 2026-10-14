using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3
{
    // READ-ONLY viewer for the HD remaster magic-effect textures under ps3data\magic.
    //
    // Proved container model (see docs/history/PS3DATA_DDS_PHYRE_DECODE_2026-06-01.md):
    //   file = [ PhyreEngine RYHPT header ] + [ texture buffer: mip0, mip1, ... (largest first) ]
    //   The real PTexture2D instance is the "PTexture2D" occurrence whose following ASCII token
    //   is an actual pixel format (ARGB8 / DXT1 / DXT3 / DXT5 / L8).
    //     width  = U32@(pidx-88), height = U32@(pidx-84)            [self-validated across the sweep]
    //     bufferStart = pidx + 11 + formatToken.Length + 38
    //   mip0 occupies the first mip0Size bytes of the buffer; that is what we render.
    //
    // ARGB8 reconstructs byte-exact vs the shipped .dds [proved]; DXT1/3/5 + L8 decoders are the
    // same ones used to mass-export 40k PNGs and confirmed visually across the ps3data sweep.
    // No writer, no repacker, no source mutation. Unknown classes degrade to metadata-only.

    internal enum Ps3TexturePreviewState
    {
        Native,
        MetadataOnly,
        Blocked
    }

    internal sealed class Ps3MagicTexture
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required long FileSize { get; init; }
        public required int NameWidth { get; init; }
        public required int NameHeight { get; init; }

        public string NameDimSummary => NameWidth > 0 && NameHeight > 0 ? $"{NameWidth}x{NameHeight}" : "?";
        public string SizeSummary => $"{FileSize:N0} bytes";
    }

    internal sealed class Ps3MagicEntry
    {
        public required string Id { get; init; }
        public required string FolderName { get; init; }
        public required string FullPath { get; init; }
        public required IReadOnlyList<Ps3MagicTexture> Textures { get; init; }
        /// <summary>Which scanned root supplied this folder (extract vs mods overlay).</summary>
        public string SourceRootLabel { get; init; } = "ps3data";

        public int TextureCount => Textures.Count;
        public string CountSummary => $"{TextureCount} tex";

        public string DisplayTitle => FolderName;

        public string DisplaySubtitle
        {
            get
            {
                string name = BestEffortNameDisplay;
                string baseLine = string.Equals(name, FolderName, StringComparison.OrdinalIgnoreCase)
                    ? $"{TextureCount} textures"
                    : $"{name} · {TextureCount} textures";
                return string.Equals(SourceRootLabel, "ps3data", StringComparison.OrdinalIgnoreCase)
                    ? baseLine
                    : $"{baseLine} · {SourceRootLabel}";
            }
        }

        public string BestEffortNameDisplay
        {
            get
            {
                int n = 0;
                bool any = false;
                foreach (char ch in Id)
                {
                    if (ch is >= '0' and <= '9')
                    {
                        n = n * 10 + (ch - '0');
                        any = true;
                    }
                }

                return any ? FFXProjectEditor.FfxLib.Dictionaries.MagicSpellNameResolver.DisplayName(n) : FolderName;
            }
        }
    }

    internal sealed class Ps3PhyreDecode
    {
        public required bool Ok { get; init; }
        public required string Format { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int BufferStart { get; init; }
        public required Ps3TexturePreviewState State { get; init; }
        public required string Note { get; init; }
        public byte[]? Bgra { get; init; }
        public Bitmap? Bitmap { get; init; }

        public string StateLabel => State switch
        {
            Ps3TexturePreviewState.Native => "Native Preview",
            Ps3TexturePreviewState.MetadataOnly => "Metadata Only",
            _ => "Blocked"
        };
        public string DimensionSummary => Width > 0 && Height > 0 ? $"{Width}x{Height}" : "-";
    }

    internal static class Ps3MagicTextureReader
    {
        /// <summary>
        /// Enumerate the magic_#### folders under &lt;ps3DataRoot&gt;\magic. File names only; no byte reads here.
        /// </summary>
        public static IReadOnlyList<Ps3MagicEntry> ScanMagicRoot(string? ps3DataRoot)
        {
            if (string.IsNullOrWhiteSpace(ps3DataRoot))
                return [];

            string magicRoot = Path.Combine(ps3DataRoot, "magic");
            return ScanMagicFolderRoot(magicRoot, "ps3data");
        }

        /// <summary>
        /// Scan every supplied magic root on disk and merge by <c>magic_####</c> folder name.
        /// Later roots win (mods overlay overrides extracted ps3data). No static index.
        /// </summary>
        public static IReadOnlyList<Ps3MagicEntry> ScanAllMagicFolderRoots(IEnumerable<(string MagicRoot, string SourceLabel)> roots)
        {
            var merged = new Dictionary<string, Ps3MagicEntry>(StringComparer.OrdinalIgnoreCase);
            foreach ((string magicRoot, string sourceLabel) in roots)
            {
                if (string.IsNullOrWhiteSpace(magicRoot) || !Directory.Exists(magicRoot))
                    continue;

                foreach (Ps3MagicEntry entry in ScanMagicFolderRoot(magicRoot, sourceLabel))
                    merged[entry.FolderName] = entry;
            }

            return merged.Values
                .OrderBy(entry => ParseMagicIdSortKey(entry.Id))
                .ThenBy(entry => entry.FolderName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static int ParseMagicIdSortKey(string id) =>
            int.TryParse(id, out int value) ? value : int.MaxValue;

        static IReadOnlyList<Ps3MagicEntry> ScanMagicFolderRoot(string magicRoot, string sourceRootLabel)
        {
            if (!Directory.Exists(magicRoot))
                return [];

            List<Ps3MagicEntry> entries = [];
            foreach (string dir in Directory.EnumerateDirectories(magicRoot).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = Path.GetFileName(dir);
                if (!folderName.StartsWith("magic_", StringComparison.OrdinalIgnoreCase))
                    continue;

                Ps3MagicEntry? entry = TryBuildEntry(dir, magicRoot, folderName, sourceRootLabel);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }

        static Ps3MagicEntry? TryBuildEntry(string dir, string magicRoot, string folderName, string sourceRootLabel)
        {
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.dds.phyre", SearchOption.AllDirectories)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return null;
            }

            if (files.Count == 0)
                return null;

            List<Ps3MagicTexture> textures = [];
            foreach (string file in files)
            {
                long size = 0;
                try { size = new FileInfo(file).Length; }
                catch { /* keep metadata best-effort */ }

                (int width, int height) = ParseNameDims(Path.GetFileName(file));
                textures.Add(new Ps3MagicTexture
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    RelativePath = Path.GetRelativePath(magicRoot, file),
                    FileSize = size,
                    NameWidth = width,
                    NameHeight = height
                });
            }

            string id = folderName["magic_".Length..];
            return new Ps3MagicEntry
            {
                Id = id,
                FolderName = folderName,
                FullPath = dir,
                Textures = textures,
                SourceRootLabel = sourceRootLabel
            };
        }

        // <id>_<fmt>_0_0_W_H[_suffix].dds.phyre  ->  (W, H). 0,0 when the name is non-standard.
        static (int Width, int Height) ParseNameDims(string fileName)
        {
            string core = fileName;
            int marker = core.IndexOf(".dds.phyre", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
                core = core[..marker];

            string[] parts = core.Split('_');
            if (parts.Length >= 6
                && int.TryParse(parts[4], out int width)
                && int.TryParse(parts[5], out int height)
                && width is > 0 and <= 8192
                && height is > 0 and <= 8192)
            {
                return (width, height);
            }

            return (0, 0);
        }

        /// <summary>
        /// Decode mip0 of a .dds.phyre to a BGRA Avalonia bitmap. Never throws; degrades to a
        /// metadata-only / blocked result with an honest note.
        /// </summary>
        public static Ps3PhyreDecode Decode(string path)
        {
            byte[] buffer;
            try
            {
                buffer = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                return Blocked($"Read failed: {ex.Message}");
            }

            if (buffer.Length < 96
                || buffer[0] != (byte)'R' || buffer[1] != (byte)'Y' || buffer[2] != (byte)'H'
                || buffer[3] != (byte)'P' || buffer[4] != (byte)'T')
            {
                return Blocked("Not a RYHPT/.phyre container.");
            }

            int scanLimit = Math.Min(buffer.Length, 16384);
            int pidx = -1;
            string format = string.Empty;
            int from = 0;
            while (true)
            {
                int hit = FindAscii(buffer, "PTexture2D", from, scanLimit);
                if (hit < 0)
                    break;

                string token = ReadToken(buffer, hit + 10);
                if (IsKnownFormat(token))
                {
                    pidx = hit;
                    format = token;
                    break;
                }

                from = hit + 1;
            }

            if (pidx < 0)
                return MetadataOnly("No PTexture2D instance with a known pixel format (atlas/other class).", "?", 0, 0, 0);

            int width = (int)ReadU32(buffer, pidx - 88);
            int height = (int)ReadU32(buffer, pidx - 84);
            if (width is < 1 or > 8192 || height is < 1 or > 8192)
                return MetadataOnly($"Header dims implausible ({width}x{height}).", format, 0, 0, 0);

            int bufferStart = pidx + 11 + format.Length + 38;
            int mip0 = Mip0Size(width, height, format);
            if (bufferStart < 0 || mip0 <= 0 || (long)bufferStart + mip0 > buffer.Length)
                return MetadataOnly($"mip0 region out of range (W={width} H={height} fmt={format}).", format, width, height, bufferStart);

            byte[]? bgra;
            try
            {
                bgra = format switch
                {
                    "ARGB8" => CopyArgb8(buffer, bufferStart, width, height),
                    "DXT1" => DecodeDxt1(buffer, bufferStart, width, height),
                    "DXT3" => DecodeDxt3(buffer, bufferStart, width, height),
                    "DXT5" => DecodeDxt5(buffer, bufferStart, width, height),
                    "L8" => DecodeL8(buffer, bufferStart, width, height),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                return MetadataOnly($"Decode error: {ex.Message}", format, width, height, bufferStart);
            }

            if (bgra == null)
                return MetadataOnly($"Unsupported format token '{format}'.", format, width, height, bufferStart);

            Bitmap bitmap = BuildBitmapFromBgra(bgra, width, height);
            return new Ps3PhyreDecode
            {
                Ok = true,
                Format = format,
                Width = width,
                Height = height,
                BufferStart = bufferStart,
                State = Ps3TexturePreviewState.Native,
                Note = "Native preview: mip0 decoded from the proved RYHPT buffer (single surface).",
                Bgra = bgra,
                Bitmap = bitmap
            };
        }

        public static Bitmap BuildBitmapFromBgra(byte[] bgra, int width, int height)
        {
            WriteableBitmap bitmap = new(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using ILockedFramebuffer locked = bitmap.Lock();
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(
                    bgra,
                    row * width * 4,
                    IntPtr.Add(locked.Address, row * locked.RowBytes),
                    width * 4);
            }

            return bitmap;
        }

        static int Mip0Size(int width, int height, string format) => format switch
        {
            "ARGB8" => width * height * 4,
            "DXT1" => ((width + 3) / 4) * ((height + 3) / 4) * 8,
            "DXT3" or "DXT5" => ((width + 3) / 4) * ((height + 3) / 4) * 16,
            "L8" => width * height,
            _ => 0
        };

        static byte[] CopyArgb8(byte[] data, int offset, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            int count = Math.Min(output.Length, data.Length - offset);
            if (count > 0)
                Array.Copy(data, offset, output, 0, count);
            return output;
        }

        // --- BC color block helper (ported verbatim from the proved Export-Ps3Textures TexBC class) ---
        static void DecodeColorBlock(ushort c0, ushort c1, int[] r, int[] g, int[] b, bool dxt1)
        {
            r[0] = ((c0 >> 11) & 31) * 255 / 31; g[0] = ((c0 >> 5) & 63) * 255 / 63; b[0] = (c0 & 31) * 255 / 31;
            r[1] = ((c1 >> 11) & 31) * 255 / 31; g[1] = ((c1 >> 5) & 63) * 255 / 63; b[1] = (c1 & 31) * 255 / 31;
            if (!dxt1 || c0 > c1)
            {
                r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; b[2] = (2 * b[0] + b[1]) / 3;
                r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; b[3] = (b[0] + 2 * b[1]) / 3;
            }
            else
            {
                r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; b[2] = (b[0] + b[1]) / 2;
                r[3] = g[3] = b[3] = 0;
            }
        }

        static byte[] DecodeDxt5(byte[] d, int off, int w, int h)
        {
            byte[] o = new byte[w * h * 4];
            int bx = (w + 3) / 4, by = (h + 3) / 4, p = off;
            int[] r = new int[4], g = new int[4], b = new int[4];
            for (int Y = 0; Y < by; Y++)
                for (int X = 0; X < bx; X++)
                {
                    byte a0 = d[p], a1 = d[p + 1];
                    ulong ab = 0;
                    for (int i = 0; i < 6; i++) ab |= (ulong)d[p + 2 + i] << (8 * i);
                    byte[] al = new byte[8]; al[0] = a0; al[1] = a1;
                    if (a0 > a1) { for (int i = 1; i < 7; i++) al[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7); }
                    else { for (int i = 1; i < 5; i++) al[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5); al[6] = 0; al[7] = 255; }
                    DecodeColorBlock((ushort)(d[p + 8] | (d[p + 9] << 8)), (ushort)(d[p + 10] | (d[p + 11] << 8)), r, g, b, false);
                    uint cb = (uint)(d[p + 12] | (d[p + 13] << 8) | (d[p + 14] << 16) | (d[p + 15] << 24));
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = X * 4 + px, y = Y * 4 + py;
                            if (x >= w || y >= h) continue;
                            int ci = (int)((cb >> (2 * (py * 4 + px))) & 3);
                            int ai = (int)((ab >> (3 * (py * 4 + px))) & 7);
                            int q = (y * w + x) * 4;
                            o[q] = (byte)b[ci]; o[q + 1] = (byte)g[ci]; o[q + 2] = (byte)r[ci]; o[q + 3] = al[ai];
                        }
                    p += 16;
                }
            return o;
        }

        static byte[] DecodeDxt3(byte[] d, int off, int w, int h)
        {
            byte[] o = new byte[w * h * 4];
            int bx = (w + 3) / 4, by = (h + 3) / 4, p = off;
            int[] r = new int[4], g = new int[4], b = new int[4];
            for (int Y = 0; Y < by; Y++)
                for (int X = 0; X < bx; X++)
                {
                    ulong ab = 0;
                    for (int i = 0; i < 8; i++) ab |= (ulong)d[p + i] << (8 * i);
                    DecodeColorBlock((ushort)(d[p + 8] | (d[p + 9] << 8)), (ushort)(d[p + 10] | (d[p + 11] << 8)), r, g, b, false);
                    uint cb = (uint)(d[p + 12] | (d[p + 13] << 8) | (d[p + 14] << 16) | (d[p + 15] << 24));
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = X * 4 + px, y = Y * 4 + py;
                            if (x >= w || y >= h) continue;
                            int ci = (int)((cb >> (2 * (py * 4 + px))) & 3);
                            int an = (int)((ab >> (4 * (py * 4 + px))) & 0xF);
                            int q = (y * w + x) * 4;
                            o[q] = (byte)b[ci]; o[q + 1] = (byte)g[ci]; o[q + 2] = (byte)r[ci]; o[q + 3] = (byte)(an * 255 / 15);
                        }
                    p += 16;
                }
            return o;
        }

        static byte[] DecodeDxt1(byte[] d, int off, int w, int h)
        {
            byte[] o = new byte[w * h * 4];
            int bx = (w + 3) / 4, by = (h + 3) / 4, p = off;
            int[] r = new int[4], g = new int[4], b = new int[4];
            for (int Y = 0; Y < by; Y++)
                for (int X = 0; X < bx; X++)
                {
                    ushort c0 = (ushort)(d[p] | (d[p + 1] << 8)), c1 = (ushort)(d[p + 2] | (d[p + 3] << 8));
                    DecodeColorBlock(c0, c1, r, g, b, true);
                    uint cb = (uint)(d[p + 4] | (d[p + 5] << 8) | (d[p + 6] << 16) | (d[p + 7] << 24));
                    bool punchThrough = c0 <= c1;
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = X * 4 + px, y = Y * 4 + py;
                            if (x >= w || y >= h) continue;
                            int ci = (int)((cb >> (2 * (py * 4 + px))) & 3);
                            int q = (y * w + x) * 4;
                            o[q] = (byte)b[ci]; o[q + 1] = (byte)g[ci]; o[q + 2] = (byte)r[ci];
                            o[q + 3] = (byte)((punchThrough && ci == 3) ? 0 : 255);
                        }
                    p += 8;
                }
            return o;
        }

        static byte[] DecodeL8(byte[] d, int off, int w, int h)
        {
            byte[] o = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                byte v = d[off + i];
                o[i * 4] = v; o[i * 4 + 1] = v; o[i * 4 + 2] = v; o[i * 4 + 3] = 255;
            }
            return o;
        }

        static bool IsKnownFormat(string token) =>
            token is "ARGB8" or "DXT1" or "DXT3" or "DXT5" or "L8";

        static int FindAscii(byte[] buffer, string needle, int start, int limit)
        {
            byte[] needleBytes = Encoding.ASCII.GetBytes(needle);
            int end = Math.Min(buffer.Length - needleBytes.Length, limit);
            for (int i = Math.Max(0, start); i <= end; i++)
            {
                bool match = true;
                for (int k = 0; k < needleBytes.Length; k++)
                {
                    if (buffer[i + k] != needleBytes[k]) { match = false; break; }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        static string ReadToken(byte[] buffer, int start)
        {
            int o = start;
            while (o < buffer.Length && buffer[o] == 0) o++;
            StringBuilder sb = new();
            while (o < buffer.Length && buffer[o] >= 32 && buffer[o] < 127)
            {
                sb.Append((char)buffer[o]);
                o++;
            }
            return sb.ToString();
        }

        static uint ReadU32(byte[] buffer, int offset) =>
            offset < 0 || offset + 4 > buffer.Length ? 0u : BitConverter.ToUInt32(buffer, offset);

        static Ps3PhyreDecode Blocked(string note) => new()
        {
            Ok = false,
            Format = "?",
            Width = 0,
            Height = 0,
            BufferStart = 0,
            State = Ps3TexturePreviewState.Blocked,
            Note = note,
            Bitmap = null
        };

        static Ps3PhyreDecode MetadataOnly(string note, string format, int width, int height, int bufferStart) => new()
        {
            Ok = false,
            Format = format,
            Width = width,
            Height = height,
            BufferStart = bufferStart,
            State = Ps3TexturePreviewState.MetadataOnly,
            Note = note,
            Bitmap = null
        };
    }
}
