using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MapoutZoneLab <mapout.vpa> [btl.bin] [fieldToken]");
    return 1;
}

string mapoutPath = args[0];
int? groupCount = null;
if (args.Length >= 3)
{
    string btlPath = args[1];
    string token = args[2];
    if (File.Exists(btlPath))
    {
        EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(btlPath));
        EncounterTable_Entry? table = enc.Tables.FirstOrDefault(t =>
            t.Map.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (table != null)
            groupCount = table.GroupCount;
    }
}

MapoutVpa_EncounterZones.ParseResult result = MapoutVpa_EncounterZones.ParseFile(mapoutPath, groupCount);
var payload = new
{
    mapout = mapoutPath,
    fieldToken = args.Length >= 3 ? args[2] : null,
    btlGroupCount = groupCount,
    status = result.Status.ToString(),
    family = result.Family,
    fileSize = result.FileSize,
    note = result.Note,
    zoneCount = result.Zones.Count,
    zones = result.Zones.Select(z => new
    {
        entryKey = z.EntryKey,
        tag = z.Tag,
        groupIndex = z.GroupIndex,
        linkConfidence = z.LinkConfidence,
        bounds = new { z.MinX, z.MaxX, z.MinZ, z.MaxZ },
        polygonCount = z.Polygons.Count,
        firstPolygonVerts = z.Polygons.FirstOrDefault()?.Vertices.Count ?? 0,
    }),
};

Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
return result.Zones.Count > 0 ? 0 : 2;
