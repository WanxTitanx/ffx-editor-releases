using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.WeaponGear;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.WeaponGear
{
    /// <summary>
    /// RT0 byte-identity tests for weapon.bin (WeaponGear_File).
    /// Validates: preserve-write round-trip, count-grow (GrowByOne), and field edit
    /// via the WeaponGearEntry setters.
    /// </summary>
    public class WeaponGearRoundTripTests
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x16;

        /// <summary>
        /// Build a minimal weapon.bin mock: 2 entries (indices 0..1).
        /// Header: 8B prefix + MinIndex(0x08) + MaxIndex(0x0A) + EntryLength(0x0C)
        ///        + TotalDataLength(0x0E) + 4B tail. Entries 2 x 0x16.
        /// </summary>
        static byte[] BuildMockWeapon(int entryCount = 2)
        {
            byte[] blob = new byte[HeaderLength + entryCount * EntryLength];

            // Prefix bytes (8 bytes) — arbitrary placeholder.
            for (int i = 0; i < 8; i++) blob[i] = (byte)(0x50 + i);

            WriteU16(blob, 0x08, 0);                         // MinIndex
            WriteU16(blob, 0x0A, (ushort)(entryCount - 1));  // MaxIndex
            WriteU16(blob, 0x0C, EntryLength);               // EntryLength
            WriteU16(blob, 0x0E, (ushort)(entryCount * EntryLength)); // TotalDataLength

            // Tail bytes (0x10..0x13).
            for (int i = 0; i < 4; i++) blob[0x10 + i] = (byte)(0xAA + i);

            // Entries with distinct field values.
            for (int e = 0; e < entryCount; e++)
            {
                int off = HeaderLength + e * EntryLength;
                WriteU16(blob, off + 0x00, 0);                 // nameId
                blob[off + 0x02] = 1;                          // exists
                blob[off + 0x03] = (byte)(0x01 + e);           // flags
                blob[off + 0x04] = (byte)e;                    // character
                blob[off + 0x05] = 0;                          // isArmor
                WriteU16(blob, off + 0x06, 0);                 // saveFields
                blob[off + 0x08] = 1;                          // formula
                blob[off + 0x09] = (byte)(16 + e);             // power
                blob[off + 0x0A] = (byte)(3 + e);              // crit
                blob[off + 0x0B] = 1;                          // slotCount
                WriteU16(blob, off + 0x0C, 0);                 // modelId
                WriteU16(blob, off + 0x0E, 0x00FF);            // ability1
                WriteU16(blob, off + 0x10, 0x00FF);            // ability2
                WriteU16(blob, off + 0x12, 0x00FF);            // ability3
                WriteU16(blob, off + 0x14, 0x00FF);            // ability4
            }

            return blob;
        }

        static void WriteU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        static WeaponGearEntry EntryAt(WeaponGearTable table, int index)
        {
            return table.EntriesByIndex[index];
        }

        [Fact]
        public void ReadWrite_RoundTrip_IsByteIdentical()
        {
            byte[] mock = BuildMockWeapon(2);
            var table = WeaponGear_File.Read(mock);
            byte[] written = WeaponGear_File.Write(table);

            Assert.Equal(mock, written);
        }

        [Fact]
        public void GrowByOne_AppendsEntry_AndUpdatesHeader()
        {
            byte[] mock = BuildMockWeapon(2);
            var table = WeaponGear_File.Read(mock);

            var newEntry = new WeaponGearEntry
            {
                Index = 2,                   // MaxIndex(1) + 1
                AlwaysZero1 = 0,
                ZeroOrOne = 1,
                Flags = 0x04,
                Character = 6,               // Rikku
                IsArmor = 1,                 // Armor
                AlwaysZero2 = 0,
                Formula = 1,
                Power = 40,
                Crit = 5,
                SlotCount = 2,
                ModelId = 0,
                Ability1 = 0x00FF,
                Ability2 = 0x0019,           // Triple AP
                Ability3 = 0x00FF,
                Ability4 = 0x00FF
            };

            byte[] grown = WeaponGear_File.GrowByOne(table, newEntry);

            // Length: + one entry.
            Assert.Equal(mock.Length + EntryLength, grown.Length);

            var grownTable = WeaponGear_File.Read(grown);
            Assert.Equal(3, grownTable.EntryCount);
            Assert.Equal(2, grownTable.MaxIndex);
            Assert.Equal(0, grownTable.MinIndex);
            Assert.Equal(3 * EntryLength, grownTable.Header.TotalDataLength);

            // Verify the appended entry's bytes.
            var parsed = EntryAt(grownTable, 2);
            Assert.Equal(6, parsed.Character);
            Assert.Equal(1, parsed.IsArmor);
            Assert.Equal(40, parsed.Power);
            Assert.Equal(5, parsed.Crit);
            Assert.Equal(2, parsed.SlotCount);
            Assert.Equal(0x0019, parsed.Ability2);
            Assert.Equal(0x04, parsed.Flags);

            // Original entries untouched.
            Assert.Equal(0, EntryAt(grownTable, 0).Character);
            Assert.Equal(1, EntryAt(grownTable, 1).Character);
        }

        [Fact]
        public void GrowByOne_RejectsIndexNotMaxPlusOne()
        {
            byte[] mock = BuildMockWeapon(2);
            var table = WeaponGear_File.Read(mock);

            var bad = new WeaponGearEntry { Index = 5 };
            Assert.Throws<InvalidOperationException>(() => WeaponGear_File.GrowByOne(table, bad));
        }

        [Fact]
        public void EditEntry_FieldWrites_ReflectInBytes()
        {
            byte[] mock = BuildMockWeapon(2);
            var table = WeaponGear_File.Read(mock);

            // Edit entry 0: power, modelId, ability1.
            var e0 = EntryAt(table, 0);
            e0.Power = 99;
            e0.ModelId = 0x4067;
            e0.Ability1 = 0x0019;  // Triple AP

            byte[] written = WeaponGear_File.Write(table);

            // Power at offset 0x09.
            Assert.Equal(99, written[HeaderLength + 0x09]);
            // ModelId (LE) at 0x0C.
            Assert.Equal(0x67, written[HeaderLength + 0x0C]);
            Assert.Equal(0x40, written[HeaderLength + 0x0D]);
            // Ability1 (LE) at 0x0E.
            Assert.Equal(0x19, written[HeaderLength + 0x0E]);
            Assert.Equal(0x00, written[HeaderLength + 0x0F]);

            // Entry 1 untouched (power = 16 + 1 = 17).
            Assert.Equal(17, written[HeaderLength + EntryLength + 0x09]);
        }
    }
}