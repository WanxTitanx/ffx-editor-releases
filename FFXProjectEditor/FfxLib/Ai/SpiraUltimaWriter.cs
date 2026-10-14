using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Ai.SpiraNativeBossWriter;

namespace FFXProjectEditor.FfxLib.Ai;

public static class SpiraUltimaWriter
{
    public static byte[] BuildBoss(byte[] monster, IReadOnlyDictionary<string, ushort> commands)
    {
        if (Monster_File.Read(monster).StatSheetFile?.MonsterId != 99)
            throw new InvalidDataException("Expected native Ultima Weapon m099.");
        var script = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);
        if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out var turn, out string error))
            throw new InvalidDataException(error);
        if (commands.Count != 8 || commands.Values.Distinct().Count() != 8
            || commands.Values.Any(v => (v & 0xF000) != 0x6000)
            || script.Instructions.Any(op => op.Opcode == 0xAE && commands.Values.Contains(op.Operand)))
            throw new InvalidDataException("Invalid command allocation or already authored Ultima.");
        var reachable = Reachable(script, turn);
        var sites = script.Instructions.Where(op => reachable.Contains(op.Offset - script.ScriptStart)
            && op.Opcode == 0xD8 && op.Operand == 0x700B).Select(op => op.Offset - script.ScriptStart).ToArray();
        ushort step = checked((ushort)script.Variables.Single(v => v.Storage == 0x56 && v.Slot == 8).Index);
        var vars = AddPrivateVariables(ref script, turn.WorkerIndex, sites.Length * 3);
        var locals = sites.Select((at, i) => (at, vars: vars.Skip(i * 3).Take(3).ToArray())).ToDictionary(v => v.at, v => v.vars);
        var emit = new Emission(script, turn.WorkerIndex);
        AiInstruction[] Is(ushort v, params ushort[] choices)
        {
            var result = new List<AiInstruction>();
            foreach (ushort choice in choices)
            {
                result.AddRange(new[] { Op(0x9F, v), Op(0xAE, choice), Op(0x06) });
                if (result.Count > 3) result.Add(Op(0x01));
            }
            return result.ToArray();
        }
        foreach (var op in script.Instructions)
        {
            if (!locals.TryGetValue(op.Offset - script.ScriptStart, out var v)) { emit.Add(op); continue; }
            emit.Add(new AiInstruction { Offset = op.Offset, Opcode = 0xA0, Operand = v[0], HasOperand = true, OperandKind = AiOperandKind.VarStore }, Op(0xA0, v[1]));
            // Confuse belongs to both native steps 2 and 4. The step guard is
            // essential: replacing by command ID alone would erase Meteor.
            emit.If(Is(step, 2).Concat(Is(v[0], 0x604F, 0x606E, 0x6072)).Append(Op(0x02)),
                () => emit.Add(Op(0xAE, commands["break"]), Op(0xA0, v[0])));
            emit.If(Is(step, 4), () =>
            {
                emit.If(Is(v[0], 0x303F), () => emit.Add(Op(0xAE, 0x6124), Op(0xA0, v[0])));
                emit.If(Is(v[0], 0x6071), () => emit.Add(Op(0xAE, commands["darkMeteor"]), Op(0xA0, v[0])));
                emit.If(Is(v[0], 0x6072), () => emit.Add(Op(0xAE, commands["holyMeteor"]), Op(0xA0, v[0])));
            });
            emit.Add(Op(0x9F, v[1]), Op(0x9F, v[0]), Op(0xD8, 0x700B));
            void RandomPair(ushort first, ushort second)
            {
                emit.Add(Op(0xB5, 0x00A9), Op(0xAE, 2), Op(0x18), Op(0xA0, v[2]));
                emit.If(Is(v[2], 0), () => emit.RandomCommand(first));
                emit.If(Is(v[2], 1), () => emit.RandomCommand(second));
            }
            emit.If(Is(v[0], 0x407F), () => RandomPair(commands["darkAttack"], commands["holyAttack"]));
            emit.If(Is(v[0], commands["break"]), () => RandomPair(0x615D, commands["holyFlare"]));
            emit.If(Is(v[0], 0x4022), () => emit.Command(0xFFF3, commands["secret"]));
            emit.If(Is(v[0], 0x4023), () => emit.Command(0xFFF1, commands["guard"]));
        }
        return emit.Build();
    }

    public static byte[] BuildSmallNormal(byte[] originalMonster, float scale = 0.55f)
    {
        if (!(scale > 0 && scale < 1) || !float.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        var ai = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(originalMonster)!);
        if (!AiWorkerMapping.TryResolveCombatOnTurn(originalMonster, ai, out var turn, out string error))
            throw new InvalidDataException(error);
        var (bytes, index) = AiScript_File.AppendFloatConst(ai, scale);
        // Native examples m044 and m230 use PUSHF(0.5) three times then 0x7028.
        return AiScript_File.AppendGuardedAction(AiScript_File.Read(bytes), turn.WorkerIndex, 0,
            new[] { Op(0xAE, 1) }, new[] { Op(0xAF, checked((ushort)index)), Op(0xAF, checked((ushort)index)), Op(0xAF, checked((ushort)index)), Op(0xD8, 0x7028) });
    }
}
