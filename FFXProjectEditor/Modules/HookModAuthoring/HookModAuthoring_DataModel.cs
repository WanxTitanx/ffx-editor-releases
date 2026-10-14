using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.HookModAuthoring;

internal partial class HookModAbilityRow : ObservableObject
{
    public int Id { get; init; }
    public string EffectKey { get; init; } = "";
    public string Label { get; init; } = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private int price;
    public Mod002Edit Edit() => new(Id, Name, Description, Price);
}

internal partial class HookModAuthoring_DataModel : ObservableObject
{
    private Mod002Authoring? session;
    private Mod002Preview? preview;
    private string importedRoot = "", abilityPath = "", pricePath = "";
    public string[] Locales => Mod002Authoring.Locales;
    public ObservableCollection<HookModAbilityRow> Abilities { get; } = new();

    [ObservableProperty] private bool mod002Enabled;
    [ObservableProperty] private bool mod004Enabled;
    [ObservableProperty] private bool mod005Enabled;
    [ObservableProperty] private bool mod006Enabled;
    [ObservableProperty] private string sourceRoot = "";
    [ObservableProperty] private string manifestPath = "";
    [ObservableProperty] private string locale = "new_uspc";
    [ObservableProperty] private string destination = "";
    [ObservableProperty] private string workshopDirectory = "";
    [ObservableProperty] private string previewText = "";
    [ObservableProperty] private string workshopText = "";
    [ObservableProperty] private string languageSample = "Ação, coração, opções, bênção.";
    [ObservableProperty] private string languageReport = "";
    [ObservableProperty] private string status = Strings.U_HookMods_DefaultStatus;
    [ObservableProperty] private HookModAbilityRow? selectedAbility;

