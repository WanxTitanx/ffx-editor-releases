// ============================================================================
// FfxSaveSphereGridMigrationAnalyzer — diagnostics: can the save hold the SG asset state?
// PURPOSE : loads a save + a sphere-grid layout/contents pair, compares counts (nodes/links) against the
//           save's known capacity, and reports a compatability policy + findings as JSON/text.
// WHY     : extra nodes/links beyond vanilla capacity CANNOT be persisted in the fixed 25848-byte save
//           without a sidecar/hook or format break — this analyzer makes that explicit before any write.
// EVIDENCE: capacities from FfxSaveSphereGrid (8748 model) + KnownStandardLinkCapacity=881.
// MAINT   : counts_fit_vanilla_save=false does NOT auto-block a write here — it just reports; policy is
//           advisory. Node/link capacity constants must track the editor's real grid writer.
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed record FfxSaveSphereGridMigrationReport(
        string SavePath,
        string SaveFormat,
        string LayoutPath,
        string ContentsPath,
        int SavePayloadBytes,
        string SaveSha256,
        int SaveGridMode,
        string SaveGridModeName,
        int SaveNodeCapacity,
        int SaveActivationCapacity,
        int SaveKnownLinkCapacity,
        int AssetClusters,
        int AssetNodes,
        int AssetLinks,
        string AssetLayoutSha256,
        string AssetContentsSha256,
        int ExtraNodes,
        int ExtraLinks,
        bool CountsFitVanillaSave,
        string Policy,
        string[] Findings);

    public static class FfxSaveSphereGridMigrationAnalyzer
    {
        public const int KnownStandardLinkCapacity = 881;
        public const int GridModeOffset = 15693;

        public static FfxSaveSphereGridMigrationReport Analyze(string savePath, string layoutPath, string contentsPath)
        {
            FfxSaveFile save = FfxSaveFile.Load(savePath);
            SphereGridLayoutFile grid = SphereGrid_File.ReadLayout(layoutPath, contentsPath, Path.GetFileNameWithoutExtension(layoutPath));

            int mode = save.Core.Data[GridModeOffset];
            int saveNodeCapacity = FfxSaveSphereGrid.NodeCount;
            int saveActivationCapacity = FfxSaveSphereGrid.ActivationLast - FfxSaveSphereGrid.ActivationBase + 1;
            int extraNodes = Math.Max(0, grid.NodeCount - saveNodeCapacity);
            int extraLinks = Math.Max(0, grid.LinkCount - KnownStandardLinkCapacity);
            bool countsFit = grid.NodeCount <= saveNodeCapacity && grid.LinkCount <= KnownStandardLinkCapacity;

            string policy = countsFit
                ? "compatible-counts: vanilla save can represent the known node/link state slices; topology changes still need RT2."
                : "requires-sidecar-or-format-break: vanilla save cannot represent all nodes/links from the asset.";

            string[] findings =
            {
                $"save_grid_mode={mode} ({GridModeName(mode)})",
                $"save_node_capacity={saveNodeCapacity}",
                $"save_activation_capacity={saveActivationCapacity}",
                $"known_standard_link_capacity={KnownStandardLinkCapacity}",
                $"asset_counts={grid.ClusterCount}/{grid.NodeCount}/{grid.LinkCount}",
                $"extra_nodes={extraNodes}",
                $"extra_links={extraLinks}",
                countsFit
                    ? "counts_fit_vanilla_save=true"
                    : "counts_fit_vanilla_save=false; do not write this state into the fixed 25848-byte save without sidecar/hook or format break",
            };

            return new FfxSaveSphereGridMigrationReport(
                savePath,
                save.Format.ToString(),
                layoutPath,
                contentsPath,
                FfxSaveCore.DataSize,
                FfxSaveHash.Sha256Hex(save.Core.Data),
                mode,
                GridModeName(mode),
                saveNodeCapacity,
                saveActivationCapacity,
                KnownStandardLinkCapacity,
                grid.ClusterCount,
                grid.NodeCount,
                grid.LinkCount,
                FfxSaveHash.Sha256Hex(grid.RawLayoutBytes),
                FfxSaveHash.Sha256Hex(grid.RawContentsBytes),
                extraNodes,
                extraLinks,
                countsFit,
                policy,
                findings);
        }

        public static string ToJson(FfxSaveSphereGridMigrationReport report) =>
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        public static string ToText(FfxSaveSphereGridMigrationReport report) =>
            string.Join(Environment.NewLine, new[]
            {
                "FFX Sphere Grid save migration analyzer",
                $"save: {report.SavePath}",
                $"save_format: {report.SaveFormat}",
                $"save_sha256: {report.SaveSha256}",
                $"asset: {report.LayoutPath} + {report.ContentsPath}",
                $"asset_sha256: layout={report.AssetLayoutSha256} contents={report.AssetContentsSha256}",
                $"save_grid_mode: {report.SaveGridMode} ({report.SaveGridModeName})",
                $"save_capacity: nodes={report.SaveNodeCapacity} activationBytes={report.SaveActivationCapacity} knownStandardLinks={report.SaveKnownLinkCapacity}",
                $"asset_counts: clusters={report.AssetClusters} nodes={report.AssetNodes} links={report.AssetLinks}",
                $"delta: extraNodes={report.ExtraNodes} extraLinks={report.ExtraLinks}",
                $"policy: {report.Policy}",
                "findings:",
            }.Concat(report.Findings.Select(f => $"  - {f}")));

        private static string GridModeName(int mode) => mode switch
        {
            0 => "Original",
            1 => "Standard",
            2 => "Expert",
            _ => "Unknown",
        };

    }
}
