using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiPhaseTriggerKind
    {
        OnTurn = 0,
        BattleStart = 1,
        OnHit = 2,
        AfterAnyValidTurn = 3, // disabled/legacy experimental runtime hook
    }

    /// <summary>
    /// Writer v1 for human phase rotations.
    /// It does not emit SWITCH/labels yet. Instead, it lowers each phase to the proven
    /// AppendGuardedAction shape: guard -> action -> optional RET, chaining the old handler when
    /// a guard fails. This is intentionally conservative and RT2-required.
    /// </summary>
    public sealed record AiPhaseRotationRecipe(
        ushort CounterVariableIndex,
        IReadOnlyList<AiPhaseRotationStep> Steps,
        int FinalLimit,
        int WorkerIndex,
        int EntrypointIndex,
        AiTargetRecipe DefaultTargetRecipe,
        string? MonsterId = null);

    public sealed record AiPhaseRotationStep(
        int Number,
        string Label,
        ushort CommandOperand,
        bool IsFinalPhase,
        bool StopHere,
        IReadOnlyList<AiPhaseVarMutation>? VarMutations = null,
        AiTargetRecipe? TargetRecipe = null,
        bool RandomChance = false,
        int RandomK = 2,
        IReadOnlyList<AiInstruction>? ExtraGuard = null,
        IReadOnlyList<AiInstruction>? AdditionalGuard = null,
        AiPhaseForbiddenRiteEffect? ForbiddenRite = null,
        AiPhaseTriggerKind TriggerKind = AiPhaseTriggerKind.OnTurn,
        int? WorkerIndex = null,
        int? EntrypointIndex = null,
        string? HookLabel = null);

    public sealed record AiPhaseVarMutation(ushort VariableIndex, int AddMin, int AddMax, bool SetInsteadOfAdd = false);

    public sealed record AiPhaseForbiddenRiteEffect(
        bool UsePhaseTarget,
        ushort TargetOperand,
        ushort FieldId,
        ushort Value);

    public sealed record AiPhaseRotationApplyResult(
        byte[] AiFile,
        int AppliedSteps,
        int FinalSteps,
        int CommonSteps,
        string Summary);

    public static class AiPhaseRotationWriter
    {
        const byte LAnd = 0x02;
        const byte Eq = 0x06;
        const byte Gt = 0x0A;
        const byte Add = 0x14;
        const byte Mod = 0x18;
        const byte PopI0 = 0x59;
        const byte PushI0 = 0x67;
        const byte PushV = 0x9F;
        const byte PopV = 0xA0;
        const byte PushII = 0xAE;
        const byte Call = 0xB5;
        const byte CallPopA = 0xD8;
        // 2026-08-01: no direct "greater-than" opcode — with stack [A(base), B(topo)] the interpreter
        // tests 0x0B = A < B and 0x0F = A <= B. Preserve greater-than via swapped order + LT.
        // Evidence: FFX_EVENTVM_OPS_2026-09-17 section 7, interpreter @0x864180.
        const byte Lt = 0x0B;

        const ushort GetRandomValue = 0x00A9;
        const ushort PerformCommand = 0x700B;
        const ushort ForcePerformCommand = 0x705A;
        const ushort FindMatchingChr = 0x7010;
        const ushort WriteChrProperty = 0x7018;
        const ushort FrontlineChars = 0xFFF2;
        const ushort MatchingGroup = 0xFFF0;
        const ushort ChrFieldHp = AiAutomation.CurrentHpField;
        const ushort ChrFieldIsAlive = 0x0004;
        const ushort SelectorAny = 0x0000;
        const ushort SelectorLowest = 0x0002;

        public static AiPhaseRotationApplyResult Apply(AiScriptFile source, AiPhaseRotationRecipe recipe)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(recipe);
            if (!source.HasScript)
                throw new InvalidOperationException("AiFile without a script.");
            if (recipe.WorkerIndex < 0 || recipe.WorkerIndex >= source.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(recipe.WorkerIndex));
            if (recipe.EntrypointIndex < 0 || recipe.EntrypointIndex >= source.Workers[recipe.WorkerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(recipe.EntrypointIndex));
            if (source.Variables.Count == 0 || recipe.CounterVariableIndex >= source.Variables.Count)
                throw new InvalidOperationException($"var[{recipe.CounterVariableIndex}] does not exist in this AiFile table.");
            if (recipe.Steps.Count == 0)
                throw new InvalidOperationException("Recipe without phases.");
            if (recipe.FinalLimit < 0 || recipe.FinalLimit > ushort.MaxValue)
                throw new InvalidOperationException("Threshold of the last phase must be between 0 and 65535.");
            foreach (AiPhaseRotationStep step in recipe.Steps)
            {
                if (step.RandomChance && (step.RandomK < 2 || step.RandomK > ushort.MaxValue))
                    throw new InvalidOperationException($"{step.Label}: chance must be 1 in K, with K between 2 and 65535.");
                int stepWorkerIndex = step.WorkerIndex ?? recipe.WorkerIndex;
                int stepEntrypointIndex = step.EntrypointIndex ?? recipe.EntrypointIndex;
                if (stepWorkerIndex < 0 || stepWorkerIndex >= source.Workers.Count)
                    throw new InvalidOperationException($"{step.Label}: worker {stepWorkerIndex} invalid for trigger {step.TriggerKind}.");
                if (stepEntrypointIndex < 0 || stepEntrypointIndex >= source.Workers[stepWorkerIndex].Entrypoints.Count)
                    throw new InvalidOperationException($"{step.Label}: entrypoint {stepEntrypointIndex} invalid for worker {stepWorkerIndex}.");
                foreach (AiPhaseVarMutation mutation in step.VarMutations ?? Array.Empty<AiPhaseVarMutation>())
                {
                    if (mutation.VariableIndex >= source.Variables.Count)
                        throw new InvalidOperationException($"{step.Label}: var[{mutation.VariableIndex}] does not exist in this AiFile table.");
                    if (mutation.SetInsteadOfAdd)
                    {
                        if (mutation.AddMin < 0 || mutation.AddMin > ushort.MaxValue)
                            throw new InvalidOperationException($"{step.Label}: var reset/set must be between 0 and 65535.");
                    }
                    else if (mutation.AddMin < 0 || mutation.AddMax < mutation.AddMin || mutation.AddMax > ushort.MaxValue)
                    {
                        throw new InvalidOperationException($"{step.Label}: var advance must be between 0 and 65535, with min <= max.");
                    }
                }
            }

            if (recipe.Steps.Any(step => step.TriggerKind == AiPhaseTriggerKind.BattleStart))
            {
                throw new InvalidOperationException(
                    "The 'battle start' trigger of the Phase Manager was disabled on this AiFile route. " +
                    "The desired behavior ('after any valid turn passed') belongs to the CTB/runtime edge " +
                    "and not to a safe entrypoint already proven inside the CombatHandler.");
            }
            if (recipe.Steps.Any(step => step.TriggerKind == AiPhaseTriggerKind.AfterAnyValidTurn))
            {
                throw new InvalidOperationException(
                    "The experimental runtime hook trigger was removed from this surface. " +
                    "Use 'On its turn' or 'On taking a hit'.");
            }

            List<AiPhaseRotationStep> common = recipe.Steps.Where(s => !s.IsFinalPhase).ToList();
            List<AiPhaseRotationStep> final = recipe.Steps.Where(s => s.IsFinalPhase).ToList();
            if (final.Count > 1)
                throw new InvalidOperationException("The v1 writer accepts at most one last phase per recipe.");

            // Common/onTurn/onHit phases still use AppendGuardedAction, which makes the newest block run first.
            // Keep the reverse apply order so runtime/visual order stays aligned with the phase cards.
            IEnumerable<AiPhaseRotationStep> appendOrder = common.AsEnumerable().Reverse().Concat(final);
            byte[] working = source.OriginalAiFileBytes.ToArray();
            int applied = 0;
            int baselineUnknownPasses = 0;

            foreach (AiPhaseRotationStep step in appendOrder)
            {
                AiScriptFile current = AiScript_File.Read(working);
                IReadOnlyList<AiInstruction> guard = BuildGuard(recipe, step);
                List<AiInstruction> action = BuildAction(recipe, step);
                int stepWorkerIndex = step.WorkerIndex ?? recipe.WorkerIndex;
                int stepEntrypointIndex = step.EntrypointIndex ?? recipe.EntrypointIndex;
                working = AiScript_File.AppendGuardedAction(
                    current,
                    stepWorkerIndex,
                    stepEntrypointIndex,
                    guard,
                    action,
                    step.StopHere);

                if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                        working,
                        current,
                        current.OriginalAiFileBytes.Length,
                        out _,
                        out string validationReason))
                {
                    throw new InvalidOperationException("Validator refused phase " + step.Label + ": " + validationReason);
                }

                if (!string.IsNullOrWhiteSpace(validationReason))
                    baselineUnknownPasses++;
                applied++;
            }

            int mutationCount = recipe.Steps.Sum(s => s.VarMutations?.Count ?? 0);
            int chanceCount = recipe.Steps.Count(s => s.RandomChance);
            string summary =
                $"PhaseRotationRecipe v1 applied {applied} phase(s): {common.Count} common, {final.Count} last; " +
                $"{mutationCount} var advance(s) per phase, {chanceCount} local chance(s), final fallback if > {recipe.FinalLimit}." +
                (baselineUnknownPasses > 0
                    ? $" Baseline unknowns preserved in {baselineUnknownPasses} phase(s)."
                    : string.Empty);

            return new AiPhaseRotationApplyResult(working, applied, final.Count, common.Count, summary);
        }

        static IReadOnlyList<AiInstruction> BuildGuard(AiPhaseRotationRecipe recipe, AiPhaseRotationStep step)
        {
            List<AiInstruction> guard =
                step.ExtraGuard != null && step.ExtraGuard.Count > 0
                    ? step.ExtraGuard.Select(Clone).ToList()
                    : step.IsFinalPhase
                        ? new List<AiInstruction>
                        {
                            Op(PushII, (ushort)recipe.FinalLimit),
                            Op(PushV, recipe.CounterVariableIndex),
                            Op(Lt),
                        }
                        : new List<AiInstruction>
                        {
                            Op(PushV, recipe.CounterVariableIndex),
                            Op(PushII, (ushort)Math.Max(0, step.Number - 1)),
                            Op(Eq),
                        };

            if (step.AdditionalGuard != null && step.AdditionalGuard.Count > 0)
            {
                guard.AddRange(step.AdditionalGuard.Select(Clone));
                guard.Add(Op(LAnd));
            }
            if (step.RandomChance)
            {
                guard.AddRange(BuildRandomGuard(step.RandomK));
                guard.Add(Op(LAnd));
            }
            return guard;
        }

        static List<AiInstruction> BuildAction(AiPhaseRotationRecipe recipe, AiPhaseRotationStep step)
        {
            var action = new List<AiInstruction>();
            AiTargetRecipe targetRecipe = step.TargetRecipe ?? recipe.DefaultTargetRecipe;
            AiPhaseForbiddenRiteEffect? rite = step.ForbiddenRite;

            if (rite is { UsePhaseTarget: true } && targetRecipe.Kind != AiTargetRecipeKind.Literal)
            {
                AddTargetToStack(action, targetRecipe);
                action.Add(Op(PopI0));
                action.Add(Op(PushI0));
                action.Add(Op(PushII, step.CommandOperand));
                action.Add(Op(CallPopA, CommandCallFor(step)));
                action.Add(Op(PushI0));
                action.Add(Op(PushII, rite.FieldId));
                action.Add(Op(PushII, rite.Value));
                action.Add(Op(CallPopA, WriteChrProperty));
            }
            else
            {
                AddTargetToStack(action, targetRecipe);
                action.Add(Op(PushII, step.CommandOperand));
                action.Add(Op(CallPopA, CommandCallFor(step)));
                if (rite != null)
                {
                    ushort target = rite.UsePhaseTarget && targetRecipe.Kind == AiTargetRecipeKind.Literal
                        ? targetRecipe.LiteralOperand
                        : rite.TargetOperand;
                    action.AddRange(BuildChrPropertyWrite(target, rite.FieldId, rite.Value));
                }
            }

            foreach (AiPhaseVarMutation mutation in step.VarMutations ?? Array.Empty<AiPhaseVarMutation>())
                action.AddRange(mutation.SetInsteadOfAdd
                    ? BuildSetCounter(mutation.VariableIndex, mutation.AddMin)
                    : BuildAdvanceCounter(mutation.VariableIndex, mutation.AddMin, mutation.AddMax));

            return action;
        }

        static ushort CommandCallFor(AiPhaseRotationStep step) => ForcePerformCommand;

        static IEnumerable<AiInstruction> BuildChrPropertyWrite(ushort targetOperand, ushort fieldId, ushort value)
        {
            yield return Op(PushII, targetOperand);
            yield return Op(PushII, fieldId);
            yield return Op(PushII, value);
            yield return Op(CallPopA, WriteChrProperty);
        }

        static IEnumerable<AiInstruction> BuildSetCounter(ushort counterIndex, int value)
        {
            yield return Op(PushII, unchecked((ushort)value));
            yield return Op(PopV, counterIndex);
        }

        static IEnumerable<AiInstruction> BuildAdvanceCounter(ushort counterIndex, int min, int max)
        {
            yield return Op(PushV, counterIndex);
            if (max > min)
            {
                int range = max - min + 1;
                yield return Op(Call, GetRandomValue);
                yield return Op(PushII, (ushort)range);
                yield return Op(Mod);
                if (min != 0)
                {
                    yield return Op(PushII, (ushort)min);
                    yield return Op(Add);
                }
                yield return Op(Add);
            }
            else if (min != 0)
            {
                yield return Op(PushII, (ushort)min);
                yield return Op(Add);
            }
            yield return Op(PopV, counterIndex);
        }

        static IEnumerable<AiInstruction> BuildRandomGuard(int k)
        {
            yield return Op(Call, GetRandomValue);
            yield return Op(PushII, (ushort)Math.Clamp(k, 2, ushort.MaxValue));
            yield return Op(Mod);
            yield return Op(PushII, 0);
            yield return Op(Eq);
        }

        static void AddTargetToStack(List<AiInstruction> action, AiTargetRecipe recipe)
        {
            switch (recipe.Kind)
            {
                case AiTargetRecipeKind.Literal:
                    action.Add(Op(PushII, recipe.LiteralOperand));
                    return;
                case AiTargetRecipeKind.FindAliveFrontlineAny:
                    action.Add(Op(PushII, FrontlineChars));
                    action.Add(Op(PushII, ChrFieldIsAlive));
                    action.Add(Op(PushII, 0));
                    action.Add(Op(PushII, SelectorAny));
                    action.Add(Op(Call, FindMatchingChr));
                    return;
                case AiTargetRecipeKind.FindAliveFrontlineLowestHp:
                    action.Add(Op(PushII, FrontlineChars));
                    action.Add(Op(PushII, ChrFieldIsAlive));
                    action.Add(Op(PushII, 0));
                    action.Add(Op(PushII, SelectorAny));
                    action.Add(Op(CallPopA, FindMatchingChr));
                    action.Add(Op(PushII, MatchingGroup));
                    action.Add(Op(PushII, ChrFieldHp));
                    action.Add(Op(PushII, 0));
                    action.Add(Op(PushII, SelectorLowest));
                    action.Add(Op(Call, FindMatchingChr));
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(recipe), $"Unknown target recipe: {recipe.Kind}");
            }
        }

        static AiInstruction Clone(AiInstruction instruction) => new()
        {
            Offset = -1,
            Opcode = instruction.Opcode,
            HasOperand = instruction.HasOperand,
            Operand = instruction.Operand,
            OperandKind = instruction.OperandKind,
        };

        static AiInstruction Op(byte opcode, ushort operand = 0) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }
}
