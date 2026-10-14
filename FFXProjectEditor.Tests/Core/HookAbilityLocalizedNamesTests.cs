using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class HookAbilityLocalizedNamesTests
{
    private static HookAbilityBankResult Localized(string locale)
    {
        var (source, prices) = HookModAuthoringTests.Kernel(135);
        var bank = HookAbilityBankAuthoring.Generate(source, prices, locale);
        var table = NameDescriptionTextPrefixTable_File.Read(bank.Abilities,
            Customization_File.ReadHeader(bank.Abilities), FfxEncoding.UsDecoder, "test");
        var names = HookAbilityRegistry.Abilities.ToDictionary(
            a => (a.DefaultId, 0), a => HookAbilityLocalizedNames.Name(a.DefaultId, locale));
        return bank with { Abilities = table.WritePreservingPool(FfxEncoding.UsDecoder, names) };
    }

    [Theory]
    [InlineData("new_uspc")]
    [InlineData("inpc")]
    [InlineData("new_depc")]
    [InlineData("new_frpc")]
    [InlineData("new_itpc")]
    [InlineData("new_sppc")]
    [InlineData("jppc")]
    [InlineData("new_jppc")]
    [InlineData("new_chpc")]
    [InlineData("new_krpc")]
    public void AllRegionalNames_KeepIdentityAndPayloadGuards(string locale)
    {
        var bank = Localized(locale);
        HookAbilityRegistry.ValidateBank(bank.Abilities, bank.Prices, HookAbilityRegistry.Mapping(), locale);
        var reauthored = HookAbilityBankAuthoring.Generate(bank.Abilities, bank.Prices, locale);
        Assert.Equal(bank.Abilities, reauthored.Abilities);
        Assert.Equal(bank.Prices, reauthored.Prices);
        var table = HookAbilityRegistry.ReadBankView(bank.Abilities, bank.Prices);
        foreach (var definition in HookAbilityRegistry.Abilities)
            Assert.Equal(definition.Key, HookAbilityRegistry.Identify(table.Entries[definition.DefaultId])?.Key);
        var invalid = (byte[])bank.Abilities.Clone();
        invalid[20 + 150 * 108 + 0x55] ^= 1;
        Assert.Throws<InvalidDataException>(() => HookAbilityRegistry.ValidateBank(
            invalid, bank.Prices, HookAbilityRegistry.Mapping(), locale));
    }

    [Fact]
    public void TranslatedName_MustBelongToTheSameIdentityAndLocale()
    {
        var bytes = HookAbilityLocalizedNames.Name(150, "new_krpc");
        Assert.True(HookAbilityLocalizedNames.Matches(150, "new_krpc", bytes));
        Assert.False(HookAbilityLocalizedNames.Matches(151, "new_krpc", bytes));
        Assert.False(HookAbilityLocalizedNames.Matches(150, "new_frpc", bytes));
        Assert.False(HookAbilityLocalizedNames.Matches(150, "new_krpc", bytes[..^1]));
        Assert.False(HookAbilityLocalizedNames.Matches(150, "new_krpc", bytes.Concat(new byte[] { 0 }).ToArray()));
    }
}
