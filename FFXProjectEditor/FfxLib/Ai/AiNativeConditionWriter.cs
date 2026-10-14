using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record AiNativeConditionDescriptor(
    int Offset, ushort VariableIndex, string VariableName, AiVarCompareOperator Operator,
    int Value, int WorkerIndex, int BranchOffset, int BranchTargetOffset,
    bool UsesSwitchRegister = false, bool BranchWhenTrue = false, int SwitchLoadOffset = -1);

/// <summary>Edits proven variable/immediate guards without relocating the native control flow.</summary>
public static class AiNativeConditionWriter
{
    public static IReadOnlyList<AiNativeConditionDescriptor> Detect(AiScriptFile script)
    {
        var rows = new List<AiNativeConditionDescriptor>();
        if (!script.HasScript || !script.CodeWalkClosedExactly) return rows;
        var owners = AiScript_File.InstructionOwners(script);
        var targets = script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets))
            .Select(offset => offset + script.ScriptStart).ToHashSet();
        var boundaries = script.Instructions.Select(i => i.Offset).ToHashSet();
        for (int i = 0; i + 3 < script.Instructions.Count; i++)
        {
            if (!AiVarConditionBuilder.TryReadImmediateComparison(script.Instructions, i, out var clause)
                || clause.VariableIndex >= script.Variables.Count) continue;
            var start = script.Instructions[i];
            var branch = script.Instructions[i + 3];
            if (branch.Opcode != 0xD7 || !branch.HasOperand
                || !script.Instructions[i].HasOperand || !script.Instructions[i + 1].HasOperand
                || script.Instructions[i + 2].HasOperand
                || !owners.TryGetValue(start.Offset, out var ownerIds) || ownerIds.Count != 1) continue;
            if (Enumerable.Range(i + 1, 3).Any(j => targets.Contains(script.Instructions[j].Offset)
                || script.Instructions[j].Offset != script.Instructions[j - 1].Offset + script.Instructions[j - 1].Length)) continue;
            var worker = script.Workers.Single(w => w.Index == ownerIds[0]);
            if (branch.Operand >= worker.JumpTargets.Count) continue;
            int destination = script.ScriptStart + worker.JumpTargets[branch.Operand];
            if (!boundaries.Contains(destination)) continue;
            rows.Add(new AiNativeConditionDescriptor(start.Offset, clause.VariableIndex,
                script.Variables[clause.VariableIndex].Name, clause.Operator, clause.Value,
                worker.Index, branch.Offset, destination));
        }
        AddSwitchConditions(script, owners, targets, boundaries, rows);
        return rows.OrderBy(row => row.Offset).ToArray();
    }

    static void AddSwitchConditions(AiScriptFile script, IReadOnlyDictionary<int, IReadOnlyList<int>> owners,
        HashSet<int> targets, HashSet<int> boundaries, List<AiNativeConditionDescriptor> rows)
    {
        var instructions = script.Instructions;
        var indexes = instructions.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
        foreach (var worker in script.Workers)
        {
            // Prove the CASE register's provenance through CFG predecessors.
            // A nearby SWITCH in file order is insufficient: another branch or
            // entrypoint could enter the case table with a different register.
            var incoming = new Dictionary<int, HashSet<int>>();
            void Edge(int from, int to)
            {
                if (!incoming.TryGetValue(to, out var sources)) incoming[to] = sources = new HashSet<int>();
                sources.Add(from);
            }
            foreach (int entry in worker.Entrypoints) Edge(-1, script.ScriptStart + entry);
            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (!owners.TryGetValue(instruction.Offset, out var ids) || !ids.Contains(worker.Index)) continue;
                if (instruction.Opcode is 0xB0 or 0xD6 or 0xD7 && instruction.Operand < worker.JumpTargets.Count)
                    Edge(instruction.Offset, script.ScriptStart + worker.JumpTargets[instruction.Operand]);
                if (instruction.Opcode is not (0xB0 or 0x3C or 0x40) && i + 1 < instructions.Count)
                    Edge(instruction.Offset, instructions[i + 1].Offset);
            }
            bool OnlyFrom(int offset, int predecessor) => incoming.TryGetValue(offset, out var sources)
                && sources.Count == 1 && sources.Contains(predecessor);
            bool Owned(int offset) => owners.TryGetValue(offset, out var ids) && ids.Count == 1 && ids[0] == worker.Index;

            for (int i = 0; i + 2 < instructions.Count; i++)
            {
                var load = instructions[i];
                var saveRegister = instructions[i + 1];
                var jump = instructions[i + 2];
                if (load.Opcode != 0x9F || !load.HasOperand || load.Operand >= script.Variables.Count
                    || saveRegister.Opcode != 0x2C || saveRegister.HasOperand
                    || jump.Opcode != 0xB0 || !jump.HasOperand || jump.Operand >= worker.JumpTargets.Count
                    || !Owned(load.Offset) || !OnlyFrom(saveRegister.Offset, load.Offset) || !OnlyFrom(jump.Offset, saveRegister.Offset)
                    || saveRegister.Offset != load.Offset + 3 || jump.Offset != saveRegister.Offset + 1) continue;
                int first = script.ScriptStart + worker.JumpTargets[jump.Operand];
                if (!indexes.TryGetValue(first, out int cursor)) continue;
                int previous = jump.Offset;
                while (cursor + 3 < instructions.Count)
                {
                    var literal = instructions[cursor];
                    var pushRegister = instructions[cursor + 1];
                    var comparison = instructions[cursor + 2];
                    var branch = instructions[cursor + 3];
                    if (literal.Opcode != 0xAE || !literal.HasOperand || pushRegister.Opcode != 0x29 || pushRegister.HasOperand
                        || comparison.HasOperand || !AiVarConditionBuilder.TryGetComparisonOperator(comparison.Opcode, true, out var op)
                        || branch.Opcode != 0xD6 || !branch.HasOperand || branch.Operand >= worker.JumpTargets.Count
                        || !OnlyFrom(literal.Offset, previous) || !Owned(literal.Offset)
                        || pushRegister.Offset != literal.Offset + 3 || comparison.Offset != pushRegister.Offset + 1
                        || branch.Offset != comparison.Offset + 1
                        || targets.Contains(pushRegister.Offset) || targets.Contains(comparison.Offset) || targets.Contains(branch.Offset)) break;
                    int destination = script.ScriptStart + worker.JumpTargets[branch.Operand];
                    if (!boundaries.Contains(destination)) break;
                    rows.Add(new AiNativeConditionDescriptor(literal.Offset, load.Operand,
                        script.Variables[load.Operand].Name, op, unchecked((short)literal.Operand), worker.Index,
                        branch.Offset, destination, true, true, load.Offset));
                    previous = branch.Offset;
                    cursor += 4;
                }
            }
        }
    }

    public static bool TryApply(AiScriptFile script, AiNativeConditionDescriptor expected,
        AiVarCompareOperator comparison, int value, out byte[]? editedAi, out string error)
    {
        editedAi = null;
        error = string.Empty;
        if (expected == null || !Detect(script).Contains(expected))
        {
            error = Strings.AiAdvancedConditionChanged;
            return false;
        }
        if (!Enum.IsDefined(comparison) || value < short.MinValue || value > short.MaxValue)
        {
            error = Strings.AiAdvancedConditionRange;
            return false;
        }
        // Keep the native operand order, including GT/LE variants. Normalizing
        // every expression through the emitter would turn a no-op into a patch
        // and prevent byte-exact restoration of the original comparison.
        byte[] output = AiScript_File.Write(script);
        int expressionLength = expected.UsesSwitchRegister ? 5 : 7;
        if (expected.Offset < script.ScriptStart || expected.Offset > output.Length - expressionLength)
        {
            error = Strings.AiAdvancedConditionBounds;
            return false;
        }
        bool reversed = output[expected.Offset] == 0xAE;
        byte opcode = Enumerable.Range(0x06, 10).Select(raw => (byte)raw).Single(raw =>
            AiVarConditionBuilder.TryGetComparisonOperator(raw, reversed, out var decoded) && decoded == comparison);
        int literalOffset = expected.Offset + (reversed ? 0 : 3);
        ushort encoded = unchecked((ushort)(short)value);
        output[literalOffset + 1] = (byte)encoded;
        output[literalOffset + 2] = (byte)(encoded >> 8);
        output[expected.BranchOffset - 1] = opcode;
        editedAi = output;
        return true;
    }
}
