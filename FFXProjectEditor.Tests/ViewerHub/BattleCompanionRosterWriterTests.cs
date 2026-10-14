using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class BattleCompanionRosterWriterTests
{
    internal static byte[] Fixture()
    {
        byte[] bytes = BattleOpeningWriterTests.Fixture(Array.Empty<byte>());
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)) + 12;
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + i * 2), 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), 0x10D5);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 2), 0xA004);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), 0x100C);
        return bytes;
    }

    [Fact]
    public void ReplacementPreservesFlagsScriptsPositionsAndOtherMembers()
    {
        byte[] source = Fixture(), original = source.ToArray();
        var before = Battle_File.Read("test00_00", source);
        byte[] output = BattleCompanionRosterWriter.Apply("test00_00", source, BattleOpeningWriter.Revision(source),
            new[] { new BattleCompanionRosterEdit(1, 141) });
        var after = Battle_File.Read("test00_00", output);
        Assert.Equal(141, after.Formation!.Slots[1].DictionaryId);
        Assert.Equal(0xA08D, after.Formation.Slots[1].RawMonsterId);
        Assert.Equal(original, source);
        for (int i = 0; i < source.Length; i++)
            if (i != before.FormationSlotsOffset + 2 && i != before.FormationSlotsOffset + 3)
                Assert.Equal(source[i], output[i]);
    }

    [Fact]
    public void InvalidOrStaleEditsLeaveTheInputUntouched()
    {
        byte[] source = Fixture(), original = source.ToArray();
        string revision = BattleOpeningWriter.Revision(source);
        Assert.Throws<InvalidOperationException>(() => BattleCompanionRosterWriter.Apply("test00_00", source, "stale", Array.Empty<BattleCompanionRosterEdit>()));
        Assert.Throws<InvalidDataException>(() => BattleCompanionRosterWriter.Apply("test00_00", source, revision, new[] { new BattleCompanionRosterEdit(7, 4) }));
        Assert.Throws<InvalidDataException>(() => BattleCompanionRosterWriter.Apply("test00_00", source, revision, new[] { new BattleCompanionRosterEdit(1, 4), new BattleCompanionRosterEdit(1, 5) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleCompanionRosterWriter.Apply("test00_00", source, revision, new[] { new BattleCompanionRosterEdit(1, 1000) }));
        Assert.Equal(original, source);
    }
}
