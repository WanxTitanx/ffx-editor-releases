using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiTargetRecipeDescriptor(
        string RoleKey,
        string SlotLabel,
        string VariableName,
        int AnchorInstructionOffset,
        int SequenceStartCodeRelative,
        int SequenceEndCodeRelative,
        AiTargetRecipeKind CurrentRecipe,
        ushort CurrentLiteralOperand,
        bool IsEditable)
    {
        public bool RequiresLegacyRepair { get; init; }
    }

    public sealed record AiTargetRecipeSwapRequest(
        string RoleKey,
        int AnchorInstructionOffset,
        AiTargetRecipeKind NewRecipe,
        ushort LiteralOperand);

    public sealed record AiTargetRecipeEditResult(
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiInstruction> NewSequence);

    /// <summary>Sequence-level target authoring: rewrites a target slot's findMatchingChr recipe (or a literal
    /// sentinel) into any other supported recipe, growing/shrinking the code region via
    /// AiScript_File.RebuildWithCodeRegion (which remaps entrypoints, jump targets and data-section pointers).</summary>
    public static class AiTargetRecipeWriter
    {
        const ushort FindMatchingChr = 0x7010, FrontlineChars = 0xFFF2, MatchingGroup = 0xFFF0;
        const ushort ChrFieldIsAlive = 0x0004, ChrFieldHp = AiAutomation.CurrentHpField;
        const ushort SelectorAny = 0x0000, SelectorLowest = 0x0002;
        const byte PushIi = 0xAE, Call = 0xB5, CallPopA = 0xD8, VarStore = 0xA0;

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiTargetRecipeDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiTargetRecipeDescriptor>();
            error = string.Empty;
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            var rows = new List<AiTargetRecipeDescriptor>();
            var seen = new HashSet<int>();

            for (int i = 1; i < ins.Count; i++)
            {
                if (ins[i].Opcode != VarStore)
                    continue;

                AiInstruction source = ins[i - 1];
                int anchor = ins[i].Offset;
                int seqStart, seqEnd;
                AiTargetRecipeKind kind;
                bool editable;

                if (source.Opcode == PushIi)
                {
                    seqStart = source.Offset - script.ScriptStart;
                    seqEnd = seqStart + 3;
                    kind = AiTargetRecipeKind.Literal;
                    editable = true;
                }
                else if (source.Opcode == Call && source.Operand == FindMatchingChr)
                {
                    if (IsFrontlineAnyPattern(ins, i - 1))
                    {
                        seqStart = ins[i - 5].Offset - script.ScriptStart;
                        seqEnd = ins[i - 1].Offset + 3 - script.ScriptStart;
                        kind = AiTargetRecipeKind.FindAliveFrontlineAny;
                        editable = true;
                    }
                    else if (IsLowestHpPattern(ins, i - 1))
                    {
                        seqStart = ins[i - 10].Offset - script.ScriptStart;
                        seqEnd = ins[i - 1].Offset + 3 - script.ScriptStart;
                        kind = AiTargetRecipeKind.FindAliveFrontlineLowestHp;
                        editable = true;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                if (!seen.Add(anchor))
                    continue;

                rows.Add(new AiTargetRecipeDescriptor(
                    $"target-slot.{rows.Count}",
                    "alvo",
                    script.Variables.Count > 0 ? $"var{ins[i].Operand}" : string.Empty,
                    anchor,
                    seqStart,
                    seqEnd,
                    kind,
                    kind == AiTargetRecipeKind.Literal ? source.Operand : (ushort)0,
                    editable)
                {
                    RequiresLegacyRepair = kind == AiTargetRecipeKind.FindAliveFrontlineLowestHp
                        && IsLegacyLowestHpPattern(ins, i - 1),
                });
            }

            descriptors = rows;
            return rows.Count > 0;
        }

        static bool IsFrontlineAnyPattern(IReadOnlyList<AiInstruction> ins, int callIndex)
        {
            if (callIndex < 4)
                return false;
            return ins[callIndex - 1].Opcode == PushIi && ins[callIndex - 1].Operand == SelectorAny
                && ins[callIndex - 2].Opcode == PushIi && ins[callIndex - 2].Operand == 0
                && ins[callIndex - 3].Opcode == PushIi && ins[callIndex - 3].Operand == ChrFieldIsAlive
                && ins[callIndex - 4].Opcode == PushIi && ins[callIndex - 4].Operand == FrontlineChars;
        }

        static bool IsLowestHpPattern(IReadOnlyList<AiInstruction> ins, int callIndex)
        {
            if (callIndex < 9)
                return false;
            return ins[callIndex - 1].Opcode == PushIi && ins[callIndex - 1].Operand == SelectorLowest
                && ins[callIndex - 2].Opcode == PushIi && ins[callIndex - 2].Operand == 0
                && ins[callIndex - 3].Opcode == PushIi && ins[callIndex - 3].Operand is ChrFieldHp or 0x011B
                && ins[callIndex - 4].Opcode == PushIi && ins[callIndex - 4].Operand == MatchingGroup
                && ins[callIndex - 5].Opcode is CallPopA or 0xAF && ins[callIndex - 5].Operand == FindMatchingChr
                && ins[callIndex - 6].Opcode == PushIi && ins[callIndex - 6].Operand == SelectorAny
                && ins[callIndex - 7].Opcode == PushIi && ins[callIndex - 7].Operand == 0
                && ins[callIndex - 8].Opcode == PushIi && ins[callIndex - 8].Operand == ChrFieldIsAlive
                && ins[callIndex - 9].Opcode == PushIi && ins[callIndex - 9].Operand == FrontlineChars;
        }

        // Exact malformed shape emitted by older versions. It is recognized only
        // to repair it; new writes always use numeric HP and CALLPOPA (D8).
        static bool IsLegacyLowestHpPattern(IReadOnlyList<AiInstruction> ins, int callIndex) =>
            ins[callIndex - 3].Operand == 0x011B || ins[callIndex - 5].Opcode == 0xAF;

        public static bool TryApplyRecipeSwap(
            AiScriptFile script,
            AiTargetRecipeSwapRequest request,
            out AiTargetRecipeEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (script.Instructions.Count == 0)
            {
                error = "script vazio.";
                return false;
            }

            int anchorIndex = -1;
            for (int i = 0; i < script.Instructions.Count; i++)
            {
                if (script.Instructions[i].Offset == request.AnchorInstructionOffset)
                {
                    anchorIndex = i;
                    break;
                }
            }
            if (anchorIndex <= 0)
            {
                error = $"anchor 0x{request.AnchorInstructionOffset:X4} nao encontrado.";
                return false;
            }

            AiInstruction store = script.Instructions[anchorIndex];
            if (store.Opcode != VarStore)
            {
                error = $"anchor 0x{request.AnchorInstructionOffset:X4} nao e um VarStore.";
                return false;
            }

            AiInstruction source = script.Instructions[anchorIndex - 1];
            AiTargetRecipeKind current;
            int seqStart, seqEnd;
            if (source.Opcode == PushIi)
            {
                current = AiTargetRecipeKind.Literal;
                seqStart = source.Offset - script.ScriptStart;
                seqEnd = seqStart + 3;
            }
            else if (source.Opcode == Call && source.Operand == FindMatchingChr)
            {
                if (IsFrontlineAnyPattern(script.Instructions, anchorIndex - 1))
                {
                    current = AiTargetRecipeKind.FindAliveFrontlineAny;
                    seqStart = script.Instructions[anchorIndex - 5].Offset - script.ScriptStart;
                    seqEnd = source.Offset + 3 - script.ScriptStart;
                }
                else if (IsLowestHpPattern(script.Instructions, anchorIndex - 1))
                {
                    current = AiTargetRecipeKind.FindAliveFrontlineLowestHp;
                    seqStart = script.Instructions[anchorIndex - 10].Offset - script.ScriptStart;
                    seqEnd = source.Offset + 3 - script.ScriptStart;
                }
                else
                {
                    error = "findMatchingChr pattern not recognized at anchor.";
                    return false;
                }
            }
            else
            {
                error = "fonte do anchor nao e PUSHII nem findMatchingChr.";
                return false;
            }

            if (!TryBuildDescriptors(script, out var live, out _)
                || !live.Any(row => row.AnchorInstructionOffset == request.AnchorInstructionOffset && row.RoleKey == request.RoleKey))
            {
                error = "target slot identity changed; reload before applying.";
                return false;
            }
            bool repairLegacy = current == AiTargetRecipeKind.FindAliveFrontlineLowestHp
                && IsLegacyLowestHpPattern(script.Instructions, anchorIndex - 1);
            if (current == request.NewRecipe && !repairLegacy
                && (current != AiTargetRecipeKind.Literal || source.Operand == request.LiteralOperand))
            {
                error = $"recipe ja e {request.NewRecipe}.";
                return false;
            }

            List<AiInstruction> replacement = BuildRecipeSequence(request.NewRecipe, request.LiteralOperand, out string buildError);
            if (replacement == null)
            {
                error = buildError;
                return false;
            }

            byte[]? edited = AiScript_File.RebuildWithCodeRegion(
                script, seqStart, seqEnd, replacement, out string remapError);
            if (edited == null)
            {
                error = remapError;
                return false;
            }

            result = new AiTargetRecipeEditResult(edited, replacement);
            return true;
        }

        static AiInstruction NewInstr(byte opcode, ushort operand) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        static List<AiInstruction> BuildRecipeSequence(
            AiTargetRecipeKind recipe,
            ushort literalOperand,
            out string error)
        {
            error = string.Empty;
            var seq = new List<AiInstruction>();
            switch (recipe)
            {
                case AiTargetRecipeKind.Literal:
                    seq.Add(NewInstr(PushIi, literalOperand));
                    break;
                case AiTargetRecipeKind.FindAliveFrontlineAny:
                    seq.Add(NewInstr(PushIi, FrontlineChars));
                    seq.Add(NewInstr(PushIi, ChrFieldIsAlive));
                    seq.Add(NewInstr(PushIi, 0));
                    seq.Add(NewInstr(PushIi, SelectorAny));
                    seq.Add(NewInstr(Call, FindMatchingChr));
                    break;
                case AiTargetRecipeKind.FindAliveFrontlineLowestHp:
                    seq.Add(NewInstr(PushIi, FrontlineChars));
                    seq.Add(NewInstr(PushIi, ChrFieldIsAlive));
                    seq.Add(NewInstr(PushIi, 0));
                    seq.Add(NewInstr(PushIi, SelectorAny));
                    seq.Add(NewInstr(CallPopA, FindMatchingChr));
                    seq.Add(NewInstr(PushIi, MatchingGroup));
                    seq.Add(NewInstr(PushIi, ChrFieldHp));
                    seq.Add(NewInstr(PushIi, 0));
                    seq.Add(NewInstr(PushIi, SelectorLowest));
                    seq.Add(NewInstr(Call, FindMatchingChr));
                    break;
                default:
                    error = $"recipe desconhecida: {recipe}";
                    return null;
            }

            return seq;
        }
    }
}
