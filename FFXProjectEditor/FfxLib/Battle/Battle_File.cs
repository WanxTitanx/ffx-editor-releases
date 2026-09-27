// ============================================================================
// Battle_File — battle file (.bin) container: chunked read + typed formation/area + byte-safe writes
// PURPOSE : parses the chunked battle bin (chunk0 script, chunk1 worker map, chunk2 formation, chunk3 areas,
//           chunk4+ text/ftcx), exposes typed Formation + AreaSummary, and supports writing the 8-slot
//           formation region (16 bytes) byte-safely.
// WHY     : the battle file is a chunk container — the 4-chunk form is flagged unsafe (IsUnsafeFormat);
//           formation slot writes are guarded to the 16-byte window (FormationSlotsOffset +0x0C).
// EVIDENCE: RT0 baseline; FormationSlotsLength = 0x10 (8 ushorts); chunk count marker = RawChunkValue - 1.
// MAINT   : CanWriteFormation is the gate for any formation write (chunk2 present + >= 0x1C). Changing the
//           chunk layout breaks every reader — re-prove with RT0. F2_no_formation label = i18n, keep via Strings.
// ============================================================================
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battle
{
    public sealed class Battle_File
    {
        public required string BattleId { get; init; }
        public required int FileSize { get; init; }
        public required int RawChunkValue { get; init; }
        public required int ChunkCount { get; init; }
        public required IReadOnlyList<Battle_Chunk> Chunks { get; init; }
        public required byte[] OriginalBytes { get; init; }
        public Battle_Formation? Formation { get; init; }
        public Battle_AreaSummary? AreaSummary { get; init; }

        public bool IsUnsafeFormat => ChunkCount == 4;
        public int ScriptChunkLength => GetChunkLength(0);
        public int WorkerMapChunkLength => GetChunkLength(1);
        public int FormationChunkLength => GetChunkLength(2);
        public int BattleAreasChunkLength => GetChunkLength(3);
        public int PrimaryTextChunkLength => GetChunkLength(4);
        public int FtcxChunkLength => GetChunkLength(5);
        public int EnglishTextChunkLength => GetChunkLength(6);

        public bool HasPrimaryTextChunk => PrimaryTextChunkLength > 0;
        public bool HasFtcxChunk => FtcxChunkLength > 0;
        public bool HasEnglishTextChunk => EnglishTextChunkLength > 0;
        public bool CanWriteFormation => Formation != null && GetChunkOffset(2) > 0 && FormationChunkLength >= 0x1C;

        // SPIRA FORGE v0.2: absolute offsets of the formation chunk + its 8 monster-slot region.
        // WriteWithFormationSlots mutates exactly the 16 bytes at FormationSlotsOffset (8 ushorts).
        public int FormationChunkOffset => GetChunkOffset(2);
        public int FormationSlotsOffset => CanWriteFormation ? GetChunkOffset(2) + 0x0C : -1;
        public const int FormationSlotsLength = 0x10; // 8 ushorts

        public string FormationLabel => Formation?.GetFormationLabel() ?? Strings.F2_no_formation_09772db3;

        public static Battle_File Read(string battleId, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length < 8)
                throw new InvalidDataException("Battle file too small to contain a valid chunk header.");

            int rawChunkValue = ReadInt32(bytes, 0x00);
            int chunkCount = rawChunkValue - 1;
            if (chunkCount <= 0)
                throw new InvalidDataException($"Invalid chunk count marker: {rawChunkValue}");

            List<Battle_Chunk> chunks = ParseChunks(bytes, chunkCount, 0x04);

            return new Battle_File
            {
                BattleId = battleId,
                FileSize = bytes.Length,
                RawChunkValue = rawChunkValue,
                ChunkCount = chunks.Count,
                Chunks = chunks,
                OriginalBytes = bytes.ToArray(),
                Formation = Battle_Formation.TryRead(GetChunkBytes(chunks, 2)),
                AreaSummary = Battle_AreaSummary.TryRead(GetChunkBytes(chunks, 3))
            };
        }

        public byte[] WriteWithFormationSlots(IReadOnlyList<ushort> rawMonsterIds)
        {
            if (!CanWriteFormation)
                throw new InvalidOperationException("This battle file does not expose a writable formation chunk.");

            if (rawMonsterIds.Count != 8)
                throw new ArgumentException("Formation writing expects exactly 8 slot values.", nameof(rawMonsterIds));

            byte[] updatedBytes = OriginalBytes.ToArray();
            int formationOffset = GetChunkOffset(2);

            for (int i = 0; i < rawMonsterIds.Count; i++)
            {
                WriteUInt16(updatedBytes, formationOffset + 0x0C + i * 2, rawMonsterIds[i]);
            }

            return updatedBytes;
        }

        // Formation Authoring Tier 1 — chunk3 (battle areas / actor positions). The editor-side entry point that
        // drag-to-place writes through: it mutates ONLY the X/Y/Z floats of one role's anchor array (+0x20, stride
        // 16, W preserved) by delegating to the proven, position-only BattleArenaPositionWriter — already gated
        // byte-safe on all 862 writable battles by RuntimeTools/BattleArenaPositionLab. No grow/shrink: the coord
        // count must match the on-disk anchor count.
        public int PositionChunkOffset => GetChunkOffset(3);
        public int PositionChunkLength => GetChunkLength(3);
        public bool CanWritePositions => PositionChunkOffset > 0 && PositionChunkLength >= 0x30;

        public byte[] WriteWithMonsterPositions(
            int areaIndex,
            FFXProjectEditor.FfxLib.BattleMap.BattleArena_AnchorRole role,
            IReadOnlyList<(float X, float Y, float Z)> coords)
        {
            ArgumentNullException.ThrowIfNull(coords);
            if (!CanWritePositions)
                throw new InvalidOperationException("This battle file does not expose a writable position (chunk3) region.");

            // WriteAnchorPositions clones OriginalBytes internally and re-stamps X/Y/Z in place (value-only).
            return FFXProjectEditor.FfxLib.BattleMap.BattleArenaPositionWriter.WriteAnchorPositions(
                OriginalBytes, areaIndex, role, coords);
        }

        private int GetChunkLength(int index)
        {
            if (index < 0 || index >= Chunks.Count)
                return 0;

            return Chunks[index].Length;
        }

        private int GetChunkOffset(int index)
        {
            if (index < 0 || index >= Chunks.Count)
                return 0;

            return Chunks[index].Offset;
        }

        private static byte[] GetChunkBytes(IReadOnlyList<Battle_Chunk> chunks, int index)
        {
            if (index < 0 || index >= chunks.Count)
                return Array.Empty<byte>();

            return chunks[index].Bytes;
        }

        private static List<Battle_Chunk> ParseChunks(byte[] bytes, int assumedChunkCount, int chunkOffset)
        {
            int chunkCount = assumedChunkCount;
            int[] offsets = new int[chunkCount + 1];

            for (int i = 0; i <= chunkCount; i++)
            {
                int offset = ReadInt32(bytes, chunkOffset + i * 4);
                if (offset == unchecked((int)0xFFFFFFFF))
                {
                    chunkCount = i - 1;
                    break;
                }

                offsets[i] = offset;
            }

            List<Battle_Chunk> chunks = new();
            for (int i = 0; i < chunkCount; i++)
            {
                int offset = offsets[i];
                if (offset == 0)
                {
                    chunks.Add(new Battle_Chunk(i, 0, 0, Array.Empty<byte>(), GetChunkLabel(i)));
                    continue;
                }

                int endOffset = -1;
                for (int j = i + 1; j <= chunkCount; j++)
                {
                    if (offsets[j] >= offset)
                    {
                        endOffset = offsets[j];
                        break;
                    }
                }

                if (endOffset < 0 || endOffset > bytes.Length)
                    endOffset = bytes.Length;

                // Some battle files (e.g. kino03_*, mihn05_*, cdsp00_02) declare more chunks than the file
                // actually carries — a tail chunk offset points past EOF. Treat that chunk and everything
                // after it as absent instead of rejecting the whole file; the earlier chunks (incl. the
                // formation chunk @2) stay valid and editable. SPIRA FORGE v0.2 reader robustness.
                if (offset > bytes.Length)
                    break;

                int length = Math.Max(0, endOffset - offset);
                byte[] chunkBytes = new byte[length];
                Array.Copy(bytes, offset, chunkBytes, 0, length);

                chunks.Add(new Battle_Chunk(i, offset, length, chunkBytes, GetChunkLabel(i)));
            }

            return chunks;
        }

        private static string GetChunkLabel(int index)
        {
            return index switch
            {
                0 => "ATEL Script",
                1 => "Worker Mapping",
                2 => "Formation",
                3 => "Battle Areas / Positions",
                4 => "Primary Text Chunk",
                5 => "FTCX",
                6 => "English Text Chunk",
                _ => $"Chunk {index:00}"
            };
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

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "Attempted to write outside battle file bounds.");

            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    public sealed class Battle_Chunk
    {
        public Battle_Chunk(int index, int offset, int length, byte[] bytes, string label)
        {
            Index = index;
            Offset = offset;
            Length = length;
            Bytes = bytes;
            Label = label;
        }

        public int Index { get; }
        public int Offset { get; }
        public int Length { get; }
        public byte[] Bytes { get; }
        public string Label { get; }
        public bool IsPresent => Offset > 0 && Length > 0;
    }

    public sealed class Battle_Formation
    {
        public required byte CommonVoiceLinesByte { get; init; }
        public required byte UnknownByte01 { get; init; }
        public required byte UnknownByte02 { get; init; }
        public required byte InWaterByte { get; init; }
        public required byte[] PaddingBytes { get; init; }
        public required IReadOnlyList<Battle_FormationSlot> Slots { get; init; }

        public bool CommonVoiceLinesEnabled => CommonVoiceLinesByte > 0;
        public bool InWater => InWaterByte > 0;

        public static Battle_Formation? TryRead(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 0x1C)
                return null;

            List<Battle_FormationSlot> slots = new();
            for (int i = 0; i < 8; i++)
            {
                int rawMonsterId = ReadUInt16(bytes, 0x0C + i * 2);
                int dictionaryId = rawMonsterId == 0xFFFF ? -1 : (rawMonsterId & 0x0FFF);
                string monsterName = rawMonsterId == 0xFFFF
                    ? "(Empty)"
                    : (Monster_Dictionary.Instance.ContainsKey((short)dictionaryId)
                        ? Monster_Dictionary.Instance[(short)dictionaryId]
                        : "<NOT INDEXED>");

                slots.Add(new Battle_FormationSlot
                {
                    SlotIndex = i,
                    RawMonsterId = rawMonsterId,
                    DictionaryId = dictionaryId,
                    MonsterName = monsterName
                });
            }

            return new Battle_Formation
            {
                CommonVoiceLinesByte = bytes[0x00],
                UnknownByte01 = bytes[0x01],
                UnknownByte02 = bytes[0x02],
                InWaterByte = bytes[0x03],
                PaddingBytes = bytes.Skip(0x04).Take(0x08).ToArray(),
                Slots = slots
            };
        }

        public string GetFormationLabel()
        {
            List<string> monsterLabels = Slots
                .Where(slot => !slot.IsEmpty)
                .Select(slot => $"m{slot.DictionaryId:D3} - {slot.MonsterName}")
                .ToList();

            if (monsterLabels.Count == 0)
                return "(Empty)";

            return "[" + string.Join(", ", monsterLabels) + "]";
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return bytes[offset] | (bytes[offset + 1] << 8);
        }
    }

    public sealed class Battle_FormationSlot
    {
        public required int SlotIndex { get; init; }
        public required int RawMonsterId { get; init; }
        public required int DictionaryId { get; init; }
        public required string MonsterName { get; init; }

        public bool IsEmpty => RawMonsterId == 0xFFFF;
        public string SlotLabel => $"Slot {SlotIndex:00}";
        public string RawMonsterIdHex => RawMonsterId == 0xFFFF ? "FFFFh" : $"{RawMonsterId:X4}h";
        public string MonsterLabel => IsEmpty ? "(Empty)" : $"m{DictionaryId:D3} - {MonsterName}";
    }

    public sealed class Battle_AreaSummary
    {
        public required int AreaCount { get; init; }
        public required int PartyPositionCount { get; init; }
        public required int AeonPositionCount { get; init; }
        public required int MonsterPositionCount { get; init; }
        public required int UnknownTargetStructCount { get; init; }

        public static Battle_AreaSummary? TryRead(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 0x09)
                return null;

            return new Battle_AreaSummary
            {
                AreaCount = bytes[0x01],
                PartyPositionCount = bytes[0x04],
                AeonPositionCount = bytes[0x05],
                MonsterPositionCount = bytes[0x06],
                UnknownTargetStructCount = bytes[0x08]
            };
        }
    }
}
