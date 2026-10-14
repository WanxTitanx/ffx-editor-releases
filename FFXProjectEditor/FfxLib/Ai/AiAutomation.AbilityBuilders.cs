using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1: builders de guard/ability extraídos do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
        static List<AiInstruction> AlwaysGuard() => new() { Op(PUSHII, 1) };
        static List<AiInstruction> RngGuard(int k) =>
            new() { Op(CALL, GetRandomValue), Op(PUSHII, (ushort)(k < 2 ? 2 : k)), Op(MOD), Op(PUSHII, 0), Op(EQ) };
        // single-stack-item pushes — the only "target" shapes droppable together with the command push (stack-neutral).
        static readonly HashSet<byte> SinglePush = new() { 0xAD, 0xAE, 0xAF, 0x9F, PUSHI0, 0x68, 0x69, 0x6A };

        static AiInstruction Op(byte opcode, ushort operand = 0) => new AiInstruction
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        // ── worker / entrypoint auto-pick ───────────────────────────────────────────────────────────────────
        /// <summary>Prefer the inferred combat worker (it decides actions); fall back to the first worker with
        /// entrypoints. Null only when no worker has an entrypoint to hook.</summary>
        public static AiWorker? PickCombatWorker(AiScriptFile script) =>
            script.Workers.FirstOrDefault(w => w.Entrypoints.Count > 0 && w.InferredType == "CombatHandler")
            ?? script.Workers.FirstOrDefault(w => w.Entrypoints.Count > 0);

        /// <summary>Heuristic "main loop" entrypoint index: the one whose code span (to the next entrypoint, or
        /// codeLength) is largest — i.e. the biggest body, most likely the per-turn handler rather than a tiny
        /// init/return stub. NOT byte-proven; reported transparently and flagged RT2-pending by the caller.</summary>
        public static int PickMainEntrypoint(AiScriptFile script, AiWorker worker)
        {
            int code = script.CodeLength;
            int best = 0, bestSpan = -1;
            for (int k = 0; k < worker.Entrypoints.Count; k++)
            {
                int e = worker.Entrypoints[k];
                int next = code;
                foreach (int other in worker.Entrypoints)
                    if (other > e && other < next) next = other;
                int span = next - e;
                if (span > bestSpan) { bestSpan = span; best = k; }
            }
            return best;
        }

        // ── ➕ add ability ──────────────────────────────────────────────────────────────────────────────────
        /// <summary>Build the legacy synthesized (guard, action) pair for forcing a command. Kept for low-level
        /// experiments; the public editor path now copies an existing monster action as the command template.</summary>
        public static (List<AiInstruction> guard, List<AiInstruction> action) BuildForceAbility(ushort commandOperand, bool random, int k)
        {
            List<AiInstruction> guard = BuildAbilityGuard(random, k);
            var action = new List<AiInstruction> { Op(PUSHII, SelfTarget), Op(PUSHII, commandOperand), Op(CALLPOPA, ForcePerformCommand) };
            return (guard, action);
        }

        /// <summary>Build the default synthesized (guard, action) pair for a queued command. This is the human-editor
        /// default: chance/random controls WHEN the block runs; it must not silently flip the command into force mode.</summary>
        public static (List<AiInstruction> guard, List<AiInstruction> action) BuildQueuedAbility(ushort commandOperand, bool random, int k) =>
            BuildQueuedAbility(commandOperand, random, k, SelfTarget);

        public static (List<AiInstruction> guard, List<AiInstruction> action) BuildQueuedAbility(ushort commandOperand, bool random, int k, ushort targetOperand)
        {
            List<AiInstruction> guard = BuildAbilityGuard(random, k);
            var action = new List<AiInstruction> { Op(PUSHII, targetOperand), Op(PUSHII, commandOperand), Op(CALLPOPA, PerformCommand) };
            return (guard, action);
        }

        public static (List<AiInstruction> guard, List<AiInstruction> action) BuildQueuedAbility(
            ushort commandOperand, bool random, int k, AiTargetRecipe targetRecipe)
        {
            List<AiInstruction> guard = BuildAbilityGuard(random, k);
            var action = new List<AiInstruction>();
            AddTargetToStack(action, targetRecipe);
            action.Add(Op(PUSHII, commandOperand));
            action.Add(Op(CALLPOPA, PerformCommand));
            return (guard, action);
        }

        /// <summary>Build a direct battle-actor status write. Public because the human editor can intentionally link
        /// this payload after a command, making "the second spell applies Silence" / "the third applies Darkness"
        /// without modifying the command definition itself.</summary>
        public static List<AiInstruction> BuildChrPropertyWriteAction(ushort targetOperand, ushort fieldId, ushort value) =>
            new()
            {
                Op(PUSHII, targetOperand),
                Op(PUSHII, fieldId),
                Op(PUSHII, value),
                Op(CALLPOPA, WriteChrProperty),
            };

        public static List<AiInstruction> BuildSetStatFieldAction(ushort fieldId, ushort value) =>
            new()
            {
                Op(PUSHII, fieldId),
                Op(PUSHII, value),
                Op(CALLPOPA, SetStatField),
            };

        static List<AiInstruction> BuildChrPropertyWriteAction(AiInstruction targetPush, ushort fieldId, ushort value) =>
            new()
            {
                CloneForInsert(targetPush),
                Op(PUSHII, fieldId),
                Op(PUSHII, value),
                Op(CALLPOPA, WriteChrProperty),
            };

        static void AddFindAliveFrontlineAnyToStack(List<AiInstruction> action)
        {
            action.Add(Op(PUSHII, FrontlineChars));
            action.Add(Op(PUSHII, ChrFieldIsAlive));
            action.Add(Op(PUSHII, 0));
            action.Add(Op(PUSHII, SelectorAny));
            action.Add(Op(CALL, FindMatchingChr));
        }

        static void AddFindLowestHpAliveFrontlineToStack(List<AiInstruction> action)
        {
            // Shred's idiom: first seed the MatchingGroup with alive frontline actors, then select the lowest HP.
            action.Add(Op(PUSHII, FrontlineChars));
            action.Add(Op(PUSHII, ChrFieldIsAlive));
            action.Add(Op(PUSHII, 0));
            action.Add(Op(PUSHII, SelectorAny));
            action.Add(Op(CALLPOPA, FindMatchingChr));
            action.Add(Op(PUSHII, MatchingGroup));
            action.Add(Op(PUSHII, ChrFieldHp));
            action.Add(Op(PUSHII, 0));
            action.Add(Op(PUSHII, SelectorLowest));
            action.Add(Op(CALL, FindMatchingChr));
        }

        static void AddTargetToStack(List<AiInstruction> action, AiTargetRecipe recipe)
        {
            switch (recipe.Kind)
            {
                case AiTargetRecipeKind.Literal:
                    action.Add(Op(PUSHII, recipe.LiteralOperand));
                    break;
                case AiTargetRecipeKind.FindAliveFrontlineAny:
                    AddFindAliveFrontlineAnyToStack(action);
                    break;
                case AiTargetRecipeKind.FindAliveFrontlineLowestHp:
                    AddFindLowestHpAliveFrontlineToStack(action);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(recipe), $"Unknown target recipe: {recipe.Kind}");
            }
        }

        static void AddTargetToTemp0(List<AiInstruction> action, AiTargetRecipe recipe)
        {
            AddTargetToStack(action, recipe);
            action.Add(Op(POPI0));
        }

        static List<AiInstruction> BuildAbilityGuard(bool random, int k) =>
            random
                ? new() { Op(CALL, GetRandomValue), Op(PUSHII, (ushort)(k < 2 ? 2 : k)), Op(MOD), Op(PUSHII, 0), Op(EQ) }
                : new() { Op(PUSHII, 1) };

        public static List<AiInstruction> BuildAlwaysGuard() => AlwaysGuard();

        public static List<AiInstruction> BuildRandomGuard(int k) =>
            RngGuard(k).Select(i => CloneForInsert(i)).ToList();

        public static List<AiInstruction> BuildAndGuard(params IReadOnlyList<AiInstruction>[] guards)
        {
            var live = guards.Where(g => g != null && g.Count > 0).ToList();
            if (live.Count == 0) return AlwaysGuard();
            var result = live[0].Select(i => CloneForInsert(i)).ToList();
            for (int i = 1; i < live.Count; i++)
            {
                result.AddRange(live[i].Select(x => CloneForInsert(x)));
                result.Add(Op(LAND));
            }
            return result;
        }

        public static List<AiInstruction> BuildOrGuard(params IReadOnlyList<AiInstruction>[] guards)
        {
            var live = guards.Where(g => g != null && g.Count > 0).ToList();
            if (live.Count == 0) return AlwaysGuard();
            var result = live[0].Select(i => CloneForInsert(i)).ToList();
            for (int i = 1; i < live.Count; i++)
            {
                result.AddRange(live[i].Select(x => CloneForInsert(x)));
                result.Add(Op(LOR));
            }
            return result;
        }

        // ceil(maxHP * percent / 100), without overflowing signed I32 even at
        // maxHP=int.MaxValue. q*p + ceil(r*p/100), q=maxHP/100, r=maxHP%100.
        // Keeping the remainder is necessary at odd HP boundaries (e.g. 101).
        static void AddHpPercentCeilingToStack(List<AiInstruction> result, ushort percent)
        {
            AddReadSelfFieldToStack(result, ChrFieldMaxHp);
            result.Add(Op(PUSHII, 100)); result.Add(Op(DIV));
            result.Add(Op(PUSHII, percent)); result.Add(Op(MUL));
            AddReadSelfFieldToStack(result, ChrFieldMaxHp);
            result.Add(Op(PUSHII, 100)); result.Add(Op(MOD));
            result.Add(Op(PUSHII, percent)); result.Add(Op(MUL));
            result.Add(Op(PUSHII, 99)); result.Add(Op(ADD));
            result.Add(Op(PUSHII, 100)); result.Add(Op(DIV));
            result.Add(Op(ADD));
        }

        public static List<AiInstruction> BuildHpBelowPercentGuard(ushort percent)
        {
            ushort value = (ushort)Math.Clamp(percent == 0 ? 50 : (int)percent, 1, 100);
            var guard = new List<AiInstruction>();
            AddReadSelfFieldToStack(guard, ChrFieldHp);
            AddHpPercentCeilingToStack(guard, value);
            guard.Add(Op(LT));
            return guard;
        }

        /// <summary>Last damage >= the requested percentage of numeric maxHP.</summary>
        public static List<AiInstruction> BuildLastDamageTakenAtLeastPercentMaxHpGuard(ushort percent)
        {
            ushort value = (ushort)Math.Clamp(percent == 0 ? 25 : (int)percent, 1, 100);
            var guard = new List<AiInstruction>();
            AddHpPercentCeilingToStack(guard, value);
            AddReadSelfFieldToStack(guard, ChrFieldLastDamageTakenHp);
            guard.Add(Op(LE));
            return guard;
        }

        public static List<AiInstruction> BuildLastDamageTakenEqualsGuard(ushort value)
        {
            var guard = new List<AiInstruction>();
            AddReadSelfFieldToStack(guard, ChrFieldLastDamageTakenHp);
            guard.Add(Op(PUSHII, value));
            guard.Add(Op(EQ));
            return guard;
        }

        public static List<AiInstruction> BuildUsedCommandDamageTypeGuard(ushort damageType)
        {
            var guard = new List<AiInstruction>
            {
                Op(CALL, UsedCommand),
                Op(PUSHII, MoveFieldDamageType),
                Op(CALL, ReadMoveProperty),
                Op(PUSHII, damageType),
                Op(EQ),
            };
            return guard;
        }

        /// <summary>Numeric HP range: lower inclusive, upper exclusive.</summary>
        public static List<AiInstruction> BuildHpBetweenPercentGuard(ushort minPercent, ushort maxPercent)
        {
            ushort lo = (ushort)Math.Clamp((int)minPercent, 0, 100);
            ushort hi = (ushort)Math.Clamp(maxPercent == 0 ? 100 : (int)maxPercent, 1, 100);
            if (hi <= lo) hi = (ushort)Math.Min(100, lo + 1);

            var lower = new List<AiInstruction>();
            AddHpPercentCeilingToStack(lower, lo);
            AddReadSelfFieldToStack(lower, ChrFieldHp);
            lower.Add(Op(LE));
            var upper = new List<AiInstruction>();
            AddReadSelfFieldToStack(upper, ChrFieldHp);
            AddHpPercentCeilingToStack(upper, hi);
            upper.Add(Op(LT));
            return BuildAndGuard(lower, upper);
        }

        public static List<AiInstruction> BuildSelfFieldGreaterThanGuard(ushort fieldId, ushort threshold)
        {
            var guard = new List<AiInstruction>();
            // field > threshold → emit [threshold, field] + LT (threshold < field).
            guard.Add(Op(PUSHII, threshold));
            AddReadSelfFieldToStack(guard, fieldId);
            guard.Add(Op(LT));
            return guard;
        }

        public static List<AiInstruction> BuildAnyPositiveSelfStatusGuard()
        {
            return BuildOrGuard(SelfBuffPresets
                .Select(p => (IReadOnlyList<AiInstruction>)BuildSelfFieldGreaterThanGuard(p.FieldId, 0))
                .ToArray());
        }

        public static List<AiInstruction> BuildActorFieldGreaterThanGuard(ushort targetOperand, ushort fieldId, ushort threshold)
        {
            var guard = new List<AiInstruction>();
            // field > threshold → emit [threshold, field] + LT (threshold < field).
            guard.Add(Op(PUSHII, threshold));
            AddReadActorFieldToStack(guard, targetOperand, fieldId);
            guard.Add(Op(LT));
            return guard;
        }

        public static List<AiInstruction> BuildEveryNTurnsGuard(ushort interval)
        {
            ushort n = (ushort)Math.Clamp(interval == 0 ? 3 : (int)interval, 2, 999);
            var guard = new List<AiInstruction>();
            AddReadSelfFieldToStack(guard, ChrFieldTurnsTaken);
            guard.Add(Op(PUSHII, n));
            guard.Add(Op(MOD));
            guard.Add(Op(PUSHII, 0));
            guard.Add(Op(EQ));
            return guard;
        }

        /// <summary>First time this actor takes a turn: TurnsTaken is often 0 or 1 at onTurn (not only 0).</summary>
        public static List<AiInstruction> BuildFirstTurnGuard()
        {
            var guard = new List<AiInstruction>();
            AddReadSelfFieldToStack(guard, ChrFieldTurnsTaken);
            guard.Add(Op(PUSHII, 2));
            guard.Add(Op(LT));
            return guard;
        }

        public static List<AiInstruction> BuildFrontlineDeadExistsGuard()
        {
            var guard = new List<AiInstruction>
            {
                Op(PUSHII, FrontlineChars),
                Op(PUSHII, ChrFieldIsAlive),
                Op(PUSHII, 0),
                Op(PUSHII, SelectorAny),
                Op(CALL, FindMatchingChr),
                Op(PUSHII, 0),
                Op(NE),
            };
            return guard;
        }

        static AiInstruction CloneForInsert(AiInstruction ins, ushort? operand = null) => new AiInstruction
        {
            Offset = -1,
            Opcode = ins.Opcode,
            HasOperand = ins.HasOperand,
            Operand = operand ?? ins.Operand,
            OperandKind = ins.OperandKind,
        };

        /// <summary>Copy a real command action from this monster and replace ONLY the command operand. This preserves
        /// the original target push and the perform/forcePerform mode instead of inventing a synthetic self/force
        /// command. Null means the selected action is not a simple stack-neutral command statement.</summary>
        public static List<AiInstruction>? BuildCopiedCommandAction(AiScriptFile script, AiDetectedAction template, ushort commandOperand)
        {
            if (template.Kind != AiActionKind.Command || !template.Removable || template.RemoveOffsets.Count != 3)
                return null;

            var src = template.RemoveOffsets.OrderBy(o => o)
                .Select(off => script.Instructions.FirstOrDefault(i => i.Offset == off))
                .ToList();
            if (src.Any(i => i == null)) return null;

            AiInstruction cmd = src.First(i => i!.Offset == template.CmdPushOffset)!;
            AiInstruction call = src.First(i => i!.Offset == template.CallOffset)!;
            AiInstruction? target = src.FirstOrDefault(i => i!.Offset != template.CmdPushOffset && i.Offset != template.CallOffset);
            if (target == null) return null;
            if (!SinglePush.Contains(target.Opcode)) return null;
            if (cmd.Opcode != PUSHII || !AiCommandId.IsCommandOperand(cmd.Operand)) return null;
            if (call.Opcode != CALLPOPA || (call.Operand != PerformCommand && call.Operand != ForcePerformCommand)) return null;

            return src.Select(i => CloneForInsert(i!, i!.Offset == template.CmdPushOffset ? commandOperand : null)).ToList();
        }

        /// <summary>Append a guarded command copied from an existing action template. The guard controls when the
        /// copied action runs; the action itself keeps the monster's real target/mode shape and changes only the
        /// selected ability id.</summary>
        public static byte[] AddAbilityFromActionTemplate(AiScriptFile script, AiDetectedAction template, ushort commandOperand,
            bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            List<AiInstruction> guard = random ? RngGuard(k) : AlwaysGuard();
            List<AiInstruction> action = BuildCopiedCommandAction(script, template, commandOperand)
                ?? throw new InvalidOperationException(Strings.F2_select_a_simple_command_action_from_the__41787355);
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        /// <summary>Build the default guarded queued command and append it to an explicit worker/entrypoint hook.</summary>
        public static byte[] AddAbility(AiScriptFile script, ushort commandOperand, bool random, int k,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            return AddAbility(script, commandOperand, random, k, workerIndex, entrypointIndex, SelfTarget, stopAfterAction);
        }

        public static byte[] AddAbility(AiScriptFile script, ushort commandOperand, bool random, int k,
            int workerIndex, int entrypointIndex, ushort targetOperand, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            (List<AiInstruction> guard, List<AiInstruction> action) = BuildQueuedAbility(commandOperand, random, k, targetOperand);
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        public static byte[] AddAbility(AiScriptFile script, ushort commandOperand, bool random, int k,
            int workerIndex, int entrypointIndex, AiTargetRecipe targetRecipe, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            (List<AiInstruction> guard, List<AiInstruction> action) = BuildQueuedAbility(commandOperand, random, k, targetRecipe);
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        /// <summary>Append a queued command under a caller-built guard. Used by YUNALESCA so human conditions
        /// such as HP&lt;50% can drive a real spell command instead of a synthetic status bundle.</summary>
        public static byte[] AddQueuedAbilityWithGuard(AiScriptFile script, ushort commandOperand,
            IReadOnlyList<AiInstruction> guard, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));

            var action = new List<AiInstruction>
            {
                Op(PUSHII, SelfTarget),
                Op(PUSHII, commandOperand),
                Op(CALLPOPA, PerformCommand),
            };
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        public static byte[] AddAbilityWithGuard(AiScriptFile script, ushort commandOperand,
            AiTargetRecipe targetRecipe, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));

            var action = new List<AiInstruction>();
            AddTargetToStack(action, targetRecipe);
            action.Add(Op(PUSHII, commandOperand));
            action.Add(Op(CALLPOPA, PerformCommand));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        public static byte[] AddAbilityFromActionTemplateWithGuard(AiScriptFile script, AiDetectedAction template,
            ushort commandOperand, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));

            List<AiInstruction> action = BuildCopiedCommandAction(script, template, commandOperand)
                ?? throw new InvalidOperationException(Strings.F2_select_a_simple_command_action_from_the__cbe7a976);
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>Append a guarded queued command followed immediately by a direct status write. The guard controls
        /// the pair as one behaviour: if the command is skipped by chance/condition, the status write is skipped too.</summary>
        public static byte[] AddAbilityWithChrPropertyWrite(AiScriptFile script, ushort commandOperand,
            ushort targetOperand, ushort fieldId, ushort value, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            return AddAbilityWithChrPropertyWrite(script, commandOperand, SelfTarget, targetOperand, fieldId, value, random, k, workerIndex, entrypointIndex, stopAfterAction);
        }

        public static byte[] AddAbilityWithChrPropertyWrite(AiScriptFile script, ushort commandOperand,
            ushort commandTargetOperand, ushort statusTargetOperand, ushort fieldId, ushort value, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            (List<AiInstruction> guard, List<AiInstruction> action) = BuildQueuedAbility(commandOperand, random, k, commandTargetOperand);
            action.AddRange(BuildChrPropertyWriteAction(statusTargetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        public static byte[] AddAbilityWithChrPropertyWrite(AiScriptFile script, ushort commandOperand,
            AiTargetRecipe commandTargetRecipe, ushort statusTargetOperand, ushort fieldId, ushort value, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            (List<AiInstruction> guard, List<AiInstruction> action) = BuildQueuedAbility(commandOperand, random, k, commandTargetRecipe);
            action.AddRange(BuildChrPropertyWriteAction(statusTargetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        public static byte[] AddAbilityWithChrPropertyWriteWithGuard(AiScriptFile script, ushort commandOperand,
            AiTargetRecipe commandTargetRecipe, ushort statusTargetOperand, ushort fieldId, ushort value,
            IReadOnlyList<AiInstruction> guard, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));
            var action = new List<AiInstruction>();
            AddTargetToStack(action, commandTargetRecipe);
            action.Add(Op(PUSHII, commandOperand));
            action.Add(Op(CALLPOPA, PerformCommand));
            action.AddRange(BuildChrPropertyWriteAction(statusTargetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        public static byte[] AddAbilityFromActionTemplateWithChrPropertyWriteWithGuard(AiScriptFile script, AiDetectedAction template,
            ushort commandOperand, ushort statusTargetOperand, ushort fieldId, ushort value,
            IReadOnlyList<AiInstruction> guard, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));
            List<AiInstruction> action = BuildCopiedCommandAction(script, template, commandOperand)
                ?? throw new InvalidOperationException("could not copy target/mode of the selected action.");
            action.AddRange(BuildChrPropertyWriteAction(statusTargetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>Append a command and direct status write sharing the exact same human target recipe. Computed
        /// recipes are evaluated once into int-temp0, then reused for both performCommand and writeChrProperty.</summary>
        public static byte[] AddAbilityWithLinkedChrPropertyWrite(AiScriptFile script, ushort commandOperand,
            AiTargetRecipe targetRecipe, ushort fieldId, ushort value, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            List<AiInstruction> guard = BuildAbilityGuard(random, k);
            var action = new List<AiInstruction>();
            if (targetRecipe.Kind == AiTargetRecipeKind.Literal)
            {
                action.Add(Op(PUSHII, targetRecipe.LiteralOperand));
                action.Add(Op(PUSHII, commandOperand));
                action.Add(Op(CALLPOPA, PerformCommand));
                action.AddRange(BuildChrPropertyWriteAction(targetRecipe.LiteralOperand, fieldId, value));
            }
            else
            {
                AddTargetToTemp0(action, targetRecipe);
                action.Add(Op(PUSHI0));
                action.Add(Op(PUSHII, commandOperand));
                action.Add(Op(CALLPOPA, PerformCommand));
                action.Add(Op(PUSHI0));
                action.Add(Op(PUSHII, fieldId));
                action.Add(Op(PUSHII, value));
                action.Add(Op(CALLPOPA, WriteChrProperty));
            }
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        public static byte[] AddAbilityWithLinkedChrPropertyWriteWithGuard(AiScriptFile script, ushort commandOperand,
            AiTargetRecipe targetRecipe, ushort fieldId, ushort value, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));

            var action = new List<AiInstruction>();
            if (targetRecipe.Kind == AiTargetRecipeKind.Literal)
            {
                action.Add(Op(PUSHII, targetRecipe.LiteralOperand));
                action.Add(Op(PUSHII, commandOperand));
                action.Add(Op(CALLPOPA, PerformCommand));
                action.AddRange(BuildChrPropertyWriteAction(targetRecipe.LiteralOperand, fieldId, value));
            }
            else
            {
                AddTargetToTemp0(action, targetRecipe);
                action.Add(Op(PUSHI0));
                action.Add(Op(PUSHII, commandOperand));
                action.Add(Op(CALLPOPA, PerformCommand));
                action.Add(Op(PUSHI0));
                action.Add(Op(PUSHII, fieldId));
                action.Add(Op(PUSHII, value));
                action.Add(Op(CALLPOPA, WriteChrProperty));
            }
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        public static byte[] AddAbilityFromActionTemplateWithLinkedChrPropertyWriteWithGuard(AiScriptFile script, AiDetectedAction template,
            ushort commandOperand, ushort fieldId, ushort value, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_enter_a_condition_guard_for_the_ability_f7d044ec, nameof(guard));
            List<AiInstruction> action = BuildCopiedCommandAction(script, template, commandOperand)
                ?? throw new InvalidOperationException("could not copy target/mode of the selected action.");
            AiInstruction? target = TargetPushFromCommandBody(action);
            if (target == null)
                throw new InvalidOperationException("I could not reuse the target of the selected action.");
            action.AddRange(BuildChrPropertyWriteAction(target, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>Legacy structural fallback: auto-pick the inferred combat worker + largest entrypoint. The editor
        /// uses AiWorkerMapping instead so the 1-click button hooks the real CombatHandler.onTurn entrypoint.</summary>
        public static byte[] AddAbility(AiScriptFile script, ushort commandOperand, bool random, int k,
            out int workerIndex, out int entrypointIndex)
        {
            AiWorker worker = PickCombatWorker(script)
                ?? throw new InvalidOperationException("This monster does not have a worker with entrypoint to hook the action.");
            workerIndex = worker.Index;
            entrypointIndex = PickMainEntrypoint(script, worker);
            return AddAbility(script, commandOperand, random, k, workerIndex, entrypointIndex);
        }

    }
}
