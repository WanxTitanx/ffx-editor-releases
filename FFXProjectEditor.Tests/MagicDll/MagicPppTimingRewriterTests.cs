using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicPppTimingRewriterTests
{
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public void CompressesDurationsOrdersCollidingKeysAndCompensatesRatesWithoutChangingPointers()
    {
        var file=Fixture(); byte[] source=file.FileBytes.ToArray();
        var result=MagicPppTimingRewriter.Compress(file,45,Hash(source));
        Assert.Equal(new[]{580},result.BeforeFrames);Assert.Equal(new[]{45},result.AfterFrames);
        Assert.Equal(source,file.FileBytes);
        byte[] output=result.Bytes;
        uint Read(int at)=>BitConverter.ToUInt32(output,32+at);
        Assert.Equal(45u*4096,Read(128));
        Assert.Equal(0u,Read(256));Assert.Equal(4096u,Read(288));Assert.Equal(8192u,Read(320));
        Assert.Equal(3u*4096,Read(144+20)); // dense keys remain executable, not collapsed into one tick
        Assert.Equal(0x70000000u,Read(144+24));Assert.Equal(0x70000000u,Read(144+28));
        Assert.Equal(0xFFFFF000u,Read(352));
        Assert.Equal((float)(580.0/45),BitConverter.ToSingle(output,32+272));
        Assert.Equal(-1289,BitConverter.ToInt16(output,32+520));
        var permitted=result.Edits.SelectMany(e=>Enumerable.Range(32+e.DataOffset,e.Bytes.Length/2)).ToHashSet();
        Assert.All(Enumerable.Range(0,source.Length).Where(i=>source[i]!=output[i]),i=>Assert.Contains(i,permitted));
        Assert.Equal(source.Take(32),output.Take(32));
        Assert.Equal(source.Skip(32+184).Take(32),output.Skip(32+184).Take(32)); // both 16B slot descriptors
    }

    [Fact]
    public void PreservesExternalStartAndInfiniteLifetime()
    {
        var file=Fixture(0x7FFFFFFF,0x70000000);
        var result=MagicPppTimingRewriter.Compress(file,60,Hash(file.FileBytes));
        Assert.Equal(0x7FFFFFFFu,BitConverter.ToUInt32(result.Bytes,32+144+16));
        Assert.Equal(0x70000000u,BitConverter.ToUInt32(result.Bytes,32+144+20));
    }

    [Theory]
    [InlineData(256,1u)] // fractional event cannot be hit by the native +4096 tick
    [InlineData(288,0u)] // duplicate keys
    [InlineData(352,0u)] // missing end sentinel
    [InlineData(188,0u)] // slot width/metadata mismatch
    public void RejectsInvalidInputWithoutMutatingIt(int offset,uint value)
    {
        var file=Fixture();BitConverter.GetBytes(value).CopyTo(file.Data,offset);
        file.Data.CopyTo(file.FileBytes,32);byte[] source=file.FileBytes.ToArray();
        Assert.Throws<InvalidDataException>(()=>MagicPppTimingRewriter.Compress(file,45,Hash(source)));
        Assert.Equal(source,file.FileBytes);
    }

    [Fact]
    public void RejectsHashDriftAndInconsistentSnapshots()
    {
        var file=Fixture();
        Assert.Throws<InvalidDataException>(()=>MagicPppTimingRewriter.Compress(file,45,new string('0',64)));
        file.Data[0]^=1;
        Assert.Throws<InvalidDataException>(()=>MagicPppTimingRewriter.Compress(file,45,Hash(file.FileBytes)));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(6001)]
    public void RejectsInvalidTarget(int frames)
    {
        var file=Fixture();Assert.Throws<ArgumentOutOfRangeException>(()=>MagicPppTimingRewriter.Compress(file,frames,Hash(file.FileBytes)));
    }

    [Fact]
    public void TickMappingPreservesSentinelsAndRoundsToNativeTicks()
    {
        Assert.Equal(0x70000000u,MagicPppTimingRewriter.MapTick(0x70000000,580,45));
        Assert.Equal(0x7FFFFFFFu,MagicPppTimingRewriter.MapTick(0x7FFFFFFF,580,45));
        Assert.Equal(0xFFFFF000u,MagicPppTimingRewriter.MapTick(0xFFFFF000,580,45));
        Assert.Equal(14u*4096,MagicPppTimingRewriter.MapTick(174u*4096,580,45));
        Assert.Equal(4096u,MagicPppTimingRewriter.MapTick(4096,580,45,true));
    }

    private static MagicDllFile Fixture(uint start=0,uint life=3*4096)
    {
        byte[] d=new byte[1024];void U(int p,uint v)=>BitConverter.GetBytes(v).CopyTo(d,p);
        U(128,580*4096);U(144+16,start);U(144+20,life);U(144+24,0x70000000);U(144+28,0x70000000);
        BitConverter.GetBytes((ushort)2).CopyTo(d,144+38);
        // Program and slots retain their exact serialized metadata.
        BitConverter.GetBytes((ushort)32).CopyTo(d,188);BitConverter.GetBytes((ushort)3).CopyTo(d,190);U(192,128);
        BitConverter.GetBytes((ushort)16).CopyTo(d,204);BitConverter.GetBytes((ushort)1).CopyTo(d,206);U(208,384);
        for(int i=0;i<3;i++){U(256+i*32,(uint)i*4096);U(260+i*32,0x12345678);BitConverter.GetBytes(1f).CopyTo(d,272+i*32);}
        U(352,0xFFFFF000);U(528,0xFFFFF000);
        BitConverter.GetBytes((short)-100).CopyTo(d,520);
        var slots=new[]{
            new MagicSlot(0,184,0,32,3,128,0,256,32,d[256..288],"anchor","pppMove",null,null),
            new MagicSlot(1,200,0,16,1,384,0,512,16,d[512..528],"anchor","pppColMove",null,null)};
        var program=new MagicProgram(144,128,0,0,d[152..181],2,slots);
        var root=new MagicDllRoot(0,49,65537,1,0,0,0,32,36,40,44,new[]{128},Array.Empty<int>(),
            Array.Empty<MagicDescriptor>(),new[]{program},new[]{0});
        byte[] bytes=Enumerable.Repeat((byte)0xA5,1056).ToArray();d.CopyTo(bytes,32);
        return new MagicDllFile("magic_0866.dll",bytes,d,32,d.Length,0,Array.Empty<MagicPeSection>(),new[]{root});
    }
}
