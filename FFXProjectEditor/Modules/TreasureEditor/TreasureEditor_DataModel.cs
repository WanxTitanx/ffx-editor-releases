using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.TreasureEditor
{
    internal partial class TreasureEditor_DataModel : ObservableObject
    {
        static readonly IReadOnlyList<TreasureKindOption> SharedKindOptions =
        [
            new("Gil (0x00)", 0x00, "Money payload. Normal gil uses Type 0000h and raw Quantity x 100."),
            new("Item (0x02)", 0x02, "Type stores 0x2000 | item id."),
            new("Gear Pickup / buki_get (0x05)", 0x05, "Type stores a raw buki_get.bin row index."),
            new("Key Item (0x0A)", 0x0A, "Type stores 0xA000 | key item id."),
            new("Special / Reserved (0x01)", 0x01, "Observed special row; keep raw semantics explicit."),
            new("Special / Reserved (0x04)", 0x04, "Observed special row; keep raw semantics explicit."),
            new("Special / Reserved (0x14)", 0x14, "Observed special row; keep raw semantics explicit."),
            new("Raw / Unmapped", null, "Preserve unusual kind values without assigning fake semantics.")
        ];

        IReadOnlyList<TreasurePayloadOption> itemOptions = [];
        IReadOnlyList<TreasurePayloadOption> keyItemOptions = [];
        IReadOnlyList<TreasurePayloadOption> gearOptions = [];

        public ObservableCollection<TreasureRow> LoadedTreasures { get; } = new();
        public ObservableCollection<TreasureRow> DisplayedTreasures { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading takara.bin...";
        [ObservableProperty] private string selectedTreasureSummary = Strings.F2_select_a_treasure_entry_to_inspect_or_ed_863fdaf2;
        [ObservableProperty] private string selectedTreasureScope = "Safe scope: edit existing takara.bin entries only. Placement, chest visibility, and treasure flags still live elsewhere.";
        [ObservableProperty] private string selectorCatalogSummary = "Payload selectors are idle until takara.bin loads.";
        [ObservableProperty] private string editActionSummary = "Save writes takara.bin. Undo reverts the latest local edit. Restore Original rewrites the as-loaded snapshot even after Save.";
        [ObservableProperty] private TreasureRow? selectedTreasure;
        [ObservableProperty] private AtlasEvidenceInfo? selectedTreasureEvidence;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        TreasureTable? loadedTreasureTable;

        public TreasureEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedTreasureChanged(TreasureRow? value)
        {
            SelectedTreasureSummary = value == null
                ? Strings.F2_select_a_treasure_entry_to_inspect_or_ed_863fdaf2
                : $"Treasure #{value.Index:D3} · {value.Summary}";

            RefreshSelectedTreasureEvidence();
        }

        // Read-only Spira Data Atlas evidence for the selected gear chest, shown only when a stable crosslink exists.
        // The treasure-buki-get crosslink keys source_key as "treasure:0x{index:X4}" (proved 1:1 vs takara.bin), and
        // the accessor re-checks the current buki_get target, so the badge hides the moment the chest is repointed or
        // changed away from gear. Non-gear payloads, or indices the corpus never recorded as gear, return null and the
        // strip hides itself — no guessing, no stale badge.
        void RefreshSelectedTreasureEvidence()
        {
            TreasureRow? row = SelectedTreasure;
            SelectedTreasureEvidence = row != null
                && row.IsGearKind
                && SpiraDataAtlasCatalog.TryGetTreasureGear(row.Index, row.ItemId, out SpiraDataAtlasDetailEntry? detail)
                && detail != null
                    ? AtlasEvidenceInfo.ForDetail(detail)
                    : null;
        }

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        public void RefreshFromDisk()
        {
            LoadFromDisk();
        }

        public void SetSelectedTreasureKind(byte kind)
        {
            if (SelectedTreasure == null)
                return;

            SelectedTreasure.Kind = kind;
        }

        public int? GetSelectedBukiGetRow()
        {
            return SelectedTreasure?.IsGearKind == true
                ? SelectedTreasure.ItemId
                : null;
        }

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedTreasureTable = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadedTreasures.Clear();
                DisplayedTreasures.Clear();
                SelectedTreasure = null;
                LoadSummary = "Project root not loaded.";
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelTreasure))
            {
                LoadedTreasures.Clear();
                DisplayedTreasures.Clear();
                SelectedTreasure = null;
                LoadSummary = "takara.bin not found in the loaded workspace.";
                return;
            }

            string keyItemPath = File.Exists(Project_Service.Instance.Path_KernelImportantUs)
                ? Project_Service.Instance.Path_KernelImportantUs
                : Path.Combine(Project_Service.Instance.Path_Kernel, "important.bin");
            KeyItem_Dictionary.EnsureLoaded(keyItemPath);
            itemOptions = BuildItemOptions();
            keyItemOptions = BuildKeyItemOptions();
            gearOptions = TryBuildGearOptions(out string gearSummary);
            SelectorCatalogSummary = $"Selectors: {itemOptions.Count} items · {keyItemOptions.Count} key items · {gearSummary}";

            byte[] bytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelTreasure);
            LoadRowsFromBytes(bytes, preserveSelectionIndex: SelectedTreasure?.Index);
            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                "treasure data",
                BuildFile())
            {
                RevertWritesToDisk = true
            };
        }

        void LoadRowsFromBytes(byte[] bytes, int? preserveSelectionIndex = null)
        {
            foreach (TreasureRow row in LoadedTreasures)
            {
                row.PropertyChanged -= TreasureRowChanged;
            }

            LoadedTreasures.Clear();
            DisplayedTreasures.Clear();

            TreasureTable table = Treasure_File.ReadTable(bytes);
            loadedTreasureTable = table;
            IReadOnlyList<Treasure_Entry> entries = table.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                TreasureRow row = TreasureRow.Wrap(i, entries[i], SharedKindOptions, itemOptions, keyItemOptions, gearOptions);
                row.PropertyChanged += TreasureRowChanged;
                LoadedTreasures.Add(row);
            }

            ApplyFilter();

            TreasureRow? restoredSelection = preserveSelectionIndex.HasValue
                ? LoadedTreasures.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedTreasures.FirstOrDefault();

            SelectedTreasure = restoredSelection ?? DisplayedTreasures.FirstOrDefault();
            LoadSummary = $"Loaded {LoadedTreasures.Count} treasure entries from takara.bin (0x14 header, index {table.Header.MinIndex}..{table.Header.MaxIndex}).";
        }

        void ApplyFilter()
        {
            DisplayedTreasures.Clear();
            string normalizedFilter = FilterText.Trim();

            foreach (TreasureRow row in LoadedTreasures)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayedTreasures.Add(row);
                }
            }

            if (SelectedTreasure != null && !DisplayedTreasures.Contains(SelectedTreasure))
            {
                SelectedTreasure = DisplayedTreasures.FirstOrDefault();
            }
        }

        byte[] BuildFile()
        {
            IReadOnlyList<Treasure_Entry> entries = LoadedTreasures.Select(row => row.Unwrap()).ToList();
            if (loadedTreasureTable == null)
                return Treasure_File.WriteAll(entries);

            return Treasure_File.WriteTable(new TreasureTable
            {
                OriginalBytes = loadedTreasureTable.OriginalBytes,
                Header = loadedTreasureTable.Header,
                Entries = entries
            });
        }

        void RestoreFromBytes(byte[] bytes)
        {
            LoadRowsFromBytes(bytes, preserveSelectionIndex: SelectedTreasure?.Index);
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelTreasure, bytes);
        }

        void TreasureRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            // Re-evaluate the read-only Atlas badge when the selected chest's payload identity changes, so editing the
            // kind or the gear target hides/updates the evidence instead of leaving a stale reward badge on screen.
            if (ReferenceEquals(sender, SelectedTreasure)
                && (e.PropertyName == nameof(TreasureRow.Kind) || e.PropertyName == nameof(TreasureRow.ItemId)))
            {
                RefreshSelectedTreasureEvidence();
            }
        }

        static IReadOnlyList<TreasurePayloadOption> BuildItemOptions()
        {
            return Item_Dictionary.Instance
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    ushort rawType = (ushort)(0x2000 | pair.Key);
                    return new TreasurePayloadOption(
                        rawType,
                        pair.Key,
                        $"[{pair.Key:D3}] {pair.Value}",
                        $"Type 0x{rawType:X4} · category Items (0x2) · index {pair.Key:D3}");
                })
                .ToList();
        }

        static IReadOnlyList<TreasurePayloadOption> BuildKeyItemOptions()
        {
            return KeyItem_Dictionary.Instance
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    ushort rawType = (ushort)(0xA000 | pair.Key);
                    return new TreasurePayloadOption(
                        rawType,
                        pair.Key,
                        $"[{pair.Key:D3}] {pair.Value}",
                        $"Type 0x{rawType:X4} · category KeyItems (0xA) · index {pair.Key:D3}");
                })
                .ToList();
        }

        static IReadOnlyList<TreasurePayloadOption> TryBuildGearOptions(out string summary)
        {
            string bukiGetPath = Project_Service.Instance.Path_KernelBukiGet;
            if (!File.Exists(bukiGetPath))
            {
                summary = "buki_get.bin not found; gear selector falls back to raw Type.";
                return [];
            }

            try
            {
                BukiGetTreasureCatalog catalog = BukiGetTreasureCatalog_File.Read(File.ReadAllBytes(bukiGetPath));
                summary = $"{catalog.EntriesByIndex.Count} buki_get rows (shape min=0 max=85 entryLen=0x10).";
                return catalog.EntriesByIndex.Values
                    .OrderBy(entry => entry.Index)
                    .Select(entry => new TreasurePayloadOption(
                        (ushort)entry.Index,
                        (ushort)entry.Index,
                        entry.DisplayLabel,
                        $"{entry.Summary} · {entry.DetailSummary}"))
                    .ToList();
            }
            catch (Exception ex)
            {
                summary = $"buki_get.bin did not pass the narrow shape gate: {ex.Message}";
                return [];
            }
        }

        internal partial class TreasureRow : ObservableObject
        {
            readonly IReadOnlyList<TreasureKindOption> kindOptions;
            readonly IReadOnlyList<TreasurePayloadOption> itemOptions;
            readonly IReadOnlyList<TreasurePayloadOption> keyItemOptions;
            readonly IReadOnlyList<TreasurePayloadOption> gearOptions;

            [ObservableProperty] private byte kind;
            [ObservableProperty] private byte quantity;
            [ObservableProperty] private ushort itemId;
            [ObservableProperty] private string itemFilterText = string.Empty;
            [ObservableProperty] private string keyItemFilterText = string.Empty;
            [ObservableProperty] private string gearFilterText = string.Empty;

            public int Index { get; init; }

            public string IndexLabel => $"#{Index:D3}";
            public IReadOnlyList<TreasureKindOption> KindOptions => kindOptions;
            public TreasureKindOption? SelectedKindOption
            {
                get => kindOptions.FirstOrDefault(option => option.Kind == Kind)
                    ?? kindOptions.FirstOrDefault(option => option.Kind == null);
                set
                {
                    if (value?.Kind is byte selectedKind)
                        Kind = selectedKind;
                }
            }

            public IReadOnlyList<TreasurePayloadOption> FilteredItemOptions => FilterOptions(itemOptions, ItemFilterText);
            public IReadOnlyList<TreasurePayloadOption> FilteredKeyItemOptions => FilterOptions(keyItemOptions, KeyItemFilterText);
            public IReadOnlyList<TreasurePayloadOption> FilteredGearOptions => FilterOptions(gearOptions, GearFilterText);

            public TreasurePayloadOption? SelectedItemOption
            {
                get => itemOptions.FirstOrDefault(option => option.RawType == ItemId);
                set
                {
                    if (value == null)
                        return;

                    Kind = 0x02;
                    ItemId = value.RawType;
                }
            }

            public TreasurePayloadOption? SelectedKeyItemOption
            {
                get => keyItemOptions.FirstOrDefault(option => option.RawType == ItemId);
                set
                {
                    if (value == null)
                        return;

                    Kind = 0x0A;
                    ItemId = value.RawType;
                }
            }

            public TreasurePayloadOption? SelectedGearOption
            {
                get => gearOptions.FirstOrDefault(option => option.RawType == ItemId);
                set
                {
                    if (value == null)
                        return;

                    Kind = 0x05;
                    ItemId = value.RawType;
                }
            }

            public string KindLabel => ResolveKindLabel(Kind);
            public string ItemIdHex => $"0x{ItemId:X4}";
            public string ItemIdDecimal => ItemId.ToString();
            public string ResolvedName => ResolveTypeDisplayName(Kind, ItemId, Quantity);
            public string Summary => BuildSummary();
            public string SearchBlob => $"{Index:D3} {KindLabel} {ResolvedName} {Summary} {PayloadDetailSummary} {ItemId:X4}";
            public string KnownKindSummary => "Known kinds: 00 = Gil · 02 = Item · 05 = Gear Pickup · 0A = Key Item · 01/04/14 = Special / Reserved";
            public string ValuePreview => Kind == 0x00 ? $"{Quantity * 100} gil" : "-";
            public bool IsGilKind => Kind == 0x00;
            public bool IsItemKind => Kind == 0x02;
            public bool IsGearKind => Kind == 0x05;
            public bool IsKeyItemKind => Kind == 0x0A;
            public bool IsSpecialOrRawKind => !IsGilKind && !IsItemKind && !IsGearKind && !IsKeyItemKind;
            public bool ShowGilSentinelWarning => Kind == 0x00 && (Quantity == 0 || ItemId != 0);
            public string GilFormulaSummary => Kind == 0x00
                ? $"Gil amount = raw Quantity {Quantity} x 100 = {Quantity * 100} gil. Normal money rows use ItemId 0x0000."
                : "Gil formula applies only to Kind 0x00.";
            public string GilSentinelSummary => ShowGilSentinelWarning
                ? "Reserved/sentinel money-like row: Quantity is 0 or ItemId is not 0x0000, so Jarvis is not labeling this as a normal gil chest."
                : "Normal gil payload shape.";
            public string PayloadDetailSummary => BuildPayloadDetailSummary();
            public string GearBridgeWarning => "Gear pickup names are not promoted here: buki_get row details are shown, but final equipment naming still needs the w_name.bin / weapon-data bridge.";

            TreasureRow(
                IReadOnlyList<TreasureKindOption> kindOptions,
                IReadOnlyList<TreasurePayloadOption> itemOptions,
                IReadOnlyList<TreasurePayloadOption> keyItemOptions,
                IReadOnlyList<TreasurePayloadOption> gearOptions)
            {
                this.kindOptions = kindOptions;
                this.itemOptions = itemOptions;
                this.keyItemOptions = keyItemOptions;
                this.gearOptions = gearOptions;
            }

            public static TreasureRow Wrap(
                int index,
                Treasure_Entry entry,
                IReadOnlyList<TreasureKindOption> kindOptions,
                IReadOnlyList<TreasurePayloadOption> itemOptions,
                IReadOnlyList<TreasurePayloadOption> keyItemOptions,
                IReadOnlyList<TreasurePayloadOption> gearOptions)
            {
                return new TreasureRow(kindOptions, itemOptions, keyItemOptions, gearOptions)
                {
                    Index = index,
                    Kind = entry.Kind,
                    Quantity = entry.Quantity,
                    ItemId = entry.ItemId
                };
            }

            public Treasure_Entry Unwrap()
            {
                return new Treasure_Entry
                {
                    Kind = Kind,
                    Quantity = Quantity,
                    ItemId = ItemId
                };
            }

            partial void OnKindChanged(byte value) => NotifyComputedChanged();
            partial void OnQuantityChanged(byte value) => NotifyComputedChanged();
            partial void OnItemIdChanged(ushort value) => NotifyComputedChanged();
            partial void OnItemFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredItemOptions));
            partial void OnKeyItemFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredKeyItemOptions));
            partial void OnGearFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredGearOptions));

            void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(SelectedKindOption));
                OnPropertyChanged(nameof(SelectedItemOption));
                OnPropertyChanged(nameof(SelectedKeyItemOption));
                OnPropertyChanged(nameof(SelectedGearOption));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(ItemIdHex));
                OnPropertyChanged(nameof(ItemIdDecimal));
                OnPropertyChanged(nameof(ResolvedName));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
                OnPropertyChanged(nameof(ValuePreview));
                OnPropertyChanged(nameof(IsGilKind));
                OnPropertyChanged(nameof(IsItemKind));
                OnPropertyChanged(nameof(IsGearKind));
                OnPropertyChanged(nameof(IsKeyItemKind));
                OnPropertyChanged(nameof(IsSpecialOrRawKind));
                OnPropertyChanged(nameof(ShowGilSentinelWarning));
                OnPropertyChanged(nameof(GilFormulaSummary));
                OnPropertyChanged(nameof(GilSentinelSummary));
                OnPropertyChanged(nameof(PayloadDetailSummary));
            }

            string BuildSummary()
            {
                return Kind switch
                {
                    0x00 => $"Gil chest · {Quantity * 100} gil",
                    0x02 => $"Item chest · {Quantity}x {ResolvedName}",
                    0x05 => $"Gear chest · {ResolvedName}" + (Quantity == 1 ? string.Empty : $" · qty {Quantity}"),
                    0x0A => $"Key item chest · {ResolvedName}",
                    0x01 or 0x04 or 0x14 => $"Special / reserved payload · K={Kind:X2}h · Q={Quantity} · T={ItemId:X4}h",
                    _ => $"Unmapped chest payload · K={Kind:X2}h · Q={Quantity} · T={ItemId:X4}h"
                };
            }

            static string ResolveKindLabel(byte kind)
            {
                return kind switch
                {
                    0x00 => "Gil",
                    0x02 => "Item",
                    0x05 => "Gear Pickup",
                    0x0A => "Key Item",
                    0x01 or 0x04 or 0x14 => $"Special / Reserved ({kind:X2}h)",
                    _ => $"Unmapped ({kind:X2}h)"
                };
            }

            string ResolveTypeDisplayName(byte kind, ushort type, byte quantity)
            {
                return kind switch
                {
                    0x00 => $"{quantity * 100} gil",
                    0x02 => ResolveItemDisplayName(type),
                    0x05 => gearOptions.FirstOrDefault(option => option.RawType == type)?.Display ?? $"buki_get #{type:D4}",
                    0x0A => ResolveKeyItemDisplayName(type),
                    0x01 or 0x04 or 0x14 => $"Special / reserved · Type {type:X4}h",
                    _ => $"Type {type:X4}h"
                };
            }

            static string ResolveItemDisplayName(ushort rawType)
            {
                try
                {
                    byte category = FfxCommon_Util.GetGameCategory(rawType);
                    ushort index = FfxCommon_Util.GetGameIndex(rawType);

                    if (category == (byte)GameCategory_Enum.Items)
                    {
                        return FfxCommon_Util.GetGameIndexName(category, index);
                    }
                }
                catch
                {
                }

                if (Item_Dictionary.Instance.ContainsKey(rawType))
                {
                    return Item_Dictionary.Instance[rawType];
                }

                return $"Item #{rawType:X4}";
            }

            static string ResolveKeyItemDisplayName(ushort rawType)
            {
                try
                {
                    byte category = FfxCommon_Util.GetGameCategory(rawType);
                    ushort index = FfxCommon_Util.GetGameIndex(rawType);

                    if (category == (byte)GameCategory_Enum.KeyItems &&
                        KeyItem_Dictionary.TryGetName(index, out string resolved))
                    {
                        return resolved;
                    }
                }
                catch
                {
                }

                return $"Key item #{rawType:X4}";
            }

            string BuildPayloadDetailSummary()
            {
                return Kind switch
                {
                    0x00 => $"{GilFormulaSummary} {GilSentinelSummary}",
                    0x02 => BuildGameIndexDetail("Items", 0x2, ItemId, SelectedItemOption),
                    0x0A => BuildGameIndexDetail("KeyItems", 0xA, ItemId, SelectedKeyItemOption),
                    0x05 => SelectedGearOption == null
                        ? $"Gear pickup raw Type {ItemId:X4}h. buki_get row is unavailable or outside the proven 0..85 range."
                        : $"{SelectedGearOption.Detail} {GearBridgeWarning}",
                    0x01 or 0x04 or 0x14 => $"Special / reserved payload. Raw values stay explicit: Kind {Kind:X2}h, Quantity {Quantity}, Type {ItemId:X4}h.",
                    _ => $"Raw / unmapped payload. Raw values stay explicit: Kind {Kind:X2}h, Quantity {Quantity}, Type {ItemId:X4}h."
                };
            }

            static string BuildGameIndexDetail(string categoryName, byte expectedCategory, ushort rawType, TreasurePayloadOption? option)
            {
                byte category = FfxCommon_Util.GetGameCategory(rawType);
                ushort index = FfxCommon_Util.GetGameIndex(rawType);
                string resolved = option == null ? "not resolved by the current selector" : option.Display;
                string categoryNote = category == expectedCategory
                    ? $"category {categoryName} (0x{category:X})"
                    : $"unexpected category 0x{category:X}; expected 0x{expectedCategory:X}";

                return $"Type 0x{rawType:X4} · {categoryNote} · index {index:D3} · {resolved}.";
            }

            static IReadOnlyList<TreasurePayloadOption> FilterOptions(IReadOnlyList<TreasurePayloadOption> options, string filterText)
            {
                string filter = filterText.Trim();
                if (filter.Length == 0)
                    return options;

                return options
                    .Where(option => option.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        internal sealed class TreasureKindOption
        {
            public TreasureKindOption(string display, byte? kind, string detail)
            {
                Display = display;
                Kind = kind;
                Detail = detail;
            }

            public string Display { get; }
            public byte? Kind { get; }
            public string Detail { get; }
        }

        internal sealed class TreasurePayloadOption
        {
            public TreasurePayloadOption(ushort rawType, ushort index, string display, string detail)
            {
                RawType = rawType;
                Index = index;
                Display = display;
                Detail = detail;
                SearchText = $"{rawType:X4} {rawType} {index:X4} {index} {display} {detail}";
            }

            public ushort RawType { get; }
            public ushort Index { get; }
            public string Display { get; }
            public string Detail { get; }
            public string SearchText { get; }
        }
    }
}
