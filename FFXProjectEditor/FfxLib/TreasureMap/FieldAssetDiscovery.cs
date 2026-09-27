using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── FieldAssetDiscovery ─────────────────────────────────────────────────────────────────
// Maps the master folder's field data into FieldMapAsset records:
//   mapRoot  = <master>/jppc/map/{area}/{field}/bin/mapout.vpa
//   eventRoot= <master>/jppc/event/obj/.../{field}*.ebp
// Field id is the 6-char basename of the map folder; events are grouped by that field id.
// Excludes cn_* and psv* events (cinematics / PS2 worker variants that are not real chests).
// ──────────────────────────────────────────────────────────────────────────────────────
public sealed record FieldMapAsset(
    string FieldId, string AreaId, string MapPath, IReadOnlyList<string> EventPaths)
{
    public bool HasEvents => EventPaths.Count > 0;
}

public static class FieldAssetDiscovery
{
    public static IReadOnlyList<FieldMapAsset> ScanMaster(string masterPath)
    {
        string root = System.IO.Path.GetFullPath(masterPath);
        string mapRoot = Path.Combine(root, "jppc", "map");
        string eventRoot = Path.Combine(root, "jppc", "event", "obj");
        if (!Directory.Exists(mapRoot)) throw new DirectoryNotFoundException($"Missing field-map folder: {mapRoot}");
        if (!Directory.Exists(eventRoot)) throw new DirectoryNotFoundException($"Missing field-event folder: {eventRoot}");

        Dictionary<string, string[]> eventsByField = Directory
            .EnumerateFiles(eventRoot, "*.ebp", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).StartsWith("cn_", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(p).StartsWith("psv", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => ToFieldId(Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length == 6)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(mapRoot, "mapout.vpa", SearchOption.AllDirectories)
            .Select(p =>
            {
                DirectoryInfo? fieldDir = Directory.GetParent(Path.GetDirectoryName(p)!);
                string fieldId = fieldDir?.Name ?? "";
                string areaId = fieldDir?.Parent?.Name ?? "";
                eventsByField.TryGetValue(fieldId, out string[]? evPaths);
                return new FieldMapAsset(fieldId, areaId, System.IO.Path.GetFullPath(p), evPaths ?? []);
            })
            .Where(a => a.FieldId.Length == 6)
            .OrderBy(a => a.FieldId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ToFieldId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "";
        string n = eventId.Trim().ToLowerInvariant();
        return n.Length >= 6 ? n[..6] : n;
    }
}
