using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class BattleOpeningWriterTests
{
    [Fact]
    public void EmptyScriptCanReceiveItsFirstWorkerWithoutTreatingMetadataAsCode()
    {
        byte[] source = Fixture(Array.Empty<byte>());
        int ai = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(4));
        int map = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(ai), 0);
        foreach (int at in new[] { 0x14, 0x34, 0x36 }) BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(ai + at), 0);
        source[map] = 0; source[map + 139] = 255;
        Assert.Empty(BattleOpeningWriter.Read(source));
        byte[] output = BattleOpeningTemplateWriter.CreateMonstersFirst(source, BattleOpeningWriter.Revision(source));
        Assert.Equal(4, BattleOpeningWriter.Read(output).Count);
        Assert.Equal(0x74, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(ai + 0x30)));
    }

    [Fact]
    public void TargetLabelsMatchTheNativeEnemySlotRange()
    {
        Assert.Equal("AllMonsters", AiTargetNames.Get(0xFFF1));
        Assert.Equal("Monster#0", AiTargetNames.Get(0x14));
        Assert.Equal("Monster#7", AiTargetNames.Get(0x1B));
        Assert.Null(AiTargetNames.Get(0x13));
    }
    [Fact]
    public void ExistingLifecycleHandlerGetsAPrefixWithoutReplacingItsCode()
    {
        byte[] source = Fixture(Array.Empty<byte>());
        byte[] output = BattleOpeningTemplateWriter.CreateMonstersFirst(source, BattleOpeningWriter.Revision(source));
        Assert.Equal(4, BattleOpeningWriter.Read(output).Count);
        int oldMap = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        int newMap = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8));
        Assert.Equal(1, output[newMap]); // Reuses the existing lifecycle worker, never the camera worker.
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(newMap + 156)));
        for (int i = 0; i < 160; i++)
            if (i != 156 && i != 157) Assert.Equal(source[oldMap + i], output[newMap + i]);
    }

    [Fact]
    public void CreateOpening_AddsAnIndependentWorkerAndPreservesFormationAndOriginalCode()
    {
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 9));
        int map = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        source[map + 139] = 255;
        byte[] before = source.ToArray();
        int oldFormation = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(12));
        byte[] output = BattleOpeningTemplateWriter.CreateMonstersFirst(source, BattleOpeningWriter.Revision(source));
        Assert.Equal(before, source);
        Assert.Equal(4, BattleOpeningWriter.Read(output).Count);
        Assert.Equal(new short[] { 1, 0, 0, 2 }, BattleOpeningWriter.Read(output).Select(r => r.Value));
        int newFormation = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(12));
        Assert.Equal(source.AsSpan(oldFormation).ToArray(), output.AsSpan(newFormation).ToArray());
        Assert.Equal(0, (output.Length - source.Length) % 128);
        Assert.Throws<InvalidOperationException>(() => BattleOpeningTemplateWriter.CreateMonstersFirst(output, BattleOpeningWriter.Revision(output)));
    }

    [Fact]
    public void CreationRejectsExistingHooksStaleInputAndInvalidRelocationPointers()
    {
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 9));
        Assert.Throws<InvalidOperationException>(() => BattleOpeningTemplateWriter.CreateMonstersFirst(source, BattleOpeningWriter.Revision(source)));
        Assert.Throws<InvalidOperationException>(() => BattleOpeningTemplateWriter.CreateMonstersFirst(source, "stale"));
        int map = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        source[map + 139] = 255;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x20 + 0x20), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => BattleOpeningTemplateWriter.CreateMonstersFirst(source, BattleOpeningWriter.Revision(source)));
    }

    [Fact]
    public void Apply_ChangesOnlyTheLiteralAndReopensWithoutTouchingOtherChunks()
    {
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 0));
        byte[] original = source.ToArray();
        var row = Assert.Single(BattleOpeningWriter.Read(source));
        byte[] edited = BattleOpeningWriter.Apply(source, BattleOpeningWriter.Revision(source),
            new[] { new BattleOpeningValueEdit(row.InstructionOffset, 255) });
        Assert.Equal(original, source);
        Assert.Equal(255, Assert.Single(BattleOpeningWriter.Read(edited)).Value);
        Assert.Equal(source.Length, edited.Length);
        for (int i = 0; i < source.Length; i++)
            if (i != row.ValueOffset && i != row.ValueOffset + 1) Assert.Equal(source[i], edited[i]);
        Assert.Equal(source, BattleOpeningWriter.Apply(source, BattleOpeningWriter.Revision(source), Array.Empty<BattleOpeningValueEdit>()));
    }

    [Fact]
    public void WritesOutsideTheOpeningEventAreNotEditable()
    {
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 0));
        int map = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(map + 156), 0xFFFF);
        Assert.Empty(BattleOpeningWriter.Read(source));
    }

    [Theory]
    [InlineData(0x3B, -1)]
    [InlineData(0x3B, 2)]
    [InlineData(0xE8, -1)]
    [InlineData(0xE8, 256)]
    public void NativeFieldWidthsAreEnforced(int field, int value)
    {
        byte[] source = Fixture(Direct(0xFFF1, (ushort)field, 0));
        var row = Assert.Single(BattleOpeningWriter.Read(source));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleOpeningWriter.Apply(source,
            BattleOpeningWriter.Revision(source), new[] { new BattleOpeningValueEdit(row.InstructionOffset, value) }));
    }

    [Fact]
    public void StaleDuplicateAndForeignRequestsAreRejected()
    {
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 0));
        var row = Assert.Single(BattleOpeningWriter.Read(source));
        var edit = new BattleOpeningValueEdit(row.InstructionOffset, 1);
        Assert.Throws<InvalidOperationException>(() => BattleOpeningWriter.Apply(source, "stale", new[] { edit }));
        Assert.Throws<InvalidDataException>(() => BattleOpeningWriter.Apply(source,
            BattleOpeningWriter.Revision(source), new[] { edit, edit }));
        Assert.Throws<InvalidDataException>(() => BattleOpeningWriter.Apply(source,
            BattleOpeningWriter.Revision(source), new[] { new BattleOpeningValueEdit(row.ValueOffset, 1) }));
    }

    [Fact]
    public void ReadModifyWriteRequiresMatchingActorAndFieldPairs()
    {
        var code = new List<byte>();
        code.AddRange(Push(0xFFFA)); code.AddRange(Push(0xE8));
        code.AddRange(Push(0xFFFA)); code.AddRange(Push(0xE8));
        code.AddRange(new byte[] { 0xB5, 0x0F, 0x70 }); code.AddRange(Push(2)); code.Add(0x14);
        code.AddRange(new byte[] { 0xD8, 0x18, 0x70 });
        byte[] source = Fixture(code.ToArray());
        var row = Assert.Single(BattleOpeningWriter.Read(source));
        Assert.True(row.Add);
        Assert.Equal(2, row.Value);
        byte[] edited = BattleOpeningWriter.Apply(source, BattleOpeningWriter.Revision(source),
            new[] { new BattleOpeningValueEdit(row.InstructionOffset, -2) });
        Assert.Equal(-2, Assert.Single(BattleOpeningWriter.Read(edited)).Value);
        code[7] = 0xF9; // Read another actor: cannot be presented as this actor += delta.
        Assert.Empty(BattleOpeningWriter.Read(Fixture(code.ToArray())));
    }

    [Fact]
    public void TruncatedAndInvalidMappingInputsNeverWrite()
    {
        Assert.Throws<InvalidDataException>(() => BattleOpeningWriter.Read(new byte[8]));
        byte[] source = Fixture(Direct(0xFFF1, 0xE8, 0));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(8), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => BattleOpeningWriter.Read(source));
        source = Fixture(Direct(0xFFF1, 0xE8, 0));
        int map = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        source[map + 139] = 4;
        Assert.Throws<InvalidDataException>(() => BattleOpeningWriter.Read(source));
    }

    private static byte[] Push(ushort value) => new[] { (byte)0xAE, (byte)value, (byte)(value >> 8) };
    private static byte[] Direct(ushort target, ushort field, ushort value) =>
        Push(target).Concat(Push(field)).Concat(Push(value)).Concat(new byte[] { 0xD8, 0x18, 0x70 }).ToArray();

    internal static byte[] Fixture(byte[] opening)
    {
        byte[] code = new byte[] { 0x3C, 0xD8, 0x5F, 0, 0x3C }.Concat(opening).Append((byte)0x3C).ToArray();
        int entryTable = (0x70 + code.Length + 3) & ~3;
        byte[] ai = new byte[entryTable + 12];
        void I32(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        void U16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        I32(ai, 0, code.Length); I32(ai, 0x10, ai.Length); U16(ai, 0x14, 1);
        I32(ai, 0x30, 0x70); U16(ai, 0x34, 1); U16(ai, 0x36, 1); I32(ai, 0x38, 0x3C);
        U16(ai, 0x44, 3); I32(ai, 0x5C, entryTable); I32(ai, 0x60, entryTable + 12);
        code.CopyTo(ai, 0x70); I32(ai, entryTable, 0); I32(ai, entryTable + 4, 1); I32(ai, entryTable + 8, 5);
        byte[] map = Enumerable.Repeat((byte)0xFF, 160).ToArray(); map[0] = 1; map[1] = 138; map[139] = 0;
        map[140] = 0; map[141] = 6; U16(map, 142, 144); U16(map, 144, 6); U16(map, 156, 2);
        int mapOffset = 0x20 + ai.Length, formation = mapOffset + map.Length;
        byte[] battle = new byte[formation + 32];
        I32(battle, 0, 4); I32(battle, 4, 0x20); I32(battle, 8, mapOffset);
        I32(battle, 12, formation); I32(battle, 16, battle.Length);
        ai.CopyTo(battle, 0x20); map.CopyTo(battle, mapOffset);
        Array.Fill(battle, (byte)0xCD, formation, 32);
        return battle;
    }
}
