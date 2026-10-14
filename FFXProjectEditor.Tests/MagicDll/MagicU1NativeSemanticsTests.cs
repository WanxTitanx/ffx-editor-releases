using System;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicU1NativeSemanticsTests
{
    internal static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    internal static byte[] Ints(params int[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void ScaleImpulse_IsAppliedOnlyAtItsKeyframe()
    {
        var result = MagicU1Simulator.Run(new[] { new MagicU1SlotInput("pppScale", 0, Floats(2, 3, 4, 0)) }, 4);
        Assert.All(result.Original, frame =>
        {
            Assert.Equal(2, frame.ScaleX);
            Assert.Equal(3, frame.ScaleY);
            Assert.Equal(4, frame.ScaleZ);
        });
    }

    [Fact]
    public void PointImpulse_ContributesToPosition()
    {
        var result = MagicU1Simulator.Run(new[] { new MagicU1SlotInput("pppPoint", 0, Floats(7, 8, 9, 0)) }, 3);
        Assert.All(result.Original, frame => Assert.Equal(7, frame.PosX));
    }

    [Fact]
    public void ScaleVelocity_IntegratesEachAxisWithoutReapplyingTheImpulse()
    {
        var result = MagicU1Simulator.Run(new[] { new MagicU1SlotInput("pppSclMove", 0, Floats(1, 2, 3, 0)) }, 4);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i + 1, result.Original[i].ScaleX);
            Assert.Equal(2 * (i + 1), result.Original[i].ScaleY);
            Assert.Equal(3 * (i + 1), result.Original[i].ScaleZ);
        }
    }

    [Fact]
    public void AngularVelocity_IsSupportedAndAdvancesOncePerFrame()
    {
        var result = MagicU1Simulator.Run(new[] { new MagicU1SlotInput("pppAngMove", 0, Ints(0, 2, 0, 0)) }, 4, wrapAngles: false);
        Assert.Equal(1, result.SlotCount);
        Assert.Equal(8, result.Original[3].AngleYDeg);
    }

    [Fact]
    public void LaterKeyframes_ChangeVelocityOnlyWhenTheirTimestampMatches()
    {
        var slot = new MagicU1SlotInput("pppSclMove", new[]
        {
            new MagicU1Keyframe(0, 0x100, Floats(1, 0, 0, 0)),
            new MagicU1Keyframe(2 * 4096, 0x120, Floats(1, 0, 0, 0)),
        });
        var result = MagicU1Simulator.Run(new[] { slot }, 4, 0x120, 2);
        Assert.Equal(new[] { 1d, 2d, 4d, 6d }, result.Original.Select(f => f.ScaleX));
        Assert.Equal(new[] { 1d, 2d, 5d, 8d }, result.Mutated.Select(f => f.ScaleX));
    }

    [Fact]
    public void NativeStateAliases_ComposeAccelerationAndMovementInSlotOrder()
    {
        var acceleration = new MagicU1SlotInput("pppAccele",
            new[] { new MagicU1Keyframe(0, 0x100, Floats(1, 2, 3, 0)) }, 0, 16, 32);
        var movement = new MagicU1SlotInput("pppMove",
            new[] { new MagicU1Keyframe(0, 0x200, Floats(0, 0, 0, 0)) }, 0, 0, 16);
        var forward = MagicU1Simulator.Run(new[] { acceleration, movement }, 3);
        var reversed = MagicU1Simulator.Run(new[] { movement, acceleration }, 3);
        Assert.Equal(new[] { 1d, 3d, 6d }, forward.Original.Select(f => f.PosX));
        Assert.Equal(new[] { 0d, 1d, 3d }, reversed.Original.Select(f => f.PosX));
        Assert.Equal(12, forward.Original[2].PosY);
        Assert.Equal(18, forward.Original[2].PosZ);
    }

    [Fact]
    public void ProgramScopes_DoNotShareEqualRuntimeOffsets()
    {
        var acceleration = new MagicU1SlotInput("pppAccele",
            new[] { new MagicU1Keyframe(0, 0x100, Floats(1, 0, 0, 0)) }, 10, 16, 32);
        var movement = new MagicU1SlotInput("pppMove",
            new[] { new MagicU1Keyframe(0, 0x200, Floats(0, 0, 0, 0)) }, 20, 0, 16);
        Assert.All(MagicU1Simulator.Run(new[] { acceleration, movement }, 3).Original,
            f => Assert.Equal(0, f.PosX));
    }

    [Fact]
    public void Angles_UseNativeInt32OverflowAndNoImplicitDegreeWrapping()
    {
        var slot = new MagicU1SlotInput("pppAngle", new[]
        {
            new MagicU1Keyframe(0, 0x100, Ints(0, int.MaxValue, 0, 0)),
            new MagicU1Keyframe(4096, 0x120, Ints(0, 1, 0, 0)),
        });
        var result = MagicU1Simulator.Run(new[] { slot }, 2);
        Assert.Equal(int.MaxValue, result.Original[0].AngleYRaw);
        Assert.Equal(int.MinValue, result.Original[1].AngleYRaw);
    }

    [Fact]
    public void FrameBeforeFirstKeyframe_HasNoImpulse()
    {
        var slot = new MagicU1SlotInput("pppPoint", new[]
        {
            new MagicU1Keyframe(2 * 4096, 0, Floats(7, 8, 9, 0)),
        });
        Assert.Equal(new[] { 0d, 0d, 7d, 7d }, MagicU1Simulator.Run(new[] { slot }, 4).Original.Select(f => f.PosX));
    }
}
