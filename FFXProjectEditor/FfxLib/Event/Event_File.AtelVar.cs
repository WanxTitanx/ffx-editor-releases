using System;
using System.Runtime.InteropServices;

namespace FFXProjectEditor.FfxLib.Event
{
    // ATEL eventData-variable addressing + in-place patcher.
    // PROVEN vs bltz0002.ebp (docs/reverse/FFX_BLITZBALL_ROSTER_BASE_RE_2026-06-10.md):
    // the ATEL script chunk (chunk 0) stores typed script variables; the "eventData" location
    // ones hold flat value arrays embedded in the chunk. bltz0002 vars 0x126..0x12E are the
    // 60-player blitzball stat-growth tables (240 floats each = 60 players x (a,b,c,growthType)).
    //
    // Addressing (all offsets relative to the START of the chunk-0 byte array):
    //   worker0       = u32 @ 0x38
    //   varTable      = u32 @ worker0 + 0x14      (variable declaration table; shared across workers)
    //   eventDataBase = u32 @ worker0 + 0x30      (base of eventData value storage)
    //   var entry[id] = 8 bytes @ varTable + id*8 : lb=u32, hb=u32
    //     declOffset  = lb & 0x00FFFFFF ; format = (lb>>28)&0xF ; elementCount = hb & 0xFFFF
    //     width       = format<2 ? 1 : format<4 ? 2 : 4
    //   element bytes = eventDataBase + declOffset + index*width    (chunk-0-relative)
    //
    // PatchEventDataElement overwrites exactly `width` bytes in place => length-preserving, so
    // Event_File.Write() (via ScriptChunkOverride) round-trips byte-identical except the patched slice.

    /// <summary>ATEL script variable type, encoded in the high nibble of the properties byte of each 8-byte var entry.
    /// Validated via Ghidra RE (Fahrenheit) against the opcode switch at 0x864180.</summary>
    public enum AtelScriptVarType : byte
    {
        U8  = 0x00,
        I8  = 0x01,
        U16 = 0x02,
        I16 = 0x03,
        U32 = 0x04,
        I32 = 0x05,
        F32 = 0x06,
    }

    /// <summary>ATEL script variable storage location, encoded in the low nibble of the properties byte.</summary>
    public enum AtelScriptVarLocation : byte
    {
        SaveData     = 0x00,
        CommonVars   = 0x01,
        Data         = 0x02,
        Private      = 0x03,
        Shared       = 0x04,
        IntRegisters = 0x05,
        EventData    = 0x06,
    }

    public sealed partial class Event_File
    {
        /// <summary>A located ATEL eventData variable's value array, offsets chunk-0-relative.</summary>
        public readonly record struct AtelVariableSlot(int ValueOffset, int Width, int ElementCount, int Format)
        {
            /// <summary>Decoded variable type from the format nibble. Unknown values (7-15) return null.</summary>
            public AtelScriptVarType? VariableType => Format switch
            {
                0 => AtelScriptVarType.U8,
                1 => AtelScriptVarType.I8,
                2 => AtelScriptVarType.U16,
                3 => AtelScriptVarType.I16,
                4 => AtelScriptVarType.U32,
                5 => AtelScriptVarType.I32,
                6 => AtelScriptVarType.F32,
                _ => null,
            };
        }

        byte[] CurrentScriptChunk() => ScriptChunkOverride ?? Chunks[ChunkScript].Bytes;

        static int ReadU32AsInt(byte[] b, int offset)
            => b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);

        (int eventDataBase, int varTable) ReadAtelLayout(byte[] script)
        {
            int worker0 = ReadU32AsInt(script, 0x38);
            int varTable = ReadU32AsInt(script, worker0 + 0x14);
            int eventDataBase = ReadU32AsInt(script, worker0 + 0x30);
            return (eventDataBase, varTable);
        }

