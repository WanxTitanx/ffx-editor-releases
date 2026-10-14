using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Modules.AuroraChamber;

internal partial class AuroraChamber_DataModel
{
    private AuroraBattleAuthoringSession? _battleAuthoring;
    private byte[]? _openingDraft;
    private byte[]? _openingTemplate;
    private bool _openingPrepared;
    public ObservableCollection<AuroraOpeningEditRow> OpeningEditorRows { get; } = new();
    public ObservableCollection<AuroraCompanionEditRow> CompanionEditorRows { get; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasOpeningEditStatus))] private string openingEditStatus = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasEncounterEditStatus))] private string encounterEditStatus = string.Empty;
    [ObservableProperty] private string openingScriptText = string.Empty;
    public bool HasOpeningEditStatus => !string.IsNullOrWhiteSpace(OpeningEditStatus);
    public bool HasEncounterEditStatus => !string.IsNullOrWhiteSpace(EncounterEditStatus);
    public bool CanCreateOpening => _openingTemplate != null && OpeningEditorRows.Count == 0 && CanOpenBattlePreview;
    public bool HasPendingEncounterEdits => _openingPrepared || OpeningEditorRows.Any(row => row.Value != row.Source.Value) ||
        CompanionEditorRows.Any(row => row.MonsterId != row.OriginalMonsterId);
    public bool CanSaveEncounterEdits => _battleAuthoring != null && CanOpenBattlePreview && HasPendingEncounterEdits;
    public bool HasOpeningEditorRows => OpeningEditorRows.Count > 0;
    public bool CanOpenCompanionAi => CanOpenBattlePreview && !HasPendingEncounterEdits;

    private void NotifyEncounterEditors()
    {
        OnPropertyChanged(nameof(CanCreateOpening));
        OnPropertyChanged(nameof(CanSaveEncounterEdits));
        OnPropertyChanged(nameof(HasOpeningEditorRows));
        OnPropertyChanged(nameof(HasPendingEncounterEdits));
        OnPropertyChanged(nameof(CanOpenCompanionAi));
    }

    private void ClearEncounterEditors()
    {
        _battleAuthoring = null; _openingDraft = _openingTemplate = null;
        _openingPrepared = false;
        foreach (var row in OpeningEditorRows) row.PropertyChanged -= OnEncounterRowChanged;
        foreach (var row in CompanionEditorRows) row.PropertyChanged -= OnEncounterRowChanged;
        OpeningEditorRows.Clear(); CompanionEditorRows.Clear();
        OpeningScriptText = string.Empty;
        OpeningEditStatus = EncounterEditStatus = string.Empty;
        NotifyEncounterEditors();
    }

    private void LoadEncounterEditors(string battleId, byte[] bytes)
    {
        _battleAuthoring = new(battleId, Project_Service.Instance.GetPathBattle(battleId), bytes);
        _openingDraft = bytes.ToArray();
        try
        {
            PopulateOpeningEditor(bytes);
            OpeningScriptText = AiScript_File.Disassemble(BattleOpeningWriter.ReadLayout(bytes).Script);
            if (OpeningEditorRows.Count == 0)
            {
                try { _openingTemplate = BattleOpeningTemplateWriter.CreateMonstersFirst(bytes, BattleOpeningWriter.Revision(bytes)); }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
                { OpeningEditStatus = string.Format(Strings.U_Au_OpeningEditUnavailable, ex.Message); }
                if (_openingTemplate != null) OpeningEditStatus = Strings.U_Au_OpeningUnconfigured;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
        { OpeningEditStatus = string.Format(Strings.U_Au_OpeningEditUnavailable, ex.Message); }
        if (_currentLineup != null)
            foreach (var slot in _currentLineup.Where(slot => !slot.IsEmpty))
            {
                var row = new AuroraCompanionEditRow(slot.SlotIndex, slot.DictionaryId);
                row.PropertyChanged += OnEncounterRowChanged;
                CompanionEditorRows.Add(row);
            }
        NotifyEncounterEditors();
    }

    private void PopulateOpeningEditor(byte[] bytes)
    {
        foreach (var row in OpeningEditorRows) row.PropertyChanged -= OnEncounterRowChanged;
        OpeningEditorRows.Clear();
        foreach (var value in BattleOpeningWriter.Read(bytes))
        {
            var row = new AuroraOpeningEditRow(value);
            row.PropertyChanged += OnEncounterRowChanged;
            OpeningEditorRows.Add(row);
        }
        NotifyEncounterEditors();
    }

    private void OnEncounterRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AuroraOpeningEditRow.Value) or nameof(AuroraCompanionEditRow.MonsterId))
            NotifyEncounterEditors();
    }

    public bool PrepareOpeningTemplate()
    {
        if (!CanCreateOpening || _openingTemplate == null) return false;
        _openingDraft = _openingTemplate.ToArray();
        _openingPrepared = true;
        PopulateOpeningEditor(_openingDraft);
        OpeningScriptText = AiScript_File.Disassemble(BattleOpeningWriter.ReadLayout(_openingDraft).Script);
        OpeningEditStatus = Strings.U_Au_OpeningPrepared;
        return true;
    }

    public bool SaveEncounterEdits()
    {
        if (!CanSaveEncounterEdits || _battleAuthoring == null || _openingDraft == null || SelectedBattle == null) return false;
        try
        {
            if (OpeningEditorRows.Any(row => row.Value != decimal.Truncate(row.Value)) ||
                CompanionEditorRows.Any(row => row.MonsterId != decimal.Truncate(row.MonsterId)))
                throw new ArgumentException(Strings.U_Au_EncounterWholeNumbers);
            byte[] candidate = _openingDraft.ToArray();
            if (OpeningEditorRows.Count > 0)
                candidate = BattleOpeningWriter.Apply(candidate, BattleOpeningWriter.Revision(candidate),
                    OpeningEditorRows.Select(row => new BattleOpeningValueEdit(row.Source.InstructionOffset, checked((int)row.Value))).ToArray());
            var rosterEdits = CompanionEditorRows.Where(row => row.MonsterId != row.OriginalMonsterId)
                .Select(row => new BattleCompanionRosterEdit(row.Slot, checked((int)row.MonsterId))).ToArray();
            foreach (var edit in rosterEdits)
                if (!File.Exists(Project_Service.Instance.GetPathMon(edit.MonsterId)))
                    throw new InvalidOperationException(string.Format(Strings.U_Au_CompanionFileMissing, edit.MonsterId));
            if (rosterEdits.Length > 0)
                candidate = BattleCompanionRosterWriter.Apply(SelectedBattle, candidate,
                    BattleOpeningWriter.Revision(candidate), rosterEdits);
            bool changed = _battleAuthoring.Save(SelectedBattle, Project_Service.Instance.GetPathBattle(SelectedBattle), candidate);
            if (changed) ReloadSelectedBattle();
            EncounterEditStatus = changed ? Strings.U_Au_EncounterEditsSaved : Strings.U_Noclip_NoChanges;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException)
        {
            DebugLog.Error("aurora-chamber.Authoring", "Encounter authoring save failed", ex);
            EncounterEditStatus = string.Format(Strings.U_Au_EncounterEditFailed, ex.Message);
            return false;
        }
    }
}

