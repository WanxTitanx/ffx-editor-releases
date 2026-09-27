using FFXProjectEditor.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.TreasureMap;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

// ── TreasureMapEditor_DataModel ────────────────────────────────────────────────────────
// ViewModel for the Treasure Map editor. Flows:
//   LoadIndex()  -> TreasureMapIndexBuilder.Build + ChestLocationIndexBuilder.Build (cached
//                   once here; NOT rebuilt on every field selection - performance).
//   OnSelectedFieldChanged() -> filters the cached projected locations for the field,
//                   builds TreasureChestRow (reward text + placeholder + edit state).
//   Save()/Discard() -> apply edits to takara.bin via TreasureCatalogSaveTransaction.
//
// A TreasureChestRow has a real position (Location.GuideX/Z from ATEL constants) or a
// bounds placeholder (PlaceholderX/Z = center of the active map model) for chests whose
// position is runtime-only. RewardText is the real takara.bin reward description.
// Debug logging category: "TreasureMap.UI" (see docs/ai/DEBUG_LOGGING_MAP_2026-08-02.md).
// ──────────────────────────────────────────────────────────────────────────────────────
public sealed partial class TreasureFieldItem : ObservableObject
{
    public FieldMapAsset Asset { get; } public string Display { get; }
    public TreasureFieldItem(FieldMapAsset a) { Asset = a; Display = TreasureFieldNameLookup.GetDisplayName(a.FieldId, a.AreaId); }
}

public sealed partial class TreasureChestRow : ObservableObject
{
    private bool _loading;
    public ProjectedChestLocation Location { get; }
    public string Label => Location.TreasureIds.Count == 1 ? $"Chest #{Location.TreasureIds[0]}" : "Conditional chest";
    public string Confidence => Location.Confidence.ToString();
    public IReadOnlyList<TreasureKind> KindOptions { get; } = Enum.GetValues<TreasureKind>();
    public IReadOnlyList<TreasureRewardOption> RewardOptions { get; set; } = [];
    public bool CanEdit => ActiveReward is not null;

    /// <summary>Real reward description resolved from the takara.bin catalog (e.g. "2 × Potion").</summary>
    public string RewardText { get; set; } = "";
    /// <summary>Guide-space fallback marker (center of the active map model) for chests without a recovered position.</summary>
    public float? PlaceholderX { get; set; }
    public float? PlaceholderZ { get; set; }
    public bool HasRealPosition => Location.GuideX.HasValue && Location.GuideZ.HasValue;
    public bool ShowAsPlaceholder => !HasRealPosition && PlaceholderX.HasValue && PlaceholderZ.HasValue;

    private TreasureRecord? _activeReward;
    public TreasureRecord? ActiveReward { get => _activeReward; set { if (SetProperty(ref _activeReward, value)) OnPropertyChanged(nameof(CanEdit)); } }

    private TreasureKind _selKind; private TreasureRewardOption? _selReward;
    public TreasureKind SelectedKind { get => _selKind; set { if (!SetProperty(ref _selKind, value)) return; OnPropertyChanged(nameof(AmountLabel)); OnPropertyChanged(nameof(SelectedRewardVisible)); } }
    public TreasureRewardOption? SelectedReward { get => _selReward; set => SetProperty(ref _selReward, value); }
    public string AmountLabel => SelectedKind == TreasureKind.Gil ? "Gil x100" : "Quantity";
    [ObservableProperty] private byte quantity;

    /// <summary>True when the combo should show the reward picker (non-Gil kinds).</summary>
    public bool SelectedRewardVisible => SelectedKind != TreasureKind.Gil;
    /// <summary>True when the current UI values differ from the on-disk treasure record.</summary>
    public bool HasChanges
    {
        get
        {
            if (ActiveReward is null) return false;
            // Editing a non-Gil reward requires a concrete selection; preserving "no reward"
            // is not a valid edit (we must never guess the encoded id).
            if (SelectedKind != TreasureKind.Gil && SelectedReward is null) return false;
            ushort newType = SelectedReward?.EncodedId ?? ActiveReward.Type;
            return ActiveReward.RawKind != (byte)SelectedKind
                || ActiveReward.Quantity != Quantity
                || ActiveReward.Type != newType;
        }
    }

