using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal sealed class Ps2PresentationContainerRecord
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Extension { get; init; }
        public required long FileSize { get; init; }
        public required string Signature { get; init; }
        public required string Domain { get; init; }
        public required string Cohort { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string First16Hex { get; init; }
        public required string CompanionSummary { get; init; }
        public required string BlockedReason { get; init; }
        public string SizeSummary => $"{FileSize:N0} bytes";
    }

    internal static class Ps2PresentationContainerReader
    {
        static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vpa",
            ".ebp",
            ".omd",
            ".sps2"
        };

        public static IReadOnlyList<Ps2PresentationContainerRecord> Scan(string ffxPs2Root)
        {
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return [];

            List<string> files = Directory
                .EnumerateFiles(ffxPs2Root, "*.*", SearchOption.AllDirectories)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            Dictionary<string, List<string>> companionsByStem = files
                .GroupBy(path => $"{Path.GetDirectoryName(path)}|{Path.GetFileNameWithoutExtension(path)}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            return files
                .Select(path => TryRead(ffxPs2Root, path, companionsByStem))
                .Where(record => record != null)
                .Cast<Ps2PresentationContainerRecord>()
                .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static Ps2PresentationContainerRecord? TryRead(string root, string path, IReadOnlyDictionary<string, List<string>> companionsByStem)
        {
            byte[] head = ReadHead(path, 16);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string signature = head.Length >= 4
                ? new string(head.Take(4).Select(value => value >= 0x20 && value <= 0x7E ? (char)value : '.').ToArray())
                : "-";
            string key = $"{Path.GetDirectoryName(path)}|{Path.GetFileNameWithoutExtension(path)}";

            return new Ps2PresentationContainerRecord
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                RelativePath = Path.GetRelativePath(root, path),
                Extension = ext,
                FileSize = new FileInfo(path).Length,
                Signature = signature,
                Domain = ClassifyDomain(path, ext),
                Cohort = ClassifyCohort(path, ext),
                EvidenceLabel = ClassifyEvidence(ext),
                First16Hex = head.Length == 0 ? "-" : BitConverter.ToString(head).Replace('-', ' '),
                CompanionSummary = BuildCompanionSummary(path, companionsByStem.TryGetValue(key, out List<string>? values) ? values : []),
                BlockedReason = ClassifyBlockedReason(ext)
            };
        }

        static string ClassifyDomain(string path, string ext)
        {
            string lower = path.ToLowerInvariant();
            return ext switch
            {
                ".vpa" when lower.Contains("\\btlmap\\") => "battle_map_container",
                ".vpa" => "map_container",
                ".ebp" => "event_object_container",
                ".omd" => "presentation_effect_container",
                ".sps2" => "help_support_container",
                _ => "unknown"
            };
        }

        static string ClassifyCohort(string path, string ext)
        {
            string lower = path.ToLowerInvariant();
            return ext switch
            {
                ".vpa" when lower.Contains("\\btlmap\\") => "vpa_btlmap",
                ".vpa" => "vpa_map",
                ".ebp" when lower.Contains("\\event\\obj\\") => "ebp_event_obj",
                ".ebp" => "ebp_other",
                ".omd" when lower.Contains("encount2") => "omd_encount2",
                ".omd" when lower.Contains("encount") => "omd_encount",
                ".omd" when lower.Contains("mag_") => "omd_magic_effect",
                ".omd" when lower.Contains("abmap") => "omd_abmap",
                ".omd" => "omd_other",
                ".sps2" when lower.Contains("\\help_inter\\") => "sps2_help_inter",
                ".sps2" when lower.Contains("\\help\\") => "sps2_help",
                ".sps2" => "sps2_other",
                _ => "unknown"
            };
        }

        static string ClassifyEvidence(string ext)
        {
            return ext switch
            {
                ".vpa" => "proved",
                ".ebp" => "proved",
                ".omd" => "proved",
                ".sps2" => "structural",
                _ => "guess"
            };
        }

        static string ClassifyBlockedReason(string ext)
        {
            return ext switch
            {
                ".vpa" => "MAP1 family is strong, but deep map semantics remain blocked.",
                ".ebp" => "EV01 family is strong, but deep event semantics remain blocked.",
                ".omd" => "Cohorts are strong, but deep presentation semantics remain blocked.",
                ".sps2" => "Support/help role is stronger than any final image/script decode claim.",
                _ => "Unknown family."
            };
        }

        static string BuildCompanionSummary(string path, IReadOnlyList<string> siblingPaths)
        {
            if (siblingPaths.Count <= 1)
                return "No same-stem companion detected in the local directory.";

            string stem = Path.GetFileNameWithoutExtension(path);
            string directory = Path.GetDirectoryName(path) ?? "-";
            string joined = string.Join(", ", siblingPaths
                .Select(value => Path.GetExtension(value).ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            return $"{directory}\\{stem}.* -> sibling set {joined}";
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