        /// <summary>Locate an ATEL eventData variable's value array inside the script chunk (chunk-0-relative).</summary>
        public AtelVariableSlot LocateEventDataVariable(int variableId)
        {
            byte[] script = CurrentScriptChunk();
            (int eventDataBase, int varTable) = ReadAtelLayout(script);
            int entry = varTable + variableId * 8;
            uint lb = (uint)ReadU32AsInt(script, entry);
            uint hb = (uint)ReadU32AsInt(script, entry + 4);
            int declOffset = (int)(lb & 0x00FFFFFF);
            int format = (int)((lb >> 28) & 0xF);
            int count = (int)(hb & 0xFFFF);
            int width = format < 2 ? 1 : format < 4 ? 2 : 4;
            return new AtelVariableSlot(eventDataBase + declOffset, width, count, format);
        }

        /// <summary>Absolute FILE offset of an eventData element (for gates/diagnostics).</summary>
        public int EventDataElementFileOffset(int variableId, int entryIndex)
        {
            AtelVariableSlot slot = LocateEventDataVariable(variableId);
            return Chunks[ChunkScript].Offset + slot.ValueOffset + entryIndex * slot.Width;
        }

        /// <summary>Read the raw bytes of one eventData element.</summary>
        public byte[] ReadEventDataElement(int variableId, int entryIndex)
        {
            byte[] script = CurrentScriptChunk();
            AtelVariableSlot slot = LocateEventDataVariable(variableId);
            if ((uint)entryIndex >= (uint)slot.ElementCount)
                throw new ArgumentOutOfRangeException(nameof(entryIndex),
                    $"ATEL var 0x{variableId:X} has {slot.ElementCount} elements; index {entryIndex} out of range.");
            byte[] outBytes = new byte[slot.Width];
            Array.Copy(script, slot.ValueOffset + entryIndex * slot.Width, outBytes, 0, slot.Width);
            return outBytes;
        }

