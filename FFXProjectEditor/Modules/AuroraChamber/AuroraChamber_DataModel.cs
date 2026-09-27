using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Modules.Common.ViewerHub;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — Layer 3 (the union). Joins 🌙 BIANCA (Layer 1, the battle-scene catalog) with the
    /// MapViewer (Layer 2, the Phyre renderer) and lifts actor COORDINATES out of the per-battle chunk3. Dedicated to
    /// Aurora &amp; her mother Bianca. 💛
    ///
    /// Pipeline, all read-only / additive:
    ///   1. SCENE PICKER  — <see cref="BattleMapCatalog_File"/>.Scan(btlmap) → the 25-area scene catalog (BIANCA).
    ///   2. RENDER        — <see cref="AuroraSceneRenderer"/> drives the existing export pipeline + MapViewer via its
    ///                      own <c>?catalog=/?map=/?actors=</c> overrides (shared catalog.json untouched).
    ///   2b. RENDER PIPELINE — <see cref="AuroraRenderPipeline"/> porta o mapeamento GS→renderer do noclip.website
    ///                      render.ts (blend modes 0x00/0x42/0x44/0x04/0x48/0x46/0x88, 12 render layers, alpha/depth
    ///                      test do TEST_1, fog via PRIM 0x20). O summary + blend modes vao no mini-catalog JSON
    ///                      (aurora-catalog.json) para o viewer descrever draw calls; base para renderer nativo futuro.
    ///   3. COORDINATES   — <see cref="BattleArenaAnchors_File"/> decodes chunk3 actor anchors (battle-local) for the
    ///                      scene's battles (resolved through <see cref="EncounterTable_File"/> by the PROVEN map key).
    ///
    /// Preview reads the selected extraction. Explicit authoring writes only project battle files
    /// through bounded writers with backups; this module performs no in-process game writes.
    /// </summary>
    internal partial class AuroraChamber_DataModel : ObservableObject
    {
        private BattleMapCatalog_File? _catalog;
        private BattlefieldSceneResolver? _resolver;
        private EncounterTable_File? _encounter;
        private bool _encounterLoadAttempted;
        private readonly List<AuroraSceneRow> _allScenes = new();

        // The decoded anchors of the currently-selected battle (kept for render-overlay + export reuse).
        private BattleArenaAnchors_File? _currentAnchors;
        private IReadOnlyList<Battle_FormationSlot>? _currentLineup; // chunk2 monster lineup for the selected battle (slot -> monster model)
        private BattleCameraScript_File? _currentCamera; // chunk0 ATEL camera shots (camReq) for the selected battle

        // Causa A (2026-08-16): motivo (não-fatal) pelo qual o último render não anexou overlay de atores.
        // Uso: sinaliza na UI por que o EditViewer abrirá mapa sem monstros/câmeras, sem falhar o render.
        private string? renderEmptyOverlayReason;

        [ObservableProperty] private string dragSaveStatus = Strings.U_Au_DragSaveHint;

        [ObservableProperty]
        private string battle3DStatus = Strings.U_Au_RealGameDesc;

        /// <summary>Última URL aberta no viewer embutido (EditViewer ou RealGame).</summary>
        public string? LastEmbeddedUrl { get; private set; }

        /// <summary>Disparado quando um save re-aplica o override do RealGame — o code-behind recarrega o viewer.</summary>
        public event Action? EmbeddedReloadRequested;

        /// <summary>Battle atualmente com override ativo no RealGame (para re-sincronizar após saves).</summary>
        private string? _realGameBattleId;
        private AuroraNativePositionSession? _nativePositionSession;
        private readonly AuroraBattleOverlayOwner _realGameOverlayOwner =
            new(ViewerHubService.ViewerDataRoot);

        /// <summary>True when the last EditViewer URL is the rendered PS2-native battle stage (noclip
        /// ?edit=1) instead of the MapViewer scene — the code-behind titles the window accordingly.</summary>
        public bool LastEmbeddedIsBattleStage { get; private set; }
        public bool CanOpenBattlePreview => !IsRendering && !string.IsNullOrEmpty(SelectedBattle);
        public bool CanMountScenes => !IsRendering && !IsMountingAll && _allScenes.Any(r => !r.Scene.IsMapOnlyVirtual);
        public bool CanRemountScene => !IsRendering && SelectedScene is { Scene.IsMapOnlyVirtual: false };

        partial void OnIsRenderingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanOpenBattlePreview));
            OnPropertyChanged(nameof(CanMountScenes));
            OnPropertyChanged(nameof(CanRemountScene));
        }

        partial void OnIsMountingAllChanged(bool value) => OnPropertyChanged(nameof(CanMountScenes));

        [ObservableProperty] private string embeddedTitle = Strings.U_Au_EmbeddedTitle;
        [ObservableProperty] private string embeddedStatus = Strings.U_Au_ViewersDesc;

        [ObservableProperty]
        private string honestyBanner =
            "READ-ONLY · ADDITIVE. Scene bridge (EncounterTable map → btlmap) = PROVEN (100% on shipped HD scenes, 0 " +
            "false positives). Battle→scene transform = IDENTITY, PROVEN in IDA: the engine copies the chunk3 coord " +
            Strings.U_Au_TransformDesc;

        [ObservableProperty] private string catalogSummary = Strings.U_Au_Scanning;
        [ObservableProperty] private string filterText = string.Empty;

        public ObservableCollection<AuroraSceneRow> Scenes { get; } = new();
        [ObservableProperty] private AuroraSceneRow? selectedScene;

        [ObservableProperty] private string sceneHeader = Strings.U_Au_NoScene;
        [ObservableProperty] private string sceneDetail = Strings.U_Au_PickScene;
        [ObservableProperty] private string renderStatus = Strings.U_Au_RenderHint;
        [ObservableProperty] private string textureBindSummary = "—";
        [ObservableProperty] private string rt2PendingSummary = AuroraRt2Catalog.Summary;

        public ObservableCollection<string> TextureBindBreakdown { get; } = new();
        public ObservableCollection<string> Rt2Checklist { get; } = new(AuroraRt2Catalog.Checklist);

        public ObservableCollection<string> Battles { get; } = new();
        [ObservableProperty] private string? selectedBattle;
        [ObservableProperty] private string battleSummary = "—";

        public ObservableCollection<AuroraAnchorRow> Anchors { get; } = new();
        [ObservableProperty] private string anchorsSummary = Strings.U_Au_AnchorsHint;
        [ObservableProperty] private string coordExportStatus = "—";

        // 🎬 Encounter opener / CTB seed (read-only, lazy per selected battle).
    private BattleEncounterOpener_File? _currentOpener;
    public ObservableCollection<EncounterOpenerWriteRow> OpenerWrites { get; } = new();
        [ObservableProperty] private string openerSummary = Strings.U_Au_OpenerHint;
        [ObservableProperty] private string openerGuardrail = "";
        [ObservableProperty] private string openerFormationMapping = "";

        // 👥 Hidden companion activation / summon-handoff (m213 lane, read-only).
        private BattleCompanionActivation_File? _currentCompanionActivation;
        public ObservableCollection<BattleCompanionActivationRow> CompanionActivationRows { get; } = new();
        [ObservableProperty] private string companionActivationSummary = Strings.U_Au_CompanionHint;
        [ObservableProperty] private string companionActivationBattleToken = "";
        [ObservableProperty] private string companionActivationCoverage = "";
        [ObservableProperty] private string companionActivationGuardrail = "";

    // 🎥 Camera cuts (camReq in chunk0) for the selected battle.
        public ObservableCollection<AuroraCameraRow> CameraShots { get; } = new();
        [ObservableProperty] private string cameraSummary = Strings.U_Au_CameraHint;
        [ObservableProperty] private string cameraStatus = "—";

        // 🎥 Camera PARAM KNOBS — the editable angle/distance/position/roll floats in chunk0 (camSetPolar/refSetPos/...).
        private BattleCameraSetup_File? _currentCameraSetup;
        private readonly List<AuroraCameraKnobRow> _allKnobs = new();
        public ObservableCollection<AuroraCameraKnobRow> CameraKnobs { get; } = new();
        public ObservableCollection<string> CameraRoleFilters { get; } = new();
        [ObservableProperty] private string cameraRoleFilter = "todos";
        [ObservableProperty] private string cameraKnobSearch = string.Empty;
        [ObservableProperty] private string cameraParamsSummary = "—";
        [ObservableProperty] private string cameraParamsStatus = "—";

        [ObservableProperty] private bool isRendering;
        [ObservableProperty] private bool isMountingAll;

        public AuroraChamber_DataModel()
        {
            RefreshCatalog();
        }

        private void RefreshTextureBindSummary(BattleMap_Scene scene)
        {
            TextureBindBreakdown.Clear();
            if (scene.IsMapOnlyVirtual)
            {
                TextureBindSummary = Strings.U_Au_MapOnly;
                return;
            }

            BattleMap_ExportQuality? q = AuroraSceneRenderer.GetSceneExportQuality(scene);
            if (q == null)
            {
                TextureBindSummary = Strings.U_Au_NoExport;
                return;
            }

            TextureBindSummary = q.QualityLabel + (q.UnboundSubmeshCount > 0
                ? string.Format(Strings.U_Au_UnboundVertexColor, q.UnboundSubmeshCount)
                : "");
            foreach (BattleMap_TextureBindBreakdownRow row in q.Breakdown.OrderByDescending(r => r.Count))
                TextureBindBreakdown.Add($"{row.Label}: {row.Count}");
        }

        /******************************************
         * 1. SCENE PICKER (BIANCA)
         ******************************************/

        /// <summary>Re-read the SELECTED battle's formation + anchors from disk so an EXTERNAL save (e.g. the
        /// Formation Editor changing the monster lineup) shows on the map. RefreshCatalog only reloads the scene
        /// list — the open battle's bytes are cached — so without this a formation edit wouldn't appear until the
        /// user re-selected the battle. Re-fires OnSelectedBattleChanged (which re-reads the .bin).</summary>
        public void ReloadSelectedBattle()
        {
            string? battle = SelectedBattle;
            if (string.IsNullOrEmpty(battle)) { RefreshCatalog(); return; }
            SelectedBattle = null;   // clears the cached read
            SelectedBattle = battle; // re-fires the disk read (formation lineup + chunk3 anchors)
        }

        public void RefreshCatalog()
        {
            _allScenes.Clear();
            _encounter = null;
            _encounterLoadAttempted = false;

            string btlmapRoot = AuroraSceneRenderer.ResolveBtlmapRoot();
            try
            {
                _catalog = BattleMapCatalog_File.Scan(btlmapRoot);
                _resolver = new BattlefieldSceneResolver(_catalog);

                foreach (BattleMap_Scene s in _catalog.Scenes)
                    _allScenes.Add(new AuroraSceneRow(s));

                int mounted = _allScenes.Count(r => r.IsMounted && !r.Scene.IsMapOnlyVirtual);
                int hdScenes = _allScenes.Count(r => !r.Scene.IsMapOnlyVirtual);
                int stubs = _catalog.Scenes.Count(s => s.IsStub);
                CatalogSummary =
                    string.Format(Strings.U_Au_SceneCatalogSummary, _catalog.AreaCount, _catalog.SceneCount,
                        $"({_catalog.TotalPrimaryBytes / (1024 * 1024.0):0.#} MB geometry, {stubs} stub) · " +
                        $"{mounted}/{hdScenes} mounted in EditViewer · source: {btlmapRoot}");
            }
            catch (Exception ex)
            {
                _catalog = null;
                _resolver = null;
                CatalogSummary =
                    string.Format(Strings.U_Au_CatalogUnavailable, ex.Message, btlmapRoot);
            }

            // Even without HD catalog, surface virtual scenes from encounter data
            AppendVirtualMapScenes();

            ApplyFilter();
            OnPropertyChanged(nameof(CanMountScenes));
        }

        /// <summary>Maps like zzzz00 have encounter + btl_* bins but no HD btlmap folder — surface them in the picker.</summary>
        private void AppendVirtualMapScenes()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
                return;
            if (!EnsureEncounterLoaded(out _))
                return;

            HashSet<string> seen = _allScenes
                .Select(r => r.Scene.MapKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string mapKey in _encounter!.Tables
                         .Select(t => t.Map)
                         .Where(m => !string.IsNullOrWhiteSpace(m))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Contains(mapKey))
                    continue;
                if (_catalog?.ScenesForMap(mapKey).Count > 0)
                    continue;
                if (CountBattlesOnDisk(mapKey) <= 0)
                    continue;

                _allScenes.Add(new AuroraSceneRow(BattleMap_Scene.CreateMapOnlyVirtual(mapKey)));
                seen.Add(mapKey);
            }
        }

        private static int CountBattlesOnDisk(string mapKey)
        {
            try
            {
                string btlRoot = Project_Service.Instance.Path_Btl;
                if (!Directory.Exists(btlRoot))
                    return 0;
                return Directory.EnumerateDirectories(btlRoot, mapKey + "_*")
                    .Count(d =>
                    {
                        string? id = Path.GetFileName(d);
                        return !string.IsNullOrEmpty(id) && BattleFileExists(id);
                    });
            }
            catch { return 0; }
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            string filter = (FilterText ?? string.Empty).Trim();
            AuroraSceneRow? previous = SelectedScene;

            IEnumerable<AuroraSceneRow> view = _allScenes;
            if (filter.Length > 0)
            {
                view = _allScenes.Where(r =>
                    r.Scene.SceneId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    r.Scene.AreaCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    r.Scene.MapKey.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            Scenes.Clear();
            foreach (AuroraSceneRow r in view)
                Scenes.Add(r);

            if (previous != null && Scenes.Contains(previous))
                SelectedScene = previous;
        }

        /// <summary>Programmatically focus the first scene for an encounter map key (e.g. "bika02"). Used by the
        /// EncounterTable Explorer double-click bridge: clears any active filter so the target is in the visible
        /// list, then selects it (which fires OnSelectedSceneChanged → resolves battles + anchors). Returns true on match.</summary>
        public bool SelectSceneByMapKey(string mapKey)
        {
            FFXProjectEditor.Diagnostics.DebugLog.Info("Aurora.SelectScene", $"mapKey={mapKey}");
            if (string.IsNullOrWhiteSpace(mapKey))
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn("Aurora.SelectScene", "mapKey vazio/em branco");
                return false;
            }
            FilterText = string.Empty; // ensure the target scene is in the visible Scenes list (no-op if already empty)
            AuroraSceneRow? match = Scenes.FirstOrDefault(
                r => string.Equals(r.Scene.MapKey, mapKey, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn("Aurora.SelectScene", string.Format(Strings.U_Au_MapKeyNotFound, mapKey, Scenes.Count));
                return false;
            }
            SelectedScene = match;
            FFXProjectEditor.Diagnostics.DebugLog.Info("Aurora.SelectScene", string.Format(Strings.U_Au_SelectedScene, match.Scene.SceneId, match.IsMounted));
            return true;
        }

        partial void OnSelectedSceneChanged(AuroraSceneRow? value)
        {
            OnPropertyChanged(nameof(CanRemountScene));
            Battles.Clear();
            SelectedBattle = null;
            Anchors.Clear();
            _currentAnchors = null;
            AnchorsSummary = Strings.U_Au_AnchorsHint;
            CoordExportStatus = "—";

            if (value == null)
            {
                SceneHeader = Strings.U_Au_NoScene;
                SceneDetail = Strings.U_Au_PickScene;
                RenderStatus = Strings.U_Au_RenderHint;
                TextureBindSummary = "—";
                TextureBindBreakdown.Clear();
                BattleSummary = "—";
                return;
            }

            BattleMap_Scene s = value.Scene;
            SceneHeader = $"{s.SceneId}  ·  map {s.MapKey}";

            IReadOnlyList<BattleMap_Scene> variants = _resolver?.ResolveScenes(s.MapKey) ?? Array.Empty<BattleMap_Scene>();
            // 🌅 A04 (2026-06-15): runtime selector for `_a/_b/_c` is NOT yet RE-proved (story-flag → variant mapping
            // unknown). The chamber renders whichever variant the catalog/UI picked, which may NOT match what FFX
            // loads at runtime. Mark as UNVERIFIED until selector is hooked. See FFX_AURORA_ARENA_VARIANT_SELECTION_RE_2026-06-15.md.
            string variantNote = variants.Count > 1
                ? string.Format(Strings.U_Au_VariantsNote, variants.Count, string.Join("/", variants.Select(v => v.Variant)))
                : "";
            if (s.IsMapOnlyVirtual)
            {
                SceneDetail = Strings.U_Au_BattleStageFallback;
                RenderStatus = Strings.U_Au_PreviewLimits;
            }
            else
            {
                string stubNote = s.IsStub
                    ? Strings.U_Au_BtlmapStub
                    : "";
                string mountNote = value.IsMounted
                    ? (value.TextureQualityLabel.Length > 0
                        ? $" · glTF vertex-color · {value.TextureQualityLabel}"
                        : Strings.U_Au_GltfMountedNote)
                    : Strings.U_Au_ExportPendingNote;
                SceneDetail =
                    string.Format(Strings.U_Au_AreaDetail, s.AreaCode, s.PrimaryDaeSize / 1024.0, s.TextureCount) +
                    $"{(s.HasTwoDSlice ? " · +slice 2d" : "")}{stubNote}{variantNote}{mountNote}\n" +
                    string.Format(Strings.U_Au_ModelLabel, s.PrimaryDaeRelPath);
                RenderStatus = value.IsMounted
                    ? Strings.U_Au_ReadyRenderCached
                    : Strings.U_Au_ReadyRenderOnDemand;
            }

            ResolveBattlesForScene(s);
            RefreshTextureBindSummary(s);
        }

        /******************************************
         * 2/3 helpers — battles + anchors for the scene
         ******************************************/

        private void ResolveBattlesForScene(BattleMap_Scene scene)
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                BattleSummary = Strings.U_Au_LoadProjectHint;
                return;
            }

            var battleIds = new List<string>();

            // Primary path: the PROVEN map-key bridge over the encounter table.
            if (EnsureEncounterLoaded(out string err))
            {
                battleIds = _encounter!.Tables
                    .Where(t => string.Equals(t.Map, scene.MapKey, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(t => t.Groups.SelectMany(g => g.Formations.Select(f => f.BattleId)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(BattleFileExists)
                    .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Orphan/boss fallback: scenes with no encounter map (bika04/grid00/nagi03/sfia00…) — scan btl folders.
            if (battleIds.Count == 0)
            {
                battleIds = ScanOrphanBattles(scene.MapKey);
            }

            foreach (string b in battleIds)
                Battles.Add(b);

            BattleSummary = battleIds.Count == 0
                ? string.Format(Strings.U_Au_NoBattleOnDisk, scene.MapKey) + (string.IsNullOrEmpty(_lastEncounterError) ? "" : $" ({_lastEncounterError})")
                : string.Format(Strings.U_Au_SelectBattleForAnchors, battleIds.Count, scene.MapKey);

            if (battleIds.Count > 0)
                SelectedBattle = battleIds[0];
        }

        private List<string> ScanOrphanBattles(string mapKey)
        {
            try
            {
                string btlRoot = Project_Service.Instance.Path_Btl;
                if (!Directory.Exists(btlRoot)) return new List<string>();
                return Directory.EnumerateDirectories(btlRoot, mapKey + "_*")
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n) && BattleFileExists(n!))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        private static bool BattleFileExists(string battleId)
        {
            try { return File.Exists(Project_Service.Instance.GetPathBattle(battleId)); }
            catch { return false; }
        }

        private string _lastEncounterError = string.Empty;

        private bool EnsureEncounterLoaded(out string error)
        {
            error = string.Empty;
            if (_encounter != null) return true;
            if (_encounterLoadAttempted) { error = _lastEncounterError; return false; }

            _encounterLoadAttempted = true;
            try
            {
                string path = Project_Service.Instance.Path_KernelEncounterTable;
                if (!File.Exists(path)) { _lastEncounterError = error = string.Format(Strings.U_Au_BtlBinNotFound, path); return false; }
                _encounter = EncounterTable_File.Read(File.ReadAllBytes(path));
                return true;
            }
            catch (Exception ex) { _lastEncounterError = error = string.Format(Strings.U_Au_FailedReadingBtl, ex.Message); _encounter = null; return false; }
        }

        partial void OnSelectedBattleChanged(string? value)
        {
            OnPropertyChanged(nameof(CanOpenBattlePreview));
            Anchors.Clear();
            _currentAnchors = null;
            _currentLineup = null;
            CameraShots.Clear();
            _currentCamera = null;
            CameraKnobs.Clear();
            _allKnobs.Clear();
            _currentCameraSetup = null;
            OpenerWrites.Clear();
            _currentOpener = null;
            CompanionActivationRows.Clear();
            _currentCompanionActivation = null;
            CoordExportStatus = "—";
            CameraStatus = "—";
            CameraParamsStatus = "—";

            if (string.IsNullOrEmpty(value))
            {
                AnchorsSummary = Strings.U_Au_AnchorsHint;
                CameraSummary = Strings.U_Au_CameraHint;
                CameraParamsSummary = "—";
                OpenerSummary = Strings.U_Au_OpenerHint;
                OpenerGuardrail = "";
                OpenerFormationMapping = "";
                CompanionActivationSummary = Strings.U_Au_CompanionHint;
                CompanionActivationBattleToken = "";
                CompanionActivationCoverage = "";
                CompanionActivationGuardrail = "";
                return;
            }

            try
            {
                string path = Project_Service.Instance.GetPathBattle(value);
                if (!File.Exists(path)) { AnchorsSummary = string.Format(Strings.U_Au_BtlNotFound, path); return; }

                byte[] battleBytes = File.ReadAllBytes(path);
                _currentAnchors = BattleArenaAnchors_File.ReadFromBattleBin(value, battleBytes);
                try { _currentLineup = Battle_File.Read(value, battleBytes).Formation?.Slots; }  // chunk2 lineup -> monster model per anchor
                catch { _currentLineup = null; }
                PopulateAnchorRows(_currentAnchors);

                // 🎥 chunk0 camera shots (same bytes, independent decode — never throws).
                _currentCamera = BattleCameraScript_File.ReadFromBattleBin(value, battleBytes);
                PopulateCameraRows(_currentCamera);

                // 🎥 chunk0 camera PARAM KNOBS (the editable angle/distance/pos/roll floats).
                _currentCameraSetup = BattleCameraSetup_File.ReadFromBattleBin(value, battleBytes);
                PopulateCameraKnobs(_currentCameraSetup);

                // 🎬 Encounter opener / CTB seed (chunk0 HookStart — same bytes, read-only decode).
                PopulateOpenerData(value, battleBytes);

                // 👥 Hidden companion activation / summon-handoff (m213 lane, same battle + resolved monster bins).
                PopulateCompanionActivationData(value, battleBytes);
            }
            catch (Exception ex)
            {
                AnchorsSummary = string.Format(Strings.U_Au_AnchorsReadFailed, value, ex.Message);
            }
        }

        private void PopulateAnchorRows(BattleArenaAnchors_File anchors)
        {
            if (anchors.Areas.Count == 0)
            {
                AnchorsSummary = Strings.U_Au_NoChunk3;
                return;
            }

            // Area-record 0 is the primary arena; show every well-defined group, monsters-live first.
            BattleArena_AreaRecord area0 = anchors.Areas[0];
            BattleArena_AnchorRole[] order =
            {
                BattleArena_AnchorRole.MonsterLive,
                BattleArena_AnchorRole.PartyFront,
                BattleArena_AnchorRole.Aeon,
                BattleArena_AnchorRole.PartyBack,
                BattleArena_AnchorRole.MonsterStagingA,
                BattleArena_AnchorRole.MonsterStagingB,
                BattleArena_AnchorRole.Camera,
            };

            int live = 0;
            foreach (BattleArena_AnchorRole role in order)
            {
                BattleArena_AnchorGroup? g = area0[role];
                if (g == null || g.Anchors.Count == 0) continue;
                foreach (BattleArena_Anchor a in g.Anchors)
                {
                    // Live-monster anchor [i] maps 1:1 to formation slot [i]; surface its real name.
                    string? monsterName = null;
                    if (role == BattleArena_AnchorRole.MonsterLive && _currentLineup != null
                        && a.Index >= 0 && a.Index < _currentLineup.Count)
                    {
                        monsterName = _currentLineup[a.Index].MonsterName;
                    }
                    Anchors.Add(new AuroraAnchorRow(a, role == BattleArena_AnchorRole.MonsterLive, monsterName));
                    if (role == BattleArena_AnchorRole.MonsterLive) live++;
                }
            }

            string areaNote = anchors.Areas.Count > 1 ? string.Format(Strings.U_Au_AreaNote, anchors.Areas.Count) : "";
            string notes = anchors.Notes.Count > 0 ? string.Format(Strings.U_Au_DecodeNotesCount, anchors.Notes.Count) : "";
            AnchorsSummary =
                string.Format(Strings.U_Au_LiveMonstersOnField, live, areaNote, notes) +
                Strings.U_Au_CoordsBattleLocal;
        }

        /// <summary>Pull the coords the user dragged in the EditViewer (buffered by AuroraDragBridge) and write the
        /// new monster_live positions to the battle .bin via the proven position-only writer. MVP: monster_live,
        /// area-record 0. Byte-safe (WriteLooseFile guards the diff to the anchor array span + backs up once).</summary>
        public void SaveDraggedPositions()
        {
            AuroraDragBridge.EnsureStarted();
            if (string.IsNullOrEmpty(SelectedBattle) || _currentAnchors == null)
            { DragSaveStatus = Strings.U_Au_SelectBattleFirst; return; }

            var drags = new Dictionary<string, AuroraDragBridge.Coord>(AuroraDragBridge.Snapshot(SelectedBattle));

            // 🐉 Merge the noclip edit-gizmo sidecar (battle-stage EditViewer): the viewer's S key saves
            // per-slot position DELTAS to viewer-data/.../edits/<encId>.json via POST /api/edits. Convert
            // each to an absolute coord (file anchor + delta) in the same monster_live|<index> keyspace.
            // The DragBridge wins on a key collision (its drag is the freshest MapViewer edit).
            int sidecarSlots = MergeNoclipSidecarDeltas(
                SelectedBattle, drags, out int sidecarEncId, out List<int> sidecarSlotList);
            if (drags.Count == 0)
            { DragSaveStatus = Strings.U_Au_NothingDragged; return; }

            try
            {
                string path = Project_Service.Instance.GetPathBattle(SelectedBattle);

                // 🎥 camera drags first — byte-local writes in chunk0. Eye inverse uses the dragged ref when both move.
                var camMessages = new List<string>();
                AuroraDragBridge.Coord? refDrag = null;
                if (drags.TryGetValue("camera_ref|0", out AuroraDragBridge.Coord camc))
                {
                    refDrag = camc;
                    camMessages.Add(ApplyCameraRefDragToDisk(path, camc));
                }
                if (drags.TryGetValue("camera_eye|0", out AuroraDragBridge.Coord eye))
                    camMessages.Add(ApplyCameraEyeDragToDisk(path, eye, refDrag));
                string camMsg = string.Join("  ·  ", camMessages.Where(s => s.Length > 0));

                byte[] original = File.ReadAllBytes(path); // re-read: the camera edit may have just changed the file
                Battle_File bf = Battle_File.Read(SelectedBattle, original);

                const BattleArena_AnchorRole role = BattleArena_AnchorRole.MonsterLive;
                BattleArena_AnchorGroup? group = bf.CanWritePositions ? _currentAnchors.Areas[0][role] : null;

                List<(float X, float Y, float Z)> coords = new();
                int applied = 0;
                if (group != null)
                    foreach (BattleArena_Anchor a in group.Anchors)
                    {
                        if (drags.TryGetValue($"monster_live|{a.Index}", out AuroraDragBridge.Coord c))
                        { coords.Add((c.X, c.Y, c.Z)); applied++; }
                        else
                        { coords.Add((a.X, a.Y, a.Z)); }
                    }

                string monMsg = "";
                if (applied > 0 && group != null)
                {
                    byte[] updated = bf.WriteWithMonsterPositions(0, role, coords);
                    (int Offset, int Length, int Count)? loc = BattleArenaPositionWriter.LocateAnchorArray(original, 0, role);
                    if (loc == null) { DragSaveStatus = Strings.U_Au_NoPosArray; return; }
                    BattleArenaPositionWriter.SaveResult save = BattleArenaPositionWriter.WriteLooseFile(path, original, updated, loc.Value.Offset, loc.Value.Length);
                    // WHY aurora-chamber.* area: DebugLog.IsActive inherits dotted prefixes, and the
                    // active area is the module id "aurora-chamber" — plain "Aurora.*" never logs.
                    FFXProjectEditor.Diagnostics.DebugLog.Info("aurora-chamber.DragSave",
                        $"battle={SelectedBattle} path={path} applied={applied} sidecar={sidecarSlots} save={save.Status} :: {save.Message}");
                    monMsg = string.Format(Strings.U_Au_MonstersMoved, applied, save.Message);
                    if (sidecarSlots > 0)
                        monMsg += string.Format(Strings.U_Au_SidecarMergedSuffix, sidecarSlots);
                    // The consumed deltas are baked into the .bin ONLY when the write actually landed —
                    // on Error/AbortedNotPositionOnly the sidecar keeps them so nothing is lost silently.
                    // Zeroed positions keep heading/scale edits (nulls mean "don't touch").
                    if (save.Ok)
                        ClearConsumedSidecarPositions(sidecarEncId, sidecarSlotList);
                }

                if (camMsg.Length == 0 && monMsg.Length == 0)
                { DragSaveStatus = Strings.U_Au_NoDragApplicable; return; }

                DragSaveStatus = string.Join("  ·  ", new[] { camMsg, monMsg }.Where(s => s.Length > 0));
                AuroraDragBridge.Clear(SelectedBattle);
                OnSelectedBattleChanged(SelectedBattle); // reload so the markers show the saved values
                RefreshRealGameOverride(); // 🐉 re-aplica o override: o RealGame reflete a posição salva
            }
            catch (Exception ex) { DragSaveStatus = string.Format(Strings.U_Au_SaveFailed, ex.Message); }
        }

        /// <summary>Merge the noclip edit-gizmo sidecar (<c>edits/&lt;encId&gt;.json</c>) into the drag keyspace:
        /// each <c>actors.&lt;slot&gt;.position</c> delta becomes an absolute <c>monster_live|&lt;index&gt;</c>
        /// coord (file anchor + delta). noclip slot i == monster_live anchor Index i == formation slot i —
        /// all three index the same chunk3/positions lineup. Returns how many slots were merged.</summary>
        private int MergeNoclipSidecarDeltas(
            string battleId,
            Dictionary<string, AuroraDragBridge.Coord> drags,
            out int encId,
            out List<int> mergedSlots)
        {
            encId = -1;
            mergedSlots = new List<int>();
            if (_currentAnchors == null || _currentAnchors.Areas.Count == 0 ||
                !Aurora3DLauncher.TryResolveEncounter(battleId, out encId) ||
                !TryReadNoclipEditDeltas(
                    NoclipOverlayStore.ResolveEditsRoot(ViewerHubService.ViewerDataRoot),
                    encId,
                    out Dictionary<int, (double Dx, double Dy, double Dz)>? deltas) ||
                deltas == null)
                return 0;

            BattleArena_AnchorGroup? group = _currentAnchors.Areas[0][BattleArena_AnchorRole.MonsterLive];
            if (group == null) return 0;

            int merged = 0;
            foreach (BattleArena_Anchor a in group.Anchors)
            {
                if (!deltas.TryGetValue(a.Index, out (double Dx, double Dy, double Dz) d))
                    continue;
                string key = $"monster_live|{a.Index}";
                if (drags.ContainsKey(key))
                    continue; // MapViewer drag on the same slot is fresher — the bridge wins.
                // WHY dy is dropped: since the phantom-Y fix the sidecar baseline is captured AFTER
                // noclip's fly/underwater lift, so a non-zero dy is a real user edit (e.g. G floor-snap).
                // We still commit XZ only — for fly/aquatic monsters the engine re-applies its own lift
                // at runtime over the file Y, so a snapped floor-Y would come back double-lifted; and the
                // gizmo's normal drag plane is XZ, so dy is near-zero for ground monsters anyway.
                drags[key] = new AuroraDragBridge.Coord(a.X + (float)d.Dx, a.Y, a.Z + (float)d.Dz);
                mergedSlots.Add(a.Index);
                merged++;
            }
            return merged;
        }

        /// <summary>Reads the sidecar <c>&lt;editsRoot&gt;/&lt;encId&gt;.json</c> and returns per-slot position
        /// deltas. Static + pure for tests. Missing file / malformed JSON / non-finite or near-zero deltas
        /// all produce no deltas — a viewer reset writes [0,0,0], which is a no-op anyway.</summary>
        internal static bool TryReadNoclipEditDeltas(
            string editsRoot,
            int encId,
            out Dictionary<int, (double Dx, double Dy, double Dz)>? deltas)
        {
            deltas = null;
            string path;
            try
            {
                path = Path.Combine(Path.GetFullPath(editsRoot), encId + ".json");
            }
            catch { return false; }
            if (!File.Exists(path)) return false;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("actors", out System.Text.Json.JsonElement actors))
                    return false;
                var map = new Dictionary<int, (double, double, double)>();
                foreach (System.Text.Json.JsonProperty actor in actors.EnumerateObject())
                {
                    if (!int.TryParse(actor.Name, out int slot) || slot is < 0 or > 7)
                        continue;
                    if (!actor.Value.TryGetProperty("position", out System.Text.Json.JsonElement pos) ||
                        pos.ValueKind != System.Text.Json.JsonValueKind.Array || pos.GetArrayLength() != 3)
                        continue;
                    double dx = pos[0].GetDouble(), dy = pos[1].GetDouble(), dz = pos[2].GetDouble();
                    if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dz))
                        continue;
                    if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6 && Math.Abs(dz) < 1e-6)
                        continue;
                    map[slot] = (dx, dy, dz);
                }
                if (map.Count == 0) return false;
                deltas = map;
                return true;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException
                or InvalidOperationException or UnauthorizedAccessException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn("aurora-chamber.Sidecar", $"edits/{encId}.json ilegível: {ex.Message}");
                return false;
            }
        }

        /// <summary>Zero the position deltas of consumed slots — the absolute positions were written to the
        /// .bin, so keeping the delta would double-apply on the next page load. Heading/scale are preserved
        /// (nulls mean "don't touch" in <see cref="SidecarEditsWriter.ApplySlot"/>).</summary>
        private static void ClearConsumedSidecarPositions(int encId, List<int> slots)
        {
            if (encId < 0 || slots.Count == 0) return;
            foreach (int slot in slots)
            {
                try
                {
                    NoclipOverlayStore.WriteEdit(
                        ViewerHubService.ViewerDataRoot, encId, slot, 0.0, 0.0, 0.0, null, null);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    FFXProjectEditor.Diagnostics.DebugLog.Warn("aurora-chamber.Sidecar",
                        $"falha ao zerar delta do slot {slot} (enc {encId}): {ex.Message}");
                }
            }
        }

        /// <summary>Apply a camera REF (look-at) drag: write the refSetPos x/y/z floats in chunk0 (byte-local,
        /// same length, backup-once). EXACT (refSetPos is a real scene position). Returns a status string.</summary>
        private string ApplyCameraRefDragToDisk(string path, AuroraDragBridge.Coord c)
        {
            CameraEstablishingShot? e = _currentCameraSetup?.Establishing;
            if (e == null || !e.HasRef || e.RefXIndex < 0)
                return Strings.U_Au_CamRefNoRefSet;

            byte[] original = File.ReadAllBytes(path);
            byte[] working = original;
            foreach ((int idx, float val) in new[] { (e.RefXIndex, c.X), (e.RefYIndex, c.Y), (e.RefZIndex, c.Z) })
            {
                if (idx < 0) continue;
                BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(SelectedBattle!, working);
                working = setup.WithFloat(idx, val);
            }
            if (working.AsSpan().SequenceEqual(original)) return Strings.U_Au_CamRefNoChange;
            SaveBattleBytes(path, original, working);
            return string.Format(Strings.U_Au_CamRefApplied, c.X, c.Y, c.Z);
        }

        /// <summary>Apply a camera EYE drag: inverse the IDA-proven camSetPolar 0x6004 formula and write the
        /// horizontal/elevation/distance floats in chunk0. Requires all three polar args to be PUSHF-backed.</summary>
        private string ApplyCameraEyeDragToDisk(string path, AuroraDragBridge.Coord eye, AuroraDragBridge.Coord? refOverride)
        {
            CameraEstablishingShot? e = _currentCameraSetup?.Establishing;
            if (e == null || !e.HasRef || !e.HasPolar)
                return Strings.U_Au_CamEyeNoPolar;
            if (!e.CanEditPolarEye)
                return Strings.U_Au_CamEyeNoFloats;

            float rx = refOverride?.X ?? e.RefX;
            float ry = refOverride?.Y ?? e.RefY;
            float rz = refOverride?.Z ?? e.RefZ;
            var polar = BattleCameraSetup_File.EyeToPolar(rx, ry, rz, eye.X, eye.Y, eye.Z);

            byte[] original = File.ReadAllBytes(path);
            byte[] working = original;
            foreach ((int idx, float val) in new[]
                     {
                         (e.PolarHorizontalIndex, polar.HorizontalDeg),
                         (e.PolarElevationIndex, polar.ElevationDeg),
                         (e.PolarDistanceIndex, polar.Distance)
                     })
            {
                if (idx < 0) continue;
                BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(SelectedBattle!, working);
                working = setup.WithFloat(idx, val);
            }

            if (working.AsSpan().SequenceEqual(original)) return Strings.U_Au_CamEyeNoChange;
            SaveBattleBytes(path, original, working);
            return string.Format(Strings.U_Au_CamEyeApplied, polar.HorizontalDeg, polar.ElevationDeg, polar.Distance);
        }

        /******************************************
         * 3b. AUTHOR — add/remove a monster (chunk2 formation + chunk3 anchors in lock-step)
         ******************************************/

        [ObservableProperty]
        private string authorStatus = Strings.U_Au_AddCloneHint;

        /// <summary>Add a monster to the selected battle: clone the last live formation slot + ensure a spawn anchor
        /// (reserved if available, else grow chunk3). Byte-safe via BattleArenaAuthor; backs up once before writing.</summary>
        public void AddMonster()
        {
            if (string.IsNullOrEmpty(SelectedBattle)) { AuthorStatus = Strings.U_Au_AuthorSelectBattle; return; }
            try
            {
                string path = Project_Service.Instance.GetPathBattle(SelectedBattle);
                if (!File.Exists(path)) { AuthorStatus = string.Format(Strings.U_Au_AuthorBtlNotFound, path); return; }
                byte[] original = File.ReadAllBytes(path);
                if (!BattleArenaAuthor.CanAdd(original, out string why)) { AuthorStatus = string.Format(Strings.U_Au_AuthorCannotAdd, why); return; }
                byte[] updated = BattleArenaAuthor.AddMonsterCloneLast(original);
                AuthorStatus = Strings.U_Au_AuthorAdded + SaveBattleBytes(path, original, updated);
                ReloadSelectedBattle();
                RefreshRealGameOverride(); // 🐉 formação mudou → RealGame reflete
            }
            catch (Exception ex) { AuthorStatus = $"Falha ao adicionar: {ex.Message}"; }
        }

        /// <summary>Remove the last live monster from the selected battle (clears its formation slot). Refuses to drop
        /// below 1. Byte-safe via BattleArenaAuthor; backs up once before writing.</summary>
        public void RemoveMonster()
        {
            if (string.IsNullOrEmpty(SelectedBattle)) { AuthorStatus = Strings.U_Au_AuthorSelectBattle; return; }
            try
            {
                string path = Project_Service.Instance.GetPathBattle(SelectedBattle);
                if (!File.Exists(path)) { AuthorStatus = string.Format(Strings.U_Au_AuthorBtlNotFound, path); return; }
                byte[] original = File.ReadAllBytes(path);
                if (!BattleArenaAuthor.CanRemove(original, out string why)) { AuthorStatus = string.Format(Strings.U_Au_CannotRemove, why); return; }
                byte[] updated = BattleArenaAuthor.RemoveLastMonster(original);
                AuthorStatus = Strings.U_Au_Removed + SaveBattleBytes(path, original, updated);
                ReloadSelectedBattle();
                RefreshRealGameOverride(); // 🐉 formação mudou → RealGame reflete
            }
            catch (Exception ex) { AuthorStatus = string.Format(Strings.U_Au_RemoveFailed, ex.Message); }
        }

        /// <summary>Backup-once (<c>.aurora.bak</c>) then overwrite — used for the STRUCTURAL author writes (the file
        /// size can change), so the position-only WriteLooseFile guard does not apply. Safety is the gate-proven
        /// BattleArenaAuthor/GrowWriter + this backup.</summary>
        private static string SaveBattleBytes(string path, byte[] original, byte[] updated)
        {
            string bak = path + BattleArenaPositionWriter.DefaultBackupSuffix;
            if (!File.Exists(bak)) File.Copy(path, bak);
            File.WriteAllBytes(path, updated);
            return string.Format(Strings.U_Au_SavedBytes, Path.GetFileName(path), original.Length, updated.Length, Path.GetFileName(bak));
        }

        /******************************************
         * 3c. CAMERA — edit the chunk0 camReq cuts (SHOT angle + TARGET actor)
         ******************************************/

        private void PopulateCameraRows(BattleCameraScript_File cam)
        {
            CameraShots.Clear();
            if (cam.Chunk0Offset < 0 || cam.Script == null)
            {
                CameraSummary = Strings.U_Au_NoCameraScript;
                return;
            }
            if (cam.ShotCount == 0)
            {
                CameraSummary = Strings.U_Au_NoCameraCuts;
                return;
            }

            foreach (CameraShotRef s in cam.Shots)
                CameraShots.Add(new AuroraCameraRow(s));

            int editable = cam.Shots.Count(s => s.FullyEditable);
            CameraSummary =
                string.Format(Strings.U_Au_CameraCutsSummary, cam.ShotCount, editable) +
                Strings.U_Au_CameraKnobHint;
        }

        /// <summary>Apply the edited SHOT/TARGET values of every editable camera cut to the selected battle's chunk0.
        /// Each edit is a same-length operand patch (byte-local), so offsets stay stable across cuts; backup-once
        /// before overwriting. Re-reads after each cut so the next patch sees fresh offsets (defensive; they don't move).</summary>
        public void SaveCameraShots()
        {
            if (string.IsNullOrEmpty(SelectedBattle) || _currentCamera == null)
            { CameraStatus = Strings.U_Au_SelectCameraBattle; return; }
            if (CameraShots.Count == 0)
            { CameraStatus = Strings.U_Au_NoEditableCamReq; return; }

            try
            {
                string path = Project_Service.Instance.GetPathBattle(SelectedBattle);
                if (!File.Exists(path)) { CameraStatus = string.Format(Strings.U_Au_BtlNotFound, path); return; }

                byte[] original = File.ReadAllBytes(path);
                byte[] working = original;
                int changed = 0;
                var problems = new List<string>();

                foreach (AuroraCameraRow row in CameraShots)
                {
                    if (!row.Editable) continue;

                    BattleCameraScript_File cam = BattleCameraScript_File.ReadFromBattleBin(SelectedBattle, working);
                    if (row.Index < 0 || row.Index >= cam.Shots.Count) continue;
                    CameraShotRef cur = cam.Shots[row.Index];

                    // SHOT (1-based)
                    if (short.TryParse(row.ShotText, out short newShot))
                    {
                        if (newShot != (short)(cur.Shot.Value ?? 0))
                        {
                            working = cam.WithShot(cur, newShot);
                            changed++;
                            cam = BattleCameraScript_File.ReadFromBattleBin(SelectedBattle, working);
                            cur = cam.Shots[row.Index];
                        }
                    }
                    else problems.Add(string.Format(Strings.U_Au_ShotInvalid, row.Index, row.ShotText));

                    // TARGET (-1 / "none" => 0xFFFF)
                    string t = (row.TargetText ?? string.Empty).Trim();
                    ushort newTgt;
                    if (t.Equals("none", StringComparison.OrdinalIgnoreCase) || t == "-1") newTgt = 0xFFFF;
                    else if (ushort.TryParse(t, out ushort tv)) newTgt = tv;
                    else { problems.Add(string.Format(Strings.U_Au_TargetInvalid, row.Index, row.TargetText)); continue; }

                    if (newTgt != cur.Target.RawValue)
                    {
                        working = cam.WithTarget(cur, newTgt);
                        changed++;
                    }
                }

                if (changed == 0)
                {
                    CameraStatus = problems.Count > 0
                        ? Strings.U_Au_NothingSaved + string.Join("; ", problems)
                        : Strings.U_Au_NoCameraChange;
                    return;
                }

                CameraStatus = string.Format(Strings.U_Au_CameraSaved, changed) + SaveBattleBytes(path, original, working)
                               + (problems.Count > 0 ? "  ⚠ " + string.Join("; ", problems) : "");
                ReloadSelectedBattle();
                RefreshRealGameOverride(); // 🐉 câmera editada → RealGame reflete
            }
            catch (Exception ex) { CameraStatus = string.Format(Strings.U_Au_CameraSaveFailed, ex.Message); }
        }

        /******************************************
         * 3d. CAMERA PARAMS — edit the real angle/distance/position/roll FLOATS (chunk0 camSetPolar/refSetPos/...)
         ******************************************/

        private void PopulateCameraKnobs(BattleCameraSetup_File setup)
        {
            _allKnobs.Clear();
            CameraRoleFilters.Clear();
            CameraRoleFilters.Add(Strings.U_Au_All);

            if (setup.Chunk0Offset < 0 || setup.FloatParams.Count == 0)
            {
                CameraParamsSummary = setup.Calls.Count == 0
                    ? Strings.U_Au_NoParamCalls
                    : Strings.U_Au_NoEditableFloats;
                ApplyCameraKnobFilter();
                return;
            }

            var roles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CameraFloatParam p in setup.FloatParams)
            {
                _allKnobs.Add(new AuroraCameraKnobRow(p));
                foreach (string r in p.Roles) roles.Add(r);
            }
            foreach (string r in roles) CameraRoleFilters.Add(r);

            CameraParamsSummary =
                string.Format(Strings.U_Au_CameraParamsCount, setup.FloatParams.Count, setup.Calls.Count) +
                Strings.U_Au_PolarHint;
            ApplyCameraKnobFilter();
        }

        // 🎬 Encounter opener / CTB seed (chunk0 HookStart — read-only, lazy).
        void PopulateOpenerData(string battleId, byte[] battleBytes)
        {
            OpenerWrites.Clear();
            _currentOpener = BattleEncounterOpener_File.ReadFromBattleBin(battleId, battleBytes);

            if (_currentOpener.HasRecognizedSeed)
            {
                OpenerSummary = _currentOpener.HumanSummary;
                OpenerGuardrail = Strings.U_Au_OpenerGuardrailHook;

                // Build formation mapping from existing lineup.
                OpenerFormationMapping = BuildFormationMapping();

                foreach (EncounterOpenerWriteRow w in _currentOpener.Writes)
                {
                    OpenerWrites.Add(w);
                }
            }
            else
            {
                OpenerSummary = Strings.U_Au_NoOpenerRecognized;
                if (_currentOpener.Notes.Count > 0)
                    OpenerSummary += " " + string.Join(" ", _currentOpener.Notes);
                OpenerGuardrail = Strings.U_Au_GuardrailNoHookStart;
                OpenerFormationMapping = "";
            }
        }

        string BuildFormationMapping()
        {
            if (_currentLineup == null) return "";
            var parts = new List<string>();
            foreach (Battle_FormationSlot? slot in _currentLineup)
            {
                if (slot != null && !slot.IsEmpty)
                    parts.Add($"slot{slot.SlotIndex}=m{slot.DictionaryId:D3}");
            }
            return parts.Count > 0 ? "Formation: " + string.Join(", ", parts) : "";
        }

        // 👥 Hidden companion activation / summon-handoff (m213 lane, read-only, narrow by design).
        void PopulateCompanionActivationData(string battleId, byte[] battleBytes)
        {
            CompanionActivationRows.Clear();
            _currentCompanionActivation = BattleCompanionActivation_File.ReadFromBattleBin(
                battleId,
                battleBytes,
                ResolveMonsterBinForCompanionActivation);

            CompanionActivationSummary = _currentCompanionActivation.HumanSummary;
            CompanionActivationBattleToken = _currentCompanionActivation.BattleTokenLabel;
            CompanionActivationCoverage = _currentCompanionActivation.ActivationCoverageSummary;

            foreach (BattleCompanionActivationRow row in _currentCompanionActivation.Rows)
                CompanionActivationRows.Add(row);

            if (_currentCompanionActivation.HasRecognizedPackage)
            {
                CompanionActivationGuardrail = Strings.U_Au_GuardrailM213;
            }
            else if (_currentCompanionActivation.Notes.Count > 0)
            {
                CompanionActivationGuardrail =
                    "read-only · " + string.Join(" ", _currentCompanionActivation.Notes);
            }
            else
            {
                CompanionActivationGuardrail = Strings.U_Au_NoM213Package;
            }
        }

        byte[]? ResolveMonsterBinForCompanionActivation(int monsterId)
        {
            try
            {
                string path = Project_Service.Instance.GetPathMon(monsterId);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }

        partial void OnCameraRoleFilterChanged(string value) => ApplyCameraKnobFilter();
        partial void OnCameraKnobSearchChanged(string value) => ApplyCameraKnobFilter();

        private void ApplyCameraKnobFilter()
        {
            CameraKnobs.Clear();
            string role = CameraRoleFilter ?? "todos";
            string search = (CameraKnobSearch ?? string.Empty).Trim();

            IEnumerable<AuroraCameraKnobRow> view = _allKnobs;
            if (!string.Equals(role, "todos", StringComparison.OrdinalIgnoreCase))
                view = view.Where(k => k.Role.Contains(role, StringComparison.OrdinalIgnoreCase));
            if (search.Length > 0)
                view = view.Where(k =>
                    k.Role.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    k.PoolIndex.ToString().Contains(search) ||
                    k.ValueText.Contains(search, StringComparison.OrdinalIgnoreCase));

            int shown = 0;
            foreach (AuroraCameraKnobRow k in view) { CameraKnobs.Add(k); if (++shown >= 400) break; }
        }

        /// <summary>Apply every changed camera float knob to the selected battle's chunk0 (byte-local float-pool edits;
        /// same length, backup-once, reload). Editing a float affects every camera cut that references that pool index.</summary>
        public void SaveCameraFloats()
        {
            if (string.IsNullOrEmpty(SelectedBattle) || _currentCameraSetup == null)
            { CameraParamsStatus = Strings.U_Au_SelectCameraBattleParams; return; }
            var changed = _allKnobs.Where(k => k.IsChanged(out _)).ToList();
            if (changed.Count == 0) { CameraParamsStatus = Strings.U_Au_NoCameraParamsChanged; return; }

            try
            {
                string path = Project_Service.Instance.GetPathBattle(SelectedBattle);
                byte[] original = File.ReadAllBytes(path);
                byte[] working = original;
                int applied = 0;
                var problems = new List<string>();
                foreach (AuroraCameraKnobRow k in changed)
                {
                    if (!k.IsChanged(out float nv)) continue;
                    BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(SelectedBattle, working);
                    try { working = setup.WithFloat(k.PoolIndex, nv); applied++; }
                    catch (Exception ex) { problems.Add($"f[{k.PoolIndex}]: {ex.Message}"); }
                }
                if (applied == 0) { CameraParamsStatus = Strings.U_Au_NothingSaved + string.Join("; ", problems); return; }

                CameraParamsStatus = string.Format(Strings.U_Au_CameraParamsSaved, applied) + SaveBattleBytes(path, original, working)
                                     + (problems.Count > 0 ? "  ⚠ " + string.Join("; ", problems) : "");
                ReloadSelectedBattle();
                RefreshRealGameOverride(); // 🐉 câmera editada → RealGame reflete
            }
            catch (Exception ex) { CameraParamsStatus = string.Format(Strings.U_Au_CameraParamsSaveFailed, ex.Message); }
        }

        /******************************************
         * 2. RENDER (MapViewer via export bridge)
         ******************************************/

        public async Task RenderSelectedAsync(bool forceReexport = false)
        {
            (bool ok, string? url, string message) = await RenderSelectedCoreAsync(forceReexport);
            RenderStatus = message;
            if (ok && url != null)
            {
                string launchMsg = AuroraSceneRenderer.Launch(url);
                RenderStatus = $"✅ {message} — {launchMsg}";
            }
        }

        /// <summary>Export the selected scene (if needed), write the overlay JSON and return the EditViewer
        /// deep-link URL (NO browser — the embedded WebView2 navigates to it).</summary>
        public async Task<(bool Ok, string? Url, string Message)> RenderSelectedEmbeddedAsync(bool forceReexport = false)
        {
            (bool ok, string? url, string message) = await RenderSelectedCoreAsync(forceReexport);
            if (ok && url != null)
            {
                LastEmbeddedUrl = url;
                EmbeddedTitle = LastEmbeddedIsBattleStage
                    ? $"EditViewer · {SelectedBattle} (batalha)"
                    : $"EditViewer · {SelectedScene?.Scene.SceneId}";
                EmbeddedStatus = LastEmbeddedIsBattleStage
                    ? Strings.U_Au_EditStageDesc
                    : string.Format(Strings.U_Au_SceneMounted, SelectedBattle);
            }
            return (ok, url, message);
        }

        /// <summary>Shared render pipeline: export + overlay + deep-link URL (with the drag bridge armed).</summary>
        private async Task<(bool Ok, string? Url, string Message)> RenderSelectedCoreAsync(bool forceReexport)
        {
            if (SelectedScene == null) return (false, null, Strings.U_Au_NoSceneSelected);
            if (IsRendering) return (false, null, Strings.U_Au_Rendering);
            FFXProjectEditor.Diagnostics.DebugLog.Info("Aurora.Render", $"scene={SelectedScene.Scene.SceneId} forceReexport={forceReexport}");

            BattleMap_Scene scene = SelectedScene.Scene;
            /* IsMapOnlyVirtual scenes now use map fallback in ExportSceneGeometry */
            AuroraActorsOverlay? overlay = BuildOverlay(scene);

            // Causa A (2026-08-16, Jarvis-Aurora): overlay null = EditViewer abre mapa PURO (0 atores/monstros)
            // sem qualquer pista. Antes o fluxo seguia silencioso e o usuário via "viewer de mapa" sem saber por quê.
            // Agora: (1) auto-seleciona o 1º battle da cena se houver battle mas nenhum selecionado; (2) loga o
            // MOTIVO preciso em [aurora-dbg] (fecha a evidência runtime do Passo 1 do plano); (3) sinaliza na UI.
            if (overlay == null)
            {
                // Fallback: se a cena tem battles mas nenhum está selecionado, tenta o 1º e reconstrói o overlay.
                if (string.IsNullOrEmpty(SelectedBattle) && Battles.Count > 0 && _currentAnchors == null)
                {
                    SelectedBattle = Battles[0];
                    overlay = BuildOverlay(scene);
                }
                if (overlay == null)
                {
                    string reason = DescribeEmptyOverlay();
                    FFXProjectEditor.Diagnostics.DebugLog.Warn("Aurora.Render",
                        string.Format(Strings.U_Au_NoActorOverlay, reason, scene.SceneId, SelectedBattle));
                    // Sinaliza na UI (não é falha de render — o mapa continua abrindo, mas o usuário sabe o porquê).
                    renderEmptyOverlayReason = reason;
                }
                else
                {
                    renderEmptyOverlayReason = null;
                }
            }
            else
            {
                renderEmptyOverlayReason = null;
            }

            // 🐉 Battle-stage emulation (2026-09-15, Jarvis-UI): the EditViewer must show the environment
            // the selected battle actually happens in. For map-only/stub scenes the MapViewer's map/
            // fallback renders the FIELD map (where the party walks) — not the dedicated 1a/ battle stage —
            // and without the Phyre exporter no glTF is produced at all (the user saw an empty grid +
            // anchors at azit00). When the battle resolves to a 0e/ encounter, the battle-correct surface
            // is the PS2-native noclip render with the edit gizmo (?edit=1): real stage, real monsters,
            // drag XZ + S saves the sidecar that "Salvar posições" then commits to the .bin.
            // Real btlmap geometry keeps the MapViewer suite (HD battle map + anchor/zone/camera tools).
            bool battleResolves = !string.IsNullOrEmpty(SelectedBattle)
                                  && Aurora3DLauncher.TryResolveEncounter(SelectedBattle, out _);
            bool hasRealGeometry = !scene.IsMapOnlyVirtual && !scene.IsStub && scene.HasPrimaryDae;
            if (battleResolves && (!hasRealGeometry || !AuroraSceneRenderer.CanExportSceneGeometry)
                && TryBuildBattleEditViewerUrl(SelectedBattle!, out string? editUrl, out string editStatus))
            {
                LastEmbeddedIsBattleStage = true;
                RenderStatus = $"✅ {editStatus}";
                return (true, editUrl, editStatus);
            }
            LastEmbeddedIsBattleStage = false;

            AuroraDragBridge.EnsureStarted(); // bring up the web→editor leg so drag-to-place can POST coords back
            // let the viewer's own "Salvar posições" button trigger the write (marshal back to the UI thread).
            AuroraDragBridge.SaveRequested = battleId =>
            {
                string? expectedBattle = SelectedBattle;
                if (!string.Equals(battleId, expectedBattle, StringComparison.Ordinal))
                    return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (string.Equals(SelectedBattle, battleId, StringComparison.Ordinal))
                        SaveDraggedPositions();
                });
            };

            IsRendering = true;
            RenderStatus = forceReexport
                ? string.Format(Strings.U_Au_ReExporting, scene.SceneId)
                : SelectedScene.IsMounted
                    ? string.Format(Strings.U_Au_OpeningInViewer, scene.SceneId)
                    : string.Format(Strings.U_Au_Exporting, scene.SceneId);
            try
            {
                AuroraSceneRenderer.RenderResult result =
                    await Task.Run(() => AuroraSceneRenderer.RenderScene(scene, overlay, forceReexport)).ConfigureAwait(false);

                if (!result.Ok)
                {
                    if (battleResolves && TryBuildBattleEditViewerUrl(SelectedBattle!, out string? fbUrl, out string fbStatus))
                    {
                        LastEmbeddedIsBattleStage = true;
                        RenderStatus = $"✅ {fbStatus}";
                        return (true, fbUrl, fbStatus);
                    }
                    FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Render", result.Message);
                    RenderStatus = $"❌ {result.Message}";
                    return (false, null, result.Message);
                }

                SelectedScene.RefreshMountStatus();
                RefreshMountCountsInSummary();
                RefreshTextureBindSummary(scene);

                // Hand the drag-bridge port to the viewer so its drag-to-place POSTs reach THIS editor
                // (the port is OS-assigned/free; never hardcode — a stray python http.server squats fixed ports).
                string url = result.DeepLinkUrl!;
                if (AuroraDragBridge.IsRunning && AuroraDragBridge.SetAllowedOrigin(url))
                {
                    url += (url.Contains('?') ? "&" : "?") + "drag=" + AuroraDragBridge.Port;
                    url += "&dragToken=" + Uri.EscapeDataString(AuroraDragBridge.RequestToken);
                }
                RenderStatus = renderEmptyOverlayReason == null
                    ? $"✅ {result.Message}"
                    : string.Format(Strings.U_Au_NoActorOverlayWarn2, result.Message, renderEmptyOverlayReason);
                return (true, url, result.Message);
            }
            catch (Exception ex)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Render", "render exception", ex);
                RenderStatus = $"❌ Render failed: {ex.Message}";
                return (false, null, ex.Message);
            }
            finally
            {
                IsRendering = false;
            }
        }

        /// <summary>🐉 Open the selected battle in the RealGame (noclip PS2-native render) — INSIDE the editor:
        /// returns the deep-link URL for the embedded WebView2 (override of 0e/ already applied).</summary>
        public string? OpenRealGameEmbedded()
        {
            if (string.IsNullOrEmpty(SelectedBattle))
            {
                if (OperatingSystem.IsLinux()) CloseRealGamePreview();
                Battle3DStatus = Strings.U_Au_SelectBattle;
                return null;
            }
            string? url = Aurora3DLauncher.BuildBattleUrlWithoutOverride(
                SelectedBattle,
                out string status,
                out int encounterId,
                edit: true);
            if (url != null && ViewerHubService.Server is { IsRunning: true } server)
            {
                string overrideStatus = _realGameOverlayOwner.Update(
                    true,
                    server,
                    () => Aurora3DLauncher.CreateBattleOverride(SelectedBattle, encounterId, server),
                    Strings.U_Vh_OverrideDisabled);
                status = $"{overrideStatus} · {status}";
                if (_realGameOverlayOwner.Current is not { Success: true })
                    url = null;
            }
            else if (OperatingSystem.IsLinux())
            {
                CloseRealGamePreview();
            }
            Battle3DStatus = url != null ? $"🐉 {status}" : $"❌ {status}";
            if (url != null)
            {
                _realGameBattleId = SelectedBattle;
                url = PrepareNativePositionEditor(SelectedBattle, url);
                LastEmbeddedUrl = url;
                EmbeddedTitle = $"RealGame · {SelectedBattle}";
                EmbeddedStatus = Strings.U_Au_RenderPs2NativeLabel;
            }
            return url;
        }

        /// <summary>Battle-correct EditViewer URL: the PS2-native rendered battle stage with the position
        /// gizmo (<c>?edit=1</c>). Shares the RealGame overlay owner — both viewers show the SAME staged
        /// 0e/ bytes, and any sidecar edits already on disk are re-mapped so the page shows them.</summary>
        private bool TryBuildBattleEditViewerUrl(string battleId, out string? url, out string status)
        {
            url = Aurora3DLauncher.BuildBattleUrlWithoutOverride(
                battleId, out status, out int encId, edit: true);
            if (url != null && ViewerHubService.Server is { IsRunning: true } server)
            {
                string overrideStatus = _realGameOverlayOwner.Update(
                    true,
                    server,
                    () => Aurora3DLauncher.CreateBattleOverride(battleId, encId),
                    Strings.U_Vh_OverrideDisabled);
                // Re-map a pre-existing sidecar edit file so the page renders prior deltas on load
                // (the server only auto-registers sidecars present at construction time).
                string sidecar = Path.Combine(
                    NoclipOverlayStore.ResolveEditsRoot(ViewerHubService.ViewerDataRoot),
                    $"{encId}.json");
                if (File.Exists(sidecar))
                    server.TryMapExactFile($"/data/FinalFantasyX/edits/{encId}.json", sidecar);
                status = $"{overrideStatus} · {status} · edit=1";
                if (_realGameOverlayOwner.Current is not { Success: true })
                    url = null;
            }
            if (url != null)
            {
                _realGameBattleId = battleId; // RefreshRealGameOverride keeps this preview's bytes current
                url = PrepareNativePositionEditor(battleId, url);
                status = string.Format(Strings.U_Au_EditViewerBattleStage, battleId, status);
            }
            FFXProjectEditor.Diagnostics.DebugLog.Info("aurora-chamber.EditStage",
                $"battleId={battleId} url={(url != null ? "ok" : "null")} :: {status}");
            return url != null;
        }

        private string PrepareNativePositionEditor(string battleId, string url)
        {
            if (ViewerHubService.Server is not { IsRunning: true } server)
                throw new InvalidOperationException(Strings.U_Noclip_Reopen);
            var names = SelectedScene == null ? null : BuildOverlay(SelectedScene.Scene)?.Anchors
                .Where(a => a.Role == "monster_live").OrderBy(a => a.Index)
                .Select(a => a.MonsterName ?? a.Label);
            _nativePositionSession = new AuroraNativePositionSession(
                battleId, Project_Service.Instance.GetPathBattle(battleId), names);
            string token = server.ConfigureNativePositions(HandleNativePositionRequest);
            var builder = new UriBuilder(url);
            builder.Query = builder.Query.TrimStart('?') +
                "&auroraBattle=" + Uri.EscapeDataString(battleId) +
                "&bridgeToken=" + Uri.EscapeDataString(token) +
                "&lang=" + System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return builder.Uri.AbsoluteUri;
        }

        private async Task<(bool Ok, string Json)> HandleNativePositionRequest(string route, string body)
        {
            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                string battleId = doc.RootElement.GetProperty("battleId").GetString() ?? "";
                var session = _nativePositionSession;
                if (session == null || SelectedBattle != battleId || _realGameBattleId != battleId ||
                    !session.Matches(battleId, Project_Service.Instance.GetPathBattle(battleId)))
                    return (false, System.Text.Json.JsonSerializer.Serialize(
                        new AuroraNativePositionSession.Reply(false, Strings.U_Noclip_Reopen), AuroraNativePositionSession.JsonOptions));
                if (route == "/position-session") return (true, session.Describe());

                var request = System.Text.Json.JsonSerializer.Deserialize<AuroraNativePositionSession.Request>(body, AuroraNativePositionSession.JsonOptions);
                var reply = request == null
                    ? new AuroraNativePositionSession.Reply(false, Strings.U_Noclip_InvalidPositions)
                    : session.Save(request);
                DragSaveStatus = reply.Message;
                if (reply.Ok)
                {
                    OnSelectedBattleChanged(battleId);
                    if (Aurora3DLauncher.TryResolveEncounter(battleId, out int id) && ViewerHubService.Server is { IsRunning: true } server)
                    {
                        ClearConsumedSidecarPositions(id, request!.Positions.Select(p => p.Slot).ToList());
                        _realGameOverlayOwner.Update(true, server,
                            () => Aurora3DLauncher.CreateBattleOverride(battleId, id), Strings.U_Vh_OverrideDisabled);
                    }
                    Battle3DStatus = reply.Message;
                }
                return (reply.Ok, System.Text.Json.JsonSerializer.Serialize(reply, AuroraNativePositionSession.JsonOptions));
            });
        }

        /// <summary>Re-apply the RealGame override with the CURRENT battle bytes after a save (drag/camera/add).
        /// Returns true when an override is active (caller should reload the embedded viewer).</summary>
        public bool RefreshRealGameOverride()
        {
            if (OperatingSystem.IsLinux() && _realGameOverlayOwner.Current == null)
            {
                CloseRealGamePreview();
                return false;
            }
            if (string.IsNullOrEmpty(_realGameBattleId)) return false;
            if (!Aurora3DLauncher.TryResolveEncounter(_realGameBattleId, out int encounterId) ||
                ViewerHubService.Server is not { IsRunning: true } server)
            {
                if (OperatingSystem.IsLinux()) CloseRealGamePreview();
                return false;
            }
            string msg = _realGameOverlayOwner.Update(
                true,
                server,
                () => Aurora3DLauncher.CreateBattleOverride(_realGameBattleId, encounterId, server),
                Strings.U_Vh_OverrideDisabled);
            if (OperatingSystem.IsLinux() && _realGameOverlayOwner.Current == null)
            {
                Battle3DStatus = msg;
                CloseRealGamePreview();
                return false;
            }
            Battle3DStatus = string.Format(Strings.U_Au_OverrideReapplied2, msg);
            EmbeddedReloadRequested?.Invoke();
            return true;
        }

        /// <summary>Release the native Aurora process family or Windows staged previews.
        /// MAINT: Other exact owners may remain active; this does not rewrite selected vanilla files.</summary>
        public void RestoreBattle3DOverrides()
        {
            if (OperatingSystem.IsLinux())
            {
                int released = Aurora3DLauncher.RestoreAllOverrides(out string memoryDetail);
                _realGameOverlayOwner.Clear();
                _realGameBattleId = null;
                Battle3DStatus = released > 0
                    ? string.Format(Strings.U_Au_MemoryPreviewsCleared, released, memoryDetail)
                    : Strings.U_Au_NoMemoryPreview;
                return;
            }
            _realGameOverlayOwner.Clear();
            int n = Aurora3DLauncher.RestoreAllOverrides(out string detail);
            _realGameBattleId = null;
            Battle3DStatus = n > 0
                ? string.Format(Strings.U_Au_EncounterRestored2, n, detail)
                : Strings.U_Au_NoPendingOverride2;
        }

        internal void CloseRealGamePreview()
        {
            _nativePositionSession = null;
            ViewerHubService.Server?.ClearNativePositions(this);
            _realGameOverlayOwner.Clear();
            _realGameBattleId = null;
        }

        public Task ReloadSelectedSceneAsync() => RenderSelectedAsync(forceReexport: true);

        public async Task RemountSelectedSceneAsync()
        {
            if (SelectedScene == null || SelectedScene.Scene.IsMapOnlyVirtual)
            {
                RenderStatus = Strings.U_Au_SelectHdSceneRemount;
                return;
            }
            if (IsRendering) return;
            IsRendering = true;
            RenderStatus = string.Format(Strings.U_Au_Remounting, SelectedScene.Scene.SceneId);
            try
            {
                AuroraSceneRenderer.RenderResult result = await Task.Run(() =>
                    AuroraSceneRenderer.ExportSceneGeometry(SelectedScene.Scene, forceReexport: true)).ConfigureAwait(false);
                SelectedScene.RefreshMountStatus();
                RefreshMountCountsInSummary();
                RefreshTextureBindSummary(SelectedScene.Scene);
                RenderStatus = result.Ok ? $"✅ {result.Message}" : $"❌ {result.Message}";
            }
            catch (Exception ex) { RenderStatus = $"❌ {ex.Message}"; }
            finally { IsRendering = false; }
        }

        public void OpenSceneOutputFolder()
        {
            if (SelectedScene == null || SelectedScene.Scene.IsMapOnlyVirtual)
            {
                RenderStatus = Strings.U_Au_NoExportFolder;
                return;
            }
            string? dir = AuroraSceneRenderer.GetSceneOutputDir(SelectedScene.Scene);
            if (dir == null || !Directory.Exists(dir))
            {
                RenderStatus = Strings.U_Au_ExportDirMissing2;
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
                RenderStatus = string.Format(Strings.U_Au_FolderOpened, dir);
            }
            catch (Exception ex) { RenderStatus = string.Format(Strings.U_Au_FolderOpenFailed, ex.Message); }
        }

        public void OpenMapViewerWebFolder()
        {
            string? index = AuroraSceneRenderer.ResolveMapViewerIndex();
            if (index == null) { RenderStatus = Strings.U_Au_EditViewerNotFound2; return; }
            string dir = Path.GetDirectoryName(index)!;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
                RenderStatus = string.Format(Strings.U_Au_EditViewerDir, dir);
            }
            catch (Exception ex) { RenderStatus = string.Format(Strings.U_Au_Failed, ex.Message); }
        }

        public async Task RemountAllScenesAsync()
        {
            if (IsMountingAll || IsRendering) return;
            IsMountingAll = true;
            RenderStatus = Strings.U_Au_RemountingAll;
            try
            {
                var progress = new Progress<string>(msg => RenderStatus = msg);
                AuroraSceneRenderer.MountAllResult result =
                    await AuroraSceneRenderer.MountAllMissingAsync(forceReexport: true, progress).ConfigureAwait(false);
                foreach (AuroraSceneRow row in _allScenes)
                    row.RefreshMountStatus();
                RefreshMountCountsInSummary();
                if (SelectedScene != null && !SelectedScene.Scene.IsMapOnlyVirtual)
                    RefreshTextureBindSummary(SelectedScene.Scene);
                RenderStatus =
                    string.Format(Strings.U_Au_RemountAllDone, result.Mounted, result.Skipped, result.Failed);
            }
            catch (Exception ex) { RenderStatus = $"❌ {ex.Message}"; }
            finally { IsMountingAll = false; }
        }

        /// <summary>Pre-export every BIANCA scene that is not yet mounted (glTF + aurora-catalog.json). Skips map-only
        /// virtual rows and already-cached scenes. Does not open the browser — use Render for that.</summary>
        public async Task MountAllMissingAsync()
        {
            if (IsMountingAll || IsRendering) return;

            IsMountingAll = true;
            RenderStatus = Strings.U_Au_MountingAllMissing;
            try
            {
                var progress = new Progress<string>(msg => RenderStatus = msg);
                AuroraSceneRenderer.MountAllResult result =
                    await AuroraSceneRenderer.MountAllMissingAsync(forceReexport: false, progress);

                foreach (AuroraSceneRow row in _allScenes)
                    row.RefreshMountStatus();
                RefreshMountCountsInSummary();

                string errNote = result.Errors.Count > 0
                    ? string.Format(Strings.U_Au_ErrorsNote, string.Join("; ", result.Errors.Take(3))) + (result.Errors.Count > 3 ? "…" : "")
                    : "";
                RenderStatus =
                    string.Format(Strings.U_Au_BatchMountDone, result.Mounted, result.Skipped, result.Failed, result.Total, errNote);
            }
            catch (Exception ex)
            {
                RenderStatus = string.Format(Strings.U_Au_BatchMountFailed, ex.Message);
            }
            finally
            {
                IsMountingAll = false;
            }
        }

        private void RefreshMountCountsInSummary()
        {
            if (_catalog == null) return;
            int mounted = _allScenes.Count(r => r.IsMounted && !r.Scene.IsMapOnlyVirtual);
            int hdScenes = _allScenes.Count(r => !r.Scene.IsMapOnlyVirtual);
            int stubs = _catalog.Scenes.Count(s => s.IsStub);
            string btlmapRoot = AuroraSceneRenderer.ResolveBtlmapRoot();
            CatalogSummary =
                string.Format(Strings.U_Au_CatalogSummary, _catalog.AreaCount, _catalog.SceneCount, _catalog.TotalPrimaryBytes / (1024 * 1024.0), stubs, mounted, hdScenes, btlmapRoot);
        }

        /// <summary>Build the actor overlay from the currently-decoded anchors (monsters-live + party). Null if none.</summary>
        private AuroraActorsOverlay? BuildOverlay(BattleMap_Scene scene)
        {
            if (_currentAnchors == null || _currentAnchors.Areas.Count == 0)
                return null;

            var overlay = new AuroraActorsOverlay
            {
                Scene = scene.SceneId,
                MapKey = scene.MapKey,
                BattleId = SelectedBattle ?? "",
            };

            BattleArena_AreaRecord area0 = _currentAnchors.Areas[0];
            AddOverlayGroup(overlay, area0, BattleArena_AnchorRole.MonsterLive);
            AddOverlayGroup(overlay, area0, BattleArena_AnchorRole.PartyFront);
            overlay.Camera = BuildCameraMarker();
            overlay.Zones = BuildEncounterZones(scene.MapKey, out string zonesNote);
            overlay.ZonesNote = zonesNote;
            BattleMap_ExportQuality? q = AuroraSceneRenderer.GetSceneExportQuality(scene);
            if (q != null)
            {
                overlay.ExportQuality = new AuroraExportQualitySnapshot
                {
                    SubmeshCount = q.SubmeshCount,
                    BoundSubmeshCount = q.BoundSubmeshCount,
                    Label = q.QualityLabel,
                    Breakdown = q.BreakdownLabel,
                };
            }

            return (overlay.Anchors.Count > 0 || overlay.Camera != null) ? overlay : null;
        }

        /// <summary>Diagnóstico preciso de POR QUE o BuildOverlay não produziu âncoras — para o log [aurora-dbg]
        /// e a sinalização de UI (Causa A). Usa o estado decodificado atual, nunca lança.</summary>
        private string DescribeEmptyOverlay()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
                return Strings.U_Au_NoProjectLoaded2;
            if (string.IsNullOrEmpty(SelectedBattle))
                return Strings.U_Au_NoBattleSelected2;
            if (_currentAnchors == null)
            {
                // O battle não expôs chunk3 — tenta arriscar o motivo com o que existe.
                if (_currentCameraSetup?.Establishing is { } est && (est.HasRef || est.HasPolar))
                    return string.Format(Strings.U_Au_NoChunk3Anchors2, SelectedBattle);
                return string.Format(Strings.U_Au_NoChunk3_2, SelectedBattle);
            }
            if (_currentAnchors.Areas.Count == 0)
                return string.Format(Strings.U_Au_Chunk3NoAreas2, SelectedBattle);
            return Strings.U_Au_AnchorsEmpty2;
        }

        /// <summary>Build the establishing-camera 3D marker: the look-at REF (exact, scene frame) + the IDA-proven
        /// camSetPolar eye (0x6004 only) + the angle/distance label. Null when none.</summary>
        private AuroraCameraMarker? BuildCameraMarker()
        {
            CameraEstablishingShot? e = _currentCameraSetup?.Establishing;
            if (e == null || (!e.HasRef && !e.HasPolar)) return null;

            var m = new AuroraCameraMarker();
            if (e.HasRef) { m.HasRef = true; m.RefX = e.RefX; m.RefY = e.RefY; m.RefZ = e.RefZ; }
            if (e.HasPolar)
            {
                m.Angle = e.PolarHorizontalAngle;
                m.Distance = e.PolarDistance;
                float bx = e.HasRef ? e.RefX : 0f, by = e.HasRef ? e.RefY : 0f, bz = e.HasRef ? e.RefZ : 0f;
                (float x, float y, float z) = BattleCameraSetup_File.PolarToEye(
                    bx, by, bz, e.PolarHorizontalAngle, e.PolarElevationAngle, e.PolarDistance);
                m.HasEye = true;
                m.EyeX = x;
                m.EyeY = y;
                m.EyeZ = z;
                m.EyeDraggable = e.CanEditPolarEye;
            }
            m.Label = e.HasPolar
                ? $"camSetPolar IDA · horiz {e.PolarHorizontalAngle:0.#}° · elev {e.PolarElevationAngle:0.#}° · dist {e.PolarDistance:0.#}"
                : Strings.U_Au_CamRefNoPolarEdit;
            return m;
        }

        /// <summary>Load the PS2 field encounter zones (mapout.vpa MAP1) for a map key, e.g. "azit03" →
        /// &lt;master&gt;/jppc/map/azit/azit03/bin/mapout.vpa. Read-only, best-effort; coordinates are field-local
        /// (the overlay draws them on the debug plane with an honest PS2-field badge — never scene-aligned).</summary>
        private static List<AuroraEncounterZoneSnapshot>? BuildEncounterZones(string mapKey, out string note)
        {
            note = "";
            if (string.IsNullOrWhiteSpace(mapKey) || mapKey.Length < 6)
                return null;

            string? masterRoot = Project_Service.Instance?.ProjectPath;
            string area = mapKey[..4];
            string? vpaPath = null;
            if (!string.IsNullOrWhiteSpace(masterRoot))
            {
                string candidate = Path.Combine(masterRoot, "jppc", "map", area, mapKey, "bin", "mapout.vpa");
                if (File.Exists(candidate)) vpaPath = candidate;
            }
            if (vpaPath == null)
            {
                // An optional reference corpus must be explicit. Product builds never guess a
                // developer drive or walk outside the user-selected/project roots.
                string? referenceMaster = Environment.GetEnvironmentVariable("FFX_VANILLA_MASTER");
                if (!string.IsNullOrWhiteSpace(referenceMaster) && Directory.Exists(referenceMaster))
                {
                    string fallback = Path.Combine(referenceMaster, "jppc", "map", area, mapKey, "bin", "mapout.vpa");
                    if (File.Exists(fallback)) vpaPath = fallback;
                }
            }
            if (vpaPath == null)
            {
                note = string.Format(Strings.U_Au_MapoutVpaMissing2, mapKey, area, mapKey);
                return null;
            }

            MapoutVpa_EncounterZones.ParseResult result = MapoutVpa_EncounterZones.ParseFile(vpaPath);
            note = string.Format(Strings.U_Au_ZonesNote, Path.GetFileName(vpaPath), result.Status, result.Zones.Count);
            if (result.Status != MapoutVpa_EncounterZones.MapoutZoneStatus.Ok || result.Zones.Count == 0)
                return null;

            var zones = new List<AuroraEncounterZoneSnapshot>(result.Zones.Count);
            foreach (MapoutVpa_EncounterZones.EncounterZone z in result.Zones)
            {
                var snap = new AuroraEncounterZoneSnapshot
                {
                    EntryKey = z.EntryKey,
                    Tag = z.Tag,
                    GroupIndex = z.GroupIndex,
                    MinX = z.MinX,
                    MaxX = z.MaxX,
                    MinZ = z.MinZ,
                    MaxZ = z.MaxZ,
                };
                foreach (MapoutVpa_EncounterZones.ZonePolygon poly in z.Polygons)
                {
                    var p = new AuroraZonePolygonSnapshot();
                    foreach ((float px, float pz) in poly.Vertices)
                    {
                        p.Xs.Add(px);
                        p.Zs.Add(pz);
                    }
                    snap.Polygons.Add(p);
                }
                zones.Add(snap);
            }
            return zones;
        }

        private void AddOverlayGroup(AuroraActorsOverlay overlay, BattleArena_AreaRecord area, BattleArena_AnchorRole role)
        {
            BattleArena_AnchorGroup? g = area[role];
            if (g == null) return;
            string roleKey = role switch
            {
                BattleArena_AnchorRole.MonsterLive => "monster_live",
                BattleArena_AnchorRole.PartyFront => "party",
                _ => role.ToString().ToLowerInvariant(),
            };
            bool isMonster = role == BattleArena_AnchorRole.MonsterLive;
            foreach (BattleArena_Anchor a in g.Anchors)
            {
                var anchor = new AuroraActorAnchor
                {
                    Role = roleKey,
                    Index = a.Index,
                    X = a.X, Y = a.Y, Z = a.Z, W = a.W,
                    Label = $"{roleKey}[{a.Index}]",
                };

                // Bind the actual monster model: formation lineup (chunk2) slot i ↔ monster-live anchor i.
                // Model URL is server-root-absolute (the http.server roots at the repo) so app.js's page can fetch it.
                if (isMonster && _currentLineup != null && a.Index >= 0 && a.Index < _currentLineup.Count)
                {
                    Battle_FormationSlot slot = _currentLineup[a.Index];
                    if (!slot.IsEmpty && slot.DictionaryId >= 0)
                    {
                        anchor.MonsterId = slot.DictionaryId;
                        anchor.MonsterName = slot.MonsterName;
                        anchor.Scale = AuroraSceneRenderer.GetMonsterScale(slot.DictionaryId) ?? 1f;
                        anchor.FlipX = AuroraSceneRenderer.GetMonsterFlipX(slot.DictionaryId);
                        anchor.Model = ResolveMonsterModelUrl(slot.DictionaryId);
                        anchor.Label = $"{slot.MonsterName} (m{slot.DictionaryId:D3})";
                        // Named animation cycles from the .ath catalog (game symbol names — e.g. "m117_bat_pos_loop01_s").
                        foreach (MonsterAthAnimCatalog.AnimEntry cyc in MonsterAthAnimCatalog.GetCycles(slot.DictionaryId.ToString()))
                            anchor.Anims.Add(new AuroraAnimEntry { Name = cyc.Name, Id = cyc.Id });
                    }
                }

                overlay.Anchors.Add(anchor);
            }
        }

        private static string? ResolveMonsterModelUrl(int monsterId)
        {
            string? urlRoot = Environment.GetEnvironmentVariable("FFX_AURORA_MODEL_URL_ROOT");
#if FFX_INCLUDE_DEVTOOLS
            // Preserve the source-worktree viewer route for developer builds only. Official product
            // builds require an explicitly hosted/model-mapped URL root and otherwise render anchors.
            urlRoot ??= "/RuntimeTools/FFXModelAssets/chr";
#endif
            if (string.IsNullOrWhiteSpace(urlRoot))
                return null;

            string root = urlRoot.Trim().TrimEnd('/');
            return $"{root}/m{monsterId:D3}/m{monsterId:D3}_animated.gltf";
        }

        /******************************************
         * 3. COORDINATES — export the anchors JSON (durable artifact for Encounter Authoring)
         ******************************************/

        public void ExportAnchorsJson()
        {
            if (SelectedScene == null || _currentAnchors == null || string.IsNullOrEmpty(SelectedBattle))
            {
                CoordExportStatus = Strings.U_Au_SelectBattleAnchors2;
                return;
            }

            AuroraActorsOverlay? overlay = BuildOverlay(SelectedScene.Scene);
            if (overlay == null || overlay.Anchors.Count == 0)
            {
                CoordExportStatus = Strings.U_Au_NoPositionableAnchors2;
                return;
            }

            try
            {
                string? repo = AuroraSceneRenderer.ResolveRepoRoot();
                string dir = repo != null
                    ? Path.Combine(repo, "work", "aurora_exports")
                    : Path.Combine(AppContext.BaseDirectory, "aurora_exports");
                string file = Path.Combine(dir, $"aurora-actors.{SelectedBattle}.json");
                AuroraSceneRenderer.WriteActorsOverlay(file, overlay);
                CoordExportStatus = string.Format(Strings.U_Au_AnchorsExported2, overlay.Anchors.Count, file);
            }
            catch (Exception ex)
            {
                CoordExportStatus = string.Format(Strings.U_Au_ExportFailed, ex.Message);
            }
        }
    }

    /// <summary>A BIANCA scene as a list row.</summary>
    internal sealed partial class AuroraSceneRow : ObservableObject
    {
        public AuroraSceneRow(BattleMap_Scene scene)
        {
            Scene = scene;
            RefreshMountStatus();
        }

        public BattleMap_Scene Scene { get; }

        [ObservableProperty] private bool isMounted;
        [ObservableProperty] private string mountBadge = "";
        [ObservableProperty] private string textureQualityLabel = "";

        /// <summary>Badges compactos da lista (em vez da parede de texto): [ gLTF ] [ N tex ] [ STUB ] [ map-only ].</summary>
        public IReadOnlyList<AuroraBadge> Badges { get; private set; } = Array.Empty<AuroraBadge>();

        public void RefreshMountStatus()
        {
            if (Scene.IsMapOnlyVirtual)
            {
                IsMounted = false;
                MountBadge = "map-only";
                TextureQualityLabel = "";
                Badges = new[] { new AuroraBadge("map-only", "muted") };
                return;
            }

            IsMounted = AuroraSceneRenderer.IsSceneMounted(Scene);
            MountBadge = IsMounted ? Strings.U_Au_FieldDetailMounted : Strings.U_Au_FieldDetailPending;
            BattleMap_ExportQuality? q = AuroraSceneRenderer.GetSceneExportQuality(Scene);
            TextureQualityLabel = q?.QualityLabel ?? "";
            TextureBreakdownLabel = q?.BreakdownLabel ?? "";

            var badges = new List<AuroraBadge>
            {
                new(IsMounted ? Strings.U_Au_BadgeGltf : Strings.U_Au_BadgeExportPending, IsMounted ? "ok" : "warn"),
                new($"{Scene.TextureCount} tex", "info"),
            };
            if (q is { SubmeshCount: > 0 })
                badges.Add(new AuroraBadge($"{q.BoundSubmeshCount}/{q.SubmeshCount} submeshes", q.UnboundSubmeshCount == 0 ? "ok" : "warn"));
            if (Scene.HasTwoDSlice)
                badges.Add(new AuroraBadge("+2d", "info"));
            if (Scene.IsStub)
                badges.Add(new AuroraBadge("STUB", "warn"));
            Badges = badges;
        }

        [ObservableProperty] private string textureBreakdownLabel = "";

        public string DisplayName => Scene.IsMapOnlyVirtual
            ? $"{Scene.MapKey} (map-only)"
            : Scene.SceneId;
        public string Summary => Scene.IsMapOnlyVirtual
            ? string.Format(Strings.U_Au_MapNoHdScene2, Scene.MapKey)
            : string.Format(Strings.U_Au_AreaMapDetail2, Scene.AreaCode, Scene.MapKey, MountBadge);
        public string DetailSummary => Scene.IsMapOnlyVirtual
            ? Strings.U_Au_OnlyBtlOnDisk2
            : $"{Scene.PrimaryDaeSize / 1024.0:0.#} KB · {Scene.TextureCount} tex" +
              $"{(Scene.HasTwoDSlice ? " · +2d" : "")}{(Scene.IsStub ? " · STUB" : "")}" +
              (IsMounted ? " · " + Strings.U_Au_GltfReady : " · " + Strings.U_Au_ExportPending) +
              (TextureQualityLabel.Length > 0 ? $"\n{TextureQualityLabel}" : "") +
              (TextureBreakdownLabel.Length > 0 ? $"\n{TextureBreakdownLabel}" : "");
    }

    /// <summary>An editable chunk0 camera cut (camReq) as a list row. SHOT = angle (1-based); TARGET = framed actor
    /// (-1/"none" == 0xFFFF). Non-literal operands are surfaced read-only (Editable == false).</summary>
    internal partial class AuroraCameraRow : ObservableObject
    {
        public AuroraCameraRow(CameraShotRef shot)
        {
            ShotRef = shot;
            Index = shot.Index;
            Offset = shot.Offset;
            Editable = shot.FullyEditable;
            shotText = (shot.Shot.Value ?? 0).ToString();
            targetText = shot.Target.RawValue == 0xFFFF ? "none" : shot.Target.RawValue.ToString();
        }

        public CameraShotRef ShotRef { get; }
        public int Index { get; }
        public int Offset { get; }
        public bool Editable { get; }

        [ObservableProperty] private string shotText;    // 1-based shot index (engine uses shot-1)
        [ObservableProperty] private string targetText;  // framed actor id; "none"/-1 == 0xFFFF

        public string Header => $"camReq #{Index}  @0x{Offset:X4}";
    }

    /// <summary>An editable camera FLOAT knob (a chunk0 float-pool entry fed to camera calls: angle/distance/pos/roll).
    /// Editing it is byte-local (the pool float) and affects every camera cut that references this pool index.</summary>
    internal partial class AuroraCameraKnobRow : ObservableObject
    {
        public AuroraCameraKnobRow(CameraFloatParam p)
        {
            PoolIndex = p.PoolIndex;
            Role = p.RolesLabel;
            UseCount = p.UseCount;
            OriginalValue = p.Value;
            valueText = p.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        public int PoolIndex { get; }
        public string Role { get; }
        public int UseCount { get; }
        public float OriginalValue { get; }
        [ObservableProperty] private string valueText;

        public string Header => $"{Role}  ·  f[{PoolIndex}]  ·  " + string.Format(Strings.U_Au_CutsCount, UseCount);

        /// <summary>True when ValueText parses to a float that differs (bit-exact) from the original.</summary>
        public bool IsChanged(out float newValue)
        {
            newValue = OriginalValue;
            if (!float.TryParse(ValueText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v)) return false;
            newValue = v;
            return BitConverter.SingleToInt32Bits(v) != BitConverter.SingleToInt32Bits(OriginalValue);
        }
    }

    /// <summary>A decoded chunk3 anchor as a list row.</summary>
    internal sealed class AuroraAnchorRow
    {
        public AuroraAnchorRow(BattleArena_Anchor anchor, bool isMonsterLive, string? monsterName = null)
        {
            Anchor = anchor;
            IsMonsterLive = isMonsterLive;
            MonsterName = monsterName;
        }

        public BattleArena_Anchor Anchor { get; }
        public bool IsMonsterLive { get; }
        public string? MonsterName { get; }

        public string RoleLabel => Anchor.Role switch
        {
            BattleArena_AnchorRole.MonsterLive => !string.IsNullOrWhiteSpace(MonsterName) ? $"🔴 {MonsterName}" : Strings.U_Au_MonsterLiveLabel2,
            BattleArena_AnchorRole.PartyFront => "🔵 party (front)",
            BattleArena_AnchorRole.PartyBack => "party (back)",
            BattleArena_AnchorRole.Aeon => "🟦 aeon",
            BattleArena_AnchorRole.MonsterStagingA => Strings.U_Au_MonsterStagingA,
            BattleArena_AnchorRole.MonsterStagingB => Strings.U_Au_MonsterStagingB,
            BattleArena_AnchorRole.Camera => "🎥 camera",
            _ => Anchor.Role.ToString(),
        };

        public string IndexLabel => $"[{Anchor.Index}]";
        public string CoordLabel =>
            Anchor.IsPositionLike
                ? $"X {Anchor.X,8:0.00}   Y {Anchor.Y,7:0.00}   Z {Anchor.Z,8:0.00}"
                : $"X {Anchor.X,8:0.00}   Y {Anchor.Y,7:0.00}   Z {Anchor.Z,8:0.00}   W {Anchor.W:0.00}";
    }

    /// <summary>Badge visual de lista (texto + tom: ok / warn / info / muted).</summary>
    internal sealed record AuroraBadge(string Text, string Kind);
}
