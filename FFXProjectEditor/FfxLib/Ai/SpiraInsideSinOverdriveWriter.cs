using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai;

/// <summary>Native monster OD. The AiFile owns its visible gauge, charge, clamp
/// and finisher, including sins05 as authorized by the user. A zero maximum
/// remains a safe disabled state; no runtime region gate is required.</summary>
public static class SpiraInsideSinOverdriveWriter
{
    const ushort Self = 0xFFF3, Current = 0x13, Max = 0x14, Show = 0x89;
    static AiInstruction Op(byte code, ushort value = 0) => new()
    {
        Offset = -1, Opcode = code, Operand = value,
        HasOperand = AiScript_File.OperandKindOf(code) != AiOperandKind.None,
        OperandKind = AiScript_File.OperandKindOf(code),
    };
    static List<AiInstruction> Read(ushort field) => [Op(0xAE, Self), Op(0xAE, field), Op(0xB5, 0x700F)];
    static List<AiInstruction> Enabled() => AiAutomation.BuildSelfFieldGreaterThanGuard(Max, 0);
    static List<AiInstruction> CurrentVsMax(byte comparison)
        => AiAutomation.BuildAndGuard(Enabled(), Read(Max).Concat(Read(Current)).Append(Op(comparison)).ToArray());

    public static byte[] Build(byte[] source, int monsterId, ushort command)
        => BuildCore(source, monsterId, command, omega: false);

    public static byte[] BuildOmega(byte[] source, int monsterId, ushort command)
        => BuildCore(source, monsterId, command, omega: true);

