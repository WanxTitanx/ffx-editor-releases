using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed class BattleTextTable_File
    {
        static readonly HashSet<byte> PrefixControls = [0x09, 0x12];

        public required int FileSize { get; init; }
        public required int MinIndex { get; init; }
        public required int MaxIndex { get; init; }
        public required int EntryLength { get; init; }
        public required int DataBlockLength { get; init; }
        public required IReadOnlyList<BattleTextTable_Entry> Entries { get; init; }

        // Full snapshot of the source file. The string pool decode is lossy
        // (decode-only), so the preserve-only Write re-stamps only the clearly
        // fixed scalar fields (header words + per-entry word offsets) over a
        // clone of this snapshot. A no-edit save is byte-identical by construction.
        public required byte[] OriginalBytes { get; init; }

        public int EntryCount => Entries.Count;

        public static BattleTextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < 0x14)
                throw new InvalidDataException("Battle text table is too small.");

            int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
            int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
            int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
            int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

            if (entryLength != 0x08)
                throw new InvalidDataException($"Battle text table expected 8-byte entries, got {entryLength:X}h.");

            if (dataBlockLength <= 0 || 0x14 + dataBlockLength > bytes.Length)
                throw new InvalidDataException("Battle text table has an invalid data block length.");

            if (maxIndex < minIndex)
                throw new InvalidDataException("Battle text table has an invalid index range.");

            int entryCount = (maxIndex - minIndex) + 1;
            if (entryCount <= 0)
                throw new InvalidDataException("Battle text table has no entries.");

            if (entryCount * entryLength > dataBlockLength)
                throw new InvalidDataException("Battle text table data block is shorter than its declared entries.");

            int stringDataOffset = 0x14 + dataBlockLength;
            byte[] stringBytes = new byte[bytes.Length - stringDataOffset];
            Array.Copy(bytes, stringDataOffset, stringBytes, 0, stringBytes.Length);

            List<BattleTextTable_Entry> entries = new(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = 0x14 + (i * entryLength);
                ushort word0 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x00);
                ushort word1 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x02);
                ushort word2 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x04);
                ushort word3 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x06);

                BattleStringRead primary = DecodeBattleString(stringBytes, word0, decoder);
                BattleStringRead secondary = DecodeBattleString(stringBytes, word1, decoder);
                BattleStringRead tertiary = DecodeBattleString(stringBytes, word2, decoder);
                BattleStringRead quaternary = DecodeBattleString(stringBytes, word3, decoder);

                entries.Add(new BattleTextTable_Entry
                {
                    Index = minIndex + i,
                    Word0 = word0,
                    Word1 = word1,
                    Word2 = word2,
                    Word3 = word3,
                    PrimaryText = primary.Text,
                    PrimaryPrefix = primary.PrefixLabel,
                    SecondaryText = secondary.Text,
                    SecondaryPrefix = secondary.PrefixLabel,
                    TertiaryText = tertiary.Text,
                    TertiaryPrefix = tertiary.PrefixLabel,
                    QuaternaryText = quaternary.Text,
                    QuaternaryPrefix = quaternary.PrefixLabel
                });
            }

            return new BattleTextTable_File
            {
                FileSize = bytes.Length,
                MinIndex = minIndex,
                MaxIndex = maxIndex,
                EntryLength = entryLength,
                DataBlockLength = dataBlockLength,
                Entries = entries,
                OriginalBytes = bytes.ToArray()
            };
        }

        // Preserve-only writer (RT0 identity). Clones the original file snapshot
        // and re-stamps ONLY the clearly-fixed scalar fields in place:
        //   - header: MinIndex (0x08), MaxIndex (0x0A), EntryLength (0x0C),
        //     DataBlockLength (0x0E) as little-endian u16
        //   - each entry's 4 word offsets (Word0..Word3) at 0x14 + i*8
        // The entire string pool and every other byte are preserved verbatim,
        // so a no-edit Read -> Write round-trip is byte-identical by construction.
        // (The decoded text is decode-only / lossy and is intentionally NOT
        // re-encoded here.)
        public static byte[] Write(BattleTextTable_File table)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (table.OriginalBytes is null)
                throw new InvalidOperationException("Battle text table has no original byte snapshot to preserve.");

            byte[] output = table.OriginalBytes.ToArray();

            if (output.Length < 0x14)
                throw new InvalidDataException("Battle text table snapshot is too small to re-stamp.");

            if (table.EntryLength != 0x08)
                throw new InvalidOperationException($"Battle text table writer only supports 8-byte entries, got {table.EntryLength:X}h.");

            // Re-stamp the fixed header scalars.
            WriteUInt16(output, 0x08, table.MinIndex);
            WriteUInt16(output, 0x0A, table.MaxIndex);
            WriteUInt16(output, 0x0C, table.EntryLength);
            WriteUInt16(output, 0x0E, table.DataBlockLength);

            // Re-stamp each entry's fixed word offsets in place.
            int i = 0;
            foreach (BattleTextTable_Entry entry in table.Entries)
            {
                int entryOffset = 0x14 + (i * table.EntryLength);
                if (entryOffset + table.EntryLength > output.Length)
                    throw new InvalidDataException($"Battle text table entry {i} extends past EOF.");

                WriteUInt16(output, entryOffset + 0x00, entry.Word0);
                WriteUInt16(output, entryOffset + 0x02, entry.Word1);
                WriteUInt16(output, entryOffset + 0x04, entry.Word2);
                WriteUInt16(output, entryOffset + 0x06, entry.Word3);
                i++;
            }

            return output;
        }

        static void WriteUInt16(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 1 >= bytes.Length)
                throw new InvalidOperationException($"Attempted to write a u16 outside the battle text table at offset 0x{offset:X}.");
            if ((uint)value > ushort.MaxValue)
                throw new InvalidOperationException($"Value {value} does not fit in a u16 at offset 0x{offset:X}.");

            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static BattleStringRead DecodeBattleString(byte[] stringBytes, ushort rawOffset, Dictionary<byte, char> decoder)
        {
            if (rawOffset >= stringBytes.Length)
            {
                return new BattleStringRead
                {
                    Text = string.Empty,
                    PrefixLabel = "out-of-range"
                };
            }

            int offset = rawOffset;

            while (offset < stringBytes.Length && stringBytes[offset] == 0)
                offset++;

            if (offset + 1 < stringBytes.Length && stringBytes[offset] != 0 && stringBytes[offset + 1] == 0)
            {
                offset += 2;
                while (offset < stringBytes.Length && stringBytes[offset] == 0)
                    offset++;
            }

            string prefixLabel = string.Empty;
            if (offset + 1 < stringBytes.Length && PrefixControls.Contains(stringBytes[offset]))
            {
                prefixLabel = $"{stringBytes[offset]:X2} {stringBytes[offset + 1]:X2}";
                offset += 2;
            }

            if (offset >= stringBytes.Length)
            {
                return new BattleStringRead
                {
                    Text = string.Empty,
                    PrefixLabel = prefixLabel
                };
            }

            byte[] scriptBytes = TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
            string decoded = TextBinary_Util.DecodeScriptToString(scriptBytes, decoder, true);

            return new BattleStringRead
            {
                Text = decoded,
                PrefixLabel = prefixLabel
            };
        }

        readonly record struct BattleStringRead
        {
            public required string Text { get; init; }
            public required string PrefixLabel { get; init; }
        }
    }

    public sealed class BattleTextTable_Entry
    {
        public required int Index { get; init; }
        public required ushort Word0 { get; init; }
        public required ushort Word1 { get; init; }
        public required ushort Word2 { get; init; }
        public required ushort Word3 { get; init; }
        public required string PrimaryText { get; init; }
        public required string PrimaryPrefix { get; init; }
        public required string SecondaryText { get; init; }
        public required string SecondaryPrefix { get; init; }
        public required string TertiaryText { get; init; }
        public required string TertiaryPrefix { get; init; }
        public required string QuaternaryText { get; init; }
        public required string QuaternaryPrefix { get; init; }

        public string IndexLabel => $"Index {Index:X2}h";
        public string PreferredTitle =>
            FirstNonEmpty(PrimaryText, SecondaryText, TertiaryText, QuaternaryText, "(No decoded battle text)");
        public string PreferredSummary =>
            FirstNonEmpty(SecondaryText, TertiaryText, QuaternaryText, PrimaryText, "(No secondary line)");

        public string SearchBlob =>
            $"{Index:X2} {Word0:X4} {Word1:X4} {Word2:X4} {Word3:X4} {PrimaryText} {SecondaryText} {TertiaryText} {QuaternaryText}";

        public string BuildRawSummary()
        {
            return $"W0 {Word0:X4}h{FormatPrefix(PrimaryPrefix)}{Environment.NewLine}" +
                   $"W1 {Word1:X4}h{FormatPrefix(SecondaryPrefix)}{Environment.NewLine}" +
                   $"W2 {Word2:X4}h{FormatPrefix(TertiaryPrefix)}{Environment.NewLine}" +
                   $"W3 {Word3:X4}h{FormatPrefix(QuaternaryPrefix)}";
        }

        static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        static string FormatPrefix(string prefix)
        {
            return string.IsNullOrWhiteSpace(prefix) ? string.Empty : $" · prefix {prefix}";
        }
    }
}