    public bool HasSession => OnlyModEnabled && Mod002Enabled && session != null;
    public bool CanEditText => HasSession && session!.CanEditText;
    public bool CanExport => HasSession && preview != null && !string.IsNullOrWhiteSpace(Destination);
    public bool WorkshopEnabled => OnlyModEnabled && (Mod004Enabled || Mod005Enabled);
    public bool CanExportLanguagePack => OnlyModEnabled && LanguageBuild != null && LanguageBuild.RecipeHash == ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(LanguageRecipeJson));
    public string CodecStatus => !HasSession ? Strings.U_HookMods_ImportFirst
        : CanEditText ? Strings.U_HookMods_WesternCodec : Strings.U_HookMods_UnprovedCodec;

    public HookModAuthoring_DataModel(string sourceRoot) { SourceRoot = sourceRoot; InitializeIntegration(); }

    partial void OnMod002EnabledChanged(bool value) => ResetImport();
    partial void OnSourceRootChanged(string value) { prepared.Clear(); ResetImport(); ReloadProjectProfile(); InvalidatePackage(); }
    partial void OnLocaleChanged(string value) { prepared.Clear(); ResetImport(); InvalidatePackage(); }
    partial void OnManifestPathChanged(string value) => ResetImport();
    partial void OnDestinationChanged(string value) { OnPropertyChanged(nameof(CanExport)); InvalidatePackage(); }
    partial void OnMod004EnabledChanged(bool value) => ResetWorkshop();
    partial void OnMod005EnabledChanged(bool value) => ResetWorkshop();
    partial void OnWorkshopDirectoryChanged(string value) => ResetWorkshop();
    partial void OnMod006EnabledChanged(bool value) { LanguageReport = ""; OnPropertyChanged(nameof(CanExportLanguagePack)); UpdateProfileStatus(); }

    private void ResetImport()
    {
        session = null; preview = null; Abilities.Clear(); SelectedAbility = null; PreviewText = "";
        UpdateProfileStatus();
        StateChanged();
    }
    private void ResetWorkshop() { WorkshopText = ""; OnPropertyChanged(nameof(WorkshopEnabled)); UpdateProfileStatus(); }
    private void UpdateProfileStatus() => Status = OnlyModEnabled
        ? Strings.U_HookMods_ProfileStatus : Strings.U_HookMods_DefaultStatus;
    private void StateChanged()
    {
        OnPropertyChanged(nameof(HasSession)); OnPropertyChanged(nameof(CanEditText));
        OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CodecStatus));
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Status = string.Format(Strings.U_HookMods_Failed, ex.Message);
            DebugLog.Warn("HookModAuthoring", ex.Message);
        }
    }

    [RelayCommand]
    private void ImportMod002() => Run(() =>
    {
        ResetImport();
        if (!OnlyModEnabled || !Mod002Enabled) return;
        CurrentProject.Require("vanguard.v1");
        importedRoot = Path.GetFullPath(SourceRoot);
        abilityPath = Path.Combine(importedRoot, Locale, "battle", "kernel", "a_ability.bin");
        string regionalPrice = Path.Combine(importedRoot, Locale, "battle", "kernel", "arms_rate.bin");
        pricePath = File.Exists(regionalPrice) ? regionalPrice : Path.Combine(importedRoot, "jppc", "battle", "kernel", "arms_rate.bin");
        var manifest = string.IsNullOrWhiteSpace(ManifestPath) ? Mod002Manifest.Default()
            : Mod002Manifest.Parse(Encoding.UTF8.GetString(ModAuthoringFiles.Read(ManifestPath, 64_000)));
        var shared = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,int>>(RegistryJson)
            ?? throw new InvalidDataException("Missing shared ability registry.");
        foreach (var effect in manifest.Abilities) shared[effect.Key] = effect.Id;
        _ = HookAbilityRegistry.Mapping(shared);
        var loaded = new Mod002Authoring(ModAuthoringFiles.Read(abilityPath, 65535),
            ModAuthoringFiles.Read(pricePath, 100_000), manifest, Locale);
        session = loaded;
        foreach (var row in loaded.Rows)
        {
            var effect = manifest.Abilities.Single(a => a.Id == row.Id);
            var editable = new HookModAbilityRow { Id = row.Id, EffectKey = effect.Key,
                Label = $"#{row.Id} · {effect.Name}", Name = row.Name, Description = row.Description, Price = row.Price };
            editable.PropertyChanged += (_, _) => { preview = null; PreviewText = ""; OnPropertyChanged(nameof(CanExport)); };
            Abilities.Add(editable);
        }
        SelectedAbility = Abilities.FirstOrDefault();
        Status = string.Format(Strings.U_HookMods_Imported, Abilities.Count);
        StateChanged();
    });

    [RelayCommand]
    private void PreviewExport() => Run(() =>
    {
        preview = null; OnPropertyChanged(nameof(CanExport));
        if (!HasSession) return;
        preview = session!.Preview(Abilities.Select(a => a.Edit()).ToArray());
        PreviewText = preview.Report;
        OnPropertyChanged(nameof(CanExport));
    });

    [RelayCommand]
    private void Export() => Run(() =>
    {
        if (!CanExport) return;
        CurrentProject.Require("vanguard.v1");
        session!.Export(importedRoot, abilityPath, pricePath, Destination, Abilities.Select(a => a.Edit()).ToArray(), preview!.Fingerprint);
        Status = string.Format(Strings.U_HookMods_Exported, Path.GetFullPath(Destination));
        preview = null; OnPropertyChanged(nameof(CanExport));
    });

    [RelayCommand]
    private void InspectWorkshop() => Run(() =>
    {
        WorkshopText = "";
        if (!WorkshopEnabled) return;
        var inspection = WorkshopInspector.ReadDirectory(WorkshopDirectory);
        var text = new StringBuilder(string.Format(Strings.U_HookMods_WorkshopSummary,
            inspection.SaveId, inspection.Revision, inspection.SuccessfulRolls, inspection.Pieces.Count));
        foreach (var piece in inspection.Pieces)
        {
            text.AppendLine();
            text.AppendLine(string.Format(Strings.U_HookMods_PieceSummary, piece.Slot, piece.Id, piece.Owner, piece.Type,
                piece.Mode switch { 1 => "A", 2 => "B", _ => "—" }, piece.GlobalRank));
            int count = Mod005Enabled ? 5 : 4;
            for (int i = 0; i < count; i++)
                text.AppendLine(string.Format(Strings.U_HookMods_AbilitySummary, i + 1, piece.Abilities[i].ToString("X4"), piece.AbilityIds[i], piece.Ranks[i]));
            if (Mod005Enabled) text.AppendLine(string.Format(Strings.U_HookMods_FifthSummary, piece.FifthUnlocked));
        }
        text.AppendLine("\n" + Strings.U_HookMods_Materials);
        text.AppendLine(string.Join(", ", inspection.Materials.Select((n, i) => $"#{i}={n}").Where((_, i) => inspection.Materials[i] > 0)));
        WorkshopText = text.ToString();
        Status = Strings.U_HookMods_InspectionPassed;
    });

    [RelayCommand]
    private void CheckLanguage() => Run(() =>
    {
        if (!Mod006Enabled) return;
        string missing = Mod006Readiness.MissingUsCharacters(LanguageSample);
        LanguageReport = string.Format(Strings.U_HookMods_LanguageReport, missing.Length == 0 ? "—" : missing);
    });
}
