using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public sealed class HookRewardProfileTests
{
    [Fact]
    public void VanillaReopensWithoutWritingAnythingAndBlocksHookExports()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var p = HookProjectProfile.Load(root, root + "-metadata");
        Assert.Equal(HookProjectMode.Vanilla, p.Mode);
        p.Save(root, root + "-metadata", null);
        Assert.False(Directory.Exists(root)); Assert.False(Directory.Exists(root + "-metadata"));
        Assert.Throws<InvalidDataException>(() => p.Require("monster-rewards.v1"));
    }
    [Fact]
    public void All4096RowsRoundTripWithoutTouchingNativeWords()
    {
        var p = new MonsterRewardProfile();
        for (int i = 0; i < 4096; i++) p[i] = new(1000, 1000);
        byte[] bytes = MonsterRewardSettingsCodec.Serialize(p);
        Assert.True(bytes.Length <= 65535);
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            var reloaded = MonsterRewardSettingsCodec.Parse(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace("\n", newline)));
            for (int i = 0; i < 4096; i++) Assert.Equal(p[i], reloaded[i]);
        }
    }
    [Theory]
    [InlineData("1\t2\t3")][InlineData("1\t2\t3\n1\t4\t5\n")][InlineData("\n")]
    [InlineData("4096\t2\t3\n")][InlineData("1\t0\t3\n")][InlineData("1\t1001\t3\n")]
    [InlineData("1\t2\t3\t4\n")][InlineData("+1\t2\t3\n")][InlineData(" 1\t2\t3\n")]
    [InlineData("1\t2.0\t3\n")][InlineData("1\t2\t3\r\r\n")]
    public void MalformedTsvRejectsWholeTable(string row) => Assert.Throws<InvalidDataException>(() =>
        MonsterRewardSettingsCodec.Parse(Encoding.ASCII.GetBytes(MonsterRewardSettingsCodec.Header + "\n" + row)));
    [Fact]
    public void PreviewPreservesIndependentRatesBasesAndGlobalGates()
    {
        var p = new MonsterRewardProfile(); p[342] = new(2, 7);
        var source = new MonsterRewardSource(342, "m342", 100, 65000, 65535, MonsterRewardOrigin.Observation, null, null, DateTimeOffset.UtcNow);
        var view = p.Preview(source, 3, 5, true, false);
        Assert.Equal(130000ul, view.Ap!.Individual); Assert.Equal(390000ul, view.Ap.Combined);
        Assert.Equal(393210ul, view.OverkillAp!.Combined); Assert.Equal(700ul, view.Gil!.Combined);
        Assert.Equal(MonsterRewardRates.Neutral, p[343]);
        p.ResetAp(342); Assert.Equal(new MonsterRewardRates(1, 7), p[342]);
        Assert.Null(p.Preview(MonsterRewardSourceResolver.Missing(343, "unknown"), 1, 1, false, false).Ap);
        var capped = MonsterRewardProfile.Calculate(65535, 1000, 100, false);
        Assert.Equal(6553500000ul, capped.Combined); Assert.True(capped.Clamped); Assert.Equal(382494549u, capped.Applied);
        Assert.Throws<InvalidDataException>(() => MonsterRewardProfile.FromRawIdentity(7));
    }
    [Fact]
    public void ExternalWriteSurvivesStaleSave()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "old"); string hash = HookExportTransaction.CurrentHash(path)!;
            File.WriteAllText(path, "other writer");
            Assert.Throws<IOException>(() => HookExportTransaction.ReplaceFile(path, Encoding.ASCII.GetBytes("mine"), hash));
            Assert.Equal("other writer", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ConsumerProfileRejectsIncompatibleExecutableAndPresentationCapability()
    {
        Assert.Throws<InvalidDataException>(()=>(new HookCompatibilityProfile {ExeSha256=new string('a',64)}).Require("monster-rewards.v1"));
        Assert.Throws<InvalidDataException>(()=>(new HookCompatibilityProfile()).Require("mod007.presentation.v1"));
    }
    [HookNativeFact]
    public async Task NativeConsumerAcceptsAll4096SerializedRowsAndRejectsTruncation()
    {
        var profile=new MonsterRewardProfile();for(int i=0;i<4096;i++)profile[i]=new(1000,999);
        string path=Path.GetTempFileName();var validator=new HookOfflineValidator(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!);
        try
        {
            byte[] bytes=MonsterRewardSettingsCodec.Serialize(profile);File.WriteAllBytes(path,bytes);
            Assert.True((await validator.ValidateAsync("rewards",path,Array.Empty<string>(),CancellationToken.None)).Admitted);
            File.WriteAllBytes(path,bytes[..^1]);Assert.False((await validator.ValidateAsync("rewards",path,Array.Empty<string>(),CancellationToken.None)).Admitted);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void CatalogHasFortyIdentitiesAndProtectsSharedReservations()
    {
        Assert.Equal(40, HookAbilityRegistry.Abilities.Count); Assert.Equal(31, HookAbilityRegistry.Features.Count);
        Assert.Throws<InvalidDataException>(() => HookAbilityRegistry.Mapping(new System.Collections.Generic.Dictionary<string,int> { ["hero_bravery"] = 148 }));
        Assert.Throws<InvalidDataException>(() => HookAbilityRegistry.Mapping(new System.Collections.Generic.Dictionary<string,int> { ["hero_bravery"] = 175, ["spira_mana_spring"] = 175 }));
        Assert.False(HookAbilityRegistry.Get("spira_wardens_oath").Allows(1, 1));
        Assert.True(HookAbilityRegistry.Get("spira_wardens_oath").Allows(2, 1));
        Assert.False(HookAbilityRegistry.Get("spira_foolstrike").IsDefined);
    }
}
