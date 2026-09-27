using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class AuroraNativePositionSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-native-save-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_ChangesOnlyXZPreservesYWAndOtherBytesAndCreatesOriginalBackup()
    {
        var (path, original, session) = Create();
        var reply = session.Save(Request(original));
        Assert.True(reply.Ok, reply.Message);
        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(original, File.ReadAllBytes(path + ".aurora.bak"));
        Assert.Equal(100f, BitConverter.ToSingle(saved, 0xE0));
        Assert.Equal(-30f, BitConverter.ToSingle(saved, 0xE8));
        for (int i = 0; i < saved.Length; i++)
        {
            bool xz = Enumerable.Range(0, 2).Any(slot =>
                i >= 0xE0 + slot * 16 && i < 0xE4 + slot * 16 ||
                i >= 0xE8 + slot * 16 && i < 0xEC + slot * 16);
            if (!xz) Assert.Equal(original[i], saved[i]);
        }
        Assert.Equal(AuroraNativePositionSession.Revision(saved), reply.Revision);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.False(session.Save(Request(original)).Ok); // stale/replayed browser baseline
        Assert.Equal(saved, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_RejectsAFileChangedByAnotherEditor()
    {
        var (path, original, session) = Create();
        byte[] changed = original.ToArray(); changed[0x30] = 0xCA;
        File.WriteAllBytes(path, changed);
        Assert.False(session.Save(Request(original)).Ok);
        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".aurora.bak"));
    }

    [Fact]
    public void Save_RejectsWrongBattleDuplicateSlotsInvalidCountsAndNonFiniteCoordinates()
    {
        var (path, original, session) = Create();
        var valid = Request(original);
        var invalid = new[]
        {
            valid with { BattleId = "other_00" },
            valid with { Positions = Array.Empty<AuroraNativePositionSession.Position>() },
            valid with { Positions = new[] { new AuroraNativePositionSession.Position(0, 1, 2) } },
            valid with { Positions = new[] { new AuroraNativePositionSession.Position(0, 1, 2), new AuroraNativePositionSession.Position(0, 3, 4) } },
            valid with { Positions = new[] { new AuroraNativePositionSession.Position(0, float.NaN, 2), new AuroraNativePositionSession.Position(1, 3, 4) } },
            valid with { Positions = new[] { new AuroraNativePositionSession.Position(-1, 1, 2), new AuroraNativePositionSession.Position(1, 3, 4) } }
        };
        foreach (var request in invalid) Assert.False(session.Save(request).Ok);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".aurora.bak"));
    }

    [Fact]
    public void Session_MatchesOnlyItsOriginalProjectPathAndBattle()
    {
        var (path, _, session) = Create();
        Assert.True(session.Matches("test00_00", path));
        Assert.False(session.Matches("test00_01", path));
        Assert.False(session.Matches("test00_00", Path.Combine(_root, "other.bin")));
    }

    private (string Path, byte[] Bytes, AuroraNativePositionSession Session) Create()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "battle.bin");
        byte[] bytes = new byte[0x110];
        int[] header = { 5, 0x20, 0x30, 0x40, 0x80, bytes.Length };
        for (int i = 0; i < header.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), header[i]);
        bytes[0x81] = 1; bytes[0x86] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0xA0), 0x60);
        for (int slot = 0; slot < 2; slot++)
        for (int axis = 0; axis < 4; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0xE0 + slot * 16 + axis * 4), slot * 10 + axis + 0.25f);
        File.WriteAllBytes(path, bytes);
        return (path, bytes, new AuroraNativePositionSession("test00_00", path));
    }

    private static AuroraNativePositionSession.Request Request(byte[] bytes) => new(
        "test00_00", AuroraNativePositionSession.Revision(bytes),
        new[] { new AuroraNativePositionSession.Position(0, 100, -30), new AuroraNativePositionSession.Position(1, 20, 40) });

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
