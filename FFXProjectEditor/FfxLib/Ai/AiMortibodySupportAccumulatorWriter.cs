using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiMortibodyAccumulatorDescriptor(
        string VariableName,
        string RoleLabel,
        int StoreInstructionOffset,
        int ScoreInstructionOffset,
        ushort CurrentScoreValue)
    {
        public string CurrentScoreSummary => $"{VariableName} = {CurrentScoreValue} @0x{ScoreInstructionOffset:X4}";
    }

    public sealed record AiMortibodyAccumulatorPatchRequest(
        string VariableName,
        int ScoreInstructionOffset,
        ushort OldScoreValue,
        ushort NewScoreValue);

    public sealed record AiMortibodyAccumulatorByteChange(int Offset, byte OldValue, byte NewValue);

    public sealed record AiMortibodyAccumulatorEditResult(
        AiMortibodyAccumulatorPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiMortibodyAccumulatorByteChange> ChangedBytes);

    public static class AiMortibodySupportAccumulatorWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte PopVariableOpcode = 0xA0;
        const byte CallOpcode = 0xB5;
        const ushort ForcePerformCommand = 0x705A;

        static readonly string[] AccumulatorVarNames = { "priv0010", "priv0014", "priv0018", "priv001C" };

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiMortibodyAccumulatorDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiMortibodyAccumulatorDescriptor>();
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar acumuladores do Mortibody.";
                return false;
            }

            var built = new List<AiMortibodyAccumulatorDescriptor>();

            foreach (string varName in AccumulatorVarNames)
            {
                if (TryBuildAccumulatorDescriptor(script, varName, out AiMortibodyAccumulatorDescriptor? desc, out _))
                    built.Add(desc!);
            }

            if (built.Count < 2)
            {
                error = "The Mortibody requires at least 2 proven accumulators priv0010/0014/0018/001C.";
                descriptors = Array.Empty<AiMortibodyAccumulatorDescriptor>();
                return false;
            }

            descriptors = built;
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiMortibodyAccumulatorPatchRequest request,
            out AiMortibodyAccumulatorEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do Mortibody.";
                return false;
            }

            if (request == null || request.ScoreInstructionOffset < 0)
            {
                error = "Patch do Mortibody veio sem offset.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                if (!TryPatchPushIiOperand(script, request.ScoreInstructionOffset, request.OldScoreValue, request.NewScoreValue, touched, out error))
                    return false;

                byte[] editedAi = AiScript_File.Write(script);
                List<AiMortibodyAccumulatorByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = new()
                {
                    request.ScoreInstructionOffset + 1,
                    request.ScoreInstructionOffset + 2,
                };
                List<AiMortibodyAccumulatorByteChange> forbidden = changed
                    .Where(c => !allowedOffsets.Contains(c.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error = "O patch raw do Mortibody abortou: apareceram bytes fora dos PUSHII autorizados. " +
                        string.Join("; ", forbidden.Select(c => $"+0x{c.Offset:X4} {c.OldValue:X2}->{c.NewValue:X2}"));
                    return false;
                }

                result = new AiMortibodyAccumulatorEditResult(request, editedAi, changed);
                success = true;
                return true;
            }
            finally
            {
                if (!success)
                {
                    foreach ((AiInstruction instruction, ushort originalOperand) in touched)
                        instruction.Operand = originalOperand;
                }
            }
        }

        static bool TryBuildAccumulatorDescriptor(
            AiScriptFile script,
            string variableName,
            out AiMortibodyAccumulatorDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (!TryFindVariableIndexByName(script, variableName, out ushort variableIndex))
            {
                error = $"A var {variableName} nao existe neste script.";
                return false;
            }

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 1; i < instructions.Count; i++)
            {
                AiInstruction store = instructions[i];
                if (store.Opcode != PopVariableOpcode || !store.HasOperand || store.Operand != variableIndex)
                    continue;

                AiInstruction scorePush = instructions[i - 1];
                if (scorePush.Opcode != PushIiOpcode || !scorePush.HasOperand)
                    continue;

                descriptor = new AiMortibodyAccumulatorDescriptor(
                    variableName,
                    $"Acumulador {variableName}",
                    store.Offset,
                    scorePush.Offset,
                    scorePush.Operand);
                return true;
            }

            error = $"Nao achei o store + PUSHII do acumulador {variableName}.";
            return false;
        }

        static bool TryFindVariableIndexByName(AiScriptFile script, string variableName, out ushort variableIndex)
        {
            variableIndex = 0;
            AiVariable? variable = script.Variables.FirstOrDefault(candidate =>
                candidate.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase));
            if (variable == null)
                return false;
            variableIndex = (ushort)variable.Index;
            return true;
        }

        static bool TryPatchPushIiOperand(
            AiScriptFile script,
            int instructionOffset,
            ushort oldValue,
            ushort newValue,
            List<(AiInstruction Instruction, ushort OriginalOperand)> touched,
            out string error)
        {
            error = string.Empty;
            AiInstruction? instruction = script.Instructions.FirstOrDefault(c => c.Offset == instructionOffset);
            if (instruction == null)
            {
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch do Mortibody.";
                return false;
            }
            if (instruction.Opcode != PushIiOpcode || !instruction.HasOperand)
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do Mortibody.";
                return false;
            }
            if (instruction.Operand != oldValue)
            {
                error = $"Offset 0x{instructionOffset:X4} mudou desde a leitura (esperado {oldValue}, atual {instruction.Operand}).";
                return false;
            }
            touched.Add((instruction, instruction.Operand));
            instruction.Operand = newValue;
            return true;
        }

        static List<AiMortibodyAccumulatorByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiMortibodyAccumulatorByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiMortibodyAccumulatorByteChange(i, before[i], after[i]));
            }
            return changes;
        }
    }
}