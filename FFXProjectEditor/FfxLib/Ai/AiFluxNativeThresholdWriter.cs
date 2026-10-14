using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiFluxNativeThresholdDescriptor(
        string VariableName,
        string RoleLabel,
        int StoreInstructionOffset,
        int DenominatorInstructionOffset,
        ushort CurrentDenominator,
        int? NumeratorInstructionOffset,
        ushort CurrentNumerator)
    {
        public bool HasExplicitNumerator => NumeratorInstructionOffset.HasValue;

        public decimal DerivedPercent =>
            CurrentDenominator == 0
                ? 0m
                : Math.Round((CurrentNumerator * 100m) / CurrentDenominator, 2);

        public string RawThresholdSummary =>
            HasExplicitNumerator
                ? $"maxHP / {CurrentDenominator} * {CurrentNumerator}"
                : $"maxHP / {CurrentDenominator}";

        public string DerivedThresholdSummary =>
            $"{DerivedPercent:0.##}% do maxHP ({RawThresholdSummary})";
    }

    public sealed record AiFluxNativeThresholdPatchRequest(
        string VariableName,
        int DenominatorInstructionOffset,
        ushort OldDenominator,
        ushort NewDenominator,
        int? NumeratorInstructionOffset,
        ushort? OldNumerator,
        ushort? NewNumerator);

    public sealed record AiFluxNativeThresholdByteChange(
        int Offset,
        byte OldValue,
        byte NewValue);

    public sealed record AiFluxNativeThresholdEditResult(
        AiFluxNativeThresholdPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiFluxNativeThresholdByteChange> ChangedBytes);

    public static class AiFluxNativeThresholdWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte PopVariableOpcode = 0xA0;
        const byte CallOpcode = 0xB5;
        const byte FluxDivideLikeOpcode = 0x17;
        const byte FluxMultiplyLikeOpcode = 0x16;
        const ushort ReadChrPropertyFuncId = 0x700F;
        const ushort SelfOperand = 0xFFF3;
        const ushort MaxHpFieldOperand = 0x0002;

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiFluxNativeThresholdDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiFluxNativeThresholdDescriptor>();
            error = string.Empty;

            if (!TryBuildDescriptor(script, "priv0018", out AiFluxNativeThresholdDescriptor? protect, out error)
                || protect == null)
            {
                return false;
            }

            if (!TryBuildDescriptor(script, "priv001C", out AiFluxNativeThresholdDescriptor? reflect, out error)
                || reflect == null)
            {
                return false;
            }

            descriptors = new[] { protect, reflect };
            return true;
        }

        public static bool TryBuildDescriptor(
            AiScriptFile script,
            string variableName,
            out AiFluxNativeThresholdDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar threshold nativo do Flux.";
                return false;
            }

            if (!TryFindVariableIndexByName(script, variableName, out ushort variableIndex))
            {
                error = $"A var {variableName} nao existe neste script.";
                return false;
            }

            int storeIndex = FindFirstStoreIndex(script.Instructions, variableIndex);
            if (storeIndex < 0)
            {
                error = $"Nao achei o store inicial de {variableName} no pacote nativo do Flux.";
                return false;
            }

            if (variableName.Equals("priv0018", StringComparison.OrdinalIgnoreCase))
            {
                return TryBuildProtectDescriptor(script.Instructions, storeIndex, out descriptor, out error);
            }

            if (variableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase))
            {
                return TryBuildReflectDescriptor(script.Instructions, storeIndex, out descriptor, out error);
            }

            error = $"A writer narrow do Flux ainda nao cobre {variableName}.";
            return false;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiFluxNativeThresholdPatchRequest request,
            out AiFluxNativeThresholdEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do threshold nativo do Flux.";
                return false;
            }

            if (!TryBuildDescriptor(script, request.VariableName, out AiFluxNativeThresholdDescriptor? live, out error)
                || live == null)
            {
                return false;
            }

            if (live.DenominatorInstructionOffset != request.DenominatorInstructionOffset)
            {
                error =
                    $"O offset do divisor de {request.VariableName} mudou desde a leitura " +
                    $"(esperado 0x{request.DenominatorInstructionOffset:X4}, atual 0x{live.DenominatorInstructionOffset:X4}).";
                return false;
            }

            if (live.CurrentDenominator != request.OldDenominator)
            {
                error =
                    $"O divisor de {request.VariableName} mudou desde a leitura " +
                    $"(esperado {request.OldDenominator}, atual {live.CurrentDenominator}).";
                return false;
            }

            if (live.NumeratorInstructionOffset != request.NumeratorInstructionOffset)
            {
                error =
                    $"O shape do numerador de {request.VariableName} mudou desde a leitura. " +
                    "Reabra o card do Flux antes de aplicar.";
                return false;
            }

            if (live.HasExplicitNumerator)
            {
                if (!request.OldNumerator.HasValue || !request.NewNumerator.HasValue)
                {
                    error = $"O patch de {request.VariableName} exige numerador explicito.";
                    return false;
                }

                if (live.CurrentNumerator != request.OldNumerator.Value)
                {
                    error =
                        $"O numerador de {request.VariableName} mudou desde a leitura " +
                        $"(esperado {request.OldNumerator.Value}, atual {live.CurrentNumerator}).";
                    return false;
                }
            }
            else if (request.NewNumerator.HasValue && request.NewNumerator.Value != 1)
            {
                error =
                    $"{request.VariableName} hoje e um chain narrow 'maxHP / divisor'. " +
                    "Nao existe numerador extra neste shape sem grow estrutural.";
                return false;
            }

            if (request.NewDenominator == 0)
            {
                error = "The divisor of the native Flux threshold must be greater than zero.";
                return false;
            }

            ushort targetNumerator = request.NewNumerator ?? live.CurrentNumerator;
            if (targetNumerator == 0)
            {
                error = "The numerator of the native Flux threshold must be greater than zero.";
                return false;
            }

            if (targetNumerator > request.NewDenominator)
            {
                error =
                    "O writer narrow do Flux so libera thresholds ate 100% do maxHP " +
                    "(numerador nao pode passar do divisor).";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                if (!TryPatchImmediateOperand(
                        script,
                        request.DenominatorInstructionOffset,
                        request.OldDenominator,
                        request.NewDenominator,
                        touched,
                        out error))
                {
                    return false;
                }

                if (live.HasExplicitNumerator
                    && request.NumeratorInstructionOffset.HasValue
                    && request.OldNumerator.HasValue
                    && request.NewNumerator.HasValue
                    && !TryPatchImmediateOperand(
                        script,
                        request.NumeratorInstructionOffset.Value,
                        request.OldNumerator.Value,
                        request.NewNumerator.Value,
                        touched,
                        out error))
                {
                    return false;
                }

                byte[] editedAi = AiScript_File.Write(script);
                List<AiFluxNativeThresholdByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = BuildAllowedOffsets(request);
                List<AiFluxNativeThresholdByteChange> forbidden = changed
                    .Where(change => !allowedOffsets.Contains(change.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error =
                        "O patch raw do Flux abortou: apareceram bytes fora dos immediates autorizados. " +
                        string.Join("; ", forbidden.Select(change =>
                            $"+0x{change.Offset:X4} {change.OldValue:X2}->{change.NewValue:X2}"));
                    return false;
                }

                result = new AiFluxNativeThresholdEditResult(request, editedAi, changed);
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

        static bool TryBuildProtectDescriptor(
            IReadOnlyList<AiInstruction> instructions,
            int storeIndex,
            out AiFluxNativeThresholdDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (storeIndex < 7)
            {
                error = "The raw chain of priv0018 was too short for the Flux narrow writer.";
                return false;
            }

            AiInstruction self = instructions[storeIndex - 7];
            AiInstruction field = instructions[storeIndex - 6];
            AiInstruction call = instructions[storeIndex - 5];
            AiInstruction denominator = instructions[storeIndex - 4];
            AiInstruction divider = instructions[storeIndex - 3];
            AiInstruction numerator = instructions[storeIndex - 2];
            AiInstruction multiplier = instructions[storeIndex - 1];
            AiInstruction store = instructions[storeIndex];

            if (!MatchesMaxHpRead(self, field, call)
                || !IsImmediate(denominator)
                || divider.Opcode != FluxDivideLikeOpcode
                || !IsImmediate(numerator)
                || multiplier.Opcode != FluxMultiplyLikeOpcode
                || !IsStore(store))
            {
                error =
                    "priv0018 does not match the proven shape 'maxHP / divisor * numerator' of the Flux narrow writer.";
                return false;
            }

            descriptor = new AiFluxNativeThresholdDescriptor(
                "priv0018",
                "Gate nativo do Protect",
                store.Offset,
                denominator.Offset,
                denominator.Operand,
                numerator.Offset,
                numerator.Operand);
            return true;
        }

        static bool TryBuildReflectDescriptor(
            IReadOnlyList<AiInstruction> instructions,
            int storeIndex,
            out AiFluxNativeThresholdDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (storeIndex < 5)
            {
                error = "The raw chain of priv001C was too short for the Flux narrow writer.";
                return false;
            }

            AiInstruction self = instructions[storeIndex - 5];
            AiInstruction field = instructions[storeIndex - 4];
            AiInstruction call = instructions[storeIndex - 3];
            AiInstruction denominator = instructions[storeIndex - 2];
            AiInstruction divider = instructions[storeIndex - 1];
            AiInstruction store = instructions[storeIndex];

            if (!MatchesMaxHpRead(self, field, call)
                || !IsImmediate(denominator)
                || divider.Opcode != FluxDivideLikeOpcode
                || !IsStore(store))
            {
                error =
                    "priv001C does not match the proven shape 'maxHP / divisor' of the Flux narrow writer.";
                return false;
            }

            descriptor = new AiFluxNativeThresholdDescriptor(
                "priv001C",
                "Gate nativo do Reflect",
                store.Offset,
                denominator.Offset,
                denominator.Operand,
                null,
                1);
            return true;
        }

        static bool MatchesMaxHpRead(AiInstruction self, AiInstruction field, AiInstruction call) =>
            IsImmediate(self, SelfOperand)
            && IsImmediate(field, MaxHpFieldOperand)
            && call.Opcode == CallOpcode
            && call.HasOperand
            && call.Operand == ReadChrPropertyFuncId;

        static bool IsImmediate(AiInstruction instruction, ushort? expectedValue = null) =>
            instruction.Opcode == PushIiOpcode
            && instruction.HasOperand
            && (!expectedValue.HasValue || instruction.Operand == expectedValue.Value);

        static bool IsStore(AiInstruction instruction) =>
            instruction.Opcode == PopVariableOpcode && instruction.HasOperand;

        static bool TryPatchImmediateOperand(
            AiScriptFile script,
            int instructionOffset,
            ushort oldValue,
            ushort newValue,
            List<(AiInstruction Instruction, ushort OriginalOperand)> touched,
            out string error)
        {
            error = string.Empty;

            AiInstruction? instruction = script.Instructions.FirstOrDefault(candidate => candidate.Offset == instructionOffset);
            if (instruction == null)
            {
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch raw do Flux.";
                return false;
            }

            if (!IsImmediate(instruction))
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do Flux.";
                return false;
            }

            if (instruction.Operand != oldValue)
            {
                error =
                    $"Offset 0x{instructionOffset:X4} mudou desde a leitura " +
                    $"(esperado {oldValue}, atual {instruction.Operand}).";
                return false;
            }

            touched.Add((instruction, instruction.Operand));
            instruction.Operand = newValue;
            return true;
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

        static int FindFirstStoreIndex(IReadOnlyList<AiInstruction> instructions, ushort variableIndex)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode == PopVariableOpcode
                    && instruction.HasOperand
                    && instruction.Operand == variableIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        static HashSet<int> BuildAllowedOffsets(AiFluxNativeThresholdPatchRequest request)
        {
            var offsets = new HashSet<int>
            {
                request.DenominatorInstructionOffset + 1,
                request.DenominatorInstructionOffset + 2,
            };

            if (request.NumeratorInstructionOffset.HasValue)
            {
                offsets.Add(request.NumeratorInstructionOffset.Value + 1);
                offsets.Add(request.NumeratorInstructionOffset.Value + 2);
            }

            return offsets;
        }

        static List<AiFluxNativeThresholdByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiFluxNativeThresholdByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiFluxNativeThresholdByteChange(i, before[i], after[i]));
            }

            return changes;
        }
    }
}
