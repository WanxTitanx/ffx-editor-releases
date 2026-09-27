using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // Vanilla "Possessed by Yu Yevon!" — monmagic2 rows 231–238 (operands 0x60E7..0x60EE).
    // Corpus anchor: m166 Shiva-Yuna — ability 0x60EA, performCommand (NOT force) on Self 0xFFF3.
    public static class SinPossessedOpener
    {
        public const string OpenerRecipeId = "SIN-POSSESSED-OPENER";

        /// <summary>Row 231 / magic_0630 — default VFX variant.</summary>
        public const ushort DefaultOperand = 0x60E7;
        const byte PUSHV = 0x9F, POPV = 0xA0, PUSHII = 0xAE, EQ = 0x06;
        const ushort VanillaLatchVar = 5;

        public static ushort OperandForMonsterIndex(int monsterNumericId) =>
            (ushort)(DefaultOperand + (monsterNumericId % 8));

        public static SinChainRecipe BuildOpenerRecipe(ushort? operand = null, bool forcePerform = false) => new()
        {
            Id = OpenerRecipeId,
            DisplayName = "Possessed by Yu Yevon!",
            Tier = AiSinPresetTier.A,
            Threat = 1,
            Intent = "Battle-open perform Possessed on self (vanilla m166 pattern)",
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = "performCommand 0x700B · Self · monmagic2 Possessed",
                    Actions = new SinAction[]
                    {
                        new SinAction.PerformCommand(
                            operand ?? DefaultOperand,
                            AiSnippetLibrary.SelfRef,
                            Force: forcePerform),
                    },
                },
            },
        };

        /// <summary>Add Possessed operand to first empty ability slot if missing.</summary>
        public static bool EnsureAbility(ref byte[] monsterBin, ushort operand)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            Monster_File mon = Monster_File.Read(monsterBin);
            ushort[] abilities = mon.StatSheetFile.Abilities;
            if (abilities.Any(a => a == operand))
                return false;

            int slot = Array.FindIndex(abilities, a => a == 0);
            if (slot < 0)
                throw new InvalidOperationException("no free ability slot for Possessed opener");

            abilities[slot] = operand;
            monsterBin = mon.Write();
            return true;
        }

        public static IReadOnlyList<SinChainRecipe> RecipesWithOpener(SinChainRecipe main, ushort? operand = null)
        {
            ArgumentNullException.ThrowIfNull(main);
            if (main.Id.Equals(OpenerRecipeId, StringComparison.OrdinalIgnoreCase))
                return new[] { main };

            return new[] { BuildOpenerRecipe(operand), main };
        }

        static IReadOnlyList<AiInstruction> GuardForOpenerHook(int? entrypointOverride) =>
            entrypointOverride == 0
                ? AiAutomation.BuildAlwaysGuard()
                : BuildVanillaPossessedLatchGuard();

        static IReadOnlyList<AiInstruction> BuildVanillaPossessedLatchGuard() => new[]
        {
            Op(PUSHV, VanillaLatchVar),
            Op(PUSHII, 0),
            Op(EQ),
        };

        static IReadOnlyList<AiInstruction> BuildVanillaPossessedLatchSet() => new[]
        {
            Op(PUSHII, 0x00FF),
            Op(POPV, VanillaLatchVar),
        };

        public static SinMonsterEmitResult TryEmitOpenerOnly(
            byte[] monsterBin,
            ushort? operand = null,
            bool forcePerform = false,
            int? entrypointOverride = null)
        {
            ushort cmd = operand ?? DefaultOperand;
            try { EnsureAbility(ref monsterBin, cmd); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(OpenerRecipeId, ex.Message); }

            return SinSandboxApplySession.TryEmitInMemory(
                monsterBin,
                BuildOpenerRecipe(cmd, forcePerform),
                modAuthoringBake: true,
                stopAfterAction: true,
                guardOverride: GuardForOpenerHook(entrypointOverride),
                actionPrefixOverride: entrypointOverride is 0 ? null : BuildVanillaPossessedLatchSet(),
                entrypointOverride: entrypointOverride);
        }

        static AiInstruction Op(byte opcode, ushort operand = 0) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        public static SinMonsterEmitResult TryEmitWithPossessedOpener(
            byte[] monsterBin,
            SinChainRecipe mainRecipe,
            ushort? operand = null,
            bool forcePerform = false,
            int? entrypointOverride = null)
        {
            ushort cmd = operand ?? DefaultOperand;
            try { EnsureAbility(ref monsterBin, cmd); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(mainRecipe.Id, ex.Message); }

            SinMonsterEmitResult last = default!;
            byte[] current = monsterBin;
            // AppendGuardedAction runs newest block first — main first, opener last (Possessed runs before Ward).
            last = SinSandboxApplySession.TryEmitInMemory(current, mainRecipe, modAuthoringBake: true);
            if (!last.Ok)
                return SinMonsterEmitResult.Fail(mainRecipe.Id, $"emit failed at '{mainRecipe.Id}': {last.Error}");
            current = last.EditedMonster!;

            // Battle-init hook (entry 0) + RET; onTurn stays vanilla for the main preset body.
            last = TryEmitOpenerOnly(current, cmd, forcePerform, entrypointOverride ?? 0);
            if (!last.Ok)
                return SinMonsterEmitResult.Fail(mainRecipe.Id,
                    $"emit failed at '{OpenerRecipeId}': {last.Error}");

            current = last.EditedMonster!;

            return new SinMonsterEmitResult
            {
                Ok = true,
                RecipeId = mainRecipe.Id,
                EditedMonster = current,
                AddedRows = last.AddedRows,
                WorkerIndex = last.WorkerIndex,
                EntrypointIndex = last.EntrypointIndex,
                WorkerResolution = last.WorkerResolution,
                Notes = new[] { "possessed opener chained before main recipe" },
            };
        }
    }
}
