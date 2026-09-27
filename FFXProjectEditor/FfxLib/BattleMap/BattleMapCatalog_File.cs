using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌙 BIANCA — Battle-map Indexer And Navigation Coordinate Atlas (layer 1 of the Aurora Chamber).
    ///
    /// READ-ONLY catalog of the HD battle SCENES under <c>ps3data/btlmap</c>. Each btlmap "scene" is a folder
    /// <c>&lt;area&gt;/&lt;area&gt;NN_&lt;variant&gt;</c> (e.g. <c>azit/azit03_a</c>) carrying the SAME Phyre geometry
    /// format as the field maps — a primary model at <c>&lt;scene&gt;/mdl/d3d11/&lt;scene&gt;.dae.phyre</c> plus an
    /// <c>.ahwin32</c> manifest and <c>.dds.phyre</c> textures. This reader only ENUMERATES the tree (folder walk +
    /// <see cref="FileInfo.Length"/>); it never opens or decodes a single .phyre byte and never mutates anything.
    ///
    /// After Aurora mounts a scene, <see cref="BattleMap_ExportQuality"/> reads the export lab's
    /// <c>material-slot-analysis.json</c> to report texture bind coverage and pick the vertex-color glTF variant.
    ///
    /// Intentionally Avalonia-free and dependency-free (only <c>System.*</c>) so it can be linked into a headless
    /// gate (RuntimeTools/BiancaCatalogLab) exactly the way FormationSlotLab links Battle_File. Mirrors the
    /// <c>EncounterTable_File.Read(byte[])</c> idiom (sealed immutable records, static factory) but scans the
    /// filesystem the way <c>MapAreaResolver.Resolve</c> does.
    ///
    /// Pairs with <see cref="BattlefieldSceneResolver"/>: the EncounterTable header's 6-char <c>map</c> field
    /// (<c>area(4)+NN(2)</c>, e.g. "azit03") is the PROVEN bridge to a scene — NOT the <c>battlefield</c> u16,
    /// which RE proved to be a global arena id shared across unrelated maps
    /// (see docs/reverse/FFX_BATTLEFIELD_SCENE_BRIDGE_2026-06-05.md).
    /// </summary>
    public sealed class BattleMapCatalog_File
    {
        /// <summary>
        /// Portable default HD btlmap root. Headless labs link this file without the editor services,
        /// so resolution stays System-only: explicit environment overrides first, then an optional
        /// payload beside the executable. An empty value means the capability is not configured.
        /// </summary>
        public static string DefaultBtlmapRoot => ResolveDefaultBtlmapRoot() ?? string.Empty;

        public static string? ResolveDefaultBtlmapRoot()
        {
            string? direct = ExistingDirectory(Environment.GetEnvironmentVariable("FFX_BTLMAP_ROOT"));
            if (direct != null)
                return direct;

            string? ps3Data = ExistingDirectory(Environment.GetEnvironmentVariable("FFX_PS3DATA_ROOT"));
            if (ps3Data != null)
            {
                string? fromPs3Data = ExistingDirectory(Path.Combine(ps3Data, "btlmap"));
                if (fromPs3Data != null)
                    return fromPs3Data;
            }

            string? extracted = ExistingDirectory(Environment.GetEnvironmentVariable("FFX_EXTRACTED_ROOT"));
            if (extracted != null)
            {
                string? fromExtraction = ExistingDirectory(Path.Combine(
                    extracted, "ffx_data", "gamedata", "ps3data", "btlmap"));
                if (fromExtraction != null)
                    return fromExtraction;
            }

            return ExistingDirectory(Path.Combine(AppContext.BaseDirectory, "ps3data", "btlmap"));
        }

        // leaf = area(4 letters) + index(2 digits) + '_' + variant. e.g. azit03_a, bika02_b, nagi05_c.
        private static readonly Regex SceneLeafPattern =
            new(@"^(?<area>[a-z]{4})(?<idx>\d{2})_(?<variant>[a-z0-9]+)$", RegexOptions.Compiled);

        public required string Root { get; init; }
        public required IReadOnlyList<BattleMap_Area> Areas { get; init; }
        /// <summary>Flat list of every scene across all areas, ordered by <see cref="BattleMap_Scene.SceneId"/>.</summary>
        public required IReadOnlyList<BattleMap_Scene> Scenes { get; init; }
        public int AreaCount => Areas.Count;
        public int SceneCount => Scenes.Count;
        public long TotalPrimaryBytes => Scenes.Sum(s => s.PrimaryDaeSize);

        /// <summary>Scan a btlmap root into an immutable catalog. Throws if the root does not exist. Read-only.</summary>
        public static BattleMapCatalog_File Scan(string btlmapRoot)
        {
            if (string.IsNullOrWhiteSpace(btlmapRoot))
                throw new ArgumentException(
                    "btlmap root is unavailable; configure FFX_BTLMAP_ROOT or FFX_PS3DATA_ROOT.",
                    nameof(btlmapRoot));
            if (!Directory.Exists(btlmapRoot))
                throw new DirectoryNotFoundException($"btlmap root not found: {btlmapRoot}");

            string root = Path.GetFullPath(btlmapRoot);
            List<BattleMap_Area> areas = new();

            foreach (string areaDir in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string areaCode = Path.GetFileName(areaDir);
                List<BattleMap_Scene> scenes = new();

                foreach (string sceneDir in Directory.EnumerateDirectories(areaDir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string sceneId = Path.GetFileName(sceneDir);
                    Match m = SceneLeafPattern.Match(sceneId);
                    if (!m.Success)
                        continue; // non-scene helper folder; the gate logs these as skipped, not as scenes.

                    string sceneArea = m.Groups["area"].Value;
                    string idx = m.Groups["idx"].Value;
                    string variant = m.Groups["variant"].Value;
                    string mapKey = sceneArea + idx; // == EncounterTable_Entry.Map (6 chars)

                    string primaryRel = $"{areaCode}/{sceneId}/mdl/d3d11/{sceneId}.dae.phyre";
                    string primaryFull = Path.Combine(sceneDir, "mdl", "d3d11", sceneId + ".dae.phyre");
                    long primarySize = File.Exists(primaryFull) ? new FileInfo(primaryFull).Length : 0L;

                    string twoDFull = Path.Combine(sceneDir, "2d", "mdl", "d3d11", sceneId + ".dae.phyre");
                    bool hasTwoD = File.Exists(twoDFull);
                    string? twoDRel = hasTwoD ? $"{areaCode}/{sceneId}/2d/mdl/d3d11/{sceneId}.dae.phyre" : null;
                    long? twoDSize = hasTwoD ? new FileInfo(twoDFull).Length : (long?)null;

                    int allDaeCount = SafeCount(sceneDir, "*.dae.phyre");

                    scenes.Add(new BattleMap_Scene
                    {
                        AreaCode = areaCode,
                        SceneId = sceneId,
                        MapKey = mapKey,
                        Variant = variant,
                        MapIndex = int.TryParse(idx, out int n) ? n : -1,
                        PrimaryDaeRelPath = primaryRel,
                        PrimaryDaeFullPath = primaryFull,
                        PrimaryDaeSize = primarySize,
                        HasPrimaryDae = primarySize > 0,
                        HasTwoDSlice = hasTwoD,
                        TwoDDaeRelPath = twoDRel,
                        TwoDDaeSize = twoDSize,
                        AllDaeCount = allDaeCount,
                        TextureCount = SafeCount(sceneDir, "*.dds.phyre"),
                        HasAhwin32 = SafeCount(sceneDir, "*.ahwin32") > 0,
                    });
                }

                if (scenes.Count == 0)
                    continue; // a top-level folder with no parseable scenes is not a battle-map area.

                areas.Add(new BattleMap_Area
                {
                    AreaCode = areaCode,
                    Scenes = scenes.OrderBy(s => s.SceneId, StringComparer.OrdinalIgnoreCase).ToList(),
                });
            }

            List<BattleMap_Scene> flat = areas
                .SelectMany(a => a.Scenes)
                .OrderBy(s => s.SceneId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new BattleMapCatalog_File
            {
                Root = root,
                Areas = areas.OrderBy(a => a.AreaCode, StringComparer.OrdinalIgnoreCase).ToList(),
                Scenes = flat,
            };
        }

        /// <summary>Scene by leaf id (e.g. "mihn00_a"); null if absent.</summary>
        public BattleMap_Scene? FindScene(string sceneId) =>
            Scenes.FirstOrDefault(s => string.Equals(s.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));

        /// <summary>All variant scenes for a 6-char map key (e.g. "mihn00" -> mihn00_a, mihn00_b).</summary>
        public IReadOnlyList<BattleMap_Scene> ScenesForMap(string mapKey) =>
            Scenes.Where(s => string.Equals(s.MapKey, mapKey, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(s => s.Variant, StringComparer.OrdinalIgnoreCase)
                  .ToList();

        /// <summary>OrdinalIgnoreCase index leaf -> scene.</summary>
        public IReadOnlyDictionary<string, BattleMap_Scene> BySceneId() =>
            Scenes.ToDictionary(s => s.SceneId, s => s, StringComparer.OrdinalIgnoreCase);

        /// <summary>Map key -> its variant scenes (OrdinalIgnoreCase).</summary>
        public ILookup<string, BattleMap_Scene> ByMap() =>
            Scenes.ToLookup(s => s.MapKey, s => s, StringComparer.OrdinalIgnoreCase);

        private static int SafeCount(string dir, string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).Count(); }
            catch { return 0; }
        }

        private static string? ExistingDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                string full = Path.GetFullPath(path);
                return Directory.Exists(full) ? full : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class BattleMap_Area
    {
        public required string AreaCode { get; init; }
        public required IReadOnlyList<BattleMap_Scene> Scenes { get; init; }
        public int SceneCount => Scenes.Count;
    }

    public sealed class BattleMap_Scene
    {
        public required string AreaCode { get; init; }      // "mihn"
        public required string SceneId { get; init; }       // "mihn00_a"
        public required string MapKey { get; init; }        // "mihn00" (== EncounterTable_Entry.Map)
        public required string Variant { get; init; }       // "a" / "b" / "c"
        public required int MapIndex { get; init; }         // the NN, e.g. 0
        public required string PrimaryDaeRelPath { get; init; }  // POSIX, btlmap-root-relative
        public required string PrimaryDaeFullPath { get; init; }
        public required long PrimaryDaeSize { get; init; }
        public required bool HasPrimaryDae { get; init; }
        public required bool HasTwoDSlice { get; init; }    // bsil03_a/cdsp00_a/mtgz07_a/stbv00_a carry a 2d/ slice
        public string? TwoDDaeRelPath { get; init; }
        public long? TwoDDaeSize { get; init; }
        public required int AllDaeCount { get; init; }      // total .dae.phyre under the scene (1 normally, 2 with 2d/)
        public required int TextureCount { get; init; }     // .dds.phyre count
        public required bool HasAhwin32 { get; init; }

        /// <summary>True for synthetic picker rows: the map has btl_* battles on disk but no shipped HD btlmap scene
        /// (debug maps like zzzz00). Aurora can still read chunk2/3 from the battle bins; render stays unavailable.</summary>
        public bool IsMapOnlyVirtual { get; init; }

        /// <summary>A scene model so tiny it is a placeholder stub with no textures (kami03_a/kino05_a/nagi03_a/bika04_a).</summary>
        public bool IsStub => !IsMapOnlyVirtual && PrimaryDaeSize > 0 && PrimaryDaeSize < 8192 && TextureCount == 0;

        /// <summary>Build a virtual scene row for maps that exist in encounter/btl but not in ps3data/btlmap.</summary>
        public static BattleMap_Scene CreateMapOnlyVirtual(string mapKey)
        {
            mapKey = (mapKey ?? string.Empty).Trim();
            string area = mapKey.Length >= 4 ? mapKey[..4] : mapKey;
            int mapIndex = -1;
            if (mapKey.Length >= 6 && int.TryParse(mapKey.AsSpan(4, 2), out int nn))
                mapIndex = nn;

            return new BattleMap_Scene
            {
                AreaCode = area,
                SceneId = mapKey + "_maponly",
                MapKey = mapKey,
                Variant = "maponly",
                MapIndex = mapIndex,
                PrimaryDaeRelPath = "(sem cena HD btlmap)",
                PrimaryDaeFullPath = string.Empty,
                PrimaryDaeSize = 0,
                HasPrimaryDae = false,
                HasTwoDSlice = false,
                TwoDDaeRelPath = null,
                TwoDDaeSize = null,
                AllDaeCount = 0,
                TextureCount = 0,
                HasAhwin32 = false,
                IsMapOnlyVirtual = true,
            };
        }

        public override string ToString() =>
            IsMapOnlyVirtual
                ? $"{MapKey} (map-only · sem cena HD)"
                : $"{SceneId} ({PrimaryDaeSize:N0}B, {TextureCount} tex{(HasTwoDSlice ? ", +2d" : "")}{(IsStub ? ", STUB" : "")})";
    }
}
