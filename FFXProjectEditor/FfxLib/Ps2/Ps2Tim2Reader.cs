using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal enum Ps2TexturePreviewState
    {
        Native,
        Experimental,
        MetadataOnly,
        Blocked
    }

    internal sealed class Ps2TextureProvenance
    {
        public Ps2TextureProvenance(string domain, string? bundleHint, IReadOnlyList<string> relations)
        {
            Domain = domain;
            BundleHint = bundleHint ?? "-";
            Relations = relations;
        }

        public string Domain { get; }
        public string BundleHint { get; }
        public IReadOnlyList<string> Relations { get; }
        public string RelationSummary => Relations.Count == 0 ? "-" : string.Join(" · ", Relations);
    }

    internal sealed class Ps2Tim2Header
    {
        public required uint U32_0x04 { get; init; }
        public required uint U32_0x0C { get; init; }
        public required uint BlockSizeLike_0x10 { get; init; }
        public required uint PaletteBytes_0x14 { get; init; }
        public required uint ImageBytes_0x18 { get; init; }
        public required ushort PictureHeaderSize_0x1C { get; init; }
        public required ushort ColorCount_0x1E { get; init; }
        public required byte Bppish_0x23 { get; init; }
        public required ushort Width_0x24 { get; init; }
        public required ushort Height_0x26 { get; init; }
    }

    internal sealed class Ps2Tim2Metadata
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required long FileSize { get; init; }
        public required bool FileSizeMatchesFormula { get; init; }
        public required string Sha256Prefix { get; init; }
        public required Ps2TexturePreviewState PreviewState { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string Variant { get; init; }
        public required string? BlockedReason { get; init; }
        public required Ps2TextureProvenance Provenance { get; init; }
        public required Ps2Tim2Header Header { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public string PreviewStateLabel => PreviewState switch
        {
            Ps2TexturePreviewState.Native => "Native Preview",
            Ps2TexturePreviewState.Experimental => "Experimental Preview",
            Ps2TexturePreviewState.MetadataOnly => "Metadata Only",
            _ => "Blocked"
        };
        public string DimensionSummary => $"{Header.Width_0x24}x{Header.Height_0x26}";
        public string PaletteSummary => $"{Header.PaletteBytes_0x14} bytes / {Header.ColorCount_0x1E} colors";
    }

    internal static class Ps2Tim2Reader
    {
        public static IReadOnlyList<Ps2Tim2Metadata> Scan(string ffxPs2Root)
        {
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return [];

            return Directory
                .EnumerateFiles(ffxPs2Root, "*.tm2", SearchOption.AllDirectories)
                .Select(path => TryReadMetadata(ffxPs2Root, path))
                .Where(metadata => metadata != null)
                .Cast<Ps2Tim2Metadata>()
                .OrderBy(metadata => metadata.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static Ps2Tim2Metadata? TryReadMetadata(string ffxPs2Root, string path)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }

            if (data.Length < 0x28 || data[0] != (byte)'T' || data[1] != (byte)'I' || data[2] != (byte)'M' || data[3] != (byte)'2')
                return null;

            Ps2Tim2Header header = new()
            {
                U32_0x04 = ReadU32(data, 0x04),
                U32_0x0C = ReadU32(data, 0x0C),
                BlockSizeLike_0x10 = ReadU32(data, 0x10),
                PaletteBytes_0x14 = ReadU32(data, 0x14),
                ImageBytes_0x18 = ReadU32(data, 0x18),
                PictureHeaderSize_0x1C = ReadU16(data, 0x1C),
                ColorCount_0x1E = ReadU16(data, 0x1E),
                Bppish_0x23 = data[0x23],
                Width_0x24 = ReadU16(data, 0x24),
                Height_0x26 = ReadU16(data, 0x26)
            };

            (Ps2TexturePreviewState previewState, string evidenceLabel, string variant, string? blockedReason) = Classify(header, data.LongLength);
            bool fileSizeMatchesFormula = data.LongLength == 0x40L + header.ImageBytes_0x18 + header.PaletteBytes_0x14;

            List<string> warnings =
            [
                "Preview only.",
                "No writer, repacker, or source mutation.",
                "Alpha/channel order is still not fully validated.",
                "Swizzle and CLUT order may still be wrong."
            ];

            if (!fileSizeMatchesFormula)
                warnings.Add("Layout formula mismatch: keep this as metadata or blocked, not final decode.");

            return new Ps2Tim2Metadata
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                RelativePath = Path.GetRelativePath(ffxPs2Root, path),
                FileSize = data.LongLength,
                FileSizeMatchesFormula = fileSizeMatchesFormula,
                Sha256Prefix = ComputeSha256Prefix(data),
                PreviewState = previewState,
                EvidenceLabel = evidenceLabel,
                Variant = variant,
                BlockedReason = blockedReason,
                Provenance = ClassifyProvenanceFromPath(path),
                Header = header,
                Warnings = warnings
            };
        }

        public static Bitmap? TryBuildPreviewBitmap(Ps2Tim2Metadata metadata)
        {
            if (metadata.PreviewState != Ps2TexturePreviewState.Native && metadata.PreviewState != Ps2TexturePreviewState.Experimental)
                return null;

            byte[] data = File.ReadAllBytes(metadata.FullPath);
            int width = metadata.Header.Width_0x24;
            int height = metadata.Header.Height_0x26;

            if (width <= 0 || height <= 0 || data.Length < 0x40)
                return null;

            byte[] pixelBuffer = new byte[width * height * 4];
            int imageOffset = 0x40;
            int clutOffset = imageOffset + checked((int)metadata.Header.ImageBytes_0x18);

            if (metadata.PreviewState == Ps2TexturePreviewState.Native)
            {
                if (data.Length < clutOffset + metadata.Header.PaletteBytes_0x14)
                    return null;

                for (int index = 0; index < width * height; index++)
                {
                    int paletteIndex = data[imageOffset + index];
                    WritePaletteColor(data, clutOffset, paletteIndex, pixelBuffer, index * 4);
                }
            }
            else
            {
                if (data.Length < clutOffset + metadata.Header.PaletteBytes_0x14)
                    return null;

                int pixelIndex = 0;
                for (int sourceIndex = 0; sourceIndex < metadata.Header.ImageBytes_0x18 && pixelIndex < width * height; sourceIndex++)
                {
                    byte packed = data[imageOffset + sourceIndex];
                    int low = packed & 0x0F;
                    int high = (packed >> 4) & 0x0F;

                    WritePaletteColor(data, clutOffset, low, pixelBuffer, pixelIndex * 4);
                    pixelIndex++;
                    if (pixelIndex >= width * height)
                        break;

                    WritePaletteColor(data, clutOffset, high, pixelBuffer, pixelIndex * 4);
                    pixelIndex++;
                }
            }

            WriteableBitmap bitmap = new(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using ILockedFramebuffer locked = bitmap.Lock();
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(
                    pixelBuffer,
                    row * width * 4,
                    IntPtr.Add(locked.Address, row * locked.RowBytes),
                    width * 4);
            }

            return bitmap;
        }

        static void WritePaletteColor(byte[] data, int clutOffset, int paletteIndex, byte[] pixelBuffer, int targetOffset)
        {
            int sourceOffset = clutOffset + (paletteIndex * 4);
            if (sourceOffset + 3 >= data.Length)
                return;

            // WHY (fixed 2026-09-14, FFX_CODEC_P1_WD_TM2_2026-09-14): the TIM2 CLUT entry order
            // is [R,G,B,A] — proven semantically on the full PS2 corpus (84/84: fire5 reads red,
            // sky_02/icetex read blue in the decoded PNGs). The previous [B,G,R,A] assumption
            // swapped R/B in the BGRA8888 preview. Target buffer is Avalonia Bgra8888.
            byte red = data[sourceOffset + 0];
            byte green = data[sourceOffset + 1];
            byte blue = data[sourceOffset + 2];
            byte alpha = NormalizePs2Alpha(data[sourceOffset + 3]);

            pixelBuffer[targetOffset + 0] = blue;
            pixelBuffer[targetOffset + 1] = green;
            pixelBuffer[targetOffset + 2] = red;
            pixelBuffer[targetOffset + 3] = alpha;
        }

        static byte NormalizePs2Alpha(byte raw)
        {
            if (raw == 0)
                return 0;

            int expanded = raw * 2;
            return (byte)Math.Min(255, expanded);
        }

        static (Ps2TexturePreviewState PreviewState, string EvidenceLabel, string Variant, string? BlockedReason) Classify(Ps2Tim2Header header, long fileSize)
        {
            bool matchesFormula = fileSize == 0x40L + header.ImageBytes_0x18 + header.PaletteBytes_0x14;

            if (header.PaletteBytes_0x14 == 1024
                && header.ColorCount_0x1E == 256
                && header.Bppish_0x23 == 5
                && header.ImageBytes_0x18 == (uint)(header.Width_0x24 * header.Height_0x26)
                && matchesFormula)
            {
                return (Ps2TexturePreviewState.Native, "proved", "indexed_8bpp_candidate", null);
            }

            if (header.PaletteBytes_0x14 == 64
                && header.ColorCount_0x1E == 16
                && header.Bppish_0x23 == 4
                && header.ImageBytes_0x18 * 2 == header.Width_0x24 * header.Height_0x26
                && matchesFormula)
            {
                return (Ps2TexturePreviewState.Experimental, "structural", "indexed_4bpp_candidate", null);
            }

            if (header.PaletteBytes_0x14 == 0
                && header.ColorCount_0x1E == 0
                && header.Bppish_0x23 == 1)
            {
                return (Ps2TexturePreviewState.Blocked, "structural", "direct_or_no_clut_candidate", "Direct-color path remains blocked.");
            }

            return (Ps2TexturePreviewState.MetadataOnly, "guess", "other_or_unknown", matchesFormula ? null : "Layout mismatch.");
        }

        internal static Ps2TextureProvenance ClassifyProvenanceFromPath(string path)
        {
            string[] parts = path
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Select(part => part.ToLowerInvariant())
                .ToArray();

            if (parts.Contains("master") && parts.Contains("menu"))
                return new Ps2TextureProvenance("ui_menu", "master", ["ui", "menu"]);

            if (parts.Contains("master") && (parts.Contains("help") || parts.Contains("help_inter")))
                return new Ps2TextureProvenance("help_support", "master", ["ui", "help"]);

            if (parts.Contains("dat_et") && parts.Contains("bat_eff"))
                return new Ps2TextureProvenance("battle_presentation", "bat_eff", ["battle", "effect"]);

            string? magicBundle = parts.FirstOrDefault(part => part.StartsWith("mag_", StringComparison.Ordinal));
            if (parts.Contains("dat_ov") && magicBundle != null)
                return new Ps2TextureProvenance("magic_effect", magicBundle, ["magic", "effect"]);

            if (parts.Contains("eiichi_abmap_data"))
                return new Ps2TextureProvenance("abmap_support", parts.Contains("maho") ? "maho" : null, ["abmap", "magic"]);

            if (parts.Contains("yonishi_data") && parts.Contains("dat"))
                return new Ps2TextureProvenance("palette_family", null, ["palette", "dat"]);

            if (parts.Contains("encount2"))
                return new Ps2TextureProvenance("encounter_effect", "encount2", ["battle", "encounter"]);

            if (parts.Contains("encount"))
                return new Ps2TextureProvenance("encounter_effect", "encount", ["battle", "encounter"]);

            return new Ps2TextureProvenance("unknown", null, []);
        }

        static uint ReadU32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
        static ushort ReadU16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);

        static string ComputeSha256Prefix(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            return string.Concat(hash.Take(8).Select(b => b.ToString("X2")));
        }
    }
}
