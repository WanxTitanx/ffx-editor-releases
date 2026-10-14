using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Ai.SpiraNativeBossWriter;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record YunalescaCommands(IReadOnlyList<ushort> Debuffs, IReadOnlyList<ushort> Furies,
    ushort Darkga, ushort Darkja, ushort DarkWind, ushort DarkMightGuard, ushort HyperDarkGuard);

/// <summary>Native m130 phase state (private variable 1: 0/1/2) and death/morph
/// paths survive. Extra normal-turn actions never apply to native onHit counters.</summary>
public static class SpiraYunalescaWriter
{
    const ushort Self = 0xFFF3;
    static AiInstruction At(AiInstruction op, int offset) => new()
    {
        Offset = offset, Opcode = op.Opcode, Operand = op.Operand,
        HasOperand = op.HasOperand, OperandKind = op.OperandKind,
    };
    public static byte[] Build(byte[] monster, YunalescaCommands skills)
    {
        ArgumentNullException.ThrowIfNull(monster); ArgumentNullException.ThrowIfNull(skills);
        if (skills.Debuffs.Count != 8 || skills.Furies.Count != 16
            || skills.Debuffs.Concat(skills.Furies).Append(skills.Darkja).Append(skills.Darkga)
                .Append(skills.DarkWind).Append(skills.DarkMightGuard).Append(skills.HyperDarkGuard)
                .Any(v => (v & 0xF000) != 0x6000))
            throw new ArgumentException("Invalid Yunalesca skill packet.", nameof(skills));
        if (Monster_File.Read(monster).StatSheetFile?.MonsterId != 130)
            throw new InvalidDataException("This writer owns Yunalesca m130.");
        byte[] input = AiScript_File.SliceAiFileFromMonster(monster) ?? throw new InvalidDataException("Missing AI.");
        var baseline = AiScript_File.Read(input); var script = baseline;
        if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out var turn, out string error)
            || !AiWorkerMapping.TryResolveCombatOnHit(monster, script, out var hit, out error)
            || turn.WorkerIndex != 1 || hit.WorkerIndex != 1)
            throw new InvalidDataException("Yunalesca native worker mapping changed: " + error);
        if (script.Instructions.Any(op => op.Opcode == 0xAE && op.Operand == skills.Darkja))
            throw new InvalidOperationException("Yunalesca is already authored; use the immutable baseline.");
        var turnReach = Reachable(script, turn); var hitReach = Reachable(script, hit);
        var sites = script.Instructions.Where(op => op.Opcode == 0xD8 && op.Operand == 0x700B
            && turnReach.Contains(op.Offset - script.ScriptStart)).Select(op => op.Offset - script.ScriptStart).ToHashSet();
        if (sites.Count != 3 || sites.Any(hitReach.Contains))
            throw new InvalidDataException("Native turn sites changed or overlap counters.");
        var ops = script.Instructions;
        var anchors = Enumerable.Range(0, ops.Count - 2).Where(i => hitReach.Contains(ops[i].Offset - script.ScriptStart)
            && ops[i].Opcode == 0x9F && ops[i].Operand == 1 && ops[i + 1].Opcode == 0x2C
            && ops[i + 2].Opcode == 0xB0).ToArray();
        if (anchors.Length != 1) throw new InvalidDataException("Native alive-counter phase dispatch changed.");
        int anchor = ops[anchors[0]].Offset - script.ScriptStart;
        // Native initialization replaces STR with 5/40/40. Scale these actual
        // phase values too; scaling only the StatSheet would be overwritten.
        var strengthSites = Enumerable.Range(3, ops.Count - 3).Where(i =>
            ops[i].Opcode == 0xD8 && ops[i].Operand == 0x70A8
            && ops[i - 3].Opcode == 0xAE && ops[i - 3].Operand == Self
            && ops[i - 2].Opcode == 0xAE && ops[i - 2].Operand == 9 && ops[i - 1].Opcode == 0xAE).ToArray();
        if (strengthSites.Length != 3 || !strengthSites.Select(i => ops[i - 1].Operand).SequenceEqual(new ushort[] { 5, 40, 40 }))
            throw new InvalidDataException("Native phase STR witnesses changed.");
        var strengthOffsets = strengthSites.Select(i => ops[i - 1].Offset - script.ScriptStart).ToHashSet();
        // Persistent cycle/gate flags, isolated onHit scratch, then separate
        // scratch for each suspended native onTurn call.
        ushort[] vars = AddPrivateVariables(ref script, 1, 11 + sites.Count * 5);
        ushort furyCycle = vars[0], form2Heal = vars[1], guard75 = vars[2], wind50 = vars[3], hyper25 = vars[4];
        ushort hitKind = vars[5], hitTarget = vars[6], hitHp = vars[7], hitChoice = vars[8];
        ushort counterCommand = vars[9], hitMaxOrTarget = vars[10];
        var hitSites = script.Instructions.Where(op => op.Opcode == 0xD8 && op.Operand == 0x700B
            && hitReach.Contains(op.Offset - script.ScriptStart)).Select(op => op.Offset - script.ScriptStart).ToHashSet();
        var scratch = sites.OrderBy(x => x).Select((x, i) => (x, v: vars.Skip(11 + i * 5).Take(5).ToArray()))
            .ToDictionary(x => x.x, x => x.v);
        var emit = new Emission(script, 1);
        IEnumerable<AiInstruction> Phase(ushort value) => [Op(0x9F, 1), Op(0xAE, value), Op(0x06)];
        IEnumerable<AiInstruction> Zero(ushort variable) => [Op(0x9F, variable), Op(0xAE, 0), Op(0x06)];
        IEnumerable<AiInstruction> HpAtMost(ushort percent, bool cached) => cached
            ? [Op(0x9F, hitHp), Op(0x9F, hitMaxOrTarget), Op(0xAE, percent), Op(0x16), Op(0x0F)]
            : [Op(0xAE, Self), Op(0xAE, 0), Op(0xB5, 0x700F), Op(0xAE, 100), Op(0x16),
                Op(0xAE, Self), Op(0xAE, 2), Op(0xB5, 0x700F), Op(0xAE, percent), Op(0x16), Op(0x0F)];
        void Roll(ushort variable, ushort max) => emit.Add(Op(0xB5, 0x00A9), Op(0xAE, max), Op(0x18), Op(0xA0, variable));
        IEnumerable<AiInstruction> PartyAlive() =>
            [Op(0xAE, 0xFFFA), Op(0xAE, 4), Op(0xB5, 0x700F),
             Op(0xAE, 0xFFF9), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x01),
             Op(0xAE, 0xFFF8), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x01)];
        void RandomLiving(ushort command) => emit.If(PartyAlive(), () => emit.RandomCommand(command));
        void Gate(ushort percent, ushort once, ushort skill, bool cached)
        {
            emit.If(HpAtMost(percent, cached).Concat(Zero(once)).Append(Op(0x02)), () =>
            {
                emit.Add(Op(0xAE, 1), Op(0xA0, once)); // Before yielding into native callbacks.
                emit.Command(Self, skill);
            });
        }
        void CounterPrefix()
        {
            emit.Add(Op(0x9F, 10), Op(0xA0, hitKind), Op(0x9F, 5), Op(0xA0, hitTarget));
            emit.If(Phase(1), () =>
            {
                Gate(25, form2Heal, skills.DarkWind, false);
                emit.If([Op(0x9F, hitKind), Op(0xAE, 2), Op(0x06)], () =>
                {
                    emit.If([Op(0xB5, 0x70E0)], () =>
                    {
                        Roll(hitChoice, 100);
                        emit.If([Op(0x9F, hitChoice), Op(0xAE, 50), Op(0x0B)], () =>
                            emit.Add(Op(0x9F, hitTarget), Op(0xAE, skills.Darkga), Op(0xD8, 0x700B)));
                    });
                    emit.Add(Op(0x3C)); // Failed magic roll cannot fall into native Absorb.
                });
            });
            emit.If(Phase(2), () =>
            {
                emit.If([Op(0xAE, 0), Op(0xAE, Self), Op(0xAE, 0xA6), Op(0xB5, 0x700F), Op(0x0B)], () =>
                {
                    emit.Add(Op(0xAE, Self), Op(0xAE, 0), Op(0xB5, 0x700F), Op(0xAE, 100), Op(0x16), Op(0xA0, hitHp),
                        Op(0xAE, Self), Op(0xAE, 2), Op(0xB5, 0x700F), Op(0xA0, hitMaxOrTarget));
                    Gate(75, guard75, skills.DarkMightGuard, true);
                    Gate(50, wind50, skills.DarkWind, true);
                    Gate(25, hyper25, skills.HyperDarkGuard, true);
                    emit.If([Op(0xB5, 0x70E0), Op(0xAE, 0), Op(0x06)], () => emit.Add(Op(0x3C)));
                    Roll(hitChoice, 100);
                    emit.If([Op(0x9F, hitChoice), Op(0xAE, 15), Op(0x0B)], () =>
                    {
                        emit.Add(Op(0x9F, hitTarget), Op(0xAE, skills.Darkja), Op(0xD8, 0x700B), Op(0x3C));
                    });
                });
                // The other 85% use the original counter tail and original attacker
                // variable. Restore it after self-buffs may invoke onHit again.
                emit.Add(Op(0x9F, hitKind), Op(0xA0, 10), Op(0x9F, hitTarget), Op(0xA0, 5));
            });
        }
        foreach (var op in script.Instructions)
        {
            int rel = op.Offset - script.ScriptStart;
            if (rel == anchor)
            {
                int start = emit.Output.Count; CounterPrefix(); emit.Output[start] = At(emit.Output[start], op.Offset);
                emit.Add(Op(op.Opcode, op.Operand));
                continue;
            }
            if (strengthOffsets.Contains(rel))
            {
                var scaled = At(Op(op.Opcode, SpiraNativeBossStats.ScaleAttribute((byte)op.Operand, 7)), op.Offset);
                emit.Add(scaled); continue;
            }
            if (hitSites.Contains(rel))
            {
                var firstCounter = At(Op(0xA0, counterCommand), op.Offset);
                emit.Add(firstCounter, Op(0xA0, hitMaxOrTarget));
                // FFFef is mutable across nested self-healing callbacks.
                emit.If([Op(0xAE, 0), Op(0x9F, 1), Op(0x0B)], () =>
                    emit.Add(Op(0x9F, hitTarget), Op(0xA0, hitMaxOrTarget)));
                emit.Add(Op(0x9F, hitMaxOrTarget), Op(0x9F, counterCommand), Op(op.Opcode, op.Operand));
                continue;
            }
            if (!scratch.TryGetValue(rel, out ushort[]? v)) { emit.Add(op); continue; }
            var first = At(Op(0xA0, v[0]), op.Offset); emit.Add(first, Op(0xA0, v[1]),
                Op(0x9F, 1), Op(0xA0, v[2]), Op(0x9F, v[1]), Op(0x9F, v[0]), Op(op.Opcode, op.Operand));
            emit.If([Op(0xAE, Self), Op(0xAE, 4), Op(0xB5, 0x700F),
                Op(0x9F, 1), Op(0x9F, v[2]), Op(0x06), Op(0x02)], () =>
            {
                emit.If(Phase(0), () =>
                {
                    Roll(v[3], 8);
                    for (ushort i = 0; i < 8; i++)
                    {
                        ushort index = i;
                        emit.If([Op(0x9F, v[3]), Op(0xAE, index), Op(0x06)], () => RandomLiving(skills.Debuffs[index]));
                    }
                });
                emit.If(Phase(1), () =>
                {
                    // Native command packets have four slots, including the
                    // ordinary action/morph. A Fury must occupy one slot; its
                    // native HitCount/RandomTargets execute all 3..6 impacts.
                    // Order: four spells, each with hit counts 3,4,5,6.
                    Roll(v[3], 16);
                    for (ushort choice = 0; choice < 16; choice++)
                    {
                        ushort index = choice;
                        emit.If([Op(0x9F, v[3]), Op(0xAE, index), Op(0x06)], () =>
                            emit.Command(0xFFF2, skills.Furies[index]));
                    }
                    emit.Add(Op(0x9F, furyCycle), Op(0xAE, 1), Op(0x14), Op(0xA0, furyCycle));
                    emit.If([Op(0xAE, 4), Op(0x9F, furyCycle), Op(0x0F)], () =>
                    {
                        emit.Add(Op(0xAE, 0), Op(0xA0, furyCycle)); Roll(v[3], 2);
                        emit.If(Zero(v[3]), () => emit.Command(Self, skills.DarkWind));
                        emit.If([Op(0x9F, v[3]), Op(0xAE, 1), Op(0x06)], () => emit.Command(Self, skills.DarkMightGuard));
                    });
                });
            });
        }
        byte[] result = emit.Build();
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result, baseline, out _, out error))
            throw new InvalidDataException(error);
        return result;
    }
}
