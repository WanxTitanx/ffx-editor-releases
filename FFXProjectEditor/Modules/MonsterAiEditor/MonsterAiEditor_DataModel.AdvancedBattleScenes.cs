using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

internal partial class MonsterAiEditor_DataModel
{
    string? advancedSceneEncounterPath;
    public ObservableCollection<AiAdvancedSceneRow> AdvancedBattleScenes { get; } = new();
    public ObservableCollection<AiAdvancedSceneOptionRow> AdvancedBattleSceneOptions { get; } = new();
    [ObservableProperty] private AiAdvancedSceneRow? selectedAdvancedBattleScene;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAdvancedBattleScene))]
    private AiAdvancedSceneOptionRow? selectedAdvancedBattleSceneOption;
    [ObservableProperty] private string advancedBattleSceneStatus = string.Empty;
    [ObservableProperty] private string advancedBattleSceneEncounter = string.Empty;
    public bool HasAdvancedSceneCalls => selectedScript?.Instructions.Any(i => i.Opcode == 0xD8 && i.Operand is 0x703C or 0x7097) == true;
    public bool HasAdvancedBattleScenes => AdvancedBattleScenes.Count > 0;
    public bool CanApplyAdvancedBattleScene => SelectedAdvancedBattleSceneOption != null;

    partial void OnSelectedAdvancedBattleSceneChanged(AiAdvancedSceneRow? value)
    {
        AdvancedBattleSceneOptions.Clear();
        if (value != null)
            foreach (var option in value.Source.Options)
                AdvancedBattleSceneOptions.Add(new AiAdvancedSceneOptionRow(option));
        SelectedAdvancedBattleSceneOption = AdvancedBattleSceneOptions.FirstOrDefault(o => o.Source.Selector == value?.Source.Selector);
    }

    public bool LoadAdvancedSceneEncounter(string path)
    {
        advancedSceneEncounterPath = path;
        RefreshAdvancedBattleScenes();
        return HasAdvancedBattleScenes;
    }

    void RefreshAdvancedBattleScenes()
    {
        int? call = SelectedAdvancedBattleScene?.Source.CallOffset;
        SelectedAdvancedBattleScene = null;
        AdvancedBattleScenes.Clear();
        AdvancedBattleSceneStatus = string.Empty;
        AdvancedBattleSceneEncounter = string.Empty;
        OnPropertyChanged(nameof(HasAdvancedSceneCalls));
        if (HasAdvancedSceneCalls && selectedScript != null && advancedSceneEncounterPath != null
            && TryGetSelectedMonsterNumber(out int number))
        {
            try
            {
                byte[] encounter = File.ReadAllBytes(advancedSceneEncounterPath);
                foreach (var row in AiBattleSceneWriter.Detect(selectedScript, number, encounter))
                    AdvancedBattleScenes.Add(new AiAdvancedSceneRow(row));
                AdvancedBattleSceneEncounter = Path.GetFileName(advancedSceneEncounterPath);
                AdvancedBattleSceneStatus = HasAdvancedBattleScenes ? Strings.AiAdvancedScenesReady : Strings.AiAdvancedScenesUnresolved;
            }
            catch (Exception ex) { AdvancedBattleSceneStatus = ex.Message; }
        }
        SelectedAdvancedBattleScene = AdvancedBattleScenes.FirstOrDefault(r => r.Source.CallOffset == call)
            ?? AdvancedBattleScenes.FirstOrDefault();
        OnPropertyChanged(nameof(HasAdvancedBattleScenes));
    }

    public bool ApplyAdvancedBattleScene()
    {
        if (selectedScript == null || selectedPath == null || advancedSceneEncounterPath == null
            || !TryGetSelectedMonsterNumber(out int number) || SelectedAdvancedBattleScene == null
            || SelectedAdvancedBattleSceneOption == null || !AdvancedBattleScenes.Contains(SelectedAdvancedBattleScene)
            || !AdvancedBattleSceneOptions.Contains(SelectedAdvancedBattleSceneOption)) return false;
        if (HasPendingAuthoringEdits)
        {
            AdvancedBattleSceneStatus = Strings.AiAdvancedPendingEdits;
            return false;
        }
        try
        {
            // Re-read the encounter at apply. A previously loaded map must not
            // authorize writing a selector after that external file changes.
            byte[] encounter = File.ReadAllBytes(advancedSceneEncounterPath);
            if (!AiBattleSceneWriter.TryApply(selectedScript, number, encounter, SelectedAdvancedBattleScene.Source,
                    SelectedAdvancedBattleSceneOption.Source.Selector, out byte[]? output, out string error) || output == null)
            {
                AdvancedBattleSceneStatus = error;
                return false;
            }
            AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, output);
        }
        catch (Exception ex)
        {
            AdvancedBattleSceneStatus = ex.Message;
            return false;
        }
        bool reloaded = ReloadSelectedFromDisk();
        RefreshPhaseRotationAudit();
        RebuildAdvancedPhaseUnits();
        AdvancedBattleSceneStatus = reloaded ? Strings.AiAdvancedNativeConditionSaved : Strings.AiAdvancedNativeConditionReload;
        return reloaded;
    }
}

internal sealed record AiAdvancedSceneRow(AiBattleSceneDescriptor Source)
{
    public string Summary => $"{(Source.FunctionId == 0x703C ? "A" : "B")}({Source.Selector}) · 0x{Source.CallOffset:X4}";
}
internal sealed record AiAdvancedSceneOptionRow(AiBattleSceneOption Source)
{
    public string Summary => $"{Source.Selector} → w{Source.WorkerIndex}/e{Source.EntrypointIndex} · 0x{Source.EntrypointOffset:X4}";
}
