using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // "COMO ESTE MONSTRO PENSA" — the accessible BEHAVIOUR view. The flat action list hides the only thing that
    // matters to a human: WHEN each thing runs, under WHICH condition, and WHAT runs in sequence with what. This
    // layer reads that structure straight out of the bytecode (no disassembly shown to the user):
    //   • GATILHO  — which worker/event the action hooks (onTurn resolved via AiWorkerMapping; else worker role).
    //   • CONDIÇÃO — the guard wrapping the action: AlwaysGuard `[PUSHII 1]` = "sempre"; RngGuard
    //                `[CALL GetRandomValue, PUSHII k, MOD, PUSHII 0, EQ]` = "às vezes (1 em k)"; nenhum = "direta".
    //                (The guarded block shape is `[guard] D7/POPXNCJMP [action] B0/JMP`, so the guard sits right
    //                before the D7 that precedes the action's first instruction.)
    //   • SEQUÊNCIA — actions under the same gatilho run in order, in the same turn (the "↳ em seguida" connector).
    // Selecting a behaviour row drives the SAME SelectedAutomationAction the edit panel uses, so editing keeps working.
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<BehaviorGroupVm> BehaviorGroups { get; } = new();
        [ObservableProperty] private string behaviorViewSummary = Strings.U_Ai_BehSelectMonster;
        [ObservableProperty] private BehaviorActionVm? selectedBehaviorAction;

        bool _syncingBehaviorSelection;   // re-entrancy guard between the two selection sources

        // opcodes used to read the guard shape
        const byte OpPushii = 0xAE, OpPopxncjmp = 0xD7, OpPopxcjmp = 0xD6,
            OpMod = 0x18, OpEq = 0x06, OpLt = 0x0B, OpMul = 0x16, OpCall = 0xB5, OpCallpopa = 0xD8;
        const ushort GetRandomValueId = 0x00A9, ReadChrPropertyId = 0x700F, SelfTargetId = 0xFFF3;

        partial void OnSelectedBehaviorActionChanged(BehaviorActionVm? value)
        {
            if (_syncingBehaviorSelection || value == null) return;
            _syncingBehaviorSelection = true;
            // The edit panel + action buttons all read SelectedAutomationAction. Make sure it can be found even if
            // the flat list's type filter is hiding it (reset to "Todas" so the row exists), then select it.
            if (!AutomationActions.Any(v => v.Action.CallOffset == value.Action.CallOffset))
                SelectedActionFilter = AutomationActionFilters.FirstOrDefault();   // "Todas"
            SelectedAutomationAction = AutomationActions.FirstOrDefault(v => v.Action.CallOffset == value.Action.CallOffset)
                                       ?? SelectedAutomationAction;
            _syncingBehaviorSelection = false;
        }

        // Called from OnSelectedAutomationActionChanged (.Automations.cs) so picking in the flat list / after an edit
        // highlights the matching behaviour row too.
        void SyncBehaviorSelectionFromAction(AiActionVm? value)
        {
            if (_syncingBehaviorSelection) return;
            _syncingBehaviorSelection = true;
            SelectedBehaviorAction = value == null ? null
                : BehaviorGroups.SelectMany(g => g.Actions).FirstOrDefault(b => b.Action.CallOffset == value.Action.CallOffset);
            _syncingBehaviorSelection = false;
        }

        void RebuildBehaviorGroups()
        {
            BehaviorGroups.Clear();
            if (selectedScript == null || !selectedScript.HasScript)
            {
                BehaviorViewSummary = Strings.U_Ai_BehNoScript;
                return;
            }

            // Resolve the real onTurn hook so the actions hooked there read as "no turno dele".
            int onTurnWorker = -1;
            if (TryResolveOnTurnHook(out AiEventHook hook, out _)) onTurnWorker = hook.WorkerIndex;

            // Group key = the trigger (worker + whether it's the onTurn hook). Order: onTurn first, then by worker.
            var groups = new Dictionary<int, BehaviorGroupVm>();
            var order = new List<int>();
            foreach (AiDetectedAction a in _allDetectedActions.OrderBy(a => a.CallOffset))
            {
                int wi = a.WorkerIndex;
                if (!groups.TryGetValue(wi, out BehaviorGroupVm? g))
                {
                    g = new BehaviorGroupVm(wi, WorkerTypeOf(wi), wi == onTurnWorker);
                    groups[wi] = g;
                    order.Add(wi);
                }
                (string badge, Color color) = ClassifyCondition(a);
                g.Actions.Add(new BehaviorActionVm(a, badge, new SolidColorBrush(color), runsAfterPrevious: g.Actions.Count > 0));
            }

            foreach (int wi in order.OrderByDescending(w => w == onTurnWorker).ThenBy(w => w))
            {
                BehaviorGroupVm g = groups[wi];
                g.FinalizeNotes();
                BehaviorGroups.Add(g);
            }

            int total = _allDetectedActions.Count;
            BehaviorViewSummary = total == 0
                ? Strings.U_Ai_BehNoActions
                : string.Format(Strings.U_Ai_BehActionsSummary, total, BehaviorGroups.Count);
        }

        string WorkerTypeOf(int workerIndex) =>
            (selectedScript != null && workerIndex >= 0 && workerIndex < selectedScript.Workers.Count)
                ? selectedScript.Workers[workerIndex].InferredType ?? "?"
                : "?";

        // Classify the guard immediately before the action's first instruction (the `[guard] D7 [action]` shape).
        (string badge, Color color) ClassifyCondition(AiDetectedAction a)
        {
            if (!TryDescribeGuardForAction(a, out AiGuardDescription guard))
                return ("direta", Color.FromRgb(0x7E, 0x93, 0xA6));   // not a guarded automation block — runs inline

            return (guard.Label, guard.Color);
        }

        bool TryDescribeGuardForAction(AiDetectedAction a, out AiGuardDescription guard)
        {
            guard = default;
            if (selectedScript == null) return false;
            IReadOnlyList<AiInstruction> ins = selectedScript.Instructions;
            int firstOffset = a.RemoveOffsets.Count > 0 ? a.RemoveOffsets.Min() : a.CallOffset;
            int firstIdx = -1;
            for (int i = 0; i < ins.Count; i++) if (ins[i].Offset == firstOffset) { firstIdx = i; break; }
            if (firstIdx <= 0 || ins[firstIdx - 1].Opcode != OpPopxncjmp) return false;
            guard = DescribeGuardBeforeBranch(firstIdx - 1);
            return true;
        }

        AiGuardDescription DescribeGuardBeforeBranch(int branchIndex)
        {
            IReadOnlyList<AiInstruction> ins = selectedScript!.Instructions;

            if (TryDescribeHpPercentGuard(branchIndex, out ushort hpPercent))
                return new AiGuardDescription($"HP < {hpPercent}%", Color.FromRgb(0xD7, 0xB2, 0x5C), false);

            if (TryDescribeChrPropertyBelowGuard(branchIndex, out ushort fieldId, out ushort threshold))
            {
                string field = AiChrPropertyNames.Get(fieldId) ?? string.Format(Strings.U_Ai_BehField, fieldId);
                return new AiGuardDescription($"{field} < {threshold}", Color.FromRgb(0xD7, 0xB2, 0x5C), false);
            }

            // The guard ops sit just before the branch. Scan a small window back for the RNG signature; else Always.
            for (int j = branchIndex - 1; j >= System.Math.Max(0, branchIndex - 8); j--)
            {
                AiInstruction op = ins[j];
                if ((op.Opcode == OpCall || op.Opcode == OpCallpopa) && op.Operand == GetRandomValueId)
                {
                    int k = (j + 1 < ins.Count && ins[j + 1].Opcode == OpPushii) ? ins[j + 1].Operand : 0;
                    return new AiGuardDescription(string.Format(Strings.U_Ai_BehSometimes, k), Color.FromRgb(0xD7, 0xB2, 0x5C), true);
                }
            }
            if (branchIndex > 0 && ins[branchIndex - 1].Opcode == OpPushii && ins[branchIndex - 1].Operand == 1)
                return new AiGuardDescription(Strings.U_Ai_BehAlways, Color.FromRgb(0x6F, 0xE3, 0xB7), false);
            return new AiGuardDescription(Strings.U_Ai_BehAdvancedCondition, Color.FromRgb(0x8F, 0xB7, 0xFF), false);
        }

        bool TryDescribeHpPercentGuard(int branchIndex, out ushort percent)
        {
            percent = 0;
            IReadOnlyList<AiInstruction> ins = selectedScript!.Instructions;
            int start = branchIndex - 11;
            if (start < 0) return false;

            if (ins[start + 0].Opcode == OpPushii && ins[start + 0].Operand == SelfTargetId &&
                ins[start + 1].Opcode == OpPushii && ins[start + 1].Operand == 0x0000 &&
                (ins[start + 2].Opcode == OpCall || ins[start + 2].Opcode == OpCallpopa) && ins[start + 2].Operand == ReadChrPropertyId &&
                ins[start + 3].Opcode == OpPushii && ins[start + 3].Operand == 100 &&
                ins[start + 4].Opcode == OpMul &&
                ins[start + 5].Opcode == OpPushii && ins[start + 5].Operand == SelfTargetId &&
                ins[start + 6].Opcode == OpPushii && ins[start + 6].Operand == 0x0002 &&
                (ins[start + 7].Opcode == OpCall || ins[start + 7].Opcode == OpCallpopa) && ins[start + 7].Operand == ReadChrPropertyId &&
                ins[start + 8].Opcode == OpPushii &&
                ins[start + 9].Opcode == OpMul &&
                ins[start + 10].Opcode == OpLt)
            {
                percent = ins[start + 8].Operand;
                return true;
            }

            return false;
        }

        bool TryDescribeChrPropertyBelowGuard(int branchIndex, out ushort fieldId, out ushort threshold)
        {
            fieldId = 0;
            threshold = 0;
            IReadOnlyList<AiInstruction> ins = selectedScript!.Instructions;
            int start = branchIndex - 5;
            if (start < 0) return false;

            if (ins[start + 0].Opcode == OpPushii && ins[start + 0].Operand == SelfTargetId &&
                ins[start + 1].Opcode == OpPushii &&
                (ins[start + 2].Opcode == OpCall || ins[start + 2].Opcode == OpCallpopa) && ins[start + 2].Operand == ReadChrPropertyId &&
                ins[start + 3].Opcode == OpPushii &&
                ins[start + 4].Opcode == OpLt)
            {
                fieldId = ins[start + 1].Operand;
                threshold = ins[start + 3].Operand;
                return true;
            }

            return false;
        }
    }

    internal readonly record struct AiGuardDescription(string Label, Color Color, bool Random);

    // One trigger bucket: every action hooked at the same worker/event, in execution order.
    internal sealed class BehaviorGroupVm
    {
        public BehaviorGroupVm(int workerIndex, string workerType, bool isOnTurn)
        {
            WorkerIndex = workerIndex;
            IsOnTurn = isOnTurn;
            Title = isOnTurn ? Strings.U_Ai_BehOnItsTurn
                  : workerType == "CombatHandler" ? Strings.U_Ai_BehDuringCombat
                  : workerType == "CameraHandler" ? Strings.U_Ai_BehCamera
                  : workerType == "MotionHandler" ? Strings.U_Ai_BehMotion
                  : string.Format(Strings.U_Ai_BehWorker, workerIndex);
            WorkerTag = $"worker {workerIndex} · {workerType}";
        }

        public int WorkerIndex { get; }
        public bool IsOnTurn { get; }
        public string Title { get; }
        public string WorkerTag { get; private set; }
        public ObservableCollection<BehaviorActionVm> Actions { get; } = new();

        public void FinalizeNotes()
        {
            if (Actions.Count > 1)
                WorkerTag += Strings.U_Ai_BehRunInSequence;
        }
    }

    // One behaviour row. Wraps the AiDetectedAction so selection drives the same edit pipeline as the flat list.
    internal sealed class BehaviorActionVm
    {
        public BehaviorActionVm(AiDetectedAction action, string conditionBadge, IBrush conditionBrush, bool runsAfterPrevious)
        {
            Action = action;
            ConditionBadge = conditionBadge;
            ConditionBrush = conditionBrush;
            RunsAfterPrevious = runsAfterPrevious;
            Icon = action.Kind switch { AiActionKind.Buff => "🛡", AiActionKind.Stat => "📊", _ => "⚔" };
        }

        public AiDetectedAction Action { get; }
        public string Icon { get; }
        public string Name => Action.AbilityName;
        public string ConditionBadge { get; }
        public IBrush ConditionBrush { get; }
        public bool RunsAfterPrevious { get; }
        public string SequenceConnector => RunsAfterPrevious ? Strings.U_Ai_BehThen : Strings.U_Ai_BehStartsHere;
    }
}
