using System.Collections.Generic;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

// ── TreasureFieldNameLookup ────────────────────────────────────────────────────────────
// Small static map of the known FFX area slugs (from the field folder parent) to human
// names, so the field list shows "Area · fieldId" instead of raw ids like "bika03".
// Unknown areas fall back to the raw area slug.
// ──────────────────────────────────────────────────────────────────────────────────────
public static class TreasureFieldNameLookup
{
    private static readonly Dictionary<string, string> AreaNames = new()
    {
        ["zanarkand"] = "Zanarkand", ["besaid"] = "Besaid", ["kilika"] = "Kilika",
        ["luca"] = "Luca", ["mihen"] = "Mi'ihen Highroad", ["djose"] = "Djose",
        ["moonflow"] = "Moonflow", ["guado"] = "Guadosalam", ["thunder"] = "Thunder Plains",
        ["macalania"] = "Macalania", ["bikanel"] = "Bikanel", ["calm"] = "Calm Lands",
        ["mtgagazet"] = "Mt. Gagazet", ["zanarkand_ruins"] = "Zanarkand Ruins",
        ["sin"] = "Sin", ["omega"] = "Omega Ruins", ["baaj"] = "Baaj Temple",
        ["bevelle"] = "Bevelle", ["home"] = "Home", ["cactuar"] = "Cactuar Nation",
        ["remiem"] = "Remiem Temple", ["nagi"] = "Nagi Plains", ["bika"] = "Bikanel Island",
    };

    public static string GetDisplayName(string fieldId, string areaId)
    {
        string area = AreaNames.TryGetValue(areaId.ToLowerInvariant(), out string? a) ? a : areaId;
        return $"{area} · {fieldId}";
    }
}
