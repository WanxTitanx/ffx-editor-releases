using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    // Lossless, editable reader/writer for btl_txt.bin (battle text).
    //
    // Layout: header (0x14) + entry table (dataBlockLength bytes; one 8-byte entry = 4 u16 word
    // offsets per index) + a string pool. The pool is a SUFFIX-SHARED OVERLAPPING blob: words point
    // to arbitrary byte offsets, sometimes into the middle of another entry's string (shared tails),
    // and empty entries point into a trailing "00 47 00" padding run. Because strings overlap, the pool
    // cannot be safely repacked by dedup.
    //
    // The writer is therefore APPEND-ONLY: the original pool is preserved verbatim, an edited word's new
    // script is appended (NUL-terminated) and the word is repointed; every other reference keeps pointing
    // at unchanged bytes. So no-edit Write == the original file byte-for-byte (RT0), and editing one word
    // touches only that word (2 bytes) plus appended tail bytes (RT1). Control bytes inside scripts
    // (btl_txt uses 0x09/0x12 inline) survive via the reversible lossless codec.
    public sealed class BtlTextTable_File
    {
        public const int HeaderLength = 0x14;
        public const int ExpectedEntryLength = 0x08;
        public const int WordsPerEntry = 4;

        public required int FileSize { get; init; }
        public required int MinIndex { get; init; }
        public required int MaxIndex { get; init; }
        public required int EntryLength { get; init; }
        public required int DataBlockLength { get; init; }
        public required byte[] HeaderBytes { get; init; }
        public required byte[] EntryTablePaddingBytes { get; init; }
        public required byte[] OriginalPoolBytes { get; init; }
        public required IReadOnlyList<BtlTextEntry> Entries { get; init; }

        // Verbatim clone of the whole file as read from disk. Used by the preserve-only
        // identity writer (RT0 gate): a no-edit save is byte-identical by construction.
        public required byte[] OriginalBytes { get; init; }

        public int EntryCount => Entries.Count;

        public static BtlTextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < HeaderLength)
                throw new InvalidDataException("btl_txt.bin is smaller than the expected 0x14-byte header.");

            int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
            int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
            int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
            int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

            if (entryLength != ExpectedEntryLength)
                throw new InvalidDataException($"btl_txt.bin expected 0x{ExpectedEntryLength:X2}-byte entries, got {entryLength:X}h.");

            if (dataBlockLength <= 0 || HeaderLength + dataBlockLength > bytes.Length)
                throw new InvalidDataException("btl_txt.bin has an invalid data block length.");

            if (maxIndex < minIndex)
                throw new InvalidDataException("btl_txt.bin has an invalid index range.");

            int entryCount = (maxIndex - minIndex) + 1;
            if (entryCount <= 0)
                throw new InvalidDataException("btl_txt.bin has no entries.");

            int entryTableLength = entryCount * entryLength;
            if (entryTableLength > dataBlockLength)
                throw new InvalidDataException("btl_txt.bin data block is shorter than its declared entries.");

            byte[] headerBytes = bytes[..HeaderLength];
            byte[] entryTablePadding = bytes[(HeaderLength + entryTableLength)..(HeaderLength + dataBlockLength)];
            byte[] pool = bytes[(HeaderLength + dataBlockLength)..];

            List<BtlTextEntry> entries = new(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = HeaderLength + (i * entryLength);
                List<BtlTextRef> words = new(WordsPerEntry);
                for (int w = 0; w < WordsPerEntry; w++)
                {
                    ushort off = TextBinary_Util.ReadUInt16(bytes, entryOffset + (w * 2));
                    byte[] script = ReadScriptAt(pool, off);
                    words.Add(new BtlTextRef
                    {
                        Slot = w,
                        Offset = off,
                        ScriptBytes = script,
                        Text = FfxEncoding.DecodeScriptLossless(script, decoder)
                    });
                }

                entries.Add(new BtlTextEntry { Index = minIndex + i, Words = words });
            }

            return new BtlTextTable_File
            {
                FileSize = bytes.Length,
                MinIndex = minIndex,
                MaxIndex = maxIndex,
                EntryLength = entryLength,
                DataBlockLength = dataBlockLength,
                HeaderBytes = headerBytes,
                EntryTablePaddingBytes = entryTablePadding,
                OriginalPoolBytes = pool,
                Entries = entries,
                OriginalBytes = bytes.ToArray()
            };
        }

        // Preserve-only identity writer for the RT0 gate (no decoder, no pool repack).
        // Clones the original file bytes verbatim and re-stamps ONLY the four fixed header
        // scalars (MinIndex/MaxIndex/EntryLength/DataBlockLength) in place. dataBlockLength is
        // recomputed exactly as the live entry-table + padding length, matching the original on a
        // no-edit save. Everything else (entry-offset table, padding, overlapping string pool) is
        // preserved byte-for-byte, so a no-edit Read -> WriteIdentity is byte-identical by construction.
        public byte[] WriteIdentity()
        {
            if (HeaderBytes.Length != HeaderLength)
                throw new InvalidDataException("btl_txt.bin header proof is incomplete.");
            if (OriginalBytes.Length < HeaderLength)
                throw new InvalidDataException("btl_txt.bin original byte proof is incomplete.");

            int entryTableLength = EntryCount * EntryLength;
            int dataBlockLength = entryTableLength + EntryTablePaddingBytes.Length;

            byte[] output = (byte[])OriginalBytes.Clone();
            TextBinary_Util.WriteUInt16(output, 0x08, checked((ushort)MinIndex));
            TextBinary_Util.WriteUInt16(output, 0x0A, checked((ushort)MaxIndex));
            TextBinary_Util.WriteUInt16(output, 0x0C, checked((ushort)EntryLength));
            TextBinary_Util.WriteUInt16(output, 0x0E, checked((ushort)dataBlockLength));
            return output;
        }

        public byte[] Write(Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            if (HeaderBytes.Length != HeaderLength)
                throw new InvalidDataException("btl_txt.bin header proof is incomplete.");

            int entryTableLength = EntryCount * EntryLength;
            int dataBlockLength = entryTableLength + EntryTablePaddingBytes.Length;

            byte[] headerBytes = (byte[])HeaderBytes.Clone();
            TextBinary_Util.WriteUInt16(headerBytes, 0x08, checked((ushort)MinIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0A, checked((ushort)MaxIndex));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0C, checked((ushort)EntryLength));
            TextBinary_Util.WriteUInt16(headerBytes, 0x0E, checked((ushort)dataBlockLength));

            // Append-only pool: start from the original bytes; edited words append their new script.
            List<byte> pool = [.. OriginalPoolBytes];
            byte[] entryTable = new byte[entryTableLength];

            for (int i = 0; i < EntryCount; i++)
            {
                BtlTextEntry entry = Entries[i];
                for (int w = 0; w < WordsPerEntry; w++)
                {
                    BtlTextRef wordRef = entry.Words[w];
                    ushort offset = ResolveWordOffset(pool, wordRef, decoder);
                    TextBinary_Util.WriteUInt16(entryTable, (i * EntryLength) + (w * 2), offset);
                }
            }

            using MemoryStream stream = new();
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(entryTable, 0, entryTable.Length);
            stream.Write(EntryTablePaddingBytes, 0, EntryTablePaddingBytes.Length);
            stream.Write(pool.ToArray(), 0, pool.Count);
            return stream.ToArray();
        }

        static ushort ResolveWordOffset(List<byte> pool, BtlTextRef wordRef, Dictionary<byte, char> decoder)
        {
            string original = FfxEncoding.DecodeScriptLossless(wordRef.ScriptBytes, decoder);
            if (string.Equals(wordRef.Text ?? string.Empty, original, StringComparison.Ordinal))
                return wordRef.Offset; // unchanged: keep the original (possibly shared/mid-blob) offset

            if (!FfxEncoding.TryEncodeScriptLossless(wordRef.Text, decoder, out byte[] encoded, out string? error))
                throw new InvalidDataException($"btl_txt.bin entry word could not be encoded: {error}");

            if (pool.Count + encoded.Length + 1 > ushort.MaxValue)
                throw new InvalidDataException("btl_txt.bin string pool exceeded 64KB after appending edits.");

            ushort newOffset = checked((ushort)pool.Count);
            pool.AddRange(encoded);
            pool.Add(0);
            return newOffset;
        }

        static byte[] ReadScriptAt(byte[] pool, int offset)
        {
            if (offset < 0 || offset >= pool.Length)
                return Array.Empty<byte>();

            return TextBinary_Util.ReadNullTerminatedScript(pool, offset);
        }
    }

    public sealed class BtlTextEntry
    {
        public required int Index { get; init; }
        public required IReadOnlyList<BtlTextRef> Words { get; init; }

        public string IndexLabel => $"Index {Index:X2}h";
    }

    public sealed class BtlTextRef
    {
        public required int Slot { get; init; }
        public required ushort Offset { get; init; }
        public required byte[] ScriptBytes { get; init; }
        public required string Text { get; set; }

        public string SlotLabel => Slot switch { 0 => "W0", 1 => "W1", 2 => "W2", _ => "W3" };
        public bool IsEmpty => ScriptBytes.Length == 0;
    }
}
