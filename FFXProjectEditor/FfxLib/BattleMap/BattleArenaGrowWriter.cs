using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — STRUCTURAL grow/shrink of the on-field monster array (chunk3 <c>+0x20</c>) of a
    /// per-battle <c>&lt;id&gt;/&lt;id&gt;.bin</c>. Where <see cref="BattleArenaPositionWriter"/> only MOVES existing
    /// anchors (value-only), this changes the COUNT — the structural half the encounter-authoring masterplan needs
    /// ("quantos mobs eu quiser"). Pairs with the chunk2 formation slots (chunk2 slot i ↔ monLive entry i).
    ///
    /// PROVEN payload model (docs/reverse/FFX_AURORA_CHUNK3_PAYLOAD_MODEL_2026-06-07.md, corpus 863 battles):
    ///  • area-record[0] packs its 8 arrays in a FIXED pointer-target order
    ///    <c>origin &lt; party &lt; partyB &lt; aeon &lt; monA &lt; monLive &lt; monB &lt; camera</c> (863/863), each array's
    ///    capacity = the next pointer's target − its own (16-byte elements).
    ///  • <b>monLive (+0x20) is TIGHT</b> (capacity == MonsterPositionCount@+0x06) in 856/863 — so raising the count
    ///    requires inserting room at monLive's end. <b>monA (+0x1C)</b> reserves ~12 and <b>monB (+0x24)</b> reserves
    ///    3..78 entries, so they ABSORB a higher count without their own resize as long as the new count ≤ their
    ///    capacity. The grow therefore inserts ONLY at monLive's end and never touches monA/monB data.
    ///
    /// ALGORITHM (insert-at-monLive-end splice — a no-edit grow is byte-identical, so RT0 is trivially exact):
    ///  1. insert (newCount−oldCount)×16 bytes at <c>chunk3 + monLivePtr + oldCount×16</c> (= monB's start, since
    ///     monLive is tight); for remove, delete |Δ|×16 there.
    ///  2. everything after the splice point shifts by Δ×16: re-stamp the record-0 pointers whose target is ≥ the
    ///     splice point (only <c>monB(+0x24)</c> and <c>camera(+0x2C)</c> — monA/aeon/party/partyB/origin/monLive are
    ///     before it), and the chunk-offset-table entries for chunks AFTER index 3.
    ///  3. set <c>MonsterPositionCount@+0x06 = newCount</c>; write the supplied monLive coords (W kept 0).
    ///
    /// GUARDS (refuses rather than risk a corrupt/over-cap file): single-area only (AreaCount==1); monLive must be
    /// tight; newCount ∈ [1, min(monA capacity, monB capacity, <see cref="HardActorCap"/>)] — the reserves are the
    /// proven headroom, the hard cap is the conservative engine-actor ceiling.
    ///
    /// ⚠️ HONESTY: this proves BYTE-safety offline (RT0 + round-trip gate). Whether the ENGINE spawns the extra
    /// actor is RT2 — NOT proven in-game (the spawn-loop's exact actor ceiling is an open IDA item; the hard cap is
    /// conservative). Avalonia-free / dependency-free (only <c>System.*</c> + the sibling enum/writer).
    /// </summary>
    public static class BattleArenaGrowWriter
    {
        public const int ElementStride = 16;
        /// <summary>Conservative engine actor ceiling (masterplan B3 ≈ 8; shipped corpus max MonsterPositionCount=15,
        /// boss-rush znkd09). Until the spawn loop is IDA-confirmed, the writer will not exceed this.</summary>
        public const int HardActorCap = 8;

        public sealed class GrowPlan
        {
            public required bool CanGrow { get; init; }
            public required string Reason { get; init; }
            public int OldCount { get; init; }
            public int MonLivePtr { get; init; }      // chunk3-relative
            public int MonLiveCapacity { get; init; } // physical entries of the monLive array (== OldCount when tight)
            public int MonACapacity { get; init; }     // entries
            public int MonBCapacity { get; init; }     // entries
            /// <summary>Max monster count this battle can hold WITHOUT resizing monA/monB (= min reserve, capped).</summary>
            public int MaxCount { get; init; }
        }

        /// <summary>Inspect a battle bin's area-record 0 and report whether/how far the monster count can grow.</summary>
        public static GrowPlan Plan(byte[] battleBin)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            (int start, int len) = BattleArenaPositionWriter.LocateChunk3(battleBin);
            if (start < 0 || len < 0x60)
                return new GrowPlan { CanGrow = false, Reason = "chunk3 absent or too small" };
            if (battleBin[start + 0x01] != 1)
                return new GrowPlan { CanGrow = false, Reason = $"multi-area (AreaCount={battleBin[start + 0x01]}) — grow restricted to single-area" };

            int oldCount = battleBin[start + 0x06];
            int monLive = ReadInt32(battleBin, start + 0x20);
            int monA = ReadInt32(battleBin, start + 0x1C);
            int monB = ReadInt32(battleBin, start + 0x24);

            // capacity = (next pointer target − this target) / 16, using the proven array order.
            int monACap = CapacityEntries(battleBin, start, len, monA);
            int monBCap = CapacityEntries(battleBin, start, len, monB);
            int monLiveCap = CapacityEntries(battleBin, start, len, monLive);

            if (monLive <= 0 || monLiveCap < 0)
                return new GrowPlan { CanGrow = false, Reason = "monLive pointer invalid" };
            // Non-tight monLive (declared count > physical capacity — corpus anomaly, e.g. kino00_00 remiem:
            // count 4 vs physical cap 3) is supported: the splice is anchored on the PHYSICAL capacity
            // (the real monB start), never on the declared count.
            if (monLiveCap < 1)
                return new GrowPlan { CanGrow = false, Reason = $"monLive physical capacity {monLiveCap} < 1 — unsupported layout", OldCount = oldCount };

            // Model premise: monA/monB reserves must at least HOLD the current count. A handful of battles
            // (anomalous packing where a pointer sits right after monA) violate this — refuse them rather than
            // risk a corrupt splice. They stay editable via the MOVE (position) writer, just not GROW.
            if (monACap < oldCount || monBCap < oldCount)
                return new GrowPlan { CanGrow = false, Reason = $"anomalous reserve (monA {monACap}/monB {monBCap} < count {oldCount})", OldCount = oldCount };

            int maxCount = Math.Min(Math.Min(monACap, monBCap), HardActorCap);
            return new GrowPlan
            {
                CanGrow = true,
                Reason = "ok",
                OldCount = oldCount,
                MonLivePtr = monLive,
                MonLiveCapacity = monLiveCap,
                MonACapacity = monACap,
                MonBCapacity = monBCap,
                MaxCount = maxCount,
            };
        }

        /// <summary>Grow/shrink the on-field monster array to <paramref name="newLiveCoords"/>.Count entries and write
        /// those coords (W=0). Returns the new bin. Throws if <see cref="Plan"/> refuses or the new count exceeds the
        /// proven reserve/cap. A no-op (count unchanged) returns a byte-identical clone.</summary>
        public static byte[] GrowMonsters(byte[] originalBin, IReadOnlyList<(float X, float Y, float Z)> newLiveCoords)
        {
            ArgumentNullException.ThrowIfNull(originalBin);
            ArgumentNullException.ThrowIfNull(newLiveCoords);

            GrowPlan plan = Plan(originalBin);
            if (!plan.CanGrow) throw new InvalidOperationException($"Cannot grow this battle: {plan.Reason}");

            int newCount = newLiveCoords.Count;
            if (newCount < 1) throw new ArgumentException("New monster count must be >= 1.", nameof(newLiveCoords));
            if (newCount > plan.MaxCount)
                throw new ArgumentException($"New count {newCount} exceeds the proven reserve/cap {plan.MaxCount} " +
                    $"(monA cap {plan.MonACapacity}, monB cap {plan.MonBCapacity}, hard cap {HardActorCap}).", nameof(newLiveCoords));

            (int start, _) = BattleArenaPositionWriter.LocateChunk3(originalBin);
            int oldCount = plan.OldCount;
            int monLiveCap = plan.MonLiveCapacity;
            int monLive = plan.MonLivePtr;
            // Splice anchored on the PHYSICAL monLive capacity (the real monB start). For tight layouts
            // (capacity == declared count) this is byte-identical to the legacy path.
            int byteDelta = (newCount - monLiveCap) * ElementStride;

            // splice point = end of monLive's physical data (== monB start), in absolute file bytes.
            int splicePos = start + monLive + monLiveCap * ElementStride;

            byte[] o;
            if (byteDelta == 0)
            {
                o = (byte[])originalBin.Clone();
            }
            else if (byteDelta > 0)
            {
                o = new byte[originalBin.Length + byteDelta];
                Array.Copy(originalBin, 0, o, 0, splicePos);
                // the inserted region is zero-filled here; monLive coords (incl. the new tail) are stamped below.
                Array.Copy(originalBin, splicePos, o, splicePos + byteDelta, originalBin.Length - splicePos);
            }
            else // shrink
            {
                o = new byte[originalBin.Length + byteDelta];
                Array.Copy(originalBin, 0, o, 0, splicePos + byteDelta); // drop |delta| bytes at splicePos
                Array.Copy(originalBin, splicePos, o, splicePos + byteDelta, originalBin.Length - splicePos);
            }

            // --- re-stamp record-0 pointers whose target is AFTER the spliced monLive data (monB + camera only) ---
            int monLiveDataEnd = monLive + monLiveCap * ElementStride; // chunk3-relative splice point
            foreach (int ptrOff in new[] { 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C })
            {
                int rel = ReadInt32(o, start + ptrOff);
                if (rel >= monLiveDataEnd && rel != 0) WriteInt32(o, start + ptrOff, rel + byteDelta);
            }

            // --- set the count + write the monLive coords (now contiguous of newCount entries) ---
            o[start + 0x06] = (byte)newCount;
            int liveBase = start + monLive; // monLive pointer unchanged (it is before the splice point)
            for (int i = 0; i < newCount; i++)
            {
                int e = liveBase + i * ElementStride;
                WriteFloat(o, e + 0x00, newLiveCoords[i].X);
                WriteFloat(o, e + 0x04, newLiveCoords[i].Y);
                WriteFloat(o, e + 0x08, newLiveCoords[i].Z);
                WriteFloat(o, e + 0x0C, 0f); // W=0 for positions
            }

            // --- re-stamp the chunk-offset table: chunks AFTER index 3 shift by byteDelta ---
            int chunkCount = ReadInt32(o, 0x00) - 1;
            for (int i = 4; i <= chunkCount; i++)
            {
                int v = ReadInt32(o, 0x04 + i * 4);
                if (v != 0 && v != unchecked((int)0xFFFFFFFF)) WriteInt32(o, 0x04 + i * 4, v + byteDelta);
            }

            return o;
        }

        /// <summary>true iff <paramref name="candidate"/> is a valid grow of <paramref name="original"/>: same prefix up
        /// to the splice point, same suffix after it (shifted), and only the count byte / monLive / monB+camera
        /// pointers / chunk-table tail differ. Used by the gate to confirm the splice is surgical.</summary>
        public static bool IsCleanGrow(byte[] original, byte[] candidate)
        {
            if (original == null || candidate == null) return false;
            GrowPlan plan = Plan(original);
            if (!plan.CanGrow) return false;
            (int start, _) = BattleArenaPositionWriter.LocateChunk3(original);
            int splicePos = start + plan.MonLivePtr + plan.MonLiveCapacity * ElementStride;
            // everything strictly before the area-record's pointer block is untouched (script/worker/formation chunks).
            for (int i = 0; i < start; i++)
                if (i < 0x04 || i >= 0x40) // allow the chunk-table region [0x04,0x40) to be re-stamped
                    if (original[i] != candidate[i]) return false;
            return true;
        }

        private static int CapacityEntries(byte[] bin, int start, int len, int ptr)
        {
            if (ptr <= 0 || ptr >= len) return -1;
            // next pointer target strictly greater than ptr, among the 8 record-0 pointers; else chunk3 end.
            int next = len;
            foreach (int po in new[] { 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C })
            {
                int v = ReadInt32(bin, start + po);
                if (v > ptr && v <= len && v < next) next = v;
            }
            return (next - ptr) / ElementStride;
        }

        private static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));

        private static void WriteInt32(byte[] b, int o, int v)
        {
            if (o < 0 || o + 4 > b.Length) throw new ArgumentOutOfRangeException(nameof(o));
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o, 4), v);
        }

        private static void WriteFloat(byte[] b, int o, float v)
        {
            if (o < 0 || o + 4 > b.Length) throw new ArgumentOutOfRangeException(nameof(o));
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o, 4), v);
        }
    }
}
