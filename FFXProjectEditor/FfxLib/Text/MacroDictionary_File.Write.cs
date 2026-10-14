using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class MacroDictionary_File
    {
        const int ChunkSlotCount = 16;

        // Preserve-only RT0 identity writer for the macrodic.dcp container.
        //
        // The UI-facing Write(decoder) below is a *rebuild*: for any chunk it judges
        // "edited" it recomputes the 16-slot u32 offset table and re-packs a deduped
        // string pool from scratch. That repack is intentional for real edits but is
        // not guaranteed byte-identical to the original container layout (string-pool
        // dedup order, inter-chunk packing, trailing padding can all differ). It is
        // therefore a LOSSY-rebuild path with respect to a no-edit save.
        //
        // WriteIdentity is the byte-faithful path used by the RT0 gate: a no-edit
        // save is the original container, unmodified, by construction. There are no
        // separately-authorable fixed scalar fields outside the macro text pool, so
        // the preserve-only stamp is simply re-emitting the captured OriginalBytes.
        public byte[] WriteIdentity()
        {
            if (OriginalBytes is null)
                throw new InvalidOperationException("MacroDictionary_File was not read from disk; OriginalBytes is unavailable.");

            return (byte[])OriginalBytes.Clone();
        }

        // Lossless writer for the macrodic.dcp container.
        // Chunks whose text is unchanged re-emit their original bytes (byte-identical);
        // only chunks with an edited string are rebuilt (header table + deduped string pool).
        // The 16-slot u32 offset table is recomputed from the (contiguous, slot-order) bodies.
        public byte[] Write(Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            byte[]?[] bodies = new byte[ChunkSlotCount][];
            foreach (MacroDictionary_Chunk chunk in Chunks)
            {
                if (chunk.Index < 0 || chunk.Index >= ChunkSlotCount)
                    throw new InvalidDataException($"Macro dictionary chunk index {chunk.Index} is outside the 16-slot table.");

                if (!chunk.IsPresent)
                {
                    bodies[chunk.Index] = null;
                    continue;
                }

                bodies[chunk.Index] = ChunkHasTextEdits(chunk, decoder)
                    ? BuildChunkBody(chunk, decoder)
                    : chunk.RawBytes;
            }

            byte[] offsetTable = new byte[ChunkSlotCount * 4];
            int running = ChunkSlotCount * 4;
            for (int i = 0; i < ChunkSlotCount; i++)
            {
                if (bodies[i] is { Length: > 0 } body)
                {
                    WriteUInt32(offsetTable, i * 4, (uint)running);
                    running += body.Length;
                }
                else
                {
                    WriteUInt32(offsetTable, i * 4, 0);
                }
            }

            using MemoryStream stream = new();
            stream.Write(offsetTable, 0, offsetTable.Length);
            for (int i = 0; i < ChunkSlotCount; i++)
            {
                if (bodies[i] is { Length: > 0 } body)
                    stream.Write(body, 0, body.Length);
            }

            return stream.ToArray();
        }

        static bool ChunkHasTextEdits(MacroDictionary_Chunk chunk, Dictionary<byte, char> decoder)
        {
            foreach (MacroDictionary_Entry entry in chunk.Entries)
            {
                if (!TextUnchanged(entry.RegularText, entry.RegularScriptBytes, decoder))
                    return true;
                if (!TextUnchanged(entry.SimplifiedText, entry.SimplifiedScriptBytes, decoder))
                    return true;
            }

            return false;
        }

        static bool TextUnchanged(string? currentText, byte[] originalBytes, Dictionary<byte, char> decoder)
        {
            string current = TextBinary_Util.NormalizeText(currentText);
            string original = TextBinary_Util.NormalizeText(
                TextBinary_Util.DecodeScriptToString(originalBytes, decoder, true));
            return string.Equals(current, original, StringComparison.Ordinal);
        }

        static byte[] BuildChunkBody(MacroDictionary_Chunk chunk, Dictionary<byte, char> decoder)
        {
            int count = chunk.Entries.Count;
            int headerLength = count * 0x04;

            byte[] header = new byte[headerLength];
            List<byte> pool = new();
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                MacroDictionary_Entry entry = chunk.Entries[i];

                byte[] regularBytes = TextBinary_Util.ResolveTextBytes(entry.RegularText, entry.RegularScriptBytes, decoder);
                byte[] simplifiedBytes = TextBinary_Util.ResolveTextBytes(entry.SimplifiedText, entry.SimplifiedScriptBytes, decoder);

                ushort regularOffset = AppendString(pool, stringPool, regularBytes, headerLength);
                ushort simplifiedOffset = BytesEqual(regularBytes, simplifiedBytes)
                    ? regularOffset
                    : AppendString(pool, stringPool, simplifiedBytes, headerLength);

                TextBinary_Util.WriteUInt16(header, i * 0x04, regularOffset);
                TextBinary_Util.WriteUInt16(header, i * 0x04 + 0x02, simplifiedOffset);
            }

            byte[] body = new byte[headerLength + pool.Count];
            Array.Copy(header, 0, body, 0, headerLength);
            pool.CopyTo(body, headerLength);
            return body;
        }

        static ushort AppendString(List<byte> pool, Dictionary<string, ushort> stringPool, byte[] scriptBytes, int baseOffset)
        {
            string key = Convert.ToHexString(scriptBytes);
            if (stringPool.TryGetValue(key, out ushort existing))
                return existing;

            int offset = baseOffset + pool.Count;
            if (offset > ushort.MaxValue)
                throw new InvalidDataException("Macro dictionary chunk exceeded 64KB while rebuilding its string pool.");

            ushort result = (ushort)offset;
            pool.AddRange(scriptBytes);
            pool.Add(0);
            stringPool[key] = result;
            return result;
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
