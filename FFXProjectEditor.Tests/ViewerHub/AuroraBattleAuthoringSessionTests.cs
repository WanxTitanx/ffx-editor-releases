using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class AuroraBattleAuthoringSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-authoring-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveReopensTheExactCandidateAndKeepsAnImmutableBackupPerRevision()
    {
        Directory.CreateDirectory(_root); string path = Path.Combine(_root, "test00_00.bin");
        byte[] original = BattleCompanionRosterWriterTests.Fixture(); File.WriteAllBytes(path, original);
        var session = new AuroraBattleAuthoringSession("test00_00", path, original);
        var candidate = BattleCompanionRosterWriter.Apply("test00_00", original, BattleOpeningWriter.Revision(original),
            new[] { new BattleCompanionRosterEdit(1, 141) });
        Assert.True(session.Save("test00_00", path, candidate));
        Assert.Equal(candidate, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(session.LastBackup!));
        Assert.False(session.Save("test00_00", path, candidate));
        Assert.Equal(141, Battle_File.Read("test00_00", File.ReadAllBytes(path)).Formation!.Slots[1].DictionaryId);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void ExternalChangesAndWrongDestinationsAreRejectedBeforeAnyBackupOrWrite()
    {
        Directory.CreateDirectory(_root); string path = Path.Combine(_root, "test00_00.bin");
        byte[] original = BattleCompanionRosterWriterTests.Fixture(); File.WriteAllBytes(path, original);
        var session = new AuroraBattleAuthoringSession("test00_00", path, original);
        byte[] changed = original.ToArray(); changed[^1] ^= 1; File.WriteAllBytes(path, changed);
        Assert.Throws<InvalidOperationException>(() => session.Save("test00_00", path, original));
        Assert.Throws<InvalidOperationException>(() => session.Save("other_00", path, original));
        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
