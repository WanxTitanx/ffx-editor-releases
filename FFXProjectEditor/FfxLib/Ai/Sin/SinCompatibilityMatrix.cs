using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// Compatibility matrix: which SIN universal presets a monster can receive.
    /// v1 = manual lookup for Macalania pilot. Future = heuristic from AiScriptFile.
    /// </summary>
    public static class SinCompatibilityMatrix
    {
        /// <summary>Monster ID → list of compatible UNI preset IDs.</summary>
        static readonly Dictionary<string, string[]> ManualMatrix = new(StringComparer.OrdinalIgnoreCase)
        {
            // Macalania Woods (mcyt00)
            ["m004"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-004" },
            ["m012"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-004", "UNI-006" },
            ["m019"] = new[] { "UNI-001", "UNI-003", "UNI-004", "UNI-005", "UNI-006" },
            ["m037"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-004", "UNI-006" },

            // Macalania Field (mcfr00)
            ["m003"] = new[] { "UNI-001", "UNI-003", "UNI-004", "UNI-007" },
            ["m026"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-004" },
            ["m033"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-006" },
            ["m081"] = new[] { "UNI-001", "UNI-003", "UNI-004", "UNI-005", "UNI-006" },
            ["m217"] = new[] { "UNI-001", "UNI-002", "UNI-003", "UNI-004", "UNI-006", "UNI-007" },

            // Macalania bosses
            ["m087"] = new[] { "UNI-001", "UNI-003", "UNI-004", "UNI-006", "UNI-008" },
        };

        /// <summary>Returns compatible preset IDs for a monster, or empty if unknown.</summary>
        public static IReadOnlyList<string> GetCompatiblePresetIds(string monsterId)
        {
            string key = monsterId.Trim().ToLowerInvariant();
            if (!key.StartsWith("m"))
                key = "m" + key;

            return ManualMatrix.TryGetValue(key, out var ids)
                ? ids
                : Array.Empty<string>();
        }

        /// <summary>Returns compatible <see cref="AiSinPresetEntry"/> for a monster, filtered by threat cap and bake-ready.</summary>
        public static IReadOnlyList<AiSinPresetEntry> GetCompatiblePresets(
            string monsterId, int threatCap, bool onlyBakeReady = true)
        {
            var presetIds = GetCompatiblePresetIds(monsterId);
            if (presetIds.Count == 0)
                return Array.Empty<AiSinPresetEntry>();

            return AiSinPresetCatalog.All
                .Where(e => presetIds.Contains(e.Id, StringComparer.OrdinalIgnoreCase))
                .Where(e => e.Threat <= threatCap)
                .Where(e => !onlyBakeReady || e.IsBakeReady)
                .ToList();
        }

        /// <summary>Returns true if the monster has any compatible preset within threat cap.</summary>
        public static bool HasCompatiblePreset(string monsterId, int threatCap)
        {
            return GetCompatiblePresets(monsterId, threatCap).Count > 0;
        }

        /// <summary>Pick a random compatible preset for the monster, or null if none.</summary>
        public static AiSinPresetEntry? PickRandomPreset(
            string monsterId, int threatCap, Random rng, bool onlyBakeReady = true)
        {
            var presets = GetCompatiblePresets(monsterId, threatCap, onlyBakeReady);
            if (presets.Count == 0)
                return null;
            return presets[rng.Next(presets.Count)];
        }

        /// <summary>Register or update compatibility for a monster (for testing or future heuristic population).</summary>
        public static void Register(string monsterId, params string[] presetIds)
        {
            string key = monsterId.Trim().ToLowerInvariant();
            if (!key.StartsWith("m"))
                key = "m" + key;
            ManualMatrix[key] = presetIds;
        }

        /// <summary>Load compatibility from a CSV roster file (format: monster_id,allowed_presets).</summary>
        public static void LoadFromRoster(string csvPath)
        {
            if (!File.Exists(csvPath))
                return;

            foreach (string line in File.ReadAllLines(csvPath))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#'))
                    continue;
                if (t.StartsWith("monster_id", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = t.Split(',');
                if (parts.Length < 2)
                    continue;

                string monsterId = parts[0].Trim();
                string allowedPresets = parts.Length > 2 ? parts[2].Trim() : "";

                if (allowedPresets.Length > 0)
                {
                    Register(monsterId, allowedPresets.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }
        }

        /// <summary>Returns all monster IDs currently in the matrix.</summary>
        public static IReadOnlySet<string> KnownMonsters =>
            new HashSet<string>(ManualMatrix.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
