using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class HookNativeFactAttribute : FactAttribute
{
    public HookNativeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")))
            Skip = "Requires the explicitly built canonical offline validator (FFX_HOOK_CONTRACT_VALIDATOR).";
    }
}

public sealed class HookElementalContractTests
{
    private static readonly HookProjectProfile Mod = new() { Mode = HookProjectMode.OnlyMod };
    internal static JsonObject WithExtras()
    {
        var root = JsonNode.Parse(ElementalPackDocument.Create().Serialize())!.AsObject();
        foreach (string key in new[] { "mod.aether", "mod.void" }) root["elements"]!.AsArray().Add(new JsonObject
            { ["key"] = key, ["label_key"] = "label." + key, ["label"] = key, ["rgb"] = 0x123456, ["native_bit"] = 0 });
        return root;
    }
    [Fact]
    public void RegistryAndPreferencesRemainKeyedWhenOrderChanges()
    {
        var root = WithExtras(); var registry = new ElementalPackDocument(Encoding.UTF8.GetBytes(root.ToJsonString())).Elements;
        var folderLocale=root.DeepClone().AsObject();folderLocale["banks"]=new JsonArray(new JsonObject{["locale"]="new_uspc"});
        Assert.Throws<InvalidDataException>(()=>new ElementalPackDocument(Encoding.UTF8.GetBytes(folderLocale.ToJsonString())));
        var scan = ScanElementPreferences.Parse("[element_scan]\nhook.mod.aether.rgb = 123\nhook.mod.void.enabled = 0\n", registry);
        var reversed = registry.Reverse().ToArray(); var roundTrip = ScanElementPreferences.Parse(scan.Serialize(), reversed);
        Assert.Equal((123, true), roundTrip.ForExternal("mod.aether", reversed));
        Assert.Equal((0x123456, false), roundTrip.ForExternal("mod.void", reversed));
        Assert.Null(roundTrip.ForExternal("mod.missing", reversed));
        Assert.DoesNotContain("elemental", roundTrip.Serialize());
        byte[] f7 = Encoding.UTF8.GetBytes("{\"elemWeak\":32,\"elemResist\":64,\"opaque\":77,\"diff_elemExtra\":[{\"key\":\"mod.void\",\"affinity\":0}]}" );
        var restored = JsonNode.Parse(F7ElementPreferencesCodec.ValidateAndRoundTrip(f7, reversed))!;
        Assert.Equal(77, restored["opaque"]!.GetValue<int>()); Assert.Equal(32, restored["elemWeak"]!.GetValue<int>());
        Assert.Equal(0, restored["diff_elemExtra"]![0]!["affinity"]!.GetValue<int>());
        Assert.Throws<InvalidDataException>(() => F7ElementPreferencesCodec.ValidateAndRoundTrip(Encoding.UTF8.GetBytes("{\"elemWeak\":256}"), registry));
        Assert.Throws<InvalidDataException>(() => F7ElementPreferencesCodec.ValidateAndRoundTrip(Encoding.UTF8.GetBytes("{\"elemWeak\":32,\"elemAbsorb\":32}"), registry));
    }
    [Fact]
    public void SignedWeightedAffinityDividesOnceAndWardDoesNotDeepenAbsorption()
    {
        Assert.Equal(-10000, ElementalPreviewService.Effective(-10000, 0, 0, 4));
        Assert.Equal(0, ElementalPreviewService.Effective(2500, 0, 0, 4));
        Assert.Equal(25000, ElementalPreviewService.Effective(25000, 35000, 4, 0));
        Assert.Contains("Effective: 25000", (new ElementalPreviewRequest { EquipmentBp=70000 }).Calculate());
        Assert.Equal(-1, ElementalPreviewService.Resolve(3, new[] { (-10000, 1), (0, 1) }, "split_weighted"));
        Assert.Equal(7, ElementalPreviewService.Resolve(3, new[] { (-10000, 1), (25000, 1) }, "highest_exposure"));
        Assert.Equal(0, ElementalPreviewService.Gravity(1600, 1, 16, true));
        Assert.Throws<InvalidDataException>(() => ElementalPreviewService.Gravity(1600, 100, 8, true));
        Assert.Equal(unchecked(int.MaxValue * 3) / 2, ElementalPreviewService.NativeExact(int.MaxValue, 1, 1, 0, 0, 0));
    }
    [Fact]
    public void FortyAbilityWriterPreservesVanillaAndUsesDefaultPlaceholderAfterRemap()
    {
        Assert.Equal("5774817E41823A51817085748188", Convert.ToHexString(HookAbilityRegistry.CanonicalName(HookAbilityRegistry.Get("hero_bravery"), false)));
        var (source, prices) = HookModAuthoringTests.Kernel(135);
        var original = (byte[])source.Clone();
        var result = HookAbilityBankAuthoring.Generate(source, prices, "new_uspc");
        Assert.Equal(original, source);
        Assert.Equal(original.AsSpan(20, 135 * 108).ToArray(), result.Abilities.AsSpan(20, 135 * 108).ToArray());
        HookAbilityRegistry.ValidateBank(result.Abilities, result.Prices, HookAbilityRegistry.Mapping(), "new_uspc");
        result.Abilities[20 + 158 * 108 + 0x55] ^= 1;
        Assert.Throws<InvalidDataException>(() => HookAbilityRegistry.ValidateBank(result.Abilities, result.Prices, HookAbilityRegistry.Mapping(), "new_uspc"));
        var mapping = new Dictionary<string,int> { ["aeon_break_hp_mp_limit"] = 175 };
        var asian = HookAbilityBankAuthoring.Generate(source, prices, "new_jppc", mapping);
        int pool = 20 + BinaryPrimitives.ReadUInt16LittleEndian(asian.Abilities.AsSpan(14));
        int text = pool + BinaryPrimitives.ReadUInt16LittleEndian(asian.Abilities.AsSpan(20 + 175 * 108));
        Assert.Equal("148", Encoding.ASCII.GetString(asian.Abilities, text, 3));
        Assert.Throws<InvalidDataException>(() => HookAbilityBankAuthoring.Generate(source, prices, "new_uspc", new Dictionary<string,int> { ["hero_bravery"] = 4095 }));
    }
    [Fact]
    public void AbilityExpansionPreservesExtraPriceRowsAndUnknownHeaderBytes()
    {
        var (abilities,_)=HookModAuthoringTests.Kernel(135);var (_,prices)=HookModAuthoringTests.Kernel(200);
        abilities[4]=0x5a;prices[4]=0xa5;
        var result=HookAbilityBankAuthoring.Generate(abilities,prices,"new_uspc");
        Assert.Equal(prices,result.Prices);Assert.Equal(0x5a,result.Abilities[4]);
    }
    [Fact]
    public void VanguardControlsAreIndependentAndOdNativeDiffersFromZero()
    {
        var preset = new VanguardPreset(); preset.Rules["quickcast_replace_doublecast"] = true;
        string ini = preset.ExportIni(Mod);
        Assert.Contains("quickcast_replace_doublecast = 1", ini); Assert.Contains("dualcast_white_magic = 0", ini);
        Assert.Contains("monster_rewards = 0", ini); Assert.Contains("single_hit_percent = 100", ini);
        Assert.Equal(0x3000u, VanguardPreset.EncodeCommand(0x3000, null));
        Assert.Equal(0u, VanguardPreset.EncodeCommand(0, null));
        Assert.Equal(0x13000u, VanguardPreset.EncodeCommand(0x3000, 0));
        Assert.Throws<InvalidDataException>(() => VanguardPreset.EncodeCommand(0x3000, 256));
        Assert.Throws<InvalidDataException>(() => (preset with { SingleHitPercent = 401 }).ExportIni(Mod));
    }
    [HookNativeFact]
    public async Task NativeAbilityConsumerValidatesGeneratedLatinAndNumericBanks()
    {
        var (a,p)=HookModAuthoringTests.Kernel(135);var validator=new HookOfflineValidator(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!);
        foreach(string locale in new[]{"new_uspc","new_jppc"})
        {
            var generated=HookAbilityBankAuthoring.Generate(a,p,locale);
            Assert.True((await HookAbilityBankAuthoring.AdmitAsync(generated,locale,HookAbilityRegistry.Mapping(),validator,CancellationToken.None)).Admitted);
            generated.Abilities[20+135*108+0x10]=1;
            await Assert.ThrowsAsync<InvalidDataException>(()=>HookAbilityBankAuthoring.AdmitAsync(generated,locale,HookAbilityRegistry.Mapping(),validator,CancellationToken.None));
        }
    }
    [HookNativeFact]
    public async Task CanonicalSchemaCoversActorTacticsGravityAndEquipmentRejections()
    {
        var root=WithExtras();root["requires"]=new JsonArray("mod007.registry.v1","mod007.affinity.v1","mod007.context.v1","mod007.tactics.v1","mod007.gravity.v1","mod007.equipment.v1");
        root["banks"]=new JsonArray(new JsonObject{["key"]="bank.commands",["kind"]="command",["locale"]="us",["bytes"]=116,["sha256"]=new string('a',64),["sections"]=new JsonArray(new JsonObject{["first"]=0,["last"]=0,["width"]=96,["offset"]=20})},
            new JsonObject{["key"]="bank.abilities",["kind"]="autoability",["locale"]="us",["bytes"]=128,["sha256"]=new string('b',64),["sections"]=new JsonArray(new JsonObject{["first"]=0,["last"]=0,["width"]=108,["offset"]=20})});
        root["commands"]=new JsonArray(new JsonObject{["key"]="spell.test",["bank"]="bank.commands",["index"]=0,["row_sha256"]=new string('c',64),["elements"]=new JsonArray(new JsonObject{["key"]="mod.void",["weight"]=1}),
            ["effects"]=new JsonArray(new JsonObject{["kind"]="imperil",["element"]="mod.void",["stacks"]=1,["turns"]=3,["chance_bp"]=10000})});
        root["profiles"]=new JsonArray(new JsonObject{["key"]="enemy.test",["kind"]="monster",["id"]=0x1156,["file_bytes"]=256,["file_sha256"]=new string('d',64),["affinities"]=new JsonArray(),
            ["gravity"]=new JsonObject{["maximum_hp_divisor"]=16,["nonlethal"]=true,["override_native_immunity"]=false}},
            new JsonObject{["key"]="ally.test",["kind"]="character",["id"]=7,["affinities"]=new JsonArray()},
            new JsonObject{["key"]="aeon.test",["kind"]="aeon",["id"]=8,["affinities"]=new JsonArray()});
        root["equipment"]=new JsonArray(new JsonObject{["key"]="gear.test",["bank"]="bank.abilities",["index"]=0,["row_sha256"]=new string('e',64),["kind"]="armor",["owners"]=new JsonArray(2),["deltas"]=new JsonArray(new JsonObject{["key"]="mod.void",["delta_bp"]=-2500})});
        var mutations=new Action<JsonObject>[] {
            r=>r["commands"]![0]!["elements"]![0]!["weight"]=0,
            r=>r["commands"]![0]!["effects"]![0]!["stacks"]=5,
            r=>r["commands"]![0]!["effects"]![0]!["turns"]=256,
            r=>r["commands"]![0]!["effects"]![0]!["chance_bp"]=10001,
            r=>r["commands"]![0]!["effects"]!.AsArray().Add(r["commands"]![0]!["effects"]![0]!.DeepClone()),
            r=>r["profiles"]![0]!["gravity"]!["maximum_hp_divisor"]=8,
            r=>r["profiles"]![0]!["gravity"]!["nonlethal"]=false,
            r=>r["profiles"]![1]!["id"]=8,
            r=>r["profiles"]![2]!["id"]=7,
            r=>r["profiles"]![1]!["file_sha256"]=new string('a',64),
            r=>r["equipment"]![0]!["owners"]=new JsonArray(18),
            r=>r["equipment"]![0]!["owners"]=new JsonArray(2,2),
            r=>r["equipment"]![0]!["deltas"]![0]!["key"]="native.fire",
            r=>r["equipment"]![0]!["deltas"]![0]!["delta_bp"]=1,
            r=>r["banks"]![0]!["sections"]![0]!["width"]=108,
            r=>r["banks"]![0]!["sections"]!.AsArray().Add(r["banks"]![0]!["sections"]![0]!.DeepClone()),
            r=>r["commands"]![0]!["index"]=1,
            r=>r["unknown"]=true
        };
        string path=Path.GetTempFileName();string validator=Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!;
        try
        {
            File.WriteAllText(path,root.ToJsonString());var good=await HookOfflineValidator.RunAsync(validator,new[]{"elemental-schema",path},CancellationToken.None);Assert.Equal(0,good.ExitCode);Assert.Contains("PARSED RT0",good.Output);
            foreach(var mutate in mutations)
            {
                var altered=root.DeepClone().AsObject();mutate(altered);File.WriteAllText(path,altered.ToJsonString());
                var result=await HookOfflineValidator.RunAsync(validator,new[]{"elemental-schema",path},CancellationToken.None);Assert.Equal(2,result.ExitCode);Assert.Contains("REJECTED",result.Output);
            }
        }
        finally{File.Delete(path);}
    }
    [HookNativeFact]
    public async Task CanonicalElementalConsumerAdmitsFinalBankAndRejectsHashOrUnavailableCapability()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "ffx-elemental-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            byte[] bank = new byte[116]; BinaryPrimitives.WriteUInt16LittleEndian(bank, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bank.AsSpan(12), 96); BinaryPrimitives.WriteUInt16LittleEndian(bank.AsSpan(14), 96); BinaryPrimitives.WriteInt32LittleEndian(bank.AsSpan(16), 20);
            string path = Path.Combine(temporary, "command.bin"); await File.WriteAllBytesAsync(path, bank);
            var root = WithExtras(); root["requires"] = new JsonArray("mod007.registry.v1", "mod007.context.v1", "mod007.affinity.v1");
            root["banks"] = new JsonArray(new JsonObject { ["key"]="kernel.commands",["kind"]="command",["locale"]="us",["bytes"]=116,["sha256"]=new string('0',64),
                ["sections"]=new JsonArray(new JsonObject { ["first"]=0,["last"]=0,["width"]=96,["offset"]=20 }) });
            root["commands"] = new JsonArray(new JsonObject { ["key"]="test.attack",["bank"]="kernel.commands",["index"]=0,["row_sha256"]=new string('0',64),
                ["elements"]=new JsonArray(new JsonObject { ["key"]="mod.aether",["weight"]=1 }),["policy"]="split_weighted",["augment"]=false,["gravity"]=false });
            var inputs = new[] { new ElementalInput("bank:kernel.commands",path,"us","data/mods/ffx_ps2/ffx/master/new_uspc/battle/kernel/command.bin") };
            var document = new ElementalPackDocument(Encoding.UTF8.GetBytes(root.ToJsonString())).RefreshFingerprints(inputs);
            var validator = new HookOfflineValidator(Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!);
            var admitted = await document.AdmitAsync(Mod,inputs,validator,CancellationToken.None);
            Assert.True(admitted.Native.Admitted); Assert.Contains("ADMITTED RT0",admitted.Native.Report);
            bank[30] ^= 1; await File.WriteAllBytesAsync(path, bank);
            await Assert.ThrowsAsync<InvalidDataException>(() => document.AdmitAsync(Mod,inputs,validator,CancellationToken.None));
            root["requires"]!.AsArray().Add("mod007.presentation.v1");
            await Assert.ThrowsAsync<InvalidDataException>(() => new ElementalPackDocument(Encoding.UTF8.GetBytes(root.ToJsonString())).AdmitAsync(Mod,inputs,validator,CancellationToken.None));
        }
        finally { Directory.Delete(temporary,true); }
    }
}
