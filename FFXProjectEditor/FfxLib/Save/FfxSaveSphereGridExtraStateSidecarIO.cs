// ============================================================================
// FfxSaveSphereGridExtraStateSidecarIO — persist extra sphere-grid state as a JSON sidecar
// PURPOSE : reads/writes the extra-node/link sphere-grid sidecar (validated JSON) at a deterministic path
//           keyed by (profile_key, save_sha256-prefix). Load API rejects invalid sidecars atomically.
// WHY     : vanilla 25848-byte save can't hold extra SG nodes/links — a sidecar beside the save carries them,
//           keyed by exact save hash so re-load/re-apply is deterministic.
// EVIDENCE: SchemaVersion=1; Path pattern "mods/Spira Reforge/save-sidecars/<profile>/<sha16>.sphere-grid-extra.json".
// MAINT   : bump SchemaVersion on ANY breaking change to the DTO shape; Save() writes via .tmp + atomic move
//           and refuses writes on validation errors. AllowedGridKinds matches FfxSaveSphereGridMigrationAnalyzer.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Save
{
    public static class FfxSaveSphereGridExtraStateSidecarIO
    {
        public const int SchemaVersion = 1;
        public const int DefaultVanillaNodeCapacity = FfxSaveSphereGrid.NodeCount;
        public const int DefaultVanillaLinkCapacity = FfxSaveSphereGridMigrationAnalyzer.KnownStandardLinkCapacity;
        public const int ContentsPayloadOffset = 0x08;

        static readonly string[] AllowedGridKinds = ["Original", "Standard", "Expert", "Unknown"];

        static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string DefaultPath(string profileKey, string saveSha256)
        {
            if (string.IsNullOrWhiteSpace(profileKey))
                throw new ArgumentException("profile_key is required.", nameof(profileKey));
            if (!FfxSaveHash.IsSha256Hex(saveSha256))
                throw new ArgumentException("save_sha256 must be a 64-char lowercase hex digest.", nameof(saveSha256));

            string safeProfile = SanitizePathSegment(profileKey);
            string prefix = saveSha256[..16];
            return Path.Combine("mods", "Spira Reforge", "save-sidecars", safeProfile, $"{prefix}.sphere-grid-extra.json");
        }

        public static FfxSaveSphereGridExtraStateSidecar Load(string path)
        {
            string json = File.ReadAllText(path);
            FfxSaveSphereGridExtraStateSidecar? sidecar =
                JsonSerializer.Deserialize<FfxSaveSphereGridExtraStateSidecar>(json, JsonReadOptions)
                ?? throw new InvalidDataException("sidecar JSON deserialized to null.");

            IReadOnlyList<string> errors = Validate(sidecar);
            if (errors.Count > 0)
                throw new InvalidDataException($"sidecar validation failed: {string.Join("; ", errors)}");

            return sidecar;
        }

        public static void Save(string path, FfxSaveSphereGridExtraStateSidecar sidecar)
        {
            IReadOnlyList<string> errors = Validate(sidecar);
            if (errors.Count > 0)
                throw new InvalidOperationException($"refusing to save invalid sidecar: {string.Join("; ", errors)}");

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(sidecar, JsonWriteOptions);
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, path, overwrite: true);
        }

        public static IReadOnlyList<string> Validate(FfxSaveSphereGridExtraStateSidecar sidecar)
        {
            List<string> errors = [];

            if (sidecar.Schema != SchemaVersion)
                errors.Add($"schema must be {SchemaVersion}.");

            if (string.IsNullOrWhiteSpace(sidecar.ProfileKey))
                errors.Add("profile_key is required.");

            ValidateHash(sidecar.SaveSha256, "save_sha256", errors);
            ValidateHash(sidecar.LayoutSha256, "layout_sha256", errors);
            ValidateHash(sidecar.ContentsSha256, "contents_sha256", errors);

            if (!AllowedGridKinds.Contains(sidecar.GridKind))
                errors.Add($"grid_kind must be one of: {string.Join(", ", AllowedGridKinds)}.");

            if (sidecar.VanillaCapacity is null)
            {
                errors.Add("vanilla_capacity is required.");
            }
            else
            {
                if (sidecar.VanillaCapacity.Nodes < 0)
                    errors.Add("vanilla_capacity.nodes must be >= 0.");
                if (sidecar.VanillaCapacity.Links < 0)
                    errors.Add("vanilla_capacity.links must be >= 0.");
                if (sidecar.VanillaCapacity.ActivationBytes < 0)
                    errors.Add("vanilla_capacity.activation_bytes must be >= 0.");
            }

            if (sidecar.AssetCounts is null)
            {
                errors.Add("asset_counts is required.");
            }
            else
            {
                if (sidecar.AssetCounts.Clusters < 0)
                    errors.Add("asset_counts.clusters must be >= 0.");
                if (sidecar.AssetCounts.Nodes < 0)
                    errors.Add("asset_counts.nodes must be >= 0.");
                if (sidecar.AssetCounts.Links < 0)
                    errors.Add("asset_counts.links must be >= 0.");
            }

            if (sidecar.ExtraNodes is null)
            {
                errors.Add("extra_nodes is required.");
            }
            else
            {
                HashSet<int> seenNodeIds = [];
                foreach (FfxSaveSphereGridExtraNode node in sidecar.ExtraNodes)
                {
                    if (node.NodeId < 0)
                        errors.Add($"extra_nodes node_id must be >= 0 (got {node.NodeId}).");
                    else if (!seenNodeIds.Add(node.NodeId))
                        errors.Add($"duplicate extra_nodes node_id {node.NodeId}.");

                    if (node.Content is < 0 or > 255)
                        errors.Add($"extra_nodes[{node.NodeId}].content must be 0..255.");
                    if (node.Status is < 0 or > 255)
                        errors.Add($"extra_nodes[{node.NodeId}].status must be 0..255.");
                    if (node.ActivatedMask is < 0 or > 127)
                        errors.Add($"extra_nodes[{node.NodeId}].activated_mask must be 0..127 when present.");
                }
            }

            if (sidecar.ExtraLinks is null)
            {
                errors.Add("extra_links is required.");
            }
            else
            {
                HashSet<int> seenLinkIds = [];
                foreach (FfxSaveSphereGridExtraLink link in sidecar.ExtraLinks)
                {
                    if (link.LinkId < 0)
                        errors.Add($"extra_links link_id must be >= 0 (got {link.LinkId}).");
                    else if (!seenLinkIds.Add(link.LinkId))
                        errors.Add($"duplicate extra_links link_id {link.LinkId}.");

                    if (link.State is < 0 or > 255)
                        errors.Add($"extra_links[{link.LinkId}].state must be 0..255.");
                    if (link.Node1 is < 0)
                        errors.Add($"extra_links[{link.LinkId}].node1 must be >= 0 when present.");
                    if (link.Node2 is < 0)
                        errors.Add($"extra_links[{link.LinkId}].node2 must be >= 0 when present.");
                    if (link.Anchor is < 0)
                        errors.Add($"extra_links[{link.LinkId}].anchor must be >= 0 when present.");
                }
            }

            if (sidecar.CursorNodes is not null)
            {
                if (sidecar.CursorNodes.Length != 7)
                    errors.Add("cursor_nodes must contain exactly 7 integers when present.");
                else if (sidecar.CursorNodes.Any(v => v < 0))
                    errors.Add("cursor_nodes values must be >= 0.");
            }

            return errors;
        }

        public static bool Matches(
            FfxSaveSphereGridExtraStateSidecar sidecar,
            string saveSha256,
            string layoutSha256,
            string contentsSha256) =>
            string.Equals(sidecar.SaveSha256, saveSha256, StringComparison.Ordinal)
            && string.Equals(sidecar.LayoutSha256, layoutSha256, StringComparison.Ordinal)
            && string.Equals(sidecar.ContentsSha256, contentsSha256, StringComparison.Ordinal);

        public static FfxSaveSphereGridExtraStateSidecar BuildEmptyFromAnalyzer(FfxSaveSphereGridMigrationReport report)
        {
            string profileKey = DeriveProfileKey(report.SavePath);
            byte[] contentsBytes = File.ReadAllBytes(report.ContentsPath);

            List<FfxSaveSphereGridExtraNode> extraNodes = DiffExtraNodes(report.AssetNodes)
                .Select(nodeId =>
                {
                    int content = ReadContentsByte(contentsBytes, nodeId);
                    return new FfxSaveSphereGridExtraNode(nodeId, content, 0);
                })
                .ToList();

            List<FfxSaveSphereGridExtraLink> extraLinks = DiffExtraLinks(report.AssetLinks)
                .Select(linkId => new FfxSaveSphereGridExtraLink(linkId, 0))
                .ToList();

            return new FfxSaveSphereGridExtraStateSidecar(
                SchemaVersion,
                profileKey,
                report.SaveSha256,
                report.AssetLayoutSha256,
                report.AssetContentsSha256,
                report.SaveGridModeName,
                new FfxSaveSphereGridVanillaCapacity(
                    report.SaveNodeCapacity,
                    report.SaveKnownLinkCapacity,
                    report.SaveActivationCapacity),
                new FfxSaveSphereGridAssetCounts(
                    report.AssetClusters,
                    report.AssetNodes,
                    report.AssetLinks),
                extraNodes,
                extraLinks);
        }

        public static void ApplyDefaultsFromContents(
            FfxSaveSphereGridExtraStateSidecar sidecar,
            byte[] dat1xBytes,
            int nodeId)
        {
            if (sidecar.ExtraNodes is null)
                throw new InvalidOperationException("sidecar.extra_nodes is null.");

            int content = ReadContentsByte(dat1xBytes, nodeId);
            for (int i = 0; i < sidecar.ExtraNodes.Count; i++)
            {
                if (sidecar.ExtraNodes[i].NodeId != nodeId)
                    continue;

                FfxSaveSphereGridExtraNode existing = sidecar.ExtraNodes[i];
                sidecar.ExtraNodes[i] = existing with { Content = content };
                return;
            }

            sidecar.ExtraNodes.Add(new FfxSaveSphereGridExtraNode(nodeId, content, 0));
        }

        public static IReadOnlyList<int> DiffExtraNodes(int assetNodeCount, int vanillaCapacity = DefaultVanillaNodeCapacity)
        {
            if (assetNodeCount <= vanillaCapacity)
                return Array.Empty<int>();

            return Enumerable.Range(vanillaCapacity, assetNodeCount - vanillaCapacity).ToArray();
        }

        public static IReadOnlyList<int> DiffExtraLinks(int assetLinkCount, int vanillaCapacity = DefaultVanillaLinkCapacity)
        {
            if (assetLinkCount <= vanillaCapacity)
                return Array.Empty<int>();

            return Enumerable.Range(vanillaCapacity, assetLinkCount - vanillaCapacity).ToArray();
        }

        public static int ReadContentsByte(byte[] dat1xBytes, int nodeId)
        {
            int index = ContentsPayloadOffset + nodeId;
            if (index < 0 || index >= dat1xBytes.Length)
                return 0xFF;

            return dat1xBytes[index];
        }

        public static string DeriveProfileKey(string savePath)
        {
            string fullPath = Path.GetFullPath(savePath);
            return FfxSaveHash.Sha256Hex(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()))[..16];
        }

        static void ValidateHash(string value, string fieldName, List<string> errors)
        {
            if (!FfxSaveHash.IsSha256Hex(value))
                errors.Add($"{fieldName} must be a 64-char lowercase hex digest.");
        }

        static string SanitizePathSegment(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new(value.Length);
            foreach (char ch in value)
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            return sb.ToString();
        }
    }
}
