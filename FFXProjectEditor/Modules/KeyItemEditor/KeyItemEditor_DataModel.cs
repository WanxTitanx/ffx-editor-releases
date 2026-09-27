using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.KeyItemEditor
{
    internal partial class KeyItemEditor_DataModel : ObservableObject
    {
        KeyItemTable? loadedTable;

        public ObservableCollection<KeyItemRow> LoadedItems { get; } = new();
        public ObservableCollection<KeyItemRow> DisplayedItems { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading important.bin...";
        [ObservableProperty] private string scopeSummary = "Pt21/Pt22 production slice: structural reader plus no-edit guard only. The localized text prefix is now decoded correctly, but all mutation slices remain technical backlog.";
        [ObservableProperty] private string selectedItemSummary = "Select a key item to inspect its structural text prefix and preserved payload bytes.";
        [ObservableProperty] private KeyItemRow? selectedItem;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public bool MutationSurfaceEnabled => false;
        public string MutationSurfaceSummary => "Mutation surface held back";
        public string MutationSurfaceDetail => "Pt21 proved the real important.bin layout and byte-identical no-edit rebuild. Pt22 only measured candidate ranges; it did not promote a production writer. Save stays intentionally unavailable here.";

        public KeyItemEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedItemChanged(KeyItemRow? value)
        {
            UpdateSelectedSummary(value);
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

            if (!File.Exists(Project_Service.Instance.Path_KernelImportantUs))
            {
                ClearRows("new_uspc/battle/kernel/important.bin not found in the loaded workspace.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelImportantUs);
            loadedTable = KeyItem_File.Read(bytes);
            LoadRows(loadedTable, SelectedItem?.Index);

        }

        void ClearRows(string message)
        {
            foreach (KeyItemRow row in LoadedItems)
                row.PropertyChanged -= RowChanged;

            LoadedItems.Clear();
            DisplayedItems.Clear();
            SelectedItem = null;
            LoadSummary = message;
        }

        void LoadRows(KeyItemTable table, int? preserveSelectionIndex)
        {
            foreach (KeyItemRow row in LoadedItems)
                row.PropertyChanged -= RowChanged;

            LoadedItems.Clear();
            DisplayedItems.Clear();

            foreach (KeyItemEntry entry in table.Entries)
            {
                KeyItemRow row = KeyItemRow.Wrap(entry);
                row.PropertyChanged += RowChanged;
                LoadedItems.Add(row);
            }

            ApplyFilter();
            SelectedItem = preserveSelectionIndex.HasValue
                ? LoadedItems.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedItems.FirstOrDefault();

            LoadSummary = $"Loaded {LoadedItems.Count} key items from important.bin.";
        }

        void ApplyFilter()
        {
            DisplayedItems.Clear();
            string normalized = FilterText.Trim();

            foreach (KeyItemRow row in LoadedItems)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedItems.Add(row);
            }

            if (SelectedItem != null && !DisplayedItems.Contains(SelectedItem))
                SelectedItem = DisplayedItems.FirstOrDefault();
        }

        byte[] BuildFile()
        {
            if (loadedTable == null)
                return Array.Empty<byte>();

            loadedTable = new KeyItemTable
            {
                OriginalBytes = loadedTable.OriginalBytes,
                Header = loadedTable.Header,
                Entries = LoadedItems.Select(row => row.ToEntry()).ToList()
            };

            return KeyItem_File.Write(loadedTable);
        }

        void RestoreFile(byte[] bytes)
        {
            loadedTable = KeyItem_File.Read(bytes);
            LoadRows(loadedTable, SelectedItem?.Index);
        }

        void PersistFile(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelImportantUs, bytes);
            loadedTable = KeyItem_File.Read(bytes);
        }

        void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (sender is KeyItemRow row && ReferenceEquals(row, SelectedItem))
                UpdateSelectedSummary(row);
        }

        void UpdateSelectedSummary(KeyItemRow? row)
        {
            SelectedItemSummary = row == null
                ? "Select a key item to inspect its structural text prefix and preserved payload bytes."
                : $"{row.Label} · Pt21 reader closed on the real 0x14 entry shape · Raw payload bytes remain visualized, but mutation stays backlog-only.";
        }

        internal partial class KeyItemRow : ObservableObject
        {
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int itemType;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(RawTailSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int itemValue;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(RawTailSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int icon;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int number;

            public required int Index { get; init; }
            public required string Label { get; init; }
            public required byte[] RawBytes { get; init; }
            public required string NameText { get; init; }
            public required string DashText { get; init; }
            public required string DescriptionText { get; init; }
            public required string OtherText { get; init; }

            public string IndexLabel => $"#{Index:D3}";
            public string PayloadSummary => $"Number 13h {Number} · ItemType 10h {ItemType}";
            public string RawTailSummary => $"11h {ItemValue:X2}h · 12h {Icon:X2}h";
            public string SearchBlob => $"{Index:D3} {Label} {NameText} {DescriptionText} {DashText} {OtherText} {PayloadSummary} {RawTailSummary}";

            public static KeyItemRow Wrap(KeyItemEntry entry)
            {
                return new KeyItemRow
                {
                    Index = entry.Index,
                    Label = entry.Label,
                    RawBytes = entry.RawBytes.ToArray(),
                    NameText = entry.NameText,
                    DashText = entry.DashText,
                    DescriptionText = entry.DescriptionText,
                    OtherText = entry.OtherText,
                    ItemType = entry.ItemType,
                    ItemValue = entry.ItemValue,
                    Icon = entry.Icon,
                    Number = entry.Number
                };
            }

            public KeyItemEntry ToEntry()
            {
                return new KeyItemEntry
                {
                    Index = Index,
                    Label = Label,
                    RawBytes = RawBytes.ToArray(),
                    NameText = NameText,
                    DashText = DashText,
                    DescriptionText = DescriptionText,
                    OtherText = OtherText,
                    ItemType = ItemType,
                    ItemValue = ItemValue,
                    Icon = Icon,
                    Number = Number
                };
            }
        }
    }
}
