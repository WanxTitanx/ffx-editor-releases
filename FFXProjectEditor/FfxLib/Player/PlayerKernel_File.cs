using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Player
{
    internal static class PlayerKernel_File
    {
        const int HeaderLength = 0x14;
        const int SaveEntryLength = 0x94;
        const int RomEntryLength = 0x2C;

        public static PlayerSaveTable ReadSave(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length, SaveEntryLength, "ply_save.bin");

            List<PlayerSaveEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();

                entries.Add(new PlayerSaveEntry
                {
                    Index = index,
                    Label = ResolveSlotLabel(index),
                    RawBytes = raw,
                    BaseHp = ReadInt32(raw, 0x04),
                    BaseMp = ReadInt32(raw, 0x08),
                    BaseStrength = ReadByte(raw, 0x0C),
                    BaseDefense = ReadByte(raw, 0x0D),
                    BaseMagic = ReadByte(raw, 0x0E),
                    BaseMagicDefense = ReadByte(raw, 0x0F),
                    BaseAgility = ReadByte(raw, 0x10),
                    BaseLuck = ReadByte(raw, 0x11),
                    BaseEvasion = ReadByte(raw, 0x12),
                    BaseAccuracy = ReadByte(raw, 0x13),
                    CurrentAp = ReadInt32(raw, 0x18),
                    CurrentHp = ReadInt32(raw, 0x1C),
                    CurrentMp = ReadInt32(raw, 0x20),
                    MaxHp = ReadInt32(raw, 0x24),
                    MaxMp = ReadInt32(raw, 0x28),
                    EquippedWeaponIndex = ReadByte(raw, 0x2D),
                    EquippedArmorIndex = ReadByte(raw, 0x2E),
                    Strength = ReadByte(raw, 0x2F),
                    Defense = ReadByte(raw, 0x30),
                    Magic = ReadByte(raw, 0x31),
                    MagicDefense = ReadByte(raw, 0x32),
                    Agility = ReadByte(raw, 0x33),
                    Luck = ReadByte(raw, 0x34),
                    Evasion = ReadByte(raw, 0x35),
                    Accuracy = ReadByte(raw, 0x36),
                    PoisonDamagePercent = ReadByte(raw, 0x37),
                    OverdriveMode = ReadByte(raw, 0x38),
                    OverdriveCurrent = ReadByte(raw, 0x39),
                    OverdriveMax = ReadByte(raw, 0x3A),
                    SphereLevelsAvailable = ReadByte(raw, 0x3B),
                    SphereLevelsUsed = ReadByte(raw, 0x3C),
                    EncounterCount = ReadInt32(raw, 0x50),
                    KillCount = ReadInt32(raw, 0x54)
                });
            }

            return new PlayerSaveTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static PlayerRomTable ReadRom(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length, RomEntryLength, "ply_rom.bin");

            List<PlayerRomEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();

                entries.Add(new PlayerRomEntry
                {
                    Index = index,
                    Label = ResolveSlotLabel(index),
                    RawBytes = raw,
                    GenreByte = ReadByte(raw, 0x10),
                    ApReqCoefficientA = ReadByte(raw, 0x11),
                    ApReqCoefficientB = ReadByte(raw, 0x12),
                    ApReqCoefficientC = ReadByte(raw, 0x13),
                    ApReqMax = ReadInt32(raw, 0x14),
                    HpCoefficientA = ReadByte(raw, 0x18),
                    HpCoefficientB = ReadByte(raw, 0x19),
                    MpCoefficientA = ReadByte(raw, 0x1A),
                    MpCoefficientB = ReadByte(raw, 0x1B),
                    StrengthCoefficientA = ReadByte(raw, 0x1C),
                    StrengthCoefficientB = ReadByte(raw, 0x1D),
                    DefenseCoefficientA = ReadByte(raw, 0x1E),
                    DefenseCoefficientB = ReadByte(raw, 0x1F),
                    MagicCoefficientA = ReadByte(raw, 0x20),
                    MagicCoefficientB = ReadByte(raw, 0x21),
                    MagicDefenseCoefficientA = ReadByte(raw, 0x22),
                    MagicDefenseCoefficientB = ReadByte(raw, 0x23),
                    AgilityCoefficientA = ReadByte(raw, 0x24),
                    AgilityCoefficientB = ReadByte(raw, 0x25),
                    EvasionCoefficientA = ReadByte(raw, 0x26),
                    EvasionCoefficientB = ReadByte(raw, 0x27),
                    AccuracyCoefficientA = ReadByte(raw, 0x28),
                    AccuracyCoefficientB = ReadByte(raw, 0x29),
                    TailFlags = ReadUInt16(raw, 0x2A)
                });
            }

            return new PlayerRomTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static byte[] WriteSave(PlayerSaveTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            byte[] output = table.OriginalBytes.ToArray();
            ValidateWriteShape(table.Header, table.Entries.Count, SaveEntryLength, "ply_save.bin");

            foreach (PlayerSaveEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                byte[] raw = entry.RawBytes.ToArray();
                EnsureLength(ref raw, SaveEntryLength);

                WriteInt32(raw, 0x04, entry.BaseHp);
                WriteInt32(raw, 0x08, entry.BaseMp);
                WriteByte(raw, 0x0C, entry.BaseStrength);
                WriteByte(raw, 0x0D, entry.BaseDefense);
                WriteByte(raw, 0x0E, entry.BaseMagic);
                WriteByte(raw, 0x0F, entry.BaseMagicDefense);
                WriteByte(raw, 0x10, entry.BaseAgility);
                WriteByte(raw, 0x11, entry.BaseLuck);
                WriteByte(raw, 0x12, entry.BaseEvasion);
                WriteByte(raw, 0x13, entry.BaseAccuracy);
                WriteInt32(raw, 0x18, entry.CurrentAp);
                WriteInt32(raw, 0x1C, entry.CurrentHp);
                WriteInt32(raw, 0x20, entry.CurrentMp);
                WriteInt32(raw, 0x24, entry.MaxHp);
                WriteInt32(raw, 0x28, entry.MaxMp);
                WriteByte(raw, 0x2D, entry.EquippedWeaponIndex);
                WriteByte(raw, 0x2E, entry.EquippedArmorIndex);
                WriteByte(raw, 0x2F, entry.Strength);
                WriteByte(raw, 0x30, entry.Defense);
                WriteByte(raw, 0x31, entry.Magic);
                WriteByte(raw, 0x32, entry.MagicDefense);
                WriteByte(raw, 0x33, entry.Agility);
                WriteByte(raw, 0x34, entry.Luck);
                WriteByte(raw, 0x35, entry.Evasion);
                WriteByte(raw, 0x36, entry.Accuracy);
                WriteByte(raw, 0x37, entry.PoisonDamagePercent);
                WriteByte(raw, 0x38, entry.OverdriveMode);
                WriteByte(raw, 0x39, entry.OverdriveCurrent);
                WriteByte(raw, 0x3A, entry.OverdriveMax);
                WriteByte(raw, 0x3B, entry.SphereLevelsAvailable);
                WriteByte(raw, 0x3C, entry.SphereLevelsUsed);
                WriteInt32(raw, 0x50, entry.EncounterCount);
                WriteInt32(raw, 0x54, entry.KillCount);

                CopyEntryBytes(output, table.Header, entry.Index, raw);
            }

            return output;
        }

        public static byte[] WriteRom(PlayerRomTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            byte[] output = table.OriginalBytes.ToArray();
            ValidateWriteShape(table.Header, table.Entries.Count, RomEntryLength, "ply_rom.bin");

            foreach (PlayerRomEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                byte[] raw = entry.RawBytes.ToArray();
                EnsureLength(ref raw, RomEntryLength);

                WriteByte(raw, 0x10, entry.GenreByte);
                WriteByte(raw, 0x11, entry.ApReqCoefficientA);
                WriteByte(raw, 0x12, entry.ApReqCoefficientB);
                WriteByte(raw, 0x13, entry.ApReqCoefficientC);
                WriteInt32(raw, 0x14, entry.ApReqMax);
                WriteByte(raw, 0x18, entry.HpCoefficientA);
                WriteByte(raw, 0x19, entry.HpCoefficientB);
                WriteByte(raw, 0x1A, entry.MpCoefficientA);
                WriteByte(raw, 0x1B, entry.MpCoefficientB);
                WriteByte(raw, 0x1C, entry.StrengthCoefficientA);
                WriteByte(raw, 0x1D, entry.StrengthCoefficientB);
                WriteByte(raw, 0x1E, entry.DefenseCoefficientA);
                WriteByte(raw, 0x1F, entry.DefenseCoefficientB);
                WriteByte(raw, 0x20, entry.MagicCoefficientA);
                WriteByte(raw, 0x21, entry.MagicCoefficientB);
                WriteByte(raw, 0x22, entry.MagicDefenseCoefficientA);
                WriteByte(raw, 0x23, entry.MagicDefenseCoefficientB);
                WriteByte(raw, 0x24, entry.AgilityCoefficientA);
                WriteByte(raw, 0x25, entry.AgilityCoefficientB);
                WriteByte(raw, 0x26, entry.EvasionCoefficientA);
                WriteByte(raw, 0x27, entry.EvasionCoefficientB);
                WriteByte(raw, 0x28, entry.AccuracyCoefficientA);
                WriteByte(raw, 0x29, entry.AccuracyCoefficientB);
                WriteUInt16(raw, 0x2A, entry.TailFlags);

                CopyEntryBytes(output, table.Header, entry.Index, raw);
            }

            return output;
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength, int expectedEntryLength, string label)
        {
            if (header.EntryLength != expectedEntryLength)
                throw new InvalidDataException($"{label} uses unexpected entry length {header.EntryLength}; expected 0x{expectedEntryLength:X2}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException($"{label} declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount, int expectedEntryLength, string label)
        {
            if (header.EntryLength != expectedEntryLength)
                throw new InvalidOperationException($"{label} writer expected entry length 0x{expectedEntryLength:X2}, found 0x{header.EntryLength:X2}.");

            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"{label} writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static void CopyEntryBytes(byte[] output, IndexedFixedTableHeader header, int index, byte[] raw)
        {
            int relativeIndex = index - header.MinIndex;
            if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                throw new InvalidOperationException($"Entry index {index} is outside the fixed table range.");

            int offset = HeaderLength + (relativeIndex * header.EntryLength);
            Array.Copy(raw, 0, output, offset, Math.Min(header.EntryLength, raw.Length));
        }

        static string ResolveSlotLabel(int index)
        {
            if (Enum.IsDefined(typeof(Character_Enum), (sbyte)index))
                return ((Character_Enum)(sbyte)index).ToString();

            return $"Unknown Slot {index:D2}";
        }

        static int ReadInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return 0;

            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static int ReadByte(byte[] bytes, int offset)
        {
            if (offset < 0 || offset >= bytes.Length)
                return 0;

            return bytes[offset];
        }

        static void EnsureLength(ref byte[] bytes, int expectedLength)
        {
            if (bytes.Length >= expectedLength)
                return;

            Array.Resize(ref bytes, expectedLength);
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
            bytes[offset + 2] = unchecked((byte)((value >> 16) & 0xFF));
            bytes[offset + 3] = unchecked((byte)((value >> 24) & 0xFF));
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
        }

        static void WriteByte(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset >= bytes.Length)
                return;

            bytes[offset] = unchecked((byte)value);
        }
    }

    internal sealed class PlayerSaveTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<PlayerSaveEntry> Entries { get; init; }
    }

    internal sealed class PlayerRomTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<PlayerRomEntry> Entries { get; init; }
    }

    internal sealed class PlayerSaveEntry
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required byte[] RawBytes { get; init; }

        public int BaseHp { get; set; }
        public int BaseMp { get; set; }
        public int BaseStrength { get; set; }
        public int BaseDefense { get; set; }
        public int BaseMagic { get; set; }
        public int BaseMagicDefense { get; set; }
        public int BaseAgility { get; set; }
        public int BaseLuck { get; set; }
        public int BaseEvasion { get; set; }
        public int BaseAccuracy { get; set; }
        public int CurrentAp { get; set; }
        public int CurrentHp { get; set; }
        public int CurrentMp { get; set; }
        public int MaxHp { get; set; }
        public int MaxMp { get; set; }
        public int EquippedWeaponIndex { get; set; }
        public int EquippedArmorIndex { get; set; }
        public int Strength { get; set; }
        public int Defense { get; set; }
        public int Magic { get; set; }
        public int MagicDefense { get; set; }
        public int Agility { get; set; }
        public int Luck { get; set; }
        public int Evasion { get; set; }
        public int Accuracy { get; set; }
        public int PoisonDamagePercent { get; set; }
        public int OverdriveMode { get; set; }
        public int OverdriveCurrent { get; set; }
        public int OverdriveMax { get; set; }
        public int SphereLevelsAvailable { get; set; }
        public int SphereLevelsUsed { get; set; }
        public int EncounterCount { get; set; }
        public int KillCount { get; set; }
    }

    internal sealed class PlayerRomEntry
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required byte[] RawBytes { get; init; }

        public int GenreByte { get; set; }
        public int ApReqCoefficientA { get; set; }
        public int ApReqCoefficientB { get; set; }
        public int ApReqCoefficientC { get; set; }
        public int ApReqMax { get; set; }
        public int HpCoefficientA { get; set; }
        public int HpCoefficientB { get; set; }
        public int MpCoefficientA { get; set; }
        public int MpCoefficientB { get; set; }
        public int StrengthCoefficientA { get; set; }
        public int StrengthCoefficientB { get; set; }
        public int DefenseCoefficientA { get; set; }
        public int DefenseCoefficientB { get; set; }
        public int MagicCoefficientA { get; set; }
        public int MagicCoefficientB { get; set; }
        public int MagicDefenseCoefficientA { get; set; }
        public int MagicDefenseCoefficientB { get; set; }
        public int AgilityCoefficientA { get; set; }
        public int AgilityCoefficientB { get; set; }
        public int EvasionCoefficientA { get; set; }
        public int EvasionCoefficientB { get; set; }
        public int AccuracyCoefficientA { get; set; }
        public int AccuracyCoefficientB { get; set; }
        public ushort TailFlags { get; set; }
    }
}
