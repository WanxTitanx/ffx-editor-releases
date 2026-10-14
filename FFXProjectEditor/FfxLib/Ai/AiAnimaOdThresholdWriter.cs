using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiAnimaOdThresholdDescriptor(
        int MaximumInstructionOffset,
        ushort CurrentMaximum,
        int SetterCallOffset,
        int ComparisonInstructionOffset,
        int AttackInstructionOffset,
        ushort CurrentAttackThreshold);

    public sealed record AiAnimaOdThresholdPatchRequest(
        int MaximumInstructionOffset,
        ushort OldMaximum,
        ushort NewMaximum,
        ushort? OldAttackThreshold = null);

    public sealed record AiAnimaOdThresholdByteChange(int Offset, byte OldValue, byte NewValue);

    public sealed record AiAnimaOdThresholdEditResult(
        AiAnimaOdThresholdPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiAnimaOdThresholdByteChange> ChangedBytes);

    public static class AiAnimaOdThresholdWriter
    {
        // Native field 0x14 uses clamp(value, 1, 100) @7B4BB6 and stores
        // the result to actor+0x5BD @7B4E05; values above 100 have no effect.
        // m125 initializes Self.OverdriveMax = 100 and its combat worker tests
        // read(Self, Current) >= read(Self, Max). There is no division.
        // The old loose scan matched writeChrProperty(Self, 0x57, 1) followed by
        // a literal in another entrypoint. That literal was NOT an OD threshold.
        // See docs/reverse/FFX_ATEL_ADVANCED_AUTHORING_AUDIT_2026-09-20.md.
        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiAnimaOdThresholdDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiAnimaOdThresholdDescriptor>();
            error = string.Empty;
            if (script == null || !script.HasScript || !script.CodeWalkClosedExactly)
            {
                error = Strings.AiAdvancedIncompleteScript;
                return false;
            }

            var instructions = script.Instructions;
            var owners = AiScript_File.InstructionOwners(script);
            var branchTargets = script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets))
                .Select(offset => offset + script.ScriptStart).ToHashSet();
            var setters = new List<int>();
            var comparisons = new List<int>();
            var attackComparisons = new List<int>();

            bool Is(int index, byte opcode, ushort? operand = null) =>
                instructions[index].Opcode == opcode
                && instructions[index].HasOperand == ((opcode & 0x80) != 0)
                && (!operand.HasValue || instructions[index].Operand == operand.Value);

            bool SingleBlock(int start, int length) => Enumerable.Range(start + 1, length - 1)
                .All(i => instructions[i].Offset == instructions[i - 1].Offset + instructions[i - 1].Length
                    && !branchTargets.Contains(instructions[i].Offset));

            for (int i = 0; i < instructions.Count; i++)
            {
                if (i + 3 < instructions.Count
                    && Is(i, 0xAE, 0xFFF3) && Is(i + 1, 0xAE, 0x0014)
                    && Is(i + 2, 0xAE) && Is(i + 3, 0xD8, 0x7018)
                    && SingleBlock(i, 4))
                    setters.Add(i);

                if (i + 7 < instructions.Count
                    && Is(i, 0xAE, 0xFFF3) && Is(i + 1, 0xAE, 0x0013) && Is(i + 2, 0xB5, 0x700F)
                    && Is(i + 3, 0xAE, 0xFFF3) && Is(i + 4, 0xAE, 0x0014) && Is(i + 5, 0xB5, 0x700F)
                    && Is(i + 6, 0x0E) && Is(i + 7, 0xD7) && SingleBlock(i, 8))
                    comparisons.Add(i);

                if (i + 5 < instructions.Count
                    && Is(i, 0xAE, 0xFFF3) && Is(i + 1, 0xAE, 0x0013) && Is(i + 2, 0xB5, 0x700F)
                    && Is(i + 3, 0xAE) && Is(i + 4, 0x0E) && Is(i + 5, 0xD7) && SingleBlock(i, 6))
                    attackComparisons.Add(i);
            }

            // Ambiguous or cross-worker matches require investigation. They are
            // not writable descriptors. Re-derive this contract after every edit.
            if (setters.Count != 1 || comparisons.Count != 1 || attackComparisons.Count != 1
                || !owners.TryGetValue(instructions[setters[0]].Offset, out var setterOwners)
                || !owners.TryGetValue(instructions[comparisons[0]].Offset, out var comparisonOwners)
                || !owners.TryGetValue(instructions[attackComparisons[0]].Offset, out var attackOwners)
                || setterOwners.Count != 1 || comparisonOwners.Count != 1 || attackOwners.Count != 1
                || setterOwners[0] != comparisonOwners[0] || setterOwners[0] != attackOwners[0])
            {
                error = Strings.AiAdvancedOverdriveUnresolved;
                return false;
            }

            int setter = setters[0], comparison = comparisons[0];
            ushort maximum = instructions[setter + 2].Operand;
            int attack = attackComparisons[0];
            if (maximum == 0 || maximum > 100)
            {
                error = Strings.AiAdvancedOverdriveOriginalRange;
                return false;
            }
            // m125's scene gate reads capacity, but the action gate uses an
            // independent literal at 03F8. Lowering capacity alone left that
            // gate at 100. Preserve their proven equality with one atomic patch.
            ushort attackThreshold = instructions[attack + 3].Operand;
            if (attackThreshold == 0 || attackThreshold > 100)
            {
                error = Strings.AiAdvancedOverdriveUnresolved;
                return false;
            }
            descriptors = new[] { new AiAnimaOdThresholdDescriptor(instructions[setter + 2].Offset, maximum,
                instructions[setter + 3].Offset, instructions[comparison + 6].Offset, instructions[attack + 3].Offset, attackThreshold) };
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiAnimaOdThresholdPatchRequest request,
            out AiAnimaOdThresholdEditResult? result,
            out string error)
        {
            result = null;
            if (!TryBuildDescriptors(script, out var descriptors, out error))
                return false;
            var live = descriptors[0];
            if (request == null || live.MaximumInstructionOffset != request.MaximumInstructionOffset
                || live.CurrentMaximum != request.OldMaximum
                || live.CurrentAttackThreshold != (request.OldAttackThreshold ?? request.OldMaximum))
            {
                error = Strings.AiAdvancedOverdriveChanged;
                return false;
            }
            if (request.NewMaximum == 0 || request.NewMaximum > 100)
            {
                error = Strings.AiAdvancedOverdriveRange;
                return false;
            }

            var instruction = script.Instructions.Single(i => i.Offset == live.MaximumInstructionOffset);
            var attackInstruction = script.Instructions.Single(i => i.Offset == live.AttackInstructionOffset);
            byte[] before = AiScript_File.Write(script);
            try
            {
                instruction.Operand = request.NewMaximum;
                attackInstruction.Operand = request.NewMaximum;
                byte[] after = AiScript_File.Write(script);
                var changes = new List<AiAnimaOdThresholdByteChange>();
                if (after.Length != before.Length)
                {
                    error = Strings.AiAdvancedUnexpectedSize;
                    return false;
                }
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == after[i]) continue;
                    if (i != live.MaximumInstructionOffset + 1 && i != live.MaximumInstructionOffset + 2
                        && i != live.AttackInstructionOffset + 1 && i != live.AttackInstructionOffset + 2)
                    {
                        error = Strings.AiAdvancedUnexpectedBytes;
                        return false;
                    }
                    changes.Add(new AiAnimaOdThresholdByteChange(i, before[i], after[i]));
                }
                result = new AiAnimaOdThresholdEditResult(request, after, changes);
                return true;
            }
            finally
            {
                // Failed validation or save must not leave a pending mutation.
                instruction.Operand = live.CurrentMaximum;
                attackInstruction.Operand = live.CurrentAttackThreshold;
            }
        }
    }
}
