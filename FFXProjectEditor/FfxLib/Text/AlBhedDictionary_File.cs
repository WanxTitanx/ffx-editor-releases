using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed class AlBhedDictionary_File
    {
        const int EntryLengthConst = 0x04;
        const int MaxGroupIndex = 25;

        public required byte[] OriginalBytes { get; init; }
        public required int FileSize { get; init; }
        public required int EntryLength { get; init; }
        public required string VariantLabel { get; init; }
        public required IReadOnlyList<AlBhedDictionary_Entry> Entries { get; init; }

        public int EntryCount => Entries.Count;
        public int ActiveEntryCount => Entries.Count(entry => !entry.IsPadding);
        public int PaddingEntryCount => Entries.Count(entry => entry.IsPadding);
        public int DistinctGroupCount => Entries.Where(entry => !entry.IsPadding).Select(entry => entry.GroupIndex).Distinct().Count();
        public byte MinSourceCode => Entries.Where(entry => !entry.IsPadding).Min(entry => entry.SourceCode);
        public byte MaxSourceCode => Entries.Where(entry => !entry.IsPadding).Max(entry => entry.SourceCode);

        public string Summary =>
            $"{VariantLabel} · {ActiveEntryCount} active 4-byte mappings · source range {MinSourceCode:X2}h..{MaxSourceCode:X2}h · {DistinctGroupCount} distinct group bucket(s) · {PaddingEntryCount} trailing padding row(s).";

        public static AlBhedDictionary_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (bytes.Length < EntryLengthConst || bytes.Length % EntryLengthConst != 0)
                throw new InvalidDataException("Al Bhed dictionary expected a non-empty 4-byte record table.");

            List<AlBhedDictionary_Entry> entries = new(bytes.Length / EntryLengthConst);
            bool paddingStarted = false;
            byte? previousSource = null;

            for (int offset = 0; offset < bytes.Length; offset += EntryLengthConst)
            {
                byte sourceCode = bytes[offset + 0x00];
                byte mappedCode = bytes[offset + 0x01];
                ushort groupIndex = BitConverter.ToUInt16(bytes, offset + 0x02);
                bool isPadding = sourceCode == 0 && mappedCode == 0 && groupIndex == 0;
                byte[] rawRecord = new byte[EntryLengthConst];
                Array.Copy(bytes, offset, rawRecord, 0, EntryLengthConst);

                if (isPadding)
                {
                    paddingStarted = true;
                    entries.Add(new AlBhedDictionary_Entry
                    {
                        Index = offset / EntryLengthConst,
                        RawBytes = rawRecord,
                        SourceCode = 0,
                        SourceGlyph = '\0',
                        MappedCode = 0,
                        MappedGlyph = '\0',
                        GroupIndex = 0,
                        IsPadding = true
                    });
                    continue;
                }

                if (paddingStarted)
                    throw new InvalidDataException("Al Bhed dictionary contains non-padding rows after trailing padding began.");

                if (groupIndex > MaxGroupIndex)
                    throw new InvalidDataException($"Al Bhed dictionary group index {groupIndex} is outside the proven 0..{MaxGroupIndex} range.");

                if (!decoder.TryGetValue(sourceCode, out char sourceGlyph))
                    throw new InvalidDataException($"Al Bhed dictionary source byte {sourceCode:X2}h is not valid for the chosen decoder.");

                if (!decoder.TryGetValue(mappedCode, out char mappedGlyph))
                    throw new InvalidDataException($"Al Bhed dictionary mapped byte {mappedCode:X2}h is not valid for the chosen decoder.");

                if (previousSource.HasValue && sourceCode != previousSource.Value + 1)
                {
                    throw new InvalidDataException(
                        $"Al Bhed dictionary expected sequential source bytes but found {sourceCode:X2}h after {previousSource.Value:X2}h.");
                }

                previousSource = sourceCode;
                entries.Add(new AlBhedDictionary_Entry
                {
                    Index = offset / EntryLengthConst,
                    RawBytes = rawRecord,
                    SourceCode = sourceCode,
                    SourceGlyph = sourceGlyph,
                    MappedCode = mappedCode,
                    MappedGlyph = mappedGlyph,
                    GroupIndex = groupIndex,
                    IsPadding = false
                });
            }

            List<AlBhedDictionary_Entry> activeEntries = entries.Where(entry => !entry.IsPadding).ToList();
            if (activeEntries.Count == 0)
                throw new InvalidDataException("Al Bhed dictionary has no active mapping rows.");

            string variantLabel = ClassifyVariant(activeEntries, entries.Count - activeEntries.Count);
            return new AlBhedDictionary_File
            {
                OriginalBytes = bytes.ToArray(),
                FileSize = bytes.Length,
                EntryLength = EntryLengthConst,
                VariantLabel = variantLabel,
                Entries = entries
            };
        }

        // Preserve-only writer (proven pattern): clone the original bytes and re-stamp ONLY the
        // clearly-fixed scalar fields per 4-byte record (SourceCode @0, MappedCode @1, GroupIndex
        // @2 as LE u16). Everything else is preserved from the original buffer, so a no-edit
        // Read -> Write is byte-identical by construction.
        public static byte[] Write(AlBhedDictionary_File table)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(table.OriginalBytes);

            byte[] output = table.OriginalBytes.ToArray();

            if (output.Length % EntryLengthConst != 0)
                throw new InvalidDataException("Al Bhed dictionary original buffer is not a whole number of 4-byte records.");

            int recordCount = output.Length / EntryLengthConst;
            if (table.Entries.Count != recordCount)
            {
                throw new InvalidOperationException(
                    $"Al Bhed dictionary writer only supports preserving the existing record count. Expected {recordCount}, got {table.Entries.Count}.");
            }

            foreach (AlBhedDictionary_Entry entry in table.Entries)
            {
                if (entry.Index < 0 || entry.Index >= recordCount)
                    throw new InvalidOperationException($"Al Bhed dictionary entry index {entry.Index} is outside the record table range.");

                int offset = entry.Index * EntryLengthConst;

                if (entry.IsPadding)
                {
                    // Padding rows are preserved verbatim from the original buffer.
                    continue;
                }

                if (entry.GroupIndex < 0 || entry.GroupIndex > ushort.MaxValue)
                    throw new InvalidOperationException($"Al Bhed dictionary group index {entry.GroupIndex} does not fit in a LE u16.");

                output[offset + 0x00] = entry.SourceCode;
                output[offset + 0x01] = entry.MappedCode;
                output[offset + 0x02] = (byte)(entry.GroupIndex & 0xFF);
                output[offset + 0x03] = (byte)((entry.GroupIndex >> 8) & 0xFF);
            }

            return output;
        }

        static string ClassifyVariant(IReadOnlyList<AlBhedDictionary_Entry> activeEntries, int paddingCount)
        {
            byte first = activeEntries[0].SourceCode;
            byte last = activeEntries[^1].SourceCode;

            if (activeEntries.Count == 58 && paddingCount == 2 && first == 0x50 && last == 0x89)
            {
                for (int i = 26; i <= 31; i++)
                {
                    if (activeEntries[i].SourceCode != activeEntries[i].MappedCode || activeEntries[i].GroupIndex != 0)
                        throw new InvalidDataException("Latin Al Bhed dictionary variant failed its proven pass-through punctuation rows.");
                }

                return "AL BHED DICTIONARY · LATIN VARIANT · PROVEN";
            }

            if (activeEntries.Count == 80 && paddingCount == 0 && first == 0x5F && last == 0xAE)
                return "AL BHED DICTIONARY · KANA VARIANT · PROVEN";

            throw new InvalidDataException(
                $"Al Bhed dictionary matched the 4-byte mapping layout, but its active range {first:X2}h..{last:X2}h with {activeEntries.Count} rows does not match a proven variant.");
        }
    }

    public sealed class AlBhedDictionary_Entry
    {
        public required int Index { get; init; }
        public required byte[] RawBytes { get; init; }
        public required byte SourceCode { get; init; }
        public required char SourceGlyph { get; init; }
        public required byte MappedCode { get; init; }
        public required char MappedGlyph { get; init; }
        public required int GroupIndex { get; init; }
        public required bool IsPadding { get; init; }

        public string IndexLabel => $"Map {Index:X2}h";
        public string Title => IsPadding ? "(Padding)" : $"{SourceGlyph} -> {MappedGlyph}";
        public string Summary =>
            IsPadding
                ? "Trailing zero padding row."
                : $"Source {SourceCode:X2}h ('{SourceGlyph}') -> mapped {MappedCode:X2}h ('{MappedGlyph}') · group {GroupIndex}.";
        public string SearchBlob =>
            IsPadding
                ? $"padding {Index:X2}"
                : $"{Index:X2} {SourceCode:X2} {MappedCode:X2} {GroupIndex} {SourceGlyph} {MappedGlyph} {Summary}";
    }
}
