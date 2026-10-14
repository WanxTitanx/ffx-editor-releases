using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // Two things live here:
    //   (1) the top-chrome session state (Save/Undo enablement), and
    //   (2) INLINE high-level editing of the selected recognized action, RIGHT IN THE ACTION LIST — the actions list
    //       IS the monster's behaviour, so you edit it there, not in a raw-opcode view. For a buff (🛡) or stat (📊)
    //       you change the STATUS/FIELD and the VALUE in place; the edit is byte-local (it rewrites the two PUSHII
    //       operands — field + value — of the existing statement and saves the loose monster_*.bin, same path the
    //       Save button uses). Command rows already edit via the ability picker + "Mudar alvo" + force toggle nearby.
    //       Insert/remove/reorder and moving an action between workers stay structural (a future cut).
    internal partial class MonsterAiEditor_DataModel
    {
        // ---- top-chrome session state ----
        [ObservableProperty] private bool hasPendingEdits;   // any operand delta not yet flushed -> enables 💾 Salvar
        [ObservableProperty] private bool canUndoBackup;      // a .prev.bak exists -> enables ↶ Desfazer
        void RefreshSessionState() => CanUndoBackup = selectedPath != null && File.Exists(selectedPath + ".prev.bak");

        // ---- inline edit of the selected buff/stat row (status + value), in place, no assembly ----
        const byte PushIiOpcode = 0xAE;   // AE PUSHII — the immediate-push whose operand is the field id / value
        public IReadOnlyList<AiFieldPlantOption> BuffStatFieldOptions => _allPlantFields;
        [ObservableProperty] private bool canEditBuffStat;
        [ObservableProperty] private AiFieldPlantOption? selectedBuffStatField;
        [ObservableProperty] private string buffStatValue = "";
        [ObservableProperty] private string buffStatEditSummary = Strings.U_Ai_IeIntro;

        // Wired from OnSelectedAutomationActionChanged (.Automations.cs). Enables the inline editor only when the
        // selected action is a buff/stat whose field + value are literal PUSHII immediates (a fixed value we can
        // rewrite byte-local); seeds the dropdown + value box with the current values.
        void RebuildBuffStatEditor()
        {
            AiActionVm? sel = SelectedAutomationAction;
            AiInstruction? fieldInstr = sel == null ? null : FindFieldInstr(sel.Action);
            AiInstruction? valueInstr = sel == null ? null : FindBuffStatValueInstr(sel.Action);
            CanEditBuffStat = sel != null
                && (sel.Action.Kind == AiActionKind.Buff || sel.Action.Kind == AiActionKind.Stat)
                && fieldInstr is { Opcode: PushIiOpcode }
                && valueInstr is { Opcode: PushIiOpcode };
            if (!CanEditBuffStat || sel == null) return;
            SelectedBuffStatField = BuffStatFieldOptions.FirstOrDefault(o => o.FieldId == sel.Action.FieldId)
                                    ?? BuffStatFieldOptions.FirstOrDefault();
            BuffStatValue = sel.Action.FieldValue.ToString();
            BuffStatEditSummary = string.Format(Strings.U_Ai_IeEditing, sel.Action.AbilityName);
        }

        AiInstruction? FindFieldInstr(AiDetectedAction a) =>
            selectedScript?.Instructions.FirstOrDefault(i => i.Offset == a.CmdPushOffset);

        // The value PUSHII is the statement's last arg push — the instruction immediately before the CALLPOPA.
        AiInstruction? FindBuffStatValueInstr(AiDetectedAction a)
        {
            if (selectedScript == null) return null;
            IReadOnlyList<AiInstruction> ins = selectedScript.Instructions;
            for (int i = 1; i < ins.Count; i++)
                if (ins[i].Offset == a.CallOffset) return ins[i - 1];
            return null;
        }

        public void EditSelectedBuffStat()
        {
            if (selectedScript == null || selectedPath == null)
            { BuffStatEditSummary = Strings.U_Ai_IeNeedAiFile; return; }
            AiActionVm? sel = SelectedAutomationAction;
            if (sel == null || (sel.Action.Kind != AiActionKind.Buff && sel.Action.Kind != AiActionKind.Stat))
            { BuffStatEditSummary = Strings.U_Ai_IeSelectBuffStat; return; }
            AiFieldPlantOption? field = SelectedBuffStatField;
            if (field == null) { BuffStatEditSummary = Strings.U_Ai_IePickStatusField; return; }

            AiInstruction? fieldInstr = FindFieldInstr(sel.Action);
            AiInstruction? valueInstr = FindBuffStatValueInstr(sel.Action);
            if (fieldInstr is not { Opcode: PushIiOpcode } || valueInstr is not { Opcode: PushIiOpcode })
            { BuffStatEditSummary = Strings.U_Ai_IeComputedValue; return; }

            if (!TryParseU16Loose(BuffStatValue, out ushort newValue))
            { BuffStatEditSummary = Strings.AiAdvancedInvalidNumber; return; }
            int callOffset = sel.Action.CallOffset;     // byte-local edit preserves offsets -> stable across reload
            fieldInstr.Operand = field.FieldId;
            valueInstr.Operand = newValue;
            RefreshPendingEditPreview();
            if (!Save())
            { BuffStatEditSummary = LoadSummary; return; }

            // Keep the edited row selected so the inline editor stays on it (Save's reload resets selection otherwise).
            SelectedAutomationAction = AutomationActions.FirstOrDefault(v => v.Action.CallOffset == callOffset)
                                       ?? SelectedAutomationAction;
            BuffStatEditSummary = string.Format(Strings.U_Ai_IeApplied, field.Name, newValue, Path.GetFileName(selectedPath));
        }
    }
}
