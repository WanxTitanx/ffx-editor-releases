using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class AuroraBattleSceneTests
{
    [Theory]
    [InlineData(0x18B, 395)] // Evrae: airship field, not arena 0x8B.
    [InlineData(0x209, 521)] // Yunalesca.
    [InlineData(0x236, 566)] // Sin.
    [InlineData(0x24A, 586)] // Braska's Final Aeon.
    [InlineData(0x24F, 591)] // Omega.
    [InlineData(0x12, 18)] // Sinspawn Ammes.
    public void BossKeepsTheFullFieldIndex(int battlefield, int expected)
    {
        var scene = Assert.IsType<AuroraBattleScene>(AuroraBattleScene.FromBattlefield(battlefield));
        Assert.False(scene.IsBattleMap);
        Assert.Equal(expected, scene.Index);
        Assert.Equal("13", scene.DataFolder);
        Assert.Equal($"map={expected}&mapKind=field", scene.Query);
    }

    [Fact]
    public void RegularEncounterRetainsItsSeparateBattleArena()
    {
        var scene = Assert.IsType<AuroraBattleScene>(AuroraBattleScene.FromBattlefield(0x425));
        Assert.True(scene.IsBattleMap);
        Assert.Equal(37, scene.Index);
        Assert.Equal("1a", scene.DataFolder);
        Assert.Equal("map=37&mapKind=battle", scene.Query);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x500)]
    [InlineData(0x800)]
    [InlineData(0x10000)]
    public void UnknownFlagsAreNotTruncatedToAnUnrelatedScene(int battlefield) =>
        Assert.Null(AuroraBattleScene.FromBattlefield(battlefield));
}
