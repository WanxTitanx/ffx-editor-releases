using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Treasure;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Byte-identical round-trip tests for the takara.bin (Treasure) table.
    /// The fixture encodes the known HD table shape: a 0x14-byte header followed
    /// by 498 entries of 4 bytes each (total data 0x07C8). It is deliberately
    /// versioned here rather than read from a developer's local game installation
    /// so the round-trip is reproducible in CI.
    /// </summary>
    public class TreasureRoundTripTests
    {
        const int HeaderLength = 0x14;
        const int ExpectedEntryCount = 0x01F2; // 498 = 0x01F1 max + 1
        const int TotalDataLength = 0x07C8;
        const int ExpectedFileLength = HeaderLength + TotalDataLength; // 0x07DC (2012)

        [Theory]
        [InlineData(new byte[] { 0x12, 0x34, 0x78, 0x56 })] // Kind=0x12 Qty=0x34 Type=0x5678 (LE)
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 })] // all-zero
        [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })] // all-max
        [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x00 })] // Type low byte only
        [InlineData(new byte[] { 0x0A, 0x0B, 0xCC, 0x0D })] // mixed endian coverage
        public void WriteSingle_RoundTripsKnownEntryBytes(byte[] knownBytes)
        {
            Treasure_Entry entry = Treasure_Entry.ReadSingle(knownBytes);

            byte[] written = entry.WriteSingle();

            Assert.Equal(knownBytes, written);
        }

        [Fact]
        public void ReadSingle_RejectsWrongLength()
        {
            Assert.Throws<ArgumentException>(
                () => Treasure_Entry.ReadSingle(new byte[] { 0x01, 0x02, 0x03 }));
        }

        [Fact]
        public void ReadAll_WriteAll_RoundTrip_IsByteIdentical()
        {
            byte[] known = BuildKnownTakaraBin();

            List<Treasure_Entry> entries = Treasure_File.ReadAll(known);
            Assert.Equal(ExpectedEntryCount, entries.Count);

            // Prove the reader decoded fields correctly (not just self-consistency).
            Assert.Equal((byte)0xA0, entries[0].Kind);
            Assert.Equal((byte)0x01, entries[0].Quantity);
            Assert.Equal((ushort)0x8000, entries[0].ItemId);
            Assert.Equal((byte)0xA1, entries[1].Kind);
            Assert.Equal((byte)0x06, entries[1].Quantity);
            Assert.Equal((ushort)0x8003, entries[1].ItemId);

            byte[] written = Treasure_File.WriteAll(entries);

            Assert.Equal(ExpectedFileLength, written.Length);
            Assert.Equal(known, written);
        }

        [Fact]
        public void WriteAll_ProducesExactKnownBytes_ForKnownInput()
        {
            var entries = new Treasure_Entry[ExpectedEntryCount];
            entries[0] = new Treasure_Entry { Kind = 0x12, Quantity = 0x34, ItemId = 0x5678 };
            for (int i = 1; i < entries.Length; i++)
                entries[i] = new Treasure_Entry { Kind = 0x00, Quantity = 0x00, ItemId = 0x0000 };

            byte[] written = Treasure_File.WriteAll(entries);

            Assert.Equal(ExpectedFileLength, written.Length);

            // Header: prefix 01 00 00 00 00 00 00 00, then LE u16s and tail 14 00 00 00.
            Assert.Equal(0x01, written[0x00]);
            Assert.Equal(0x00, written[0x08]); // MinIndex low
            Assert.Equal(0x00, written[0x09]); // MinIndex high
            Assert.Equal(0xF1, written[0x0A]); // MaxIndex low
            Assert.Equal(0x01, written[0x0B]); // MaxIndex high
            Assert.Equal(0x04, written[0x0C]); // EntryLength low
            Assert.Equal(0x00, written[0x0D]); // EntryLength high
            Assert.Equal(0xC8, written[0x0E]); // TotalDataLength low
            Assert.Equal(0x07, written[0x0F]); // TotalDataLength high
            Assert.Equal(0x14, written[0x10]); // Tail low

            // First entry at 0x14: Kind=0x12 Qty=0x34 Type=0x5678 (LE).
            Assert.Equal(0x12, written[0x14]);
            Assert.Equal(0x34, written[0x15]);
            Assert.Equal(0x78, written[0x16]);
            Assert.Equal(0x56, written[0x17]);

            // Second entry (all zeros) starts at 0x18; last byte of last entry at EOF-1.
            Assert.Equal(0x00, written[0x18]);
            Assert.Equal(0x00, written[ExpectedFileLength - 1]);
        }

        [Fact]
        public void WriteAll_RejectsUnexpectedEntryCount()
        {
            var entries = new Treasure_Entry[10];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new Treasure_Entry();

            Assert.Throws<InvalidOperationException>(() => Treasure_File.WriteAll(entries));
        }

        static byte[] BuildKnownTakaraBin()
        {
            byte[] bytes = new byte[ExpectedFileLength];

            // Prefix: 01 00 00 00 00 00 00 00
            bytes[0x00] = 0x01;
            WriteUInt16(bytes, 0x08, 0x0000); // MinIndex
            WriteUInt16(bytes, 0x0A, 0x01F1); // MaxIndex
            WriteUInt16(bytes, 0x0C, 0x0004); // EntryLength
            WriteUInt16(bytes, 0x0E, (ushort)TotalDataLength);
            WriteUInt32(bytes, 0x10, 0x00000014); // TailBytes = 14 00 00 00

            for (int i = 0; i < ExpectedEntryCount; i++)
            {
                int offset = HeaderLength + (i * 4);
                bytes[offset + 0] = (byte)(0xA0 + i);
                bytes[offset + 1] = (byte)((i * 5) + 1);
                WriteUInt16(bytes, offset + 2, (ushort)(0x8000 + (i * 3)));
            }

            return bytes;
        }

        static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}

