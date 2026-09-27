using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Dictionaries
{
    /// <summary>
    /// Battle runtime model index catalog (Model1 / Model2 on the monster stat sheet).
    /// Sourced from the FFXmon community enemy editor <c>FFX models.txt</c> (Nexus mod 169).
    /// </summary>
    public static class BattleModel_Dictionary
    {
        public sealed class Entry
        {
            public required int Id { get; init; }
            public required string Hex { get; init; }
            public required string Name { get; init; }
            public int? MainModelId { get; init; }
            public string? MainModelName { get; init; }
            public IReadOnlyList<string> Flags { get; init; } = [];

            public bool IsVariant => MainModelId.HasValue;
            public bool CrashesGame => Flags.Contains("crash", StringComparer.OrdinalIgnoreCase);
            public bool IsInvisible => Flags.Contains("invisible", StringComparer.OrdinalIgnoreCase);
            public bool IsUnderwater => Flags.Contains("underwater", StringComparer.OrdinalIgnoreCase);
            public string Display => $"0x{Id:X4} — {Name}";
        }

        static readonly Dictionary<int, Entry> Instance;
        static readonly IReadOnlyList<Entry> Ordered;

        static BattleModel_Dictionary()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "FfxLib", "Dictionaries", "battle-model-catalog.json");
            if (!File.Exists(path))
            {
                Instance = new Dictionary<int, Entry>();
                Ordered = [];
                return;
            }

            using FileStream stream = File.OpenRead(path);
            CatalogRoot? root = JsonSerializer.Deserialize<CatalogRoot>(stream, JsonOptions);
            Dictionary<int, Entry> map = new();
            if (root?.Entries != null)
            {
                foreach (KeyValuePair<string, CatalogEntry> pair in root.Entries)
                {
                    if (!int.TryParse(pair.Key, out int id))
                        continue;

                    CatalogEntry raw = pair.Value;
                    map[id] = new Entry
                    {
                        Id = id,
                        Hex = raw.Hex ?? $"{id:X4}",
                        Name = raw.Name ?? $"Model {id:X4}",
                        MainModelId = raw.MainModelId,
                        MainModelName = raw.MainModelName,
                        Flags = raw.Flags ?? [],
                    };
                }
            }

            Instance = map;
            Ordered = map.Values.OrderBy(entry => entry.Id).ToList();
        }

        public static IReadOnlyList<Entry> All => Ordered;

        public static bool TryGet(int modelId, out Entry entry) => Instance.TryGetValue(modelId, out entry!);

        public static string ResolveName(int modelId)
        {
            return TryGet(modelId, out Entry? entry) ? entry.Name : $"Unknown (0x{modelId:X4})";
        }

        public static IEnumerable<Entry> Search(string? filter)
        {
            IEnumerable<Entry> query = Ordered;
            if (string.IsNullOrWhiteSpace(filter))
                return query;

            string needle = filter.Trim();
            return query.Where(entry =>
                entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                entry.Hex.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                entry.Id.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                $"0x{entry.Id:X4}".Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        sealed class CatalogRoot
        {
            [JsonPropertyName("entries")]
            public Dictionary<string, CatalogEntry>? Entries { get; set; }
        }

        sealed class CatalogEntry
        {
            [JsonPropertyName("hex")]
            public string? Hex { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("mainModelId")]
            public int? MainModelId { get; set; }

            [JsonPropertyName("mainModelName")]
            public string? MainModelName { get; set; }

            [JsonPropertyName("flags")]
            public List<string>? Flags { get; set; }
        }

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
