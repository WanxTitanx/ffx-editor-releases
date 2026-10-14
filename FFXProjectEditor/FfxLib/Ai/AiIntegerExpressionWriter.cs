using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record AiIntegerExpressionLiteral(int Offset, int Value, int Minimum, int Maximum, int? PoolOffset = null);
public sealed record AiIntegerExpressionOperator(int Offset, byte Opcode);
public sealed record AiIntegerExpressionEdit(int? LiteralOffset = null, int? Value = null, int? OperatorOffset = null, byte? Opcode = null);
public sealed record AiIntegerExpressionDescriptor(int StartOffset, int SinkOffset, int WorkerIndex,
    string Expression, string Destination, IReadOnlyList<AiIntegerExpressionLiteral> Literals, string SourceSha256,
    IReadOnlyList<AiIntegerExpressionOperator> Operators);

/// <summary>Authoring for bounded, pure integer expression trees in native ATEL.</summary>
public static class AiIntegerExpressionWriter
{
    sealed record Node(int StartIndex, int EndIndex, string Text, bool Compound,
        IReadOnlyList<AiIntegerExpressionLiteral> Literals, IReadOnlyList<AiIntegerExpressionOperator>? Operators = null)
    {
        public IReadOnlyList<AiIntegerExpressionOperator> OperatorNodes => Operators ?? Array.Empty<AiIntegerExpressionOperator>();
    }

    public static IReadOnlyList<byte> AllowedOperators(byte opcode) => opcode switch
    {
        0x01 or 0x02 => new byte[] { 0x01, 0x02 },
        0x06 or 0x07 or 0x0A or 0x0B or 0x0E or 0x0F => new byte[] { 0x06, 0x07, 0x0A, 0x0B, 0x0E, 0x0F },
        _ => Array.Empty<byte>()
    };
    public static string OperatorLabel(byte opcode) => Operator(opcode) ?? $"0x{opcode:X2}";

    // Integer-only grammar. PUSHV must use the signed 32-bit format nibble
    // (5), with one element. Legacy AiVariable.TypeId stores the descriptor's
    // second dword; it is NOT the VM numeric type. Float tags change DIV/MUL
    // semantics, so they must never be silently formatted as integer formulas.
    static bool ScalarInt(AiScriptFile script, ushort index) => index < script.Variables.Count
        && (script.Variables[index].Storage >> 4) == 5
        && script.Variables[index].Storage is 0x52 or 0x56
        && script.Variables[index].TypeId == 1;

    static string? Operator(byte opcode) => opcode switch
    {
        0x01 => "||", 0x02 => "&&", 0x03 => "|", 0x04 => "^", 0x05 => "&",
        0x06 => "==", 0x07 => "!=", 0x0A => ">", 0x0B => "<", 0x0E => ">=", 0x0F => "<=",
        0x12 => "<<", 0x13 => ">>", 0x14 => "+", 0x15 => "-", 0x16 => "*", 0x17 => "/", 0x18 => "%",
        _ => null
    };

    // Read-side contracts only. Do not reuse the contradictory shared labels
    // for fields 0/2 or confuse the boolean 119/11B predicates with HP values.
    static string? IntegerProperty(ushort field) => field switch
    {
        0x0000 => "HP.current", 0x0002 => "HP.max", 0x0013 => "OD.current", 0x0014 => "OD.capacity",
        0x00A6 => "HP.lastDamage", 0x00E8 => "turnDelay", 0x0119 => "HP.belowHalf", 0x011B => "HP.full",
        _ => null
    };

