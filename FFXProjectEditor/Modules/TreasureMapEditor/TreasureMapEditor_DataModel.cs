using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public sealed partial class TreasureFieldItem : ObservableObject
{
    public FieldMapAsset Asset { get; }
    public string Display { get; }
    public TreasureFieldItem(FieldMapAsset asset) { Asset = asset; Display = TreasureFieldNameLookup.GetDisplayName(asset.FieldId, asset.AreaId); }
}

public sealed partial class TreasureMapEditor_DataModel : ObservableObject
{
    private static readonly string PrefsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "treasure-map-preferences.json");
    private readonly string _masterPath;
    private TreasureMapIndex? _index;
    private readonly Dictionary<int, TreasureRecord> _edits = [];
    private readonly List<TreasureChestRow> _fieldRows = [];
    private ChestLocationIndex? _locIndex;
    private GuideMapGeometry? _geometry;
    private List<TreasureFieldItem> _allFields = [];
    private bool _selectingField;

    public ObservableCollection<TreasureFieldItem> Fields { get; } = [];
    public ObservableCollection<TreasureChestRow> Chests { get; } = [];
    public ObservableCollection<TreasureChestRow> MapChests { get; } = [];
    public Task Initialization { get; }
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
    public bool HasMultiModel => ModelCount > 1;
    public string MapStateLabel => ModelCount > 1 ? string.Format(Strings.TreasureMapState, ModelIndex + 1, ModelCount) : "";
    public bool CanSave => !IsScanning && IsDirty && _fieldRows.All(r => r.IsValid);
    public bool CanChangeField => _fieldRows.All(r => r.IsValid);
    public string PositionSummary => string.Format(Strings.TreasureMapPositionSummary,
        MapChests.Count, Chests.Count(r => !r.HasRealPosition));
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(CanSave));
    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(CanSave));
    partial void OnModelIndexChanged(int value) => OnPropertyChanged(nameof(MapStateLabel));
    partial void OnModelCountChanged(int value) { OnPropertyChanged(nameof(MapStateLabel)); OnPropertyChanged(nameof(HasMultiModel)); }

    public TreasureMapEditor_DataModel(string masterPath)
    { _masterPath = masterPath; LoadPreferences(); Initialization = LoadIndex(); }

    private void LoadPreferences()
    {
        try { if (File.Exists(PrefsPath)) IsConfirmSuppressed = JsonSerializer.Deserialize<TreasureMapPreferences>(File.ReadAllText(PrefsPath))?.SuppressSaveConfirm == true; }
        catch (Exception ex) { DebugLog.Warn("TreasureMap.UI", $"Preferences unavailable: {ex.Message}"); }
    }

    public async Task LoadIndex()
    {
        if (IsScanning || IsDirty) return;
        IsScanning = true; ProgressPercent = 0; Status = Strings.TreasureMapLoading;
        try
        {
            var loaded = await Task.Run(() =>
            {
                var index = TreasureMapIndexBuilder.Build(_masterPath);
                return (Index: index, Locations: ChestLocationIndexBuilder.Build(index));
            });
            _index = loaded.Index; _locIndex = loaded.Locations;
            _allFields = _index.Fields.Select(f => new TreasureFieldItem(f)).ToList();
            ApplyFilter();
            Status = string.Format(Strings.TreasureMapLoaded, Fields.Count, _index.ConfirmedChestCandidates.Count, _index.Failures.Count);
            ProgressPercent = 100;
        }
        catch (Exception ex) { Status = string.Format(Strings.TreasureMapLoadError, ex.Message); DebugLog.Error("TreasureMap.UI", "Index load failed", ex); }
        finally { IsScanning = false; }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    private void ApplyFilter()
    {
        if (!CanChangeField) return;
        var previous = SelectedField;
        _selectingField = true;
        try
        {
            Fields.Clear();
            foreach (var field in _allFields.Where(f => string.IsNullOrWhiteSpace(FilterText) || f.Display.Contains(FilterText, StringComparison.OrdinalIgnoreCase))) Fields.Add(field);
            SelectedField = previous is not null && Fields.Contains(previous) ? previous : null;
        }
        finally { _selectingField = false; }
        OnSelectedFieldChanged(SelectedField);
    }

    partial void OnSelectedFieldChanged(TreasureFieldItem? value)
    {
        if (_selectingField) return;
        foreach (var row in _fieldRows) row.Edited -= ChestRowChanged;
        _fieldRows.Clear(); Chests.Clear(); MapChests.Clear(); SelectedChest = null;
        ActiveModel = null; _geometry = null; ModelCount = 0; ModelIndex = 0;
        if (value is null) { RefreshEditState(); return; }
        try
        {
            _geometry = GuideMapGeometry.Read(Map1Archive.Read(value.Asset.MapPath));
            ModelCount = _geometry.Models.Count;
            if (_index is not null && _locIndex is not null)
            {
                foreach (var location in _locIndex.Locations.Where(l => l.FieldId == value.Asset.FieldId))
                {
                    // Conditional scripts can grant several catalog rows. Each must be
                    // editable explicitly; never silently choose TreasureIds[0].
                    foreach (int id in location.TreasureIds.Distinct())
                    {
                        var row = new TreasureChestRow(location with { TreasureIds = new[] { id } }, _masterPath);
                        if (id >= 0 && id < _index.Catalog.Records.Count)
                            row.LoadRecord(_index.Catalog.Records[id], _edits.GetValueOrDefault(id));
                        row.Edited += ChestRowChanged;
                        _fieldRows.Add(row);
                    }
                }
            }
            ShowModel();
            Status = string.Format(Strings.TreasureMapFieldSummary, value.Display, Chests.Count);
        }
        catch (Exception ex) { Status = string.Format(Strings.TreasureMapLoadError, ex.Message); DebugLog.Error("TreasureMap.UI", $"Field load failed: {value.Asset.FieldId}", ex); }
        RefreshEditState();
    }

    public void NextModel(int delta)
    {
        int next = ModelIndex + delta;
        if (next < 0 || next >= ModelCount || !CanChangeField) return;
        ModelIndex = next;
        ShowModel();
    }

    private void ShowModel()
    {
        var selectedId = SelectedChest?.ActiveReward?.Id;
        Chests.Clear(); MapChests.Clear();
        ActiveModel = ModelCount > 0 ? _geometry!.Models[ModelIndex] : null;
        foreach (var row in _fieldRows.Where(r => !r.HasRealPosition || r.Location.ModelIndex == ModelIndex))
        {
            Chests.Add(row);
            if (row.HasRealPosition) MapChests.Add(row);
        }
        SelectedChest = Chests.FirstOrDefault(r => r.ActiveReward?.Id == selectedId) ?? Chests.FirstOrDefault();
        OnPropertyChanged(nameof(PositionSummary));
    }

    private void ChestRowChanged(object? sender, EventArgs e)
    {
        if (sender is not TreasureChestRow row || row.ActiveReward is null) return;
        if (row.IsValid)
        {
            int id = row.ActiveReward.Id;
            var edited = row.ToEditedRecord();
            if (edited is null) _edits.Remove(id); else _edits[id] = edited;
            foreach (var other in _fieldRows.Where(r => !ReferenceEquals(r, row) && r.ActiveReward?.Id == id))
                other.LoadRecord(_index!.Catalog.Records[id], edited);
        }
        RefreshEditState();
    }

    private void RefreshEditState()
    {
        IsDirty = _edits.Count > 0 || _fieldRows.Any(r => !r.IsValid);
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanChangeField));
    }

    public void Save()
    {
        if (_index is null || !IsDirty) return;
        if (!CanSave) throw new InvalidOperationException(Strings.TreasureMapChooseReward);
        var records = _index.Catalog.Records.Select(r => _edits.GetValueOrDefault(r.Id) ?? r);
        byte[] output = TreasureCatalogWriter.Write(_index.Catalog, records);
        var saved = TreasureCatalogSaveTransaction.Save(_index.Catalog, output);
        _index = _index with { Catalog = saved };
        _edits.Clear();
        foreach (var row in _fieldRows.Where(r => r.ActiveReward is not null)) row.LoadRecord(saved.Records[row.ActiveReward!.Id]);
        RefreshEditState();
        Status = Strings.TreasureMapSaved;
        DebugLog.Info("TreasureMap.Save", "Catalog saved and edit baselines refreshed.");
    }

    public void Discard()
    {
        _edits.Clear();
        if (_index is not null)
            foreach (var row in _fieldRows.Where(r => r.ActiveReward is not null)) row.LoadRecord(_index.Catalog.Records[row.ActiveReward!.Id]);
        RefreshEditState();
        Status = Strings.TreasureMapDiscarded;
    }

    public void SuppressConfirm()
    {
        IsConfirmSuppressed = true;
        try { Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!); File.WriteAllText(PrefsPath, JsonSerializer.Serialize(new TreasureMapPreferences(true))); }
        catch (Exception ex) { DebugLog.Warn("TreasureMap.UI", $"Preferences could not be saved: {ex.Message}"); }
    }
    private sealed record TreasureMapPreferences(bool SuppressSaveConfirm);
}
