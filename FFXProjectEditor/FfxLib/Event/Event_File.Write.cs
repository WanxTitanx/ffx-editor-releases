using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Event
{
    public sealed partial class Event_File
    {
        /// <summary>
        /// TIER 1 EVENT WRITER — re-pack the EV01 container. Each chunk is re-emitted in index order on a 0x40
        /// boundary and the absolute offset table (+ EOF terminator) is recomputed. The two text chunks (JP=1,
        /// EN=4) are re-emitted via <see cref="TextTable_File.Write"/> ONLY when their text was edited; every
        /// other chunk (ATEL script 0, Unknown 2, FTCX 3) — and any unedited text chunk — is preserved
        /// byte-for-byte from the original slice. A no-edit save is therefore byte-identical (proven by the
        /// <c>--event-rt0</c> gate over the corpus), independent of whether the field-string rebuild reproduces
        /// the original pool ordering. An edited save shifts the later chunks and repoints the table.
        /// </summary>
        public byte[] Write()
        {
            // 1. Resolve each chunk's output bytes (text chunks re-emitted only if edited; rest verbatim).
            //    Absent / zero-length chunks resolve to an empty array.
            byte[][] chunkOut = new byte[Chunks.Count][];
            for (int i = 0; i < Chunks.Count; i++)
                chunkOut[i] = ResolveChunkBytes(i, Chunks[i]);

            // 2. Lay out the data region: each positioned chunk at the running cursor, padded up to a 0x40 boundary.
            //    Recompute the absolute offsets + the 0x40-aligned EOF terminator.
            //    Two distinct "empty" encodings must be preserved:
            //      - offset == 0          -> ABSENT slot: stays 0 in the table (skipped entirely).
            //      - offset != 0, len == 0 -> POSITIONED-but-empty chunk (e.g. a trailing empty text chunk whose
            //        offset == EOF): must keep a real offset == the running cursor so the table re-reads identically.
            int[] newOffsets = new int[Chunks.Count];
            List<byte> body = new(FileSize);
            int cursor = DataStart; // = HeaderBytes.Length (first present chunk offset, always 0x40 in the corpus)
            for (int i = 0; i < Chunks.Count; i++)
            {
                if (Chunks[i].Offset == 0) { newOffsets[i] = 0; continue; }

                newOffsets[i] = cursor;
                body.AddRange(chunkOut[i]);
                cursor += chunkOut[i].Length;

                int aligned = AlignUp(cursor, ChunkAlignment);
                for (int p = cursor; p < aligned; p++) body.Add(0); // verbatim chunks are already 0x40-aligned (no-op)
                cursor = aligned;
            }
            int eof = cursor;

            // 3. Clone the header and rewrite ONLY the offset table (chunk starts + EOF terminator). The magic,
            //    the 0xFFFFFFFF sentinel and the post-sentinel zero pad are preserved verbatim from the clone.
            byte[] header = (byte[])HeaderBytes.Clone();
            for (int i = 0; i < Chunks.Count; i++)
                WriteInt32(header, 0x04 + i * 4, newOffsets[i]);
            WriteInt32(header, 0x04 + Chunks.Count * 4, eof); // terminator slot, immediately before the sentinel

            // 4. Emit header + data region.
            byte[] outBytes = new byte[header.Length + body.Count];
            Array.Copy(header, 0, outBytes, 0, header.Length);
            body.CopyTo(outBytes, header.Length);
            return outBytes;
        }

        byte[] ResolveChunkBytes(int index, BinaryChunk chunk)
        {
            if (index == ChunkScript && ScriptChunkOverride != null)
                return ScriptChunkOverride; // Tier 2: edited ATEL script chunk (from the AiScript_File codec donor)
            if (index == ChunkJapaneseText && JapaneseTable != null)
                return ResolveTextChunk(JapaneseTable, FfxEncoding.JpDecoder, chunk);
            if (index == ChunkEnglishText && EnglishTable != null)
                return ResolveTextChunk(EnglishTable, FfxEncoding.UsDecoder, chunk);
            return chunk.Bytes; // ATEL script / Unknown 2 / FTCX preserved verbatim
        }

        // A text chunk is re-emitted ONLY when at least one string was actually edited; otherwise the original
        // slice is returned verbatim. This makes a no-edit save byte-identical even if the field-string rebuild
        // would re-order the pool. When edited, emit table+pool WITHOUT the captured trailing padding (its length
        // was sized for the OLD pool); the container re-pads the chunk to 0x40 in the layout loop.
        static byte[] ResolveTextChunk(TextTable_File table, Dictionary<byte, char> decoder, BinaryChunk chunk)
        {
            return TextTableEdited(table, decoder)
                ? table.Write(decoder, includeTrailingPadding: false)
                : chunk.Bytes;
        }

        static bool TextTableEdited(TextTable_File table, Dictionary<byte, char> decoder)
        {
            foreach (TextTable_Entry e in table.Entries)
            {
                if (!ByteEq(TextBinary_Util.ResolveTextBytes(e.RegularText, e.RegularScriptBytes, decoder), e.RegularScriptBytes))
                    return true;
                if (!ByteEq(TextBinary_Util.ResolveTextBytes(e.SimplifiedText, e.SimplifiedScriptBytes, decoder), e.SimplifiedScriptBytes))
                    return true;
            }
            return false;
        }

        static bool ByteEq(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        static int AlignUp(int value, int align) => (value + align - 1) & ~(align - 1);

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
