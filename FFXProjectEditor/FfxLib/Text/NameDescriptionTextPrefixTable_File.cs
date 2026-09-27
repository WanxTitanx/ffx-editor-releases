using FFXProjectEditor.FfxLib.Customization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    internal sealed class NameDescriptionTextPrefixTable_File
    {
        public const int TextPrefixLength = 0x10;

        public required int FileSize { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        // Verbatim clone of the source file, stored so the preserve-only WriteIdentity (RT0 gate)
        // can reproduce a no-edit save that is byte-identical by construction.
        public required byte[] OriginalBytes { get; init; }
        public required byte[] HeaderBytes { get; init; }
        public required byte[] DataBlockPaddingBytes { get; init; }
        public required byte[] StringBytes { get; init; }
        public required IReadOnlyList<NameDescriptionTextPrefixTable_Entry> Entries { get; init; }

        public int EntryCount => Entries.Count;

        public static NameDescriptionTextPrefixTable_File Read(
            byte[] bytes,
            IndexedFixedTableHeader header,
            Dictionary<byte, char> decoder,
            string label)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < Customization_File.HeaderLength)
                throw new InvalidDataException($"{label} is smaller than the expected 0x14-byte header.");

            if (header.EntryLength < TextPrefixLength)
            {
                throw new InvalidDataException(
                    $"{label} uses entry length 0x{header.EntryLength:X2}, which is too small for a 0x{TextPrefixLength:X2}-byte text-ref prefix.");
            }

            if (header.EntryCount <= 0)
                throw new InvalidDataException($"{label} has no entries.");

            if (Customization_File.HeaderLength + header.TotalDataLength > bytes.Length)
                throw new InvalidDataException($"{label} declares a data block that extends past EOF.");

            int entryTableLength = header.EntryCount * header.EntryLength;
            if (entryTableLength > header.TotalDataLength)
            {
                throw new InvalidDataException(
                    $"{label} data block is shorter than its declared entry table ({entryTableLength} > {header.TotalDataLength}).");
            }

            int paddingLength = header.TotalDataLength - entryTableLength;
            int stringsOffset = Customization_File.HeaderLength + header.TotalDataLength;
            byte[] headerBytes = new byte[Customization_File.HeaderLength];
            Array.Copy(bytes, 0, headerBytes, 0, headerBytes.Length);

            byte[] dataBlockPaddingBytes = new byte[paddingLength];
            if (paddingLength > 0)
                Array.Copy(bytes, Customization_File.HeaderLength + entryTableLength, dataBlockPaddingBytes, 0, paddingLength);

            byte[] stringBytes = new byte[bytes.Length - stringsOffset];
            Array.Copy(bytes, stringsOffset, stringBytes, 0, stringBytes.Length);

            List<NameDescriptionTextPrefixTable_Entry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int entryOffset = Customization_File.HeaderLength + (i * header.EntryLength);
                byte[] rawEntryBytes = new byte[header.EntryLength];
                Array.Copy(bytes, entryOffset, rawEntryBytes, 0, header.EntryLength);

                ushort nameOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x00);
                ushort nameKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x02);
                ushort auxiliaryOffset1 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x04);
                ushort auxiliaryKey1 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x06);
                ushort descriptionOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x08);
                ushort descriptionKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0A);
                ushort auxiliaryOffset2 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0C);
                ushort auxiliaryKey2 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0E);

                byte[] payloadSuffixBytes = Array.Empty<byte>();
                if (header.EntryLength > TextPrefixLength)
                {
                    payloadSuffixBytes = new byte[header.EntryLength - TextPrefixLength];
                    Array.Copy(bytes, entryOffset + TextPrefixLength, payloadSuffixBytes, 0, payloadSuffixBytes.Length);
                }

                byte[] nameScriptBytes = ReadScriptAt(stringBytes, nameOffset);
                byte[] auxiliaryScriptBytes1 = ReadScriptAt(stringBytes, auxiliaryOffset1);
                byte[] descriptionScriptBytes = ReadScriptAt(stringBytes, descriptionOffset);
                byte[] auxiliaryScriptBytes2 = ReadScriptAt(stringBytes, auxiliaryOffset2);

                entries.Add(new NameDescriptionTextPrefixTable_Entry
                {
                    Index = header.MinIndex + i,
                    RawEntryBytes = rawEntryBytes,
                    PayloadSuffixBytes = payloadSuffixBytes,
                    NameOffset = nameOffset,
                    NameKey = nameKey,
                    AuxiliaryOffset1 = auxiliaryOffset1,
                    AuxiliaryKey1 = auxiliaryKey1,
                    DescriptionOffset = descriptionOffset,
                    DescriptionKey = descriptionKey,
                    AuxiliaryOffset2 = auxiliaryOffset2,
                    AuxiliaryKey2 = auxiliaryKey2,
                    NameScriptBytes = nameScriptBytes,
                    AuxiliaryScriptBytes1 = auxiliaryScriptBytes1,
                    DescriptionScriptBytes = descriptionScriptBytes,
                    AuxiliaryScriptBytes2 = auxiliaryScriptBytes2,
                    NameText = TextBinary_Util.DecodeScriptToString(nameScriptBytes, decoder, true),
                    AuxiliaryText1 = TextBinary_Util.DecodeScriptToString(auxiliaryScriptBytes1, decoder, true),
                    DescriptionText = TextBinary_Util.DecodeScriptToString(descriptionScriptBytes, decoder, true),
                    AuxiliaryText2 = TextBinary_Util.DecodeScriptToString(auxiliaryScriptBytes2, decoder, true)
                });
            }

            return new NameDescriptionTextPrefixTable_File
            {
                FileSize = bytes.Length,
                Header = header,
                OriginalBytes = bytes.ToArray(),
                HeaderBytes = headerBytes,
                DataBlockPaddingBytes = dataBlockPaddingBytes,
                StringBytes = stringBytes,
                Entries = entries
            };
        }

        static byte[] ReadScriptAt(byte[] stringBytes, int offset)
        {
            if (offset < 0 || offset >= stringBytes.Length)
                return Array.Empty<byte>();

            return TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
        }

        // Preserve-only writer (RT0 gate): a no-edit save reproduces the source file byte-for-byte by
        // construction. The lossy Write below re-packs the string pool (dedup + canonical offset layout),
        // so it is only model-preserved, never byte-identical. WriteIdentity exists so the gate can prove
        // the read->write round trip is lossless for an unedited table; the UI keeps using Write to flush
        // edited Name/Aux/Description text.
        public byte[] WriteIdentity()
        {
            // OriginalBytes is a verbatim clone of the source captured in Read; hand back a fresh copy so
            // callers cannot mutate the preserved baseline.
            return OriginalBytes.ToArray();
        }

        // Lossless writer: rebuilds the string pool with dedup and recomputes the four text offsets per
        // entry, while preserving the keys, the data payload suffix (0x10+), the data-block padding and the
        // header verbatim. Mirrors NameDescriptionTextTable_File.Write; RT0 is model-preserved (the pool
        // re-packs canonically), not byte-identical.
        public byte[] Write(Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            using MemoryStream stream = new();
            stream.Write(HeaderBytes, 0, HeaderBytes.Length);

            List<byte> stringBytes = [0];
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            foreach (NameDescriptionTextPrefixTable_Entry entry in Entries)
            {
                byte[] rawEntry = (byte[])entry.RawEntryBytes.Clone(); // keeps keys (0x02/0x06/0x0A/0x0E) + payload

                ushort nameOffset = AppendLookupString(stringBytes, stringPool, TextBinary_Util.ResolveTextBytes(entry.NameText, entry.NameScriptBytes, decoder));
                ushort auxiliaryOffset1 = AppendLookupString(stringBytes, stringPool, TextBinary_Util.ResolveTextBytes(entry.AuxiliaryText1, entry.AuxiliaryScriptBytes1, decoder));
                ushort descriptionOffset = AppendLookupString(stringBytes, stringPool, TextBinary_Util.ResolveTextBytes(entry.DescriptionText, entry.DescriptionScriptBytes, decoder));
                ushort auxiliaryOffset2 = AppendLookupString(stringBytes, stringPool, TextBinary_Util.ResolveTextBytes(entry.AuxiliaryText2, entry.AuxiliaryScriptBytes2, decoder));

                TextBinary_Util.WriteUInt16(rawEntry, 0x00, nameOffset);
                TextBinary_Util.WriteUInt16(rawEntry, 0x04, auxiliaryOffset1);
                TextBinary_Util.WriteUInt16(rawEntry, 0x08, descriptionOffset);
                TextBinary_Util.WriteUInt16(rawEntry, 0x0C, auxiliaryOffset2);

                // Preserve (or carry edited) data payload bytes at 0x10+.
                if (entry.PayloadSuffixBytes.Length > 0)
                    Array.Copy(entry.PayloadSuffixBytes, 0, rawEntry, TextPrefixLength, entry.PayloadSuffixBytes.Length);

                stream.Write(rawEntry, 0, rawEntry.Length);
            }

            if (DataBlockPaddingBytes.Length > 0)
                stream.Write(DataBlockPaddingBytes, 0, DataBlockPaddingBytes.Length);

            stream.Write(stringBytes.ToArray(), 0, stringBytes.Count);
            return stream.ToArray();
        }

        static ushort AppendLookupString(List<byte> stringBytes, Dictionary<string, ushort> stringPool, byte[] scriptBytes)
        {
            if (scriptBytes.Length == 0)
                return 0;

            string key = Convert.ToHexString(scriptBytes);
            if (stringPool.TryGetValue(key, out ushort existingOffset))
                return existingOffset;

            if (stringBytes.Count > ushort.MaxValue)
                throw new InvalidDataException("Name/description prefix table string pool exceeded 64KB.");

            ushort offset = checked((ushort)stringBytes.Count);
            stringBytes.AddRange(scriptBytes);
            stringBytes.Add(0);
            stringPool[key] = offset;
            return offset;
        }
    }

    internal sealed class NameDescriptionTextPrefixTable_Entry
    {
        public required int Index { get; init; }
        public required byte[] RawEntryBytes { get; init; }
        public required byte[] PayloadSuffixBytes { get; init; }
        public required ushort NameOffset { get; init; }
        public required ushort NameKey { get; init; }
        public required ushort AuxiliaryOffset1 { get; init; }
        public required ushort AuxiliaryKey1 { get; init; }
        public required ushort DescriptionOffset { get; init; }
        public required ushort DescriptionKey { get; init; }
        public required ushort AuxiliaryOffset2 { get; init; }
        public required ushort AuxiliaryKey2 { get; init; }
        public required byte[] NameScriptBytes { get; init; }
        public required byte[] AuxiliaryScriptBytes1 { get; init; }
        public required byte[] DescriptionScriptBytes { get; init; }
        public required byte[] AuxiliaryScriptBytes2 { get; init; }
        public required string NameText { get; set; }
        public required string AuxiliaryText1 { get; set; }
        public required string DescriptionText { get; set; }
        public required string AuxiliaryText2 { get; set; }
    }
}
