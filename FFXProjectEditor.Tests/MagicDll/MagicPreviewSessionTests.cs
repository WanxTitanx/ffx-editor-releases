using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicPreviewSessionTests
{
    [Theory]
    [InlineData("13312_19_0_0_128_128.dds.phyre", true)]
    [InlineData("13312_19_1_0_128_128.dds.phyre", false)]
    [InlineData("edited_texture.dds.phyre", false)]
    [InlineData("13312_19_0_0_0_128.dds.phyre", false)]
    public void TextureMatchingRequiresAnExplicitGsAddressAndDimensions(string name, bool valid)
        => Assert.Equal(valid,MagicPreviewSession.TryTextureKey(name,out _,out _,out _,out _));

    [Fact]
    public void HeaderResolutionRejectsMissingAmbiguousAndOutOfBoundsLinks()
    {
        byte[] data = new byte[512];
        Put(data,60,128); Put(data,160,96); data[208]=1; Put(data,224,192);
        Assert.Equal((0,0), Assert.Single(MagicPreviewSession.FindResources(data,new[]{320})));
        Assert.Empty(MagicPreviewSession.FindResources(data,new[]{321}));
        Put(data,124,64); // second header points to the same root: fail closed
        Assert.Empty(MagicPreviewSession.FindResources(data,new[]{320}));
        Put(data,124,uint.MaxValue); Put(data,160,uint.MaxValue);
        Assert.Empty(MagicPreviewSession.FindResources(data,new[]{320}));
    }

    [Fact]
    public void CustomIdPublishesActualWorkingDataAndUpdatesTheSameSessionWithoutASelectedDataRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "magic-preview-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] data=new byte[512];
            Put(data,60,128);Put(data,160,96);data[208]=1;Put(data,224,192);
            byte[] rdata=System.Text.Encoding.ASCII.GetBytes("pppScale\0pppColor\0");
            byte[] original=new byte[1024];rdata.CopyTo(original,0);data.CopyTo(original,512);
            var particleRoot = new MagicDllRoot(320,49,65537,1,0,0,0,48,64,80,96,
                Array.Empty<int>(),Array.Empty<int>(),Array.Empty<MagicDescriptor>(),Array.Empty<MagicProgram>(),new[]{0,1});
            var file = new MagicDllFile("magic_9999.dll",original,data,512,512,4096,
                new[]{new MagicPeSection(".rdata",rdata.Length,0,rdata.Length,0)},new[]{particleRoot});
            using(var session = new MagicPreviewSession(root))
            {
                session.Publish(file,original,"Custom spell");
                string manifest=Path.Combine(root,"magic-preview",session.Id,"current.json");
                using var first=JsonDocument.Parse(File.ReadAllBytes(manifest));
                string rev=first.RootElement.GetProperty("revision").GetString()!;
                Assert.Equal(9999,first.RootElement.GetProperty("magicId").GetInt32());
                byte[] working=(byte[])original.Clone(); working[512+400]=42;
                session.Publish(file,working,"Custom spell");
                Assert.NotEqual(rev,session.Revision);
                Assert.Equal(0,original[912]);
                byte[] served=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifest)!,session.Revision+".bin"));
                Assert.Equal(42,served[400]);Assert.Equal(512,served.Length);
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(manifest)!,rev+".bin")));
            }
            Assert.Empty(Directory.GetFiles(root,"*",SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root,true); }
    }
    private static void Put(byte[] bytes,int offset,uint value) => BitConverter.GetBytes(value).CopyTo(bytes,offset);
}
