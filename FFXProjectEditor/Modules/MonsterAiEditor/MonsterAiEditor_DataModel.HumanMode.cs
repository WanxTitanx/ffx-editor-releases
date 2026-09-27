using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        static readonly MonsterAiHumanModeChoice[] HumanModeChoiceSet =
        {
            new(MonsterAiHumanMode.Normal, "Normal", Strings.U_Ai_HmNormalDesc),
            new(MonsterAiHumanMode.Avancado, "Avancado", Strings.U_Ai_HmAdvancedDesc),
            new(MonsterAiHumanMode.DevKit, "DevKit", Strings.U_Ai_HmDevKitDesc),
        };

        readonly HashSet<int> _batchSelectedActionOffsets = new();
        AiScriptFile? _batchSelectionScript;

        public IReadOnlyList<MonsterAiHumanModeChoice> HumanModeChoices => HumanModeChoiceSet;

        [ObservableProperty] private MonsterAiHumanModeChoice? selectedHumanMode = HumanModeChoiceSet[0];
        [ObservableProperty] private string batchActionSummary = Strings.U_Ai_HmNoBatchSelection;

        public MonsterAiHumanMode HumanMode => SelectedHumanMode?.Mode ?? MonsterAiHumanMode.Normal;
        public string HumanModeSummary => SelectedHumanMode?.Summary ?? HumanModeChoiceSet[0].Summary;
        public bool IsNormalMode => HumanMode == MonsterAiHumanMode.Normal;
        public bool IsAdvancedMode => HumanMode == MonsterAiHumanMode.Avancado;
        public bool IsDevKitMode => HumanMode == MonsterAiHumanMode.DevKit;
        public bool ShowsAdvancedTools => HumanMode != MonsterAiHumanMode.Normal;
        public bool HasBatchSelection => _batchSelectedActionOffsets.Count > 0;
        public bool HasMultiBatchSelection => _batchSelectedActionOffsets.Count > 1;
        public int BatchSelectionCount => _batchSelectedActionOffsets.Count;

        public void SetHumanMode(MonsterAiHumanMode mode)
        {
            SelectedHumanMode = HumanModeChoices.FirstOrDefault(c => c.Mode == mode) ?? HumanModeChoiceSet[0];
        }

        partial void OnSelectedHumanModeChanged(MonsterAiHumanModeChoice? value)
        {
            OnPropertyChanged(nameof(HumanMode));
            OnPropertyChanged(nameof(HumanModeSummary));
            OnPropertyChanged(nameof(IsNormalMode));
            OnPropertyChanged(nameof(IsAdvancedMode));
            OnPropertyChanged(nameof(IsDevKitMode));
            OnPropertyChanged(nameof(ShowsAdvancedTools));
            RebuildAutomationActions();
        }

        void ResetBatchSelectionScopeIfNeeded()
        {
            if (ReferenceEquals(_batchSelectionScript, selectedScript)) return;
            _batchSelectionScript = selectedScript;
            _batchSelectedActionOffsets.Clear();
            RefreshBatchSelectionState();
        }

        void ReconcileBatchSelectionWithDetectedActions()
        {
            if (_batchSelectedActionOffsets.Count == 0)
            {
                RefreshBatchSelectionState();
                return;
            }

            HashSet<int> validOffsets = _allDetectedActions.Select(a => a.CallOffset).ToHashSet();
            _batchSelectedActionOffsets.RemoveWhere(offset => !validOffsets.Contains(offset));
            RefreshBatchSelectionState();
        }

        bool IsActionBatchSelected(AiDetectedAction action) =>
            _batchSelectedActionOffsets.Contains(action.CallOffset);

        void OnActionBatchSelectionChanged(AiActionVm action, bool selected)
        {
            if (selected)
                _batchSelectedActionOffsets.Add(action.Action.CallOffset);
            else
                _batchSelectedActionOffsets.Remove(action.Action.CallOffset);

            RefreshBatchSelectionState();
        }

        public void ClearBatchSelection()
        {
            _batchSelectedActionOffsets.Clear();
            foreach (AiActionVm action in AutomationActions)
                action.SetBatchSelectedFromModel(false);
            RefreshBatchSelectionState();
            RemoveActionSummary = Strings.U_Ai_HmCleared;
        }

        public void RemoveActionOrBatch()
        {
            if (!HasBatchSelection) { RemoveSelectedAction(); return; }
            RemoveBatchActions();
        }

        public void ChangeActionOrBatchToPickedAbility()
        {
            if (!HasBatchSelection) { ChangeSelectedActionToPickedAbility(); return; }
            ChangeBatchActionsToPickedAbility();
        }

        public void DuplicateActionOrBatch()
        {
            if (!HasBatchSelection) { DuplicateSelectedAction(); return; }
            DuplicateBatchActions();
        }

        public void InsertSecondAbilityOrBatch()
        {
            if (!HasBatchSelection) { InsertSecondAbility(); return; }
            InsertSecondAbilityForBatch();
        }

        public void MoveActionOrBatchToHook()
        {
            if (!HasBatchSelection) { MoveSelectedActionToHook(); return; }
            MoveBatchActionsToHook();
        }

        public void ToggleForceActionOrBatch()
        {
            if (!HasBatchSelection) { ToggleForceSelectedAction(); return; }
            ToggleForceBatchActions();
        }

        void RemoveBatchActions()
        {
            if (!TryGetBatchActions("remover em lote", out List<AiDetectedAction> actions)) return;
            if (actions.Any(a => !a.Removable))
            {
                SetBatchError(Strings.F2_batch_removal_blocked_one_of_the_selecte_18518fc0);
                return;
            }

            var drop = actions.SelectMany(a => a.RemoveOffsets).ToHashSet();
            List<AiInstruction> kept = selectedScript!.Instructions.Where(i => !drop.Contains(i.Offset)).ToList();
            string names = BatchNames(actions);
            ApplyAndSave(kept,
                string.Format(Strings.U_Ai_HmRemovedBatch, actions.Count, names, System.IO.Path.GetFileName(selectedPath)),
                -1);
            FinishBatchAfterWrite(clearSelection: true);
        }

        void ChangeBatchActionsToPickedAbility()
        {
            if (!TryGetBatchActions("trocar em lote", out List<AiDetectedAction> actions)) return;
            AiCommandOption? to = SelectedAutomationAbility;
            if (to == null) { SetBatchError(Strings.F2_choose_the_target_ability_before_batch_s_e985d7c9); return; }
            if (actions.Any(a => a.Kind != AiActionKind.Command || a.CmdPushOffset < 0))
            {
                SetBatchError(Strings.F2_batch_switching_only_applies_to_command_57852839);
                return;
            }

            if (LinkForbiddenRiteToBehavior)
            {
                string riteError = string.Empty;
                if (!TryGetForbiddenRitePayload(out AiTargetOption riteTarget, out AiForbiddenStatusPreset riteStatus, out ushort riteValue, out riteError))
                {
                    SetBatchError(riteError);
                    return;
                }
                if (!TryResolveForbiddenRiteTargetsForBatch(riteTarget, actions, out Dictionary<int, ushort> riteTargetsByCall, out string riteTargetLabel, out riteError))
                {
                    SetBatchError(riteError);
                    return;
                }
                if (!TryApplySequentialBatchListMutation(actions,
                        (script, action) => AiAutomation.ChangeActionAndInsertChrPropertyWrite(script, action, to.Operand, riteTargetsByCall[action.CallOffset], riteStatus.FieldId, riteValue),
                        out byte[] finalAi,
                        out string error))
                {
                    SetBatchError(error);
                    return;
                }

                string linkedNames = BatchNames(actions);
                if (!SaveNewAi(finalAi, out string saveErr)) { SetBatchError(saveErr); return; }
                RemoveActionSummary =
                    string.Format(Strings.U_Ai_HmSwappedBatch, actions.Count, to.Name, linkedNames) +
                    LinkedForbiddenRiteNote(riteTargetLabel, riteStatus, riteValue) +
                    $"{ContextNoteForActions(actions)} ⚠️ Confirme in-game (RT2).";
                ClearBatchSelectionAfterSuccessfulWrite();
                return;
            }

            var commandSlots = actions.Select(a => a.CmdPushOffset).ToHashSet();
            List<AiInstruction> changed = selectedScript!.Instructions.Select(ins =>
                commandSlots.Contains(ins.Offset)
                    ? new AiInstruction
                    {
                        Offset = ins.Offset,
                        Opcode = ins.Opcode,
                        HasOperand = ins.HasOperand,
                        Operand = to.Operand,
                        OperandKind = ins.OperandKind,
                    }
                    : ins).ToList();

            IReadOnlyList<int> keepCalls = actions.Select(a => a.CallOffset).ToList();
            string names = BatchNames(actions);
            ApplyAndSave(changed,
                string.Format(Strings.U_Ai_HmSwappedBatch2, actions.Count, to.Name, names, System.IO.Path.GetFileName(selectedPath), ContextNoteForActions(actions)),
                -1);
            FinishBatchAfterWrite(clearSelection: false, keepCalls: keepCalls);
        }

        void ToggleForceBatchActions()
        {
            if (!TryGetBatchActions("alternar imediato/fila em lote", out List<AiDetectedAction> actions)) return;
            if (actions.Any(a => a.Kind != AiActionKind.Command))
            {
                SetBatchError("Batch Immediate/Queue only applies to commands. Buffs/stats do not use performCommand.");
                return;
            }

            var calls = actions.ToDictionary(a => a.CallOffset, a => a.ForcePerform ? (ushort)0x700B : (ushort)0x705A);
            List<AiInstruction> changed = selectedScript!.Instructions.Select(ins =>
                calls.TryGetValue(ins.Offset, out ushort newCall)
                    ? new AiInstruction
                    {
                        Offset = ins.Offset,
                        Opcode = ins.Opcode,
                        HasOperand = ins.HasOperand,
                        Operand = newCall,
                        OperandKind = ins.OperandKind,
                    }
                    : ins).ToList();

            IReadOnlyList<int> keepCalls = actions.Select(a => a.CallOffset).ToList();
            string names = BatchNames(actions);
            ApplyAndSave(changed,
                string.Format(Strings.U_Ai_HmToggledBatch, actions.Count, names, System.IO.Path.GetFileName(selectedPath), ContextNoteForActions(actions)),
                -1);
            FinishBatchAfterWrite(clearSelection: false, keepCalls: keepCalls);
        }

        void DuplicateBatchActions()
        {
            if (!TryGetBatchActions("duplicar em lote", out List<AiDetectedAction> actions)) return;
            if (actions.Any(a => !a.Removable))
            {
                SetBatchError(Strings.U_Ai_HmDuplicateBlocked);
                return;
            }

            if (!TryApplySequentialBatchListMutation(actions, (script, action) => AiAutomation.DuplicateActionInstructions(script, action), out byte[] finalAi, out string error))
            {
                SetBatchError(error);
                return;
            }

            string names = BatchNames(actions);
            if (!SaveNewAi(finalAi, out string err)) { SetBatchError(err); return; }
            RemoveActionSummary = string.Format(Strings.U_Ai_HmDuplicatedBatch, actions.Count, names, ContextNoteForActions(actions));
            ClearBatchSelectionAfterSuccessfulWrite();
        }

        void InsertSecondAbilityForBatch()
        {
            if (!TryGetBatchActions("combo em lote", out List<AiDetectedAction> actions)) return;
            AiCommandOption? to = SelectedAutomationAbility;
            if (to == null) { SetBatchError(Strings.F2_choose_the_second_ability_before_applyin_37e4e679); return; }
            if (actions.Any(a => a.Kind != AiActionKind.Command || !a.Removable))
            {
                SetBatchError(Strings.F2_batch_combo_only_applies_to_simple_comma_8b84f58e);
                return;
            }

            bool linkRite = LinkForbiddenRiteToBehavior;
            AiTargetOption? riteTarget = null;
            AiForbiddenStatusPreset? riteStatus = null;
            ushort riteValue = 0;
            Dictionary<int, ushort>? riteTargetsByCall = null;
            string riteTargetLabel = string.Empty;
            string riteError = string.Empty;
            if (linkRite && !TryGetForbiddenRitePayload(out riteTarget, out riteStatus, out riteValue, out riteError))
            {
                SetBatchError(riteError);
                return;
            }
            if (linkRite && !TryResolveForbiddenRiteTargetsForBatch(riteTarget!, actions, out riteTargetsByCall, out riteTargetLabel, out riteError))
            {
                SetBatchError(riteError);
                return;
            }

            if (!TryApplySequentialBatchListMutation(actions,
                    (script, action) => linkRite
                        ? AiAutomation.InsertSecondCommandWithChrPropertyWrite(script, action, to.Operand, riteTargetsByCall![action.CallOffset], riteStatus!.FieldId, riteValue)
                        : AiAutomation.InsertSecondCommand(script, action, to.Operand),
                    out byte[] finalAi,
                    out string error))
            {
                SetBatchError(error);
                return;
            }

            string names = BatchNames(actions);
            if (!SaveNewAi(finalAi, out string err)) { SetBatchError(err); return; }
            RemoveActionSummary =
                string.Format(Strings.U_Ai_HmSecondBatch, to.Name, actions.Count, names) +
                (linkRite ? LinkedForbiddenRiteNote(riteTargetLabel, riteStatus!, riteValue) : "") +
                string.Format(Strings.U_Ai_HmDirectComboNote, ContextNoteForActions(actions));
            ClearBatchSelectionAfterSuccessfulWrite();
        }

        void MoveBatchActionsToHook()
        {
            if (!TryGetBatchActions("mover lote para gatilho", out List<AiDetectedAction> actions)) return;
            if (actions.Any(a => !a.Removable))
            {
                SetBatchError(Strings.U_Ai_HmMoveBlocked);
                return;
            }
            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            {
                SetBatchError(hookError);
                return;
            }

            if (!TryApplySequentialBatchAiRewrite(actions, (script, action) => AiAutomation.MoveActionToHook(script, action, hook.WorkerIndex, hook.EntrypointIndex), out byte[] finalAi, out string error))
            {
                SetBatchError(error);
                return;
            }

            string names = BatchNames(actions);
            if (!SaveNewAi(finalAi, out string err)) { SetBatchError(err); return; }
            RemoveActionSummary = string.Format(Strings.U_Ai_HmMovedBatch, actions.Count, hookLabel, names);
            ClearBatchSelectionAfterSuccessfulWrite();
        }

        public void MoveBatchSelectionUp() => MoveBatchSelection(up: true);
        public void MoveBatchSelectionDown() => MoveBatchSelection(up: false);

        public void MoveBatchSelection(bool up)
        {
            ResetBatchSelectionScopeIfNeeded();
            ReconcileBatchSelectionWithDetectedActions();

            if (_batchSelectedActionOffsets.Count == 0)
            {
                BatchActionSummary = Strings.F2_select_at_least_one_safe_action_before_b_acb48215;
                return;
            }

            if (_batchSelectedActionOffsets.Count > 1)
            {
                var selected = _allDetectedActions
                    .Where(a => _batchSelectedActionOffsets.Contains(a.CallOffset))
                    .OrderBy(a => a.CallOffset)
                    .ToList();
                List<AiInstruction>? reordered = AiAutomation.MoveActionGroupInstructions(selectedScript!, selected, up);
                if (reordered == null)
                {
                    BatchActionSummary =
                        string.Format(Strings.U_Ai_HmGroupNotConsecutive2, _batchSelectedActionOffsets.Count);
                    RemoveActionSummary = BatchActionSummary;
                    return;
                }

                int reselectIndex = BatchReselectIndex(up, selected);
                string names = string.Join(", ", selected.Take(3).Select(a => a.AbilityName));
                if (selected.Count > 3) names += $" +{selected.Count - 3}";
                ApplyAndSave(reordered,
                    string.Format(Strings.U_Ai_HmGroupMoved, (up ? "↑ para cima" : "↓ para baixo"), names, System.IO.Path.GetFileName(selectedPath)),
                    reselectIndex);
                _batchSelectedActionOffsets.Clear();
                foreach (AiActionVm vm in AutomationActions)
                    vm.SetBatchSelectedFromModel(false);
                RefreshBatchSelectionState();
                BatchActionSummary = RemoveActionSummary;
                return;
            }

            AiActionVm? action = VisibleBatchAction();
            if (action == null)
            {
                SelectedActionFilter = AutomationActionFilters.FirstOrDefault();
                action = VisibleBatchAction();
            }

            if (action == null)
            {
                BatchActionSummary = Strings.F2_the_selected_batch_action_is_no_longer_i_59b48c51;
                RemoveActionSummary = BatchActionSummary;
                return;
            }

            SelectedAutomationAction = action;
            MoveSelectedAction(up);
            string result = RemoveActionSummary;

            _batchSelectedActionOffsets.Clear();
            if (SelectedAutomationAction?.CanBatchSelect == true)
                _batchSelectedActionOffsets.Add(SelectedAutomationAction.Action.CallOffset);
            foreach (AiActionVm vm in AutomationActions)
                vm.SetBatchSelectedFromModel(IsActionBatchSelected(vm.Action));
            RefreshBatchSelectionState();
            BatchActionSummary = result;
        }

        AiActionVm? VisibleBatchAction() =>
            AutomationActions.FirstOrDefault(a => _batchSelectedActionOffsets.Contains(a.Action.CallOffset));

        bool TryGetBatchActions(string verb, out List<AiDetectedAction> actions)
        {
            actions = new List<AiDetectedAction>();
            ResetBatchSelectionScopeIfNeeded();
            ReconcileBatchSelectionWithDetectedActions();

            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                SetBatchError(string.Format(Strings.U_Ai_HmNeedAiFileVerb, verb));
                return false;
            }

            if (_batchSelectedActionOffsets.Count == 0)
            {
                SetBatchError(string.Format(Strings.U_Ai_HmSelectActionVerb, verb));
                return false;
            }

            foreach (AiDetectedAction action in _allDetectedActions)
                if (_batchSelectedActionOffsets.Contains(action.CallOffset))
                    actions.Add(action);

            if (actions.Count != _batchSelectedActionOffsets.Count)
            {
                SetBatchError(Strings.F2_the_batch_selection_no_longer_matches_th_4aa7eff1);
                return false;
            }

            return true;
        }

        string BatchNames(IReadOnlyList<AiDetectedAction> actions)
        {
            string names = string.Join(", ", actions.Take(4).Select(a => a.AbilityName));
            if (actions.Count > 4) names += $" +{actions.Count - 4}";
            return names;
        }

        void SetBatchError(string message)
        {
            BatchActionSummary = message;
            RemoveActionSummary = message;
        }

        void FinishBatchAfterWrite(bool clearSelection, IReadOnlyList<int>? keepCalls = null)
        {
            if (!RemoveActionSummary.StartsWith("✅", StringComparison.Ordinal))
            {
                BatchActionSummary = RemoveActionSummary;
                return;
            }

            if (clearSelection)
                ClearBatchSelectionAfterSuccessfulWrite();
            else
                RestoreBatchSelectionByCallOffsets(keepCalls ?? Array.Empty<int>(), RemoveActionSummary);
        }

        void ClearBatchSelectionAfterSuccessfulWrite()
        {
            string summary = RemoveActionSummary;
            _batchSelectedActionOffsets.Clear();
            foreach (AiActionVm vm in AutomationActions)
                vm.SetBatchSelectedFromModel(false);
            RefreshBatchSelectionState();
            BatchActionSummary = summary;
        }

        void RestoreBatchSelectionByCallOffsets(IReadOnlyList<int> callOffsets, string summary)
        {
            _batchSelectedActionOffsets.Clear();
            foreach (int call in callOffsets)
                _batchSelectedActionOffsets.Add(call);
            foreach (AiActionVm vm in AutomationActions)
                vm.SetBatchSelectedFromModel(IsActionBatchSelected(vm.Action));
            SelectedAutomationAction = AutomationActions.FirstOrDefault(a => _batchSelectedActionOffsets.Contains(a.Action.CallOffset))
                                       ?? SelectedAutomationAction;
            RefreshBatchSelectionState();
            BatchActionSummary = summary;
        }

        bool TryApplySequentialBatchListMutation(
            IReadOnlyList<AiDetectedAction> originalActions,
            Func<AiScriptFile, AiDetectedAction, List<AiInstruction>?> mutate,
            out byte[] finalAi,
            out string error)
        {
            finalAi = Array.Empty<byte>();
            error = string.Empty;
            AiScriptFile working = selectedScript!;
            byte[]? currentBytes = null;

            foreach (AiDetectedAction original in originalActions.OrderByDescending(a => a.CallOffset))
            {
                AiDetectedAction? current = AiAutomation.DetectActions(working).FirstOrDefault(a => a.CallOffset == original.CallOffset);
                if (current == null)
                {
                    error = string.Format(Strings.U_Ai_HmOffsetChanged, original.AbilityName);
                    return false;
                }

                List<AiInstruction>? changed = mutate(working, current);
                if (changed == null)
                {
                    error = string.Format(Strings.U_Ai_HmBackendRefused, original.AbilityName);
                    return false;
                }

                AiValidationReport check = AiValidator.Validate(working, changed);
                if (!check.IsValid)
                {
                    error = string.Format(Strings.U_Ai_HmValidationBlocked, check.Errors.FirstOrDefault()?.Message);
                    return false;
                }

                try { currentBytes = AiScript_File.Rebuild(working, changed); }
                catch (Exception ex)
                {
                    error = string.Format(Strings.U_Ai_HmRebuildRejected, original.AbilityName, ex.Message);
                    return false;
                }

                working = AiScript_File.Read(currentBytes);
            }

            if (currentBytes == null)
            {
                error = Strings.F2_no_change_was_generated_21994846;
                return false;
            }

            finalAi = currentBytes;
            return true;
        }

        bool TryApplySequentialBatchAiRewrite(
            IReadOnlyList<AiDetectedAction> originalActions,
            Func<AiScriptFile, AiDetectedAction, byte[]> mutate,
            out byte[] finalAi,
            out string error)
        {
            finalAi = Array.Empty<byte>();
            error = string.Empty;
            AiScriptFile working = selectedScript!;
            byte[]? currentBytes = null;

            foreach (AiDetectedAction original in originalActions.OrderByDescending(a => a.CallOffset))
            {
                AiDetectedAction? current = AiAutomation.DetectActions(working).FirstOrDefault(a => a.CallOffset == original.CallOffset);
                if (current == null)
                {
                    error = string.Format(Strings.U_Ai_HmOffsetChanged, original.AbilityName);
                    return false;
                }

                try { currentBytes = mutate(working, current); }
                catch (Exception ex)
                {
                    error = string.Format(Strings.U_Ai_HmBackendRefused2, original.AbilityName, ex.Message);
                    return false;
                }

                working = AiScript_File.Read(currentBytes);
            }

            if (currentBytes == null)
            {
                error = Strings.F2_no_change_was_generated_21994846;
                return false;
            }

            finalAi = currentBytes;
            return true;
        }

        int BatchReselectIndex(bool up, IReadOnlyList<AiDetectedAction> selected)
        {
            var indexes = selected
                .Select(a =>
                {
                    for (int i = 0; i < AutomationActions.Count; i++)
                        if (AutomationActions[i].Action.CallOffset == a.CallOffset)
                            return i;
                    return -1;
                })
                .Where(i => i >= 0)
                .OrderBy(i => i)
                .ToList();
            if (indexes.Count == 0) return -1;
            int target = up ? indexes[0] - 1 : indexes[^1] + 1;
            if (target < 0) return 0;
            if (target >= AutomationActions.Count) return AutomationActions.Count - 1;
            return target;
        }

        void RefreshBatchSelectionState()
        {
            OnPropertyChanged(nameof(HasBatchSelection));
            OnPropertyChanged(nameof(HasMultiBatchSelection));
            OnPropertyChanged(nameof(BatchSelectionCount));

            if (_batchSelectedActionOffsets.Count == 0)
            {
                BatchActionSummary = Strings.U_Ai_HmNoBatchSelection;
                return;
            }

            var selected = _allDetectedActions
                .Where(a => _batchSelectedActionOffsets.Contains(a.CallOffset))
                .OrderBy(a => a.CallOffset)
                .ToList();
            string names = string.Join(", ", selected.Take(3).Select(a => a.AbilityName));
            if (selected.Count > 3) names += $" +{selected.Count - 3}";
            BatchActionSummary = selected.Count == 1
                ? string.Format(Strings.U_Ai_HmOneSelected, names)
                : string.Format(Strings.U_Ai_HmManySelected, selected.Count, names);
        }
    }

    internal enum MonsterAiHumanMode
    {
        Normal,
        Avancado,
        DevKit,
    }

    internal sealed record MonsterAiHumanModeChoice(MonsterAiHumanMode Mode, string Label, string Summary)
    {
        public override string ToString() => Label;
    }
}
