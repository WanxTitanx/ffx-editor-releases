using Avalonia.Media.Imaging;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Modules.BattleKernel.Commands
{
    internal static class KernelCommandReferenceIndex_Service
    {
        sealed class Snapshot
        {
            public required string MonsterRoot { get; init; }
            public required string StatusSummary { get; init; }
            public required IReadOnlyDictionary<ushort, IReadOnlyList<CommandReferenceRow>> ReferencesByGameIndex { get; init; }
        }

        public sealed class CommandReferenceRow
        {
            public required ushort RawGameIndex { get; init; }
            public required int MonsterIndex { get; init; }
            public required string MonsterName { get; init; }
            public required string Domain { get; init; }
            public required string Source { get; init; }

            public string MonsterLabel => $"[{MonsterIndex}] {MonsterName}";
            public string Summary => $"{Domain} · {Source}";
            public Bitmap? ItemIconImage => ItemIcon_Service.TryResolveBitmap(
                FfxCommon_Util.GetGameCategory(RawGameIndex),
                FfxCommon_Util.GetGameIndex(RawGameIndex),
                out Bitmap? bitmap)
                ? bitmap
                : null;
            public bool HasItemIcon => ItemIconImage != null;
            public string ItemIconTooltip => FfxCommon_Util.GetGameCategory(RawGameIndex) == (byte)GameCategory_Enum.Items
                ? ItemIcon_Service.BuildTooltip(FfxCommon_Util.GetGameIndex(RawGameIndex))
                : string.Empty;
        }

        static readonly Regex MonsterDirectoryRegex = new(@"^_m(?<index>\d+)$", RegexOptions.Compiled);
        static Snapshot? CachedSnapshot { get; set; }

        public static string StatusSummary => GetSnapshot().StatusSummary;

        public static IReadOnlyList<CommandReferenceRow> GetReferences(ushort rawGameIndex)
        {
            if (rawGameIndex == 0 || rawGameIndex == 0x00FF)
            {
                return Array.Empty<CommandReferenceRow>();
            }

            Snapshot snapshot = GetSnapshot();
            if (snapshot.ReferencesByGameIndex.TryGetValue(rawGameIndex, out IReadOnlyList<CommandReferenceRow>? references))
            {
                return references;
            }

            return Array.Empty<CommandReferenceRow>();
        }

        static Snapshot GetSnapshot()
        {
            string monsterRoot = Project_Service.Instance.Path_Mon;

            if (CachedSnapshot != null &&
                string.Equals(CachedSnapshot.MonsterRoot, monsterRoot, StringComparison.OrdinalIgnoreCase))
            {
                return CachedSnapshot;
            }

            CachedSnapshot = BuildSnapshot(monsterRoot);
            return CachedSnapshot;
        }

        static Snapshot BuildSnapshot(string monsterRoot)
        {
            if (!Project_Service.Instance.IsProjectLoaded || string.IsNullOrWhiteSpace(monsterRoot) || !Directory.Exists(monsterRoot))
            {
                return new Snapshot
                {
                    MonsterRoot = monsterRoot,
                    StatusSummary = "Where-used index offline because the monster workspace is not loaded.",
                    ReferencesByGameIndex = new Dictionary<ushort, IReadOnlyList<CommandReferenceRow>>()
                };
            }

            Dictionary<ushort, List<CommandReferenceRow>> referencesByGameIndex = new();

            foreach (string monsterDirectory in Directory.GetDirectories(monsterRoot).OrderBy(path => path))
            {
                string? directoryName = Path.GetFileName(monsterDirectory);
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    continue;
                }

                Match match = MonsterDirectoryRegex.Match(directoryName);
                if (!match.Success)
                {
                    continue;
                }

                int monsterIndex = int.Parse(match.Groups["index"].Value);
                string monsterName = Monster_Dictionary.Instance.TryGetValue((short)monsterIndex, out string? knownName)
                    ? knownName
                    : "<NOT INDEXED>";

                string monsterPath = Project_Service.Instance.GetPathMon(monsterIndex);
                if (!File.Exists(monsterPath))
                {
                    continue;
                }

                try
                {
                    Monster_File monsterFile = Monster_File.Read(File.ReadAllBytes(monsterPath));

                    if (monsterFile.StatSheetFile != null)
                    {
                        AddReference(referencesByGameIndex, monsterFile.StatSheetFile.ForcedAction, monsterIndex, monsterName, "Monster Stat Sheet", "Forced Ability");

                        for (int i = 0; i < monsterFile.StatSheetFile.Abilities.Length; i++)
                        {
                            AddReference(referencesByGameIndex, monsterFile.StatSheetFile.Abilities[i], monsterIndex, monsterName, "Monster Stat Sheet", $"Ability {i + 1}");
                        }
                    }

                    if (monsterFile.LootFile != null)
                    {
                        AddReference(referencesByGameIndex, monsterFile.LootFile.Drop1Id, monsterIndex, monsterName, "Monster Loot", "Drop 1");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.Drop1RareId, monsterIndex, monsterName, "Monster Loot", "Drop 1 Rare");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.Drop2Id, monsterIndex, monsterName, "Monster Loot", "Drop 2");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.Drop2RareId, monsterIndex, monsterName, "Monster Loot", "Drop 2 Rare");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.DropOverkillId, monsterIndex, monsterName, "Monster Loot", "Drop Overkill 1");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.DropOverkillRareId, monsterIndex, monsterName, "Monster Loot", "Drop Overkill 1 Rare");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.DropOverkill2Id, monsterIndex, monsterName, "Monster Loot", "Drop Overkill 2");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.DropOverkill2RareId, monsterIndex, monsterName, "Monster Loot", "Drop Overkill 2 Rare");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.StealId, monsterIndex, monsterName, "Monster Loot", "Steal");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.StealRareId, monsterIndex, monsterName, "Monster Loot", "Steal Rare");
                        AddReference(referencesByGameIndex, monsterFile.LootFile.BribeId, monsterIndex, monsterName, "Monster Loot", "Bribe");
                    }

                    foreach (MonsterAiAbilityCorpus_Service.MonsterAiAbilityReference reference in MonsterAiAbilityCorpus_Service.GetAbilityReferences((short)monsterIndex))
                    {
                        AddReference(referencesByGameIndex, reference.RawGameIndex, monsterIndex, monsterName, "Monster AI", reference.Source);
                    }
                }
                catch
                {
                    // Skip monsters that fail to parse; the reference index should stay resilient.
                }
            }

            Dictionary<ushort, IReadOnlyList<CommandReferenceRow>> normalized = referencesByGameIndex
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<CommandReferenceRow>)pair.Value
                        .GroupBy(reference => $"{reference.MonsterIndex}|{reference.Domain}|{reference.Source}|{reference.RawGameIndex}")
                        .Select(group => group.First())
                        .OrderBy(reference => reference.MonsterIndex)
                        .ThenBy(reference => reference.Domain)
                        .ThenBy(reference => reference.Source)
                        .ToList());

            int monsterHits = normalized
                .SelectMany(pair => pair.Value)
                .Select(reference => reference.MonsterIndex)
                .Distinct()
                .Count();

            int referenceCount = normalized.Sum(pair => pair.Value.Count);

            return new Snapshot
            {
                MonsterRoot = monsterRoot,
                StatusSummary = $"Where-used index scanned monster stat sheets, loot, and AI corpus ({monsterHits} monsters / {referenceCount} references).",
                ReferencesByGameIndex = normalized
            };
        }

        static void AddReference(
            IDictionary<ushort, List<CommandReferenceRow>> referencesByGameIndex,
            ushort rawGameIndex,
            int monsterIndex,
            string monsterName,
            string domain,
            string source)
        {
            byte category = FfxCommon_Util.GetGameCategory(rawGameIndex);
            ushort index = FfxCommon_Util.GetGameIndex(rawGameIndex);

            if (category == (byte)GameCategory_Enum.None && (index == 0 || index == 255))
            {
                return;
            }

            if (!referencesByGameIndex.TryGetValue(rawGameIndex, out List<CommandReferenceRow>? bucket))
            {
                bucket = new List<CommandReferenceRow>();
                referencesByGameIndex.Add(rawGameIndex, bucket);
            }

            bucket.Add(new CommandReferenceRow
            {
                RawGameIndex = rawGameIndex,
                MonsterIndex = monsterIndex,
                MonsterName = monsterName,
                Domain = domain,
                Source = source
            });
        }
    }
}