    public static IReadOnlyList<AiIntegerExpressionDescriptor> Detect(AiScriptFile script)
    {
        var result = new List<AiIntegerExpressionDescriptor>();
        if (script == null || !script.HasScript || !script.CodeWalkClosedExactly || script.UnknownOpcodes.Count != 0) return result;
        var instructions = script.Instructions;
        var owners = AiScript_File.InstructionOwners(script);
        var targets = script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets))
            .Select(o => o + script.ScriptStart).ToHashSet();
        var boundaries = instructions.Select(i => i.Offset).ToHashSet();
        byte[] serialized = AiScript_File.Write(script);
        // AiScriptFile currently exposes one variable/constant table. Native
        // accessors use the current worker descriptor. A heterogeneous layout
        // needs a per-worker codec contract before these names/values can edit it.
        if (script.Workers.Count > 1)
        {
            int first = script.Workers[0].DescriptorOffset;
            if (first < 0 || (long)first + 0x20 > serialized.Length
                || script.Workers.Any(w => w.DescriptorOffset < 0 || (long)w.DescriptorOffset + 0x20 > serialized.Length
                    || !serialized.AsSpan(w.DescriptorOffset + 0x14, 12).SequenceEqual(serialized.AsSpan(first + 0x14, 12)))) return result;
        }
        string hash = Convert.ToHexString(SHA256.HashData(serialized));
        var poolReferences = instructions.Where(i => i.Opcode == 0xAD).GroupBy(i => i.Operand)
            .ToDictionary(g => g.Key, g => g.Count());
        var dedicated = new HashSet<int>();
        var dedicatedOperators = new HashSet<int>();
        if (AiSeymourHandoffThresholdWriter.TryDetect(script, out var seymour))
            dedicated.UnionWith(new[] { seymour!.FirstOffset, seymour.SecondOffset, seymour.ThirdOffset });
        if (AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var anima, out _))
        {
            dedicated.UnionWith(anima.SelectMany(a => new[] { a.MaximumInstructionOffset, a.AttackInstructionOffset }));
            dedicatedOperators.UnionWith(anima.SelectMany(a => new[] { a.ComparisonInstructionOffset, a.AttackInstructionOffset + 3 }));
        }

        Node? Parse(ref int cursor, int depth, int end)
        {
            if (cursor < 0 || depth > 32 || end - cursor > 128) return null;
            int index = cursor--;
            var instruction = instructions[index];
            if (instruction.HasOperand != ((instruction.Opcode & 0x80) != 0)) return null;
            if (instruction.Opcode == 0xAE)
            {
                int value = unchecked((short)instruction.Operand);
                return new Node(index, index, value.ToString(System.Globalization.CultureInfo.InvariantCulture), false,
                    new[] { new AiIntegerExpressionLiteral(instruction.Offset, value, short.MinValue, short.MaxValue) });
            }
            if (instruction.Opcode == 0x9F && ScalarInt(script, instruction.Operand))
                return new Node(index, index, script.Variables[instruction.Operand].Name, false, Array.Empty<AiIntegerExpressionLiteral>());
            if (instruction.Opcode == 0xAD)
            {
                long offset = (long)script.IntPoolOffset + instruction.Operand * 4L;
                if (script.IntPoolOffset < 0 || offset < script.IntPoolOffset || offset + 4 > script.FloatPoolOffset
                    || offset + 4 > serialized.Length) return null;
                int value = BitConverter.ToInt32(serialized, (int)offset);
                // Pools can be shared across unrelated expressions/workers.
                // A shared slot is readable, but never an independent field.
                IReadOnlyList<AiIntegerExpressionLiteral> literals = poolReferences[instruction.Operand] == 1
                    ? new[] { new AiIntegerExpressionLiteral(instruction.Offset, value, int.MinValue, int.MaxValue, (int)offset) }
                    : Array.Empty<AiIntegerExpressionLiteral>();
                return new Node(index, index, value.ToString(System.Globalization.CultureInfo.InvariantCulture), false, literals);
            }
            if (instruction.Opcode == 0xB5 && instruction.Operand is 0x00A9 or 0x7019)
                return new Node(index, index, instruction.Operand == 0x00A9 ? "random()" : "usedCommand()", false, Array.Empty<AiIntegerExpressionLiteral>());
            if (instruction.Opcode == 0xB5 && instruction.Operand == 0x700F && cursor >= 1
                && instructions[cursor].Opcode == 0xAE && instructions[cursor - 1].Opcode == 0xAE
                && IntegerProperty(instructions[cursor].Operand) is string property)
            {
                ushort actor = instructions[cursor - 1].Operand;
                // Groups can aggregate properties. Only a single actor or Self
                // is described here; the other actor selector forms stay opaque.
                if (actor != 0xFFF3 && actor > 31) return null;
                cursor -= 2;
                string target = actor == 0xFFF3 ? "Self" : $"actor[{actor}]";
                return new Node(index - 2, index, $"{target}.{property}", false, Array.Empty<AiIntegerExpressionLiteral>());
            }
            if (instruction.Opcode is 0x19 or 0x1C)
            {
                var operand = Parse(ref cursor, depth + 1, end);
                return operand == null ? null : new Node(operand.StartIndex, index,
                    $"{(instruction.Opcode == 0x19 ? "!" : "~")}({operand.Text})", true, operand.Literals, operand.OperatorNodes);
            }
            if (Operator(instruction.Opcode) is not string op) return null;
            var right = Parse(ref cursor, depth + 1, end);
            var left = Parse(ref cursor, depth + 1, end);
            if (left == null || right == null) return null;
            var rightLiterals = right.Literals;
            if (instruction.Opcode is 0x17 or 0x18 or 0x12 or 0x13)
            {
                // A computed divisor/shift has an unproven range. Only literal
                // positive divisors and 0..31 shifts have this write contract.
                if (right.StartIndex != right.EndIndex || right.Literals.Count != 1
                    || instructions[right.StartIndex].Opcode is not (0xAE or 0xAD)) return null;
                bool shift = instruction.Opcode is 0x12 or 0x13;
                var literal = right.Literals[0] with { Minimum = shift ? 0 : 1, Maximum = shift ? 31 : right.Literals[0].Maximum };
                if (literal.Value < literal.Minimum || literal.Value > literal.Maximum) return null;
                rightLiterals = new[] { literal };
            }
            var operators = left.OperatorNodes.Concat(right.OperatorNodes).ToList();
            if (AllowedOperators(instruction.Opcode).Count > 0)
                operators.Add(new AiIntegerExpressionOperator(instruction.Offset, instruction.Opcode));
            return new Node(left.StartIndex, index, $"({left.Text} {op} {right.Text})", true,
                left.Literals.Concat(rightLiterals).ToArray(), operators);
        }

        for (int sinkIndex = 1; sinkIndex < instructions.Count; sinkIndex++)
        {
            var sink = instructions[sinkIndex];
            if (sink.Opcode is not (0xA0 or 0xD6 or 0xD7) || !sink.HasOperand
                || !owners.TryGetValue(sink.Offset, out var ownerIds) || ownerIds.Count != 1) continue;
            if (sink.Opcode == 0xA0 && !ScalarInt(script, sink.Operand)) continue;
            int cursor = sinkIndex - 1;
            var expression = Parse(ref cursor, 0, sinkIndex);
            if (expression == null || !expression.Compound || (expression.Literals.Count == 0 && expression.OperatorNodes.Count == 0)
                || expression.Literals.Any(l => dedicated.Contains(l.Offset))
                || expression.OperatorNodes.Any(o => dedicatedOperators.Contains(o.Offset))) continue;
            if (Enumerable.Range(expression.StartIndex, sinkIndex - expression.StartIndex + 1).Any(i =>
                !owners.TryGetValue(instructions[i].Offset, out var ids) || ids.Count != 1 || ids[0] != ownerIds[0]
                || (i > expression.StartIndex && (targets.Contains(instructions[i].Offset)
                    || instructions[i].Offset != instructions[i - 1].Offset + instructions[i - 1].Length)))) continue;
            string destination;
            if (sink.Opcode == 0xA0) destination = script.Variables[sink.Operand].Name;
            else
            {
                var worker = script.Workers.Single(w => w.Index == ownerIds[0]);
                if (sink.Operand >= worker.JumpTargets.Count) continue;
                int target = worker.JumpTargets[sink.Operand] + script.ScriptStart;
                if (!boundaries.Contains(target)) continue;
                destination = $"{(sink.Opcode == 0xD6 ? "true" : "false")} → 0x{target:X4}";
                // Existing simple guards already have an editor. CASE provenance
                // is intentionally left to AiNativeConditionWriter.
                if (sinkIndex - expression.StartIndex == 3
                    && ((instructions[expression.StartIndex].Opcode == 0xAE && instructions[expression.StartIndex + 1].Opcode == 0x9F)
                        || (instructions[expression.StartIndex].Opcode == 0x9F && instructions[expression.StartIndex + 1].Opcode == 0xAE))) continue;
            }
            result.Add(new AiIntegerExpressionDescriptor(instructions[expression.StartIndex].Offset, sink.Offset,
                ownerIds[0], expression.Text, destination, expression.Literals, hash, expression.OperatorNodes));
        }
        return result;
    }
    public static bool TryApply(AiScriptFile script, AiIntegerExpressionDescriptor expected,
        AiIntegerExpressionEdit request, out byte[]? edited, out string error)
    {
        edited = null;
        error = string.Empty;
        var live = expected == null ? null : Detect(script).SingleOrDefault(row => row.StartOffset == expected.StartOffset
            && row.SinkOffset == expected.SinkOffset && row.WorkerIndex == expected.WorkerIndex && row.SourceSha256 == expected.SourceSha256);
        if (live == null || request == null || (!request.LiteralOffset.HasValue && !request.OperatorOffset.HasValue)
            || request.LiteralOffset.HasValue != request.Value.HasValue || request.OperatorOffset.HasValue != request.Opcode.HasValue)
        {
            error = Strings.AiAdvancedConditionChanged;
            return false;
        }
        byte[] output = AiScript_File.Write(script);
        if (request.LiteralOffset.HasValue)
        {
            var literal = live.Literals.SingleOrDefault(l => l.Offset == request.LiteralOffset.Value);
            if (literal == null)
            {
                error = Strings.AiAdvancedConditionChanged;
                return false;
            }
            int value = request.Value!.Value;
            if (value < literal.Minimum || value > literal.Maximum)
            {
                error = string.Format(Strings.AiAdvancedExpressionRange, literal.Minimum, literal.Maximum);
                return false;
            }
            int valueOffset = literal.PoolOffset ?? (literal.Offset + 1);
            int width = literal.PoolOffset.HasValue ? 4 : 2;
            if (literal.Offset < script.ScriptStart || literal.Offset + 2 >= output.Length
                || output[literal.Offset] != (literal.PoolOffset.HasValue ? 0xAD : 0xAE)
                || valueOffset < 0 || (long)valueOffset + width > output.Length)
            {
                error = Strings.AiAdvancedConditionBounds;
                return false;
            }
            for (int i = 0; i < width; i++) output[valueOffset + i] = (byte)(value >> (i * 8));
        }
        if (request.OperatorOffset.HasValue)
        {
            var operation = live.Operators.SingleOrDefault(o => o.Offset == request.OperatorOffset.Value);
            if (operation == null || !AllowedOperators(operation.Opcode).Contains(request.Opcode!.Value))
            {
                error = Strings.AiAdvancedConditionRange;
                return false;
            }
            output[operation.Offset] = request.Opcode.Value;
        }
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(output, script, output.Length, out _, out error)) return false;
        edited = output;
        return true;
    }

    public static bool TryApply(AiScriptFile script, AiIntegerExpressionDescriptor expected, int literalOffset,
        int value, out byte[]? edited, out string error) => TryApply(script, expected,
            new AiIntegerExpressionEdit(literalOffset, value), out edited, out error);
}