    static byte[] BuildCore(byte[] source, int monsterId, ushort command, bool omega)
    {
        bool eligible = omega ? monsterId is 188 or 224 or 910 : monsterId is 65 or 86 or 210;
        bool elite = omega ? monsterId is 188 or 224 : monsterId == 86;
        if (!eligible || (command & 0xF000) != 0x6000)
            throw new ArgumentException("Unsupported native overdrive identity.");
        if (Monster_File.Read(source).StatSheetFile?.MonsterId != monsterId)
            throw new InvalidDataException("Monster identity mismatch.");
        byte[] oldAi = AiScript_File.SliceAiFileFromMonster(source) ?? throw new InvalidDataException("Missing AI.");
        var original = AiScript_File.Read(oldAi);
        if (!AiWorkerMapping.TryResolveCombatOnTurn(source, original, out var turn, out string error)
            || !AiWorkerMapping.TryResolveCombatOnHit(source, original, out var hit, out error)
            || turn.WorkerIndex != hit.WorkerIndex) throw new InvalidDataException(error);
        if (AiAutomation.CountGeneratedOverdriveLabBlocks(original) != 0
            || original.Instructions.Any(op => op.Opcode == 0xAE && op.Operand == command))
            throw new InvalidOperationException("An authored overdrive already exists; do not stack it.");
        byte[] ai = AiAutomation.AddChrPropertyValueWritesWithGuard(original, Self,
            new[] { (Show, (ushort)1), (Max, (ushort)100), (Current, (ushort)0) },
            AiAutomation.BuildAlwaysGuard(), turn.WorkerIndex, 0);
        var finisher = new List<AiInstruction> { Op(0xAE, 0xFFF2), Op(0xAE, command), Op(0xD8, 0x700B) };
        finisher.AddRange(AiAutomation.BuildChrPropertyWriteAction(Self, Current, 0));
        ai = AiScript_File.AppendGuardedAction(AiScript_File.Read(ai), turn.WorkerIndex, turn.EntrypointIndex,
            CurrentVsMax(0x0F), finisher, stopAfterAction: true); // [max, current] + LE
        byte[] Clamp(byte[] input, AiEventHook hook)
        {
            var action = new List<AiInstruction> { Op(0xAE, Self), Op(0xAE, Current) };
            action.AddRange(Read(Max)); action.Add(Op(0xD8, 0x7018));
            return AiScript_File.AppendGuardedAction(AiScript_File.Read(input), hook.WorkerIndex, hook.EntrypointIndex,
                CurrentVsMax(0x0B), action);
        }
        ai = Clamp(ai, turn);
        ai = AiAutomation.AddOverdriveChargeSource(AiScript_File.Read(ai), 25, Enabled(), turn.WorkerIndex, turn.EntrypointIndex);
        ai = Clamp(ai, hit);
        var guard = AiAutomation.BuildAndGuard(Enabled(), AiAutomation.BuildSelfFieldGreaterThanGuard(0xA6, 0),
            AiAutomation.BuildSelfFieldGreaterThanGuard(4, 0));
        if (elite) guard = AiAutomation.BuildAndGuard(guard, AiAutomation.BuildLastDamageTakenAtLeastPercentMaxHpGuard(25));
        ai = AiAutomation.AddOverdriveChargeSource(AiScript_File.Read(ai), (ushort)(elite ? 30 : 20),
            guard, hit.WorkerIndex, hit.EntrypointIndex);
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(ai, original, out _, out error))
            throw new InvalidDataException(error);
        if (!AiScript_File.Write(AiScript_File.Read(ai)).AsSpan().SequenceEqual(ai)) throw new InvalidDataException("AI RT0 failed.");
        byte[] output = AiScript_File.SpliceAiFileIntoMonsterGrow(source, ai);
        if (!Monster_File.Read(output).Write().AsSpan().SequenceEqual(output)) throw new InvalidDataException("Monster RT0 failed.");
        if (!AiScript_File.SpliceAiFileIntoMonsterGrow(output, oldAi).AsSpan().SequenceEqual(source))
            throw new InvalidDataException("OD changed data outside the AI partition.");
        return output;
    }

    /// <summary>Execute only the newly appended wrappers, stopping at the original
    /// native entrypoint. This is an offline contract interpreter, not gameplay proof.</summary>
    public static (bool OriginalReached, int Commands, int Current, int Max, int Show) Probe(
        byte[] before, byte[] after, bool onHit, int current, int max, int hp, int damage, bool alive = true, bool init = false)
    {
        var original = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(before)!);
        var script = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(after)!);
        bool ok = onHit ? AiWorkerMapping.TryResolveCombatOnHit(before, original, out var hook, out var error)
            : AiWorkerMapping.TryResolveCombatOnTurn(before, original, out hook, out error);
        if (!ok) throw new InvalidDataException(error);
        int entry = init ? 0 : hook.EntrypointIndex;
        var oldWorker = original.Workers[hook.WorkerIndex]; var worker = script.Workers[hook.WorkerIndex];
        int stop = oldWorker.Entrypoints[entry], pc = worker.Entrypoints[entry], commands = 0;
        var instructions = script.Instructions.ToDictionary(i => i.Offset - script.ScriptStart);
        var stack = new Stack<int>();
        var fields = new Dictionary<int, int> { [Current] = current, [Max] = max, [Show] = 9, [2] = hp, [4] = alive ? 1 : 0, [0xA6] = damage };
        (bool, int, int, int, int) Result(bool reached)
        {
            if (stack.Count != 0) throw new InvalidDataException("Wrapper leaves a nonempty stack.");
            return (reached, commands, fields[Current], fields[Max], fields[Show]);
        }
        for (int steps = 0; steps < 400; steps++)
        {
            if (pc == stop) return Result(true);
            var op = instructions[pc]; pc += op.HasOperand ? 3 : 1;
            switch (op.Opcode)
            {
                case 0xAE: stack.Push(op.Operand); break;
                case 0xB5:
                    if (op.Operand != 0x700F) throw new InvalidDataException("Unexpected wrapper call.");
                    int field = stack.Pop(); if (stack.Pop() != Self) throw new InvalidDataException("Non-self field read.");
                    stack.Push(fields[field]); break;
                case 0xD8:
                    if (op.Operand == 0x7018)
                    {
                        int value = stack.Pop(), property = stack.Pop();
                        if (stack.Pop() != Self) throw new InvalidDataException("Non-self field write.");
                        fields[property] = value;
                    }
                    else if (op.Operand == 0x700B) { _ = stack.Pop(); _ = stack.Pop(); commands++; }
                    else throw new InvalidDataException("Unexpected wrapper action.");
                    break;
                case 0x02: { int a = stack.Pop(), b = stack.Pop(); stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x0B: { int a = stack.Pop(), b = stack.Pop(); stack.Push(a > b ? 1 : 0); break; }
                case 0x0F: { int a = stack.Pop(), b = stack.Pop(); stack.Push(a >= b ? 1 : 0); break; }
                case 0x14: stack.Push(stack.Pop() + stack.Pop()); break;
                case 0x16: stack.Push(stack.Pop() * stack.Pop()); break;
                case 0x17: { int divisor = stack.Pop(); stack.Push(stack.Pop() / divisor); break; }
                case 0x18: { int divisor = stack.Pop(); stack.Push(stack.Pop() % divisor); break; }
                case 0xB0: pc = worker.JumpTargets[op.Operand]; break;
                case 0xD7: if (stack.Pop() == 0) pc = worker.JumpTargets[op.Operand]; break;
                case 0x3C: return Result(false);
                default: throw new InvalidDataException($"Unexpected wrapper opcode {op.Opcode:X2}.");
            }
        }
        throw new InvalidDataException("Wrapper did not terminate.");
    }
}
