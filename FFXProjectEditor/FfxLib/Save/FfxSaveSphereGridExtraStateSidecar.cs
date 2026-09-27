// ============================================================================
// FfxSaveSphereGridExtraStateSidecar — JSON schema for extra sphere-grid state (sidecar I/O)
// PURPOSE : DTOs for the profile-keyed extra node/link state persisted alongside a save.
// WHY     : extra SG nodes/links can't fit vanilla capacity; this sidecar carries them keyed by
//           save+layout SHA-256 so a restored save re-applies exactly.
// EVIDENCE: schema vN; consumer FfxSaveSphereGridExtraStateSidecarIO.
// MAINT   : JSON property names are the wire format — rename with a schema bump, never silently.
// ============================================================================
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed record FfxSaveSphereGridExtraStateSidecar(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("profile_key")] string ProfileKey,
        [property: JsonPropertyName("save_sha256")] string SaveSha256,
        [property: JsonPropertyName("layout_sha256")] string LayoutSha256,
        [property: JsonPropertyName("contents_sha256")] string ContentsSha256,
        [property: JsonPropertyName("grid_kind")] string GridKind,
        [property: JsonPropertyName("vanilla_capacity")] FfxSaveSphereGridVanillaCapacity VanillaCapacity,
        [property: JsonPropertyName("asset_counts")] FfxSaveSphereGridAssetCounts AssetCounts,
        [property: JsonPropertyName("extra_nodes")] List<FfxSaveSphereGridExtraNode> ExtraNodes,
        [property: JsonPropertyName("extra_links")] List<FfxSaveSphereGridExtraLink> ExtraLinks,
        [property: JsonPropertyName("cursor_nodes")] int[]? CursorNodes = null,
        [property: JsonPropertyName("notes")] string[]? Notes = null);

    public sealed record FfxSaveSphereGridVanillaCapacity(
        [property: JsonPropertyName("nodes")] int Nodes,
        [property: JsonPropertyName("links")] int Links,
        [property: JsonPropertyName("activation_bytes")] int ActivationBytes);

    public sealed record FfxSaveSphereGridAssetCounts(
        [property: JsonPropertyName("clusters")] int Clusters,
        [property: JsonPropertyName("nodes")] int Nodes,
        [property: JsonPropertyName("links")] int Links);

    public sealed record FfxSaveSphereGridExtraNode(
        [property: JsonPropertyName("node_id")] int NodeId,
        [property: JsonPropertyName("content")] int Content,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("activated_mask")] int? ActivatedMask = null);

    public sealed record FfxSaveSphereGridExtraLink(
        [property: JsonPropertyName("link_id")] int LinkId,
        [property: JsonPropertyName("state")] int State,
        [property: JsonPropertyName("node1")] int? Node1 = null,
        [property: JsonPropertyName("node2")] int? Node2 = null,
        [property: JsonPropertyName("anchor")] int? Anchor = null);
}
