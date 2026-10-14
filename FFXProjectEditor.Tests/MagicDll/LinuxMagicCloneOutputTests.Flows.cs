using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Magic;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

// ── Existing public wrapper wired to the native create-only path ──
// Real DLLs remain data, never loaded as native code. Copies and mutations use only private fixtures.
public sealed partial class LinuxMagicCloneOutputTests
{
    [Fact]
    public void PublicClone_UsesEditedSnapshotAndDoesNotAcquireRestoreOwnership()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string source = MagicDllTestFixture.Write(fixture.Base, "magic_0021.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out string loadError), loadError);
        var field = document.ParsedFile!.Roots.SelectMany(root => root.Programs).SelectMany(program => program.Slots)
            .SelectMany(slot => document.ResolveAllFields(slot))
            .First(value => value.Type == FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.F32);
        Assert.True(document.TryApplyFieldEdit(field, BitConverter.GetBytes(197.25f), out string editError), editError);
        byte[] expected = MagicDllNameRewriter.RewriteMagicNameStrings(document.WorkingBytes!, 21, 140);
        byte[] sourceBefore = File.ReadAllBytes(source);
        string beforeHash = document.ShaBefore, afterHash = document.ShaAfter;
        Assert.True(document.TryCloneMagic(fixture.Destination, out string cloneError), cloneError);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(beforeHash, document.ShaBefore);
        Assert.Equal(afterHash, document.ShaAfter);
        Assert.False(document.HasRestorableBackup);
        Assert.Null(document.LastSavedPath);
        Assert.False(document.TryRestoreBackup(fixture.Destination, out _));
        Assert.Single(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Theory]
    [InlineData("magic_0021.dll")]
    [InlineData("magic_0140.dll")]
    public void RealCorpusClone_UsesLoadedBytesAndExistingNameRewriter(string name)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string corpus = Path.Combine(TestDataPaths.MagicCorpus, name);
        byte[] original = File.ReadAllBytes(corpus);
        string source = Path.Combine(fixture.Base, name);
        File.WriteAllBytes(source, original);
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out string loadError), loadError);
        byte[] expected = MagicDllNameRewriter.RewriteMagicNameStrings(document.WorkingBytes!, document.MagicId, 140);
        // A later change in our fixture source cannot make the clone re-read that path.
        File.WriteAllBytes(source, new byte[] { 13 });
        Assert.True(document.TryCloneMagic(fixture.Destination, out string cloneError), cloneError);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(source));
        Assert.Equal(original, File.ReadAllBytes(corpus));
        Assert.False(document.HasRestorableBackup);
        Assert.Null(document.LastSavedPath);
    }
}

