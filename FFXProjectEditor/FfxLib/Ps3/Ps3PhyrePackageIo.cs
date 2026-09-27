using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal enum Ps3PhyrePackageKind
    {
        DdsTexture,
        DaeModel,
        AgsTextureAnimation,
        FxShader,
        OtherPhyre,
        NotPhyre
    }

    internal sealed record Ps3PhyreMarkerCount(string Marker, int Count);

    internal sealed record Ps3PhyreStringEntry(int Offset, string Value, string Role)
    {
        public string OffsetDisplay => "0x" + Offset.ToString("X", CultureInfo.InvariantCulture);
    }

    internal sealed record Ps3PhyrePackageInspection(
        string FilePath,
        string FileName,
        Ps3PhyrePackageKind Kind,
        bool IsPhyre,
        long FileSize,
        string Sha256,
        string PlatformMarker,
        uint EndianMarker,
        uint MaxTextureOrBufferSize,
        IReadOnlyList<Ps3PhyreMarkerCount> MarkerCounts,
        IReadOnlyList<Ps3PhyreStringEntry> InterestingStrings,
        string Note)
    {
        public string KindLabel => Kind switch
        {
            Ps3PhyrePackageKind.DdsTexture => ".dds.phyre texture",
            Ps3PhyrePackageKind.DaeModel => ".dae.phyre model/scene",
            Ps3PhyrePackageKind.AgsTextureAnimation => ".ags.phyre texture animation",
            Ps3PhyrePackageKind.FxShader => ".fx.phyre shader blob",
            Ps3PhyrePackageKind.OtherPhyre => "generic .phyre package",
            _ => "Not a Phyre package."
        };

        public bool CanDdsPayloadRoundTrip => Kind == Ps3PhyrePackageKind.DdsTexture;
        public bool CanCompiledPackageImport => IsPhyre && Kind != Ps3PhyrePackageKind.DdsTexture;
        public string SizeSummary => FileSize.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
        public string Summary => IsPhyre
            ? $"{KindLabel} · {PlatformMarker} · {SizeSummary} · sha256 {Sha256[..Math.Min(12, Sha256.Length)]}"
            : $"{KindLabel} · {Note}";
    }

    internal sealed record Ps3PhyrePackageExtractResult(
        string SourcePath,
        string OutputDirectory,
        string PackageCopyPath,
        string ManifestPath,
        string StringsPath,
        string? ExtractedDdsPath,
        Ps3PhyrePackageInspection Inspection);

    internal sealed record Ps3PhyrePackageImportResult(
        string SourceTemplatePath,
        string ImportPath,
        string OutputPath,
        string? BackupPath,
        Ps3PhyrePackageInspection OutputInspection,
        string Mode,
        string Note);

    internal static class Ps3PhyrePackageIo
    {
        static readonly string[] MarkerNames =
        [
            "PTexture2D",
            "PTexture2DD3D11",
            "PNode",
            "PWorldMatrix",
            "PMesh",
            "PMeshSegment",
            "PMeshInstance",
            "PDataBlock",
            "PVertexStream",
            "PMaterial",
            "PParameterBuffer",
            "PAssetReferenceImport",
            "PSkinBoneRemap",
            "PAnimationSet",
            "PAnimationClip",
            "PAnimationChannelTarget",
            "PClusterHeaderD3D11",
            "PShader",
            "DXBC"
        ];

        public static Ps3PhyrePackageInspection Inspect(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool isPhyre = bytes.Length >= 16
                && bytes[0] == (byte)'R'
                && bytes[1] == (byte)'Y'
                && bytes[2] == (byte)'H'
                && bytes[3] == (byte)'P'
                && bytes[4] == (byte)'T';

            IReadOnlyList<Ps3PhyreMarkerCount> markers = isPhyre
                ? MarkerNames.Select(marker => new Ps3PhyreMarkerCount(marker, CountAscii(bytes, marker))).ToList()
                : [];

            IReadOnlyList<Ps3PhyreStringEntry> strings = isPhyre
                ? ScanInterestingStrings(bytes, 256).ToList()
                : [];

            return new Ps3PhyrePackageInspection(
                path,
                Path.GetFileName(path),
                ClassifyKind(path, isPhyre),
                isPhyre,
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)),
                isPhyre && bytes.Length >= 16 ? Encoding.ASCII.GetString(bytes, 12, Math.Min(4, bytes.Length - 12)).TrimEnd('\0') : "-",
                ReadU32(bytes, 84),
                ReadU32(bytes, 80),
                markers,
                strings,
                isPhyre ? "RYHPT header detected." : "Missing RYHPT header.");
        }

        public static Ps3PhyrePackageExtractResult ExtractPackage(string sourcePath, string outputDirectory)
        {
            Ps3PhyrePackageInspection inspection = Inspect(sourcePath);
            if (!inspection.IsPhyre)
                throw new InvalidDataException(inspection.Note);

            string packageDir = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inspection.FileName) + "_phyre_extract");
            Directory.CreateDirectory(packageDir);

            string copyPath = Path.Combine(packageDir, inspection.FileName);
            string manifestPath = Path.Combine(packageDir, "phyre_manifest.json");
            string stringsPath = Path.Combine(packageDir, "phyre_strings.txt");
            File.Copy(sourcePath, copyPath, overwrite: true);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(inspection, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllLines(stringsPath, inspection.InterestingStrings.Select(s => $"{s.OffsetDisplay}\t{s.Role}\t{s.Value}"));

            string? ddsPath = null;
            if (inspection.Kind == Ps3PhyrePackageKind.DdsTexture)
            {
                ddsPath = Path.Combine(packageDir, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(inspection.FileName)) + ".dds");
                Ps3MagicTextureWriter.ExtractMip0Dds(sourcePath, ddsPath);
            }

            return new Ps3PhyrePackageExtractResult(sourcePath, packageDir, copyPath, manifestPath, stringsPath, ddsPath, inspection);
        }

        public static Ps3PhyrePackageImportResult ImportDdsPayload(string templatePath, string payloadPath, string outputPath)
        {
            Ps3PhyrePackageInspection template = Inspect(templatePath);
            if (template.Kind != Ps3PhyrePackageKind.DdsTexture)
                throw new InvalidDataException("DDS payload import requires a .dds.phyre template.");

            if (!Ps3MagicTextureWriter.TryReadMip0Layout(templatePath, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                throw new InvalidDataException(note);

            byte[] payload = Ps3MagicTextureWriter.ReadCompatibleMip0Payload(payloadPath, layout, out string payloadKind);
            string? backup = BackupIfOverwriting(templatePath, outputPath, "dds_import");
            Ps3PhyreWriteResult write = Ps3MagicTextureWriter.WriteSameShapeMip0(templatePath, payload, outputPath);
            if (!write.SameLayout)
                throw new InvalidDataException("Output Phyre layout drifted after DDS import.");

            return new Ps3PhyrePackageImportResult(
                templatePath,
                payloadPath,
                outputPath,
                backup,
                Inspect(outputPath),
                "dds-mip0-payload",
                $"Imported {payloadKind}; replaced 0x{payload.Length:X} bytes inside mip0 only.");
        }

        public static Ps3PhyrePackageImportResult ImportCompiledPackage(string templatePath, string replacementPhyrePath, string outputPath)
        {
            Ps3PhyrePackageInspection template = Inspect(templatePath);
            Ps3PhyrePackageInspection replacement = Inspect(replacementPhyrePath);
            if (!template.IsPhyre)
                throw new InvalidDataException("Template is not a Phyre package.");
            if (!replacement.IsPhyre)
                throw new InvalidDataException("Replacement is not a Phyre package.");
            if (template.Kind != replacement.Kind)
                throw new InvalidDataException($"Replacement kind '{replacement.KindLabel}' does not match template kind '{template.KindLabel}'.");
            if (template.Kind == Ps3PhyrePackageKind.DdsTexture)
                throw new InvalidDataException("Use DDS payload import for .dds.phyre. Whole-package import is reserved for compiled non-DDS Phyre packages.");

            string? backup = BackupIfOverwriting(templatePath, outputPath, "compiled_phyre_import");
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.Copy(replacementPhyrePath, outputPath, overwrite: true);

            return new Ps3PhyrePackageImportResult(
                templatePath,
                replacementPhyrePath,
                outputPath,
                backup,
                Inspect(outputPath),
                "compiled-phyre-package",
                "Imported a precompiled Phyre package. This does not compile glTF/FBX/DAE source; it safely stages an already-built .dae.phyre/.ags.phyre/.fx.phyre.");
        }

        public static string BuildDefaultOutputPath(string sourcePath, string suffix)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            string file = Path.GetFileName(sourcePath);
            return Path.Combine(dir, file + suffix);
        }

        static string? BackupIfOverwriting(string sourcePath, string outputPath, string tag)
        {
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                return null;

            string backupPath = outputPath + ".backup_before_" + tag + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            File.Copy(sourcePath, backupPath, overwrite: false);
            return backupPath;
        }

        static Ps3PhyrePackageKind ClassifyKind(string path, bool isPhyre)
        {
            if (!isPhyre)
                return Ps3PhyrePackageKind.NotPhyre;

            string name = Path.GetFileName(path);
            if (name.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
                return Ps3PhyrePackageKind.DdsTexture;
            if (name.EndsWith(".dae.phyre", StringComparison.OrdinalIgnoreCase))
                return Ps3PhyrePackageKind.DaeModel;
            if (name.EndsWith(".ags.phyre", StringComparison.OrdinalIgnoreCase))
                return Ps3PhyrePackageKind.AgsTextureAnimation;
            if (name.Contains(".fx", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".phyre", StringComparison.OrdinalIgnoreCase))
                return Ps3PhyrePackageKind.FxShader;
            return Ps3PhyrePackageKind.OtherPhyre;
        }

        static IEnumerable<Ps3PhyreStringEntry> ScanInterestingStrings(byte[] bytes, int cap)
        {
            int start = -1;
            HashSet<string> seen = new(StringComparer.Ordinal);
            for (int i = 0; i <= bytes.Length && seen.Count < cap; i++)
            {
                bool ascii = i < bytes.Length && bytes[i] is >= 0x20 and <= 0x7E;
                if (ascii)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start >= 0 && i - start >= 4)
                {
                    string value = Encoding.ASCII.GetString(bytes, start, i - start);
                    string role = ClassifyString(value);
                    if (role != "unclassified" && seen.Add(start.ToString(CultureInfo.InvariantCulture) + value))
                        yield return new Ps3PhyreStringEntry(start, value, role);
                }

                start = -1;
            }
        }

        static string ClassifyString(string value)
        {
            if (value.Contains(".dds", StringComparison.OrdinalIgnoreCase))
                return "texture_path";
            if (value.EndsWith(".dae", StringComparison.OrdinalIgnoreCase) || value.Contains(".dae/", StringComparison.OrdinalIgnoreCase))
                return "dae_source_path";
            if (value.StartsWith('P') && (value.Contains("Mesh", StringComparison.Ordinal) || value.Contains("Material", StringComparison.Ordinal) || value.Contains("Texture", StringComparison.Ordinal) || value.Contains("Node", StringComparison.Ordinal)))
                return "phyre_type_or_block";
            if (value.Contains("Shader", StringComparison.OrdinalIgnoreCase) || value.Contains("Sampler", StringComparison.OrdinalIgnoreCase) || value.Contains("Diffuse", StringComparison.OrdinalIgnoreCase) || value.Contains("Color", StringComparison.OrdinalIgnoreCase) || value.Contains("Colour", StringComparison.OrdinalIgnoreCase))
                return "shader_or_material_parameter";
            if (value.Contains("Skeleton", StringComparison.OrdinalIgnoreCase) || value.Contains("Bone", StringComparison.OrdinalIgnoreCase))
                return "skeleton_or_bone";
            if (value.Contains("PAssetReference", StringComparison.Ordinal) || value.Contains("PParameterBuffer", StringComparison.Ordinal))
                return "phyre_reference_or_parameter";
            return "unclassified";
        }

        static int CountAscii(byte[] bytes, string marker)
        {
            byte[] needle = Encoding.ASCII.GetBytes(marker);
            int count = 0;
            for (int i = 0; i <= bytes.Length - needle.Length; i++)
            {
                bool match = true;
                for (int k = 0; k < needle.Length; k++)
                {
                    if (bytes[i + k] != needle[k])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    count++;
            }
            return count;
        }

        static uint ReadU32(byte[] buffer, int offset) =>
            offset < 0 || offset + 4 > buffer.Length ? 0u : BitConverter.ToUInt32(buffer, offset);
    }
}
