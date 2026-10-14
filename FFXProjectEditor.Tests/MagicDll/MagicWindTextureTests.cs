using System;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicWindTextureTests
{
    [Fact]
    public void AccountsForEveryDxt5MipIncludingSubBlockLevels()
    {
        Assert.Equal(new[] {131072,32768,8192,2048,512,128,32,16,16,16},
            Ps3MagicWindTextureWriter.LevelSizes(256,512,174800));
        Assert.Throws<InvalidDataException>(()=>Ps3MagicWindTextureWriter.LevelSizes(256,512,174801));
        Assert.Throws<InvalidDataException>(()=>Ps3MagicWindTextureWriter.LevelSizes(256,512,174799));
        Assert.Throws<InvalidDataException>(()=>Ps3MagicWindTextureWriter.LevelSizes(257,512,174800));
        Assert.Throws<InvalidDataException>(()=>Ps3MagicWindTextureWriter.LevelSizes(16384,512,174800));
    }

    [Fact]
    public void ReplacesAndTintsAllLevelsWithoutTouchingHeaderOrOriginal()
    {
        string root=Path.Combine(Path.GetTempPath(),"wind-texture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"neutral.phyre"),target=Path.Combine(root,"wind.phyre");
            byte[] donor=Texture(4,4,new[]{16,16,16}), original=Texture(8,4,new[]{32,16,16,16});
            File.WriteAllBytes(source,donor);File.WriteAllBytes(target,original);
            Ps3MagicWindTextureWriter.Write(target,source,.62,1.12,.94,.75);
            byte[] changed=File.ReadAllBytes(target);
            Assert.Equal(donor,File.ReadAllBytes(source));
            Assert.Equal(original.Length,changed.Length);
            Assert.Equal(original.Take(181),changed.Take(181));
            for(int offset=181;offset<changed.Length;offset+=16)
            {
                Assert.Equal(150,changed[offset]);
                Assert.Equal(75,changed[offset+1]);
                Assert.True(changed[offset]>changed[offset+1]);
                Assert.NotEqual(original.Skip(offset+8).Take(4),changed.Skip(offset+8).Take(4));
            }
            byte[] valid=changed.ToArray();
            File.WriteAllBytes(target,changed.Concat(new byte[]{42}).ToArray());
            Assert.Throws<InvalidDataException>(()=>Ps3MagicWindTextureWriter.Write(target,null,1,1,1,1));
            Assert.Equal(valid.Concat(new byte[]{42}),File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root,true); }
    }

    private static byte[] Texture(int width,int height,int[] sizes)
    {
        byte[] bytes=new byte[181+sizes.Sum()];
        Encoding.ASCII.GetBytes("RYHPT").CopyTo(bytes,0);
        BitConverter.GetBytes(width).CopyTo(bytes,40);BitConverter.GetBytes(height).CopyTo(bytes,44);
        Encoding.ASCII.GetBytes("PTexture2D\0DXT5\0").CopyTo(bytes,128);
        for(int block=181;block<bytes.Length;block+=16)
        {
            bytes[block]=200;bytes[block+1]=100;
            bytes[block+8]=255;bytes[block+9]=255;
            bytes[block+10]=224;bytes[block+11]=255;
        }
        return bytes;
    }
}
