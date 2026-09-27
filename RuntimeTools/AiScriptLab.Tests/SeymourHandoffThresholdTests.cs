using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace AiScriptLab.Tests;

public class SeymourHandoffThresholdTests
{
    static byte[] Monster(string id = "m124") => File.ReadAllBytes(Path.Combine(
        Environment.GetEnvironmentVariable("FFX_CORPUS")
        ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon", "_" + id, id + ".bin"));
    static AiScriptFile Read(byte[] monster) => AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);

    [Fact]
    public void CalculatedStages_EditReopenRestoreAndPreserveOtherBytes()
    {
        byte[] monster = Monster();
        var script = Read(monster);
        byte[] before = AiScript_File.Write(script);
        Assert.True(AiSeymourHandoffThresholdWriter.TryDetect(script, out var descriptor));
        Assert.Equal((0x25E, 0x7CA, 0x84A), (descriptor!.FirstOffset, descriptor.SecondOffset, descriptor.ThirdOffset));
        Assert.Equal((5, 4, 3), (descriptor.First, descriptor.Second, descriptor.Third));
        Assert.True(AiSeymourHandoffThresholdWriter.TryApply(script, descriptor, 5, 3, 2, out var edited, out var error), error);
        Assert.Equal(before, AiScript_File.Write(script));
        Assert.Equal(before.Length, edited!.Length);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != edited[i]).ToArray();
        Assert.Equal(new[] { 0x7CB, 0x84B }, changed);
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(edited, script, before.Length, out _, out error), error);
        byte[] spliced = AiScript_File.SpliceAiFileIntoMonster(monster, edited);
        int start = BitConverter.ToInt32(monster, 4);
        Assert.All(Enumerable.Range(0, monster.Length).Where(i => monster[i] != spliced[i]),
            i => Assert.Contains(i - start, changed));
        var reopened = Read(spliced);
        Assert.True(AiSeymourHandoffThresholdWriter.TryDetect(reopened, out var current));
        Assert.Equal((5, 3, 2), (current!.First, current.Second, current.Third));
        Assert.True(AiSeymourHandoffThresholdWriter.TryApply(reopened, current, 5, 4, 3, out var restored, out error), error);
        Assert.Equal(before, restored);
        Assert.Equal(monster, AiScript_File.SpliceAiFileIntoMonster(spliced, restored!));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(6, 4, 3)]
    [InlineData(4, 4, 3)]
    [InlineData(3, 4, 5)]
    [InlineData(5, 3, -1)]
    [InlineData(32768, 4, 3)]
    public void InvalidStageOrderOrRangeIsRejectedWithoutMutation(int first, int second, int third)
    {
        var script = Read(Monster());
        byte[] before = AiScript_File.Write(script);
        Assert.True(AiSeymourHandoffThresholdWriter.TryDetect(script, out var descriptor));
        Assert.False(AiSeymourHandoffThresholdWriter.TryApply(script, descriptor!, first, second, third, out var edited, out _));
        Assert.Null(edited);
        Assert.Equal(before, AiScript_File.Write(script));
    }

    [Theory]
    [InlineData(0x025A, 0)] // Zero divisor.
    [InlineData(0x0254, 0x0119)] // Boolean predicate is not maxHP.
    [InlineData(0x075B, 1)] // Wrong state guard.
    [InlineData(0x0763, 2)] // Wrong state producer.
    [InlineData(0x0863, 255)] // Wrong handoff sentinel.
    [InlineData(0x097D, 0x6051)] // Wrong activation command.
    public void BrokenProducerConsumerContractIsNotPromoted(int offset, int operand)
    {
        var script = Read(Monster());
        script.Instructions.Single(i => i.Offset == offset).Operand = (ushort)operand;
        Assert.False(AiSeymourHandoffThresholdWriter.TryDetect(script, out _));
    }

    [Fact]
    public void StaleDescriptorAndIncomingBranchIntoExpressionAreRejected()
    {
        var script = Read(Monster());
        Assert.True(AiSeymourHandoffThresholdWriter.TryDetect(script, out var descriptor));
        script.Instructions.Single(i => i.Offset == 0x02CC).Operand++;
        Assert.True(AiSeymourHandoffThresholdWriter.TryDetect(script, out _)); // Compatible route authoring remains possible.
        Assert.False(AiSeymourHandoffThresholdWriter.TryApply(script, descriptor!, 5, 3, 2, out _, out _));
        byte[] redirected = AiScript_File.EditJumpTarget(script, 0, 0, 0x025A - script.ScriptStart);
        Assert.False(AiSeymourHandoffThresholdWriter.TryDetect(AiScript_File.Read(redirected), out _));
        Assert.False(AiSeymourHandoffThresholdWriter.TryDetect(Read(Monster("m125")), out _));
    }
}
