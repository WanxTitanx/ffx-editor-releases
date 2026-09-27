// ============================================================================
// MonsterAthAnimCatalog — named animation cycles + motion ids per monster (.ath data)
// PURPOSE : plain-data tables (cycles + motions per monsterId) with normalization and lookup helpers; used by
//           the Aurora overlay to label animations and match exported CHR clips by token.
// WHY     : values come from the game's ffx_ps2/proj2/chr/ath/m/*.ath headers (Fahrenheit project, LGPL) —
//           re-implemented as data with attribution, not their code. Avalonia/dependency-free for headless RT0.
// EVIDENCE: docs/reverse/FFX_ATH_ANIM_CATALOG_IMPORT_2026-08-01.md; fahrenheit MonsterAnimationCycle/MotionId.
// MAINT   : tables only cover imported monsters — unknown ids return EMPTY (honest fallback, never guess).
//           Add cycles/motions by importing the .ath source; keep NormalizeMonsterId canonical ("mNNN").
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Monster
{
    /// <summary>
    /// 🐾 MONSTER .ath ANIMATION CATALOG — named animation cycles + motion ids per monster.
    ///
    /// DATA SOURCE: the Fahrenheit project (fahrenheit-crew/fahrenheit, LGPL-3.0-or-later) generated these tables
    /// from the game's own <c>ffx_ps2/ffx/proj2/chr/ath/m/*.ath</c> headers (Switch release). We re-implement the
    /// VALUES as plain data (with attribution), NOT their code — the naming scheme is the game's own symbol set
    /// (e.g. <c>m117_bat_pos_loop01_s</c> = "bat, position, loop 01, start"). Cross-check doc:
    /// docs/reverse/FFX_ATH_ANIM_CATALOG_IMPORT_2026-08-01.md.
    ///
    /// The Aurora overlay uses these names to (a) label the animation dropdown per monster and (b) match a clip
    /// inside the exported CHR glTF by token similarity (e.g. "loop01" → clip whose name contains "loop01").
    /// The raw numeric ids are the game's own animation ids — useful for future runtime work, honest as-is.
    ///
    /// Deliberately Avalonia-free and dependency-free so a headless RT0 gate can link it directly.
    /// </summary>
    public static class MonsterAthAnimCatalog
    {
        public sealed record AnimEntry(string Name, int Id);

        /// <summary>All animation cycles known per monsterId (from .ath MonsterAnimationCycle defines).</summary>
        private static readonly Dictionary<string, List<AnimEntry>> Cycles = new(StringComparer.OrdinalIgnoreCase)
        {
            // m109 (gaaa — lupine fiend). Source: fahrenheit src/core/ffx/ids/MonsterAnimationCycle.cs.
            ["m109"] = new List<AnimEntry>
            {
                new("m109_gaaa_act01_e", 193),
                new("m109_gaaa_loop01_s", 194),
            },
            // m117 (bat). Source: fahrenheit MonsterAnimationCycle.cs.
            ["m117"] = new List<AnimEntry>
            {
                new("m117_punch_loop01_e", 29),
                new("m117_punch_act01_s", 30),
                new("m117_punch_act01_e", 159),
                new("m117_punch_loop02_s", 160),
                new("m117_bat_pos_act01_e", 69),
                new("m117_bat_pos_loop01_s", 70),
            },
            // m124 (aerial — flying fiend). Source: fahrenheit MonsterAnimationCycle.cs.
            ["m124"] = new List<AnimEntry>
            {
                new("m124_lookdown_act01_e", 29),
                new("m124_lookdown_loop01_s", 30),
                new("m124_naname_miru_act01_e", 29),
                new("m124_naname_miru_loop01_s", 30),
                new("m124_naname_miru_act02_s", 80),
                new("m124_naname_miru_loop01_e", 79),
                new("m124_fukumi_warai_act01_e", 19),
                new("m124_fukumi_warai_loop01_s", 20),
                new("m124_fukumi_warai_act02_s", 65),
                new("m124_fukumi_warai_loop01_e", 64),
                new("m124_aerial_act01_e", 99),
                new("m124_aerial_loop01_s", 100),
                new("m124_aerial_act02_s", 160),
                new("m124_aerial_loop01_e", 159),
                new("m124_aerial_act02_e", 219),
                new("m124_aerial_loop02_s", 220),
            },
        };

        /// <summary>Motion ids known per monsterId (from .ath MonsterMotionId defines, e.g. m001_sp3_07 = 0x1001106D).</summary>
        private static readonly Dictionary<string, List<AnimEntry>> Motions = new(StringComparer.OrdinalIgnoreCase)
        {
            // m001 (koura — turtle/armored fiend). Source: fahrenheit src/core/ffx/ids/MonsterMotionId.cs.
            ["m001"] = new List<AnimEntry>
            {
                new("m001_sp3_07", 0x1001106D),
                new("m001_sp3_06", 0x1001106C),
                new("m001_sp3_05", 0x1001106B),
                new("m001_sp3_04", 0x1001106A),
                new("m001_sp3_03", 0x10011069),
                new("m001_sp3_02", 0x10011068),
                new("m001_sp3_01", 0x10011067),
                new("m001_sp3_00", 0x10011066),
                new("m001_sp2_07", 0x10011065),
                new("m001_sp2_06", 0x10011064),
                new("m001_sp2_05", 0x10011063),
                new("m001_sp2_04", 0x10011062),
            },
        };

        /// <summary>Normalize a monster id ("m117", "117", 117) to the canonical "mNNN" key used by the tables.</summary>
        public static string NormalizeMonsterId(string? monsterId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
                return string.Empty;
            string t = monsterId.Trim();
            if (t.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                t = t[1..];
            if (int.TryParse(t, out int n) && n >= 0)
                return "m" + n.ToString("D3");
            return monsterId.Trim().ToLowerInvariant();
        }

        /// <summary>Animation cycles for a monster id. Empty when unknown (honest: the .ath batch only covers the
        /// monsters we imported; the rest have no named table yet — the overlay falls back to raw clips).</summary>
        public static IReadOnlyList<AnimEntry> GetCycles(string? monsterId)
        {
            string key = NormalizeMonsterId(monsterId);
            return key.Length > 0 && Cycles.TryGetValue(key, out List<AnimEntry>? list)
                ? list
                : Array.Empty<AnimEntry>();
        }

        /// <summary>Motion ids for a monster id. Empty when unknown.</summary>
        public static IReadOnlyList<AnimEntry> GetMotions(string? monsterId)
        {
            string key = NormalizeMonsterId(monsterId);
            return key.Length > 0 && Motions.TryGetValue(key, out List<AnimEntry>? list)
                ? list
                : Array.Empty<AnimEntry>();
        }

        /// <summary>All cycles across every imported monster (for a "pick any" dropdown).</summary>
        public static IReadOnlyList<AnimEntry> AllCycles =>
            Cycles.Values.SelectMany(v => v).DistinctBy(e => e.Id).OrderBy(e => e.Id).ToList();

        /// <summary>Total imported cycle count (RT0 gate: > 0 and stable).</summary>
        public static int ImportedCycleCount => AllCycles.Count;

        /// <summary>Monsters with an imported cycle table (RT0 gate: non-empty).</summary>
        public static IReadOnlyList<string> ImportedMonsters =>
            Cycles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

