using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonEditor
{
    public partial class MonEditorSelector_DataModel : ObservableObject
    {
        /******************************************
         * Data
         ******************************************/
        public ObservableCollection<MonsterListEntry> LoadedMonsters { get; } = new();
        public ObservableCollection<MonsterListEntry> DisplayedMonsters { get; } = new();
        public ObservableCollection<MonsterListEntry> BatchSelectedTargets { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string batchSelectionSummary = Strings.F2_no_mirror_targets_armed_yet_48fed751;
        [ObservableProperty] private string selectorModeSummary = "Open one monster, mark only the targets you want on the left, then save once to mirror safely.";
        [ObservableProperty] private bool hasBatchSelection;
        [ObservableProperty] private bool canOpenBatchEditor;

        [ObservableProperty] public bool infoExpanded = false;
        [ObservableProperty] public bool statsExpanded = true;
        [ObservableProperty] public bool propertiesExpanded = true;
        [ObservableProperty] public bool elementalWeaknessesExpanded = true;
        [ObservableProperty] public bool statusExpanded = true;
        [ObservableProperty] public bool menuAbilitiesExpanded = false;
        [ObservableProperty] public bool identifiersExpanded = true;

        [ObservableProperty] public bool lootStatsExpanded = true;
        [ObservableProperty] public bool lootDropsExpanded = true;
        [ObservableProperty] public bool lootStealExpanded = true;
        [ObservableProperty] public bool lootGearExpanded = true;
        [ObservableProperty] public int selectedTab = 0;

        // Camada 4 (overhaul de UI): estado do Expander que substituiu a sidebar interna de 280-420px.
        [ObservableProperty] private bool isMonsterListExpanded = true;

        public event Action? BatchSelectionChanged;

        public MonEditorSelector_DataModel()
        {
            LoadEntries();
            ApplyFilter();
            RefreshBatchSelectionState();
        }

        public void LoadEntries()
        {
            foreach (MonsterListEntry monster in LoadedMonsters)
            {
                monster.PropertyChanged -= MonsterEntry_PropertyChanged;
            }

            LoadedMonsters.Clear();

            if (!Project_Service.Instance.IsProjectLoaded || !Directory.Exists(Project_Service.Instance.Path_Mon))
            {
                DisplayedMonsters.Clear();
                RefreshBatchSelectionState();
                return;
            }

            Regex regex = new(@"^_m(\d+)$");

            List<int> indices = Directory.GetDirectories(Project_Service.Instance.Path_Mon)
                .Select(Path.GetFileName)
                .Where(name => name != null && regex.IsMatch(name))
                .Select(name => int.Parse(regex.Match(name!).Groups[1].Value))
                .OrderBy(i => i)
                .ToList();

            foreach (int index in indices)
            {
                MonsterListEntry entry = new(index);
                entry.PropertyChanged += MonsterEntry_PropertyChanged;
                LoadedMonsters.Add(entry);
            }

            ApplyFilter();
            RefreshBatchSelectionState();
        }

        public void ApplyFilter()
        {
            DisplayedMonsters.Clear();

            IEnumerable<MonsterListEntry> filtered = LoadedMonsters
                .Where(monster => MatchesFilter(monster.Name));

            foreach (MonsterListEntry monster in filtered)
            {
                DisplayedMonsters.Add(monster);
            }
        }

        bool MatchesFilter(string value)
        {
            return string.IsNullOrWhiteSpace(FilterText) ||
                   value.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
        }

        public MonsterListEntry? FindMonster(int monsterIndex)
        {
            return LoadedMonsters.FirstOrDefault(monster => monster.Index == monsterIndex);
        }

        public void UpdateMonsterDisplayName(int monsterIndex, string? displayName)
        {
            MonsterListEntry? entry = FindMonster(monsterIndex);
            if (entry == null)
            {
                return;
            }

            bool wasVisible = MatchesFilter(entry.Name);
            if (!entry.TrySetDisplayName(displayName))
            {
                return;
            }

            bool isVisible = MatchesFilter(entry.Name);
            if (wasVisible != isVisible)
            {
                ApplyFilter();
            }

            if (entry.IsBatchSelected)
            {
                RefreshBatchSelectionState();
            }
        }

        public string GetMonsterPath(int monsterIndex)
        {
            return Project_Service.Instance.GetPathMon(monsterIndex);
        }

        public void LoadMonster(MonsterListEntry? monsterEntry, ContentControl contentFrame)
        {
            if (monsterEntry == null)
            {
                return;
            }

            string monsterPath = Project_Service.Instance.GetPathMon(monsterEntry.Index);
            byte[] byteFile = File.ReadAllBytes(monsterPath);

            // EditorContextHub (Jarvis-UI): o agente do AI Assistant recebe
            // "module=monster-editor, file=jppc/battle/mon/mNNN.bin, record=[idx] name".
            string? ws = Project_Service.Instance.ProjectPath;
            string rel = ws is not null && monsterPath.StartsWith(ws, StringComparison.OrdinalIgnoreCase)
                ? monsterPath[ws.Length..].TrimStart('/', '\\')
                : Path.GetFileName(monsterPath);
            Core.EditorContextHub.ReportSelection("monster-editor", rel, monsterEntry.Name);

            contentFrame.Content = new MonEditor_Control(Monster_File.Read(byteFile), monsterPath, this);
        }

        public void LoadBulkEditor(ContentControl contentFrame)
        {
            contentFrame.Content = new MonEditorBulk_Control(this);
        }

        public void SelectFilteredMonsters()
        {
            foreach (MonsterListEntry monster in DisplayedMonsters)
            {
                monster.IsBatchSelected = true;
            }

            RefreshBatchSelectionState();
        }

        public void ClearBatchSelection()
        {
            foreach (MonsterListEntry monster in LoadedMonsters)
            {
                monster.IsBatchSelected = false;
            }

            RefreshBatchSelectionState();
        }

        public IReadOnlyList<MonsterListEntry> GetBatchSelectedMonsters()
        {
            return LoadedMonsters
                .Where(monster => monster.IsBatchSelected)
                .OrderBy(monster => monster.Index)
                .ToList();
        }

        void MonsterEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MonsterListEntry.IsBatchSelected))
            {
                RefreshBatchSelectionState();
            }
        }

        void RefreshBatchSelectionState()
        {
            IReadOnlyList<MonsterListEntry> selected = GetBatchSelectedMonsters();
            BatchSelectedTargets.Clear();
            foreach (MonsterListEntry entry in selected.Take(12))
            {
                BatchSelectedTargets.Add(entry);
            }

            HasBatchSelection = selected.Count > 0;
            CanOpenBatchEditor = selected.Count > 0;

            if (selected.Count == 0)
            {
                BatchSelectionSummary = Strings.F2_no_mirror_targets_armed_yet_48fed751;
                SelectorModeSummary = "Open one monster, mark only the targets you want on the left, then save once to mirror safely.";
            }
            else
            {
                string preview = string.Join(", ", selected.Take(4).Select(monster => monster.ShortName));
                if (selected.Count > 4)
                {
                    preview += $", +{selected.Count - 4} more";
                }

                BatchSelectionSummary = $"{selected.Count} mirror target(s) armed: {preview}";
                SelectorModeSummary = "The monster you open acts as the source. Save mirrors the chosen scope into the armed targets only.";
            }

            BatchSelectionChanged?.Invoke();
        }

        public partial class MonsterListEntry : ObservableObject
        {
            [ObservableProperty] private bool isBatchSelected;
            [ObservableProperty] private string shortName;

            public int Index { get; }
            public string Name => $"[{Index}] {ShortName}";
            public string DefaultShortName { get; }
            public string BatchStateLabel => IsBatchSelected ? "Mirror target armed" : "Not armed";

            public MonsterListEntry(int index)
            {
                Index = index;
                DefaultShortName = Monster_Dictionary.Instance.ContainsKey((short)Index)
                    ? Monster_Dictionary.Instance[(short)Index]
                    : "<NOT INDEXED>";
                shortName = DefaultShortName;
            }

            public bool TrySetDisplayName(string? displayName)
            {
                string normalized = NormalizeDisplayName(displayName);
                if (string.Equals(ShortName, normalized, StringComparison.Ordinal))
                {
                    return false;
                }

                ShortName = normalized;
                return true;
            }

            partial void OnIsBatchSelectedChanged(bool value)
            {
                OnPropertyChanged(nameof(BatchStateLabel));
            }

            partial void OnShortNameChanged(string value)
            {
                OnPropertyChanged(nameof(Name));
            }

            string NormalizeDisplayName(string? displayName)
            {
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    return DefaultShortName;
                }

                string normalized = Regex.Replace(displayName, @"\s+", " ").Trim();
                return string.IsNullOrWhiteSpace(normalized)
                    ? DefaultShortName
                    : normalized;
            }
        }
    }
}
