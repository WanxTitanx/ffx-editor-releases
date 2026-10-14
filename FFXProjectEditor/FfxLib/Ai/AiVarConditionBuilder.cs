using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiVarCompareOperator
    {
        Equal,
        NotEqual,
        GreaterThan,
        LessThan,
        GreaterOrEqual,
        LessOrEqual,
        UnsignedGreaterThan,
        UnsignedLessThan,
        UnsignedGreaterOrEqual,
        UnsignedLessOrEqual,
    }

    public enum AiConditionJoinOperator
    {
        And,
        Or,
    }

    public readonly record struct AiVarConditionClause(
        ushort VariableIndex,
        AiVarCompareOperator Operator,
        int Value,
        AiConditionJoinOperator JoinBefore = AiConditionJoinOperator.And);

    /// <summary>
    /// Small human-condition emitter for Monster AI vars. It deliberately emits only stack-clean
    /// boolean guards, so callers can feed the result to AppendGuardedAction without exposing ATEL.
    ///
    /// With stack [A(base), B(top)], 0x0A = A &gt; B, 0x0B = A &lt; B,
    /// 0x0E = A &gt;= B and 0x0F = A &lt;= B. Operand tags select int/float.
    /// Emission keeps the existing swapped-LT/LE form for greater-than comparisons.
    /// See docs/reverse/FFX_EVENTVM_OPS_2026-09-17.md section 7 (interpreter @0x864180).
    /// </summary>
    public static class AiVarConditionBuilder
    {
        const byte PUSHV = 0x9F, PUSHII = 0xAE;
        const byte EQ = 0x06, NE = 0x07, LT = 0x0B, LE = 0x0F;
        const byte LTU = 0x09, LEU = 0x0D;
        const byte LOR = 0x01, LAND = 0x02;

        public static IReadOnlyList<AiInstruction> BuildImmediateComparison(
            ushort variableIndex, AiVarCompareOperator op, int value)
        {
            ValidateImmediate(value);
            // Preserve the swapped operand idiom for greater-than: emit LT/LE
            // (value < var  ≡ var > value ; value <= var ≡ var >= value).
            bool swap = op is AiVarCompareOperator.GreaterThan or AiVarCompareOperator.GreaterOrEqual
                or AiVarCompareOperator.UnsignedGreaterThan or AiVarCompareOperator.UnsignedGreaterOrEqual;
            return swap
                ? new[]
                {
                    Op(PUSHII, unchecked((ushort)value)),
                    Op(PUSHV, variableIndex),
                    Op(OpcodeFor(op)),
                }
                : new[]
                {
                    Op(PUSHV, variableIndex),
                    Op(PUSHII, unchecked((ushort)value)),
                    Op(OpcodeFor(op)),
                };
        }

        public static IReadOnlyList<AiInstruction> BuildJoinedImmediateComparisons(
            ushort leftVariableIndex, AiVarCompareOperator leftOp, int leftValue,
            AiConditionJoinOperator join,
            ushort rightVariableIndex, AiVarCompareOperator rightOp, int rightValue)
        {
            var guard = new List<AiInstruction>();
            guard.AddRange(BuildImmediateComparison(leftVariableIndex, leftOp, leftValue));
            guard.AddRange(BuildImmediateComparison(rightVariableIndex, rightOp, rightValue));
            guard.Add(Op(join == AiConditionJoinOperator.And ? LAND : LOR));
            return guard;
        }

        public static IReadOnlyList<AiInstruction> BuildClauseChain(IReadOnlyList<AiVarConditionClause> clauses)
        {
            ArgumentNullException.ThrowIfNull(clauses);
            if (clauses.Count == 0)
                throw new ArgumentException(Strings.F2_add_at_least_one_condition_907dd534, nameof(clauses));

            var guard = new List<AiInstruction>();
            for (int i = 0; i < clauses.Count; i++)
            {
                AiVarConditionClause clause = clauses[i];
                guard.AddRange(BuildImmediateComparison(clause.VariableIndex, clause.Operator, clause.Value));
                if (i > 0)
                    guard.Add(Op(clause.JoinBefore == AiConditionJoinOperator.And ? LAND : LOR));
            }
            return guard;
        }

        public static bool IsStackCleanBooleanGuard(IReadOnlyList<AiInstruction> guard, out string reason)
        {
            ArgumentNullException.ThrowIfNull(guard);
            int depth = 0;
            foreach (AiInstruction i in guard)
            {
                int? delta = AiStackModel.NetNonCall(i.Opcode);
                if (i.Opcode is 0xB5 or 0xD8)
                {
                    int? args = AiStackModel.CallArgCount(i.Operand);
                    delta = args == null ? null : i.Opcode == 0xB5 ? 1 - args.Value : -args.Value;
                }
                if (delta == null)
                {
                    reason = $"opcode 0x{i.Opcode:X2} sem modelo de pilha no guard.";
                    return false;
                }

                // Net -1 alone cannot distinguish a valid binary operation from
                // an underflow at depth 1 followed by another push.
                if (i.Opcode is >= 0x01 and <= 0x18 && depth < 2)
                {
                    reason = "guard consome mais valores do que empilha.";
                    return false;
                }

                depth += delta.Value;
                if (depth < 0)
                {
                    reason = "guard consome mais valores do que empilha.";
                    return false;
                }
            }

            if (depth != 1)
            {
                reason = string.Format(Strings.U_Ai_GuardDepth, depth);
                return false;
            }

            reason = string.Format(Strings.U_Ai_GuardValid, guard.Count);
            return true;
        }

        public static bool IsComparisonOpcode(byte opcode) => opcode is >= 0x06 and <= 0x0F;

        /// <summary>Decode the native relation, optionally expressed from the top operand's side.
        /// U opcodes compare uint32 values on the integer path (JBE/JB @86462D/86469C),
        /// unlike signed JLE/JL @864711/864786. Operand tags still select int/float.</summary>
        public static bool TryGetComparisonOperator(byte opcode, bool operandsSwapped, out AiVarCompareOperator op)
        {
            op = opcode switch
            {
                0x06 => AiVarCompareOperator.Equal,
                0x07 => AiVarCompareOperator.NotEqual,
                0x08 => AiVarCompareOperator.UnsignedGreaterThan,
                0x09 => AiVarCompareOperator.UnsignedLessThan,
                0x0C => AiVarCompareOperator.UnsignedGreaterOrEqual,
                0x0D => AiVarCompareOperator.UnsignedLessOrEqual,
                0x0A => AiVarCompareOperator.GreaterThan,
                0x0B => AiVarCompareOperator.LessThan,
                0x0E => AiVarCompareOperator.GreaterOrEqual,
                0x0F => AiVarCompareOperator.LessOrEqual,
                _ => default,
            };
            if (!IsComparisonOpcode(opcode))
                return false;
            if (operandsSwapped)
                op = op switch
                {
                    AiVarCompareOperator.GreaterThan => AiVarCompareOperator.LessThan,
                    AiVarCompareOperator.LessThan => AiVarCompareOperator.GreaterThan,
                    AiVarCompareOperator.GreaterOrEqual => AiVarCompareOperator.LessOrEqual,
                    AiVarCompareOperator.LessOrEqual => AiVarCompareOperator.GreaterOrEqual,
                    AiVarCompareOperator.UnsignedGreaterThan => AiVarCompareOperator.UnsignedLessThan,
                    AiVarCompareOperator.UnsignedLessThan => AiVarCompareOperator.UnsignedGreaterThan,
                    AiVarCompareOperator.UnsignedGreaterOrEqual => AiVarCompareOperator.UnsignedLessOrEqual,
                    AiVarCompareOperator.UnsignedLessOrEqual => AiVarCompareOperator.UnsignedGreaterOrEqual,
                    _ => op,
                };
            return true;
        }

        /// <summary>Read [var, immediate, compare] or [immediate, var, compare].
        /// PUSHII sign-extends its u16 encoding in the native interpreter (@0x864B51).</summary>
        public static bool TryReadImmediateComparison(
            IReadOnlyList<AiInstruction> instructions, int start, out AiVarConditionClause clause)
        {
            ArgumentNullException.ThrowIfNull(instructions);
            clause = default;
            if (start < 0 || start > instructions.Count - 3)
                return false;
            bool swapped = instructions[start].Opcode == PUSHII && instructions[start + 1].Opcode == PUSHV;
            if (!swapped && !(instructions[start].Opcode == PUSHV && instructions[start + 1].Opcode == PUSHII))
                return false;
            if (!TryGetComparisonOperator(instructions[start + 2].Opcode, swapped, out AiVarCompareOperator op))
                return false;
            clause = new AiVarConditionClause(
                instructions[start + (swapped ? 1 : 0)].Operand, op,
                unchecked((short)instructions[start + (swapped ? 0 : 1)].Operand));
            return true;
        }

        public static string OperatorLabel(AiVarCompareOperator op) => op switch
        {
            AiVarCompareOperator.Equal => "==",
            AiVarCompareOperator.NotEqual => "!=",
            AiVarCompareOperator.GreaterThan => ">",
            AiVarCompareOperator.LessThan => "<",
            AiVarCompareOperator.GreaterOrEqual => ">=",
            AiVarCompareOperator.LessOrEqual => "<=",
            // Stable technical symbols, also persisted in phase drafts. Do not
            // localize these into culture-dependent parser/serialization tokens.
            AiVarCompareOperator.UnsignedGreaterThan => "> (u32)",
            AiVarCompareOperator.UnsignedLessThan => "< (u32)",
            AiVarCompareOperator.UnsignedGreaterOrEqual => ">= (u32)",
            AiVarCompareOperator.UnsignedLessOrEqual => "<= (u32)",
            _ => Strings.F2__5bab61eb,
        };

        public static bool TryParseOperator(string? label, out AiVarCompareOperator op)
        {
            op = label?.Trim() switch
            {
                "==" or "=" => AiVarCompareOperator.Equal,
                "!=" or "<>" => AiVarCompareOperator.NotEqual,
                ">" => AiVarCompareOperator.GreaterThan,
                "<" => AiVarCompareOperator.LessThan,
                ">=" => AiVarCompareOperator.GreaterOrEqual,
                "<=" => AiVarCompareOperator.LessOrEqual,
                "> (u32)" => AiVarCompareOperator.UnsignedGreaterThan,
                "< (u32)" => AiVarCompareOperator.UnsignedLessThan,
                ">= (u32)" => AiVarCompareOperator.UnsignedGreaterOrEqual,
                "<= (u32)" => AiVarCompareOperator.UnsignedLessOrEqual,
                _ => AiVarCompareOperator.Equal,
            };
            return label?.Trim() is "==" or "=" or "!=" or "<>" or ">" or "<" or ">=" or "<="
                or "> (u32)" or "< (u32)" or ">= (u32)" or "<= (u32)";
        }

        public static bool TryParseImmediate(string? text, out int value)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value)
                    && value <= ushort.MaxValue;
            return int.TryParse(text, out value) && value >= short.MinValue && value <= ushort.MaxValue;
        }

        static void ValidateImmediate(int value)
        {
            if (value < short.MinValue || value > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), Strings.F2_pushii_accepts_values_from_32768_to_6553_94a379a3);
        }

        static byte OpcodeFor(AiVarCompareOperator op) => op switch
        {
            AiVarCompareOperator.Equal => EQ,
            AiVarCompareOperator.NotEqual => NE,
            // With the swapped operand order (value pushed first): 0x0B tests value < var ≡ var > value;
            // 0x0F tests value <= var ≡ var >= value.
            AiVarCompareOperator.GreaterThan => LT,
            AiVarCompareOperator.LessThan => LT,
            AiVarCompareOperator.GreaterOrEqual => LE,
            AiVarCompareOperator.LessOrEqual => LE,
            AiVarCompareOperator.UnsignedGreaterThan or AiVarCompareOperator.UnsignedLessThan => LTU,
            AiVarCompareOperator.UnsignedGreaterOrEqual or AiVarCompareOperator.UnsignedLessOrEqual => LEU,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

        static AiInstruction Op(byte opcode, ushort operand = 0) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }
}
