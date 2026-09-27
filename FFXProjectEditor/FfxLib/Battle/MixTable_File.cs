// ============================================================================
// MixTable_File — prepare.bin (Mix table) reader/writer
// PURPOSE : the index table of the Rikku Mix: 0x70 origins, each a 0xE0-byte entry of result item ids
//           (u16 x 0x70 slots). Write preserves entry count and re-stamps only result ids (RT0 slot-preserve).
// WHY     : a Mix origin's "game index" is derived from its table index (0x2000 + index) — the label resolves
//           through CustomizationNaming_Util. Entries read the header via Customization_File.
// EVIDENCE: RT0 byte-identity prepare.bin; entry length 0xE0, result slots 0x70, base 0x2000.
// MAINT   : writer only preserves existing entry count (grow needs structural writer + RT0). ResultSlotCount
//           and EntryLength are the on-disk constants — keep in sync with the real file.
// ============================================================================
using FFXProjectEditor.FfxLib.Customization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battle
{
    internal static class MixTable_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0xE0;
        const int ResultSlotCount = 0x70;
        const ushort ItemGameIndexBase = 0x2000;

        public static MixTable Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            List<MixOriginEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();
                ushort[] results = new ushort[ResultSlotCount];

                for (int resultIndex = 0; resultIndex < ResultSlotCount; resultIndex++)
                {
                    results[resultIndex] = ReadUInt16(raw, resultIndex * 2);
                }

                ushort originRawGameIndex = ResolveItemGameIndex(index);
                entries.Add(new MixOriginEntry
                {
                    Index = index,
                    OriginRawGameIndex = originRawGameIndex,
                    OriginLabel = ResolveGameLabel(originRawGameIndex),
                    RawBytes = raw,
                    Results = results
                });
            }

            return new MixTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static byte[] Write(MixTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateWriteShape(table.Header, table.Entries.Count);
            byte[] output = table.OriginalBytes.ToArray();

            foreach (MixOriginEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                byte[] raw = entry.RawBytes.ToArray();
                EnsureLength(ref raw, EntryLength);

                for (int resultIndex = 0; resultIndex < ResultSlotCount; resultIndex++)
                {
                    ushort value = resultIndex < entry.Results.Length ? entry.Results[resultIndex] : (ushort)0;
                    WriteUInt16(raw, resultIndex * 2, value);
                }

                CopyEntryBytes(output, table.Header, entry.Index, raw);
            }

            return output;
        }

        public static ushort ResolveItemGameIndex(int index)
        {
            return unchecked((ushort)(ItemGameIndexBase + index));
        }

        public static string ResolveGameLabel(ushort rawGameIndex)
        {
            if (rawGameIndex == 0)
                return "<Empty>";

            return CustomizationNaming_Util.ResolveGameLabel(rawGameIndex);
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"prepare.bin uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            if (header.EntryCount != ResultSlotCount)
                throw new InvalidDataException($"prepare.bin uses unexpected origin count {header.EntryCount}; expected {ResultSlotCount}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("prepare.bin declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"prepare.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static void CopyEntryBytes(byte[] output, IndexedFixedTableHeader header, int index, byte[] raw)
        {
            int relativeIndex = index - header.MinIndex;
            if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                throw new InvalidOperationException($"Entry index {index} is outside the prepare.bin table range.");

            int offset = HeaderLength + (relativeIndex * header.EntryLength);
            Array.Copy(raw, 0, output, offset, Math.Min(header.EntryLength, raw.Length));
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
        }

        static void EnsureLength(ref byte[] bytes, int expectedLength)
        {
            if (bytes.Length >= expectedLength)
                return;

            Array.Resize(ref bytes, expectedLength);
        }
    }

    internal sealed class MixTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<MixOriginEntry> Entries { get; init; }
    }

    internal sealed class MixOriginEntry
    {
        public required int Index { get; init; }
        public required ushort OriginRawGameIndex { get; init; }
        public required string OriginLabel { get; init; }
        public required byte[] RawBytes { get; init; }
        public required ushort[] Results { get; init; }
    }
}
