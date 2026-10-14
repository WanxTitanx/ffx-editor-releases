// ============================================================================
// EncounterTable_File — btl.bin (encounter table) reader + structural Rebuild writer
// PURPOSE : parses the 2-chunk encounter table (header 0x0E/row: id/dataOffset/formationOffset/map/unk0C +
//           payload groups->formations) and exposes a Rebuild() writer that can ADD a formation structurally
//           (grow) and save via the byte-safe encounter-rebuild path.
// WHY     : the random-encounter table is header + payload slice-by-absolute-offset; group[+3] is the "grace"
//           byte per Fahrenheit (historically mis-named "Danger" in this editor — kept for UI binding stability).
// EVIDENCE: RT0 byte-identity no-edit + grow (+1 formation +2B) on btl.bin (96 tables/192 groups/863 formations);
//           --encounter-rebuild-rt0 gate.
// MAINT   : Renaming "Danger" -> "grace" breaks UI bindings — do it as a coordinated rename + RT0. Rebuild()
//           is the structural path (slot-only Write() is separate and shape-preserving).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Battle
{
    public sealed class EncounterTable_File
    {
        public required IReadOnlyList<EncounterTable_Entry> Tables { get; init; }

        /// <summary>Original file bytes + the two chunk starts, kept so Write() preserves the whole layout
        /// byte-for-byte (RT0) and only re-stamps the slot-editable fields in place (the table is
        /// fixed-structure). Same proven preserve-patch pattern as the Monster writer.</summary>
        public byte[] OriginalBytes { get; init; } = Array.Empty<byte>();
        public int Chunk0Start { get; init; }
        public int Chunk1Start { get; init; }

        public static EncounterTable_File Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            int chunk0Start = ReadInt32(bytes, 0x04);
            int chunk1Start = ReadInt32(bytes, 0x08);
            List<ChunkData> chunks = ParseChunks(bytes, 2, 0x04);
            if (chunks.Count < 2)
                throw new InvalidOperationException("Encounter table file does not expose the expected 2 chunks.");

            byte[] headerBytes = chunks[0].Bytes;
            byte[] payloadBytes = chunks[1].Bytes;
            int tableCount = headerBytes.Length / 0x0E;

            List<EncounterTable_Entry> tables = new();
            for (int i = 0; i < tableCount; i++)
            {
                int headerOffset = i * 0x0E;
                int id = ReadUInt16(headerBytes, headerOffset);
                int dataOffset = ReadUInt16(headerBytes, headerOffset + 0x02);
                int formationOffset = ReadUInt16(headerBytes, headerOffset + 0x04);
                string map = ReadUtf8String(headerBytes, headerOffset + 0x06, 0x06);
                int unknown0C = ReadUInt16(headerBytes, headerOffset + 0x0C);

                if (dataOffset < 0 || dataOffset + 2 > payloadBytes.Length)
                    continue;

                int totalFormationCount = payloadBytes[dataOffset];
                int groupCount = payloadBytes[dataOffset + 0x01];
                int relativeGroupOffset = 0x02;
                List<EncounterTable_Group> groups = new();

                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    int absoluteGroupOffset = dataOffset + relativeGroupOffset;
                    if (absoluteGroupOffset + 0x05 > payloadBytes.Length)
                        break;

                    int formationCount = payloadBytes[absoluteGroupOffset];
                    int battlefield = ReadUInt16(payloadBytes, absoluteGroupOffset + 0x01);
                    // Fahrenheit (BtlBinGroup) rotula o byte @+3 como "grace" (período de graça de encontro),
                    // não "danger". Lemos o byte certo; o nome "Danger" é legado do editor — manter por não
                    // renomear sem quebrar a UI (bindings). Ver docs/reverse/FFX_ENCOUNTER_* .
                    int danger = payloadBytes[absoluteGroupOffset + 0x03];
                    int totalWeight = payloadBytes[absoluteGroupOffset + 0x04];

                    List<EncounterTable_Formation> formations = new();
                    for (int formationIndex = 0; formationIndex < formationCount; formationIndex++)
                    {
                        int formationOffsetInGroup = absoluteGroupOffset + 0x05 + formationIndex * 0x02;
                        if (formationOffsetInGroup + 0x02 > payloadBytes.Length)
                            break;

                        int formationId = payloadBytes[formationOffsetInGroup];
                        int weight = payloadBytes[formationOffsetInGroup + 0x01];

                        formations.Add(new EncounterTable_Formation
                        {
                            FormationId = formationId,
                            Weight = weight,
                            BattleId = $"{map}_{formationId:00}"
                        });
                    }

                    groups.Add(new EncounterTable_Group
                    {
                        GroupIndex = groupIndex,
                        FormationCount = formationCount,
                        Battlefield = battlefield,
                        Danger = danger,
                        TotalWeight = totalWeight,
                        Formations = formations
                    });

                    relativeGroupOffset += 0x05 + formationCount * 0x02;
                }

                tables.Add(new EncounterTable_Entry
                {
                    TableIndex = i,
                    Id = id,
                    DataOffset = dataOffset,
                    FormationOffset = formationOffset,
                    Map = map,
                    MapNamePadding = unknown0C,
                    TotalFormationCount = totalFormationCount,
                    GroupCount = groupCount,
                    Groups = groups
                });
            }

            return new EncounterTable_File
            {
                Tables = tables,
                OriginalBytes = bytes,
                Chunk0Start = chunk0Start,
                Chunk1Start = chunk1Start
            };
        }

        /// <summary>Slot-only writer (RT0): clone the original bytes and re-stamp ONLY the editable fields
        /// (table Id/MapNamePadding, group Battlefield/Danger/TotalWeight, formation Id/Weight) in place. No
        /// structure change -> a no-edit save is byte-identical; an edit is byte-local. Skipped/undecoded
        /// regions stay exactly as the original (they are preserved by the clone).</summary>
        public byte[] Write()
        {
            byte[] o = (byte[])OriginalBytes.Clone();
            foreach (EncounterTable_Entry t in Tables)
            {
                int h = Chunk0Start + t.TableIndex * 0x0E;
                WriteUInt16(o, h, t.Id);
                WriteUInt16(o, h + 0x0C, t.MapNamePadding);

                int rel = 0x02;
                foreach (EncounterTable_Group g in t.Groups)
                {
                    int go = Chunk1Start + t.DataOffset + rel;
                    WriteUInt16(o, go + 0x01, g.Battlefield);
                    WriteByte(o, go + 0x03, g.Danger);
                    WriteByte(o, go + 0x04, g.TotalWeight);
                    for (int j = 0; j < g.Formations.Count; j++)
                    {
                        int fo = go + 0x05 + j * 0x02;
                        WriteByte(o, fo, g.Formations[j].FormationId);
                        WriteByte(o, fo + 0x01, g.Formations[j].Weight);
                    }
                    rel += 0x05 + g.FormationCount * 0x02;
                }
            }
            return o;
        }

        /// <summary>STRUCTURAL rebuild (RT0 + grow-capable). Unlike Write() (slot-only, fixed structure),
        /// Rebuild() re-emits chunk1 (the formation/group payload) from the live Entries/Groups/Formations,
        /// so it supports VARIABLE formation/group counts and brand-new tables. It assigns fresh
        /// dataOffsets, rebuilds chunk0 (the 0x0E-byte header entries) with those offsets while keeping the
        /// non-structural header fields (Id/FormationOffset/Map/MapNamePadding), restamps the chunk table u32s at
        /// 0x04/0x08/0x0C, preserves file[0..0x04) verbatim, and reproduces the original trailing
        /// alignment/padding so a NO-EDIT Rebuild() is BYTE-IDENTICAL to the source file.
        ///
        /// Block sharing is preserved: in the source, several header entries can point to the SAME data
        /// block (same original DataOffset). Rebuild() groups tables by their original DataOffset, emits one
        /// block per distinct offset (in ascending original-offset order, matching the on-disk payload
        /// order), and re-points every sharing table at that single rebuilt block. The per-block content is
        /// taken from the FIRST table that owned the offset. Counts (TotalFormationCount/GroupCount/
        /// FormationCount) are RECOMPUTED from the live lists so a grow stays internally consistent while an
        /// unedited block reproduces the original bytes exactly.</summary>
        public byte[] Rebuild()
        {
            int headerEntrySize = 0x0E;
            int chunk0Start = Chunk0Start > 0 ? Chunk0Start : 0x10;

            // --- 1. Order distinct data blocks by original DataOffset (preserves on-disk payload order),
            //         and remember which tables share each block. ---
            // Distinct original offsets in ascending order:
            List<int> orderedOffsets = Tables
                .Select(t => t.DataOffset)
                .Distinct()
                .OrderBy(off => off)
                .ToList();

            // First (owning) table per original offset -> its decoded groups define the block content.
            Dictionary<int, EncounterTable_Entry> ownerByOffset = new();
            foreach (EncounterTable_Entry t in Tables)
            {
                if (!ownerByOffset.ContainsKey(t.DataOffset))
                    ownerByOffset[t.DataOffset] = t;
            }

            // --- 2. Emit chunk1 payload block-by-block; record old->new offset remap. ---
            Dictionary<int, int> newOffsetByOldOffset = new();
            List<byte> payload = new();
            foreach (int oldOffset in orderedOffsets)
            {
                newOffsetByOldOffset[oldOffset] = payload.Count;
                EncounterTable_Entry owner = ownerByOffset[oldOffset];

                int totalFormationCount = owner.Groups.Sum(g => g.Formations.Count);
                payload.Add((byte)(totalFormationCount & 0xFF));
                payload.Add((byte)(owner.Groups.Count & 0xFF));

                foreach (EncounterTable_Group g in owner.Groups)
                {
                    payload.Add((byte)(g.Formations.Count & 0xFF));
                    payload.Add((byte)(g.Battlefield & 0xFF));
                    payload.Add((byte)((g.Battlefield >> 8) & 0xFF));
                    payload.Add((byte)(g.Danger & 0xFF));
                    payload.Add((byte)(g.TotalWeight & 0xFF));
                    foreach (EncounterTable_Formation f in g.Formations)
                    {
                        payload.Add((byte)(f.FormationId & 0xFF));
                        payload.Add((byte)(f.Weight & 0xFF));
                    }
                }
            }

            // --- 3. Reproduce original trailing alignment/padding of chunk1. ---
            // The source records the chunk-table end (u32 @0x0C) as the original chunk1 end. If the original
            // payload was padded past the decoded data, copy those exact trailing bytes so a no-edit rebuild
            // is byte-identical. (For btl.bin there is no padding; this is defensive + general.)
            int originalChunk1Start = Chunk1Start > 0 ? Chunk1Start : (chunk0Start + Tables.Count * headerEntrySize);
            int originalChunk1End = ReadInt32(OriginalBytes, 0x0C);
            if (originalChunk1End <= originalChunk1Start || originalChunk1End > OriginalBytes.Length)
                originalChunk1End = OriginalBytes.Length;
            int originalPayloadLen = originalChunk1End - originalChunk1Start;
            int originalDecodedLen = ComputeOriginalDecodedPayloadLength();
            if (originalDecodedLen >= 0 && originalPayloadLen > originalDecodedLen
                && payload.Count == originalDecodedLen)
            {
                // No-edit case: append the original trailing padding bytes verbatim.
                for (int k = originalChunk1Start + originalDecodedLen; k < originalChunk1End; k++)
                    payload.Add(OriginalBytes[k]);
            }

            // --- 4. Rebuild chunk0 (header entries) with the new dataOffsets. ---
            // Keep the entries in their original table order (TableIndex), preserving every non-structural
            // header field; only DataOffset is restamped through the remap.
            List<EncounterTable_Entry> orderedTables = Tables.OrderBy(t => t.TableIndex).ToList();
            int headerLen = orderedTables.Count * headerEntrySize;
            byte[] header = new byte[headerLen];
            for (int i = 0; i < orderedTables.Count; i++)
            {
                EncounterTable_Entry t = orderedTables[i];
                int ho = i * headerEntrySize;
                int newDataOffset = newOffsetByOldOffset.TryGetValue(t.DataOffset, out int v) ? v : t.DataOffset;
                WriteUInt16(header, ho, t.Id);
                WriteUInt16(header, ho + 0x02, newDataOffset);
                WriteUInt16(header, ho + 0x04, t.FormationOffset);
                WriteUtf8Fixed(header, ho + 0x06, 0x06, t.Map);
                WriteUInt16(header, ho + 0x0C, t.MapNamePadding);
            }

            // --- 5. Assemble the file: preserve [0..0x04), restamp chunk table u32 @0x04/0x08/0x0C. ---
            int newChunk1Start = chunk0Start + headerLen;
            int newFileEnd = newChunk1Start + payload.Count;
            byte[] outBytes = new byte[newFileEnd];

            // Preserve the file preamble [0..chunk0Start). Includes file[0..0x04) (chunk count) verbatim and
            // the chunk table at 0x04/0x08/0x0C which we then re-stamp. Anything between 0x10 and chunk0Start
            // (none for btl.bin) is preserved verbatim too.
            int preserveLen = Math.Min(chunk0Start, OriginalBytes.Length);
            Array.Copy(OriginalBytes, 0, outBytes, 0, preserveLen);

            WriteInt32(outBytes, 0x04, chunk0Start);
            WriteInt32(outBytes, 0x08, newChunk1Start);
            WriteInt32(outBytes, 0x0C, newFileEnd);

            Array.Copy(header, 0, outBytes, chunk0Start, headerLen);
            for (int k = 0; k < payload.Count; k++)
                outBytes[newChunk1Start + k] = payload[k];

            return outBytes;
        }

        /// <summary>Length (in bytes) of the original chunk1 payload region that is actually covered by the
        /// decoded data blocks (distinct dataOffsets only), i.e. the max decoded block end. Used to detect &amp;
        /// preserve any trailing padding. Returns -1 if decoding runs off the end.</summary>
        private int ComputeOriginalDecodedPayloadLength()
        {
            int chunk1Start = Chunk1Start;
            int chunk1End = ReadInt32(OriginalBytes, 0x0C);
            if (chunk1Start <= 0 || chunk1End <= chunk1Start || chunk1End > OriginalBytes.Length)
                return -1;
            int payloadLen = chunk1End - chunk1Start;

            int maxEnd = 0;
            foreach (int off in Tables.Select(t => t.DataOffset).Distinct())
            {
                if (off < 0 || off + 2 > payloadLen) return -1;
                int groupCount = OriginalBytes[chunk1Start + off + 0x01];
                int rel = 0x02;
                for (int g = 0; g < groupCount; g++)
                {
                    int ao = off + rel;
                    if (ao + 0x05 > payloadLen) return -1;
                    int fc = OriginalBytes[chunk1Start + ao];
                    rel += 0x05 + fc * 0x02;
                }
                if (off + rel > maxEnd) maxEnd = off + rel;
            }
            return maxEnd;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length) return;
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUtf8Fixed(byte[] bytes, int offset, int length, string value)
        {
            if (offset < 0 || offset + length > bytes.Length) return;
            // Zero the field first (preserves the original NUL-padding behaviour).
            for (int k = 0; k < length; k++) bytes[offset + k] = 0;
            byte[] enc = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int n = Math.Min(enc.Length, length);
            Array.Copy(enc, 0, bytes, offset, n);
        }

        private static void WriteUInt16(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 2 > bytes.Length) return;
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteByte(byte[] bytes, int offset, int value)
        {
            if (offset >= 0 && offset < bytes.Length) bytes[offset] = (byte)(value & 0xFF);
        }

        public Dictionary<string, List<EncounterTable_Reference>> BuildReferenceLookup()
        {
            Dictionary<string, List<EncounterTable_Reference>> lookup = new(StringComparer.OrdinalIgnoreCase);

            foreach (EncounterTable_Entry table in Tables)
            {
                foreach (EncounterTable_Group group in table.Groups)
                {
                    foreach (EncounterTable_Formation formation in group.Formations)
                    {
                        if (!lookup.TryGetValue(formation.BattleId, out List<EncounterTable_Reference>? references))
                        {
                            references = new List<EncounterTable_Reference>();
                            lookup[formation.BattleId] = references;
                        }

                        references.Add(new EncounterTable_Reference
                        {
                            BattleId = formation.BattleId,
                            TableIndex = table.TableIndex,
                            TableId = table.Id,
                            Map = table.Map,
                            GroupIndex = group.GroupIndex,
                            Battlefield = group.Battlefield,
                            Danger = group.Danger,
                            FormationId = formation.FormationId,
                            Weight = formation.Weight,
                            TotalWeight = group.TotalWeight
                        });
                    }
                }
            }

            return lookup;
        }

        private static List<ChunkData> ParseChunks(byte[] bytes, int assumedChunkCount, int chunkOffset)
        {
            int chunkCount = assumedChunkCount;
            int[] offsets = new int[chunkCount + 1];

            for (int i = 0; i <= chunkCount; i++)
            {
                offsets[i] = ReadInt32(bytes, chunkOffset + i * 4);
            }

            List<ChunkData> chunks = new();
            for (int i = 0; i < chunkCount; i++)
            {
                int offset = offsets[i];
                int endOffset = offsets[i + 1];
                if (offset <= 0 || endOffset <= offset || offset > bytes.Length)
                {
                    chunks.Add(new ChunkData(Array.Empty<byte>()));
                    continue;
                }

                if (endOffset > bytes.Length)
                    endOffset = bytes.Length;

                byte[] chunkBytes = new byte[endOffset - offset];
                Array.Copy(bytes, offset, chunkBytes, 0, chunkBytes.Length);
                chunks.Add(new ChunkData(chunkBytes));
            }

            return chunks;
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return 0;

            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return bytes[offset] | (bytes[offset + 1] << 8);
        }

        private static string ReadUtf8String(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || offset + length > bytes.Length)
                return string.Empty;

            return Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\0');
        }

        private sealed class ChunkData
        {
            public ChunkData(byte[] bytes)
            {
                Bytes = bytes;
            }

            public byte[] Bytes { get; }
        }
    }

    public sealed class EncounterTable_Entry
    {
        // Settable so STRUCTURAL edits (grow-test, new tables) can mutate the decoded model in place.
        // Read() still populates them via object initializers exactly as before.
        public int TableIndex { get; set; }
        public int Id { get; set; }
        public int DataOffset { get; set; }
        public int FormationOffset { get; set; }
        public string Map { get; set; } = string.Empty;
        // IDA-proven (sub_7810F0, FFX.exe): the u16 @ +0x0C is the NUL terminator + padding of the 8-byte
        // map-name string (+0x06..+0x0D), not a gameplay field — constant 0 in 192/192 real records. Kept as
        // a preserved scalar so the slot/rebuild writers re-stamp the exact original bytes (RT0).
        // See docs/reverse/UNKNOWN_BURNDOWN_LEDGER.md (Unknown0C row, code-proved L4).
        public int MapNamePadding { get; set; }
        public int TotalFormationCount { get; set; }
        public int GroupCount { get; set; }
        public IList<EncounterTable_Group> Groups { get; set; } = new List<EncounterTable_Group>();
    }

    public sealed class EncounterTable_Group
    {
        public int GroupIndex { get; set; }
        public int FormationCount { get; set; }
        public int Battlefield { get; set; }
        public int Danger { get; set; }
        public int TotalWeight { get; set; }
        public IList<EncounterTable_Formation> Formations { get; set; } = new List<EncounterTable_Formation>();
    }

    public sealed class EncounterTable_Formation
    {
        public int FormationId { get; set; }
        public int Weight { get; set; }
        public string BattleId { get; set; } = string.Empty;
    }

    public sealed class EncounterTable_Reference
    {
        public required string BattleId { get; init; }
        public required int TableIndex { get; init; }
        public required int TableId { get; init; }
        public required string Map { get; init; }
        public required int GroupIndex { get; init; }
        public required int Battlefield { get; init; }
        public required int Danger { get; init; }
        public required int FormationId { get; init; }
        public required int Weight { get; init; }
        public required int TotalWeight { get; init; }
    }
}
