using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

/// <summary>F3.5 — grava o sidecar de edição do Aurora (modo edição): JSON em
/// <c>data/FinalFantasyX/edits/&lt;encId&gt;.json</c> com deltas por slot que o noclip
/// aplica no <c>setUpMonsters</c> do battle-preview. Formato:
/// <c>{ "actors": { "&lt;slot&gt;": { "position": [dx,dy,dz], "heading": rad, "scale": mult } } }</c>.</summary>
public static class SidecarEditsWriter
{
    public static string ApplySlot(string? existingJson, int slot, double? dx, double? dy, double? dz,
        double? heading, double? scale)
    {
        JsonObject root = JsonNode.Parse(existingJson ?? "{}") as JsonObject ?? new JsonObject();
        JsonObject actors = root["actors"] as JsonObject ?? new JsonObject();
        string slotKey = slot.ToString(CultureInfo.InvariantCulture);
        JsonObject s = actors[slotKey] as JsonObject ?? new JsonObject();

        if (dx.HasValue && dy.HasValue && dz.HasValue)
            s["position"] = new JsonArray(dx.Value, dy.Value, dz.Value);
        if (heading.HasValue)
            s["heading"] = heading.Value;
        if (scale.HasValue)
            s["scale"] = scale.Value;

        actors[slotKey] = s;
        root["actors"] = actors;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
