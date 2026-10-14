using System;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SinMonsterThrowAdapterTests
{
    [Theory]
    [InlineData(790)] [InlineData(791)] [InlineData(802)] [InlineData(803)]
    [InlineData(807)] [InlineData(815)] [InlineData(829)] [InlineData(837)]
    [InlineData(845)] [InlineData(852)] [InlineData(864)]
    public void RecreatingKnownMonsterEffectCannotSilentlyRestorePlayerOnlyThrow(int target)
    {
        // Admission happens before any source read or destination creation.
        var error = Assert.Throws<ArgumentException>(() => MagicEffectClonePipeline.DeployClone(
            241, target, "unused-extracted", "unused-stage", "unused-dll-root"));
        Assert.Equal("adaptSinMonsterThrow", error.ParamName);
    }

    [Fact]
    public void AdapterCannotBeAppliedToNativeDonorOrUnreviewedFamily()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SinMonsterThrowVisualAdapter.Adapt(Array.Empty<byte>(), 241));
        var error = Assert.Throws<ArgumentException>(() => MagicEffectClonePipeline.DeployClone(
            158, 802, "unused-extracted", "unused-stage", "unused-dll-root", adaptSinMonsterThrow: true));
        Assert.Equal("sourceMagicId", error.ParamName);
    }
}
