using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class MacroDictionary_File
    {
        public required int FileSize { get; init; }
        public required IReadOnlyList<MacroDictionary_Chunk> Chunks { get; init; }

        // Full, untouched container bytes captured at Read time. This is what the
        // preserve-only WriteIdentity re-emits so a no-edit save is byte-identical
        // by construction (see MacroDictionary_File.Write.cs / WriteIdentity).
        public required byte[] OriginalBytes { get; init; }

        public static MacroDictionary_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            List<BinaryChunk> chunks = TextBinary_Util.ParseChunks(bytes, 16, 0, index => $"- Macro Dict {index:X2}h -");
            List<MacroDictionary_Chunk> output = new(chunks.Count);

            foreach (BinaryChunk chunk in chunks)
            {
                List<MacroDictionary_Entry> entries = new();
                if (chunk.IsPresent && chunk.Bytes.Length > 1)
                {
                    int first = TextBinary_Util.ReadUInt16(chunk.Bytes, 0x00);
                    int count = Math.Max(0, first / 0x04);

                    for (int i = 0; i < count; i++)
                    {
                        int headerOffset = i * 0x04;
                        ushort regularOffset = TextBinary_Util.ReadUInt16(chunk.Bytes, headerOffset);
                        ushort simplifiedOffset = TextBinary_Util.ReadUInt16(chunk.Bytes, headerOffset + 0x02);
                        byte[] regularBytes = TextBinary_Util.ReadNullTerminatedScript(chunk.Bytes, regularOffset);
                        byte[] simplifiedBytes = regularOffset == simplifiedOffset
                            ? regularBytes
                            : TextBinary_Util.ReadNullTerminatedScript(chunk.Bytes, simplifiedOffset);

                        entries.Add(new MacroDictionary_Entry
                        {
                            ChunkIndex = chunk.Index,
                            EntryIndex = i,
                            GlobalMacroId = chunk.Index * 0x100 + i,
                            RegularOffset = regularOffset,
                            SimplifiedOffset = simplifiedOffset,
                            RegularScriptBytes = regularBytes,
                            SimplifiedScriptBytes = simplifiedBytes,
                            RegularText = TextBinary_Util.DecodeScriptToString(regularBytes, decoder, true),
                            SimplifiedText = TextBinary_Util.DecodeScriptToString(simplifiedBytes, decoder, true)
                        });
                    }
                }

                output.Add(new MacroDictionary_Chunk
                {
                    Index = chunk.Index,
                    Label = chunk.Label,
                    IsPresent = chunk.IsPresent,
                    Offset = chunk.Offset,
                    RawBytes = chunk.Bytes,
                    Entries = entries
                });
            }

            return new MacroDictionary_File
            {
                FileSize = bytes.Length,
                OriginalBytes = bytes.ToArray(),
                Chunks = output
            };
        }
    }

    public sealed class MacroDictionary_Chunk
    {
        public required int Index { get; init; }
        public required string Label { get; init; }
        public required bool IsPresent { get; init; }
        public required int Offset { get; init; }
        public required byte[] RawBytes { get; init; }
        public required IReadOnlyList<MacroDictionary_Entry> Entries { get; init; }

        public string IndexLabel => $"Dict {Index:X2}h";
        public string Summary => $"{Entries.Count} macro strings";
    }

    public sealed class MacroDictionary_Entry
    {
        public required int ChunkIndex { get; init; }
        public required int EntryIndex { get; init; }
        public required int GlobalMacroId { get; init; }
        public required ushort RegularOffset { get; init; }
        public required ushort SimplifiedOffset { get; init; }
        public required byte[] RegularScriptBytes { get; init; }
        public required byte[] SimplifiedScriptBytes { get; init; }
        public required string RegularText { get; set; }
        public required string SimplifiedText { get; set; }

        public string MacroIdHex => $"{GlobalMacroId:X4}h";
        public string EntryLabel => $"[{ChunkIndex:X2}:{EntryIndex:X2}] {MacroIdHex}";
        public bool HasDistinctSimplified => !string.Equals(RegularText, SimplifiedText, StringComparison.Ordinal);
        public string Preview => string.IsNullOrWhiteSpace(RegularText) ? "(Empty)" : RegularText;
        public string Summary => HasDistinctSimplified
            ? $"R {RegularOffset:X4}h · S {SimplifiedOffset:X4}h"
            : $"Shared string at {RegularOffset:X4}h";
        public string SearchBlob => $"{MacroIdHex} {RegularText} {SimplifiedText}";
    }
}
