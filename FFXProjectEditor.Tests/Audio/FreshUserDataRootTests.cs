using System;
using System.IO;
using FFXProjectEditor.Services.ReleaseRuntime;
using Xunit;

namespace FFXProjectEditor.Tests.Audio;

public sealed class FreshUserDataRootTests
{
    [Fact]
    public void MissingAbsoluteProfileKeepsAudioCatalogIsolatedAndEmpty()
    {
        string profile = Path.Combine(Path.GetTempPath(), "ffx-fresh-profile-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(profile));
        string root = GameMusicImportService.ResolveLocalApplicationRoot(profile);
        Assert.Equal(Path.Combine(Path.GetFullPath(profile), "FFXProjectEditor"), root);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Empty(ImportedGameMusicCatalog.Load(Path.Combine(root, "Audio")));
        Assert.False(Directory.Exists(profile));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-profile")]
    public void MissingOrRelativeBasesNeverFallBackToTheWorkingDirectory(string profile)
    {
        Assert.Throws<ArgumentException>(() => GameMusicImportService.ResolveLocalApplicationRoot(profile));
    }
}
