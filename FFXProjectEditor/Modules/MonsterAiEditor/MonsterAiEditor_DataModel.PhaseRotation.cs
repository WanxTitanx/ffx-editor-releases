using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiPhaseVariableAuditRow> PhaseVariableAudits { get; } = new();
        public ObservableCollection<AiPhaseDraftStepVm> PhaseDraftSteps { get; } = new();
        public ObservableCollection<AiPhaseConditionClauseVm> PhaseConditionClauses { get; } = new();
        public ObservableCollection<AiPhaseVarLinkedBlockVm> PhaseVariableEvidenceBlocks { get; } = new();
        public ObservableCollection<AiCommandOption> PhaseAbilityOptions { get; } = new();
        public IReadOnlyList<AiTargetOption> PhaseForbiddenRiteTargets { get; } = new[]
        {
            new AiTargetOption(0, Strings.F2_same_target_as_this_phase_dd25e21b, UseLinkedActionTarget: true),
            new AiTargetOption(0xFFFA, "Personagem #1"),
            new AiTargetOption(0xFFF9, "Personagem #2"),
            new AiTargetOption(0xFFF8, "Personagem #3"),
            new AiTargetOption(0xFFF2, Strings.F2_all_active_characters_frontlinechars_e1fa2f54),
            new AiTargetOption(0xFFFD, Strings.U_Ai_TargetCurrentAction),
            new AiTargetOption(0xFFFC, "Alvo imediato (TargetActorsNow)"),
            new AiTargetOption(0xFFEF, Strings.U_Ai_TargetLastAttacker),
            new AiTargetOption(0xFFF0, "Random Enemies / grupo predefinido (PredefinedGroup · RT2)"),
        };
        public IReadOnlyList<AiPhaseTriggerOption> PhaseTriggerOptions { get; } = new[]
        {
            new AiPhaseTriggerOption(
                AiPhaseTriggerKind.OnTurn,
                Strings.F2_on_its_turn_b92a95f8,
                Strings.F2_runs_on_the_monster_s_actual_combathandl_08ad5f00,
                "onTurn"),
            new AiPhaseTriggerOption(
                AiPhaseTriggerKind.OnHit,
                Strings.F2_on_being_hit_3c8949a2,
                Strings.F2_runs_on_the_monster_s_actual_combathandl_bd3f73e0,
                "onHit"),
        };

        readonly Dictionary<string, PhaseDraftSnapshot> phaseDraftsByCounter = new(StringComparer.OrdinalIgnoreCase);
        string? activePhaseDraftCounterKey;
        bool loadingPhaseDraft;
        bool loadingPhaseConditionBuilder;
        bool syncingPhaseAbilityOptions;
        bool updatingSelectedPhaseAbility;

        [ObservableProperty] private AiCommandOption? selectedPhaseAbility;
        partial void OnSelectedPhaseAbilityChanged(AiCommandOption? value)
        {
            if (updatingSelectedPhaseAbility || syncingPhaseAbilityOptions)
                return;

            if (value == null)
            {
                UpdateSelectedPhaseAbilityProjection();
                return;
            }

            AssignPhaseAbilityToSelectedStep(value, overwriteExisting: true);
        }

        [ObservableProperty] private AiPhaseVariableAuditRow? selectedPhaseVariableAudit;
        partial void OnSelectedPhaseVariableAuditChanged(AiPhaseVariableAuditRow? value)
        {
            SaveActivePhaseDraft();
            activePhaseDraftCounterKey = value?.AliasKey;
            PhaseVarAliasInput = value?.Alias ?? string.Empty;
            PhaseVarAuthoringIndexInput = value == null
                ? string.Empty
                : (value.PreferredVariableIndex ?? value.VariableIndex).ToString();
            UpdatePhaseVarAuthoringIndexSummary(value);
            LoadActivePhaseDraft();
            EnsurePhaseConditionSelection();
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
            RebuildPhaseVariableEvidence(value);
            RebuildFineAiPhaseVariableCrossLink();
            RebuildAdvancedPhaseUnits();
            UpdateAdvancedConditionContextSummary();
            OnPropertyChanged(nameof(CanSavePhaseVarAlias));
            OnPropertyChanged(nameof(CanSavePhaseVarAuthoringIndex));
            OnPropertyChanged(nameof(CanSeedPhaseConditionFromAdvancedFocus));
        }

        [ObservableProperty] private AiPhaseVariableAuditRow? selectedPhaseAdvanceVariableAudit;
        partial void OnSelectedPhaseAdvanceVariableAuditChanged(AiPhaseVariableAuditRow? value)
        {
            if (!loadingPhaseDraft)
                SaveActivePhaseDraft();
            OnPropertyChanged(nameof(PhaseAdvanceTargetSummary));
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
        }

        [ObservableProperty] private AiPhaseDraftStepVm? selectedPhaseDraftStep;
        partial void OnSelectedPhaseDraftStepChanged(AiPhaseDraftStepVm? value)
        {
            if (value != null && SelectedDraftIndirectRoute != null)
                SelectedDraftIndirectRoute = null;

            if (value?.SelectedAbility != null)
                SelectedAutomationAbility = value.SelectedAbility;
            SyncPhaseAbilityOptions();
            UpdateSelectedPhaseAbilityProjection();
            SelectedPhaseVarAdvance = value?.VarAdvances.FirstOrDefault();
            LoadSelectedPhaseStepGuardEditor(value);
            LoadPhaseConditionBuilderFromStep(value);
            NotifyPhaseDraftStateChanged();
            UpdatePhaseRotationPreview();
        }

        [ObservableProperty] private AiDraftIndirectRouteFocusVm? selectedDraftIndirectRoute;
        partial void OnSelectedDraftIndirectRouteChanged(AiDraftIndirectRouteFocusVm? value)
        {
            OnPropertyChanged(nameof(HasSelectedPhaseDraftEditor));
            OnPropertyChanged(nameof(ShowDirectPhaseDraftEditor));
            OnPropertyChanged(nameof(ShowIndirectPhaseDraftEditor));
        }

        [ObservableProperty] private AiPhaseVarAdvanceVm? selectedPhaseVarAdvance;
        partial void OnSelectedPhaseVarAdvanceChanged(AiPhaseVarAdvanceVm? value)
        {
            OnPropertyChanged(nameof(CanRemovePhaseVarAdvance));
        }

        [ObservableProperty] private AiPhaseConditionClauseVm? selectedPhaseConditionClause;
        partial void OnSelectedPhaseConditionClauseChanged(AiPhaseConditionClauseVm? value)
        {
            OnPropertyChanged(nameof(CanRemovePhaseConditionClause));
        }

        bool loadingPhaseStepGuardEditor;

        [ObservableProperty] private AiPhaseVariableAuditRow? selectedPhaseStepGuardVariableAudit;
        partial void OnSelectedPhaseStepGuardVariableAuditChanged(AiPhaseVariableAuditRow? value) => UpdatePhaseStepGuardPreview();

        [ObservableProperty] private string phaseStepGuardOperator = "==";
        partial void OnPhaseStepGuardOperatorChanged(string value) => UpdatePhaseStepGuardPreview();

        [ObservableProperty] private string phaseStepGuardValue = "0";
        partial void OnPhaseStepGuardValueChanged(string value) => UpdatePhaseStepGuardPreview();

        [ObservableProperty] private string phaseStepGuardPreview =
            Strings.F2_select_a_phase_to_edit_its_condition_12d1a627;

        [ObservableProperty] private string phaseStepGuardTechnicalSummary =
            Strings.F2_the_phase_condition_replaces_the_visual__b1a2014a;

        [ObservableProperty] private string phaseRotationAuditSummary =
            Strings.F2_open_the_manager_with_a_monster_selected_f88b6a1d;

        [ObservableProperty] private string phaseRotationPreview =
            Strings.F2_the_phase_rotation_has_not_been_set_up_y_f19ebd76;

        [ObservableProperty] private string phaseRotationApplySummary =
            Strings.F2_audit_ready_for_design_the_v1_writer_rec_fcc1f604;

        [ObservableProperty] private string phaseVariableEvidenceSummary =
            Strings.F2_choose_a_var_to_see_the_blocks_linked_to_8f8ef11e;

        [ObservableProperty] private string phaseVarAliasInput = string.Empty;

        [ObservableProperty] private string phaseVarAliasSummary =
            Strings.F2_aliases_are_saved_as_local_editor_metada_5e2920d9;

        [ObservableProperty] private string phaseVarAuthoringIndexInput = string.Empty;

        [ObservableProperty] private string phaseVarAuthoringIndexSummary =
            Strings.F2_the_author_template_id_may_request_a_hig_35bfabd6;

        [ObservableProperty] private string phasePrivateVarLabSummary = Strings.U_Ai_PrPhaseEntry;

        public IReadOnlyList<string> PhaseConditionOperators { get; } = new[]
        {
            "==", "!=", ">", "<", ">=", "<=", "> (u32)", "< (u32)", ">= (u32)", "<= (u32)",
        };
        public IReadOnlyList<string> PhaseConditionJoins { get; } = new[] { "E", "OU" };

        [ObservableProperty] private AiPhaseVariableAuditRow? phaseConditionLeftVariable;
        partial void OnPhaseConditionLeftVariableChanged(AiPhaseVariableAuditRow? value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private AiPhaseVariableAuditRow? phaseConditionRightVariable;
        partial void OnPhaseConditionRightVariableChanged(AiPhaseVariableAuditRow? value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionLeftOperator = ">";
        partial void OnPhaseConditionLeftOperatorChanged(string value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionRightOperator = "==";
        partial void OnPhaseConditionRightOperatorChanged(string value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionLeftValue = "0";
        partial void OnPhaseConditionLeftValueChanged(string value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionRightValue = "0";
        partial void OnPhaseConditionRightValueChanged(string value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionJoin = "E";
        partial void OnPhaseConditionJoinChanged(string value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private bool phaseConditionUseSecondVariable = true;
        partial void OnPhaseConditionUseSecondVariableChanged(bool value) => UpdatePhaseConditionPreview();

        [ObservableProperty] private string phaseConditionPreview =
            Strings.F2_choose_one_or_two_vars_to_build_a_condit_d4050ad7;

        [ObservableProperty] private string phaseConditionTechnicalSummary =
            Strings.F2_the_multivar_condition_becomes_an_atel_g_014e55ab;

        [ObservableProperty] private bool phaseConditionUseHpBelowGuard;
        partial void OnPhaseConditionUseHpBelowGuardChanged(bool value) { UpdatePhaseConditionPreview(); SaveActivePhaseDraft(); }

        [ObservableProperty] private string phaseConditionHpBelowPercentText = "50";
        partial void OnPhaseConditionHpBelowPercentTextChanged(string value) { UpdatePhaseConditionPreview(); SaveActivePhaseDraft(); }

        public bool HasPhaseConditionHpGate => PhaseConditionUseHpBelowGuard;

        [ObservableProperty] private string phaseAdvanceMin = "1";
        partial void OnPhaseAdvanceMinChanged(string value)
        {
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        [ObservableProperty] private string phaseAdvanceMax = "3";
        partial void OnPhaseAdvanceMaxChanged(string value)
        {
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        [ObservableProperty] private string phaseFinalLimit = "4";
        partial void OnPhaseFinalLimitChanged(string value)
        {
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        [ObservableProperty] private bool phaseResetAfterFinal = true;
        partial void OnPhaseResetAfterFinalChanged(bool value)
        {
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        public bool CanRemovePhaseDraftStep => SelectedPhaseDraftStep != null;
        public bool CanAddPhaseFinalStep => !PhaseDraftSteps.Any(s => s.IsFinalPhase);
        public bool HasSelectedPhaseDraftStep => SelectedPhaseDraftStep != null;
        public bool HasSelectedPhaseDraftEditor => SelectedPhaseDraftStep != null || SelectedDraftIndirectRoute != null;
        public bool ShowDirectPhaseDraftEditor => SelectedPhaseDraftStep != null && SelectedDraftIndirectRoute == null;
        public bool ShowIndirectPhaseDraftEditor => SelectedDraftIndirectRoute != null;
        public bool ShowPhaseDraftSelectionPlaceholder => !HasSelectedPhaseDraftEditor;
        public bool IsCommonPhaseDraftEmpty => PhaseDraftSteps.Count == 0;
        public bool IsPhaseDraftEmpty => PhaseDraftSteps.Count == 0 && PhaseDraftRouteCards.Count == 0;
        public bool CanRemovePhaseVarAdvance => SelectedPhaseVarAdvance != null;
        public bool CanRemovePhaseConditionClause => SelectedPhaseConditionClause != null;
        public bool HasPhaseConditionClauses => PhaseConditionClauses.Count > 0;
        public bool CanSavePhaseVarAlias => SelectedPhaseVariableAudit != null;
        public bool CanSavePhaseVarAuthoringIndex => SelectedPhaseVariableAudit != null;
        public bool CanCreatePhasePrivateVarLab =>
            selectedScript != null &&
            selectedPath != null &&
            selectedScript.HasScript;

        public string PhaseCounterRuleSummary => SelectedPhaseVariableAudit == null
            ? Strings.F2_var_that_determines_the_phase_none_9970c1f7
            : string.Format(Strings.U_Ai_PrVarDecidesPhase, SelectedPhaseVariableAudit.VariableName, SelectedPhaseVariableAudit.IndexLabel, SelectedPhaseVariableAudit.StorageLabel);

        AiPhaseVariableAuditRow? PhaseAdvanceVariable => SelectedPhaseAdvanceVariableAudit;
        string PhaseAdvanceVariableName => PhaseAdvanceVariable?.VariableName ?? Strings.U_Ai_PrAdvanceVar;
        bool SamePhaseAndAdvanceVariable => SelectedPhaseVariableAudit?.AliasKey == PhaseAdvanceVariable?.AliasKey;

        public string PhaseAdvanceTargetSummary => SelectedPhaseAdvanceVariableAudit == null
            ? Strings.F2_explicitly_choose_which_var_this_phase_w_58c16d64
            : string.Format(Strings.U_Ai_PrAdvanceWrites, SelectedPhaseAdvanceVariableAudit.VariableName, SelectedPhaseAdvanceVariableAudit.IndexLabel);

        public string PhaseAdvanceRuleSummary
        {
            get
            {
                string counter = SelectedPhaseVariableAudit?.VariableName ?? Strings.U_Ai_PrPhaseVar;
                return string.Format(Strings.U_Ai_PrNoGlobalAdvance, counter, PhaseFinalLimit);
            }
        }

        public string PhaseDraftCounterSummary
        {
            get
            {
                string counter = SelectedPhaseVariableAudit?.VariableName ?? Strings.F2_variable_not_yet_selected_39071e63;
                return PhaseDraftSteps.Count == 0
                    ? string.Format(Strings.U_Ai_PrEmptyDraftOf, counter)
                    : string.Format(Strings.U_Ai_PrDraftOf, counter, PhaseFinalLimit);
            }
        }

        public void PreparePhaseRotationManager() => PrepareCommonPhaseRotationManager();

        public void PrepareCommonPhaseRotationManager()
        {
            SelectedDraftIndirectRoute = null;
            RefreshPhaseRotationAudit();
            EnsurePhaseDraftDefaults();
            UpdatePhaseRotationPreview();
            OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
        }

        public void RefreshPhaseRotationAudit()
        {
            SaveActivePhaseDraft();
            string? previousSelectedKey = SelectedPhaseVariableAudit?.AliasKey ?? activePhaseDraftCounterKey;
            string? previousAdvanceKey = SelectedPhaseAdvanceVariableAudit?.AliasKey;

            PhaseVariableAudits.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                PhaseRotationAuditSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_164a3993;
                PhaseRotationApplySummary = Strings.F2_nothing_was_saved_no_aifile_selected_8cff6418;
                PhasePrivateVarLabSummary = Strings.F2_private_var_unavailable_no_aifile_select_f47273cb;
                OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
                return;
            }

            Dictionary<int, PhaseVarAccumulator> acc = selectedScript.Variables
                .ToDictionary(v => v.Index, v => new PhaseVarAccumulator(v));

            IReadOnlyList<AiInstruction> ins = selectedScript.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                AiInstruction row = ins[i];
                if (row.OperandKind is not (AiOperandKind.VarLoad or AiOperandKind.VarStore))
                    continue;
                if (!acc.TryGetValue(row.Operand, out PhaseVarAccumulator? item))
                    continue;

                int workerIndex = AiAutomation.OwningWorkerIndex(selectedScript, row.Offset);
                if (workerIndex >= 0) item.Workers.Add(workerIndex);
                item.FirstOffset = item.FirstOffset < 0 ? row.Offset : Math.Min(item.FirstOffset, row.Offset);
                item.LastOffset = Math.Max(item.LastOffset, row.Offset);

                if (row.OperandKind == AiOperandKind.VarLoad)
                {
                    item.Reads++;
                    PhaseLoadUse use = ClassifyLoadUse(ins, i);
                    if (use.HasCondition)
                        item.ConditionHints++;
                    if (use.HasCompare)
                        item.CompareHints++;
                    if (use.HasSwitch)
                        item.SwitchHints++;
                }
                else
                {
                    item.Writes++;
                    if (LooksLikeReset(ins, i))
                        item.ResetHints++;
                    if (LooksLikeIncrementOrRandomAdvance(ins, i, row.Operand))
                        item.AdvanceHints++;
                    if (LooksLikeMathStore(ins, i))
                        item.MathStoreHints++;
                    foreach (ushort sourceVar in SourceVarsForStore(ins, i, row.Operand))
                    {
                        if (acc.TryGetValue(sourceVar, out PhaseVarAccumulator? source))
                            item.SourceVariableNames.Add(source.Variable.Name);
                    }
                }
            }

            IReadOnlyDictionary<int, AiVariableSemanticAnnotation> semanticMap =
                AiPhaseVariableSemanticResolver.Resolve(selectedScript);

            foreach (PhaseVarAccumulator item in acc.Values
                         .OrderByDescending(v => v.Score)
                         .ThenBy(v => v.Variable.Name, StringComparer.OrdinalIgnoreCase))
            {
                string key = PhaseVarAliasStore.Key(selectedMonster?.Id ?? "m???", item.Variable.Storage, item.Variable.Slot);
                PhaseVarAliasEntry metadata = PhaseVarAliasStore.GetMetadata(key);
                PhaseVariableAudits.Add(item.ToRow(selectedScript, key, metadata, CurrentAiHashShort(), semanticMap));
            }

            SeedPhaseDraftsFromScriptIfMissing();

            SelectedPhaseVariableAudit =
                (!string.IsNullOrWhiteSpace(previousSelectedKey)
                    ? PhaseVariableAudits.FirstOrDefault(v =>
                        string.Equals(v.AliasKey, previousSelectedKey, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? PhaseVariableAudits.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(previousAdvanceKey))
            {
                AiPhaseVariableAuditRow? previousAdvance = PhaseVariableAudits.FirstOrDefault(v =>
                    string.Equals(v.AliasKey, previousAdvanceKey, StringComparison.OrdinalIgnoreCase));
                if (previousAdvance != null)
                    SelectedPhaseAdvanceVariableAudit = previousAdvance;
            }

            EnsurePhaseConditionSelection();
            PhaseRotationAuditSummary = PhaseVariableAudits.Count == 0
                ? Strings.F2_no_declared_variable_was_found_in_this_a_3dace490
                : string.Format(Strings.U_Ai_PrLibraryFound, PhaseVariableAudits.Count);
            PhasePrivateVarLabSummary = AiScript_File.TryFindFreePrivateVariableSlot(selectedScript, out int slot, out string reason)
                ? string.Format(Strings.U_Ai_NextPrivVar, slot, reason)
                : string.Format(Strings.U_Ai_PrivVarUnavailable, reason);
            OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
            UpdatePhaseConditionPreview();
        }

        void EnsurePhaseConditionSelection()
        {
            if (PhaseVariableAudits.Count == 0)
            {
                PhaseConditionLeftVariable = null;
                PhaseConditionRightVariable = null;
                SelectedPhaseStepGuardVariableAudit = null;
                UpdatePhaseStepGuardPreview();
                return;
            }

            if (PhaseConditionLeftVariable == null || !PhaseVariableAudits.Contains(PhaseConditionLeftVariable))
                PhaseConditionLeftVariable = SelectedPhaseVariableAudit ?? PhaseVariableAudits.FirstOrDefault();
            if (PhaseConditionRightVariable == null || !PhaseVariableAudits.Contains(PhaseConditionRightVariable))
                PhaseConditionRightVariable = PhaseVariableAudits.FirstOrDefault(v => v != PhaseConditionLeftVariable)
                                             ?? PhaseVariableAudits.FirstOrDefault();

            foreach (AiPhaseConditionClauseVm clause in PhaseConditionClauses)
            {
                AiPhaseVariableAuditRow? stable = FindPhaseVariableByKey(clause.SelectedVariable?.AliasKey);
                if (stable != null && !ReferenceEquals(stable, clause.SelectedVariable))
                    clause.SelectedVariable = stable;
                else if (clause.SelectedVariable == null || !PhaseVariableAudits.Contains(clause.SelectedVariable))
                    clause.SelectedVariable = PhaseVariableAudits.FirstOrDefault();
            }

            SelectedPhaseStepGuardVariableAudit =
                FindPhaseVariableByKey(SelectedPhaseStepGuardVariableAudit?.AliasKey)
                ?? (SelectedPhaseStepGuardVariableAudit != null && PhaseVariableAudits.Contains(SelectedPhaseStepGuardVariableAudit)
                    ? SelectedPhaseStepGuardVariableAudit
                    : SelectedPhaseVariableAudit ?? PhaseVariableAudits.FirstOrDefault());
            UpdatePhaseStepGuardPreview();
        }

        void LoadSelectedPhaseStepGuardEditor(AiPhaseDraftStepVm? step)
        {
            loadingPhaseStepGuardEditor = true;
            try
            {
                if (step == null)
                {
                    SelectedPhaseStepGuardVariableAudit = SelectedPhaseVariableAudit ?? PhaseVariableAudits.FirstOrDefault();
                    PhaseStepGuardOperator = "==";
                    PhaseStepGuardValue = "0";
                    return;
                }

                SelectedPhaseStepGuardVariableAudit =
                    FindPhaseVariableByKey(step.SimpleConditionVariableKey)
                    ?? SelectedPhaseVariableAudit
                    ?? PhaseVariableAudits.FirstOrDefault();
                PhaseStepGuardOperator = string.IsNullOrWhiteSpace(step.SimpleConditionOperator)
                    ? "=="
                    : step.SimpleConditionOperator;
                PhaseStepGuardValue = string.IsNullOrWhiteSpace(step.SimpleConditionValue)
                    ? Math.Max(0, step.Number - 1).ToString()
                    : step.SimpleConditionValue;
            }
            finally
            {
                loadingPhaseStepGuardEditor = false;
            }

            UpdatePhaseStepGuardPreview();
        }

        void LoadPhaseConditionBuilderFromStep(AiPhaseDraftStepVm? step)
        {
            loadingPhaseConditionBuilder = true;
            string source = string.Empty;
            try
            {
                PhaseConditionClauses.Clear();
                SelectedPhaseConditionClause = null;

                IReadOnlyList<PhaseConditionClauseSnapshot> clauses = Array.Empty<PhaseConditionClauseSnapshot>();
                if (step != null &&
                    step.ConditionGuard != null &&
                    TryDecodePhaseConditionClauses(step.ConditionGuard, out IReadOnlyList<PhaseConditionClauseSnapshot> decoded))
                {
                    clauses = decoded;
                    source = Strings.U_Ai_PrOwnConditionRecorded;
                }
                else if (TryBuildFallbackConditionClause(step, out PhaseConditionClauseSnapshot fallback))
                {
                    clauses = new[] { fallback };
                    source = Strings.U_Ai_PrCardFallback;
                }

                foreach (PhaseConditionClauseSnapshot item in clauses)
                {
                    PhaseConditionClauses.Add(new AiPhaseConditionClauseVm(
                        PhaseConditionClauses.Count + 1,
                        OnPhaseConditionClauseChanged,
                        PhaseVariableAudits,
                        PhaseConditionOperators,
                        PhaseConditionJoins)
                    {
                        SelectedVariable = FindPhaseVariableByKey(item.VariableKey)
                                           ?? SelectedPhaseVariableAudit
                                           ?? PhaseVariableAudits.FirstOrDefault(),
                        Operator = string.IsNullOrWhiteSpace(item.Operator) ? "==" : item.Operator,
                        Value = string.IsNullOrWhiteSpace(item.Value) ? "0" : item.Value,
                        Join = string.IsNullOrWhiteSpace(item.Join) ? "E" : item.Join,
                    });
                }

                ReindexPhaseConditionClauses();
                SelectedPhaseConditionClause = PhaseConditionClauses.FirstOrDefault();
            }
            finally
            {
                loadingPhaseConditionBuilder = false;
            }

            UpdatePhaseConditionPreview();
            if (PhaseConditionClauses.Count > 0 &&
                TryBuildPhaseConditionGuard(out IReadOnlyList<AiInstruction> guard, out string message))
            {
                PhaseConditionTechnicalSummary =
                    string.Format(Strings.U_Ai_PrBuilderLoaded, source, message, guard.Count, PhaseConditionRiskWarning());
            }
        }

        bool TryBuildFallbackConditionClause(AiPhaseDraftStepVm? step, out PhaseConditionClauseSnapshot clause)
        {
            clause = new PhaseConditionClauseSnapshot(null, "==", "0", "E");
            if (step == null || SelectedPhaseVariableAudit == null)
                return false;

            clause = step.IsFinalPhase
                ? new PhaseConditionClauseSnapshot(SelectedPhaseVariableAudit.AliasKey, ">", PhaseFinalLimit, "E")
                : new PhaseConditionClauseSnapshot(SelectedPhaseVariableAudit.AliasKey, "==", Math.Max(0, step.Number - 1).ToString(), "E");
            return true;
        }

        bool TryDecodePhaseConditionClauses(
            IReadOnlyList<AiInstruction> guard,
            out IReadOnlyList<PhaseConditionClauseSnapshot> clauses)
        {
            clauses = Array.Empty<PhaseConditionClauseSnapshot>();
            List<AiInstruction> core = StripPhaseChanceTail(guard);
            var result = new List<PhaseConditionClauseSnapshot>();
            int i = 0;
            while (i < core.Count)
            {
                if (i + 2 >= core.Count)
                    return false;
                if (!AiVarConditionBuilder.TryReadImmediateComparison(core, i, out AiVarConditionClause clause))
                    return false;
                if (!TryFindVariableKeyByAuthoringIndex(clause.VariableIndex, out string? key))
                    return false;

                string op = AiVarConditionBuilder.OperatorLabel(clause.Operator);
                string value = clause.Value.ToString();
                i += 3;
                string join = "E";
                if (result.Count > 0)
                {
                    if (i >= core.Count || core[i].Opcode is not (0x01 or 0x02))
                        return false;
                    join = core[i].Opcode == 0x01 ? "OU" : "E";
                    i++;
                }

                result.Add(new PhaseConditionClauseSnapshot(
                    key,
                    op,
                    value,
                    join));
            }

            clauses = result;
            return result.Count > 0;
        }

        static List<AiInstruction> StripPhaseChanceTail(IReadOnlyList<AiInstruction> guard)
        {
            var core = guard.Select(ClonePhaseInstruction).ToList();
            if (core.Count >= 9 &&
                core[^6].Opcode == 0xB5 && core[^6].Operand == 0x00A9 &&
                core[^5].Opcode == 0xAE &&
                core[^4].Opcode == 0x18 &&
                core[^3].Opcode == 0xAE && core[^3].Operand == 0 &&
                core[^2].Opcode == 0x06 &&
                core[^1].Opcode == 0x02)
            {
                core.RemoveRange(core.Count - 6, 6);
            }
            return core;
        }

        void HydratePhaseStepConditionFromGuard(AiPhaseDraftStepVm step)
        {
            if (step.ConditionGuard == null || step.ConditionGuard.Count == 0)
                return;
            if (!TryDecodePhaseConditionClauses(step.ConditionGuard, out IReadOnlyList<PhaseConditionClauseSnapshot> clauses) ||
                clauses.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(step.ConditionSummary))
                step.ConditionSummary = BuildPhaseConditionSummaryFromClauses(clauses);

            if (clauses.Count != 1)
                return;

            PhaseConditionClauseSnapshot clause = clauses[0];
            if (string.IsNullOrWhiteSpace(step.SimpleConditionVariableKey))
            {
                step.SimpleConditionVariableKey = clause.VariableKey;
                step.SimpleConditionOperator = string.IsNullOrWhiteSpace(clause.Operator) ? "==" : clause.Operator;
                step.SimpleConditionValue = string.IsNullOrWhiteSpace(clause.Value) ? "0" : clause.Value;
                return;
            }

            if (string.IsNullOrWhiteSpace(step.SimpleConditionOperator))
                step.SimpleConditionOperator = string.IsNullOrWhiteSpace(clause.Operator) ? "==" : clause.Operator;
            if (string.IsNullOrWhiteSpace(step.SimpleConditionValue))
                step.SimpleConditionValue = string.IsNullOrWhiteSpace(clause.Value) ? "0" : clause.Value;
        }

        string BuildPhaseConditionSummaryFromClauses(IReadOnlyList<PhaseConditionClauseSnapshot> clauses) =>
            string.Join(" ", clauses.Select((c, i) =>
            {
                string variableName = FindPhaseVariableByKey(c.VariableKey)?.VariableName ?? c.VariableKey ?? "var";
                return i == 0
                    ? $"{variableName} {c.Operator} {c.Value}"
                    : $"{c.Join} {variableName} {c.Operator} {c.Value}";
            }));

        public void AddPhaseConditionClause()
        {
            var clause = new AiPhaseConditionClauseVm(
                PhaseConditionClauses.Count + 1,
                OnPhaseConditionClauseChanged,
                PhaseVariableAudits,
                PhaseConditionOperators,
                PhaseConditionJoins)
            {
                SelectedVariable = SelectedPhaseVariableAudit ?? PhaseConditionLeftVariable ?? PhaseVariableAudits.FirstOrDefault(),
                Operator = PhaseConditionClauses.Count == 0 ? ">" : "==",
                Value = "0",
                Join = "E",
            };
            PhaseConditionClauses.Add(clause);
            ReindexPhaseConditionClauses();
            SelectedPhaseConditionClause = clause;
            PhaseConditionTechnicalSummary = Strings.F2_condition_added_to_the_builder_edit_var__78ac5c03;
            UpdatePhaseConditionPreview();
            SaveActivePhaseDraft();
        }

        public void RemoveSelectedPhaseConditionClause()
        {
            AiPhaseConditionClauseVm? clause = SelectedPhaseConditionClause;
            if (clause == null)
            {
                PhaseConditionTechnicalSummary = Strings.U_Ai_PrSelectConditionRemove;
                return;
            }

            int idx = PhaseConditionClauses.IndexOf(clause);
            PhaseConditionClauses.Remove(clause);
            ReindexPhaseConditionClauses();
            SelectedPhaseConditionClause = PhaseConditionClauses.Count == 0
                ? null
                : PhaseConditionClauses[Math.Clamp(idx, 0, PhaseConditionClauses.Count - 1)];
            PhaseConditionTechnicalSummary = Strings.F2_condition_removed_from_the_builder_nothi_3babfed4;
            UpdatePhaseConditionPreview();
            SaveActivePhaseDraft();
        }

        public void ClearPhaseConditionClauses()
        {
            PhaseConditionClauses.Clear();
            SelectedPhaseConditionClause = null;
            ReindexPhaseConditionClauses();
            PhaseConditionTechnicalSummary = Strings.F2_condition_builder_cleared_nothing_was_ch_d49a624a;
            UpdatePhaseConditionPreview();
            SaveActivePhaseDraft();
        }

        void OnPhaseConditionClauseChanged()
        {
            UpdatePhaseConditionPreview();
            if (!loadingPhaseConditionBuilder)
                SaveActivePhaseDraft();
        }

        void ReindexPhaseConditionClauses()
        {
            for (int i = 0; i < PhaseConditionClauses.Count; i++)
                PhaseConditionClauses[i].Number = i + 1;
            OnPropertyChanged(nameof(CanRemovePhaseConditionClause));
            OnPropertyChanged(nameof(HasPhaseConditionClauses));
            if (!loadingPhaseConditionBuilder)
                SaveActivePhaseDraft();
        }

        static bool LooksLikeReset(IReadOnlyList<AiInstruction> ins, int storeIndex)
        {
            int lo = Math.Max(0, storeIndex - 3);
            for (int i = storeIndex - 1; i >= lo; i--)
                if (ins[i].Opcode == 0xAE && ins[i].Operand == 0)
                    return true;
            return false;
        }

        static bool LooksLikeIncrementOrRandomAdvance(IReadOnlyList<AiInstruction> ins, int storeIndex, ushort varOperand)
        {
            int lo = Math.Max(0, storeIndex - 10);
            bool sawSameVar = false;
            bool sawMath = false;
            bool sawRandom = false;
            for (int i = lo; i < storeIndex; i++)
            {
                AiInstruction row = ins[i];
                if (row.Opcode == 0x9F && row.Operand == varOperand)
                    sawSameVar = true;
                if (row.Opcode is 0x14 or 0x15 or 0x18)
                    sawMath = true;
                if (row.Opcode == 0x2B || (row.OperandKind == AiOperandKind.FuncId && row.Operand == 0x00A9))
                    sawRandom = true;
            }
            return sawSameVar && (sawMath || sawRandom);
        }

        static PhaseLoadUse ClassifyLoadUse(IReadOnlyList<AiInstruction> ins, int loadIndex)
        {
            int hi = Math.Min(ins.Count - 1, loadIndex + 8);
            bool sawCompare = false;
            bool sawSwitch = false;
            bool sawBranch = false;
            for (int i = loadIndex + 1; i <= hi; i++)
            {
                byte op = ins[i].Opcode;
                if (AiVarConditionBuilder.IsComparisonOpcode(op))
                    sawCompare = true;
                if (op == 0x2C)
                    sawSwitch = true;
                if ((sawCompare || sawSwitch) && op is 0xD6 or 0xD7 or 0xB0)
                    sawBranch = true;
            }
            return new PhaseLoadUse(sawCompare || sawSwitch || sawBranch, sawCompare, sawSwitch);
        }

        static bool LooksLikeMathStore(IReadOnlyList<AiInstruction> ins, int storeIndex)
        {
            int lo = Math.Max(0, storeIndex - 12);
            for (int i = lo; i < storeIndex; i++)
                if (ins[i].Opcode is 0x12 or 0x14 or 0x15 or 0x16 or 0x17 or 0x18)
                    return true;
            return false;
        }

        static IEnumerable<ushort> SourceVarsForStore(IReadOnlyList<AiInstruction> ins, int storeIndex, ushort target)
        {
            int lo = Math.Max(0, storeIndex - 12);
            HashSet<ushort> sources = new();
            for (int i = lo; i < storeIndex; i++)
                if (ins[i].Opcode == 0x9F && ins[i].Operand != target)
                    sources.Add(ins[i].Operand);
            return sources;
        }

        void EnsurePhaseDraftDefaults()
        {
            ReindexPhaseDraftSteps();
            if (SelectedPhaseDraftStep != null && !PhaseDraftSteps.Contains(SelectedPhaseDraftStep))
                SelectedPhaseDraftStep = PhaseDraftSteps.FirstOrDefault();
        }

        void SaveActivePhaseDraft()
        {
            if (loadingPhaseDraft || string.IsNullOrWhiteSpace(activePhaseDraftCounterKey))
                return;

            phaseDraftsByCounter[activePhaseDraftCounterKey] = new PhaseDraftSnapshot(
                SelectedPhaseAdvanceVariableAudit?.AliasKey,
                PhaseAdvanceMin,
                PhaseAdvanceMax,
                PhaseFinalLimit,
                PhaseResetAfterFinal,
                PhaseConditionClauses.Select(SnapshotConditionClause).ToList(),
                PhaseDraftSteps.Select(SnapshotStep).ToList(),
                PhaseConditionUseHpBelowGuard,
                PhaseConditionHpBelowPercentText);
        }

        PhaseDraftStepSnapshot SnapshotStep(AiPhaseDraftStepVm step) => new(
            step.IsFinalPhase,
            step.SelectedAbility?.Operand,
            step.TriggerKind,
            step.SelectedTarget == null ? null : new PhaseTargetSnapshot(step.SelectedTarget.Operand, step.SelectedTarget.TargetRecipeKind),
            step.StopHere,
            step.UseChance,
            step.ChanceKText,
            step.ConditionSummary,
            step.ConditionGuard?.Select(ClonePhaseInstruction).ToList(),
            step.SimpleConditionVariableKey,
            step.SimpleConditionOperator,
            step.SimpleConditionValue,
            step.UseHpBelowGuard,
            step.HpBelowPercentText,
            step.UseForbiddenRite,
            step.ForbiddenTarget == null ? null : new PhaseTargetSnapshot(step.ForbiddenTarget.Operand, step.ForbiddenTarget.TargetRecipeKind, step.ForbiddenTarget.UseLinkedActionTarget),
            step.ForbiddenStatus?.FieldId,
            step.ForbiddenValue,
            step.VarAdvances.Select(SnapshotAdvance).ToList());

        static PhaseVarAdvanceSnapshot SnapshotAdvance(AiPhaseVarAdvanceVm advance) => new(
            advance.Variable?.AliasKey,
            advance.MinText,
            advance.MaxText,
            advance.IsReset);

        static PhaseConditionClauseSnapshot SnapshotConditionClause(AiPhaseConditionClauseVm clause) => new(
            clause.SelectedVariable?.AliasKey,
            clause.Operator,
            clause.Value,
            clause.Join);

        void LoadActivePhaseDraft()
        {
            loadingPhaseDraft = true;
            try
            {
                PhaseDraftSteps.Clear();
                PhaseConditionClauses.Clear();
                SelectedPhaseDraftStep = null;
                SelectedPhaseVarAdvance = null;
                SelectedPhaseConditionClause = null;

                PhaseDraftSnapshot? snapshot = null;
                if (!string.IsNullOrWhiteSpace(activePhaseDraftCounterKey))
                    phaseDraftsByCounter.TryGetValue(activePhaseDraftCounterKey, out snapshot);

                SelectedPhaseAdvanceVariableAudit = FindPhaseVariableByKey(snapshot?.DefaultAdvanceVariableKey)
                                                     ?? SelectedPhaseVariableAudit
                                                     ?? PhaseVariableAudits.FirstOrDefault();
                PhaseAdvanceMin = snapshot?.DefaultAdvanceMin ?? "1";
                PhaseAdvanceMax = snapshot?.DefaultAdvanceMax ?? "3";
                PhaseFinalLimit = snapshot?.FinalLimit ?? "4";
                PhaseResetAfterFinal = snapshot?.ResetAfterFinal ?? true;
                PhaseConditionUseHpBelowGuard = snapshot?.PhaseConditionUseHpBelowGuard ?? false;
                PhaseConditionHpBelowPercentText = string.IsNullOrWhiteSpace(snapshot?.PhaseConditionHpBelowPercentText)
                    ? "50" : snapshot.PhaseConditionHpBelowPercentText;

                if (snapshot != null)
                {
                    foreach (PhaseConditionClauseSnapshot item in snapshot.ConditionBuilderClauses)
                    {
                        PhaseConditionClauses.Add(new AiPhaseConditionClauseVm(
                            PhaseConditionClauses.Count + 1,
                            OnPhaseConditionClauseChanged,
                            PhaseVariableAudits,
                            PhaseConditionOperators,
                            PhaseConditionJoins)
                        {
                            SelectedVariable = FindPhaseVariableByKey(item.VariableKey)
                                               ?? SelectedPhaseVariableAudit
                                               ?? PhaseVariableAudits.FirstOrDefault(),
                            Operator = string.IsNullOrWhiteSpace(item.Operator) ? "==" : item.Operator,
                            Value = string.IsNullOrWhiteSpace(item.Value) ? "0" : item.Value,
                            Join = string.IsNullOrWhiteSpace(item.Join) ? "E" : item.Join,
                        });
                    }
                }

                if (snapshot != null)
                {
                    foreach (PhaseDraftStepSnapshot item in snapshot.Steps)
                    {
                        AiCommandOption? ability = ResolvePhaseAbilityOption(item.AbilityOperand);
                        var step = new AiPhaseDraftStepVm(PhaseDraftSteps.Count + 1, item.IsFinalPhase, OnPhaseDraftStepChanged)
                        {
                            SelectedAbility = ability,
                            SelectedTriggerOption = ResolvePhaseTriggerOption(item.TriggerKind),
                            SelectedTarget = FindTargetOption(item.Target) ?? SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault(),
                            StopHere = item.StopHere,
                            UseChance = item.UseChance,
                            ChanceKText = item.ChanceKText,
                            ConditionSummary = item.ConditionSummary,
                            ConditionGuard = item.ConditionGuard?.Select(ClonePhaseInstruction).ToList(),
                            SimpleConditionVariableKey = item.SimpleConditionVariableKey,
                            SimpleConditionOperator = item.SimpleConditionOperator,
                            SimpleConditionValue = item.SimpleConditionValue,
                            UseHpBelowGuard = item.UseHpBelowGuard,
                            HpBelowPercentText = string.IsNullOrWhiteSpace(item.HpBelowPercentText) ? "50" : item.HpBelowPercentText,
                            UseForbiddenRite = item.UseForbiddenRite,
                            ForbiddenTarget = FindPhaseForbiddenTargetOption(item.ForbiddenTarget) ?? PhaseForbiddenRiteTargets.FirstOrDefault(),
                            ForbiddenStatus = FindForbiddenStatusByField(item.ForbiddenStatusFieldId) ?? ForbiddenStatusPresets.FirstOrDefault(),
                            ForbiddenValue = string.IsNullOrWhiteSpace(item.ForbiddenValue) ? "1" : item.ForbiddenValue,
                        };
                        HydratePhaseStepConditionFromGuard(step);
                        foreach (PhaseVarAdvanceSnapshot advance in item.Advances)
                        {
                            step.VarAdvances.Add(new AiPhaseVarAdvanceVm(
                                step.VarAdvances.Count + 1,
                                FindPhaseVariableByKey(advance.VariableKey),
                                advance.MinText,
                                advance.MaxText,
                                advance.IsReset,
                                OnPhaseDraftStepChanged));
                        }
                        step.NotifyVarAdvancesChanged();
                        PhaseDraftSteps.Add(step);
                    }
                }

                ReindexPhaseDraftSteps();
                SyncPhaseAbilityOptions();
                ReindexPhaseConditionClauses();
                SelectedPhaseDraftStep = PhaseDraftSteps.FirstOrDefault();
                SelectedPhaseConditionClause = PhaseConditionClauses.FirstOrDefault();
            }
            finally
            {
                loadingPhaseDraft = false;
            }
            UpdatePhaseConditionPreview();
        }

        AiPhaseVariableAuditRow? FindPhaseVariableByKey(string? key) =>
            string.IsNullOrWhiteSpace(key)
                ? null
                : PhaseVariableAudits.FirstOrDefault(v => string.Equals(v.AliasKey, key, StringComparison.OrdinalIgnoreCase));

        AiTargetOption? FindTargetOption(PhaseTargetSnapshot? target) =>
            target == null
                ? null
                : AuthoringTargetOptions.FirstOrDefault(t =>
                    t.Operand == target.Operand && t.TargetRecipeKind == target.Kind);

        AiTargetOption? FindPhaseForbiddenTargetOption(PhaseTargetSnapshot? target) =>
            target == null
                ? null
                : target.UsePhaseTarget
                    ? PhaseForbiddenRiteTargets.FirstOrDefault(t => t.UseLinkedActionTarget)
                : PhaseForbiddenRiteTargets.FirstOrDefault(t =>
                    t.UseLinkedActionTarget == target.UsePhaseTarget &&
                    t.Operand == target.Operand &&
                    t.TargetRecipeKind == target.Kind);

        AiPhaseTriggerOption ResolvePhaseTriggerOption(AiPhaseTriggerKind kind) =>
            kind switch
            {
                AiPhaseTriggerKind.OnHit => PhaseTriggerOptions.First(option => option.Kind == AiPhaseTriggerKind.OnHit),
                AiPhaseTriggerKind.BattleStart => PhaseTriggerOptions.First(option => option.Kind == AiPhaseTriggerKind.OnTurn),
                AiPhaseTriggerKind.AfterAnyValidTurn => PhaseTriggerOptions.First(option => option.Kind == AiPhaseTriggerKind.OnHit),
                _ => PhaseTriggerOptions.First(option => option.Kind == AiPhaseTriggerKind.OnTurn),
            };

        AiForbiddenStatusPreset? FindForbiddenStatusByField(ushort? fieldId) =>
            fieldId == null ? null : ForbiddenStatusPresets.FirstOrDefault(s => s.FieldId == fieldId.Value);

        void SeedPhaseDraftsFromScriptIfMissing()
        {
            if (selectedScript == null || !selectedScript.HasScript || PhaseVariableAudits.Count == 0)
                return;

            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (!TryGetRuntimeVariableIndex(row, out ushort counterIndex))
                    continue;
                if (!TryReadPhaseDraftSnapshotFromScript(counterIndex, row.AliasKey, out PhaseDraftSnapshot snapshot))
                    continue;

                if (!phaseDraftsByCounter.TryGetValue(row.AliasKey, out PhaseDraftSnapshot? existing) ||
                    existing.Steps.Count == 0)
                {
                    phaseDraftsByCounter[row.AliasKey] = snapshot;
                }
            }
        }

        void RebuildPhaseVariableEvidence(AiPhaseVariableAuditRow? row)
        {
            PhaseVariableEvidenceBlocks.Clear();
            PhaseVariableEvidenceRouteCards.Clear();

            if (row == null)
            {
                PhaseVariableEvidenceSummary =
                    Strings.F2_select_a_variable_to_see_the_blocks_link_ddb99f6d;
                return;
            }

            if (selectedScript == null || !selectedScript.HasScript)
            {
                PhaseVariableEvidenceSummary = Strings.F2_no_aifile_loaded_to_audit_the_blocks_of__330f30f1;
                return;
            }

            if (!TryGetRuntimeVariableIndex(row, out ushort runtimeIndex))
            {
                PhaseVariableEvidenceSummary = string.Format(Strings.U_Ai_PrIndexResolveFailed, row.VariableName);
                return;
            }

            List<AiPhaseVarLinkedBlockVm> blocks = BuildPhaseVariableEvidenceBlocks(row, runtimeIndex);
            foreach (AiPhaseVarLinkedBlockVm block in blocks)
                PhaseVariableEvidenceBlocks.Add(block);

            string overview = $"Panorama bruto: {row.EvidenceSummary}. Encadeamento bruto: {row.FlowSummary}.";
            PhaseVariableEvidenceSummary = blocks.Count == 0
                ? $"{row.VariableName}: nenhum bloco com comando/buff/stat/mutacao ficou legivel pela leitura atual. Isso costuma indicar guarda avancada, salto indireto ou fluxo so de controle. Ainda assim, a var continua sendo memoria de estado consultada quando os hooks entram. {overview}"
                : $"{row.VariableName}: {blocks.Count} bloco(s) ligados a esta var na leitura read-only. O card abaixo nao altera writer nem receita; ele so expoe o que a fase esta realmente arrastando junto. Leia como memoria de estado: guard = leitura, advance/reset = escrita. {overview}";

            RebuildVarEvidenceRouteCards(row, blocks);
        }

        List<AiPhaseVarLinkedBlockVm> BuildPhaseVariableEvidenceBlocks(AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            var contextsByOffset = BuildPhaseVariableEvidenceContexts();
            if (selectedScript == null)
                return new List<AiPhaseVarLinkedBlockVm>();

            var buckets = new Dictionary<string, PhaseVariableEvidenceAccumulator>(StringComparer.OrdinalIgnoreCase);

            foreach (AiDetectedAction action in AiAutomation.DetectActions(selectedScript))
            {
                if (!contextsByOffset.TryGetValue(action.CallOffset, out List<PhaseEvidenceContext>? contexts))
                    continue;

                foreach (PhaseEvidenceContext context in contexts)
                {
                    if (!GuardMentionsPhaseVariable(context.GuardLabel, row, runtimeIndex))
                        continue;

                    PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(buckets, context);
                    bucket.Effects.Add(DescribePhaseEvidenceAction(action));
                    bucket.Offsets.Add(action.CallOffset);
                }
            }

            IReadOnlyList<AiInstruction> instructions = selectedScript.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (!TryReadPhaseVarMutation(instructions, i, out PhaseVarAdvanceSnapshot advance, out int consumed))
                    continue;

                int offset = instructions[i].Offset;
                if (contextsByOffset.TryGetValue(offset, out List<PhaseEvidenceContext>? contexts))
                {
                    foreach (PhaseEvidenceContext context in contexts)
                    {
                        if (!GuardMentionsPhaseVariable(context.GuardLabel, row, runtimeIndex))
                            continue;

                        PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(buckets, context);
                        bucket.Writes.Add(DescribePhaseAdvanceEvidence(advance));
                        bucket.Offsets.Add(offset);
                    }
                }

                i += Math.Max(0, consumed - 1);
            }

            AppendPhaseVariableControlFlowEvidence(buckets, contextsByOffset, row, runtimeIndex, instructions);

            if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
            {
                try
                {
                    byte[] monsterBin = File.ReadAllBytes(selectedPath);
                    foreach (AiDetectedBranchAction branch in AiAutomation.DetectBranchSensitiveActions(monsterBin, selectedScript))
                    {
                        if (!BranchActionMentionsPhaseVariable(branch, row, runtimeIndex))
                            continue;

                        string hookLabel = BranchHookLabel(branch.HookKind);
                        string guardLabel = string.IsNullOrWhiteSpace(branch.GuardSummary) ? "payload indireto" : branch.GuardSummary;
                        PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(
                            buckets,
                            new PhaseEvidenceContext(hookLabel, guardLabel));
                        bucket.Effects.Add($"{branch.CommandSummary} em {branch.TargetSummary}");
                        if (TextMentionsPhaseVariable(branch.CommandProvenance, row, runtimeIndex))
                            bucket.Consumers.Add($"slot cmd: {branch.CommandProvenance} -> {branch.CommandSummary}");
                        if (TextMentionsPhaseVariable(branch.TargetProvenance, row, runtimeIndex))
                            bucket.Consumers.Add($"slot alvo: {branch.TargetProvenance} -> {branch.TargetSummary}");
                        if (TextMentionsPhaseVariable(branch.GuardSummary, row, runtimeIndex))
                            bucket.Notes.Add($"branch: {branch.GuardSummary}");
                        string semanticNote = DescribeBranchSemanticNotes(branch.HighLevelHints);
                        if (!string.IsNullOrWhiteSpace(semanticNote))
                            bucket.Notes.Add(semanticNote);
                        foreach (string hint in branch.HighLevelHints)
                            bucket.Notes.Add($"label: {hint}");
                        bucket.Offsets.Add(branch.CallOffset);
                    }

                    foreach (AiIndirectDispatchUnit unit in DetectPromotedIndirectDispatchUnits(monsterBin))
                    {
                        if (!IndirectDispatchUnitMentionsPhaseVariable(unit, row, runtimeIndex))
                            continue;

                        PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(
                            buckets,
                            new PhaseEvidenceContext(BranchHookLabel(unit.HookKind), unit.GuardSummary));
                        foreach (AiIndirectDispatchWrite write in unit.PayloadWrites)
                            bucket.Writes.Add($"{write.VariableName} = {write.ValueSummary} ({write.RoleSummary})");
                        foreach (AiIndirectDispatchConsumer consumer in unit.Consumers)
                            bucket.Consumers.Add($"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName}");
                        if (!string.IsNullOrWhiteSpace(unit.NextStateSummary))
                            bucket.NextStates.Add(unit.NextStateSummary);
                        foreach (string effect in unit.CompanionEffects)
                            bucket.Notes.Add(effect);
                        bucket.Effects.Add($"dispatch indireto [{unit.CapabilityLabel}]");
                        bucket.TryAddIndirectDispatchUnit(BuildIndirectDispatchLinkVm(unit, row, runtimeIndex));
                        if (unit.Consumers.Count > 0)
                            bucket.Offsets.Add(unit.Consumers[0].CallOffset);
                    }
                }
                catch
                {
                    // read-only helper only; if the file read fails, keep the direct evidence that already exists
                }
            }

            return buckets.Values
                .OrderBy(b => b.SortKey)
                .Select(b => new AiPhaseVarLinkedBlockVm(
                    b.HookLabel,
                    b.Badge,
                    b.GuardLabel,
                    string.Join("; ", b.Effects.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    b.WriteSummary,
                    b.ConsumerSummary,
                    b.NextStateSummary,
                    b.NoteSummary,
                    b.OffsetSummary,
                    b.TechnicalSummary,
                    b.IndirectDispatchUnits))
                .ToList();
        }

        void AppendPhaseVariableControlFlowEvidence(
            Dictionary<string, PhaseVariableEvidenceAccumulator> buckets,
            Dictionary<int, List<PhaseEvidenceContext>> contextsByOffset,
            AiPhaseVariableAuditRow row,
            ushort runtimeIndex,
            IReadOnlyList<AiInstruction> instructions)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode != 0x9F || instruction.Operand != runtimeIndex)
                    continue;

                PhaseLoadUse use = ClassifyLoadUse(instructions, i);
                if (!use.HasCondition)
                    continue;

                string effectSummary = DescribePhaseVariableControlFlow(row, instructions, i, use);
                int offset = instruction.Offset;
                if (contextsByOffset.TryGetValue(offset, out List<PhaseEvidenceContext>? contexts) && contexts.Count > 0)
                {
                    foreach (PhaseEvidenceContext context in contexts)
                    {
                        string guardLabel = string.IsNullOrWhiteSpace(context.GuardLabel)
                            ? effectSummary
                            : context.GuardLabel;
                        PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(
                            buckets,
                            new PhaseEvidenceContext(context.HookLabel, guardLabel));
                        bucket.Notes.Add(effectSummary);
                        bucket.Offsets.Add(offset);
                    }
                }
                else
                {
                    PhaseVariableEvidenceAccumulator bucket = GetOrCreateEvidenceBucket(
                        buckets,
                        new PhaseEvidenceContext("Fluxo estrutural", effectSummary));
                    bucket.Notes.Add(effectSummary);
                    bucket.Offsets.Add(offset);
                }
            }
        }

        static string DescribePhaseVariableControlFlow(
            AiPhaseVariableAuditRow row,
            IReadOnlyList<AiInstruction> instructions,
            int loadIndex,
            PhaseLoadUse use)
        {
            string name = row.VariableName;
            int hi = Math.Min(instructions.Count - 1, loadIndex + 8);

            for (int i = loadIndex + 1; i <= hi; i++)
            {
                if (instructions[i].Opcode == 0x2C)
                    return $"fluxo: switch/dispatch por {name}";
            }

            if (WindowHasFieldCallOrLiteral(instructions, loadIndex + 1, hi, 0x001C))
                return string.Format(Strings.U_Ai_PrFlowArea, name);

            if (WindowHasCall(instructions, loadIndex + 1, hi, 0x7038) && WindowHasLiteral(instructions, loadIndex + 1, hi, 0x3105))
                return $"fluxo: {name} participa de removeCommand(Talk)";

            if (WindowHasLiteral(instructions, loadIndex + 1, hi, 0x6001))
                return $"fluxo: {name} participa de handoff para Special 1";

            if (WindowHasLiteral(instructions, loadIndex + 1, hi, 0x6051))
                return $"fluxo: {name} participa do beat 0x6051 (dismiss da Anima)";

            for (int i = loadIndex + 1; i <= hi; i++)
            {
                if (!AiVarConditionBuilder.TryGetComparisonOperator(instructions[i].Opcode, false, out AiVarCompareOperator comparison))
                    continue;

                if (AiVarConditionBuilder.TryReadImmediateComparison(instructions, i - 2, out AiVarConditionClause clause)
                    && (loadIndex == i - 2 || loadIndex == i - 1))
                    return string.Format(Strings.U_Ai_PrCondOp1, name,
                        AiVarConditionBuilder.OperatorLabel(clause.Operator), clause.Value);

                if (i - 2 >= 0 &&
                    instructions[i - 1].Opcode == 0x29 &&
                    instructions[i - 2].Opcode == 0xAE &&
                    AiVarConditionBuilder.TryGetComparisonOperator(instructions[i].Opcode, true, out comparison))
                    return string.Format(Strings.U_Ai_PrCondOp2, name,
                        AiVarConditionBuilder.OperatorLabel(comparison), unchecked((short)instructions[i - 2].Operand));

                return string.Format(Strings.U_Ai_PrCondScript, name);
            }

            bool hasBranch = false;
            for (int i = loadIndex + 1; i <= hi; i++)
            {
                if (instructions[i].Opcode is 0xD6 or 0xD7 or 0xB0)
                {
                    hasBranch = true;
                    break;
                }
            }

            if (use.HasSwitch)
                return $"fluxo: {name} participa de switch/dispatch";
            if (use.HasCompare)
                return string.Format(Strings.U_Ai_PrCondAdvanced, name);
            if (hasBranch)
                return $"fluxo: guarda estrutural usando {name}";
            return $"fluxo: leitura de controle usando {name}";
        }

        static bool WindowHasCall(IReadOnlyList<AiInstruction> instructions, int start, int end, ushort operand)
        {
            for (int i = start; i <= end; i++)
            {
                AiInstruction instruction = instructions[i];
                if ((instruction.Opcode == 0xB5 || instruction.Opcode == 0xD8) && instruction.Operand == operand)
                    return true;
            }

            return false;
        }

        static bool WindowHasLiteral(IReadOnlyList<AiInstruction> instructions, int start, int end, ushort operand)
        {
            for (int i = start; i <= end; i++)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode == 0xAE && instruction.Operand == operand)
                    return true;
            }

            return false;
        }

        static bool WindowHasFieldCallOrLiteral(IReadOnlyList<AiInstruction> instructions, int start, int end, ushort fieldOperand)
        {
            if (WindowHasLiteral(instructions, start, end, fieldOperand))
                return true;

            for (int i = start; i <= end; i++)
            {
                AiInstruction instruction = instructions[i];
                if ((instruction.Opcode == 0xB5 || instruction.Opcode == 0xD8)
                    && instruction.Operand is 0x700F or 0x7018 or 0x70AA or 0x70AB)
                {
                    if (i - 1 >= 0 && instructions[i - 1].Opcode == 0xAE && instructions[i - 1].Operand == fieldOperand)
                        return true;
                    if (i - 2 >= 0 && instructions[i - 2].Opcode == 0xAE && instructions[i - 2].Operand == fieldOperand)
                        return true;
                }
            }

            return false;
        }

        Dictionary<int, List<PhaseEvidenceContext>> BuildPhaseVariableEvidenceContexts()
        {
            var contextsByOffset = new Dictionary<int, List<PhaseEvidenceContext>>();
            if (selectedScript == null)
                return contextsByOffset;

            void Merge(AiEventHook hook, string hookLabel)
            {
                foreach ((int offset, RunPathContext context) in BuildReachability(hook))
                {
                    if (!contextsByOffset.TryGetValue(offset, out List<PhaseEvidenceContext>? list))
                    {
                        list = new List<PhaseEvidenceContext>();
                        contextsByOffset[offset] = list;
                    }

                    if (!list.Any(existing =>
                            existing.HookLabel.Equals(hookLabel, StringComparison.OrdinalIgnoreCase) &&
                            existing.GuardLabel.Equals(context.GuardLabel, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new PhaseEvidenceContext(hookLabel, context.GuardLabel));
                    }
                }
            }

            bool hasOnTurnHook = TryResolveOnTurnHook(out AiEventHook resolvedOnTurnHook, out _);
            bool hasOnHitHook = TryResolveOnHitHook(out AiEventHook resolvedOnHitHook, out _);
            if (hasOnTurnHook)
                Merge(resolvedOnTurnHook, Strings.F2_on_its_turn_b92a95f8);
            if (hasOnHitHook)
                Merge(resolvedOnHitHook, Strings.F2_when_hit_faa494f4);

            foreach (AiWorker worker in selectedScript.Workers)
            {
                for (int entrypointIndex = 0; entrypointIndex < worker.Entrypoints.Count; entrypointIndex++)
                {
                    if (hasOnTurnHook &&
                        resolvedOnTurnHook.WorkerIndex == worker.Index &&
                        resolvedOnTurnHook.EntrypointIndex == entrypointIndex)
                        continue;
                    if (hasOnHitHook &&
                        resolvedOnHitHook.WorkerIndex == worker.Index &&
                        resolvedOnHitHook.EntrypointIndex == entrypointIndex)
                        continue;

                    string hookLabel = entrypointIndex == 0
                        ? Strings.U_Ai_PrBattleStartLab
                        : string.Format(Strings.U_Ai_PrAuxEvent, HumanEntrypointLabel(entrypointIndex));
                    Merge(new AiEventHook(worker.Index, entrypointIndex), hookLabel);
                }
            }

            return contextsByOffset;
        }

        static PhaseVariableEvidenceAccumulator GetOrCreateEvidenceBucket(
            Dictionary<string, PhaseVariableEvidenceAccumulator> buckets,
            PhaseEvidenceContext context)
        {
            string key = $"{context.HookLabel}|{context.GuardLabel}";
            if (buckets.TryGetValue(key, out PhaseVariableEvidenceAccumulator? existing))
                return existing;

            var created = new PhaseVariableEvidenceAccumulator(context.HookLabel, context.GuardLabel);
            buckets[key] = created;
            return created;
        }

        bool GuardMentionsPhaseVariable(string guardLabel, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            if (string.IsNullOrWhiteSpace(guardLabel))
                return false;

            return TextMentionsPhaseVariable(guardLabel, row, runtimeIndex);
        }

        static string DescribePhaseEvidenceAction(AiDetectedAction action) => action.Kind switch
        {
            AiActionKind.Command => string.Format(Strings.U_Ai_PrSkill, DescribePhaseCommandMeaning(action.CommandOperand, action.AbilityName)),
            AiActionKind.Buff => $"status: {action.AbilityName}",
            AiActionKind.Stat => $"stat: {action.AbilityName}",
            _ => action.AbilityName,
        };

        string DescribePhaseAdvanceEvidence(PhaseVarAdvanceSnapshot advance)
        {
            string variableName = FindPhaseVariableByKey(advance.VariableKey)?.VariableName ?? advance.VariableKey ?? "var";
            if (advance.IsReset)
                return string.Format(Strings.U_Ai_PrVarEquals, variableName);

            return advance.MinText == advance.MaxText
                ? string.Format(Strings.U_Ai_PrVarPlus, variableName, advance.MinText)
                : string.Format(Strings.U_Ai_PrVarPlusRange, variableName, advance.MinText, advance.MaxText);
        }

        bool BranchActionMentionsPhaseVariable(AiDetectedBranchAction branch, AiPhaseVariableAuditRow row, ushort runtimeIndex) =>
            TextMentionsPhaseVariable(branch.GuardSummary, row, runtimeIndex)
            || TextMentionsPhaseVariable(branch.CommandProvenance, row, runtimeIndex)
            || TextMentionsPhaseVariable(branch.TargetProvenance, row, runtimeIndex)
            || TextMentionsPhaseVariable(branch.CommandSummary, row, runtimeIndex)
            || TextMentionsPhaseVariable(branch.TargetSummary, row, runtimeIndex);

        static bool TextMentionsPhaseVariable(string? text, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains(row.RawVariableName, StringComparison.OrdinalIgnoreCase)
                || text.Contains(row.VariableName, StringComparison.OrdinalIgnoreCase)
                || text.Contains(row.IndexLabel, StringComparison.OrdinalIgnoreCase)
                || text.Contains($"var[{runtimeIndex}]", StringComparison.OrdinalIgnoreCase)
                || text.Contains($"0x{runtimeIndex:X4}", StringComparison.OrdinalIgnoreCase);
        }

        static string BranchHookLabel(string hookKind) => hookKind switch
        {
            "onTurn real" => Strings.F2_on_its_turn_b92a95f8,
            "onHit real" => Strings.F2_when_hit_faa494f4,
            "evento auxiliar" => "Evento auxiliar contextual",
            "init/evento auxiliar" => "Init / evento auxiliar",
            _ => hookKind,
        };

        string DescribeBranchEvidenceAction(AiDetectedBranchAction branch, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            bool commandSlot = TextMentionsPhaseVariable(branch.CommandProvenance, row, runtimeIndex);
            bool targetSlot = TextMentionsPhaseVariable(branch.TargetProvenance, row, runtimeIndex);
            bool guardSlot = TextMentionsPhaseVariable(branch.GuardSummary, row, runtimeIndex);

            string prefix = commandSlot && targetSlot
                ? "slot cmd+alvo"
                : commandSlot
                    ? "slot cmd"
                    : targetSlot
                        ? "slot alvo"
                        : guardSlot
                            ? "guard"
                            : "branch";

            string summary = $"{prefix}: {branch.CommandSummary} em {branch.TargetSummary}";
            List<string> notes = new();
            if (!string.IsNullOrWhiteSpace(branch.CommandProvenance) && commandSlot)
                notes.Add($"cmd {branch.CommandProvenance}");
            if (!string.IsNullOrWhiteSpace(branch.TargetProvenance) && targetSlot)
                notes.Add($"alvo {branch.TargetProvenance}");
            string semanticNote = DescribeBranchSemanticNotes(branch.HighLevelHints);
            if (!string.IsNullOrWhiteSpace(semanticNote))
                notes.Add(semanticNote);
            if (branch.HighLevelHints.Count > 0)
                notes.Add(string.Join(", ", branch.HighLevelHints));

            return notes.Count == 0 ? summary : $"{summary} ({string.Join(" | ", notes)})";
        }

        static string DescribePhaseCommandMeaning(ushort operand, string fallback) => operand switch
        {
            0x6001 => Strings.U_Ai_PrSpecial1Handoff,
            0x6051 => "Seymour dismisses Anima! (beat visual / row normal via performCommand)",
            _ => fallback,
        };

        static string DescribeBranchSemanticNotes(IReadOnlyList<string> hints)
        {
            List<string> notes = new();
            if (hints.Contains(Strings.U_Ai_PrNotesHandoff, StringComparer.OrdinalIgnoreCase))
                notes.Add(Strings.U_Ai_PrNotesHandoff);
            if (hints.Contains("corte contextual/scriptado", StringComparer.OrdinalIgnoreCase))
                notes.Add("runBtlSceneA");
            if (hints.Contains(Strings.U_Ai_PresentationHandoff, StringComparer.OrdinalIgnoreCase))
                notes.Add("runBtlSceneB");
            if (hints.Contains("damage-gated transition", StringComparer.OrdinalIgnoreCase))
                notes.Add(Strings.U_Ai_PrDamageTransition);
            return string.Join(", ", notes);
        }

        bool IndirectDispatchUnitMentionsPhaseVariable(AiIndirectDispatchUnit unit, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            if (TextMentionsPhaseVariable(unit.GuardSummary, row, runtimeIndex)
                || TextMentionsPhaseVariable(unit.NextStateSummary, row, runtimeIndex))
                return true;

            if (unit.PayloadWrites.Any(write =>
                    TextMentionsPhaseVariable(write.VariableName, row, runtimeIndex)
                    || TextMentionsPhaseVariable($"var[{write.VariableIndex}]", row, runtimeIndex)
                    || TextMentionsPhaseVariable($"0x{write.VariableIndex:X4}", row, runtimeIndex)))
            {
                return true;
            }

            if (unit.Consumers.Any(consumer =>
                    TextMentionsPhaseVariable(consumer.CommandVariableName, row, runtimeIndex)
                    || TextMentionsPhaseVariable(consumer.TargetVariableName, row, runtimeIndex)
                    || TextMentionsPhaseVariable($"var[{consumer.CommandVariableIndex}]", row, runtimeIndex)
                    || TextMentionsPhaseVariable($"var[{consumer.TargetVariableIndex}]", row, runtimeIndex)))
            {
                return true;
            }

            return unit.CompanionEffects.Any(effect => TextMentionsPhaseVariable(effect, row, runtimeIndex));
        }

        string DescribeIndirectDispatchUnitEvidence(AiIndirectDispatchUnit unit, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            bool payloadVar = unit.PayloadWrites.Any(write =>
                TextMentionsPhaseVariable(write.VariableName, row, runtimeIndex)
                || TextMentionsPhaseVariable($"var[{write.VariableIndex}]", row, runtimeIndex));
            bool commandConsumer = unit.Consumers.Any(consumer =>
                TextMentionsPhaseVariable(consumer.CommandVariableName, row, runtimeIndex)
                || TextMentionsPhaseVariable($"var[{consumer.CommandVariableIndex}]", row, runtimeIndex));
            bool targetConsumer = unit.Consumers.Any(consumer =>
                TextMentionsPhaseVariable(consumer.TargetVariableName, row, runtimeIndex)
                || TextMentionsPhaseVariable($"var[{consumer.TargetVariableIndex}]", row, runtimeIndex));

            string prefix = payloadVar && (commandConsumer || targetConsumer)
                ? "dispatch indireto"
                : payloadVar
                    ? "payload indireto"
                    : commandConsumer
                        ? "consumer cmd"
                        : targetConsumer
                            ? "consumer alvo"
                            : "dispatch indireto";

            string payloadSummary = string.Join(", ", unit.PayloadWrites.Select(write =>
                $"{write.VariableName}={write.ValueSummary}"));
            string consumerSummary = string.Join(", ", unit.Consumers.Select(consumer =>
                $"{consumer.Label}: {consumer.CommandVariableName}+{consumer.TargetVariableName}"));
            return $"{prefix}: {unit.GuardSummary} -> {unit.NextStateSummary} ({payloadSummary}) [consumers: {consumerSummary}] [{unit.CapabilityLabel}]";
        }

        AiPhaseVarIndirectDispatchLinkVm BuildIndirectDispatchLinkVm(AiIndirectDispatchUnit unit, AiPhaseVariableAuditRow row, ushort runtimeIndex)
        {
            bool guardRole = TextMentionsPhaseVariable(unit.GuardSummary, row, runtimeIndex);
            bool nextStateRole = TextMentionsPhaseVariable(unit.NextStateSummary, row, runtimeIndex);

            List<string> roles = new();
            if (guardRole)
                roles.Add(Strings.U_Ai_PaPhaseGuard);

            foreach (AiIndirectDispatchWrite write in unit.PayloadWrites.Where(write =>
                         TextMentionsPhaseVariable(write.VariableName, row, runtimeIndex)
                         || TextMentionsPhaseVariable($"var[{write.VariableIndex}]", row, runtimeIndex)))
            {
                roles.Add(write.RoleSummary);
            }

            if (unit.Consumers.Any(consumer =>
                    TextMentionsPhaseVariable(consumer.CommandVariableName, row, runtimeIndex)
                    || TextMentionsPhaseVariable($"var[{consumer.CommandVariableIndex}]", row, runtimeIndex)))
            {
                roles.Add(Strings.U_Ai_PrCommandConsumer);
            }

            if (unit.Consumers.Any(consumer =>
                    TextMentionsPhaseVariable(consumer.TargetVariableName, row, runtimeIndex)
                    || TextMentionsPhaseVariable($"var[{consumer.TargetVariableIndex}]", row, runtimeIndex)))
            {
                roles.Add(Strings.U_Ai_PrTargetConsumer);
            }

            if (nextStateRole)
                roles.Add(Strings.U_Ai_PrNextState);

            if (unit.CompanionEffects.Any(effect => TextMentionsPhaseVariable(effect, row, runtimeIndex)))
                roles.Add(Strings.U_Ai_PrStructuralNote);

            string payloadSummary = string.Join("; ", unit.PayloadWrites.Select(write =>
                $"{write.RoleSummary}: {write.ValueSummary}"));
            string consumerSummary = string.Join("; ", unit.Consumers.Select(consumer =>
                $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName}"));
            string noteSummary = string.Join("; ", unit.CompanionEffects
                .Where(effect => !string.IsNullOrWhiteSpace(effect))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(effect => effect, StringComparer.OrdinalIgnoreCase));

            return new AiPhaseVarIndirectDispatchLinkVm(
                unit.UnitId,
                $"rota {unit.UnitIndex}",
                string.Join(" · ", roles.Distinct(StringComparer.OrdinalIgnoreCase)),
                payloadSummary,
                consumerSummary,
                unit.NextStateSummary,
                noteSummary,
                unit.CapabilityLabel,
                unit.GuardSummary,
                unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                && (unit.EditableSlots.Count > 0
                    || unit.EditableTargetSlots.Any(slot => slot.CanEdit)
                    || unit.EditableNextStateOffset.HasValue));
        }

        bool TryReadPhaseDraftSnapshotFromScript(ushort counterIndex, string counterKey, out PhaseDraftSnapshot snapshot)
        {
            snapshot = new PhaseDraftSnapshot(
                counterKey,
                "1",
                "3",
                "4",
                true,
                Array.Empty<PhaseConditionClauseSnapshot>(),
                Array.Empty<PhaseDraftStepSnapshot>());

            if (selectedScript == null)
                return false;

            var found = new List<DetectedPhaseDraftBlock>();
            IReadOnlyList<AiInstruction> ins = selectedScript.Instructions;
            int hookWorker = -1;
            if (TryResolveAuthoringHook(out AiEventHook hook, out _, out _))
                hookWorker = hook.WorkerIndex;

            for (int callIndex = 1; callIndex < ins.Count; callIndex++)
            {
                AiInstruction call = ins[callIndex];
                if (call.Opcode != 0xD8 || (call.Operand != 0x700B && call.Operand != 0x705A))
                    continue;
                if (hookWorker >= 0 && AiAutomation.OwningWorkerIndex(selectedScript, call.Offset) != hookWorker)
                    continue;

                int cmdIndex = callIndex - 1;
                if (ins[cmdIndex].Opcode != 0xAE || !AiCommandId.IsCommandOperand(ins[cmdIndex].Operand))
                    continue;
                if (!TryFindPhaseGuardBeforeCommand(ins, cmdIndex, out int guardStart, out int branchIndex))
                    continue;

                IReadOnlyList<AiInstruction> guard = ins.Skip(guardStart).Take(branchIndex - guardStart).Select(ClonePhaseInstruction).ToList();
                if (!GuardMentionsVariable(guard, counterIndex))
                    continue;
                if (!TryParsePhaseGuard(guard, counterIndex, out bool isFinal, out int number, out int finalLimit, out bool useChance, out int chanceK, out string conditionSummary))
                    continue;
                if (!TryReadPhaseTargetBeforeCommand(ins, cmdIndex, out PhaseTargetSnapshot target))
                    continue;
                if (!TryReadPhaseActionTail(ins, callIndex + 1, target, out bool stopHere, out PhaseForbiddenRiteSnapshot? forbiddenRite, out IReadOnlyList<PhaseVarAdvanceSnapshot> advances))
                    continue;

                found.Add(new DetectedPhaseDraftBlock(
                    ins[guardStart].Offset,
                    isFinal,
                    number,
                    finalLimit,
                    ins[cmdIndex].Operand,
                    TryResolvePhaseTriggerKindForCallOffset(ins[callIndex].Offset, out AiPhaseTriggerKind triggerKind)
                        ? triggerKind
                        : AiPhaseTriggerKind.OnTurn,
                    target,
                    stopHere,
                    useChance,
                    chanceK,
                    conditionSummary,
                    guard,
                    forbiddenRite,
                    advances));
            }

            if (found.Count == 0)
                return false;

            List<DetectedPhaseDraftBlock> common = found
                .Where(f => !f.IsFinal)
                .OrderBy(f => f.Offset)
                .ToList();
            DetectedPhaseDraftBlock? final = found
                .Where(f => f.IsFinal)
                .OrderBy(f => f.Offset)
                .FirstOrDefault();

            var steps = new List<PhaseDraftStepSnapshot>();
            foreach (DetectedPhaseDraftBlock item in common)
                steps.Add(item.ToSnapshot());
            if (final != null)
                steps.Add(final.ToSnapshot());

            if (steps.Count == 0)
                return false;

            string defaultAdvanceKey = steps
                .SelectMany(s => s.Advances)
                .Select(a => a.VariableKey)
                .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k))
                ?? counterKey;
            string finalLimitText = final?.FinalLimit.ToString() ?? Math.Max(4, common.Count).ToString();

            snapshot = new PhaseDraftSnapshot(
                defaultAdvanceKey,
                "1",
                "3",
                finalLimitText,
                true,
                Array.Empty<PhaseConditionClauseSnapshot>(),
                steps);
            return true;
        }

        static bool TryFindPhaseGuardBeforeCommand(
            IReadOnlyList<AiInstruction> ins,
            int commandPushIndex,
            out int guardStart,
            out int branchIndex)
        {
            guardStart = -1;
            branchIndex = -1;
            int lo = Math.Max(0, commandPushIndex - 32);
            for (int i = commandPushIndex - 1; i >= lo; i--)
            {
                if (ins[i].Opcode != 0xD7)
                    continue;

                branchIndex = i;
                // Generated numeric HP guards can exceed 24 instructions; retain
                // the whole bounded block so reopening cannot lose the counter.
                guardStart = Math.Max(0, i - 128);
                for (int j = i - 1; j >= guardStart; j--)
                {
                    if (ins[j].Opcode is 0x3C or 0xB0 or 0xD6 or 0xD7)
                    {
                        guardStart = j + 1;
                        break;
                    }
                }
                return guardStart < branchIndex;
            }
            return false;
        }

        static bool GuardMentionsVariable(IReadOnlyList<AiInstruction> guard, ushort variableIndex) =>
            guard.Any(i => i.Opcode == 0x9F && i.Operand == variableIndex);

        static bool TryParsePhaseGuard(
            IReadOnlyList<AiInstruction> guard,
            ushort counterIndex,
            out bool isFinal,
            out int number,
            out int finalLimit,
            out bool useChance,
            out int chanceK,
            out string conditionSummary)
        {
            isFinal = false;
            number = 1;
            finalLimit = 4;
            useChance = false;
            chanceK = 2;
            conditionSummary = string.Empty;

            List<AiInstruction> core = guard.ToList();
            if (core.Count >= 9 &&
                core[^6].Opcode == 0xB5 && core[^6].Operand == 0x00A9 &&
                core[^5].Opcode == 0xAE &&
                core[^4].Opcode == 0x18 &&
                core[^3].Opcode == 0xAE && core[^3].Operand == 0 &&
                core[^2].Opcode == 0x06 &&
                core[^1].Opcode == 0x02)
            {
                useChance = true;
                chanceK = Math.Max(2, (int)core[^5].Operand);
                core = core.Take(core.Count - 6).ToList();
            }

            if (core.Count == 3 &&
                AiVarConditionBuilder.TryReadImmediateComparison(core, 0, out AiVarConditionClause clause) &&
                clause.VariableIndex == counterIndex && clause.Value >= 0)
            {
                if (clause.Operator == AiVarCompareOperator.Equal)
                {
                    number = clause.Value + 1;
                    return true;
                }
                if (clause.Operator == AiVarCompareOperator.GreaterThan)
                {
                    isFinal = true;
                    number = int.MaxValue;
                    finalLimit = clause.Value;
                    return true;
                }
            }

            int firstCompare = core.FindIndex(i => AiVarConditionBuilder.IsComparisonOpcode(i.Opcode));
            conditionSummary = firstCompare >= 0
                ? Strings.F2_condition_detected_in_bytecode_4a4f9a74
                : Strings.U_Ai_PrAdvancedGuard;
            number = 9000 + Math.Max(0, guard.FirstOrDefault()?.Offset ?? 0);
            return true;
        }

        bool TryReadPhaseTargetBeforeCommand(
            IReadOnlyList<AiInstruction> ins,
            int commandPushIndex,
            out PhaseTargetSnapshot target)
        {
            target = new PhaseTargetSnapshot(0xFFF3, AiTargetRecipeKind.Literal);

            if (commandPushIndex >= 1 && ins[commandPushIndex - 1].Opcode == 0xAE)
            {
                target = new PhaseTargetSnapshot(ins[commandPushIndex - 1].Operand, AiTargetRecipeKind.Literal);
                return true;
            }

            if (commandPushIndex >= 5 &&
                ins[commandPushIndex - 5].Opcode == 0xAE && ins[commandPushIndex - 5].Operand == 0xFFF2 &&
                ins[commandPushIndex - 4].Opcode == 0xAE && ins[commandPushIndex - 4].Operand == 0x0004 &&
                ins[commandPushIndex - 3].Opcode == 0xAE && ins[commandPushIndex - 3].Operand == 0 &&
                ins[commandPushIndex - 2].Opcode == 0xAE && ins[commandPushIndex - 2].Operand == 0 &&
                ins[commandPushIndex - 1].Opcode == 0xB5 && ins[commandPushIndex - 1].Operand == 0x7010)
            {
                target = new PhaseTargetSnapshot(0, AiTargetRecipeKind.FindAliveFrontlineAny);
                return true;
            }

            if (commandPushIndex >= 10 &&
                ins[commandPushIndex - 10].Opcode == 0xAE && ins[commandPushIndex - 10].Operand == 0xFFF2 &&
                ins[commandPushIndex - 9].Opcode == 0xAE && ins[commandPushIndex - 9].Operand == 0x0004 &&
                ins[commandPushIndex - 8].Opcode == 0xAE && ins[commandPushIndex - 8].Operand == 0 &&
                ins[commandPushIndex - 7].Opcode == 0xAE && ins[commandPushIndex - 7].Operand == 0 &&
                ins[commandPushIndex - 6].Opcode == 0xD8 && ins[commandPushIndex - 6].Operand == 0x7010 &&
                ins[commandPushIndex - 5].Opcode == 0xAE && ins[commandPushIndex - 5].Operand == 0xFFF0 &&
                ins[commandPushIndex - 4].Opcode == 0xAE && ins[commandPushIndex - 4].Operand == 0 &&
                ins[commandPushIndex - 3].Opcode == 0xAE && ins[commandPushIndex - 3].Operand == 0 &&
                ins[commandPushIndex - 2].Opcode == 0xAE && ins[commandPushIndex - 2].Operand == 2 &&
                ins[commandPushIndex - 1].Opcode == 0xB5 && ins[commandPushIndex - 1].Operand == 0x7010)
            {
                target = new PhaseTargetSnapshot(0, AiTargetRecipeKind.FindAliveFrontlineLowestHp);
                return true;
            }

            return false;
        }

        bool TryReadPhaseActionTail(
            IReadOnlyList<AiInstruction> ins,
            int start,
            PhaseTargetSnapshot phaseTarget,
            out bool stopHere,
            out PhaseForbiddenRiteSnapshot? forbiddenRite,
            out IReadOnlyList<PhaseVarAdvanceSnapshot> advances)
        {
            stopHere = true;
            forbiddenRite = null;
            var list = new List<PhaseVarAdvanceSnapshot>();
            int i = start;
            while (i < ins.Count && ins[i].Opcode is not (0x3C or 0xB0))
            {
                if (TryReadPhaseForbiddenRite(ins, i, phaseTarget, out PhaseForbiddenRiteSnapshot? rite, out int riteConsumed))
                {
                    forbiddenRite ??= rite;
                    i += riteConsumed;
                    continue;
                }
                if (TryReadPhaseVarMutation(ins, i, out PhaseVarAdvanceSnapshot advance, out int consumed))
                {
                    list.Add(advance);
                    i += consumed;
                    continue;
                }
                i++;
            }

            if (i >= ins.Count)
            {
                advances = list;
                return false;
            }

            stopHere = ins[i].Opcode == 0x3C;
            advances = list;
            return true;
        }

        bool TryReadPhaseForbiddenRite(
            IReadOnlyList<AiInstruction> ins,
            int start,
            PhaseTargetSnapshot phaseTarget,
            out PhaseForbiddenRiteSnapshot? forbiddenRite,
            out int consumed)
        {
            forbiddenRite = null;
            consumed = 0;
            if (start + 3 >= ins.Count)
                return false;
            if (ins[start + 1].Opcode != 0xAE ||
                ins[start + 2].Opcode != 0xAE ||
                ins[start + 3].Opcode != 0xD8 ||
                ins[start + 3].Operand != 0x7018)
                return false;

            ushort field = ins[start + 1].Operand;
            ushort value = ins[start + 2].Operand;
            bool usePhaseTarget = false;
            PhaseTargetSnapshot? target = null;
            if (ins[start].Opcode == 0xAE)
            {
                usePhaseTarget = phaseTarget.Kind == AiTargetRecipeKind.Literal &&
                                 phaseTarget.Operand == ins[start].Operand;
                target = new PhaseTargetSnapshot(ins[start].Operand, AiTargetRecipeKind.Literal, usePhaseTarget);
            }
            else if (ins[start].Opcode == 0x67)
            {
                usePhaseTarget = true;
                target = new PhaseTargetSnapshot(0, phaseTarget.Kind, true);
            }
            else
            {
                return false;
            }

            forbiddenRite = new PhaseForbiddenRiteSnapshot(target, field, value.ToString());
            consumed = 4;
            return true;
        }

        bool TryReadPhaseVarMutation(
            IReadOnlyList<AiInstruction> ins,
            int start,
            out PhaseVarAdvanceSnapshot advance,
            out int consumed)
        {
            advance = new PhaseVarAdvanceSnapshot(null, "0", "0", false);
            consumed = 0;

            if (start + 1 < ins.Count &&
                ins[start].Opcode == 0xAE &&
                ins[start + 1].Opcode == 0xA0 &&
                TryFindVariableKeyByRuntimeIndex(ins[start + 1].Operand, out string? setKey))
            {
                advance = new PhaseVarAdvanceSnapshot(setKey, ins[start].Operand.ToString(), ins[start].Operand.ToString(), true);
                consumed = 2;
                return true;
            }

            if (ins[start].Opcode != 0x9F)
                return false;
            ushort variableIndex = ins[start].Operand;
            if (!TryFindVariableKeyByRuntimeIndex(variableIndex, out string? key))
                return false;

            if (start + 1 < ins.Count && ins[start + 1].Opcode == 0xA0 && ins[start + 1].Operand == variableIndex)
            {
                advance = new PhaseVarAdvanceSnapshot(key, "0", "0", false);
                consumed = 2;
                return true;
            }

            if (start + 3 < ins.Count &&
                ins[start + 1].Opcode == 0xAE &&
                ins[start + 2].Opcode == 0x14 &&
                ins[start + 3].Opcode == 0xA0 && ins[start + 3].Operand == variableIndex)
            {
                string value = ins[start + 1].Operand.ToString();
                advance = new PhaseVarAdvanceSnapshot(key, value, value, false);
                consumed = 4;
                return true;
            }

            if (start + 5 < ins.Count &&
                ins[start + 1].Opcode == 0xB5 && ins[start + 1].Operand == 0x00A9 &&
                ins[start + 2].Opcode == 0xAE &&
                ins[start + 3].Opcode == 0x18 &&
                ins[start + 4].Opcode == 0x14 &&
                ins[start + 5].Opcode == 0xA0 && ins[start + 5].Operand == variableIndex)
            {
                int max = Math.Max(0, ins[start + 2].Operand - 1);
                advance = new PhaseVarAdvanceSnapshot(key, "0", max.ToString(), false);
                consumed = 6;
                return true;
            }

            if (start + 7 < ins.Count &&
                ins[start + 1].Opcode == 0xB5 && ins[start + 1].Operand == 0x00A9 &&
                ins[start + 2].Opcode == 0xAE &&
                ins[start + 3].Opcode == 0x18 &&
                ins[start + 4].Opcode == 0xAE &&
                ins[start + 5].Opcode == 0x14 &&
                ins[start + 6].Opcode == 0x14 &&
                ins[start + 7].Opcode == 0xA0 && ins[start + 7].Operand == variableIndex)
            {
                int min = ins[start + 4].Operand;
                int max = min + Math.Max(0, ins[start + 2].Operand - 1);
                advance = new PhaseVarAdvanceSnapshot(key, min.ToString(), max.ToString(), false);
                consumed = 8;
                return true;
            }

            return false;
        }

        bool TryFindVariableKeyByRuntimeIndex(ushort variableIndex, out string? key)
        {
            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (TryGetRuntimeVariableIndex(row, out ushort idx) && idx == variableIndex)
                {
                    key = row.AliasKey;
                    return true;
                }
            }

            key = null;
            return false;
        }

        bool TryFindVariableKeyByAuthoringIndex(ushort variableIndex, out string? key)
        {
            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (TryGetPreferredVariableIndex(row, out ushort idx) && idx == variableIndex)
                {
                    key = row.AliasKey;
                    return true;
                }
            }

            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (TryGetRuntimeVariableIndex(row, out ushort idx) && idx == variableIndex)
                {
                    key = row.AliasKey;
                    return true;
                }
            }

            key = null;
            return false;
        }

        void ApplySharedAbilityPickerToSelectedPhase(AiCommandOption? value)
        {
            if (loadingPhaseDraft || value == null)
                return;

            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null || !PhaseDraftSteps.Contains(step))
                return;

            // The phase manager reuses the global ability search. Typing "thu" auto-selects a global ability,
            // but the phase card must not stay empty while the user moves to another card.
            AssignPhaseAbilityToSelectedStep(value, overwriteExisting: step.SelectedAbility == null);
        }

        void AssignPhaseAbilityToSelectedStep(AiCommandOption value, bool overwriteExisting)
        {
            if (loadingPhaseDraft)
                return;

            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null || !PhaseDraftSteps.Contains(step))
                return;

            if (!overwriteExisting && step.SelectedAbility != null)
            {
                UpdateSelectedPhaseAbilityProjection();
                return;
            }

            AiCommandOption resolved = ResolvePhaseAbilityOption(value.Operand) ?? value;
            if (step.SelectedAbility?.Operand == resolved.Operand)
            {
                UpdateSelectedPhaseAbilityProjection();
                return;
            }

            step.SelectedAbility = resolved;
            UpdateSelectedPhaseAbilityProjection();
        }

        AiCommandOption? ResolvePhaseAbilityOption(ushort? operand) =>
            operand == null ? null : AiCommandId.OptionFor(operand.Value);

        AiCommandOption? DefaultPhaseAbilityOption() =>
            SelectedAutomationAbility
            ?? PhaseAbilityOptions.FirstOrDefault()
            ?? AutomationAbilityOptions.FirstOrDefault()
            ?? AiCommandId.AllOptions().FirstOrDefault();

        void SyncPhaseAbilityOptions()
        {
            string f = (AbilitySearch ?? string.Empty).Trim();
            IReadOnlyList<AiCommandOption> all = SelectedAbilityCategory?.Category is AiCommandCategory cat
                ? AiCommandId.OptionsFor(cat)
                : AiCommandId.AllOptions();

            var options = new List<AiCommandOption>();
            foreach (AiCommandOption option in all)
            {
                if (f.Length == 0 || MatchesAbility(option, f))
                    options.Add(option);
            }

            // A busca do dropdown nao pode apagar uma fase ja montada. Mantemos as habilidades
            // usadas no draft visiveis mesmo quando o filtro atual nao bateria nelas.
            foreach (AiCommandOption? option in PhaseDraftSteps
                         .Select(s => s.SelectedAbility)
                         .Append(SelectedAutomationAbility)
                         .Where(o => o != null)
                         .Cast<AiCommandOption>())
            {
                if (!options.Any(o => o.Operand == option.Operand))
                    options.Add(option);
            }

            syncingPhaseAbilityOptions = true;
            try
            {
                PhaseAbilityOptions.Clear();
                foreach (AiCommandOption option in options.OrderBy(o => o.Category).ThenBy(o => o.CommandId))
                    PhaseAbilityOptions.Add(option);

                if (SelectedPhaseDraftStep?.SelectedAbility is { } selected &&
                    !PhaseAbilityOptions.Any(o => o.Operand == selected.Operand) &&
                    AiCommandId.OptionFor(selected.Operand) is { } resolved)
                {
                    PhaseAbilityOptions.Add(resolved);
                }
            }
            finally
            {
                syncingPhaseAbilityOptions = false;
            }

            UpdateSelectedPhaseAbilityProjection();
        }

        void UpdateSelectedPhaseAbilityProjection()
        {
            AiCommandOption? selected = SelectedPhaseDraftStep?.SelectedAbility;
            AiCommandOption? projected = selected == null
                ? null
                : PhaseAbilityOptions.FirstOrDefault(o => o.Operand == selected.Operand)
                  ?? ResolvePhaseAbilityOption(selected.Operand)
                  ?? selected;

            updatingSelectedPhaseAbility = true;
            try
            {
                SelectedPhaseAbility = projected;
            }
            finally
            {
                updatingSelectedPhaseAbility = false;
            }
        }

        public void AddPhaseDraftStep()
        {
            EnsurePhaseDraftDefaults();
            int finalIndex = PhaseDraftSteps.ToList().FindIndex(s => s.IsFinalPhase);
            if (finalIndex < 0) finalIndex = PhaseDraftSteps.Count;
            PhaseDraftSteps.Insert(finalIndex, new AiPhaseDraftStepVm(finalIndex, false, OnPhaseDraftStepChanged)
            {
                SelectedAbility = SelectedPhaseDraftStep?.SelectedAbility ?? DefaultPhaseAbilityOption(),
                SelectedTriggerOption = SelectedPhaseDraftStep?.SelectedTriggerOption ?? ResolvePhaseTriggerOption(AiPhaseTriggerKind.OnTurn),
                SelectedTarget = SelectedPhaseDraftStep?.SelectedTarget ?? SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault(),
                ForbiddenTarget = SelectedPhaseDraftStep?.ForbiddenTarget ?? PhaseForbiddenRiteTargets.FirstOrDefault(),
                ForbiddenStatus = SelectedPhaseDraftStep?.ForbiddenStatus ?? SelectedForbiddenStatus ?? ForbiddenStatusPresets.FirstOrDefault(),
                ForbiddenValue = SelectedPhaseDraftStep?.ForbiddenValue ?? ForbiddenStatusValue,
            });
            ReindexPhaseDraftSteps();
            SelectedPhaseDraftStep = PhaseDraftSteps[Math.Clamp(finalIndex, 0, PhaseDraftSteps.Count - 1)];
            PhaseRotationApplySummary = Strings.F2_common_phase_added_to_draft_nothing_was__f8833188;
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        public void AddPhaseFinalDraftStep()
        {
            EnsurePhaseDraftDefaults();
            AiPhaseDraftStepVm? existing = PhaseDraftSteps.FirstOrDefault(s => s.IsFinalPhase);
            if (existing != null)
            {
                SelectedPhaseDraftStep = existing;
                PhaseRotationApplySummary = Strings.F2_this_draft_already_has_a_last_phase_edit_a446216f;
                return;
            }

            var final = new AiPhaseDraftStepVm(PhaseDraftSteps.Count + 1, true, OnPhaseDraftStepChanged)
            {
                SelectedAbility = SelectedPhaseDraftStep?.SelectedAbility ?? DefaultPhaseAbilityOption(),
                SelectedTriggerOption = SelectedPhaseDraftStep?.SelectedTriggerOption ?? ResolvePhaseTriggerOption(AiPhaseTriggerKind.OnTurn),
                SelectedTarget = SelectedPhaseDraftStep?.SelectedTarget ?? SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault(),
                ForbiddenTarget = SelectedPhaseDraftStep?.ForbiddenTarget ?? PhaseForbiddenRiteTargets.FirstOrDefault(),
                ForbiddenStatus = SelectedPhaseDraftStep?.ForbiddenStatus ?? SelectedForbiddenStatus ?? ForbiddenStatusPresets.FirstOrDefault(),
                ForbiddenValue = SelectedPhaseDraftStep?.ForbiddenValue ?? ForbiddenStatusValue,
            };
            PhaseDraftSteps.Add(final);
            ReindexPhaseDraftSteps();
            SelectedPhaseDraftStep = final;
            PhaseRotationApplySummary = Strings.F2_last_phase_added_to_draft_nothing_was_sa_4ef1bb5d;
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        public void RemovePhaseDraftStep()
        {
            EnsurePhaseDraftDefaults();
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseRotationApplySummary = Strings.U_Ai_PrSelectPhaseRemove;
                return;
            }

            int idx = PhaseDraftSteps.IndexOf(step);
            PhaseDraftSteps.Remove(step);
            ReindexPhaseDraftSteps();
            SelectedPhaseDraftStep = PhaseDraftSteps.Count == 0 ? null : PhaseDraftSteps[Math.Clamp(idx, 0, PhaseDraftSteps.Count - 1)];
            PhaseRotationApplySummary = Strings.F2_phase_removed_from_draft_nothing_was_sav_42fc8343;
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        void ReindexPhaseDraftSteps()
        {
            int common = 1;
            foreach (AiPhaseDraftStepVm step in PhaseDraftSteps)
            {
                step.Number = step.IsFinalPhase ? common : common++;
            }
            UpdatePhaseDraftCounterLabels();
            NotifyPhaseDraftStateChanged();
        }

        void OnPhaseDraftStepChanged()
        {
            if (!loadingPhaseDraft)
                SaveActivePhaseDraft();
            SyncPhaseAbilityOptions();
            NotifyPhaseDraftStateChanged();
            UpdatePhaseRotationPreview();
        }

        void NotifyPhaseDraftStateChanged()
        {
            OnPropertyChanged(nameof(CanRemovePhaseDraftStep));
            OnPropertyChanged(nameof(CanAddPhaseFinalStep));
            OnPropertyChanged(nameof(HasSelectedPhaseDraftStep));
            OnPropertyChanged(nameof(HasSelectedPhaseDraftEditor));
            OnPropertyChanged(nameof(ShowDirectPhaseDraftEditor));
            OnPropertyChanged(nameof(ShowIndirectPhaseDraftEditor));
            OnPropertyChanged(nameof(ShowPhaseDraftSelectionPlaceholder));
            OnPropertyChanged(nameof(IsCommonPhaseDraftEmpty));
            OnPropertyChanged(nameof(IsPhaseDraftEmpty));
            OnPropertyChanged(nameof(CanRemovePhaseVarAdvance));
            OnPropertyChanged(nameof(PhaseDraftCounterSummary));
            OnPropertyChanged(nameof(AdvancedDraftSelectionSummary));
            OnPropertyChanged(nameof(AdvancedDraftSelectionDetail));
            RebuildPhaseDraftRouteCards();
        }

        void NotifyPhaseCounterBindingsChanged()
        {
            OnPropertyChanged(nameof(PhaseCounterRuleSummary));
            OnPropertyChanged(nameof(PhaseAdvanceRuleSummary));
            OnPropertyChanged(nameof(PhaseDraftCounterSummary));
            OnPropertyChanged(nameof(PhaseAdvanceTargetSummary));
            UpdatePhaseDraftCounterLabels();
            UpdatePhaseStepGuardPreview();
        }

        void UpdatePhaseDraftCounterLabels()
        {
            string counter = SelectedPhaseVariableAudit?.VariableName ?? "contador";
            foreach (AiPhaseDraftStepVm step in PhaseDraftSteps)
            {
                step.CounterRuleSummary = string.IsNullOrWhiteSpace(step.ConditionSummary)
                    ? (step.IsFinalPhase
                        ? $"guarda fallback: {counter} > {PhaseFinalLimit}"
                        : $"guarda fallback: {counter} == {Math.Max(0, step.Number - 1)}")
                    : $"guarda: {step.ConditionSummary}";
                step.AdvanceRuleSummary = step.VarAdvanceSummary;
            }
        }

        public void AddVarAdvanceToSelectedPhase()
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseRotationApplySummary = Strings.F2_select_a_phase_before_adding_var_advance_4f71338d;
                return;
            }

            AiPhaseVariableAuditRow? variable = PhaseAdvanceVariable;
            if (variable == null)
            {
                PhaseRotationApplySummary = Strings.F2_explicitly_choose_the_var_that_this_phas_da3744c8;
                return;
            }

            if (!TryParsePhaseNumber(PhaseAdvanceMin, out int min) ||
                !TryParsePhaseNumber(PhaseAdvanceMax, out int max))
            {
                PhaseRotationApplySummary = Strings.U_Ai_PrInvalidAdvance;
                return;
            }
            if (max < min)
            {
                PhaseRotationApplySummary = Strings.F2_invalid_advance_the_maximum_must_be_grea_383b4335;
                return;
            }

            var advance = new AiPhaseVarAdvanceVm(
                step.VarAdvances.Count + 1,
                variable,
                PhaseAdvanceMin,
                PhaseAdvanceMax,
                isReset: false,
                OnPhaseDraftStepChanged);
            step.VarAdvances.Add(advance);
            SelectedPhaseVarAdvance = advance;
            ReindexPhaseVarAdvances(step);
            PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrAdvanceAdded, step.StepLabel, advance.Summary);
            OnPhaseDraftStepChanged();
        }

        public void RemoveSelectedPhaseVarAdvance()
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            AiPhaseVarAdvanceVm? advance = SelectedPhaseVarAdvance;
            if (step == null || advance == null || !step.VarAdvances.Contains(advance))
            {
                PhaseRotationApplySummary = Strings.U_Ai_PrSelectAdvanceRemove;
                return;
            }

            int idx = step.VarAdvances.IndexOf(advance);
            step.VarAdvances.Remove(advance);
            ReindexPhaseVarAdvances(step);
            SelectedPhaseVarAdvance = step.VarAdvances.Count == 0 ? null : step.VarAdvances[Math.Clamp(idx, 0, step.VarAdvances.Count - 1)];
            PhaseRotationApplySummary = Strings.F2_advance_removed_from_phase_nothing_was_s_4de54b95;
            OnPhaseDraftStepChanged();
        }

        static void ReindexPhaseVarAdvances(AiPhaseDraftStepVm step)
        {
            for (int i = 0; i < step.VarAdvances.Count; i++)
                step.VarAdvances[i].Number = i + 1;
            step.NotifyVarAdvancesChanged();
        }

        bool TryBuildPhaseConditionGuard(out IReadOnlyList<AiInstruction> guard, out string message)
        {
            guard = Array.Empty<AiInstruction>();
            if (PhaseConditionClauses.Count == 0)
            {
                message = Strings.F2_add_at_least_one_condition_to_the_builde_842bae6a;
                return false;
            }

            var clauses = new List<AiVarConditionClause>();
            foreach (AiPhaseConditionClauseVm row in PhaseConditionClauses)
            {
                AiPhaseVariableAuditRow? variable = row.SelectedVariable;
                if (variable == null)
                {
                    message = string.Format(Strings.U_Ai_PrCondSelectVar, row.Number);
                    return false;
                }
                if (!TryGetPreferredVariableIndex(variable, out ushort index))
                {
                    message = string.Format(Strings.U_Ai_PrCondIndexFail, row.Number, variable.RawVariableName);
                    return false;
                }
                if (!AiVarConditionBuilder.TryParseOperator(row.Operator, out AiVarCompareOperator op))
                {
                    message = string.Format(Strings.U_Ai_PrCondInvalidOp, row.Number, row.Operator);
                    return false;
                }
                if (!AiVarConditionBuilder.TryParseImmediate(row.Value, out int value))
                {
                    message = string.Format(Strings.U_Ai_PrCondInvalidValue, row.Number);
                    return false;
                }

                AiConditionJoinOperator join = string.Equals(row.Join, "OU", StringComparison.OrdinalIgnoreCase)
                    ? AiConditionJoinOperator.Or
                    : AiConditionJoinOperator.And;
                clauses.Add(new AiVarConditionClause(index, op, value, join));
            }

            guard = AiVarConditionBuilder.BuildClauseChain(clauses);
            return AiVarConditionBuilder.IsStackCleanBooleanGuard(guard, out message);
        }

        static bool TryGetRuntimeVariableIndex(AiPhaseVariableAuditRow row, out ushort index)
        {
            if (row.VariableIndex < 0 || row.VariableIndex > ushort.MaxValue)
            {
                index = 0;
                return false;
            }

            index = (ushort)row.VariableIndex;
            return true;
        }

        static bool TryGetPreferredVariableIndex(AiPhaseVariableAuditRow row, out ushort index)
        {
            int preferred = row.PreferredVariableIndex ?? row.VariableIndex;
            if (preferred < 0 || preferred > ushort.MaxValue)
            {
                index = 0;
                return false;
            }

            index = (ushort)preferred;
            return true;
        }

        void UpdatePhaseConditionPreview()
        {
            PhaseConditionPreview = string.Format(Strings.U_Ai_PrCondPreview, CurrentPhaseConditionHumanText());

            if (TryBuildPhaseConditionGuard(out IReadOnlyList<AiInstruction> guard, out string message))
            {
                PhaseConditionTechnicalSummary =
                    string.Format(Strings.U_Ai_PrCondReady, message, guard.Count, PhaseConditionRiskWarning());
            }
            else
            {
                PhaseConditionTechnicalSummary = $"⚠️ {message}";
            }

            RebuildConditionPreviewRouteCards();
        }

        string CurrentPhaseConditionHumanText()
        {
            if (PhaseConditionClauses.Count == 0)
                return Strings.U_Ai_PrNoCondition;

            return string.Join(" ", PhaseConditionClauses.Select((c, i) =>
                i == 0 ? c.ExpressionText : $"{c.Join} {c.ExpressionText}"));
        }

        public void ValidatePhaseMultivarCondition()
        {
            if (TryBuildPhaseConditionGuard(out IReadOnlyList<AiInstruction> guard, out string message))
            {
                PhaseConditionTechnicalSummary =
                    string.Format(Strings.U_Ai_PrCondValidated, message, guard.Count, PhaseConditionRiskWarning());
            }
            else
            {
                PhaseConditionTechnicalSummary = string.Format(Strings.U_Ai_PrCondInvalid, message);
            }
        }

        bool TryBuildSelectedPhaseStepSimpleGuard(out IReadOnlyList<AiInstruction> guard, out string summary, out string message)
        {
            guard = Array.Empty<AiInstruction>();
            summary = string.Empty;
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                message = Strings.F2_select_a_phase_to_edit_its_condition_12d1a627;
                return false;
            }

            AiPhaseVariableAuditRow? variable = SelectedPhaseStepGuardVariableAudit;
            if (variable == null)
            {
                message = Strings.F2_choose_the_var_that_this_phase_will_test_bf71b9ac;
                return false;
            }
            if (!TryGetPreferredVariableIndex(variable, out ushort variableIndex))
            {
                message = string.Format(Strings.U_Ai_PrIndexFail2, variable.RawVariableName);
                return false;
            }
            if (!AiVarConditionBuilder.TryParseOperator(PhaseStepGuardOperator, out AiVarCompareOperator op))
            {
                message = string.Format(Strings.U_Ai_PrInvalidOp2, PhaseStepGuardOperator);
                return false;
            }
            if (!AiVarConditionBuilder.TryParseImmediate(PhaseStepGuardValue, out int value))
            {
                message = Strings.F2_invalid_value_use_decimal_small_negative_df0e1896;
                return false;
            }

            guard = AiVarConditionBuilder.BuildImmediateComparison(variableIndex, op, value);
            if (!AiVarConditionBuilder.IsStackCleanBooleanGuard(guard, out message))
                return false;

            string opLabel = AiVarConditionBuilder.OperatorLabel(op);
            summary = $"{variable.VariableName} {opLabel} {value}";
            return true;
        }

        void UpdatePhaseStepGuardPreview()
        {
            if (loadingPhaseStepGuardEditor)
                return;

            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseStepGuardPreview = Strings.F2_select_a_phase_to_edit_its_condition_12d1a627;
                PhaseStepGuardTechnicalSummary = Strings.U_Ai_PrNoPhaseSelected;
                return;
            }

            string current = string.IsNullOrWhiteSpace(step.ConditionSummary)
                ? $"atual: fallback do card ({step.CounterRuleSummary})"
                : string.Format(Strings.U_Ai_PrOwnCondition, step.ConditionSummary);

            if (TryBuildSelectedPhaseStepSimpleGuard(out IReadOnlyList<AiInstruction> guard, out string summary, out string message))
            {
                PhaseStepGuardPreview =
                    $"{current}. Candidato: se {summary}, esta fase pode rodar. " +
                    Strings.U_Ai_PrRepeatCondition;
                PhaseStepGuardTechnicalSummary =
                    string.Format(Strings.U_Ai_PrGuardReady, message, guard.Count, PhaseStepGuardRiskWarning(SelectedPhaseStepGuardVariableAudit));
            }
            else
            {
                PhaseStepGuardPreview = string.Format(Strings.U_Ai_PrCandidateInvalid, current);
                PhaseStepGuardTechnicalSummary = $"⚠️ {message}";
            }
        }

        public void ApplySimpleConditionToSelectedPhaseStep()
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseStepGuardTechnicalSummary = Strings.F2_select_a_phase_before_applying_condition_889bb326;
                return;
            }

            if (!TryBuildSelectedPhaseStepSimpleGuard(out IReadOnlyList<AiInstruction> guard, out string summary, out string message))
            {
                PhaseStepGuardTechnicalSummary = string.Format(Strings.U_Ai_PrGuardNotApplied, message);
                return;
            }

            step.ConditionSummary = summary;
            step.ConditionGuard = guard.Select(ClonePhaseInstruction).ToList();
            step.SimpleConditionVariableKey = SelectedPhaseStepGuardVariableAudit?.AliasKey;
            step.SimpleConditionOperator = PhaseStepGuardOperator;
            step.SimpleConditionValue = PhaseStepGuardValue;
            PhaseStepGuardTechnicalSummary =
                string.Format(Strings.U_Ai_PrOwnCondApplied, step.StepLabel, summary, message, PhaseStepGuardRiskWarning(SelectedPhaseStepGuardVariableAudit));
            UpdatePhaseRotationPreview();
            SaveActivePhaseDraft();
        }

        public void AddVarResetToSelectedPhase()
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseRotationApplySummary = Strings.F2_select_a_phase_before_adding_var_reset_6a076194;
                return;
            }

            AiPhaseVariableAuditRow? variable = PhaseAdvanceVariable;
            if (variable == null)
            {
                PhaseRotationApplySummary = Strings.F2_explicitly_choose_which_var_this_phase_w_728bdd97;
                return;
            }

            var reset = new AiPhaseVarAdvanceVm(
                step.VarAdvances.Count + 1,
                variable,
                "0",
                "0",
                isReset: true,
                OnPhaseDraftStepChanged);
            step.VarAdvances.Add(reset);
            SelectedPhaseVarAdvance = reset;
            ReindexPhaseVarAdvances(step);
            OnPhaseDraftStepChanged();
            PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrResetAdded, step.StepLabel, variable.VariableName);
            SaveActivePhaseDraft();
        }

        public void ClearConditionFromSelectedPhaseStep()
        {
            AiPhaseDraftStepVm? step = SelectedPhaseDraftStep;
            if (step == null)
            {
                PhaseStepGuardTechnicalSummary = Strings.F2_select_a_phase_before_returning_to_fallb_38810b59;
                return;
            }

            step.ConditionSummary = string.Empty;
            step.ConditionGuard = null;
            step.SimpleConditionVariableKey = null;
            step.SimpleConditionOperator = "==";
            step.SimpleConditionValue = string.Empty;
            PhaseStepGuardTechnicalSummary =
                string.Format(Strings.U_Ai_PrBackToFallback, step.StepLabel);
            UpdatePhaseRotationPreview();
            LoadSelectedPhaseStepGuardEditor(step);
            SaveActivePhaseDraft();
        }

        public void AssignPhaseConditionToSelectedStep()
        {
            if (SelectedPhaseDraftStep == null)
            {
                PhaseConditionTechnicalSummary = Strings.F2_create_or_select_a_phase_before_applying_b1e93d2a;
                return;
            }

            if (!TryBuildPhaseConditionGuard(out IReadOnlyList<AiInstruction> guard, out string message))
            {
                PhaseConditionTechnicalSummary = string.Format(Strings.U_Ai_PrCondNotAppliedDraft, message);
                return;
            }

            SelectedPhaseDraftStep.ConditionSummary = CurrentPhaseConditionHumanText();
            SelectedPhaseDraftStep.ConditionGuard = guard.Select(ClonePhaseInstruction).ToList();
            SelectedPhaseDraftStep.SimpleConditionVariableKey = null;
            SelectedPhaseDraftStep.SimpleConditionOperator = "==";
            SelectedPhaseDraftStep.SimpleConditionValue = string.Empty;
            PhaseConditionTechnicalSummary =
                string.Format(Strings.U_Ai_PrCondAttachedDraft, SelectedPhaseDraftStep.StepLabel, message, guard.Count, PhaseConditionRiskWarning());
            UpdatePhaseRotationPreview();
            LoadSelectedPhaseStepGuardEditor(SelectedPhaseDraftStep);
            SaveActivePhaseDraft();
        }

        internal static AiInstruction ClonePhaseInstruction(AiInstruction instruction) => new()
        {
            Offset = -1,
            Opcode = instruction.Opcode,
            HasOperand = instruction.HasOperand,
            Operand = instruction.Operand,
            OperandKind = instruction.OperandKind,
        };

        string PhaseConditionRiskWarning()
        {
            var rows = PhaseConditionClauses
                .Select(c => c.SelectedVariable)
                .Where(v => v != null)
                .Cast<AiPhaseVariableAuditRow>()
                .Distinct()
                .ToList();

            var warnings = new List<string>();
            if (rows.Any(r => r.StorageCode == 0x52))
                warnings.Add(Strings.AiPhaseSharedStorageWarning);
            if (rows.Any(r => r.HasSwitchUsage))
                warnings.Add(Strings.U_Ai_PrWarnSwitch);
            if (rows.Any(r => r.HasDataDependency))
                warnings.Add(Strings.AiPhaseVariableDependencyWarning);
            if (rows.Any(r => r.Score >= 10))
                warnings.Add(Strings.U_Ai_PrWarnHighScore);

            return warnings.Count == 0
                ? Strings.AiPhaseNoObviousRisk
                : string.Format(Strings.U_Ai_PrPermissiveWarning, string.Join("; ", warnings));
        }

        static string PhaseStepGuardRiskWarning(AiPhaseVariableAuditRow? row)
        {
            if (row == null)
                return Strings.F2_select_a_var_before_applying_ff483d08;

            var warnings = new List<string>();
            if (row.StorageCode == 0x52)
                warnings.Add(Strings.AiPhaseSharedStorageWarning);
            if (row.HasSwitchUsage)
                warnings.Add(Strings.U_Ai_PrWarnSwitch2);
            if (row.HasDataDependency)
                warnings.Add(Strings.AiPhaseVariableDependencyWarning);
            if (row.Score >= 10)
                warnings.Add(Strings.U_Ai_PrWarnHighScore);

            return warnings.Count == 0
                ? Strings.AiPhaseNoObviousRisk
                : string.Format(Strings.U_Ai_PrPermissiveWarning2, string.Join("; ", warnings));
        }

        void UpdatePhaseRotationPreview()
        {
            UpdatePhaseDraftCounterLabels();
            OnPropertyChanged(nameof(PhaseCounterRuleSummary));
            OnPropertyChanged(nameof(PhaseAdvanceRuleSummary));
            OnPropertyChanged(nameof(PhaseDraftCounterSummary));
            string counter = SelectedPhaseVariableAudit?.VariableName ?? "counter not yet selected";
            List<AiPhaseDraftStepVm> common = PhaseDraftSteps.Where(s => !s.IsFinalPhase).ToList();
            if (PhaseDraftSteps.Count == 0)
            {
                PhaseRotationPreview =
                    string.Format(Strings.U_Ai_PrAuditedCounter, counter) +
                    Strings.U_Ai_PrNoPhaseProof;
                PhaseRotationApplySummary =
                    Strings.F2_choose_how_many_phases_the_behavior_will_f72d53f4;
                return;
            }

            string commonSteps = common.Count == 0
                ? Strings.U_Ai_PrNoCommonPhase
                : string.Join(" -> ", common.Select(s => s.PreviewLabel));
            AiPhaseDraftStepVm? final = PhaseDraftSteps.FirstOrDefault(s => s.IsFinalPhase);
            string finalText = final == null ? Strings.U_Ai_PrNoLastPhase : final.PreviewLabel;

            PhaseRotationPreview =
                string.Format(Strings.U_Ai_PrCounterSummary, counter, commonSteps) +
                string.Format(Strings.U_Ai_PrLastPhaseDetail, finalText);

            PhaseRotationApplySummary =
                Strings.F2_draft_ready_for_human_review_applying_sa_3d3a1b24;
        }

        public void ApplyPhaseRotationDraft()
        {
            SaveActivePhaseDraft();
            EnsurePhaseDraftDefaults();
            UpdatePhaseRotationPreview();

            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                PhaseRotationApplySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_40c0eeeb;
                return;
            }
            if (SelectedPhaseVariableAudit == null)
            {
                PhaseRotationApplySummary = Strings.F2_select_an_audited_var_to_be_the_rotation_1c31aa70;
                return;
            }
            if (PhaseDraftSteps.Count == 0 && PhaseDraftRouteCards.Any(card => card.SourceKind == AiPhaseRouteCardSourceKind.IndirectEvidence))
            {
                PhaseRotationApplySummary =
                    Strings.F2_the_current_draft_of_this_var_is_an_indi_c8146654;
                return;
            }
            if (PhaseDraftSteps.Count == 0)
            {
                PhaseRotationApplySummary = Strings.F2_assemble_at_least_one_phase_before_apply_0ab5f20d;
                return;
            }
            if (!TryParsePhaseNumber(PhaseFinalLimit, out int finalLimit))
            {
                PhaseRotationApplySummary = Strings.U_Ai_PrInvalidThreshold;
                return;
            }
            if (PhaseDraftSteps.Count(s => s.IsFinalPhase) > 1)
            {
                PhaseRotationApplySummary = "The writer v1 accepts at most one last phase.";
                return;
            }
            if (PhaseDraftSteps.Any(s => s.SelectedAbility == null))
            {
                PhaseRotationApplySummary = Strings.U_Ai_PrNeedAbility;
                return;
            }
            AiTargetOption? defaultTarget = SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault();
            if (defaultTarget == null)
            {
                PhaseRotationApplySummary = Strings.F2_no_target_available_for_the_rotation_rel_fa9b5531;
                return;
            }

            if (!TryPreparePhaseVariableMaterialization(
                    selectedScript,
                    out AiScriptFile preparedScript,
                    out IReadOnlyDictionary<string, ushort> materializedByKey,
                    out IReadOnlyDictionary<ushort, ushort> remapByPreferredIndex,
                    out string materializationSummary,
                    out string materializationError))
            {
                PhaseRotationApplySummary = materializationError;
                return;
            }

            if (!materializedByKey.TryGetValue(SelectedPhaseVariableAudit.AliasKey, out ushort counterIndex))
            {
                PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrCounterMaterializeFail, SelectedPhaseVariableAudit.VariableName);
                return;
            }

            var steps = new List<AiPhaseRotationStep>();
            AiEventHook? defaultHook = null;
            var hookLabels = new List<string>();

            IReadOnlyList<AiInstruction>? phaseHpGuard = null;
            if (PhaseConditionUseHpBelowGuard)
            {
                if (!TryParsePhaseNumber(PhaseConditionHpBelowPercentText, out int phaseHpPct) || phaseHpPct < 1 || phaseHpPct > 100)
                {
                    PhaseRotationApplySummary = Strings.F2_invalid_phase_condition_hp_use_a_percent_09f8c127;
                    return;
                }
                phaseHpGuard = AiAutomation.BuildHpBelowPercentGuard((ushort)phaseHpPct);
                if (!AiVarConditionBuilder.IsStackCleanBooleanGuard(phaseHpGuard, out string phaseHpReason))
                {
                    PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrInvalidPhaseHp, phaseHpReason);
                    return;
                }
            }

            foreach (AiPhaseDraftStepVm s in PhaseDraftSteps)
            {
                if (s.SelectedTarget == null)
                {
                    PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrChooseTarget, s.StepLabel);
                    return;
                }

                int chanceK = 2;
                if (s.UseChance)
                {
                    if (!TryParsePhaseNumber(s.ChanceKText, out chanceK) || chanceK < 2)
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrInvalidChance, s.StepLabel);
                        return;
                    }
                }

                var mutations = new List<AiPhaseVarMutation>();
                foreach (AiPhaseVarAdvanceVm advance in s.VarAdvances)
                {
                    if (advance.Variable == null || !materializedByKey.TryGetValue(advance.Variable.AliasKey, out ushort variableIndex))
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrAdvanceNoVar, s.StepLabel);
                        return;
                    }
                    if (!TryParsePhaseNumber(advance.MinText, out int min) ||
                        !TryParsePhaseNumber(advance.MaxText, out int max))
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrAdvanceInvalid, s.StepLabel, advance.Variable.VariableName);
                        return;
                    }
                    if (max < min)
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrAdvanceMaxBelowMin, s.StepLabel, advance.Variable.VariableName);
                        return;
                    }
                    mutations.Add(new AiPhaseVarMutation(variableIndex, min, max, advance.IsReset));
                }

                IReadOnlyList<AiInstruction>? additionalGuard = null;

                if (phaseHpGuard != null)
                {
                    additionalGuard = phaseHpGuard;
                }

                if (s.UseHpBelowGuard)
                {
                    if (!TryParsePhaseNumber(s.HpBelowPercentText, out int hpPercent) || hpPercent < 1 || hpPercent > 100)
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrInvalidHp, s.StepLabel);
                        return;
                    }
                    var stepHpGuard = AiAutomation.BuildHpBelowPercentGuard((ushort)hpPercent);
                    if (!AiVarConditionBuilder.IsStackCleanBooleanGuard(stepHpGuard, out string hpGuardReason))
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrInvalidHpGuard, s.StepLabel, hpGuardReason);
                        return;
                    }
                    if (additionalGuard != null)
                    {
                        var combined = new List<AiInstruction>(additionalGuard);
                        combined.AddRange(stepHpGuard);
                        combined.Add(Op0(0x02));
                        additionalGuard = combined;
                    }
                    else
                    {
                        additionalGuard = stepHpGuard;
                    }
                }

                AiPhaseForbiddenRiteEffect? forbiddenRite = null;
                if (s.UseForbiddenRite)
                {
                    if (s.ForbiddenStatus == null)
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrChooseRiteStatus, s.StepLabel);
                        return;
                    }
                    if (s.ForbiddenTarget == null)
                    {
                        PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrChooseRiteTarget, s.StepLabel);
                        return;
                    }
                    if (!TryParseU16Loose(s.ForbiddenValue, out ushort riteValue))
                    { PhaseRotationApplySummary = Strings.AiAdvancedInvalidNumber; return; }
                    forbiddenRite = new AiPhaseForbiddenRiteEffect(
                        s.ForbiddenTarget.UseLinkedActionTarget,
                        s.ForbiddenTarget.Operand,
                        s.ForbiddenStatus.FieldId,
                        riteValue);
                }

                if (!TryResolvePhaseStepHook(s.TriggerKind, out AiEventHook stepHook, out string stepHookLabel, out string stepHookError))
                {
                    PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrTriggerResolveFailed, s.StepLabel, DescribePhaseTriggerKind(s.TriggerKind), stepHookError);
                    return;
                }

                defaultHook ??= stepHook;
                if (!hookLabels.Contains(stepHookLabel, StringComparer.Ordinal))
                    hookLabels.Add(stepHookLabel);

                steps.Add(new AiPhaseRotationStep(
                    s.Number,
                    s.StepLabel,
                    s.SelectedAbility!.Operand,
                    s.IsFinalPhase,
                    s.StopHere,
                    mutations,
                    s.SelectedTarget.ToRecipe(),
                    s.UseChance,
                    chanceK,
                    RemapGuardVariableOperands(s.ConditionGuard, remapByPreferredIndex),
                    additionalGuard,
                    forbiddenRite,
                    s.TriggerKind,
                    stepHook.WorkerIndex,
                    stepHook.EntrypointIndex,
                    stepHookLabel));
            }

            if (defaultHook == null)
            {
                PhaseRotationApplySummary = "Could not resolve any phase trigger.";
                return;
            }

            var recipe = new AiPhaseRotationRecipe(
                counterIndex,
                steps,
                finalLimit,
                defaultHook.Value.WorkerIndex,
                defaultHook.Value.EntrypointIndex,
                defaultTarget.ToRecipe(),
                MonsterId: selectedPath != null ? Path.GetFileNameWithoutExtension(selectedPath) : null);

            AiPhaseRotationApplyResult result;
            try
            {
                result = AiPhaseRotationWriter.Apply(preparedScript, recipe);
            }
            catch (Exception ex)
            {
                PhaseRotationApplySummary = string.Format(Strings.U_Ai_PrWriterAborted, ex.Message);
                return;
            }

            if (!SaveNewAi(result.AiFile, out string err))
            {
                PhaseRotationApplySummary = err;
                return;
            }

            RefreshPhaseRotationAudit();
            string triggerWarnings = BuildPhaseTriggerWarnings(steps);
            PhaseRotationApplySummary =
                string.Format(Strings.U_Ai_PrRecipeApplied, result.Summary, string.Join(" | ", hookLabels), Path.GetFileName(selectedPath)) +
                Strings.U_Ai_PrWriterV1 +
                materializationSummary +
                triggerWarnings;
        }

        bool TryPreparePhaseVariableMaterialization(
            AiScriptFile source,
            out AiScriptFile preparedScript,
            out IReadOnlyDictionary<string, ushort> materializedByKey,
            out IReadOnlyDictionary<ushort, ushort> remapByPreferredIndex,
            out string summary,
            out string error)
        {
            preparedScript = source;
            summary = string.Empty;
            error = string.Empty;
            var byKey = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            var byPreferred = new Dictionary<ushort, ushort>();

            var duplicatePreferred = PhaseVariableAudits
                .Where(row => TryGetPreferredVariableIndex(row, out _))
                .Select(row => new
                {
                    Row = row,
                    Index = (ushort)(row.PreferredVariableIndex ?? row.VariableIndex),
                })
                .GroupBy(item => item.Index)
                .FirstOrDefault(group => group.Select(item => item.Row.AliasKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
            if (duplicatePreferred != null)
            {
                string collision = string.Join(", ", duplicatePreferred.Select(item => item.Row.VariableName));
                materializedByKey = byKey;
                remapByPreferredIndex = byPreferred;
                error = string.Format(Strings.U_Ai_PrIdConflict, duplicatePreferred.Key, collision);
                return false;
            }

            var referencedRows = new Dictionary<string, AiPhaseVariableAuditRow>(StringComparer.OrdinalIgnoreCase);
            void AddReferenced(AiPhaseVariableAuditRow? row)
            {
                if (row != null)
                    referencedRows[row.AliasKey] = row;
            }

            AddReferenced(SelectedPhaseVariableAudit);
            foreach (AiPhaseDraftStepVm step in PhaseDraftSteps)
            {
                foreach (AiPhaseVarAdvanceVm advance in step.VarAdvances)
                    AddReferenced(advance.Variable);

                foreach (ushort operand in EnumerateGuardVariableOperands(step.ConditionGuard))
                {
                    AiPhaseVariableAuditRow? row = FindPhaseVariableByPreferredIndex(operand);
                    if (row == null)
                    {
                        materializedByKey = byKey;
                        remapByPreferredIndex = byPreferred;
                        error = string.Format(Strings.U_Ai_PrCondUsesUnknownVar, step.StepLabel, operand);
                        return false;
                    }
                    AddReferenced(row);
                }
            }

            var materializedNotes = new List<string>();
            foreach (AiPhaseVariableAuditRow row in referencedRows.Values
                         .OrderBy(r => r.PreferredVariableIndex ?? r.VariableIndex)
                         .ThenBy(r => r.VariableIndex))
            {
                if (!TryGetPreferredVariableIndex(row, out ushort preferredIndex))
                {
                    materializedByKey = byKey;
                    remapByPreferredIndex = byPreferred;
                    error = row.VariableName + ": " + Strings.U_Ai_PrInvalidAuthoringId;
                    return false;
                }

                int targetIndex = preferredIndex;
                bool preferredExists = targetIndex < preparedScript.Variables.Count;
                if (!preferredExists ||
                    !VariableDescriptorMatches(preparedScript.Variables[targetIndex], row))
                {
                    int existing = FindMatchingVariableDescriptor(preparedScript, row);
                    if (existing >= 0)
                    {
                        targetIndex = existing;
                    }
                    else
                    {
                        targetIndex = preparedScript.Variables.Count;
                        byte[] grown = AiScript_File.AppendVariableDescriptor(
                            preparedScript,
                            row.StorageCode,
                            row.VariableSlot,
                            row.TypeId,
                            allowDuplicateDescriptor: false);
                        preparedScript = AiScript_File.Read(grown);
                    }
                }

                if (!VariableDescriptorMatches(preparedScript.Variables[targetIndex], row))
                {
                    materializedByKey = byKey;
                    remapByPreferredIndex = byPreferred;
                    error = string.Format(Strings.U_Ai_PrDescriptorMismatch, row.VariableName, targetIndex, row.RawVariableName);
                    return false;
                }

                byKey[row.AliasKey] = (ushort)targetIndex;
                byPreferred[preferredIndex] = (ushort)targetIndex;
                if (targetIndex != row.VariableIndex || preferredIndex != targetIndex)
                    materializedNotes.Add($"{row.VariableName}: ID{preferredIndex} -> var[{targetIndex}]");
            }

            materializedByKey = byKey;
            remapByPreferredIndex = byPreferred;
            summary = materializedNotes.Count == 0
                ? string.Empty
                : string.Format(Strings.U_Ai_PrVarMaterialization, string.Join("; ", materializedNotes));
            return true;
        }

        AiPhaseVariableAuditRow? FindPhaseVariableByPreferredIndex(ushort variableIndex)
        {
            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (TryGetPreferredVariableIndex(row, out ushort idx) && idx == variableIndex)
                    return row;
            }

            foreach (AiPhaseVariableAuditRow row in PhaseVariableAudits)
            {
                if (TryGetRuntimeVariableIndex(row, out ushort idx) && idx == variableIndex)
                    return row;
            }

            return null;
        }

        static IEnumerable<ushort> EnumerateGuardVariableOperands(IReadOnlyList<AiInstruction>? guard)
        {
            if (guard == null)
                yield break;

            foreach (AiInstruction instruction in guard)
            {
                AiOperandKind kind = AiScript_File.OperandKindOf(instruction.Opcode);
                if (kind is AiOperandKind.VarLoad or AiOperandKind.VarStore)
                    yield return instruction.Operand;
            }
        }

        static IReadOnlyList<AiInstruction>? RemapGuardVariableOperands(
            IReadOnlyList<AiInstruction>? guard,
            IReadOnlyDictionary<ushort, ushort> remapByPreferredIndex)
        {
            if (guard == null || guard.Count == 0 || remapByPreferredIndex.Count == 0)
                return guard == null ? null : guard.Select(ClonePhaseInstruction).ToList();

            var cloned = new List<AiInstruction>(guard.Count);
            foreach (AiInstruction instruction in guard)
            {
                AiInstruction copy = ClonePhaseInstruction(instruction);
                AiOperandKind kind = AiScript_File.OperandKindOf(copy.Opcode);
                if (kind is AiOperandKind.VarLoad or AiOperandKind.VarStore)
                {
                    if (remapByPreferredIndex.TryGetValue(copy.Operand, out ushort remapped))
                        copy.Operand = remapped;
                }
                cloned.Add(copy);
            }

            return cloned;
        }

        static bool VariableDescriptorMatches(AiVariable variable, AiPhaseVariableAuditRow row) =>
            variable.Storage == row.StorageCode &&
            variable.Slot == row.VariableSlot &&
            variable.TypeId == row.TypeId;

        static int FindMatchingVariableDescriptor(AiScriptFile script, AiPhaseVariableAuditRow row)
        {
            for (int i = 0; i < script.Variables.Count; i++)
                if (VariableDescriptorMatches(script.Variables[i], row))
                    return i;
            return -1;
        }

        bool TryResolvePhaseTriggerKindForCallOffset(int callOffset, out AiPhaseTriggerKind kind)
        {
            kind = AiPhaseTriggerKind.OnTurn;
            if (selectedScript == null)
                return false;

            int workerIndex = AiAutomation.OwningWorkerIndex(selectedScript, callOffset);
            if (workerIndex < 0 || workerIndex >= selectedScript.Workers.Count)
                return false;

            if (TryResolveOnTurnHook(out AiEventHook onTurnHook, out _) &&
                onTurnHook.WorkerIndex == workerIndex &&
                BuildReachability(onTurnHook).ContainsKey(callOffset))
            {
                kind = AiPhaseTriggerKind.OnTurn;
                return true;
            }

            if (TryResolveOnHitHook(out AiEventHook onHitHook, out _) &&
                onHitHook.WorkerIndex == workerIndex &&
                BuildReachability(onHitHook).ContainsKey(callOffset))
            {
                kind = AiPhaseTriggerKind.OnHit;
                return true;
            }

            AiWorker worker = selectedScript.Workers[workerIndex];
            if (worker.Entrypoints.Count > 0 &&
                BuildReachability(new AiEventHook(workerIndex, 0)).ContainsKey(callOffset))
            {
                kind = AiPhaseTriggerKind.BattleStart;
                return true;
            }

            return false;
        }

        bool TryResolvePhaseStepHook(AiPhaseTriggerKind kind, out AiEventHook hook, out string hookLabel, out string error)
        {
            hook = default;
            hookLabel = string.Empty;
            error = string.Empty;

            switch (kind)
            {
                case AiPhaseTriggerKind.BattleStart:
                    return TryResolveCombatEntrypointForYunalesca(0, Strings.U_Ai_BattleStart, out hook, out hookLabel, out error);
                case AiPhaseTriggerKind.OnHit:
                    if (!TryResolveOnHitHook(out hook, out error))
                        return false;
                    hookLabel = HookLabel(hook, "onHit real");
                    return true;
                default:
                    if (!TryResolveOnTurnHook(out hook, out error))
                        return false;
                    hookLabel = HookLabel(hook, "onTurn real");
                    return true;
            }
        }

        static string DescribePhaseTriggerKind(AiPhaseTriggerKind kind) => kind switch
        {
            AiPhaseTriggerKind.OnHit => Strings.F2_upon_being_hit_e193aae0,
            _ => "onTurn",
        };

        static string BuildPhaseTriggerWarnings(IReadOnlyList<AiPhaseRotationStep> steps)
        {
            var warnings = new List<string>();
            if (steps.Any(step => step.TriggerKind == AiPhaseTriggerKind.OnHit))
                warnings.Add(Strings.F2_phase_s_onhit_use_the_reaction_combathan_7433e40b);
            return warnings.Count == 0 ? string.Empty : string.Format(Strings.U_Ai_PrWarnings, string.Join("; ", warnings));
        }

        static bool TryParsePhaseNumber(string? text, out int value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value)
                       && value >= 0 && value <= ushort.MaxValue;
            return int.TryParse(text, out value) && value >= 0 && value <= ushort.MaxValue;
        }

        public void SaveSelectedPhaseVarAlias()
        {
            AiPhaseVariableAuditRow? row = SelectedPhaseVariableAudit;
            if (row == null)
            {
                PhaseVarAliasSummary = Strings.F2_select_a_variable_before_saving_the_alia_b433816b;
                return;
            }

            string alias = (PhaseVarAliasInput ?? string.Empty).Trim();
            PhaseVarAliasStore.SetMetadata(
                row.AliasKey,
                alias,
                row.PreferredVariableIndex,
                CurrentAiHashShort());
            row.SetAlias(alias);
            PhaseVarAliasSummary = string.IsNullOrWhiteSpace(alias)
                ? string.Format(Strings.U_Ai_PrAliasRemoved, row.RawVariableName)
                : string.Format(Strings.U_Ai_PrAliasSet, row.RawVariableName, alias);
            UpdatePhaseRotationPreview();
        }

        public void ClearSelectedPhaseVarAlias()
        {
            PhaseVarAliasInput = string.Empty;
            SaveSelectedPhaseVarAlias();
        }

        public void SaveSelectedPhaseVarAuthoringIndex()
        {
            AiPhaseVariableAuditRow? row = SelectedPhaseVariableAudit;
            if (row == null)
            {
                PhaseVarAuthoringIndexSummary = Strings.F2_select_a_variable_before_saving_the_auth_ad717124;
                return;
            }

            string raw = (PhaseVarAuthoringIndexInput ?? string.Empty).Trim();
            if (!TryParsePhaseNumber(raw, out int requestedIndex))
            {
                PhaseVarAuthoringIndexSummary = Strings.U_Ai_PrInvalidAuthoringId;
                return;
            }

            row.SetPreferredVariableIndex(requestedIndex);
            PhaseVarAuthoringIndexInput = requestedIndex.ToString();
            PhaseVarAliasStore.SetMetadata(
                row.AliasKey,
                row.Alias,
                row.PreferredVariableIndex,
                CurrentAiHashShort());
            UpdatePhaseVarAuthoringIndexSummary(row);
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
        }

        public void ClearSelectedPhaseVarAuthoringIndex()
        {
            AiPhaseVariableAuditRow? row = SelectedPhaseVariableAudit;
            if (row == null)
            {
                PhaseVarAuthoringIndexSummary = Strings.F2_select_a_variable_before_clearing_the_au_5aee7a80;
                return;
            }

            row.ClearPreferredVariableIndex();
            PhaseVarAuthoringIndexInput = row.VariableIndex.ToString();
            PhaseVarAliasStore.SetMetadata(
                row.AliasKey,
                row.Alias,
                row.PreferredVariableIndex,
                CurrentAiHashShort());
            UpdatePhaseVarAuthoringIndexSummary(row);
            NotifyPhaseCounterBindingsChanged();
            UpdatePhaseRotationPreview();
        }

        void UpdatePhaseVarAuthoringIndexSummary(AiPhaseVariableAuditRow? row)
        {
            if (row == null)
            {
                PhaseVarAuthoringIndexSummary =
                    Strings.F2_the_authoring_template_id_may_request_a__47ca6be5;
                return;
            }

            int preferred = row.PreferredVariableIndex ?? row.VariableIndex;
            PhaseVarAuthoringIndexSummary = row.HasPreferredVariableIndex
                ? string.Format(Strings.U_Ai_PrPreferredId, row.RawVariableName, preferred)
                : string.Format(Strings.U_Ai_PrNoOverride, row.RawVariableName, row.VariableIndex);
        }

        public void CreateFreePrivateVarLab()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                PhasePrivateVarLabSummary = Strings.U_Ai_PrPrivUnavailable;
                OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
                return;
            }

            if (!TryEnsureFreePrivateVarSlot(out int slot, out string reason))
            {
                PhasePrivateVarLabSummary = string.Format(Strings.U_Ai_PrPrivReason, reason);
                OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
                return;
            }

            byte[] newAi;
            try
            {
                newAi = AiScript_File.AppendPrivateVariableDescriptor(selectedScript, slot);
            }
            catch (Exception ex)
            {
                PhasePrivateVarLabSummary = string.Format(Strings.U_Ai_PrPrivAborted, ex.Message);
                return;
            }

            AiValidationReport report = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!report.IsValid)
            {
                PhasePrivateVarLabSummary = string.Format(Strings.U_Ai_PrPrivBlocked, report.Errors.FirstOrDefault()?.Message);
                return;
            }

            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                WriteMonsterWithBackup(AiScript_File.SpliceAiFileIntoMonsterGrow(monster, newAi));
            }
            catch (Exception ex)
            {
                PhasePrivateVarLabSummary = $"Var priv abortada no save/splice: {ex.Message}";
                return;
            }

            string newName = $"priv{slot:X4}";
            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            SelectedPhaseVariableAudit = PhaseVariableAudits.FirstOrDefault(v => v.RawVariableName == newName)
                                         ?? PhaseVariableAudits.FirstOrDefault();
            PhasePrivateVarLabSummary = reloaded
                ? string.Format(Strings.U_Ai_PrPrivCreated, newName, Path.GetFileName(selectedPath))
                : string.Format(Strings.U_Ai_PrPrivReloadFail, newName);
            PhaseVarAliasInput = SelectedPhaseVariableAudit?.Alias ?? string.Empty;
            OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
            UpdatePhaseRotationPreview();
        }

        string CurrentAiHashShort()
        {
            if (selectedScript?.OriginalAiFileBytes == null || selectedScript.OriginalAiFileBytes.Length == 0)
                return "no-ai";
            byte[] hash = SHA256.HashData(selectedScript.OriginalAiFileBytes);
            return Convert.ToHexString(hash.AsSpan(0, 6));
        }

        sealed class PhaseVarAccumulator
        {
            public PhaseVarAccumulator(AiVariable variable) => Variable = variable;

            /// <summary>Infer a generic semantic role from the usage hints when no family-specific
            /// detector recognizes this variable. Translates bytecode patterns into human labels.</summary>
            AiVariableSemanticAnnotation? InferGenericAnnotation()
            {
                if (Reads == 0 && Writes == 0) return null;

                // Counter: increments/resets + used in conditions
                if (ResetHints > 0 && AdvanceHints > 0 && ConditionHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrCounterPhase,
                        string.Format(Strings.U_Ai_PrReadsWrites, Reads, Writes, ResetHints, AdvanceHints),
                        "", "", "analise-uso");

                // Pure counter: increments/resets but no condition on it (maybe write-only)
                if (ResetHints > 0 && AdvanceHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrCounter,
                        string.Format(Strings.U_Ai_PrReadsWritesResets, Reads, Writes, ResetHints, AdvanceHints),
                        "", "", "analise-uso");

                // Gate one-shot: written once, checked in conditions
                if (ConditionHints > 0 && Writes <= 1 && ResetHints == 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrGateOneShot,
                        string.Format(Strings.U_Ai_PrWrittenRead, Writes, ConditionHints),
                        "", "", "analise-uso");

                // Threshold: math store + compare
                if (MathStoreHints > 0 && CompareHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrThresholdStore,
                        string.Format(Strings.U_Ai_PrThresholdDetail, MathStoreHints, CompareHints, SourceVariableNames.Count),
                        "", "", "analise-uso");

                // Dispatch table: switch target
                if (SwitchHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.F2_dispatch_table_2f5cd32b,
                        string.Format(Strings.U_Ai_PrSwitchDetail, SwitchHints, Reads, Writes),
                        "", "", "analise-uso");

                // Timer/countdown: advances + compared
                if (AdvanceHints > 0 && CompareHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrTimerCountdown,
                        string.Format(Strings.U_Ai_PrTimerDetail, AdvanceHints, CompareHints),
                        "", "", "analise-uso");

                // Flag de estado: condition-only, few writes
                if (ConditionHints > 0 && Writes <= 3)
                    return new AiVariableSemanticAnnotation(
                        "Flag de estado",
                        string.Format(Strings.U_Ai_PrReadWritten, ConditionHints, Writes),
                        "", "", "analise-uso");

                // Math intermediate: math store without compare
                if (MathStoreHints > 0)
                    return new AiVariableSemanticAnnotation(
                        Strings.U_Ai_PrIntermediate,
                        $"{MathStoreHints}x math-store",
                        "", "", "analise-uso");

                // Fallback: used but role unclear
                if (Reads > 0 || Writes > 0)
                    return new AiVariableSemanticAnnotation(
                        "Var de uso geral",
                        $"{Reads} leitura(s), {Writes} escrita(s)",
                        "", "", "analise-uso");

                return null;
            }

            static string DecodeStorage(byte storage)
            {
                int typeNibble = (storage >> 4) & 0xF;
                int locationNibble = storage & 0xF;
                string typeName = typeNibble switch
                {
                    0 => "u8", 1 => "i8", 2 => "u16", 3 => "i16",
                    4 => "u32", 5 => "i32", 6 => "f32",
                    _ => $"tipo_{typeNibble:X1}",
                };
                string locationName = locationNibble switch
                {
                    0 => "saveData", 1 => "commonVars", 2 => "data",
                    3 => "priv", 4 => "shared", 5 => "intRegs",
                    6 => "eventData",
                    _ => $"loc_{locationNibble:X1}",
                };
                return $"{typeName} · {locationName}";
            }

            public AiVariable Variable { get; }
            public int Reads { get; set; }
            public int Writes { get; set; }
            public int ResetHints { get; set; }
            public int AdvanceHints { get; set; }
            public int ConditionHints { get; set; }
            public int CompareHints { get; set; }
            public int SwitchHints { get; set; }
            public int MathStoreHints { get; set; }
            public int FirstOffset { get; set; } = -1;
            public int LastOffset { get; set; } = -1;
            public HashSet<int> Workers { get; } = new();
            public HashSet<string> SourceVariableNames { get; } = new(StringComparer.OrdinalIgnoreCase);

            public int Score =>
                (Reads > 0 ? 2 : 0) +
                (Writes > 0 ? 2 : 0) +
                Math.Min(ResetHints, 2) +
                Math.Min(AdvanceHints, 3) +
                Math.Min(ConditionHints, 3) +
                Math.Min(CompareHints, 2) +
                Math.Min(SwitchHints, 2) +
                Math.Min(MathStoreHints, 2) +
                Math.Min(SourceVariableNames.Count, 2) +
                (Variable.Storage == 0x56 ? 1 : 0);

            public AiPhaseVariableAuditRow ToRow(AiScriptFile script, string aliasKey, PhaseVarAliasEntry metadata, string aiHash,
                IReadOnlyDictionary<int, AiVariableSemanticAnnotation>? semanticMap = null)
            {
                string storage = DecodeStorage(Variable.Storage);
                string workers = Workers.Count == 0
                    ? "worker desconhecido"
                    : string.Join(", ", Workers.OrderBy(w => w).Select(w =>
                {
                    string kind = w >= 0 && w < script.Workers.Count ? script.Workers[w].InferredType ?? "?" : "?";
                    return $"w{w} {kind}";
                }));
                string badge = Score >= 10 ? Strings.U_Ai_PrStrongCandidate : Score >= 6 ? Strings.U_Ai_PrPossibleCounter : Reads + Writes == 0 ? Strings.U_Ai_PrDeclaredUnused : Strings.U_Ai_PrSimpleUse;
                string recommendation = Variable.Storage == 0x52
                    ? Strings.F2_warning_battlevar_may_be_shared_between__254e9918
                    : Variable.Storage == 0x56
                        ? Strings.F2_good_candidate_if_the_worker_where_the_r_1d89d3c8
                        : Strings.F2_review_before_using_as_a_human_counter_70beeac5;

                string hints = Reads + Writes == 0
                    ? Strings.U_Ai_PrDeclaredNoPus
                    : string.Format(Strings.U_Ai_PrAccessSummary, Reads, Writes, ResetHints, AdvanceHints, ConditionHints);
                string flow = string.Join(" · ", new[]
                {
                    CompareHints > 0 ? $"compare {CompareHints}" : "",
                    SwitchHints > 0 ? $"switch {SwitchHints}" : "",
                    MathStoreHints > 0 ? $"math-store {MathStoreHints}" : "",
                    SourceVariableNames.Count > 0 ? string.Format(Strings.AiPhaseDependsOn, string.Join(", ", SourceVariableNames.OrderBy(x => x))) : "",
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(flow)) flow = Strings.U_Ai_PrNoAdvancedFlow;
                string workerCoverage = Variable.Storage == 0x56
                    ? string.Format(Strings.AiPhasePrivateCoverage, string.Join(", ", script.Workers.Select(w =>
                        w.PrivateDataLength >= Variable.Slot + 4 ? $"w{w.Index} OK" : string.Format(Strings.AiPhaseWorkerTooSmall, w.Index))))
                    : Strings.U_Ai_PrNonLocalStorage;
                AiVariableSemanticAnnotation? ctx =
                    semanticMap?.TryGetValue(Variable.Index, out AiVariableSemanticAnnotation? c) == true
                        ? c
                        : InferGenericAnnotation();
                return new AiPhaseVariableAuditRow(
                    aliasKey,
                    Variable.Name,
                    metadata.Alias ?? string.Empty,
                    aiHash,
                    Variable.Index,
                    Variable.Storage,
                    Variable.Slot,
                    Variable.TypeId,
                    metadata.PreferredVariableIndex,
                    $"ID{Variable.Index} · var[{Variable.Index}] · slot 0x{Variable.Slot:X4}",
                    storage,
                    badge,
                    Score,
                    workers,
                    FirstOffset < 0 ? "sem offset" : $"0x{FirstOffset:X4}..0x{LastOffset:X4}",
                    hints,
                    flow,
                    workerCoverage,
                    recommendation,
                    ctx)
                {
                    HasDataDependency = SourceVariableNames.Count > 0,
                    HasSwitchUsage = SwitchHints > 0,
                };
            }
        }
    }

    internal readonly record struct PhaseLoadUse(bool HasCondition, bool HasCompare, bool HasSwitch);

    internal sealed class AiPhaseVariableAuditRow : ObservableObject
    {
        string alias;

        public AiPhaseVariableAuditRow(
            string aliasKey,
            string rawVariableName,
            string alias,
            string aiHash,
            int variableIndex,
            byte storageCode,
            int variableSlot,
            int typeId,
            int? preferredVariableIndex,
            string indexLabel,
            string storageLabel,
            string badge,
            int score,
            string workersSummary,
            string offsetSummary,
            string evidenceSummary,
            string flowSummary,
            string workerCoverageSummary,
            string recommendation,
            AiVariableSemanticAnnotation? semanticContext = null)
        {
            AliasKey = aliasKey;
            RawVariableName = rawVariableName;
            this.alias = alias;
            AiHash = aiHash;
            VariableIndex = variableIndex;
            StorageCode = storageCode;
            VariableSlot = variableSlot;
            TypeId = typeId;
            PreferredVariableIndex = preferredVariableIndex;
            IndexLabel = indexLabel;
            StorageLabel = storageLabel;
            Badge = badge;
            Score = score;
            WorkersSummary = workersSummary;
            OffsetSummary = offsetSummary;
            EvidenceSummary = evidenceSummary;
            FlowSummary = flowSummary;
            WorkerCoverageSummary = workerCoverageSummary;
            Recommendation = recommendation;
            SemanticContext = semanticContext;
        }

        public string AliasKey { get; }
        public string RawVariableName { get; }
        public string Alias => alias;
        public string AiHash { get; }
        public int VariableIndex { get; }
        public byte StorageCode { get; }
        public int VariableSlot { get; }
        public int TypeId { get; }
        public int? PreferredVariableIndex { get; private set; }
        public bool HasPreferredVariableIndex => PreferredVariableIndex.HasValue && PreferredVariableIndex.Value != VariableIndex;
        public int EffectiveVariableIndex => PreferredVariableIndex ?? VariableIndex;
        public string VariableIdLabel => $"ID{EffectiveVariableIndex}";
        public string ActualVariableIdLabel => HasPreferredVariableIndex ? string.Format(Strings.AiPhaseActualId, VariableIndex) : Strings.AiPhaseUsesActualId;
        public string VariableName => string.IsNullOrWhiteSpace(alias) ? RawVariableName : alias;
        public string AliasLabel => string.IsNullOrWhiteSpace(alias) ? Strings.AiPhaseNoAlias : string.Format(Strings.AiPhaseAliasOf, RawVariableName);
        public string IndexLabel { get; }
        public string StorageLabel { get; }
        public string Badge { get; }
        public int Score { get; }
        public string WorkersSummary { get; }
        public string OffsetSummary { get; }
        public string EvidenceSummary { get; }
        public string FlowSummary { get; }
        public bool HasDataDependency { get; init; }
        public bool HasSwitchUsage { get; init; }
        public string WorkerCoverageSummary { get; }
        public string Recommendation { get; }

        public AiVariableSemanticAnnotation? SemanticContext { get; }
        public bool HasSemanticContext => SemanticContext != null;
        public string SemanticLabel => SemanticContext?.Label ?? string.Empty;
        public string SemanticCalculation => SemanticContext?.CalculationSummary ?? string.Empty;
        public string SemanticFullDisplay => SemanticContext?.FullDisplay ?? string.Empty;

        public void SetAlias(string value)
        {
            if (!SetProperty(ref alias, value ?? string.Empty, nameof(Alias))) return;
            OnPropertyChanged(nameof(VariableName));
            OnPropertyChanged(nameof(AliasLabel));
        }

        public void SetPreferredVariableIndex(int value)
        {
            if (PreferredVariableIndex == value) return;
            PreferredVariableIndex = value;
            OnPropertyChanged(nameof(PreferredVariableIndex));
            OnPropertyChanged(nameof(HasPreferredVariableIndex));
            OnPropertyChanged(nameof(EffectiveVariableIndex));
            OnPropertyChanged(nameof(VariableIdLabel));
            OnPropertyChanged(nameof(ActualVariableIdLabel));
        }

        public void ClearPreferredVariableIndex()
        {
            if (!PreferredVariableIndex.HasValue) return;
            PreferredVariableIndex = null;
            OnPropertyChanged(nameof(PreferredVariableIndex));
            OnPropertyChanged(nameof(HasPreferredVariableIndex));
            OnPropertyChanged(nameof(EffectiveVariableIndex));
            OnPropertyChanged(nameof(VariableIdLabel));
            OnPropertyChanged(nameof(ActualVariableIdLabel));
        }

        public override string ToString() => $"{VariableIdLabel} · {VariableName} · {Badge}";
    }

    internal static class PhaseVarAliasStore
    {
        static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "monster-ai-var-aliases.json");

        static readonly Dictionary<string, PhaseVarAliasEntry> Aliases = Load();

        public static string Key(string monsterId, byte storage, int slot) =>
            $"{monsterId.ToLowerInvariant()}|{storage:X2}|{slot:X6}";

        public static PhaseVarAliasEntry GetMetadata(string key) =>
            Aliases.TryGetValue(key, out PhaseVarAliasEntry? entry)
                ? entry
                : new PhaseVarAliasEntry(string.Empty, null, string.Empty, DateTime.MinValue);

        public static void SetMetadata(string key, string alias, int? preferredVariableIndex, string aiHash)
        {
            alias = (alias ?? string.Empty).Trim();
            bool hasAlias = !string.IsNullOrWhiteSpace(alias);
            bool hasPreferred = preferredVariableIndex.HasValue;
            if (!hasAlias && !hasPreferred)
                Aliases.Remove(key);
            else
                Aliases[key] = new PhaseVarAliasEntry(alias, preferredVariableIndex, aiHash, DateTime.UtcNow);
            Save();
        }

        static Dictionary<string, PhaseVarAliasEntry> Load()
        {
            try
            {
                if (File.Exists(StorePath))
                    return JsonSerializer.Deserialize<Dictionary<string, PhaseVarAliasEntry>>(File.ReadAllText(StorePath)) ?? new();
            }
            catch { /* metadata convenience only */ }
            return new();
        }

        static void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(Aliases, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* metadata convenience only */ }
        }
    }

    internal sealed record PhaseVarAliasEntry(string Alias, int? PreferredVariableIndex, string AiHash, DateTime UpdatedUtc);

    internal sealed class AiPhaseConditionClauseVm : ObservableObject
    {
        readonly Action changed;
        int number;
        string join = "E";
        AiPhaseVariableAuditRow? selectedVariable;
        string op = ">";
        string value = "0";

        public AiPhaseConditionClauseVm(
            int number,
            Action changed,
            ObservableCollection<AiPhaseVariableAuditRow> variableOptions,
            IReadOnlyList<string> operatorOptions,
            IReadOnlyList<string> joinOptions)
        {
            this.number = number;
            this.changed = changed;
            VariableOptions = variableOptions;
            OperatorOptions = operatorOptions;
            JoinOptions = joinOptions;
        }

        public ObservableCollection<AiPhaseVariableAuditRow> VariableOptions { get; }
        public IReadOnlyList<string> OperatorOptions { get; }
        public IReadOnlyList<string> JoinOptions { get; }

        public int Number
        {
            get => number;
            set
            {
                if (!SetProperty(ref number, value)) return;
                OnPropertyChanged(nameof(JoinEnabled));
                OnPropertyChanged(nameof(Label));
            }
        }

        public string Join
        {
            get => join;
            set
            {
                if (!SetProperty(ref join, value ?? "E")) return;
                OnPropertyChanged(nameof(ExpressionText));
                changed();
            }
        }

        public AiPhaseVariableAuditRow? SelectedVariable
        {
            get => selectedVariable;
            set
            {
                if (!SetProperty(ref selectedVariable, value)) return;
                OnPropertyChanged(nameof(ExpressionText));
                changed();
            }
        }

        public string Operator
        {
            get => op;
            set
            {
                if (!SetProperty(ref op, value ?? "==")) return;
                OnPropertyChanged(nameof(ExpressionText));
                changed();
            }
        }

        public string Value
        {
            get => value;
            set
            {
                if (!SetProperty(ref this.value, value ?? "0")) return;
                OnPropertyChanged(nameof(ExpressionText));
                changed();
            }
        }

        public bool JoinEnabled => Number > 1;
        public string Label => string.Format(Strings.U_Ai_ConditionNumber, Number);
        public string ExpressionText => $"{SelectedVariable?.VariableName ?? "var"} {Operator} {Value}";
    }

    internal sealed record PhaseDraftSnapshot(
        string? DefaultAdvanceVariableKey,
        string DefaultAdvanceMin,
        string DefaultAdvanceMax,
        string FinalLimit,
        bool ResetAfterFinal,
        IReadOnlyList<PhaseConditionClauseSnapshot> ConditionBuilderClauses,
        IReadOnlyList<PhaseDraftStepSnapshot> Steps,
        bool PhaseConditionUseHpBelowGuard = false,
        string PhaseConditionHpBelowPercentText = "50");

    internal sealed record PhaseConditionClauseSnapshot(
        string? VariableKey,
        string Operator,
        string Value,
        string Join);

    internal sealed record PhaseDraftStepSnapshot(
        bool IsFinalPhase,
        ushort? AbilityOperand,
        AiPhaseTriggerKind TriggerKind,
        PhaseTargetSnapshot? Target,
        bool StopHere,
        bool UseChance,
        string ChanceKText,
        string ConditionSummary,
        IReadOnlyList<AiInstruction>? ConditionGuard,
        string? SimpleConditionVariableKey,
        string SimpleConditionOperator,
        string SimpleConditionValue,
        bool UseHpBelowGuard,
        string HpBelowPercentText,
        bool UseForbiddenRite,
        PhaseTargetSnapshot? ForbiddenTarget,
        ushort? ForbiddenStatusFieldId,
        string ForbiddenValue,
        IReadOnlyList<PhaseVarAdvanceSnapshot> Advances);

    internal sealed record PhaseTargetSnapshot(ushort Operand, AiTargetRecipeKind Kind, bool UsePhaseTarget = false);

    internal sealed record PhaseVarAdvanceSnapshot(string? VariableKey, string MinText, string MaxText, bool IsReset);

    internal sealed record PhaseEvidenceContext(string HookLabel, string GuardLabel);

    internal sealed class PhaseVariableEvidenceAccumulator
    {
        readonly Dictionary<string, AiPhaseVarIndirectDispatchLinkVm> indirectDispatchUnitsById = new(StringComparer.OrdinalIgnoreCase);

        public PhaseVariableEvidenceAccumulator(string hookLabel, string guardLabel)
        {
            HookLabel = hookLabel;
            GuardLabel = guardLabel;
        }

        public string HookLabel { get; }
        public string GuardLabel { get; }
        public HashSet<string> Effects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Writes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Consumers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NextStates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Notes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public SortedSet<int> Offsets { get; } = new();
        public IReadOnlyList<AiPhaseVarIndirectDispatchLinkVm> IndirectDispatchUnits =>
            indirectDispatchUnitsById.Values
                .OrderBy(unit => unit.RouteTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        public string SortKey => $"{HookLabel}|{GuardLabel}";
        public int FactCount => Effects.Count + Writes.Count + Consumers.Count + NextStates.Count + Notes.Count + indirectDispatchUnitsById.Count;
        public string Badge => FactCount == 1 ? "1 pista" : $"{FactCount} pistas";
        public string WriteSummary => JoinFacts(Writes);
        public string ConsumerSummary => JoinFacts(Consumers);
        public string NextStateSummary => JoinFacts(NextStates);
        public string NoteSummary => JoinFacts(Notes);
        public string OffsetSummary => Offsets.Count == 0
            ? "sem offsets"
            : "offsets " + string.Join(", ", Offsets.Select(o => $"0x{o:X4}"));
        public string TechnicalSummary =>
            $"guard: {GuardLabel}" +
            (Writes.Count > 0 ? $" · writes {Writes.Count}" : string.Empty) +
            (Consumers.Count > 0 ? $" · consumers {Consumers.Count}" : string.Empty) +
            (NextStates.Count > 0 ? $" · next-state {NextStates.Count}" : string.Empty) +
            (indirectDispatchUnitsById.Count > 0 ? $" · familias indiretas {indirectDispatchUnitsById.Count}" : string.Empty);

        public bool TryAddIndirectDispatchUnit(AiPhaseVarIndirectDispatchLinkVm unit)
        {
            if (indirectDispatchUnitsById.ContainsKey(unit.UnitId))
                return false;

            indirectDispatchUnitsById[unit.UnitId] = unit;
            return true;
        }

        static string JoinFacts(IEnumerable<string> facts) =>
            string.Join("; ", facts
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    internal sealed class AiPhaseVarLinkedBlockVm
    {
        public AiPhaseVarLinkedBlockVm(
            string hookLabel,
            string badge,
            string guardSummary,
            string effectSummary,
            string writeSummary,
            string consumerSummary,
            string nextStateSummary,
            string noteSummary,
            string offsetSummary,
            string technicalSummary,
            IReadOnlyList<AiPhaseVarIndirectDispatchLinkVm>? indirectDispatchUnits = null)
        {
            HookLabel = hookLabel;
            Badge = badge;
            GuardSummary = guardSummary;
            EffectSummary = effectSummary;
            WriteSummary = writeSummary;
            ConsumerSummary = consumerSummary;
            NextStateSummary = nextStateSummary;
            NoteSummary = noteSummary;
            OffsetSummary = offsetSummary;
            TechnicalSummary = technicalSummary;
            IndirectDispatchUnits = indirectDispatchUnits ?? Array.Empty<AiPhaseVarIndirectDispatchLinkVm>();
        }

        public string HookLabel { get; }
        public string Badge { get; }
        public string GuardSummary { get; }
        public string EffectSummary { get; }
        public bool HasEffectSummary => !string.IsNullOrWhiteSpace(EffectSummary);
        public string WriteSummary { get; }
        public bool HasWriteSummary => !string.IsNullOrWhiteSpace(WriteSummary);
        public string ConsumerSummary { get; }
        public bool HasConsumerSummary => !string.IsNullOrWhiteSpace(ConsumerSummary);
        public string NextStateSummary { get; }
        public bool HasNextStateSummary => !string.IsNullOrWhiteSpace(NextStateSummary);
        public string NoteSummary { get; }
        public bool HasNoteSummary => !string.IsNullOrWhiteSpace(NoteSummary);
        public string OffsetSummary { get; }
        public string TechnicalSummary { get; }
        public IReadOnlyList<AiPhaseVarIndirectDispatchLinkVm> IndirectDispatchUnits { get; }
        public bool HasIndirectDispatchUnits => IndirectDispatchUnits.Count > 0;
    }

    internal sealed record DetectedPhaseDraftBlock(
        int Offset,
        bool IsFinal,
        int Number,
        int FinalLimit,
        ushort AbilityOperand,
        AiPhaseTriggerKind TriggerKind,
        PhaseTargetSnapshot Target,
        bool StopHere,
        bool UseChance,
        int ChanceK,
        string ConditionSummary,
        IReadOnlyList<AiInstruction>? ConditionGuard,
        PhaseForbiddenRiteSnapshot? ForbiddenRite,
        IReadOnlyList<PhaseVarAdvanceSnapshot> Advances)
    {
        public PhaseDraftStepSnapshot ToSnapshot() => new(
            IsFinal,
            AbilityOperand,
            TriggerKind,
            Target,
            StopHere,
            UseChance,
            ChanceK.ToString(),
            ConditionSummary,
            ConditionGuard,
            null,
            "==",
            string.Empty,
            false,
            "50",
            ForbiddenRite != null,
            ForbiddenRite?.Target,
            ForbiddenRite?.FieldId,
            ForbiddenRite?.Value ?? "1",
            Advances);
    }

    internal sealed record PhaseForbiddenRiteSnapshot(PhaseTargetSnapshot? Target, ushort FieldId, string Value);

    internal sealed record AiPhaseTriggerOption(
        AiPhaseTriggerKind Kind,
        string Label,
        string Summary,
        string Badge);

    internal sealed class AiPhaseVarAdvanceVm : ObservableObject
    {
        readonly Action changed;
        int number;
        AiPhaseVariableAuditRow? variable;
        string minText;
        string maxText;
        bool isReset;

        public AiPhaseVarAdvanceVm(
            int number,
            AiPhaseVariableAuditRow? variable,
            string minText,
            string maxText,
            bool isReset,
            Action changed)
        {
            this.number = number;
            this.variable = variable;
            this.minText = minText;
            this.maxText = maxText;
            this.isReset = isReset;
            this.changed = changed;
        }

        public int Number
        {
            get => number;
            set
            {
                if (!SetProperty(ref number, value)) return;
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(Summary));
            }
        }

        public AiPhaseVariableAuditRow? Variable
        {
            get => variable;
            set
            {
                if (!SetProperty(ref variable, value)) return;
                OnPropertyChanged(nameof(Summary));
                changed();
            }
        }

        public string MinText
        {
            get => minText;
            set
            {
                if (!SetProperty(ref minText, value ?? "0")) return;
                OnPropertyChanged(nameof(Summary));
                changed();
            }
        }

        public string MaxText
        {
            get => maxText;
            set
            {
                if (!SetProperty(ref maxText, value ?? "0")) return;
                OnPropertyChanged(nameof(Summary));
                changed();
            }
        }

        public bool IsReset
        {
            get => isReset;
            set
            {
                if (!SetProperty(ref isReset, value)) return;
                OnPropertyChanged(nameof(Summary));
                changed();
            }
        }

        public string Label => string.Format(Strings.U_Ai_AdvanceNumber, Number);
        public string Summary => IsReset
            ? $"{Variable?.VariableName ?? "var"} = 0"
            : MinText == MaxText
            ? $"{Variable?.VariableName ?? "var"} += {MinText}"
            : $"{Variable?.VariableName ?? "var"} += {MinText}..{MaxText}";
    }

    internal sealed class AiPhaseDraftStepVm : ObservableObject
    {
        readonly Action changed;
        int number;
        AiCommandOption? selectedAbility;
        AiPhaseTriggerOption? selectedTriggerOption;
        AiTargetOption? selectedTarget;
        bool stopHere = true;
        bool useChance;
        string chanceKText = "2";
        bool useHpBelowGuard;
        string hpBelowPercentText = "50";
        bool useForbiddenRite;
        AiTargetOption? forbiddenTarget;
        AiForbiddenStatusPreset? forbiddenStatus;
        string forbiddenValue = "1";
        string conditionSummary = string.Empty;
        IReadOnlyList<AiInstruction>? conditionGuard;
        string? simpleConditionVariableKey;
        string simpleConditionOperator = "==";
        string simpleConditionValue = string.Empty;
        string counterRuleSummary = Strings.F2_guard_counter_not_chosen_yet_3ea3fdcd;
        string advanceRuleSummary = Strings.F2_when_running_advance_not_configured_yet_6077573b;

        public AiPhaseDraftStepVm(int number, bool isFinalPhase, Action changed)
        {
            this.number = number;
            IsFinalPhase = isFinalPhase;
            this.changed = changed;
        }

        public ObservableCollection<AiPhaseVarAdvanceVm> VarAdvances { get; } = new();

        public int Number
        {
            get => number;
            set
            {
                if (!SetProperty(ref number, value)) return;
                OnPropertyChanged(nameof(StepLabel));
            }
        }

        public bool IsFinalPhase { get; }
        public string StepLabel => IsFinalPhase ? Strings.U_Ai_LastPhase : string.Format(Strings.U_Ai_PrPhaseLabel, Number);
        public string PreviewLabel => string.IsNullOrWhiteSpace(ConditionSummary)
            ? $"{StepLabel}: {Title}{TriggerSuffix}{TargetSuffix}{ChanceSuffix}{HpGuardSuffix}{ForbiddenRiteSuffix}{VarAdvancePreviewSuffix}"
            : $"{StepLabel}: {Title}{TriggerSuffix}{TargetSuffix} se {ConditionSummary}{ChanceSuffix}{HpGuardSuffix}{ForbiddenRiteSuffix}{VarAdvancePreviewSuffix}";

        public AiCommandOption? SelectedAbility
        {
            get => selectedAbility;
            set
            {
                // Avalonia ComboBox can briefly push null while its ItemsSource is rebuilt by the search/filter.
                // A phase with a chosen ability should not be erased by that transient UI state.
                if (value == null && selectedAbility != null)
                    return;

                if (!SetProperty(ref selectedAbility, value)) return;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public AiPhaseTriggerOption? SelectedTriggerOption
        {
            get => selectedTriggerOption;
            set
            {
                if (!SetProperty(ref selectedTriggerOption, value)) return;
                OnPropertyChanged(nameof(TriggerKind));
                OnPropertyChanged(nameof(TriggerSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public AiPhaseTriggerKind TriggerKind => SelectedTriggerOption?.Kind ?? AiPhaseTriggerKind.OnTurn;

        public AiTargetOption? SelectedTarget
        {
            get => selectedTarget;
            set
            {
                if (!SetProperty(ref selectedTarget, value)) return;
                OnPropertyChanged(nameof(TargetSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public bool UseChance
        {
            get => useChance;
            set
            {
                if (!SetProperty(ref useChance, value)) return;
                OnPropertyChanged(nameof(ChanceSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public string ChanceKText
        {
            get => chanceKText;
            set
            {
                if (!SetProperty(ref chanceKText, value ?? "2")) return;
                OnPropertyChanged(nameof(ChanceSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public bool UseHpBelowGuard
        {
            get => useHpBelowGuard;
            set
            {
                if (!SetProperty(ref useHpBelowGuard, value)) return;
                OnPropertyChanged(nameof(HpGuardSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public string HpBelowPercentText
        {
            get => hpBelowPercentText;
            set
            {
                if (!SetProperty(ref hpBelowPercentText, value ?? "50")) return;
                OnPropertyChanged(nameof(HpGuardSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public bool UseForbiddenRite
        {
            get => useForbiddenRite;
            set
            {
                if (!SetProperty(ref useForbiddenRite, value)) return;
                OnPropertyChanged(nameof(ForbiddenRiteSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public AiTargetOption? ForbiddenTarget
        {
            get => forbiddenTarget;
            set
            {
                if (!SetProperty(ref forbiddenTarget, value)) return;
                OnPropertyChanged(nameof(ForbiddenRiteSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public AiForbiddenStatusPreset? ForbiddenStatus
        {
            get => forbiddenStatus;
            set
            {
                if (!SetProperty(ref forbiddenStatus, value)) return;
                if (value != null)
                    ForbiddenValue = value.DefaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(ForbiddenRiteSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public string ForbiddenValue
        {
            get => forbiddenValue;
            set
            {
                if (!SetProperty(ref forbiddenValue, value ?? "1")) return;
                OnPropertyChanged(nameof(ForbiddenRiteSummary));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public bool StopHere
        {
            get => stopHere;
            set
            {
                if (!SetProperty(ref stopHere, value)) return;
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                changed();
            }
        }

        public string ConditionSummary
        {
            get => conditionSummary;
            set
            {
                if (!SetProperty(ref conditionSummary, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PreviewLabel));
                OnPropertyChanged(nameof(GuardModeSummary));
                changed();
            }
        }

        public string? SimpleConditionVariableKey
        {
            get => simpleConditionVariableKey;
            set
            {
                if (!SetProperty(ref simpleConditionVariableKey, value)) return;
                changed();
            }
        }

        public string SimpleConditionOperator
        {
            get => simpleConditionOperator;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? "==" : value;
                if (!SetProperty(ref simpleConditionOperator, value)) return;
                changed();
            }
        }

        public string SimpleConditionValue
        {
            get => simpleConditionValue;
            set
            {
                if (!SetProperty(ref simpleConditionValue, value ?? string.Empty)) return;
                changed();
            }
        }

        public string CounterRuleSummary
        {
            get => counterRuleSummary;
            set => SetProperty(ref counterRuleSummary, value ?? string.Empty);
        }

        public string AdvanceRuleSummary
        {
            get => advanceRuleSummary;
            set => SetProperty(ref advanceRuleSummary, value ?? string.Empty);
        }

        public IReadOnlyList<AiInstruction>? ConditionGuard
        {
            get => conditionGuard;
            set
            {
                conditionGuard = value == null ? null : value.Select(MonsterAiEditor_DataModel.ClonePhaseInstruction).ToList();
                changed();
            }
        }

        public string Title => SelectedAbility?.Name ?? Strings.U_Ai_CaPickSkill;
        public string GuardModeSummary => string.IsNullOrWhiteSpace(ConditionSummary)
            ? Strings.F2_condition_for_this_ability_phase_uses_th_6ef1f9ec
            : string.Format(Strings.U_Ai_PrConditionOfAbility, ConditionSummary);
        public string TriggerSummary => SelectedTriggerOption?.Summary
            ?? Strings.F2_runs_on_the_monster_s_common_turn_b92f6a04;
        public string Subtitle => IsFinalPhase
            ? $"{(StopHere ? Strings.U_Ai_PrSubtitleFinalStop : Strings.U_Ai_PrSubtitleFinalContinue)}{TriggerSuffix}{TargetSuffix}{ChanceSuffix}{HpGuardSuffix}{ConditionSuffix}{ForbiddenRiteSuffix}"
            : $"{(StopHere ? Strings.U_Ai_PrSubtitleCommonStop : Strings.U_Ai_PrSubtitleCommonContinue)}{TriggerSuffix}{TargetSuffix}{ChanceSuffix}{HpGuardSuffix}{ConditionSuffix}{ForbiddenRiteSuffix}";

        string ConditionSuffix => string.IsNullOrWhiteSpace(ConditionSummary) ? "" : string.Format(Strings.U_Ai_PrSe, ConditionSummary);
        string HpGuardSuffix => UseHpBelowGuard ? $" · HP < {HpBelowPercentText}%" : "";
        string ForbiddenRiteSuffix => UseForbiddenRite ? string.Format(Strings.U_Ai_PrRiteStatus, ForbiddenStatus?.Name ?? "status") : "";
        string TriggerSuffix => SelectedTriggerOption == null ? "" : $" · {SelectedTriggerOption.Badge}";
        string TargetSuffix => SelectedTarget == null ? "" : string.Format(Strings.U_Ai_PrTargetSuffix, SelectedTarget.Label);
        string ChanceSuffix => UseChance ? string.Format(Strings.U_Ai_PrChanceSuffix, ChanceKText) : "";
        public string TargetSummary => SelectedTarget?.Label ?? Strings.U_Ai_PrNoTarget;
        public string ChanceSummary => UseChance ? string.Format(Strings.U_Ai_PrChanceSummary, ChanceKText) : Strings.F2_whenever_the_guard_passes_82f2e9f2;
        public string HpGuardSummary => UseHpBelowGuard
            ? string.Format(Strings.U_Ai_PrHpGuardSummary, HpBelowPercentText)
            : Strings.U_Ai_PrNoHpGuard;
        public string ForbiddenRiteSummary => UseForbiddenRite
            ? string.Format(Strings.U_Ai_PrForbiddenRiteSummary, ForbiddenStatus?.Name ?? "status", ForbiddenValue, ForbiddenTarget?.Label ?? "target")
            : Strings.F2_disabled_in_this_phase_forbidden_rite_ma_aebfb4fd;
        string VarAdvancePreviewSuffix => VarAdvances.Count == 0 ? "" : $" · {VarAdvanceSummary}";
        public string VarAdvanceSummary => VarAdvances.Count == 0
            ? Strings.F2_on_run_does_not_change_variable_automati_6b8cd513
            : string.Format(Strings.U_Ai_PrVarAdvanceOnRun, string.Join("; ", VarAdvances.Select(v => v.Summary)));

        public void NotifyVarAdvancesChanged()
        {
            OnPropertyChanged(nameof(VarAdvanceSummary));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(PreviewLabel));
        }
    }
}
