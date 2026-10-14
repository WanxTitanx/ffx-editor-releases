using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        readonly Dictionary<string, AiIndirectDispatchUnit> advancedPhaseUnitsById = new(StringComparer.OrdinalIgnoreCase);
        bool syncingAdvancedVariableFocusSelection;
        bool preferAdvancedOpeningFocusOnNextBuild;
        AiIndirectDispatchOpeningFocus? currentAdvancedOpeningFocus;
        string? lastAdvancedCloneSourceUnitId;
        string? lastAdvancedCloneNewUnitId;
        int? lastAdvancedCloneSourceUnitIndex;
        int? lastAdvancedCloneNewUnitIndex;

        public ObservableCollection<AiAdvancedPhaseUnitVm> AdvancedPhaseUnits { get; } = new();
        public ObservableCollection<AiAdvancedVariableFocusRowVm> AdvancedVariableFocusRows { get; } = new();

        [ObservableProperty] private AiAdvancedPhaseUnitVm? selectedAdvancedPhaseUnit;
        partial void OnSelectedAdvancedPhaseUnitChanged(AiAdvancedPhaseUnitVm? value)
        {
            OnPropertyChanged(nameof(HasSelectedAdvancedPhaseUnit));
            OnPropertyChanged(nameof(CanEditAdvancedRoute));
            OnPropertyChanged(nameof(CanEditSelectedAdvancedNativeCondition));
            OnPropertyChanged(nameof(ShowAdvancedPhaseUnitSelectionPlaceholder));
            OnPropertyChanged(nameof(CanCloneSelectedAdvancedPhaseUnit));
            OnPropertyChanged(nameof(SelectedAdvancedPayloadLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedTargetLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedNextStateLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedPayloadCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedTargetCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedConsumerCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedNextStateCardTarget));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedPayloadEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedTargetEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedNextStateEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedPayloadCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedTargetCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedConsumerCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedNextStateCard));
            OnPropertyChanged(nameof(AdvancedDraftSelectionSummary));
            OnPropertyChanged(nameof(AdvancedDraftSelectionDetail));
            SyncAdvancedVariableFocusSelectionForUnit(value);
            UpdateAdvancedVariableFocusSummary();
            UpdateAdvancedPhaseModeSummary();
            UpdateAdvancedFocusInspector();
            UpdateAdvancedConditionContextSummary();
            ApplyFocusHighlight(ActiveFocusContext);
            UpdateAdvancedAuthoringGuidance();
            UpdateAdvancedRouteBundleContext();
        }

        [ObservableProperty] private AiIndirectDispatchEditorLaunchContext? activeFocusContext;

        public string ActiveFocusSummary => ActiveFocusContext == null
            ? string.Empty
            : $"{ActiveFocusContext.VariableName} como {ActiveFocusContext.RoleLabel} ({ActiveFocusContext.RoleKey} @ 0x{ActiveFocusContext.InstructionOffset:X4})";

        public bool HasActiveFocus => ActiveFocusContext != null;

        public void RememberActiveFocus(AiIndirectDispatchEditorLaunchContext? context)
        {
            ActiveFocusContext = context;
            OnPropertyChanged(nameof(ActiveFocusSummary));
            OnPropertyChanged(nameof(HasActiveFocus));
            ApplyFocusHighlight(context);
        }

        public void ClearActiveFocus() => RememberActiveFocus(null);

        void ApplyFocusHighlight(AiIndirectDispatchEditorLaunchContext? context)
        {
            AiAdvancedPhaseUnitVm? unit = SelectedAdvancedPhaseUnit;
            if (unit == null)
                return;

            bool Matches(AiAdvancedPhaseDetailRowVm row) =>
                context != null
                && !string.IsNullOrWhiteSpace(row.VariableName)
                && string.Equals(row.VariableName, context.VariableName, StringComparison.OrdinalIgnoreCase);

            foreach (AiAdvancedPhaseDetailRowVm row in unit.CommandSlots)
                row.IsFocusTarget = context != null
                    && context.FocusKind == AiIndirectDispatchEditorFocusKind.CommandSlot
                    && Matches(row);
            foreach (AiAdvancedPhaseDetailRowVm row in unit.TargetSlots)
                row.IsFocusTarget = context != null
                    && context.FocusKind == AiIndirectDispatchEditorFocusKind.TargetSlot
                    && Matches(row);
        }

        [ObservableProperty] private AiAdvancedVariableFocusRowVm? selectedAdvancedVariableFocusRow;
        partial void OnSelectedAdvancedVariableFocusRowChanged(AiAdvancedVariableFocusRowVm? value)
        {
            OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
            OnPropertyChanged(nameof(ShowAdvancedVariableFocusPlaceholder));
            OnPropertyChanged(nameof(HasSelectedAdvancedVariableFocusRow));
            OnPropertyChanged(nameof(CanSeedPhaseConditionFromAdvancedFocus));
            OnPropertyChanged(nameof(SelectedAdvancedPayloadLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedTargetLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedNextStateLaunchContext));
            OnPropertyChanged(nameof(SelectedAdvancedPayloadCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedTargetCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedConsumerCardTarget));
            OnPropertyChanged(nameof(SelectedAdvancedNextStateCardTarget));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedPayloadEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedTargetEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedNextStateEditor));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedPayloadCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedTargetCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedConsumerCard));
            OnPropertyChanged(nameof(CanOpenSelectedAdvancedNextStateCard));
            OnPropertyChanged(nameof(AdvancedDraftSelectionSummary));
            OnPropertyChanged(nameof(AdvancedDraftSelectionDetail));
            if (syncingAdvancedVariableFocusSelection)
            {
                UpdateAdvancedVariableFocusSummary();
                UpdateAdvancedFocusInspector();
                UpdateAdvancedConditionContextSummary();
                UpdateAdvancedAuthoringGuidance();
                UpdateAdvancedRouteBundleContext();
                return;
            }

            if (value != null)
            {
                AiAdvancedPhaseUnitVm? unit = AdvancedPhaseUnits.FirstOrDefault(candidate =>
                    candidate.UnitId.Equals(value.UnitId, StringComparison.OrdinalIgnoreCase));
                if (unit != null && !ReferenceEquals(unit, SelectedAdvancedPhaseUnit))
                    SelectedAdvancedPhaseUnit = unit;
            }

            UpdateAdvancedVariableFocusSummary();
            UpdateAdvancedFocusInspector();
            UpdateAdvancedConditionContextSummary();
            UpdateAdvancedAuthoringGuidance();
            UpdateAdvancedRouteBundleContext();
        }

        [ObservableProperty] private string advancedPhaseUnitsSummary = Strings.U_Ai_PaIntro;

        [ObservableProperty] private string advancedVariableFocusSummary =
            Strings.F2_select_a_var_to_see_its_structural_role_a0d8e116;

        [ObservableProperty] private string advancedPhaseModeSummary =
            Strings.F2_advanced_v2_read_the_entire_family_grow_d0a850e4;

        [ObservableProperty] private string advancedFocusHeroKicker = Strings.U_Ai_PaStructuralFocus;

        [ObservableProperty] private string advancedFocusHeroTitle = Strings.U_Ai_PaStructuralMap;

        [ObservableProperty] private string advancedFocusHeroSummary =
            Strings.F2_select_a_var_and_a_route_to_see_how_the_65a17415;

        [ObservableProperty] private string advancedFocusHonestSummary =
            Strings.F2_the_indirect_family_remains_fully_on_scr_610d30db;

        [ObservableProperty] private string advancedFocusPreviewBar =
            Strings.F2_no_route_in_focus_choose_a_structural_re_ce514c60;

        [ObservableProperty] private string advancedConditionContextSummary =
            Strings.F2_the_free_builder_above_is_for_drafting_a_cc08febf;

        [ObservableProperty] private string advancedAuthoringPlanTitle =
            Strings.F2_choose_a_route_to_grow_the_family_905652a6;

        [ObservableProperty] private string advancedAuthoringPlanSummary =
            Strings.F2_the_strong_path_in_v2_is_to_clone_an_exi_9955949b;

        [ObservableProperty] private string advancedAuthoringPlanHint =
            Strings.F2_when_a_route_is_in_focus_the_panel_relea_591193c9;

        [ObservableProperty] private string advancedSelectedPayloadActionSummary =
            Strings.F2_select_a_route_to_open_the_payload_lane_aecae74d;

        [ObservableProperty] private string advancedSelectedTargetActionSummary =
            Strings.F2_select_a_route_to_see_if_the_target_of_t_9f155fde;

        [ObservableProperty] private string advancedSelectedConsumersActionSummary =
            Strings.F2_select_a_route_to_open_the_full_package_c0502f47;

        [ObservableProperty] private string advancedSelectedNextStateActionSummary =
            Strings.F2_select_a_route_to_see_if_the_next_step_o_d9601300;

        public bool HasAdvancedPhaseUnits => AdvancedPhaseUnits.Count > 0;
        public bool HasSelectedAdvancedPhaseUnit => SelectedAdvancedPhaseUnit != null;
        public bool ShowAdvancedPhaseUnitSelectionPlaceholder => SelectedAdvancedPhaseUnit == null;
        public bool HasAdvancedVariableFocusRows => AdvancedVariableFocusRows.Count > 0;
        public bool ShowAdvancedVariableFocusPlaceholder => AdvancedVariableFocusRows.Count == 0;
        public bool HasSelectedAdvancedVariableFocusRow => SelectedAdvancedVariableFocusRow != null;
        public bool CanSeedPhaseConditionFromAdvancedFocus =>
            SelectedPhaseVariableAudit != null && SelectedAdvancedVariableFocusRow != null;
        public AiIndirectDispatchEditorLaunchContext? SelectedAdvancedPayloadLaunchContext =>
            BuildSelectedAdvancedQuickEditContext(AiIndirectDispatchEditorFocusKind.CommandSlot);
        public AiIndirectDispatchEditorLaunchContext? SelectedAdvancedTargetLaunchContext =>
            BuildSelectedAdvancedQuickEditContext(AiIndirectDispatchEditorFocusKind.TargetSlot);
        public AiIndirectDispatchEditorLaunchContext? SelectedAdvancedNextStateLaunchContext =>
            BuildSelectedAdvancedQuickEditContext(AiIndirectDispatchEditorFocusKind.NextState);
        public object? SelectedAdvancedPayloadCardTarget =>
            BuildSelectedAdvancedCardTarget(AiIndirectDispatchEditorFocusKind.CommandSlot);
        public object? SelectedAdvancedTargetCardTarget =>
            BuildSelectedAdvancedCardTarget(AiIndirectDispatchEditorFocusKind.TargetSlot);
        public object? SelectedAdvancedConsumerCardTarget =>
            BuildSelectedAdvancedCardTarget(null);
        public object? SelectedAdvancedNextStateCardTarget =>
            BuildSelectedAdvancedCardTarget(AiIndirectDispatchEditorFocusKind.NextState);
        public bool CanOpenSelectedAdvancedPayloadEditor => SelectedAdvancedPayloadLaunchContext != null;
        public bool CanOpenSelectedAdvancedTargetEditor => SelectedAdvancedTargetLaunchContext != null;
        public bool CanOpenSelectedAdvancedNextStateEditor => SelectedAdvancedNextStateLaunchContext != null;
        public bool CanOpenSelectedAdvancedPayloadCard => SelectedAdvancedPayloadCardTarget != null;
        public bool CanOpenSelectedAdvancedTargetCard => SelectedAdvancedTargetCardTarget != null;
        public bool CanOpenSelectedAdvancedConsumerCard => SelectedAdvancedConsumerCardTarget != null;
        public bool CanOpenSelectedAdvancedNextStateCard => SelectedAdvancedNextStateCardTarget != null;
        public string AdvancedDraftSelectionSummary =>
            SelectedPhaseDraftStep == null
                ? SelectedAdvancedPhaseUnit == null
                    ? Strings.F2_no_draft_phase_is_armed_yet_4b680351
                    : string.Format(Strings.U_Ai_PaNoDraftPhase, SelectedAdvancedPhaseUnit.Title)
                : $"{SelectedPhaseDraftStep.StepLabel}: {SelectedPhaseDraftStep.Title}";
        public string AdvancedDraftSelectionDetail =>
            SelectedPhaseDraftStep == null
                ? SelectedAdvancedPhaseUnit == null
                    ? Strings.U_Ai_PaUseCommonPhase
                    : string.Format(Strings.U_Ai_PaUseCommonPhaseFor, SelectedAdvancedPhaseUnit.Title)
                : $"{SelectedPhaseDraftStep.TriggerSummary} · alvo {SelectedPhaseDraftStep.TargetSummary}. {SelectedPhaseDraftStep.GuardModeSummary}";

        public void PrepareAdvancedPhaseRotationManager()
        {
            SelectedDraftIndirectRoute = null;
            preferAdvancedOpeningFocusOnNextBuild = true;
            RefreshPhaseRotationAudit();
            preferAdvancedOpeningFocusOnNextBuild = true;
            if (!TryPromoteAdvancedOpeningSelection())
                RebuildAdvancedPhaseUnits();
            UpdateAdvancedPhaseModeSummary();
            UpdateAdvancedFocusInspector();
            UpdateAdvancedConditionContextSummary();
            UpdateAdvancedAuthoringGuidance();
            OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
        }

        void UpdateAdvancedSpecializedFamilyContexts()
        {
            RefreshAdvancedSeymourHandoff();
            UpdateAdvancedFluxFamilyContext();
            UpdateAdvancedFluxThresholdContext();
            UpdateAdvancedAnimaContext();
            UpdateAdvancedElementalClusterContext();
            UpdateAdvancedMortiorchisContext();
            UpdateAdvancedSupportAccumulatorContext();
            UpdateAdvancedReactiveSensorContext();
            UpdateAdvancedRoundScriptedBossContext();
            UpdateAdvancedTonberryCameraRoutingContext();
            UpdateAdvancedEncounterAppearContext();
            UpdateAdvancedCompanionActivationContext();
            RefreshAdvancedNativeConditions();
            RefreshAdvancedExpressions();
            RefreshAdvancedBattleScenes();
        }

        void RebuildAdvancedPhaseUnits()
        {
            bool preferOpeningFocus = preferAdvancedOpeningFocusOnNextBuild;
            preferAdvancedOpeningFocusOnNextBuild = false;
            string? previousUnitId = preferOpeningFocus ? null : SelectedAdvancedPhaseUnit?.UnitId;
            string? previousFocusRowKey = preferOpeningFocus ? null : SelectedAdvancedVariableFocusRow?.StableKey;
            AdvancedPhaseUnits.Clear();
            advancedPhaseUnitsById.Clear();
            AdvancedVariableFocusRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                currentAdvancedOpeningFocus = null;
                SelectedAdvancedPhaseUnit = null;
                SelectedAdvancedVariableFocusRow = null;
                AdvancedPhaseUnitsSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_bab06d36;
                AdvancedVariableFocusSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_93a873a9;
                UpdateAdvancedSpecializedFamilyContexts();
                OnPropertyChanged(nameof(HasAdvancedPhaseUnits));
                OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
            {
                currentAdvancedOpeningFocus = null;
                SelectedAdvancedPhaseUnit = null;
                SelectedAdvancedVariableFocusRow = null;
                AdvancedPhaseUnitsSummary = Strings.F2_the_current_monster_does_not_have_an_acc_6b850432;
                AdvancedVariableFocusSummary = Strings.F2_no_accessible_bin_available_cannot_focus_8ce7ebda;
                UpdateAdvancedSpecializedFamilyContexts();
                OnPropertyChanged(nameof(HasAdvancedPhaseUnits));
                OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
                return;
            }

            byte[] monsterBin;
            try
            {
                monsterBin = File.ReadAllBytes(selectedPath);
            }
            catch (Exception ex)
            {
                currentAdvancedOpeningFocus = null;
                SelectedAdvancedPhaseUnit = null;
                SelectedAdvancedVariableFocusRow = null;
                AdvancedPhaseUnitsSummary = string.Format(Strings.U_Ai_PaOpenFailed, ex.Message);
                AdvancedVariableFocusSummary = string.Format(Strings.U_Ai_PaOpenVarFocusFailed, ex.Message);
                UpdateAdvancedSpecializedFamilyContexts();
                OnPropertyChanged(nameof(HasAdvancedPhaseUnits));
                OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
                return;
            }

            ushort? selectedRuntimeIndex = null;
            if (SelectedPhaseVariableAudit != null && TryGetRuntimeVariableIndex(SelectedPhaseVariableAudit, out ushort runtimeIndex))
                selectedRuntimeIndex = runtimeIndex;

            List<AiIndirectDispatchUnit> units = DetectPromotedIndirectDispatchUnits(monsterBin)
                .OrderBy(unit => unit.UnitIndex)
                .ToList();

            foreach (AiIndirectDispatchUnit unit in units)
            {
                advancedPhaseUnitsById[unit.UnitId] = unit;
                AdvancedPhaseUnits.Add(BuildAdvancedPhaseUnitVm(unit, SelectedPhaseVariableAudit, selectedRuntimeIndex));
            }

            RebuildAdvancedVariableFocusRows(units, SelectedPhaseVariableAudit, selectedRuntimeIndex, previousFocusRowKey);

            if (AdvancedPhaseUnits.Count == 0)
            {
                currentAdvancedOpeningFocus = null;
                SelectedAdvancedPhaseUnit = null;
                SelectedAdvancedVariableFocusRow = null;
                AdvancedPhaseUnitsSummary =
                    Strings.F2_no_complex_family_detected_for_this_mons_f00dbbdd;
                UpdateAdvancedSpecializedFamilyContexts();
                OnPropertyChanged(nameof(HasAdvancedPhaseUnits));
                return;
            }

            int previewCount = AdvancedPhaseUnits.Count(unit => !unit.CanOpenEditor);
            int linkedCount = AdvancedPhaseUnits.Count(unit => unit.IsLinkedToSelectedVariable);
            if (SelectedPhaseVariableAudit == null)
            {
                AdvancedPhaseUnitsSummary =
                    previewCount > 0
                        ? string.Format(Strings.U_Ai_PaComplexFamilies, AdvancedPhaseUnits.Count, previewCount)
                        : string.Format(Strings.U_Ai_PaIndirectFamilies, AdvancedPhaseUnits.Count);
            }
            else if (linkedCount == 0)
            {
                AdvancedPhaseUnitsSummary =
                    previewCount > 0
                        ? string.Format(Strings.U_Ai_PaComplexFamiliesNoPart, AdvancedPhaseUnits.Count, previewCount)
                        : string.Format(Strings.U_Ai_PaIndirectFamiliesNoPart, AdvancedPhaseUnits.Count);
            }
            else
            {
                AdvancedPhaseUnitsSummary =
                    previewCount > 0
                        ? $"{AdvancedPhaseUnits.Count} familia(s) complexa(s) detectada(s), incluindo {previewCount} preview(s) read-only; {linkedCount} ligada(s) a {SelectedPhaseVariableAudit.VariableName}."
                        : $"{AdvancedPhaseUnits.Count} familia(s) indireta(s) detectada(s); {linkedCount} ligada(s) a {SelectedPhaseVariableAudit.VariableName}.";
            }

            string? preferredLinkedUnitId = SelectedAdvancedVariableFocusRow?.UnitId
                ?? AdvancedVariableFocusRows.FirstOrDefault()?.UnitId;
            SelectedAdvancedPhaseUnit =
                (!string.IsNullOrWhiteSpace(previousUnitId)
                    ? AdvancedPhaseUnits.FirstOrDefault(unit => unit.UnitId.Equals(previousUnitId, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? (!string.IsNullOrWhiteSpace(preferredLinkedUnitId)
                    ? AdvancedPhaseUnits.FirstOrDefault(unit => unit.UnitId.Equals(preferredLinkedUnitId, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? AdvancedPhaseUnits
                    .OrderByDescending(unit => unit.IsLinkedToSelectedVariable)
                    .ThenBy(unit => unit.UnitIndex)
                    .FirstOrDefault();

            UpdateAdvancedSpecializedFamilyContexts();
            OnPropertyChanged(nameof(HasAdvancedPhaseUnits));
            OnPropertyChanged(nameof(CanCloneSelectedAdvancedPhaseUnit));
        }

        void RebuildAdvancedVariableFocusRows(
            IReadOnlyList<AiIndirectDispatchUnit> units,
            AiPhaseVariableAuditRow? selectedVariable,
            ushort? selectedRuntimeIndex,
            string? previousFocusRowKey)
        {
            AdvancedVariableFocusRows.Clear();

            if (selectedVariable == null || !selectedRuntimeIndex.HasValue)
            {
                currentAdvancedOpeningFocus = null;
                SetSelectedAdvancedVariableFocusRow(null);
                AdvancedVariableFocusSummary = Strings.F2_select_a_var_to_see_its_structural_role_a0d8e116;
                OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
                OnPropertyChanged(nameof(ShowAdvancedVariableFocusPlaceholder));
                return;
            }

            ushort runtimeIndex = selectedRuntimeIndex.Value;
            currentAdvancedOpeningFocus =
                AiAutomation.DescribeIndirectDispatchOpeningFocus(units, selectedVariable.VariableName, runtimeIndex);
            foreach (AiIndirectDispatchUnit unit in units)
            {
                foreach (AiAdvancedVariableFocusRowVm row in BuildAdvancedVariableFocusRows(unit, selectedVariable, runtimeIndex))
                    AdvancedVariableFocusRows.Add(row);
            }

            AiAdvancedVariableFocusRowVm? selectedRow =
                (!string.IsNullOrWhiteSpace(previousFocusRowKey)
                    ? AdvancedVariableFocusRows.FirstOrDefault(row =>
                        row.StableKey.Equals(previousFocusRowKey, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? FindFocusRowForOpeningContract(currentAdvancedOpeningFocus)
                ?? PickPreferredAdvancedOpeningFocusRow(AdvancedVariableFocusRows)
                ?? AdvancedVariableFocusRows.FirstOrDefault();
            SetSelectedAdvancedVariableFocusRow(selectedRow);
            UpdateAdvancedVariableFocusSummary();
            OnPropertyChanged(nameof(HasAdvancedVariableFocusRows));
            OnPropertyChanged(nameof(ShowAdvancedVariableFocusPlaceholder));
        }

        bool TryPromoteAdvancedOpeningSelection()
        {
            if (selectedScript == null
                || !selectedScript.HasScript
                || string.IsNullOrWhiteSpace(selectedPath)
                || !File.Exists(selectedPath)
                || PhaseVariableAudits.Count == 0)
            {
                return false;
            }

            List<AiIndirectDispatchUnit> units;
            try
            {
                byte[] monsterBin = File.ReadAllBytes(selectedPath);
                units = DetectPromotedIndirectDispatchUnits(monsterBin)
                    .OrderBy(unit => unit.UnitIndex)
                    .ToList();
            }
            catch
            {
                return false;
            }

            if (units.Count == 0)
                return false;

            AiIndirectDispatchOpeningFocus? best = AiAutomation.PickIndirectDispatchOpeningSelection(selectedScript, units);
            if (best == null)
                return false;

            AiPhaseVariableAuditRow? bestAudit = PhaseVariableAudits.FirstOrDefault(audit =>
                TryGetRuntimeVariableIndex(audit, out ushort runtimeIndex) && runtimeIndex == best.VariableIndex);
            bestAudit ??= PhaseVariableAudits.FirstOrDefault(audit =>
                audit.VariableName.Equals(best.VariableName, StringComparison.OrdinalIgnoreCase));
            if (bestAudit == null)
                return false;

            if (SelectedPhaseVariableAudit != null
                && bestAudit.AliasKey.Equals(SelectedPhaseVariableAudit.AliasKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (SelectedPhaseVariableAudit != null
                && TryGetRuntimeVariableIndex(SelectedPhaseVariableAudit, out ushort currentRuntimeIndex))
            {
                AiIndirectDispatchOpeningFocus? current =
                    AiAutomation.DescribeIndirectDispatchOpeningFocus(
                        units,
                        SelectedPhaseVariableAudit.VariableName,
                        currentRuntimeIndex);
                if (current != null && !ShouldPromoteAdvancedOpeningSelection(current, best))
                    return false;
            }

            preferAdvancedOpeningFocusOnNextBuild = true;
            SelectedPhaseVariableAudit = bestAudit;
            return true;
        }

        static bool ShouldPromoteAdvancedOpeningSelection(
            AiIndirectDispatchOpeningFocus current,
            AiIndirectDispatchOpeningFocus best)
        {
            if (best.GuardRouteCount > current.GuardRouteCount && best.GuardRouteCount >= 2)
                return true;

            if (current.GuardRouteCount == 0 && best.GuardRouteCount > 0)
                return true;

            if (current.LinkedRouteCount == 0 && best.LinkedRouteCount > 0)
                return true;

            return false;
        }

        AiAdvancedVariableFocusRowVm? FindFocusRowForOpeningContract(AiIndirectDispatchOpeningFocus? focus)
        {
            if (focus == null)
                return null;

            return AdvancedVariableFocusRows.FirstOrDefault(row =>
                       row.UnitId.Equals(focus.PreferredUnitId, StringComparison.OrdinalIgnoreCase)
                       && row.RoleLabel.Equals(focus.PreferredRoleLabel, StringComparison.OrdinalIgnoreCase)
                       && (focus.PreferredInstructionOffset < 0 || row.OffsetSortKey == focus.PreferredInstructionOffset))
                   ?? AdvancedVariableFocusRows.FirstOrDefault(row =>
                       row.UnitId.Equals(focus.PreferredUnitId, StringComparison.OrdinalIgnoreCase)
                       && row.RoleLabel.Equals(focus.PreferredRoleLabel, StringComparison.OrdinalIgnoreCase))
                   ?? AdvancedVariableFocusRows.FirstOrDefault(row =>
                       row.UnitIndex == focus.PreferredUnitIndex
                       && row.RoleLabel.Equals(focus.PreferredRoleLabel, StringComparison.OrdinalIgnoreCase));
        }

        static AiAdvancedVariableFocusRowVm? PickPreferredAdvancedOpeningFocusRow(
            IReadOnlyList<AiAdvancedVariableFocusRowVm> rows)
        {
            List<AiAdvancedVariableFocusRowVm> guardRows = rows
                .Where(row => row.IsGuardRole)
                .ToList();
            if (guardRows.Count > 0)
            {
                double midpoint = (guardRows.Min(row => row.UnitIndex) + guardRows.Max(row => row.UnitIndex)) / 2.0;
                return guardRows
                    .OrderBy(row => Math.Abs(row.UnitIndex - midpoint))
                    .ThenByDescending(row => row.UnitIndex)
                    .FirstOrDefault();
            }

            return rows
                .OrderBy(row => row.RoleSortKey)
                .ThenBy(row => row.UnitIndex)
                .FirstOrDefault();
        }

        void UpdateAdvancedPhaseModeSummary()
        {
            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedPhaseModeSummary = Strings.U_Ai_PaV2Intro;
                return;
            }

            if (!SelectedAdvancedPhaseUnit.CanOpenEditor)
            {
                AdvancedPhaseModeSummary =
                    string.Format(Strings.U_Ai_PaComplexStructuralRead, SelectedAdvancedPhaseUnit.Title);
                return;
            }

            if (!SelectedAdvancedPhaseUnit.CanCloneRoute)
            {
                AdvancedPhaseModeSummary =
                    string.Format(Strings.U_Ai_PaRowOnlyLocal, SelectedAdvancedPhaseUnit.Title);
                return;
            }

            AdvancedPhaseModeSummary =
                string.Format(Strings.U_Ai_PaRouteGuard, SelectedAdvancedPhaseUnit.UnitIndex, SelectedAdvancedPhaseUnit.GuardSummary) +
                Strings.F2_current_safe_edit_clone_this_route_to_op_109a404a;
        }

        void UpdateAdvancedAuthoringGuidance()
        {
            UpdateAdvancedQuickEditSummaries();

            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedAuthoringPlanTitle = Strings.F2_choose_a_route_to_grow_the_family_905652a6;
                AdvancedAuthoringPlanSummary =
                    Strings.F2_the_strong_path_in_v2_is_to_clone_an_exi_9955949b;
                AdvancedAuthoringPlanHint =
                    Strings.F2_when_the_route_is_in_focus_the_panel_rel_58e1f802;
                return;
            }

            if (!SelectedAdvancedPhaseUnit.CanOpenEditor)
            {
                AdvancedAuthoringPlanTitle = string.Format(Strings.U_Ai_PaHonestRead, SelectedAdvancedPhaseUnit.Title);
                AdvancedAuthoringPlanSummary =
                    Strings.F2_this_family_was_recognized_as_a_complex_8a2491a3;
                AdvancedAuthoringPlanHint =
                    Strings.F2_if_the_goal_is_real_authoring_here_the_s_3da4e994;
                return;
            }

            if (!SelectedAdvancedPhaseUnit.CanCloneRoute)
            {
                AdvancedAuthoringPlanTitle = string.Format(Strings.U_Ai_PaLightEditor, SelectedAdvancedPhaseUnit.Title);
                AdvancedAuthoringPlanSummary =
                    Strings.F2_this_slice_already_exposes_real_indirect_3bc741d8;
                AdvancedAuthoringPlanHint = CanOpenSelectedAdvancedPayloadEditor
                    ? Strings.F2_start_with_switch_ability_and_then_revie_4d089370
                    : Strings.F2_open_the_full_editor_to_review_the_paylo_4ab270f2;
                return;
            }

            int suggestedRouteIndex = AdvancedPhaseUnits.Count == 0
                ? SelectedAdvancedPhaseUnit.UnitIndex + 1
                : AdvancedPhaseUnits.Max(unit => unit.UnitIndex) + 1;
            bool showingRecentClone =
                !string.IsNullOrWhiteSpace(lastAdvancedCloneNewUnitId)
                && SelectedAdvancedPhaseUnit.UnitId.Equals(lastAdvancedCloneNewUnitId, StringComparison.OrdinalIgnoreCase);

            if (showingRecentClone)
            {
                AdvancedAuthoringPlanTitle = string.Format(Strings.U_Ai_PaBornVariation, SelectedAdvancedPhaseUnit.Title);
                AdvancedAuthoringPlanSummary = lastAdvancedCloneSourceUnitIndex.HasValue
                    ? string.Format(Strings.U_Ai_PaCloneDone, lastAdvancedCloneSourceUnitIndex.Value)
                    : Strings.F2_structural_cloning_completed_now_the_str_0394cd56;
                AdvancedAuthoringPlanHint = CanOpenSelectedAdvancedPayloadEditor
                    ? Strings.F2_if_the_idea_is_to_open_a_fifth_option_wi_54b5760a
                    : Strings.F2_the_new_route_is_already_in_focus_open_t_682920cf;
                return;
            }

            AdvancedAuthoringPlanTitle = string.Format(Strings.U_Ai_PaOpenRoute, suggestedRouteIndex);
            AdvancedAuthoringPlanSummary =
                string.Format(Strings.U_Ai_PaCloneStrongPath, SelectedAdvancedPhaseUnit.Title);
            AdvancedAuthoringPlanHint = CanOpenSelectedAdvancedPayloadEditor
                ? Strings.F2_after_cloning_use_switch_ability_to_set_a0227d12
                : Strings.F2_after_cloning_the_safe_next_step_is_to_o_6a350898;
        }

        void UpdateAdvancedQuickEditSummaries()
        {
            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedSelectedPayloadActionSummary =
                    Strings.F2_select_a_route_to_open_the_payload_lane_aecae74d;
                AdvancedSelectedTargetActionSummary =
                    Strings.F2_select_a_route_to_see_if_the_target_of_t_9f155fde;
                AdvancedSelectedConsumersActionSummary =
                    Strings.F2_select_a_route_to_open_the_full_package_c0502f47;
                AdvancedSelectedNextStateActionSummary =
                    Strings.F2_select_a_route_to_see_if_the_next_step_o_d9601300;
                return;
            }

            if (!SelectedAdvancedPhaseUnit.CanOpenEditor)
            {
                AdvancedSelectedPayloadActionSummary =
                    Strings.F2_click_to_open_the_full_read_of_this_pack_40dac36d;
                AdvancedSelectedTargetActionSummary =
                    Strings.F2_click_to_open_the_full_read_of_this_pack_c89a300e;
                AdvancedSelectedConsumersActionSummary =
                    Strings.F2_click_to_open_the_full_read_of_this_pack_f878958a;
                AdvancedSelectedNextStateActionSummary =
                    Strings.F2_click_to_open_the_full_read_of_this_pack_1d8fede2;
                return;
            }

            AdvancedSelectedPayloadActionSummary = CanOpenSelectedAdvancedPayloadEditor
                ? Strings.F2_click_to_swap_the_ability_of_this_route_4b487052
                : Strings.F2_the_payload_of_this_route_still_has_no_n_3c000ec7;
            AdvancedSelectedTargetActionSummary = CanOpenSelectedAdvancedTargetEditor
                ? Strings.F2_click_to_swap_the_target_of_this_route_dbcba792
                : Strings.F2_the_target_of_this_route_remains_structu_77d17cc8;
            AdvancedSelectedConsumersActionSummary =
                Strings.F2_click_to_open_the_full_package_of_this_r_ae1805a6;
            AdvancedSelectedNextStateActionSummary = CanOpenSelectedAdvancedNextStateEditor
                ? Strings.F2_click_to_adjust_the_next_step_of_this_ro_b3e6d391
                : Strings.F2_the_next_step_of_this_route_remains_stru_40137ef4;
        }

        void UpdateAdvancedVariableFocusSummary()
        {
            if (SelectedPhaseVariableAudit == null)
            {
                AdvancedVariableFocusSummary = Strings.F2_select_a_var_to_see_its_structural_role_a0d8e116;
                return;
            }

            if (AdvancedVariableFocusRows.Count == 0)
            {
                AdvancedVariableFocusSummary =
                    string.Format(Strings.U_Ai_PaVarNotAppeared, SelectedPhaseVariableAudit.VariableName);
                return;
            }

            int routeCount = AdvancedVariableFocusRows
                .Select(row => row.UnitId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            string routeSummary = currentAdvancedOpeningFocus?.RouteSummary ?? string.Empty;

            List<string> roleSample = AdvancedVariableFocusRows
                .Select(row => row.RoleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            string roleText = string.Join(" · ", roleSample) + (AdvancedVariableFocusRows.Count > roleSample.Count ? " · ..." : string.Empty);
            if (SelectedAdvancedVariableFocusRow != null)
            {
                AdvancedVariableFocusSummary =
                    $"{SelectedPhaseVariableAudit.VariableName}: " +
                    $"{(string.IsNullOrWhiteSpace(routeSummary) ? string.Format(Strings.U_Ai_PaStructuralReads, AdvancedVariableFocusRows.Count, routeCount) : $"{routeSummary}.")} " +
                    string.Format(Strings.U_Ai_PaCurrentFocus, DescribeAdvancedFocusScope(SelectedAdvancedVariableFocusRow), SelectedAdvancedVariableFocusRow.RoleLabel) +
                    string.Format(Strings.U_Ai_PaRolesSeen, roleText);
                return;
            }

            AdvancedVariableFocusSummary =
                $"{SelectedPhaseVariableAudit.VariableName}: " +
                $"{(string.IsNullOrWhiteSpace(routeSummary) ? string.Format(Strings.U_Ai_PaStructuralReads, AdvancedVariableFocusRows.Count, routeCount) : $"{routeSummary}.")} " +
                string.Format(Strings.U_Ai_PaRolesSeen, roleText);
        }

        void UpdateAdvancedFocusInspector()
        {
            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedFocusHeroKicker = Strings.U_Ai_PaStructuralFocus;
                AdvancedFocusHeroTitle = Strings.U_Ai_PaComplexMap;
                AdvancedFocusHeroSummary =
                    Strings.F2_select_a_var_and_a_route_to_see_how_the_65a17415;
                AdvancedFocusHonestSummary =
                    Strings.F2_the_indirect_family_remains_fully_on_scr_610d30db;
                AdvancedFocusPreviewBar =
                    Strings.F2_no_route_in_focus_choose_a_structural_re_ce514c60;
                return;
            }

            bool previewReadOnly = !SelectedAdvancedPhaseUnit.CanOpenEditor;
            string routeLabel = DescribeAdvancedUnitScope(SelectedAdvancedPhaseUnit);
            string payloadSummary = BuildPrimaryPayloadHeadline(SelectedAdvancedPhaseUnit.UnitId);
            string nextStateSummary = BuildShortNextStateHeadline(SelectedAdvancedPhaseUnit.NextStateSummary);

            if (SelectedAdvancedVariableFocusRow == null)
            {
                AdvancedFocusHeroKicker = previewReadOnly ? Strings.U_Ai_PaComplexRead : Strings.U_Ai_PaStructuralFocus;
                AdvancedFocusHeroTitle = previewReadOnly ? Strings.U_Ai_PaFullPackageContext : Strings.U_Ai_PaFullRouteContext;
                AdvancedFocusHeroSummary =
                    previewReadOnly
                        ? string.Format(Strings.U_Ai_PaViewComplexFamily, routeLabel)
                        : string.Format(Strings.U_Ai_PaViewCompleteUnit, routeLabel);
                AdvancedFocusHonestSummary =
                    previewReadOnly
                        ? Strings.F2_this_clip_is_a_read_only_preview_v2_diss_566c1333
                        : Strings.U_Ai_PaStructuralReadFirst;
                AdvancedFocusPreviewBar =
                    string.Format(Strings.U_Ai_PaObservedEntry, SelectedAdvancedPhaseUnit.GuardSummary, payloadSummary, nextStateSummary);
                return;
            }

            AiAdvancedVariableFocusRowVm row = SelectedAdvancedVariableFocusRow;
            if (row.IsGuardRole)
            {
                string variableName = SelectedPhaseVariableAudit?.VariableName ?? Strings.F2_the_selected_var_52885feb;
                AdvancedFocusHeroKicker = previewReadOnly ? Strings.U_Ai_PaObservedEntryShort : Strings.U_Ai_PaRouteEntry;
                AdvancedFocusHeroTitle = previewReadOnly ? Strings.F2_package_guided_by_selector_a8359cb2 : Strings.F2_route_guided_by_phase_selector_34f6715d;
                AdvancedFocusHeroSummary =
                    string.Format(Strings.U_Ai_PaStrongestCut, variableName, routeLabel);
                AdvancedFocusHonestSummary =
                    string.Format(Strings.U_Ai_PaReadOnlyGuard, row.ValueSummary, routeLabel);
                AdvancedFocusPreviewBar =
                    string.Format(Strings.U_Ai_PaObservedEntry, row.ValueSummary, payloadSummary, nextStateSummary);
                return;
            }

            if (row.IsCommandRole)
            {
                AdvancedFocusHeroKicker = previewReadOnly ? Strings.U_Ai_PaObservedPayload : Strings.U_Ai_PaRouteSkill;
                AdvancedFocusHeroTitle = previewReadOnly ? Strings.U_Ai_PaObservedPayloadFamily : Strings.U_Ai_PaEditablePayloadRoute;
                AdvancedFocusHeroSummary =
                    string.Format(Strings.U_Ai_PaViewCommandSlot, routeLabel);
                AdvancedFocusHonestSummary =
                    string.Format(Strings.U_Ai_PaPayloadNoRumor, row.ValueSummary, routeLabel, SelectedAdvancedPhaseUnit.GuardSummary);
                AdvancedFocusPreviewBar =
                    string.Format(Strings.U_Ai_PaMainSkill, row.ValueSummary, SelectedAdvancedPhaseUnit.GuardSummary, nextStateSummary);
                return;
            }

            if (row.IsTargetRole)
            {
                AdvancedFocusHeroKicker = previewReadOnly ? Strings.U_Ai_PaObservedTarget : Strings.U_Ai_PaRouteTarget;
                AdvancedFocusHeroTitle = previewReadOnly ? Strings.U_Ai_PaObservedTargetFamily : Strings.U_Ai_PaEditableTargetRoute;
                AdvancedFocusHeroSummary =
                    string.Format(Strings.U_Ai_PaViewTargetSlot, routeLabel);
                AdvancedFocusHonestSummary =
                    string.Format(Strings.U_Ai_PaTargetNoLongerImplicit, row.ValueSummary);
                AdvancedFocusPreviewBar =
                    string.Format(Strings.U_Ai_PaCurrentTarget, row.ValueSummary, SelectedAdvancedPhaseUnit.GuardSummary, payloadSummary);
                return;
            }

            if (row.IsNextStateRole)
            {
                AdvancedFocusHeroKicker = previewReadOnly ? Strings.U_Ai_PaObservedAftermath : Strings.U_Ai_PaNextStep;
                AdvancedFocusHeroTitle = previewReadOnly ? Strings.U_Ai_PaObservedClosure : Strings.U_Ai_PaNextStepRoute;
                AdvancedFocusHeroSummary =
                    string.Format(Strings.U_Ai_PaViewCycleClosure, routeLabel);
                AdvancedFocusHonestSummary =
                    string.Format(Strings.U_Ai_PaNextStateImplicit, row.ValueSummary, routeLabel);
                AdvancedFocusPreviewBar =
                    string.Format(Strings.U_Ai_PaNextStep, row.ValueSummary, SelectedAdvancedPhaseUnit.GuardSummary, payloadSummary);
                return;
            }

            AdvancedFocusHeroKicker = Strings.U_Ai_PaStructuralFocus;
            AdvancedFocusHeroTitle = Strings.U_Ai_PaRouteInFocus;
            AdvancedFocusHeroSummary =
                string.Format(Strings.U_Ai_PaViewRole, routeLabel, row.RoleLabel);
            AdvancedFocusHonestSummary =
                string.Format(Strings.U_Ai_PaContextualized, row.ValueSummary);
            AdvancedFocusPreviewBar =
                string.Format(Strings.U_Ai_PaRoleBar, row.RoleLabel, row.ValueSummary, SelectedAdvancedPhaseUnit.GuardSummary, nextStateSummary);
        }

        void UpdateAdvancedConditionContextSummary()
        {
            OnPropertyChanged(nameof(CanSeedPhaseConditionFromAdvancedFocus));
            if (SelectedPhaseVariableAudit == null)
            {
                AdvancedConditionContextSummary =
                    Strings.F2_select_a_var_to_use_the_free_builder_as_add5c63f;
                return;
            }

            if (SelectedAdvancedVariableFocusRow == null)
            {
                AdvancedConditionContextSummary =
                    string.Format(Strings.U_Ai_PaVarDefaultCondition, SelectedPhaseVariableAudit.VariableName) +
                    $"{(SelectedPhaseDraftStep == null ? Strings.U_Ai_PaDryCommonPhase : string.Format(Strings.U_Ai_PaDraftIs, SelectedPhaseDraftStep.StepLabel))} " +
                    Strings.F2_the_indirect_family_continues_to_be_read_ebe323e6;
                return;
            }

            if (SelectedAdvancedVariableFocusRow.IsGuardRole)
            {
                AdvancedConditionContextSummary =
                    Strings.U_Ai_PaGuardDetected +
                    $"{(SelectedPhaseDraftStep == null ? Strings.U_Ai_PaAttachNoPhase : string.Format(Strings.U_Ai_PaConditionLands, SelectedPhaseDraftStep.StepLabel))} " +
                    "The original guard of the family continues as structural reading, not arbitrary writer.";
                return;
            }

            AdvancedConditionContextSummary =
                string.Format(Strings.U_Ai_PaFreeBuilderGuard, SelectedPhaseVariableAudit.VariableName, SelectedAdvancedVariableFocusRow.RoleLabel) +
                $"{(SelectedPhaseDraftStep == null ? Strings.U_Ai_PaNoPhaseCreates : string.Format(Strings.U_Ai_PaDestinationIs, SelectedPhaseDraftStep.StepLabel))}";
        }

        AiIndirectDispatchEditorLaunchContext? BuildSelectedAdvancedQuickEditContext(
            AiIndirectDispatchEditorFocusKind focusKind)
        {
            if (SelectedAdvancedPhaseUnit == null
                || !SelectedAdvancedPhaseUnit.CanOpenEditor
                || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out AiIndirectDispatchUnit? unit))
            {
                return null;
            }

            if (SelectedAdvancedVariableFocusRow != null
                && SelectedAdvancedVariableFocusRow.UnitId.Equals(unit.UnitId, StringComparison.OrdinalIgnoreCase))
            {
                if ((focusKind == AiIndirectDispatchEditorFocusKind.CommandSlot && SelectedAdvancedVariableFocusRow.IsCommandRole)
                    || (focusKind == AiIndirectDispatchEditorFocusKind.TargetSlot && SelectedAdvancedVariableFocusRow.IsTargetRole)
                    || (focusKind == AiIndirectDispatchEditorFocusKind.NextState && SelectedAdvancedVariableFocusRow.IsNextStateRole))
                {
                    return SelectedAdvancedVariableFocusRow.EditorLaunchContext;
                }
            }

            return focusKind switch
            {
                AiIndirectDispatchEditorFocusKind.CommandSlot => BuildSelectedAdvancedCommandContext(unit),
                AiIndirectDispatchEditorFocusKind.TargetSlot => BuildSelectedAdvancedTargetContext(unit),
                AiIndirectDispatchEditorFocusKind.NextState => BuildSelectedAdvancedNextStateContext(unit),
                _ => null,
            };
        }

        object? BuildSelectedAdvancedCardTarget(AiIndirectDispatchEditorFocusKind? focusKind)
        {
            if (SelectedAdvancedPhaseUnit == null
                || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out AiIndirectDispatchUnit? unit))
            {
                return null;
            }

            if (focusKind.HasValue)
            {
                AiIndirectDispatchEditorLaunchContext? quickContext = BuildSelectedAdvancedQuickEditContext(focusKind.Value);
                if (quickContext != null)
                    return quickContext;
            }

            return unit.UnitId;
        }

        static AiIndirectDispatchEditorLaunchContext? BuildSelectedAdvancedCommandContext(AiIndirectDispatchUnit unit)
        {
            AiIndirectDispatchEditableSlot? slot = unit.EditableSlots
                .OrderBy(candidate => candidate.PushInstructionOffset)
                .FirstOrDefault();
            if (slot == null)
                return null;

            AiIndirectDispatchWrite? write = unit.PayloadWrites.FirstOrDefault(candidate =>
                                                 candidate.Offset == slot.PushInstructionOffset)
                                             ?? unit.PayloadWrites.FirstOrDefault();
            string roleLabel = string.IsNullOrWhiteSpace(slot.SlotLabel) ? Strings.U_Ai_PrSlotCmd : slot.SlotLabel;
            string variableName = write?.VariableName ?? roleLabel;
            return new AiIndirectDispatchEditorLaunchContext(
                unit.UnitId,
                AiIndirectDispatchEditorFocusKind.CommandSlot,
                slot.RoleKey,
                slot.PushInstructionOffset,
                roleLabel,
                variableName);
        }

        static AiIndirectDispatchEditorLaunchContext? BuildSelectedAdvancedTargetContext(AiIndirectDispatchUnit unit)
        {
            AiIndirectDispatchEditableTargetSlot? slot = unit.EditableTargetSlots
                .Where(candidate => candidate.CanEdit)
                .OrderBy(candidate => candidate.SourceInstructionOffset)
                .FirstOrDefault();
            if (slot == null)
                return null;

            string roleLabel = string.IsNullOrWhiteSpace(slot.SlotLabel) ? Strings.U_Ai_PrSlotTarget : slot.SlotLabel;
            return new AiIndirectDispatchEditorLaunchContext(
                unit.UnitId,
                AiIndirectDispatchEditorFocusKind.TargetSlot,
                slot.RoleKey,
                slot.SourceInstructionOffset,
                roleLabel,
                slot.VariableName);
        }

        static AiIndirectDispatchEditorLaunchContext? BuildSelectedAdvancedNextStateContext(AiIndirectDispatchUnit unit)
        {
            if (!unit.EditableNextStateOffset.HasValue)
                return null;

            return new AiIndirectDispatchEditorLaunchContext(
                unit.UnitId,
                AiIndirectDispatchEditorFocusKind.NextState,
                "next-state",
                unit.EditableNextStateOffset.Value,
                "proximo estado",
                "next-state");
        }

        string BuildPrimaryPayloadHeadline(string unitId)
        {
            if (!advancedPhaseUnitsById.TryGetValue(unitId, out AiIndirectDispatchUnit? unit))
                return Strings.U_Ai_PaNoStrongRead;

            AiIndirectDispatchWrite? write = unit.PayloadWrites
                .Where(candidate => !candidate.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Offset)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(write?.ValueSummary) ? Strings.U_Ai_PaNoStrongRead : write.ValueSummary;
        }

        static string BuildShortNextStateHeadline(string nextStateSummary)
        {
            if (string.IsNullOrWhiteSpace(nextStateSummary))
                return Strings.U_Ai_PaNoReadableNextState;

            const string prefix = "Next state:";
            return nextStateSummary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? nextStateSummary[prefix.Length..].Trim()
                : nextStateSummary.Trim();
        }

        public void SeedPhaseConditionFromAdvancedFocus()
        {
            if (SelectedPhaseVariableAudit == null || SelectedAdvancedVariableFocusRow == null)
            {
                PhaseConditionTechnicalSummary =
                    Strings.F2_select_a_var_and_a_structural_focus_befo_bb05e3df;
                return;
            }

            if (SelectedPhaseConditionClause == null)
                AddPhaseConditionClause();

            AiPhaseConditionClauseVm? clause = SelectedPhaseConditionClause;
            if (clause == null)
            {
                PhaseConditionTechnicalSummary =
                    "I could not open a clause in the condition builder.";
                return;
            }

            clause.SelectedVariable = SelectedPhaseVariableAudit;
            string seededFrom = Strings.U_Ai_PaSelectedVar;
            if (SelectedAdvancedVariableFocusRow.IsGuardRole)
            {
                if (TryParseSimpleGuardPreview(
                    SelectedAdvancedVariableFocusRow.ValueSummary,
                    SelectedPhaseVariableAudit.VariableName,
                    out string parsedOperator,
                    out string parsedValue))
                {
                    clause.Operator = parsedOperator;
                    clause.Value = parsedValue;
                    seededFrom = $"guard atual ({SelectedAdvancedVariableFocusRow.ValueSummary})";
                }
                else
                {
                    clause.Operator = "==";
                    seededFrom = "current guard (var pulled; value/shape still need manual review)";
                }
            }

            SelectedPhaseConditionClause = clause;
            PhaseConditionTechnicalSummary =
                string.Format(Strings.U_Ai_PaBuilderAligned, seededFrom);
            UpdatePhaseConditionPreview();
            SaveActivePhaseDraft();
        }

        public void AddAdvancedCommonPhaseDraft()
        {
            AddPhaseDraftStep();
            SeedSelectedPhaseDraftFromAdvancedFocus(forceOverwrite: true);
            PhaseRotationApplySummary = SelectedAdvancedPhaseUnit == null
                ? Strings.F2_common_phase_added_to_the_draft_in_advan_eeb2bdca
                : string.Format(Strings.U_Ai_PaCommonPhaseAdded, SelectedAdvancedPhaseUnit.Title);
            UpdateAdvancedConditionContextSummary();
        }

        public void AddAdvancedFinalPhaseDraft()
        {
            AddPhaseFinalDraftStep();
            SeedSelectedPhaseDraftFromAdvancedFocus(forceOverwrite: true);
            PhaseRotationApplySummary = SelectedAdvancedPhaseUnit == null
                ? Strings.F2_last_phase_added_to_the_draft_in_advance_8524f166
                : string.Format(Strings.U_Ai_PaLastPhaseAdded, SelectedAdvancedPhaseUnit.Title);
            UpdateAdvancedConditionContextSummary();
        }

        public void RemoveAdvancedSelectedPhaseDraft()
        {
            RemovePhaseDraftStep();
            UpdateAdvancedConditionContextSummary();
        }

        public void AssignPhaseConditionToAdvancedDraft()
        {
            EnsureAdvancedDraftStepForConditionFlow();
            AssignPhaseConditionToSelectedStep();
            UpdateAdvancedConditionContextSummary();
        }

        static bool TryParseSimpleGuardPreview(
            string text,
            string expectedVariableName,
            out string op,
            out string value)
        {
            op = "==";
            value = "0";
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(expectedVariableName))
                return false;

            string trimmed = text.Trim();
            if (!trimmed.StartsWith(expectedVariableName, StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = trimmed[expectedVariableName.Length..].Trim();
            // Match typed operators before their shorter signed prefixes.
            foreach (string candidate in new[] { ">= (u32)", "<= (u32)", "> (u32)", "< (u32)", ">=", "<=", "==", "!=", ">", "<" })
            {
                if (!rest.StartsWith(candidate, StringComparison.Ordinal))
                    continue;

                string parsedValue = rest[candidate.Length..].Trim();
                if (string.IsNullOrWhiteSpace(parsedValue) || parsedValue.Contains(' ') || parsedValue.Contains('\t'))
                    return false;

                op = candidate;
                value = parsedValue;
                return true;
            }

            return false;
        }

        void EnsureAdvancedDraftStepForConditionFlow()
        {
            if (SelectedPhaseDraftStep == null)
            {
                AddPhaseDraftStep();
                SeedSelectedPhaseDraftFromAdvancedFocus(forceOverwrite: true);
                PhaseRotationApplySummary = SelectedAdvancedPhaseUnit == null
                    ? Strings.F2_common_phase_created_automatically_in_ad_d952cf67
                    : string.Format(Strings.U_Ai_PaCommonPhaseAuto, SelectedAdvancedPhaseUnit.Title);
                return;
            }

            SeedSelectedPhaseDraftFromAdvancedFocus(forceOverwrite: false);
        }

        void SeedSelectedPhaseDraftFromAdvancedFocus(bool forceOverwrite)
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null
                || SelectedAdvancedPhaseUnit == null
                || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out AiIndirectDispatchUnit? unit))
            {
                return;
            }

            if (forceOverwrite || step.SelectedTriggerOption == null)
            {
                step.SelectedTriggerOption = ResolvePhaseTriggerOption(
                    unit.HookKind.Contains("onHit", StringComparison.OrdinalIgnoreCase)
                        ? AiPhaseTriggerKind.OnHit
                        : AiPhaseTriggerKind.OnTurn);
            }

            if ((forceOverwrite || step.SelectedAbility == null)
                && unit.EditableSlots
                    .OrderBy(slot => slot.PushInstructionOffset < 0 ? int.MaxValue : slot.PushInstructionOffset)
                    .FirstOrDefault() is { } commandSlot)
            {
                step.SelectedAbility = ResolvePhaseAbilityOption(commandSlot.CurrentValue)
                    ?? step.SelectedAbility
                    ?? DefaultPhaseAbilityOption();
            }

            if ((forceOverwrite || step.SelectedTarget == null) && ResolveAdvancedDraftTargetOption(unit) is { } target)
                step.SelectedTarget = target;
        }

        AiTargetOption? ResolveAdvancedDraftTargetOption(AiIndirectDispatchUnit unit)
        {
            foreach (AiIndirectDispatchEditableTargetSlot slot in unit.EditableTargetSlots
                         .OrderBy(candidate => candidate.SourceInstructionOffset < 0 ? int.MaxValue : candidate.SourceInstructionOffset))
            {
                AiTargetOption? option = TryMapAdvancedDraftTargetOption(slot);
                if (option != null)
                    return option;
            }

            return null;
        }

        AiTargetOption? TryMapAdvancedDraftTargetOption(AiIndirectDispatchEditableTargetSlot slot)
        {
            if (slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue
                || slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Unknown)
            {
                return null;
            }

            AiTargetRecipeKind recipeKind = slot.RecipeKind ?? AiTargetRecipeKind.Literal;
            ushort operand = recipeKind == AiTargetRecipeKind.Literal ? slot.CurrentValue : (ushort)0;
            return AuthoringTargetOptions.FirstOrDefault(option =>
                !option.UseLinkedActionTarget
                && option.TargetRecipeKind == recipeKind
                && option.Operand == operand);
        }

        void SyncAdvancedVariableFocusSelectionForUnit(AiAdvancedPhaseUnitVm? unit)
        {
            if (unit == null || AdvancedVariableFocusRows.Count == 0)
                return;

            AiAdvancedVariableFocusRowVm? row = AdvancedVariableFocusRows.FirstOrDefault(candidate =>
                candidate.UnitId.Equals(unit.UnitId, StringComparison.OrdinalIgnoreCase));
            if (row != null && !ReferenceEquals(row, SelectedAdvancedVariableFocusRow))
                SetSelectedAdvancedVariableFocusRow(row);
        }

        void SetSelectedAdvancedVariableFocusRow(AiAdvancedVariableFocusRowVm? row)
        {
            syncingAdvancedVariableFocusSelection = true;
            SelectedAdvancedVariableFocusRow = row;
            syncingAdvancedVariableFocusSelection = false;
        }

        AiAdvancedPhaseUnitVm BuildAdvancedPhaseUnitVm(
            AiIndirectDispatchUnit unit,
            AiPhaseVariableAuditRow? selectedVariable,
            ushort? selectedRuntimeIndex)
        {
            IReadOnlyList<AiIndirectDispatchWrite> commandWrites = unit.PayloadWrites
                .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string commandSlotsSummary = commandWrites.Count == 0
                ? "No readable command slot."
                : string.Join(Environment.NewLine, commandWrites.Select(write =>
                    $"{write.RoleSummary}: {write.VariableName} <- {write.ValueSummary} @ 0x{write.Offset:X4}"));

            string consumerSummary = unit.Consumers.Count == 0
                ? "No readable consumer."
                : string.Join(Environment.NewLine, unit.Consumers.Select(consumer =>
                    $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName} -> 0x{consumer.CallOffset:X4}"));

            string notesSummary = string.Join(
                Environment.NewLine,
                unit.CompanionEffects
                    .Prepend(unit.WarningSummary)
                    .Where(note => !string.IsNullOrWhiteSpace(note))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

            string selectedVariableRoleSummary = BuildAdvancedUnitRelationSummary(unit, selectedVariable, selectedRuntimeIndex);
            bool linkedToSelectedVariable = selectedVariable != null
                && selectedRuntimeIndex.HasValue
                && IndirectDispatchUnitMentionsPhaseVariable(unit, selectedVariable, selectedRuntimeIndex.Value);
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface =
                AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers);

            string targetSlotsSummary = targetSlotSurface.Count == 0
                ? "No readable target slot."
                : string.Join(Environment.NewLine, targetSlotSurface.Select(slot =>
                    $"{slot.SlotLabel}: {slot.VariableName} <- {slot.ValueSummary}"));

            bool canOpenEditor = unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                && (unit.EditableSlots.Count > 0
                    || unit.EditableTargetSlots.Any(slot => slot.CanEdit)
                    || unit.EditableNextStateOffset.HasValue);
            bool canCloneRoute = canOpenEditor
                && unit.EditableNextStateOffset.HasValue
                && unit.CurrentNextStateValue.HasValue;
            List<AiAdvancedPhaseDetailRowVm> commandSlots = BuildAdvancedCommandSlotRows(commandWrites, canOpenEditor);
            List<AiAdvancedPhaseDetailRowVm> targetSlots = BuildAdvancedTargetSlotRows(targetSlotSurface);
            List<AiAdvancedPhaseDetailRowVm> consumerRows = BuildAdvancedConsumerRows(unit.Consumers, canOpenEditor);
            bool isRecentClone =
                !string.IsNullOrWhiteSpace(lastAdvancedCloneNewUnitId)
                && unit.UnitId.Equals(lastAdvancedCloneNewUnitId, StringComparison.OrdinalIgnoreCase);
            bool isCloneSource =
                !string.IsNullOrWhiteSpace(lastAdvancedCloneSourceUnitId)
                && unit.UnitId.Equals(lastAdvancedCloneSourceUnitId, StringComparison.OrdinalIgnoreCase);
            string cloneBadgeText = isRecentClone
                ? "novo clone"
                : isCloneSource
                    ? "rota-mestre"
                    : string.Empty;
            string cloneSummary = isRecentClone
                ? lastAdvancedCloneSourceUnitIndex.HasValue
                    ? string.Format(Strings.U_Ai_PaBornFromRoute, lastAdvancedCloneSourceUnitIndex.Value)
                    : Strings.U_Ai_PaBornByClone
                : isCloneSource
                    ? lastAdvancedCloneNewUnitIndex.HasValue
                        ? string.Format(Strings.U_Ai_PaServedAsBase, lastAdvancedCloneNewUnitIndex.Value)
                        : "Serviu de base para um clone recente nesta sessao."
                    : string.Empty;
            string familyLabel = DescribeAdvancedFamilyLabel(unit, canOpenEditor);
            string familySummary = BuildAdvancedFamilySummary(unit, familyLabel, canOpenEditor);
            string editabilitySummary = BuildAdvancedEditabilitySummary(unit, familyLabel, canOpenEditor);
            string conditionEditingSummary = BuildAdvancedConditionEditingSummary(unit, familyLabel, canOpenEditor);
            string thresholdSummary = BuildAdvancedThresholdSummary(unit, familyLabel);

            return new AiAdvancedPhaseUnitVm(
                unit.UnitId,
                unit.UnitIndex,
                canOpenEditor ? $"rota {unit.UnitIndex}" : $"pacote {unit.UnitIndex}",
                unit.GuardSummary,
                commandSlotsSummary,
                targetSlotsSummary,
                consumerSummary,
                unit.NextStateSummary,
                notesSummary,
                unit.CapabilityLabel,
                unit.OffsetSummary,
                selectedVariableRoleSummary,
                familyLabel,
                familySummary,
                editabilitySummary,
                conditionEditingSummary,
                thresholdSummary,
                commandSlots,
                targetSlots,
                consumerRows,
                linkedToSelectedVariable,
                canOpenEditor,
                canCloneRoute,
                cloneBadgeText,
                cloneSummary);
        }

        static string DescribeAdvancedFamilyLabel(AiIndirectDispatchUnit unit, bool canOpenEditor)
        {
            if (unit.CapabilityLabel.Contains("preview complexa elemental", StringComparison.OrdinalIgnoreCase))
                return "elemental cluster";
            if (unit.CapabilityLabel.Contains("preview acoplada host/companheiro", StringComparison.OrdinalIgnoreCase))
                return "host/companion pair";
            if (unit.CapabilityLabel.Contains("preview companheiro acoplado", StringComparison.OrdinalIgnoreCase))
                return "companion bundle";
            if (unit.CapabilityLabel.Contains("preview suporte por acumulador", StringComparison.OrdinalIgnoreCase))
                return "support accumulator";
            if (unit.CapabilityLabel.Contains("preview sensor reativo", StringComparison.OrdinalIgnoreCase))
                return "reactive sensor";
            if (unit.CapabilityLabel.Contains("preview tonberry camera routing", StringComparison.OrdinalIgnoreCase))
                return "tonberry camera routing boss";
            if (unit.CapabilityLabel.Contains("preview encounter-keyed appear-disable", StringComparison.OrdinalIgnoreCase))
                return "encounter-keyed appear-disable";
            if (unit.UnitId.Contains("summon", StringComparison.OrdinalIgnoreCase)
                || unit.UnitId.Contains("appear", StringComparison.OrdinalIgnoreCase)
                || unit.CompanionEffects.Any(note =>
                    note.Contains("btlSetAppear", StringComparison.OrdinalIgnoreCase)
                    || note.Contains("Summon", StringComparison.OrdinalIgnoreCase)))
            {
                return "summon-handoff/appear";
            }

            if (unit.CapabilityLabel.Contains("candidato estrutural", StringComparison.OrdinalIgnoreCase)
                && unit.Consumers.Count <= 2
                && unit.EditableSlots.Count <= 2)
            {
                return "support indirect payload picker";
            }

            if (unit.CapabilityLabel.Contains("candidato", StringComparison.OrdinalIgnoreCase))
                return "generic switch dispatch";

            return canOpenEditor ? "indirect route family" : "complex family preview";
        }

        static string BuildAdvancedFamilySummary(
            AiIndirectDispatchUnit unit,
            string familyLabel,
            bool canOpenEditor) => familyLabel switch
        {
            "elemental cluster" =>
                "Element cluster guided by discs/attributes. The package alternates barrage, Dispel break, and Ultima break without becoming a fake route table.",
            "host/companion pair" =>
                "Host and companion share state, follow-up, and aftermath. V2 shows the real beats of the pair without flattening everything into a single simple phase.",
            "companion bundle" =>
                "Contextual companion package: body/scene handoff and native follow-ups live together, but still outside a universal writer.",
            "support accumulator" =>
                "Family that first scores/accumulates state and only then chooses the support/offense package. The reader must respect this extra layer.",
            "reactive sensor" =>
                Strings.U_Ai_PaReactiveSensor,
            "tonberry camera routing boss" =>
                Strings.U_Ai_PaPressurePackage,
            "encounter-keyed appear-disable" =>
                "Contextual reveal/handoff package: a reactive trigger opens the host, selects the actor per encounter, and swaps presence/CTB flags without becoming generic switch authoring.",
            "support indirect payload picker" =>
                "Light indirect selector: picks a payload in one slot, calculates a target in another, and consumes both in a contextual performCommand. It is smaller than the heavy Seymour panel.",
            "summon-handoff/appear" =>
                Strings.U_Ai_PaAppearancePackage,
            "generic switch dispatch" =>
                "A var or switch selects the structural route, writes payload/next-state, and lets indirect consumers consume the slots afterward.",
            _ => canOpenEditor
                ? "Indirect family with sufficient authored offsets for strong reading and some guided row-only."
                : "Structurally detected complex family, but still without a strong shape for native writer."
        };

        static string BuildAdvancedEditabilitySummary(
            AiIndirectDispatchUnit unit,
            string familyLabel,
            bool canOpenEditor)
        {
            if (unit.UnitId.Contains("preview-flux-self-buff", StringComparison.OrdinalIgnoreCase)
                || unit.GuardSummary.Contains("75%/50%", StringComparison.OrdinalIgnoreCase))
            {
                return "The Flux family remains preview on the structural map, but the card 'Raw Flux evidence' now releases narrow writer for the raw fraction in priv0018/priv001C.";
            }

            if (!canOpenEditor)
            {
                return "Today this remains PreviewReadOnly. The screen explains guard, payload, target, consumers, and aftermath, but no native write for this family is released yet.";
            }

            if (familyLabel.Equals("support indirect payload picker", StringComparison.OrdinalIgnoreCase))
            {
                return "Current safe edit: row-only for payload and target at proven offsets. Good for switching the local crop without pretending the entire family became a giant Seymour.";
            }

            return "Current safe edit: row-only at proven slots and, when the family has real routes, guided structural clone. HP/chance/stop/Forbidden Rite continue coming from the embedded common draft below.";
        }

        static string BuildAdvancedConditionEditingSummary(
            AiIndirectDispatchUnit unit,
            string familyLabel,
            bool canOpenEditor)
        {
            if (unit.UnitId.Contains("preview-flux-self-buff", StringComparison.OrdinalIgnoreCase)
                || unit.GuardSummary.Contains("75%/50%", StringComparison.OrdinalIgnoreCase))
            {
                return "Flux native thresholds now have their own narrow writer right below: you edit the raw fraction of the Protect/Reflect gate at priv0018/priv001C. The common draft still applies for extra HP guard but does not replace this native patch.";
            }

            if (familyLabel.Equals("elemental cluster", StringComparison.OrdinalIgnoreCase))
            {
                return "Omnis mixes counter/HP thresholds with elemental alignment. V2 dissects this, but native condition editing has not yet left preview.";
            }

            if (!canOpenEditor)
            {
                return Strings.U_Ai_PaNativeConditionsPreview;
            }

            return "The native structural guard of this family is not yet an arbitrary writer. Today you edit the conditions of the materialized phase in the common draft; the structural reader continues explaining the real guard alongside.";
        }

        static string BuildAdvancedThresholdSummary(AiIndirectDispatchUnit unit, string familyLabel)
        {
            if (unit.UnitId.Contains("preview-flux-self-buff", StringComparison.OrdinalIgnoreCase)
                || unit.GuardSummary.Contains("75%/50%", StringComparison.OrdinalIgnoreCase))
            {
                return "Native thresholds detected in the Flux package. V2 shows the raw IR from priv0018/priv001C and now releases family-specific raw patch for the proven fractions.";
            }

            if (familyLabel.Equals("elemental cluster", StringComparison.OrdinalIgnoreCase)
                && (unit.GuardSummary.Contains("threshold", StringComparison.OrdinalIgnoreCase)
                    || unit.NextStateSummary.Contains("Ultima", StringComparison.OrdinalIgnoreCase)
                    || unit.PayloadWrites.Any(write => write.RoleSummary.Contains("contador de ataques", StringComparison.OrdinalIgnoreCase))))
            {
                return "Thresholds nativos detectados: contador de ataques e/ou janela de HP antes da quebra elemental.";
            }

            return string.Empty;
        }

        static List<AiAdvancedPhaseDetailRowVm> BuildAdvancedCommandSlotRows(
            IReadOnlyList<AiIndirectDispatchWrite> commandWrites,
            bool canOpenEditor)
        {
            List<AiAdvancedPhaseDetailRowVm> rows = commandWrites
                .Select(write => new AiAdvancedPhaseDetailRowVm(
                    write.RoleSummary,
                    write.VariableName,
                    write.ValueSummary,
                    canOpenEditor
                        ? Strings.U_Ai_PrPayloadOfRoute
                        : Strings.U_Ai_PaObservedPackagePayload,
                    $"0x{write.Offset:X4}"))
                .ToList();

            if (rows.Count == 0)
            {
                rows.Add(new AiAdvancedPhaseDetailRowVm(
                    Strings.U_Ai_PaNoCommandSlot,
                    "-",
                    Strings.U_Ai_PrNoCommandPayload,
                    string.Empty,
                    "-"));
            }

            return rows;
        }

        static List<AiAdvancedPhaseDetailRowVm> BuildAdvancedTargetSlotRows(
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots)
        {
            List<AiAdvancedPhaseDetailRowVm> rows = targetSlots
                .Select(slot =>
                {
                    return new AiAdvancedPhaseDetailRowVm(
                        slot.SlotLabel,
                        slot.VariableName,
                        slot.ValueSummary,
                        slot.DetailSummary,
                        slot.SourceInstructionOffset >= 0 ? $"0x{slot.SourceInstructionOffset:X4}" : Strings.U_Ai_PaUnresolvedOffset);
                })
                .ToList();

            if (rows.Count == 0)
            {
                rows.Add(new AiAdvancedPhaseDetailRowVm(
                    Strings.U_Ai_PaNoTargetSlot,
                    "-",
                    Strings.U_Ai_PrNoTargetSlot,
                    string.Empty,
                    "-"));
            }

            return rows;
        }

        static List<AiAdvancedPhaseDetailRowVm> BuildAdvancedConsumerRows(
            IReadOnlyList<AiIndirectDispatchConsumer> consumers,
            bool canOpenEditor)
        {
            List<AiAdvancedPhaseDetailRowVm> rows = consumers
                .Select(consumer => new AiAdvancedPhaseDetailRowVm(
                    consumer.Label,
                    $"{consumer.CommandVariableName} + {consumer.TargetVariableName}",
                    canOpenEditor ? Strings.U_Ai_RouteIndirectPerform : "consumer observado",
                    canOpenEditor
                        ? string.Format(Strings.U_Ai_PrUsesSlots, consumer.CommandVariableName, consumer.TargetVariableName)
                        : string.Format(Strings.U_Ai_PrConsumesInObservedPackage, consumer.Label, consumer.CommandVariableName, consumer.TargetVariableName),
                    consumer.CallOffset >= 0 ? $"0x{consumer.CallOffset:X4}" : Strings.U_Ai_PaUnresolvedOffset))
                .ToList();

            if (rows.Count == 0)
            {
                rows.Add(new AiAdvancedPhaseDetailRowVm(
                    Strings.U_Ai_PaNoConsumer,
                    "-",
                    "Nenhum performCommand indireto ficou materializado.",
                    string.Empty,
                    "-"));
            }

            return rows;
        }

        static string DescribeAdvancedTargetSlotRole(string variableName, IReadOnlyList<string> labels)
        {
            if (variableName.Equals("priv0014", StringComparison.OrdinalIgnoreCase))
                return Strings.U_Ai_PrSlotTargetUnit;
            if (variableName.Equals("priv0018", StringComparison.OrdinalIgnoreCase))
                return Strings.U_Ai_PrSlotTargetMulti1;
            if (variableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase))
                return Strings.U_Ai_PrSlotTargetMulti2;

            if (labels.Any(label => label.Contains("aeon", StringComparison.OrdinalIgnoreCase)))
                return Strings.U_Ai_PrSlotTargetContextual;

            return Strings.U_Ai_PrSlotTargetIndirect;
        }

        static string DescribeTargetSourceKind(AiIndirectDispatchTargetSlotSourceKind kind) => kind switch
        {
            AiIndirectDispatchTargetSlotSourceKind.Literal => "literal",
            AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => Strings.U_Ai_PrRecipeComputed,
            AiIndirectDispatchTargetSlotSourceKind.CopiedValue => Strings.U_Ai_PrCopyOfVar,
            _ => Strings.U_Ai_PaUnknownSource,
        };

        static string DescribeAdvancedUnitScope(AiAdvancedPhaseUnitVm unit) =>
            unit.CanOpenEditor ? $"rota {unit.UnitIndex}" : $"pacote {unit.UnitIndex}";

        static string DescribeAdvancedFocusScope(AiAdvancedVariableFocusRowVm row) =>
            row.CanOpenEditor ? $"rota {row.UnitIndex}" : $"pacote {row.UnitIndex}";

        static IReadOnlyList<AiAdvancedVariableFocusRowVm> BuildAdvancedVariableFocusRows(
            AiIndirectDispatchUnit unit,
            AiPhaseVariableAuditRow selectedVariable,
            ushort runtimeIndex)
        {
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface =
                AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers);
            List<AiAdvancedVariableFocusRowVm> rows = new();

            if (TextMentionsPhaseVariable(unit.GuardSummary, selectedVariable, runtimeIndex))
            {
                AiIndirectDispatchEditorLaunchContext? guardLaunchContext =
                    unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                        ? BuildAdvancedEditorLaunchContext(
                            unit,
                            selectedVariable.VariableName,
                            AiIndirectDispatchEditorFocusKind.Guard,
                            Strings.U_Ai_PaPhaseGuard,
                            roleKey: "guard",
                            instructionOffset: -1)
                        : null;
                rows.Add(new AiAdvancedVariableFocusRowVm(
                    $"{unit.UnitId}|guard",
                    unit.UnitId,
                    unit.UnitIndex,
                    Strings.U_Ai_PaPhaseGuard,
                    unit.GuardSummary,
                    BuildGuardFocusDependencySummary(unit, targetSlotSurface),
                    "guard estrutural",
                    guardLaunchContext != null,
                    guardLaunchContext));
            }

            foreach (AiIndirectDispatchWrite write in unit.PayloadWrites
                         .Where(write => write.VariableIndex == runtimeIndex)
                         .OrderBy(write => write.Offset))
            {
                if (write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                    continue;

                AiIndirectDispatchEditableSlot? editableSlot = unit.EditableSlots.FirstOrDefault(slot =>
                    slot.PushInstructionOffset == write.Offset);
                AiIndirectDispatchEditorLaunchContext? payloadLaunchContext =
                    editableSlot != null
                        ? BuildAdvancedEditorLaunchContext(
                            unit,
                            selectedVariable.VariableName,
                            AiIndirectDispatchEditorFocusKind.CommandSlot,
                            write.RoleSummary,
                            editableSlot.RoleKey,
                            editableSlot.PushInstructionOffset)
                        : null;
                rows.Add(new AiAdvancedVariableFocusRowVm(
                    $"{unit.UnitId}|payload|{write.Offset:X4}",
                    unit.UnitId,
                    unit.UnitIndex,
                    write.RoleSummary,
                    write.ValueSummary,
                    BuildPayloadFocusDependencySummary(unit, targetSlotSurface, write.VariableIndex),
                    $"0x{write.Offset:X4}",
                    payloadLaunchContext != null,
                    payloadLaunchContext));
            }

            foreach (AiIndirectDispatchTargetSlotSurface slot in targetSlotSurface
                         .Where(slot => slot.VariableIndex == runtimeIndex)
                         .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset))
            {
                AiIndirectDispatchEditableTargetSlot? editableTargetSlot = unit.EditableTargetSlots.FirstOrDefault(candidate =>
                    candidate.VariableIndex == slot.VariableIndex
                    && candidate.SourceInstructionOffset == slot.SourceInstructionOffset);
                AiIndirectDispatchEditorLaunchContext? targetLaunchContext =
                    editableTargetSlot?.CanEdit == true
                        ? BuildAdvancedEditorLaunchContext(
                            unit,
                            selectedVariable.VariableName,
                            AiIndirectDispatchEditorFocusKind.TargetSlot,
                            slot.SlotLabel,
                            editableTargetSlot.RoleKey,
                            editableTargetSlot.SourceInstructionOffset)
                        : null;
                rows.Add(new AiAdvancedVariableFocusRowVm(
                    $"{unit.UnitId}|target|{slot.SourceInstructionOffset:X4}|{slot.VariableIndex:X4}",
                    unit.UnitId,
                    unit.UnitIndex,
                    slot.SlotLabel,
                    slot.ValueSummary,
                    BuildTargetFocusDependencySummary(unit, targetSlotSurface, slot),
                    slot.SourceInstructionOffset >= 0 ? $"0x{slot.SourceInstructionOffset:X4}" : Strings.U_Ai_PaUnresolvedOffset,
                    targetLaunchContext != null,
                    targetLaunchContext));
            }

            if (TextMentionsPhaseVariable(unit.NextStateSummary, selectedVariable, runtimeIndex))
            {
                AiIndirectDispatchEditorLaunchContext? nextStateLaunchContext =
                    unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                    && unit.EditableNextStateOffset.HasValue
                    && unit.CurrentNextStateValue.HasValue
                        ? BuildAdvancedEditorLaunchContext(
                            unit,
                            selectedVariable.VariableName,
                            AiIndirectDispatchEditorFocusKind.NextState,
                            "proximo estado",
                            roleKey: "next-state",
                            instructionOffset: unit.EditableNextStateOffset.Value)
                        : null;
                rows.Add(new AiAdvancedVariableFocusRowVm(
                    $"{unit.UnitId}|next-state",
                    unit.UnitId,
                    unit.UnitIndex,
                    "proximo estado",
                    unit.NextStateSummary,
                    BuildNextStateFocusDependencySummary(unit, targetSlotSurface),
                    "next-state estrutural",
                    nextStateLaunchContext != null,
                    nextStateLaunchContext));
            }

            return rows
                .OrderBy(row => row.UnitIndex)
                .ThenBy(row => row.RoleSortKey)
                .ThenBy(row => row.OffsetSortKey)
                .ToList();
        }

        static string BuildGuardFocusDependencySummary(
            AiIndirectDispatchUnit unit,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface)
        {
            var parts = new List<string>();
            AppendFocusSummary(parts, "Payload da rota", BuildPayloadFocusSummary(unit));
            AppendFocusSummary(parts, "Alvos da rota", BuildTargetFocusSummary(targetSlotSurface));
            AppendFocusSummary(parts, "Consumers", BuildConsumerFocusSummary(unit.Consumers));
            AppendFocusSummary(parts, "Next state", unit.NextStateSummary);
            AppendFocusSummary(parts, "Notas", unit.WarningSummary);
            return string.Join(" ", parts);
        }

        static string BuildPayloadFocusDependencySummary(
            AiIndirectDispatchUnit unit,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface,
            ushort payloadVariableIndex)
        {
            var parts = new List<string>();
            AppendFocusSummary(parts, "Guard", unit.GuardSummary);
            AppendFocusSummary(
                parts,
                "Consumers",
                BuildConsumerFocusSummary(unit.Consumers.Where(consumer => consumer.CommandVariableIndex == payloadVariableIndex)));
            AppendFocusSummary(parts, "Alvos da rota", BuildTargetFocusSummary(targetSlotSurface));
            AppendFocusSummary(parts, "Next state", unit.NextStateSummary);
            AppendFocusSummary(parts, "Notas", unit.WarningSummary);
            return string.Join(" ", parts);
        }

        static string BuildTargetFocusDependencySummary(
            AiIndirectDispatchUnit unit,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface,
            AiIndirectDispatchTargetSlotSurface slot)
        {
            var parts = new List<string>();
            AppendFocusSummary(parts, "Shape do slot", slot.DetailSummary);
            AppendFocusSummary(parts, "Guard", unit.GuardSummary);
            AppendFocusSummary(parts, "Payload da rota", BuildPayloadFocusSummary(unit));
            AppendFocusSummary(
                parts,
                "Consumers",
                BuildConsumerFocusSummary(unit.Consumers.Where(consumer => consumer.TargetVariableIndex == slot.VariableIndex)));
            AppendFocusSummary(parts, "Next state", unit.NextStateSummary);
            AppendFocusSummary(parts, "Notas", unit.WarningSummary);
            return string.Join(" ", parts);
        }

        static string BuildNextStateFocusDependencySummary(
            AiIndirectDispatchUnit unit,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface)
        {
            var parts = new List<string>();
            AppendFocusSummary(parts, "Guard", unit.GuardSummary);
            AppendFocusSummary(parts, "Payload da rota", BuildPayloadFocusSummary(unit));
            AppendFocusSummary(parts, "Alvos da rota", BuildTargetFocusSummary(targetSlotSurface));
            AppendFocusSummary(parts, "Consumers", BuildConsumerFocusSummary(unit.Consumers));
            AppendFocusSummary(parts, "Notas", unit.WarningSummary);
            return string.Join(" ", parts);
        }

        static string BuildPayloadFocusSummary(AiIndirectDispatchUnit unit) =>
            string.Join(
                " | ",
                unit.PayloadWrites
                    .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(write => write.Offset)
                    .Select(write => $"{write.RoleSummary}={write.ValueSummary}"));

        static string BuildTargetFocusSummary(IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlotSurface) =>
            string.Join(
                " | ",
                targetSlotSurface
                    .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                    .Select(slot => $"{slot.SlotLabel}={slot.ValueSummary}"));

        static string BuildConsumerFocusSummary(IEnumerable<AiIndirectDispatchConsumer> consumers) =>
            string.Join(
                " / ",
                consumers
                    .OrderBy(consumer => consumer.CallOffset < 0 ? int.MaxValue : consumer.CallOffset)
                    .Select(consumer => $"{consumer.Label} ({consumer.CommandVariableName} + {consumer.TargetVariableName})"));

        static void AppendFocusSummary(List<string> parts, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            parts.Add($"{label}: {value}.");
        }

        static AiIndirectDispatchEditorLaunchContext BuildAdvancedEditorLaunchContext(
            AiIndirectDispatchUnit unit,
            string variableName,
            AiIndirectDispatchEditorFocusKind focusKind,
            string roleLabel,
            string? roleKey,
            int instructionOffset) =>
            new AiIndirectDispatchEditorLaunchContext(
                unit.UnitId,
                focusKind,
                roleKey ?? string.Empty,
                instructionOffset,
                roleLabel,
                variableName);

        string BuildAdvancedUnitRelationSummary(
            AiIndirectDispatchUnit unit,
            AiPhaseVariableAuditRow? selectedVariable,
            ushort? selectedRuntimeIndex)
        {
            if (selectedVariable == null || !selectedRuntimeIndex.HasValue)
                return Strings.U_Ai_PaNoVarSelected;

            ushort runtimeIndex = selectedRuntimeIndex.Value;
            var roles = new List<string>();

            if (TextMentionsPhaseVariable(unit.GuardSummary, selectedVariable, runtimeIndex))
                roles.Add(Strings.U_Ai_PaPhaseGuard);

            foreach (AiIndirectDispatchWrite write in unit.PayloadWrites.Where(write => write.VariableIndex == runtimeIndex))
                roles.Add(write.RoleSummary);

            foreach (AiIndirectDispatchConsumer consumer in unit.Consumers.Where(consumer => consumer.CommandVariableIndex == runtimeIndex))
                roles.Add($"slot cmd consumido por {consumer.Label}");

            foreach (AiIndirectDispatchEditableTargetSlot targetSlot in unit.EditableTargetSlots
                         .Where(slot => slot.VariableIndex == runtimeIndex))
            {
                List<string> labels = unit.Consumers
                    .Where(consumer => consumer.TargetVariableIndex == targetSlot.VariableIndex)
                    .Select(consumer => consumer.Label)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string role = string.IsNullOrWhiteSpace(targetSlot.SlotLabel)
                    ? DescribeAdvancedTargetSlotRole(targetSlot.VariableName, labels)
                    : targetSlot.SlotLabel;
                roles.Add(role);
            }

            if (TextMentionsPhaseVariable(unit.NextStateSummary, selectedVariable, runtimeIndex))
                roles.Add(Strings.U_Ai_PrNextState);

            if (roles.Count == 0)
                return string.Format(Strings.U_Ai_PrVarNotInFamily, selectedVariable.VariableName);

            return string.Format(Strings.U_Ai_PrParticipatesAs, selectedVariable.VariableName,
                string.Join(" · ", roles.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
    }

    internal sealed class AiAdvancedPhaseUnitVm
    {
        public AiAdvancedPhaseUnitVm(
            string unitId,
            int unitIndex,
            string title,
            string guardSummary,
            string commandSlotsSummary,
            string targetSlotsSummary,
            string consumerSummary,
            string nextStateSummary,
            string notesSummary,
            string capabilityLabel,
            string offsetSummary,
            string selectedVariableRoleSummary,
            string familyLabel,
            string familySummary,
            string editabilitySummary,
            string conditionEditingSummary,
            string thresholdSummary,
            IReadOnlyList<AiAdvancedPhaseDetailRowVm> commandSlots,
            IReadOnlyList<AiAdvancedPhaseDetailRowVm> targetSlots,
            IReadOnlyList<AiAdvancedPhaseDetailRowVm> consumerRows,
            bool isLinkedToSelectedVariable,
            bool canOpenEditor,
            bool canCloneRoute,
            string cloneBadgeText,
            string cloneSummary)
        {
            UnitId = unitId;
            UnitIndex = unitIndex;
            Title = title;
            GuardSummary = guardSummary;
            CommandSlotsSummary = commandSlotsSummary;
            TargetSlotsSummary = targetSlotsSummary;
            ConsumerSummary = consumerSummary;
            NextStateSummary = nextStateSummary;
            NotesSummary = notesSummary;
            CapabilityLabel = capabilityLabel;
            OffsetSummary = offsetSummary;
            SelectedVariableRoleSummary = selectedVariableRoleSummary;
            FamilyLabel = familyLabel;
            FamilySummary = familySummary;
            EditabilitySummary = editabilitySummary;
            ConditionEditingSummary = conditionEditingSummary;
            ThresholdSummary = thresholdSummary;
            CommandSlots = commandSlots;
            TargetSlots = targetSlots;
            ConsumerRows = consumerRows;
            IsLinkedToSelectedVariable = isLinkedToSelectedVariable;
            CanOpenEditor = canOpenEditor;
            CanCloneRoute = canCloneRoute;
            CloneBadgeText = cloneBadgeText;
            CloneSummary = cloneSummary;
        }

        public string ActionSummary => string.Join(" · ", CommandSlots.Select(row => row.ValueSummary).Distinct());
        public string FriendlyTargetSummary => string.Join(" · ", TargetSlots.Select(row => row.ValueSummary).Distinct());
        public string DisplayTitle => string.Format(Strings.AiAdvancedRouteNumber, UnitIndex + 1);
        public string AuthoringStatus => CanOpenEditor ? Strings.AiAdvancedEditableFields : Strings.AiAdvancedReadOnlyFields;
        public string UnitId { get; }
        public int UnitIndex { get; }
        public string Title { get; }
        public string GuardSummary { get; }
        public string CommandSlotsSummary { get; }
        public string TargetSlotsSummary { get; }
        public string ConsumerSummary { get; }
        public string NextStateSummary { get; }
        public string NotesSummary { get; }
        public string CapabilityLabel { get; }
        public string OffsetSummary { get; }
        public string SelectedVariableRoleSummary { get; }
        public string FamilyLabel { get; }
        public string FamilySummary { get; }
        public string EditabilitySummary { get; }
        public string ConditionEditingSummary { get; }
        public string ThresholdSummary { get; }
        public IReadOnlyList<AiAdvancedPhaseDetailRowVm> CommandSlots { get; }
        public IReadOnlyList<AiAdvancedPhaseDetailRowVm> TargetSlots { get; }
        public IReadOnlyList<AiAdvancedPhaseDetailRowVm> ConsumerRows { get; }
        public bool IsLinkedToSelectedVariable { get; }
        public bool CanOpenEditor { get; }
        public bool CanCloneRoute { get; }
        public string CloneBadgeText { get; }
        public string CloneSummary { get; }
        public bool HasCloneBadge => !string.IsNullOrWhiteSpace(CloneBadgeText);
        public bool HasCloneSummary => !string.IsNullOrWhiteSpace(CloneSummary);
        public bool HasThresholdSummary => !string.IsNullOrWhiteSpace(ThresholdSummary);
    }

    [ObservableObject]
    internal sealed partial class AiAdvancedPhaseDetailRowVm
    {
        public AiAdvancedPhaseDetailRowVm(
            string label,
            string variableName,
            string valueSummary,
            string detailSummary,
            string offsetSummary)
        {
            Label = label;
            VariableName = variableName;
            ValueSummary = valueSummary;
            DetailSummary = detailSummary;
            OffsetSummary = offsetSummary;
        }

        [ObservableProperty] private bool isFocusTarget;

        public string Label { get; }
        public string VariableName { get; }
        public string ValueSummary { get; }
        public string DetailSummary { get; }
        public string OffsetSummary { get; }
        public bool HasDetailSummary => !string.IsNullOrWhiteSpace(DetailSummary);
    }

    readonly record struct AiAdvancedOpeningVariableCandidate(
        AiPhaseVariableAuditRow Audit,
        int GuardRouteCount,
        int LinkedRouteCount,
        int FocusRowCount)
    {
        public bool HasStructuralPresence => FocusRowCount > 0;
    }

    internal sealed class AiAdvancedVariableFocusRowVm
    {
        readonly AiIndirectDispatchEditorLaunchContext? editorLaunchContext;

        public AiAdvancedVariableFocusRowVm(
            string stableKey,
            string unitId,
            int unitIndex,
            string roleLabel,
            string valueSummary,
            string dependencySummary,
            string offsetSummary,
            bool canOpenEditor,
            AiIndirectDispatchEditorLaunchContext? editorLaunchContext)
        {
            StableKey = stableKey;
            UnitId = unitId;
            UnitIndex = unitIndex;
            RoleLabel = roleLabel;
            ValueSummary = valueSummary;
            DependencySummary = dependencySummary;
            OffsetSummary = offsetSummary;
            CanOpenEditor = canOpenEditor && editorLaunchContext != null;
            this.editorLaunchContext = editorLaunchContext;
        }

        public string StableKey { get; }
        public string UnitId { get; }
        public int UnitIndex { get; }
        public string RoleLabel { get; }
        public string ValueSummary { get; }
        public string DependencySummary { get; }
        public string OffsetSummary { get; }
        public bool CanOpenEditor { get; }
        public AiIndirectDispatchEditorLaunchContext? EditorLaunchContext => editorLaunchContext;
        public string Title => $"{(CanOpenEditor ? "rota" : "pacote")} {UnitIndex} · {RoleLabel}";
        public bool HasDependencySummary => !string.IsNullOrWhiteSpace(DependencySummary);
        public bool IsGuardRole => RoleLabel.Equals(Strings.U_Ai_PaPhaseGuard, StringComparison.OrdinalIgnoreCase);
        public bool IsNextStateRole => RoleLabel.Equals("proximo estado", StringComparison.OrdinalIgnoreCase);
        public bool IsCommandRole => RoleLabel.Contains("slot cmd", StringComparison.OrdinalIgnoreCase);
        public bool IsTargetRole => RoleLabel.Contains("slot alvo", StringComparison.OrdinalIgnoreCase);
        public int RoleSortKey =>
            RoleLabel == Strings.U_Ai_PaPhaseGuard ? 0
            : RoleLabel == "proximo estado" ? 3
            : RoleLabel.Contains("slot cmd", StringComparison.OrdinalIgnoreCase) ? 1
            : RoleLabel.Contains("slot alvo", StringComparison.OrdinalIgnoreCase) ? 2
            : 4;
        public int OffsetSortKey =>
            OffsetSummary.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(OffsetSummary[2..], System.Globalization.NumberStyles.HexNumber, null, out int parsed)
                ? parsed
                : int.MaxValue;
    }
}
