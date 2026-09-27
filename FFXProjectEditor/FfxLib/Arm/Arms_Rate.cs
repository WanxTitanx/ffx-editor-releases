using FFXProjectEditor.FfxLib.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Arm
{
    public class Arms_Rate
    {
        const int EntryLength = 0x04;

        public static List<int> ReadList(byte[] byteFile)
        {
            return ReadTable(byteFile).Rates.ToList();
        }

        public static ArmsRateTable ReadTable(byte[] byteFile)
        {
            ArgumentNullException.ThrowIfNull(byteFile);

            EntryListFile listFile = EntryListFile.Unpack(byteFile);
            ValidateHeader(listFile.Header, byteFile.Length);

            List<int> rateList = new(listFile.Header.RealEntryCount);
            using (MemoryStream stream = new MemoryStream(listFile.FirstFile))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                for (int i = 0; i < listFile.Header.RealEntryCount; i++)
                    rateList.Add(reader.ReadInt32());
            }

            return new ArmsRateTable
            {
                OriginalBytes = byteFile.ToArray(),
                Header = listFile.Header,
                Rates = rateList
            };
        }

        public static byte[] Write(ArmsRateTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);
            if (table.Rates.Count != table.Header.RealEntryCount)
            {
                throw new InvalidOperationException(
                    $"arms_rate.bin writer only supports preserving the existing entry count. Expected {table.Header.RealEntryCount}, got {table.Rates.Count}.");
            }

            byte[] output = table.OriginalBytes.ToArray();
            int baseOffset = table.Header.EntryTableFileOffset;
            for (int index = 0; index < table.Rates.Count; index++)
                WriteInt32(output, baseOffset + (index * table.Header.EntrySize), table.Rates[index]);

            return output;
        }

        public static byte[] AppendRate(ArmsRateTable table, int rate)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateHeader(table.Header, table.OriginalBytes.Length);

            int insertOffset = table.Header.EntryTableFileOffset + (table.Header.RealEntryCount * table.Header.EntrySize);
            if (insertOffset < 0 || insertOffset > table.OriginalBytes.Length)
                throw new InvalidOperationException("Cannot grow arms_rate.bin because the table end is outside the source file.");

            byte[] output = new byte[table.OriginalBytes.Length + EntryLength];
            Array.Copy(table.OriginalBytes, 0, output, 0, insertOffset);
            WriteInt32(output, insertOffset, rate);
            Array.Copy(
                table.OriginalBytes,
                insertOffset,
                output,
                insertOffset + EntryLength,
                table.OriginalBytes.Length - insertOffset);

            int newRealEntryCount = table.Header.RealEntryCount + 1;
            WriteInt16(output, 0x0A, checked((short)(table.Header.EntryCount + 1)));
            WriteInt16(output, 0x0E, checked((short)(newRealEntryCount * EntryLength)));

            return output;
        }

        static void ValidateHeader(EntryListFile.FileHeader header, int totalLength)
        {
            if (header.EntrySize != EntryLength)
                throw new InvalidDataException($"arms_rate.bin uses unexpected entry length {header.EntrySize}; expected 0x{EntryLength:X2}.");

            int dataLength = header.RealEntryCount * header.EntrySize;
            if (header.EntryTableFileOffset < 0 || header.EntryTableFileOffset + dataLength > totalLength)
                throw new InvalidDataException("arms_rate.bin declares a table section that extends past EOF.");
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return;

            bytes[offset + 0] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
            bytes[offset + 2] = unchecked((byte)((value >> 16) & 0xFF));
            bytes[offset + 3] = unchecked((byte)((value >> 24) & 0xFF));
        }

        static void WriteInt16(byte[] bytes, int offset, short value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return;

            bytes[offset + 0] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
        }
    }

    public sealed class ArmsRateTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required EntryListFile.FileHeader Header { get; init; }
        public required IReadOnlyList<int> Rates { get; init; }
    }
}
