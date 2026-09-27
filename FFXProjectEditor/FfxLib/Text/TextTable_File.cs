using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class TextTable_File
    {
        public required int FileSize { get; init; }
        public required int HeaderLength { get; init; }
        public required int EntryCount { get; init; }
        public required IReadOnlyList<TextTable_Entry> Entries { get; init; }

        /// <summary>Bytes that follow the reconstructable string pool up to EOF (zero alignment
        /// padding to a 4-byte boundary in the proven samples). The header has no total-length or
        /// data-block-length field, so the writer cannot derive this; capturing it here lets a
        /// no-edit Read-&gt;Write stay tail-faithful. Mirrors NameDescription's DataBlockPaddingBytes.</summary>
        public required byte[] TrailingPaddingBytes { get; init; }

        public static TextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            if (PointerScriptTable_File.MatchesProvenLayout(bytes))
            {
                throw new InvalidDataException(
                    "Field-string table matched the proven 4-byte pointer-script family instead of the proven 8-byte field-string family.");
            }

            if (bytes.Length <= 1)
            {
                return new TextTable_File
                {
                    FileSize = bytes.Length,
                    HeaderLength = 0,
                    EntryCount = 0,
                    Entries = Array.Empty<TextTable_Entry>(),
                    TrailingPaddingBytes = Array.Empty<byte>()
                };
            }

            int first = TextBinary_Util.ReadUInt16(bytes, 0x00);
            if (first <= 0 || first % 0x08 != 0)
                throw new InvalidDataException($"Field-string table has an invalid header length: {first:X}h.");

            int count = first / 0x08;
            int headerLength = count * 0x08;
            if (headerLength > bytes.Length)
                throw new InvalidDataException("Field-string table header runs past EOF.");

            List<TextTable_Entry> entries = new(count);
            // Highest byte index consumed by a referenced null-terminated script (offset + length + 1
            // for the terminator). Everything from here to EOF is trailing alignment padding.
            int stringPoolEnd = headerLength;

            for (int i = 0; i < count; i++)
            {
                int baseOffset = i * 0x08;
                byte[] rawEntryBytes = new byte[0x08];
                Array.Copy(bytes, baseOffset, rawEntryBytes, 0, rawEntryBytes.Length);

                int regularHeader = TextBinary_Util.ReadInt32(bytes, baseOffset);
                int simplifiedHeader = TextBinary_Util.ReadInt32(bytes, baseOffset + 0x04);

                ushort regularOffset = (ushort)(regularHeader & 0xFFFF);
                byte regularFlags = (byte)((regularHeader >> 16) & 0xFF);
                byte regularChoices = (byte)((regularHeader >> 24) & 0xFF);

                ushort simplifiedOffset = (ushort)(simplifiedHeader & 0xFFFF);
                byte simplifiedFlags = (byte)((simplifiedHeader >> 16) & 0xFF);
                byte simplifiedChoices = (byte)((simplifiedHeader >> 24) & 0xFF);

                byte[] regularBytes = ReadScriptAt(bytes, regularOffset, headerLength, $"field-string {i:X2}h regular");
                byte[] simplifiedBytes = regularOffset == simplifiedOffset
                    ? regularBytes
                    : ReadScriptAt(bytes, simplifiedOffset, headerLength, $"field-string {i:X2}h simplified");

                stringPoolEnd = Math.Max(stringPoolEnd, regularOffset + regularBytes.Length + 1);
                stringPoolEnd = Math.Max(stringPoolEnd, simplifiedOffset + simplifiedBytes.Length + 1);

                entries.Add(new TextTable_Entry
                {
                    Index = i,
                    RawEntryBytes = rawEntryBytes,
                    RegularOffset = regularOffset,
                    RegularFlags = regularFlags,
                    RegularChoices = regularChoices,
                    SimplifiedOffset = simplifiedOffset,
                    SimplifiedFlags = simplifiedFlags,
                    SimplifiedChoices = simplifiedChoices,
                    RegularScriptBytes = regularBytes,
                    SimplifiedScriptBytes = simplifiedBytes,
                    RegularText = TextBinary_Util.DecodeScriptToString(regularBytes, decoder, true),
                    SimplifiedText = TextBinary_Util.DecodeScriptToString(simplifiedBytes, decoder, true)
                });
            }

            int paddingLength = Math.Max(0, bytes.Length - stringPoolEnd);
            byte[] trailingPaddingBytes = new byte[paddingLength];
            if (paddingLength > 0)
                Array.Copy(bytes, stringPoolEnd, trailingPaddingBytes, 0, paddingLength);

            return new TextTable_File
            {
                FileSize = bytes.Length,
                HeaderLength = headerLength,
                EntryCount = entries.Count,
                Entries = entries,
                TrailingPaddingBytes = trailingPaddingBytes
            };
        }

        static byte[] ReadScriptAt(byte[] bytes, int offset, int headerLength, string label)
        {
            if (offset < headerLength || offset >= bytes.Length)
                throw new InvalidDataException($"{label} offset {offset:X4}h is outside the string area.");

            if (TextBinary_Util.FindNullTerminator(bytes, offset) < 0)
                throw new InvalidDataException($"{label} is missing a terminating NULL byte.");

            return TextBinary_Util.ReadNullTerminatedScript(bytes, offset);
        }
    }

    public sealed class TextTable_Entry
    {
        public required int Index { get; init; }
        public required byte[] RawEntryBytes { get; init; }
        public required ushort RegularOffset { get; init; }
        public required byte RegularFlags { get; init; }
        public required byte RegularChoices { get; init; }
        public required ushort SimplifiedOffset { get; init; }
        public required byte SimplifiedFlags { get; init; }
        public required byte SimplifiedChoices { get; init; }
        public required byte[] RegularScriptBytes { get; init; }
        public required byte[] SimplifiedScriptBytes { get; init; }
        public required string RegularText { get; set; }
        public required string SimplifiedText { get; set; }

        public string IndexLabel => $"String {Index:X2}h";
        public bool HasDistinctSimplified => !string.Equals(RegularText, SimplifiedText, StringComparison.Ordinal);
        public string Preview => string.IsNullOrWhiteSpace(RegularText) ? "(Empty)" : RegularText;
        public string FlagsSummary => $"R Ofst {RegularOffset:X4}h · Flags {RegularFlags:X2}h · Choices {RegularChoices:X2}h";
        public string SimplifiedSummary => HasDistinctSimplified
            ? $"S Ofst {SimplifiedOffset:X4}h · Flags {SimplifiedFlags:X2}h · Choices {SimplifiedChoices:X2}h"
            : "Simplified string is shared with regular string.";
        public string SearchBlob => $"{Index:X2} {RegularOffset:X4} {SimplifiedOffset:X4} {RegularText} {SimplifiedText}";
    }
}
