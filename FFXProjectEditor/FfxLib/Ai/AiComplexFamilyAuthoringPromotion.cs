using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    public static class AiComplexFamilyAuthoringPromotion
    {
        const byte PushIiOpcode = 0xAE;
        const byte PopVariableOpcode = 0xA0;
        const byte ReactiveOrOpcode = 0x03;
        const byte PushVariableOpcode = 0x9F;

        public static IReadOnlyList<AiIndirectDispatchUnit> Promote(
            AiScriptFile script,
            IReadOnlyList<AiIndirectDispatchUnit> units)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(units);

            return units
                .Select(unit => PromoteUnit(script, unit))
                .ToList();
        }

        static AiIndirectDispatchUnit PromoteUnit(AiScriptFile script, AiIndirectDispatchUnit unit)
        {
            if (unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate)
                return unit;

            AiIndirectDispatchUnit promotedDirect = PromoteDirectBranchBundle(script, unit);
            if (!ReferenceEquals(promotedDirect, unit))
                return promotedDirect;

            AiIndirectDispatchUnit promotedReactive = PromoteReactiveSensor(script, unit);
            return promotedReactive;
        }

        static AiIndirectDispatchUnit PromoteDirectBranchBundle(AiScriptFile script, AiIndirectDispatchUnit unit)
        {
            if (!unit.UnitId.StartsWith("preview-", StringComparison.OrdinalIgnoreCase))
                return unit;

            if (unit.UnitId.StartsWith("preview-round-", StringComparison.OrdinalIgnoreCase))
                return unit;

            if (unit.UnitId.StartsWith("preview-tonberry-", StringComparison.OrdinalIgnoreCase))
                return unit;

            if (unit.UnitId.StartsWith("preview-encounter-", StringComparison.OrdinalIgnoreCase))
                return unit;

            if (unit.Consumers.Count == 0)
                return unit;

            var payloadWrites = unit.PayloadWrites
                .Where(write => write.Offset >= 0)
                .OrderBy(write => write.Offset)
                .ToList();
            List<AiIndirectDispatchEditableSlot> editableSlots = unit.EditableSlots.ToList();
            List<AiIndirectDispatchEditableTargetSlot> targetSlots = unit.EditableTargetSlots
                .OrderBy(slot => slot.SourceInstructionOffset)
                .ToList();
            bool changed = false;

            for (int slotIndex = 0; slotIndex < unit.Consumers.Count; slotIndex++)
            {
                AiIndirectDispatchConsumer consumer = unit.Consumers[slotIndex];
                int callIndex = FindInstructionIndexByOffset(script, consumer.CallOffset);
                if (callIndex < 1)
                    continue;

                AiInstruction commandPush = script.Instructions[callIndex - 1];
                if (commandPush.Opcode == PushIiOpcode
                    && AiCommandId.IsCommandOperand(commandPush.Operand)
                    && editableSlots.All(slot => slot.PushInstructionOffset != commandPush.Offset))
                {
                    AiIndirectDispatchWrite? payloadWrite = payloadWrites.FirstOrDefault(write => write.Offset == consumer.CallOffset)
                        ?? payloadWrites.ElementAtOrDefault(slotIndex);
                    string slotLabel = payloadWrite?.RoleSummary ?? $"payload #{slotIndex + 1}";
                    string valueResolved = payloadWrite?.ValueSummary ?? DescribeCommandOperand(commandPush.Operand);

                    editableSlots.Add(new AiIndirectDispatchEditableSlot(
                        $"command.preview.{unit.UnitIndex}.{slotIndex}",
                        slotLabel,
                        commandPush.Offset,
                        commandPush.Operand,
                        valueResolved));
                    changed = true;
                }

                if (callIndex < 2)
                    continue;

                AiInstruction targetPush = script.Instructions[callIndex - 2];
                if (targetPush.Opcode != PushIiOpcode)
                    continue;

                int targetSlotIndex = slotIndex < targetSlots.Count
                    ? slotIndex
                    : targetSlots.FindIndex(slot => slot.VariableIndex == consumer.TargetVariableIndex);
                if (targetSlotIndex < 0 || targetSlotIndex >= targetSlots.Count)
                    continue;

                AiIndirectDispatchEditableTargetSlot sourceTarget = targetSlots[targetSlotIndex];
                targetSlots[targetSlotIndex] = sourceTarget with
                {
                    SourceInstructionOffset = targetPush.Offset,
                    SourceKind = AiIndirectDispatchTargetSlotSourceKind.Literal,
                    CurrentValue = targetPush.Operand,
                    CurrentValueResolved = DescribeLiteralTarget(targetPush.Operand),
                    DetailSummary = AppendPromotionNote(sourceTarget.DetailSummary, "Writer narrow: alvo literal editavel no push direto deste cast."),
                    CanEdit = true,
                    RecipeKind = null,
                    EditableOperands =
                    [
                        new AiIndirectDispatchEditableOperand(
                            sourceTarget.RoleKey,
                            sourceTarget.SlotLabel,
                            targetPush.Offset,
                            targetPush.Opcode,
                            targetPush.Operand,
                            DescribeLiteralTarget(targetPush.Operand)),
                    ],
                    CopyVariableCandidates = Array.Empty<AiIndirectDispatchVariableReference>(),
                };
                changed = true;
            }

            if (!changed)
                return unit;

            return unit with
            {
                CapabilityTier = AiIndirectDispatchCapabilityTier.AuthoringCandidate,
                WarningSummary =
                    Strings.U_Ai_FamilyWriterWarning,
                EditableSlots = editableSlots,
                EditableTargetSlots = targetSlots,
            };
        }

        static AiIndirectDispatchUnit PromoteReactiveSensor(AiScriptFile script, AiIndirectDispatchUnit unit)
        {
            if (!unit.UnitId.StartsWith("preview-reactive-", StringComparison.OrdinalIgnoreCase))
                return unit;

            List<(int Offset, ushort Value)> candidates = unit.PayloadWrites
                .Where(write =>
                    write.RoleSummary.Contains("estado reativo", StringComparison.OrdinalIgnoreCase)
                    || write.RoleSummary.Contains("flag reativa", StringComparison.OrdinalIgnoreCase)
                    || write.RoleSummary.Contains("slot de cena", StringComparison.OrdinalIgnoreCase))
                .Select(write => TryResolveReactiveLiteralEdit(script, write.Offset, out int literalOffset, out ushort literalValue)
                    ? (literalOffset, literalValue)
                    : (-1, (ushort)0))
                .Where(candidate => candidate.Item1 >= 0)
                .Select(candidate => (candidate.Item1, candidate.Item2))
                .Distinct()
                .ToList();

            if (candidates.Count != 1)
                return unit;

            (int offset, ushort value) = candidates[0];
            return unit with
            {
                CapabilityTier = AiIndirectDispatchCapabilityTier.AuthoringCandidate,
                WarningSummary =
                    Strings.U_Ai_FamilyWriterReactive,
                EditableNextStateOffset = offset,
                CurrentNextStateValue = value,
            };
        }

        static bool TryResolveReactiveLiteralEdit(
            AiScriptFile script,
            int writeOffset,
            out int literalOffset,
            out ushort literalValue)
        {
            literalOffset = -1;
            literalValue = 0;

            int index = FindInstructionIndexByOffset(script, writeOffset);
            if (index < 0)
                return false;

            AiInstruction instruction = script.Instructions[index];
            if (instruction.Opcode == PushIiOpcode)
            {
                literalOffset = instruction.Offset;
                literalValue = instruction.Operand;
                return true;
            }

            if (instruction.Opcode != PopVariableOpcode || index < 3)
                return false;

            AiInstruction op = script.Instructions[index - 1];
            AiInstruction literalPush = script.Instructions[index - 2];
            AiInstruction sourcePush = script.Instructions[index - 3];
            if (op.Opcode != ReactiveOrOpcode
                || literalPush.Opcode != PushIiOpcode
                || sourcePush.Opcode != PushVariableOpcode
                || sourcePush.Operand != instruction.Operand)
            {
                return false;
            }

            literalOffset = literalPush.Offset;
            literalValue = literalPush.Operand;
            return true;
        }

        static int FindInstructionIndexByOffset(AiScriptFile script, int offset)
        {
            for (int i = 0; i < script.Instructions.Count; i++)
            {
                if (script.Instructions[i].Offset == offset)
                    return i;
            }

            return -1;
        }

        static string DescribeCommandOperand(ushort operand)
        {
            AiCommandDecode decoded = AiCommandId.Decode(operand);
            return $"{decoded.Name} [0x{operand:X4}]";
        }

        static string DescribeLiteralTarget(ushort operand)
        {
            string? targetName = AiTargetNames.Get(operand);
            return string.IsNullOrWhiteSpace(targetName)
                ? $"0x{operand:X4}"
                : $"{targetName} [0x{operand:X4}]";
        }

        static string AppendPromotionNote(string detailSummary, string suffix)
        {
            if (string.IsNullOrWhiteSpace(detailSummary))
                return suffix;

            if (detailSummary.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                return detailSummary;

            return $"{detailSummary} {suffix}";
        }
    }
}
