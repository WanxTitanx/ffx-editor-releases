using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — read-only decoder of the battle ARENA ANCHORS (chunk3 "Battle Areas / Positions")
    /// of a per-battle <c>&lt;id&gt;/&lt;id&gt;.bin</c>. This is the coordinate half of the Aurora Chamber: it lifts the
    /// actor spawn coordinates out of chunk3 so the chamber can LIST/EXPORT them as scene anchors.
    ///
    /// Layout proven in <c>docs/reverse/FFX_BATTLE_FORMATION_POSITION_CHUNK3_DECODED_2026-06-05.md</c>: chunk3 holds
    /// one or more 96-byte AREA-RECORDs (<c>fmtFlag@+0x00 == 0 ⇒ stride 96</c> in 862/862 shipped battles) with
    /// chunk3-base-relative dword pointers at <c>+0x10..+0x2C</c> into arrays of 16-byte position elements
    /// (<c>float32 X,Y,Z,W</c>, Y-up, <c>W == 0</c> for positions). The on-field monsters live at the pointer
    /// <c>+0x20</c> (count = <c>MonsterPositionCount@+0x06</c>); formation slot <c>i</c> (chunk2) maps to live entry
    /// <c>i</c>. Coordinates are in the <b>battle-local</b> frame, NOT the rendered scene frame — the battle→scene
    /// transform is design-only/uncalibrated (see the Aurora coordinate doc). This reader never decodes a .phyre byte
    /// and never mutates anything.
    ///
    /// Deliberately Avalonia-free and dependency-free (only <c>System.*</c>) so the RT0 gate
    /// (RuntimeTools/AuroraChamberLab) can link it directly, mirroring how BiancaCatalogLab links
    /// <see cref="BattleMapCatalog_File"/> without pulling Monster_Dictionary or the rest of Battle_File.
    /// </summary>
    public sealed class BattleArenaAnchors_File
    {
        public const int AreaRecordStride = 96;       // 0x60 — fmtFlag==0 path (862/862 shipped)
        public const int AnchorElementStride = 16;    // 4× float32 (X,Y,Z,W)

        public required string BattleId { get; init; }
        public required int DeclaredAreaCount { get; init; }
        public required IReadOnlyList<BattleArena_AreaRecord> Areas { get; init; }
        /// <summary>Diagnostics (pointer-out-of-bounds / truncated arrays). Empty == clean decode.</summary>
        public required IReadOnlyList<string> Notes { get; init; }

        public int AreaCount => Areas.Count;
        /// <summary>The on-field monster anchors of area-record 0 (the primary arena) — the "list the anchors" target.</summary>
        public IReadOnlyList<BattleArena_Anchor> PrimaryMonsterAnchors =>
            Areas.Count > 0
                ? Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
                  ?? Array.Empty<BattleArena_Anchor>()
                : Array.Empty<BattleArena_Anchor>();

        /// <summary>Decode the anchors from a whole per-battle bin (parses the chunk table inline, no Battle_File
        /// dependency). Returns an empty (but valid) result when chunk3 is absent/too small.</summary>
        public static BattleArenaAnchors_File ReadFromBattleBin(string battleId, byte[] battleBinBytes)
        {
            ArgumentNullException.ThrowIfNull(battleBinBytes);
            byte[] chunk3 = ExtractChunk3(battleBinBytes);
            return ReadFromChunk3(battleId, chunk3);
        }

        /// <summary>Decode the anchors directly from the already-sliced chunk3 bytes (e.g. Battle_File.Chunks[3].Bytes).</summary>
        public static BattleArenaAnchors_File ReadFromChunk3(string battleId, byte[] chunk3Bytes)
        {
            ArgumentNullException.ThrowIfNull(chunk3Bytes);
            List<string> notes = new();
            List<BattleArena_AreaRecord> areas = new();

            if (chunk3Bytes.Length < 0x09)
            {
                return new BattleArenaAnchors_File
                {
                    BattleId = battleId,
                    DeclaredAreaCount = 0,
                    Areas = areas,
                    Notes = new List<string> { "chunk3 absent or smaller than one area-record header" },
                };
            }

            int fmtFlag = chunk3Bytes[0x00];
            int declaredAreaCount = chunk3Bytes[0x01];
            if (fmtFlag != 0)
                notes.Add($"fmtFlag@+0x00 == {fmtFlag} (expected 0 / stride-96); decoded with stride-96 anyway (stride-32 path never exercised in the shipped corpus).");

            int areaCount = Math.Max(0, declaredAreaCount);
            // Defensive clamp: never read more area-records than the chunk can physically hold.
            int maxByLen = chunk3Bytes.Length / AreaRecordStride;
            if (areaCount > maxByLen + 1) // +1 tolerance: record[0] header may exist with arrays past it
            {
                notes.Add($"AreaCount={declaredAreaCount} exceeds chunk capacity ({maxByLen} records of 96B); clamped.");
                areaCount = Math.Max(1, maxByLen);
            }
            if (areaCount == 0) areaCount = 1; // a present chunk3 always carries at least record[0]

            for (int a = 0; a < areaCount; a++)
            {
                int recBase = a * AreaRecordStride;
                if (recBase + 0x30 > chunk3Bytes.Length)
                {
                    notes.Add($"area-record {a} truncated (base 0x{recBase:X} past chunk3 end 0x{chunk3Bytes.Length:X}); stopped.");
                    break;
                }

                int partyCount = chunk3Bytes[recBase + 0x04];
                int aeonCount = chunk3Bytes[recBase + 0x05];
                int monsterCount = chunk3Bytes[recBase + 0x06];

                List<BattleArena_AnchorGroup> groups = new();
                // (pointerOffsetInRecord, role, count) — the well-defined arrays from the decode doc.
                AddGroup(groups, chunk3Bytes, recBase, 0x10, partyCount, BattleArena_AnchorRole.PartyFront, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x14, partyCount, BattleArena_AnchorRole.PartyBack, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x18, aeonCount, BattleArena_AnchorRole.Aeon, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x1C, monsterCount, BattleArena_AnchorRole.MonsterStagingA, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x20, monsterCount, BattleArena_AnchorRole.MonsterLive, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x24, monsterCount, BattleArena_AnchorRole.MonsterStagingB, notes, a);
                AddGroup(groups, chunk3Bytes, recBase, 0x2C, 1, BattleArena_AnchorRole.Camera, notes, a);

                areas.Add(new BattleArena_AreaRecord
                {
                    AreaIndex = a,
                    PartyCount = partyCount,
                    AeonCount = aeonCount,
                    MonsterCount = monsterCount,
                    Groups = groups,
                });
            }

            return new BattleArenaAnchors_File
            {
                BattleId = battleId,
                DeclaredAreaCount = declaredAreaCount,
                Areas = areas,
                Notes = notes,
            };
        }

        private static void AddGroup(
            List<BattleArena_AnchorGroup> groups, byte[] chunk3, int recBase, int ptrOffsetInRecord,
            int count, BattleArena_AnchorRole role, List<string> notes, int areaIndex)
        {
            if (count <= 0)
            {
                groups.Add(new BattleArena_AnchorGroup { Role = role, PointerOffsetInRecord = ptrOffsetInRecord, Pointer = 0, Anchors = Array.Empty<BattleArena_Anchor>() });
                return;
            }

            int ptr = ReadInt32(chunk3, recBase + ptrOffsetInRecord);
            List<BattleArena_Anchor> anchors = new();

            // 64-bit arithmetic so a corrupt pointer near int.MaxValue can't overflow the addend past the guard.
            if (ptr <= 0 || (long)ptr + (long)count * AnchorElementStride > chunk3.Length)
            {
                notes.Add($"area {areaIndex} {role}: pointer@+0x{ptrOffsetInRecord:X2}=0x{ptr:X} + {count}×16 out of chunk3 bounds (0x{chunk3.Length:X}); group skipped.");
                groups.Add(new BattleArena_AnchorGroup { Role = role, PointerOffsetInRecord = ptrOffsetInRecord, Pointer = ptr, Anchors = anchors });
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int e = ptr + i * AnchorElementStride;
                anchors.Add(new BattleArena_Anchor
                {
                    Index = i,
                    Role = role,
                    X = ReadFloat(chunk3, e + 0x00),
                    Y = ReadFloat(chunk3, e + 0x04),
                    Z = ReadFloat(chunk3, e + 0x08),
                    W = ReadFloat(chunk3, e + 0x0C),
                });
            }

            groups.Add(new BattleArena_AnchorGroup { Role = role, PointerOffsetInRecord = ptrOffsetInRecord, Pointer = ptr, Anchors = anchors });
        }

        /// <summary>Slice chunk index 3 ("Battle Areas / Positions") out of a per-battle bin, mirroring
        /// Battle_File.ParseChunks (EOF-robust, tolerates tail-chunk offsets past EOF). Returns empty if absent.
        /// Intentionally chunk-3-LOCAL: it validates only chunk 3's own start/end (not earlier chunks), so on a
        /// malformed file where an earlier chunk points past EOF but offsets[3] is still valid it may return a slice
        /// where Battle_File would bail — a pathological case absent from the shipped corpus (862/862 well-formed).</summary>
        private static byte[] ExtractChunk3(byte[] bytes)
        {
            const int wantIndex = 3;
            if (bytes.Length < 8) return Array.Empty<byte>();

            int rawChunkValue = ReadInt32(bytes, 0x00);
            int chunkCount = rawChunkValue - 1;
            if (chunkCount <= wantIndex) return Array.Empty<byte>();

            int[] offsets = new int[chunkCount + 1];
            for (int i = 0; i <= chunkCount; i++)
            {
                int off = ReadInt32(bytes, 0x04 + i * 4);
                if (off == unchecked((int)0xFFFFFFFF)) { chunkCount = i - 1; break; }
                offsets[i] = off;
            }
            if (chunkCount <= wantIndex) return Array.Empty<byte>();

            int start = offsets[wantIndex];
            if (start <= 0 || start > bytes.Length) return Array.Empty<byte>();

            // end = first later offset that is >= start (same rule Battle_File uses), else EOF.
            int end = -1;
            for (int j = wantIndex + 1; j <= chunkCount; j++)
            {
                if (offsets[j] >= start) { end = offsets[j]; break; }
            }
            if (end < 0 || end > bytes.Length) end = bytes.Length;

            int len = Math.Max(0, end - start);
            byte[] chunk = new byte[len];
            Array.Copy(bytes, start, chunk, 0, len);
            return chunk;
        }

        private static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));

        private static float ReadFloat(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0f : BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o, 4));
    }

    public enum BattleArena_AnchorRole
    {
        PartyFront,       // +0x10
        PartyBack,        // +0x14 (approach / parking row, Z far)
        Aeon,             // +0x18 (12 aeon slots)
        MonsterStagingA,  // +0x1C (off-field staging)
        MonsterLive,      // +0x20 ← on-field monsters (formation slot i ↔ entry i)
        MonsterStagingB,  // +0x24 (off-field staging / flee)
        Camera,           // +0x2C (1 record; W≠0 — NOT a position)
    }

    public sealed class BattleArena_AreaRecord
    {
        public required int AreaIndex { get; init; }
        public required int PartyCount { get; init; }
        public required int AeonCount { get; init; }
        public required int MonsterCount { get; init; }
        public required IReadOnlyList<BattleArena_AnchorGroup> Groups { get; init; }

        public BattleArena_AnchorGroup? this[BattleArena_AnchorRole role] =>
            Groups.FirstOrDefault(g => g.Role == role);
    }

    public sealed class BattleArena_AnchorGroup
    {
        public required BattleArena_AnchorRole Role { get; init; }
        public required int PointerOffsetInRecord { get; init; }   // +0x10..+0x2C
        public required int Pointer { get; init; }                 // chunk3-base-relative offset (0 == none)
        public required IReadOnlyList<BattleArena_Anchor> Anchors { get; init; }
        public int Count => Anchors.Count;
    }

    public sealed class BattleArena_Anchor
    {
        public required int Index { get; init; }
        public required BattleArena_AnchorRole Role { get; init; }
        public required float X { get; init; }
        public required float Y { get; init; }
        public required float Z { get; init; }
        public required float W { get; init; }   // 0 for positions; the camera record carries a non-zero W (FOV/roll?)

        /// <summary>Positions carry W==0; a non-zero W marks a non-position record (the camera entry).</summary>
        public bool IsPositionLike => W == 0f;

        public override string ToString() =>
            $"[{Index}] ({X:0.##}, {Y:0.##}, {Z:0.##}{(IsPositionLike ? "" : $", W={W:0.##}")})";
    }
}
