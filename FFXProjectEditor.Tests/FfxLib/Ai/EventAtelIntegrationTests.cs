using System;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.Modules.EventExplorer;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public class EventAtelIntegrationTests
{
    [Theory]
    [InlineData(0x20, "POPV")]
    [InlineData(0x22, "PUSHAR")]
    [InlineData(0x25, "POPA")]
    [InlineData(0x2B, "REPUSH")]
    [InlineData(0x35, "CALL")]
    [InlineData(0x41, "PUSHN")]
    [InlineData(0x47, "BREQ")]
    [InlineData(0x58, "CALLPOPA")]
    [InlineData(0x77, "REQWAIT")]
    [InlineData(0x7B, "?0x7B")]
    public void EventMnemonics_DescribeTheNativeInstruction(int opcode, string expected)
    {
        Assert.Equal(expected, EventAtelDisassembler.Mnemonic((byte)opcode));
        Assert.Equal(expected, EventAtelDisassembler.Mnemonic((byte)(opcode | 0x80)));
    }

    [Fact]
    public void StructuredRange_DoesNotReadOperandBytesOutsideTheRequestedRange()
    {
        byte[] bytes = { 0xAE, 0x34, 0x12, 0x3C };
        Assert.Empty(EventAtelDisassembler.DisassembleStructured(bytes, 0, 1));
    }

    [Fact]
    public void TextRange_ReportsAnOperandTruncatedByTheRequestedRange()
    {
        byte[] bytes = { 0xAE, 0x34, 0x12, 0x3C };
        Assert.Contains("TRUNCATED", Assert.Single(EventAtelDisassembler.Disassemble(bytes, 0, 1)));
    }

    [Theory]
    [InlineData(0x18)] // fake native-call byte inside the header
    [InlineData(0x41)] // first operand byte, not an instruction boundary
    [InlineData(0x50)] // fake native call after code, inside data
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void EventEditor_RejectsNativeCallSelectionsOutsideAnInstructionBoundary(int offset)
    {
        Event_File file = Fixture();
        byte[] original = (byte[])file.Chunks[0].Bytes.Clone();
        EventExplorer_DataModel model = Editor(file);
        model.SelectedInstruction = Call(offset, 0xB5, 0x00A9);
        model.PatchOperandHex = "0x700B";
        model.ApplyPatchedOperand();
        Assert.Null(file.ScriptChunkOverride);
        Assert.Equal(original, file.Chunks[0].Bytes);
    }

    [Fact]
    public void ChunkDisassembly_UsesOnlyDeclaredCodeAndKeepsChunkRelativeOffsets()
    {
        var instructions = EventAtelDisassembler.DisassembleAtelChunk(Fixture().Chunks[0].Bytes);
        Assert.Equal(new[] { 0x40, 0x43, 0x46 }, instructions.Select(i => i.Offset));
        Assert.Equal(new[] { "CALL", "CALLPOPA", "RET" }, instructions.Select(i => i.Mnemonic));
    }

    [Theory]
    [InlineData(-1, 7)]
    [InlineData(0x20, 7)]
    [InlineData(int.MaxValue, 7)]
    [InlineData(0x40, -1)]
    [InlineData(0x40, int.MaxValue)]
    [InlineData(0x80, 1)]
    public void ChunkDisassembly_RejectsInvalidAndOverflowingCodeRanges(int offset, int length)
    {
        byte[] chunk = Fixture().Chunks[0].Bytes;
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(0x30), offset);
        BinaryPrimitives.WriteInt32LittleEndian(chunk, length);
        Assert.Throws<InvalidDataException>(() => EventAtelDisassembler.DisassembleAtelChunk(chunk));
    }

    [Fact]
    public void ChunkDisassembly_RejectsShortHeadersAndTruncatedInstructions()
    {
        Assert.Throws<InvalidDataException>(() => EventAtelDisassembler.DisassembleAtelChunk(new byte[0x37]));
        Event_File file = Fixture();
        BinaryPrimitives.WriteInt32LittleEndian(file.Chunks[0].Bytes, 2);
        Assert.Throws<InvalidDataException>(() => file.PatchNativeCallOperand(0x40, 0x700B));
        Assert.Null(file.ScriptChunkOverride);
    }

    [Fact]
    public void EmptyDeclaredCode_ProducesNoPatchableRows()
    {
        Event_File file = Fixture();
        BinaryPrimitives.WriteInt32LittleEndian(file.Chunks[0].Bytes, 0);
        Assert.Empty(EventAtelDisassembler.DisassembleAtelChunk(file.Chunks[0].Bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.PatchNativeCallOperand(0x40, 0x700B));
    }

    [Fact]
    public void RawRanges_RejectOutOfBoundsArgumentsWithoutOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventAtelDisassembler.DisassembleStructured(new byte[4], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventAtelDisassembler.DisassembleStructured(new byte[4], 1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventAtelDisassembler.Disassemble(new byte[4], 0, -1));
    }

    [Fact]
    public void EventEditor_KeepsBothPatchesAndNeverChangesOriginalChunk()
    {
        Event_File file = Fixture();
        byte[] original = (byte[])file.Chunks[0].Bytes.Clone();
        EventExplorer_DataModel model = Editor(file);
        model.SelectedInstruction = Call(0x40, 0xB5, 0x00A9);
        model.PatchOperandHex = "0x700B";
        model.ApplyPatchedOperand();
        model.SelectedInstruction = Call(0x43, 0xD8, 0x7015);
        model.PatchOperandHex = "0x705A";
        model.ApplyPatchedOperand();

        byte[] expected = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(0x41), 0x700B);
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(0x44), 0x705A);
        Assert.Equal(expected, file.ScriptChunkOverride);
        Assert.Equal(original, file.Chunks[0].Bytes);
        Assert.Equal(new[] { 0x40, 0x43, 0x46 }, model.DisassemblyLines.Select(i => i.Offset));
        Event_File reread = Event_File.Read("synthetic", file.Write());
        Assert.Equal(expected, reread.Chunks[0].Bytes);
    }

    internal static Event_File Fixture()
    {
        byte[] ebp = new byte[0xC0];
        "EV01"u8.CopyTo(ebp);
        BinaryPrimitives.WriteInt32LittleEndian(ebp.AsSpan(4), 0x40);
        BinaryPrimitives.WriteInt32LittleEndian(ebp.AsSpan(8), ebp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(ebp.AsSpan(12), -1);
        Span<byte> chunk = ebp.AsSpan(0x40);
        BinaryPrimitives.WriteInt32LittleEndian(chunk, 7);
        BinaryPrimitives.WriteInt32LittleEndian(chunk[0x10..], chunk.Length);
        BinaryPrimitives.WriteInt32LittleEndian(chunk[0x30..], 0x40);
        new byte[] { 0xB5, 0xA9, 0x00, 0xD8, 0x15, 0x70, 0x3C }.CopyTo(chunk[0x40..]);
        new byte[] { 0xB5, 0x00, 0x70 }.CopyTo(chunk[0x18..]);
        new byte[] { 0xB5, 0x00, 0x70 }.CopyTo(chunk[0x50..]);
        return Event_File.Read("synthetic", ebp);
    }

    static DisassemblyInstruction Call(int offset, byte raw, ushort operand) => new()
    {
        Offset = offset, Length = 3, Raw = raw, Mnemonic = "CALL", HasOperand = true,
        Operand = operand, IsNativeCall = true, GlossaryName = "test call",
    };

    static EventExplorer_DataModel Editor(Event_File file)
    {
        // Exercise the real patch command without loading the user's project.
        var model = (EventExplorer_DataModel)RuntimeHelpers.GetUninitializedObject(typeof(EventExplorer_DataModel));
        typeof(EventExplorer_DataModel).GetField("loadedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, file);
        typeof(EventExplorer_DataModel).GetField("<DisassemblyLines>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new ObservableCollection<DisassemblyInstruction>());
        return model;
    }
}