    /// <summary>Returns the edited treasure record, or null when nothing changed / not completable.</summary>
    public TreasureRecord? ToEditedRecord()
    {
        if (ActiveReward is null || !HasChanges) return null;
        ushort newType = SelectedReward?.EncodedId ?? ActiveReward.Type;
        return ActiveReward with
        {
            RawKind = (byte)SelectedKind,
            Quantity = Quantity,
            Type = newType
        };
    }

    public TreasureChestRow(ProjectedChestLocation loc) { Location = loc; }
}

public sealed partial class TreasureMapEditor_DataModel : ObservableObject
{
    private static readonly string PrefsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "treasure-map-preferences.json");

    private readonly string _masterPath;
    private TreasureMapIndex? _index; private Dictionary<int, TreasureRecord> _edits = [];
    private readonly List<TreasureChestRow> _activeRows = [];
    private ChestLocationIndex? _locIndex;
    private GuideMapGeometry? _geometry;
    private List<TreasureFieldItem> _allFields = [];

    public ObservableCollection<TreasureFieldItem> Fields { get; } = [];
    public ObservableCollection<TreasureChestRow> Chests { get; } = [];

    [ObservableProperty] private TreasureFieldItem? selectedField;
    [ObservableProperty] private TreasureChestRow? selectedChest;
    [ObservableProperty] private GuideMapModel? activeModel;
    [ObservableProperty] private int modelIndex;
    [ObservableProperty] private int modelCount;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private bool isConfirmSuppressed;
    [ObservableProperty] private bool isScanning;

    // Multi-state (multi-model) navigation: most fields have a single YNGM state, so the
    // paging UI stays hidden unless ModelCount > 1.
    public bool HasMultiModel => ModelCount > 1;
    public string MapStateLabel => ModelCount > 1 ? $"Map state {ModelIndex + 1}/{ModelCount}" : "";
    partial void OnModelIndexChanged(int value) => OnPropertyChanged(nameof(MapStateLabel));
    partial void OnModelCountChanged(int value) { OnPropertyChanged(nameof(MapStateLabel)); OnPropertyChanged(nameof(HasMultiModel)); }

    public TreasureMapEditor_DataModel(string masterPath)
    { _masterPath = masterPath; LoadPreferences(); LoadIndex(); }

    private void LoadPreferences()
    {
        try { if (File.Exists(PrefsPath)) { var p = JsonSerializer.Deserialize<TreasureMapPreferences>(File.ReadAllText(PrefsPath)); IsConfirmSuppressed = p?.SuppressSaveConfirm == true; } }
        catch { }
    }

    private void SavePreferences()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!); File.WriteAllText(PrefsPath, JsonSerializer.Serialize(new TreasureMapPreferences(IsConfirmSuppressed))); }
        catch { }
    }

    public async Task LoadIndex()
    {
        DebugLog.Info("TreasureMap.UI", "Loading treasure index from "+_masterPath);
        Status = "Loading treasure index..."; IsScanning = true; ProgressPercent = 0;
        await Task.Run(() => _index = TreasureMapIndexBuilder.Build(_masterPath,
            p => { if (p.Contains("%")) { int idx = p.IndexOf('%'); if (idx > 0) { int.TryParse(p[(idx-3)..idx].Trim(), out int v); ProgressPercent = v; } } Status = p; }));
        _locIndex = ChestLocationIndexBuilder.Build(_index);
        _allFields = _index!.Fields.Select(f => new TreasureFieldItem(f)).ToList();
        Fields.Clear(); foreach (var f in _allFields) Fields.Add(f);
        DebugLog.Info("TreasureMap.UI", $"Index loaded: {Fields.Count} fields, {_index!.ConfirmedChestCandidates.Count} chests.");
        Status = $"{Fields.Count} fields loaded. {_index.ConfirmedChestCandidates.Count} confirmed chests."; IsScanning = false; ProgressPercent = 100;
    }

    partial void OnFilterTextChanged(string value)
    {
        Fields.Clear();
        foreach (var f in _allFields)
            if (string.IsNullOrWhiteSpace(value) || f.Display.Contains(value, StringComparison.OrdinalIgnoreCase))
                Fields.Add(f);
        DebugLog.Info("TreasureMap.UI", $"Filter '{value}' -> {Fields.Count} fields");
    }

    partial void OnSelectedFieldChanged(TreasureFieldItem? value)
    {
        Chests.Clear(); _activeRows.Clear(); SelectedChest = null; ActiveModel = null; ModelCount = 0;
        IsDirty = false;
        if (value is null) return;
        DebugLog.Info("TreasureMap.UI", $"Selecting field {value.Asset.FieldId} ({value.Display})");
        try
        {
            _geometry = GuideMapGeometry.Read(Map1Archive.Read(value.Asset.MapPath));
            ModelCount = _geometry.Models.Count;
            ModelIndex = 0;
            if (ModelCount > 0) ActiveModel = _geometry.Models[0];

            if (_index is not null)
            {
                // Fallback marker: center of the active model's bounds, in guide-space,
                // drawn only for chests without a recovered position.
                float? phX = null, phZ = null;
                if (ModelCount > 0 && _geometry is not null)
                {
                    var m0 = _geometry.Models[0];
                    phX = (m0.BoundsMin.X + m0.BoundsMax.X) / 2f;
                    phZ = (m0.BoundsMin.Z + m0.BoundsMax.Z) / 2f;
                }

                foreach (ProjectedChestLocation loc in (_locIndex ?? ChestLocationIndexBuilder.Build(_index)).Locations.Where(l => l.FieldId == value.Asset.FieldId))
                {
                    TreasureChestRow row = new(loc);
                    if (loc.TreasureIds.Count > 0)
                    {
                        int tid = loc.TreasureIds[0];
                        if (_edits.TryGetValue(tid, out TreasureRecord? edit))
                            row.ActiveReward = edit;
                        else if (tid < _index.Catalog.Records.Count)
                            row.ActiveReward = _index.Catalog.Records[tid];
                        if (row.ActiveReward is not null)
                        {
                            row.SelectedKind = row.ActiveReward.Kind ?? TreasureKind.Gil;
                            row.Quantity = row.ActiveReward.Quantity;
                            row.RewardOptions = TreasureRewardLookup.Build(row.ActiveReward.Kind ?? TreasureKind.Gil, _masterPath).ToArray();
                            // Fix ("hidden" reward): initialize the picker with the CURRENT value so the
                            // chest's real reward is visible/editable instead of an empty combo. 304/338
                            // match by EncodedId==Type; 5 rare key-items stay null (honest) and Require a
                            // user selection before Save allows the edit.
                            row.SelectedReward = row.RewardOptions.FirstOrDefault(o => o.EncodedId == row.ActiveReward.Type);
                            row.RewardText = row.ActiveReward.Kind.HasValue
                                ? TreasureRewardLookup.Describe(row.ActiveReward.Kind.Value, row.ActiveReward.Quantity, row.ActiveReward.Type, _masterPath)
                                : "Unknown kind";
                        }
                        else
                        {
                            row.RewardText = $"Unknown treasure 0x{tid:X}";
                        }
                    }
                    row.PlaceholderX = phX; row.PlaceholderZ = phZ;
                    Chests.Add(row);
                    _activeRows.Add(row);
                    row.PropertyChanged += ChestRow_Changed;
                }

                // 🐛 MAP OBJECTS (2026-08-19, Jarvis-Aurora): a seção 2 do MAP1 tem TODOS os
                // objetos de campo com posição (model_id 0x5000+slot -> x,z em 16-bit). O scanner
                // de eventos só recupera posição de constantes ATEL (5/338 = 1.7%). Os objetos do
                // mapa dão a posição REAL de todos os baús (e outros objetos da faixa de tesouro).
                // A ponte worker->objeto é runtime (unk_2310F8E), então o treasure id é aproximado
                // por PROXIMIDADE: o candidato de evento mais próximo (posição ATEL quando existe,
                // senão por ordem) é atribuído ao objeto do mapa. Projeção: guide = obj/65535*range+min.
                if (ModelCount > 0 && _geometry is not null)
                {
                    var m0 = _geometry.Models[0];
                    var fieldCandidates = _index.ConfirmedChestCandidates
                        .Where(c => c.FieldId == value.Asset.FieldId).ToList();
                    var mapObjs = MapObjectExtractor.ExtractFromObjectTable(value.Asset.MapPath)
                        .Where(o => o.IsChestRange).ToList();

                    // Candidatos com posição ATEL (raros) — casa por proximidade.
                    var positioned = fieldCandidates.Where(c => c.Positions.Count > 0).ToList();
                    // Candidatos sem posição — casa por ordem (N-ésimo candidato -> N-ésimo objeto).
                    var unpositioned = fieldCandidates.Where(c => c.Positions.Count == 0).ToList();

                    for (int mi = 0; mi < mapObjs.Count; mi++)
                    {
                        var mo = mapObjs[mi];
                        var (gx, gz) = MapObjectExtractor.ProjectToGuide(mo, m0);
                        int? tid = null;
                        string bridgeNote = $"MAP1 section 2 object table (slot {mo.Slot})";

                        // 1) Proximidade: candidato com posição ATEL mais próximo (ambos em guide space).
                        if (positioned.Count > 0)
                        {
                            float scale = m0.LocalTransform.M11;
                            var best = positioned
                                .Select(c => (C: c, D: c.Positions.Min(p =>
                                {
                                    float cgx = p.X * scale / ChestLocationIndexBuilder.BaseWorldToGuideScale;
                                    float cgz = p.Z * scale / ChestLocationIndexBuilder.BaseWorldToGuideScale;
                                    return DistSq(cgx, cgz, gx, gz);
                                })))
                                .OrderBy(x => x.D).First();
                            if (best.D < 200 * 200)
                            {
                                tid = best.C.TreasureIds.FirstOrDefault();
                                bridgeNote = $"proximity to worker {best.C.WorkerIndex:X2} (treasure {tid})";
                                positioned.Remove(best.C);
                            }
                        }

                        // 2) Ordem: candidato sem posição (N-ésimo).
                        if (tid is null && unpositioned.Count > 0 && mi < unpositioned.Count)
                        {
                            tid = unpositioned[mi].TreasureIds.FirstOrDefault();
                            bridgeNote = $"order match to worker {unpositioned[mi].WorkerIndex:X2} (treasure {tid})";
                        }

                        var loc = new ProjectedChestLocation(
                            value.Asset.FieldId, $"mapobj-{mo.ModelId:X4}", value.Asset.MapPath, mo.Slot,
                            tid is null ? [] : [tid.Value], mo.ModelId, null, gx, gz, null, null,
                            ChestLocationConfidence.Exact, bridgeNote);
                        var row = new TreasureChestRow(loc)
                        {
                            PlaceholderX = phX, PlaceholderZ = phZ,
                            RewardText = tid is null
                                ? $"Map object slot {mo.Slot} (0x{mo.ModelId:X4})"
                                : $"Treasure {tid} (slot {mo.Slot})",
                        };
                        if (tid is not null && tid.Value < _index.Catalog.Records.Count)
                        {
                            row.ActiveReward = _index.Catalog.Records[tid.Value];
                            row.SelectedKind = row.ActiveReward.Kind ?? TreasureKind.Gil;
                            row.Quantity = row.ActiveReward.Quantity;
                            row.RewardOptions = TreasureRewardLookup.Build(row.ActiveReward.Kind ?? TreasureKind.Gil, _masterPath).ToArray();
                            row.SelectedReward = row.RewardOptions.FirstOrDefault(o => o.EncodedId == row.ActiveReward.Type);
                            row.RewardText = row.ActiveReward.Kind.HasValue
                                ? TreasureRewardLookup.Describe(row.ActiveReward.Kind.Value, row.ActiveReward.Quantity, row.ActiveReward.Type, _masterPath)
                                : "Unknown kind";
                        }
                        Chests.Add(row);
                        _activeRows.Add(row);
                        row.PropertyChanged += ChestRow_Changed;
                    }
                }
            }
            Status = $"{value.Display} · {Chests.Count} chests";
            DebugLog.Info("TreasureMap.UI", $"{value.Asset.FieldId}: models={ModelCount} chests={Chests.Count}");
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; DebugLog.Error("TreasureMap.UI", $"Field selection failed for {value.Asset.FieldId}", ex); }
    }

    public void NextModel(int delta) { if (ModelCount == 0) return; int next = ModelIndex + delta; if (next >= 0 && next < ModelCount) { ModelIndex = next; ActiveModel = _geometry!.Models[next]; } }

    public void Save()
    {
        if (_index is null) { Status = "No index loaded."; return; }
        var edits = _index.Catalog.Records.ToDictionary(r => r.Id);
        foreach ((int id, TreasureRecord r) in _edits) edits[id] = r;
        // Persist actual UI edits from the currently visible chest rows (fix: Save used to
        // write the original catalog back because rows were never applied to _edits).
        foreach (TreasureChestRow row in _activeRows)
        {
            TreasureRecord? edited = row.ToEditedRecord();
            if (edited is not null) edits[edited.Id] = edited;
        }
        byte[] output = TreasureCatalogWriter.Write(_index.Catalog, edits.Values);
        TreasureCatalog saved = TreasureCatalogSaveTransaction.Save(_index.Catalog, output);
        _index = new TreasureMapIndex(saved, _index.Fields, _index.EventScans, _index.Failures);
        _edits.Clear(); IsDirty = false;
        DebugLog.Info("TreasureMap.Save", "Catalog saved.");
        Status = "Treasure catalog saved.";
    }

    public void Discard()
    {
        int editsCount = _edits.Count + _activeRows.Count(r => r.HasChanges);
        // Restore every visible row to its on-disk values (the rows stay in the list).
        foreach (TreasureChestRow row in _activeRows)
        {
            if (row.ActiveReward is null) continue;
            row.SelectedKind = row.ActiveReward.Kind ?? TreasureKind.Gil;
            row.Quantity = row.ActiveReward.Quantity;
            row.SelectedReward = row.RewardOptions.FirstOrDefault(o => o.EncodedId == row.ActiveReward.Type);
        }
        _edits.Clear(); IsDirty = false; Status = "Unsaved changes discarded.";
        DebugLog.Info("TreasureMap.Save", $"Discarded {editsCount} unsaved edits; catalog on disk unchanged.");
    }

    private void ChestRow_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TreasureChestRow row) return;
        if (e.PropertyName is nameof(TreasureChestRow.SelectedKind)
            or nameof(TreasureChestRow.SelectedReward)
            or nameof(TreasureChestRow.Quantity))
        {
            IsDirty = _activeRows.Any(r => r.HasChanges) || _edits.Count > 0;
        }
    }

    public void SuppressConfirm() { IsConfirmSuppressed = true; SavePreferences(); }

    private static double DistSq(float ax, float az, float bx, float bz)
    {
        double dx = ax - bx, dz = az - bz;
        return dx * dx + dz * dz;
    }

    private sealed record TreasureMapPreferences(bool SuppressSaveConfirm);
}