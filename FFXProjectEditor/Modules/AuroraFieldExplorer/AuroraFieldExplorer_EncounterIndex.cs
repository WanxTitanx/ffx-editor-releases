using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    internal sealed class FieldEncounterFormationRow
    {
        public required string BattleId { get; init; }
        public required int FormationId { get; init; }
        public required int Weight { get; init; }
        public bool HasBattleFile { get; init; }

        public string DisplayLine =>
            string.Format(Strings.U_Au_FormationDisplayLine, BattleId, FormationId, Weight, HasBattleFile ? "" : Strings.U_Au_NoBinSuffix);
    }

    internal sealed class FieldEncounterGroupRow
    {
        public required string MapBucket { get; init; }
        public required int TableIndex { get; init; }
        public required int GroupIndex { get; init; }
        public required int Battlefield { get; init; }
        public required int Danger { get; init; }
        public required int TotalWeight { get; init; }
        public required IReadOnlyList<FieldEncounterFormationRow> Formations { get; init; }
        public bool IsExactFieldMatch { get; init; }

        public string Summary =>
            string.Format(Strings.U_Au_GroupSummary, MapBucket, GroupIndex, Battlefield, Danger, TotalWeight);
    }

    internal sealed class FieldEncounterIndexResult
    {
        public required IReadOnlyList<FieldEncounterGroupRow> ExactGroups { get; init; }
        public required IReadOnlyList<FieldEncounterGroupRow> AreaRelatedGroups { get; init; }
        public BattleMap_Scene? SuggestedArenaScene { get; init; }
        public string? LoadError { get; init; }

        public int TotalGroups => ExactGroups.Count + AreaRelatedGroups.Count;
    }

    /// <summary>Joins a field token to btl.bin encounter tables (read-only). Spatial zones on the map = RE pending.</summary>
    internal static class AuroraFieldExplorer_EncounterIndex
    {
        public static FieldEncounterIndexResult Build(FieldMapRow field, BattleMapCatalog_File? btlmapCatalog)
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                return new FieldEncounterIndexResult
                {
                    ExactGroups = Array.Empty<FieldEncounterGroupRow>(),
                    AreaRelatedGroups = Array.Empty<FieldEncounterGroupRow>(),
                    LoadError = Strings.U_Au_LoadMasterFirst,
                };
            }

            string path = Project_Service.Instance.Path_KernelEncounterTable;
            if (!File.Exists(path))
            {
                return new FieldEncounterIndexResult
                {
                    ExactGroups = Array.Empty<FieldEncounterGroupRow>(),
                    AreaRelatedGroups = Array.Empty<FieldEncounterGroupRow>(),
                    LoadError = string.Format(Strings.U_Au_BtlBinNotFound, path),
                };
            }

            EncounterTable_File enc;
            try
            {
                enc = EncounterTable_File.Read(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                return new FieldEncounterIndexResult
                {
                    ExactGroups = Array.Empty<FieldEncounterGroupRow>(),
                    AreaRelatedGroups = Array.Empty<FieldEncounterGroupRow>(),
                    LoadError = ex.Message,
                };
            }

            var exact = new List<FieldEncounterGroupRow>();
            var related = new List<FieldEncounterGroupRow>();

            for (int ti = 0; ti < enc.Tables.Count; ti++)
            {
                EncounterTable_Entry table = enc.Tables[ti];
                bool exactMap = table.Map.Equals(field.FieldToken, StringComparison.OrdinalIgnoreCase);
                bool areaRelated = !exactMap && IsSameArea(table.Map, field);

                if (!exactMap && !areaRelated)
                    continue;

                for (int gi = 0; gi < table.Groups.Count; gi++)
                {
                    EncounterTable_Group group = table.Groups[gi];
                    var formations = group.Formations
                        .Select(f => new FieldEncounterFormationRow
                        {
                            BattleId = f.BattleId,
                            FormationId = f.FormationId,
                            Weight = f.Weight,
                            HasBattleFile = File.Exists(Project_Service.Instance.GetPathBattle(f.BattleId)),
                        })
                        .ToList();

                    var row = new FieldEncounterGroupRow
                    {
                        MapBucket = table.Map,
                        TableIndex = ti,
                        GroupIndex = gi,
                        Battlefield = group.Battlefield,
                        Danger = group.Danger,
                        TotalWeight = group.TotalWeight,
                        Formations = formations,
                        IsExactFieldMatch = exactMap,
                    };

                    if (exactMap)
                        exact.Add(row);
                    else
                        related.Add(row);
                }
            }

            BattleMap_Scene? arena = null;
            if (btlmapCatalog != null)
                arena = new BattlefieldSceneResolver(btlmapCatalog).ResolvePrimaryScene(field.FieldToken);

            return new FieldEncounterIndexResult
            {
                ExactGroups = exact,
                AreaRelatedGroups = related,
                SuggestedArenaScene = arena,
            };
        }

        static bool IsSameArea(string mapBucket, FieldMapRow field)
        {
            if (string.IsNullOrEmpty(mapBucket))
                return false;
            return mapBucket.StartsWith(field.Area, StringComparison.OrdinalIgnoreCase)
                && !mapBucket.Equals(field.FieldToken, StringComparison.OrdinalIgnoreCase);
        }
    }
}
