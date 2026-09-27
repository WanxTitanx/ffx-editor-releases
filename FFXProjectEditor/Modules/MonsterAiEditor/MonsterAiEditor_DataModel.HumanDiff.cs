using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // AEON human diff: a UI-ready layer over the existing AiScript_Diff/AiDiffRow output.
    // It does not calculate a second diff; it only translates the changed rows into layperson action language.
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiHumanDiffRow> HumanDiffEntries { get; } = new();

        [ObservableProperty] private bool hasHumanDiffEntries;
        [ObservableProperty] private bool hasNoHumanDiffEntries = true;
        [ObservableProperty] private string humanDiffSummary =
            Strings.F2_human_aeon_select_a_monster_and_load_the_7e142ab0;
        [ObservableProperty] private string humanDiffEmptyMessage = Strings.U_Ai_HdEmpty;

        partial void OnHasHumanDiffEntriesChanged(bool value) => HasNoHumanDiffEntries = !value;

        void ResetHumanDiff()
        {
            HumanDiffEntries.Clear();
            HasHumanDiffEntries = false;
            HumanDiffSummary = Strings.U_Ai_HdLoading;
            HumanDiffEmptyMessage = Strings.U_Ai_HdEmpty;
        }

        void ClearHumanDiff(string summary)
        {
            HumanDiffEntries.Clear();
            HasHumanDiffEntries = false;
            HumanDiffSummary = summary;
            HumanDiffEmptyMessage = summary;
        }

        void RebuildHumanDiffEntries(IReadOnlyList<AiDiffRow> diffRows, AiScriptFile original, AiScriptFile edited)
        {
            HumanDiffEntries.Clear();

            if (diffRows.Count == 0)
            {
                HasHumanDiffEntries = false;
                HumanDiffSummary = Strings.U_Ai_HdNoChanges;
                HumanDiffEmptyMessage = Strings.F2_the_current_ai_matches_the_loaded_vanill_1783c160;
                return;
            }

            HumanDiffScriptContext before = HumanDiffScriptContext.From(original);
            HumanDiffScriptContext after = HumanDiffScriptContext.From(edited);
            bool approximate = original.OriginalAiFileBytes.Length != edited.OriginalAiFileBytes.Length
                               || original.CodeLength != edited.CodeLength
                               || original.Instructions.Count != edited.Instructions.Count;

            foreach (AiDiffRow row in diffRows)
            {
                HumanDiffPoint beforePoint = row.Type == AiDiffType.Added
                    ? HumanDiffPoint.None(row.OriginalText)
                    : before.Describe(row.Offset);
                HumanDiffPoint afterPoint = row.Type == AiDiffType.Removed
                    ? HumanDiffPoint.None(row.EditedText)
                    : after.Describe(row.Offset);

                HumanDiffEntries.Add(AiHumanDiffRow.From(row, beforePoint, afterPoint, approximate));
            }

            HasHumanDiffEntries = HumanDiffEntries.Count > 0;
            int actionRows = HumanDiffEntries.Count(r => r.IsRecognizedAction);
            int technicalRows = HumanDiffEntries.Count - actionRows;
            HumanDiffSummary = technicalRows == 0
                ? string.Format(Strings.U_Ai_HdChangesAsActions, HumanDiffEntries.Count)
                : string.Format(Strings.U_Ai_HdActionsAndTechnical, actionRows, technicalRows);
            HumanDiffEmptyMessage = HasHumanDiffEntries
                ? string.Empty
                : Strings.U_Ai_HdEmpty;
        }
    }

    internal sealed class AiHumanDiffRow
    {
        static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x4C, 0x32));
        static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0x24, 0x2A));
        static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x4B, 0x1C));
        static readonly IBrush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x38, 0x48));

        static readonly IBrush LowRiskBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x5E, 0x45));
        static readonly IBrush MediumRiskBrush = new SolidColorBrush(Color.FromRgb(0x54, 0x49, 0x24));
        static readonly IBrush HighRiskBrush = new SolidColorBrush(Color.FromRgb(0x61, 0x2F, 0x35));
        static readonly IBrush ApproxRiskBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x45, 0x68));

        private AiHumanDiffRow(
            AiDiffRow source,
            string actionHuman,
            string actionTypeLabel,
            string beforeHuman,
            string afterHuman,
            string riskLabel,
            string riskSummary,
            string summary,
            bool isRecognizedAction)
        {
            Type = source.Type;
            ChangeTypeLabel = source.TypeLabel;
            WorkerLabel = source.WorkerLabel;
            OffsetLabel = source.OffsetLabel;
            ActionHuman = actionHuman;
            ActionTypeLabel = actionTypeLabel;
            BeforeHuman = beforeHuman;
            AfterHuman = afterHuman;
            BeforeTechnical = source.OriginalText;
            AfterTechnical = source.EditedText;
            RiskLabel = riskLabel;
            RiskSummary = riskSummary;
            Summary = summary;
            IsRecognizedAction = isRecognizedAction;
        }

        public static AiHumanDiffRow From(
            AiDiffRow source,
            HumanDiffPoint before,
            HumanDiffPoint after,
            bool approximate)
        {
            bool recognized = before.IsRecognizedAction || after.IsRecognizedAction;
            string actionHuman = source.Type switch
            {
                AiDiffType.Added => after.Human,
                AiDiffType.Removed => before.Human,
                AiDiffType.Modified => after.IsMeaningful ? after.Human : before.Human,
                _ => after.IsMeaningful ? after.Human : before.Human,
            };

            string actionType = after.IsMeaningful ? after.ActionTypeLabel : before.ActionTypeLabel;
            (string riskLabel, string riskSummary) = ClassifyRisk(source.Type, before, after, approximate);
            string summary = BuildSummary(source.Type, before, after);

            return new AiHumanDiffRow(
                source,
                actionHuman,
                actionType,
                before.Display,
                after.Display,
                riskLabel,
                riskSummary,
                summary,
                recognized);
        }

        public AiDiffType Type { get; }
        public string ChangeTypeLabel { get; }
        public string WorkerLabel { get; }
        public string OffsetLabel { get; }
        public string ActionHuman { get; }
        public string ActionTypeLabel { get; }
        public string BeforeHuman { get; }
        public string AfterHuman { get; }
        public string BeforeTechnical { get; }
        public string AfterTechnical { get; }
        public string RiskLabel { get; }
        public string RiskSummary { get; }
        public string Summary { get; }
        public bool IsRecognizedAction { get; }

        public IBrush ChangeTypeBrush => Type switch
        {
            AiDiffType.Added => AddedBrush,
            AiDiffType.Removed => RemovedBrush,
            AiDiffType.Modified => ModifiedBrush,
            _ => NeutralBrush,
        };

        public IBrush RiskBrush =>
            RiskLabel == Strings.U_Ai_HdRiskLow ? LowRiskBrush
            : RiskLabel == Strings.U_Ai_HdRiskMedium ? MediumRiskBrush
            : RiskLabel == Strings.U_Ai_HdRiskHigh ? HighRiskBrush
            : ApproxRiskBrush;

        static string BuildSummary(AiDiffType type, HumanDiffPoint before, HumanDiffPoint after)
        {
            return type switch
            {
                AiDiffType.Added => string.Format(Strings.U_Ai_HdAdded, after.Human),
                AiDiffType.Removed => string.Format(Strings.U_Ai_HdRemoved, before.Human),
                AiDiffType.Modified when before.Human == after.Human =>
                    string.Format(Strings.U_Ai_HdInternalDetail, after.Human),
                AiDiffType.Modified =>
                    string.Format(Strings.U_Ai_HdChangedFromTo, before.Human, after.Human),
                _ => after.IsMeaningful ? after.Human : before.Human,
            };
        }

        static (string label, string summary) ClassifyRisk(
            AiDiffType type,
            HumanDiffPoint before,
            HumanDiffPoint after,
            bool approximate)
        {
            if (approximate)
            {
                return (Strings.U_Ai_HdApproximate,
                    Strings.F2_the_structure_size_changed_after_the_fir_bb82810e);
            }

            if (type == AiDiffType.Removed)
                return (Strings.U_Ai_HdRiskHigh, Strings.F2_removes_behavior_confirm_the_flow_in_gam_72c33664);

            if (type == AiDiffType.Added)
                return (Strings.U_Ai_HdRiskMedium, Strings.F2_adds_behavior_structure_is_offline_real__746e358a);

            if (before.IsRecognizedAction && after.IsRecognizedAction)
                return (Strings.U_Ai_HdRiskLow, Strings.F2_local_byte_change_in_recognized_action_s_2e4ae87b);

            return (Strings.U_Ai_HdRiskMedium, Strings.F2_technical_flow_line_not_translated_as_a__819b38dd);
        }
    }

    internal sealed class HumanDiffScriptContext
    {
        readonly IReadOnlyList<AiDetectedAction> actions;
        readonly Dictionary<int, AiInstruction> instructionsByOffset;

        HumanDiffScriptContext(IReadOnlyList<AiDetectedAction> actions, Dictionary<int, AiInstruction> instructionsByOffset)
        {
            this.actions = actions;
            this.instructionsByOffset = instructionsByOffset;
        }

        public static HumanDiffScriptContext From(AiScriptFile script)
        {
            IReadOnlyList<AiDetectedAction> detected;
            try { detected = AiAutomation.DetectActions(script); }
            catch { detected = Array.Empty<AiDetectedAction>(); }

            Dictionary<int, AiInstruction> instructions = script.Instructions
                .GroupBy(i => i.Offset)
                .ToDictionary(g => g.Key, g => g.First());

            return new HumanDiffScriptContext(detected, instructions);
        }

        public HumanDiffPoint Describe(int offset)
        {
            AiDetectedAction? action = FindAction(offset);
            if (action != null)
                return HumanDiffPoint.ForAction(action);

            if (instructionsByOffset.TryGetValue(offset, out AiInstruction? instruction))
                return HumanDiffPoint.ForInstruction(instruction);

            return HumanDiffPoint.None("-");
        }

        AiDetectedAction? FindAction(int offset)
        {
            AiDetectedAction? exact = actions.FirstOrDefault(a =>
                a.CallOffset == offset ||
                a.CmdPushOffset == offset ||
                a.TargetPushOffset == offset ||
                a.RemoveOffsets.Contains(offset));
            if (exact != null)
                return exact;

            return actions.FirstOrDefault(a =>
                a.RemoveOffsets.Count > 0 &&
                offset >= a.RemoveOffsets.Min() &&
                offset <= a.RemoveOffsets.Max());
        }
    }

    internal sealed class HumanDiffPoint
    {
        HumanDiffPoint(
            string human,
            string actionTypeLabel,
            string display,
            bool isRecognizedAction,
            bool isMeaningful)
        {
            Human = human;
            ActionTypeLabel = actionTypeLabel;
            Display = display;
            IsRecognizedAction = isRecognizedAction;
            IsMeaningful = isMeaningful;
        }

        public string Human { get; }
        public string ActionTypeLabel { get; }
        public string Display { get; }
        public bool IsRecognizedAction { get; }
        public bool IsMeaningful { get; }

        public static HumanDiffPoint None(string technical)
        {
            string text = string.IsNullOrWhiteSpace(technical) || technical == "-"
                ? Strings.F2_no_line_ec9226a5
                : technical;
            return new HumanDiffPoint(Strings.U_Ai_HdNoAction, Strings.F2_none_71f8e797, text, false, false);
        }

        public static HumanDiffPoint ForAction(AiDetectedAction action)
        {
            string human = ActionHuman(action);
            string type = action.Kind switch
            {
                AiActionKind.Command => Strings.U_Ai_HdKindCommand,
                AiActionKind.Buff => Strings.U_Ai_HdKindBuff,
                AiActionKind.Stat => Strings.U_Ai_HdKindStat,
                _ => Strings.U_Ai_HdKindAction,
            };
            return new HumanDiffPoint(human, type, human, true, true);
        }

        public static HumanDiffPoint ForInstruction(AiInstruction instruction)
        {
            string human = InstructionHuman(instruction);
            string type = InstructionType(instruction);
            string tech = AiScript_File.Format(instruction);
            return new HumanDiffPoint(human, type, $"{human} · {tech}", false, true);
        }

        static string ActionHuman(AiDetectedAction action)
        {
            return action.Kind switch
            {
                AiActionKind.Command =>
                    string.Format(Strings.U_Ai_HdUses, action.AbilityName, CommandTarget(action), CommandMode(action)),
                AiActionKind.Buff when action.FieldValue == 0 =>
                    string.Format(Strings.U_Ai_HdRemoves, action.AbilityName),
                AiActionKind.Buff =>
                    string.Format(Strings.U_Ai_HdApplies, action.AbilityName),
                AiActionKind.Stat =>
                    string.Format(Strings.U_Ai_HdAdjusts, action.AbilityName),
                _ => action.AbilityName,
            };
        }

        static string CommandTarget(AiDetectedAction action)
        {
            if (!action.TargetIsLiteral)
                return Strings.U_Ai_HdScriptTarget;
            return string.Format(Strings.U_Ai_HdTargetOn, FriendlyTarget(action.TargetOperand));
        }

        static string CommandMode(AiDetectedAction action) =>
            action.ForcePerform ? Strings.F2_executes_now_b405f4e0 : Strings.U_Ai_HdNormalQueue;

        static string FriendlyTarget(ushort operand)
        {
            return AiTargetNames.Get(operand) switch
            {
                "Self" => Strings.F2_self_40380bc1,
                "FrontlineChars" => Strings.F2_all_active_characters_f8a40d0e,
                "Character#1" => Strings.U_Ai_HdTargetChar1,
                "Character#2" => Strings.U_Ai_HdTargetChar2,
                "Character#3" => Strings.U_Ai_HdTargetChar3,
                "AllMonsters" => Strings.F2_all_allied_monsters_118cf8fc,
                "AllAeons" => Strings.F2_all_aeons_b930a0be,
                "LastAttacker" => Strings.U_Ai_HdTargetLastAttacker,
                "TargetActors" => Strings.U_Ai_HdTargetCurrent,
                "TargetActorsNow" => Strings.U_Ai_HdTargetImmediate,
                "AllActors" => Strings.F2_all_actors_4c82e9ce,
                "ActiveActors" => Strings.F2_active_actor_owner_rt2_the_monster_hit_i_7fa05aea,
                "Null" => Strings.F2_no_target_b46e1180,
                string name => name,
                null => string.Format(Strings.U_Ai_HdTargetLiteral, (short)operand),
            };
        }

        static string InstructionHuman(AiInstruction instruction)
        {
            string mnemonic = AiScript_File.Mnemonic(instruction.Opcode);
            if (!instruction.HasOperand)
                return string.Format(Strings.U_Ai_HdLogicOp, mnemonic);

            return AiScript_File.OperandKindOf(instruction.Opcode) switch
            {
                AiOperandKind.FuncId => FunctionHuman(instruction.Operand),
                AiOperandKind.Immediate => ImmediateHuman(instruction.Operand),
                AiOperandKind.JumpIndex => instruction.Opcode == 0xB0
                    ? string.Format(Strings.U_Ai_HdJumpTo, instruction.Operand)
                    : string.Format(Strings.U_Ai_HdCondDecides, instruction.Operand),
                AiOperandKind.VarLoad => string.Format(Strings.U_Ai_HdReadsVar, instruction.Operand),
                AiOperandKind.VarStore => string.Format(Strings.U_Ai_HdWritesVar, instruction.Operand),
                AiOperandKind.ArrLoad => string.Format(Strings.U_Ai_HdReadsArr, instruction.Operand),
                AiOperandKind.ArrStore => string.Format(Strings.U_Ai_HdWritesArr, instruction.Operand),
                AiOperandKind.FloatConst => string.Format(Strings.U_Ai_HdFloatConst, instruction.Operand),
                AiOperandKind.IntConst => string.Format(Strings.U_Ai_HdIntConst, instruction.Operand),
                _ => $"{mnemonic} 0x{instruction.Operand:X4}",
            };
        }

        static string FunctionHuman(ushort funcId)
        {
            return funcId switch
            {
                AiAutomation.PerformCommand => Strings.F2_executes_the_skill_stacked_in_the_normal_144794fd,
                AiAutomation.ForcePerformCommand => Strings.F2_force_the_stacked_ability_now_a727895a,
                0x7018 => Strings.F2_changes_status_field_of_an_actor_98b74c6b,
                0x70AB => Strings.F2_adjusts_actor_s_stat_field_87964a03,
                _ => string.Format(Strings.U_Ai_HdCalls, AiScript_File.CallNamespace(funcId), AiScript_File.CallName(funcId)),
            };
        }

        static string ImmediateHuman(ushort operand)
        {
            if (AiCommandId.IsCommandOperand(operand))
            {
                AiCommandDecode decoded = AiCommandId.Decode(operand);
                string name = decoded.IsKnown ? decoded.Name : string.Format(Strings.U_Ai_HdCommandFallback, operand);
                return string.Format(Strings.U_Ai_HdSelectsSkill, name);
            }

            string? target = AiTargetNames.Get(operand);
            if (target != null)
                return string.Format(Strings.U_Ai_HdSelectsTarget, FriendlyTarget(operand));

            return string.Format(Strings.U_Ai_HdUsesValue, (short)operand, operand);
        }

        static string InstructionType(AiInstruction instruction)
        {
            if (!instruction.HasOperand)
                return Strings.U_Ai_HdLogic;

            return AiScript_File.OperandKindOf(instruction.Opcode) switch
            {
                AiOperandKind.FuncId => Strings.U_Ai_HdCall,
                AiOperandKind.JumpIndex => Strings.U_Ai_HdConditionFlow,
                AiOperandKind.VarLoad or AiOperandKind.VarStore => Strings.U_Ai_HdVariable,
                AiOperandKind.ArrLoad or AiOperandKind.ArrStore => Strings.U_Ai_HdArray,
                AiOperandKind.Immediate => AiCommandId.IsCommandOperand(instruction.Operand) ? Strings.U_Ai_HdKindCommand : Strings.F2_value_target_3f42f828,
                _ => Strings.U_Ai_HdInstruction,
            };
        }
    }
}
