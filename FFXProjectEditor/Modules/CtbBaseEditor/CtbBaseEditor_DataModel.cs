using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.CtbBaseEditor
{
    internal partial class CtbBaseEditor_DataModel : ObservableObject
    {
        CtbBaseTable? loadedTable;

        public ObservableCollection<CtbBaseRow> LoadedRows { get; } = new();
        public ObservableCollection<CtbBaseRow> DisplayedRows { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading ctb_base.bin...";
        [ObservableProperty] private string scopeSummary = "Safe scope: edit existing agility rows only. Jarvis preserves the fixed 255-row table shape and only rewrites tickspeed + ICV bonus bytes.";
        [ObservableProperty] private string selectedSummary = "Select an agility row to inspect the CTB timing conversion.";
        [ObservableProperty] private CtbBaseRow? selectedRow;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public CtbBaseEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedRowChanged(CtbBaseRow? value)
        {
            SelectedSummary = value == null
                ? "Select an agility row to inspect the CTB timing conversion."
                : $"AGI {value.Agility} · TickSpeed {value.TickSpeed} · ICV {value.IcvMin}..{value.IcvMax}";
        }

        public void RefreshFromDisk() => LoadFromDisk();
        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedTable = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearRows("Project root not loaded.");
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelCtbBase))
            {
                ClearRows("jppc/battle/kernel/ctb_base.bin not found in the loaded workspace.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelCtbBase);
            loadedTable = CtbBase_File.Read(bytes);
            LoadRows(loadedTable, SelectedRow?.Index);

            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                "ctb base table",
                BuildFile());
        }

        void ClearRows(string message)
        {
            foreach (CtbBaseRow row in LoadedRows)
                row.PropertyChanged -= RowChanged;

            LoadedRows.Clear();
            DisplayedRows.Clear();
            SelectedRow = null;
            LoadSummary = message;
        }

        void LoadRows(CtbBaseTable table, int? preserveSelectionIndex)
        {
            foreach (CtbBaseRow row in LoadedRows)
                row.PropertyChanged -= RowChanged;

            LoadedRows.Clear();
            DisplayedRows.Clear();

            foreach (CtbBaseEntry entry in table.Entries)
            {
                CtbBaseRow row = CtbBaseRow.Wrap(entry);
                row.PropertyChanged += RowChanged;
                LoadedRows.Add(row);
            }

            ApplyFilter();
            SelectedRow = preserveSelectionIndex.HasValue
                ? LoadedRows.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedRows.FirstOrDefault();

            LoadSummary = $"Loaded {LoadedRows.Count} agility rows from ctb_base.bin.";
        }

        void ApplyFilter()
        {
            DisplayedRows.Clear();
            string normalized = FilterText.Trim();

            foreach (CtbBaseRow row in LoadedRows)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedRows.Add(row);
            }

            if (SelectedRow != null && !DisplayedRows.Contains(SelectedRow))
                SelectedRow = DisplayedRows.FirstOrDefault();
        }

        byte[] BuildFile()
        {
            if (loadedTable == null)
                return Array.Empty<byte>();

            loadedTable = new CtbBaseTable
            {
                OriginalBytes = loadedTable.OriginalBytes,
                Header = loadedTable.Header,
                Entries = LoadedRows.Select(row => row.ToEntry()).ToList()
            };

            return CtbBase_File.Write(loadedTable);
        }

        void RestoreFromBytes(byte[] bytes)
        {
            loadedTable = CtbBase_File.Read(bytes);
            LoadRows(loadedTable, SelectedRow?.Index);
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelCtbBase, bytes);
            loadedTable = CtbBase_File.Read(bytes);
        }

        void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (sender is CtbBaseRow row && ReferenceEquals(row, SelectedRow))
            {
                SelectedSummary = $"AGI {row.Agility} · TickSpeed {row.TickSpeed} · ICV {row.IcvMin}..{row.IcvMax}";
            }
        }

        internal partial class CtbBaseRow : ObservableObject
        {
            [ObservableProperty] private int tickSpeed;
            [ObservableProperty] private int icvBonus;

            public required int Index { get; init; }
            public required byte[] RawBytes { get; init; }

            public int Agility => Index + 1;
            public string AgilityLabel => $"AGI {Agility:D3}";
            public int IcvMax => TickSpeed * 3;
            public int IcvMin => IcvMax - IcvBonus;
            public string Summary => $"TickSpeed {TickSpeed} · ICV {IcvMin}..{IcvMax}";
            public string SearchBlob => $"{Agility} {AgilityLabel} {Summary}";

            public static CtbBaseRow Wrap(CtbBaseEntry entry)
            {
                return new CtbBaseRow
                {
                    Index = entry.Index,
                    RawBytes = entry.RawBytes.ToArray(),
                    TickSpeed = entry.TickSpeed,
                    IcvBonus = entry.IcvBonus
                };
            }

            public CtbBaseEntry ToEntry()
            {
                return new CtbBaseEntry
                {
                    Index = Index,
                    RawBytes = RawBytes.ToArray(),
                    TickSpeed = TickSpeed,
                    IcvBonus = IcvBonus
                };
            }

            partial void OnTickSpeedChanged(int value) => NotifyComputedChanged();
            partial void OnIcvBonusChanged(int value) => NotifyComputedChanged();

            void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(IcvMax));
                OnPropertyChanged(nameof(IcvMin));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
            }
        }
    }
}