internal sealed partial class AuroraOpeningEditRow : ObservableObject
{
    public BattleOpeningValue Source { get; }
    [ObservableProperty] private decimal value;
    public decimal Minimum => Source.Add ? -255 : 0;
    public decimal Maximum => Source.Field == BattleOpeningWriter.FirstStrike ? 1 : 255;
    public string FieldLabel => Source.Field == BattleOpeningWriter.FirstStrike ? Strings.U_Au_OpeningFirstStrike : Strings.U_Au_OpeningDelay;
    public string OperationLabel => Source.Add ? Strings.U_Au_OpeningAdd : Strings.U_Au_OpeningSet;
    public string TargetLabel => Source.Target switch
    {
        0xFFF1 => Strings.U_Au_OpeningMonsters,
        0xFFF2 => Strings.U_Au_OpeningParty,
        >= 0x14 and <= 0x1B => string.Format(Strings.U_Au_OpeningMonsterSlot, Source.Target - 0x14),
        >= 0xFFF8 and <= 0xFFFA => string.Format(Strings.U_Au_OpeningPartySlot, 0xFFFA - Source.Target + 1),
        >= 0xFFF4 and <= 0xFFF7 => string.Format(Strings.U_Au_OpeningReserveSlot, 0xFFF7 - Source.Target + 1),
        _ => $"0x{Source.Target:X4}"
    };
    public string AccessibleLabel => $"{TargetLabel} · {FieldLabel} · {OperationLabel}";
    public AuroraOpeningEditRow(BattleOpeningValue source) { Source = source; Value = source.Value; }
}

internal sealed partial class AuroraCompanionEditRow : ObservableObject
{
    public int Slot { get; }
    public int OriginalMonsterId { get; }
    public decimal Maximum => Math.Max(999, OriginalMonsterId);
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MonsterName))] private decimal monsterId;
    public string SlotLabel => string.Format(Strings.U_Au_CompanionSlot, Slot);
    public string AiAccessibleLabel => $"{SlotLabel} · {MonsterName} · {Strings.U_Au_CompanionEditAi}";
    public string MonsterName => MonsterId >= 0 && MonsterId <= 999 && Monster_Dictionary.Instance.TryGetValue((short)MonsterId, out string? name)
        ? name : $"m{MonsterId:000}";
    public AuroraCompanionEditRow(int slot, int monster) { Slot = slot; OriginalMonsterId = monster; MonsterId = monster; }
}
