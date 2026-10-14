using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.HookModAuthoring;

internal sealed partial class MonsterRewardRow : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Label => $"m{Id:D3} · {Name}";
    [ObservableProperty] private int apRate = 1;
    [ObservableProperty] private int gilRate = 1;
}
internal sealed partial class MonsterRewards_DataModel : ObservableObject
{
    private readonly MonsterRewardRow[] all;
    private MonsterRewardSource? source;
    private bool reloading;
    public event Action? Changed;
    public ObservableCollection<MonsterRewardRow> Rows { get; } = new();
    [ObservableProperty] private MonsterRewardRow? selected;
    [ObservableProperty] private string filter = "";
    [ObservableProperty] private string tsvPath = "";
    [ObservableProperty] private string monsterPath = "";
    [ObservableProperty] private string report = "";
    [ObservableProperty] private int globalAp = 1;
    [ObservableProperty] private int globalGil = 1;
    [ObservableProperty] private bool globalApEnabled;
    [ObservableProperty] private bool globalGilEnabled;
    public string? ImportedHash { get; private set; }
    public MonsterRewards_DataModel()
    {
        all = Enumerable.Range(0, 4096).Select(id => new MonsterRewardRow { Id = id, Name = Monster_Dictionary.Instance.GetValueOrDefault((short)id) ?? "—" }).ToArray();
        foreach (var row in all) row.PropertyChanged += (_, _) => { if (!reloading) { UpdatePreview(); Changed?.Invoke(); } };
        FilterRows(); Selected = all[0];
    }
    partial void OnFilterChanged(string value) => FilterRows();
    private void FilterRows()
    {
        Rows.Clear(); foreach (var row in all.Where(r => r.Label.Contains(Filter, StringComparison.OrdinalIgnoreCase))) Rows.Add(row);
    }
    partial void OnSelectedChanged(MonsterRewardRow? value) { source = null; UpdatePreview(); }
    partial void OnMonsterPathChanged(string value) { source = null; UpdatePreview(); Changed?.Invoke(); }
    partial void OnTsvPathChanged(string value) { ImportedHash = null; Changed?.Invoke(); }
    partial void OnGlobalApChanged(int value) => UpdatePreview();
    partial void OnGlobalGilChanged(int value) => UpdatePreview();
    partial void OnGlobalApEnabledChanged(bool value) => UpdatePreview();
    partial void OnGlobalGilEnabledChanged(bool value) => UpdatePreview();
    public MonsterRewardProfile Profile()
    {
        var profile = new MonsterRewardProfile(); foreach (var row in all) profile[row.Id] = new(row.ApRate, row.GilRate); return profile;
    }
    public byte[] ExportBytes() => MonsterRewardSettingsCodec.Serialize(Profile());
    [RelayCommand] private void Reload()
    {
        try
        {
            byte[] bytes = ModAuthoringFiles.Read(TsvPath, 65535); var profile = MonsterRewardSettingsCodec.Parse(bytes);
            reloading = true;
            try { foreach (var row in all) { row.ApRate = profile[row.Id].Ap; row.GilRate = profile[row.Id].Gil; } }
            finally { reloading = false; }
            ImportedHash = ModAuthoringFiles.Sha256(bytes); UpdatePreview(); Changed?.Invoke();
        }
        catch (Exception ex) { Report = ex.Message; }
    }
    [RelayCommand] private void LoadBase()
    {
        if (Selected == null) return;
        try { source = MonsterRewardSourceResolver.FromFile(Selected.Id, Selected.Name, MonsterPath); UpdatePreview(); }
        catch (Exception ex) { source = null; Report = ex.Message; }
    }
    [RelayCommand] private void ResetAp() { if (Selected != null) Selected.ApRate = 1; }
    [RelayCommand] private void ResetGil() { if (Selected != null) Selected.GilRate = 1; }
    [RelayCommand] private void ResetMonster() { ResetAp(); ResetGil(); }
    public void Save(HookProjectProfile project)
    {
        MonsterRewardSettingsCodec.Save(TsvPath, Profile(), project, ImportedHash);
        ImportedHash = HookExportTransaction.CurrentHash(TsvPath); Report = Strings.U_HookIntegration_DesiredOnly;
    }
    public void UpdatePreview()
    {
        if (Selected == null) return;
        try
        {
            var origin = source ?? MonsterRewardSourceResolver.Missing(Selected.Id, Selected.Name);
            if (source?.Path != null && HookExportTransaction.CurrentHash(source.Path) != source.Sha256) { source = null; origin = MonsterRewardSourceResolver.Missing(Selected.Id, Selected.Name); }
            var p = new MonsterRewardProfile(); p[Selected.Id] = new(Selected.ApRate, Selected.GilRate);
            var preview = p.Preview(origin, GlobalAp, GlobalGil, GlobalApEnabled, GlobalGilEnabled);
            string Line(string label, MonsterRewardCalculation? value) => value == null ? label + ": " + Strings.U_HookIntegration_MissingBase :
                $"{label}: {value.Original:N0} → {value.Individual:N0} → {value.Combined:N0}" + (value.Clamped ? $" / {Strings.U_HookIntegration_AppliedCap}: {value.Applied:N0}" : "");
            Report = string.Join("\n", Line("AP", preview.Ap), Line("AP Overkill", preview.OverkillAp), Line(Strings.CommonGil, preview.Gil),
                Strings.U_HookIntegration_VanillaBonuses, $"{origin.Origin}: {origin.Path ?? "—"}\nSHA-256: {origin.Sha256 ?? "—"}");
        }
        catch (Exception ex) { Report = ex.Message; }
    }
}
