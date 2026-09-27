using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — high-level "author a monster into a battle" orchestrator. Keeps chunk2 (the formation
    /// lineup) and chunk3 (the spawn anchors) in lock-step so the UI can ADD/REMOVE a monster in one call.
    ///
    /// KEY MODEL (corpus-proven): the formation-LIVE count (chunk2 slots != 0xFFFF) is NOT the same as
    /// <c>MonsterPositionCount</c> (chunk3 +0x06, the RESERVED anchor count) — they differ in 305/863 battles
    /// (e.g. dome00 reserves 4 anchors but the formation has 1 live monster). So "add a monster" has TWO regimes:
    ///   • REGIME 1 — there is a reserved anchor free (next formation slot index &lt; MonsterPositionCount): just
    ///     fill the next empty formation slot (chunk2-only, slot-safe). The spawn anchor already exists → NO chunk3
    ///     change. This is the common, lowest-risk case.
    ///   • REGIME 2 — the formation already uses every reserved anchor (next index == MonsterPositionCount): GROW
    ///     chunk3's monLive by one (<see cref="BattleArenaGrowWriter"/>) THEN fill the formation slot.
    /// Either way the new slot CLONES the last live slot's raw value (species + the 0x1000 "live on field" flag);
    /// the user re-types the species in the Formation Editor and drags the position in the MapViewer afterwards.
    ///
    /// REMOVE clears the last live formation slot (chunk2-only); the reserved chunk3 anchor simply goes back to
    /// being unused (it never needs shrinking to "remove a monster from the battle").
    ///
    /// Both paths are byte-safe + offline-gated (RuntimeTools/BattleArenaGrowLab). Whether the ENGINE spawns the new
    /// actor is RT2 (in-game-unproven banner). Dependency-light (FfxLib only).
    /// </summary>
    public static class BattleArenaAuthor
    {
        /// <summary>Append a monster (regime 1 or 2 chosen automatically), cloning the last live slot. Returns new bin.</summary>
        public static byte[] AddMonsterCloneLast(byte[] battleBin)
        {
            ArgumentNullException.ThrowIfNull(battleBin);

            ushort[] slots = ReadSlots(battleBin);
            int[] liveIdx = LiveIndices(slots);
            if (liveIdx.Length == 0)
                throw new InvalidOperationException("The formation is empty — nothing to clone.");
            int newIndex = FirstEmptySlot(slots);
            if (newIndex < 0)
                throw new InvalidOperationException("Formation full (8 slots) — no room for another monster.");
            ushort cloneRaw = slots[liveIdx[^1]]; // last live slot's raw (species + 0x1000 live flag)

            int monPosCount = MonsterPositionCount(battleBin);

            if (newIndex < monPosCount)
            {
                // REGIME 1 — a reserved chunk3 anchor already exists for this slot index; chunk2-only fill.
                return SetFormationSlot(battleBin, newIndex, cloneRaw);
            }

            // REGIME 2 — out of reserved anchors → grow chunk3, then fill the slot.
            BattleArenaGrowWriter.GrowPlan plan = BattleArenaGrowWriter.Plan(battleBin);
            if (!plan.CanGrow)
                throw new InvalidOperationException($"No free reserved anchor and GROW unavailable: {plan.Reason}");
            if (newIndex + 1 > plan.MaxCount)
                throw new InvalidOperationException(
                    $"Arena cap: {plan.MaxCount} anchors (reserve monA {plan.MonACapacity}/monB {plan.MonBCapacity}, cap {BattleArenaGrowWriter.HardActorCap}).");

            (int oldCount, IReadOnlyList<(float X, float Y, float Z)> live) = ReadLive(battleBin);
            var coords = live.ToList();
            (float lx, float ly, float lz) = coords.Count > 0 ? coords[^1] : (0f, 0f, 0f);
            coords.Add((lx + 2f, ly, lz + 2f));
            byte[] grown = BattleArenaGrowWriter.GrowMonsters(battleBin, coords);
            return SetFormationSlot(grown, newIndex, cloneRaw);
        }

        /// <summary>Remove the last live monster: clear its formation slot (chunk2-only). Refuses to drop below 1.</summary>
        public static byte[] RemoveLastMonster(byte[] battleBin)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            ushort[] slots = ReadSlots(battleBin);
            int[] liveIdx = LiveIndices(slots);
            if (liveIdx.Length <= 1)
                throw new InvalidOperationException("Need at least 2 monsters to remove one (I will not leave the formation at 0).");
            return SetFormationSlot(battleBin, liveIdx[^1], 0xFFFF);
        }

        /// <summary>True if a monster can be added (a free formation slot + either a reserved anchor or a valid grow).</summary>
        public static bool CanAdd(byte[] battleBin, out string reason)
        {
            ushort[] slots = ReadSlots(battleBin);
            if (LiveIndices(slots).Length == 0) { reason = "empty formation (nothing to clone)"; return false; }
            int newIndex = FirstEmptySlot(slots);
            if (newIndex < 0) { reason = "formation full (8 slots)"; return false; }
            if (newIndex < MonsterPositionCount(battleBin)) { reason = "ok (free reserved anchor)"; return true; }
            BattleArenaGrowWriter.GrowPlan plan = BattleArenaGrowWriter.Plan(battleBin);
            if (!plan.CanGrow) { reason = $"no reserved anchor and GROW unavailable ({plan.Reason})"; return false; }
            if (newIndex + 1 > plan.MaxCount) { reason = $"arena cap ({plan.MaxCount})"; return false; }
            reason = "ok (via grow)"; return true;
        }

        /// <summary>True if there is a live monster to remove and at least 2 are present.</summary>
        public static bool CanRemove(byte[] battleBin, out string reason)
        {
            int live = LiveIndices(ReadSlots(battleBin)).Length;
            if (live <= 1) { reason = "need >= 2 monsters"; return false; }
            reason = "ok"; return true;
        }

        private static int[] LiveIndices(ushort[] slots) =>
            Enumerable.Range(0, slots.Length).Where(i => slots[i] != 0xFFFF).ToArray();

        private static int FirstEmptySlot(ushort[] slots)
        {
            for (int i = 0; i < slots.Length; i++) if (slots[i] == 0xFFFF) return i;
            return -1;
        }

        private static int MonsterPositionCount(byte[] bin)
        {
            (int start, int len) = BattleArenaPositionWriter.LocateChunk3(bin);
            return (start >= 0 && len >= 0x07) ? bin[start + 0x06] : 0;
        }

        private static (int Count, IReadOnlyList<(float X, float Y, float Z)> Coords) ReadLive(byte[] bin)
        {
            BattleArenaAnchors_File a = BattleArenaAnchors_File.ReadFromBattleBin(string.Empty, bin);
            BattleArena_AnchorGroup? g = a.Areas.Count > 0 ? a.Areas[0][BattleArena_AnchorRole.MonsterLive] : null;
            if (g == null) return (0, Array.Empty<(float, float, float)>());
            var list = g.Anchors.Select(an => (an.X, an.Y, an.Z)).ToList();
            return (list.Count, list);
        }

        private static ushort[] ReadSlots(byte[] bin)
        {
            Battle_File bf = Battle_File.Read(string.Empty, bin);
            if (bf.Formation == null)
                throw new InvalidOperationException("This battle does not expose the formation (chunk2).");
            return bf.Formation.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();
        }

        private static byte[] SetFormationSlot(byte[] bin, int slotIndex, ushort rawValue)
        {
            Battle_File bf = Battle_File.Read(string.Empty, bin);
            if (bf.Formation == null || !bf.CanWriteFormation)
                throw new InvalidOperationException("This battle does not expose a writable formation (chunk2).");
            ushort[] slots = bf.Formation.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();
            if (slotIndex < 0 || slotIndex >= slots.Length)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            slots[slotIndex] = rawValue;
            return bf.WriteWithFormationSlots(slots);
        }
    }
}
