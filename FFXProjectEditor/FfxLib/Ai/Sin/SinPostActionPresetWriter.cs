using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin;

public sealed record SinPostActionEdit(byte[] AiFile, int NativeActionCount);

/// <summary>In-memory, additive UNI authoring. Original instructions and control flow stay in place.
/// A single pass over the original calls prevents the inserted skills from triggering their own UNI.</summary>
public static class SinPostActionPresetWriter
{
    static readonly ushort[] FrontlineSlots = { 0xFFFA, 0xFFF9, 0xFFF8 };

    public static bool TryBuild(byte[] monster, string monsterId, string presetId,
        out SinPostActionEdit? edit, out string error)
    {
        edit = null;
        error = string.Empty;
        try
        {
            SinPostActionPreset recipe = SinPostActionPresetCatalog.Find(presetId)
                ?? throw new InvalidOperationException($"No post-action UNI recipe for '{presetId}'.");
            if (recipe.RequiredMonster is string required && !required.Equals(monsterId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{recipe.Id} requires {required}.");
            if (recipe.DamageOnly && !new[] { "m198", "m200" }.Contains(monsterId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("UNI-040 requires m198 or m200.");

            byte[] originalAi = AiScript_File.SliceAiFileFromMonster(monster)
                ?? throw new InvalidOperationException("Monster has no AI partition.");
            AiScriptFile original = AiScript_File.Read(originalAi);
            if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, original, out AiEventHook hook, out string why))
                throw new InvalidOperationException(why);
            HashSet<int> calls = FindNativeCalls(original, hook);
            if (calls.Count == 0) throw new InvalidOperationException("No native onTurn command calls.");

            // Repeat baking is deliberately rejected, including applying another post-action UNI over one
            // already baked. Otherwise its generated command calls would be mistaken for native actions.
            HashSet<ushort> custom = SinPostActionPresetCatalog.All.SelectMany(x => x.Skills).Select(s => s.Operand).ToHashSet();
            for (int i = 1; i < original.Instructions.Count; i++)
            {
                AiInstruction op = original.Instructions[i];
                if (calls.Contains(op.Offset) && original.Instructions[i - 1].Opcode == 0xAE
                    && custom.Contains(original.Instructions[i - 1].Operand))
                    throw new InvalidOperationException("An added UNI skill already exists in onTurn. Restore the previous AI before rebaking.");
                if (recipe.DamageOnly && i >= 2 && op.Opcode == 0xD8 && op.Operand == 0x7018
                    && original.Instructions[i - 2].Opcode == 0xAE && original.Instructions[i - 2].Operand == 0x0007)
                    throw new InvalidOperationException("A direct Zombie write already exists. Restore the previous AI before rebaking.");
                if (calls.Contains(op.Offset) && IsReplaySequence(original.Instructions, i))
                    throw new InvalidOperationException("A Mirage replay already exists in onTurn. Restore the previous AI before rebaking.");
            }

            bool sameTarget = recipe.Skills.Any(s => s.Target == SinUniTarget.Previous);
            AiScriptFile script = original;
            ushort[] vars = AddPrivateVariables(ref script, hook.WorkerIndex,
                recipe.DamageOnly ? 3 : recipe.ReplayNative ? 2 : sameTarget ? 1 : 0);
            calls = FindNativeCalls(script, hook);
            int firstJump = script.Workers[hook.WorkerIndex].JumpTargets.Count;
            var skipTargets = new List<AiInstruction>();
            var output = new List<AiInstruction>();
            foreach (AiInstruction op in script.Instructions)
            {
                AiInstruction native = op;
                if (calls.Contains(op.Offset) && (recipe.DamageOnly || recipe.ReplayNative))
                {
                    // Snapshot immediately before the original call. These balanced reads leave its
                    // already-computed target/command arguments untouched on the evaluation stack.
                    int anchorIndex = output.Count;
                    if (recipe.ReplayNative)
                    {
                        // Capture the actual resolved arguments, including indirect commands/targets.
                        // Replaying a target-selection expression could roll a different target.
                        output.Add(Op(0xA0, vars[1]));
                        output.Add(Op(0xA0, vars[0]));
                        output.Add(Op(0x9F, vars[0]));
                        output.Add(Op(0x9F, vars[1]));
                    }
                    else
                        for (int i = 0; i < FrontlineSlots.Length; i++)
                        {
                            output.AddRange(ReadHp(FrontlineSlots[i]));
                            output.Add(Op(0xA0, vars[i]));
                        }
                    // A native branch directly to the consumer must also pass through the snapshots.
                    AiInstruction anchor = output[anchorIndex];
                    output[anchorIndex] = new AiInstruction { Offset = op.Offset, Opcode = anchor.Opcode,
                        Operand = anchor.Operand, HasOperand = anchor.HasOperand, OperandKind = anchor.OperandKind };
                    native = Op(op.Opcode, op.Operand);
                }
                output.Add(native);
                if (!calls.Contains(op.Offset)) continue;
                if (recipe.DamageOnly)
                {
                    for (int i = 0; i < FrontlineSlots.Length; i++)
                    {
                        // A miss, heal or non-damaging action must not afflict the whole party.
                        // Compare fresh numeric HP with this command's snapshot, not stale last-damage.
                        output.AddRange(ReadHp(FrontlineSlots[i]));
                        output.Add(Op(0x9F, vars[i]));
                        output.Add(Op(0x0B)); // currentHP < beforeHP
                        output.Add(Op(0xD7, checked((ushort)(firstJump + skipTargets.Count))));
                        output.AddRange(AiAutomation.BuildChrPropertyWriteAction(FrontlineSlots[i], 0x0007, 1));
                        AiInstruction rejoin = Op(0x00);
                        skipTargets.Add(rejoin);
                        output.Add(rejoin);
                    }
                }
                else if (recipe.ReplayNative)
                {
                    output.Add(Op(0x9F, vars[0]));
                    output.Add(Op(0x9F, vars[1]));
                    output.Add(Op(0xD8, op.Operand));
                }
                else
                {
                    foreach (SinUniSkill skill in recipe.Skills)
                    {
                        if (skill.Target == SinUniTarget.Previous)
                            output.Add(Op(0x9F, vars[0]));
                        else
                        {
                            output.AddRange(Target(skill.Target));
                            if (sameTarget)
                            {
                                output.Add(Op(0xA0, vars[0]));
                                output.Add(Op(0x9F, vars[0]));
                            }
                        }
                        output.Add(Op(0xAE, skill.Operand));
                        output.Add(Op(0xD8, op.Operand)); // preserve native perform / forcePerform mode
                    }
                    if (recipe.ForbiddenField is ushort field)
                    {
                        // The existing Forbidden Rite contract: [actor][field][value] CALLPOPA 7018.
                        // UNI-039 chooses a random living party member after Willbreaker, as designed.
                        output.AddRange(Target(SinUniTarget.RandomFrontline));
                        output.Add(Op(0xAE, field));
                        output.Add(Op(0xAE, 1));
                        output.Add(Op(0xD8, 0x7018));
                    }
                }
                // Do not insert RET here: the native tail may update its rotation state or execute
                // another native action. Its existing exit runs only AFTER the entire UNI sequence.
            }

            byte[] rebuilt = AiScript_File.Rebuild(script, output);
            if (skipTargets.Count > 0)
            {
                int relative = 0;
                var offsets = new Dictionary<AiInstruction, int>();
                foreach (AiInstruction op in output) { offsets[op] = relative; relative += op.Length; }
                rebuilt = AiScript_File.GrowWorkerJumpTable(AiScript_File.Read(rebuilt), hook.WorkerIndex,
                    skipTargets.Select(op => offsets[op]).ToArray());
            }
            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(rebuilt, original, originalAi.Length, out _, out why))
                throw new InvalidOperationException(why);
            edit = new SinPostActionEdit(rebuilt, calls.Count);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidDataException)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool IsReplaySequence(IReadOnlyList<AiInstruction> ops, int call)
    {
        if (call < 4 || call + 3 >= ops.Count) return false;
        AiInstruction command = ops[call - 4], target = ops[call - 3];
        return command.Opcode == 0xA0 && target.Opcode == 0xA0 && command.Operand != target.Operand
            && ops[call - 2].Opcode == 0x9F && ops[call - 2].Operand == target.Operand
            && ops[call - 1].Opcode == 0x9F && ops[call - 1].Operand == command.Operand
            && ops[call + 1].Opcode == 0x9F && ops[call + 1].Operand == target.Operand
            && ops[call + 2].Opcode == 0x9F && ops[call + 2].Operand == command.Operand
            && ops[call + 3].Opcode == 0xD8 && ops[call + 3].Operand == ops[call].Operand;
    }

    static HashSet<int> FindNativeCalls(AiScriptFile script, AiEventHook hook)
    {
        HashSet<int> active = Reachable(script, hook.WorkerIndex, hook.EntrypointIndex);
        var calls = script.Instructions.Where(op => active.Contains(op.Offset) && op.Opcode is 0xD8 or 0xB5
            && op.Operand is 0x700B or 0x705A).ToArray();
        if (calls.Any(op => op.Opcode != 0xD8))
            throw new InvalidOperationException("A command call returns a value; this post-action writer requires CALLPOPA.");
        HashSet<int> result = calls.Select(op => op.Offset).ToHashSet();
        foreach (AiWorker worker in script.Workers)
            for (int ei = 0; ei < worker.Entrypoints.Count; ei++)
            {
                if (worker.Index == hook.WorkerIndex && ei == hook.EntrypointIndex) continue;
                if (Reachable(script, worker.Index, ei).Overlaps(result))
                    throw new InvalidOperationException("onTurn shares a command site with another event; isolate that route before baking.");
            }
        return result;
    }

    static HashSet<int> Reachable(AiScriptFile script, int workerIndex, int entrypoint)
    {
        AiWorker worker = script.Workers[workerIndex];
        var nodes = script.Instructions.ToDictionary(op => op.Offset - script.ScriptStart);
        var pending = new Stack<int>();
        var seen = new HashSet<int>();
        pending.Push(worker.Entrypoints[entrypoint]);
        while (pending.TryPop(out int relative))
        {
            if (relative == script.CodeLength || !seen.Add(relative)) continue;
            if (!nodes.TryGetValue(relative, out AiInstruction? op))
                throw new InvalidOperationException($"Invalid control-flow target 0x{relative:X}.");
            if (op.Opcode is 0xB0 or 0xD6 or 0xD7)
            {
                if (op.Operand >= worker.JumpTargets.Count)
                    throw new InvalidOperationException("Invalid native jump slot.");
                pending.Push(worker.JumpTargets[op.Operand]);
                if (op.Opcode == 0xB0) continue;
            }
            if (op.Opcode is not (0x3C or 0x40)) pending.Push(relative + op.Length);
        }
        return seen.Where(x => x < script.CodeLength).Select(x => x + script.ScriptStart).ToHashSet();
    }

    static ushort[] AddPrivateVariables(ref AiScriptFile script, int workerIndex, int count)
    {
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            int end = Math.Max(script.Workers.Max(w => w.PrivateDataLength),
                script.Variables.Where(v => v.Storage == 0x56).Select(v => v.Slot + 4).DefaultIfEmpty().Max());
            int slot = checked((end + 3) / 4 * 4);
            ushort length = checked((ushort)(slot + 4));
            byte[] grown = (byte[])script.OriginalAiFileBytes.Clone();
            int descriptor = script.Workers[workerIndex].DescriptorOffset;
            grown[descriptor + 0x10] = (byte)length;
            grown[descriptor + 0x11] = (byte)(length >> 8);
            script = AiScript_File.Read(grown);
            result[i] = checked((ushort)script.Variables.Count);
            script = AiScript_File.Read(AiScript_File.AppendPrivateVariableDescriptor(script, slot));
        }
        return result;
    }

    static IEnumerable<AiInstruction> ReadHp(ushort actor) => new[] { Op(0xAE, actor), Op(0xAE, 0), Op(0xB5, 0x700F) };

    static IEnumerable<AiInstruction> Target(SinUniTarget target)
    {
        if (target is SinUniTarget.RandomFrontline or SinUniTarget.RandomAlly)
            return new[] { Op(0xAE, target == SinUniTarget.RandomAlly ? (ushort)0xFFF1 : (ushort)0xFFF2),
                Op(0xAE, 4), Op(0xAE, 0), Op(0xAE, 0), Op(0xB5, 0x7010) };
        return new[] { Op(0xAE, target switch
        {
            SinUniTarget.Self => 0xFFF3, SinUniTarget.Frontline => 0xFFF2, SinUniTarget.Allies => 0xFFF1,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        }) };
    }

    static AiInstruction Op(byte opcode, ushort operand = 0) => new()
    {
        Offset = -1, Opcode = opcode, HasOperand = AiScript_File.IsOperandBearing(opcode),
        Operand = operand, OperandKind = AiScript_File.OperandKindOf(opcode),
    };
}
