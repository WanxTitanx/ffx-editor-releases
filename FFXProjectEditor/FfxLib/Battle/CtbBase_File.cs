// ============================================================================
// CtbBase_File — ctb_base.bin reader/writer (agility -> tick-speed + ICV bonus table)
// PURPOSE : reads/writes the indexed fixed table (header 0x14 + 2-byte entries) mapping agility/CTB timing;
//           write preserves the existing entry count and only re-stamps entries (RT0 slot-preserve).
// WHY     : CTB base is a small indexed table (0x02/entry); the writer is shape-preserving by design.
// EVIDENCE: RT0 byte-identity ctb_base.bin (baseline 185/185); header via Customization_File.ReadHeader.
// MAINT   : write supports ONLY preserving the entry count (real grow requires a structural writer + RT0).
//           HeaderLength/EntryLength are the on-disk constants — keep in sync.
// ============================================================================
using FFXProjectEditor.FfxLib.Customization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battle
{
    internal static class CtbBase_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x02;

        public static CtbBaseTable Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            List<CtbBaseEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int offset = HeaderLength + (i * header.EntryLength);
                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();
                entries.Add(new CtbBaseEntry
                {
                    Index = header.MinIndex + i,
                    RawBytes = raw,
                    TickSpeed = ReadByte(raw, 0x00),
                    IcvBonus = ReadByte(raw, 0x01)
                });
            }

            return new CtbBaseTable
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static byte[] Write(CtbBaseTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            ValidateWriteShape(table.Header, table.Entries.Count);
            byte[] output = table.OriginalBytes.ToArray();

            foreach (CtbBaseEntry entry in table.Entries.OrderBy(entry => entry.Index))
            {
                byte[] raw = entry.RawBytes.ToArray();
                EnsureLength(ref raw, EntryLength);
                raw[0x00] = unchecked((byte)entry.TickSpeed);
                raw[0x01] = unchecked((byte)entry.IcvBonus);
                CopyEntryBytes(output, table.Header, entry.Index, raw);
            }

            return output;
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"ctb_base.bin uses unexpected entry length {header.EntryLength}; expected 0x02.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("ctb_base.bin declares a data section that extends past EOF.");
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
                throw new InvalidOperationException($"ctb_base.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
        }

        static void CopyEntryBytes(byte[] output, IndexedFixedTableHeader header, int index, byte[] raw)
        {
            int relativeIndex = index - header.MinIndex;
            if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                throw new InvalidOperationException($"Entry index {index} is outside the ctb_base.bin table range.");

            int offset = HeaderLength + (relativeIndex * header.EntryLength);
            Array.Copy(raw, 0, output, offset, Math.Min(header.EntryLength, raw.Length));
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
    }

    internal sealed class CtbBaseTable
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<CtbBaseEntry> Entries { get; init; }
    }

    internal sealed class CtbBaseEntry
    {
        public required int Index { get; init; }
        public required byte[] RawBytes { get; init; }
        public int TickSpeed { get; set; }
        public int IcvBonus { get; set; }
    }
}
