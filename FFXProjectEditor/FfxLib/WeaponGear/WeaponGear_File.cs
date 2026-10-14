using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.WeaponGear
{
    /// <summary>
    /// Parser + writer for weapon.bin (the master gear catalog, 153 entries, 0x16 bytes each).
    /// weapon.bin stores every weapon and armor template in the game. It is the canonical source
    /// of truth for existing gear — shop_arms.bin and buki_get.bin are subsets derived from it.
    ///
    /// Entry layout (0x16 = 22 bytes), VERIFIED from hexpat + cross-reference + RE:
    ///   0x00: u16 nameId          — always 0x0000 (catalog: name→model via w_name.bin)
    ///   0x02: u8  exists           — always 0x01 (catalog: every entry exists)
    ///   0x03: u8  flags            — bitfield: +1=summon, +2=hidden, +4=no_kaizou, +8=brotherhood
    ///   0x04: u8  character        — 0=Tidus..6=Rikku, 8=Aeons
    ///   0x05: u8  isArmor          — 0=Weapon, 1=Armor
    ///   0x06: u16 saveFields       — always 0x0000 (CharacterEquipped+Unk7; save-time only)
    ///   0x08: u8  formula          — DamageFormula enum (0-23)
    ///   0x09: u8  power            — attack power
    ///   0x0A: u8  crit             — critical hit bonus
    ///   0x0B: u8  slotCount        — ability slots (1-4)
    ///   0x0C: u8  modelId_lo       — Model_id low byte (split u16 LE, matches save-file format)
    ///   0x0D: u8  modelId_hi       — Model_id high byte; (lo | hi<<8) = 0xNNNN
    ///                                 Only populated for characters without w_name entry
    ///                                 (Seymour 0x4066/0x4067, Yojimbo 0x3017). 150/153 = 0.
    ///   0x0E: u16 ability1         — first auto-ability (0x00FF = empty)
    ///   0x10: u16 ability2         — second auto-ability
    ///   0x12: u16 ability3         — third auto-ability
    ///   0x14: u16 ability4         — fourth auto-ability
    ///
    /// RE CONFIRMATION (3 agents, 2026-07-01): weapon.bin has EXACT same byte layout as
    /// EquipmentStruct (22 bytes). The only difference is weapon.bin stores Model_id as split
    /// bytes (0x0C+0x0D, LE) instead of packed u16. Same field, different serialization.
    /// </summary>
    internal static class WeaponGear_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x16;

        public static WeaponGearTable Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            Dictionary<int, WeaponGearEntry> entriesByIndex = new(header.EntryCount);

            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int entryOffset = HeaderLength + (i * header.EntryLength);
                WeaponGearEntry entry = ReadEntry(bytes, entryOffset, index);
                entriesByIndex[index] = entry;
            }

            return new WeaponGearTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                EntriesByIndex = entriesByIndex
            };
        }

        static WeaponGearEntry ReadEntry(byte[] bytes, int entryOffset, int index)
        {
            if (entryOffset < 0 || entryOffset + EntryLength > bytes.Length)
                throw new InvalidDataException($"weapon.bin entry at offset 0x{entryOffset:X4} extends past EOF.");

            return new WeaponGearEntry
            {
                Index = index,
                AlwaysZero1 = ReadUInt16(bytes, entryOffset + 0x00),
                ZeroOrOne = bytes[entryOffset + 0x02],
                Flags = bytes[entryOffset + 0x03],
                Character = bytes[entryOffset + 0x04],
                IsArmor = bytes[entryOffset + 0x05],
                AlwaysZero2 = ReadUInt16(bytes, entryOffset + 0x06),
                Formula = bytes[entryOffset + 0x08],
                Power = bytes[entryOffset + 0x09],
                Crit = bytes[entryOffset + 0x0A],
                SlotCount = bytes[entryOffset + 0x0B],
                ModelId = ReadUInt16(bytes, entryOffset + 0x0C),
                Ability1 = ReadUInt16(bytes, entryOffset + 0x0E),
                Ability2 = ReadUInt16(bytes, entryOffset + 0x10),
                Ability3 = ReadUInt16(bytes, entryOffset + 0x12),
                Ability4 = ReadUInt16(bytes, entryOffset + 0x14)
            };
        }

        // ── Preserve-only writer: clone + re-stamp scalars ──────────────────────

        /// <summary>
        /// Preserve-only write. Clones original bytes and re-stamps known scalar fields.
        /// Preserves unknown/padding bytes. Count must match — use GrowByOne for new entries.
        /// </summary>
        public static byte[] Write(WeaponGearTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);
            ValidateWriteShape(table.Header, table.EntriesByIndex.Count);

            byte[] output = table.OriginalBytes.ToArray();

            for (int i = 0; i < table.Header.EntryCount; i++)
            {
                int index = table.Header.MinIndex + i;
                if (!table.EntriesByIndex.TryGetValue(index, out WeaponGearEntry? entry))
                    throw new InvalidOperationException($"weapon.bin is missing entry {index} required for a faithful write.");

                int entryOffset = HeaderLength + (i * table.Header.EntryLength);
                StampEntry(output, entryOffset, entry);
            }

            return output;
        }

        static void StampEntry(byte[] output, int entryOffset, WeaponGearEntry entry)
        {
            if (entryOffset < 0 || entryOffset + EntryLength > output.Length)
                throw new InvalidOperationException($"weapon.bin entry at offset 0x{entryOffset:X4} extends past EOF on write.");

            WriteUInt16(output, entryOffset + 0x00, entry.AlwaysZero1);
            output[entryOffset + 0x02] = entry.ZeroOrOne;
            output[entryOffset + 0x03] = entry.Flags;
            output[entryOffset + 0x04] = entry.Character;
            output[entryOffset + 0x05] = entry.IsArmor;
            WriteUInt16(output, entryOffset + 0x06, entry.AlwaysZero2);
            output[entryOffset + 0x08] = entry.Formula;
            output[entryOffset + 0x09] = entry.Power;
            output[entryOffset + 0x0A] = entry.Crit;
            output[entryOffset + 0x0B] = entry.SlotCount;
            WriteUInt16(output, entryOffset + 0x0C, entry.ModelId);
            WriteUInt16(output, entryOffset + 0x0E, entry.Ability1);
            WriteUInt16(output, entryOffset + 0x10, entry.Ability2);
            WriteUInt16(output, entryOffset + 0x12, entry.Ability3);
            WriteUInt16(output, entryOffset + 0x14, entry.Ability4);
        }

        // ── Count-grow: append a new entry ────────────────────────────────────

        /// <summary>
        /// Appends a new entry at MaxIndex+1. Clones old data, inserts new raw entry,
        /// updates MaxIndex@0x0A and TotalDataLength@0x0E. New entry index must be header.MaxIndex + 1.
        /// </summary>
        public static byte[] GrowByOne(WeaponGearTable table, WeaponGearEntry newEntry)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(newEntry);

            ValidateHeader(table.Header, table.OriginalBytes.Length);

            int expectedNewIndex = table.Header.MaxIndex + 1;
            if (newEntry.Index != expectedNewIndex)
                throw new InvalidOperationException($"New weapon.bin entry index must be {expectedNewIndex}; got {newEntry.Index}.");

            int oldDataEnd = HeaderLength + table.Header.TotalDataLength;
            if (oldDataEnd > table.OriginalBytes.Length)
                throw new InvalidOperationException("Cannot grow weapon.bin because the source data section extends past EOF.");

            byte[] output = new byte[table.OriginalBytes.Length + EntryLength];
            Array.Copy(table.OriginalBytes, 0, output, 0, oldDataEnd);

            byte[] raw = BuildEntryRaw(newEntry);
            Array.Copy(raw, 0, output, oldDataEnd, EntryLength);

            Array.Copy(
                table.OriginalBytes,
                oldDataEnd,
                output,
                oldDataEnd + EntryLength,
                table.OriginalBytes.Length - oldDataEnd);

            int newEntryCount = table.Header.EntryCount + 1;
            WriteUInt16(output, 0x0A, checked((ushort)(table.Header.MinIndex + newEntryCount - 1)));
            WriteUInt16(output, 0x0E, checked((ushort)(newEntryCount * EntryLength)));
            return output;
        }

        static byte[] BuildEntryRaw(WeaponGearEntry entry)
        {
            byte[] raw = new byte[EntryLength];
            WriteUInt16(raw, 0x00, entry.AlwaysZero1);
            raw[0x02] = entry.ZeroOrOne;
            raw[0x03] = entry.Flags;
            raw[0x04] = entry.Character;
            raw[0x05] = entry.IsArmor;
            WriteUInt16(raw, 0x06, entry.AlwaysZero2);
            raw[0x08] = entry.Formula;
            raw[0x09] = entry.Power;
            raw[0x0A] = entry.Crit;
            raw[0x0B] = entry.SlotCount;
            WriteUInt16(raw, 0x0C, entry.ModelId);
            WriteUInt16(raw, 0x0E, entry.Ability1);
            WriteUInt16(raw, 0x10, entry.Ability2);
            WriteUInt16(raw, 0x12, entry.Ability3);
            WriteUInt16(raw, 0x14, entry.Ability4);
            return raw;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"weapon.bin uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("weapon.bin declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"weapon.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}. Use GrowByOne for new entries.");
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    // ── Container ─────────────────────────────────────────────────────────────

    internal sealed class WeaponGearTable
    {
        public byte[] OriginalBytes { get; set; } = [];
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyDictionary<int, WeaponGearEntry> EntriesByIndex { get; init; }

        public int EntryCount => EntriesByIndex.Count;
        public int MinIndex => Header.MinIndex;
        public int MaxIndex => Header.MaxIndex;
    }

    // ── Entry (read from disk, mutable for editing) ───────────────────────────

    internal sealed class WeaponGearEntry
    {
        public int Index { get; set; }
        public ushort AlwaysZero1 { get; set; }
        public byte ZeroOrOne { get; set; }
        public byte Flags { get; set; }
        public byte Character { get; set; }
        public byte IsArmor { get; set; }
        public ushort AlwaysZero2 { get; set; }
        public byte Formula { get; set; }
        public byte Power { get; set; }
        public byte Crit { get; set; }
        public byte SlotCount { get; set; }
        public ushort ModelId { get; set; }
        public ushort Ability1 { get; set; }
        public ushort Ability2 { get; set; }
        public ushort Ability3 { get; set; }
        public ushort Ability4 { get; set; }

        // For new entries: copy fields from a template entry
        public static WeaponGearEntry CloneFrom(WeaponGearEntry source, int newIndex)
        {
            return new WeaponGearEntry
            {
                Index = newIndex,
                AlwaysZero1 = source.AlwaysZero1,
                ZeroOrOne = source.ZeroOrOne,
                Flags = source.Flags,
                Character = source.Character,
                IsArmor = source.IsArmor,
                AlwaysZero2 = source.AlwaysZero2,
                Formula = source.Formula,
                Power = source.Power,
                Crit = source.Crit,
                SlotCount = source.SlotCount,
                ModelId = source.ModelId,
                Ability1 = source.Ability1,
                Ability2 = source.Ability2,
                Ability3 = source.Ability3,
                Ability4 = source.Ability4
            };
        }
    }
}
