using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Services;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
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

    [Fact]
    public void Describe_ExposesThreePartyAnchorsWithoutInventingACamera()
    {
        var (_, _, session) = Create();
        using var json = JsonDocument.Parse(session.Describe());
        var party = json.RootElement.GetProperty("party");
        Assert.Equal(3, party.GetArrayLength());
        Assert.Equal(0.5f, party[0].GetProperty("x").GetSingle());
        Assert.Equal(1.5f, party[0].GetProperty("y").GetSingle());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("camera").ValueKind);
    }

    [Fact]
    public void Save_PartyAndMonstersAreAtomicXZOnlyAndBackupRemainsOriginal()
    {
        var (path, original, session) = Create();
        var request = Request(original) with { PartyPositions = PartyPositions() };
        var reply = session.Save(request);
        Assert.True(reply.Ok, reply.Message);
        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(-120f, BitConverter.ToSingle(saved, 0x100));
        Assert.Equal(60f, BitConverter.ToSingle(saved, 0x108));
        var allowed = new System.Collections.Generic.HashSet<int>();
        foreach (var (offset, count) in new[] { (0xE0, 2), (0x100, 3) })
        for (int slot = 0; slot < count; slot++)
        for (int b = 0; b < 4; b++)
        {
            allowed.Add(offset + slot * 16 + b);
            allowed.Add(offset + slot * 16 + 8 + b);
        }
        for (int i = 0; i < saved.Length; i++)
            if (!allowed.Contains(i)) Assert.Equal(original[i], saved[i]);
        Assert.Equal(original, File.ReadAllBytes(path + ".aurora.bak"));
        Assert.True(session.Save(request with { Revision = reply.Revision! }).Ok);
        Assert.Equal(original, File.ReadAllBytes(path + ".aurora.bak"));
    }

    [Fact]
    public void Save_InvalidPartyPayloadCannotPartiallyWriteMonsters()
    {
        var (path, original, session) = Create();
        var valid = PartyPositions();
        foreach (var party in new[]
        {
            Array.Empty<AuroraNativePositionSession.Position>(), valid.Take(2).ToArray(),
            new[] { valid[0], valid[0], valid[2] },
            new[] { valid[0] with { X = float.PositiveInfinity }, valid[1], valid[2] },
            new[] { valid[0], valid[1] with { Z = -1_000_001 }, valid[2] }
        })
        {
            Assert.False(session.Save(Request(original) with { PartyPositions = party }).Ok);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        Assert.False(File.Exists(path + ".aurora.bak"));
    }

    [Fact]
    public async Task Save_RefreshedNativePreviewServesSavedBytesOnEveryReload()
    {
        if (!AuroraMemoryFixture.Native()) return;
        var (_, original, _) = Create();
        await using var fixture = new AuroraMemoryFixture();
        File.WriteAllBytes(fixture.Battle, original);
        var session = new AuroraNativePositionSession("test00_00", fixture.Battle);
        using var owner = new AuroraBattleOverlayOwner(fixture.ViewerRoot);
        owner.Update(true, fixture.Server, () => fixture.Stage(), "disabled");
        var before = owner.Current!.Value;
        var reply = session.Save(Request(original) with { PartyPositions = PartyPositions() });
        Assert.True(reply.Ok, reply.Message);
        owner.Update(true, fixture.Server, () => fixture.Stage(), "disabled");
        Assert.False(before.MemoryLease!.IsActive);
        byte[] saved = File.ReadAllBytes(fixture.Battle);
        Assert.NotEqual(original, saved);
        await fixture.AssertMemoryAsync(owner.Current!.Value, saved);
        Assert.Equal(saved, await fixture.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        Assert.Equal(new byte[] { 0x10 }, File.ReadAllBytes(fixture.SelectedPath));
    }

    private static AuroraNativePositionSession.Position[] PartyPositions() => new[]
    {
        new AuroraNativePositionSession.Position(0, -120, 60),
        new AuroraNativePositionSession.Position(1, -80, 30),
        new AuroraNativePositionSession.Position(2, -40, 0)
    };

    [Fact]
    public void Save_PreservesAdditionalPartyAnchorsOutsideTheThreePreviewSlots()
    {
        var (path, original, _) = Create();
        original[0x84] = 4;
        for (int i = 0; i < 16; i++) original[0x130 + i] = (byte)(0xA0 + i);
        File.WriteAllBytes(path, original);
        var session = new AuroraNativePositionSession("test00_00", path);
        var reply = session.Save(Request(original) with { PartyPositions = PartyPositions() });
        Assert.True(reply.Ok, reply.Message);
        Assert.Equal(original.AsSpan(0x130, 16).ToArray(), File.ReadAllBytes(path).AsSpan(0x130, 16).ToArray());
    }

    [Fact]
    public void Save_RejectsOverlappingPartyAndMonsterArraysWithoutWriting()
    {
        var (path, original, _) = Create();
        BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(0x90), 0x60);
        File.WriteAllBytes(path, original);
        var session = new AuroraNativePositionSession("test00_00", path);
        Assert.False(session.Save(Request(original) with { PartyPositions = PartyPositions() }).Ok);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".aurora.bak"));
    }

    [Fact]
    public void Save_RemovedMonsterReservedAnchorIsNotRequiredOrOverwritten()
    {
        var (path, original, _) = Create();
        for (int slot = 0; slot < 8; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x4C + slot * 2), slot == 0 ? (ushort)0x1009 : (ushort)0xFFFF);
        File.WriteAllBytes(path, original);
        var session = new AuroraNativePositionSession("test00_00", path);
        using var description = JsonDocument.Parse(session.Describe());
        Assert.Equal(1, description.RootElement.GetProperty("actors").GetArrayLength());
        var request = new AuroraNativePositionSession.Request("test00_00", AuroraNativePositionSession.Revision(original),
            new[] { new AuroraNativePositionSession.Position(0, 88, -22) });
        var reply = session.Save(request);
        Assert.True(reply.Ok, reply.Message);
        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(88f, BitConverter.ToSingle(saved, 0xE0));
        Assert.Equal(original.AsSpan(0xF0, 16).ToArray(), saved.AsSpan(0xF0, 16).ToArray());
    }

    [Fact]
    public void FormationPositions_HidesUnusedMonsterSlotsUntilTechnicalDetailsEnabled()
    {
        var (_, bytes, _) = Create();
        for (int slot = 0; slot < 8; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x4C + slot * 2), slot == 0 ? (ushort)0x1009 : (ushort)0xFFFF);
        var project = Project_Service.Instance;
        string? previous = project.ProjectPath;
        AuroraChamber_DataModel? model = null;
        try
        {
            project.ProjectPath = Path.Combine(_root, "master");
            Directory.CreateDirectory(project.ProjectPath);
            string path = project.GetPathBattle("test00_00");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            model = new AuroraChamber_DataModel { SelectedBattle = "test00_00" };
            Assert.Equal(4, model.VisibleAnchors.Count()); // One monster and three party positions.
            Assert.Single(model.VisibleAnchors.Where(a => a.IsMonsterLive));
            model.ShowTechnicalAnchors = true;
            Assert.Equal(5, model.VisibleAnchors.Count()); // Reserved coordinates remain inspectable.
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            model?.CloseRealGamePreview();
            project.ProjectPath = previous;
        }
    }

    [Fact]
    public void ReloadSelectedBattle_PreservesSelectionWhileReadingUpdatedPositions()
    {
        var (_, bytes, _) = Create();
        var project = Project_Service.Instance;
        string? previous = project.ProjectPath;
        AuroraChamber_DataModel? model = null;
        try
        {
            string master = Path.Combine(_root, "master");
            Directory.CreateDirectory(master);
            project.ProjectPath = master;
            string path = project.GetPathBattle("test00_00");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            model = new AuroraChamber_DataModel { SelectedBattle = "test00_00" };
            var selectionChanges = new System.Collections.Generic.List<string?>();
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(model.SelectedBattle)) selectionChanges.Add(model.SelectedBattle);
            };
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0xE0), 123);
            File.WriteAllBytes(path, bytes);
            model.ReloadSelectedBattle();
            Assert.Equal("test00_00", model.SelectedBattle);
            Assert.Empty(selectionChanges); // The control closes its viewers on this notification.
            Assert.Equal(123f, model.Anchors.Single(a => a.IsMonsterLive && a.Anchor.Index == 0).Anchor.X);
        }
        finally
        {
            model?.CloseRealGamePreview();
            project.ProjectPath = previous;
        }
    }

    private (string Path, byte[] Bytes, AuroraNativePositionSession Session) Create()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "battle.bin");
        byte[] bytes = new byte[0x150];
        int[] header = { 5, 0x20, 0x30, 0x40, 0x80, bytes.Length };
        for (int i = 0; i < header.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), header[i]);
        bytes[0x81] = 1; bytes[0x86] = 2;
        bytes[0x84] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x90), 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0xA0), 0x60);
        for (int slot = 0; slot < 2; slot++)
        for (int axis = 0; axis < 4; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0xE0 + slot * 16 + axis * 4), slot * 10 + axis + 0.25f);
        for (int slot = 0; slot < 3; slot++)
        for (int axis = 0; axis < 4; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x100 + slot * 16 + axis * 4), slot * 20 + axis + 0.5f);
        File.WriteAllBytes(path, bytes);
        return (path, bytes, new AuroraNativePositionSession("test00_00", path));
    }

    private static AuroraNativePositionSession.Request Request(byte[] bytes) => new(
        "test00_00", AuroraNativePositionSession.Revision(bytes),
        new[] { new AuroraNativePositionSession.Position(0, 100, -30), new AuroraNativePositionSession.Position(1, 20, 40) });

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
