using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    public static class SinBossBindingCatalog
    {
        public sealed record Binding(string MonsterId, string PresetId, string? Note);

        static readonly Lazy<IReadOnlyDictionary<string, Binding>> _byMonster = new(Load);

        public static IReadOnlyDictionary<string, Binding> ByMonster => _byMonster.Value;

        public static Binding? FindForMonster(string monsterId)
        {
            string key = NormalizeMonsterId(monsterId);
            return ByMonster.TryGetValue(key, out Binding? b) ? b : null;
        }

        static IReadOnlyDictionary<string, Binding> Load()
        {
            string path = Path.Combine(AiSinPresetCatalog.DefaultCatalogRoot(), "boss-bindings.csv");
            var map = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return map;

            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("monster_id", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = t.Split(',');
                if (parts.Length < 2)
                    continue;

                string mob = NormalizeMonsterId(parts[0].Trim());
                string preset = parts[1].Trim();
                if (mob.Length == 0 || preset.Length == 0)
                    continue;

                string? note = parts.Length > 2 ? parts[2].Trim() : null;
                map[mob] = new Binding(mob, preset, note);
            }

            return map;
        }

        static string NormalizeMonsterId(string raw)
        {
            string s = raw.Trim().ToLowerInvariant();
            if (s.StartsWith('m'))
                return s;
            if (int.TryParse(s, out int n))
                return $"m{n:D3}";
            return s;
        }
    }
}
