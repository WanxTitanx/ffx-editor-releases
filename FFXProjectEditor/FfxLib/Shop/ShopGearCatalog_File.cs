using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopGearCatalog_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x16;

        public static ShopGearCatalog Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            Dictionary<int, ShopGearCatalogEntry> entriesByIndex = new(header.EntryCount);

            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int entryOffset = HeaderLength + (i * header.EntryLength);
                EquipmentStruct equipment = ReadEquipment(bytes, entryOffset);
                entriesByIndex[index] = new ShopGearCatalogEntry
                {
                    Index = index,
                    Equipment = equipment,
                    DisplayLabel = BuildDisplayLabel(index, equipment),
                    Summary = BuildSummary(equipment),
                    DetailSummary = BuildDetailSummary(equipment)
                };
            }

            return new ShopGearCatalog
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                EntriesByIndex = entriesByIndex
            };
        }

        // Preserve-only writer: clone the original bytes and re-stamp ONLY the clearly-fixed
        // scalar fields of each EquipmentStruct in place. The header and any bytes outside the
        // re-stamped scalars (including the lossy "Exists" presence byte, which Read collapses
        // to a bool) stay preserved from the original entry shape -> a no-edit Read->Write save
        // is byte-identical by construction.
        public static byte[] Write(ShopGearCatalog table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);
            ValidateWriteShape(table.Header, table.EntriesByIndex.Count);

            byte[] output = table.OriginalBytes.ToArray();

            for (int i = 0; i < table.Header.EntryCount; i++)
            {
                int index = table.Header.MinIndex + i;
                if (!table.EntriesByIndex.TryGetValue(index, out ShopGearCatalogEntry? entry))
                    throw new InvalidOperationException($"shop_arms.bin is missing entry {index} required for a faithful write.");

                int entryOffset = HeaderLength + (i * table.Header.EntryLength);
                StampEquipment(output, entryOffset, entry.Equipment);
            }

            return output;
        }

        static void StampEquipment(byte[] output, int entryOffset, EquipmentStruct equipment)
        {
            if (entryOffset < 0 || entryOffset + EntryLength > output.Length)
                throw new InvalidOperationException($"shop_arms.bin entry at offset 0x{entryOffset:X4} extends past EOF on write.");

            // Lossless scalar re-stamp. Offset 0x02 (Exists/presence byte) is intentionally
            // preserved from the clone because Read collapses it to a bool (value != 0),
            // which would not round-trip a non-0/1 raw byte.
            WriteUInt16(output, entryOffset + 0x00, equipment.Name_id);
            output[entryOffset + 0x03] = (byte)equipment.Flags;
            output[entryOffset + 0x04] = (byte)(sbyte)equipment.Character;
            output[entryOffset + 0x05] = (byte)equipment.Type;
            output[entryOffset + 0x06] = (byte)(sbyte)equipment.CharacterEquipped;
            output[entryOffset + 0x07] = equipment.Unk7;
            output[entryOffset + 0x08] = (byte)equipment.Dmg_formula;
            output[entryOffset + 0x09] = equipment.Power;
            output[entryOffset + 0x0A] = equipment.Crit_bonus;
            output[entryOffset + 0x0B] = equipment.Slot_count;
            WriteUInt16(output, entryOffset + 0x0C, equipment.Model_id);
            WriteUInt16(output, entryOffset + 0x0E, equipment.Ability1);
            WriteUInt16(output, entryOffset + 0x10, equipment.Ability2);
            WriteUInt16(output, entryOffset + 0x12, equipment.Ability3);
            WriteUInt16(output, entryOffset + 0x14, equipment.Ability4);
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"shop_arms.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"shop_arms.bin uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("shop_arms.bin declares a data section that extends past EOF.");
        }

        static EquipmentStruct ReadEquipment(byte[] bytes, int entryOffset)
        {
            if (entryOffset < 0 || entryOffset + EntryLength > bytes.Length)
                throw new InvalidDataException($"shop_arms.bin entry at offset 0x{entryOffset:X4} extends past EOF.");

            return new EquipmentStruct
            {
                Name_id = ReadUInt16(bytes, entryOffset + 0x00),
                Exists = bytes[entryOffset + 0x02] != 0,
                Flags = (EquipmentStruct.WeaponFlags)bytes[entryOffset + 0x03],
                Character = ReadCharacter(bytes, entryOffset + 0x04),
                Type = (EquipmentStruct.EquipmentType_Enum)bytes[entryOffset + 0x05],
                CharacterEquipped = ReadCharacter(bytes, entryOffset + 0x06),
                Unk7 = bytes[entryOffset + 0x07],
                Dmg_formula = (DamageFormula_Enum)bytes[entryOffset + 0x08],
                Power = bytes[entryOffset + 0x09],
                Crit_bonus = bytes[entryOffset + 0x0A],
                Slot_count = bytes[entryOffset + 0x0B],
                Model_id = ReadUInt16(bytes, entryOffset + 0x0C),
                Ability1 = ReadUInt16(bytes, entryOffset + 0x0E),
                Ability2 = ReadUInt16(bytes, entryOffset + 0x10),
                Ability3 = ReadUInt16(bytes, entryOffset + 0x12),
                Ability4 = ReadUInt16(bytes, entryOffset + 0x14)
            };
        }

        static Character_Enum ReadCharacter(byte[] bytes, int offset)
        {
            return (Character_Enum)(sbyte)bytes[offset];
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static string BuildDisplayLabel(int index, EquipmentStruct equipment)
        {
            string baseLabel = $"{equipment.Character} {FormatType(equipment.Type)}";
            string disambiguator = BuildFriendlyDisambiguator(equipment);
            return string.IsNullOrWhiteSpace(disambiguator)
                ? $"{baseLabel} · {index:X4}h"
                : $"{baseLabel} · {disambiguator}";
        }

        static string BuildSummary(EquipmentStruct equipment)
        {
            List<string> sections = new()
            {
                $"formula {equipment.Dmg_formula}",
                $"power {equipment.Power}",
                $"crit {equipment.Crit_bonus}",
                $"slots {equipment.Slot_count}"
            };

            string friendlyFlags = BuildFriendlyFlagSummary(equipment);
            if (!string.IsNullOrWhiteSpace(friendlyFlags))
                sections.Insert(0, friendlyFlags);

            string friendlyAbilities = BuildFriendlyAbilitySummary(equipment);
            if (!string.IsNullOrWhiteSpace(friendlyAbilities))
                sections.Add(friendlyAbilities);

            return string.Join(" · ", sections);
        }

        static string BuildDetailSummary(EquipmentStruct equipment)
        {
            List<string> sections = new()
            {
                $"name-id {equipment.Name_id:X4}h",
                $"model {equipment.Model_id:X4}h",
                $"abilities {BuildAbilitySummary(equipment)}"
            };

            string flags = BuildFlagsSummary(equipment);
            if (!string.IsNullOrWhiteSpace(flags))
                sections.Add(flags);

            return string.Join(" · ", sections);
        }

        static string BuildAbilitySummary(EquipmentStruct equipment)
        {
            return "[" + string.Join(", ", new[]
            {
                FormatAbility(equipment.Ability1),
                FormatAbility(equipment.Ability2),
                FormatAbility(equipment.Ability3),
                FormatAbility(equipment.Ability4)
            }) + "]";
        }

        static string BuildFriendlyAbilitySummary(EquipmentStruct equipment)
        {
            List<string> abilityNames = new[]
            {
                TryFormatFriendlyAbility(equipment.Ability1),
                TryFormatFriendlyAbility(equipment.Ability2),
                TryFormatFriendlyAbility(equipment.Ability3),
                TryFormatFriendlyAbility(equipment.Ability4)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

            return abilityNames.Count == 0
                ? string.Empty
                : string.Join(" / ", abilityNames);
        }

        static string TryFormatFriendlyAbility(ushort rawValue)
        {
            if (rawValue == 0x00FF)
                return string.Empty;

            if (AutoAbility_Dictionary.Instance.TryGetValue(rawValue, out string? autoAbilityName))
                return autoAbilityName;

            return $"{rawValue:X4}h";
        }

        static string FormatAbility(ushort rawValue)
        {
            if (rawValue == 0x00FF)
                return "Empty";

            if (AutoAbility_Dictionary.Instance.TryGetValue(rawValue, out string? autoAbilityName))
                return autoAbilityName;

            return $"{rawValue:X4}h";
        }

        static string BuildFlagsSummary(EquipmentStruct equipment)
        {
            List<string> flags = new();
            if (equipment.FlagIsSummon)
                flags.Add("summon");
            if (equipment.FlagIsHidden)
                flags.Add("hidden");
            if (equipment.FlagIsCelestial)
                flags.Add("celestial");
            if (equipment.FlagIsBrotherhood)
                flags.Add("brotherhood");

            return flags.Count == 0
                ? string.Empty
                : $"flags {string.Join(", ", flags)}";
        }

        static string BuildFriendlyFlagSummary(EquipmentStruct equipment)
        {
            List<string> flags = new();
            if (equipment.FlagIsBrotherhood)
                flags.Add("Brotherhood");
            if (equipment.FlagIsCelestial)
                flags.Add("Celestial");
            if (equipment.FlagIsSummon)
                flags.Add("Summon");
            if (equipment.FlagIsHidden)
                flags.Add("Hidden");

            return flags.Count == 0
                ? string.Empty
                : string.Join(" / ", flags);
        }

        static string BuildFriendlyDisambiguator(EquipmentStruct equipment)
        {
            string friendlyFlags = BuildFriendlyFlagSummary(equipment);
            if (!string.IsNullOrWhiteSpace(friendlyFlags))
                return friendlyFlags;

            string friendlyAbilities = BuildFriendlyAbilitySummary(equipment);
            if (!string.IsNullOrWhiteSpace(friendlyAbilities))
                return friendlyAbilities;

            return $"crit {equipment.Crit_bonus} · slots {equipment.Slot_count}";
        }

        static string FormatType(EquipmentStruct.EquipmentType_Enum type)
        {
            return type == EquipmentStruct.EquipmentType_Enum.Armor ? "Armor" : "Weapon";
        }
    }

    internal sealed class ShopGearCatalog
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyDictionary<int, ShopGearCatalogEntry> EntriesByIndex { get; init; }
    }

    internal sealed class ShopGearCatalogEntry
    {
        public required int Index { get; init; }
        public required EquipmentStruct Equipment { get; init; }
        public required string DisplayLabel { get; init; }
        public required string Summary { get; init; }
        public required string DetailSummary { get; init; }
    }
}
