// ============================================================================
// FfxSaveCore — low-level accessors for the 25848-byte FFX save payload
// PURPOSE : leaves the save blob exposed as a byte[] and provides typed LE read/write, bit flags, the
//           FFX string codec, and the pre-save tamper-tag + CRC both-way update.
// WHY     : the save is a flat LE struct (FFXED community format); every editor reads/writes through here.
// EVIDENCE: FFXED v0.749; CRC/tamper layout verified against real ffx_000 saves.
// MAINT   : DataSize is the full payload length — do not change without re-proving every provider offset.
// ============================================================================
using System;
using System.Buffers.Binary;
using System.Text;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Low-level accessors for the 25848-byte FFX save payload (FFXED / community format).
    /// </summary>
    public sealed class FfxSaveCore
    {
        public const int DataSize = 25848;

        // Written on every save (FFXED C.e).
        static readonly byte[] TamperTag =
        {
            84, 115, 120, 131, 116, 115, 58, 113, 136, 58, 85, 85, 103, 84, 83,
        };

        public byte[] Data { get; }

        public FfxSaveCore()
        {
            Data = new byte[DataSize];
        }

        public FfxSaveCore(byte[] data)
        {
            if (data.Length != DataSize)
                throw new ArgumentException($"Expected {DataSize} bytes, got {data.Length}.", nameof(data));
            Data = data;
        }

        public FfxSaveCore Clone() => new((byte[])Data.Clone());

        public int ReadInt32Le(int offset, int byteCount)
        {
            return byteCount switch
            {
                1 => Data[offset],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset)),
                4 => (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(offset)),
                _ => throw new ArgumentOutOfRangeException(nameof(byteCount)),
            };
        }

        public void WriteInt32Le(int offset, int value, int byteCount)
        {
            switch (byteCount)
            {
                case 1:
                    Data[offset] = (byte)Math.Clamp(value, 0, 255);
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(Data.AsSpan(offset), (ushort)Math.Clamp(value, 0, 65535));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(Data.AsSpan(offset), (uint)Math.Max(0, value));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(byteCount));
            }
        }

        public static void WriteUInt16Le(byte[] buffer, int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

        public bool ReadBit(int byteOffset, int bitIndex) =>
            ((Data[byteOffset] >> bitIndex) & 1) == 1;

        public void WriteBit(int byteOffset, int bitIndex, bool value)
        {
            if (value)
                Data[byteOffset] = (byte)(Data[byteOffset] | (1 << bitIndex));
            else
                Data[byteOffset] = (byte)(Data[byteOffset] & ~(1 << bitIndex));
        }

        /// <summary>
        /// FFXED bit field: <paramref name="fieldBitIndex"/> spans bytes from <paramref name="fieldBaseOffset"/>.
        /// </summary>
        public bool ReadSaveBit(int fieldBaseOffset, int fieldBitIndex, int characterStrideAdd = 0)
        {
            int byteOffset = fieldBaseOffset + characterStrideAdd + (fieldBitIndex / 8);
            return ReadBit(byteOffset, fieldBitIndex % 8);
        }

        public void WriteSaveBit(int fieldBaseOffset, int fieldBitIndex, bool value, int characterStrideAdd = 0)
        {
            int byteOffset = fieldBaseOffset + characterStrideAdd + (fieldBitIndex / 8);
            WriteBit(byteOffset, fieldBitIndex % 8, value);
        }

        public string ReadFfxString(int offset)
        {
            var sb = new StringBuilder();
            for (int i = offset; i < DataSize && Data[i] != 0; i++)
                sb.Append(FfxSaveStringCodec.DecodeByte(Data[i]));
            return sb.ToString();
        }

        public void WriteFfxString(int offset, string text)
        {
            int i = 0;
            for (; i < text.Length && offset + i < DataSize - 1; i++)
                Data[offset + i] = FfxSaveStringCodec.EncodeChar(text[i]);
            Data[offset + i] = 0;
        }

        public void PrepareForSave()
        {
            Data[26] = 0;
            Data[27] = 0;
            Data[25844] = 0;
            Data[25845] = 0;
            Data[16384] = 0xFE; // -2 signed
            // WHY (2026-09-14, FFX_SAVE_EXP finding F1): the header keeps a MIRROR of the
            // gil counter at payload+0x14 (== gil@15752 in 8/8 corpus samples); editing gil
            // without refreshing the mirror left a stale value in the saved file.
            WriteInt32Le(0x14, ReadInt32Le(FfxSaveItems.GilOffset, 4), 4);
            TamperTag.CopyTo(Data, 32);
            FfxSaveChecksum.ApplyToBuffer(Data);
        }
    }
}
