using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1: slots editaveis + records de edicao extraidos do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
static AiIndirectDispatchEditableTargetSlot BuildPreviewTargetSlot(
            int unitIndex,
            int slotIndex,
            string slotLabel,
            string valueSummary,
            string detailSummary,
            int offset,
            AiIndirectDispatchTargetSlotSourceKind sourceKind,
            ushort? forcedVariableIndex = null,
            string? forcedVariableName = null) =>
            new(
                $"preview.target.{unitIndex}.{slotIndex}",
                slotLabel,
                forcedVariableIndex ?? PreviewTargetVariableIndex(unitIndex, slotIndex),
                forcedVariableName ?? $"preview.target{slotIndex + 1}",
                offset,
                sourceKind,
                0,
                valueSummary,
                detailSummary,
                false,
                null,
                Array.Empty<AiIndirectDispatchEditableOperand>(),
                Array.Empty<AiIndirectDispatchVariableReference>());

        static AiIndirectDispatchTargetSlotSourceKind InferPreviewTargetKind(AiDetectedBranchAction action)
        {
            if (action.TargetSummary.Contains("calculado", StringComparison.OrdinalIgnoreCase)
                || action.TargetSummary.Contains("LastAttacker", StringComparison.OrdinalIgnoreCase)
                || action.TargetSummary.Contains("random", StringComparison.OrdinalIgnoreCase)
                || action.TargetProvenance.Contains("findMatchingChr", StringComparison.OrdinalIgnoreCase))
            {
                return AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe;
            }

            if (!string.IsNullOrWhiteSpace(action.TargetSummary))
                return AiIndirectDispatchTargetSlotSourceKind.Literal;

            return AiIndirectDispatchTargetSlotSourceKind.Unknown;
        }

        static string BuildPreviewTargetDetail(AiDetectedBranchAction action)
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(action.TargetProvenance))
                parts.Add(string.Format(Strings.U_Ai_EditableSlotsProvenance, action.TargetProvenance));
            if (!string.IsNullOrWhiteSpace(action.Confidence))
                parts.Add(string.Format(Strings.U_Ai_EditableSlotsReading, action.Confidence));
            if (action.HighLevelHints.Count > 0)
                parts.Add(string.Format(Strings.U_Ai_EditableSlotsHints, string.Join(" / ", action.HighLevelHints)));
            return parts.Count == 0 ? Strings.U_Ai_EditableSlotsNoTarget : string.Join(" ", parts);
        }

        static IReadOnlyList<string> DescribeAftermathNotes(AiScriptFile script, IEnumerable<int> callOffsets)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var notes = new List<string>();
            foreach (int callOffset in callOffsets.Distinct())
            {
                int index = instructions
                    .Select((instruction, idx) => (instruction, idx))
                    .FirstOrDefault(pair => pair.instruction.Offset == callOffset)
                    .idx;
                if (index <= 0)
                    continue;

                for (int i = index + 1; i + 1 < instructions.Count && i <= index + 10; i++)
                {
                    if (instructions[i].Opcode == PUSHII
                        && instructions[i + 1].Opcode == 0xA0
                        && instructions[i + 1].Operand < script.Variables.Count)
                    {
                        notes.Add($"{VarName(script, instructions[i + 1].Operand)} <- {instructions[i].Operand}");
                    }

                    if (i + 3 < instructions.Count
                        && instructions[i].Opcode == PUSHV
                        && instructions[i + 1].Opcode == PUSHII
                        && instructions[i + 2].Opcode == 0x03
                        && instructions[i + 3].Opcode == 0xA0
                        && instructions[i + 3].Operand == instructions[i].Operand)
                    {
                        notes.Add($"{VarName(script, instructions[i].Operand)} |= 0x{instructions[i + 1].Operand:X4}");
                    }

                    if (i + 3 < instructions.Count
                        && instructions[i].Opcode == PUSHII
                        && instructions[i + 1].Opcode == PUSHII
                        && instructions[i + 2].Opcode == PUSHII
                        && instructions[i + 3].Opcode == CALLPOPA
                        && instructions[i + 3].Operand == WriteChrProperty)
                    {
                        string fieldName = AiChrPropertyNames.Get(instructions[i + 1].Operand) ?? $"field 0x{instructions[i + 1].Operand:X4}";
                        notes.Add($"{fieldName} <- {instructions[i + 2].Operand}");
                    }

                    if (instructions[i].Opcode == CALLPOPA && instructions[i].Operand == RunBtlSceneA)
                        notes.Add("runBtlSceneA");
                    if (instructions[i].Opcode == CALLPOPA && instructions[i].Operand == RunBtlSceneB)
                        notes.Add("runBtlSceneB");
                }
            }

            return notes
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static IReadOnlyList<string> DescribeReactiveAftermath(
            IEnumerable<ReactiveSensorPattern> patterns,
            AiScriptFile script)
        {
            var notes = new List<string>();
            foreach (ReactiveSensorPattern pattern in patterns)
            {
                if (pattern.SceneStateValue.HasValue && pattern.SceneStateVarIndex != 0xFFFF)
                    notes.Add($"{VarName(script, pattern.SceneStateVarIndex)} <- {pattern.SceneStateValue.Value}");
                if (!pattern.UsesSceneStateOnly)
                    notes.Add($"{VarName(script, pattern.StateVarIndex)} |= 0x{pattern.OrLiteral:X4}");
            }

            return notes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string BuildOffsetSummary(IEnumerable<int> offsets)
        {
            List<int> materialized = offsets
                .Where(offset => offset >= 0)
                .Distinct()
                .OrderBy(offset => offset)
                .ToList();
            if (materialized.Count == 0)
                return "offset nao isolado";
            if (materialized.Count == 1)
                return $"0x{materialized[0]:X4}";
            return $"0x{materialized[0]:X4}..0x{materialized[^1]:X4}";
        }

        static ushort PreviewNamedVariableIndex(string variableName)
        {
            int hash = Math.Abs(variableName.GetHashCode(StringComparison.OrdinalIgnoreCase));
            return (ushort)(0xB000 + (hash % 0x0FFF));
        }

        static ushort PreviewCommandVariableIndex(int unitIndex, int slotIndex) =>
            PreviewPseudoVariableIndex(0xD000, unitIndex, slotIndex);

        static ushort PreviewTargetVariableIndex(int unitIndex, int slotIndex) =>
            PreviewPseudoVariableIndex(0xE000, unitIndex, slotIndex);

        static ushort PreviewPseudoVariableIndex(int baseValue, int unitIndex, int slotIndex) =>
            (ushort)(baseValue + ((unitIndex & 0x3F) * 16) + (slotIndex & 0x0F));

        static void AddIndirectDispatchConsumer(
            List<AiIndirectDispatchConsumer> consumers,
            AiScriptFile script,
            string label,
            ushort commandVarIndex,
            ushort targetVarIndex,
            int callOffset)
        {
            consumers.Add(new AiIndirectDispatchConsumer(
                label,
                commandVarIndex,
                VarName(script, commandVarIndex),
                targetVarIndex,
                VarName(script, targetVarIndex),
                callOffset));
        }

        static List<GenericIndirectDispatchConsumer> FindGenericIndirectDispatchConsumers(AiScriptFile script)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var consumers = new List<GenericIndirectDispatchConsumer>();
            for (int i = 2; i < instructions.Count; i++)
            {
                AiInstruction call = instructions[i];
                if (call.Opcode != CALLPOPA || (call.Operand != PerformCommand && call.Operand != ForcePerformCommand))
                    continue;

                if (instructions[i - 2].Opcode != PUSHV || instructions[i - 1].Opcode != PUSHV)
                    continue;

                consumers.Add(new GenericIndirectDispatchConsumer(
                    instructions[i - 1].Operand,
                    instructions[i - 2].Operand,
                    call.Offset,
                    call.Operand == ForcePerformCommand));
            }

            return consumers
                .Distinct()
                .OrderBy(consumer => consumer.CallOffset)
                .ToList();
        }

        static List<GenericIndirectDispatchRouteCluster> FindGenericRouteClusters(
            AiScriptFile script,
            IReadOnlyList<ushort> commandVars,
            IReadOnlyList<ushort> targetVars)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            HashSet<ushort> commandSet = commandVars.ToHashSet();
            HashSet<ushort> targetSet = targetVars.ToHashSet();
            var clusters = new List<GenericIndirectDispatchRouteCluster>();

            for (int i = 0; i + (commandVars.Count * 2) + 1 < instructions.Count; i++)
            {
                int cursor = i;
                var writes = new List<GenericIndirectDispatchCommandWrite>();
                var seenCommandVars = new HashSet<ushort>();

                while (cursor + 1 < instructions.Count
                       && instructions[cursor].Opcode == PUSHII
                       && instructions[cursor + 1].Opcode == 0xA0
                       && commandSet.Contains(instructions[cursor + 1].Operand)
                       && seenCommandVars.Add(instructions[cursor + 1].Operand)
                       && AiCommandId.IsCommandOperand(instructions[cursor].Operand))
                {
                    writes.Add(new GenericIndirectDispatchCommandWrite(
                        instructions[cursor + 1].Operand,
                        instructions[cursor].Operand,
                        instructions[cursor].Offset));
                    cursor += 2;
                }

                if (writes.Count != commandVars.Count || cursor + 1 >= instructions.Count)
                    continue;

                if (instructions[cursor].Opcode != PUSHII || instructions[cursor + 1].Opcode != 0xA0)
                    continue;

                ushort nextStateVar = instructions[cursor + 1].Operand;
                if (commandSet.Contains(nextStateVar) || targetSet.Contains(nextStateVar))
                    continue;

                clusters.Add(new GenericIndirectDispatchRouteCluster(
                    instructions[i].Offset,
                    instructions[cursor + 1].Offset,
                    writes,
                    nextStateVar,
                    instructions[cursor].Operand,
                    instructions[cursor].Offset));
            }

            return clusters;
        }

        static List<SupportIndirectPayloadPickerCluster> FindSupportIndirectPayloadPickerClusters(
            AiScriptFile script,
            ushort commandVar,
            ushort targetVar,
            IReadOnlyList<GenericIndirectDispatchConsumer> consumers)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var clusters = new List<SupportIndirectPayloadPickerCluster>();
            var seenRanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GenericIndirectDispatchConsumer consumer in consumers.OrderBy(candidate => candidate.CallOffset))
            {
                int callIndex = FindInstructionIndexByOffset(instructions, consumer.CallOffset);
                if (callIndex < 0)
                    continue;

                int previousRetIndex = FindPreviousOpcodeIndex(instructions, callIndex - 1, 0x3C);
                List<GenericIndirectDispatchCommandWrite> trailingWrites = CollectSupportPayloadWrites(
                    instructions,
                    previousRetIndex >= 0 ? previousRetIndex + 1 : Math.Max(0, callIndex - 48),
                    callIndex - 1,
                    commandVar);

                int bridgeRetIndex = previousRetIndex;
                int bridgeStartIndex = bridgeRetIndex >= 0
                    ? FindPreviousOpcodeIndex(instructions, bridgeRetIndex - 1, 0x3C) + 1
                    : Math.Max(0, callIndex - 64);
                List<GenericIndirectDispatchCommandWrite> bridgeWrites = bridgeRetIndex >= 0
                    ? CollectSupportPayloadWrites(instructions, bridgeStartIndex, bridgeRetIndex - 1, commandVar)
                    : new List<GenericIndirectDispatchCommandWrite>();

                List<GenericIndirectDispatchCommandWrite> chosenWrites = bridgeWrites.Count >= trailingWrites.Count
                    ? bridgeWrites
                    : trailingWrites;
                if (chosenWrites.Count < 2)
                    continue;
                if (chosenWrites.Select(write => write.CommandOperand).Distinct().Count() < 2)
                    continue;

                int rangeStartIndex = bridgeWrites.Count >= trailingWrites.Count
                    ? bridgeStartIndex
                    : (previousRetIndex >= 0 ? previousRetIndex + 1 : Math.Max(0, callIndex - 48));
                int rangeEndIndex = bridgeWrites.Count >= trailingWrites.Count
                    ? bridgeRetIndex - 1
                    : callIndex - 1;
                if (rangeEndIndex < rangeStartIndex)
                    continue;

                int startOffset = chosenWrites.Min(write => write.Offset);
                int endOffset = chosenWrites.Max(write => write.Offset);
                string dedupeKey = $"{startOffset:X4}-{endOffset:X4}-{commandVar:X4}-{targetVar:X4}";
                if (!seenRanges.Add(dedupeKey))
                    continue;

                bool usesSwitch = UsesSwitchLikeOpcode(instructions, rangeStartIndex, rangeEndIndex);
                bool isRandomized = HasCallInRange(instructions, rangeStartIndex, rangeEndIndex, GetRandomValue);
                string guardSummary = BuildSupportPayloadPickerGuardSummary(script, commandVar, targetVar, usesSwitch, isRandomized);

                clusters.Add(new SupportIndirectPayloadPickerCluster(
                    startOffset,
                    endOffset,
                    chosenWrites,
                    commandVar,
                    targetVar,
                    consumer.CallOffset,
                    consumer.ForcePerform,
                    guardSummary,
                    usesSwitch,
                    isRandomized));
            }

            return clusters
                .OrderBy(cluster => cluster.StartOffset)
                .ToList();
        }

        static List<GenericIndirectDispatchCommandWrite> CollectSupportPayloadWrites(
            IReadOnlyList<AiInstruction> instructions,
            int startIndex,
            int endIndex,
            ushort commandVar)
        {
            var writes = new List<GenericIndirectDispatchCommandWrite>();
            if (startIndex < 0 || endIndex <= startIndex)
                return writes;

            for (int i = Math.Max(1, startIndex); i <= endIndex && i < instructions.Count; i++)
            {
                if (instructions[i - 1].Opcode == PUSHII
                    && instructions[i].Opcode == 0xA0
                    && instructions[i].Operand == commandVar
                    && AiCommandId.IsCommandOperand(instructions[i - 1].Operand))
                {
                    writes.Add(new GenericIndirectDispatchCommandWrite(
                        commandVar,
                        instructions[i - 1].Operand,
                        instructions[i - 1].Offset));
                }
            }

            return writes
                .Distinct()
                .ToList();
        }

        static int FindPreviousOpcodeIndex(IReadOnlyList<AiInstruction> instructions, int startIndexInclusive, byte opcode)
        {
            for (int i = Math.Min(startIndexInclusive, instructions.Count - 1); i >= 0; i--)
            {
                if (instructions[i].Opcode == opcode)
                    return i;
            }

            return -1;
        }

        static bool UsesSwitchLikeOpcode(IReadOnlyList<AiInstruction> instructions, int startIndex, int endIndex)
        {
            for (int i = Math.Max(0, startIndex); i <= endIndex && i < instructions.Count; i++)
            {
                string mnemonic = AiScript_File.Mnemonic(instructions[i].Opcode);
                if (string.Equals(mnemonic, "SWITCH", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mnemonic, "CASE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasCallInRange(IReadOnlyList<AiInstruction> instructions, int startIndex, int endIndex, ushort operand)
        {
            for (int i = Math.Max(0, startIndex); i <= endIndex && i < instructions.Count; i++)
            {
                if ((instructions[i].Opcode == CALL || instructions[i].Opcode == CALLPOPA)
                    && instructions[i].Operand == operand)
                {
                    return true;
                }
            }

            return false;
        }

        static string BuildSupportPayloadPickerGuardSummary(
            AiScriptFile script,
            ushort commandVar,
            ushort targetVar,
            bool usesSwitch,
            bool isRandomized)
        {
            string pickerSummary = isRandomized
                ? "picker local aleatorio"
                : usesSwitch
                    ? "picker local por switch"
                    : "picker local";

            return $"{pickerSummary} escreve {VarName(script, commandVar)} antes de calcular {VarName(script, targetVar)}.";
        }

        static bool HasDirectSwitchOnVariable(AiScriptFile script, ushort variableIndex)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i + 1 < instructions.Count; i++)
            {
                if (instructions[i].Opcode == PUSHV
                    && instructions[i].Operand == variableIndex
                    && string.Equals(AiScript_File.Mnemonic(instructions[i + 1].Opcode), "SWITCH", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static string DescribeGenericCommandSlotLabel(ushort variableIndex, int slotIndex)
        {
            return variableIndex switch
            {
                _ when slotIndex == 0 => "slot cmd #1",
                _ when slotIndex == 1 => "slot cmd #2",
                _ when slotIndex == 2 => "slot cmd #3",
                _ when slotIndex == 3 => "slot cmd #4",
                _ => $"slot cmd #{slotIndex + 1}",
            };
        }

        static string DescribeGenericTargetSlotLabel(ushort variableIndex, int slotIndex)
        {
            return variableIndex switch
            {
                _ when slotIndex == 0 => "slot alvo #1",
                _ when slotIndex == 1 => "slot alvo #2",
                _ when slotIndex == 2 => "slot alvo #3",
                _ when slotIndex == 3 => "slot alvo #4",
                _ => $"slot alvo #{slotIndex + 1}",
            };
        }

        static string DescribeGenericConsumerLabel(int slotIndex, bool forcePerform) =>
            $"{(forcePerform ? "dispatch imediato" : "dispatch em fila")} #{slotIndex + 1}";

        static List<AiIndirectDispatchEditableTargetSlot> BuildEditableTargetSlots(
            AiScriptFile script,
            params (ushort VariableIndex, string RoleKey, string SlotLabel)[] targetSlots)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var rows = new List<AiIndirectDispatchEditableTargetSlot>();
            List<AiIndirectDispatchVariableReference> allTargetCandidates = targetSlots
                .Select(slot => new AiIndirectDispatchVariableReference(slot.VariableIndex, VarName(script, slot.VariableIndex)))
                .Distinct()
                .ToList();
            List<AiIndirectDispatchVariableReference> allScriptVariables = script.Variables
                .Select(variable => new AiIndirectDispatchVariableReference((ushort)variable.Index, variable.Name))
                .Distinct()
                .ToList();

            foreach ((ushort variableIndex, string roleKey, string slotLabel) in targetSlots)
            {
                string variableName = VarName(script, variableIndex);
                bool foundAny = false;

                for (int i = 1; i < instructions.Count; i++)
                {
                    AiInstruction store = instructions[i];
                    if (store.Opcode != 0xA0 || store.Operand != variableIndex)
                        continue;

                    foundAny = true;
                    AiInstruction source = instructions[i - 1];
                    string derivedRoleKey = $"{roleKey}.{rows.Count(r => r.VariableIndex == variableIndex)}";

                    if (source.Opcode == PUSHII)
                    {
                        rows.Add(new AiIndirectDispatchEditableTargetSlot(
                            derivedRoleKey,
                            slotLabel,
                            variableIndex,
                            variableName,
                            source.Offset,
                            AiIndirectDispatchTargetSlotSourceKind.Literal,
                            source.Operand,
                            DescribeTargetOperand(source.Operand),
                            "literal target saved in the slot before the indirect performCommand.",
                            true,
                            AiTargetRecipeKind.Literal,
                            new[]
                            {
                                new AiIndirectDispatchEditableOperand(
                                    $"{derivedRoleKey}.literal",
                                    slotLabel,
                                    source.Offset,
                                    source.Opcode,
                                    source.Operand,
                                    DescribeTargetOperand(source.Operand)),
                            },
                            Array.Empty<AiIndirectDispatchVariableReference>()));
                        continue;
                    }

                    if ((source.Opcode == CALL || source.Opcode == CALLPOPA) && source.Operand == FindMatchingChr)
                    {
                        if (TryBuildEditableFindMatchingRecipe(script, i - 1, derivedRoleKey, slotLabel, out AiTargetRecipeKind recipeKind, out List<AiIndirectDispatchEditableOperand> recipeOperands))
                        {
                            rows.Add(new AiIndirectDispatchEditableTargetSlot(
                                derivedRoleKey,
                                slotLabel,
                                variableIndex,
                                variableName,
                                source.Offset,
                                AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe,
                                0,
                                DescribeFindMatchingRecipe(instructions, i - 1),
                                "recipe calculated in the script; this V2 can already swap recognized recipes without rewriting the entire topology of findMatchingChr.",
                                true,
                                recipeKind,
                                recipeOperands,
                                Array.Empty<AiIndirectDispatchVariableReference>()));
                            continue;
                        }

                        rows.Add(new AiIndirectDispatchEditableTargetSlot(
                            derivedRoleKey,
                            slotLabel,
                            variableIndex,
                            variableName,
                            source.Offset,
                            AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe,
                            0,
                            DescribeFindMatchingRecipe(instructions, i - 1),
                            "recipe calculated in the script; this V2 does not yet rewrite the complete sequence of findMatchingChr.",
                            false,
                            null,
                            Array.Empty<AiIndirectDispatchEditableOperand>(),
                            Array.Empty<AiIndirectDispatchVariableReference>()));
                        continue;
                    }

                    if (source.Opcode == PUSHV)
                    {
                        List<AiIndirectDispatchVariableReference> copyCandidates = allTargetCandidates
                            .Concat(allScriptVariables)
                            .Concat(new[] { new AiIndirectDispatchVariableReference(source.Operand, VarName(script, source.Operand)) })
                            .Distinct()
                            .ToList();

                        rows.Add(new AiIndirectDispatchEditableTargetSlot(
                            derivedRoleKey,
                            slotLabel,
                            variableIndex,
                            variableName,
                            source.Offset,
                            AiIndirectDispatchTargetSlotSourceKind.CopiedValue,
                            source.Operand,
                            $"copia de {VarName(script, source.Operand)}",
                            "the slot reuses another target/var value; this V2 can already swap which var feeds this copy.",
                            true,
                            null,
                            new[]
                            {
                                new AiIndirectDispatchEditableOperand(
                                    $"{derivedRoleKey}.copy-source",
                                    $"{slotLabel} · origem da copia",
                                    source.Offset,
                                    source.Opcode,
                                    source.Operand,
                                    VarName(script, source.Operand)),
                            },
                            copyCandidates));
                        continue;
                    }

                    rows.Add(new AiIndirectDispatchEditableTargetSlot(
                        derivedRoleKey,
                        slotLabel,
                        variableIndex,
                        variableName,
                        source.Offset,
                        AiIndirectDispatchTargetSlotSourceKind.Unknown,
                        0,
                        $"{AiScript_File.Mnemonic(source.Opcode)} 0x{source.Operand:X4}",
                        "target source not yet classified for writer row-only.",
                        false,
                        null,
                        Array.Empty<AiIndirectDispatchEditableOperand>(),
                        Array.Empty<AiIndirectDispatchVariableReference>()));
                }

                if (!foundAny)
                {
                    rows.Add(new AiIndirectDispatchEditableTargetSlot(
                        $"{roleKey}.missing",
                        slotLabel,
                        variableIndex,
                        variableName,
                        -1,
                        AiIndirectDispatchTargetSlotSourceKind.Unknown,
                        0,
                        "sem write legivel",
                        "no clear POPV for this slot appeared in the current reading.",
                        false,
                        null,
                        Array.Empty<AiIndirectDispatchEditableOperand>(),
                        Array.Empty<AiIndirectDispatchVariableReference>()));
                }
            }

            return rows;
        }

        static bool TryBuildEditableFindMatchingRecipe(
            AiScriptFile script,
            int callIndex,
            string roleKey,
            string slotLabel,
            out AiTargetRecipeKind recipeKind,
            out List<AiIndirectDispatchEditableOperand> operands)
        {
            operands = new List<AiIndirectDispatchEditableOperand>();
            recipeKind = AiTargetRecipeKind.Literal;

            IReadOnlyList<AiInstruction> ins = script.Instructions;
            if (callIndex < 4
                || ins[callIndex].Operand != FindMatchingChr
                || ins[callIndex - 1].Opcode != PUSHII
                || ins[callIndex - 2].Opcode != PUSHII
                || ins[callIndex - 3].Opcode != PUSHII
                || ins[callIndex - 4].Opcode != PUSHII)
            {
                return false;
            }

            ushort group = ins[callIndex - 4].Operand;
            ushort property = ins[callIndex - 3].Operand;
            ushort compareValue = ins[callIndex - 2].Operand;
            ushort selector = ins[callIndex - 1].Operand;

            if (group == FrontlineChars && property == ChrFieldIsAlive && compareValue == 0 && selector == SelectorAny)
                recipeKind = AiTargetRecipeKind.FindAliveFrontlineAny;
            else if (group == MatchingGroup && property == ChrFieldHp && compareValue == 0 && selector == SelectorLowest)
                recipeKind = AiTargetRecipeKind.FindAliveFrontlineLowestHp;
            else
                return false;

            operands.Add(new AiIndirectDispatchEditableOperand(
                $"{roleKey}.recipe.group",
                $"{slotLabel} · grupo",
                ins[callIndex - 4].Offset,
                ins[callIndex - 4].Opcode,
                group,
                DescribeTargetOperand(group)));
            operands.Add(new AiIndirectDispatchEditableOperand(
                $"{roleKey}.recipe.property",
                $"{slotLabel} · propriedade",
                ins[callIndex - 3].Offset,
                ins[callIndex - 3].Opcode,
                property,
                $"property 0x{property:X4}"));
            operands.Add(new AiIndirectDispatchEditableOperand(
                $"{roleKey}.recipe.value",
                $"{slotLabel} · comparador",
                ins[callIndex - 2].Offset,
                ins[callIndex - 2].Opcode,
                compareValue,
                compareValue.ToString()));
            operands.Add(new AiIndirectDispatchEditableOperand(
                $"{roleKey}.recipe.selector",
                $"{slotLabel} · seletor",
                ins[callIndex - 1].Offset,
                ins[callIndex - 1].Opcode,
                selector,
                selector.ToString()));
            return true;
        }

        public static IReadOnlyList<AiIndirectDispatchTargetSlotSurface> BuildTargetSlotSurface(
            IReadOnlyList<AiIndirectDispatchEditableTargetSlot> targetSlots,
            IReadOnlyList<AiIndirectDispatchConsumer> consumers)
        {
            if (targetSlots == null || targetSlots.Count == 0)
                return Array.Empty<AiIndirectDispatchTargetSlotSurface>();

            var surfaces = new List<AiIndirectDispatchTargetSlotSurface>();
            foreach (IGrouping<ushort, AiIndirectDispatchEditableTargetSlot> group in targetSlots
                         .GroupBy(slot => slot.VariableIndex)
                         .OrderBy(group => group.Min(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)))
            {
                List<AiIndirectDispatchEditableTargetSlot> writes = group
                    .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                    .ToList();
                AiIndirectDispatchEditableTargetSlot primary = PickPrimaryTargetSlotForSurface(writes);
                List<string> consumerLabels = consumers
                    .Where(consumer => consumer.TargetVariableIndex == group.Key)
                    .Select(consumer => consumer.Label)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                surfaces.Add(new AiIndirectDispatchTargetSlotSurface(
                    string.IsNullOrWhiteSpace(primary.SlotLabel) ? primary.VariableName : primary.SlotLabel,
                    primary.VariableIndex,
                    primary.VariableName,
                    primary.SourceKind,
                    BuildTargetSurfaceValueSummary(writes, primary),
                    BuildTargetSurfaceDetailSummary(writes, primary, consumerLabels),
                    primary.SourceInstructionOffset,
                    writes.Any(slot => slot.CanEdit),
                    writes.Count));
            }

            return surfaces;
        }

        public static AiIndirectDispatchOpeningFocus? PickIndirectDispatchOpeningSelection(
            AiScriptFile script,
            IReadOnlyList<AiIndirectDispatchUnit> units)
        {
            if (script == null || !script.HasScript || units == null || units.Count == 0)
                return null;

            AiIndirectDispatchOpeningFocus? best = null;
            for (ushort variableIndex = 0; variableIndex < script.Variables.Count; variableIndex++)
            {
                string variableName = VarName(script, variableIndex);
                AiIndirectDispatchOpeningFocus? candidate =
                    DescribeIndirectDispatchOpeningFocus(units, variableName, variableIndex);
                if (candidate == null)
                    continue;

                if (best == null || CompareIndirectDispatchOpeningFocus(candidate, best) > 0)
                    best = candidate;
            }

            return best;
        }

        public static AiIndirectDispatchOpeningFocus? DescribeIndirectDispatchOpeningFocus(
            IReadOnlyList<AiIndirectDispatchUnit> units,
            string variableName,
            ushort variableIndex)
        {
            if (units == null || units.Count == 0 || string.IsNullOrWhiteSpace(variableName))
                return null;

            var reads = new List<IndirectDispatchStructuralRead>();
            HashSet<string> linkedUnitIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (AiIndirectDispatchUnit unit in units.OrderBy(candidate => candidate.UnitIndex))
            {
                if (TextMentionsIndirectDispatchVariable(unit.GuardSummary, variableName, variableIndex))
                {
                    reads.Add(new IndirectDispatchStructuralRead(
                        unit.UnitId,
                        unit.UnitIndex,
                        AiIndirectDispatchStructuralRole.Guard,
                        "guard de fase",
                        -1));
                    linkedUnitIds.Add(unit.UnitId);
                }

                foreach (AiIndirectDispatchWrite write in unit.PayloadWrites
                             .Where(write => write.VariableIndex == variableIndex)
                             .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(write => write.Offset))
                {
                    reads.Add(new IndirectDispatchStructuralRead(
                        unit.UnitId,
                        unit.UnitIndex,
                        AiIndirectDispatchStructuralRole.Command,
                        write.RoleSummary,
                        write.Offset));
                    linkedUnitIds.Add(unit.UnitId);
                }

                foreach (AiIndirectDispatchTargetSlotSurface slot in BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers)
                             .Where(slot => slot.VariableIndex == variableIndex)
                             .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset))
                {
                    reads.Add(new IndirectDispatchStructuralRead(
                        unit.UnitId,
                        unit.UnitIndex,
                        AiIndirectDispatchStructuralRole.Target,
                        slot.SlotLabel,
                        slot.SourceInstructionOffset));
                    linkedUnitIds.Add(unit.UnitId);
                }

                if (TextMentionsIndirectDispatchVariable(unit.NextStateSummary, variableName, variableIndex))
                {
                    reads.Add(new IndirectDispatchStructuralRead(
                        unit.UnitId,
                        unit.UnitIndex,
                        AiIndirectDispatchStructuralRole.NextState,
                        "proximo estado",
                        unit.EditableNextStateOffset ?? -1));
                    linkedUnitIds.Add(unit.UnitId);
                }
            }

            if (reads.Count == 0)
                return null;

            List<IndirectDispatchStructuralRead> guardReads = reads
                .Where(read => read.Role == AiIndirectDispatchStructuralRole.Guard)
                .ToList();
            IndirectDispatchStructuralRead preferredRead = PickPreferredIndirectDispatchStructuralRead(reads);
            List<int> routeIndexes = (guardReads.Count > 0 ? guardReads : reads)
                .Select(read => read.UnitIndex)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            string routeSummary =
                routeIndexes.Count == 0
                    ? preferredRead.RoleLabel
                    : $"rota {string.Join("/", routeIndexes)} · {(guardReads.Count > 0 ? "guard de fase" : preferredRead.RoleLabel)}";

            return new AiIndirectDispatchOpeningFocus(
                variableIndex,
                variableName,
                guardReads.Select(read => read.UnitId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                linkedUnitIds.Count,
                reads.Count,
                routeIndexes,
                preferredRead.UnitId,
                preferredRead.UnitIndex,
                preferredRead.Role,
                preferredRead.RoleLabel,
                preferredRead.InstructionOffset,
                routeSummary);
        }

        static IndirectDispatchStructuralRead PickPreferredIndirectDispatchStructuralRead(
            IReadOnlyList<IndirectDispatchStructuralRead> reads)
        {
            List<IndirectDispatchStructuralRead> guardReads = reads
                .Where(read => read.Role == AiIndirectDispatchStructuralRole.Guard)
                .ToList();
            if (guardReads.Count > 0)
            {
                double midpoint = (guardReads.Min(read => read.UnitIndex) + guardReads.Max(read => read.UnitIndex)) / 2.0;
                return guardReads
                    .OrderBy(read => Math.Abs(read.UnitIndex - midpoint))
                    .ThenByDescending(read => read.UnitIndex)
                    .First();
            }

            return reads
                .OrderBy(read => RoleSortKey(read.Role))
                .ThenBy(read => read.UnitIndex)
                .ThenBy(read => read.InstructionOffset < 0 ? int.MaxValue : read.InstructionOffset)
                .First();
        }

        static int CompareIndirectDispatchOpeningFocus(
            AiIndirectDispatchOpeningFocus left,
            AiIndirectDispatchOpeningFocus right)
        {
            int compare = left.GuardRouteCount.CompareTo(right.GuardRouteCount);
            if (compare != 0)
                return compare;

            compare = left.LinkedRouteCount.CompareTo(right.LinkedRouteCount);
            if (compare != 0)
                return compare;

            compare = left.StructuralReadCount.CompareTo(right.StructuralReadCount);
            if (compare != 0)
                return compare;

            int leftPriv = left.VariableName.StartsWith("priv", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            int rightPriv = right.VariableName.StartsWith("priv", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            compare = leftPriv.CompareTo(rightPriv);
            if (compare != 0)
                return compare;

            return right.VariableIndex.CompareTo(left.VariableIndex);
        }

        static bool TextMentionsIndirectDispatchVariable(string text, string variableName, ushort variableIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(variableName))
                return false;

            return text.Contains(variableName, StringComparison.OrdinalIgnoreCase)
                   || text.Contains($"var[{variableIndex}]", StringComparison.OrdinalIgnoreCase)
                   || text.Contains($"0x{variableIndex:X4}", StringComparison.OrdinalIgnoreCase);
        }

        static int RoleSortKey(AiIndirectDispatchStructuralRole role) => role switch
        {
            AiIndirectDispatchStructuralRole.Guard => 0,
            AiIndirectDispatchStructuralRole.Command => 1,
            AiIndirectDispatchStructuralRole.Target => 2,
            AiIndirectDispatchStructuralRole.NextState => 3,
            _ => 4,
        };

        static AiIndirectDispatchEditableTargetSlot PickPrimaryTargetSlotForSurface(IReadOnlyList<AiIndirectDispatchEditableTargetSlot> writes)
        {
            List<AiIndirectDispatchEditableTargetSlot> recipes = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe)
                .OrderByDescending(slot => slot.CanEdit)
                .ThenBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .ToList();
            if (recipes.Count > 0)
                return recipes[0];

            List<AiIndirectDispatchEditableTargetSlot> literals = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
                .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .ToList();
            if (literals.Select(slot => slot.CurrentValueResolved).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                return literals[0];

            List<AiIndirectDispatchEditableTargetSlot> copies = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue)
                .OrderByDescending(slot => slot.CanEdit)
                .ThenBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .ToList();
            if (copies.Count > 0)
                return copies[0];

            if (literals.Count > 0)
                return literals[0];

            return writes
                .OrderByDescending(slot => slot.CanEdit)
                .ThenBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .First();
        }

        static string BuildTargetSurfaceValueSummary(
            IReadOnlyList<AiIndirectDispatchEditableTargetSlot> writes,
            AiIndirectDispatchEditableTargetSlot primary)
        {
            List<string> literalValues = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
                .Select(slot => slot.CurrentValueResolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return primary.SourceKind switch
            {
                AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => $"{primary.CurrentValueResolved} (recipe calculada)",
                AiIndirectDispatchTargetSlotSourceKind.CopiedValue => $"{primary.CurrentValueResolved} (copia de var)",
                AiIndirectDispatchTargetSlotSourceKind.Literal when literalValues.Count > 1
                    => $"{FormatTargetSurfaceLiteralSample(literalValues)} ({literalValues.Count} literais possiveis)",
                AiIndirectDispatchTargetSlotSourceKind.Literal => $"{primary.CurrentValueResolved} (literal)",
                _ => $"{primary.CurrentValueResolved} (fonte desconhecida)",
            };
        }

        static string BuildTargetSurfaceDetailSummary(
            IReadOnlyList<AiIndirectDispatchEditableTargetSlot> writes,
            AiIndirectDispatchEditableTargetSlot primary,
            IReadOnlyList<string> consumerLabels)
        {
            var parts = new List<string>();
            parts.Add(BuildTargetSurfaceLead(primary, writes));

            if (consumerLabels.Count > 0)
                parts.Add($"Consumers atuais: {string.Join(" / ", consumerLabels)}.");

            if (writes.Count > 1)
            {
                if (BuildTargetSurfaceSecondaryHistory(writes, primary) is string secondaryHistory
                    && !string.IsNullOrWhiteSpace(secondaryHistory))
                {
                    parts.Add(secondaryHistory);
                }
            }

            if (writes.Any(slot => slot.CanEdit))
                parts.Add("At least one source of this slot already enters row-only.");

            return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        static string BuildTargetSurfaceLead(
            AiIndirectDispatchEditableTargetSlot primary,
            IReadOnlyList<AiIndirectDispatchEditableTargetSlot> writes)
        {
            int literalCount = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
                .Select(slot => slot.CurrentValueResolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            List<string> literalValues = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
                .Select(slot => slot.CurrentValueResolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return primary.SourceKind switch
            {
                AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => "Shape principal: recipe calculada reconhecida para este slot.",
                AiIndirectDispatchTargetSlotSourceKind.CopiedValue => "Main shape: this slot copies the value of another var.",
                AiIndirectDispatchTargetSlotSourceKind.Literal when literalCount > 1
                    => $"Shape principal: este slot varia entre {FormatTargetSurfaceLiteralSample(literalValues)}.",
                AiIndirectDispatchTargetSlotSourceKind.Literal => "Main shape: literal target saved in the slot before the indirect performCommand.",
                _ => "Main shape: the source of this slot has not yet been classified with full confidence.",
            };
        }

        static string? BuildTargetSurfaceSecondaryHistory(
            IReadOnlyList<AiIndirectDispatchEditableTargetSlot> writes,
            AiIndirectDispatchEditableTargetSlot primary)
        {
            List<string> segments = new();

            List<string> literalValues = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
                .Select(slot => slot.CurrentValueResolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int literalWriteCount = writes.Count(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal);
            if (literalValues.Count > 0
                && (primary.SourceKind != AiIndirectDispatchTargetSlotSourceKind.Literal || literalValues.Count > 1))
            {
                segments.Add($"{literalWriteCount} write(s) literal(is) entre {FormatTargetSurfaceLiteralSample(literalValues)}");
            }

            int recipeWriteCount = writes.Count(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe);
            if (recipeWriteCount > 0 && primary.SourceKind != AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe)
                segments.Add($"{recipeWriteCount} recipe(s) reconhecida(s)");

            List<string> copySources = writes
                .Where(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue)
                .Select(slot => slot.CurrentValueResolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (copySources.Count > 0 && primary.SourceKind != AiIndirectDispatchTargetSlotSourceKind.CopiedValue)
                segments.Add($"copia(s) vista(s): {string.Join(" / ", copySources.Take(2))}{(copySources.Count > 2 ? " / ..." : string.Empty)}");

            int unknownWriteCount = writes.Count(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Unknown);
            if (unknownWriteCount > 0)
                segments.Add($"{unknownWriteCount} write(s) ainda nao classificado(s)");

            if (segments.Count == 0)
                return null;

            return $"Historico secundario: {string.Join("; ", segments)}.";
        }

        static string FormatTargetSurfaceLiteralSample(IReadOnlyList<string> literalValues)
        {
            if (literalValues.Count == 0)
                return "alvos literais";

            List<string> sample = literalValues
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return string.Join(" / ", sample) + (literalValues.Count > sample.Count ? " / ..." : string.Empty);
        }

        static int FindIndirectPerformCallOffset(AiScriptFile script, ushort targetVarIndex, ushort commandVarIndex)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 2; i < instructions.Count; i++)
            {
                AiInstruction call = instructions[i];
                if (call.Opcode != CALLPOPA || call.Operand != PerformCommand)
                    continue;

                if (instructions[i - 2].Opcode == PUSHV
                    && instructions[i - 2].Operand == targetVarIndex
                    && instructions[i - 1].Opcode == PUSHV
                    && instructions[i - 1].Operand == commandVarIndex)
                {
                    return call.Offset;
                }
            }

            return -1;
        }

        static int FindLiteralCompareOffset(AiScriptFile script, ushort varIndex, ushort literal)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 2; i < instructions.Count; i++)
            {
                if (AiVarConditionBuilder.TryReadImmediateComparison(instructions, i - 2, out AiVarConditionClause clause)
                    && clause.VariableIndex == varIndex && clause.Value == unchecked((short)literal))
                {
                    return instructions[i].Offset;
                }
            }

            return -1;
        }

        static bool TryReadStrictSeymourDispatchRow(
            IReadOnlyList<AiInstruction> instructions,
            int startIndex,
            ushort normalCmdVar,
            ushort aeonCmdVar,
            ushort multiCmdVar,
            ushort pairCmdVar,
            ushort phaseVar,
            out StrictSeymourDispatchRow row)
        {
            row = default;
            int endIndex = startIndex + 9;
            if (endIndex >= instructions.Count)
                return false;

            if (!IsPushLiteralCommand(instructions[startIndex], out ushort normalCastCommand)
                || !IsPopVar(instructions[startIndex + 1], normalCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 2], out ushort aeonCastCommand)
                || !IsPopVar(instructions[startIndex + 3], aeonCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 4], out ushort multiCastCommand)
                || !IsPopVar(instructions[startIndex + 5], multiCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 6], out ushort pairCastCommand)
                || !IsPopVar(instructions[startIndex + 7], pairCmdVar)
                || !IsPushLiteral(instructions[startIndex + 8], out ushort nextState)
                || !IsPopVar(instructions[startIndex + 9], phaseVar))
            {
                return false;
            }

            row = new StrictSeymourDispatchRow(
                0,
                instructions[startIndex].Offset,
                instructions[startIndex].Offset,
                instructions[startIndex + 2].Offset,
                instructions[startIndex + 4].Offset,
                instructions[startIndex + 6].Offset,
                instructions[startIndex + 8].Offset,
                instructions[startIndex + 9].Offset,
                normalCastCommand,
                aeonCastCommand,
                multiCastCommand,
                pairCastCommand,
                nextState);
            return true;
        }

        static bool TryFindVariableIndexByName(AiScriptFile script, string variableName, out ushort index)
        {
            index = 0;
            AiVariable? variable = script.Variables.FirstOrDefault(v =>
                v.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase));
            if (variable == null || variable.Index < 0 || variable.Index > ushort.MaxValue)
                return false;

            index = (ushort)variable.Index;
            return true;
        }

        static bool IsPushLiteralCommand(AiInstruction instruction, out ushort operand)
        {
            operand = instruction.Operand;
            return instruction.Opcode == PUSHII && AiCommandId.IsCommandOperand(operand);
        }

        static bool IsPushLiteral(AiInstruction instruction, out ushort operand)
        {
            operand = instruction.Operand;
            return instruction.Opcode == PUSHII;
        }

        static bool IsPopVar(AiInstruction instruction, ushort variableIndex) =>
            instruction.Opcode == 0xA0 && instruction.Operand == variableIndex;

        sealed record StrictSeymourDispatchRow(
            int PhaseIndex,
            int StartOffset,
            int NormalCommandOffset,
            int AeonCommandOffset,
            int MultiCommandOffset,
            int PairCommandOffset,
            int NextStateOffset,
            int EndOffset,
            ushort NormalCastCommand,
            ushort AeonCastCommand,
            ushort MultiCastCommand,
            ushort PairCastCommand,
            ushort NextState);
    }

    /// <summary>What KIND of action statement a detected action is (#6). Drives the friendly icon/type tag in the
    /// list and which 1-click operations apply (change/toggle are Command-only; move/duplicate/remove are generic).</summary>
    public enum AiActionKind { Command, Buff, Stat }

    public enum AiTargetRecipeKind
    {
        Literal,
        FindAliveFrontlineAny,
        FindAliveFrontlineLowestHp,
    }

    public readonly record struct AiTargetRecipe(AiTargetRecipeKind Kind, ushort LiteralOperand)
    {
        public static AiTargetRecipe Literal(ushort operand) => new(AiTargetRecipeKind.Literal, operand);
    }

    /// <summary>An editable action statement recognised in a script (a performCommand, a writeChrProperty buff, or a
    /// setStatField tune). RemoveOffsets are the AiFile offsets a 1-click removal drops (the whole stack-neutral run
    /// when Removable). CmdPushOffset is the PUSHII that carries the command id (Command — the byte a "change ability"
    /// edit rewrites) or the field id (Buff/Stat). FieldId/FieldValue carry the decoded field+value for Buff/Stat
    /// (0 for Command, and 0 when the arg isn't a literal PUSHII).
    /// TargetPushOffset/TargetOperand/TargetOpcode/TargetIsLiteral describe the Command's TARGET slot — the push right before the
    /// command id (#8). TargetIsLiteral is true only when it is a PUSHII immediate (a target sentinel like self 0xFFF3,
    /// or another corpus-observed mode); only then can a 1-byte "change target" edit rewrite it length-preserving.
    /// A computed target (PUSHV findMatchingChr result) is not a literal, so it is not swappable by operand.</summary>
    public sealed record AiDetectedAction(
        AiActionKind Kind, string AbilityName, ushort CommandOperand, ushort PerformOperand, bool ForcePerform,
        int CallOffset, int CmdPushOffset, int WorkerIndex, bool Removable, IReadOnlyList<int> RemoveOffsets,
        ushort FieldId, ushort FieldValue,
        int TargetPushOffset = -1, ushort TargetOperand = 0, bool TargetIsLiteral = false, byte TargetOpcode = 0,
        byte CmdPushOpcode = 0xAE, bool CommandIsLiteral = true, IReadOnlyList<ushort>? CommandCandidates = null);

    public sealed record AiDetectedBranchAction(
        int WorkerIndex,
        int EntrypointIndex,
        int CallOffset,
        string HookKind,
        string Confidence,
        string GuardSummary,
        string TargetSummary,
        string CommandSummary,
        string TargetProvenance,
        string CommandProvenance,
        string PathId,
        IReadOnlyList<string> HighLevelHints);

    public enum AiIndirectDispatchCapabilityTier
    {
        Blocked,
        PreviewReadOnly,
        AuthoringCandidate,
    }

    public enum AiIndirectDispatchStructuralRole
    {
        Guard,
        Command,
        Target,
        NextState,
    }

    public sealed record AiIndirectDispatchOpeningFocus(
        ushort VariableIndex,
        string VariableName,
        int GuardRouteCount,
        int LinkedRouteCount,
        int StructuralReadCount,
        IReadOnlyList<int> RouteIndexes,
        string PreferredUnitId,
        int PreferredUnitIndex,
        AiIndirectDispatchStructuralRole PreferredRole,
        string PreferredRoleLabel,
        int PreferredInstructionOffset,
        string RouteSummary);

    public sealed record AiIndirectDispatchWrite(
        ushort VariableIndex,
        string VariableName,
        string RoleSummary,
        string ValueSummary,
        int Offset);

    public sealed record AiIndirectDispatchConsumer(
        string Label,
        ushort CommandVariableIndex,
        string CommandVariableName,
        ushort TargetVariableIndex,
        string TargetVariableName,
        int CallOffset);

    public sealed record AiIndirectDispatchTargetSlotSurface(
        string SlotLabel,
        ushort VariableIndex,
        string VariableName,
        AiIndirectDispatchTargetSlotSourceKind SourceKind,
        string ValueSummary,
        string DetailSummary,
        int SourceInstructionOffset,
        bool CanEdit,
        int HistoricalWriteCount);

    public sealed record AiIndirectDispatchUnit(
        string UnitId,
        int UnitIndex,
        string HookKind,
        string GuardSummary,
        string NextStateSummary,
        AiIndirectDispatchCapabilityTier CapabilityTier,
        string CapabilityLabel,
        IReadOnlyList<AiIndirectDispatchWrite> PayloadWrites,
        IReadOnlyList<AiIndirectDispatchConsumer> Consumers,
        IReadOnlyList<string> CompanionEffects,
        string OffsetSummary,
        string WarningSummary,
        IReadOnlyList<AiIndirectDispatchEditableSlot> EditableSlots,
        IReadOnlyList<AiIndirectDispatchEditableTargetSlot> EditableTargetSlots,
        int? EditableNextStateOffset,
        ushort? CurrentNextStateValue);

    sealed record IndirectDispatchStructuralRead(
        string UnitId,
        int UnitIndex,
        AiIndirectDispatchStructuralRole Role,
        string RoleLabel,
        int InstructionOffset);

    sealed record GenericIndirectDispatchConsumer(
        ushort CommandVariableIndex,
        ushort TargetVariableIndex,
        int CallOffset,
        bool ForcePerform);

    sealed record GenericIndirectDispatchCommandWrite(
        ushort VariableIndex,
        ushort CommandOperand,
        int Offset);

    sealed record GenericIndirectDispatchRouteCluster(
        int StartOffset,
        int EndOffset,
        IReadOnlyList<GenericIndirectDispatchCommandWrite> CommandWrites,
        ushort NextStateVariableIndex,
        ushort NextState,
        int NextStateOffset);

    sealed record SupportIndirectPayloadPickerCluster(
        int StartOffset,
        int EndOffset,
        IReadOnlyList<GenericIndirectDispatchCommandWrite> CommandWrites,
        ushort CommandVariableIndex,
        ushort TargetVariableIndex,
        int ConsumerCallOffset,
        bool ForcePerform,
        string GuardSummary,
        bool UsesSwitch,
        bool IsRandomized);

    sealed record ReactiveSensorPattern(
        int UsedCommandOffset,
        ushort UsedCommandVarIndex,
        ushort PropertyVarIndex,
        string PropertyName,
        int PropertyWriteOffset,
        ushort StateVarIndex,
        ushort OrLiteral,
        int OrWriteOffset,
        ushort SceneStateVarIndex,
        ushort? SceneStateValue,
        int SceneStateOffset,
        int SceneCallOffset,
        ushort SceneCallOperand,
        bool UsesSceneStateOnly);

    sealed record GenericRouteFamilyKey(
        ushort NextStateVariableIndex,
        string CommandSignature);

    enum BranchValueKind
    {
        ScalarLiteral,
        CommandLiteral,
        TargetLiteral,
        TargetRecipe,
    }

    sealed record BranchValue(BranchValueKind Kind, ushort Literal, string Summary, string Provenance);

    sealed class BranchTraversalState
    {
        public BranchTraversalState(
            int relative,
            Dictionary<ushort, BranchValue> vars,
            string guardSummary,
            string pathId,
            ushort? switchVar,
            bool random)
        {
            Relative = relative;
            Vars = vars;
            GuardSummary = guardSummary;
            PathId = pathId;
            SwitchVar = switchVar;
            Random = random;
        }

        public int Relative { get; set; }
        public Dictionary<ushort, BranchValue> Vars { get; }
        public string GuardSummary { get; set; }
        public string PathId { get; set; }
        public ushort? SwitchVar { get; set; }
        public bool Random { get; set; }

        public BranchTraversalState CloneFor(int relative, string pathId, string? extraGuard = null, bool? random = null)
            => new(
                relative,
                new Dictionary<ushort, BranchValue>(Vars),
                string.IsNullOrWhiteSpace(extraGuard) ? GuardSummary : CombineGuards(GuardSummary, extraGuard),
                pathId,
                SwitchVar,
                random ?? Random);

        static string CombineGuards(string outer, string inner)
        {
            if (string.IsNullOrWhiteSpace(outer)) return inner;
            if (string.IsNullOrWhiteSpace(inner)) return outer;
            // DescribeBranchGuard supplies only the newly encountered clause.
            // Re-entering a loop must not expand the inherited path formula.
            if (outer.Equals(inner, StringComparison.OrdinalIgnoreCase)
                || outer.Split(" + ", StringSplitOptions.None).Contains(inner, StringComparer.OrdinalIgnoreCase)) return outer;
            return $"{outer} + {inner}";
        }
    }

    /// <summary>A self-buff the editor can grant (writeChrProperty field id). ToString = Name for plain ComboBoxes.</summary>
    public readonly record struct AiBuffPreset(string Name, ushort FieldId)
    {
        public override string ToString() => Name;
    }
}
