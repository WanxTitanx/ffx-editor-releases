using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌙 BIANCA — read-only quality snapshot for a scene already exported to the MapViewer tree.
    /// Parses the PhyreMapExportLab <c>material-slot-analysis.json</c> (when present) so Aurora can surface
    /// texture bind coverage and pick the best glTF variant without re-decoding .phyre bytes.
    /// </summary>
    public sealed class BattleMap_ExportQuality
    {
        /// <summary>Preferred Aurora glTF suffix: vertex-color variant binds per-vertex Color on unbound shader
        /// surfaces instead of garish debug pastels.</summary>
        public const string PreferredGltfSuffix = ".static-textured-phyre-slots-vertex-color.gltf";

        public const string FallbackGltfSuffix = ".static-textured-phyre-slots.gltf";

        public required int SubmeshCount { get; init; }
        public required int BoundSubmeshCount { get; init; }
        public required int UnboundSubmeshCount { get; init; }
        public required bool HasVertexColorVariant { get; init; }
        public required bool HasPlainVariant { get; init; }
        public IReadOnlyList<BattleMap_TextureBindBreakdownRow> Breakdown { get; init; } =
            Array.Empty<BattleMap_TextureBindBreakdownRow>();

        public double BindRatio => SubmeshCount > 0 ? BoundSubmeshCount / (double)SubmeshCount : 0;

        public string QualityLabel =>
            SubmeshCount <= 0
                ? Strings.U_Bb_BattleMapNoTextureAnalysis
                : string.Format(Strings.U_Bb_BattleMapBoundSubmeshes, BoundSubmeshCount, SubmeshCount, BindRatio * 100)
                  + (HasVertexColorVariant ? " · glTF vertex-color" : "");

        /// <summary>Human-readable breakdown of unbound/bound families for Aurora UI.</summary>
        public string BreakdownLabel
        {
            get
            {
                if (Breakdown.Count == 0)
                    return UnboundSubmeshCount > 0
                        ? string.Format(Strings.U_Bb_BattleMapNoDds, UnboundSubmeshCount)
                        : Strings.U_Bb_BattleMapFullDds;
                var parts = Breakdown
                    .OrderByDescending(r => r.Count)
                    .Select(r => $"{r.Label}: {r.Count}");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>Resolve the best existing glTF under <paramref name="outputDir"/> for asset
        /// <paramref name="assetId"/> (e.g. <c>btlmap_mihn_mihn00_a</c>). Prefers the .glb container (smaller wire
        /// footprint, faster browser parse — F5) when it exists; falls back to the .gltf variants.</summary>
        public static bool TryResolveBestGltf(string outputDir, string assetId, out string gltfFull, out BattleMap_ExportQuality? quality)
        {
            quality = TryRead(outputDir, assetId);
            string[] suffixes = new[]
            {
                PreferredGltfSuffix.Replace(".gltf", ".glb"),
                FallbackGltfSuffix.Replace(".gltf", ".glb"),
                PreferredGltfSuffix,
                FallbackGltfSuffix,
                ".static-textured.gltf",
                ".static-debug.gltf",
            };
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(outputDir, assetId + suffix);
                if (!File.Exists(candidate))
                    continue;
                gltfFull = candidate;
                return true;
            }

            gltfFull = Path.Combine(outputDir, assetId + FallbackGltfSuffix);
            return false;
        }

        /// <summary>Read export quality from a mounted scene folder. Returns null when the folder or analysis JSON is absent.</summary>
        public static BattleMap_ExportQuality? TryRead(string outputDir, string assetId)
        {
            if (string.IsNullOrWhiteSpace(outputDir) || string.IsNullOrWhiteSpace(assetId))
                return null;

            string analysisPath = Path.Combine(outputDir, assetId + ".material-slot-analysis.json");
            int submesh = 0, bound = 0;
            var breakdown = new List<BattleMap_TextureBindBreakdownRow>();
            if (File.Exists(analysisPath))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(analysisPath));
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("submeshCount", out JsonElement sc))
                        submesh = sc.GetInt32();
                    if (root.TryGetProperty("boundSubmeshCount", out JsonElement bc))
                        bound = bc.GetInt32();

                    if (root.TryGetProperty("statusBreakdown", out JsonElement sb) && sb.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement row in sb.EnumerateArray())
                        {
                            string status = row.TryGetProperty("status", out JsonElement st) ? st.GetString() ?? "" : "";
                            int count = row.TryGetProperty("count", out JsonElement ct) ? ct.GetInt32() : 0;
                            if (count <= 0) continue;
                            breakdown.Add(new BattleMap_TextureBindBreakdownRow(ClassifyStatusLabel(status), count, status));
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            bool hasVc = File.Exists(Path.Combine(outputDir, assetId + PreferredGltfSuffix));
            bool hasPlain = File.Exists(Path.Combine(outputDir, assetId + FallbackGltfSuffix));
            if (submesh <= 0 && !hasVc && !hasPlain)
                return null;

            return new BattleMap_ExportQuality
            {
                SubmeshCount = submesh,
                BoundSubmeshCount = bound,
                UnboundSubmeshCount = Math.Max(0, submesh - bound),
                HasVertexColorVariant = hasVc,
                HasPlainVariant = hasPlain,
                Breakdown = breakdown,
            };
        }

        private static string ClassifyStatusLabel(string status)
        {
            if (string.IsNullOrEmpty(status)) return Strings.F2_other_d0941e68;
            if (status.StartsWith("bound_", StringComparison.Ordinal)) return "DDS bound";
            if (status.Contains("paired_even", StringComparison.Ordinal)) return "DDS (PMesh par)";
            if (status.Contains("local_object_ref", StringComparison.Ordinal)) return "local-ref (vertex-color)";
            if (status.Contains("not_dds", StringComparison.Ordinal) || status.Contains(".fx", StringComparison.Ordinal))
                return Strings.U_Bb_BattleMapShaderFx;
            if (status.Contains("missing_pmesh", StringComparison.Ordinal)) return Strings.U_Bb_BattleMapNoPmesh;
            if (status.Contains("missing_pparameterbuffer", StringComparison.Ordinal) || status.Contains("missing_texture", StringComparison.Ordinal))
                return Strings.U_Bb_BattleMapNoTextureLink;
            if (status.Contains("multiple_", StringComparison.Ordinal)) return Strings.U_Bb_BattleMapMultiTexture;
            return Strings.U_Bb_BattleMapBlocked;
        }
    }

    public sealed record BattleMap_TextureBindBreakdownRow(string Label, int Count, string RawStatus);
}
