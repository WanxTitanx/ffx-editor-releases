using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — VALUE-ONLY position writer for the battle ARENA ANCHORS (chunk3 of a per-battle
    /// <c>&lt;id&gt;/&lt;id&gt;.bin</c>). This is the write half of <see cref="BattleArenaAnchors_File"/>: it re-stamps the
    /// X/Y/Z of an anchor array IN PLACE so the Aurora Chamber can "posicionar" actors and save the move.
    ///
    /// SAFETY MODEL (mirrors <c>FormationSlotWriter</c> — the proven slot-only pattern):
    ///  • It edits ONLY the 16-byte position elements of ONE anchor array (e.g. the on-field monsters at pointer
    ///    <c>+0x20</c>). It NEVER changes the area-record header, the dword pointers, the element count, or any other
    ///    chunk — so a no-edit write is byte-identical (RT0) and an edit is confined to that array's byte span.
    ///  • Per element it writes X,Y,Z and PRESERVES W verbatim (positions ship W==0; the writer never invents a W).
    ///  • The battle→scene transform is IDENTITY (proven in
    ///    <c>docs/reverse/FFX_AURORA_BATTLE_TO_SCENE_TRANSFORM_IDA_PROVEN_2026-06-05.md</c>): a coordinate the Aurora
    ///    Chamber picks in scene space is written here verbatim (no flip/scale). Y is the model-height term.
    ///
    /// Byte-safety is GATED offline (RuntimeTools/BattleArenaPositionLab). Whether the engine HONORS a moved spawn is
    /// RT2 (live, needs the probe) — out of scope for this offline writer. Avalonia-free / dependency-free
    /// (only <c>System.*</c> + the sibling enum) so the gate can link it the way FormationSlotLab links its writer.
    /// </summary>
    public static class BattleArenaPositionWriter
    {
        public const int ElementStride = 16;   // 4× float32 (X,Y,Z,W)
        public const string DefaultBackupSuffix = ".aurora.bak";

        public enum SaveStatus { Saved, AbortedNotPositionOnly, Error }

        public sealed class SaveResult
        {
            public required SaveStatus Status { get; init; }
            public required string Message { get; init; }
            public string? BackupPath { get; init; }
            public bool Ok => Status == SaveStatus.Saved;
        }

        /// <summary>Absolute byte span (offset+length) of <paramref name="role"/>'s anchor array in
        /// area-record <paramref name="areaIndex"/> within the FULL battle bin. Null if chunk3/the array is absent
        /// or its pointer is out of bounds (same guard the reader uses). <paramref name="count"/> returns the element
        /// count read from the area-record header.</summary>
        public static (int Offset, int Length, int Count)? LocateAnchorArray(
            byte[] battleBin, int areaIndex, BattleArena_AnchorRole role)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            (int start, int len) = LocateChunk3(battleBin);
            if (start < 0 || len < 0x30) return null;

            int recBase = start + areaIndex * BattleArenaAnchors_File.AreaRecordStride;
            if (recBase + 0x30 > start + len) return null;

            int count = RoleCount(battleBin, recBase, role);
            if (count <= 0) return null;

            int ptrOff = RolePointerOffset(role);
            int ptr = ReadInt32(battleBin, recBase + ptrOff);            // chunk3-base-relative
            if (ptr <= 0) return null;

            long absOffset = (long)start + ptr;
            long byteLen = (long)count * ElementStride;
            if (absOffset < start || absOffset + byteLen > start + len) return null; // confined to chunk3

            return ((int)absOffset, (int)byteLen, count);
        }

        /// <summary>Write the X/Y/Z of every element of an anchor array (W preserved), value-only. Clones the
        /// original, re-stamps in place, and returns the new bytes. Throws if the array is unlocatable or the
        /// coordinate count does not match the on-disk element count (no grow/shrink here — structure is fixed).</summary>
        public static byte[] WriteAnchorPositions(
            byte[] originalBin, int areaIndex, BattleArena_AnchorRole role,
            IReadOnlyList<(float X, float Y, float Z)> coords)
        {
            ArgumentNullException.ThrowIfNull(originalBin);
            ArgumentNullException.ThrowIfNull(coords);

            (int Offset, int Length, int Count)? loc = LocateAnchorArray(originalBin, areaIndex, role)
                ?? throw new InvalidOperationException(
                    $"No writable {role} anchor array in area {areaIndex} of this battle bin.");

            if (coords.Count != loc.Value.Count)
                throw new ArgumentException(
                    $"{role} has {loc.Value.Count} anchors but {coords.Count} coords were supplied (value-only writer does not resize).",
                    nameof(coords));

            byte[] o = (byte[])originalBin.Clone();
            int baseOff = loc.Value.Offset;
            for (int i = 0; i < coords.Count; i++)
            {
                int e = baseOff + i * ElementStride;
                WriteFloat(o, e + 0x00, coords[i].X);
                WriteFloat(o, e + 0x04, coords[i].Y);
                WriteFloat(o, e + 0x08, coords[i].Z);
                // e + 0x0C (W) preserved verbatim.
            }
            return o;
        }

        /// <summary>true iff every byte that differs between <paramref name="original"/> and
        /// <paramref name="candidate"/> lies within <c>[off, off+len)</c> (and lengths match) — the position-only
        /// invariant the gate asserts.</summary>
        public static bool IsPositionOnly(byte[] original, byte[] candidate, int off, int len)
        {
            if (original == null || candidate == null) return false;
            if (original.Length != candidate.Length) return false;
            for (int i = 0; i < original.Length; i++)
                if (original[i] != candidate[i] && (i < off || i >= off + len)) return false;
            return true;
        }

        /// <summary>Save <paramref name="newBytes"/> to <paramref name="path"/> as a loose file with a position-only
        /// guard + backup-once, mirroring <c>FormationSlotWriter.WriteLooseFile</c>. Never writes if the diff escapes
        /// the anchor-array span <c>[off, off+len)</c>.</summary>
        public static SaveResult WriteLooseFile(
            string path, byte[] originalBytes, byte[] newBytes, int off, int len,
            string backupSuffix = DefaultBackupSuffix)
        {
            if (off < 0)
                return new SaveResult { Status = SaveStatus.Error, Message = "Invalid array offset (position not writable)." };

            if (!IsPositionOnly(originalBytes, newBytes, off, len))
                return new SaveResult
                {
                    Status = SaveStatus.AbortedNotPositionOnly,
                    Message = "ABORTED: the write would change bytes outside the anchor array — position-only guard blocked. Nothing written."
                };

            try
            {
                string backupPath = path + backupSuffix;
                if (!File.Exists(backupPath))
                    File.Copy(path, backupPath);
                File.WriteAllBytes(path, newBytes);
                return new SaveResult
                {
                    Status = SaveStatus.Saved,
                    Message = $"Saved to {Path.GetFileName(path)} (backup: {Path.GetFileName(backupPath)}). Position-only confirmed.",
                    BackupPath = backupPath
                };
            }
            catch (Exception ex)
            {
                return new SaveResult { Status = SaveStatus.Error, Message = $"Falha gravando: {ex.Message}" };
            }
        }

        // ---- internals (chunk-3 location mirrors BattleArenaAnchors_File.ExtractChunk3, but returns the span) ----

        /// <summary>(start, length) of chunk index 3 in the full bin, or (-1, 0) if absent. Same EOF-robust rule as
        /// Battle_File / BattleArenaAnchors_File.</summary>
        public static (int Start, int Length) LocateChunk3(byte[] bytes)
        {
            const int wantIndex = 3;
            if (bytes.Length < 8) return (-1, 0);

            int chunkCount = ReadInt32(bytes, 0x00) - 1;
            if (chunkCount <= wantIndex) return (-1, 0);

            int[] offsets = new int[chunkCount + 1];
            for (int i = 0; i <= chunkCount; i++)
            {
                int off = ReadInt32(bytes, 0x04 + i * 4);
                if (off == unchecked((int)0xFFFFFFFF)) { chunkCount = i - 1; break; }
                offsets[i] = off;
            }
            if (chunkCount <= wantIndex) return (-1, 0);

            int start = offsets[wantIndex];
            if (start <= 0 || start > bytes.Length) return (-1, 0);

            int end = -1;
            for (int j = wantIndex + 1; j <= chunkCount; j++)
                if (offsets[j] >= start) { end = offsets[j]; break; }
            if (end < 0 || end > bytes.Length) end = bytes.Length;

            return (start, Math.Max(0, end - start));
        }

        private static int RolePointerOffset(BattleArena_AnchorRole role) => role switch
        {
            BattleArena_AnchorRole.PartyFront => 0x10,
            BattleArena_AnchorRole.PartyBack => 0x14,
            BattleArena_AnchorRole.Aeon => 0x18,
            BattleArena_AnchorRole.MonsterStagingA => 0x1C,
            BattleArena_AnchorRole.MonsterLive => 0x20,
            BattleArena_AnchorRole.MonsterStagingB => 0x24,
            BattleArena_AnchorRole.Camera => 0x2C,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        private static int RoleCount(byte[] bin, int recBase, BattleArena_AnchorRole role) => role switch
        {
            BattleArena_AnchorRole.PartyFront => bin[recBase + 0x04],
            BattleArena_AnchorRole.PartyBack => bin[recBase + 0x04],
            BattleArena_AnchorRole.Aeon => bin[recBase + 0x05],
            BattleArena_AnchorRole.MonsterStagingA => bin[recBase + 0x06],
            BattleArena_AnchorRole.MonsterLive => bin[recBase + 0x06],
            BattleArena_AnchorRole.MonsterStagingB => bin[recBase + 0x06],
            BattleArena_AnchorRole.Camera => 1,
            _ => 0,
        };

        // ---- position-only write (auto-layout / profile apply) -----------------------------

        /// <summary>
        /// Re-stamp X/Z of the first <paramref name="positions"/> elements of <paramref name="role"/>'s anchor array
        /// IN PLACE (clones the bin). Preserves Y (model-height term) and W verbatim. Byte-confined to the array
        /// span (RT0-identical when positions are unchanged). Returns null when the array is absent/short.
        /// </summary>
        public static byte[]? WritePositions(
            byte[] battleBin, int areaIndex, BattleArena_AnchorRole role,
            IReadOnlyList<(float X, float Z)> positions)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            ArgumentNullException.ThrowIfNull(positions);
            var span = LocateAnchorArray(battleBin, areaIndex, role);
            if (span is not { } s || s.Count == 0 || positions.Count == 0) return null;

            byte[] output = (byte[])battleBin.Clone();
            int count = Math.Min(s.Count, positions.Count);
            for (int i = 0; i < count; i++)
            {
                int off = s.Offset + i * ElementStride;
                WriteFloat(output, off, positions[i].X);
                WriteFloat(output, off + 8, positions[i].Z);
                // Y preservado; W preservado verbatim (posições W==0; nunca inventar)
            }
            return output;
        }

        private static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));

        private static void WriteFloat(byte[] b, int o, float v)
        {
            if (o < 0 || o + 4 > b.Length) throw new ArgumentOutOfRangeException(nameof(o), "Write outside bin bounds.");
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o, 4), v);
        }
    }
}
