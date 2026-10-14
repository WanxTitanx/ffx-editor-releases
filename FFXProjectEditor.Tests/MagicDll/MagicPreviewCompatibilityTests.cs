using System;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicPreviewCompatibilityTests
{
    [Fact]
    public void TornadoResourceDimensionsAloneDoNotAdmitUnknownCode()
    {
        var bytes = new byte[27648];
        var file = Layout(bytes);

        Assert.False(MagicPreviewCompatibility.SkipExtraFlipbooks(file, bytes));
        bytes[100] = 42;
        Assert.False(MagicPreviewCompatibility.SkipExtraFlipbooks(file, bytes));
    }

    [Fact]
    public void CodeWindowOutsideTheCapturedBytesIsRejected()
    {
        var file = Layout(new byte[27648]);

        Assert.False(MagicPreviewCompatibility.SkipExtraFlipbooks(file, new byte[27647]));
        Assert.False(MagicPreviewCompatibility.SkipExtraFlipbooks(file, ReadOnlySpan<byte>.Empty));
    }

    private static MagicDllFile Layout(byte[] bytes)
    {
        var root = new MagicDllRoot(0x1D1F0, 49, 65537, 1, 0, 0, 0, 48, 64, 80, 96,
            Array.Empty<int>(), Array.Empty<int>(), Array.Empty<MagicDescriptor>(),
            Array.Empty<MagicProgram>(), new[] { 0, 1 });
        return new MagicDllFile("magic_0866.dll", bytes, Array.Empty<byte>(), 0, 997376, 0x9000,
            new[] { new MagicPeSection(".text", 27648, 0x1000, 27648, 0) }, new[] { root });
    }
}
