using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public class MonsterHpContractTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void PercentageGuards_UseNumericHp_AndHonorBoundaryWithoutIntOverflow(int percent)
    {
        foreach (int maximum in new[] { 1, 99, 100, 101, 12000000, int.MaxValue })
        {
            long scaled = (long)maximum * percent;
            int threshold = (int)((scaled + 99) / 100);
            foreach (int value in new[] { 0, Math.Max(0, threshold - 1), threshold, maximum }.Distinct())
            {
                Assert.Equal((long)value * 100 < scaled,
                    Evaluate(AiAutomation.BuildHpBelowPercentGuard((ushort)percent), value, maximum, 0));
                Assert.Equal((long)value * 100 >= scaled,
                    Evaluate(AiAutomation.BuildLastDamageTakenAtLeastPercentMaxHpGuard((ushort)percent), 0, maximum, value));
            }
        }
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(25, 75)]
    [InlineData(50, 100)]
    public void HpRange_IsLowerInclusiveAndUpperExclusive(int lower, int upper)
    {
        foreach (int maximum in new[] { 1, 101, 12000000, int.MaxValue })
        {
            int lo = (int)(((long)maximum * lower + 99) / 100);
            int hi = (int)(((long)maximum * upper + 99) / 100);
            foreach (int hp in new[] { 0, Math.Max(0, lo - 1), lo, Math.Max(0, hi - 1), hi, maximum }.Distinct())
                Assert.Equal((long)hp * 100 >= (long)maximum * lower && (long)hp * 100 < (long)maximum * upper,
                    Evaluate(AiAutomation.BuildHpBetweenPercentGuard((ushort)lower, (ushort)upper), hp, maximum, 0));
        }
    }

    // A small integer stack oracle exercises emitted instructions; its expected
    // relation uses independent 64-bit cross-products, not the builder formula.
    static bool Evaluate(IReadOnlyList<AiInstruction> instructions, int hp, int maximum, int damage)
    {
        var stack = new Stack<int>();
        foreach (var instruction in instructions)
        {
            if (instruction.Opcode == 0xAE) { stack.Push(unchecked((short)instruction.Operand)); continue; }
            if (instruction.Opcode == 0xB5)
            {
                Assert.Equal(0x700F, instruction.Operand);
                int field = stack.Pop();
                Assert.Equal(unchecked((short)0xFFF3), stack.Pop());
                stack.Push(field switch
                {
                    0 => hp, 2 => maximum, 0xA6 => damage,
                    _ => throw new InvalidOperationException($"Unexpected numeric HP field: 0x{field:X4}"),
                });
                continue;
            }
            int right = stack.Pop(), left = stack.Pop();
            stack.Push(instruction.Opcode switch
            {
                0x02 => left != 0 && right != 0 ? 1 : 0,
                0x0B => left < right ? 1 : 0,
                0x0F => left <= right ? 1 : 0,
                0x14 => checked(left + right),
                0x16 => checked(left * right),
                0x17 => left / right,
                0x18 => left % right,
                _ => throw new InvalidOperationException($"Unexpected guard opcode: {instruction.Opcode:X2}"),
            });
        }
        return Assert.Single(stack) != 0;
    }

    static AiInstruction Op(byte code, ushort value = 0) => new()
    { Offset = -1, Opcode = code, Operand = value, HasOperand = AiScript_File.IsOperandBearing(code), OperandKind = AiScript_File.OperandKindOf(code) };

    static AiScriptFile WithTargetStore()
    {
        var monster = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
        var source = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);
        var worker = AiAutomation.PickCombatWorker(source)!;
        return AiScript_File.Read(AiScript_File.AppendGuardedAction(source, worker.Index,
            AiAutomation.PickMainEntrypoint(source, worker), new[] { Op(0xAE, 1) }, new[] { Op(0xAE, 0xFFF2), Op(0xA0, 0) }));
    }

    [Fact]
    public void LowestHpTarget_UsesNumericHpAndRealCallPopA_AndRoundTrips()
    {
        var source = WithTargetStore();
        Assert.True(AiTargetRecipeWriter.TryBuildDescriptors(source, out var rows, out var why), why);
        var slot = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.Literal);
        var request = new AiTargetRecipeSwapRequest(slot.RoleKey, slot.AnchorInstructionOffset, AiTargetRecipeKind.FindAliveFrontlineLowestHp, 0);
        Assert.True(AiTargetRecipeWriter.TryApplyRecipeSwap(source, request, out var result, out why), why);
        Assert.Equal((byte)0xD8, result!.NewSequence[4].Opcode);
        Assert.Equal((ushort)0, result.NewSequence[6].Operand);
        Assert.Equal(0x7010, result.NewSequence[4].Operand);
        var edited = AiScript_File.Read(result.EditedAiFileBytes);
        Assert.True(AiTargetRecipeWriter.TryBuildDescriptors(edited, out rows, out why), why);
        var next = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.FindAliveFrontlineLowestHp);
        int store = edited.Instructions.ToList().FindIndex(i => i.Offset == next.AnchorInstructionOffset);
        Assert.Equal(edited.Instructions[store - 10].Offset - edited.ScriptStart, next.SequenceStartCodeRelative);
        Assert.True(AiTargetRecipeWriter.TryApplyRecipeSwap(edited,
            new(next.RoleKey, next.AnchorInstructionOffset, AiTargetRecipeKind.Literal, slot.CurrentLiteralOperand), out var restored, out why), why);
        Assert.Equal(source.OriginalAiFileBytes, restored!.EditedAiFileBytes);
    }

    [Fact]
    public void FrontlineDescriptor_IncludesFirstActorPush()
    {
        var source = WithTargetStore();
        AiTargetRecipeWriter.TryBuildDescriptors(source, out var rows, out _);
        var slot = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.Literal);
        Assert.True(AiTargetRecipeWriter.TryApplyRecipeSwap(source,
            new(slot.RoleKey, slot.AnchorInstructionOffset, AiTargetRecipeKind.FindAliveFrontlineAny, 0), out var result, out var why), why);
        var edited = AiScript_File.Read(result!.EditedAiFileBytes);
        AiTargetRecipeWriter.TryBuildDescriptors(edited, out rows, out _);
        var next = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.FindAliveFrontlineAny);
        int store = edited.Instructions.ToList().FindIndex(i => i.Offset == next.AnchorInstructionOffset);
        Assert.Equal(edited.Instructions[store - 5].Offset - edited.ScriptStart, next.SequenceStartCodeRelative);
    }

    [Fact]
    public void LegacyLowestHp_RequiresRepair_AndDoesNotSilentlyKeepTheOldPredicate()
    {
        var source = WithTargetStore();
        AiTargetRecipeWriter.TryBuildDescriptors(source, out var rows, out _);
        var slot = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.Literal);
        Assert.True(AiTargetRecipeWriter.TryApplyRecipeSwap(source,
            new(slot.RoleKey, slot.AnchorInstructionOffset, AiTargetRecipeKind.FindAliveFrontlineLowestHp, 0), out var result, out var why), why);
        var legacy = AiScript_File.Read(result!.EditedAiFileBytes);
        AiTargetRecipeWriter.TryBuildDescriptors(legacy, out rows, out _);
        var target = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.FindAliveFrontlineLowestHp);
        int store = legacy.Instructions.ToList().FindIndex(i => i.Offset == target.AnchorInstructionOffset);
        legacy.Instructions[store - 6].Opcode = 0xAF;
        legacy.Instructions[store - 4].Operand = 0x011B;
        legacy = AiScript_File.Read(AiScript_File.Write(legacy));
        AiTargetRecipeWriter.TryBuildDescriptors(legacy, out rows, out _);
        target = rows.Last(r => r.CurrentRecipe == AiTargetRecipeKind.FindAliveFrontlineLowestHp);
        Assert.True(target.RequiresLegacyRepair);
        Assert.True(AiTargetRecipeWriter.TryApplyRecipeSwap(legacy,
            new(target.RoleKey, target.AnchorInstructionOffset, target.CurrentRecipe, 0), out var repaired, out why), why);
        Assert.Equal(result.EditedAiFileBytes, repaired!.EditedAiFileBytes);
    }

    [Fact]
    public void SinPreview_RespectsConfiguredPercent_WithoutChangingRuntimePermission()
    {
        var recipe = new SinChainRecipe
        {
            Id = "SIN-010", DisplayName = "HP contract", Tier = AiSinPresetTier.Lab, Threat = 1,
            Nodes = new[] { new SinChainNode
            {
                Trigger = new SinTrigger(SinEvent.OnTurn), Condition = new SinCondition.HpBelowPercent(25),
                Actions = new SinAction[] { new SinAction.GrantChrProperty(0xFFF3, 0x35, 1) },
            } },
        };
        var plan = SinDryRunPlanner.Plan(recipe);
        Assert.False(plan.ApplyAllowed);
        var guard = Assert.Single(plan.Steps).GuardOps;
        Assert.True(Evaluate(guard, 24, 100, 0));
        Assert.False(Evaluate(guard, 25, 100, 0));
        Assert.False(SinRt2PilotGate.Evaluate(recipe, allowRt2InGamePilot: true).Permitted);
    }

    [Fact]
    public void PhaseReader_PreservesTheWholeCounterAndNumericHpGuard()
    {
        var instructions = new List<AiInstruction> { Op(0x3C) };
        instructions.AddRange(AiVarConditionBuilder.BuildImmediateComparison(0, AiVarCompareOperator.Equal, 0));
        instructions.AddRange(AiAutomation.BuildHpBelowPercentGuard(25));
        instructions.Add(Op(0x02));
        int branch = instructions.Count;
        instructions.Add(Op(0xD7)); instructions.Add(Op(0xAE, 0xFFF3));
        int command = instructions.Count;
        instructions.Add(Op(0xAE, 0x3049)); instructions.Add(Op(0xD8, 0x700B));
        var reader = typeof(MonsterAiEditor_DataModel).GetMethod("TryFindPhaseGuardBeforeCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] arguments = { instructions, command, null, null };
        Assert.True((bool)reader.Invoke(null, arguments)!);
        Assert.Equal(1, arguments[2]);
        Assert.Equal(branch, arguments[3]);
    }
}
