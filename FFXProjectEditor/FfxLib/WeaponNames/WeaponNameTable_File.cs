using FFXProjectEditor.FfxLib.Text;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.WeaponNames
{
    // Lossless reader/writer for w_name.bin (weapon/armor name table).
    //
    // Layout (proven on the corpus; same indexed-fixed-table + string-pool family as the
    // name/desc kernel tables):
    //   0x00  header (0x14 bytes): prefix[8], minIndex u16 @0x08, maxIndex u16 @0x0A,
    //                              entryLength u16 @0x0C, totalDataLength u16 @0x0E, tail[4] @0x10
    //   0x14  entry table: (maxIndex-minIndex+1) entries, each entryLength (0x48) bytes:
    //           0x00  7 regular name refs   : [offset u16][key u16]  (per playable character)
    //           0x1C  7 simplified name refs: [offset u16][key u16]
    //           0x38  7 model words u16      : high nibble = asset category (0x4 EquipmentModel)
    //           0x46  final word u16
    //   ....  optional data-block padding (totalDataLength - entryTableLength; 0 in the US corpus)
    //   ....  string pool: NUL-terminated FFX scripts, referenced by the offsets above
    //
    // The writer mirrors NameDescriptionTextTable_File.Write exactly: it rebuilds the string pool
    // from scratch with dedup, recomputes ONLY the text offsets, and preserves keys, model words,
    // the final word, the header and any data-block padding verbatim. RT0 invariant is therefore
    // model-preservation + idempotency (offsets may be re-packed canonically), not raw byte identity.
    public sealed class WeaponNameTable_File
    {
        public const int HeaderLength = 0x14;
        public const int CharacterCount = 0x07;
        public const int RegularBlockOffset = 0x00;
        public const int SimplifiedBlockOffset = 0x1C;
        public const int ModelBlockOffset = 0x38;
        public const int FinalWordOffset = 0x46;
        public const int ExpectedEntryLength = 0x48;

        static readonly WeaponNameCharacterSlot[] CharacterSlots =
        [
            new("T", "Tidus"),
            new("Y", "Yuna"),
            new("A", "Auron"),
            new("K", "Kimahri"),
            new("W", "Wakka"),
            new("L", "Lulu"),
            new("R", "Rikku")
        ];

        public required int FileSize { get; init; }
        public required int MinIndex { get; init; }
        public required int MaxIndex { get; init; }
        public required int EntryLength { get; init; }
        public required int DataBlockLength { get; init; }
        public required byte[] HeaderBytes { get; init; }
        public required byte[] DataBlockPaddingBytes { get; init; }
        public required IReadOnlyList<WeaponNameEntry> Entries { get; init; }

        // Verbatim clone of the source file, captured by Read. Used by WriteIdentity for the
        // RT0 byte-identity gate (the lossy Write above re-packs the string pool and drifts).
        public required byte[] OriginalBytes { get; init; }

        public int EntryCount => Entries.Count;

        public static WeaponNameTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < HeaderLength)
                throw new InvalidDataException("w_name.bin is smaller than the expected 0x14-byte header.");

            int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
            int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
            int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
            int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

            if (entryLength != ExpectedEntryLength)
                throw new InvalidDataException($"w_name.bin expected 0x{ExpectedEntryLength:X2}-byte entries, got {entryLength:X}h.");

            if (dataBlockLength <= 0 || HeaderLength + dataBlockLength > bytes.Length)
                throw new InvalidDataException("w_name.bin has an invalid data block length.");

            if (maxIndex < minIndex)
                throw new InvalidDataException("w_name.bin has an invalid index range.");

            int entryCount = (maxIndex - minIndex) + 1;
            if (entryCount <= 0)
                throw new InvalidDataException("w_name.bin has no entries.");

            int entryTableLength = entryCount * entryLength;
            if (entryTableLength > dataBlockLength)
                throw new InvalidDataException("w_name.bin data block is shorter than its declared entries.");

            int paddingLength = dataBlockLength - entryTableLength;

            byte[] headerBytes = new byte[HeaderLength];
            Array.Copy(bytes, 0, headerBytes, 0, headerBytes.Length);

            byte[] dataBlockPaddingBytes = new byte[paddingLength];
            if (paddingLength > 0)
                Array.Copy(bytes, HeaderLength + entryTableLength, dataBlockPaddingBytes, 0, paddingLength);

            byte[] stringBytes = new byte[bytes.Length - (HeaderLength + dataBlockLength)];
            Array.Copy(bytes, HeaderLength + dataBlockLength, stringBytes, 0, stringBytes.Length);

            List<WeaponNameEntry> entries = new(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = HeaderLength + (i * entryLength);
                byte[] rawEntryBytes = new byte[entryLength];
                Array.Copy(bytes, entryOffset, rawEntryBytes, 0, entryLength);

                List<WeaponNameTextRef> regularNames = new(CharacterCount);
                List<WeaponNameTextRef> simplifiedNames = new(CharacterCount);
                List<WeaponNameModelRef> models = new(CharacterCount);

                for (int slot = 0; slot < CharacterCount; slot++)
                {
                    WeaponNameCharacterSlot character = CharacterSlots[slot];
                    regularNames.Add(ReadTextRef(rawEntryBytes, RegularBlockOffset + (slot * 0x04), character, stringBytes, decoder));
                    simplifiedNames.Add(ReadTextRef(rawEntryBytes, SimplifiedBlockOffset + (slot * 0x04), character, stringBytes, decoder));
                    models.Add(ReadModelRef(rawEntryBytes, ModelBlockOffset + (slot * 0x02), character));
                }

                entries.Add(new WeaponNameEntry
                {
                    Index = minIndex + i,
                    RawEntryBytes = rawEntryBytes,
                    RegularNames = regularNames,
                    SimplifiedNames = simplifiedNames,
                    Models = models,
                    FinalWord = TextBinary_Util.ReadUInt16(rawEntryBytes, FinalWordOffset)
                });
            }

            return new WeaponNameTable_File
            {
                FileSize = bytes.Length,
                MinIndex = minIndex,
                MaxIndex = maxIndex,
                EntryLength = entryLength,
                DataBlockLength = dataBlockLength,
                HeaderBytes = headerBytes,
                DataBlockPaddingBytes = dataBlockPaddingBytes,
                Entries = entries,
                OriginalBytes = (byte[])bytes.Clone()
            };
        }

        public byte[] Write(Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            if (HeaderBytes.Length != HeaderLength)
                throw new InvalidDataException("w_name.bin header proof is incomplete.");

            if (EntryLength != ExpectedEntryLength)
                throw new InvalidDataException($"w_name.bin entry length is not the proven 0x{ExpectedEntryLength:X2} bytes.");

            int entryTableLength = EntryCount * EntryLength;
            int dataBlockLength = entryTableLength + DataBlockPaddingBytes.Length;
            if (dataBlockLength > ushort.MaxValue)
                throw new InvalidDataException("w_name.bin data block exceeded 64KB.");

            byte[] headerBytes = (byte[])HeaderBytes.Clone();
            TextBinary_Util.WriteUInt16(headerBytes, 0x08, checked((ushort)MinIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0A, checked((ushort)MaxIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0C, checked((ushort)EntryLength));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0E, checked((ushort)dataBlockLength));

            using MemoryStream stream = new();
            stream.Write(headerBytes, 0, headerBytes.Length);

            List<byte> stringBytes = [0];
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            foreach (WeaponNameEntry entry in Entries)
            {
                if (entry.RawEntryBytes.Length != EntryLength)
                    throw new InvalidDataException($"w_name.bin entry {entry.IndexLabel} no longer matches the proven entry length.");

                // Clone preserves keys, model words, the final word and any reserved bytes verbatim;
                // only the text offsets below are overwritten.
                byte[] rawEntryBytes = (byte[])entry.RawEntryBytes.Clone();

                for (int slot = 0; slot < CharacterCount; slot++)
                {
                    WeaponNameTextRef regular = entry.RegularNames[slot];
                    WeaponNameTextRef simplified = entry.SimplifiedNames[slot];

                    ushort regularOffset = AppendLookupString(
                        stringBytes,
                        stringPool,
                        TextBinary_Util.ResolveTextBytes(regular.Text, regular.ScriptBytes, decoder));
                    ushort simplifiedOffset = AppendLookupString(
                        stringBytes,
                        stringPool,
                        TextBinary_Util.ResolveTextBytes(simplified.Text, simplified.ScriptBytes, decoder));

                    TextBinary_Util.WriteUInt16(rawEntryBytes, RegularBlockOffset + (slot * 0x04), regularOffset);
                    TextBinary_Util.WriteUInt16(rawEntryBytes, SimplifiedBlockOffset + (slot * 0x04), simplifiedOffset);
                }

                stream.Write(rawEntryBytes, 0, rawEntryBytes.Length);
            }

            if (DataBlockPaddingBytes.Length > 0)
                stream.Write(DataBlockPaddingBytes, 0, DataBlockPaddingBytes.Length);

            stream.Write(stringBytes.ToArray(), 0, stringBytes.Count);
            return stream.ToArray();
        }

        // Preserve-only writer for the RT0 byte-identity gate.
        //
        // The lossy Write above rebuilds the string pool with dedup and re-packs the text offsets,
        // so a no-edit save is NOT guaranteed byte-identical (offsets can be re-ordered/de-duplicated).
        // WriteIdentity instead clones the verbatim source bytes and re-stamps ONLY the clearly-fixed
        // per-entry scalar fields back into their original slots: the 7 model words and the final word.
        // The header, the string pool, all text offsets+keys and any data-block padding are preserved
        // verbatim from OriginalBytes, so an unedited table round-trips byte-for-byte by construction.
        public byte[] WriteIdentity()
        {
            if (OriginalBytes.Length < HeaderLength)
                throw new InvalidDataException("w_name.bin original-bytes proof is incomplete.");

            if (EntryLength != ExpectedEntryLength)
                throw new InvalidDataException($"w_name.bin entry length is not the proven 0x{ExpectedEntryLength:X2} bytes.");

            int entryTableLength = EntryCount * EntryLength;
            if (HeaderLength + entryTableLength > OriginalBytes.Length)
                throw new InvalidDataException("w_name.bin entry table extends past the original file length.");

            byte[] output = (byte[])OriginalBytes.Clone();

            for (int i = 0; i < Entries.Count; i++)
            {
                WeaponNameEntry entry = Entries[i];
                int entryOffset = HeaderLength + (i * EntryLength);

                // Re-stamp the 7 model words (high nibble = asset category, low 12 bits = asset index).
                for (int slot = 0; slot < CharacterCount; slot++)
                {
                    TextBinary_Util.WriteUInt16(output, entryOffset + ModelBlockOffset + (slot * 0x02), entry.Models[slot].RawValue);
                }

                // Re-stamp the final word; everything else in the entry/string pool stays verbatim.
                TextBinary_Util.WriteUInt16(output, entryOffset + FinalWordOffset, entry.FinalWord);
            }

            return output;
        }

        static ushort AppendLookupString(List<byte> stringBytes, Dictionary<string, ushort> stringPool, byte[] scriptBytes)
        {
            if (scriptBytes.Length == 0)
                return 0;

            string key = Convert.ToHexString(scriptBytes);
            if (stringPool.TryGetValue(key, out ushort existingOffset))
                return existingOffset;

            if (stringBytes.Count > ushort.MaxValue)
                throw new InvalidDataException("w_name.bin string pool exceeded 64KB.");

            ushort offset = checked((ushort)stringBytes.Count);
            stringBytes.AddRange(scriptBytes);
            stringBytes.Add(0);
            stringPool[key] = offset;
            return offset;
        }

        static WeaponNameTextRef ReadTextRef(byte[] rawEntry, int offset, WeaponNameCharacterSlot character, byte[] stringBytes, Dictionary<byte, char> decoder)
        {
            ushort textOffset = TextBinary_Util.ReadUInt16(rawEntry, offset + 0x00);
            ushort key = TextBinary_Util.ReadUInt16(rawEntry, offset + 0x02);
            byte[] scriptBytes = ReadScriptAt(stringBytes, textOffset);

            return new WeaponNameTextRef
            {
                CharacterCode = character.Code,
                CharacterName = character.Name,
                Offset = textOffset,
                Key = key,
                ScriptBytes = scriptBytes,
                Text = TextBinary_Util.DecodeScriptToString(scriptBytes, decoder, true)
            };
        }

        static WeaponNameModelRef ReadModelRef(byte[] rawEntry, int offset, WeaponNameCharacterSlot character)
        {
            ushort rawValue = TextBinary_Util.ReadUInt16(rawEntry, offset);
            return new WeaponNameModelRef
            {
                CharacterCode = character.Code,
                CharacterName = character.Name,
                RawValue = rawValue
            };
        }

        static byte[] ReadScriptAt(byte[] stringBytes, int offset)
        {
            if (offset <= 0 || offset >= stringBytes.Length)
                return Array.Empty<byte>();

            return TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
        }

        readonly record struct WeaponNameCharacterSlot(string Code, string Name);
    }

    public sealed class WeaponNameEntry
    {
        public required int Index { get; init; }
        public required byte[] RawEntryBytes { get; init; }
        public required IReadOnlyList<WeaponNameTextRef> RegularNames { get; init; }
        public required IReadOnlyList<WeaponNameTextRef> SimplifiedNames { get; init; }
        public required IReadOnlyList<WeaponNameModelRef> Models { get; init; }
        public required ushort FinalWord { get; init; }

        public string IndexLabel => $"Index {Index:X2}h";
    }

    public sealed class WeaponNameTextRef
    {
        public required string CharacterCode { get; init; }
        public required string CharacterName { get; init; }
        public required ushort Offset { get; init; }
        public required ushort Key { get; init; }
        public required byte[] ScriptBytes { get; init; }
        public required string Text { get; set; }

        public bool IsOffsetZero => Offset == 0;
    }

    public sealed class WeaponNameModelRef
    {
        public required string CharacterCode { get; init; }
        public required string CharacterName { get; init; }
        public required ushort RawValue { get; init; }

        // High nibble = asset category (0x4 == EquipmentModel across the US corpus); low 12 bits = asset index.
        public byte AssetCategory => (byte)((RawValue & 0xF000) >> 12);
        public ushort AssetIndex => (ushort)(RawValue & 0x0FFF);
        public string DisplayLabel => AssetCategory == 0x4
            ? $"EquipmentModel asset {AssetIndex:X3}h"
            : $"{RawValue:X4}h";
    }
}
