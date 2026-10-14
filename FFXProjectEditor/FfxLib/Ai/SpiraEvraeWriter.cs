using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Ai.SpiraNativeBossWriter;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record EvraeHolyCommands(IReadOnlyList<ushort> Singles,
    IReadOnlyList<ushort> RaFuries, IReadOnlyList<ushort> BasicFuries);

/// <summary>Native Evrae extension. Event 6 continues the four-command packet
/// after Haste; it never invokes another ordinary onTurn decision.</summary>
public static class SpiraEvraeWriter
{
    const ushort Self = 0xFFF3;
    public static IReadOnlyList<int[]> Permutations { get; } = Array.AsReadOnly(
        (from a in Enumerable.Range(0, 4) from b in Enumerable.Range(0, 4)
         from c in Enumerable.Range(0, 4) from d in Enumerable.Range(0, 4)
         where new[] { a, b, c, d }.Distinct().Count() == 4
         select new[] { a, b, c, d }).ToArray());

    public static byte[] Build(byte[] originalMonster, byte[] installedMonster, EvraeHolyCommands commands)
    {
        if (Monster_File.Read(originalMonster).StatSheetFile.MonsterId != 119
            || Monster_File.Read(installedMonster).StatSheetFile.MonsterId != 119)
            throw new InvalidDataException("This writer only owns Evrae m119.");
        if (commands.Singles.Count != 4 || commands.RaFuries.Count != 16 || commands.BasicFuries.Count != 4
            || commands.Singles.Concat(commands.RaFuries).Concat(commands.BasicFuries).Any(c => (c & 0xF000) != 0x6000)
            || commands.Singles.Concat(commands.RaFuries).Concat(commands.BasicFuries).Distinct().Count() != 24)
            throw new ArgumentException("Expected four singles, sixteen ra-Furies and four basic Furies.");
        var baseline = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(originalMonster)!);
        var script = baseline;
        if (!AiWorkerMapping.TryResolveCombatOnTurn(originalMonster, script, out var turn, out string error)
            || !AiWorkerMapping.TryResolveCombatOnHit(originalMonster, script, out var hit, out error)
            || turn != new AiEventHook(1, 2) || hit != new AiEventHook(1, 4)
            || script.Workers[1].Entrypoints.Count != 6)
            throw new InvalidDataException("Evrae's native event routing changed: " + error);
        var active = Reachable(script, turn); active.UnionWith(Reachable(script, hit));
        var sites = script.Instructions.Where(o => active.Contains(o.Offset - script.ScriptStart)
            && o.Opcode == 0xD8 && o.Operand == 0x700B).Select(o => o.Offset - script.ScriptStart).ToArray();
        if (sites.Length != 11 || script.Instructions.Any(o => o.Opcode == 0xAE && commands.Singles.Contains(o.Operand)))
            throw new InvalidDataException("Use the original eleven-site Evrae AI.");
        var vars = AddPrivateVariables(ref script, 1, sites.Length * 8 + 7);
        var scratch = sites.Select((site, i) => (site, v: vars.Skip(i * 8).Take(8).ToArray()))
            .ToDictionary(x => x.site, x => x.v);
        ushort pending = vars[^7], count = vars[^6], order = vars[^5];
        ushort[] sequence = vars.TakeLast(4).ToArray();
        var emit = new Emission(script, 1);
        IEnumerable<AiInstruction> Is(ushort variable, ushort value) => [Op(0x9F, variable), Op(0xAE, value), Op(0x06)];
        IEnumerable<AiInstruction> Reflect() => [Op(0xAE, 0), Op(0xAE, Self), Op(0xAE, 0x32), Op(0xB5, 0x700F), Op(0x0B)];
        void Roll(ushort variable, ushort range) => emit.Add(Op(0xB5, 0x00A9), Op(0xAE, range), Op(0x18), Op(0xA0, variable));
        void Select(IReadOnlyList<ushort> choices, ushort random, Action<ushort> action)
        {
            Roll(random, checked((ushort)choices.Count));
            for (ushort i = 0; i < choices.Count; i++)
            {
                ushort index = i;
                emit.If(Is(random, index), () => action(choices[index]));
            }
        }
        foreach (var op in script.Instructions)
        {
            if (!scratch.TryGetValue(op.Offset - script.ScriptStart, out var v)) { emit.Add(op); continue; }
            var first = Op(0xA0, v[0]); first = new AiInstruction { Offset = op.Offset, Opcode = first.Opcode,
                Operand = first.Operand, HasOperand = true, OperandKind = first.OperandKind };
            emit.Add(first, Op(0xA0, v[1]));
            emit.If(Is(v[0], 0x3036).Concat(Reflect()).Append(Op(0x02)), () =>
                Select(commands.RaFuries, v[2], command =>
                    emit.Add(Op(0xAE, command), Op(0xA0, v[0]), Op(0xAE, 0xFFF2), Op(0xA0, v[1]))));
            emit.Add(Op(0x9F, v[1]), Op(0x9F, v[0]), Op(0xD8, 0x700B));
            emit.If(Is(v[0], 0x407F), () => emit.DifferentLivingTarget(v[1], v[0], v));
            emit.If(Is(v[0], 0x605B), () => Select(commands.BasicFuries, v[2], c => emit.Command(0xFFF2, c)));
            emit.If(Is(v[0], 0x3036), () =>
            {
                Roll(pending, 4);
                emit.Add(Op(0x9F, pending), Op(0xAE, 1), Op(0x14), Op(0xA0, pending));
                Roll(order, 24);
                for (ushort i = 0; i < Permutations.Count; i++)
                {
                    ushort index = i;
                    emit.If(Is(order, index), () =>
                    {
                        for (int slot = 0; slot < 4; slot++)
                            emit.Add(Op(0xAE, commands.Singles[Permutations[index][slot]]), Op(0xA0, sequence[slot]));
                    });
                }
            });
        }
        int continuation = emit.Output.Sum(o => o.Length);
        emit.Add(Op(0x9F, pending), Op(0xA0, count), Op(0xAE, 0), Op(0xA0, pending));
        for (ushort i = 0; i < 4; i++)
        {
            ushort slot = i;
            emit.If([Op(0xAE, slot), Op(0x9F, count), Op(0x0B),
                Op(0xAE, Self), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x02),
                Op(0xAE, 0xFFFA), Op(0xAE, 4), Op(0xB5, 0x700F),
                Op(0xAE, 0xFFF9), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x01),
                Op(0xAE, 0xFFF8), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x01), Op(0x02)], () =>
                    emit.Add(Op(0xAE, 0xFFF2), Op(0xAE, 4), Op(0xAE, 0), Op(0xAE, 0),
                        Op(0xB5, 0x7010), Op(0x9F, sequence[slot]), Op(0xD8, 0x700B)));
        }
        emit.Add(Op(0x3C));
        byte[] ai = AppendEntrypoint(emit.Build(), 1, continuation);
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(ai, baseline, out _, out error))
            throw new InvalidDataException(error);
        byte[] result = AiScript_File.SpliceAiFileIntoMonsterGrow(installedMonster, ai);
        int oldTail = BitConverter.ToInt32(installedMonster, 8), tail = BitConverter.ToInt32(result, 8);
        if (!installedMonster.AsSpan(oldTail).SequenceEqual(result.AsSpan(tail)))
            throw new InvalidDataException("Evrae's installed non-AI data changed.");
        int entry = CombatPurposeOffset(result, 6);
        if (BitConverter.ToUInt16(result, entry) != ushort.MaxValue)
            throw new InvalidOperationException("Evrae already has a native continuation; refusing to replace it.");
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(entry, 2), 6);
        return result;
    }

    static int CombatPurposeOffset(byte[] monster, int purpose)
    {
        int start = BitConverter.ToInt32(monster, 8), end = BitConverter.ToInt32(monster, 12);
        int count = monster[start], pre = monster[start + 1], table = start + pre + (pre % 2 == 0 ? 2 : 3);
        if (table + count * 4 > end) throw new InvalidDataException("Invalid WorkerFile.");
        for (int i = 0; i < count; i++)
        {
            int row = table + i * 4;
            if (monster[row] != 1 || monster[row + 1] != 2) continue;
            int section = start + BitConverter.ToUInt16(monster, row + 2);
            if (section < start || section + 2 + (purpose + 1) * 2 > end
                || BitConverter.ToUInt16(monster, section) <= purpose)
                throw new InvalidDataException("Missing native combat purpose.");
            return section + 2 + purpose * 2;
        }
        throw new InvalidDataException("Missing combat worker.");
    }

    static byte[] AppendEntrypoint(byte[] source, int worker, int target)
    {
        var script = AiScript_File.Read(source); var w = script.Workers[worker]; int desc = w.DescriptorOffset;
        if (!script.Instructions.Any(o => o.Offset - script.ScriptStart == target)
            || script.Workers.Any(v => v.DescriptorOffset + 0x28 > script.ScriptStart))
            throw new InvalidDataException("Unsupported entrypoint/table layout.");
        int entry = BitConverter.ToInt32(source, desc + 0x20), insertion = entry + w.Entrypoints.Count * 4;
        if (insertion != BitConverter.ToInt32(source, desc + 0x24) || insertion < script.ScriptStart + script.CodeLength
            || insertion > source.Length || w.Entrypoints.Count >= ushort.MaxValue)
            throw new InvalidDataException("Entrypoint table bounds changed.");
        var result = new byte[checked(source.Length + 4)];
        source.AsSpan(0, insertion).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(insertion, 4), target);
        source.AsSpan(insertion).CopyTo(result.AsSpan(insertion + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(desc + 8, 2), checked((ushort)(w.Entrypoints.Count + 1)));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x10, 4), checked(BitConverter.ToInt32(source, 0x10) + 4));
        foreach (var other in script.Workers)
            foreach (int field in new[] { 0x14, 0x18, 0x1C, 0x20, 0x24 })
            {
                int offset = other.DescriptorOffset + field, pointer = BitConverter.ToInt32(source, offset);
                if (pointer >= insertion) BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), checked(pointer + 4));
            }
        return result;
    }
}
