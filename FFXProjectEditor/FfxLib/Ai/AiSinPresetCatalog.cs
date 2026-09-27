using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiSinPresetTier
    {
        A,
        B,
        C,
        Lab,
    }

    public enum AiSinPresetScope
    {
        Universal,
        Boss,
    }

    public sealed class AiSinPresetEntry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required AiSinPresetScope Scope { get; init; }
        public required AiSinPresetTier Tier { get; init; }
        /// <summary>bake-ready | spread-only | design — replaces v1 Status strings for v2 catalog.</summary>
        public required string Maturity { get; init; }
        public required string Primitives { get; init; }
        public required string Summary { get; init; }
        public string Preview { get; init; } = "";
        public int Threat { get; init; } = 1;
        public int CatalogOrder { get; init; }
        public string Risk { get; init; } = "";
        public string NextGate { get; init; } = "";
        /// <summary>v1 prototype id (e.g. SIN-006) for RT2/sandbox bridges.</summary>
        public string? LegacyProtoId { get; init; }

        /// <summary>Backward-compat for UI/filters that still read Status.</summary>
        public string Status => Maturity;

        public int NumericId
        {
            get
            {
                ReadOnlySpan<char> tail = Id.AsSpan();
                if (tail.StartsWith("UNI-", StringComparison.OrdinalIgnoreCase))
                    tail = tail[4..];
                else if (tail.StartsWith("BOSS-", StringComparison.OrdinalIgnoreCase))
                    tail = tail[5..];
                else if (tail.StartsWith("SIN-", StringComparison.OrdinalIgnoreCase))
                    tail = tail[4..];

                int numericLength = 0;
                while (numericLength < tail.Length && tail[numericLength] >= '0' && tail[numericLength] <= '9')
                    numericLength++;

                return numericLength > 0
                    && int.TryParse(tail[..numericLength], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    ? id
                    : int.MaxValue;
            }
        }

        public string CardTitle => $"{Id} · {Name}";
        public string TierLabel => Tier == AiSinPresetTier.Lab ? "LAB" : $"Tier {Tier}";
        public string ThreatLabel => $"Threat {Threat}/10";
        public string BadgeLabel => $"{ScopeLabel} · T{Threat}";
        public string ScopeLabel => Scope == AiSinPresetScope.Boss ? "Boss" : "Universal";

        public string StatusLabel => Maturity switch
        {
            "bake-ready" => Strings.U_Ai_SinStatusBakeReady,
            "spread-only" => Strings.U_Ai_SinStatusSpreadOnly,
            "design" => Strings.U_Ai_SinStatusDesign,
            _ => Maturity,
        };

        public string ThreatBandLabel => Threat switch
        {
            <= 2 => Strings.U_Ai_SinThreatLow,
            <= 4 => Strings.U_Ai_SinThreatMedium,
            <= 6 => Strings.U_Ai_SinThreatHigh,
            <= 8 => Strings.U_Ai_SinThreatDark,
            _ => Strings.U_Ai_SinThreatForbidden,
        };

        public string MaterializationLabel => Maturity switch
        {
            "bake-ready" => Strings.U_Ai_SinMaterializableOffline,
            "spread-only" => "planner/spread only",
            "design" => Strings.U_Ai_SinAuthoringPending,
            _ => "read-only",
        };

        public bool IsBakeReady => Maturity.Equals("bake-ready", StringComparison.OrdinalIgnoreCase);
        public bool IsUniversal => Scope == AiSinPresetScope.Universal;

        public string SearchBlob =>
            $"{Id} {Name} {LegacyProtoId} {ScopeLabel} {Maturity} {Primitives} {Summary} {Preview} T{Threat}";

        public string DisplayPreview => string.IsNullOrWhiteSpace(Preview) ? Summary : Preview;

        public string LegacyHint => string.IsNullOrWhiteSpace(LegacyProtoId) ? "" : $"piloto {LegacyProtoId}";

        public string ResolvePilotId() =>
            !string.IsNullOrWhiteSpace(LegacyProtoId) ? LegacyProtoId! : Id;
    }

    // v2 catalog: CSV under mods/Spira Reforge/arena/spira-sin-catalog/ — NOT the retired SIN-001..100 blob.
    public static class AiSinPresetCatalog
    {
        sealed class SearchHit
        {
            public required AiSinPresetEntry Entry { get; init; }
            public required int Score { get; init; }
        }

        static readonly Lazy<IReadOnlyList<AiSinPresetEntry>> _all = new(LoadAll);
        public static IReadOnlyList<AiSinPresetEntry> All => _all.Value;

        public static IReadOnlyList<AiSinPresetEntry> Universal =>
            All.Where(e => e.IsUniversal).ToList();

        public static IReadOnlyList<AiSinPresetEntry> BossPresets =>
            All.Where(e => e.Scope == AiSinPresetScope.Boss).ToList();

        public static AiSinPresetEntry? Find(string id) =>
            All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                || (e.LegacyProtoId != null && e.LegacyProtoId.Equals(id, StringComparison.OrdinalIgnoreCase)));

        public static IReadOnlyList<AiSinPresetEntry> Search(
            string? query,
            string? tierFilter,
            string? threatFilter = null,
            string? sortMode = null,
            int limit = 32)
            => SearchCore(All, query, tierFilter, threatFilter, sortMode, limit);

        // Editor-only design drafts can be browsed without adding them to the mod catalog or resolver.
        internal static IReadOnlyList<AiSinPresetEntry> SearchWithAdditional(
            IEnumerable<AiSinPresetEntry> additionalEntries,
            string? query,
            string? tierFilter,
            string? threatFilter = null,
            string? sortMode = null,
            int limit = 32)
        {
            ArgumentNullException.ThrowIfNull(additionalEntries);
            var catalogIds = All.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return SearchCore(
                All.Concat(additionalEntries.Where(entry => !catalogIds.Contains(entry.Id))),
                query, tierFilter, threatFilter, sortMode, limit);
        }

        static IReadOnlyList<AiSinPresetEntry> SearchCore(
            IEnumerable<AiSinPresetEntry> source,
            string? query,
            string? tierFilter,
            string? threatFilter,
            string? sortMode,
            int limit)
        {
            string q = (query ?? "").Trim();
            string filter = (tierFilter ?? "Todos").Trim();
            string threat = (threatFilter ?? "Qualquer T").Trim();
            string sort = (sortMode ?? "UNI-001 -> UNI-999").Trim();
            string[] terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var matches = source
                .Where(e => MatchesTier(e, filter))
                .Where(e => MatchesThreat(e, threat))
                .Select(e => new SearchHit { Entry = e, Score = Score(e, terms) })
                .Where(x => x.Score >= 0)
                .ToList();

            return SortMatches(matches, sort)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        static IReadOnlyList<AiSinPresetEntry> LoadAll()
        {
            string root = DefaultCatalogRoot();
            var rows = new List<AiSinPresetEntry>();
            rows.AddRange(LoadCsv(Path.Combine(root, "universal.csv"), AiSinPresetScope.Universal));
            rows.AddRange(LoadCsv(Path.Combine(root, "boss-presets.csv"), AiSinPresetScope.Boss));

            if (rows.Count == 0)
                throw new InvalidDataException($"SIN v2 catalog empty under {root}");

            return rows;
        }

        static IEnumerable<AiSinPresetEntry> LoadCsv(string path, AiSinPresetScope scope)
        {
            if (!File.Exists(path))
                yield break;

            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#'))
                    continue;
                if (t.StartsWith("id,", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith("monster_id,", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = t.Split(',');
                if (parts.Length < 6)
                    continue;

                string id = parts[0].Trim();
                if (id.Length == 0)
                    continue;

                string maturity = parts[2].Trim().ToLowerInvariant();
                if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int threat))
                    threat = 1;

                string? legacy = parts.Length > 6 ? NullIfEmpty(parts[6].Trim()) : null;

                yield return new AiSinPresetEntry
                {
                    Id = id,
                    Name = parts[1].Trim(),
                    Scope = scope,
                    Tier = TierFromThreat(threat),
                    Maturity = maturity,
                    Threat = Math.Clamp(threat, 1, 10),
                    Primitives = parts[4].Trim(),
                    Summary = parts[5].Trim(),
                    LegacyProtoId = legacy,
                    Preview = parts.Length > 7 ? parts[7].Trim() : "",
                };
            }
        }

        static AiSinPresetTier TierFromThreat(int threat) => threat switch
        {
            <= 2 => AiSinPresetTier.A,
            <= 4 => AiSinPresetTier.B,
            <= 6 => AiSinPresetTier.C,
            _ => AiSinPresetTier.Lab,
        };

        static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        static IOrderedEnumerable<SearchHit> SortMatches(IEnumerable<SearchHit> matches, string sortMode) => sortMode switch
        {
            "Threat 1 -> 10" => matches
                .OrderBy(x => x.Entry.Threat)
                .ThenBy(x => x.Entry.NumericId)
                .ThenBy(x => x.Entry.CatalogOrder)
                .ThenByDescending(x => x.Score),
            "Threat 10 -> 1" => matches
                .OrderByDescending(x => x.Entry.Threat)
                .ThenBy(x => x.Entry.NumericId)
                .ThenBy(x => x.Entry.CatalogOrder)
                .ThenByDescending(x => x.Score),
            "Busca: relevancia" => matches
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.NumericId)
                .ThenBy(x => x.Entry.CatalogOrder),
            "SIN-001 -> SIN-100" or "UNI-001 -> UNI-999" => matches
                .OrderBy(x => x.Entry.Scope)
                .ThenBy(x => x.Entry.NumericId)
                .ThenBy(x => x.Entry.CatalogOrder)
                .ThenBy(x => x.Entry.Id, StringComparer.OrdinalIgnoreCase),
            _ => matches
                .OrderBy(x => x.Entry.NumericId)
                .ThenBy(x => x.Entry.CatalogOrder)
                .ThenBy(x => x.Entry.Id, StringComparer.OrdinalIgnoreCase),
        };

        static bool MatchesTier(AiSinPresetEntry entry, string tierFilter) => tierFilter switch
        {
            "Tier A" => entry.Tier == AiSinPresetTier.A,
            "Tier B" => entry.Tier == AiSinPresetTier.B,
            "Tier C / LAB" => entry.Tier is AiSinPresetTier.C or AiSinPresetTier.Lab,
            "Universal" => entry.IsUniversal,
            "Boss" => entry.Scope == AiSinPresetScope.Boss,
            "Bake-ready" => entry.IsBakeReady,
            "A-now" => entry.IsBakeReady,
            _ => true,
        };

        static bool MatchesThreat(AiSinPresetEntry entry, string threatFilter) => threatFilter switch
        {
            "T1" => entry.Threat == 1,
            "T2" => entry.Threat == 2,
            "T3" => entry.Threat == 3,
            "T4" => entry.Threat == 4,
            "T5" => entry.Threat == 5,
            "T6" => entry.Threat == 6,
            "T7" => entry.Threat == 7,
            "T8" => entry.Threat == 8,
            "T9" => entry.Threat == 9,
            "T10" => entry.Threat == 10,
            "T1-T3" => entry.Threat is >= 1 and <= 3,
            "T4-T6" => entry.Threat is >= 4 and <= 6,
            "T7-T8" => entry.Threat is >= 7 and <= 8,
            "T9-T10" => entry.Threat is >= 9 and <= 10,
            _ => true,
        };

        static int Score(AiSinPresetEntry entry, string[] terms)
        {
            if (terms.Length == 0)
                return entry.IsBakeReady ? 20 : entry.Tier == AiSinPresetTier.A ? 12 : 0;

            int score = 0;
            foreach (string term in terms)
            {
                int termScore = ScoreTerm(entry, term);
                if (termScore < 0)
                    return -1;
                score += termScore;
            }

            return score;
        }

        static int ScoreTerm(AiSinPresetEntry entry, string term)
        {
            if (entry.Id.Equals(term, StringComparison.OrdinalIgnoreCase))
                return 160;
            if (entry.LegacyProtoId != null && entry.LegacyProtoId.Equals(term, StringComparison.OrdinalIgnoreCase))
                return 150;
            if (entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return 90;
            if (entry.Primitives.Contains(term, StringComparison.OrdinalIgnoreCase))
                return 60;
            if (entry.ThreatLabel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                $"T{entry.Threat}".Equals(term, StringComparison.OrdinalIgnoreCase))
                return 55;
            if (entry.Maturity.Contains(term, StringComparison.OrdinalIgnoreCase))
                return 50;
            if (entry.SearchBlob.Contains(term, StringComparison.OrdinalIgnoreCase))
                return 20;
            return -1;
        }

        public static string DefaultCatalogRoot()
        {
            static bool HasUniversalCsv(string root) =>
                File.Exists(Path.Combine(root, "universal.csv"));

            string repoRoot = Path.Combine(FindRepoRoot(), "mods", "Spira Reforge", "arena", "spira-sin-catalog");
            if (HasUniversalCsv(repoRoot))
                return repoRoot;

            string besideExe = Path.Combine(AppContext.BaseDirectory, "mods", "Spira Reforge", "arena", "spira-sin-catalog");
            if (HasUniversalCsv(besideExe))
                return besideExe;

            return repoRoot;
        }

        public static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PORT_STATUS.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
