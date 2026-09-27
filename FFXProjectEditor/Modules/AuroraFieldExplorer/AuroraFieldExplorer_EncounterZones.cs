using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Services;
using System;
using System.IO;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    internal static class AuroraFieldExplorer_EncounterZones
    {
        public static MapoutVpa_EncounterZones.ParseResult TryParse(FieldMapRow field, int? btlGroupCount)
        {
            string? path = ResolveMapoutPath(field);
            if (path == null)
            {
                return new MapoutVpa_EncounterZones.ParseResult
                {
                    Status = MapoutVpa_EncounterZones.MapoutZoneStatus.MissingFile,
                    Family = "missing",
                    FileSize = 0,
                    CoordScale = MapoutVpa_EncounterZones.DefaultCoordScale,
                    Zones = Array.Empty<MapoutVpa_EncounterZones.EncounterZone>(),
                    Note = "mapout.vpa not found under project/ffx_ps2 jppc/map",
                };
            }

            return MapoutVpa_EncounterZones.ParseFile(path, btlGroupCount);
        }

        public static string? ResolveMapoutPath(FieldMapRow field)
        {
            string rel = Path.Combine("jppc", "map", field.Area, field.FieldToken, "bin", "mapout.vpa");

            if (Project_Service.Instance.IsProjectLoaded)
            {
                string fromProject = Path.Combine(Project_Service.Instance.ProjectPath!, rel);
                if (File.Exists(fromProject))
                    return fromProject;
            }

            string? ps2 = Project_Service.Instance.Path_FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ps2))
            {
                string fromPs2 = Path.Combine(ps2, "ffx", "master", rel);
                if (File.Exists(fromPs2))
                    return fromPs2;
            }

            return null;
        }
    }
}