        /// <summary>Read one eventData element as int, decoded by the slot's variable type.
        /// F32 returns BitConverter.SingleToInt32Bits (raw bits as int). Unknown type returns 0.</summary>
        public int ReadEventDataAsInt(int variableId, int entryIndex)
        {
            AtelVariableSlot slot = LocateEventDataVariable(variableId);
            byte[] raw = ReadEventDataElement(variableId, entryIndex);
            return slot.VariableType switch
            {
                AtelScriptVarType.I8   => (sbyte)raw[0],
                AtelScriptVarType.U16  => raw[0] | (raw[1] << 8),
                AtelScriptVarType.I16  => (short)(raw[0] | (raw[1] << 8)),
                AtelScriptVarType.U32  => raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24),
                AtelScriptVarType.I32  => raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24),
                AtelScriptVarType.F32  => raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24),
                AtelScriptVarType.U8   => raw[0],
                _ => 0,
            };
        }

        /// <summary>Read one eventData element as float. Only valid for F32 type; for other types returns 0.</summary>
        public float ReadEventDataAsFloat(int variableId, int entryIndex)
        {
            AtelVariableSlot slot = LocateEventDataVariable(variableId);
            if (slot.VariableType != AtelScriptVarType.F32) return 0f;
            byte[] raw = ReadEventDataElement(variableId, entryIndex);
            return BitConverter.Int32BitsToSingle(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
        }

        /// <summary>Overwrite one eventData element in place (length-preserving). Sets <see cref="ScriptChunkOverride"/>.
        /// Clones the script chunk on the first patch, then mutates the override in place on subsequent patches.</summary>
        public void PatchEventDataElement(int variableId, int entryIndex, ReadOnlySpan<byte> newBytes)
        {
            byte[] script = ScriptChunkOverride ?? (byte[])Chunks[ChunkScript].Bytes.Clone();
            (int eventDataBase, int varTable) = ReadAtelLayout(script);
            int entry = varTable + variableId * 8;
            uint lb = (uint)ReadU32AsInt(script, entry);
            uint hb = (uint)ReadU32AsInt(script, entry + 4);
            int declOffset = (int)(lb & 0x00FFFFFF);
            int format = (int)((lb >> 28) & 0xF);
            int count = (int)(hb & 0xFFFF);
            int width = format < 2 ? 1 : format < 4 ? 2 : 4;
            if ((uint)entryIndex >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(entryIndex),
                    $"ATEL var 0x{variableId:X} has {count} elements; index {entryIndex} out of range.");
            if (newBytes.Length != width)
                throw new ArgumentException($"ATEL var 0x{variableId:X} element width is {width}, got {newBytes.Length} bytes.");
            newBytes.CopyTo(script.AsSpan(eventDataBase + declOffset + entryIndex * width, width));
            ScriptChunkOverride = script;
        }

        /// <summary>Current ATEL script-chunk bytes (the override if set, else the original chunk-0). Read-only.</summary>
        public ReadOnlySpan<byte> ScriptChunkBytes => ScriptChunkOverride ?? Chunks[ChunkScript].Bytes;

        /// <summary>Patch a CALL/CALLPOPA operand at a verified instruction boundary.
        /// Uses the current override so sequential edits preserve earlier changes.</summary>
        public void PatchNativeCallOperand(int instructionOffset, ushort newValue)
        {
            byte[] script = CurrentScriptChunk();
            var instructions = EventAtelDisassembler.DisassembleAtelChunk(script);
            if (!instructions.Exists(i => i.Offset == instructionOffset && i.IsNativeCall))
                throw new ArgumentOutOfRangeException(nameof(instructionOffset),
                    $"Offset 0x{instructionOffset:X} is not a native-call instruction in the ATEL code region.");
            PatchScriptUInt16(instructionOffset + 1, newValue);
        }

        /// <summary>Overwrite one byte in the ATEL script chunk at a chunk-0-relative offset (length-preserving).
        /// Used to patch a code immediate (e.g. a blitzball recruit player-id). Sets <see cref="ScriptChunkOverride"/>;
        /// clones the chunk on the first patch, then mutates the override in place.</summary>
        public void PatchScriptByte(int scriptOffset, byte newValue)
        {
            byte[] script = ScriptChunkOverride ?? (byte[])Chunks[ChunkScript].Bytes.Clone();
            if ((uint)scriptOffset >= (uint)script.Length)
                throw new ArgumentOutOfRangeException(nameof(scriptOffset),
                    $"script offset 0x{scriptOffset:X} out of range (chunk len 0x{script.Length:X}).");
            script[scriptOffset] = newValue;
            ScriptChunkOverride = script;
        }

        /// <summary>Read a little-endian u16 code immediate (e.g. a PUSHII operand) at a chunk-0-relative offset.</summary>
        public ushort ReadScriptUInt16(int scriptOffset)
        {
            ReadOnlySpan<byte> s = ScriptChunkBytes;
            if (scriptOffset < 0 || scriptOffset + 2 > s.Length)
                throw new ArgumentOutOfRangeException(nameof(scriptOffset),
                    $"script offset 0x{scriptOffset:X} out of range (chunk len 0x{s.Length:X}).");
            return (ushort)(s[scriptOffset] | (s[scriptOffset + 1] << 8));
        }

        /// <summary>Overwrite a little-endian u16 code immediate (e.g. a PUSHII operand for a blitzball prize-index)
        /// at a chunk-0-relative offset (length-preserving). Sets <see cref="ScriptChunkOverride"/>; clones the chunk
        /// on the first patch, then mutates the override in place.</summary>
        public void PatchScriptUInt16(int scriptOffset, ushort newValue)
        {
            byte[] script = ScriptChunkOverride ?? (byte[])Chunks[ChunkScript].Bytes.Clone();
            if (scriptOffset < 0 || scriptOffset + 2 > script.Length)
                throw new ArgumentOutOfRangeException(nameof(scriptOffset),
                    $"script offset 0x{scriptOffset:X} out of range (chunk len 0x{script.Length:X}).");
            script[scriptOffset] = (byte)(newValue & 0xFF);
            script[scriptOffset + 1] = (byte)((newValue >> 8) & 0xFF);
            ScriptChunkOverride = script;
        }
    }
}
