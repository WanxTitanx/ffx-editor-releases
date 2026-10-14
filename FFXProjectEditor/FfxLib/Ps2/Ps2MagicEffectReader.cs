using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ps2
{
    internal sealed class Ps2MagicKernelArtifact
    {
        public required string Title { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Lane { get; init; }
        public required string Summary { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string First16Hex { get; init; }
        public required string BlockedReason { get; init; }
    }

    internal sealed class Ps2MagicPackageArtifact
    {
        public required string Title { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Lane { get; init; }
        public required int FileCount { get; init; }
        public required int BinCount { get; init; }
        public required int Tm2Count { get; init; }
        public required int OmdCount { get; init; }
        public required int Sps2Count { get; init; }
        public required int OtherCount { get; init; }
        public required string EvidenceLabel { get; init; }
        public required string BlockedReason { get; init; }
        public string Summary => $"{FileCount} files · BIN {BinCount} · TM2 {Tm2Count} · OMD {OmdCount} · SPS2 {Sps2Count} · other {OtherCount}";
    }

    internal sealed class Ps2MagicCrosswalkRow
    {
        public required string Source { get; init; }
        public required string Target { get; init; }
        public required string Status { get; init; }
        public required string Note { get; init; }
    }

    internal sealed class Ps2MagicEffectSnapshot
    {
        public required IReadOnlyList<Ps2MagicKernelArtifact> KernelArtifacts { get; init; }
        public required IReadOnlyList<Ps2MagicPackageArtifact> PackageArtifacts { get; init; }
        public required IReadOnlyList<Ps2MagicCrosswalkRow> CrosswalkRows { get; init; }
        public required string OverviewSummary { get; init; }
    }

    internal static class Ps2MagicEffectReader
    {
        public static Ps2MagicEffectSnapshot Scan(string? masterRoot, string? ffxPs2Root)
        {
            List<Ps2MagicKernelArtifact> kernelArtifacts = BuildKernelArtifacts(masterRoot);
            List<Ps2MagicPackageArtifact> packageArtifacts = BuildPackageArtifacts(ffxPs2Root);
            List<Ps2MagicCrosswalkRow> crosswalkRows = BuildCrosswalkRows(masterRoot, ffxPs2Root);

            return new Ps2MagicEffectSnapshot
            {
                KernelArtifacts = kernelArtifacts,
                PackageArtifacts = packageArtifacts,
                CrosswalkRows = crosswalkRows,
                OverviewSummary = $"{kernelArtifacts.Count} kernel-side artifacts · {packageArtifacts.Count} package/presentation artifacts · {crosswalkRows.Count} crosswalk rules"
            };
        }

        static List<Ps2MagicKernelArtifact> BuildKernelArtifacts(string? masterRoot)
        {
            if (string.IsNullOrWhiteSpace(masterRoot) || !Directory.Exists(masterRoot))
                return [];

            string kernelRoot = Path.Combine(masterRoot, "jppc", "battle", "kernel");
            if (!Directory.Exists(kernelRoot))
                return [];

            List<(string FileName, string Lane, string Summary)> targets =
            [
                ("magic.bin", "kernel", "Core magic table lane."),
                ("monmagic1.bin", "monster-side", "Monster magic selector lane #1."),
                ("monmagic2.bin", "monster-side", "Monster magic selector lane #2."),
                ("command.bin", "command-side", "Command table lane with action-side context."),
                ("a_ability.bin", "ability-side", "Auto-ability sidecar context."),
                ("c_ability.bin", "ability-side", "Command ability sidecar context.")
            ];

            List<Ps2MagicKernelArtifact> artifacts = [];
            foreach ((string fileName, string lane, string summary) in targets)
            {
                string path = Path.Combine(kernelRoot, fileName);
                if (!File.Exists(path))
                    continue;

                artifacts.Add(new Ps2MagicKernelArtifact
                {
                    Title = fileName,
                    FullPath = path,
                    RelativePath = Path.GetRelativePath(masterRoot, path),
                    Lane = lane,
                    Summary = summary,
                    EvidenceLabel = "structural",
                    First16Hex = ReadHeadHex(path),
                    BlockedReason = "This file is contextual, not a proved causal bridge to mag_* or bat_eff."
                });
            }

            return artifacts;
        }

        static List<Ps2MagicPackageArtifact> BuildPackageArtifacts(string? ffxPs2Root)
        {
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return [];

            List<string> directories = Directory
                .EnumerateDirectories(ffxPs2Root, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    return name.StartsWith("mag_", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "bat_eff", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "et_battle", StringComparison.OrdinalIgnoreCase);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<Ps2MagicPackageArtifact> artifacts = [];
            foreach (string directory in directories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                List<string> files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).ToList();
                string name = Path.GetFileName(directory);
                string lane = name.StartsWith("mag_", StringComparison.OrdinalIgnoreCase)
                    ? "mag_package"
                    : (string.Equals(name, "bat_eff", StringComparison.OrdinalIgnoreCase) ? "bat_eff" : "et_battle");

                artifacts.Add(new Ps2MagicPackageArtifact
                {
                    Title = name,
                    FullPath = directory,
                    RelativePath = Path.GetRelativePath(ffxPs2Root, directory),
                    Lane = lane,
                    FileCount = files.Count,
                    BinCount = files.Count(path => string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase)),
                    Tm2Count = files.Count(path => string.Equals(Path.GetExtension(path), ".tm2", StringComparison.OrdinalIgnoreCase)),
                    OmdCount = files.Count(path => string.Equals(Path.GetExtension(path), ".omd", StringComparison.OrdinalIgnoreCase)),
                    Sps2Count = files.Count(path => string.Equals(Path.GetExtension(path), ".sps2", StringComparison.OrdinalIgnoreCase)),
                    OtherCount = files.Count(path =>
                    {
                        string ext = Path.GetExtension(path);
                        return !string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(ext, ".tm2", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(ext, ".omd", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(ext, ".sps2", StringComparison.OrdinalIgnoreCase);
                    }),
                    EvidenceLabel = lane == "bat_eff" ? "proved" : "structural",
                    BlockedReason = lane == "bat_eff"
                        ? "Presentation family is strong, but direct consumer proof remains blocked."
                        : "Package family is visible, but runtime consumer and causal join remain blocked."
                });
            }

            return artifacts;
        }

        static List<Ps2MagicCrosswalkRow> BuildCrosswalkRows(string? masterRoot, string? ffxPs2Root)
        {
            List<Ps2MagicCrosswalkRow> rows =
            [
                new Ps2MagicCrosswalkRow
                {
                    Source = "battle/kernel/magic.bin",
                    Target = "magic namespace",
                    Status = "structural",
                    Note = "cdrom.tmp lists magic/mag_####.bin, but the concrete join field is still blocked."
                },
                new Ps2MagicCrosswalkRow
                {
                    Source = "battle/kernel/monmagic1.bin + monmagic2.bin",
                    Target = "monster-side spell lane",
                    Status = "structural",
                    Note = "Useful context for monster spell selection, not final package causality."
                },
                new Ps2MagicCrosswalkRow
                {
                    Source = "command.bin / ability-side tables",
                    Target = "action-side selectors",
                    Status = "structural",
                    Note = "Command-side context exists, but does not close the package bridge by itself."
                },
                new Ps2MagicCrosswalkRow
                {
                    Source = "mag_* packages",
                    Target = "bat_eff presentation",
                    Status = "blocked",
                    Note = "The kernel -> mag_* -> bat_eff causal bridge is still the hot blocker."
                }
            ];

            string? masterEffectTex = masterRoot == null ? null : FindFile(masterRoot, "et_battle_tex.bin");
            string? ps2EffectTex = ffxPs2Root == null ? null : FindFile(ffxPs2Root, "et_battle_tex.bin");
            if (!string.IsNullOrWhiteSpace(masterEffectTex) && !string.IsNullOrWhiteSpace(ps2EffectTex))
            {
                bool identical = Sha256Prefix(masterEffectTex) == Sha256Prefix(ps2EffectTex);
                rows.Add(new Ps2MagicCrosswalkRow
                {
                    Source = "master/effect/et_battle/et_battle_tex.bin",
                    Target = "yonishi_data/dat_et/bat_eff/et_battle_tex.bin",
                    Status = identical ? "proved" : "guess",
                    Note = identical
                        ? "Observed identical payload across master and bat_eff lanes."
                        : "Name match exists, but payload parity did not hold in this workspace."
                });
            }

            return rows;
        }

        static string? FindFile(string root, string fileName)
        {
            try
            {
                return Directory
                    .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        static string ReadHeadHex(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] buffer = new byte[16];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                return bytesRead == 0 ? "-" : BitConverter.ToString(buffer[..bytesRead]).Replace('-', ' ');
            }
            catch
            {
                return "-";
            }
        }

        static string Sha256Prefix(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] hash = SHA256.HashData(stream);
                return string.Concat(hash.Take(8).Select(value => value.ToString("X2")));
            }
            catch
            {
                return "ERROR";
            }
        }
    }
}
