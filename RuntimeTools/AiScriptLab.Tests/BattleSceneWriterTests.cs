using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace AiScriptLab.Tests;

public class BattleSceneWriterTests
{
    static string Root => Environment.GetEnvironmentVariable("FFX_CORPUS")
        ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";
    static byte[] Encounter() => File.ReadAllBytes(Path.Combine(Root,"..","btl","mcyt06_00","mcyt06_00.bin"));
    static AiScriptFile Actor() => AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(
        File.ReadAllBytes(Path.Combine(Root,"_m125","m125.bin")))!);

    [Fact]
    public void ResolvesActualSceneWorkerAndEditsOnlySelectorThenRestores()
    {
        var script=Actor(); byte[] encounter=Encounter(), before=AiScript_File.Write(script);
        var rows=AiBattleSceneWriter.Detect(script,125,encounter);
        var row=Assert.Single(rows.Where(r=>r.CallOffset==0x686));
        Assert.Equal(0x703C,row.FunctionId);
        Assert.Equal(2,row.Selector);
        Assert.Equal(new AiBattleSceneOption(2,0,4,0xC98),row.Options.Single(o=>o.Selector==2));
        Assert.Equal(new AiBattleSceneOption(3,0,5,0xD53),row.Options.Single(o=>o.Selector==3));
        Assert.True(AiBattleSceneWriter.TryApply(script,125,encounter,row,3,out var output,out var error),error);
        Assert.Equal(new[]{0x684},Enumerable.Range(0,before.Length).Where(i=>before[i]!=output![i]));
        Assert.Equal(before,AiScript_File.Write(script));
        var parsed=AiScript_File.Read(output!);
        var current=AiBattleSceneWriter.Detect(parsed,125,encounter).Single(r=>r.CallOffset==row.CallOffset);
        Assert.True(AiBattleSceneWriter.TryApply(parsed,125,encounter,current,2,out var restored,out error),error);
        Assert.Equal(before,restored);
    }

    [Fact]
    public void RejectsUnknownSceneWrongEncounterAndStaleEncounter()
    {
        var script=Actor(); byte[] encounter=Encounter();
        var row=AiBattleSceneWriter.Detect(script,125,encounter).First();
        foreach(int selector in new[]{-1,6,7,8,24,32768})
            Assert.False(AiBattleSceneWriter.TryApply(script,125,encounter,row,selector,out _,out _));
        Assert.Empty(AiBattleSceneWriter.Detect(script,131,encounter));
        encounter[^1]^=1;
        Assert.False(AiBattleSceneWriter.TryApply(script,125,encounter,row,2,out _,out _));
    }

    [Fact]
    public void SceneWithoutGateCleanupAndCorruptMapAreNotWritable()
    {
        var script=Actor(); byte[] encounter=Encounter();
        int aiStart=BitConverter.ToInt32(encounter,4);
        encounter[aiStart+0xD4F+1]=0x3E; // A(2)'s final clear becomes poll.
        Assert.DoesNotContain(AiBattleSceneWriter.Detect(script,125,encounter),r=>r.Selector==2);
        encounter=Encounter(); int workerStart=BitConverter.ToInt32(encounter,8);
        encounter[workerStart+2+62]=255;
        Assert.Empty(AiBattleSceneWriter.Detect(script,125,encounter));
        Assert.Empty(AiBattleSceneWriter.Detect(script,125,new byte[16]));
    }

    [Fact]
    public void CyclicSceneAndMalformedPointerTablesAreRejected()
    {
        var script=Actor(); byte[] encounter=Encounter();
        int start=BitConverter.ToInt32(encounter,4);
        var scene=AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(encounter)!);
        byte[] cyclic=AiScript_File.EditJumpTarget(scene,0,22,0xC98-scene.ScriptStart);
        Array.Copy(cyclic,0,encounter,start,cyclic.Length);
        Assert.DoesNotContain(AiBattleSceneWriter.Detect(script,125,encounter),r=>r.Selector==2);
        foreach(int offset in new[]{0,4,8,12,16,start,start+0x30,start+0x38})
            foreach(int value in new[]{-1,int.MinValue,int.MaxValue})
            {
                encounter=Encounter();
                BitConverter.GetBytes(value).CopyTo(encounter,offset);
                Assert.Empty(AiBattleSceneWriter.Detect(script,125,encounter));
            }
    }

    [Fact]
    public void OmnisSceneWithProvenCountedLoopsHasAllFourCalls()
    {
        byte[] encounter=File.ReadAllBytes(Path.Combine(Root,"..","btl","sins03_00","sins03_00.bin"));
        var script=AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(Path.Combine(Root,"_m131","m131.bin")))!);
        var rows=AiBattleSceneWriter.Detect(script,131,encounter);
        Assert.Equal(4,rows.Count);
        var row=rows.Single(r=>r.CallOffset==0x1889);
        Assert.Equal(7,row.Selector);
        Assert.Equal(0x1A7F,row.Options.Single(o=>o.Selector==7).EntrypointOffset);
    }

    [Theory]
    [InlineData(0x1B32,0)] // Limit zero is never reached after increment.
    [InlineData(0x1B28,0)] // Counter does not progress.
    [InlineData(0x1B39,1)] // Flag is never cleared.
    [InlineData(0x1ACB,1)] // Different initial counter requires another proof.
    public void OmnisLoopWithoutItsCounterContractStaysBlocked(int offset,int value)
    {
        byte[] encounter=File.ReadAllBytes(Path.Combine(Root,"..","btl","sins03_00","sins03_00.bin"));
        var script=AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(Path.Combine(Root,"_m131","m131.bin")))!);
        int start=BitConverter.ToInt32(encounter,4);
        BitConverter.GetBytes((ushort)value).CopyTo(encounter,start+offset+1);
        Assert.DoesNotContain(AiBattleSceneWriter.Detect(script,131,encounter),r=>r.Selector==7);
    }

    [Fact]
    public void OmnisLoopRejectsAnOverlappingPrivateSlotInTheSameWorker()
    {
        byte[] encounter=File.ReadAllBytes(Path.Combine(Root,"..","btl","sins03_00","sins03_00.bin"));
        var script=AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(Path.Combine(Root,"_m131","m131.bin")))!);
        int ai=BitConverter.ToInt32(encounter,4),worker=BitConverter.ToInt32(encounter,ai+0x38);
        int vars=BitConverter.ToInt32(encounter,ai+worker+0x14);
        BitConverter.GetBytes(0x5600000Cu).CopyTo(encounter,ai+vars+20*8); // Colour accumulator aliases loop counter.
        Assert.DoesNotContain(AiBattleSceneWriter.Detect(script,131,encounter),r=>r.Selector==7);
    }
}
