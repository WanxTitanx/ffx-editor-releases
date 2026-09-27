using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.EncounterTableExplorer
{
    internal partial class EncounterTableExplorer_DataModel : ObservableObject
    {
        readonly List<EncounterTableTableRow> loadedTables = new();
        private EncounterTable_File? _loadedEncounterFile;
        private string _encounterFilePath = string.Empty;

        public ObservableCollection<EncounterTableMapRow> LoadedMaps { get; } = new();
        public ObservableCollection<EncounterTableTableRow> DisplayedTables { get; } = new();
        public ObservableCollection<EncounterTableGroupRow> SelectedGroups { get; } = new();
        public ObservableCollection<EncounterTableFormationRow> SelectedFormations { get; } = new();

        [ObservableProperty] private EncounterTableMapRow? selectedMap;
        [ObservableProperty] private EncounterTableTableRow? selectedTable;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to inspect encounter routing.";
        [ObservableProperty] private string selectedMapSummary = "Select a map bucket to inspect its encounter tables.";
        [ObservableProperty] private string selectedTableSummary = "Select an encounter table to inspect its groups, danger values, and weighted battle footprint.";
        [ObservableProperty] private string selectedTableIndexLabel = "No table selected";
        [ObservableProperty] private string selectedTableTitle = "Encounter routing will land here.";
        [ObservableProperty] private string selectedTableOverview = "Pick an encounter table in the middle column to inspect map, offsets, group count, and formation weighting.";
        [ObservableProperty] private string selectedTableOffsetSummary = "-";
        [ObservableProperty] private string selectedTableUnknownSummary = "-";
        [ObservableProperty] private string selectedTableBattlefieldSummary = "-";
        [ObservableProperty] private string selectedTableDangerSummary = "-";
        [ObservableProperty] private string selectedTableWeightSummary = "-";
        [ObservableProperty] private string selectedTableBattleIdsSummary = "-";
        [ObservableProperty] private string saveStatus = string.Empty;

        public EncounterTableExplorer_DataModel()
        {
            ReloadFromDisk();
        }

        partial void OnSelectedMapChanged(EncounterTableMapRow? value)
        {
            if (value == null)
            {
                DisplayedTables.Clear();
                SelectedTable = null;
                SelectedMapSummary = "Select a map bucket to inspect its encounter tables.";
                return;
            }

            SelectedMapSummary = value.DetailSummary;
            ApplyFilter(SelectedTable?.RecordId);
        }

        partial void OnSelectedTableChanged(EncounterTableTableRow? value)
        {
            SelectedGroups.Clear();
            SelectedFormations.Clear();

            if (value == null)
            {
                SelectedTableIndexLabel = "No table selected";
                SelectedTableTitle = "Encounter routing will land here.";
                SelectedTableSummary = "Select an encounter table to inspect its groups, danger values, and weighted battle footprint.";
                SelectedTableOverview = "Pick an encounter table in the middle column to inspect map, offsets, group count, and formation weighting.";
                SelectedTableOffsetSummary = "-";
                SelectedTableUnknownSummary = "-";
                SelectedTableBattlefieldSummary = "-";
                SelectedTableDangerSummary = "-";
                SelectedTableWeightSummary = "-";
                SelectedTableBattleIdsSummary = "-";
                return;
            }

            SelectedTableIndexLabel = value.IndexLabel;
            SelectedTableTitle = value.Title;
            SelectedTableSummary = value.ContextSummary;
            SelectedTableOverview = $"Map {value.Entry.Map} · table {value.Entry.Id:X4}h · {value.Entry.GroupCount} groups · {value.Entry.TotalFormationCount} declared formation slots.";
            SelectedTableOffsetSummary = $"Data {value.Entry.DataOffset:X4}h · Formation {value.Entry.FormationOffset:X4}h";
            SelectedTableUnknownSummary = $"MapNamePadding {value.Entry.MapNamePadding:X4}h";

            string battlefieldSummary = value.Entry.Groups.Count == 0
                ? "No groups decoded."
                : string.Join(", ", value.Entry.Groups.Select(group => $"{group.GroupIndex:D2}:{group.Battlefield:X4}h"));
            string dangerSummary = value.Entry.Groups.Count == 0
                ? "No groups decoded."
                : string.Join(", ", value.Entry.Groups.Select(group => $"{group.GroupIndex:D2}:{group.Danger:D3}"));
            int totalWeight = value.Entry.Groups.Sum(group => group.TotalWeight);
            IEnumerable<string> uniqueBattleIds = value.Entry.Groups
                .SelectMany(group => group.Formations)
                .Select(formation => formation.BattleId)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            SelectedTableBattlefieldSummary = battlefieldSummary;
            SelectedTableDangerSummary = dangerSummary;
            SelectedTableWeightSummary = $"{totalWeight} total declared group weight across {value.Entry.Groups.Count} groups.";
            SelectedTableBattleIdsSummary = string.Join(", ", uniqueBattleIds.Take(12))
                + (uniqueBattleIds.Skip(12).Any() ? ", ..." : string.Empty);

            foreach (EncounterTable_Group group in value.Entry.Groups)
            {
                SelectedGroups.Add(EncounterTableGroupRow.From(value.Entry, group));
                foreach (EncounterTable_Formation formation in group.Formations)
                {
                    SelectedFormations.Add(EncounterTableFormationRow.From(value.Entry, group, formation));
                }
            }

            ResolveFormationMonsterNames();
        }

        // Join do nome dos monstros por encontro (per-campo): formation.BattleId ->
        // battle/btl/<battleId>/<battleId>.bin -> Battle_File.Formation.Slots (MonsterName já resolvido).
        // Somente leitura de nomes; não altera bytes (RT0 preservado). Guardado/try-catch: falha => fallback p/ battleId.
        void ResolveFormationMonsterNames()
        {
            try
            {
                if (!Project_Service.Instance.IsProjectLoaded) return;
                string kernel = Project_Service.Instance.Path_KernelEncounterTable;
                if (string.IsNullOrEmpty(kernel)) return;
                string btlDir = Path.Combine(Path.GetDirectoryName(kernel) ?? string.Empty, "btl");
                foreach (EncounterTableFormationRow fr in SelectedFormations)
                {
                    fr.MonsterNames = ResolveMonsterNames(fr.FormationBattleId, btlDir);
                }
            }
            catch
            {
                // Nunca derruba a UI; os nomes ficam vazios (fallback p/ battleId acontece por-formation abaixo).
            }
        }

        static string ResolveMonsterNames(string battleId, string btlDir)
        {
            try
            {
                string fp = Path.Combine(btlDir, battleId, battleId + ".bin");
                if (!File.Exists(fp)) return battleId;
                Battle_File bf = Battle_File.Read(battleId, File.ReadAllBytes(fp));
                IReadOnlyList<Battle_FormationSlot>? slots = bf.Formation?.Slots;
                if (slots == null || slots.Count == 0) return "(sem slots)";
                List<string> names = slots
                    .Where(s => s != null && !s.IsEmpty)
                    .Select(s => string.IsNullOrEmpty(s.MonsterName) ? $"[{s.DictionaryId:X3}]" : s.MonsterName)
                    .ToList();
                return names.Count == 0 ? "(sem monstros)" : string.Join(" + ", names);
            }
            catch
            {
                return battleId;
            }
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter(SelectedTable?.RecordId);
        }

        public void RefreshFromDisk()
        {
            ReloadFromDisk(SelectedMap?.Id, SelectedTable?.RecordId);
        }

        // Add one more weighted formation to the first group of the selected table.
        // Structural grow via Rebuild() on next Save; byte-safe, proven by --encounter-rebuild-rt0.
        // RT2 pending: whether the engine actually samples the new formation in-game.
        [RelayCommand]
        private void AddFormationToSelectedGroup()
        {
            var entry = SelectedTable?.Entry;
            if (entry == null || entry.Groups.Count == 0)
            {
                SaveStatus = "Selecione uma tabela com pelo menos um grupo.";
                return;
            }
            var group = entry.Groups[0];
            int nextFid = group.Formations.Count == 0 ? 0 : group.Formations.Max(f => f.FormationId) + 1;
            group.Formations.Add(new EncounterTable_Formation
            {
                FormationId = nextFid,
                Weight = 1,
                BattleId = $"{entry.Map}_{nextFid:00}",
            });
            group.FormationCount = group.Formations.Count;
            group.TotalWeight = group.Formations.Sum(f => f.Weight);
            entry.TotalFormationCount = entry.Groups.Sum(g => g.Formations.Count);
            entry.GroupCount = entry.Groups.Count;
            // Re-render the selected table so the new formation shows up in the lists.
            var rec = SelectedTable?.RecordId;
            ReloadFromDisk(SelectedMap?.Id, rec);
            SaveStatus = $"Forma??o {nextFid:02} adicionada ao grupo 0 de {entry.Map}. Use Salvar (Rebuild estrutural) para gravar.";
        }

        void ReloadFromDisk(string? preferredMapId = null, string? preferredRecordId = null)
        {
            LoadedMaps.Clear();
            DisplayedTables.Clear();
            SelectedGroups.Clear();
            SelectedFormations.Clear();
            loadedTables.Clear();
            SelectedTable = null;
            _loadedEncounterFile = null;
            _encounterFilePath = string.Empty;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                SelectedMap = null;
                return;
            }

            string encounterPath = Project_Service.Instance.Path_KernelEncounterTable;
            if (!File.Exists(encounterPath))
            {
                LoadSummary = "battle/kernel/btl.bin was not found in the loaded workspace.";
                SelectedMap = null;
                return;
            }

            EncounterTable_File encounterTable = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
            if (encounterTable.Tables.Count == 0)
            {
                LoadSummary = "Encounter table parsed, but no routing entries were decoded.";
                SelectedMap = null;
                return;
            }

            _loadedEncounterFile = encounterTable;
            _encounterFilePath = encounterPath;

            foreach (EncounterTable_Entry table in encounterTable.Tables.OrderBy(entry => entry.Map, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.TableIndex))
            {
                loadedTables.Add(EncounterTableTableRow.From(table));
            }

            int totalGroupCount = encounterTable.Tables.Sum(entry => entry.Groups.Count);
            int totalFormationCount = encounterTable.Tables.Sum(entry => entry.Groups.Sum(group => group.Formations.Count));
            int uniqueBattleIds = encounterTable.Tables
                .SelectMany(entry => entry.Groups)
                .SelectMany(group => group.Formations)
                .Select(formation => formation.BattleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            LoadedMaps.Add(new EncounterTableMapRow(
                "all",
                "All Maps",
                $"READ_ENCOUNTER_TABLE · {encounterTable.Tables.Count} tables",
                $"{encounterTable.Tables.Count} tables · {totalGroupCount} groups · {totalFormationCount} weighted formations · {uniqueBattleIds} unique battle ids.",
                "battle/kernel/btl.bin"));

            foreach (IGrouping<string, EncounterTableTableRow> grouping in loadedTables.GroupBy(row => row.Entry.Map, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                int groupCount = grouping.Sum(row => row.Entry.Groups.Count);
                int formationCount = grouping.Sum(row => row.Entry.Groups.Sum(group => group.Formations.Count));
                int battleIdCount = grouping
                    .SelectMany(row => row.Entry.Groups)
                    .SelectMany(group => group.Formations)
                    .Select(formation => formation.BattleId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                LoadedMaps.Add(new EncounterTableMapRow(
                    grouping.Key,
                    grouping.Key,
                    $"READ_ENCOUNTER_TABLE · {grouping.Count()} tables",
                    $"{grouping.Count()} tables · {groupCount} groups · {formationCount} weighted formations · {battleIdCount} unique battle ids.",
                    "battle/kernel/btl.bin"));
            }

            LoadSummary = $"Loaded {encounterTable.Tables.Count} encounter tables from battle/kernel/btl.bin covering {LoadedMaps.Count - 1} map buckets.";

            EncounterTableMapRow? preferredMap = LoadedMaps.FirstOrDefault(row => row.Id.Equals(preferredMapId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            SelectedMap = preferredMap ?? LoadedMaps.FirstOrDefault();

            ApplyFilter(preferredRecordId);
        }

        void ApplyFilter(string? preferredRecordId = null)
        {
            IEnumerable<EncounterTableTableRow> filtered = loadedTables;
            if (SelectedMap != null && !SelectedMap.Id.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(row => row.Entry.Map.Equals(SelectedMap.Id, StringComparison.OrdinalIgnoreCase));
            }

            string needle = FilterText.Trim();
            if (!string.IsNullOrWhiteSpace(needle))
            {
                filtered = filtered.Where(row => row.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            List<EncounterTableTableRow> tableRows = filtered.ToList();
            DisplayedTables.Clear();
            foreach (EncounterTableTableRow row in tableRows)
            {
                DisplayedTables.Add(row);
            }

            EncounterTableTableRow? preferred = tableRows.FirstOrDefault(row => row.RecordId.Equals(preferredRecordId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            SelectedTable = preferred ?? tableRows.FirstOrDefault();
        }

        // --- Save ---

        [RelayCommand]
        private void SaveEncounterTable()
        {
            if (_loadedEncounterFile == null || string.IsNullOrEmpty(_encounterFilePath))
            {
                SaveStatus = Strings.F2_no_table_loaded_to_save_ed3c247f;
                return;
            }
            try
            {
                string bakPath = _encounterFilePath + ".bak";
                if (!File.Exists(bakPath))
                    File.Copy(_encounterFilePath, bakPath);
                // Rebuild() is RT0 byte-identical on a no-edit and re-serializes added formations (grow).
                byte[] bytes = _loadedEncounterFile.Rebuild();
                File.WriteAllBytes(_encounterFilePath, bytes);
                SaveStatus = $"Salvo ({bytes.Length} bytes). Backup em btl.bin.bak";
            }
            catch (Exception ex)
            {
                SaveStatus = $"Erro ao salvar: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestoreFromBackup()
        {
            string bakPath = _encounterFilePath + ".bak";
            if (string.IsNullOrEmpty(_encounterFilePath) || !File.Exists(bakPath))
            {
                SaveStatus = Strings.F2_backup_file_bak_not_found_72855c08;
                return;
            }
            try
            {
                File.Copy(bakPath, _encounterFilePath, overwrite: true);
                RefreshFromDisk();
                SaveStatus = "Vanilla restaurado do backup.";
            }
            catch (Exception ex)
            {
                SaveStatus = $"Erro ao restaurar: {ex.Message}";
            }
        }

        // --- Global Danger presets ---

        [RelayCommand] private void ApplyDangerNone() => ApplyGlobalDanger(0);
        [RelayCommand] private void ApplyDangerNormal() => ApplyGlobalDanger(50);
        [RelayCommand] private void ApplyDangerIntense() => ApplyGlobalDanger(128);
        [RelayCommand] private void ApplyDangerMax() => ApplyGlobalDanger(255);

        private void ApplyGlobalDanger(int value)
        {
            if (_loadedEncounterFile == null) return;
            foreach (EncounterTable_Entry table in _loadedEncounterFile.Tables)
                foreach (EncounterTable_Group group in table.Groups)
                    group.Danger = value;
            // Refresh currently displayed group rows so the UI shows the new value immediately.
            foreach (EncounterTableGroupRow row in SelectedGroups)
                row.Danger = value;
            int groupCount = _loadedEncounterFile.Tables.Sum(t => t.Groups.Count);
            SaveStatus = $"Danger={value} aplicado em {groupCount} grupos. Clique Salvar para gravar.";
        }
    }

    internal sealed class EncounterTableMapRow
    {
        public EncounterTableMapRow(string id, string displayName, string summary, string detailSummary, string relativePath)
        {
            Id = id;
            DisplayName = displayName;
            Summary = summary;
            DetailSummary = detailSummary;
            RelativePath = relativePath;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Summary { get; }
        public string DetailSummary { get; }
        public string RelativePath { get; }
    }

    internal sealed class EncounterTableTableRow
    {
        public required string RecordId { get; init; }
        public required EncounterTable_Entry Entry { get; init; }
        public required string IndexLabel { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string ContextSummary { get; init; }
        public required string SearchText { get; init; }

        public static EncounterTableTableRow From(EncounterTable_Entry entry)
        {
            List<string> battleIds = entry.Groups
                .SelectMany(group => group.Formations)
                .Select(formation => formation.BattleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            string battleIdSummary = battleIds.Count == 0
                ? "No battle ids decoded."
                : string.Join(", ", battleIds) + (entry.Groups.SelectMany(group => group.Formations).Select(formation => formation.BattleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > battleIds.Count ? ", ..." : string.Empty);

            string summary = $"{entry.GroupCount} groups · {entry.Groups.Sum(group => group.Formations.Count)} weighted formations · data {entry.DataOffset:X4}h";
            string contextSummary = $"Map {entry.Map} · table {entry.Id:X4}h · {summary} · battle ids: {battleIdSummary}";

            return new EncounterTableTableRow
            {
                RecordId = $"{entry.Map}:{entry.TableIndex:D4}:{entry.Id:X4}",
                Entry = entry,
                IndexLabel = $"#{entry.TableIndex:D3} · {entry.Map}",
                Title = $"Table {entry.Id:X4}h",
                Summary = summary,
                ContextSummary = contextSummary,
                SearchText = string.Join(" ",
                    entry.Map,
                    entry.Id.ToString(),
                    entry.Id.ToString("X4"),
                    entry.TableIndex.ToString(),
                    entry.MapNamePadding.ToString("X4"),
                    contextSummary,
                    battleIdSummary)
            };
        }
    }

    internal sealed partial class EncounterTableGroupRow : ObservableObject
    {
        private readonly EncounterTable_Group _group;
        // Exposed so the parent DataModel can grow this group formation list (structural Rebuild).
        public EncounterTable_Group InternalGroup => _group;

        public string IndexLabel { get; }
        public string Summary { get; }
        public string BattleIdsSummary { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        [NotifyPropertyChangedFor(nameof(DangerLabel))]
        private int danger;

        public string Title => $"Battlefield {_group.Battlefield:X4}h · Danger {Danger:D3}";
        public string DangerLabel => Danger switch
        {
            0 => Strings.U_Ee_DangerNone,
            < 64 => Strings.U_Ee_RarityRare,
            < 128 => Strings.U_Ee_RarityNormal,
            < 200 => Strings.U_Ee_RarityHigh,
            _ => Strings.U_Ee_RarityMax
        };

        partial void OnDangerChanged(int value)
        {
            _group.Danger = Math.Clamp(value, 0, 255);
        }

        [RelayCommand] private void SetDangerNone() => Danger = 0;
        [RelayCommand] private void SetDangerNormal() => Danger = 50;
        [RelayCommand] private void SetDangerIntense() => Danger = 128;
        [RelayCommand] private void SetDangerMax() => Danger = 255;

        private EncounterTableGroupRow(EncounterTable_Group group, string indexLabel, string summary, string battleIdsSummary)
        {
            _group = group;
            IndexLabel = indexLabel;
            Summary = summary;
            BattleIdsSummary = battleIdsSummary;
            danger = group.Danger;
        }

        public static EncounterTableGroupRow From(EncounterTable_Entry entry, EncounterTable_Group group)
        {
            string battleIds = group.Formations.Count == 0
                ? "No formations decoded."
                : string.Join(", ", group.Formations.Select(f => $"{f.BattleId} ({f.Weight})"));

            return new EncounterTableGroupRow(
                group,
                $"Group {group.GroupIndex:D2}",
                $"{group.FormationCount} formation slots · total weight {group.TotalWeight} · map {entry.Map}",
                battleIds);
        }
    }

    internal sealed partial class EncounterTableFormationRow : ObservableObject
    {
        private readonly EncounterTable_Formation _formation;
        private readonly EncounterTable_Group _group;
        private readonly string _map;

        public string IndexLabel { get; }
        public string Title { get; }
        public string FormationBattleId => _formation.BattleId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Summary))]
        private int weight;

        // Nome(s) do(s) monstro(s) deste encontro, preenchido pelo DataModel (join read-only).
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Summary))]
        private string monsterNames = string.Empty;

        public string Summary => $"Weight {Weight} / {_group.TotalWeight} · battlefield {_group.Battlefield:X4}h · danger {_group.Danger:D3} · map {_map}"
            + (string.IsNullOrEmpty(MonsterNames) ? string.Empty : $" · monstros: {MonsterNames}");

        partial void OnWeightChanged(int value)
        {
            _formation.Weight = Math.Clamp(value, 0, 255);
            _group.TotalWeight = _group.Formations.Sum(f => f.Weight);
        }

        private EncounterTableFormationRow(EncounterTable_Formation formation, EncounterTable_Group group, string map, string indexLabel, string title)
        {
            _formation = formation;
            _group = group;
            _map = map;
            IndexLabel = indexLabel;
            Title = title;
            weight = formation.Weight;
        }

        public static EncounterTableFormationRow From(EncounterTable_Entry entry, EncounterTable_Group group, EncounterTable_Formation formation)
        {
            return new EncounterTableFormationRow(
                formation,
                group,
                entry.Map,
                formation.BattleId,
                $"Group {group.GroupIndex:D2} · Formation {formation.FormationId:D2}");
        }
    }
}
