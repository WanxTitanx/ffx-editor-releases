using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal sealed class BattleCorpusBattleRow
    {
        public required string BattleId { get; init; }
        public required string FullPath { get; init; }
        public required Battle_File BattleFile { get; init; }
        public required IReadOnlyList<EncounterTable_Reference> EncounterReferences { get; init; }
        public string FormationLabel => BattleFile.FormationLabel;
        public string SearchBlob => $"{BattleId} {FormationLabel}";
        public string Summary => $"{(BattleFile.Formation?.Slots.Count ?? 0)} formation slots · {EncounterReferences.Count} encounter refs";
    }

    internal sealed class BattleCorpusCrosswalkRow
    {
        public required string ActorRow { get; init; }
        public required string FormationSlot { get; init; }
        public required string RawMonsterHex { get; init; }
        public required string MonsterLabel { get; init; }
        public required string CorpusOverlay { get; init; }
        public required string TrustLane { get; init; }
        public required string Note { get; init; }
    }

    internal sealed class BattleCorpusEncounterRow
    {
        public required string Map { get; init; }
        public required string Table { get; init; }
        public required string Group { get; init; }
        public required string Battlefield { get; init; }
        public required string Weight { get; init; }
    }

    /// <summary>A read-only row for the Encounter Opener / CTB seed card.</summary>
    internal sealed class BattleCorpusOpenerWriteRow
    {
        public required string TargetKind { get; init; }
        public required string FieldName { get; init; }
        public required string Operation { get; init; }
        public required string ValueText { get; init; }
        public required string SourceOffsetText { get; init; }
    }

    internal partial class BattleCorpusCrosswalkExplorer_DataModel : ObservableObject
    {
        readonly List<BattleCorpusBattleRow> allBattles = [];

        public ObservableCollection<BattleCorpusBattleRow> Battles { get; } = new();
        public ObservableCollection<BattleCorpusCrosswalkRow> CrosswalkRows { get; } = new();
        public ObservableCollection<BattleCorpusEncounterRow> EncounterRows { get; } = new();
        public ObservableCollection<BattleCorpusOpenerWriteRow> OpenerWriteRows { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedBattleTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedBattlePath))]
        [NotifyPropertyChangedFor(nameof(SelectedBattleSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedBattleWarnings))]
        [NotifyPropertyChangedFor(nameof(SelectedStrideSummary))]
        [NotifyPropertyChangedFor(nameof(OpenerSummary))]
        [NotifyPropertyChangedFor(nameof(OpenerGuardrail))]
        private BattleCorpusBattleRow? selectedBattle;

        [ObservableProperty]
        private string searchText = string.Empty;

        public string HeaderSummary => "Read-only crosswalk from battleId -> formation slots 0..7 -> actor rows 0..10 -> rawMonsterId -> corpus overlay. Rows 8..10 remain watchlist, not composition truth.";
        public string SourceSummary { get; private set; } = "Load a project to resolve battle/kernel and corpus roots.";
        public string OverviewSummary { get; private set; } = "-";
        public string SelectedBattleTitle => SelectedBattle?.BattleId ?? "Select a battle";
        public string SelectedBattlePath => SelectedBattle == null ? "-" : Path.GetRelativePath(Project_Service.Instance.ProjectPath!, SelectedBattle.FullPath);
        public string SelectedBattleSummary => SelectedBattle?.Summary ?? "-";
        public string SelectedBattleWarnings => "Composition truth stays in formation slots 0..7. Actor rows 8..10 remain watchlist only. No owner, target, or dispatch claims.";
        public string SelectedStrideSummary => $"Runtime enemy row stride: 0x{MemoryMap.SIZE_BATTLE_CHR_ENTRY:X} ({MemoryMap.SIZE_BATTLE_CHR_ENTRY} bytes).";

        // 🎬 Encounter opener / CTB seed (read-only, lazy per selected battle)
        public string OpenerSummary { get; private set; } = "Select a battle to inspect the encounter opener / CTB seed (chunk0 HookStart).";
        /// <summary>Formation-aware slot mapping (e.g. "slot0=m141, slot1=m124...") for the selected battle.</summary>
        public string OpenerFormationMapping { get; private set; } = "";
        public string OpenerGuardrail { get; private set; } = "";

        public BattleCorpusCrosswalkExplorer_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            allBattles.Clear();
            Battles.Clear();
            CrosswalkRows.Clear();
            EncounterRows.Clear();

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                SourceSummary = "Project not loaded.";
                OverviewSummary = "-";
                SelectedBattle = null;
                return;
            }

            SourceSummary = $"master={Project_Service.Instance.ProjectPath}{Environment.NewLine}corpus={MonsterAiCorpus_Service.StatusSummary}";
            Dictionary<string, List<EncounterTable_Reference>> lookup = LoadEncounterLookup();

            foreach (string battleDirectory in Directory.GetDirectories(Project_Service.Instance.Path_Btl).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string? battleId = Path.GetFileName(battleDirectory);
                if (string.IsNullOrWhiteSpace(battleId))
                    continue;

                string battlePath = Project_Service.Instance.GetPathBattle(battleId);
                if (!File.Exists(battlePath))
                    continue;

                try
                {
                    Battle_File battleFile = Battle_File.Read(battleId, File.ReadAllBytes(battlePath));
                    if (battleFile.Formation == null)
                        continue;

                    allBattles.Add(new BattleCorpusBattleRow
                    {
                        BattleId = battleId,
                        FullPath = battlePath,
                        BattleFile = battleFile,
                        EncounterReferences = lookup.TryGetValue(battleId, out List<EncounterTable_Reference>? refs)
                            ? refs.OrderBy(value => value.TableIndex).ThenBy(value => value.GroupIndex).ToList()
                            : []
                    });
                }
                catch
                {
                    // Skip parse failures here; this surface is meant to stay cold and honest.
                }
            }

            OverviewSummary = $"{allBattles.Count:N0} parsed battles · {allBattles.Sum(row => row.EncounterReferences.Count):N0} encounter references · corpus {(MonsterAiCorpus_Service.IsAvailable ? "online" : "offline")}";
            ApplyFilter();
        }

        partial void OnSelectedBattleChanged(BattleCorpusBattleRow? value)
        {
            CrosswalkRows.Clear();
            EncounterRows.Clear();
            OpenerWriteRows.Clear();

            if (value == null)
            {
                OpenerSummary = "Select a battle to inspect the encounter opener / CTB seed (chunk0 HookStart).";
                OpenerFormationMapping = "";
                OpenerGuardrail = "";
                return;
            }

            // ── Actor surface rows ──
            foreach (Battle_FormationSlot slot in value.BattleFile.Formation!.Slots)
            {
                MonsterAiCorpus_Service.MonsterAiRecord? record = slot.IsEmpty
                    ? null
                    : MonsterAiCorpus_Service.GetRecordByMonsterFileIndex(slot.DictionaryId);

                CrosswalkRows.Add(new BattleCorpusCrosswalkRow
                {
                    ActorRow = $"Row {slot.SlotIndex:00}",
                    FormationSlot = $"Slot {slot.SlotIndex:00}",
                    RawMonsterHex = slot.RawMonsterIdHex,
                    MonsterLabel = slot.MonsterLabel,
                    CorpusOverlay = record == null
                        ? (slot.IsEmpty ? "(Empty slot)" : (MonsterAiCorpus_Service.IsAvailable ? "No parser corpus match." : MonsterAiCorpus_Service.StatusSummary))
                        : $"{record.DisplayTitle} · {record.WorkerCount} workers · {record.ScriptLineCount} script lines · {record.AbilityReferences.Count} refs",
                    TrustLane = "proved composition -> actor-surface candidate",
                    Note = "Rows 0..7 are the only formation-backed actor rows."
                });
            }

            for (int row = 8; row <= 10; row++)
            {
                CrosswalkRows.Add(new BattleCorpusCrosswalkRow
                {
                    ActorRow = $"Row {row:00}",
                    FormationSlot = "-",
                    RawMonsterHex = "-",
                    MonsterLabel = "(Watchlist row)",
                    CorpusOverlay = "No composition-backed slot. Runtime-only watchlist if a live battle is inspected elsewhere.",
                    TrustLane = "watchlist only",
                    Note = "Do not promote rows 8..10 into composition truth."
                });
            }

            // ── Encounter references ──
            foreach (EncounterTable_Reference reference in value.EncounterReferences)
            {
                EncounterRows.Add(new BattleCorpusEncounterRow
                {
                    Map = reference.Map,
                    Table = $"Table {reference.TableIndex:000} / Id {reference.TableId:000}",
                    Group = $"Group {reference.GroupIndex:00} / Formation {reference.FormationId:00}",
                    Battlefield = $"Battlefield {reference.Battlefield} · Danger {reference.Danger}",
                    Weight = reference.TotalWeight > 0 ? $"{reference.Weight}/{reference.TotalWeight}" : reference.Weight.ToString()
                });
            }

            // ── Encounter opener / CTB seed (lazy per selected battle) ──
            PopulateOpenerData(value.BattleId, value.FullPath, value.BattleFile);
        }

        void PopulateOpenerData(string battleId, string fullPath, Battle_File battleFile)
        {
            OpenerWriteRows.Clear();

            try
            {
                if (!File.Exists(fullPath))
                {
                    OpenerSummary = $"Battle file not found: {fullPath}";
                    OpenerFormationMapping = "";
                    OpenerGuardrail = "";
                    return;
                }

                BattleEncounterOpener_File opener = BattleEncounterOpener_File.ReadFromBattleBin(battleId, File.ReadAllBytes(fullPath));

                if (opener.HasRecognizedSeed)
                {
                    OpenerSummary = opener.HumanSummary;

                    foreach (EncounterOpenerWriteRow w in opener.Writes)
                    {
                        OpenerWriteRows.Add(new BattleCorpusOpenerWriteRow
                        {
                            TargetKind = w.TargetDisplay,
                            FieldName = w.FieldName,
                            Operation = w.Operation,
                            ValueText = w.Value.ToString(),
                            SourceOffsetText = $"0x{w.SourceOffset:X}",
                        });
                    }

                    // Build formation mapping (slot -> monster id).
                    if (battleFile.Formation != null)
                    {
                        var slotParts = new List<string>();
                        foreach (Battle_FormationSlot slot in battleFile.Formation.Slots)
                        {
                            if (!slot.IsEmpty)
                                slotParts.Add($"slot{slot.SlotIndex}=m{slot.DictionaryId:D3}");
                        }
                        OpenerFormationMapping = "Formation: " + string.Join(", ", slotParts);
                    }
                    else
                    {
                        OpenerFormationMapping = "";
                    }

                    OpenerGuardrail = "read-only · encounter-driven proof (encounter-side HookStart, not monster-local CTB authoring). Writer still closed.";
                }
                else
                {
                    OpenerSummary = "No recognizable encounter opener / CTB seed in this battle's chunk0.";

                    if (opener.Notes.Count > 0)
                        OpenerSummary += " " + string.Join(" ", opener.Notes);

                    OpenerFormationMapping = "";
                    OpenerGuardrail = "read-only · unsupported pattern or no HookStart detected.";
                }
            }
            catch (Exception ex)
            {
                OpenerSummary = $"Failed to decode encounter opener: {ex.Message}";
                OpenerFormationMapping = "";
                OpenerGuardrail = "read-only · decode error.";
            }
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        void ApplyFilter()
        {
            IEnumerable<BattleCorpusBattleRow> query = allBattles;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(row => row.SearchBlob.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            Replace(Battles, query.OrderBy(row => row.BattleId, StringComparer.OrdinalIgnoreCase).ToList());
            SelectedBattle = Battles.FirstOrDefault();
        }

        Dictionary<string, List<EncounterTable_Reference>> LoadEncounterLookup()
        {
            if (!File.Exists(Project_Service.Instance.Path_KernelEncounterTable))
                return new Dictionary<string, List<EncounterTable_Reference>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                EncounterTable_File file = EncounterTable_File.Read(File.ReadAllBytes(Project_Service.Instance.Path_KernelEncounterTable));
                return file.BuildReferenceLookup();
            }
            catch
            {
                return new Dictionary<string, List<EncounterTable_Reference>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }
}
