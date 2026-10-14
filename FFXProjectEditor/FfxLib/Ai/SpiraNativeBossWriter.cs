using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record NativeBossCommands(ushort Darkra, ushort Darkga, ushort HolyFira,
    ushort AntiFira, ushort AntiThundara, ushort AntiWatera, ushort AntiBlizzara,
    ushort AntiFlareNatus, ushort AntiFlareFlux, ushort Requiem);

public sealed record NativeBossAiEdit(byte[] AiFile, int NativeCommandSites);

/// <summary>Permanent, narrowly scoped boss authoring. Preserve native phase state,
/// WorkerFile event routing and computed targets. Insertions visit original calls once.</summary>
public static class SpiraNativeBossWriter
{
    static readonly ushort[] Frontline = { 0xFFFA, 0xFFF9, 0xFFF8 };
    const ushort Self = 0xFFF3;

    public static NativeBossAiEdit Build(byte[] monster, int monsterId, NativeBossCommands commands)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(commands);
        _ = SpiraNativeBossStats.TierFor(monsterId);
        if (Monster_File.Read(monster).StatSheetFile?.MonsterId != monsterId)
            throw new InvalidDataException("The input monster identity does not match the requested boss.");
        ushort[] added = { commands.Darkra, commands.Darkga, commands.HolyFira, commands.AntiFira,
            commands.AntiThundara, commands.AntiWatera, commands.AntiBlizzara,
            commands.AntiFlareNatus, commands.AntiFlareFlux, commands.Requiem };
        if (added.Any(c => (c & 0xF000) != 0x6000) || added.Distinct().Count() != added.Length)
            throw new ArgumentException("Boss commands require distinct monster-magic-2 operands.", nameof(commands));
        byte[] bytes = AiScript_File.SliceAiFileFromMonster(monster)
            ?? throw new InvalidDataException("Monster has no AI partition.");
        AiScriptFile script = AiScript_File.Read(bytes);
        if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out var turn, out string error)
            || !AiWorkerMapping.TryResolveCombatOnHit(monster, script, out var hit, out error))
            throw new InvalidDataException(error);
        if (turn.WorkerIndex != hit.WorkerIndex) throw new InvalidDataException("Combat events use different workers.");
        if (script.Instructions.Any(op => op.Opcode == 0xAE && added.Contains(op.Operand)))
            throw new InvalidOperationException("This boss round has already been baked; use the immutable baseline.");
        if (monsterId == 212)
            for (int i = 3; i < script.Instructions.Count; i++)
                if (script.Instructions[i].Opcode == 0xD8 && script.Instructions[i].Operand == 0x700B
                    && script.Instructions[i - 1].Opcode == 0xAE && script.Instructions[i - 1].Operand == 0x301A
                    && script.Instructions[i - 2].Opcode == 0xAE && script.Instructions[i - 2].Operand == Self
                    && script.Instructions[i - 3].Opcode == 0xD7)
                    throw new InvalidOperationException("Wendigo's post-attack Cheer has already been baked.");
        HashSet<int> active = Reachable(script, turn);
        active.UnionWith(Reachable(script, hit));
        int[] sites = script.Instructions.Where(op => active.Contains(op.Offset - script.ScriptStart)
            && op.Opcode is 0xD8 or 0xB5 && op.Operand is 0x700B or 0x705A)
            .Select(op => op.Offset - script.ScriptStart).ToArray();
        if (sites.Length == 0) throw new InvalidDataException("No native combat command sites.");
        if (script.Instructions.Any(op => sites.Contains(op.Offset - script.ScriptStart) && op.Opcode != 0xD8))
            throw new InvalidDataException("A native command call returns a value.");
        // Each original site gets separate scratch storage. A command can yield into
        // onHit: that callback must not overwrite the suspended onTurn's arguments.
        var vars = AddPrivateVariables(ref script, turn.WorkerIndex, checked(sites.Length * 8));
        var siteVars = sites.Select((rel, index) => (rel, vars: vars.Skip(index * 8).Take(8).ToArray()))
            .ToDictionary(x => x.rel, x => x.vars);
        var emit = new Emission(script, turn.WorkerIndex);
        foreach (AiInstruction op in script.Instructions)
        {
            if (!siteVars.TryGetValue(op.Offset - script.ScriptStart, out ushort[]? v))
            {
                emit.Output.Add(op);
                continue;
            }
            var anchor = new AiInstruction { Offset = op.Offset, Opcode = 0xA0, Operand = v[0],
                HasOperand = true, OperandKind = AiScript_File.OperandKindOf(0xA0) };
            emit.Output.Add(anchor);
            emit.Add(Op(0xA0, v[1])); // resolved target
            if (monsterId is 126 or 142)
            {
                ushort sourceFlare = monsterId == 126 ? (ushort)0x3052 : (ushort)0x6079;
                ushort replacement = monsterId == 126 ? commands.AntiFlareNatus : commands.AntiFlareFlux;
                emit.If(CommandIs(v[0], sourceFlare), () => emit.Add(Op(0xAE, replacement), Op(0xA0, v[0])));
            }
            emit.Add(Op(0x9F, v[1]), Op(0x9F, v[0]), Op(op.Opcode, op.Operand));
            if (monsterId == 124)
                emit.If(CommandIs(v[0], 0x303D), () => emit.Command(Self, 0x3036));
            if (monsterId == 212)
                emit.If(CommandIs(v[0], 0x4000, 0x407F, 0x4080, 0x4062), () => emit.Command(Self, 0x301A));
            if (monsterId == 119)
            {
                emit.If(CommandIs(v[0], 0x407F), () => emit.DifferentLivingTarget(v[1], v[0], v));
                emit.If(CommandIs(v[0], 0x3036), () => emit.RandomCommand(commands.HolyFira));
            }
            if (monsterId == 126)
            {
                emit.If(CommandIs(v[0], 0x303B), () => emit.Command(Self, 0x3036));
                emit.If(CommandIs(v[0], 0x604F), () =>
                {
                    emit.Add(Op(0xB5, 0x00A9), Op(0xAE, 4), Op(0x18), Op(0xA0, v[2]));
                    ushort[] choices = { commands.AntiFira, commands.AntiThundara, commands.AntiWatera, commands.AntiBlizzara };
                    for (ushort index = 0; index < choices.Length; index++)
                    {
                        ushort value = index;
                        emit.If(new[] { Op(0x9F, v[2]), Op(0xAE, value), Op(0x06) },
                            () => emit.RandomCommand(choices[value]));
                    }
                    emit.Command(Self, 0x303A);
                });
            }
        }
        bytes = emit.Build();
        if (monsterId is 124 or 126)
        {
            var action = new[] { Op(0xAE, 0xFFEF), Op(0xAE, monsterId == 124 ? commands.Darkra : commands.Darkga), Op(0xD8, 0x700B) };
            bytes = AiScript_File.AppendGuardedAction(AiScript_File.Read(bytes), hit.WorkerIndex, hit.EntrypointIndex,
                DamageGuard(counterGate: true), action);
        }
        if (monsterId == 124) bytes = AddRequiem(bytes, turn, hit, commands.Requiem);
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(bytes, script, out _, out error))
            throw new InvalidDataException(error);
        return new(bytes, sites.Length);
    }

    static byte[] AddRequiem(byte[] bytes, AiEventHook turn, AiEventHook hit, ushort command)
    {
        // Same moderate charging recipe used by the mod: empty /100, +25 turn,
        // +20 positive-damage hit. A finisher replaces the next ordinary turn.
        AiScriptFile script = AiScript_File.Read(bytes);
        bytes = AiAutomation.AddOverdriveGaugeSetup(script, 100, 0, false, 0, turn.WorkerIndex, 0);
        bytes = AiAutomation.AddOverdriveFinisherSequence(AiScript_File.Read(bytes),
            new[] { (command, AiTargetRecipe.Literal(0xFFF2)) }, true, turn.WorkerIndex, turn.EntrypointIndex, true);
        bytes = AiAutomation.AddOverdriveClampToMax(AiScript_File.Read(bytes), turn.WorkerIndex, turn.EntrypointIndex);
        bytes = AiAutomation.AddOverdriveChargeSource(AiScript_File.Read(bytes), 25,
            new[] { Op(0xAE, 1) }, turn.WorkerIndex, turn.EntrypointIndex);
        bytes = AiAutomation.AddOverdriveClampToMax(AiScript_File.Read(bytes), hit.WorkerIndex, hit.EntrypointIndex);
        bytes = AiAutomation.AddOverdriveChargeSource(AiScript_File.Read(bytes), 20,
            DamageGuard(counterGate: false), hit.WorkerIndex, hit.EntrypointIndex);
        return bytes;
    }

    static List<AiInstruction> DamageGuard(bool counterGate)
    {
        var result = new List<AiInstruction> { Op(0xAE, 0), Op(0xAE, Self), Op(0xAE, 0x00A6),
            Op(0xB5, 0x700F), Op(0x0B), Op(0xAE, Self), Op(0xAE, 4), Op(0xB5, 0x700F), Op(0x02) };
        if (counterGate) result.AddRange(new[] { Op(0xB5, 0x70E0), Op(0x02) });
        return result;
    }

    static List<AiInstruction> CommandIs(ushort variable, params ushort[] commands)
    {
        var result = new List<AiInstruction>();
        foreach (ushort command in commands)
        {
            result.AddRange(new[] { Op(0x9F, variable), Op(0xAE, command), Op(0x06) });
            if (result.Count > 3) result.Add(Op(0x01));
        }
        return result;
    }

    internal static HashSet<int> Reachable(AiScriptFile script, AiEventHook hook)
    {
        var worker = script.Workers[hook.WorkerIndex];
        var nodes = script.Instructions.ToDictionary(op => op.Offset - script.ScriptStart);
        var pending = new Stack<int>();
        var seen = new HashSet<int>();
        pending.Push(worker.Entrypoints[hook.EntrypointIndex]);
        while (pending.TryPop(out int relative))
        {
            if (relative == script.CodeLength || !seen.Add(relative)) continue;
            if (!nodes.TryGetValue(relative, out var op)) throw new InvalidDataException("Invalid native control-flow target.");
            if (op.Opcode is 0xB0 or 0xD6 or 0xD7)
            {
                if (op.Operand >= worker.JumpTargets.Count) throw new InvalidDataException("Invalid native jump slot.");
                pending.Push(worker.JumpTargets[op.Operand]);
                if (op.Opcode == 0xB0) continue;
            }
            if (op.Opcode is not (0x3C or 0x40)) pending.Push(relative + op.Length);
        }
        return seen;
    }

    internal static ushort[] AddPrivateVariables(ref AiScriptFile script, int worker, int count)
    {
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            int end = Math.Max(script.Workers.Max(w => w.PrivateDataLength),
                script.Variables.Where(v => v.Storage == 0x56).Select(v => v.Slot + 4).DefaultIfEmpty().Max());
            int slot = checked((end + 3) / 4 * 4);
            ushort length = checked((ushort)(slot + 4));
            byte[] grown = (byte[])script.OriginalAiFileBytes.Clone();
            int descriptor = script.Workers[worker].DescriptorOffset;
            grown[descriptor + 0x10] = (byte)length;
            grown[descriptor + 0x11] = (byte)(length >> 8);
            script = AiScript_File.Read(grown);
            result[i] = checked((ushort)script.Variables.Count);
            script = AiScript_File.Read(AiScript_File.AppendPrivateVariableDescriptor(script, slot));
        }
        return result;
    }

    internal static AiInstruction Op(byte opcode, ushort operand = 0) => new()
    {
        Offset = -1, Opcode = opcode, Operand = operand,
        HasOperand = AiScript_File.IsOperandBearing(opcode), OperandKind = AiScript_File.OperandKindOf(opcode),
    };

    internal sealed class Emission
    {
        readonly AiScriptFile script;
        readonly int worker;
        readonly List<AiInstruction> labels = new();
        public List<AiInstruction> Output { get; } = new();
        public Emission(AiScriptFile script, int worker) { this.script = script; this.worker = worker; }
        public void Add(params AiInstruction[] ops) => Output.AddRange(ops);
        AiInstruction Label() => Op(0x00);
        void Jump(byte opcode, AiInstruction destination)
        {
            int index = labels.Count;
            labels.Add(destination);
            Add(Op(opcode, checked((ushort)(script.Workers[worker].JumpTargets.Count + index))));
        }
        public void If(IEnumerable<AiInstruction> guard, Action action)
        {
            var done = Label();
            Output.AddRange(guard);
            Jump(0xD7, done);
            action();
            Add(done);
        }
        public void Command(ushort target, ushort command) => Add(Op(0xAE, target), Op(0xAE, command), Op(0xD8, 0x700B));
        public void RandomCommand(ushort command)
        {
            // Canonical alive-frontline selector; preserves the donor's single-target semantics.
            Add(Op(0xAE, 0xFFF2), Op(0xAE, 4), Op(0xAE, 0), Op(0xAE, 0), Op(0xB5, 0x7010),
                Op(0xAE, command), Op(0xD8, 0x700B));
        }
        public void DifferentLivingTarget(ushort previous, ushort command, ushort[] vars)
        {
            // countChrOverlap is the native actor/group intersection predicate.
            // Do not compare raw actor indices against unresolved frontline sentinels.
            for (int i = 0; i < Frontline.Length; i++)
                Add(Op(0xAE, Frontline[i]), Op(0xAE, 4), Op(0xB5, 0x700F),
                    Op(0xAE, Frontline[i]), Op(0x9F, previous), Op(0xB5, 0x701E), Op(0xAE, 0), Op(0x06),
                    Op(0x02), Op(0xA0, vars[3 + i]));
            Add(Op(0x9F, vars[3]), Op(0x9F, vars[4]), Op(0x14), Op(0x9F, vars[5]), Op(0x14), Op(0xA0, vars[6]));
            If(new[] { Op(0xAE, 0), Op(0x9F, vars[6]), Op(0x0B) }, () =>
            {
                Add(Op(0xB5, 0x00A9), Op(0x9F, vars[6]), Op(0x18), Op(0xA0, vars[7]));
                var done = Label();
                for (int i = 0; i < Frontline.Length; i++)
                {
                    int index = i;
                    If(new[] { Op(0x9F, vars[3 + i]) }, () =>
                    {
                        If(new[] { Op(0x9F, vars[7]), Op(0xAE, 0), Op(0x06) }, () =>
                        {
                            Add(Op(0xAE, Frontline[index]), Op(0x9F, command), Op(0xD8, 0x700B));
                            Jump(0xB0, done);
                        });
                        Add(Op(0x9F, vars[7]), Op(0xAE, 1), Op(0x15), Op(0xA0, vars[7]));
                    });
                }
                Add(done);
            });
        }
        public byte[] Build()
        {
            byte[] result = AiScript_File.Rebuild(script, Output);
            var offsets = new Dictionary<AiInstruction, int>();
            int relative = 0;
            foreach (var op in Output) { offsets[op] = relative; relative += op.Length; }
            if (labels.Count != 0)
                result = AiScript_File.GrowWorkerJumpTable(AiScript_File.Read(result), worker,
                    labels.Select(label => offsets[label]).ToArray());
            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result, script, out _, out string error))
                throw new InvalidDataException(error);
            return result;
        }
    }
}
