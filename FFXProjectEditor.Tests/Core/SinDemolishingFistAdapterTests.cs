using System;
using System.IO;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SinDemolishingFistAdapterTests
{
    [Fact]
    public void RecreatingDemolishingFistRequiresItsParticleReuseRepair()
    {
        var error = Assert.Throws<ArgumentException>(() => MagicEffectClonePipeline.DeployClone(
            186, 814, "unused-extracted", "unused-stage", "unused-dll-root"));
        Assert.Equal("adaptSinDemolishingFist", error.ParamName);
    }

    [Theory]
    [InlineData(241, 814)]
    [InlineData(186, 815)]
    [InlineData(814, 186)]
    public void CloneGuardRejectsOtherDonorsAndTargetsBeforeAnyOutput(int donor, int target)
    {
        var error = Assert.Throws<ArgumentException>(() => MagicEffectClonePipeline.DeployClone(
            donor, target, "unused-extracted", "unused-stage", "unused-dll-root",
            adaptSinDemolishingFist: true));
        Assert.Equal("adaptSinDemolishingFist", error.ParamName);
    }

    [Theory]
    [InlineData(186)]
    [InlineData(815)]
    [InlineData(-1)]
    public void AdapterCannotModifyNativeOrUnreviewedEffects(int id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SinDemolishingFistVisualAdapter.Adapt(Array.Empty<byte>(), id));
    }

    [Fact]
    public void AdapterRejectsMissingAndNonPeImages()
    {
        Assert.Throws<ArgumentNullException>(() => SinDemolishingFistVisualAdapter.Adapt(null!, 814));
        Assert.Throws<InvalidDataException>(() => SinDemolishingFistVisualAdapter.Adapt(new byte[64], 814));
    }
}
