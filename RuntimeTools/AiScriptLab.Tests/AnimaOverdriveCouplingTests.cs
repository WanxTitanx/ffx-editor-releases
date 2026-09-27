using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace AiScriptLab.Tests;

public class AnimaOverdriveCouplingTests
{
    static AiScriptFile Read() => AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(Path.Combine(
        Environment.GetEnvironmentVariable("FFX_CORPUS")
        ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon", "_m125", "m125.bin")))!);

    [Fact]
    public void CapacityAndAttackThresholdRemainEqualAcrossEditAndRestore()
    {
        var script = Read();
        byte[] before = AiScript_File.Write(script);
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out var rows, out var error),error);
        var row = Assert.Single(rows);
        Assert.True(AiAnimaOdThresholdWriter.TryApplyPatch(script, new(row.MaximumInstructionOffset,100,80),out var result,out error),error);
        Assert.Equal(80,BitConverter.ToUInt16(result!.EditedAiFileBytes,0x3F9));
        Assert.Equal(80,BitConverter.ToUInt16(result.EditedAiFileBytes,0x1C7));
        Assert.Equal(new[]{0x1C7,0x3F9},result.ChangedBytes.Select(c=>c.Offset));
        var reopened=AiScript_File.Read(result.EditedAiFileBytes);
        Assert.True(AiAnimaOdThresholdWriter.TryApplyPatch(reopened,new(row.MaximumInstructionOffset,80,100),out var restored,out error),error);
        Assert.Equal(before,restored!.EditedAiFileBytes);
        Assert.Equal(before,AiScript_File.Write(script));
    }

    [Fact]
    public void IndependentAttackEditInvalidatesCapacityContract()
    {
        var script=Read();
        script.Instructions.Single(i=>i.Offset==0x3F8).Operand=90;
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script,out var rows,out _));
        Assert.Equal(90,Assert.Single(rows).CurrentAttackThreshold);
        Assert.False(AiAnimaOdThresholdWriter.TryApplyPatch(script,new(0x1C6,100,80),out _,out _));
    }

    [Fact]
    public void ExplicitPairSnapshotCanRepairAnEarlierCapacityOnlyEdit()
    {
        var script=Read();
        script.Instructions.Single(i=>i.Offset==0x1C6).Operand=80;
        Assert.True(AiAnimaOdThresholdWriter.TryBuildDescriptors(script,out var rows,out var error),error);
        var row=Assert.Single(rows);
        Assert.False(AiAnimaOdThresholdWriter.TryApplyPatch(script,new(0x1C6,80,80),out _,out _));
        Assert.True(AiAnimaOdThresholdWriter.TryApplyPatch(script,new(0x1C6,80,80,row.CurrentAttackThreshold),out var result,out error),error);
        Assert.Equal(new[]{0x3F9},result!.ChangedBytes.Select(c=>c.Offset));
        Assert.Equal(80,BitConverter.ToUInt16(result.EditedAiFileBytes,0x3F9));
        Assert.Equal(100,script.Instructions.Single(i=>i.Offset==0x3F8).Operand);
    }
}
