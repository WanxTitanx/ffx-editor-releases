using FFXProjectEditor.FfxLib.Monster;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    // Capture Cascade Cap-1 Phase C — RT0 gate for `MonsterCaptureFlagWriter`.
    //
    // Walk every `mNNN.bin` under the monster root and prove three properties for the byte at
    // `bytes[StatSheetPointer + 0x78]` (ArenaId, the per-monster capture flag):
    //
    //   1. SAME-VALUE byte-identity: WriteCaptureFlag(bin, currentValue) MUST equal `bin`.
    //      (No-edit save through the writer is byte-identical — the strongest possible RT0.)
    //
    //   2. FLIP-AND-RESTORE round-trip: WriteCaptureFlag(WriteCaptureFlag(bin, target), original)
    //      MUST equal `bin`. The "target" is chosen out-of-band per category:
    //        - if currently 0xFF (uncap)        target = 0x68 (sidecar slot, away from vanilla MA)
    //        - else if currently 0x67 or below  target = 0xFF (flip to uncapturable, then restore)
    //        - else                              target = 0x68 (already sidecar; pick a different slot)
    //
    //   3. SLOT-ONLY DIFF: WriteCaptureFlag(bin, target) MUST differ from `bin` only at the single
    //      byte `[StatSheetPointer + 0x78]` (one-byte diff, padding stays 0x00, every other byte
    //      byte-identical). Guarantees zero collateral on monster identity fields, text pool,
    //      worker, AI, loot, audio, headers and padding.
    //
    // Also asserts that 100% of monsters in the corpus have ArenaIdPadding == 0x00 (Phase B
    // evidence: 58/58 samples). A failure here would point to an unknown variant struct that the
    // writer is correctly refusing to touch.
    //
    // Runs via:  FFXProjectEditor.exe --monster-capture-bit-rt0 [monsterRoot]
    internal static class MonsterCaptureFlagRt0
    {
        public static int Run(string root)
        {
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"monster root not found: {root}");
                return 2;
            }

            var files = Directory.EnumerateFiles(root, "m*.bin", SearchOption.AllDirectories)
                .Where(p => { string n = Path.GetFileNameWithoutExtension(p); return n.Length == 4 && n[0] == 'm' && n.Skip(1).All(char.IsDigit); })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = 0;
            int sameValueRt0 = 0;
            int flipRestoreOk = 0;
            int slotOnlyDiff = 0;
            int paddingZero = 0;
            int headerFail = 0;
            int paddingNonZero = 0;
            var examples = new List<string>();
            int uncap = 0, vanillaCap = 0, sidecarCap = 0, otherCat = 0;

            foreach (string path in files)
            {
                byte[] orig = File.ReadAllBytes(path);
                total++;
                string name = Path.GetFileNameWithoutExtension(path);

                if (!MonsterCaptureFlagWriter.TryGetStatSheetPointer(orig, out int ssp))
                {
                    headerFail++;
                    if (examples.Count < 12) examples.Add($"{name}: HEADER FAIL (StatSheetPointer at +0x0C invalid or out of range)");
                    continue;
                }

                int off = ssp + MonsterCaptureFlagWriter.StatBlockArenaIdOffset;
                byte original = orig[off];
                byte pad = orig[off + 1];

                if (pad != 0x00)
                {
                    paddingNonZero++;
                    if (examples.Count < 12) examples.Add($"{name}: PADDING NON-ZERO (off=+0x{off + 1:X4} = 0x{pad:X2}); writer refused — investigate variant.");
                    continue;
                }
                paddingZero++;

                if (MonsterCaptureFlagWriter.IsUncapturable(original)) uncap++;
                else if (MonsterCaptureFlagWriter.IsVanillaArenaSlot(original)) vanillaCap++;
                else if (MonsterCaptureFlagWriter.IsSidecarArenaSlot(original)) sidecarCap++;
                else otherCat++;

                byte[] re = MonsterCaptureFlagWriter.WriteCaptureFlag(orig, original);
                if (re.AsSpan().SequenceEqual(orig)) sameValueRt0++;
                else
                {
                    if (examples.Count < 12)
                    {
                        int firstDiff = 0; while (firstDiff < orig.Length && orig[firstDiff] == re[firstDiff]) firstDiff++;
                        examples.Add($"{name}: SAME-VALUE NOT RT0 — diff @0x{firstDiff:X4} (writer must be a no-op when newSlot==current)");
                    }
                    continue;
                }

                byte target;
                if (MonsterCaptureFlagWriter.IsUncapturable(original)) target = MonsterCaptureFlagWriter.SidecarArenaSlotMin;
                else if (MonsterCaptureFlagWriter.IsVanillaArenaSlot(original)) target = MonsterCaptureFlagWriter.Uncapturable;
                else target = (byte)((original == MonsterCaptureFlagWriter.SidecarArenaSlotMin)
                                    ? (MonsterCaptureFlagWriter.SidecarArenaSlotMin + 1)
                                    : MonsterCaptureFlagWriter.SidecarArenaSlotMin);

                byte[] flipped = MonsterCaptureFlagWriter.WriteCaptureFlag(orig, target);
                int diffs = 0; int firstFlipDiff = -1;
                for (int i = 0; i < orig.Length; i++)
                {
                    if (flipped[i] != orig[i])
                    {
                        diffs++;
                        if (firstFlipDiff < 0) firstFlipDiff = i;
                    }
                }
                bool slotOnly = diffs == 1 && firstFlipDiff == off && flipped[off] == target && flipped[off + 1] == 0x00;
                if (slotOnly) slotOnlyDiff++;
                else if (examples.Count < 12)
                {
                    examples.Add($"{name}: SLOT-ONLY DIFF FAIL — diffs={diffs} firstDiff=0x{firstFlipDiff:X4} (expected exactly 1 byte at +0x{off:X4})");
                }

                byte[] restored = MonsterCaptureFlagWriter.WriteCaptureFlag(flipped, original);
                if (restored.AsSpan().SequenceEqual(orig)) flipRestoreOk++;
                else if (examples.Count < 12)
                {
                    int firstRestoreDiff = 0; while (firstRestoreDiff < orig.Length && orig[firstRestoreDiff] == restored[firstRestoreDiff]) firstRestoreDiff++;
                    examples.Add($"{name}: FLIP-RESTORE FAIL — diff @0x{firstRestoreDiff:X4} after restore (writer not idempotent)");
                }
            }

            Console.WriteLine("=== MonsterCaptureFlagWriter RT0 (Capture Cascade Cap-1 Phase C) ===");
            Console.WriteLine($"monster root            : {root}");
            Console.WriteLine($"monsters scanned        : {total}");
            Console.WriteLine($"header parsed OK        : {total - headerFail}/{total}");
            Console.WriteLine($"padding == 0x00         : {paddingZero}/{total - headerFail}");
            Console.WriteLine($"  - uncapturable (0xFF) : {uncap}");
            Console.WriteLine($"  - vanilla MA slot     : {vanillaCap}  (0x00..0x67)");
            Console.WriteLine($"  - sidecar slot        : {sidecarCap}  (0x68..0xFE)");
            Console.WriteLine($"  - other (unknown)     : {otherCat}");
            int eligible = total - headerFail - paddingNonZero;
            Console.WriteLine($"same-value RT0          : {sameValueRt0}/{eligible}  (writer no-op byte-identical)");
            Console.WriteLine($"slot-only diff (flip)   : {slotOnlyDiff}/{eligible}  (1-byte diff at +0x78, padding 0x00)");
            Console.WriteLine($"flip-and-restore RT0    : {flipRestoreOk}/{eligible}  (round-trip byte-identical)");
            if (headerFail > 0)        Console.WriteLine($"header parse fails      : {headerFail}");
            if (paddingNonZero > 0)    Console.WriteLine($"padding non-zero (refused): {paddingNonZero}");
            if (examples.Count > 0)
            {
                Console.WriteLine("examples:");
                foreach (string e in examples) Console.WriteLine("  " + e);
            }
            bool pass = headerFail == 0
                     && paddingNonZero == 0
                     && sameValueRt0 == eligible
                     && slotOnlyDiff == eligible
                     && flipRestoreOk == eligible
                     && eligible > 0;
            Console.WriteLine(pass
                ? "VERDICT: PASS — Capture Cascade Cap-1 writer is byte-safe across the corpus (1-byte slot-only diff, idempotent restore, padding stays 0)."
                : "VERDICT: DRIFT — see examples above. Cap-1 writer NOT promoted; investigate variant or writer regression.");
            return pass ? 0 : 1;
        }
    }
}
