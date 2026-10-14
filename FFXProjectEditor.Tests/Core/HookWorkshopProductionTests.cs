using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.FfxLib.Save;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public class HookWorkshopProductionTests
{
    private static byte[] State()
    {
        byte[] state=new byte[16260];BinaryPrimitives.WriteUInt32LittleEndian(state,1);BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(12),3);
        for(int i=0;i<200;i++)BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(36+i*80+78),255);
        const int p=36;state[p+2]=1;state[p+4]=8;state[p+5]=1;state[p+6]=255;state[p+11]=1;state[p+30]=2;
        BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(p+14),0x8094);BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(p+22),1);BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(p+38),2);
        return state;
    }
    [Fact]
    public void AscensionRecipesAreFinalAndPolicyWireHasExactV5Sizes()
    {
        var recipes=WorkshopProductionInspector.AscensionRecipes;
        Assert.Equal(10000000,recipes[0].Gil);Assert.Equal(15000000,recipes[1].Gil);
        Assert.Equal(60,recipes[0].Items[108]);Assert.Equal(99,recipes[1].Items[53]);
        var encoded=new WorkshopPlanAuthoring { PieceId=1,Other=1 }.Encode(); Assert.Equal(62,encoded.Request.Length);Assert.Equal(188,encoded.Economy.Length);
        Assert.Equal(10000u,WorkshopPlanAuthoring.RefinementGil(10));Assert.Equal(11500u,WorkshopPlanAuthoring.RefinementGil(11));Assert.Equal(100000u,WorkshopPlanAuthoring.RefinementGil(50));
        Assert.Throws<InvalidDataException>(()=>(new WorkshopPolicy { Mode=3 }).Validate());
        Assert.Throws<InvalidDataException>(()=>(new WorkshopPolicy { ModRecipeQuantity=256 }).Validate());
    }
    [HookNativeFact]
    public async Task NativeStateAndPaidLedgerRejectForeignIdentityWithoutAnyWriter()
    {
        var validator=new HookOfflineValidator(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!);byte[] state=State(), before=(byte[])state.Clone();
        Assert.True((await WorkshopProductionInspector.AdmitStateAsync(state,validator,CancellationToken.None)).Admitted);
        byte[] saveId=Enumerable.Repeat((byte)17,32).ToArray(),ledger=new byte[680];
        BinaryPrimitives.WriteUInt32LittleEndian(ledger,1);BinaryPrimitives.WriteUInt32LittleEndian(ledger.AsSpan(4),1);saveId.CopyTo(ledger,8);
        BinaryPrimitives.WriteUInt64LittleEndian(ledger.AsSpan(40),1);BinaryPrimitives.WriteUInt64LittleEndian(ledger.AsSpan(48),2);
        BinaryPrimitives.WriteUInt32LittleEndian(ledger.AsSpan(56),8);BinaryPrimitives.WriteUInt32LittleEndian(ledger.AsSpan(64),1);BinaryPrimitives.WriteUInt16LittleEndian(ledger.AsSpan(68),0x8094);
        Assert.True((await WorkshopProductionInspector.AdmitStateAsync(state,validator,CancellationToken.None,ledger,saveId)).Admitted);
        saveId[0]^=1;
        await Assert.ThrowsAsync<InvalidDataException>(()=>WorkshopProductionInspector.AdmitStateAsync(state,validator,CancellationToken.None,ledger,saveId));
        Assert.Equal(before,state);
        BinaryPrimitives.WriteUInt64LittleEndian(ledger.AsSpan(48),77);saveId[0]^=1;
        await Assert.ThrowsAsync<InvalidDataException>(()=>WorkshopProductionInspector.AdmitStateAsync(state,validator,CancellationToken.None,ledger,saveId));
        byte[] request=new WorkshopPlanAuthoring { PieceId=1,Revision=999,Other=1 }.Encode().Request;
        byte[] economy=new WorkshopPlanAuthoring().Encode().Economy;
        await Assert.ThrowsAsync<InvalidDataException>(()=>WorkshopProductionInspector.AdmitStateAsync(state,validator,CancellationToken.None,request:request,economy:economy));
    }
    [Fact]
    public void CheckpointChecksumAnchorAndPendingStateAreVerifiedWithoutRecovery()
    {
        string path=Path.GetTempFileName();
        try
        {
            byte[] state=State(),native=new byte[0x6900];for(int i=0;i<200;i++)state.AsSpan(36+i*80,22).CopyTo(native.AsSpan(0x44dc+i*22));
            FfxSaveChecksum.ApplyToBuffer(native);string owner=@"C:\fixture\ffx_001";
            byte[] record=new byte[72+32+0x6900+4+16260];Encoding.ASCII.GetBytes("FFXWKCP1").CopyTo(record,0);
            Convert.FromHexString(ModAuthoringFiles.Sha256(Encoding.Unicode.GetBytes(owner.ToLowerInvariant()))).CopyTo(record,8);
            Convert.FromHexString(ModAuthoringFiles.Sha256(native)).CopyTo(record,72);native.CopyTo(record,104);state.CopyTo(record,104+0x6900+4);
            Convert.FromHexString(ModAuthoringFiles.Sha256(record.AsSpan(72).ToArray())).CopyTo(record,40);File.WriteAllBytes(path,record);
            Assert.True(WorkshopProductionInspector.Read(path,native,owner).Pending);
            record[104+25844]^=1;Convert.FromHexString(ModAuthoringFiles.Sha256(record.AsSpan(72).ToArray())).CopyTo(record,40);File.WriteAllBytes(path,record);
            Assert.Throws<InvalidDataException>(()=>WorkshopProductionInspector.Read(path,native,owner));Assert.Equal(record,File.ReadAllBytes(path));
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void ProductionRecordChecksEveryNativeGearByteAndHashes()
    {
        string path=Path.GetTempFileName();
        try
        {
            byte[] state=State(),native=new byte[0x6900],record=new byte[16364];
            for(int i=0;i<200;i++)state.AsSpan(36+i*80,22).CopyTo(native.AsSpan(0x44dc+i*22));
            string owner=@"C:\fixture\ffx_001";
            Encoding.ASCII.GetBytes("FFXWKS01").CopyTo(record,0);
            Convert.FromHexString(ModAuthoringFiles.Sha256(Encoding.Unicode.GetBytes(owner.ToLowerInvariant()))).CopyTo(record,8);
            Convert.FromHexString(ModAuthoringFiles.Sha256(native)).CopyTo(record,40);Convert.FromHexString(ModAuthoringFiles.Sha256(state)).CopyTo(record,72);state.CopyTo(record,104);
            File.WriteAllBytes(path,record);var inspected=WorkshopProductionInspector.Read(path,native,owner);Assert.Equal(state,inspected.State);Assert.False(inspected.Pending);
            native[0x44dc+22]^=1;Assert.Throws<InvalidDataException>(()=>WorkshopProductionInspector.Read(path,native,owner));
            Assert.Equal(record,File.ReadAllBytes(path));
        }
        finally {File.Delete(path);}
    }
}
