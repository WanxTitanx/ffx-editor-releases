using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal enum Ps2PipelineEvidenceLevel
    {
        Proved,
        Structural,
        Guess,
        Blocked
    }

    internal enum Ps2PipelineProductStatus
    {
        Documented,
        Partial,
        Blocked
    }

    internal sealed class Ps2PipelineWarning
    {
        public Ps2PipelineWarning(string code, string title, string message, Ps2PipelineEvidenceLevel evidence, Ps2PipelineProductStatus status)
        {
            Code = code;
            Title = title;
            Message = message;
            Evidence = evidence;
            Status = status;
        }

        public string Code { get; }
        public string Title { get; }
        public string Message { get; }
        public Ps2PipelineEvidenceLevel Evidence { get; }
        public Ps2PipelineProductStatus Status { get; }
        public string Summary => $"{Title}: {Message}";
    }

    internal sealed class Ps2FileProbe
    {
        public required string Path { get; init; }
        public required bool Exists { get; init; }
        public required long SizeBytes { get; init; }
        public required string Sha256 { get; init; }
        public required string HeadHex { get; init; }
        public required IReadOnlyList<string> Strings { get; init; }
        public required IReadOnlyList<Ps2PipelineWarning> Warnings { get; init; }
        public string Name => System.IO.Path.GetFileName(Path);
        public string SizeSummary => Exists ? $"{SizeBytes:N0} bytes" : "Missing";
        public string StringPreview => Strings.Count == 0 ? "-" : string.Join(Environment.NewLine, Strings.Take(8));
        public string WarningSummary => Warnings.Count == 0 ? "-" : string.Join(Environment.NewLine, Warnings.Select(warning => warning.Summary));
    }

    internal sealed class Ps2CdIndexRecord
    {
        public required string DirectoryPath { get; init; }
        public required string Variant { get; init; }
        public required Ps2FileProbe Id { get; init; }
        public required Ps2FileProbe Fid { get; init; }
        public required Ps2FileProbe Mdg { get; init; }
        public required IReadOnlyList<uint> IdU32 { get; init; }
        public required int FidU16Count { get; init; }
        public required int FidFFFFCount { get; init; }
        public required Ps2PipelineProductStatus Status { get; init; }
        public required IReadOnlyList<Ps2PipelineWarning> Warnings { get; init; }
        public string Label => $"{Variant} · {System.IO.Path.GetFileName(DirectoryPath)}";
        public string TripletSummary => $"id={Id.SizeSummary} · fid={Fid.SizeSummary} · mdg={Mdg.SizeSummary}";
        public string IdPrefixSummary => IdU32.Count == 0 ? "-" : string.Join(", ", IdU32.Take(6).Select(value => $"0x{value:X8}"));
        public string WarningSummary => Warnings.Count == 0 ? "-" : string.Join(Environment.NewLine, Warnings.Select(warning => warning.Summary));
    }

    internal sealed class Ps2DescriptorRecord
    {
        public required string Path { get; init; }
        public required string Kind { get; init; }
        public required Ps2FileProbe Probe { get; init; }
        public required int ParsedItemCount { get; init; }
        public required Ps2PipelineProductStatus Status { get; init; }
        public required IReadOnlyList<Ps2PipelineWarning> Warnings { get; init; }
        public string Label => $"{Kind} · {System.IO.Path.GetFileName(Path)}";
        public string WarningSummary => Warnings.Count == 0 ? "-" : string.Join(Environment.NewLine, Warnings.Select(warning => warning.Summary));
    }

    internal sealed class Ps2AbmapRecord
    {
        public required string Path { get; init; }
        public required string Kind { get; init; }
        public required Ps2FileProbe Probe { get; init; }
        public required IReadOnlyList<string> References { get; init; }
        public required Ps2PipelineProductStatus Status { get; init; }
        public required IReadOnlyList<Ps2PipelineWarning> Warnings { get; init; }
        public string Label => $"{Kind} · {System.IO.Path.GetFileName(Path)}";
        public string ReferenceSummary => References.Count == 0 ? "-" : string.Join(Environment.NewLine, References.Take(12));
        public string WarningSummary => Warnings.Count == 0 ? "-" : string.Join(Environment.NewLine, Warnings.Select(warning => warning.Summary));
    }

    internal sealed class Ps2PipelineEdge
    {
        public required string From { get; init; }
        public required string To { get; init; }
        public required string Relation { get; init; }
        public required Ps2PipelineEvidenceLevel Evidence { get; init; }
        public required Ps2PipelineProductStatus Status { get; init; }
        public required string Note { get; init; }
        public string Summary => $"{Relation} · {System.IO.Path.GetFileName(From)} -> {To}";
    }

    internal sealed class Ps2ProjectPipelineSnapshot
    {
        public required string Root { get; init; }
        public required bool RootExists { get; init; }
        public required IReadOnlyList<Ps2CdIndexRecord> CdIndexRecords { get; init; }
        public required IReadOnlyList<Ps2DescriptorRecord> Descriptors { get; init; }
        public required IReadOnlyList<Ps2AbmapRecord> AbmapRecords { get; init; }
        public required IReadOnlyList<Ps2PipelineEdge> Edges { get; init; }
        public required IReadOnlyList<Ps2PipelineWarning> Warnings { get; init; }
        public required IReadOnlyList<string> BlockedActions { get; init; }
    }

    internal static class Ps2ProjectPipelineReader
    {
        static readonly Ps2PipelineWarning PipelineWarning = new(
            "PIPELINE",
            "PIPELINE",
            "This file belongs to a build/index/pipeline/support lane.",
            Ps2PipelineEvidenceLevel.Structural,
            Ps2PipelineProductStatus.Partial);

        static readonly Ps2PipelineWarning NotFinalAssetWarning = new(
            "NOT_FINAL_ASSET",
            "NOT FINAL ASSET",
            "This surface does not present the file as a final editable asset.",
            Ps2PipelineEvidenceLevel.Structural,
            Ps2PipelineProductStatus.Partial);

        static readonly Ps2PipelineWarning DoNotWriteWarning = new(
            "DO_NOT_WRITE",
            "DO NOT WRITE",
            "Read-only only. Parser, regional diff, runtime consumer, and roundtrip evidence are required before any write claim.",
            Ps2PipelineEvidenceLevel.Blocked,
            Ps2PipelineProductStatus.Blocked);

        public static Ps2ProjectPipelineSnapshot Read(string? root)
        {
            List<Ps2PipelineWarning> warnings = [PipelineWarning, NotFinalAssetWarning, DoNotWriteWarning];
            List<string> blocked =
            [
                "No writer, patcher, repacker, normalizer, or deduper belongs in this Project/Pipeline surface.",
                "Do not promote .mdg, .fid, .dat, .pdt, .otp, or support blobs into final decoders here.",
                "Keep eiichi_abmap_data -> master menu abmap DAT as a bridge gap until a compiler or packer is observed."
            ];

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                warnings.Add(new Ps2PipelineWarning(
                    "ROOT_MISSING",
                    "ROOT MISSING",
                    "Configured ffx_ps2 root was not found. Surface should open in empty read-only mode.",
                    Ps2PipelineEvidenceLevel.Proved,
                    Ps2PipelineProductStatus.Blocked));

                return new Ps2ProjectPipelineSnapshot
                {
                    Root = root ?? "-",
                    RootExists = false,
                    CdIndexRecords = [],
                    Descriptors = [],
                    AbmapRecords = [],
                    Edges = [],
                    Warnings = warnings,
                    BlockedActions = blocked
                };
            }

            List<Ps2CdIndexRecord> cdIndex = ReadCdIndexRecords(root).ToList();
            List<Ps2DescriptorRecord> descriptors = ReadDescriptors(root).ToList();
            List<Ps2AbmapRecord> abmap = ReadAbmap(root).ToList();
            List<Ps2PipelineEdge> edges = BuildEdges(cdIndex, descriptors, abmap).ToList();

            return new Ps2ProjectPipelineSnapshot
            {
                Root = root,
                RootExists = true,
                CdIndexRecords = cdIndex,
                Descriptors = descriptors,
                AbmapRecords = abmap,
                Edges = edges,
                Warnings = warnings,
                BlockedActions = blocked
            };
        }

        static IEnumerable<Ps2CdIndexRecord> ReadCdIndexRecords(string root)
        {
            IEnumerable<string> idFiles = SafeEnumerateFiles(root, "cdrom.id")
                .Where(path => path.Contains(System.IO.Path.Combine("ffx", "proj"), StringComparison.OrdinalIgnoreCase));

            foreach (string idPath in idFiles)
            {
                string dir = System.IO.Path.GetDirectoryName(idPath) ?? root;
                string fidPath = System.IO.Path.Combine(dir, "cdrom.fid");
                string mdgPath = System.IO.Path.Combine(dir, "cdrom.mdg");
                Ps2FileProbe id = ProbeFile(idPath, 256);
                Ps2FileProbe fid = ProbeFile(fidPath, 256);
                Ps2FileProbe mdg = ProbeFile(mdgPath, 256);
                IReadOnlyList<uint> idU32 = ReadUInt32Prefix(idPath, 12);
                IReadOnlyList<ushort> fidValues = ReadUInt16Prefix(fidPath, 65);
                List<Ps2PipelineWarning> warnings = [PipelineWarning, NotFinalAssetWarning, DoNotWriteWarning];

                if (id.Exists && id.SizeBytes != 80)
                    warnings.Add(SizeWarning("cdrom.id", 80, id.SizeBytes));
                if (fid.Exists && fid.SizeBytes != 130)
                    warnings.Add(SizeWarning("cdrom.fid", 130, fid.SizeBytes));
                if (mdg.Exists && mdg.SizeBytes != 131072)
                    warnings.Add(SizeWarning("cdrom.mdg", 131072, mdg.SizeBytes));

                yield return new Ps2CdIndexRecord
                {
                    DirectoryPath = dir,
                    Variant = VariantFromDirectory(dir),
                    Id = id,
                    Fid = fid,
                    Mdg = mdg,
                    IdU32 = idU32,
                    FidU16Count = fidValues.Count,
                    FidFFFFCount = fidValues.Count(value => value == 0xFFFF),
                    Status = Ps2PipelineProductStatus.Partial,
                    Warnings = warnings
                };
            }
        }

        static IEnumerable<Ps2DescriptorRecord> ReadDescriptors(string root)
        {
            string[] candidates =
            [
                "cdrom.def",
                "cdrom.tmp",
                "cdrom.sc",
                "cdrom.fnd",
                "cdrom_cd.fnd",
                "cdrom_lc.fnd",
                "workname",
                "modulesize.bin",
                "sizetbl.bin",
                "sizetbl.ps3.bin",
                "sizetbl.vita.bin"
            ];

            foreach (string name in candidates)
            {
                foreach (string path in SafeEnumerateFiles(root, name).Where(path => path.Contains(System.IO.Path.Combine("ffx", "proj"), StringComparison.OrdinalIgnoreCase)))
                {
                    Ps2FileProbe probe = ProbeFile(path, 4096);
                    int count = CountDescriptorItems(path, name);
                    List<Ps2PipelineWarning> warnings = [PipelineWarning, NotFinalAssetWarning, DoNotWriteWarning];

                    if (name.EndsWith(".fnd", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add(new Ps2PipelineWarning(
                            "STRING_BEARING_BINARY",
                            "STRING-BEARING BINARY",
                            "Strings are visible, but offsets, order, and bytes remain sensitive.",
                            Ps2PipelineEvidenceLevel.Structural,
                            Ps2PipelineProductStatus.Partial));
                    }

                    yield return new Ps2DescriptorRecord
                    {
                        Path = path,
                        Kind = name,
                        Probe = probe,
                        ParsedItemCount = count,
                        Status = Ps2PipelineProductStatus.Partial,
                        Warnings = warnings
                    };
                }
            }
        }

        static IEnumerable<Ps2AbmapRecord> ReadAbmap(string root)
        {
            string sourceRoot = System.IO.Path.Combine(root, "ffx", "eiichi_abmap_data");
            if (Directory.Exists(sourceRoot))
            {
                foreach (string path in SafeEnumerateAllFiles(sourceRoot))
                {
                    string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    IReadOnlyList<string> refs = ext is ".anm" or ".otp"
                        ? ExtractAsciiStrings(path, 4096)
                            .Where(value => value.Contains(".tm2", StringComparison.OrdinalIgnoreCase) || value.Contains("abmap", StringComparison.OrdinalIgnoreCase))
                            .ToList()
                        : [];

                    yield return new Ps2AbmapRecord
                    {
                        Path = path,
                        Kind = "eiichi_abmap_data",
                        Probe = ProbeFile(path, 1024),
                        References = refs,
                        Status = Ps2PipelineProductStatus.Partial,
                        Warnings = [PipelineWarning, NotFinalAssetWarning, DoNotWriteWarning]
                    };
                }
            }

            foreach (string dat in SafeEnumerateFiles(root, "*.dat").Where(path => path.Contains(System.IO.Path.Combine("menu", "abmap"), StringComparison.OrdinalIgnoreCase)))
            {
                yield return new Ps2AbmapRecord
                {
                    Path = dat,
                    Kind = "master_menu_abmap_dat",
                    Probe = ProbeFile(dat, 1024),
                    References = [],
                    Status = Ps2PipelineProductStatus.Partial,
                    Warnings =
                    [
                        PipelineWarning,
                        NotFinalAssetWarning,
                        DoNotWriteWarning,
                        new Ps2PipelineWarning(
                            "OPAQUE_PAYLOAD",
                            "OPAQUE PAYLOAD",
                            "ABMap DAT is treated here as a final carrier reference, not as a decoded asset.",
                            Ps2PipelineEvidenceLevel.Structural,
                            Ps2PipelineProductStatus.Partial)
                    ]
                };
            }
        }

        static IEnumerable<Ps2PipelineEdge> BuildEdges(IEnumerable<Ps2CdIndexRecord> cdIndex, IEnumerable<Ps2DescriptorRecord> descriptors, IEnumerable<Ps2AbmapRecord> abmap)
        {
            foreach (Ps2CdIndexRecord record in cdIndex)
            {
                yield return new Ps2PipelineEdge
                {
                    From = record.Id.Path,
                    To = record.Mdg.Path,
                    Relation = "indexes",
                    Evidence = Ps2PipelineEvidenceLevel.Structural,
                    Status = Ps2PipelineProductStatus.Partial,
                    Note = "cdrom.id points into the CD index triplet; fields remain read-only."
                };

                yield return new Ps2PipelineEdge
                {
                    From = record.Fid.Path,
                    To = record.Mdg.Path,
                    Relation = "slot/table support",
                    Evidence = Ps2PipelineEvidenceLevel.Structural,
                    Status = Ps2PipelineProductStatus.Partial,
                    Note = "cdrom.fid slot semantics remain blocked."
                };
            }

            foreach (Ps2DescriptorRecord descriptor in descriptors.Where(value => value.Kind is "cdrom.tmp" or "cdrom.sc" or "cdrom.def"))
            {
                yield return new Ps2PipelineEdge
                {
                    From = descriptor.Path,
                    To = "cdrom.id/fid/mdg",
                    Relation = "pipeline descriptor",
                    Evidence = Ps2PipelineEvidenceLevel.Structural,
                    Status = Ps2PipelineProductStatus.Partial,
                    Note = "Build descriptor connects to the CD index surface."
                };
            }

            foreach (Ps2AbmapRecord source in abmap.Where(value => value.Kind == "eiichi_abmap_data" && value.References.Count > 0))
            {
                foreach (string target in source.References)
                {
                    yield return new Ps2PipelineEdge
                    {
                        From = source.Path,
                        To = target,
                        Relation = "explicit support reference",
                        Evidence = Ps2PipelineEvidenceLevel.Proved,
                        Status = Ps2PipelineProductStatus.Documented,
                        Note = "Read-only string reference extracted from the support tree."
                    };
                }
            }

            if (abmap.Any(value => value.Kind == "master_menu_abmap_dat"))
            {
                yield return new Ps2PipelineEdge
                {
                    From = "eiichi_abmap_data",
                    To = "master/*/menu/abmap/dat*.dat",
                    Relation = "bridge gap",
                    Evidence = Ps2PipelineEvidenceLevel.Blocked,
                    Status = Ps2PipelineProductStatus.Blocked,
                    Note = "Structural support exists, but no compiler or packer was observed."
                };
            }
        }

        static Ps2FileProbe ProbeFile(string path, int maxBytes)
        {
            if (!File.Exists(path))
            {
                return new Ps2FileProbe
                {
                    Path = path,
                    Exists = false,
                    SizeBytes = 0,
                    Sha256 = string.Empty,
                    HeadHex = string.Empty,
                    Strings = [],
                    Warnings =
                    [
                        new Ps2PipelineWarning(
                            "FILE_MISSING",
                            "FILE MISSING",
                            "Expected project pipeline file is absent.",
                            Ps2PipelineEvidenceLevel.Proved,
                            Ps2PipelineProductStatus.Blocked)
                    ]
                };
            }

            try
            {
                FileInfo info = new(path);
                byte[] head = ReadPrefix(path, maxBytes);

                return new Ps2FileProbe
                {
                    Path = path,
                    Exists = true,
                    SizeBytes = info.Length,
                    Sha256 = Sha256(path),
                    HeadHex = ToHex(head.Take(Math.Min(head.Length, 32))),
                    Strings = ExtractAsciiStrings(path, maxBytes),
                    Warnings = [PipelineWarning, NotFinalAssetWarning, DoNotWriteWarning]
                };
            }
            catch (IOException)
            {
                return UnreadableProbe(path, "File exists but could not be read during snapshot capture.");
            }
            catch (UnauthorizedAccessException)
            {
                return UnreadableProbe(path, "File exists but access was denied during snapshot capture.");
            }
        }

        static Ps2PipelineWarning SizeWarning(string label, long expected, long actual) =>
            new(
                "SIZE_MISMATCH",
                "SIZE MISMATCH",
                $"{label} expected {expected.ToString(CultureInfo.InvariantCulture)} bytes, found {actual.ToString(CultureInfo.InvariantCulture)}.",
                Ps2PipelineEvidenceLevel.Proved,
                Ps2PipelineProductStatus.Partial);

        static IReadOnlyList<uint> ReadUInt32Prefix(string path, int maxValues)
        {
            byte[] bytes = ReadPrefix(path, maxValues * 4);
            List<uint> values = [];
            for (int index = 0; index + 3 < bytes.Length; index += 4)
                values.Add(BitConverter.ToUInt32(bytes, index));

            return values;
        }

        static IReadOnlyList<ushort> ReadUInt16Prefix(string path, int maxValues)
        {
            byte[] bytes = ReadPrefix(path, maxValues * 2);
            List<ushort> values = [];
            for (int index = 0; index + 1 < bytes.Length; index += 2)
                values.Add(BitConverter.ToUInt16(bytes, index));

            return values;
        }

        static int CountDescriptorItems(string path, string name)
        {
            if (!File.Exists(path))
                return 0;

            try
            {
                if (name is "cdrom.def" or "cdrom.tmp" or "cdrom.sc")
                {
                    return File.ReadLines(path)
                        .Count(line => line.Contains("#define", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("Module", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("head", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("file", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("efile", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("Layout", StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("File", StringComparison.OrdinalIgnoreCase));
                }

                return ExtractAsciiStrings(path, 1024 * 1024).Count;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        static IReadOnlyList<string> ExtractAsciiStrings(string path, int maxBytes)
        {
            byte[] bytes = ReadPrefix(path, maxBytes);
            string text = Encoding.ASCII.GetString(bytes.Select(value => value is >= 32 and <= 126 ? value : (byte)0x20).ToArray());

            return Regex.Matches(text, @"[ -~]{4,}")
                .Select(match => match.Value.Trim())
                .Where(value => value.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(128)
                .ToList();
        }

        static byte[] ReadPrefix(string path, int maxBytes)
        {
            if (!File.Exists(path))
                return [];

            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] buffer = new byte[Math.Min(maxBytes, (int)Math.Min(stream.Length, int.MaxValue))];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                        break;

                    offset += read;
                }

                if (offset == buffer.Length)
                    return buffer;

                return buffer.Take(offset).ToArray();
            }
            catch (IOException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }

        static string Sha256(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        static string ToHex(IEnumerable<byte> bytes) =>
            string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));

        static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).ToArray();
            }
            catch
            {
                return [];
            }
        }

        static IEnumerable<string> SafeEnumerateAllFiles(string root)
        {
            try
            {
                return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
            }
            catch
            {
                return [];
            }
        }

        static Ps2FileProbe UnreadableProbe(string path, string reason) =>
            new()
            {
                Path = path,
                Exists = true,
                SizeBytes = 0,
                Sha256 = string.Empty,
                HeadHex = string.Empty,
                Strings = [],
                Warnings =
                [
                    PipelineWarning,
                    NotFinalAssetWarning,
                    DoNotWriteWarning,
                    new Ps2PipelineWarning(
                        "FILE_UNREADABLE",
                        "FILE UNREADABLE",
                        reason,
                        Ps2PipelineEvidenceLevel.Proved,
                        Ps2PipelineProductStatus.Partial)
                ]
            };

        static string VariantFromDirectory(string dir)
        {
            string name = new DirectoryInfo(dir).Name;
            return name.Equals("cddata", StringComparison.OrdinalIgnoreCase)
                ? "battle-jp-cddata"
                : name;
        }
    }
}
