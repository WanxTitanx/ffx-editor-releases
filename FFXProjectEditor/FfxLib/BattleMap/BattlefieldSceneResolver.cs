using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// Resolves an EncounterTable entry/group to a 🌙 BIANCA battle SCENE (layer 1 → the bridge the Aurora
    /// Chamber needs to put the right arena under a formation).
    ///
    /// PROVEN bridge (docs/reverse/FFX_BATTLEFIELD_SCENE_BRIDGE_2026-06-05.md): the EncounterTable header's own
    /// 6-char <c>map</c> field IS <c>area(4)+NN(2)</c> (e.g. "azit03") and resolves 1:1 to the scene folder
    /// <c>&lt;area&gt;/&lt;area&gt;NN_&lt;variant&gt;</c>. The <c>battlefield</c> u16 is NOT a scene selector — RE
    /// of the kernel encounter table showed it is a GLOBAL arena/parameter id (high-byte bucketed, shared across
    /// unrelated maps: bf=1061 spans nagi/test/tori/zzzz). It is carried here only as <see cref="ArenaId"/>
    /// metadata; what arena/variant it ultimately selects in-engine is an open RE item.
    ///
    /// Variant suffix (_a/_b/_c) cannot be disambiguated from encounter data alone (story/script driven), so the
    /// resolver returns ALL variant scenes and a primary pick (largest real geometry → the playable arena, never
    /// a stub/2d slice). Read-only; no asset access (operates over an already-scanned catalog + decoded table).
    /// </summary>
    public sealed class BattlefieldSceneResolver
    {
        private readonly BattleMapCatalog_File _catalog;

        public BattlefieldSceneResolver(BattleMapCatalog_File catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>All variant scenes shipped for a map key (e.g. "mihn00"); empty if the map has no HD scene.</summary>
        public IReadOnlyList<BattleMap_Scene> ResolveScenes(string mapKey) =>
            string.IsNullOrWhiteSpace(mapKey) ? Array.Empty<BattleMap_Scene>() : _catalog.ScenesForMap(mapKey.Trim());

        /// <summary>The single best scene for a map key: the variant with the largest real geometry (the playable
        /// arena), preferring non-stub. Null if the map has no shipped HD scene.</summary>
        public BattleMap_Scene? ResolvePrimaryScene(string mapKey)
        {
            IReadOnlyList<BattleMap_Scene> scenes = ResolveScenes(mapKey);
            if (scenes.Count == 0) return null;
            return scenes
                .OrderByDescending(s => s.IsStub ? 0 : 1)         // real geometry first
                .ThenByDescending(s => s.PrimaryDaeSize)          // biggest arena
                .ThenBy(s => s.Variant, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        /// <summary>Resolve one decoded encounter group (carries its battlefield/arena id).</summary>
        public BattlefieldSceneMatch Resolve(EncounterTable_Entry table, EncounterTable_Group group)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(group);

            IReadOnlyList<BattleMap_Scene> scenes = ResolveScenes(table.Map);
            BattleMap_Scene? primary = ResolvePrimaryScene(table.Map);

            return new BattlefieldSceneMatch
            {
                MapKey = table.Map,
                ArenaId = group.Battlefield,
                TableIndex = table.TableIndex,
                GroupIndex = group.GroupIndex,
                Scenes = scenes,
                PrimaryScene = primary,
                Resolved = primary != null,
            };
        }

        /// <summary>Resolve every (table, group) in a decoded encounter file against the catalog. Useful for the
        /// RT0 gate and for reporting HD coverage / orphans. One row per group.</summary>
        public IReadOnlyList<BattlefieldSceneMatch> ResolveAll(EncounterTable_File encounter)
        {
            ArgumentNullException.ThrowIfNull(encounter);
            List<BattlefieldSceneMatch> rows = new();
            foreach (EncounterTable_Entry t in encounter.Tables)
                foreach (EncounterTable_Group g in t.Groups)
                    rows.Add(Resolve(t, g));
            return rows;
        }

        /// <summary>Catalog scenes that no encounter map points at (story/boss-only arenas, e.g. bika04/grid00/
        /// nagi03/sfia00). These are EXPECTED unreferenced scenes, not errors.</summary>
        public IReadOnlyList<BattleMap_Scene> OrphanScenes(EncounterTable_File encounter)
        {
            ArgumentNullException.ThrowIfNull(encounter);
            HashSet<string> referenced = encounter.Tables
                .Select(t => t.Map)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _catalog.Scenes
                .Where(s => !referenced.Contains(s.MapKey))
                .OrderBy(s => s.SceneId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public sealed class BattlefieldSceneMatch
    {
        public required string MapKey { get; init; }
        public required int ArenaId { get; init; }            // the encounter group's battlefield u16 (global arena id)
        public required int TableIndex { get; init; }
        public required int GroupIndex { get; init; }
        public required IReadOnlyList<BattleMap_Scene> Scenes { get; init; }
        public BattleMap_Scene? PrimaryScene { get; init; }
        public required bool Resolved { get; init; }

        public override string ToString() =>
            $"{MapKey} (arena {ArenaId}) -> {(Resolved ? PrimaryScene!.SceneId : "<no HD scene>")}" +
            (Scenes.Count > 1 ? $" [+{Scenes.Count - 1} variant(s)]" : "");
    }
}
