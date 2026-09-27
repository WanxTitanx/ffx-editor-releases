using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal sealed class Ps2BinFtcBucketSummary
    {
        public required string Bucket { get; init; }
        public required int Count { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string SensitivityLabel { get; init; }
    }

    internal sealed class Ps2BinFtcRecord
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Root { get; init; }
        public required string ClassName { get; init; }
        public required string Extension { get; init; }
        public required long Size { get; init; }
        public required string Bucket { get; init; }
        public required string SignatureGroup { get; init; }
        public required string First16Hex { get; init; }
        public required IReadOnlyList<uint> First64Dwords { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string SensitivityLabel { get; init; }
        public required string WriteStatus { get; init; }
        public required string BlockedReason { get; init; }
        public required string FtcMagic { get; init; }
        public required string? PairedSidecarPath { get; init; }
        public string SizeSummary => $"{Size:N0} bytes";
        public string DwordSummary => First64Dwords.Count == 0
            ? "-"
            : string.Join(Environment.NewLine, First64Dwords.Select((value, index) => $"0x{index * 4:X2}: {value}"));
        public string SidecarSummary => string.IsNullOrWhiteSpace(PairedSidecarPath) ? "-" : PairedSidecarPath!;
        public string SensitivityBanner => $"{SensitivityLabel} · {BlockedReason}";
    }

    internal sealed class Ps2BinFtcAtlasSnapshot
    {
        public required string MasterRoot { get; init; }
        public required IReadOnlyList<Ps2BinFtcRecord> Records { get; init; }
        public required IReadOnlyList<Ps2BinFtcBucketSummary> BucketSummaries { get; init; }
        public required int BinCount { get; init; }
        public required int FtcCount { get; init; }
        public required int FtcxCount { get; init; }
        public required int NonFtcxCount { get; init; }
        public required int PairedCount { get; init; }
    }

    internal static class Ps2BinFtcAtlas
    {
        public static Ps2BinFtcAtlasSnapshot Scan(string masterRoot)
        {
            if (string.IsNullOrWhiteSpace(masterRoot) || !Directory.Exists(masterRoot))
            {
                return new Ps2BinFtcAtlasSnapshot
                {
                    MasterRoot = masterRoot ?? "-",
                    Records = [],
                    BucketSummaries = [],
                    BinCount = 0,
                    FtcCount = 0,
                    FtcxCount = 0,
                    NonFtcxCount = 0,
                    PairedCount = 0
                };
            }

            List<string> files = Directory
                .EnumerateFiles(masterRoot, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string ext = Path.GetExtension(path);
                    return string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".ftc", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            HashSet<string> allRelativePaths = files
                .Select(path => Path.GetRelativePath(masterRoot, path).Replace(Path.DirectorySeparatorChar, '\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<Ps2BinFtcRecord> records = [];
            foreach (string path in files)
            {
                byte[] head = ReadHead(path, 64);
                string relativePath = Path.GetRelativePath(masterRoot, path).Replace(Path.DirectorySeparatorChar, '\\');
                string[] parts = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                string ext = Path.GetExtension(path).ToLowerInvariant();

                string bucket = ext == ".bin"
                    ? ClassifyBin(relativePath, head)
                    : (head.Length >= 4 && head[0] == (byte)'F' && head[1] == (byte)'T' && head[2] == (byte)'C' && head[3] == (byte)'X'
                        ? "ftcx"
                        : "ftc_non_ftcx");

                string? pairedSidecar = FindPairedSidecar(relativePath, ext, allRelativePaths);
                (string evidenceLabel, string sensitivityLabel, string blockedReason) = ClassifyLabels(relativePath, ext, bucket);
                string ftcMagic = ext == ".ftc"
                    ? (bucket == "ftcx" ? "FTCX" : "non-FTCX")
                    : (pairedSidecar != null && pairedSidecar.EndsWith(".ftc", StringComparison.OrdinalIgnoreCase) ? "paired-ftc" : "-");

                IReadOnlyList<uint> dwords = DecodeU32Words(head);

                records.Add(new Ps2BinFtcRecord
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    RelativePath = relativePath,
                    Root = parts.Length > 0 ? parts[0] : "-",
                    ClassName = parts.Length > 1 ? parts[1] : "-",
                    Extension = ext,
                    Size = new FileInfo(path).Length,
                    Bucket = bucket,
                    SignatureGroup = $"{ext}:{bucket}:{NormalizeSignature(head)}",
                    First16Hex = BitConverter.ToString(head.Take(16).ToArray()).Replace('-', ' '),
                    First64Dwords = dwords,
                    EvidenceLabel = evidenceLabel,
                    SensitivityLabel = sensitivityLabel,
                    WriteStatus = "Read-only only",
                    BlockedReason = blockedReason,
                    FtcMagic = ftcMagic,
                    PairedSidecarPath = pairedSidecar
                });
            }

            IReadOnlyList<Ps2BinFtcBucketSummary> buckets = records
                .GroupBy(record => record.Bucket, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    Ps2BinFtcRecord sample = group.First();
                    return new Ps2BinFtcBucketSummary
                    {
                        Bucket = group.Key,
                        Count = group.Count(),
                        EvidenceLabel = sample.EvidenceLabel,
                        SensitivityLabel = sample.SensitivityLabel
                    };
                })
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Bucket, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Ps2BinFtcAtlasSnapshot
            {
                MasterRoot = masterRoot,
                Records = records.OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
                BucketSummaries = buckets,
                BinCount = records.Count(record => record.Extension == ".bin"),
                FtcCount = records.Count(record => record.Extension == ".ftc"),
                FtcxCount = records.Count(record => record.Bucket == "ftcx"),
                NonFtcxCount = records.Count(record => record.Bucket == "ftc_non_ftcx"),
                PairedCount = records.Count(record => !string.IsNullOrWhiteSpace(record.PairedSidecarPath))
            };
        }

        static byte[] ReadHead(string path, int size)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[size];
            int bytesRead = stream.Read(buffer, 0, size);
            return bytesRead == size ? buffer : buffer[..bytesRead];
        }

        static IReadOnlyList<uint> DecodeU32Words(byte[] head)
        {
            int usable = head.Length / 4;
            List<uint> values = new(usable);
            for (int index = 0; index < usable; index++)
                values.Add(BitConverter.ToUInt32(head, index * 4));

            return values;
        }

        static string NormalizeSignature(byte[] head)
        {
            if (head.Length == 0)
                return "empty";

            return BitConverter.ToString(head.Take(8).ToArray()).Replace("-", string.Empty);
        }

        static string? FindPairedSidecar(string relativePath, string ext, HashSet<string> allRelativePaths)
        {
            string candidateExt = ext == ".bin" ? ".ftc" : ".bin";
            string withoutExt = Path.ChangeExtension(relativePath, null) ?? relativePath;
            string candidate = withoutExt + candidateExt;
            return allRelativePaths.Contains(candidate)
                ? candidate
                : null;
        }

        static string ClassifyBin(string relativePath, byte[] head)
        {
            IReadOnlyList<uint> dwords = DecodeU32Words(head);
            string[] parts = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string root = parts.Length > 0 ? parts[0] : string.Empty;
            string className = parts.Length > 1 ? parts[1] : string.Empty;

            if (dwords.Count >= 8 && Enumerable.Range(0, 4).All(index => dwords[index * 2] == dwords[(index * 2) + 1]))
                return "paired_u32_0_7";

            if (dwords.Count >= 2 && dwords[0] == 8 && dwords[1] == 0x30 && string.Equals(root, "jppc", StringComparison.OrdinalIgnoreCase) && string.Equals(className, "battle", StringComparison.OrdinalIgnoreCase))
                return "jppc_battle_08_30";

            if (dwords.Count >= 2 && dwords[0] == 1 && dwords[1] == 0 && string.Equals(className, "battle", StringComparison.OrdinalIgnoreCase))
                return "battle_1_0";

            if (dwords.Count >= 2 && (dwords[0] == 0x100 || dwords[0] == 0x200) && dwords[0] == dwords[1] && className.StartsWith("help", StringComparison.OrdinalIgnoreCase))
                return "help_0100_0200";

            if (head.Length > 0 && head.All(value => value == 0))
                return "zero_head";

            return "other";
        }

        static (string EvidenceLabel, string SensitivityLabel, string BlockedReason) ClassifyLabels(string relativePath, string ext, string bucket)
        {
            string lower = relativePath.ToLowerInvariant();

            if (ext == ".ftc")
            {
                return bucket == "ftcx"
                    ? ("proved", "Dangerous", "Sensitive FTC header lane. Write blocked.")
                    : ("proved", "Dangerous", "Non-FTCX outlier lane. Write blocked.");
            }

            if (lower.Contains("\\battle\\kernel\\"))
                return ("structural", "Dangerous", "Battle kernel zone. Structural only, no parser-final claim.");

            if (lower.Contains("\\battle\\btl\\"))
                return ("proved", "Dangerous", "Battle table zone. Read-only atlas only.");

            if (lower.Contains("\\event\\obj"))
                return ("structural", "Sensitive", "Event object zone. Sidecar evidence only.");

            if (lower.Contains("\\menu\\"))
                return ("structural", "Sensitive", "Menu/script zone. Do not promote to parser or writer.");

            if (lower.Contains("\\help\\") || lower.Contains("\\help_inter\\"))
                return ("structural", "Sensitive", "Help/UI carrier zone. Metadata only.");

            if (lower.Contains("\\cloudsave\\") || lower.Contains("\\ffx_encoding\\"))
                return ("structural", "Sensitive", "Support/system zone. Navigation only.");

            if (bucket == "zero_head")
                return ("guess", "Sensitive", "Zero-head residual. Do not infer padding semantics.");

            if (bucket == "other")
                return ("guess", "Sensitive", "Residual bucket. Keep this metadata-first.");

            return ("structural", "Dangerous", "Bucket evidence only. No write path.");
        }
    }
}
