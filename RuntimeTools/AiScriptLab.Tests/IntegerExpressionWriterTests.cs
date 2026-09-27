using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace AiScriptLab.Tests;

public class IntegerExpressionWriterTests
{
    static AiScriptFile Read(string id = "m124") => AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(Path.Combine(
        Environment.GetEnvironmentVariable("FFX_CORPUS")
        ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon", "_" + id, id + ".bin")))!);

    [Fact]
    public void CompoundCondition_ExposesTheRealTreeAndRestoresItsLiteral()
    {
        var script = Read();
        byte[] before = AiScript_File.Write(script);
        var row = Assert.Single(AiIntegerExpressionWriter.Detect(script).Where(r => r.SinkOffset == 0x760));
        Assert.Equal(0x751, row.StartOffset);
        Assert.Contains("&&", row.Expression);
        Assert.Contains("priv0010 <= priv0034", row.Expression);
        var literal = Assert.Single(row.Literals);
        Assert.Equal(0x75B, literal.Offset);
        Assert.True(AiIntegerExpressionWriter.TryApply(script, row, literal.Offset, 1, out var edited, out var error), error);
        Assert.Equal(new[] { 0x75C }, Enumerable.Range(0, before.Length).Where(i => before[i] != edited![i]));
        Assert.Equal(before, AiScript_File.Write(script));
        var reopened = AiScript_File.Read(edited!);
        var current = Assert.Single(AiIntegerExpressionWriter.Detect(reopened).Where(r => r.SinkOffset == row.SinkOffset));
        Assert.True(AiIntegerExpressionWriter.TryApply(reopened, current, literal.Offset, 0, out var restored, out error), error);
        Assert.Equal(before, restored);
    }

    [Fact]
    public void CalculatedAssignmentKeepsArithmeticOrderAndRejectsZeroDivisor()
    {
        var script = Read("m142");
        var row = AiIntegerExpressionWriter.Detect(script).First(r => r.Expression.Contains("HP.max /"));
        var divisor = row.Literals.First(l => l.Minimum == 1);
        Assert.False(AiIntegerExpressionWriter.TryApply(script, row, divisor.Offset, 0, out _, out _));
        Assert.False(AiIntegerExpressionWriter.TryApply(script, row, divisor.Offset, -1, out _, out _));
        Assert.True(AiIntegerExpressionWriter.TryApply(script, row, divisor.Offset, divisor.Value + 1, out var edited, out var error), error);
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(edited!, script, script.OriginalAiFileBytes.Length, out _, out error), error);
    }

    [Fact]
    public void MidExpressionEntryUnknownCallAndFloatVariableAreNotEditable()
    {
        var script = Read();
        byte[] redirected = AiScript_File.EditJumpTarget(script, 0, 0, 0x75B - script.ScriptStart);
        Assert.DoesNotContain(AiIntegerExpressionWriter.Detect(AiScript_File.Read(redirected)), r => r.SinkOffset == 0x760);
        script.Instructions.Single(i => i.Offset == 0x06F1).Operand = 0xFFFF;
        Assert.DoesNotContain(AiIntegerExpressionWriter.Detect(script), r => r.SinkOffset == 0x06F8);
        byte[] bytes = AiScript_File.Write(Read());
        int vars = BitConverter.ToInt32(bytes, BitConverter.ToInt32(bytes, 0x38) + 0x14);
        bytes[vars + 12 * 8 + 3] = 0x66; // F32 instead of I32 for priv0010.
        Assert.DoesNotContain(AiIntegerExpressionWriter.Detect(AiScript_File.Read(bytes)), r => r.SinkOffset == 0x760);
    }

    [Fact]
    public void PropertyIdentityAndStaleDescriptorsCannotBePatched()
    {
        var script = Read();
        var row = AiIntegerExpressionWriter.Detect(script).Single(r => r.SinkOffset == 0x06F8);
        Assert.Contains("HP.lastDamage", row.Expression);
        Assert.False(AiIntegerExpressionWriter.TryApply(script, row, 0x06EE, 0, out _, out _));
        script.Instructions.Single(i => i.Offset == 0x02CC).Operand++;
        Assert.False(AiIntegerExpressionWriter.TryApply(script, row, row.Literals[0].Offset, 1, out _, out _));
    }

    [Fact]
    public void UnknownControlSemanticsCannotProvideAWriterContract()
    {
        var script=Read();
        script.Instructions.First(i=>i.Opcode==0xAE).Opcode=0xFF;
        script=AiScript_File.Read(AiScript_File.Write(script));
        Assert.NotEmpty(script.UnknownOpcodes);
        Assert.Empty(AiIntegerExpressionWriter.Detect(script));
        Assert.False(AiSeymourHandoffThresholdWriter.TryDetect(script,out _));
    }

    [Fact]
    public void DifferentWorkerVariableTablesDoNotReuseTheFirstWorkersNames()
    {
        var script=Read("m125");
        Assert.NotEmpty(AiIntegerExpressionWriter.Detect(script));
        byte[] bytes=AiScript_File.Write(script);
        int offset=script.Workers[1].DescriptorOffset+0x14;
        BitConverter.GetBytes(BitConverter.ToInt32(bytes,offset)+8).CopyTo(bytes,offset);
        Assert.Empty(AiIntegerExpressionWriter.Detect(AiScript_File.Read(bytes)));
    }

    [Fact]
    public void MortibodyCalculatedGuardCanEditItsComparisonWithoutAnImmediate()
    {
        var script=Read("m127"); byte[] original=AiScript_File.Write(script);
        var row=AiIntegerExpressionWriter.Detect(script).Single(r=>r.SinkOffset==0x617);
        Assert.Empty(row.Literals);
        Assert.Contains("priv0010 + priv0014",row.Expression);
        Assert.True(AiIntegerExpressionWriter.TryApply(script,row,new(OperatorOffset:0x616,Opcode:0x0A),out var output,out var error),error);
        Assert.Equal(new[]{0x616},Enumerable.Range(0,original.Length).Where(i=>original[i]!=output![i]));
        var parsed=AiScript_File.Read(output!);var current=AiIntegerExpressionWriter.Detect(parsed).Single(r=>r.SinkOffset==0x617);
        Assert.True(AiIntegerExpressionWriter.TryApply(parsed,current,new(OperatorOffset:0x616,Opcode:0x0E),out var restored,out error),error);
        Assert.Equal(original,restored);
    }

    [Fact]
    public void CompoundGuardEditsLiteralAndConnectiveAtomicallyAndRejectsWrongOpcodeKind()
    {
        var script=Read(); byte[] original=AiScript_File.Write(script);
        var row=AiIntegerExpressionWriter.Detect(script).Single(r=>r.SinkOffset==0x760);
        Assert.True(AiIntegerExpressionWriter.TryApply(script,row,new(0x75B,1,0x75F,0x01),out var output,out var error),error);
        Assert.Equal(new[]{0x75C,0x75F},Enumerable.Range(0,original.Length).Where(i=>original[i]!=output![i]));
        Assert.False(AiIntegerExpressionWriter.TryApply(script,row,new(OperatorOffset:0x75F,Opcode:0xD8),out _,out _));
        Assert.False(AiIntegerExpressionWriter.TryApply(script,row,new(OperatorOffset:0x757,Opcode:0x02),out _,out _));
        Assert.False(AiIntegerExpressionWriter.TryApply(script,row,new(OperatorOffset:0x75B,Opcode:0x06),out _,out _));
        Assert.Equal(original,AiScript_File.Write(script));
    }

    [Fact]
    public void UniquePooledIntegersCanBeEditedWithoutChangingCode()
    {
        var root=Environment.GetEnvironmentVariable("FFX_CORPUS")
            ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";
        var examples=Directory.EnumerateFiles(root,"m*.bin",SearchOption.AllDirectories).Order()
            .Select(path=>AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(path))!))
            .SelectMany(script=>AiIntegerExpressionWriter.Detect(script).SelectMany(row=>row.Literals
                .Where(l=>l.PoolOffset.HasValue).Select(l=>(script,row,l)))).Take(1).ToArray();
        var (script,row,literal)=Assert.Single(examples);
        byte[] before=AiScript_File.Write(script);
        int value=literal.Value==literal.Maximum ? literal.Value-1 : literal.Value+1;
        Assert.True(AiIntegerExpressionWriter.TryApply(script,row,literal.Offset,value,out var output,out var error),error);
        Assert.Equal(value,BitConverter.ToInt32(output!,literal.PoolOffset!.Value));
        Assert.All(Enumerable.Range(0,before.Length).Where(i=>before[i]!=output![i]),
            i=>Assert.InRange(i,literal.PoolOffset.Value,literal.PoolOffset.Value+3));
        var parsed=AiScript_File.Read(output!);
        var current=AiIntegerExpressionWriter.Detect(parsed).Single(r=>r.SinkOffset==row.SinkOffset);
        Assert.True(AiIntegerExpressionWriter.TryApply(parsed,current,literal.Offset,literal.Value,out var restored,out error),error);
        Assert.Equal(before,restored);
        ushort poolIndex=script.Instructions.Single(i=>i.Offset==literal.Offset).Operand;
        var secondReference=script.Instructions.First(i=>i.Opcode==0xAE);
        secondReference.Opcode=0xAD;
        secondReference.Operand=poolIndex;
        Assert.DoesNotContain(AiIntegerExpressionWriter.Detect(script).SelectMany(r=>r.Literals),l=>l.Offset==literal.Offset);
    }
}
