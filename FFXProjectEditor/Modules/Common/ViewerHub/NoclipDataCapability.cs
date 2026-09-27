using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// Validates user-owned Final Fantasy X data used by the bundled noclip viewer. This service is
    /// deliberately read-only: it never downloads, repairs, or mutates the selected game extraction.
    /// </summary>
    public static class NoclipDataCapability
    {
        private sealed record CriticalFile(string RelativePath, int OffsetField);

        private static readonly CriticalFile[] CriticalFiles =
        {
            new("common_textures.bin", 0x3C),
            new("screen_shatter.bin", 0x3C),
            new("env_map_texture.bin", 0x18),
        };

        // The noclip CDN (z.noclip.website/FinalFantasyX) publishes exactly these 12 data
        // directories plus the 3 critical root files — this is the set a clean machine can
        // bootstrap and the bundled viewer actually requires.
        private static readonly string[] CoreDirectories =
        {
            "0c", "0d", "0e", "11", "13", "1a", "1c", "1d", "1e", "20", "21", "22",
        };

        // Enriched directories exist only in local developer extractions (10=monster stats,
        // 14=actor indices, 1f=summon model, 28=weapon stats). The frontend requests them with
        // allow404, so they never gate readiness — they are reported as informational only.
        private static readonly string[] EnrichedDirectories =
        {
            "10", "14", "1f", "28",
        };

        internal static IReadOnlyList<string> RequiredDirectoryNames => CoreDirectories;
        internal static IReadOnlyList<string> EnrichedDirectoryNames => EnrichedDirectories;

        public sealed record Report(
            bool Ready,
            string? FfxDataRoot,
            IReadOnlyList<string> Issues,
            IReadOnlyList<string>? MissingEnriched = null);

        public static Report Validate(string? noclipRoot)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(noclipRoot))
                return new Report(false, null, new[] { "NOCLIP_ROOT_NOT_CONFIGURED" });

            string canonicalNoclipRoot;
            string ffxDataRoot;
            try
            {
                canonicalNoclipRoot = Path.GetFullPath(noclipRoot);
                ffxDataRoot = Path.GetFullPath(Path.Combine(
                    canonicalNoclipRoot,
                    "data",
                    "FinalFantasyX"));
            }
            catch { return new Report(false, null, new[] { "NOCLIP_ROOT_INVALID" }); }

            if (!NoclipLocator.IsLocalConfigurationPath(canonicalNoclipRoot))
                return new Report(false, ffxDataRoot, new[] { "LOCAL_FILESYSTEM_REQUIRED:NOCLIP_ROOT" });

            if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(
                    canonicalNoclipRoot,
                    out FileSystemReparseGuard.VerifiedDirectory? noclipDirectory))
                return new Report(false, ffxDataRoot, new[] { "REPARSE_POINT_REJECTED:NOCLIP_ROOT" });
            noclipDirectory.Dispose();
            if (!Directory.Exists(ffxDataRoot))
                return new Report(false, ffxDataRoot, new[] { "FFX_DATA_ROOT_MISSING" });
            if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(
                    ffxDataRoot,
                    out FileSystemReparseGuard.VerifiedDirectory? ffxDirectory))
                return new Report(false, ffxDataRoot, new[] { "REPARSE_POINT_REJECTED:FinalFantasyX" });
            ffxDirectory.Dispose();

            foreach (string directory in CoreDirectories)
            {
                string required = Path.Combine(ffxDataRoot, directory);
                if (!Directory.Exists(required))
                    issues.Add($"REQUIRED_DIRECTORY_MISSING:{directory}");
                else if (!FileSystemReparseGuard.TryOpenVerifiedDirectory(
                        required,
                        out FileSystemReparseGuard.VerifiedDirectory? requiredDirectory))
                    issues.Add($"REPARSE_POINT_REJECTED:{directory}");
                else
                {
                    requiredDirectory.Dispose();
                    if (!ContainsCanonicalDataFile(ffxDataRoot, required))
                        issues.Add($"REQUIRED_DIRECTORY_EMPTY:{directory}");
                }
            }

            var missingEnriched = new List<string>();
            foreach (string directory in EnrichedDirectories)
            {
                string enriched = Path.Combine(ffxDataRoot, directory);
                if (!Directory.Exists(enriched) || !ContainsCanonicalDataFile(ffxDataRoot, enriched))
                    missingEnriched.Add(directory);
            }

            foreach (CriticalFile critical in CriticalFiles)
            {
                string file = Path.Combine(ffxDataRoot, critical.RelativePath);
                FileSystemReparseGuard.VerifiedOpenResult openResult =
                    FileSystemReparseGuard.TryOpenVerifiedRead(
                        ffxDataRoot,
                        file,
                        out FileSystemReparseGuard.VerifiedReadFile? verified);
                if (openResult == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
                {
                    issues.Add($"CRITICAL_FILE_MISSING:{critical.RelativePath}");
                    continue;
                }
                if (openResult != FileSystemReparseGuard.VerifiedOpenResult.Success || verified == null)
                {
                    issues.Add($"REPARSE_POINT_REJECTED:{critical.RelativePath}");
                    continue;
                }
                using (verified)
                {
                    if (!HasPlausibleOffset(verified.Stream, critical.OffsetField))
                        issues.Add($"CRITICAL_FILE_INVALID:{critical.RelativePath}");
                }
            }

            return new Report(issues.Count == 0, ffxDataRoot, issues, missingEnriched);
        }

        private static bool HasPlausibleOffset(FileStream stream, int offsetField)
        {
            try
            {
                if (stream.Length < offsetField + sizeof(uint) || stream.Length > int.MaxValue)
                    return false;
                stream.Position = offsetField;
                Span<byte> bytes = stackalloc byte[sizeof(uint)];
                if (stream.Read(bytes) != bytes.Length) return false;
                uint offset = BitConverter.ToUInt32(bytes);
                return offset > 0 && offset < stream.Length;
            }
            catch { return false; }
        }

        /// <summary>
        /// Single-file plausibility probe for the managed bootstrap: verified-open under the
        /// bootstrap root (reparse-safe) then the same offset check used by the capability scan.
        /// </summary>
        internal static bool HasPlausibleOffset(string trustedRoot, string filePath, int offsetField)
        {
            if (FileSystemReparseGuard.TryOpenVerifiedRead(
                    trustedRoot,
                    filePath,
                    out FileSystemReparseGuard.VerifiedReadFile? verified) !=
                    FileSystemReparseGuard.VerifiedOpenResult.Success ||
                verified == null)
                return false;
            using (verified)
                return HasPlausibleOffset(verified.Stream, offsetField);
        }

        private static bool ContainsCanonicalDataFile(string trustedRoot, string directory)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.bin", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (name.Length != 8 ||
                        !ushort.TryParse(
                            name.AsSpan(0, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out _) ||
                        FileSystemReparseGuard.TryOpenVerifiedRead(
                            trustedRoot,
                            file,
                            out FileSystemReparseGuard.VerifiedReadFile? verified) !=
                            FileSystemReparseGuard.VerifiedOpenResult.Success ||
                        verified == null)
                        continue;
                    using (verified)
                    {
                        if (verified.Stream.Length > 0)
                            return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

    }
}
