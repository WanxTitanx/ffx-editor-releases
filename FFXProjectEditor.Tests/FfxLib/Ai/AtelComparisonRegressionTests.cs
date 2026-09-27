using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.AtelScript;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public class AtelComparisonRegressionTests
{
    [Theory]
    [InlineData("==", AiVarCompareOperator.Equal)]
    [InlineData("!=", AiVarCompareOperator.NotEqual)]
    [InlineData("<", AiVarCompareOperator.LessThan)]
    [InlineData(">", AiVarCompareOperator.GreaterThan)]
    [InlineData("<=", AiVarCompareOperator.LessOrEqual)]
    [InlineData(">=", AiVarCompareOperator.GreaterOrEqual)]
    public void Emitters_ComputeRequestedRelationAtSignedBoundaries(string symbol, AiVarCompareOperator comparison)
    {
        foreach (int threshold in new[] { short.MinValue, -5, 0, 5, short.MaxValue })
        {
            var expression = new AtelBinary(symbol, new AtelVarRef("x"), new AtelLiteral(threshold));
            var emitted = AtelEmitter.EmitComparison(expression, _ => 0);
            var built = AiVarConditionBuilder.BuildImmediateComparison(0, comparison, threshold);
            foreach (int value in new[] { int.MinValue, threshold - 1, threshold, threshold + 1, int.MaxValue })
            {
                bool expected = RequestedRelation(symbol, value, threshold);
                Assert.Equal(expected, Evaluate(emitted, value) != 0);
                Assert.Equal(expected, Evaluate(built, value) != 0);
            }
        }
    }

    public static IEnumerable<object[]> Comparisons()
    {
        yield return new object[] { (byte)0x06, "EQ", "==", "==" };
        yield return new object[] { (byte)0x07, "NE", "!=", "!=" };
        yield return new object[] { (byte)0x08, "GTU", "> (u32)", "< (u32)" };
        yield return new object[] { (byte)0x09, "LSU", "< (u32)", "> (u32)" };
        yield return new object[] { (byte)0x0A, "GT", ">", "<" };
        yield return new object[] { (byte)0x0B, "LS", "<", ">" };
        yield return new object[] { (byte)0x0C, "GTEU", ">= (u32)", "<= (u32)" };
        yield return new object[] { (byte)0x0D, "LSEU", "<= (u32)", ">= (u32)" };
        yield return new object[] { (byte)0x0E, "GTE", ">=", "<=" };
        yield return new object[] { (byte)0x0F, "LSE", "<=", ">=" };
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void ComparisonFamily_IsKnownAndUsesNativeMnemonics(byte opcode, string mnemonic, string direct, string swapped)
    {
        Assert.True(AiScript_File.IsKnownOpcode(opcode));
        Assert.Equal(-1, AiStackModel.NetNonCall(opcode));
        Assert.Equal(mnemonic, AiScript_File.Mnemonic(opcode));
        Assert.Equal(mnemonic, EventAtelDisassembler.Mnemonic(opcode));
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void ActionDetection_DescribesActualRelationInBothOperandOrders(byte opcode, string mnemonic, string direct, string swapped)
    {
        byte[] monster = Monster();
        AiScriptFile original = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);
        AiWorker worker = AiAutomation.PickCombatWorker(original)!;
        int entry = AiAutomation.PickMainEntrypoint(original, worker);
        foreach (bool reverse in new[] { false, true })
        {
            var guard = ImmediateGuard(opcode, reverse, -5);
            byte[] output = AiAutomation.AddQueuedAbilityWithGuard(original, 0x3049, guard, worker.Index, entry);
            AiScriptFile reread = AiScript_File.Read(output);
            Assert.True(reread.CodeWalkClosedExactly);
            Assert.Empty(reread.UnknownOpcodes);
            Assert.Equal(output, AiScript_File.Write(reread));
            Assert.All(AiSemanticScript.Build(reread).Nodes.Where(n => n.Opcode == opcode),
                n => Assert.NotNull(n.Meaning));
            var actions = AiAutomation.DetectBranchSensitiveActions(monster, reread);
            Assert.Contains(actions, a => a.GuardSummary.EndsWith($"{(reverse ? swapped : direct)} -5", StringComparison.Ordinal));
        }
        Assert.Equal(monster, Monster());
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void PhaseConditionEditor_ReopensNativeComparisonsInBothOrders(byte opcode, string mnemonic, string direct, string swapped)
    {
        // This reader only needs the variable audit. Avoid constructor subscriptions
        // and the user's active project while testing the actual DataModel method.
        var model = (MonsterAiEditor_DataModel)RuntimeHelpers.GetUninitializedObject(typeof(MonsterAiEditor_DataModel));
        var row = new AiPhaseVariableAuditRow("var0", "var0", "counter", "fixture", 0, 0, 0, 1, 0,
            "0", "local", "", 0, "", "", "", "", "", "");
        typeof(MonsterAiEditor_DataModel).GetField("<PhaseVariableAudits>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new ObservableCollection<AiPhaseVariableAuditRow> { row });
        var method = typeof(MonsterAiEditor_DataModel).GetMethod("TryDecodePhaseConditionClauses", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (bool reverse in new[] { false, true })
        {
            object?[] args = { ImmediateGuard(opcode, reverse, -5), null };
            Assert.True((bool)method.Invoke(model, args)!);
            var clauses = Assert.IsAssignableFrom<IReadOnlyList<PhaseConditionClauseSnapshot>>(args[1]);
            var clause = Assert.Single(clauses);
            Assert.Equal("var0", clause.VariableKey);
            Assert.Equal(reverse ? swapped : direct, clause.Operator);
            Assert.Equal("-5", clause.Value);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void ImmediateReader_RejectsOutOfBoundsStarts(int start)
    {
        Assert.False(AiVarConditionBuilder.TryReadImmediateComparison(ImmediateGuard(0x0A, false, 5), start, out _));
    }

    [Fact]
    public void ImmediateReader_RejectsUnknownOpcodesAndNonLiteralShapes()
    {
        Assert.False(AiVarConditionBuilder.TryReadImmediateComparison(ImmediateGuard(0x14, false, 5), 0, out _));
        Assert.False(AiVarConditionBuilder.TryReadImmediateComparison(new[] { Op(0x9F), Op(0x9F), Op(0x0A) }, 0, out _));
        Assert.False(AiVarConditionBuilder.TryReadImmediateComparison(new[] { Op(0xAE), Op(0xAE), Op(0x0A) }, 0, out _));
        Assert.False(AiVarConditionBuilder.TryReadImmediateComparison(Array.Empty<AiInstruction>(), 0, out _));
    }

    [Theory]
    [InlineData("> (u32)")]
    [InlineData("< (u32)")]
    [InlineData(">= (u32)")]
    [InlineData("<= (u32)")]
    public void AdvancedPhasePreview_PreservesUnsignedOperator(string symbol)
    {
        object[] args = { $"var0 {symbol} -1", "var0", "", "" };
        var method = typeof(MonsterAiEditor_DataModel).GetMethod("TryParseSimpleGuardPreview", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True((bool)method.Invoke(null, args)!);
        Assert.Equal(symbol, args[2]);
        Assert.Equal("-1", args[3]);
    }

    [Theory]
    [InlineData(0x16, false)]
    [InlineData(0x17, true)]
    public void SinArithmeticGuard_RejectsDivisionAndAcceptsNativeMultiply(int opcode, bool blocked)
    {
        var step = new SinPlannedStep
        {
            TriggerLabel = "fixture", WorkerResolution = "fixture", Lowering = AiSnippetKind.GuardedAction,
            SnippetId = "fixture", BranchShape = "fixture", StackShape = "fixture",
            PlannedInstructions = Array.Empty<string>(), Evidence = Array.Empty<SinEvidenceBadge>(),
            StepBlockers = Array.Empty<string>(), PayloadEligibility = SinPayloadEligibility.Candidate,
            GuardOps = new[] { Op(0xAE, 2), Op(0xAE, 3), Op((byte)opcode) },
        };
        var checks = new List<SinPlanCheck>();
        typeof(SinPlanValidator).GetMethod("CheckDivMulIntegrity", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { step, "fixture", checks });
        Assert.Equal(blocked, checks.Any(c => c.Severity == SinPlanCheckSeverity.Blocker));
        Assert.Contains(checks, c => c.Code == "divmul-integrity");
    }

    [Theory]
    [InlineData(0x0A, false, true)]
    [InlineData(0x08, false, false)]
    [InlineData(0x0B, true, true)]
    [InlineData(0x09, true, false)]
    [InlineData(0x0B, false, false)]
    [InlineData(0x0F, true, false)]
    [InlineData(0x0A, true, false)]
    public void PhaseReader_RecognizesOnlyStrictFinalGuard(int opcode, bool reverse, bool expectedFinal)
    {
        var method = typeof(MonsterAiEditor_DataModel).GetMethod("TryParsePhaseGuard", BindingFlags.Static | BindingFlags.NonPublic)!;
        object[] args = { ImmediateGuard((byte)opcode, reverse, 4), (ushort)0, false, 0, 0, false, 0, "" };
        Assert.True((bool)method.Invoke(null, args)!);
        Assert.Equal(expectedFinal, args[2]);
        if (expectedFinal)
        {
            Assert.Equal(int.MaxValue, args[3]);
            Assert.Equal(4, args[4]);
        }
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x13)]
    public void ProvenBinaryOperators_AreRecognized(int opcode)
    {
        Assert.True(AiScript_File.IsKnownOpcode((byte)opcode));
        Assert.Equal(-1, AiStackModel.NetNonCall((byte)opcode));
    }

    [Theory]
    [InlineData(0x08)]
    [InlineData(0x09)]
    [InlineData(0x0C)]
    [InlineData(0x0D)]
    public void ReauthoringUnsignedConditions_PreservesNativeResultsAcrossTheSignBit(int opcode)
    {
        foreach (bool reverse in new[] { false, true })
        foreach (short threshold in new short[] { -1, 0, 5, short.MinValue, short.MaxValue })
        {
            var native = ImmediateGuard((byte)opcode, reverse, threshold);
            Assert.True(AiVarConditionBuilder.TryReadImmediateComparison(native, 0, out var clause));
            Assert.True(AiVarConditionBuilder.TryParseOperator(AiVarConditionBuilder.OperatorLabel(clause.Operator), out var parsed));
            var reauthored = AiVarConditionBuilder.BuildImmediateComparison(clause.VariableIndex, parsed, clause.Value);
            foreach (int value in new[] { int.MinValue, -1, 0, 4, 5, 6, int.MaxValue })
                Assert.Equal(Evaluate(native, value), Evaluate(reauthored, value));
        }
    }

    [Fact]
    public void BooleanGuard_RejectsComparisonWithoutTwoOperands()
    {
        var malformed = new[] { Op(0xAE, 1), Op(0x0B), Op(0xAE, 1) };
        Assert.False(AiVarConditionBuilder.IsStackCleanBooleanGuard(malformed, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverdriveThreshold_ComputesCurrentAgainstMaximum(bool atLeast)
    {
        var method = typeof(AiAutomation).GetMethod("BuildOverdriveCurrentVsMaxGuard", BindingFlags.Static | BindingFlags.NonPublic)!;
        var guard = (IReadOnlyList<AiInstruction>)method.Invoke(null, new object[] { atLeast })!;
        foreach (int current in new[] { 0, 99, 100, 101 })
        {
            // The two readChrProperty calls push max then current, as in the native guard.
            var fields = new Queue<int>(new[] { 100, current });
            Assert.Equal(atLeast ? current >= 100 : current > 100, Evaluate(guard, 0, fields) != 0);
        }
    }

    [Fact]
    public void PercentageBuilders_UseNativeMultiplyAndBoundedDivision()
    {
        foreach (var guard in new[]
        {
            AiAutomation.BuildLastDamageTakenAtLeastPercentMaxHpGuard(25),
            AiAutomation.BuildHpBetweenPercentGuard(25, 75),
        })
        {
            Assert.Contains(guard, i => i.Opcode == 0x16);
            Assert.Contains(guard, i => i.Opcode == 0x17);
            // Division is intentional for overflow-safe percentage thresholds;
            // it must never replace a multiplication or divide by an actor value.
            foreach (int index in Enumerable.Range(0, guard.Count).Where(i => guard[i].Opcode == 0x17))
            {
                Assert.True(index > 0);
                Assert.Equal((byte)0xAE, guard[index - 1].Opcode);
                Assert.Equal((ushort)100, guard[index - 1].Operand);
            }
        }
    }

    internal static List<AiInstruction> ImmediateGuard(byte opcode, bool reverse, short value)
    {
        var variable = Op(0x9F, 0);
        var literal = Op(0xAE, unchecked((ushort)value));
        return new List<AiInstruction> { reverse ? literal : variable, reverse ? variable : literal, Op(opcode) };
    }

    internal static AiInstruction Op(byte opcode, ushort operand = 0) => new()
    {
        Offset = -1, Opcode = opcode, HasOperand = (opcode & 0x80) != 0, Operand = operand,
        OperandKind = AiScript_File.OperandKindOf(opcode),
    };

    static byte[] Monster() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));

    static bool RequestedRelation(string symbol, int a, int b) => symbol switch
    {
        "==" => a == b, "!=" => a != b, ">" => a > b, "<" => a < b,
        ">=" => a >= b, "<=" => a <= b, _ => throw new ArgumentException(symbol),
    };

    // Independent integer oracle from the native handlers @864577..864791, not the
    // product's opcode constants or labels. PUSHII sign-extension is @864B51.
    static int Evaluate(IReadOnlyList<AiInstruction> instructions, int variable, Queue<int>? fieldValues = null)
    {
        var stack = new Stack<int>();
        foreach (var instruction in instructions)
        {
            if (instruction.Opcode == 0x9F) { stack.Push(variable); continue; }
            if (instruction.Opcode == 0xAE) { stack.Push(unchecked((short)instruction.Operand)); continue; }
            if (instruction.Opcode == 0xB5 && instruction.Operand == 0x700F)
            {
                stack.Pop(); stack.Pop(); stack.Push(fieldValues!.Dequeue()); continue;
            }
            int b = stack.Pop(), a = stack.Pop();
            int result = instruction.Opcode switch
            {
                0x06 => a == b ? 1 : 0,
                0x07 => a != b ? 1 : 0,
                // Direct EXE disassembly: JBE @86462D / JB @86469C,
                // distinct from signed JLE @864711 / JL @864786.
                0x08 => (uint)a > (uint)b ? 1 : 0,
                0x09 => (uint)a < (uint)b ? 1 : 0,
                0x0C => (uint)a >= (uint)b ? 1 : 0,
                0x0D => (uint)a <= (uint)b ? 1 : 0,
                0x0A => a > b ? 1 : 0,
                0x0B => a < b ? 1 : 0,
                0x0E => a >= b ? 1 : 0,
                0x0F => a <= b ? 1 : 0,
                0x01 => a != 0 || b != 0 ? 1 : 0,
                0x02 => a != 0 && b != 0 ? 1 : 0,
                0x14 => unchecked(a + b), 0x15 => unchecked(a - b),
                0x16 => unchecked(a * b), 0x17 => a / b,
                _ => throw new InvalidOperationException($"Opcode {instruction.Opcode:X2} is outside this test oracle."),
            };
            stack.Push(result);
        }
        Assert.Single(stack);
        return stack.Pop();
    }
}
