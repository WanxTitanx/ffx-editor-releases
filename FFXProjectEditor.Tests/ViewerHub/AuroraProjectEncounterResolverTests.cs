using System;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class AuroraProjectEncounterResolverTests
{
    [Theory]
    [InlineData("nagi00_20")]
    [InlineData("nagi00_22")]
    [InlineData("nagi00_29")]
    public void RegionalFormationUsesItsAreasUniqueBattlefield(string battle)
    {
        var project = Table("nagi00", 0, (0x425, new[] { 0, 1, 6 }));
        var viewer = Table("nagi00", 0x150, (0x425, new[] { 0, 1, 6 }));
        Assert.True(AuroraProjectEncounterResolver.TryResolve(battle, project, viewer, _ => true, out var binding));
        Assert.Equal(0x425, binding.Battlefield);
        Assert.Equal(0x150, binding.EncounterId);
        Assert.Equal("nagi00_00", binding.PreviewSlotBattleId);
    }

    [Fact]
    public void ExplicitGroupWinsOverTheAreasOtherBattlefields()
    {
        var project = Table("nagi05", 0, (0x125, new[] { 0 }), (0x438, new[] { 70 }));
        var viewer = Table("nagi05", 0x180, (0x125, new[] { 0, 1 }), (0x438, new[] { 2 }));
        Assert.True(AuroraProjectEncounterResolver.TryResolve("nagi05_70", project, viewer, _ => true, out var binding));
        Assert.Equal(0x438, binding.Battlefield);
        Assert.Equal(0x182, binding.EncounterId);
    }

    [Fact]
    public void UnknownFormationWithMultiplePossibleStagesIsNotGuessed()
    {
        var table = Table("nagi05", 0, (0x125, new[] { 0 }), (0x438, new[] { 2 }));
        Assert.Null(AuroraProjectEncounterResolver.ResolveBattlefield(table, "nagi05_70"));
        Assert.False(AuroraProjectEncounterResolver.TryResolve("nagi05_70", table, table, _ => true, out _));
    }

    [Fact]
    public void MissingPreviewFilesAreSkippedAndNoOtherAreaIsSubstituted()
    {
        var project = Table("nagi00", 0, (0x425, new[] { 20 }));
        var viewer = Table("nagi00", 0x150, (0x425, new[] { 0, 6 }));
        Assert.True(AuroraProjectEncounterResolver.TryResolve("nagi00_20", project, viewer, id => id == 0x151, out var binding));
        Assert.Equal(0x151, binding.EncounterId);
        Assert.False(AuroraProjectEncounterResolver.TryResolve("nagi00_20", project, viewer, _ => false, out _));
        Assert.False(AuroraProjectEncounterResolver.TryResolve("nagi00_20", project,
            Table("nagi01", 0x150, (0x425, new[] { 0 })), _ => true, out _));
    }

    [Fact]
    public void ExistingFormationKeepsItsExactIdentityEvenWhenItsNumberIsNotItsOrdinal()
    {
        var table = Table("nagi00", 0x150, (0x425, new[] { 6, 0, 20 }));
        Assert.True(AuroraProjectEncounterResolver.TryResolve("nagi00_20", table, table, _ => true, out var binding));
        Assert.Equal(0x152, binding.EncounterId);
        Assert.Equal("nagi00_20", binding.PreviewSlotBattleId);
    }

    [Theory]
    [InlineData("nagi00_20/../file")]
    [InlineData("nagi00_other")]
    [InlineData("nagi0_20")]
    [InlineData("")]
    public void InvalidOrForeignIdsDoNotAcquirePreviewSlots(string battle)
    {
        var table = Table("nagi00", 0x150, (0x425, new[] { 0 }));
        Assert.False(AuroraProjectEncounterResolver.TryResolve(battle, table, table, _ => true, out _));
    }

    private static EncounterTable_File Table(string map, int firstId, params (int Field, int[] Formations)[] groups) =>
        new()
        {
            Tables = new[]
            {
                new EncounterTable_Entry
                {
                    Map = map, FormationOffset = firstId,
                    Groups = groups.Select((g, index) => new EncounterTable_Group
                    {
                        GroupIndex = index, Battlefield = g.Field,
                        Formations = g.Formations.Select(id => new EncounterTable_Formation
                        { FormationId = id, BattleId = $"{map}_{id:00}" }).ToList()
                    }).ToList()
                }
            }
        };
}
