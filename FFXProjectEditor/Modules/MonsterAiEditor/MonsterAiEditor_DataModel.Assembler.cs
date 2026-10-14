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
    // AI ASSEMBLER (free-edit) surface — ADDITIVE on top of the byte-local operand editor.
    // Insert / remove / modify whole instructions; the rebuilt AiFile comes from the RT0-proven
    // AiScript_File.Rebuild (relocates entrypoints + jump tables automatically via an old->new offset map).
    //
    // SAVE policy (proven by --aiasm-rt0): a length-PRESERVING rebuild splices back byte-safe via
    // SpliceAiFileIntoMonster (verbatim copy, no header rewrite -> no codeLength clobber). GROW/SHRINK uses
    // SpliceAiFileIntoMonsterGrow: it 16-pads the AiFile, shifts later sections and rewrites the header section
    // pointers (writing the AiFile verbatim so codeLength is NOT clobbered). RE-proven safe — the in-game loader
    // sizes the VM from worker counts + per-worker datalen (not the partition/DeclaredLength/codeLength) and
    // trusts the in-file section pointers (docs/reverse/FFX_AIFILE_LOADER_RENAME_QUEUE_2026-06-05.md). In-game
    // (RT2) validation via the probe is still recommended for a grow before treating it as production.
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiAsmRow> AssemblerInstructions { get; } = new();
        bool HasPendingAssemblerEdits => AssemblerInstructions.Any(row => row.IsModified);
        bool HasPendingAuthoringEdits => HasPendingAssemblerEdits
            || (selectedScript != null && AiAdvancedFileWriter.HasPendingEdits(selectedScript));

        [ObservableProperty] private AiAsmRow? selectedAssemblerRow;
        [ObservableProperty] private string insertOpcodeHex = string.Empty;
        [ObservableProperty] private string insertOperandHex = string.Empty;
        [ObservableProperty] private string assemblerSummary = Strings.U_Ai_AssemblerSummary;
        [ObservableProperty] private string assemblerPreview = string.Empty;

        // Called from UpdateSelected (main partial) whenever the selected monster/script changes.
        void RebuildAssemblerRows()
        {
            AssemblerInstructions.Clear();
            SelectedAssemblerRow = null;
            if (selectedScript == null || !selectedScript.HasScript)
            {
                AssemblerPreview = string.Empty;
                return;
            }
            if (SelectedMonster?.AssemblerDraft is { } draft)
            {
                foreach (AiAsmRow row in draft) AssemblerInstructions.Add(row);
            }
            else
            {
                foreach (AiInstruction ins in selectedScript.Instructions)
                    AssemblerInstructions.Add(new AiAsmRow(ins, RefreshAssemblerPreview));
            }
            RefreshAssemblerPreview();
        }

        public void InsertAssemblerInstruction()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            {
                AssemblerSummary = Strings.F2_select_a_monster_bin_with_a_real_aifile_6f80459b;
                return;
            }

            string opHex = (InsertOpcodeHex ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            if (!byte.TryParse(opHex, System.Globalization.NumberStyles.HexNumber, null, out byte opcode))
            {
                AssemblerSummary = string.Format(Strings.U_Ai_AsmOpcodeInvalid, InsertOpcodeHex);
                return;
            }

            ushort operand = 0;
            if (AiScript_File.IsOperandBearing(opcode))
            {
                string opnd = (InsertOperandHex ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                if (opnd.Length > 0 && !ushort.TryParse(opnd, System.Globalization.NumberStyles.HexNumber, null, out operand))
                {
                    AssemblerSummary = string.Format(Strings.U_Ai_AsmOperandInvalid, InsertOperandHex);
                    return;
                }
            }

            var row = new AiAsmRow(opcode, operand, RefreshAssemblerPreview);
            int at = SelectedAssemblerRow != null ? AssemblerInstructions.IndexOf(SelectedAssemblerRow) + 1 : AssemblerInstructions.Count;
            AssemblerInstructions.Insert(at, row);
            SelectedAssemblerRow = row;
            AssemblerSummary = string.Format(Strings.U_Ai_AsmInserted, AiScript_File.Mnemonic(opcode), (AiScript_File.IsOperandBearing(opcode) ? "3B" : "1B"), at);
            RefreshAssemblerPreview();
        }

        // Toggle the Removed flag on the selected row (so the delta preview shows it before Save).
        public void RemoveSelectedAssemblerRow()
        {
            if (SelectedAssemblerRow == null)
            {
                AssemblerSummary = Strings.F2_select_an_instruction_in_the_assembler_l_e6b03140;
                return;
            }
            SelectedAssemblerRow.Removed = !SelectedAssemblerRow.Removed;
            AssemblerSummary = SelectedAssemblerRow.Removed
                ? string.Format(Strings.U_Ai_AsmMarkedRemoved, SelectedAssemblerRow.Label)
                : string.Format(Strings.U_Ai_AsmRemovalUndone, SelectedAssemblerRow.Label);
            RefreshAssemblerPreview();
        }

        List<AiInstruction> BuildAssemblerInstrList() =>
            AssemblerInstructions.Where(r => !r.Removed).Select(r => r.ToInstruction()).ToList();

        void RefreshAssemblerPreview()
        {
            if (selectedScript == null || !selectedScript.HasScript) { AssemblerPreview = string.Empty; return; }
            try
            {
                List<AiInstruction> list = BuildAssemblerInstrList();
                byte[] rebuilt = AiScript_File.Rebuild(selectedScript, list);
                int oldLen = selectedScript.OriginalAiFileBytes.Length;
                int delta = rebuilt.Length - oldLen;
                int removed = AssemblerInstructions.Count(r => r.Removed);
                int inserted = AssemblerInstructions.Count(r => r.IsNew && !r.Removed);
                string mode = delta == 0
                    ? "length-preserving — splice verbatim (byte-safe)"
                    : (delta > 0 ? $"GROW +{delta}B — grow-aware splice (16-pad, RE-safe)" : $"SHRINK {delta}B — grow-aware splice (16-pad, RE-safe)");
                AssemblerPreview =
                    string.Format(Strings.U_Ai_AsmPreviewInstr, selectedScript.Instructions.Count, list.Count, inserted, removed) + Environment.NewLine +
                    string.Format(Strings.U_Ai_AsmPreviewAiFile, oldLen, rebuilt.Length, mode);
            }
            catch (Exception ex)
            {
                AssemblerPreview = $"Rebuild rejeitado: {ex.Message}";
            }
        }

        // 🪄 LEVEL 3 — auto-fix, HONEST: only applies the changes that have a SINGLE correct answer. The one
        // unambiguous fix in this model is RESTORING a row you marked for removal that turns out to be the target of
        // a jump/entrypoint — it MUST stay (re-pointing the branch would CHANGE behaviour = a guess, so we don't).
        // Everything else (unknown opcode, jump-index out of range, stack imbalance) has no single right answer, so
        // it is SUGGESTED in the report, never auto-applied ("não chuta a tua intenção"). Iterates because restoring
        // one row can resolve/reveal others; never edits operands/opcodes or removes anything.
        public void TryAutoFixAssembler()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            { AssemblerSummary = Strings.F2_select_a_monster_bin_with_a_real_aifile_46212014; return; }

            int restored = 0;
            for (int pass = 0; pass < 16; pass++)
            {
                AiValidationReport rep = AiValidator.Validate(selectedScript, BuildAssemblerInstrList());
                if (rep.IsValid) break;
                bool changed = false;
                foreach (AiValidationFinding err in rep.Errors)
                {
                    // A dangling branch/entrypoint Error carries the AiFile offset of the REMOVED target instruction.
                    // If that offset matches a row the user marked Removed, un-remove it (the single safe resolution).
                    bool dangling = err.Message.Contains("REMOVED", StringComparison.OrdinalIgnoreCase)
                                    || err.Message.Contains("orphaned", StringComparison.OrdinalIgnoreCase);
                    if (!dangling || err.Offset < 0) continue;
                    AiAsmRow? row = AssemblerInstructions.FirstOrDefault(r => r.Removed && !r.IsNew && r.OriginalOffset == err.Offset);
                    if (row != null) { row.Removed = false; restored++; changed = true; }
                }
                if (!changed) break;
            }

            AiValidationReport final = AiValidator.Validate(selectedScript, BuildAssemblerInstrList());
            ValidationReport = final.ToReportString();
            RefreshAssemblerPreview();

            int left = final.ErrorCount;
            if (restored == 0 && left == 0)
                AssemblerSummary = Strings.F2_nothing_to_fix_the_edit_is_already_valid_845560e7;
            else if (left == 0)
                AssemblerSummary = string.Format(Strings.U_Ai_AsmAutoFixed, restored);
            else
                AssemblerSummary = string.Format(Strings.U_Ai_AsmAutoFixedLeft, restored, left);
        }

        public void SaveAssembler()
        {
            if (selectedScript == null || selectedPath == null) { AssemblerSummary = Strings.F2_no_aifile_selected_4180056c; return; }

            if (AiAdvancedFileWriter.HasPendingEdits(selectedScript))
            { AssemblerSummary = Strings.AiAdvancedPendingEdits; return; }

            // Pre-flight: refuse to write when the validator finds Errors (dangling branch, unknown opcode, ...),
            // so the user gets a precise report instead of an opaque Rebuild throw. Warnings (grow/shrink) do not block.
            AiValidationReport check = AiValidator.Validate(selectedScript, BuildAssemblerInstrList());
            if (!check.IsValid)
            {
                ValidationReport = check.ToReportString();
                AssemblerSummary = string.Format(Strings.U_Ai_AsmSaveBlocked, check.ErrorCount);
                return;
            }

            byte[] rebuilt;
            try { rebuilt = AiScript_File.Rebuild(selectedScript, BuildAssemblerInstrList()); }
            catch (Exception ex)
            {
                AssemblerSummary = string.Format(Strings.U_Ai_AsmRebuildRejected, ex.Message);
                return;
            }

            int oldLen = selectedScript.OriginalAiFileBytes.Length;
            bool grew = rebuilt.Length != oldLen;

            byte[] outBin;
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                // Length-preserving -> verbatim same-length splice. GROW/SHRINK -> grow-aware splice that
                // 16-pads the AiFile, shifts later sections and rewrites the header pointers (RE-proven safe;
                // no codeLength clobber). See docs/reverse/FFX_AIFILE_LOADER_RENAME_QUEUE_2026-06-05.md.
                outBin = grew
                    ? AiScript_File.SpliceAiFileIntoMonsterGrow(monster, rebuilt)
                    : AiScript_File.SpliceAiFileIntoMonster(monster, rebuilt);
            }
            catch (Exception ex) { AssemblerSummary = string.Format(Strings.U_Ai_SaveAbortedSplice, ex.Message); return; }
            try { WriteMonsterWithBackup(outBin, applyingAssembler: true); }
            catch (Exception ex) { AssemblerSummary = string.Format(Strings.U_Ai_SaveAbortedWrite, ex.Message); return; }

            // Reload from disk so every surface reflects the saved file (offsets / disassembly / action list).
            bool reloaded = ReloadSelectedFromDisk();
            if (reloaded && selectedScript != null)
                AssemblerSummary = grew
                    ? string.Format(Strings.U_Ai_AsmSavedGrow, Path.GetFileName(selectedPath), oldLen, rebuilt.Length, selectedScript.CodeLength)
                    : string.Format(Strings.U_Ai_AsmSavedPreserving, Path.GetFileName(selectedPath), selectedScript.CodeLength);
            else
                AssemblerSummary = string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath));
        }
    }

    // One row in the AI Assembler list: an existing instruction (OriginalOffset >= 0) or an inserted one
    // (IsNew, OriginalOffset = -1). OpcodeHex/OperandHex bind TwoWay; Removed marks it for deletion on Save.
    internal sealed class AiAsmRow : ObservableObject
    {
        readonly Action? onChanged;
        byte opcode;
        ushort operand;

        readonly byte originalOpcode;
        readonly ushort originalOperand;

        public AiAsmRow(AiInstruction ins, Action? onChanged)
        {
            OriginalOffset = ins.Offset;
            IsNew = false;
            originalOpcode = opcode = ins.Opcode;
            originalOperand = operand = ins.Operand;
            this.onChanged = onChanged;
        }

        public AiAsmRow(byte opcode, ushort operand, Action? onChanged)
        {
            OriginalOffset = -1;
            IsNew = true;
            this.opcode = opcode;
            this.operand = operand;
            this.onChanged = onChanged;
        }

        public int OriginalOffset { get; }
        public bool IsNew { get; }
        public bool Removed { get; set; }
        public bool IsModified => IsNew ? !Removed
            : Removed || opcode != originalOpcode || (HasOperand && operand != originalOperand);

        public bool HasOperand => AiScript_File.IsOperandBearing(opcode);
        public string Label => IsNew
            ? $"NEW     {AiScript_File.Mnemonic(opcode)}"
            : $"0x{OriginalOffset:X4}  {AiScript_File.Mnemonic(opcode)}";

        // Plain-language decode of this instruction so the raw opcode/operand hex isn't a guessing game
        // (CALLPOPA -> Battle.<name>, jump -> label, etc.). OpcodeHelp is the opcode's hover explanation.
        public string Meaning => AiScript_File.OperandGloss(opcode, operand);
        public string OpcodeHelp => AiScript_File.OpcodeHelp(opcode);
        // Combined readable line: mnemonic + operand gloss. e.g. "PUSHII  0xFF03  (alvo: Self)" / "CALLPOPA  Battle.performCommand" / "ADD"
        public string ReadableText => HasOperand
            ? $"{AiScript_File.Mnemonic(opcode)}  {AiScript_File.OperandGloss(opcode, operand)}"
            : AiScript_File.Mnemonic(opcode);

        public string OpcodeHex
        {
            get => opcode.ToString("X2");
            set
            {
                string s = (value ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                if (byte.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out byte v) && v != opcode)
                {
                    opcode = v;
                    onChanged?.Invoke();
                    // opcode change alters the mnemonic (Label), whether it bears an operand, and the decoded Meaning
                    OnPropertyChanged(nameof(Label));
                    OnPropertyChanged(nameof(HasOperand));
                    OnPropertyChanged(nameof(OperandHex));
                    OnPropertyChanged(nameof(OpcodeHelp));
                    OnPropertyChanged(nameof(Meaning));
                    OnPropertyChanged(nameof(ReadableText));
                }
            }
        }

        public string OperandHex
        {
            get => HasOperand ? operand.ToString("X4") : string.Empty;
            set
            {
                if (!HasOperand) return;
                string s = (value ?? string.Empty).Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                if (ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out ushort v) && v != operand)
                {
                    operand = v;
                    onChanged?.Invoke();
                    OnPropertyChanged(nameof(Meaning));
                    OnPropertyChanged(nameof(ReadableText));
                }
            }
        }

        public AiInstruction ToInstruction() => new AiInstruction
        {
            Offset = IsNew ? -1 : OriginalOffset,
            Opcode = opcode,
            HasOperand = HasOperand,
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),   // so disassembly/analysis of editor-built rows is correct
        };
    }
}
