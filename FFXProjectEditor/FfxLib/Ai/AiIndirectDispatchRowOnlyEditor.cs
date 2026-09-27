using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiIndirectDispatchEditableSlot(
        string RoleKey,
        string SlotLabel,
        int PushInstructionOffset,
        ushort CurrentValue,
        string CurrentValueResolved);

    public sealed record AiIndirectDispatchEditableOperand(
        string RoleKey,
        string RoleLabel,
        int InstructionOffset,
        byte Opcode,
        ushort CurrentValue,
        string CurrentValueResolved);

    public sealed record AiIndirectDispatchVariableReference(
        ushort VariableIndex,
        string VariableName);

    public enum AiIndirectDispatchTargetSlotSourceKind
    {
        Literal,
        ComputedRecipe,
        CopiedValue,
        Unknown,
    }

    public sealed record AiIndirectDispatchEditableTargetSlot(
        string RoleKey,
        string SlotLabel,
        ushort VariableIndex,
        string VariableName,
        int SourceInstructionOffset,
        AiIndirectDispatchTargetSlotSourceKind SourceKind,
        ushort CurrentValue,
        string CurrentValueResolved,
        string DetailSummary,
        bool CanEdit,
        AiTargetRecipeKind? RecipeKind,
        IReadOnlyList<AiIndirectDispatchEditableOperand> EditableOperands,
        IReadOnlyList<AiIndirectDispatchVariableReference> CopyVariableCandidates);

    public sealed record AiIndirectDispatchValueEdit(
        string RoleKey,
        string RoleLabel,
        int InstructionOffset,
        ushort OldValue,
        ushort NewValue,
        byte? OldOpcode = null,
        byte? NewOpcode = null);

    public sealed record AiIndirectDispatchEditRequest(
        string UnitId,
        IReadOnlyList<AiIndirectDispatchValueEdit> Edits);

    public sealed record AiIndirectDispatchByteChange(
        int Offset,
        byte OldValue,
        byte NewValue);

    public sealed record AiIndirectDispatchEditResult(
        AiIndirectDispatchEditRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiIndirectDispatchByteChange> ChangedBytes);

    public static class AiIndirectDispatchRowOnlyEditor
    {
        const byte PushIiOpcode = 0xAE;
        const byte PushVOpcode = 0x9F;

        public static bool TryApplyEdits(
            AiScriptFile script,
            AiIndirectDispatchEditRequest request,
            out AiIndirectDispatchEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para edicao row-only.";
                return false;
            }

            if (request.Edits == null || request.Edits.Count == 0)
            {
                error = "No change was requested for this indirect unit.";
                return false;
            }

            if (request.Edits
                .GroupBy(edit => edit.InstructionOffset)
                .Any(group => group.Count() > 1))
            {
                error = "The request provided two different values for the same offset.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, byte OriginalOpcode, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                foreach (AiIndirectDispatchValueEdit edit in request.Edits)
                {
                    AiInstruction? instruction = script.Instructions.FirstOrDefault(i => i.Offset == edit.InstructionOffset);
                    if (instruction == null)
                    {
                        error = $"Nao achei a instrucao 0x{edit.InstructionOffset:X4} da unidade indireta.";
                        return false;
                    }

                    if ((instruction.Opcode != PushIiOpcode && instruction.Opcode != PushVOpcode) || !instruction.HasOperand)
                    {
                        error = $"Offset 0x{edit.InstructionOffset:X4} nao e um PUSHII/PUSHV editavel.";
                        return false;
                    }

                    if (edit.OldOpcode.HasValue && instruction.Opcode != edit.OldOpcode.Value)
                    {
                        error =
                            $"Offset 0x{edit.InstructionOffset:X4} mudou de opcode desde a leitura. " +
                            $"Esperado 0x{edit.OldOpcode.Value:X2}, encontrado 0x{instruction.Opcode:X2}.";
                        return false;
                    }

                    if (instruction.Operand != edit.OldValue)
                    {
                        error =
                            $"Offset 0x{edit.InstructionOffset:X4} mudou desde a leitura. " +
                            $"Esperado 0x{edit.OldValue:X4}, encontrado 0x{instruction.Operand:X4}.";
                        return false;
                    }

                    byte newOpcode = edit.NewOpcode ?? instruction.Opcode;
                    if (newOpcode != PushIiOpcode && newOpcode != PushVOpcode)
                    {
                        error = $"Opcode 0x{newOpcode:X2} nao e suportado no writer row-only.";
                        return false;
                    }

                    if (edit.RoleKey.StartsWith("command", StringComparison.OrdinalIgnoreCase)
                        && (newOpcode != PushIiOpcode || !AiCommandId.IsCommandOperand(edit.NewValue)))
                    {
                        error = $"0x{edit.NewValue:X4} nao parece um command operand valido para '{edit.RoleLabel}'.";
                        return false;
                    }

                    if (newOpcode == PushVOpcode
                        && (edit.NewValue >= script.Variables.Count))
                    {
                        error = $"0x{edit.NewValue:X4} nao aponta para uma var valida em '{edit.RoleLabel}'.";
                        return false;
                    }

                    touched.Add((instruction, instruction.Opcode, instruction.Operand));
                    instruction.Opcode = newOpcode;
                    instruction.Operand = edit.NewValue;
                }

                byte[] editedAi = AiScript_File.Write(script);
                List<AiIndirectDispatchByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);

                HashSet<int> allowedOffsets = request.Edits
                    .SelectMany(edit =>
                    {
                        if (edit.OldOpcode.HasValue || edit.NewOpcode.HasValue)
                            return new[] { edit.InstructionOffset, edit.InstructionOffset + 1, edit.InstructionOffset + 2 };
                        return new[] { edit.InstructionOffset + 1, edit.InstructionOffset + 2 };
                    })
                    .ToHashSet();

                List<AiIndirectDispatchByteChange> forbidden = changed
                    .Where(change => !allowedOffsets.Contains(change.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error =
                        "A validacao row-only abortou: apareceram bytes fora dos offsets autorizados. " +
                        string.Join("; ", forbidden.Select(change =>
                            $"+0x{change.Offset:X4} {change.OldValue:X2}->{change.NewValue:X2}"));
                    return false;
                }

                result = new AiIndirectDispatchEditResult(request, editedAi, changed);
                success = true;
                return true;
            }
            finally
            {
                if (!success)
                {
                    foreach ((AiInstruction instruction, byte originalOpcode, ushort originalOperand) in touched)
                    {
                        instruction.Opcode = originalOpcode;
                        instruction.Operand = originalOperand;
                    }
                }
            }
        }

        static List<AiIndirectDispatchByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiIndirectDispatchByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiIndirectDispatchByteChange(i, before[i], after[i]));
            }

            return changes;
        }
    }
}
