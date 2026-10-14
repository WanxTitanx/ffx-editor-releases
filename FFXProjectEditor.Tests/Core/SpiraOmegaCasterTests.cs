using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SpiraOmegaCasterTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "OmegaCaster", name));

    [Theory]
    [InlineData("mental_break", 24)]
    [InlineData("armor_break", 24)]
    [InlineData("od_tonberry", 9)]
    [InlineData("od_ultima", 10)]
    public void SpecializedEffectsKeepTheirNativeCasterSequence(string key, byte expected)
    {
        var source = Fixture("monmagic2.bin");
        var characters = Fixture("command.bin");
        var monsterOne = Fixture("monmagic1.bin");
        var players = Ability_Command.ReadList(characters, true);
        var first = Ability_Command.ReadList(monsterOne, false);
        var second = Ability_Command.ReadList(source, false);
        var effects = MonsterMagicGrowWriter.OmegaSpells.Select(spec =>
        {
            var donor = (spec.Table == 0 ? players : spec.Table == 1 ? first : second)[spec.Donor];
            return (donor.Anim1Id, donor.Anim2Id);
        }).ToArray();
        var result = MonsterMagicGrowWriter.AppendOmega(source, characters, monsterOne, effects);
        var rows = Ability_Command.ReadList(result.Bytes, false);
        var command = rows[result.Commands[key] & 0xFFF];
        Assert.Equal(expected, command.CasterAnimId);
        Assert.Equal((byte)0, command.CostMp);
        Assert.Equal((byte)0, command.CostOverdrive);
        Assert.Equal(481, rows.Count);
        Assert.Equal(result.Bytes, Ability_Command.WriteList(rows, false));
    }

    [Theory]
    [InlineData("reflect")]
    [InlineData("regen")]
    [InlineData("hastega")]
    [InlineData("slowga")]
    [InlineData("bio")]
    [InlineData("focus")]
    [InlineData("cheer")]
    [InlineData("luck")]
    [InlineData("mental_break")]
    [InlineData("armor_break")]
    public void StatusAndWeaponEffectsKeepTheNativeDamageProtocol(string key)
    {
        var source = Fixture("monmagic2.bin");
        var characters = Fixture("command.bin");
        var monsterOne = Fixture("monmagic1.bin");
        var player = Ability_Command.ReadList(characters, true);
        var first = Ability_Command.ReadList(monsterOne, false);
        var second = Ability_Command.ReadList(source, false);
        var effects = MonsterMagicGrowWriter.OmegaSpells.Select(spec =>
        {
            var donor = (spec.Table == 0 ? player : spec.Table == 1 ? first : second)[spec.Donor];
            return (donor.Anim1Id, donor.Anim2Id);
        }).ToArray();
        var result = MonsterMagicGrowWriter.AppendOmega(source, characters, monsterOne, effects);
        var command = Ability_Command.ReadList(result.Bytes, false)[result.Commands[key] & 0xFFF];
        var spec = MonsterMagicGrowWriter.OmegaSpells.Single(s => s.Key == key);
        var original = player[spec.Donor];
        Assert.Equal(original.DamageFormula, command.DamageFormula);
        Assert.Equal(original.DamageTypeFlgs, command.DamageTypeFlgs);
        Assert.Equal(original.DamageFlgs, command.DamageFlgs);
        Assert.Equal(original.CasterAnimId, command.CasterAnimId);
        if (key is "armor_break" or "mental_break")
        {
            Assert.True(command.FlagDamagePhysical);
            Assert.False(command.FlagDamageMagical);
            Assert.False(command.FlagMisc1AffectedByReflect);
            Assert.Equal((byte)20, command.AttackPower);
        }
        else Assert.Equal((byte)0, command.AttackPower);
        if (key == "reflect")
        {
            Assert.Equal((byte)255, command.StatusChance.Reflect);
            Assert.Equal((byte)254, command.StatusDuration.Reflect);
        }
        Assert.Equal(result.Bytes, Ability_Command.WriteList(Ability_Command.ReadList(result.Bytes, false), false));
    }
}
