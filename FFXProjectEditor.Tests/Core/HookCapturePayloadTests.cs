using System;
using System.IO;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class HookCapturePayloadTests
{
    [Fact]
    public void Capture_preserves_all_admitted_hook_identities()
    {
        foreach (var definition in HookAbilityRegistry.Abilities)
        {
            var original = Convert.FromHexString(definition.PayloadHex);
            Assert.True(HookAbilityRegistry.PayloadMatches(definition, original));
            var captured = original.AsSpan().ToArray();
            captured[0x66 - 16] |= 0x04;
            Assert.Equal(definition.IsDefined || definition.Definition == "NativeBaseOnly",
                HookAbilityRegistry.PayloadMatches(definition, captured));
        }
    }

    [Theory]
    [InlineData(0x65 - 16, 0x04)]
    [InlineData(0x66 - 16, 0x02)]
    [InlineData(0x66 - 16, 0x08)]
    [InlineData(0x67 - 16, 0x04)]
    [InlineData(0x11 - 16, 0x40)]
    public void Capture_does_not_admit_other_payload_changes(int offset, byte bit)
    {
        var definition = HookAbilityRegistry.Get("hero_bravery");
        var payload = Convert.FromHexString(definition.PayloadHex);
        payload[0x66 - 16] |= 0x04;
        payload[offset] ^= bit;
        Assert.False(HookAbilityRegistry.PayloadMatches(definition, payload));
    }

    [Fact]
    public void Wrong_payload_length_is_rejected()
    {
        var definition = HookAbilityRegistry.Get("hero_bravery");
        Assert.False(HookAbilityRegistry.PayloadMatches(definition, new byte[91]));
        Assert.False(HookAbilityRegistry.PayloadMatches(definition, new byte[93]));
    }

    [Fact]
    public void Regenerating_a_hook_bank_preserves_existing_capture_and_text()
    {
        var (source, prices) = HookModAuthoringTests.Kernel(135);
        var authored = HookAbilityBankAuthoring.Generate(source, prices, "new_uspc");
        var captured = authored.Abilities.AsSpan().ToArray();
        foreach (var definition in HookAbilityRegistry.Abilities)
            if (definition.IsDefined || definition.Definition == "NativeBaseOnly")
                captured[20 + definition.DefaultId * 108 + 0x66] |= 0x04;

        HookAbilityRegistry.ValidateBank(captured, authored.Prices, HookAbilityRegistry.Mapping(), "new_uspc");
        var regenerated = HookAbilityBankAuthoring.Generate(captured, authored.Prices, "new_uspc");
        Assert.Equal(captured, regenerated.Abilities);
        Assert.Equal(authored.Prices, regenerated.Prices);
        var table = HookAbilityRegistry.ReadBankView(regenerated.Abilities, regenerated.Prices);
        foreach (var definition in HookAbilityRegistry.Abilities)
            Assert.Equal(definition.Key, HookAbilityRegistry.Identify(table.Entries[definition.DefaultId])?.Key);
    }

    [Fact]
    public void Vanguard_text_authoring_preserves_capture_and_rejects_other_native_flags()
    {
        var (abilities, prices) = HookModAuthoringTests.Kernel();
        for (int id = 135; id <= 147; id++) abilities[20 + id * 108 + 0x66] = 0x04;
        var session = new Mod002Authoring(abilities, prices, Mod002Manifest.Default(), "new_uspc");
        var preview = session.Preview(session.Rows);
        for (int id = 135; id <= 147; id++) Assert.Equal(0x04, preview.Abilities[20 + id * 108 + 0x66]);
        abilities[20 + 135 * 108 + 0x66] |= 0x08;
        Assert.Throws<InvalidDataException>(() =>
            new Mod002Authoring(abilities, prices, Mod002Manifest.Default(), "new_uspc"));
    }
}
