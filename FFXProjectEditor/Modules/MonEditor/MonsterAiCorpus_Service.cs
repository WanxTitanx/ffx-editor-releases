using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal static class MonsterAiCorpus_Service
    {
        sealed class CorpusSnapshot
        {
            public required bool IsAvailable { get; init; }
            public required string StatusSummary { get; init; }
            public required IReadOnlyList<MonsterAiRecord> OrderedRecords { get; init; }
            public required IReadOnlyDictionary<int, MonsterAiRecord> RecordsByMonsterFileIndex { get; init; }
            public required IReadOnlyDictionary<int, IReadOnlyList<MonsterAiAbilityReference>> ReferencesByMonsterFileIndex { get; init; }
        }

        sealed class MonsterAiRecordBuilder
        {
            public required int MonsterFileIndex { get; init; }
            public required string RelativePath { get; init; }
            public string HeaderMonsterName { get; set; } = string.Empty;
            public ushort RawMonsterId { get; set; }
            public string AbilityListSummary { get; set; } = "-";
            public string ForcedActionSummary { get; set; } = "-";
            public string LocalizedName { get; set; } = string.Empty;
            public int WorkerCount { get; set; }
            public int VariableCount { get; set; }
            public List<MonsterAiAbilityReference> References { get; } = new();
            public Dictionary<string, List<string>> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> SectionOrder { get; } = new();

            public void EnsureSection(string sectionName)
            {
                if (Sections.ContainsKey(sectionName))
                {
                    return;
                }

                Sections.Add(sectionName, []);
                SectionOrder.Add(sectionName);
            }

            public void AppendSectionLine(string sectionName, string line)
            {
                EnsureSection(sectionName);
                Sections[sectionName].Add(line);
            }

            public string GetSectionText(string sectionName)
            {
                if (!Sections.TryGetValue(sectionName, out List<string>? lines))
                {
                    return string.Empty;
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        public sealed class MonsterAiAbilityReference
        {
            public required ushort RawGameIndex { get; init; }
            public required string Source { get; init; }
            public required string Label { get; init; }
            public required string ResolvedName { get; init; }

            public string RawHex => $"{RawGameIndex:X4}h";
            public string DisplayLabel => $"{ResolvedName} [{RawHex}]";
            public string Summary => $"{Source} · {DisplayLabel}";
        }

        public sealed class MonsterAiRecord
        {
            public required int MonsterFileIndex { get; init; }
            public required ushort RawMonsterId { get; init; }
            public required string RelativePath { get; init; }
            public required string HeaderMonsterName { get; init; }
            public required string LocalizedName { get; init; }
            public required string SensorText { get; init; }
            public required string ScanText { get; init; }
            public required string ScriptCode { get; init; }
            public required string ScriptWorkers { get; init; }
            public required string MonsterStats { get; init; }
            public required string MonsterLoot { get; init; }
            public required string AbilityListSummary { get; init; }
            public required string ForcedActionSummary { get; init; }
            public required string CommandReferenceSummary { get; init; }
            public required string OverviewSummary { get; init; }
            public required string SearchBlob { get; init; }
            public required IReadOnlyList<string> SectionOrder { get; init; }
            public required IReadOnlyDictionary<string, string> SectionTexts { get; init; }
            public required IReadOnlyList<MonsterAiAbilityReference> AbilityReferences { get; init; }
            public required string ShortScriptPreview { get; init; }
            public required int ScriptLineCount { get; init; }
            public required int WorkerCount { get; init; }
            public required int VariableCount { get; init; }

            public string DisplayTitle => $"m{MonsterFileIndex:D3} · {DisplayName}";
            public string DisplayName => !string.IsNullOrWhiteSpace(HeaderMonsterName)
                ? HeaderMonsterName
                : ResolveMonsterDictionaryName(MonsterFileIndex);
            public string IndexLabel => $"m{MonsterFileIndex:D3}";
            public string RawMonsterHex => $"{RawMonsterId:X4}h";
            public string LocalizedSummary => $"{(string.IsNullOrWhiteSpace(LocalizedName) ? DisplayName : LocalizedName)} · {RawMonsterHex}";
        }

        static readonly object SnapshotGate = new();
        static CorpusSnapshot? snapshot;

        static readonly Regex BlockHeaderRegex = new(@"^--- (?<path>.+_m(?<index>\d{3})/m\d{3}\.bin) ---$", RegexOptions.Compiled);
        static readonly Regex MonsterHeaderRegex = new(@"^(?<name>.+?) \[(?<id>[0-9A-Fa-f]{4})h\]$", RegexOptions.Compiled);
        static readonly Regex SectionHeaderRegex = new(@"^- (?<section>.+?) -$", RegexOptions.Compiled);
        static readonly Regex PerformCommandRegex = new(@"command=""(?<label>.*?)""\s+\[(?<id>[0-9A-Fa-f]{4})h\]", RegexOptions.Compiled);
        static readonly Regex ActionEntryRegex = new(@"(?<label>[^,\[]+?)\s+\[(?<id>[0-9A-Fa-f]{4})h\]", RegexOptions.Compiled);
        static readonly Regex WorkerCountRegex = new(@"^(?<count>\d+) Workers Total$", RegexOptions.Compiled);
        static readonly Regex VariableCountRegex = new(@"^Variables \((?<count>\d+) at offset .+\)$", RegexOptions.Compiled);

        public static bool IsAvailable => GetSnapshot().IsAvailable;
        public static string StatusSummary => GetSnapshot().StatusSummary;

        public static IReadOnlyList<MonsterAiRecord> GetAllRecords() => GetSnapshot().OrderedRecords;

        public static MonsterAiRecord? GetRecord(short rawMonsterId)
        {
            int monsterFileIndex = ((ushort)rawMonsterId) & 0x0FFF;
            return GetRecordByMonsterFileIndex(monsterFileIndex);
        }

        public static MonsterAiRecord? GetRecordByMonsterFileIndex(int monsterFileIndex)
        {
            CorpusSnapshot localSnapshot = GetSnapshot();
            return localSnapshot.RecordsByMonsterFileIndex.TryGetValue(monsterFileIndex, out MonsterAiRecord? record)
                ? record
                : null;
        }

        public static IReadOnlyList<MonsterAiAbilityReference> GetAbilityReferences(short rawMonsterId)
        {
            int monsterFileIndex = ((ushort)rawMonsterId) & 0x0FFF;
            CorpusSnapshot localSnapshot = GetSnapshot();

            if (localSnapshot.ReferencesByMonsterFileIndex.TryGetValue(monsterFileIndex, out IReadOnlyList<MonsterAiAbilityReference>? references))
            {
                return references;
            }

            return Array.Empty<MonsterAiAbilityReference>();
        }

        public static void Reload()
        {
            lock (SnapshotGate)
            {
                snapshot = null;
            }
        }

        static CorpusSnapshot GetSnapshot()
        {
            lock (SnapshotGate)
            {
                snapshot ??= LoadSnapshot();
                return snapshot;
            }
        }

        static CorpusSnapshot LoadSnapshot()
        {
            string? path = ResolveCorpusPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new CorpusSnapshot
                {
                    IsAvailable = false,
                    StatusSummary = "AI parser corpus not found. Place monsterAiOutput.txt in Downloads to unlock the monster AI surface.",
                    OrderedRecords = Array.Empty<MonsterAiRecord>(),
                    RecordsByMonsterFileIndex = new Dictionary<int, MonsterAiRecord>(),
                    ReferencesByMonsterFileIndex = new Dictionary<int, IReadOnlyList<MonsterAiAbilityReference>>()
                };
            }

            try
            {
                List<MonsterAiRecordBuilder> builders = new();
                MonsterAiRecordBuilder? current = null;
                string? currentSection = null;

                foreach (string line in File.ReadLines(path))
                {
                    Match blockMatch = BlockHeaderRegex.Match(line);
                    if (blockMatch.Success)
                    {
                        if (current != null)
                        {
                            builders.Add(current);
                        }

                        current = new MonsterAiRecordBuilder
                        {
                            MonsterFileIndex = int.Parse(blockMatch.Groups["index"].Value),
                            RelativePath = blockMatch.Groups["path"].Value
                        };
                        currentSection = null;
                        continue;
                    }

                    if (current == null)
                    {
                        continue;
                    }

                    if (current.RawMonsterId == 0)
                    {
                        Match headerMatch = MonsterHeaderRegex.Match(line);
                        if (headerMatch.Success)
                        {
                            current.HeaderMonsterName = headerMatch.Groups["name"].Value.Trim();
                            current.RawMonsterId = Convert.ToUInt16(headerMatch.Groups["id"].Value, 16);
                            continue;
                        }
                    }

                    Match sectionMatch = SectionHeaderRegex.Match(line);
                    if (sectionMatch.Success)
                    {
                        currentSection = sectionMatch.Groups["section"].Value.Trim();
                        current.EnsureSection(currentSection);
                        continue;
                    }

                    if (currentSection != null)
                    {
                        current.AppendSectionLine(currentSection, line);
                    }

                    TryCaptureScriptReference(current.References, line);
                    TryCaptureAbilityList(current, line);
                    TryCaptureForcedAction(current, line);
                    TryCaptureLocalizedName(current, currentSection, line);
                    TryCaptureWorkerCount(current, line);
                    TryCaptureVariableCount(current, line);
                }

                if (current != null)
                {
                    builders.Add(current);
                }

                List<MonsterAiRecord> records = builders
                    .Select(BuildRecord)
                    .OrderBy(record => record.MonsterFileIndex)
                    .ToList();

                Dictionary<int, MonsterAiRecord> recordsByMonsterFileIndex = records.ToDictionary(record => record.MonsterFileIndex);
                Dictionary<int, IReadOnlyList<MonsterAiAbilityReference>> referencesByMonsterFileIndex = records.ToDictionary(
                    record => record.MonsterFileIndex,
                    record => record.AbilityReferences);

                int monsterCount = records.Count;
                int referenceCount = records.Sum(record => record.AbilityReferences.Count);

                return new CorpusSnapshot
                {
                    IsAvailable = true,
                    StatusSummary = $"Monster AI corpus loaded from {path} ({monsterCount} monsters · {referenceCount} unique command refs).",
                    OrderedRecords = records,
                    RecordsByMonsterFileIndex = recordsByMonsterFileIndex,
                    ReferencesByMonsterFileIndex = referencesByMonsterFileIndex
                };
            }
            catch (Exception ex)
            {
                return new CorpusSnapshot
                {
                    IsAvailable = false,
                    StatusSummary = $"Failed to parse monsterAiOutput.txt: {ex.Message}",
                    OrderedRecords = Array.Empty<MonsterAiRecord>(),
                    RecordsByMonsterFileIndex = new Dictionary<int, MonsterAiRecord>(),
                    ReferencesByMonsterFileIndex = new Dictionary<int, IReadOnlyList<MonsterAiAbilityReference>>()
                };
            }
        }

        static MonsterAiRecord BuildRecord(MonsterAiRecordBuilder builder)
        {
            Dictionary<string, string> sectionTexts = builder.Sections
                .ToDictionary(pair => pair.Key, pair => string.Join(Environment.NewLine, pair.Value), StringComparer.OrdinalIgnoreCase);

            string scriptCode = GetSectionText(sectionTexts, "Script Code");
            string scriptWorkers = GetSectionText(sectionTexts, "Script Workers");
            string monsterStats = GetSectionText(sectionTexts, "Monster Stats");
            string monsterLoot = GetSectionText(sectionTexts, "Monster Loot");
            string sensorText = GetSectionText(sectionTexts, "Sensor Text");
            string scanText = GetSectionText(sectionTexts, "Scan Text");
            string localizedName = builder.LocalizedName.Trim();
            if (string.IsNullOrWhiteSpace(localizedName))
            {
                localizedName = builder.HeaderMonsterName;
            }

            List<MonsterAiAbilityReference> normalizedReferences = builder.References
                .GroupBy(reference => $"{reference.RawGameIndex:X4}|{reference.Source}|{reference.ResolvedName}")
                .Select(group => group.First())
                .OrderBy(reference => reference.RawGameIndex)
                .ThenBy(reference => reference.Source, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int scriptLineCount = CountMeaningfulLines(scriptCode);
            string commandReferenceSummary = normalizedReferences.Count == 0
                ? "No command refs parsed from the AI corpus."
                : string.Join(" · ", normalizedReferences
                    .GroupBy(reference => reference.Source)
                    .Select(group => $"{group.Key}: {group.Count()}"));

            string overviewSummary = $"{scriptLineCount} script lines · {builder.WorkerCount} workers · {builder.VariableCount} vars · {normalizedReferences.Count} command refs";

            return new MonsterAiRecord
            {
                MonsterFileIndex = builder.MonsterFileIndex,
                RawMonsterId = builder.RawMonsterId,
                RelativePath = builder.RelativePath,
                HeaderMonsterName = builder.HeaderMonsterName,
                LocalizedName = localizedName,
                SensorText = sensorText,
                ScanText = scanText,
                ScriptCode = scriptCode,
                ScriptWorkers = scriptWorkers,
                MonsterStats = monsterStats,
                MonsterLoot = monsterLoot,
                AbilityListSummary = builder.AbilityListSummary,
                ForcedActionSummary = builder.ForcedActionSummary,
                CommandReferenceSummary = commandReferenceSummary,
                OverviewSummary = overviewSummary,
                SearchBlob = BuildSearchBlob(builder, localizedName, scriptCode, scriptWorkers, monsterStats, monsterLoot, sensorText, scanText, normalizedReferences),
                SectionOrder = builder.SectionOrder.ToList(),
                SectionTexts = sectionTexts,
                AbilityReferences = normalizedReferences,
                ShortScriptPreview = BuildShortScriptPreview(scriptCode),
                ScriptLineCount = scriptLineCount,
                WorkerCount = builder.WorkerCount,
                VariableCount = builder.VariableCount
            };
        }

        static string BuildSearchBlob(
            MonsterAiRecordBuilder builder,
            string localizedName,
            string scriptCode,
            string scriptWorkers,
            string monsterStats,
            string monsterLoot,
            string sensorText,
            string scanText,
            IReadOnlyList<MonsterAiAbilityReference> references)
        {
            return string.Join(" ",
                builder.MonsterFileIndex.ToString(),
                builder.MonsterFileIndex.ToString("D3"),
                builder.HeaderMonsterName,
                localizedName,
                builder.RelativePath,
                builder.AbilityListSummary,
                builder.ForcedActionSummary,
                scriptCode,
                scriptWorkers,
                monsterStats,
                monsterLoot,
                sensorText,
                scanText,
                string.Join(" ", references.Select(reference => reference.ResolvedName)));
        }

        static string GetSectionText(IReadOnlyDictionary<string, string> sectionTexts, string sectionName)
        {
            return sectionTexts.TryGetValue(sectionName, out string? value) ? value.Trim() : string.Empty;
        }

        static int CountMeaningfulLines(string text)
        {
            return text.Split([Environment.NewLine], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }

        static string BuildShortScriptPreview(string scriptCode)
        {
            if (string.IsNullOrWhiteSpace(scriptCode))
            {
                return "No script preview available.";
            }

            string[] lines = scriptCode.Split([Environment.NewLine], StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(6)
                .ToArray();

            return string.Join(Environment.NewLine, lines);
        }

        static void TryCaptureScriptReference(ICollection<MonsterAiAbilityReference> target, string line)
        {
            Match match = PerformCommandRegex.Match(line);
            if (!match.Success)
            {
                return;
            }

            AddReference(target, Convert.ToUInt16(match.Groups["id"].Value, 16), "AI Script", match.Groups["label"].Value);
        }

        static void TryCaptureAbilityList(MonsterAiRecordBuilder builder, string line)
        {
            if (!line.StartsWith("Ability List:", StringComparison.Ordinal))
            {
                return;
            }

            builder.AbilityListSummary = line["Ability List:".Length..].Trim();
            foreach (Match match in ActionEntryRegex.Matches(builder.AbilityListSummary))
            {
                AddReference(builder.References, Convert.ToUInt16(match.Groups["id"].Value, 16), "AI Ability List", match.Groups["label"].Value);
            }
        }

        static void TryCaptureForcedAction(MonsterAiRecordBuilder builder, string line)
        {
            if (!line.StartsWith("Forced Action:", StringComparison.Ordinal))
            {
                return;
            }

            builder.ForcedActionSummary = line["Forced Action:".Length..].Trim();
            foreach (Match match in ActionEntryRegex.Matches(builder.ForcedActionSummary))
            {
                AddReference(builder.References, Convert.ToUInt16(match.Groups["id"].Value, 16), "AI Forced Action", match.Groups["label"].Value);
            }
        }

        static void TryCaptureLocalizedName(MonsterAiRecordBuilder builder, string? currentSection, string line)
        {
            if (!string.Equals(currentSection, "Localized Strings", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!line.StartsWith("Name:", StringComparison.Ordinal))
            {
                return;
            }

            builder.LocalizedName = line["Name:".Length..].Trim();
        }

        static void TryCaptureWorkerCount(MonsterAiRecordBuilder builder, string line)
        {
            Match match = WorkerCountRegex.Match(line);
            if (!match.Success)
            {
                return;
            }

            builder.WorkerCount = int.Parse(match.Groups["count"].Value);
        }

        static void TryCaptureVariableCount(MonsterAiRecordBuilder builder, string line)
        {
            Match match = VariableCountRegex.Match(line);
            if (!match.Success)
            {
                return;
            }

            builder.VariableCount = int.Parse(match.Groups["count"].Value);
        }

        static void AddReference(ICollection<MonsterAiAbilityReference> target, ushort rawGameIndex, string source, string label)
        {
            string normalizedLabel = NormalizeLabel(label);
            string resolvedName = ResolveGameIndexName(rawGameIndex, normalizedLabel);

            target.Add(new MonsterAiAbilityReference
            {
                RawGameIndex = rawGameIndex,
                Source = source,
                Label = normalizedLabel,
                ResolvedName = resolvedName
            });
        }

        static string ResolveGameIndexName(ushort rawGameIndex, string fallbackLabel)
        {
            byte category = FfxCommon_Util.GetGameCategory(rawGameIndex);
            ushort index = FfxCommon_Util.GetGameIndex(rawGameIndex);

            try
            {
                string resolved = FfxCommon_Util.GetGameIndexName(category, index);
                if (!string.IsNullOrWhiteSpace(resolved) && !resolved.Equals("<NOT_INDEXED>", StringComparison.OrdinalIgnoreCase))
                {
                    return resolved;
                }
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(fallbackLabel)
                ? $"{rawGameIndex:X4}h"
                : fallbackLabel;
        }

        static string NormalizeLabel(string label)
        {
            string normalized = label.Trim().Trim('"');
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal) && normalized.Length > 2)
            {
                normalized = normalized[1..^1];
            }

            return normalized.Trim();
        }

        static string ResolveMonsterDictionaryName(int monsterFileIndex)
        {
            return Monster_Dictionary.Instance.TryGetValue((short)monsterFileIndex, out string? name)
                ? name
                : $"Monster {monsterFileIndex:D3}";
        }

        static string? ResolveCorpusPath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidatePaths =
            [
                Path.Combine(userProfile, "Downloads", "monsterAiOutput.txt"),
                Path.Combine(userProfile, "Downloads", "monsterAiOutput (1).txt"),
                Path.Combine(AppContext.BaseDirectory, "monsterAiOutput.txt"),
                Path.Combine(Directory.GetCurrentDirectory(), "monsterAiOutput.txt")
            ];

            return candidatePaths.FirstOrDefault(File.Exists);
        }
    }
}
