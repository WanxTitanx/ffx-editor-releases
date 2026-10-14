using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Battle;

public sealed record BattleOpeningValue(int InstructionOffset, ushort Target, ushort Field, bool Add, short Value,
    int ValueOffset);
public sealed record BattleOpeningValueEdit(int InstructionOffset, int Value);

/// <summary>
/// Byte-local editing of CTB writes reached from the encounter's actor-28/event-5 mapping.
/// Steam 25501027: field 59 writes bit 1 at actor+6BC; field 232 writes a byte at actor+65C.
/// See AURORA_OPENING_COMPANIONS_AUTHORING_20261008.md. No script execution is simulated here.
/// </summary>
public static class BattleOpeningWriter
{
    public const ushort FirstStrike = 0x3B;
    public const ushort CurrentTurnDelay = 0xE8;
    public static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static IReadOnlyList<BattleOpeningValue> Read(byte[] bytes)
    {
        var layout = ReadLayout(bytes);
        if (!TryResolveHook(layout.Script, layout.Map, out int worker, out int entry))
            return Array.Empty<BattleOpeningValue>();
        var script = layout.Script;
        var instructions = script.Instructions;
        var reachable = Reachable(script, worker, entry);
        var targets = script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets))
            .Select(offset => offset + script.ScriptStart).ToHashSet();
        var rows = new List<BattleOpeningValue>();
        bool Push(int index) => index >= 0 && instructions[index].Opcode == 0xAE && instructions[index].HasOperand;
        bool Contiguous(int first, int last) => first >= 0 && Enumerable.Range(first, last - first + 1)
            .All(i => reachable.Contains(instructions[i].Offset) &&
                (i == first || !targets.Contains(instructions[i].Offset) &&
                    instructions[i - 1].Offset + instructions[i - 1].Length == instructions[i].Offset));
        for (int i = 0; i < instructions.Count; i++)
        {
            var call = instructions[i];
            if (call.Opcode != 0xD8 || call.Operand != 0x7018 || !reachable.Contains(call.Offset)) continue;
            int actor, field, value; bool add;
            if (Push(i - 3) && Push(i - 2) && Push(i - 1) && Contiguous(i - 3, i))
            { actor = i - 3; field = i - 2; value = i - 1; add = false; }
            else if (i >= 7 && Push(i - 7) && Push(i - 6) && Push(i - 5) && Push(i - 4) &&
                instructions[i - 3].Opcode == 0xB5 && instructions[i - 3].Operand == 0x700F &&
                Push(i - 2) && instructions[i - 1].Opcode == 0x14 &&
                instructions[i - 7].Operand == instructions[i - 5].Operand &&
                instructions[i - 6].Operand == instructions[i - 4].Operand && Contiguous(i - 7, i))
            { actor = i - 7; field = i - 6; value = i - 2; add = true; }
            else continue;
            ushort fieldId = instructions[field].Operand;
            if (fieldId != FirstStrike && fieldId != CurrentTurnDelay || add && fieldId != CurrentTurnDelay) continue;
            rows.Add(new(layout.AiOffset + call.Offset, instructions[actor].Operand, fieldId, add,
                unchecked((short)instructions[value].Operand), layout.AiOffset + instructions[value].Offset + 1));
        }
        return rows;
    }

    public static byte[] Apply(byte[] original, string expectedRevision, IReadOnlyList<BattleOpeningValueEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);
        if (!string.Equals(Revision(original), expectedRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("The encounter changed after the opening was loaded.");
        var rows = Read(original).ToDictionary(row => row.InstructionOffset);
        var seen = new HashSet<int>(); var allowed = new HashSet<int>();
        byte[] updated = original.ToArray();
        foreach (var edit in edits)
        {
            if (!seen.Add(edit.InstructionOffset) || !rows.TryGetValue(edit.InstructionOffset, out var row))
                throw new InvalidDataException("The requested opening write is absent, duplicated, or not safely recognized.");
            int min = row.Add ? -255 : 0, max = row.Field == FirstStrike ? 1 : 255;
            if (edit.Value < min || edit.Value > max)
                throw new ArgumentOutOfRangeException(nameof(edits), $"Value must be between {min} and {max}.");
            BinaryPrimitives.WriteInt16LittleEndian(updated.AsSpan(row.ValueOffset, 2), (short)edit.Value);
            allowed.Add(row.ValueOffset); allowed.Add(row.ValueOffset + 1);
        }
        if (original.Where((value, index) => value != updated[index] && !allowed.Contains(index)).Any())
            throw new InvalidDataException("Opening edit escaped its operand ranges.");
        var reread = Read(updated).ToDictionary(row => row.InstructionOffset);
        if (rows.Count != reread.Count || edits.Any(edit => !reread.TryGetValue(edit.InstructionOffset, out var row) || row.Value != edit.Value))
            throw new InvalidDataException("Opening edit did not survive readback.");
        return updated;
    }

    internal static (int AiOffset, AiScriptFile Script, byte[] Map) ReadLayout(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 20) throw new InvalidDataException("Battle header is truncated.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes) - 1;
        if (count < 3 || count > 64 || 4L + (count + 1L) * 4 > bytes.Length)
            throw new InvalidDataException("Invalid battle chunk table.");
        int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        int map = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        int formation = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (start < 4 + (count + 1) * 4 || map <= start || formation <= map || formation > bytes.Length)
            throw new InvalidDataException("Invalid battle script or worker-map bounds.");
        var script = AiScript_File.Read(bytes.AsSpan(start, map - start).ToArray());
        if (!script.CodeWalkClosedExactly) throw new InvalidDataException("The ATEL instruction walk is incomplete.");
        return (start, script, bytes.AsSpan(map, formation - map).ToArray());
    }

    internal static bool TryResolveHook(AiScriptFile script, byte[] map, out int worker, out int entry)
    {
        worker = entry = -1;
        if (map.Length < 2 || map[1] <= 137) return false;
        int records = map[1] + ((map[1] & 1) == 0 ? 2 : 3);
        if (records + map[0] * 4 > map.Length) throw new InvalidDataException("Truncated worker map.");
        int record = map[2 + 137];
        if (record == 255) return false;
        if (record >= map[0]) throw new InvalidDataException("Opening record is out of range.");
        int at = records + record * 4;
        if (map[at + 1] != 6) throw new InvalidDataException("Opening mapping has an unexpected type.");
        worker = map[at];
        int table = BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(at + 2));
        if (table < records + map[0] * 4 || table + 2 > map.Length) throw new InvalidDataException("Invalid opening event table.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(table));
        if (table + 2L + count * 2L > map.Length) throw new InvalidDataException("Truncated opening event table.");
        if (count <= 5) return false;
        entry = BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(table + 12));
        if (entry == 65535) return false;
        if (worker >= script.Workers.Count || entry >= script.Workers[worker].Entrypoints.Count)
            throw new InvalidDataException("Opening worker or entrypoint is out of range.");
        return true;
    }

    private static HashSet<int> Reachable(AiScriptFile script, int workerIndex, int entry)
    {
        var worker = script.Workers[workerIndex]; var visited = new HashSet<int>();
        var instructions = script.Instructions.ToDictionary(i => i.Offset);
        var pending = new Stack<int>(); pending.Push(script.ScriptStart + worker.Entrypoints[entry]);
        while (pending.TryPop(out int offset))
        {
            if (!visited.Add(offset)) continue;
            if (!instructions.TryGetValue(offset, out var ins)) throw new InvalidDataException("Opening control flow leaves the instruction map.");
            if (ins.Opcode == 0x3C) continue;
            if (ins.Opcode is 0xB0 or 0xD6 or 0xD7)
            {
                if (ins.Operand >= worker.JumpTargets.Count) throw new InvalidDataException("Opening jump is out of range.");
                pending.Push(script.ScriptStart + worker.JumpTargets[ins.Operand]);
                if (ins.Opcode == 0xB0) continue;
            }
            if (ins.Opcode is 0x3D or 0x3E or 0x3F or 0x40 || ins.Opcode == 0xD8 && ins.Operand == 0x005F)
                continue;
            pending.Push(offset + ins.Length);
        }
        return visited;
    }
}
