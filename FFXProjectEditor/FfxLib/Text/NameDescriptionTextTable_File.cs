using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class NameDescriptionTextTable_File
    {
        public required int FileSize { get; init; }
        // Verbatim copy of the bytes Read consumed. Used by the preserve-only WriteIdentity
        // (RT0 gate) so an unmodified file round-trips byte-for-byte. The string-pool region is
        // NOT otherwise retained by this type, so without this the repacking Write(decoder)
        // overload cannot be byte-faithful.
        public required byte[] OriginalBytes { get; init; }
        public required int MinIndex { get; init; }
        public required int MaxIndex { get; init; }
        public required int EntryLength { get; init; }
        public required int DataBlockLength { get; init; }
        public required byte[] HeaderBytes { get; init; }
        public required byte[] DataBlockPaddingBytes { get; init; }
        public required IReadOnlyList<NameDescriptionTextTable_Entry> Entries { get; init; }

        public int EntryCount => Entries.Count;

        public static NameDescriptionTextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < 0x14)
                throw new InvalidDataException("Name/description text table is too small.");

            int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
            int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
            int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
            int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

            if (entryLength != 0x10)
                throw new InvalidDataException($"Name/description text table expected 16-byte entries, got {entryLength:X}h.");

            if (dataBlockLength <= 0 || 0x14 + dataBlockLength > bytes.Length)
                throw new InvalidDataException("Name/description text table has an invalid data block length.");

            if (maxIndex < minIndex)
                throw new InvalidDataException("Name/description text table has an invalid index range.");

            int entryCount = (maxIndex - minIndex) + 1;
            if (entryCount <= 0)
                throw new InvalidDataException("Name/description text table has no entries.");

            if (entryCount * entryLength > dataBlockLength)
                throw new InvalidDataException("Name/description text table data block is shorter than its declared entries.");

            int entryTableLength = entryCount * entryLength;
            int paddingLength = dataBlockLength - entryTableLength;
            if (paddingLength < 0)
                throw new InvalidDataException("Name/description text table declared a shorter data block than its entry table.");

            byte[] headerBytes = new byte[0x14];
            Array.Copy(bytes, 0, headerBytes, 0, headerBytes.Length);

            byte[] dataBlockPaddingBytes = new byte[paddingLength];
            if (paddingLength > 0)
                Array.Copy(bytes, 0x14 + entryTableLength, dataBlockPaddingBytes, 0, paddingLength);

            byte[] stringBytes = new byte[bytes.Length - (0x14 + dataBlockLength)];
            Array.Copy(bytes, 0x14 + dataBlockLength, stringBytes, 0, stringBytes.Length);

            List<NameDescriptionTextTable_Entry> entries = new(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = 0x14 + (i * entryLength);
                byte[] rawEntryBytes = new byte[entryLength];
                Array.Copy(bytes, entryOffset, rawEntryBytes, 0, entryLength);

                ushort nameOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x00);
                ushort nameKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x02);
                ushort simplifiedNameOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x04);
                ushort simplifiedNameKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x06);
                ushort descriptionOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x08);
                ushort descriptionKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0A);
                ushort simplifiedDescriptionOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0C);
                ushort simplifiedDescriptionKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0E);

                byte[] nameScriptBytes = ReadScriptAt(stringBytes, nameOffset);
                byte[] simplifiedNameScriptBytes = ReadScriptAt(stringBytes, simplifiedNameOffset);
                byte[] descriptionScriptBytes = ReadScriptAt(stringBytes, descriptionOffset);
                byte[] simplifiedDescriptionScriptBytes = ReadScriptAt(stringBytes, simplifiedDescriptionOffset);

                entries.Add(new NameDescriptionTextTable_Entry
                {
                    Index = minIndex + i,
                    RawEntryBytes = rawEntryBytes,
                    NameOffset = nameOffset,
                    NameKey = nameKey,
                    SimplifiedNameOffset = simplifiedNameOffset,
                    SimplifiedNameKey = simplifiedNameKey,
                    DescriptionOffset = descriptionOffset,
                    DescriptionKey = descriptionKey,
                    SimplifiedDescriptionOffset = simplifiedDescriptionOffset,
                    SimplifiedDescriptionKey = simplifiedDescriptionKey,
                    NameScriptBytes = nameScriptBytes,
                    SimplifiedNameScriptBytes = simplifiedNameScriptBytes,
                    DescriptionScriptBytes = descriptionScriptBytes,
                    SimplifiedDescriptionScriptBytes = simplifiedDescriptionScriptBytes,
                    NameText = TextBinary_Util.DecodeScriptToString(nameScriptBytes, decoder, true),
                    SimplifiedNameText = TextBinary_Util.DecodeScriptToString(simplifiedNameScriptBytes, decoder, true),
                    DescriptionText = TextBinary_Util.DecodeScriptToString(descriptionScriptBytes, decoder, true),
                    SimplifiedDescriptionText = TextBinary_Util.DecodeScriptToString(simplifiedDescriptionScriptBytes, decoder, true)
                });
            }

            return new NameDescriptionTextTable_File
            {
                FileSize = bytes.Length,
                OriginalBytes = (byte[])bytes.Clone(),
                MinIndex = minIndex,
                MaxIndex = maxIndex,
                EntryLength = entryLength,
                DataBlockLength = dataBlockLength,
                HeaderBytes = headerBytes,
                DataBlockPaddingBytes = dataBlockPaddingBytes,
                Entries = entries
            };
        }

        static byte[] ReadScriptAt(byte[] stringBytes, int offset)
        {
            if (offset <= 0 || offset >= stringBytes.Length)
                return Array.Empty<byte>();

            return TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
        }

        // RT0 preserve-only save: byte-faithful, model-preserving, GATED.
        //
        // The string pool in this family is variable/text-heavy, so the existing repacking
        // Write(decoder) overload is NOT guaranteed to be raw byte-identical on a no-edit save
        // (offset ordering and string dedup canonicalize the pool, which drifts vs the disk
        // layout). For a no-edit (identity) save we instead clone the OriginalBytes the reader
        // captured and re-stamp ONLY the proven fixed scalar fields of each entry: the four u16
        // text offsets at +0x00/+0x04/+0x08/+0x0C. Keys, the data-block padding, the header and
        // the entire string pool are preserved verbatim, so an unmodified file round-trips
        // byte-for-byte (mirrors the KeyItem_File preserve-only pattern). Write(decoder) is left
        // intact for the editing UI; this overload exists purely for the identity gate.
        public byte[] WriteIdentity()
        {
            if (OriginalBytes is null || OriginalBytes.Length == 0)
                throw new InvalidDataException("Name/description text table has no preserved original bytes to re-stamp.");

            if (HeaderBytes.Length != 0x14)
                throw new InvalidDataException("Name/description text table header proof is incomplete.");

            if (EntryLength < 0x10)
                throw new InvalidDataException("Name/description text table entry length is smaller than the proven 0x10-byte header.");

            byte[] output = (byte[])OriginalBytes.Clone();

            for (int i = 0; i < Entries.Count; i++)
            {
                NameDescriptionTextTable_Entry entry = Entries[i];
                int entryOffset = 0x14 + (i * EntryLength);
                if (entryOffset + 0x10 > output.Length)
                    throw new InvalidDataException($"Name/description entry {entry.IndexLabel} extends past the preserved file length.");

                // Re-stamp ONLY the obviously-fixed scalar offset fields in place. On a no-edit
                // save these equal the originals, so the output stays byte-identical. Keys and
                // every other byte are preserved from OriginalBytes verbatim.
                TextBinary_Util.WriteUInt16(output, entryOffset + 0x00, entry.NameOffset);
                TextBinary_Util.WriteUInt16(output, entryOffset + 0x04, entry.SimplifiedNameOffset);
                TextBinary_Util.WriteUInt16(output, entryOffset + 0x08, entry.DescriptionOffset);
                TextBinary_Util.WriteUInt16(output, entryOffset + 0x0C, entry.SimplifiedDescriptionOffset);
            }

            return output;
        }
    }

    public sealed class NameDescriptionTextTable_Entry
    {
        public required int Index { get; init; }
        public required byte[] RawEntryBytes { get; init; }
        public required ushort NameOffset { get; init; }
        public required ushort NameKey { get; init; }
        public required ushort SimplifiedNameOffset { get; init; }
        public required ushort SimplifiedNameKey { get; init; }
        public required ushort DescriptionOffset { get; init; }
        public required ushort DescriptionKey { get; init; }
        public required ushort SimplifiedDescriptionOffset { get; init; }
        public required ushort SimplifiedDescriptionKey { get; init; }
        public required byte[] NameScriptBytes { get; init; }
        public required byte[] SimplifiedNameScriptBytes { get; init; }
        public required byte[] DescriptionScriptBytes { get; init; }
        public required byte[] SimplifiedDescriptionScriptBytes { get; init; }
        public required string NameText { get; set; }
        public required string SimplifiedNameText { get; set; }
        public required string DescriptionText { get; set; }
        public required string SimplifiedDescriptionText { get; set; }

        public string IndexLabel => $"Index {Index:X2}h";
        public bool HasDistinctSimplifiedName => !string.Equals(NameText, SimplifiedNameText, StringComparison.Ordinal);
        public bool HasDistinctSimplifiedDescription => !string.Equals(DescriptionText, SimplifiedDescriptionText, StringComparison.Ordinal);
        public string PreviewTitle => string.IsNullOrWhiteSpace(NameText) ? "(Unnamed entry)" : NameText;
        public string PreviewSummary => string.IsNullOrWhiteSpace(DescriptionText) ? "(No description)" : DescriptionText;

        public string SearchBlob =>
            $"{Index:X2} {NameOffset:X4} {DescriptionOffset:X4} {NameText} {SimplifiedNameText} {DescriptionText} {SimplifiedDescriptionText}";

        public string BuildHeadersSummary()
        {
            return $"Name {NameOffset:X4}h key {NameKey:X4}h{Environment.NewLine}" +
                   $"Simplified Name {SimplifiedNameOffset:X4}h key {SimplifiedNameKey:X4}h{Environment.NewLine}" +
                   $"Description {DescriptionOffset:X4}h key {DescriptionKey:X4}h{Environment.NewLine}" +
                   $"Simplified Description {SimplifiedDescriptionOffset:X4}h key {SimplifiedDescriptionKey:X4}h";
        }

        public string BuildVariantSummary()
        {
            string simplifiedName = HasDistinctSimplifiedName ? SimplifiedNameText : "(Shared with regular name)";
            string simplifiedDescription = HasDistinctSimplifiedDescription ? SimplifiedDescriptionText : "(Shared with regular description)";

            return $"Simplified Name:{Environment.NewLine}{(string.IsNullOrWhiteSpace(simplifiedName) ? "(Empty)" : simplifiedName)}{Environment.NewLine}{Environment.NewLine}" +
                   $"Simplified Description:{Environment.NewLine}{(string.IsNullOrWhiteSpace(simplifiedDescription) ? "(Empty)" : simplifiedDescription)}{Environment.NewLine}{Environment.NewLine}" +
                   $"Headers:{Environment.NewLine}{BuildHeadersSummary()}";
        }
    }
}
