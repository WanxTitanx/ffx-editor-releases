using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record AiBattleSceneOption(int Selector, int WorkerIndex, int EntrypointIndex, int EntrypointOffset);
public sealed record AiBattleSceneDescriptor(int CallOffset, int LiteralOffset, ushort FunctionId, int Selector,
    IReadOnlyList<AiBattleSceneOption> Options, string SourceSha256, string EncounterSha256);

/// <summary>Scene routing bound to a supplied encounter's real WorkerFile map.</summary>
public static class AiBattleSceneWriter
{
    public static IReadOnlyList<AiBattleSceneDescriptor> Detect(AiScriptFile script, int monsterNumber, byte[] encounter)
    {
        var rows = new List<AiBattleSceneDescriptor>();
        if (script == null || !script.HasScript || !script.CodeWalkClosedExactly || script.UnknownOpcodes.Count != 0
            || !TryReadOptions(encounter, monsterNumber, out var options)) return rows;
        var owners = AiScript_File.InstructionOwners(script);
        var targets = script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets))
            .Select(o => o + script.ScriptStart).ToHashSet();
        string sourceHash = Hash(AiScript_File.Write(script)), encounterHash = Hash(encounter);
        for (int i = 1; i < script.Instructions.Count; i++)
        {
            var call = script.Instructions[i];
            var literal = script.Instructions[i - 1];
            if (call.Opcode != 0xD8 || !call.HasOperand || call.Operand is not (0x703C or 0x7097)
                || literal.Opcode != 0xAE || !literal.HasOperand || literal.Offset + 3 != call.Offset
                || targets.Contains(call.Offset)
                || !owners.TryGetValue(call.Offset, out var callOwners) || callOwners.Count != 1
                || !owners.TryGetValue(literal.Offset, out var literalOwners) || literalOwners.Count != 1
                || callOwners[0] != literalOwners[0] || !options.Any(o => o.Selector == literal.Operand)) continue;
            rows.Add(new AiBattleSceneDescriptor(call.Offset, literal.Offset, call.Operand, literal.Operand,
                options, sourceHash, encounterHash));
        }
        return rows;
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static bool TryReadOptions(byte[] bytes, int monster, out IReadOnlyList<AiBattleSceneOption> options)
    {
        options = Array.Empty<AiBattleSceneOption>();
        if (bytes == null || bytes.Length < 20 || monster < 0 || monster > 0xFFF) return false;
        bool Range(int offset, long length) => offset >= 0 && length >= 0 && (long)offset + length <= bytes.Length;
        int I32(int offset) => BitConverter.ToInt32(bytes, offset);
        int U16(int offset) => BitConverter.ToUInt16(bytes, offset);
        int chunks = I32(0) - 1;
        if (chunks < 3 || chunks > 64 || !Range(4, chunks * 4L)) return false;
        int aiStart = I32(4), workerStart = I32(8), formation = I32(12);
        int formationEnd = chunks > 3 ? I32(16) : bytes.Length;
        if (aiStart < 4 + chunks * 4 || workerStart <= aiStart || formation <= workerStart
            || !Range(aiStart, workerStart - (long)aiStart) || !Range(workerStart, formation - (long)workerStart)
            || formationEnd < formation + 28L || !Range(formation, formationEnd - (long)formation)
            || !Enumerable.Range(0, 8).Any(i => U16(formation + 12 + i * 2) != 0xFFFF
                && (U16(formation + 12 + i * 2) & 0xFFF) == monster)) return false;
        int workerLength = formation - workerStart;
        if (workerLength < 2) return false;
        int count = bytes[workerStart], preLength = bytes[workerStart + 1];
        int records = preLength + (preLength % 2 == 0 ? 2 : 3);
        // Native 7981F0 sends selectors 0..7 to context (2,1), slot 62.
        // 797420 reads pre[slot], the indexed record and its u16 event map.
        // Selectors 8..23 use slot 63 and optional secondary context; no fallback
        // to the supplied encounter can prove those, so this writer excludes them.
        if (preLength <= 62 || records + count * 4 > workerLength) return false;
        int recordIndex = bytes[workerStart + 2 + 62];
        if (recordIndex == 255 || recordIndex >= count) return false;
        int record = workerStart + records + recordIndex * 4;
        int workerIndex = bytes[record], type = bytes[record + 1], mapOffset = U16(record + 2);
        if (type != 4 || mapOffset > short.MaxValue || mapOffset < records + count * 4
            || mapOffset + 2 > workerLength) return false;
        int eventCount = U16(workerStart + mapOffset);
        if (mapOffset + 2L + eventCount * 2L > workerLength) return false;
        AiScriptFile sceneScript;
        try { sceneScript = AiScript_File.Read(bytes.AsSpan(aiStart, workerStart - aiStart).ToArray()); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException) { return false; }
        if (!sceneScript.CodeWalkClosedExactly || sceneScript.UnknownOpcodes.Count != 0 || workerIndex >= sceneScript.Workers.Count) return false;
        var worker = sceneScript.Workers[workerIndex];
        var choices = new List<AiBattleSceneOption>();
        for (int selector = 0; selector < Math.Min(8, eventCount); selector++)
        {
            int entry = U16(workerStart + mapOffset + 2 + selector * 2);
            if (entry == 0xFFFF) continue;
            if (entry >= worker.Entrypoints.Count) return false;
            int start = sceneScript.ScriptStart + worker.Entrypoints[entry];
            if (AllPathsClearGate(sceneScript, worker, start))
                choices.Add(new AiBattleSceneOption(selector, workerIndex, entry, start));
        }
        options = choices;
        return choices.Count > 0;
    }

    static bool AllPathsClearGate(AiScriptFile script, AiWorker worker, int start)
    {
        var indexes = script.Instructions.Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(x => x.Offset, x => x.index);
        var owners = AiScript_File.InstructionOwners(script);
        var countedLoopExits = CountedLoopExits(script, worker, indexes, owners);
        var root = (Offset: start, Cleared: false);
        var pending = new Queue<(int Offset, bool Cleared)>();
        var remaining = new Dictionary<(int, bool), int>();
        var reverse = new Dictionary<(int, bool), HashSet<(int, bool)>>();
        var valid = new HashSet<(int, bool)>();
        var ready = new Queue<(int, bool)>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var state))
        {
            if (remaining.ContainsKey(state)) continue;
            if (!indexes.TryGetValue(state.Offset, out int index)
                || !owners.TryGetValue(state.Offset, out var ids) || ids.Count != 1 || ids[0] != worker.Index) return false;
            var instruction = script.Instructions[index];
            remaining[state] = 0;
            bool clear = state.Cleared || instruction.Opcode == 0xD8 && instruction.Operand == 0x703D;
            if (instruction.Opcode == 0x3C)
            {
                if (!clear) return false;
                ready.Enqueue(state);
                continue;
            }
            if (instruction.Opcode is 0x3D or 0x3E or 0x3F or 0x40
                || instruction.Opcode == 0xD8 && instruction.Operand is 0x005F or 0x703C or 0x7097) return false;
            var next = new HashSet<(int, bool)>();
            if (instruction.Opcode is 0xB0 or 0xD6 or 0xD7)
            {
                if (!instruction.HasOperand || instruction.Operand >= worker.JumpTargets.Count) return false;
                next.Add((countedLoopExits.TryGetValue(instruction.Offset, out int loopExit)
                    ? loopExit : script.ScriptStart + worker.JumpTargets[instruction.Operand], clear));
            }
            if (instruction.Opcode != 0xB0)
            {
                if (index + 1 >= script.Instructions.Count) return false;
                next.Add((script.Instructions[index + 1].Offset, clear));
            }
            remaining[state] = next.Count;
            foreach (var successor in next)
            {
                if (!reverse.TryGetValue(successor, out var parents)) reverse[successor] = parents = new();
                parents.Add(state);
                pending.Enqueue(successor);
            }
        }
        // Least fixed point: a control-flow cycle cannot be approved just
        // because one other path eventually clears the scene gate.
        while (ready.TryDequeue(out var state))
        {
            if (!valid.Add(state) || !reverse.TryGetValue(state, out var parents)) continue;
            foreach (var parent in parents)
                if (--remaining[parent] == 0) ready.Enqueue(parent);
        }
        return valid.Contains(root);
    }

    static Dictionary<int, int> CountedLoopExits(AiScriptFile script, AiWorker worker,
        IReadOnlyDictionary<int, int> indexes, IReadOnlyDictionary<int, IReadOnlyList<int>> owners)
    {
        var result = new Dictionary<int, int>();
        var ins = script.Instructions;
        if (BitConverter.ToInt32(script.OriginalAiFileBytes, worker.DescriptorOffset + 0x14)
            != BitConverter.ToInt32(script.OriginalAiFileBytes, script.Workers[0].DescriptorOffset + 0x14)) return result;
        var externalEntries = script.Workers.SelectMany(w => w.Entrypoints).Select(o => o + script.ScriptStart).ToHashSet();
        var jumpTargets = script.Workers.SelectMany(w => w.JumpTargets).Select(o => o + script.ScriptStart).ToHashSet();
        var usedVariables = ins.Where(i => i.Opcode is 0x9F or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4
                && owners.TryGetValue(i.Offset, out var ids) && ids.Contains(worker.Index))
            .Select(i => (int)i.Operand).ToHashSet();
        bool Is(int i, byte op, ushort? value = null) => i >= 0 && i < ins.Count && ins[i].Opcode == op
            && ins[i].HasOperand == ((op & 0x80) != 0) && (!value.HasValue || ins[i].Operand == value.Value);
        int Target(int i) => ins[i].Operand < worker.JumpTargets.Count
            ? script.ScriptStart + worker.JumpTargets[ins[i].Operand] : -1;
        bool PrivateInt(ushort index) => index < script.Variables.Count && script.Variables[index].Storage == 0x56
            && script.Variables[index].TypeId == 1
            && script.Variables[index].Slot >= 0 && (long)script.Variables[index].Slot + 4 <= worker.PrivateDataLength
            // Storage 56 resolves through this worker's descriptor+2C at
            // native 86C2EB..86C318. Identical descriptors used only by another
            // worker are not aliases; overlapping slots in THIS worker are.
            && script.Variables.Count(v => usedVariables.Contains(v.Index) && v.Storage == 0x56
                && Math.Abs((long)v.Slot - script.Variables[index].Slot) < 4) == 1;

        for (int back = 0; back + 1 < ins.Count; back++)
        {
            if (!Is(back, 0xB0) || !indexes.TryGetValue(Target(back), out int head) || head >= back
                || !Is(head, 0x9F) || !Is(head + 1, 0xD7) || Target(head + 1) != ins[back + 1].Offset) continue;
            ushort flag = ins[head].Operand;
            if (!PrivateInt(flag)) continue;
            int step = -1;
            ushort counter = 0;
            for (int i = head + 2; i + 10 <= back; i++)
            {
                if (!Is(i, 0x9F) || !Is(i + 1, 0xAE, 1) || !Is(i + 2, 0x14)
                    || !Is(i + 3, 0xA0, ins[i].Operand) || !Is(i + 4, 0x9F, ins[i].Operand)
                    || !Is(i + 5, 0xAE) || ins[i + 5].Operand == 0 || ins[i + 5].Operand > short.MaxValue || !Is(i + 6, 0x06)
                    || !Is(i + 7, 0xD7) || Target(i + 7) != ins[i + 10].Offset
                    || !Is(i + 8, 0xAE, 0) || !Is(i + 9, 0xA0, flag)) continue;
                if (step != -1) { step = -2; break; }
                step = i; counter = ins[i].Operand;
            }
            if (step < 0 || counter == flag || !PrivateInt(counter)) continue;
            int Seed(ushort variable, ushort value)
            {
                var matches = Enumerable.Range(Math.Max(0, head - 20), Math.Min(head, 20))
                    .Where(i => Is(i, 0xAE, value) && Is(i + 1, 0xA0, variable)).ToArray();
                return matches.Length == 1 ? matches[0] : -1;
            }
            int flagSeed = Seed(flag, 1), counterSeed = Seed(counter, 0);
            if (flagSeed < 0 || counterSeed < 0) continue;
            int firstSeed = Math.Min(flagSeed, counterSeed);
            bool safe = true;
            for (int i = firstSeed; i <= back && safe; i++)
            {
                if (!owners.TryGetValue(ins[i].Offset, out var ids) || ids.Count != 1 || ids[0] != worker.Index
                    || (i > firstSeed && ins[i].Offset != ins[i - 1].Offset + ins[i - 1].Length)
                    || externalEntries.Contains(ins[i].Offset)
                    || (i > firstSeed && i < head && jumpTargets.Contains(ins[i].Offset))) { safe = false; break; }
                if (ins[i].Opcode == 0xA0 && ins[i].Operand == counter && i != counterSeed + 1 && i != step + 3) safe = false;
                if (ins[i].Opcode == 0xA0 && ins[i].Operand == flag && i != flagSeed + 1 && i != step + 9) safe = false;
                if (ins[i].Opcode is 0xA1 or 0xA3 or 0xA4) safe = false;
                if (i < head && ins[i].Opcode is 0xB0 or 0xD6 or 0xD7 or 0x3C or 0x40) safe = false;
                if (i >= head && i != head + 1 && i != back)
                {
                    // The proved Omnis loops call only setActorLight (4013,
                    // six stack arguments, native A78030) and wait. No nested
                    // script requests or unknown callbacks can reset the count.
                    if (ins[i].Opcode == 0xB5 || (ins[i].Opcode == 0xD8 && ins[i].Operand is not (0x4013 or 0x0000))
                        || ins[i].Opcode is 0x3C or 0x3D or 0x3E or 0x3F or 0x40) safe = false;
                    if (ins[i].Opcode is 0xB0 or 0xD6 or 0xD7)
                    {
                        int target = Target(i);
                        if (target <= ins[i].Offset || target > ins[back].Offset
                            || (i < step && target > ins[step].Offset)) safe = false;
                    }
                }
            }
            // No other branch or entrypoint may enter a loop after its seeds.
            foreach (var instruction in ins)
            {
                if (!safe) break;
                if (instruction.Offset >= ins[head].Offset && instruction.Offset <= ins[back].Offset) continue;
                if (instruction.Opcode is not (0xB0 or 0xD6 or 0xD7)) continue;
                if (!owners.TryGetValue(instruction.Offset, out var ids) || !ids.Contains(worker.Index)) continue;
                int destination = instruction.Operand < worker.JumpTargets.Count
                    ? script.ScriptStart + worker.JumpTargets[instruction.Operand] : -1;
                if (destination >= ins[head].Offset && destination <= ins[back].Offset) safe = false;
            }
            if (safe)
            {
                // Starting at zero, every trip increments once before the back
                // edge; equality with positive N clears the only loop flag.
                // Its exit is therefore reached after N trips (N <= 32767),
                // under the native worker's serial execution. Summarize that
                // edge only; unproved cycles remain in the cleanup fixed point.
                result[ins[back].Offset] = ins[back + 1].Offset;
            }
        }
        return result;
    }
    public static bool TryApply(AiScriptFile script, int monsterNumber, byte[] encounter,
        AiBattleSceneDescriptor expected, int selector, out byte[]? output, out string error)
    {
        output = null;
        error = string.Empty;
        var live = expected == null ? null : Detect(script, monsterNumber, encounter).SingleOrDefault(row =>
            row.CallOffset == expected.CallOffset && row.LiteralOffset == expected.LiteralOffset
            && row.FunctionId == expected.FunctionId && row.SourceSha256 == expected.SourceSha256
            && row.EncounterSha256 == expected.EncounterSha256);
        if (live == null)
        {
            error = Strings.AiAdvancedConditionChanged;
            return false;
        }
        if (!live.Options.Any(o => o.Selector == selector))
        {
            error = Strings.AiAdvancedConditionRange;
            return false;
        }
        byte[] bytes = AiScript_File.Write(script);
        bytes[live.LiteralOffset + 1] = (byte)selector;
        bytes[live.LiteralOffset + 2] = 0;
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(bytes, script, bytes.Length, out _, out error)) return false;
        output = bytes;
        return true;
    }
}
