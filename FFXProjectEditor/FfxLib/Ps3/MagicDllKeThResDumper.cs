using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Dump on-disk KeThRes / PPP regions from a Family D magic DLL for offline RE.
    /// Runtime decode target is 1 MiB at <c>0x100B6510</c> (host+2840) — PE file may be mostly zero/BSS.
    /// </summary>
    internal static class MagicDllKeThResDumper
    {
        public const int RuntimeBlobSize = 0x100000;
        public const uint KeThResBlobVa = 0x100B6510;
        public const uint KeThResHandleVa = 0x100B64D4;
        public const uint KeThResBlobAltVa = 0x101B6510;

        public sealed record DumpRegion(
            string Id,
            string Label,
            uint ImageVa,
            int FileOffset,
            int RequestedSize,
            int DumpedSize,
            string Sha256,
            int NonZeroBytes,
            string Note);

        public sealed record MagicDllKeThResDumpResult(
            int MagicId,
            string DllPath,
            string OutputDir,
            IReadOnlyList<DumpRegion> Regions,
            MagicDllKeThResParser.MagicDllKeThResParseResult Parse);

        public static MagicDllKeThResDumpResult Dump(
            MagicDllInspection inspection,
            string outputDir,
            string? ps3MagicRoot = null)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            Directory.CreateDirectory(outputDir);

            MagicDllKeThResParser.MagicDllKeThResParseResult parse =
                MagicDllKeThResParser.Parse(inspection, ps3MagicRoot, maxPhyreMatches: 8);

            var regions = new List<DumpRegion>();
            MagicDllKeThResParser.KeThResResourceSpec? primary = parse.PrimaryBoltResource;

            regions.Add(ExtractRegion(bytes, inspection, outputDir,
                "kethres_blob_100B6510",
                "Thundaga_KeThResBlob_0094 (host+2840 decode target)",
                KeThResBlobVa,
                RuntimeBlobSize,
                "On-disk slice; runtime fills full 1 MiB in-game."));

            regions.Add(ExtractRegion(bytes, inspection, outputDir,
                "kethres_handle_100B64D4",
                "Thundaga_KeThResHandle_0094 (spawn bind)",
                KeThResHandleVa,
                0x100,
                "Passed to sub_100037D0 / host+2856 bind."));

            regions.Add(ExtractRegion(bytes, inspection, outputDir,
                "kethres_blob_alt_101B6510",
                "Thundaga_KeThResBlobAlt_0094 (dword_100B5C2C)",
                KeThResBlobAltVa,
                0x400,
                "Alt blob pointer set in PppKeThResInit."));

            if (primary != null)
            {
                if (TryParseVa(primary.Entry.DataRvaAHex, out uint dataA))
                {
                    regions.Add(ExtractRegion(bytes, inspection, outputDir,
                        "ppp_dataA_primary",
                        $"pppKeThRes dataA ({primary.Name})",
                        dataA,
                        0x1000,
                        primary.Entry.StaticDataNote));
                }

                if (TryParseVa(primary.Entry.HandlerAHex, out uint handlerA))
                {
                    regions.Add(ExtractRegion(bytes, inspection, outputDir,
                        "ppp_handlerA",
                        "PPP handlerA (KeThRes opcode)",
                        handlerA,
                        0x200,
                        "Handler RVA from PPP catalog entry."));
                }
            }

            int catalogStart = FindPppCatalogStart(bytes);
            if (catalogStart >= 0)
            {
                regions.Add(ExtractRaw(bytes, outputDir,
                    "ppp_catalog_cluster",
                    "PPP opcode catalog (magic 0x55D4456F + KeThRes names)",
                    catalogStart,
                    0x400,
                    $"0x{KeThResBlobVa + MagicDllKeThResParser.ImageBase:X}",
                    "Static catalog cluster in PE."));
            }

            WriteManifest(outputDir, inspection, parse, regions);
            WriteMarkdown(outputDir, inspection, parse, regions);

            return new MagicDllKeThResDumpResult(
                inspection.MagicId ?? 0,
                inspection.FilePath,
                outputDir,
                regions,
                parse);
        }

        static DumpRegion ExtractRegion(
            byte[] bytes,
            MagicDllInspection inspection,
            string outputDir,
            string id,
            string label,
            uint imageVa,
            int requestedSize,
            string note)
        {
            int fileOffset;
            try
            {
                fileOffset = inspection.RvaToFileOffset((int)(imageVa - MagicDllKeThResParser.ImageBase));
            }
            catch
            {
                return WriteEmpty(outputDir, id, label, imageVa, requestedSize, note + " [RVA unmapped]");
            }

            int available = Math.Max(0, bytes.Length - fileOffset);
            int dumpSize = Math.Min(requestedSize, available);
            if (dumpSize <= 0)
                return WriteEmpty(outputDir, id, label, imageVa, requestedSize, note + " [no bytes on disk]");

            return ExtractRaw(bytes, outputDir, id, label, fileOffset, dumpSize, $"0x{imageVa:X}", note);
        }

        static DumpRegion ExtractRaw(
            byte[] bytes,
            string outputDir,
            string id,
            string label,
            int fileOffset,
            int dumpSize,
            string vaHex,
            string note)
        {
            dumpSize = Math.Min(dumpSize, Math.Max(0, bytes.Length - fileOffset));
            byte[] slice = bytes.AsSpan(fileOffset, dumpSize).ToArray();
            string path = Path.Combine(outputDir, $"{id}.bin");
            File.WriteAllBytes(path, slice);

            uint va = TryParseVa(vaHex, out uint v) ? v : 0;
            return new DumpRegion(
                id,
                label,
                va,
                fileOffset,
                dumpSize,
                dumpSize,
                Sha256Hex(slice),
                slice.Count(b => b != 0),
                note);
        }

        static DumpRegion WriteEmpty(
            string outputDir,
            string id,
            string label,
            uint imageVa,
            int requestedSize,
            string note)
        {
            string path = Path.Combine(outputDir, $"{id}.bin");
            File.WriteAllBytes(path, []);
            return new DumpRegion(id, label, imageVa, -1, requestedSize, 0, Sha256Hex([]), 0, note);
        }

        static int FindPppCatalogStart(byte[] bytes)
        {
            byte[] magic = BitConverter.GetBytes(MagicDllKeThResParser.PppEntryMagic);
            for (int i = 0; i <= bytes.Length - magic.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < magic.Length; j++)
                {
                    if (bytes[i + j] != magic[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                    continue;

                uint typeId = BitConverter.ToUInt32(bytes, i + 8);
                if (typeId == 0x0C)
                    return Math.Max(0, i - 0x1C);
            }

            return -1;
        }

        static void WriteManifest(
            string outputDir,
            MagicDllInspection inspection,
            MagicDllKeThResParser.MagicDllKeThResParseResult parse,
            IReadOnlyList<DumpRegion> regions)
        {
            var manifest = new
            {
                magicId = inspection.MagicId,
                dll = inspection.FilePath,
                dllSha256 = Sha256Hex(File.ReadAllBytes(inspection.FilePath)),
                dumpedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                runtimeNote = "Full 1MiB KeThRes blob requires in-game dump after host+2840; this is static PE slice.",
                regions = regions.Select(r => new
                {
                    r.Id,
                    r.Label,
                    imageVa = r.ImageVa > 0 ? $"0x{r.ImageVa:X}" : null,
                    fileOffset = r.FileOffset >= 0 ? $"0x{r.FileOffset:X}" : null,
                    r.RequestedSize,
                    r.DumpedSize,
                    r.NonZeroBytes,
                    r.Sha256,
                    r.Note,
                    file = $"{r.Id}.bin",
                }),
                keThRes = parse.KeThResResources.Select(k => k.Name),
                primary = parse.PrimaryBoltResource?.Name,
            };

            File.WriteAllText(
                Path.Combine(outputDir, "kethres_dump_manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        static void WriteMarkdown(
            string outputDir,
            MagicDllInspection inspection,
            MagicDllKeThResParser.MagicDllKeThResParseResult parse,
            IReadOnlyList<DumpRegion> regions)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# KeThRes static dump — `magic_{inspection.MagicId:D4}.dll`");
            sb.AppendLine();
            sb.AppendLine($"- **DLL:** `{inspection.FilePath}`");
            sb.AppendLine($"- **Runtime target:** `0x{KeThResBlobVa:X}` size `0x{RuntimeBlobSize:X}` (host+2840)");
            sb.AppendLine($"- **Primary resource:** `{parse.PrimaryBoltResource?.Name ?? "?"}`");
            sb.AppendLine();
            sb.AppendLine("## Regions");
            sb.AppendLine();
            sb.AppendLine("| File | VA | File off | Size | Non-zero | SHA-256 |");
            sb.AppendLine("|------|-----|----------|------|----------|---------|");
            foreach (DumpRegion r in regions)
            {
                string va = r.ImageVa > 0 ? $"0x{r.ImageVa:X}" : "-";
                string off = r.FileOffset >= 0 ? $"0x{r.FileOffset:X}" : "-";
                sb.AppendLine($"| `{r.Id}.bin` | {va} | {off} | {r.DumpedSize} | {r.NonZeroBytes} | `{r.Sha256[..16]}…` |");
            }

            sb.AppendLine();
            sb.AppendLine("## Hex preview (first 256 bytes each)");
            sb.AppendLine();
            foreach (DumpRegion r in regions)
            {
                string path = Path.Combine(outputDir, $"{r.Id}.bin");
                if (!File.Exists(path))
                    continue;
                byte[] slice = File.ReadAllBytes(path);
                if (slice.Length == 0)
                {
                    sb.AppendLine($"### `{r.Id}.bin` — empty ({r.Note})");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"### `{r.Id}.bin`");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.Append(HexPreview(slice, Math.Min(256, slice.Length)));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine($"_{r.Note}_");
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(outputDir, "KETHRES_DUMP.md"), sb.ToString(), Encoding.UTF8);
        }

        static string HexPreview(byte[] bytes, int len)
        {
            var sb = new StringBuilder();
            for (int row = 0; row < len; row += 16)
            {
                sb.Append($"0x{row:X4}: ");
                for (int col = 0; col < 16 && row + col < len; col++)
                    sb.Append($"{bytes[row + col]:X2} ");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        static bool TryParseVa(string text, out uint va)
        {
            va = 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            if (text == "-")
                return false;
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out va);
        }

        static string Sha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
