using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record AiSeymourHandoffThresholdDescriptor(
    int WorkerIndex, int FirstOffset, int SecondOffset, int ThirdOffset,
    int First, int Second, int Third, string SourceSha256);

/// <summary>Bounded authoring of m124's three calculated HP thresholds.</summary>
public static class AiSeymourHandoffThresholdWriter
{
    public static bool TryDetect(AiScriptFile script, out AiSeymourHandoffThresholdDescriptor? descriptor)
    {
        descriptor = null;
        if (script == null || !script.HasScript || !script.CodeWalkClosedExactly || script.UnknownOpcodes.Count != 0 || script.Workers.Count != 1)
            return false;
        var ins = script.Instructions;
        var worker = script.Workers[0];
        var owners = AiScript_File.InstructionOwners(script);
        var targets = worker.Entrypoints.Concat(worker.JumpTargets).Select(o => o + script.ScriptStart).ToHashSet();
        int Var(byte storage, int slot)
        {
            var matches = script.Variables.Where(v => v.Storage == storage && v.Slot == slot && v.TypeId == 1).ToArray();
            return matches.Length == 1 ? matches[0].Index : -1;
        }
        int threshold = Var(0x56, 0x34), hp = Var(0x56, 0x10), phase = Var(0x52, 4), sentinel = Var(0x52, 0x14);
        if (new[] { threshold, hp, phase, sentinel }.Any(v => v < 0 || v > ushort.MaxValue)) return false;
        ushort t = (ushort)threshold, h = (ushort)hp, p = (ushort)phase, s = (ushort)sentinel;
        bool Match(int start, params (byte Op, ushort? Operand)[] pattern)
        {
            if (start < 0 || start + pattern.Length > ins.Count) return false;
            for (int j = 0; j < pattern.Length; j++)
            {
                var i = ins[start + j];
                if (i.Opcode != pattern[j].Op || i.HasOperand != ((i.Opcode & 0x80) != 0)
                    || (pattern[j].Operand is ushort operand && i.Operand != operand)
                    || !owners.TryGetValue(i.Offset, out var ids) || ids.Count != 1 || ids[0] != worker.Index
                    || (j > 0 && (targets.Contains(i.Offset) || i.Offset != ins[start + j - 1].Offset + ins[start + j - 1].Length)))
                    return false;
            }
            return true;
        }
        int[] Find(params (byte Op, ushort? Operand)[] pattern) => Enumerable.Range(0, ins.Count)
            .Where(i => Match(i, pattern)).ToArray();
        int Destination(int i) => ins[i].Operand < worker.JumpTargets.Count
            ? script.ScriptStart + worker.JumpTargets[ins[i].Operand] : -1;
        bool Linear(int start, int end) => start >= 0 && end < ins.Count && start <= end
            && Enumerable.Range(start, end - start + 1).All(i =>
                ins[i].Opcode is not (0xB0 or 0xD6 or 0xD7 or 0x3C or 0x40)
                && (i == start || (!targets.Contains(ins[i].Offset) && ins[i].Offset == ins[i - 1].Offset + ins[i - 1].Length)));

        // Keep integer DIV before MUL, the fixed denominator, and the same
        // private variable. Labels from AiChrPropertyNames are not proof of HP.
        // PE read(2) -> 79AE00 -> actor+594. See the 2026-09-20 handoff map.
        var formulas = Find((0xAE, 0xFFF3), (0xAE, 2), (0xB5, 0x700F), (0xAE, 6),
            (0x17, null), (0xAE, null), (0x16, null), (0xA0, t));
        if (formulas.Length != 3) return false;
        var conditions = new int[3];
        for (ushort stage = 0; stage < 3; stage++)
        {
            var found = Find((0x9F, h), (0x9F, t), (0x0F, null), (0x9F, p),
                (0xAE, stage), (0x06, null), (0x02, null), (0xD7, null));
            if (found.Length != 1) return false;
            conditions[stage] = found[0];
        }
        var reads = Find((0xAE, 0xFFF3), (0xAE, 0), (0xB5, 0x700F), (0xA0, h));
        var stops = Find((0xAE, 128), (0xA0, s), (0xAE, 0), (0xA0, t));
        var handoffs = Find((0xAE, 23), (0xAE, 0x6001), (0xD8, 0x705A))
            .Where(i => i + 3 < ins.Count && ins[i + 3].Opcode == 0x3C).ToArray();
        if (reads.Length != 1 || stops.Length != 1 || handoffs.Length != 1
            || !Match(reads[0] - 5, (0x9F, s), (0xAE, 0), (0x07, null), (0xD7, null), (0x3C, null))
            || Destination(reads[0] - 2) != ins[reads[0]].Offset
            || !Match(conditions[0] - 5, (0x9F, h), (0x9F, t), (0x0A, null), (0xD7, null), (0x3C, null))
            || Destination(conditions[0] - 2) != ins[conditions[0]].Offset
            || conditions[2] + 8 != stops[0]) return false;

        // The two intermediate stages write the seven character layout selectors,
        // then compute the next limit and fall through to its consumer. This is
        // a control/data-flow contract, not the visual order of route cards.
        for (ushort stage = 0; stage < 2; stage++)
        {
            int cursor = conditions[stage] + 8;
            if (!Match(cursor, (0xAE, (ushort)(stage + 1)), (0xA0, p))) return false;
            cursor += 2;
            for (ushort actor = 0; actor < 7; actor++, cursor += 4)
                if (!Match(cursor, (0xAE, actor), (0xAE, 0x1C), (0xAE, (ushort)(stage + 1)), (0xD8, 0x7018))) return false;
            if (cursor != formulas[stage + 1] || cursor + 8 != conditions[stage + 1]
                || !Linear(conditions[stage] + 8, cursor + 7)
                || Destination(conditions[stage] + 7) != ins[conditions[stage + 1]].Offset) return false;
        }
        if (!Linear(stops[0], handoffs[0] + 2)
            || Destination(conditions[2] + 7) != ins[handoffs[0] + 3].Offset
            || worker.Entrypoints.Count == 0
            || !Linear(ins.ToList().FindIndex(i => i.Offset == script.ScriptStart + worker.Entrypoints[0]), formulas[0] + 7)) return false;
        var stores = ins.Where(i => i.Opcode == 0xA0 && i.Operand == t).Select(i => i.Offset).ToHashSet();
        if (!stores.SetEquals(formulas.Select(i => ins[i + 7].Offset).Append(ins[stops[0] + 3].Offset))
            || ins.Count(i => i.Opcode == 0xA0 && i.Operand == h) != 1) return false;
        int first = ins[formulas[0] + 5].Operand, second = ins[formulas[1] + 5].Operand, third = ins[formulas[2] + 5].Operand;
        if (!ValidOrder(first, second, third)) return false;
        descriptor = new AiSeymourHandoffThresholdDescriptor(worker.Index,
            ins[formulas[0] + 5].Offset, ins[formulas[1] + 5].Offset, ins[formulas[2] + 5].Offset,
            first, second, third, Convert.ToHexString(SHA256.HashData(AiScript_File.Write(script))));
        return true;
    }

    static bool ValidOrder(int first, int second, int third) => 6 > first && first > second && second > third && third >= 1;

    public static bool TryApply(AiScriptFile script, AiSeymourHandoffThresholdDescriptor expected,
        int first, int second, int third, out byte[]? output, out string error)
    {
        output = null;
        error = string.Empty;
        if (!TryDetect(script, out var live) || live != expected)
        {
            error = Strings.AiAdvancedConditionChanged;
            return false;
        }
        if (!ValidOrder(first, second, third))
        {
            error = Strings.AiAdvancedSeymourRange;
            return false;
        }
        byte[] edited = AiScript_File.Write(script);
        foreach (var (offset, value) in new[] { (live!.FirstOffset, first), (live.SecondOffset, second), (live.ThirdOffset, third) })
        {
            if (offset < script.ScriptStart || offset + 2 >= edited.Length || edited[offset] != 0xAE)
            {
                error = Strings.AiAdvancedConditionBounds;
                return false;
            }
            edited[offset + 1] = (byte)value;
            edited[offset + 2] = 0;
        }
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(edited, script, edited.Length, out _, out error)) return false;
        output = edited;
        return true;
    }
}
