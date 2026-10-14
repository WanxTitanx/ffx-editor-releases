using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai;

/// <summary>Add one five-way magical-damage counter to the current native Flux.
/// No stat scaling and no replacement of its existing turn/phase scripts.</summary>
public static class SpiraFluxMagicCounterWriter
{
    public static byte[] Build(byte[] monster, IReadOnlyList<ushort> commands)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count != 5 || commands.Distinct().Count() != 5 || commands.Any(c => (c & 0xF000) != 0x6000))
            throw new ArgumentException("Exactly five distinct monmagic2 counter commands are required.", nameof(commands));
        if (Monster_File.Read(monster).StatSheetFile?.MonsterId != 142)
            throw new InvalidDataException("This counter belongs to Seymour Flux m142.");
        byte[] original = AiScript_File.SliceAiFileFromMonster(monster) ?? throw new InvalidDataException("No AI partition.");
        AiScriptFile baseline = AiScript_File.Read(original), script = baseline;
        if (!AiWorkerMapping.TryResolveCombatOnHit(monster, script, out var hit, out string error))
            throw new InvalidDataException(error);
        if (script.Instructions.Any(op => op.Opcode == 0xAE && commands.Contains(op.Operand)))
            throw new InvalidOperationException("Flux's anti-element counter is already authored.");
        ushort choice = AddPrivateVariable(ref script, hit.WorkerIndex);
        var guard = new List<AiInstruction>
        {
            Op(0xAE, 0), Op(0xAE, 0xFFF3), Op(0xAE, 0x00A6), Op(0xB5, 0x700F), Op(0x0B),
            Op(0xAE, 0xFFF3), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x02),
        };
        guard.AddRange(AiAutomation.BuildUsedCommandDamageTypeGuard(AiAutomation.DamageTypeMagical));
        guard.Add(Op(0x02));
        guard.Add(Op(0xB5, 0x70E0)); guard.Add(Op(0x02));
        int oldJumps = script.Workers[hit.WorkerIndex].JumpTargets.Count;
        // AppendGuardedAction reserves two slots for its own rejoin/original route.
        int firstChoiceJump = oldJumps + 2;
        var action = new List<AiInstruction> { Op(0xB5, 0x00A9), Op(0xAE, 5), Op(0x18), Op(0xA0, choice) };
        var labels = new List<AiInstruction>();
        AiInstruction done = Op(0x00);
        for (ushort index = 0; index < commands.Count; index++)
        {
            action.AddRange(new[] { Op(0x9F, choice), Op(0xAE, index), Op(0x06),
                Op(0xD7, checked((ushort)(firstChoiceJump + index))),
                Op(0xAE, 0xFFF2), Op(0xAE, commands[index]), Op(0xD8, 0x700B),
                Op(0xB0, checked((ushort)(firstChoiceJump + 5))) });
            var next = Op(0x00); labels.Add(next); action.Add(next);
        }
        labels.Add(done); action.Add(done);
        int actionStart = script.CodeLength + guard.Sum(op => op.Length) + 3;
        var relative = new Dictionary<AiInstruction, int>();
        int position = actionStart;
        foreach (var op in action) { relative[op] = position; position += op.Length; }
        byte[] bytes = AiScript_File.AppendGuardedAction(script, hit.WorkerIndex, hit.EntrypointIndex, guard, action);
        bytes = AiScript_File.GrowWorkerJumpTable(AiScript_File.Read(bytes), hit.WorkerIndex,
            labels.Select(label => relative[label]).ToArray());
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(bytes, baseline, out _, out error))
            throw new InvalidDataException(error);
        return bytes;
    }

    static ushort AddPrivateVariable(ref AiScriptFile script, int worker)
    {
        int end = Math.Max(script.Workers.Max(w => w.PrivateDataLength),
            script.Variables.Where(v => v.Storage == 0x56).Select(v => v.Slot + 4).DefaultIfEmpty().Max());
        int slot = checked((end + 3) / 4 * 4);
        ushort length = checked((ushort)(slot + 4));
        byte[] grown = (byte[])script.OriginalAiFileBytes.Clone();
        int descriptor = script.Workers[worker].DescriptorOffset;
        grown[descriptor + 0x10] = (byte)length; grown[descriptor + 0x11] = (byte)(length >> 8);
        script = AiScript_File.Read(grown);
        ushort index = checked((ushort)script.Variables.Count);
        script = AiScript_File.Read(AiScript_File.AppendPrivateVariableDescriptor(script, slot));
        return index;
    }

    static AiInstruction Op(byte opcode, ushort operand = 0) => new()
    {
        Offset = -1, Opcode = opcode, Operand = operand, HasOperand = AiScript_File.IsOperandBearing(opcode),
        OperandKind = AiScript_File.OperandKindOf(opcode),
    };
}
