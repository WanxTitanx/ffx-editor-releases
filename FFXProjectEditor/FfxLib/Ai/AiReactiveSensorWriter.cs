using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiReactiveSensorDescriptor(
        string VariableName,
        string PropertyName,
        string RoleLabel,
        int StateWriteOffset,
        ushort CurrentStateValue)
    {
        public string CurrentStateSummary => $"{VariableName} = {CurrentStateValue} @0x{StateWriteOffset:X4}";
    }

    public sealed record AiReactiveSensorPatchRequest(
        string VariableName,
        int StateWriteOffset,
        ushort OldStateValue,
        ushort NewStateValue);

    public sealed record AiReactiveSensorByteChange(int Offset, byte OldValue, byte NewValue);

    public sealed record AiReactiveSensorEditResult(
        AiReactiveSensorPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiReactiveSensorByteChange> ChangedBytes);

    public static class AiReactiveSensorWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte PopVariableOpcode = 0xA0;
        const byte CallOpcode = 0xB5;
        const byte CallPopAOpcode = 0xD8;
        const ushort UsedCommand = 0x7019;
        const ushort ReadMoveProperty = 0x701A;

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiReactiveSensorDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiReactiveSensorDescriptor>();
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar shape do reactive sensor.";
                return false;
            }

            var built = new List<AiReactiveSensorDescriptor>();

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i + 5 < instructions.Count; i++)
            {
                if (!IsCallWithOperand(instructions[i], UsedCommand))
                    continue;

                if (instructions[i + 1].Opcode != PopVariableOpcode || !instructions[i + 1].HasOperand)
                    continue;

                ushort usedCommandVar = instructions[i + 1].Operand;

                for (int j = i + 2; j + 3 < instructions.Count && j < i + 30; j++)
                {
                    if (!IsCallWithOperand(instructions[j], ReadMoveProperty))
                        continue;

                    if (instructions[j + 1].Opcode != PopVariableOpcode || !instructions[j + 1].HasOperand)
                        continue;

                    ushort propertyVar = instructions[j + 1].Operand;

                    for (int k = j + 2; k + 2 < instructions.Count && k < j + 20; k++)
                    {
                        if (instructions[k].Opcode != PushIiOpcode || !instructions[k].HasOperand)
                            continue;

                        if (instructions[k + 1].Opcode != PopVariableOpcode || !instructions[k + 1].HasOperand)
                            continue;

                        ushort stateVar = instructions[k + 1].Operand;

                        built.Add(new AiReactiveSensorDescriptor(
                            $"battleVar{stateVar:X4}",
                            $"property@{propertyVar:X4}",
                            $"Sensor reativo (usedCommand -> readMoveProperty -> state)",
                            instructions[k].Offset,
                            instructions[k].Operand));
                        break;
                    }

                    break;
                }
            }

            if (built.Count < 1)
            {
                for (int i = 1; i < instructions.Count; i++)
                {
                    if (instructions[i].Opcode != PopVariableOpcode || !instructions[i].HasOperand)
                        continue;
                    if (instructions[i - 1].Opcode != PushIiOpcode || !instructions[i - 1].HasOperand)
                        continue;

                    bool hasCallNearby = false;
                    for (int j = Math.Max(0, i - 6); j < i; j++)
                    {
                        if (IsCallWithOperand(instructions[j], UsedCommand) || IsCallWithOperand(instructions[j], ReadMoveProperty))
                        {
                            hasCallNearby = true;
                            break;
                        }
                    }
                    if (!hasCallNearby)
                        continue;

                    built.Add(new AiReactiveSensorDescriptor(
                        $"battleVar{instructions[i].Operand:X4}",
                        "loose",
                        "Sensor reativo (loose shape: CALL 0x7019/0x701A near PUSHII + POPV)",
                        instructions[i - 1].Offset,
                        instructions[i - 1].Operand));
                }
            }

            if (built.Count < 1)
            {
                error = "Did not find the proven shape 'usedCommand -> readMoveProperty -> PUSHII state' of the reactive sensor.";
                descriptors = Array.Empty<AiReactiveSensorDescriptor>();
                return false;
            }

            descriptors = built;
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiReactiveSensorPatchRequest request,
            out AiReactiveSensorEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do reactive sensor.";
                return false;
            }

            if (request == null || request.StateWriteOffset < 0)
            {
                error = "Patch do reactive sensor veio sem offset.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                if (!TryPatchPushIiOperand(script, request.StateWriteOffset, request.OldStateValue, request.NewStateValue, touched, out error))
                    return false;

                byte[] editedAi = AiScript_File.Write(script);
                List<AiReactiveSensorByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = new()
                {
                    request.StateWriteOffset + 1,
                    request.StateWriteOffset + 2,
                };
                List<AiReactiveSensorByteChange> forbidden = changed
                    .Where(c => !allowedOffsets.Contains(c.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error = "O patch raw do reactive sensor abortou: apareceram bytes fora dos PUSHII autorizados. " +
                        string.Join("; ", forbidden.Select(c => $"+0x{c.Offset:X4} {c.OldValue:X2}->{c.NewValue:X2}"));
                    return false;
                }

                result = new AiReactiveSensorEditResult(request, editedAi, changed);
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

        static bool IsCallWithOperand(AiInstruction instruction, ushort operand) =>
            (instruction.Opcode == CallOpcode || instruction.Opcode == CallPopAOpcode)
            && instruction.HasOperand && instruction.Operand == operand;

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
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch do reactive sensor.";
                return false;
            }
            if (instruction.Opcode != PushIiOpcode || !instruction.HasOperand)
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do reactive sensor.";
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

        static List<AiReactiveSensorByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiReactiveSensorByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiReactiveSensorByteChange(i, before[i], after[i]));
            }
            return changes;
        }
    }
}