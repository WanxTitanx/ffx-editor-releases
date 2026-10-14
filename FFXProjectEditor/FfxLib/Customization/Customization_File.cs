using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Customization
{
    internal static class Customization_File
    {
        public const int HeaderLength = 0x14;
        public const int EntryLength = 0x08;

        public static IndexedFixedTableHeader ReadHeader(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (bytes.Length < HeaderLength)
                throw new InvalidDataException("Customization table is smaller than the expected 0x14-byte header.");

            return new IndexedFixedTableHeader
            {
                PrefixBytes = bytes.Take(0x08).ToArray(),
                MinIndex = ReadUInt16(bytes, 0x08),
                MaxIndex = ReadUInt16(bytes, 0x0A),
                EntryLength = ReadUInt16(bytes, 0x0C),
                TotalDataLength = ReadUInt16(bytes, 0x0E),
                TailBytes = bytes.Skip(0x10).Take(0x04).ToArray()
            };
        }

        public static CustomizationTable<GearCustomizationEntry> ReadGear(byte[] bytes)
        {
            IndexedFixedTableHeader header = ReadHeader(bytes);
            ValidateHeader(header, bytes.Length, "kaizou.bin");

            List<GearCustomizationEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int offset = HeaderLength + (i * header.EntryLength);
                entries.Add(new GearCustomizationEntry
                {
                    Index = header.MinIndex + i,
                    Target = ReadUInt16(bytes, offset + 0x00),
                    Result = ReadUInt16(bytes, offset + 0x02),
                    Item = ReadUInt16(bytes, offset + 0x04),
                    PrimaryValue = bytes[offset + 0x06],
                    SecondaryValue = bytes[offset + 0x07]
                });
            }

            return new CustomizationTable<GearCustomizationEntry>(header, entries);
        }

        public static CustomizationTable<AeonCustomizationEntry> ReadAeon(byte[] bytes)
        {
            IndexedFixedTableHeader header = ReadHeader(bytes);
            ValidateHeader(header, bytes.Length, "sum_grow.bin");

            List<AeonCustomizationEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int offset = HeaderLength + (i * header.EntryLength);
                entries.Add(new AeonCustomizationEntry
                {
                    Index = header.MinIndex + i,
                    Target = ReadUInt16(bytes, offset + 0x00),
                    Result = ReadUInt16(bytes, offset + 0x02),
                    Item = ReadUInt16(bytes, offset + 0x04),
                    PrimaryValue = bytes[offset + 0x06],
                    SecondaryValue = bytes[offset + 0x07]
                });
            }

            return new CustomizationTable<AeonCustomizationEntry>(header, entries);
        }

        public static byte[] WriteGear(CustomizationTable<GearCustomizationEntry> table)
        {
            ArgumentNullException.ThrowIfNull(table);
            ValidateWriteShape(table.Header, table.Entries.Count, "kaizou.bin");

            byte[] bytes = BuildHeader(table.Header);
            foreach (GearCustomizationEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                bytes = AppendEntry(bytes, entry.Target, entry.Result, entry.Item, entry.PrimaryValue, entry.SecondaryValue);
            }

            return bytes;
        }

        public static byte[] WriteAeon(CustomizationTable<AeonCustomizationEntry> table)
        {
            ArgumentNullException.ThrowIfNull(table);
            ValidateWriteShape(table.Header, table.Entries.Count, "sum_grow.bin");

            byte[] bytes = BuildHeader(table.Header);
            foreach (AeonCustomizationEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                bytes = AppendEntry(bytes, entry.Target, entry.Result, entry.Item, entry.PrimaryValue, entry.SecondaryValue);
            }

            return bytes;
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength, string label)
        {
            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"{label} uses unexpected entry length {header.EntryLength}; expected 0x08.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException($"{label} declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount, string label)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"{label} writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static byte[] BuildHeader(IndexedFixedTableHeader header)
        {
            byte[] bytes = new byte[HeaderLength];
            Array.Copy(header.PrefixBytes, 0, bytes, 0, Math.Min(0x08, header.PrefixBytes.Length));
            WriteUInt16(bytes, 0x08, header.MinIndex);
            WriteUInt16(bytes, 0x0A, header.MaxIndex);
            WriteUInt16(bytes, 0x0C, header.EntryLength);
            WriteUInt16(bytes, 0x0E, (ushort)(header.EntryCount * header.EntryLength));
            Array.Copy(header.TailBytes, 0, bytes, 0x10, Math.Min(0x04, header.TailBytes.Length));
            return bytes;
        }

        static byte[] AppendEntry(byte[] bytes, ushort target, ushort result, ushort item, byte primaryValue, byte secondaryValue)
        {
            int offset = bytes.Length;
            Array.Resize(ref bytes, offset + EntryLength);
            WriteUInt16(bytes, offset + 0x00, target);
            WriteUInt16(bytes, offset + 0x02, result);
            WriteUInt16(bytes, offset + 0x04, item);
            bytes[offset + 0x06] = primaryValue;
            bytes[offset + 0x07] = secondaryValue;
            return bytes;
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

    internal sealed class IndexedFixedTableHeader
    {
        public required byte[] PrefixBytes { get; init; }
        public required ushort MinIndex { get; init; }
        public required ushort MaxIndex { get; init; }
        public required ushort EntryLength { get; init; }
        public required ushort TotalDataLength { get; init; }
        public required byte[] TailBytes { get; init; }

        public int EntryCount => MaxIndex + 1 - MinIndex;
    }

    internal sealed class CustomizationTable<TEntry>
    {
        public CustomizationTable(IndexedFixedTableHeader header, IReadOnlyList<TEntry> entries)
        {
            Header = header;
            Entries = entries;
        }

        public IndexedFixedTableHeader Header { get; }
        public IReadOnlyList<TEntry> Entries { get; }
    }

    internal abstract class CustomizationEntryBase
    {
        public int Index { get; init; }
        public ushort Target { get; set; }
        public ushort Result { get; set; }
        public ushort Item { get; set; }
        public byte PrimaryValue { get; set; }
        public byte SecondaryValue { get; set; }
        public byte ResultCategory => FfxCommon_Util.GetGameCategory(Result);
        public ushort ResultIndex => FfxCommon_Util.GetGameIndex(Result);
        public byte ItemCategory => FfxCommon_Util.GetGameCategory(Item);
        public ushort ItemIndex => FfxCommon_Util.GetGameIndex(Item);
    }

    internal sealed class GearCustomizationEntry : CustomizationEntryBase
    {
        public bool IsStatRecipe => ResultCategory == 0;

        public string TargetLabel => Target switch
        {
            0x0001 => "Weapon",
            0x0002 => "Armor",
            0x007F => "Aeon",
            _ => $"Unknown ({Target:X4}h)"
        };

        public string ResultLabel => IsStatRecipe
            ? $"{CustomizationNaming_Util.ResolveStatLabel(Result)} +{PrimaryValue}"
            : CustomizationNaming_Util.ResolveGameLabel(Result);

        public string CostLabel => $"{(IsStatRecipe ? SecondaryValue : PrimaryValue)}x {CustomizationNaming_Util.ResolveGameLabel(Item)}";
        public string Summary => $"{TargetLabel} · {ResultLabel} · {CostLabel}";
    }

    internal sealed class AeonCustomizationEntry : CustomizationEntryBase
    {
        public bool IsStatRecipe => SecondaryValue != 0 || ResultCategory == 0;
        public string TargetLabel => Target == 0x007F ? "Aeon" : $"Unknown ({Target:X4}h)";
        public string ResultLabel => IsStatRecipe
            ? $"{CustomizationNaming_Util.ResolveStatLabel(Result)} +{PrimaryValue}"
            : CustomizationNaming_Util.ResolveGameLabel(Result);

        public string CostLabel => $"{PrimaryValue}x {CustomizationNaming_Util.ResolveGameLabel(Item)}";
        public string Summary => $"{TargetLabel} · {ResultLabel} · {CostLabel}";
    }

    internal static class CustomizationNaming_Util
    {
        public static readonly IReadOnlyList<StatOption> StatOptions =
        [
            new StatOption(0, "HP"),
            new StatOption(1, "MP"),
            new StatOption(2, "Strength"),
            new StatOption(3, "Defense"),
            new StatOption(4, "Magic"),
            new StatOption(5, "Magic Defense"),
            new StatOption(6, "Agility"),
            new StatOption(7, "Luck"),
            new StatOption(8, "Evasion"),
            new StatOption(9, "Accuracy")
        ];

        public static string ResolveStatLabel(ushort statId)
        {
            StatOption option = StatOptions.FirstOrDefault(candidate => candidate.Id == statId);
            return string.IsNullOrWhiteSpace(option.Name) ? $"Stat {statId:X4}h" : option.Name;
        }

        public static string ResolveGameLabel(ushort rawValue)
        {
            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawValue);
                ushort index = FfxCommon_Util.GetGameIndex(rawValue);

                if (category == (byte)GameCategory_Enum.Items ||
                    category == (byte)GameCategory_Enum.Commands ||
                    category == (byte)GameCategory_Enum.MonMagic1 ||
                    category == (byte)GameCategory_Enum.MonMagic2 ||
                    category == (byte)GameCategory_Enum.AutoAbilities)
                {
                    return FfxCommon_Util.GetGameIndexName(category, index);
                }
            }
            catch
            {
            }

            if (Item_Dictionary.Instance.TryGetValue(rawValue, out string? itemName))
                return itemName;

            if (AutoAbility_Dictionary.Instance.TryGetValue(rawValue, out string? autoAbilityName))
                return autoAbilityName;

            return $"{rawValue:X4}h";
        }
    }

    internal readonly record struct StatOption(ushort Id, string Name)
    {
        public string Display => $"[{Id}] {Name}";
    }
}
