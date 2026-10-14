using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Modules.MonEditor;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiExplorer
{
    internal partial class MonsterAiExplorer_DataModel : ObservableObject
    {
        public ObservableCollection<MonsterAiCorpusRow> LoadedMonsters { get; } = new();
        public ObservableCollection<MonsterAiCorpusRow> DisplayedMonsters { get; } = new();
        public ObservableCollection<MonsterAiReferenceRow> SelectedReferences { get; } = new();

        [ObservableProperty] private MonsterAiCorpusRow? selectedMonster;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load the AI corpus to inspect monster scripts, workers, and command references.";
        [ObservableProperty] private string selectedMonsterSummary = "Select a monster AI block to inspect script, workers, forced action, and command footprint.";
        [ObservableProperty] private string selectedMonsterScope = "Read-heavy scope: parser-corpus decompilation and command routing only. No ATEL recompilation or blind script writing yet.";
        [ObservableProperty] private string selectedOverviewSummary = "-";
        [ObservableProperty] private string selectedReferenceSummary = "-";
        [ObservableProperty] private string selectedForcedActionSummary = "-";
        [ObservableProperty] private string selectedAbilityListSummary = "-";
        [ObservableProperty] private string selectedLocalizedSummary = "-";
        [ObservableProperty] private string selectedScriptSummary = "-";
        [ObservableProperty] private string selectedScriptCode = string.Empty;
        [ObservableProperty] private string selectedWorkersAndVariables = string.Empty;
        [ObservableProperty] private string selectedStatsText = string.Empty;
        [ObservableProperty] private string selectedLootText = string.Empty;
        [ObservableProperty] private string selectedSensorText = string.Empty;
        [ObservableProperty] private string selectedScanText = string.Empty;

        public MonsterAiExplorer_DataModel()
        {
            ReloadCorpus();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedMonsterChanged(MonsterAiCorpusRow? value)
        {
            SelectedReferences.Clear();

            if (value == null)
            {
                SelectedMonsterSummary = "Select a monster AI block to inspect script, workers, forced action, and command footprint.";
                SelectedOverviewSummary = "-";
                SelectedReferenceSummary = "-";
                SelectedForcedActionSummary = "-";
                SelectedAbilityListSummary = "-";
                SelectedLocalizedSummary = "-";
                SelectedScriptSummary = "-";
                SelectedScriptCode = string.Empty;
                SelectedWorkersAndVariables = string.Empty;
                SelectedStatsText = string.Empty;
                SelectedLootText = string.Empty;
                SelectedSensorText = string.Empty;
                SelectedScanText = string.Empty;
                return;
            }

            MonsterAiCorpus_Service.MonsterAiRecord record = value.Record;
            SelectedMonsterSummary = $"{record.DisplayTitle} · {record.RawMonsterHex} · {record.RelativePath}";
            SelectedOverviewSummary = record.OverviewSummary;
            SelectedReferenceSummary = record.CommandReferenceSummary;
            SelectedForcedActionSummary = record.ForcedActionSummary;
            SelectedAbilityListSummary = record.AbilityListSummary;
            SelectedLocalizedSummary = record.LocalizedSummary;
            SelectedScriptSummary = $"{record.ScriptLineCount} script lines · {record.WorkerCount} workers · {record.VariableCount} vars";
            SelectedScriptCode = string.IsNullOrWhiteSpace(record.ScriptCode)
                ? "No script code block parsed for this monster."
                : record.ScriptCode;
            SelectedWorkersAndVariables = string.IsNullOrWhiteSpace(record.ScriptWorkers)
                ? "No worker/variable block parsed for this monster."
                : record.ScriptWorkers;
            SelectedStatsText = string.IsNullOrWhiteSpace(record.MonsterStats)
                ? "No monster stats block parsed for this monster."
                : record.MonsterStats;
            SelectedLootText = string.IsNullOrWhiteSpace(record.MonsterLoot)
                ? "No monster loot block parsed for this monster."
                : record.MonsterLoot;
            SelectedSensorText = string.IsNullOrWhiteSpace(record.SensorText)
                ? "(No sensor text decoded.)"
                : record.SensorText;
            SelectedScanText = string.IsNullOrWhiteSpace(record.ScanText)
                ? "(No scan text decoded.)"
                : record.ScanText;

            foreach (MonsterAiCorpus_Service.MonsterAiAbilityReference reference in record.AbilityReferences)
            {
                SelectedReferences.Add(new MonsterAiReferenceRow
                {
                    IndexLabel = reference.RawHex,
                    Title = reference.DisplayLabel,
                    Summary = reference.Source
                });
            }
        }

        public void RefreshFromDisk()
        {
            ReloadCorpus();
        }

        void ReloadCorpus()
        {
            MonsterAiCorpus_Service.Reload();

            LoadedMonsters.Clear();
            DisplayedMonsters.Clear();
            SelectedReferences.Clear();
            SelectedMonster = null;

            foreach (MonsterAiCorpus_Service.MonsterAiRecord record in MonsterAiCorpus_Service.GetAllRecords())
            {
                LoadedMonsters.Add(MonsterAiCorpusRow.From(record));
            }

            LoadSummary = MonsterAiCorpus_Service.StatusSummary;
            ApplyFilter();
            SelectedMonster = DisplayedMonsters.FirstOrDefault();
        }

        void ApplyFilter()
        {
            string needle = FilterText.Trim();
            MonsterAiCorpusRow? previousSelection = SelectedMonster;

            DisplayedMonsters.Clear();
            foreach (MonsterAiCorpusRow row in LoadedMonsters)
            {
                if (needle.Length == 0 || row.SearchBlob.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayedMonsters.Add(row);
                }
            }

            if (previousSelection != null && DisplayedMonsters.Contains(previousSelection))
            {
                SelectedMonster = previousSelection;
            }
            else if (!DisplayedMonsters.Contains(SelectedMonster))
            {
                SelectedMonster = DisplayedMonsters.FirstOrDefault();
            }
        }
    }

    internal sealed class MonsterAiCorpusRow
    {
        public required MonsterAiCorpus_Service.MonsterAiRecord Record { get; init; }
        public required string IndexLabel { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string ContextSummary { get; init; }
        public required string SearchBlob { get; init; }

        public static MonsterAiCorpusRow From(MonsterAiCorpus_Service.MonsterAiRecord record)
        {
            return new MonsterAiCorpusRow
            {
                Record = record,
                IndexLabel = record.IndexLabel,
                Title = record.DisplayTitle,
                Summary = record.OverviewSummary,
                ContextSummary = record.RelativePath,
                SearchBlob = record.SearchBlob
            };
        }
    }

    internal sealed class MonsterAiReferenceRow
    {
        public required string IndexLabel { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
    }
}
