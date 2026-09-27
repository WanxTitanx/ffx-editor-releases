using FFXProjectEditor.FfxLib.Arm;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ability
{
    internal static class AutoAbility_File
    {
        internal const int HeaderLength = 0x14;
        internal const int EntryLength = 0x6C;
        const int StatusChanceCount = 0x19;
        const int StatusDurationCount = 0x0D;
        const ushort ExpectedMinIndex = 0x0000;
        const ushort ExpectedMaxIndex = 0x0085;
        internal const int ExpectedEntryCount = 0x86;

        public static AutoAbilityTable Read(byte[] abilityBytes, byte[] priceBytes)
        {
            ArgumentNullException.ThrowIfNull(abilityBytes);
            ArgumentNullException.ThrowIfNull(priceBytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(abilityBytes);
            ValidateAbilityHeader(header, abilityBytes.Length);

            NameDescriptionTextPrefixTable_File textTable = NameDescriptionTextPrefixTable_File.Read(
                abilityBytes,
                header,
                FfxEncoding.UsDecoder,
                "a_ability.bin");
            ArmsRateTable priceTable = Arms_Rate.ReadTable(priceBytes);

            if (textTable.EntryCount != header.EntryCount)
            {
                throw new InvalidDataException(
                    $"a_ability.bin text table count mismatch. Header count {header.EntryCount}, decoded text count {textTable.EntryCount}.");
            }

            if (priceTable.Rates.Count > header.EntryCount)
            {
                throw new InvalidDataException(
                    $"arms_rate.bin entry count mismatch. Expected at most {header.EntryCount}, got {priceTable.Rates.Count}.");
            }

            Dictionary<int, NameDescriptionTextPrefixTable_Entry> textByIndex = textTable.Entries.ToDictionary(entry => entry.Index);
            List<AutoAbilityEntry> entries = new(header.EntryCount);
            int priceCoverageCount = Math.Min(priceTable.Rates.Count, header.EntryCount);

            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = abilityBytes.Skip(offset).Take(header.EntryLength).ToArray();

                if (!textByIndex.TryGetValue(index, out NameDescriptionTextPrefixTable_Entry? textEntry))
                    throw new InvalidDataException($"a_ability.bin is missing decoded text refs for entry {index}.");

                entries.Add(new AutoAbilityEntry
                {
                    Index = index,
                    Label = ResolveLabel(index, textEntry.NameText),
                    RawBytes = raw,
                    NameText = textEntry.NameText,
                    AuxiliaryText1 = textEntry.AuxiliaryText1,
                    DescriptionText = textEntry.DescriptionText,
                    AuxiliaryText2 = textEntry.AuxiliaryText2,
                    GilPrice = i < priceCoverageCount ? priceTable.Rates[i] : 0,
                    SosFlagByte = ReadByte(raw, 0x10),
                    ElementStrike = ReadByte(raw, 0x11),
                    ElementAbsorb = ReadByte(raw, 0x12),
                    ElementImmune = ReadByte(raw, 0x13),
                    ElementResist = ReadByte(raw, 0x14),
                    ElementWeak = ReadByte(raw, 0x15),
                    StatusInflict = ReadBytes(raw, 0x16, StatusChanceCount),
                    StatusDuration = ReadBytes(raw, 0x2F, StatusDurationCount),
                    StatusResist = ReadBytes(raw, 0x3C, StatusChanceCount),
                    StatIncreaseAmount = ReadByte(raw, 0x55),
                    StatIncreaseFlags = ReadUInt16(raw, 0x56),
                    AutoStatusesPermanent = ReadUInt16(raw, 0x58),
                    AutoStatusesTemporal = ReadUInt16(raw, 0x5A),
                    AutoStatusesExtra = ReadUInt16(raw, 0x5C),
                    ExtraStatusInflict = ReadUInt16(raw, 0x5E),
                    ExtraStatusImmunities = ReadUInt16(raw, 0x60),
                    AbilityFlags62 = ReadByte(raw, 0x62),
                    AbilityFlags63 = ReadByte(raw, 0x63),
                    AbilityFlags64 = ReadByte(raw, 0x64),
                    AbilityFlags65 = ReadByte(raw, 0x65),
                    AbilityFlags66 = ReadByte(raw, 0x66),
                    UnknownByte67 = ReadByte(raw, 0x67),
                    Icon = ReadByte(raw, 0x68),
                    GroupIndex = ReadByte(raw, 0x69),
                    GroupLevel = ReadByte(raw, 0x6A),
                    InternationalBonusIndex = ReadByte(raw, 0x6B)
                });
            }

            return new AutoAbilityTable
            {
                OriginalAbilityBytes = abilityBytes.ToArray(),
                AbilityHeader = header,
                PriceTable = priceTable,
                PriceCoverageCount = priceCoverageCount,
                Entries = entries
            };
        }

        public static byte[] WriteAbilities(AutoAbilityTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateAbilityHeader(table.AbilityHeader, table.OriginalAbilityBytes.Length);
            ValidateWriteShape(table.AbilityHeader, table.Entries.Count);
            byte[] output = table.OriginalAbilityBytes.ToArray();

            foreach (AutoAbilityEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                ValidateEntryRawLength(entry.RawBytes, EntryLength, entry.Index);
                ValidateByteArrayLength(entry.StatusInflict, StatusChanceCount, $"a_ability.bin entry {entry.Index} StatusInflict");
                ValidateByteArrayLength(entry.StatusDuration, StatusDurationCount, $"a_ability.bin entry {entry.Index} StatusDuration");
                ValidateByteArrayLength(entry.StatusResist, StatusChanceCount, $"a_ability.bin entry {entry.Index} StatusResist");
                byte[] raw = BuildEntryRaw(entry);

                CopyEntryBytes(output, table.AbilityHeader, entry.Index, raw);
            }

            return output;
        }

        public static byte[] GrowByOne(AutoAbilityTable table, AutoAbilityEntry newEntry)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(newEntry);

            ValidateAbilityHeader(table.AbilityHeader, table.OriginalAbilityBytes.Length);
            ValidateWriteShape(table.AbilityHeader, table.Entries.Count);

            int expectedNewIndex = table.AbilityHeader.MaxIndex + 1;
            if (newEntry.Index != expectedNewIndex)
                throw new InvalidOperationException($"New auto-ability index must be {expectedNewIndex}; got {newEntry.Index}.");

            ValidateEntryRawLength(newEntry.RawBytes, EntryLength, newEntry.Index);
            ValidateByteArrayLength(newEntry.StatusInflict, StatusChanceCount, $"a_ability.bin entry {newEntry.Index} StatusInflict");
            ValidateByteArrayLength(newEntry.StatusDuration, StatusDurationCount, $"a_ability.bin entry {newEntry.Index} StatusDuration");
            ValidateByteArrayLength(newEntry.StatusResist, StatusChanceCount, $"a_ability.bin entry {newEntry.Index} StatusResist");

            int oldDataEnd = HeaderLength + table.AbilityHeader.TotalDataLength;
            if (oldDataEnd > table.OriginalAbilityBytes.Length)
                throw new InvalidOperationException("Cannot grow a_ability.bin because the source data section extends past EOF.");

            byte[] output = new byte[table.OriginalAbilityBytes.Length + EntryLength];
            Array.Copy(table.OriginalAbilityBytes, 0, output, 0, oldDataEnd);

            byte[] raw = BuildEntryRaw(newEntry);
            Array.Copy(raw, 0, output, oldDataEnd, EntryLength);

            Array.Copy(
                table.OriginalAbilityBytes,
                oldDataEnd,
                output,
                oldDataEnd + EntryLength,
                table.OriginalAbilityBytes.Length - oldDataEnd);

            int newEntryCount = table.AbilityHeader.EntryCount + 1;
            WriteUInt16(output, 0x0A, checked((ushort)(table.AbilityHeader.MinIndex + newEntryCount - 1)));
            WriteUInt16(output, 0x0E, checked((ushort)(newEntryCount * EntryLength)));
            return output;
        }

        public static byte[] WritePrices(AutoAbilityTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ArmsRateTable rates = new ArmsRateTable
            {
                OriginalBytes = table.PriceTable.OriginalBytes,
                Header = table.PriceTable.Header,
                Rates = table.Entries
                    .OrderBy(entry => entry.Index)
                    .Take(table.PriceTable.Header.RealEntryCount)
                    .Select(entry => entry.GilPrice)
                    .ToList()
            };

            return Arms_Rate.Write(rates);
        }

        // Full rebuild: in-place data edits (WriteAbilities) followed by a string-pool rebuild that applies
        // the edited Name/Aux/Description texts via the prefix-table writer (keys + data payload preserved).
        public static byte[] WriteAbilitiesAndText(AutoAbilityTable table, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(decoder);

            byte[] dataBytes = WriteAbilities(table); // edited data payload, original text/pool
            IndexedFixedTableHeader header = Customization_File.ReadHeader(dataBytes);
            NameDescriptionTextPrefixTable_File prefix = NameDescriptionTextPrefixTable_File.Read(dataBytes, header, decoder, "a_ability.bin");

            Dictionary<int, AutoAbilityEntry> byIndex = table.Entries.ToDictionary(entry => entry.Index);
            foreach (NameDescriptionTextPrefixTable_Entry prefixEntry in prefix.Entries)
            {
                if (!byIndex.TryGetValue(prefixEntry.Index, out AutoAbilityEntry? entry))
                    continue;

                prefixEntry.NameText = entry.NameText;
                prefixEntry.AuxiliaryText1 = entry.AuxiliaryText1;
                prefixEntry.DescriptionText = entry.DescriptionText;
                prefixEntry.AuxiliaryText2 = entry.AuxiliaryText2;
            }

            return prefix.Write(decoder);
        }

        static string ResolveLabel(int index, string nameText)
        {
            if (!string.IsNullOrWhiteSpace(nameText))
                return nameText;

            if (AutoAbility_Dictionary.Instance.TryGetValue((ushort)index, out string? fallback))
                return fallback;

            return $"Auto-Ability {index:D3}";
        }

        static void ValidateAbilityHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.MinIndex != ExpectedMinIndex || header.MaxIndex < ExpectedMaxIndex || header.EntryCount < ExpectedEntryCount)
            {
                throw new InvalidDataException(
                    $"a_ability.bin uses unexpected index range {header.MinIndex}..{header.MaxIndex} ({header.EntryCount} entries); expected at least {ExpectedMinIndex}..{ExpectedMaxIndex} ({ExpectedEntryCount} entries).");
            }

            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"a_ability.bin uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            int expectedDataLength = header.EntryCount * header.EntryLength;
            if (header.TotalDataLength != expectedDataLength)
                throw new InvalidDataException($"a_ability.bin declares data length 0x{header.TotalDataLength:X4}, but {header.EntryCount} rows of 0x{header.EntryLength:X2} require 0x{expectedDataLength:X4}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("a_ability.bin declares a data section that extends past EOF.");
        }

        static byte[] BuildEntryRaw(AutoAbilityEntry entry)
        {
            byte[] raw = entry.RawBytes.ToArray();

            WriteByte(raw, 0x10, entry.SosFlagByte);
            WriteByte(raw, 0x11, entry.ElementStrike);
            WriteByte(raw, 0x12, entry.ElementAbsorb);
            WriteByte(raw, 0x13, entry.ElementImmune);
            WriteByte(raw, 0x14, entry.ElementResist);
            WriteByte(raw, 0x15, entry.ElementWeak);
            WriteBytes(raw, 0x16, entry.StatusInflict, StatusChanceCount);
            WriteBytes(raw, 0x2F, entry.StatusDuration, StatusDurationCount);
            WriteBytes(raw, 0x3C, entry.StatusResist, StatusChanceCount);
            WriteByte(raw, 0x55, entry.StatIncreaseAmount);
            WriteUInt16(raw, 0x56, entry.StatIncreaseFlags);
            WriteUInt16(raw, 0x58, entry.AutoStatusesPermanent);
            WriteUInt16(raw, 0x5A, entry.AutoStatusesTemporal);
            WriteUInt16(raw, 0x5C, entry.AutoStatusesExtra);
            WriteUInt16(raw, 0x5E, entry.ExtraStatusInflict);
            WriteUInt16(raw, 0x60, entry.ExtraStatusImmunities);
            WriteByte(raw, 0x62, entry.AbilityFlags62);
            WriteByte(raw, 0x63, entry.AbilityFlags63);
            WriteByte(raw, 0x64, entry.AbilityFlags64);
            WriteByte(raw, 0x65, entry.AbilityFlags65);
            WriteByte(raw, 0x66, entry.AbilityFlags66);
            WriteByte(raw, 0x67, entry.UnknownByte67);
            WriteByte(raw, 0x68, entry.Icon);
            WriteByte(raw, 0x69, entry.GroupIndex);
            WriteByte(raw, 0x6A, entry.GroupLevel);
            WriteByte(raw, 0x6B, entry.InternationalBonusIndex);

            return raw;
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
            {
                throw new InvalidOperationException(
                    $"a_ability.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
            }
        }

        static void ValidateEntryRawLength(byte[] raw, int expectedLength, int index)
        {
            if (raw.Length != expectedLength)
                throw new InvalidOperationException($"a_ability.bin entry {index} expected raw length 0x{expectedLength:X2}, found 0x{raw.Length:X2}.");
        }

        static void ValidateByteArrayLength(byte[] bytes, int expectedCount, string label)
        {
            if (bytes.Length != expectedCount)
                throw new InvalidOperationException($"{label} expected exactly {expectedCount} bytes, found {bytes.Length}.");
        }

        static void CopyEntryBytes(byte[] output, IndexedFixedTableHeader header, int index, byte[] raw)
        {
            int relativeIndex = index - header.MinIndex;
            if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                throw new InvalidOperationException($"Entry index {index} is outside the a_ability.bin table range.");

            int offset = HeaderLength + (relativeIndex * header.EntryLength);
            Array.Copy(raw, 0, output, offset, Math.Min(header.EntryLength, raw.Length));
        }

        static byte[] ReadBytes(byte[] bytes, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > bytes.Length)
                return new byte[count < 0 ? 0 : count];

            return bytes.Skip(offset).Take(count).ToArray();
        }

        static int ReadByte(byte[] bytes, int offset)
        {
            if (offset < 0 || offset >= bytes.Length)
                return 0;

            return bytes[offset];
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static void WriteByte(byte[] bytes, int offset, int value)
        {
            EnsureWriteRange(bytes, offset, 1);
            if ((uint)value > byte.MaxValue)
                throw new InvalidOperationException($"Value {value} does not fit in a single byte.");

            bytes[offset] = (byte)value;
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            EnsureWriteRange(bytes, offset, 2);

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
        }

        static void WriteBytes(byte[] bytes, int offset, byte[] source, int expectedCount)
        {
            EnsureWriteRange(bytes, offset, expectedCount);
            ValidateByteArrayLength(source, expectedCount, $"byte block at offset 0x{offset:X2}");

            for (int i = 0; i < expectedCount; i++)
                bytes[offset + i] = source[i];
        }

        static void EnsureWriteRange(byte[] bytes, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > bytes.Length)
                throw new InvalidOperationException($"Attempted to write outside the fixed a_ability.bin entry at offset 0x{offset:X2}.");
        }
    }

    internal sealed class AutoAbilityTable
    {
        public required byte[] OriginalAbilityBytes { get; init; }
        public required IndexedFixedTableHeader AbilityHeader { get; init; }
        public required ArmsRateTable PriceTable { get; init; }
        public required int PriceCoverageCount { get; init; }
        public required IReadOnlyList<AutoAbilityEntry> Entries { get; init; }

        public bool HasFullPriceCoverage => PriceCoverageCount == AbilityHeader.EntryCount;
        public int MissingPriceCount => AbilityHeader.EntryCount - PriceCoverageCount;
    }

    internal sealed class AutoAbilityEntry
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required byte[] RawBytes { get; init; }
        public required string NameText { get; set; }
        public required string AuxiliaryText1 { get; set; }
        public required string DescriptionText { get; set; }
        public required string AuxiliaryText2 { get; set; }

        public int GilPrice { get; set; }
        public int SosFlagByte { get; set; }
        public int ElementStrike { get; set; }
        public int ElementAbsorb { get; set; }
        public int ElementImmune { get; set; }
        public int ElementResist { get; set; }
        public int ElementWeak { get; set; }
        public byte[] StatusInflict { get; set; } = Array.Empty<byte>();
        public byte[] StatusDuration { get; set; } = Array.Empty<byte>();
        public byte[] StatusResist { get; set; } = Array.Empty<byte>();
        public int StatIncreaseAmount { get; set; }
        public ushort StatIncreaseFlags { get; set; }
        public ushort AutoStatusesPermanent { get; set; }
        public ushort AutoStatusesTemporal { get; set; }
        public ushort AutoStatusesExtra { get; set; }
        public ushort ExtraStatusInflict { get; set; }
        public ushort ExtraStatusImmunities { get; set; }
        public int AbilityFlags62 { get; set; }
        public int AbilityFlags63 { get; set; }
        public int AbilityFlags64 { get; set; }
        public int AbilityFlags65 { get; set; }
        public int AbilityFlags66 { get; set; }
        public int UnknownByte67 { get; set; }
        public int Icon { get; set; }
        public int GroupIndex { get; set; }
        public int GroupLevel { get; set; }
        public int InternationalBonusIndex { get; set; }
    }
}
