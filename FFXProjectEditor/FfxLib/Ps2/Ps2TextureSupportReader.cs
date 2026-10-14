using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal sealed class Ps2TextureSupportMetadata
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Extension { get; init; }
        public required long FileSize { get; init; }
        public required string Cohort { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string PairSummary { get; init; }
        public required string First16Hex { get; init; }
        public required string? BlockedReason { get; init; }
        public required Ps2TextureProvenance Provenance { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }

    internal static class Ps2TextureSupportReader
    {
        static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txc",
            ".clt",
            ".fmt",
            ".sps2"
        };

        public static IReadOnlyList<Ps2TextureSupportMetadata> Scan(string ffxPs2Root)
        {
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return [];

            List<string> files = Directory
                .EnumerateFiles(ffxPs2Root, "*.*", SearchOption.AllDirectories)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            Dictionary<string, List<string>> siblingsByStem = files
                .GroupBy(path => $"{Path.GetDirectoryName(path)}|{Path.GetFileNameWithoutExtension(path)}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            return files
                .Select(path => TryRead(ffxPs2Root, path, siblingsByStem))
                .Where(metadata => metadata != null)
                .Cast<Ps2TextureSupportMetadata>()
                .OrderBy(metadata => metadata.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static Ps2TextureSupportMetadata? TryRead(string root, string path, IReadOnlyDictionary<string, List<string>> siblingsByStem)
        {
            byte[] head = ReadHead(path, 16);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string siblingKey = $"{Path.GetDirectoryName(path)}|{Path.GetFileNameWithoutExtension(path)}";
            List<string> siblingPaths = siblingsByStem.TryGetValue(siblingKey, out List<string>? values)
                ? values
                : [];

            return new Ps2TextureSupportMetadata
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                RelativePath = Path.GetRelativePath(root, path),
                Extension = ext,
                FileSize = new FileInfo(path).Length,
                Cohort = ClassifyCohort(path, ext, siblingPaths),
                EvidenceLabel = ClassifyEvidence(ext),
                PairSummary = BuildPairSummary(path, ext, siblingPaths),
                First16Hex = head.Length == 0 ? "-" : BitConverter.ToString(head).Replace('-', ' '),
                BlockedReason = ClassifyBlockedReason(ext),
                Provenance = Ps2Tim2Reader.ClassifyProvenanceFromPath(path),
                Warnings = BuildWarnings(ext, siblingPaths)
            };
        }

        static string ClassifyCohort(string path, string ext, IReadOnlyList<string> siblingPaths)
        {
            string lower = path.ToLowerInvariant();

            return ext switch
            {
                ".txc" or ".clt" when siblingPaths.Any(value => value.EndsWith(".txc", StringComparison.OrdinalIgnoreCase))
                                       && siblingPaths.Any(value => value.EndsWith(".clt", StringComparison.OrdinalIgnoreCase))
                    => "txc_clt_pair_candidate",
                ".txc" or ".clt" => "txc_clt_orphan_lane",
                ".fmt" when lower.Contains("\\menu\\") || lower.Contains("\\help\\") || lower.Contains("\\help_inter\\")
                    => "ui_font_or_menu_candidate",
                ".fmt" => "fmt_support_candidate",
                ".sps2" when lower.Contains("\\help\\") || lower.Contains("\\help_inter\\")
                    => "help_ui_support_candidate",
                ".sps2" => "presentation_support_candidate",
                _ => "unknown"
            };
        }

        static string ClassifyEvidence(string ext)
        {
            return ext switch
            {
                ".txc" => "proved",
                ".clt" => "proved",
                ".fmt" => "structural",
                ".sps2" => "structural",
                _ => "guess"
            };
        }

        static string? ClassifyBlockedReason(string ext)
        {
            return ext switch
            {
                ".txc" or ".clt" => "Experimental metadata lane only. No faithful texture decode or writer-safe CLUT rules yet.",
                ".fmt" => "UI/help support lane only. Do not promote to decoder-final or writer-safe text/image claims.",
                ".sps2" => "Support/help presentation lane only. Do not promote to image carrier or final script semantics.",
                _ => "Unknown support family."
            };
        }

        static IReadOnlyList<string> BuildWarnings(string ext, IReadOnlyList<string> siblingPaths)
        {
            List<string> warnings =
            [
                "Read-only only.",
                "Metadata-first lane.",
                "Do not promote this family into a final decoder or write path."
            ];

            if ((ext == ".txc" || ext == ".clt")
                && !(siblingPaths.Any(value => value.EndsWith(".txc", StringComparison.OrdinalIgnoreCase))
                    && siblingPaths.Any(value => value.EndsWith(".clt", StringComparison.OrdinalIgnoreCase))))
            {
                warnings.Add("Pair is incomplete in the same stem; keep the interpretation colder.");
            }

            return warnings;
        }

        static string BuildPairSummary(string path, string ext, IReadOnlyList<string> siblingPaths)
        {
            string directory = Path.GetDirectoryName(path) ?? "-";
            string stem = Path.GetFileNameWithoutExtension(path);
            bool hasTxc = siblingPaths.Any(value => value.EndsWith(".txc", StringComparison.OrdinalIgnoreCase));
            bool hasClt = siblingPaths.Any(value => value.EndsWith(".clt", StringComparison.OrdinalIgnoreCase));

            if ((ext == ".txc" || ext == ".clt") && hasTxc && hasClt)
                return $"{directory}\\{stem}.* -> paired .txc + .clt lane";

            if (siblingPaths.Count > 1)
            {
                string joined = string.Join(", ", siblingPaths
                    .Select(value => Path.GetExtension(value).ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                return $"{directory}\\{stem}.* -> sibling set {joined}";
            }

            return "No same-stem companion detected in the local directory.";
        }

        static byte[] ReadHead(string path, int size)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] buffer = new byte[size];
                int bytesRead = stream.Read(buffer, 0, size);
                return bytesRead == size ? buffer : buffer[..bytesRead];
            }
            catch
            {
                return [];
            }
        }
    }
}
