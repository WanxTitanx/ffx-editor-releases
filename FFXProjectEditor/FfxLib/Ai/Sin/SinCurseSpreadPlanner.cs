using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // Offline Modo SIN spread planner — rolls WHICH mobs in an area get WHICH SIN preset.
    //
    // RUNTIME CONTRACT (Halyson 2026-06-16): the roll happens at **screen transition enter** —
    // leave map → enter map → engine reload window for monster AiFiles → THAT moment applies seed.
    // Same master seed + new transition index ⇒ different spread; fixed per visit until you leave again.
    //
    // Matches SIN_DIFFICULTY_MODE_SPEC §3–§4: subset RNG, eligible presets by threat cap, reproducible.
    // Does NOT write monster bins — emits a manifest for reload hook / sidecar / RT2 lab.
    public static class SinCurseSpreadPlanner
    {
        public const double DefaultInfectionRate = 0.30;
        public const string ReshuffleMoment = "screen_transition_enter";

        public sealed record SpreadOptions
        {
            public required string RegionId { get; init; }
            public required string RosterPath { get; init; }
            /// <summary>Save-level RNG root (`SpiraSinState.seed`). Combined with region + transition.</summary>
            public int MasterSeed { get; init; } = 42;
            /// <summary>How many times this region was entered since last reshuffle (0 = first enter).</summary>
            public int TransitionIndex { get; init; }
            /// <summary>If set, used directly instead of DeriveTransitionSeed (legacy / debug).</summary>
            public int? DerivedSeedOverride { get; init; }
            public double InfectionRate { get; init; } = DefaultInfectionRate;
            public int? AreaThreatCapOverride { get; init; }
            /// <summary>When true, spread draws only from universal.csv (not boss-presets).</summary>
            public bool UniversalPoolOnly { get; init; } = true;

            /// <summary>Legacy alias for UniversalPoolOnly.</summary>
            public bool ANowPresetsOnly { get => UniversalPoolOnly; init => UniversalPoolOnly = value; }
            public string? ThreatCsvPath { get; init; }
            public string? IsaruCsvPath { get; init; }

            public int EffectiveSeed => DerivedSeedOverride
                ?? DeriveTransitionSeed(MasterSeed, RegionId, TransitionIndex);
        }

        /// <summary>Deterministic per (master, region, transition). Runtime hook calls this on every map enter.</summary>
        public static int DeriveTransitionSeed(int masterSeed, string regionId, int transitionIndex)
        {
            unchecked
            {
                int hash = masterSeed;
                foreach (char c in regionId.Trim().ToLowerInvariant())
                    hash = (hash * 31) + c;
                hash = (hash * 31) + transitionIndex;
                return hash;
            }
        }

        public sealed record RosterSlot
        {
            public required string MonsterId { get; init; }
            public required string Role { get; init; }
            public string? Note { get; init; }
            /// <summary>When set, spread RNG draws only from this subset (area script allowlist).</summary>
            public IReadOnlyList<string>? AllowedPresets { get; init; }
        }

        public sealed record SlotAssignment
        {
            public required string MonsterId { get; init; }
            public string? MonsterName { get; init; }
            public required string Role { get; init; }
            public bool Infected { get; init; }
            public string? PresetId { get; init; }
            public string? PresetName { get; init; }
            public int? PresetThreat { get; init; }
            public string? SkipReason { get; init; }
        }

        public sealed record SpreadResult
        {
            public required string RegionId { get; init; }
            public required int AreaThreatCap { get; init; }
            public required int MasterSeed { get; init; }
            public required int TransitionIndex { get; init; }
            public required int DerivedSeed { get; init; }
            public required double InfectionRate { get; init; }
            public required IReadOnlyList<SlotAssignment> Slots { get; init; }
            public required SpreadSummary Summary { get; init; }

            /// <summary>Backward-compat alias for DerivedSeed.</summary>
            public int Seed => DerivedSeed;
        }

        public sealed record SpreadSummary
        {
            public int TotalSlots { get; init; }
            public int Infected { get; init; }
            public int Clean { get; init; }
            public int SkippedNoEligiblePreset { get; init; }
            public IReadOnlyDictionary<string, int> PresetCounts { get; init; } = new Dictionary<string, int>();
        }

        public static SpreadResult Plan(SpreadOptions options)
        {
            if (options.InfectionRate is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(options), "InfectionRate must be 0..1.");

            IReadOnlyList<RosterSlot> roster = LoadRoster(options.RosterPath);
            int areaCap = options.AreaThreatCapOverride
                ?? LoadAreaThreatCap(options.ThreatCsvPath ?? DefaultThreatCsv(), options.RegionId);
            IReadOnlyDictionary<string, IsaruRow> isaru = LoadIsaru(options.IsaruCsvPath ?? DefaultIsaruCsv());
            int derivedSeed = options.EffectiveSeed;
            var rng = new Random(derivedSeed);

            var slots = new List<SlotAssignment>();
            var presetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int skippedNoPreset = 0;

            foreach (RosterSlot entry in roster)
            {
                isaru.TryGetValue(entry.MonsterId, out IsaruRow? meta);
                string name = meta?.Name ?? entry.Note ?? entry.MonsterId;
                int mobCap = MobNaturalThreatCap(entry.Role, areaCap);

                if (mobCap <= 0)
                {
                    slots.Add(new SlotAssignment
                    {
                        MonsterId = entry.MonsterId,
                        MonsterName = name,
                        Role = entry.Role,
                        Infected = false,
                        SkipReason = "area cap 0 (tutorial / blocked)",
                    });
                    continue;
                }

                double slotRate = SlotInfectionRate(entry.Role, options.InfectionRate);
                if (rng.NextDouble() >= slotRate)
                {
                    slots.Add(new SlotAssignment
                    {
                        MonsterId = entry.MonsterId,
                        MonsterName = name,
                        Role = entry.Role,
                        Infected = false,
                        SkipReason = slotRate >= 1.0
                            ? "RNG clean (unexpected)"
                            : $"RNG clean (subset spread, slot_rate={slotRate:P0})",
                    });
                    continue;
                }

                if (TryResolveBossBinding(entry, mobCap, out AiSinPresetEntry? boundPreset, out string? bindSkip))
                {
                    if (boundPreset is null)
                    {
                        slots.Add(new SlotAssignment
                        {
                            MonsterId = entry.MonsterId,
                            MonsterName = name,
                            Role = entry.Role,
                            Infected = false,
                            SkipReason = bindSkip,
                        });
                        continue;
                    }

                    presetCounts[boundPreset.Id] = presetCounts.GetValueOrDefault(boundPreset.Id) + 1;
                    slots.Add(new SlotAssignment
                    {
                        MonsterId = entry.MonsterId,
                        MonsterName = name,
                        Role = entry.Role,
                        Infected = true,
                        PresetId = boundPreset.Id,
                        PresetName = boundPreset.Name,
                        PresetThreat = boundPreset.Threat,
                    });
                    continue;
                }

                List<AiSinPresetEntry> eligible = FilterEligiblePresets(
                    mobCap, options.UniversalPoolOnly, meta?.Archetype, entry.AllowedPresets);

                if (eligible.Count == 0)
                {
                    skippedNoPreset++;
                    string allowHint = entry.AllowedPresets is { Count: > 0 }
                        ? $" allowlist=[{string.Join('|', entry.AllowedPresets)}]"
                        : "";
                    slots.Add(new SlotAssignment
                    {
                        MonsterId = entry.MonsterId,
                        MonsterName = name,
                        Role = entry.Role,
                        Infected = false,
                        SkipReason = $"no eligible universal preset (cap T{mobCap}){allowHint}",
                    });
                    continue;
                }

                AiSinPresetEntry pick = eligible[rng.Next(eligible.Count)];
                presetCounts[pick.Id] = presetCounts.GetValueOrDefault(pick.Id) + 1;
                slots.Add(new SlotAssignment
                {
                    MonsterId = entry.MonsterId,
                    MonsterName = name,
                    Role = entry.Role,
                    Infected = true,
                    PresetId = pick.Id,
                    PresetName = pick.Name,
                    PresetThreat = pick.Threat,
                });
            }

            int infected = slots.Count(s => s.Infected);
            return new SpreadResult
            {
                RegionId = options.RegionId,
                AreaThreatCap = areaCap,
                MasterSeed = options.MasterSeed,
                TransitionIndex = options.TransitionIndex,
                DerivedSeed = derivedSeed,
                InfectionRate = options.InfectionRate,
                Slots = slots,
                Summary = new SpreadSummary
                {
                    TotalSlots = slots.Count,
                    Infected = infected,
                    Clean = slots.Count - infected,
                    SkippedNoEligiblePreset = skippedNoPreset,
                    PresetCounts = presetCounts,
                },
            };
        }

        public static string FormatHumanReport(SpreadResult result)
        {
            var lines = new List<string>
            {
                $"=== Sin Curse spread — {result.RegionId} ===",
                $"moment={ReshuffleMoment}  transition=#{result.TransitionIndex}",
                $"master_seed={result.MasterSeed}  derived_seed={result.DerivedSeed}  infection_rate={result.InfectionRate:P0}",
                $"area_threat_cap=T{result.AreaThreatCap}  slots={result.Summary.TotalSlots}  infected={result.Summary.Infected}  clean={result.Summary.Clean}",
                "",
            };

            if (result.Summary.PresetCounts.Count > 0)
            {
                lines.Add("preset histogram:");
                foreach (KeyValuePair<string, int> kv in result.Summary.PresetCounts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add($"  {kv.Key}: {kv.Value}");
                lines.Add("");
            }

            lines.Add("assignments:");
            foreach (SlotAssignment s in result.Slots)
            {
                if (s.Infected)
                    lines.Add($"  {s.MonsterId,-6} {s.MonsterName,-22} → {s.PresetId} · {s.PresetName} (T{s.PresetThreat})");
                else
                    lines.Add($"  {s.MonsterId,-6} {s.MonsterName,-22} — clean ({s.SkipReason})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static string ToSidecarJson(SpreadResult result)
        {
            var infectedIds = result.Slots
                .Where(s => s.Infected && s.PresetId != null)
                .Select(s => s.PresetId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new
            {
                format = "spira-sin-spread",
                format_version = 2,
                generated_utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                reshuffle_moment = ReshuffleMoment,
                reshuffle_note = "Roll runs when player ENTERS the region after a screen transition (AI reload window). Leave and re-enter ⇒ increment transition_index ⇒ new spread.",
                region_id = result.RegionId,
                area_threat_cap = result.AreaThreatCap,
                master_seed = result.MasterSeed,
                transition_index = result.TransitionIndex,
                derived_seed = result.DerivedSeed,
                infection_rate = result.InfectionRate,
                summary = new
                {
                    total_slots = result.Summary.TotalSlots,
                    infected = result.Summary.Infected,
                    clean = result.Summary.Clean,
                    skipped_no_eligible_preset = result.Summary.SkippedNoEligiblePreset,
                },
                per_area_presets = new Dictionary<string, IReadOnlyList<string>>
                {
                    [result.RegionId] = infectedIds,
                },
                assignments = result.Slots.Select(s => new
                {
                    monster_id = s.MonsterId,
                    monster_name = s.MonsterName,
                    role = s.Role,
                    infected = s.Infected,
                    preset_id = s.PresetId,
                    preset_name = s.PresetName,
                    preset_threat = s.PresetThreat,
                    skip_reason = s.SkipReason,
                }).ToList(),
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        static bool TryResolveBossBinding(
            RosterSlot entry,
            int mobCap,
            out AiSinPresetEntry? preset,
            out string? skipReason)
        {
            preset = null;
            skipReason = null;
            string role = entry.Role.Trim().ToLowerInvariant();
            if (role is not ("boss" or "story_boss" or "mini_boss"))
                return false;

            SinBossBindingCatalog.Binding? binding = SinBossBindingCatalog.FindForMonster(entry.MonsterId);
            if (binding is null)
            {
                if (role == "mini_boss")
                    return false;

                skipReason = "boss slot — no fixed preset in boss-bindings.csv yet";
                return true;
            }

            preset = AiSinPresetCatalog.Find(binding.PresetId);
            if (preset is null || preset.Scope != AiSinPresetScope.Boss)
            {
                skipReason = $"boss binding {binding.PresetId} missing from boss-presets.csv";
                return true;
            }

            if (preset.Threat > mobCap)
            {
                skipReason = $"boss preset {preset.Id} above cap T{mobCap}";
                preset = null;
                return true;
            }

            return true;
        }

        static List<AiSinPresetEntry> FilterEligiblePresets(
            int mobCap,
            bool universalOnly,
            string? archetype,
            IReadOnlyList<string>? allowedPresetIds)
        {
            IEnumerable<AiSinPresetEntry> q = universalOnly
                ? AiSinPresetCatalog.Universal
                : AiSinPresetCatalog.All.Where(p => p.IsUniversal);

            q = q.Where(p => p.Threat <= mobCap)
                .Where(p => !p.Maturity.Equals("design", StringComparison.OrdinalIgnoreCase));

            if (allowedPresetIds is { Count: > 0 })
            {
                var allow = new HashSet<string>(allowedPresetIds, StringComparer.OrdinalIgnoreCase);
                q = q.Where(p => allow.Contains(p.Id));
            }

            // Light archetype guard — expand later with full matrix from spec §3.2.
            if (!string.IsNullOrWhiteSpace(archetype))
            {
                if (archetype.Contains("element-caster", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(p => !p.Primitives.Contains("TARGET", StringComparison.OrdinalIgnoreCase)
                                     || p.Primitives.Contains("CMD", StringComparison.OrdinalIgnoreCase));
            }

            return q.OrderBy(p => p.NumericId).ToList();
        }

        static int MobNaturalThreatCap(string role, int areaCap)
        {
            string r = role.Trim().ToLowerInvariant();
            return r switch
            {
                "boss" or "story_boss" => Math.Min(areaCap + 2, 8),
                "elite" or "mini_boss" => Math.Min(areaCap + 1, 7),
                _ => areaCap,
            };
        }

        /// <summary>Scripted bosses should infect often — trash stays at base rate (spec §3.1).</summary>
        static double SlotInfectionRate(string role, double baseRate)
        {
            string r = role.Trim().ToLowerInvariant();
            return r switch
            {
                "boss" or "story_boss" => Math.Max(baseRate, 0.90),
                "mini_boss" => Math.Max(baseRate, 0.75),
                _ => baseRate,
            };
        }

        public static IReadOnlyList<RosterSlot> LoadRoster(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Roster CSV not found: {path}");

            var rows = new List<RosterSlot>();
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("monster_id", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = t.Split(',');
                if (parts.Length < 2)
                    continue;

                string id = NormalizeMonsterId(parts[0].Trim());
                string role = parts[1].Trim().ToLowerInvariant();
                string? note = parts.Length > 2 ? parts[2].Trim() : null;
                IReadOnlyList<string>? allowed = null;
                if (parts.Length > 3)
                {
                    string raw = parts[3].Trim();
                    if (raw.Length > 0)
                    {
                        allowed = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(s => s.Length > 0)
                            .ToList();
                    }
                }

                rows.Add(new RosterSlot { MonsterId = id, Role = role, Note = note, AllowedPresets = allowed });
            }

            if (rows.Count == 0)
                throw new InvalidDataException($"Roster empty: {path}");

            return rows;
        }

        static int LoadAreaThreatCap(string csvPath, string regionId)
        {
            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"Area threat CSV not found: {csvPath}");

            foreach (string line in File.ReadAllLines(csvPath))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("region_id", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = t.Split(',');
                if (parts.Length < 4)
                    continue;
                if (!parts[0].Trim().Equals(regionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap))
                    throw new InvalidDataException($"Invalid cap for {regionId} in {csvPath}");

                return cap;
            }

            throw new KeyNotFoundException($"region_id '{regionId}' not found in {csvPath}. Add a row or pass --cap.");
        }

        sealed record IsaruRow(string Name, string Archetype);

        static IReadOnlyDictionary<string, IsaruRow> LoadIsaru(string path)
        {
            var map = new Dictionary<string, IsaruRow>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return map;

            foreach (string line in File.ReadAllLines(path).Skip(1))
            {
                string[] parts = ParseCsvLine(line);
                if (parts.Length < 13)
                    continue;
                string id = NormalizeMonsterId(parts[0]);
                map[id] = new IsaruRow(parts[1], parts[12]);
            }

            return map;
        }

        static string[] ParseCsvLine(string line)
        {
            // ISARU index has no quoted commas in practice — split is enough for v1.
            return line.Split(',');
        }

        static string NormalizeMonsterId(string raw)
        {
            string s = raw.Trim().ToLowerInvariant();
            if (s.StartsWith("m", StringComparison.Ordinal))
                return s;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return $"m{n:D3}";
            return s;
        }

        public static string DefaultThreatCsv() =>
            Path.Combine(FindRepoRoot(), "mods", "Spira Reforge", "arena", "spira-sin-area-threat.csv");

        public static string DefaultIsaruCsv() =>
            Path.Combine(FindRepoRoot(), "docs", "ai", "ISARU_MONSTER_AI_PATTERN_INDEX_2026-06-10.csv");

        public static string DefaultRosterPath(string regionId) =>
            Path.Combine(FindRepoRoot(), "mods", "Spira Reforge", "arena", "spira-sin-area-rosters", $"{regionId}.csv");

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
