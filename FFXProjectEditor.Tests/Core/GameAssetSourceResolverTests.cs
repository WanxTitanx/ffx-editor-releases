using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.Archives;
using FFXProjectEditor.FfxLib.ThunderPlains;
using FFXProjectEditor.Modules.ThunderPlainsEditor;
using FFXProjectEditor.Services;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class GameAssetSourceResolverTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "asset-source-tests-" + Guid.NewGuid().ToString("N"));
    const string North = "jppc/event/obj/ka/kami0000/kami0000.ebp";
    const string South = "jppc/event/obj/ka/kami0300/kami0300.ebp";
    string Workspace => Path.Combine(root, "workspace", "master");
    string Extracted => Path.Combine(root, "extracted", "ffx_ps2");
    string Game => Path.Combine(root, "game");
    public GameAssetSourceResolverTests() => Directory.CreateDirectory(Workspace);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void WindowsStylePathsFindExtractedFilesOnEveryPlatform()
    {
        string file = Put(Path.Combine(Workspace, North), new byte[] { 1 });
        Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(ThunderPlainsLightning_File.ResolvePath(North.Replace('/', '\\'), Workspace, null)!));
        var source = GameAssetSourceResolver.ResolveMasterFile(North.Replace('/', '\\'), Workspace, null, Game);
        Assert.False(source!.IsArchive); Assert.Equal(new byte[] { 1 }, source.ReadAllBytes());
    }

    [Fact]
    public void ResolutionPrefersWorkspaceThenExtractionThenVbfWithoutExtractingOnRead()
    {
        MakeArchive();
        string loose = Put(Path.Combine(Workspace, North), new byte[] { 3 });
        string extracted = Put(Path.Combine(Extracted, "ffx", "master", North), new byte[] { 2 });
        Assert.Equal(new byte[] { 3 }, Resolve(North)!.ReadAllBytes());
        File.Delete(loose); Assert.Equal(new byte[] { 2 }, Resolve(North)!.ReadAllBytes());
        File.Delete(extracted);
        var archive = Resolve(North)!;
        Assert.True(archive.IsArchive); Assert.Null(archive.LoosePath);
        Assert.Equal(79872, archive.ReadAllBytes().Length);
        Assert.False(File.Exists(loose)); Assert.False(File.Exists(extracted));
        Assert.Contains("FFX_Data.vbf!/ffx_ps2/ffx/master/", archive.Description);
    }

    [Fact]
    public void MissingAndUnsafePathsDoNotResolveOrCreateFiles()
    {
        Assert.Null(Resolve(North));
        Assert.Throws<ArgumentException>(() => Resolve("../outside"));
        MakeArchive(); Assert.Null(Resolve("jppc/not-present.bin"));
        Assert.Empty(Directory.GetFiles(Workspace, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ArchiveIndexIsRefreshedWhenAnInstallationChanges()
    {
        MakeArchive(); Assert.NotNull(Resolve(North));
        string archive = Path.Combine(Game, "data", "FFX_Data.vbf");
        VbfArchiveWriter.Save(Array.Empty<VbfContent>(), archive);
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddSeconds(2));
        Assert.Null(Resolve(North));
    }

    [Fact]
    public void ThunderPlainsLoadsElevenValidatedRowsDirectlyFromVbfAndSavesOnlyLooseCopies()
    {
        MakeArchive();
        string vbf = Path.Combine(Game, "data", "FFX_Data.vbf");
        byte[] hash = SHA256.HashData(File.ReadAllBytes(vbf));
        var model = new ThunderPlainsEditor_DataModel(() => (Workspace, Extracted, Game));
        Assert.Equal(11, model.Thresholds.Count);
        Assert.All(model.Thresholds, row => { Assert.True(row.PatternValidKami0000); Assert.True(row.PatternValidKami0300); });
        Assert.Contains(".vbf!/", model.FilePathSummary);
        Assert.Empty(Directory.GetFiles(Workspace, "*", SearchOption.AllDirectories));
        model.ApplyPreset("half");
        Assert.NotNull(model.ComputeSaveDiff());
        model.Save();
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(vbf)));
        Assert.True(File.Exists(Path.Combine(Workspace, North)));
        Assert.True(File.Exists(Path.Combine(Workspace, South)));
        Assert.True(File.Exists(Path.Combine(Workspace, North) + ".bak"));
        Assert.Equal(5, File.ReadAllBytes(Path.Combine(Workspace, North) + ".bak")[0x5961]);
        model.RefreshFromDisk();
        Assert.Equal(2, model.Thresholds.Single(x => x.ThresholdId == "consec_5").CurrentValue);
        Assert.DoesNotContain(".vbf!/", model.FilePathSummary);
        Assert.All(model.Thresholds, row => { Assert.True(row.PatternValidKami0000); Assert.True(row.PatternValidKami0300); });
    }

    [Fact]
    public void PairSaveStagesBothAndRejectsConcurrentChangesBeforePublishingEither()
    {
        string north = Put(Path.Combine(Workspace, "north.ebp"), new byte[] { 1 });
        string south = Put(Path.Combine(Workspace, "south.ebp"), new byte[] { 9 });
        Assert.Throws<IOException>(() => ThunderPlainsSourceSave.SavePair(
            new(north, new byte[] { 1 }, new byte[] { 2 }, false),
            new(south, new byte[] { 1 }, new byte[] { 2 }, false)));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(north));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(south));
        Assert.Empty(Directory.GetFiles(Workspace, "*.tmp"));
    }

    GameAssetSource? Resolve(string path) => GameAssetSourceResolver.ResolveMasterFile(path, Workspace, Extracted, Game);

    void MakeArchive()
    {
        byte[] north = new byte[79872], south = new byte[92032];
        foreach (var threshold in ThunderPlainsLightning_File.KnownThresholds)
        {
            Pattern(north, threshold.Kami0000Offset, threshold.DefaultValue);
            Pattern(south, threshold.Kami0300Offset, threshold.DefaultValue);
        }
        string n = Put(Path.Combine(root, "fixtures", "north.ebp"), north);
        string s = Put(Path.Combine(root, "fixtures", "south.ebp"), south);
        var inputs = VbfWorkspace.Import(new[] { n, s });
        var files = new[]
        {
            inputs.Single(x => x.Path == "north.ebp") with { Path = "ffx_ps2/ffx/master/" + North },
            inputs.Single(x => x.Path == "south.ebp") with { Path = "ffx_ps2/ffx/master/" + South }
        };
        Directory.CreateDirectory(Path.Combine(Game, "data"));
        VbfArchiveWriter.Save(files, Path.Combine(Game, "data", "FFX_Data.vbf"));
    }

    static void Pattern(byte[] data, int offset, int value)
    { data[offset - 1] = 0xae; data[offset] = (byte)value; data[offset + 1] = 0; data[offset + 2] = 0x29; data[offset + 3] = 6; }
    static string Put(string path, byte[] data)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, data); return path; }
}
