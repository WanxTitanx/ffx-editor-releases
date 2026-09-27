using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Event
{
    /// <summary>
    /// FFX EV01 event-script container (<c>*.ebp</c>). Proven layout (397/397 corpus census,
    /// docs/reverse/FFX_EVENT_EBP_CONTAINER_PROVEN_2026-06-05.md):
    /// <list type="bullet">
    /// <item><c>[0x00] u32</c> magic "EV01" (0x31305645).</item>
    /// <item><c>[0x04] u32[]</c> absolute chunk start-offset table, terminated by a <c>0xFFFFFFFF</c> sentinel.
    /// The value just before the sentinel is the EOF terminator of the last chunk. <c>offset==0</c> = chunk absent.</item>
    /// <item>Every chunk starts on a 0x40 boundary, chunks are contiguous, the first chunk is always at 0x40, so
    /// the header region is <c>[0..0x40)</c>.</item>
    /// <item>Chunk roles: 0=ATEL Script, 1=Japanese Text (<see cref="TextTable_File"/>), 2=Unknown Chunk 2,
    /// 3=FTCX, 4=English Text (<see cref="TextTable_File"/>).</item>
    /// </list>
    /// The Tier 1 writer (<see cref="Write"/>) re-packs the container, reusing <see cref="TextTable_File"/> for the
    /// edited text chunks and preserving every other chunk byte-for-byte. Gated by <c>--event-rt0</c>.
    /// </summary>
    public sealed partial class Event_File
    {
        public required string EventId { get; init; }
        public required int FileSize { get; init; }
        public required int RawHeaderValue { get; init; }
        public required IReadOnlyList<BinaryChunk> Chunks { get; init; }

        /// <summary>Header region bytes <c>[0..DataStart)</c> (magic + offset table + sentinel + pad to 0x40).
        /// Cloned + table-rewritten on <see cref="Write"/>; preserved verbatim on a no-edit save.</summary>
        public required byte[] HeaderBytes { get; init; }

        /// <summary>Absolute offset where the chunk data region begins (= first present chunk offset = 0x40).</summary>
        public required int DataStart { get; init; }

        /// <summary>Parsed Japanese text table (chunk 1), or null when chunk 1 is absent/empty. Editing its
        /// entries' text and calling <see cref="Write"/> re-emits this chunk.</summary>
        public TextTable_File? JapaneseTable { get; init; }

        /// <summary>Parsed English text table (chunk 4), or null when chunk 4 is absent/empty.</summary>
        public TextTable_File? EnglishTable { get; init; }

        /// <summary>TIER 2 hook: optional replacement bytes for the ATEL script chunk (chunk 0), produced by the
        /// read-only <c>AiScript_File</c> codec donor (operand patch / AppendCode / Rebuild). When set, <see cref="Write"/>
        /// emits these bytes for chunk 0 and re-stitches the container (0x40 realign + offset/EOF recompute), so a
        /// length-changing script edit saves to a valid <c>.ebp</c>. Null = preserve the original script chunk verbatim.</summary>
        public byte[]? ScriptChunkOverride { get; set; }

        // Convenience views kept for existing consumers (EventExplorer). Delegate to the parsed tables.
        public IReadOnlyList<TextTable_Entry> JapaneseStrings => JapaneseTable?.Entries ?? Array.Empty<TextTable_Entry>();
        public IReadOnlyList<TextTable_Entry> EnglishStrings => EnglishTable?.Entries ?? Array.Empty<TextTable_Entry>();

        public int ScriptChunkLength => GetChunkLength(0);
        public int JapaneseTextChunkLength => GetChunkLength(1);
        public int UnknownChunk2Length => GetChunkLength(2);
        public int FtcxChunkLength => GetChunkLength(3);
        public int EnglishTextChunkLength => GetChunkLength(4);

        // Chunk indices + their text decoders (the only two chunks the Tier 1 writer re-emits).
        internal const int ChunkScript = 0;
        internal const int ChunkJapaneseText = 1;
        internal const int ChunkUnknown2 = 2;
        internal const int ChunkFtcx = 3;
        internal const int ChunkEnglishText = 4;
        internal const int ChunkAlignment = 0x40;

        public static Event_File Read(string eventId, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            List<BinaryChunk> chunks = TextBinary_Util.ParseChunks(bytes, 10, 0x04, GetChunkLabel);

            // Parse the JP/EN text chunks as field-string tables so the dialogue is editable. A few event files
            // (8/397) carry field-string entries with a null (offset 0) pointer that the proven field-string
            // reader rejects; for those the table stays null and the chunk is preserved VERBATIM by the writer
            // (still byte-safe, just not text-editable yet — a documented Tier 2 frontier). NEVER let a text
            // parse failure block the container round-trip.
            TextTable_File? japaneseTable = TryReadTextChunk(chunks, ChunkJapaneseText, FfxEncoding.JpDecoder);
            TextTable_File? englishTable = TryReadTextChunk(chunks, ChunkEnglishText, FfxEncoding.UsDecoder);

            // Data region starts at the first present chunk (always 0x40 in the corpus). Everything before it is
            // the header (magic + offset table + sentinel + zero pad). Captured verbatim so a no-edit save is exact.
            int dataStart = chunks.FirstOrDefault(c => c.IsPresent)?.Offset ?? bytes.Length;
            if (dataStart < 0 || dataStart > bytes.Length)
                dataStart = Math.Min(bytes.Length, ChunkAlignment);
            byte[] headerBytes = new byte[dataStart];
            Array.Copy(bytes, 0, headerBytes, 0, dataStart);

            return new Event_File
            {
                EventId = eventId,
                FileSize = bytes.Length,
                RawHeaderValue = TextBinary_Util.ReadInt32(bytes, 0x00),
                Chunks = chunks,
                HeaderBytes = headerBytes,
                DataStart = dataStart,
                JapaneseTable = japaneseTable,
                EnglishTable = englishTable
            };
        }

        static TextTable_File? TryReadTextChunk(IReadOnlyList<BinaryChunk> chunks, int index, Dictionary<byte, char> decoder)
        {
            if (index >= chunks.Count || !chunks[index].IsPresent)
                return null;
            try { return TextTable_File.Read(chunks[index].Bytes, decoder); }
            catch { return null; } // null-pointer field-string entry etc. -> preserve the chunk verbatim
        }

        public string BuildChunkSummary()
        {
            return $"{Chunks.Count} chunks · script {ScriptChunkLength:N0} bytes · jp strings {JapaneseStrings.Count} · us strings {EnglishStrings.Count}";
        }

        /// <summary>GROW primitive: append bytes to the ATEL script chunk (chunk 0) with full offset relocation.
        /// Implementation of <c>AiScript_File.AppendCode</c>. Reloc rule: bump any AiFile-relative offset whose VALUE
        /// >= dataStart by +delta. Fields touched: codeLen@0x00 (set), declared@0x10 (bump),
        /// worker-offset-table@0x38 (each), per descriptor (only if desc>=dataStart): +0x14,+0x18,+0x1C,+0x20,+0x24.
        /// Returns new chunk 0 bytes, or null if chunk 0 is missing/invalid.
        /// Verified: 397/397 event files re-parse after append (Python PoC).</summary>
        public byte[]? GrowChunk0(byte[] appendBytes)
        {
            if (Chunks.Count < 1 || !Chunks[0].IsPresent || Chunks[0].Bytes == null)
                return null;

            byte[] ai = Chunks[0].Bytes;
            if (ai.Length < 0x38)
                return null;

            int scriptStart = ReadI32(ai, 0x30);
            int oldCodeLen = ReadI32(ai, 0);
            int declared = ReadI32(ai, 0x10);
            int dataStart = scriptStart + oldCodeLen;
            int delta = appendBytes.Length;
            int wcount = ReadI16(ai, 0x36);

            // Validate bounds
            if (scriptStart < 0x34 || dataStart < 0 || dataStart > ai.Length)
                return null;

            // Build new buffer: [0..dataStart) + appendBytes + [dataStart..end)
            byte[] o = new byte[ai.Length + delta];
            Buffer.BlockCopy(ai, 0, o, 0, dataStart);
            Buffer.BlockCopy(appendBytes, 0, o, dataStart, delta);
            Buffer.BlockCopy(ai, dataStart, o, dataStart + delta, ai.Length - dataStart);

            // Bump codeLen and declared
            WriteI32(o, 0, oldCodeLen + delta);
            WriteI32(o, 0x10, declared + delta);

            // Bump worker offset table entries
            for (int k = 0; k < wcount && k < 256; k++)
                RelocOffset(o, 0x38 + 4 * k, dataStart, delta);

            // Bump per-descriptor fields
            for (int k = 0; k < wcount && k < 256; k++)
            {
                int desc = ReadI32(ai, 0x38 + 4 * k);
                if (desc < dataStart) continue;
                desc += delta;
                foreach (int fieldOff in new[] { 0x14, 0x18, 0x1C, 0x20, 0x24 })
                    RelocOffset(o, desc + fieldOff, dataStart, delta);
            }

            return o;
        }

        /// <summary>Apply <see cref="GrowChunk0"/> and set <see cref="ScriptChunkOverride"/>.
        /// Returns true on success; false if chunk 0 is missing or the grow failed validation.</summary>
        public bool ApplyGrowChunk0(byte[] appendBytes)
        {
            byte[]? grown = GrowChunk0(appendBytes);
            if (grown == null)
                return false;
            ScriptChunkOverride = grown;
            return true;
        }

        static void RelocOffset(byte[] buf, int off, int dataStart, int delta)
        {
            if (off < 0 || off + 4 > buf.Length) return;
            int val = ReadI32(buf, off);
            if (val >= dataStart)
                WriteI32(buf, off, val + delta);
        }

        static int ReadI32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        static int ReadI16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
        static void WriteI32(byte[] b, int o, int v) { b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF); b[o + 2] = (byte)((v >> 16) & 0xFF); b[o + 3] = (byte)((v >> 24) & 0xFF); }

        int GetChunkLength(int index)
        {
            if (index < 0 || index >= Chunks.Count)
                return 0;

            return Chunks[index].Length;
        }

        static string GetChunkLabel(int index)
        {
            return index switch
            {
                ChunkScript => "ATEL Script",
                ChunkJapaneseText => "Japanese Text Chunk",
                ChunkUnknown2 => "Unknown Chunk 2",
                ChunkFtcx => "FTCX",
                ChunkEnglishText => "English Text Chunk",
                _ => $"Chunk {index:00}"
            };
        }
    }
}
