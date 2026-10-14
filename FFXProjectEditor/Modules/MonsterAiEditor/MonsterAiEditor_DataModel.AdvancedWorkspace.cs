using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

internal partial class MonsterAiEditor_DataModel
{
    bool advancedWorkspaceInitialized;
    [ObservableProperty] private string advancedRouteQuery = string.Empty;
    [ObservableProperty] private int advancedWorkspaceTabIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdvancedWorkspaceFeedback))]
    private string advancedWorkspaceFeedback = string.Empty;
    public bool HasAdvancedWorkspaceFeedback => !string.IsNullOrWhiteSpace(AdvancedWorkspaceFeedback);
    public bool CanEditAdvancedRoute => SelectedAdvancedPhaseUnit?.CanOpenEditor == true;
    public bool CanEditSelectedAdvancedNativeCondition => FindSelectedRouteCondition() != null;

    AiAdvancedNativeConditionRow? FindSelectedRouteCondition()
    {
        if (SelectedAdvancedPhaseUnit == null
            || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out var unit)
            || unit.EditableSlots.Count == 0) return null;
        int start = unit.EditableSlots.Min(slot => slot.PushInstructionOffset);
        return AdvancedNativeConditions.FirstOrDefault(row => row.Source.UsesSwitchRegister && row.Source.BranchTargetOffset == start);
    }

    public void OpenSelectedAdvancedNativeCondition()
    {
        var row = FindSelectedRouteCondition();
        if (row == null) return;
        SelectedAdvancedNativeCondition = row;
        AdvancedWorkspaceTabIndex = 2;
    }

    public bool IsAdvancedDraftTab => AdvancedWorkspaceTabIndex == 3;
    public bool IsAdvancedRoutesTab => AdvancedWorkspaceTabIndex == 0;
    public bool CanApplyAdvancedDraft => PhaseDraftSteps.Count > 0;
    public bool HasNoAdvancedRouteMatches => !AdvancedFilteredPhaseUnits.Any();
    public string AdvancedWorkspaceMonster => SelectedMonster == null
        ? Strings.AiAdvancedChooseMonster
        : string.IsNullOrWhiteSpace(SelectedMonster.MonsterName)
            ? SelectedMonster.Id
            : $"{SelectedMonster.MonsterName} · {SelectedMonster.Id}";
    public string AdvancedCoverageSummary => string.Format(Strings.AiAdvancedCoverage,
        AdvancedPhaseUnits.Count, AdvancedPhaseUnits.Count(unit => unit.CanOpenEditor));
    public string AdvancedRouteSearchSummary => string.Format(Strings.AiAdvancedSearchCount,
        AdvancedFilteredPhaseUnits.Count(), AdvancedPhaseUnits.Count);
    public string AdvancedRouteEmptySummary => string.IsNullOrWhiteSpace(AdvancedRouteQuery)
        ? Strings.AiAdvancedNoRoutes
        : Strings.AiAdvancedNoSearchResults;
    public IEnumerable<AiAdvancedPhaseUnitVm> AdvancedFilteredPhaseUnits => AdvancedPhaseUnits.Where(unit =>
        string.IsNullOrWhiteSpace(AdvancedRouteQuery)
        || $"{unit.Title} {unit.UnitId} {unit.GuardSummary} {unit.CommandSlotsSummary} {unit.TargetSlotsSummary} {unit.FamilyLabel}"
            .Contains(AdvancedRouteQuery.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public void InitializeAdvancedWorkspace()
    {
        if (advancedWorkspaceInitialized) return;
        advancedWorkspaceInitialized = true;
        AdvancedPhaseUnits.CollectionChanged += (_, _) => RefreshAdvancedWorkspace();
        PhaseDraftSteps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanApplyAdvancedDraft));
    }

    partial void OnAdvancedWorkspaceTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAdvancedDraftTab));
        OnPropertyChanged(nameof(IsAdvancedRoutesTab));
    }

    partial void OnAdvancedRouteQueryChanged(string value)
    {
        RefreshAdvancedWorkspace();
        if (SelectedAdvancedPhaseUnit != null && !AdvancedFilteredPhaseUnits.Contains(SelectedAdvancedPhaseUnit))
            SelectedAdvancedPhaseUnit = AdvancedFilteredPhaseUnits.FirstOrDefault();
    }

    void RefreshAdvancedWorkspace()
    {
        OnPropertyChanged(nameof(AdvancedWorkspaceMonster));
        OnPropertyChanged(nameof(AdvancedCoverageSummary));
        OnPropertyChanged(nameof(AdvancedFilteredPhaseUnits));
        OnPropertyChanged(nameof(AdvancedRouteSearchSummary));
        OnPropertyChanged(nameof(HasNoAdvancedRouteMatches));
        OnPropertyChanged(nameof(AdvancedRouteEmptySummary));
    }
}
