using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public class HookArcanaTests
{
    private static void Seal(byte[] bytes)
    {
        uint crc=0xffffffff;
        for(int i=0;i<272;i++){crc^=bytes[i];for(int b=0;b<8;b++)crc=(crc>>1)^(0xedb88320u&(uint)-(int)(crc&1));}
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272),~crc);
    }
    [HookNativeFact]
    public async Task ArcanaInspectionRequiresMatchingSavePackCrcAndValidOwnership()
    {
        byte[] bytes=new byte[276];Encoding.ASCII.GetBytes("ARCANA01").CopyTo(bytes,0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),1);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12),276);
        string save=ModAuthoringFiles.Sha256(Encoding.ASCII.GetBytes("synthetic save")),pack=ModAuthoringFiles.Sha256(Encoding.ASCII.GetBytes("synthetic compiled pack"));
        Convert.FromHexString(save).CopyTo(bytes,16);Convert.FromHexString(pack).CopyTo(bytes,48);BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80),3);
        for(int i=0;i<21;i++)BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(174+i*2),ushort.MaxValue);Seal(bytes);
        string path=Path.GetTempFileName();var validator=new HookOfflineValidator(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!);
        try
        {
            File.WriteAllBytes(path,bytes);string result=await ArcanaRecordInspector.InspectAsync(path,save,pack,validator,CancellationToken.None);
            Assert.Contains("revision 3",result);Assert.Contains("ADMITTED RT0",result);Assert.Equal(bytes,File.ReadAllBytes(path));
            await Assert.ThrowsAsync<InvalidDataException>(()=>ArcanaRecordInspector.InspectAsync(path,new string('a',64),pack,validator,CancellationToken.None));
            bytes[96]=2;Seal(bytes);File.WriteAllBytes(path,bytes);
            await Assert.ThrowsAsync<InvalidDataException>(()=>ArcanaRecordInspector.InspectAsync(path,save,pack,validator,CancellationToken.None));
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void SelectedArtNeedsCompiledBasenameProvenanceDimensionsAndHash()
    {
        string root=Path.Combine(Path.GetTempPath(),"ffx-arcana-art-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,ArcanaAssetAuthoring.Catalog[0].Asset);
            byte[] bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");File.WriteAllBytes(path,bytes);
            var asset=new ArcanaSelectedAsset(path,ArcanaAssetAuthoring.Catalog[0].Key,"Synthetic unit-test fixture","Unit-test fixture",ModAuthoringFiles.Sha256(bytes),1,1);
            var profile=new HookProjectProfile{Mode=HookProjectMode.OnlyMod};
            var result=ArcanaAssetAuthoring.Prepare(JsonSerializer.SerializeToUtf8Bytes(new[]{asset}),profile);Assert.Equal(2,result.Artifacts.Length);Assert.Single(result.Sources);
            Assert.Throws<InvalidDataException>(()=>ArcanaAssetAuthoring.Prepare(JsonSerializer.SerializeToUtf8Bytes(new[]{asset with {License=""}}),profile));
            Assert.Throws<InvalidDataException>(()=>ArcanaAssetAuthoring.Prepare(JsonSerializer.SerializeToUtf8Bytes(new[]{asset with {Width=2}}),profile));
            Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally{Directory.Delete(root,true);}
    }
}
