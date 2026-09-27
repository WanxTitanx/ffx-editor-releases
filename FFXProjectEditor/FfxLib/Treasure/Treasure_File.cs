using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Customization;

namespace FFXProjectEditor.FfxLib.Treasure
{
    public sealed class Treasure_Entry
    {
        public const int LENGTH = 4;

        public byte Kind { get; set; }
        public byte Quantity { get; set; }

        /// <summary>Item id (ushort) — Fahrenheit/Ghidra `Treasure.item_id`, cross-confirmed 2026-07-31.</summary>
        public ushort ItemId { get; set; }

        public static Treasure_Entry ReadSingle(byte[] bytes)
        {
            if (bytes.Length != LENGTH)
                throw new ArgumentException("[Treasure_Entry] Treasure entry must be exactly 4 bytes long.");

            return new Treasure_Entry
            {
                Kind = bytes[0],
                Quantity = bytes[1],
                ItemId = BitConverter.ToUInt16(bytes, 2)
            };
        }

        public byte[] WriteSingle()
        {
            byte[] bytes = new byte[LENGTH];
            bytes[0] = Kind;
            bytes[1] = Quantity;
            Array.Copy(BitConverter.GetBytes(ItemId), 0, bytes, 2, 2);
            return bytes;
        }
    }

    public static class Treasure_File
    {
        const int HeaderLength = 0x14;
        const ushort ExpectedMinIndex = 0;
        const ushort ExpectedMaxIndex = 0x01F1;
        const ushort ExpectedEntryLength = Treasure_Entry.LENGTH;
        const ushort ExpectedDataLength = 0x07C8;

        public static List<Treasure_Entry> ReadAll(byte[] fileBytes)
        {
            return ReadTable(fileBytes).Entries.ToList();
        }

        public static byte[] WriteAll(IEnumerable<Treasure_Entry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            List<Treasure_Entry> entryList = entries.ToList();
            IndexedFixedTableHeader header = BuildHeaderForEntryCount(entryList.Count);
            return WriteTable(new TreasureTable
            {
                OriginalBytes = BuildHeaderBytes(header),
                Header = header,
                Entries = entryList
            });
        }

        internal static TreasureTable ReadTable(byte[] fileBytes)
        {
            ArgumentNullException.ThrowIfNull(fileBytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(fileBytes);
            ValidateHeader(header, fileBytes.Length);

            List<Treasure_Entry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] chunk = fileBytes.Skip(offset).Take(header.EntryLength).ToArray();
                entries.Add(Treasure_Entry.ReadSingle(chunk));
            }

            return new TreasureTable
            {
                OriginalBytes = fileBytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        internal static byte[] WriteTable(TreasureTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);
            ValidateWriteShape(table.Header, table.Entries.Count);

            byte[] output = table.OriginalBytes.ToArray();

            for (int i = 0; i < table.Header.EntryCount; i++)
            {
                int offset = HeaderLength + (i * table.Header.EntryLength);
                byte[] entryBytes = table.Entries[i].WriteSingle();
                Array.Copy(entryBytes, 0, output, offset, entryBytes.Length);
            }

            return output;
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.MinIndex != ExpectedMinIndex
                || header.MaxIndex != ExpectedMaxIndex
                || header.EntryLength != ExpectedEntryLength
                || header.TotalDataLength != ExpectedDataLength)
            {
                throw new InvalidDataException(
                    $"takara.bin shape mismatch. Expected min=0 max=497 entryLen=0x04 total=0x07C8; got min={header.MinIndex} max={header.MaxIndex} entryLen=0x{header.EntryLength:X2} total=0x{header.TotalDataLength:X4}.");
            }

            if (HeaderLength + header.TotalDataLength != totalFileLength)
                throw new InvalidDataException(
                    $"takara.bin length mismatch. Header declares 0x{header.TotalDataLength:X4} data bytes after 0x14 header, but file length is 0x{totalFileLength:X4}.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException(
                    $"takara.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static IndexedFixedTableHeader BuildHeaderForEntryCount(int entryCount)
        {
            if (entryCount != ExpectedMaxIndex + 1)
                throw new InvalidOperationException(
                    $"takara.bin writer expected {ExpectedMaxIndex + 1} entries for the known HD table shape, got {entryCount}.");

            return new IndexedFixedTableHeader
            {
                PrefixBytes = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
                MinIndex = ExpectedMinIndex,
                MaxIndex = ExpectedMaxIndex,
                EntryLength = ExpectedEntryLength,
                TotalDataLength = ExpectedDataLength,
                TailBytes = [0x14, 0x00, 0x00, 0x00]
            };
        }

        static byte[] BuildHeaderBytes(IndexedFixedTableHeader header)
        {
            byte[] bytes = new byte[HeaderLength];
            Array.Copy(header.PrefixBytes, 0, bytes, 0, Math.Min(0x08, header.PrefixBytes.Length));
            WriteUInt16(bytes, 0x08, header.MinIndex);
            WriteUInt16(bytes, 0x0A, header.MaxIndex);
            WriteUInt16(bytes, 0x0C, header.EntryLength);
            WriteUInt16(bytes, 0x0E, header.TotalDataLength);
            Array.Copy(header.TailBytes, 0, bytes, 0x10, Math.Min(0x04, header.TailBytes.Length));
            Array.Resize(ref bytes, HeaderLength + header.TotalDataLength);
            return bytes;
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    internal sealed class TreasureTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<Treasure_Entry> Entries { get; init; }
    }
}
