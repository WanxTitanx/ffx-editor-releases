using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal enum MonsterAiActionDropPlacement
    {
        Before,
        After,
    }

    internal sealed record MonsterAiActionDragPayload(IReadOnlyList<int> CallOffsets, string Label);

    internal sealed record MonsterAiActionDropResult(
        bool Accepted,
        bool Saved,
        bool IsNoOp,
        string Message,
        int Steps = 0,
        bool Up = false)
    {
        public static MonsterAiActionDropResult Blocked(string message) =>
            new(false, false, false, message);

        public static MonsterAiActionDropResult NoOp(string message) =>
            new(true, false, true, message);
    }

    internal partial class MonsterAiEditor_DataModel
    {
        public MonsterAiActionDragPayload? CreateAutomationActionDragPayload(AiActionVm? draggedAction)
        {
            ResetBatchSelectionScopeIfNeeded();
            ReconcileBatchSelectionWithDetectedActions();

            if (draggedAction == null)
                return null;

            List<AiActionVm> visibleGroup = draggedAction.IsBatchSelected
                ? AutomationActions.Where(a => a.IsBatchSelected).ToList()
                : new List<AiActionVm> { draggedAction };

            if (visibleGroup.Count == 0)
                visibleGroup.Add(draggedAction);

            List<int> offsets = visibleGroup
                .Select(a => a.Action.CallOffset)
                .Distinct()
                .ToList();

            string names = string.Join(", ", visibleGroup.Take(3).Select(a => a.Action.AbilityName));
            if (visibleGroup.Count > 3) names += $" +{visibleGroup.Count - 3}";

            return new MonsterAiActionDragPayload(offsets, names);
        }

        public MonsterAiActionDropResult PreviewAutomationActionDrop(
            MonsterAiActionDragPayload? payload,
            AiActionVm? dropTarget,
            MonsterAiActionDropPlacement placement)
        {
            MonsterAiActionDropPlan? plan;
            return TryPlanAutomationActionDrop(payload, dropTarget, placement, out plan);
        }

        public MonsterAiActionDropResult DropAutomationActions(
            MonsterAiActionDragPayload? payload,
            AiActionVm? dropTarget,
            MonsterAiActionDropPlacement placement)
        {
            MonsterAiActionDropResult planned = TryPlanAutomationActionDrop(payload, dropTarget, placement, out MonsterAiActionDropPlan? plan);
            if (!planned.Accepted || planned.IsNoOp || plan == null)
            {
                BatchActionSummary = planned.Message;
                RemoveActionSummary = planned.Message;
                return planned;
            }

            AiScriptFile workingScript = selectedScript!;
            List<AiInstruction>? reordered = null;
            int currentStart = plan.FirstIndex;
            int finalStart = plan.FirstIndex;

            for (int step = 0; step < plan.Steps; step++)
            {
                IReadOnlyList<AiDetectedAction> actions = AiAutomation.DetectActions(workingScript);
                if (currentStart < 0 || currentStart + plan.Count > actions.Count)
                    return BlockDropDuringApply("The block went out of range during drag/drop; nothing was saved.");

                List<AiDetectedAction> movingGroup = actions
                    .Skip(currentStart)
                    .Take(plan.Count)
                    .ToList();

                reordered = AiAutomation.MoveActionGroupInstructions(workingScript, movingGroup, plan.Up);
                if (reordered == null)
                    return BlockDropDuringApply("Drop blocked by backend: the group must be consecutive and the neighbor must be self-contained.");

                AiValidationReport stepCheck = AiValidator.Validate(workingScript, reordered);
                if (!stepCheck.IsValid)
                    return BlockDropDuringApply(string.Format(Strings.U_Ai_DragBlockedStepValidation, stepCheck.Errors.FirstOrDefault()?.Message));

                finalStart = currentStart + (plan.Up ? -1 : 1);
                if (step == plan.Steps - 1)
                    break;

                byte[] stepBytes;
                try { stepBytes = AiScript_File.Rebuild(workingScript, reordered); }
                catch (Exception ex) { return BlockDropDuringApply(string.Format(Strings.U_Ai_DragRebuildIntermediate, ex.Message)); }

                workingScript = AiScript_File.Read(stepBytes);
                currentStart = finalStart;
            }

            if (reordered == null)
                return BlockDropDuringApply("No order change was generated.");

            string okMsg =
                string.Format(Strings.U_Ai_DragMovedOk,
                    plan.Up ? Strings.U_Ai_DragUp : Strings.U_Ai_DragDown,
                    plan.Steps,
                    plan.Label,
                    Path.GetFileName(selectedPath));

            return SaveDragReorder(workingScript, reordered, finalStart, plan.Count, okMsg, plan.Steps, plan.Up);
        }

        MonsterAiActionDropResult TryPlanAutomationActionDrop(
            MonsterAiActionDragPayload? payload,
            AiActionVm? dropTarget,
            MonsterAiActionDropPlacement placement,
            out MonsterAiActionDropPlan? plan)
        {
            plan = null;
            ResetBatchSelectionScopeIfNeeded();
            ReconcileBatchSelectionWithDetectedActions();

            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
                return MonsterAiActionDropResult.Blocked(Strings.F2_select_a_monster_with_a_real_aifile_befo_2cdefa24);

            if (payload == null || payload.CallOffsets.Count == 0)
                return MonsterAiActionDropResult.Blocked(Strings.U_Ai_DragNoActions);

            if (dropTarget == null)
                return MonsterAiActionDropResult.Blocked(Strings.U_Ai_DropOnActionCard);

            if (AutomationActions.Count != _allDetectedActions.Count)
                return MonsterAiActionDropResult.Blocked(Strings.F2_drag_drop_uses_the_full_real_order_switc_1fbb1e74);

            IReadOnlyList<int> calls = payload.CallOffsets.Distinct().ToList();
            var indexes = new List<int>();
            for (int i = 0; i < _allDetectedActions.Count; i++)
                if (calls.Contains(_allDetectedActions[i].CallOffset))
                    indexes.Add(i);

            if (indexes.Count != calls.Count)
                return MonsterAiActionDropResult.Blocked(Strings.F2_the_drag_selection_no_longer_matches_the_9c329a0f);

            indexes.Sort();
            int first = indexes[0];
            int last = indexes[^1];
            int count = indexes.Count;

            if (last - first + 1 != count)
                return MonsterAiActionDropResult.Blocked(Strings.F2_drop_blocked_the_selected_group_is_not_c_b129c41f);

            List<AiDetectedAction> selected = indexes.Select(i => _allDetectedActions[i]).ToList();
            if (selected.Any(a => !a.Removable))
                return MonsterAiActionDropResult.Blocked(Strings.F2_drop_blocked_one_of_the_selected_actions_a36bee5f);

            int targetIndex = -1;
            for (int i = 0; i < _allDetectedActions.Count; i++)
                if (_allDetectedActions[i].CallOffset == dropTarget.Action.CallOffset)
                {
                    targetIndex = i;
                    break;
                }

            if (targetIndex < 0)
                return MonsterAiActionDropResult.Blocked(Strings.F2_the_target_card_no_longer_exists_in_the__64b76c76);

            if (targetIndex >= first && targetIndex <= last)
                return MonsterAiActionDropResult.NoOp(Strings.F2_release_outside_the_own_group_to_change__6622beae);

            int desiredStart = targetIndex < first
                ? placement == MonsterAiActionDropPlacement.Before ? targetIndex : targetIndex + 1
                : placement == MonsterAiActionDropPlacement.Before ? targetIndex - count : targetIndex - count + 1;

            desiredStart = Math.Clamp(desiredStart, 0, _allDetectedActions.Count - count);
            if (desiredStart == first)
                return MonsterAiActionDropResult.NoOp(Strings.F2_this_drop_keeps_the_group_in_the_same_po_166d3a49);

            bool up = desiredStart < first;
            int steps = Math.Abs(desiredStart - first);
            IEnumerable<AiDetectedAction> crossed = up
                ? _allDetectedActions.Skip(desiredStart).Take(first - desiredStart)
                : _allDetectedActions.Skip(last + 1).Take(desiredStart - first);

            AiDetectedAction? unsafeNeighbour = crossed.FirstOrDefault(a => !a.Removable);
            if (unsafeNeighbour != null)
                return MonsterAiActionDropResult.Blocked(string.Format(Strings.U_Ai_DragBlockedNeighbour, unsafeNeighbour.AbilityName));

            string label = payload.Label;
            plan = new MonsterAiActionDropPlan(first, count, desiredStart, steps, up, label);
            return new MonsterAiActionDropResult(true, false, false,
                string.Format(Strings.U_Ai_DragReadyToMove, count, up ? Strings.U_Ai_DragUp : Strings.U_Ai_DragDown, steps, label),
                steps,
                up);
        }

        MonsterAiActionDropResult SaveDragReorder(
            AiScriptFile rebuildBase,
            List<AiInstruction> finalList,
            int finalStart,
            int count,
            string okMsg,
            int steps,
            bool up)
        {
            AiValidationReport check = AiValidator.Validate(rebuildBase, finalList);
            if (!check.IsValid)
                return BlockDropDuringApply(string.Format(Strings.U_Ai_DragBlockedFinalValidation, check.Errors.FirstOrDefault()?.Message));

            byte[] rebuilt;
            try { rebuilt = AiScript_File.Rebuild(rebuildBase, finalList); }
            catch (Exception ex) { return BlockDropDuringApply(string.Format(Strings.U_Ai_DragRebuildRejected, ex.Message)); }

            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath!);
                bool grew = rebuilt.Length != selectedScript!.OriginalAiFileBytes.Length;
                byte[] outBin = grew
                    ? AiScript_File.SpliceAiFileIntoMonsterGrow(monster, rebuilt)
                    : AiScript_File.SpliceAiFileIntoMonster(monster, rebuilt);
                WriteMonsterWithBackup(outBin);
            }
            catch (Exception ex)
            {
                return BlockDropDuringApply($"Save abortado no splice: {ex.Message}");
            }

            ReloadSelectedFromDisk();
            RestoreDragSelectionByIndexes(finalStart, count);
            RemoveActionSummary = okMsg;
            BatchActionSummary = okMsg;
            return new MonsterAiActionDropResult(true, true, false, okMsg, steps, up);
        }

        MonsterAiActionDropResult BlockDropDuringApply(string message)
        {
            BatchActionSummary = message;
            RemoveActionSummary = message;
            return MonsterAiActionDropResult.Blocked(message);
        }

        void RestoreDragSelectionByIndexes(int start, int count)
        {
            _batchSelectedActionOffsets.Clear();
            for (int i = start; i < start + count && i < _allDetectedActions.Count; i++)
                if (i >= 0 && _allDetectedActions[i].Removable)
                    _batchSelectedActionOffsets.Add(_allDetectedActions[i].CallOffset);

            foreach (AiActionVm vm in AutomationActions)
                vm.SetBatchSelectedFromModel(IsActionBatchSelected(vm.Action));

            SelectedAutomationAction = AutomationActions
                .FirstOrDefault(a => _batchSelectedActionOffsets.Contains(a.Action.CallOffset))
                ?? AutomationActions.FirstOrDefault();

            RefreshBatchSelectionState();
        }
    }

    internal sealed record MonsterAiActionDropPlan(
        int FirstIndex,
        int Count,
        int DesiredStart,
        int Steps,
        bool Up,
        string Label);
}
