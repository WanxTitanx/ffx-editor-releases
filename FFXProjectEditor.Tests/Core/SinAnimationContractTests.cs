using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SinAnimationContractTests
{
    [Fact]
    public void DraggingStrikeKeepsDelayAndUsesTheCompleteOwnedDelayAttackVisualPair()
    {
        var rows = Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Battle", "monmagic2.bin")), false);
        while (rows.Count < 309) rows.Add(rows[211].CloneDeep(false));
        var result = MonsterMagicGrowWriter.AppendCalmCavernSinSkill(
            Ability_Command.WriteList(rows.Take(309).ToList(), false), 309);
        var strike = Ability_Command.ReadList(result.GrownBytes, false)[309];
        Assert.Equal(805, strike.Anim1Id);
        Assert.Equal(876, strike.Anim2Id);
        Assert.Equal(23, strike.CasterAnimId);
        Assert.Equal(36, strike.AttackPower);
        Assert.True(strike.FlagMisc2DelayS);
        Assert.True(strike.DamageFlgs.HasFlag(Ability_Command.DamageFlags.Physical));
    }

    [Fact]
    public void VisualCasterDoesNotLeakFromTheGameplayDonor()
    {
        var rows = Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Battle", "monmagic2.bin")), false);
        while (rows.Count < 308) rows.Add(rows[211].CloneDeep(false));
        rows = rows.Take(308).ToList();
        rows[301].CasterAnimId = 77;
        var result = MonsterMagicGrowWriter.AppendCalmCavernSinSkill(
            Ability_Command.WriteList(rows, false), 308);
        Assert.Equal(3, Ability_Command.ReadList(result.GrownBytes, false)[308].CasterAnimId);
    }

    [Fact]
    public void SilicaShardsDoesNotInheritUndocumentedStatusesFromItsDonor()
    {
        var rows = Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Battle", "monmagic2.bin")), false);
        while (rows.Count < 303) rows.Add(rows[211].CloneDeep(false));
        rows = rows.Take(303).ToList();
        rows[302].StatusChance = new StatusByteList { Slow = 100, Poison = 80 };
        rows[302].StatusDuration = new StatusDurationByteList { Slow = 5 };
        var result = MonsterMagicGrowWriter.AppendSilicaShards(
            Ability_Command.WriteList(rows, false), donorId: 302);
        var shards = Ability_Command.ReadList(result.GrownBytes, false)[303];
        Assert.Equal(0, shards.StatusChance.Slow);
        Assert.Equal(0, shards.StatusDuration.Slow);
        Assert.Equal(0, shards.StatusChance.Poison);
        Assert.Equal(80, shards.StatusChance.Silence);
        Assert.Equal(3, shards.StatusDuration.Silence);
        Assert.Equal(34, shards.AttackPower);
        Assert.True(shards.TargetFlgs.HasFlag(Ability_Command.TargetFlags.Multi));
    }

    [Theory]
    [InlineData(315, 44, 811, 1)]
    [InlineData(318, 60, 814, 10)]
    public void PhysicalSinImpactKeepsItsOwnVisualWithTheReviewedCaster(int row, int power, int visual, int caster)
    {
        var rows = Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Battle", "monmagic2.bin")), false);
        while (rows.Count < row) rows.Add(rows[211].CloneDeep(false));
        rows = rows.Take(row).ToList();
        // The original donor is a magic-cast row. The recipe must explicitly
        // choose physical motion instead of inheriting that visual contract.
        rows[303].CasterAnimId = 3;
        byte[] original = Ability_Command.WriteList(rows, false);
        byte[] snapshot = original.ToArray();
        var result = MonsterMagicGrowWriter.AppendCalmCavernSinSkill(original, row);
        var actual = Ability_Command.ReadList(result.GrownBytes, false);
        Assert.Equal(row + 1, actual.Count);
        var impact = actual[row];
        Assert.Equal(visual, impact.Anim1Id);
        Assert.Equal(visual, impact.Anim2Id);
        Assert.Equal(caster, impact.CasterAnimId);
        Assert.Equal(DamageFormula_Enum.Normal, impact.DamageFormula);
        Assert.Equal(power, impact.AttackPower);
        Assert.False(impact.FlagMisc2DelayS);
        Assert.Equal(Ability_Command.DamageTypeFlags.Hp, impact.DamageTypeFlgs);
        Assert.True(impact.DamageFlgs.HasFlag(Ability_Command.DamageFlags.Physical));
        Assert.False(impact.TargetFlgs.HasFlag(Ability_Command.TargetFlags.Multi));
        Assert.Equal(snapshot, original);
    }

    [Fact]
    public void ManaStormAuthoringRetainsNativeThunderPair()
    {
        var thunder = Assert.Single(MonsterMagicGrowWriter.LegacyMonsterVfxSkills,
            row => row.Row == 265);
        Assert.Equal(86, thunder.Anim1Id);
        Assert.Equal(87, thunder.Anim2Id);
        var rows = Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Battle", "monmagic2.bin")), false);
        while (rows.Count < 265) rows.Add(rows[211].CloneDeep(false));
        var result = MonsterMagicGrowWriter.AppendManaStormThunder(
            Ability_Command.WriteList(rows.Take(265).ToList(), false));
        var actual = Ability_Command.ReadList(result.GrownBytes, false)[265];
        Assert.Equal(86, actual.Anim1Id);
        Assert.Equal(87, actual.Anim2Id);
        Assert.Equal(3, actual.CasterAnimId);
        Assert.Equal(38, actual.AttackPower);
        Assert.Equal(Ability_Command.ElementFlags.Thunder, actual.ElementFlgs);
    }
}
