using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Common
{
    internal static class KeyItem_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x14;
        const ushort ExpectedMinIndex = 0x0000;
        const ushort ExpectedMaxIndex = 0x003F;
        const int ExpectedEntryCount = 0x40;

        public static KeyItemTable Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            NameDescriptionTextPrefixTable_File textTable = NameDescriptionTextPrefixTable_File.Read(
                bytes,
                header,
                FfxEncoding.UsDecoder,
                "important.bin");
            if (textTable.EntryCount != header.EntryCount)
            {
                throw new InvalidDataException(
                    $"important.bin text table count mismatch. Header count {header.EntryCount}, decoded text count {textTable.EntryCount}.");
            }

            Dictionary<int, NameDescriptionTextPrefixTable_Entry> textByIndex = textTable.Entries.ToDictionary(entry => entry.Index);
            List<KeyItemEntry> entries = new(header.EntryCount);

            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();

                if (!textByIndex.TryGetValue(index, out NameDescriptionTextPrefixTable_Entry? textEntry))
                    throw new InvalidDataException($"important.bin is missing decoded text refs for entry {index}.");

                entries.Add(new KeyItemEntry
                {
                    Index = index,
                    Label = ResolveLabel(index, textEntry.NameText),
                    RawBytes = raw,
                    NameText = textEntry.NameText,
                    DashText = textEntry.AuxiliaryText1,
                    DescriptionText = textEntry.DescriptionText,
                    OtherText = textEntry.AuxiliaryText2,
                    ItemType = ReadByte(raw, 0x10),
                    ItemValue = ReadByte(raw, 0x11),
                    Icon = ReadByte(raw, 0x12),
                    Number = ReadByte(raw, 0x13)
                });
            }

            return new KeyItemTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static byte[] Write(KeyItemTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);
            ValidateWriteShape(table.Header, table.Entries.Count);
            byte[] output = table.OriginalBytes.ToArray();

            foreach (KeyItemEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                ValidateEntryRawLength(entry.RawBytes, EntryLength, entry.Index);
                byte[] raw = entry.RawBytes.ToArray();

                // Only the observed item-type byte (0x10) and number byte (0x13) are authorable here.
                // Raw bytes 11h (item value) / 12h (icon) stay preserved from the original entry shape.
                WriteByte(raw, 0x10, entry.ItemType);
                WriteByte(raw, 0x13, entry.Number);

                CopyEntryBytes(output, table.Header, entry.Index, raw);
            }

            return output;
        }

        static string ResolveLabel(int index, string nameText)
        {
            if (!string.IsNullOrWhiteSpace(nameText))
                return nameText;

            if (KeyItem_Dictionary.Instance.TryGetValue((ushort)index, out string? fallback))
                return fallback;

            return $"Key Item {index:D3}";
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.MinIndex != ExpectedMinIndex || header.MaxIndex != ExpectedMaxIndex || header.EntryCount != ExpectedEntryCount)
            {
                throw new InvalidDataException(
                    $"important.bin uses unexpected index range {header.MinIndex}..{header.MaxIndex} ({header.EntryCount} entries); expected {ExpectedMinIndex}..{ExpectedMaxIndex} ({ExpectedEntryCount} entries).");
            }

            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"important.bin uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("important.bin declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
            {
                throw new InvalidOperationException(
                    $"important.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
            }
        }

        static void ValidateEntryRawLength(byte[] raw, int expectedLength, int index)
        {
            if (raw.Length != expectedLength)
                throw new InvalidOperationException($"important.bin entry {index} expected raw length 0x{expectedLength:X2}, found 0x{raw.Length:X2}.");
        }

        static void CopyEntryBytes(byte[] output, IndexedFixedTableHeader header, int index, byte[] raw)
        {
            int relativeIndex = index - header.MinIndex;
            if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                throw new InvalidOperationException($"Entry index {index} is outside the important.bin table range.");

            int offset = HeaderLength + (relativeIndex * header.EntryLength);
            Array.Copy(raw, 0, output, offset, Math.Min(header.EntryLength, raw.Length));
        }

        static int ReadByte(byte[] bytes, int offset)
        {
            if (offset < 0 || offset >= bytes.Length)
                return 0;

            return bytes[offset];
        }

        static void WriteByte(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset >= bytes.Length)
                throw new InvalidOperationException($"Attempted to write outside the important.bin entry at offset 0x{offset:X2}.");
            if ((uint)value > byte.MaxValue)
                throw new InvalidOperationException($"Value {value} does not fit in a single byte.");

            bytes[offset] = (byte)value;
        }
    }

    internal sealed class KeyItemTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<KeyItemEntry> Entries { get; init; }
    }

    internal sealed class KeyItemEntry
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required byte[] RawBytes { get; init; }
        public required string NameText { get; init; }
        public required string DashText { get; init; }
        public required string DescriptionText { get; init; }
        public required string OtherText { get; init; }

        /// <summary>Entry byte 0x10: item type ("is Al Bhed Primer?" in FFX — Fahrenheit/Ghidra naming, cross-confirmed 2026-07-31).</summary>
        public int ItemType { get; set; }

        /// <summary>Entry byte 0x11: item value (potentially unused).</summary>
        public int ItemValue { get; set; }

        /// <summary>Entry byte 0x12: icon id.</summary>
        public int Icon { get; set; }

        /// <summary>Entry byte 0x13: primer number / sort order.</summary>
        public int Number { get; set; }
    }
}
