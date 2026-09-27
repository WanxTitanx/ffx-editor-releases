using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Round-trip tests for the two Battle families that had none in this suite:
    /// per-battle files (<see cref="Battle_File"/>, battle/btl/*.bin) and the
    /// encounter table (<see cref="EncounterTable_File"/>, battle/kernel/btl.bin).
    ///
    /// Both fixtures are built in-memory from the proven layouts so the round-trip
    /// is reproducible in CI without a local game install (same pattern as
    /// <see cref="TreasureRoundTripTests"/>).
    ///
    /// Battle_File:          u32@0x00 = chunkCount + 1; chunk offset table at 0x04;
    ///                       chunk i spans [offset[i], offset[i+1]). Chunk 2 = Formation
    ///                       (8 u16 monster slots at +0x0C, 0xFFFF = empty), chunk 3 =
    ///                       Positions. Writer WriteWithFormationSlots re-stamps exactly
    ///                       those 16 bytes -> no-edit is byte-identical, edit is byte-local.
    /// EncounterTable_File:  u32@0x00 = 2 (chunks); u32@0x04/0x08/0x0C = chunk0 start /
    ///                       chunk1 start / chunk1 end. chunk0 = 0x0E-byte header entries
    ///                       (Id, DataOffset, FormationOffset, 6-byte map, padding);
    ///                       chunk1 = payload blocks (TotalFormationCount, GroupCount,
    ///                       then per group: FormationCount, u16 Battlefield, Danger,
    ///                       TotalWeight, then per formation: FormationId, Weight).
    ///                       Write() = slot-only preserve; Rebuild() = structural. Both are
    ///                       no-edit byte-identical (proven on the real corpus by the
    ///                       --encounter-rt0 / --encounter-rebuild-rt0 headless gates).
    /// </summary>
    public class BattleRoundTripTests
    {
        // --- Battle_File (per-battle bin) -----------------------------------------

        const int BattleChunk0Offset = 0x14;
        const int BattleFormationOffset = 0x24;
        const int BattleFormationSlotsOffset = BattleFormationOffset + 0x0C; // 0x30
        const int BattlePositionsOffset = 0x40;
        const int BattleFileLength = 0x70;

        static readonly ushort[] VanillaSlots =
            { 0x0001, 0x0002, 0x0003, 0xFFFF, 0x0005, 0x0006, 0x0007, 0xFFFF };

        [Fact]
        public void Battle_Read_ParsesChunkHeaderAndFormation()
        {
            Battle_File battle = Battle_File.Read("t000", BuildBattleBin());

            Assert.Equal("t000", battle.BattleId);
            Assert.Equal(BattleFileLength, battle.FileSize);
            Assert.Equal(5, battle.RawChunkValue);   // chunkCount + 1
            Assert.Equal(4, battle.ChunkCount);
            Assert.True(battle.IsUnsafeFormat);      // ChunkCount == 4

            Assert.Equal(BattleChunk0Offset, battle.Chunks[0].Offset);
            Assert.Equal(BattleFormationOffset, battle.Chunks[2].Offset);
            Assert.Equal(0x1C, battle.FormationChunkLength);
            Assert.Equal(BattlePositionsOffset, battle.Chunks[3].Offset);
            Assert.Equal(0x30, battle.PositionChunkLength);

            Assert.True(battle.CanWriteFormation);
            Assert.Equal(BattleFormationSlotsOffset, battle.FormationSlotsOffset);

            Battle_Formation? formation = battle.Formation;
            Assert.NotNull(formation);
            Assert.True(formation!.CommonVoiceLinesEnabled);
            Assert.True(formation.InWater);
            Assert.Equal(VanillaSlots.Length, formation.Slots.Count);
            for (int i = 0; i < VanillaSlots.Length; i++)
                Assert.Equal(VanillaSlots[i], (ushort)formation.Slots[i].RawMonsterId);
            Assert.False(formation.Slots[0].IsEmpty);
            Assert.True(formation.Slots[3].IsEmpty);
            Assert.True(formation.Slots[7].IsEmpty);
        }

        [Fact]
        public void Battle_WriteWithFormationSlots_NoEdit_IsByteIdentical()
        {
            byte[] original = BuildBattleBin();
            Battle_File battle = Battle_File.Read("t000", original);

            byte[] rewritten = battle.WriteWithFormationSlots(
                battle.Formation!.Slots.Select(s => (ushort)s.RawMonsterId).ToArray());

            Assert.Equal(original.Length, rewritten.Length);
            Assert.True(
                original.AsSpan().SequenceEqual(rewritten),
                $"no-edit save drifted, first diff @0x{FirstDifference(original, rewritten):X}");
        }

        [Fact]
        public void Battle_WriteWithFormationSlots_Edit_IsByteLocalToSlotRegion()
        {
            byte[] original = BuildBattleBin();
            Battle_File battle = Battle_File.Read("t000", original);

            byte[] edited = battle.WriteWithFormationSlots(
                new ushort[] { 0x0100, 0x0002, 0x0003, 0xFFFF, 0x0005, 0x0006, 0x0007, 0xFFFF });

            int[] diffs = DifferenceIndices(original, edited);
            Assert.Equal(2, diffs.Length);
            Assert.Equal(BattleFormationSlotsOffset, diffs[0]);
            Assert.Equal(BattleFormationSlotsOffset + 1, diffs[1]);
            Assert.Equal(0x00, edited[BattleFormationSlotsOffset]);     // 0x0100 little-endian
            Assert.Equal(0x01, edited[BattleFormationSlotsOffset + 1]);
        }

        [Fact]
        public void Battle_WriteWithFormationSlots_RejectsWrongSlotCountOrMissingFormation()
        {
            Battle_File battle = Battle_File.Read("t000", BuildBattleBin());

            Assert.Throws<ArgumentException>(
                () => battle.WriteWithFormationSlots(new ushort[] { 0x0001, 0x0002, 0x0003 }));

            Battle_File noFormation = Battle_File.Read("t000", BuildBattleBinWithoutFormation());
            Assert.Throws<InvalidOperationException>(() => noFormation.WriteWithFormationSlots(VanillaSlots));
        }

        [Fact]
        public void Battle_Read_RejectsInvalidHeader()
        {
            // Too small to hold a chunk header.
            Assert.Throws<InvalidDataException>(() => Battle_File.Read("t000", new byte[] { 0, 0, 0, 0 }));
            // rawChunkValue = 0 => chunkCount = -1 => rejected.
            Assert.Throws<InvalidDataException>(() => Battle_File.Read("t000", new byte[8]));
        }

        // --- EncounterTable_File (battle/kernel/btl.bin) -------------------------

        const int EtChunk0Start = 0x10;
        const int EtChunk1Start = 0x2C;
        const int EtChunk1End = 0x40;   // == file length

        [Fact]
        public void Encounter_Read_ParsesHeaderAndPayload()
        {
            EncounterTable_File table = EncounterTable_File.Read(BuildBtlBin());

            Assert.Equal(2, table.Tables.Count);
            Assert.Equal(EtChunk0Start, table.Chunk0Start);
            Assert.Equal(EtChunk1Start, table.Chunk1Start);

            EncounterTable_Entry t0 = table.Tables[0];
            Assert.Equal(0x0001, t0.Id);
            Assert.Equal(0x0000, t0.DataOffset);
            Assert.Equal(0x0000, t0.FormationOffset);
            Assert.Equal("mush0", t0.Map);
            Assert.Equal(0, t0.MapNamePadding);
            Assert.Equal(2, t0.TotalFormationCount);
            Assert.Equal(1, t0.GroupCount);

            EncounterTable_Group g0 = t0.Groups[0];
            Assert.Equal(2, g0.FormationCount);
            Assert.Equal(0x0064, g0.Battlefield);
            Assert.Equal(1, g0.Danger);
            Assert.Equal(3, g0.TotalWeight);
            Assert.Equal(2, g0.Formations.Count);
            Assert.Equal(0x01, g0.Formations[0].FormationId);
            Assert.Equal(0x02, g0.Formations[0].Weight);

            EncounterTable_Entry t1 = table.Tables[1];
            Assert.Equal(0x0002, t1.Id);
            Assert.Equal(0x0B, t1.DataOffset);
            Assert.Equal("cave0", t1.Map);
            Assert.Equal(1, t1.TotalFormationCount);
            Assert.Equal(0x00C8, t1.Groups[0].Battlefield);
            Assert.Equal(2, t1.Groups[0].Danger);
            Assert.Equal(0x0A, t1.Groups[0].Formations[0].FormationId);
            Assert.Equal(0x04, t1.Groups[0].Formations[0].Weight);
        }

        [Fact]
        public void Encounter_Write_NoEditRoundTrip_IsByteIdentical()
        {
            byte[] original = BuildBtlBin();

            byte[] rewritten = EncounterTable_File.Read(original).Write();

            Assert.Equal(original.Length, rewritten.Length);
            Assert.True(
                original.AsSpan().SequenceEqual(rewritten),
                $"slot-only no-edit save drifted, first diff @0x{FirstDifference(original, rewritten):X}");
        }

        [Fact]
        public void Encounter_Write_Edit_IsByteLocalToSlotFields()
        {
            byte[] original = BuildBtlBin();
            EncounterTable_File table = EncounterTable_File.Read(original);
            table.Tables[1].Groups[0].TotalWeight = 5;   // was 4

            byte[] edited = table.Write();

            // go = chunk1 + DataOffset(0x0B) + 2; TotalWeight @ +0x04 => 0x2C + 0x0B + 0x06 = 0x3D.
            Assert.Equal(new[] { 0x3D }, DifferenceIndices(original, edited));
            Assert.Equal(0x05, edited[0x3D]);
        }

        [Fact]
        public void Encounter_Rebuild_NoEditRoundTrip_IsByteIdentical()
        {
            byte[] original = BuildBtlBin();

            byte[] rebuilt = EncounterTable_File.Read(original).Rebuild();

            Assert.Equal(original.Length, rebuilt.Length);
            Assert.True(
                original.AsSpan().SequenceEqual(rebuilt),
                $"structural no-edit rebuild drifted, first diff @0x{FirstDifference(original, rebuilt):X}");
        }

        [Fact]
        public void Encounter_Rebuild_GrowFormation_SurvivesReRead()
        {
            byte[] original = BuildBtlBin();
            EncounterTable_File table = EncounterTable_File.Read(original);
            table.Tables[0].Groups[0].Formations.Add(new EncounterTable_Formation
            {
                FormationId = 0x7F,
                Weight = 0x01,
                BattleId = "mush0_99"
            });

            byte[] grown = table.Rebuild();
            Assert.Equal(original.Length + 2, grown.Length);   // +1 formation = +2 bytes

            EncounterTable_File reread = EncounterTable_File.Read(grown);
            Assert.Equal(2, reread.Tables.Count);
            EncounterTable_Group group = reread.Tables[0].Groups[0];
            Assert.Equal(3, group.Formations.Count);
            Assert.Contains(group.Formations, f => f.FormationId == 0x7F && f.Weight == 0x01);

            // Chunk structure still intact: 2 chunks, ordered starts, end past chunk1.
            Assert.Equal(2u, ReadUInt32(grown, 0x00));
            Assert.Equal(EtChunk0Start, reread.Chunk0Start);
            Assert.True(reread.Chunk1Start > reread.Chunk0Start);
            Assert.True(ReadUInt32(grown, 0x0C) > (uint)reread.Chunk1Start);
        }

        // --- Fixture builders ------------------------------------------------------

        static byte[] BuildBattleBin()
        {
            byte[] bytes = new byte[BattleFileLength];
            WriteUInt32(bytes, 0x00, 5);                          // rawChunkValue = chunkCount + 1
            WriteUInt32(bytes, 0x04, BattleChunk0Offset);         // chunk0 (ATEL script) @0x14
            WriteUInt32(bytes, 0x08, BattleChunk0Offset + 0x08);  // chunk1 (worker map) @0x1C
            WriteUInt32(bytes, 0x0C, BattleFormationOffset);      // chunk2 (formation) @0x24
            WriteUInt32(bytes, 0x10, BattlePositionsOffset);      // chunk3 (positions) @0x40
            WriteUInt32(bytes, 0x14, BattleFileLength);           // end bound @0x70

            bytes[BattleChunk0Offset] = 0xAA;                     // opaque script body
            bytes[BattleChunk0Offset + 0x08] = 0xBB;              // opaque worker body

            // Formation chunk (0x1C): flags, 8-byte padding, then 8 u16 monster slots.
            bytes[BattleFormationOffset + 0x00] = 0x01;           // CommonVoiceLines
            bytes[BattleFormationOffset + 0x03] = 0x01;           // InWater
            for (int i = 0; i < VanillaSlots.Length; i++)
                WriteUInt16(bytes, BattleFormationSlotsOffset + i * 2, VanillaSlots[i]);

            bytes[BattlePositionsOffset] = 0xCC;                  // opaque positions body
            return bytes;
        }

        static byte[] BuildBattleBinWithoutFormation()
        {
            byte[] bytes = new byte[BattleFileLength];
            WriteUInt32(bytes, 0x00, 5);
            WriteUInt32(bytes, 0x04, BattleChunk0Offset);
            WriteUInt32(bytes, 0x08, BattleChunk0Offset + 0x08);
            WriteUInt32(bytes, 0x0C, 0);                          // formation chunk absent
            WriteUInt32(bytes, 0x10, BattlePositionsOffset);
            WriteUInt32(bytes, 0x14, BattleFileLength);
            return bytes;
        }

        static byte[] BuildBtlBin()
        {
            byte[] bytes = new byte[EtChunk1End];
            WriteUInt32(bytes, 0x00, 2);                          // chunk count
            WriteUInt32(bytes, 0x04, EtChunk0Start);              // chunk0 start (header)
            WriteUInt32(bytes, 0x08, EtChunk1Start);              // chunk1 start (payload)
            WriteUInt32(bytes, 0x0C, EtChunk1End);                // chunk1 end (EOF)

            // chunk0: two 0x0E-byte header entries (Id, DataOffset, FormationOffset, 6-byte map, padding).
            WriteEntry(bytes, 0x10, 0x0001, 0x0000, "mush0");
            WriteEntry(bytes, 0x1E, 0x0002, 0x000B, "cave0");

            // chunk1 payload (absolute offsets 0x2C..0x40).
            bytes[0x2C + 0x00] = 0x02;   // table0 TotalFormationCount
            bytes[0x2C + 0x01] = 0x01;   // table0 GroupCount
            bytes[0x2C + 0x02] = 0x02;   // FormationCount
            bytes[0x2C + 0x03] = 0x64;   // Battlefield lo
            bytes[0x2C + 0x04] = 0x00;   // Battlefield hi
            bytes[0x2C + 0x05] = 0x01;   // Danger
            bytes[0x2C + 0x06] = 0x03;   // TotalWeight
            bytes[0x2C + 0x07] = 0x01;   // formation0 Id
            bytes[0x2C + 0x08] = 0x02;   // formation0 Weight
            bytes[0x2C + 0x09] = 0x02;   // formation1 Id
            bytes[0x2C + 0x0A] = 0x01;   // formation1 Weight

            bytes[0x2C + 0x0B] = 0x01;   // table1 TotalFormationCount
            bytes[0x2C + 0x0C] = 0x01;   // table1 GroupCount
            bytes[0x2C + 0x0D] = 0x01;   // FormationCount
            bytes[0x2C + 0x0E] = 0xC8;   // Battlefield lo
            bytes[0x2C + 0x0F] = 0x00;   // Battlefield hi
            bytes[0x2C + 0x10] = 0x02;   // Danger
            bytes[0x2C + 0x11] = 0x04;   // TotalWeight
            bytes[0x2C + 0x12] = 0x0A;   // formation Id
            bytes[0x2C + 0x13] = 0x04;   // formation Weight
            return bytes;
        }

        static void WriteEntry(byte[] bytes, int offset, int id, int dataOffset, string map)
        {
            WriteUInt16(bytes, offset, id);
            WriteUInt16(bytes, offset + 0x02, dataOffset);
            WriteUInt16(bytes, offset + 0x04, 0x0000);   // FormationOffset
            for (int i = 0; i < 6; i++)
                bytes[offset + 0x06 + i] = i < map.Length ? (byte)map[i] : (byte)0;
            WriteUInt16(bytes, offset + 0x0C, 0x0000);   // MapNamePadding
        }

        // --- Helpers ----------------------------------------------------------------

        static void WriteUInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        static void WriteUInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        static uint ReadUInt32(byte[] buffer, int offset) =>
            (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));

        static int FirstDifference(byte[] a, byte[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            for (int i = 0; i < length; i++)
                if (a[i] != b[i])
                    return i;
            return a.Length == b.Length ? -1 : length;
        }

        static int[] DifferenceIndices(byte[] a, byte[] b)
        {
            var indices = new List<int>();
            int length = Math.Min(a.Length, b.Length);
            for (int i = 0; i < length; i++)
                if (a[i] != b[i])
                    indices.Add(i);
            if (a.Length != b.Length)
                indices.Add(length);
            return indices.ToArray();
        }
    }
}
