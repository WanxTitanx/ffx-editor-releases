using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class NameDescriptionTextTable_File
    {
        public byte[] Write(Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            if (HeaderBytes.Length != 0x14)
                throw new InvalidDataException("Name/description text table header proof is incomplete.");

            if (EntryLength < 0x10)
                throw new InvalidDataException("Name/description text table entry length is smaller than the proven 0x10-byte header.");

            int entryTableLength = EntryCount * EntryLength;
            int dataBlockLength = entryTableLength + DataBlockPaddingBytes.Length;
            if (dataBlockLength > ushort.MaxValue)
                throw new InvalidDataException("Name/description text table data block exceeded 64KB.");

            byte[] headerBytes = (byte[])HeaderBytes.Clone();
            TextBinary_Util.WriteUInt16(headerBytes, 0x08, checked((ushort)MinIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0A, checked((ushort)MaxIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0C, checked((ushort)EntryLength));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0E, checked((ushort)dataBlockLength));

            using MemoryStream stream = new();
            stream.Write(headerBytes, 0, headerBytes.Length);

            List<byte> stringBytes = [0];
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            foreach (NameDescriptionTextTable_Entry entry in Entries)
            {
                if (entry.RawEntryBytes.Length != EntryLength)
                    throw new InvalidDataException($"Name/description entry {entry.IndexLabel} no longer matches the proven entry length.");

                byte[] rawEntryBytes = (byte[])entry.RawEntryBytes.Clone();

                ushort nameOffset = AppendLookupString(
                    stringBytes,
                    stringPool,
                    TextBinary_Util.ResolveTextBytes(entry.NameText, entry.NameScriptBytes, decoder));
                ushort simplifiedNameOffset = AppendLookupString(
                    stringBytes,
                    stringPool,
                    TextBinary_Util.ResolveTextBytes(entry.SimplifiedNameText, entry.SimplifiedNameScriptBytes, decoder));
                ushort descriptionOffset = AppendLookupString(
                    stringBytes,
                    stringPool,
                    TextBinary_Util.ResolveTextBytes(entry.DescriptionText, entry.DescriptionScriptBytes, decoder));
                ushort simplifiedDescriptionOffset = AppendLookupString(
                    stringBytes,
                    stringPool,
                    TextBinary_Util.ResolveTextBytes(entry.SimplifiedDescriptionText, entry.SimplifiedDescriptionScriptBytes, decoder));

                TextBinary_Util.WriteUInt16(rawEntryBytes, 0x00, nameOffset);
                TextBinary_Util.WriteUInt16(rawEntryBytes, 0x04, simplifiedNameOffset);
                TextBinary_Util.WriteUInt16(rawEntryBytes, 0x08, descriptionOffset);
                TextBinary_Util.WriteUInt16(rawEntryBytes, 0x0C, simplifiedDescriptionOffset);

                stream.Write(rawEntryBytes, 0, rawEntryBytes.Length);
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
                throw new InvalidDataException("Name/description text table string pool exceeded 64KB.");

            ushort offset = checked((ushort)stringBytes.Count);
            stringBytes.AddRange(scriptBytes);
            stringBytes.Add(0);
            stringPool[key] = offset;
            return offset;
        }
    }
}
