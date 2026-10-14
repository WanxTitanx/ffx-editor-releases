using System;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicKeyframeBoundsTests
{
    [Theory]
    [InlineData(-1, 32, 1)]
    [InlineData(int.MaxValue, 32, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 3, 1)]
    [InlineData(0, 5, 1)]
    [InlineData(0, 32, 0)]
    [InlineData(0, 32, 4097)]
    [InlineData(0, 65532, 65535)]
    public void InvalidRanges_AreRejectedWithoutReadingOrMutatingData(int start, ushort stride, ushort count)
    {
        byte[] bytes = Enumerable.Repeat((byte)0xA5, 128).ToArray();
        byte[] original = (byte[])bytes.Clone();
        Assert.Empty(MagicKeyframe.Read(bytes, start, stride, count, out var error));
        Assert.NotNull(error);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(99)]
    [InlineData(100)]
    public void TruncatedOrMissingTerminator_DoesNotReturnAPartialTimeline(int length)
    {
        byte[] bytes = new byte[length];
        Assert.Empty(MagicKeyframe.Read(bytes, 0, 32, 3, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FractionalAndSpecialTicks_ArePreservedAsRawWords()
    {
        byte[] bytes = new byte[100];
        BitConverter.GetBytes(0x1800u).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x70000000u).CopyTo(bytes, 32);
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(bytes, 64);
        BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes, 96);
        var frames = MagicKeyframe.Read(bytes, 0, 32, 3, out var error);
        Assert.Null(error);
        Assert.Equal(3, frames.Count);
        Assert.Equal(1.5, frames[0].Frame);
        Assert.Null(frames[1].Frame);
        Assert.Null(frames[2].Frame);
        Assert.Equal(0x7FFFFFFFu, frames[2].Tick);
        Assert.Equal(bytes[..32], frames[0].Record);
    }

    [Fact]
    public void AliasedSequences_ShareTheCapturedRecordSnapshot()
    {
        byte[] bytes = new byte[100];
        BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes, 96);
        var first = MagicKeyframe.Read(bytes, 0, 32, 3, out _);
        var second = MagicKeyframe.Read(bytes, 0, 32, 3, out var error);
        Assert.Null(error);
        Assert.Same(first, second);
        bytes[16] = 1;
        Assert.Empty(MagicKeyframe.Read(bytes, 0, 32, 3, out error));
        Assert.Contains("snapshot", error);
    }

    [Fact]
    public void AggregateDecodeBudget_BoundsDistinctOverlappingSequences()
    {
        byte[] bytes = new byte[300];
        for (int start = 0; start <= 32; start += 4)
            BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes, start + 256);
        Assert.Equal(64, MagicKeyframe.Read(bytes, 0, 4, 64, out _).Count);
        Assert.Equal(64, MagicKeyframe.Read(bytes, 4, 4, 64, out _).Count);
        Assert.Empty(MagicKeyframe.Read(bytes, 8, 4, 64, out var error));
        Assert.Contains("budget", error);
    }
}
