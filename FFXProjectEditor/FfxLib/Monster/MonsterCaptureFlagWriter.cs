// ============================================================================
// MonsterCaptureFlagWriter — byte-level writer for the per-monster capture flag (Capture Cascade Cap-1)
// PURPOSE : flips ONE byte (ArenaId at StatSheetPointer+0x78) in a monster .bin, keeping every other byte
//           identical; refuses unknown variants (non-zero padding at +0x79).
// WHY     : the capture flag IS MonsterStatSheet.ArenaId (sbyte @ statBlock+0x64); StatSheetPointer = u32le@0x0C.
//           0xFF = uncapturable, 0x00..0x67 = vanilla arena slot, 0x68..0xFE = sidecar slot (Spira Reforge).
//           Slot-only diff avoids any text/pool/padding regression vs a full Read/Write round-trip.
// EVIDENCE: RE Phase B, 58 samples; docs/reverse/FFX_SPIRA_REFORGE_CAPTURE_BIT_M_HEADER_RE_RESULT_2026-06-16.md §5.1.
// MAINT   : the +0x14 statBlock base is RE-derived — keep offsets/length guards in sync with Monster_Structs.
//           Writer intentionally returns a CLONE (never mutates the input buffer).
// ============================================================================
using System;

namespace FFXProjectEditor.FfxLib.Monster
{
    // Capture Cascade Cap-1 (Phase C, Jarvis-CAPCAS-WRITE, 2026-06-16) — single-byte writer for
    // the per-monster capture flag in `m###.bin`.
    //
    // Background (Phase B RE, doc-only, v2.123.3.1, 58 amostras validadas):
    //   - The capture flag is `MonsterStatSheet.ArenaId` (sbyte at StatBlock+0x64), which is
    //     `bytes[StatSheetPointer + 0x14 + 0x64] == bytes[StatSheetPointer + 0x78]`.
    //     `StatSheetPointer = uint32_le(bytes[0x0C])` from the `MonsterHeaderFile`.
    //   - Semantics: 0xFF = uncapturable (sbyte -1 sentinel); 0x00..0x67 = capturable, value = slot
    //     in the 104-entry vanilla Monster Arena `Captured` table; 0x68..0xFE = sidecar slot (Spira
    //     Reforge / Capture Cascade region — does NOT collide with vanilla bestiary, runtime accept
    //     gated by RT2).
    //   - The padding byte `bytes[StatSheetPointer + 0x79]` is `0x00` in 58/58 samples (vanilla and
    //     modded). Writer keeps it `0x00` and refuses to touch any monster where it is non-zero
    //     (defensive — would be evidence of an unknown variant struct).
    //
    // Why this writer is byte-level (no full struct parse / re-serialize):
    //   - Phase C is intentionally narrow: flip ONE byte per monster, guarantee byte-identity for
    //     all 8 fields adjacent to ArenaId (ForcedAction / MonsterId / ModelId / CtbIconId /
    //     DoomCount / ArenaIdPadding / Model2Id) AND for every other byte in the file.
    //   - Going through `Monster_File.Read(...).Write()` would touch the StatSheet text pool /
    //     padding (the `--monster-rt0` audit already shows it is RT0 byte-safe across the corpus,
    //     but Phase C wants a strictly slot-only diff with zero risk of text/padding regression).
    //   - Future Cap-2/Cap-3 phases (sidecar tagging, persist hook, in-game flip) build on this
    //     same writer; keeping it tiny and pure makes the audit trivial.
    //
    // Reference: `docs/reverse/FFX_SPIRA_REFORGE_CAPTURE_BIT_M_HEADER_RE_RESULT_2026-06-16.md` §5.1.
    public static class MonsterCaptureFlagWriter
    {
        public const int HeaderStatSheetPointerOffset = 0x0C;
        public const int StatBlockArenaIdOffset = 0x14 + 0x64;

        public const byte Uncapturable = 0xFF;
        public const byte VanillaArenaSlotMin = 0x00;
        public const byte VanillaArenaSlotMax = 0x67;
        public const byte SidecarArenaSlotMin = 0x68;
        public const byte SidecarArenaSlotMax = 0xFE;

        public static bool TryGetStatSheetPointer(byte[] monBin, out int statSheetPointer)
        {
            statSheetPointer = 0;
            if (monBin == null || monBin.Length < HeaderStatSheetPointerOffset + 4) return false;
            uint p = (uint)monBin[HeaderStatSheetPointerOffset]
                   | ((uint)monBin[HeaderStatSheetPointerOffset + 1] << 8)
                   | ((uint)monBin[HeaderStatSheetPointerOffset + 2] << 16)
                   | ((uint)monBin[HeaderStatSheetPointerOffset + 3] << 24);
            if (p == 0 || p > int.MaxValue) return false;
            int needed = (int)p + StatBlockArenaIdOffset + 2;
            if (needed > monBin.Length) return false;
            statSheetPointer = (int)p;
            return true;
        }

        public static int GetCaptureFlagFileOffset(byte[] monBin)
        {
            if (!TryGetStatSheetPointer(monBin, out int ssp))
                throw new InvalidOperationException("monster bin: invalid or missing StatSheetPointer at +0x0C");
            return ssp + StatBlockArenaIdOffset;
        }

        public static byte ReadCaptureFlag(byte[] monBin) => monBin[GetCaptureFlagFileOffset(monBin)];

        public static byte ReadPaddingByte(byte[] monBin) => monBin[GetCaptureFlagFileOffset(monBin) + 1];

        public static bool IsUncapturable(byte v) => v == Uncapturable;
        public static bool IsVanillaArenaSlot(byte v) => v <= VanillaArenaSlotMax;
        public static bool IsSidecarArenaSlot(byte v) => v >= SidecarArenaSlotMin && v <= SidecarArenaSlotMax;

        public static byte[] WriteCaptureFlag(byte[] monBin, byte newSlot)
        {
            if (monBin == null) throw new ArgumentNullException(nameof(monBin));
            int off = GetCaptureFlagFileOffset(monBin);
            if (monBin[off + 1] != 0x00)
            {
                throw new InvalidOperationException(
                    $"monster bin: ArenaIdPadding at +0x{off + 1:X} is 0x{monBin[off + 1]:X2}, expected 0x00. " +
                    "Writer refuses unknown variant (58/58 samples in evidence corpus had padding=0x00).");
            }
            byte[] copy = (byte[])monBin.Clone();
            copy[off] = newSlot;
            copy[off + 1] = 0x00;
            return copy;
        }
    }
}
