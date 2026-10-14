using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public class AdvancedPhaseAuthoringTests
{
    [Fact]
    public void NativeConditions_EditSwitchCaseWithProvenRegisterOrigin()
    {
        var script = SwitchScript(new[] { 0 });
        var condition = Assert.Single(AiNativeConditionWriter.Detect(script));
        Assert.True(condition.UsesSwitchRegister);
        Assert.True(condition.BranchWhenTrue);
        Assert.Equal(2, condition.Value);
        byte[] before = AiScript_File.Write(script);
        Assert.True(AiNativeConditionWriter.TryApply(script, condition, condition.Operator, condition.Value, out var unchanged, out var error), error);
        Assert.Equal(before, unchanged);
        Assert.True(AiNativeConditionWriter.TryApply(script, condition, AiVarCompareOperator.NotEqual, 7, out var edited, out error), error);
        Assert.Equal(before.Take(condition.Offset), edited!.Take(condition.Offset));
        Assert.Equal(before.Skip(condition.BranchOffset), edited.Skip(condition.BranchOffset));
        Assert.Equal(7, BitConverter.ToUInt16(edited, condition.Offset + 1));
        Assert.Equal(0x07, edited[condition.BranchOffset - 1]);
        Assert.Equal(before, AiScript_File.Write(script));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(10)]
    public void NativeConditions_RejectSwitchTableWithAmbiguousEntry(int additionalEntry)
    {
        Assert.Empty(AiNativeConditionWriter.Detect(SwitchScript(new[] { 0, additionalEntry })));
    }

    static AiScriptFile SwitchScript(int[] entries)
    {
        var script = Script((0x9F, 0), (0x2C, 0), (0xB0, 0), (0xAE, 2), (0x29, 0), (0x06, 0), (0xD6, 1), (0x3C, 0));
        return new AiScriptFile
        {
            OriginalAiFileBytes = script.OriginalAiFileBytes, HeaderBytes = script.HeaderBytes,
            DataBytes = script.DataBytes, Instructions = script.Instructions,
            ScriptStart = script.ScriptStart, CodeLength = script.CodeLength, DeclaredLength = script.DeclaredLength,
            Variables = script.Variables, CodeWalkClosedExactly = true, UnknownOpcodes = script.UnknownOpcodes,
            Workers = new[] { new AiWorker { Index = 0, Entrypoints = entries, JumpTargets = new[] { 7, 15 } } },
        };
    }

    [Fact]
    public void BranchReader_DoesNotDuplicateInheritedGuardsAtEveryBranch()
    {
        var script = Script((0x9F, 0), (0xAE, 1), (0x06, 0), (0xD7, 0),
            (0x9F, 0), (0xAE, 2), (0x07, 0), (0xD7, 0),
            (0xAE, 1), (0xD7, 0), // An unnamed guard must not re-add the entire inherited expression.
            (0xAE, 0xFFF3), (0xAE, 0x3045), (0xD8, 0x700B), (0x3C, 0));
        var actions = AiAutomation.DetectBranchSensitiveActions(Array.Empty<byte>(), script);
        var action = Assert.Single(actions.Where(a => a.WorkerIndex == 0 && a.EntrypointIndex == 0));
        Assert.Equal("battleVar0004 == 1 + battleVar0004 != 2", action.GuardSummary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeConditions_EditBothOperandOrdersWithoutMovingBranches(bool reversed)
    {
        var script = reversed
            ? Script((0xAE, 5), (0x9F, 0), (0x0B, 0), (0xD7, 0), (0x3C, 0))
            : Script((0x9F, 0), (0xAE, 5), (0x0B, 0), (0xD7, 0), (0x3C, 0));
        var condition = Assert.Single(AiNativeConditionWriter.Detect(script));
        byte[] before = AiScript_File.Write(script);
        foreach (var op in Enum.GetValues<AiVarCompareOperator>())
        {
            Assert.True(AiNativeConditionWriter.TryApply(script, condition, op, -7, out var edited, out var error), error);
            Assert.Equal(before.Length, edited!.Length);
            Assert.Equal(before.Take(condition.Offset), edited.Take(condition.Offset));
            Assert.Equal(before.Skip(condition.BranchOffset), edited.Skip(condition.BranchOffset));
            var decoded = AiScript_File.Read(edited);
            Assert.True(AiVarConditionBuilder.TryReadImmediateComparison(decoded.Instructions, 0, out var clause));
            Assert.Equal(op, clause.Operator);
            Assert.Equal(-7, clause.Value);
            Assert.Equal(before, AiScript_File.Write(script));
        }
    }

    [Fact]
    public void NativeConditions_RejectStaleUnsupportedAndNonBranchComparisons()
    {
        var script = Script((0x9F, 0), (0xAE, 5), (0x0B, 0), (0xD7, 0), (0x3C, 0));
        var condition = Assert.Single(AiNativeConditionWriter.Detect(script));
        Assert.False(AiNativeConditionWriter.TryApply(script, condition, (AiVarCompareOperator)999, 1, out _, out _));
        Assert.False(AiNativeConditionWriter.TryApply(script, condition, condition.Operator, 32768, out _, out _));
        script.Instructions[1].Operand = 8;
        Assert.False(AiNativeConditionWriter.TryApply(script, condition, condition.Operator, 1, out _, out _));
        script.Instructions[3].Opcode = 0xB0;
        Assert.Empty(AiNativeConditionWriter.Detect(script));
    }

    [Fact]
    public void AdvancedSave_RejectsStaleAiAndBackupFailureAndPreservesOriginal()
    {
        string directory = Path.Combine(Path.GetTempPath(), "atel-advanced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "m001.bin");
            byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
            File.WriteAllBytes(path, original);
            byte[] ai = AiScript_File.SliceAiFileFromMonster(original)!;
            byte[] stale = (byte[])ai.Clone();
            stale[^1] ^= 1;
            Assert.Throws<InvalidOperationException>(() => AiAdvancedFileWriter.Save(path, stale, ai));
            Assert.Equal(original, File.ReadAllBytes(path));
            Directory.CreateDirectory(path + ".prev.bak");
            Exception? failure = Record.Exception(() => AiAdvancedFileWriter.Save(path, ai, ai));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Directory.Delete(path + ".prev.bak");
            AiAdvancedFileWriter.Save(path, ai, ai);
            Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(65535)]
    public void Anima_RejectsValuesOutsideNativeClampWithoutMutation(int value)
    {
        var script = GaugeScript();
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var rows, out _));
        byte[] before = AiScript_File.Write(script);
        var request = new AiAnimaOdThresholdPatchRequest(rows[0].MaximumInstructionOffset, 100, (ushort)value);
        Assert.False(AiAnimaOdThresholdWriter.TryApplyPatch(script, request, out var result, out var error));
        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, AiScript_File.Write(script));
    }

    [Fact]
    public void Anima_EditsCapacityAndAttackThresholdAndKeepsSourceModelUnmodified()
    {
        var script = GaugeScript();
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var rows, out _));
        byte[] before = AiScript_File.Write(script);
        var request = new AiAnimaOdThresholdPatchRequest(rows[0].MaximumInstructionOffset, 100, 80);
        Assert.True(AiAnimaOdThresholdWriter.TryApplyPatch(script, request, out var result, out var error), error);
        Assert.All(result!.ChangedBytes, change => Assert.Contains(change.Offset, new[] {
            request.MaximumInstructionOffset + 1, request.MaximumInstructionOffset + 2,
            rows[0].AttackInstructionOffset + 1, rows[0].AttackInstructionOffset + 2 }));
        Assert.Equal(80, BitConverter.ToUInt16(result.EditedAiFileBytes, request.MaximumInstructionOffset + 1));
        Assert.Equal(80, BitConverter.ToUInt16(result.EditedAiFileBytes, rows[0].AttackInstructionOffset + 1));
        Assert.Equal(before, AiScript_File.Write(script));
    }

    [Theory]
    [InlineData("UpdateAdvancedAnimaContext", "AdvancedAnimaOdThresholdRows")]
    [InlineData("UpdateAdvancedElementalClusterContext", "AdvancedOmnisClusterEvidenceRows")]
    [InlineData("UpdateAdvancedMortiorchisContext", "AdvancedMortiorchisCompanionEvidenceRows")]
    [InlineData("UpdateAdvancedSupportAccumulatorContext", "AdvancedMortibodyAccumulatorEvidenceRows")]
    [InlineData("UpdateAdvancedReactiveSensorContext", "AdvancedReactiveSensorEvidenceRows")]
    [InlineData("UpdateAdvancedRoundScriptedBossContext", "AdvancedRoundScriptedBossEvidenceRows")]
    public void SpecializedEditors_ClearStaleFieldsWhenMonsterChanges(string refresh, string property)
    {
        var model = new MonsterAiEditor_DataModel();
        var rows = (IList)typeof(MonsterAiEditor_DataModel).GetProperty(property)!.GetValue(model)!;
        rows.Add(RuntimeHelpers.GetUninitializedObject(rows.GetType().GetGenericArguments()[0]));
        typeof(MonsterAiEditor_DataModel).GetMethod(refresh, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        Assert.Empty(rows);
    }

    [Fact]
    public void Mortiorchis_ExposesEachCommandCallInsteadOfOnlyTheFirstLiteral()
    {
        var script = Script((0xAE, 0x608C), (0xA0, 0),
            (0xAE, 0xFFF3), (0xAE, 0x608C), (0xD8, 0x700B),
            (0xAE, 0xFFF3), (0xAE, 0x608C), (0xD8, 0x705A), (0x3C, 0));
        Assert.True(AiMortiorchisCompanionWriter.TryBuildDescriptors(script, out var rows, out var error), error);
        Assert.Equal(new[] { script.Instructions[3].Offset, script.Instructions[6].Offset }, rows.Select(r => r.CommandInstructionOffset));
        Assert.Equal(2, rows.Select(r => r.BeatName).Distinct().Count());
    }

    [Fact]
    public void Omnis_RejectsUnrelatedCommandValuedLiterals()
    {
        var script = Script((0xAE, 0x303D), (0xA0, 0), (0xAE, 0x3045), (0xA0, 0), (0xAE, 0x3046), (0xA0, 0), (0x3C, 0));
        Assert.False(AiOmnisClusterWriter.TryBuildDescriptors(script, out _, out _));
    }

    [Fact]
    public void Anima_DoesNotTreatEffectSetterAndNearbyLiteralAsOverdriveThreshold()
    {
        // m125's motion hook writes stat_efflv (0x57); the following combat hook
        // pushes 0 for btlSetBodyHit. Neither is an Overdrive divisor.
        var script = Script((0xAE, 0xFFF3), (0xAE, 0x57), (0xAE, 1), (0xD8, 0x7018),
            (0x3C, 0), (0xAE, 0), (0xD8, 0x7055), (0x3C, 0));
        Assert.False(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var descriptors, out var error));
        Assert.Empty(descriptors);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Anima_RecognizesActualMaximumAndFullGaugeComparison()
    {
        var script = GaugeScript();
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var descriptors, out var error), error);
        var descriptor = Assert.Single(descriptors);
        Assert.Equal(100, descriptor.CurrentMaximum);
        Assert.Equal(script.Instructions[2].Offset, descriptor.MaximumInstructionOffset);
    }

    [Theory]
    [InlineData(0x0057)]
    [InlineData(0x0013)]
    public void Anima_RejectsWrongPropertyEvenWhenFullGaugeComparisonExists(int field)
    {
        var script = GaugeScript();
        script.Instructions[1].Operand = (ushort)field;
        Assert.False(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad!")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("0x10000")]
    public void NextState_RejectsInvalidInputInsteadOfSavingZero(string input)
    {
        var model = new AiIndirectDispatchUnitEditorVm(new[] { Unit() }, "route");
        model.NextStateText = input;
        Assert.False(model.TryBuildEditRequest(out var request, out var error));
        Assert.Empty(request.Edits);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    [InlineData("0x10", 16)]
    public void NextState_PreservesValidDecimalAndHexInput(string input, int expected)
    {
        var model = new AiIndirectDispatchUnitEditorVm(new[] { Unit() }, "route");
        model.NextStateText = input;
        Assert.True(model.TryBuildEditRequest(out var request, out var error), error);
        Assert.Equal(expected, Assert.Single(request.Edits).NewValue);
    }

    [Fact]
    public void Anima_RejectsAttackComparisonWithoutAnEntrypointOwner()
    {
        Assert.False(AiAnimaOdThresholdWriter.TryBuildDescriptors(GaugeScript(false), out _, out _));
    }

    internal static AiScriptFile GaugeScript(bool includeAttackEntrypoint = true)
    {
        var script = Script(
        (0xAE, 0xFFF3), (0xAE, 0x14), (0xAE, 100), (0xD8, 0x7018), (0x3C, 0),
        (0xAE, 0xFFF3), (0xAE, 0x13), (0xB5, 0x700F),
        (0xAE, 0xFFF3), (0xAE, 0x14), (0xB5, 0x700F), (0x0E, 0), (0xD7, 0), (0x3C, 0),
        (0xAE, 0xFFF3), (0xAE, 0x13), (0xB5, 0x700F), (0xAE, 100), (0x0E, 0), (0xD7, 0), (0x3C, 0));
        // The capacity, scene guard and attack guard run in three entrypoints.
        // Keep the shared fixture helper unchanged for the other regressions.
        return new AiScriptFile
        {
            HeaderBytes = script.HeaderBytes, DataBytes = script.DataBytes, OriginalAiFileBytes = script.OriginalAiFileBytes,
            Instructions = script.Instructions, CodeLength = script.CodeLength, ScriptStart = script.ScriptStart,
            DeclaredLength = script.DeclaredLength, CodeWalkClosedExactly = true, UnknownOpcodes = script.UnknownOpcodes,
            Variables = script.Variables,
            Workers = new[] { new AiWorker { Index = 0,
                Entrypoints = includeAttackEntrypoint ? new[] { 0, 13, 36 } : script.Workers[0].Entrypoints,
                JumpTargets = script.Workers[0].JumpTargets } },
        };
    }

    internal static AiScriptFile Script(params (byte Opcode, ushort Operand)[] code)
    {
        const int start = 0x80;
        int offset = start;
        var instructions = code.Select(op =>
        {
            var instruction = new AiInstruction
            {
                Offset = offset, Opcode = op.Opcode, Operand = op.Operand,
                HasOperand = (op.Opcode & 0x80) != 0, OperandKind = AiScript_File.OperandKindOf(op.Opcode),
            };
            offset += instruction.Length;
            return instruction;
        }).ToArray();
        var header = new byte[start];
        BitConverter.GetBytes(offset - start).CopyTo(header, 0);
        BitConverter.GetBytes(offset).CopyTo(header, 0x10);
        BitConverter.GetBytes(start).CopyTo(header, 0x30);
        return new AiScriptFile
        {
            HeaderBytes = header, DataBytes = Array.Empty<byte>(),
            OriginalAiFileBytes = header.Concat(instructions.SelectMany(i => i.Emit())).ToArray(),
            Instructions = instructions, CodeLength = offset - start, ScriptStart = start,
            DeclaredLength = offset, CodeWalkClosedExactly = true, UnknownOpcodes = Array.Empty<byte>(),
            Variables = new[] { new AiVariable { Index = 0, Storage = 0x52, Slot = 4 } },
            Workers = new[] { new AiWorker { Index = 0, Entrypoints = new[] { 0, 13 }, JumpTargets = new[] { offset - start - 1 } } },
        };
    }

    static AiIndirectDispatchUnit Unit() => new("route", 0, "turn", "", "", AiIndirectDispatchCapabilityTier.AuthoringCandidate,
        "", Array.Empty<AiIndirectDispatchWrite>(), Array.Empty<AiIndirectDispatchConsumer>(), Array.Empty<string>(), "", "",
        Array.Empty<AiIndirectDispatchEditableSlot>(), Array.Empty<AiIndirectDispatchEditableTargetSlot>(), 0x100, 1);
}
