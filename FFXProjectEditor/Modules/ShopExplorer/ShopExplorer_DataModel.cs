using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Shop;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Modules.ShopExplorer
{
    internal partial class ShopExplorer_DataModel : ObservableObject
    {
        readonly List<ShopEntryRow> loadedShops = [];
        ShopGearCatalog? loadedGearCatalog;
        ShopItemCatalog? loadedItemCatalog;
        ShopTable? loadedTable;
        ShopWriterReadinessReport? loadedWriterReadiness;
        string? pendingPreferredRecordId;

        public ObservableCollection<ShopSourceRow> LoadedSources { get; } = [];
        public ObservableCollection<ShopEntryRow> DisplayedShops { get; } = [];
        public ObservableCollection<ShopEditableSlotRow> SelectedSlots { get; } = [];

        [ObservableProperty] private ShopSourceRow? selectedSource;
        [ObservableProperty] private ShopEntryRow? selectedShop;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to inspect the fixed shop tables.";
        [ObservableProperty] private string selectedSourceSummary = "Select item_shop.bin or arms_shop.bin to inspect its fixed rows.";
        [ObservableProperty] private string selectedShopSummary = "Select a shop row to inspect its fixed 16-slot payload.";
        [ObservableProperty] private string selectedShopIndexLabel = "No shop selected";
        [ObservableProperty] private string selectedShopTitle = "Fixed shop row details land here.";
        [ObservableProperty] private string selectedShopOverview = "Shape-first read path: header, entry count, word 00h, and 16 ushort slots.";
        [ObservableProperty] private string selectedShopHeaderSummary = "-";
        [ObservableProperty] private string selectedShopConfidenceSummary = "-";
        [ObservableProperty] private string selectedShopFootprintSummary = "-";
        [ObservableProperty] private string selectedShopOpaqueSummary = "-";
        [ObservableProperty] private string observedEvidenceSummary = "Load a shop table to inspect the observed slot patterns in the current workspace.";
        [ObservableProperty] private string sourceConfidenceSummary = "Confidence view is idle until a shop table is loaded.";
        [ObservableProperty] private string roundTripSummary = "Round-trip verification is idle until a shop table is loaded.";
        [ObservableProperty] private string editGateSummary = "Private edit gate is idle until a shop table is loaded.";
        [ObservableProperty] private string filterFeedbackSummary = "Filter is idle until a shop table is loaded.";
        [ObservableProperty] private string writerCapabilitySummary = "Writer readiness is idle until a shop table is loaded.";
        [ObservableProperty] private string writerScopeSummary = "Writer scope is idle until a shop table is loaded.";
        [ObservableProperty] private string writerRiskSummary = "Writer risk is idle until a shop table is loaded.";
        [ObservableProperty] private string writerAvailabilitySummary = "Slot writer is idle until a shop table is loaded.";
        [ObservableProperty] private string pendingWriteSummary = "No pending shop slot changes.";
        [ObservableProperty] private bool isWriterAvailable;
        [ObservableProperty] private string writerPolicySummary = "Explorer first. Slot writer stays blocked until the narrow payload slice is proven hard enough.";
        [ObservableProperty] private string compactSurfaceTitle = "Shop Slots";
        [ObservableProperty] private string compactSurfaceSummary = "Pick a shop row to inspect and edit its guarded slot payload.";
        [ObservableProperty] private string compactSurfaceGuardrail = "Only the proven slot payload slice is editable here.";
        [ObservableProperty] private string bridgeStatusSummary = "Load a shop table to inspect the guarded slot bridge.";
        [ObservableProperty] private string primaryWarningSummary = string.Empty;
        [ObservableProperty] private bool showPrimaryWarning;
        [ObservableProperty] private bool showFilterFeedback;
        [ObservableProperty] private string itemIconCoverageSummary = string.Empty;
        [ObservableProperty] private bool showItemIconCoverageWarning;

        public ShopExplorer_DataModel()
        {
            ReloadFromDisk();
        }

        partial void OnSelectedSourceChanged(ShopSourceRow? value)
        {
            if (value == null)
            {
                EditSession?.Dispose();
                EditSession = null;
                loadedGearCatalog = null;
                loadedItemCatalog = null;
                loadedTable = null;
                loadedWriterReadiness = null;
                loadedShops.Clear();
                DisplayedShops.Clear();
                DetachSelectedSlotHandlers();
                SelectedSlots.Clear();
                SelectedShop = null;
                SelectedSourceSummary = "Select item_shop.bin or arms_shop.bin to inspect its fixed rows.";
                ObservedEvidenceSummary = "Load a shop table to inspect the observed slot patterns in the current workspace.";
                SourceConfidenceSummary = "Confidence view is idle until a shop table is loaded.";
                RoundTripSummary = "Round-trip verification is idle until a shop table is loaded.";
                EditGateSummary = "Private edit gate is idle until a shop table is loaded.";
                FilterFeedbackSummary = "Filter is idle until a shop table is loaded.";
                WriterCapabilitySummary = "Writer readiness is idle until a shop table is loaded.";
                WriterScopeSummary = "Writer scope is idle until a shop table is loaded.";
                WriterRiskSummary = "Writer risk is idle until a shop table is loaded.";
                WriterAvailabilitySummary = "Slot writer is idle until a shop table is loaded.";
                PendingWriteSummary = "No pending shop slot changes.";
                IsWriterAvailable = false;
                WriterPolicySummary = "Explorer first. Slot writer stays blocked until the narrow payload slice is proven hard enough.";
                CompactSurfaceTitle = "Shop Slots";
                CompactSurfaceSummary = "Pick a shop row to inspect and edit its guarded slot payload.";
                CompactSurfaceGuardrail = "Only the proven slot payload slice is editable here.";
                BridgeStatusSummary = "Load a shop table to inspect the guarded slot bridge.";
                PrimaryWarningSummary = string.Empty;
                ShowPrimaryWarning = false;
                ShowFilterFeedback = false;
                ItemIconCoverageSummary = string.Empty;
                ShowItemIconCoverageWarning = false;
                return;
            }

            LoadSelectedSource(value, pendingPreferredRecordId);
            pendingPreferredRecordId = null;
        }

        partial void OnSelectedShopChanged(ShopEntryRow? value)
        {
            DetachSelectedSlotHandlers();
            SelectedSlots.Clear();

            if (value == null)
            {
                SelectedShopIndexLabel = "No shop selected";
                SelectedShopTitle = "Fixed shop row details land here.";
                SelectedShopSummary = "Select a shop row to inspect its fixed 16-slot payload.";
                SelectedShopOverview = "Shape-first read path: header, entry count, word 00h, and 16 ushort slots.";
                SelectedShopHeaderSummary = "-";
                SelectedShopConfidenceSummary = "-";
                SelectedShopFootprintSummary = "-";
                SelectedShopOpaqueSummary = "-";
                RefreshCompactSurfaceState();
                RefreshPrimaryWarningSummary();
                return;
            }

            SelectedShopIndexLabel = value.IndexLabel;
            SelectedShopTitle = value.Title;
            SelectedShopSummary = value.Summary;
            SelectedShopOverview = value.Overview;
            SelectedShopHeaderSummary = value.HeaderSummary;
            SelectedShopConfidenceSummary = value.ConfidenceSummary;
            SelectedShopFootprintSummary = value.FootprintSummary;
            SelectedShopOpaqueSummary = value.OpaqueSummary;

            RebuildSelectedSlots(value);
            RefreshCompactSurfaceState();
            RefreshPrimaryWarningSummary();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter(SelectedShop?.RecordId);
        }

        public void RefreshFromDisk()
        {
            ReloadFromDisk(SelectedSource?.Id, SelectedShop?.RecordId);
        }

        public void Save()
        {
            if (EditSession == null)
                return;

            EditSession.Save();
            RefreshFromDisk();
        }

        public void Undo()
        {
            EditSession?.Undo();
            RefreshPendingWriteSummary();
        }

        public void Discard()
        {
            EditSession?.Discard();
            RefreshPendingWriteSummary();
        }

        public void ClearSlot(ShopEditableSlotRow slot)
        {
            if (!slot.CanEdit)
                return;

            ShopSlotOptionRow? emptyOption = slot.AvailableOptions.FirstOrDefault(option => option.RawValue == 0);
            if (emptyOption != null)
                slot.SelectedOption = emptyOption;
        }

        void ReloadFromDisk(string? preferredSourceId = null, string? preferredRecordId = null)
        {
            EditSession?.Dispose();
            EditSession = null;
            LoadedSources.Clear();
            loadedShops.Clear();
            loadedGearCatalog = null;
            loadedItemCatalog = null;
            loadedTable = null;
            loadedWriterReadiness = null;
            DisplayedShops.Clear();
            DetachSelectedSlotHandlers();
            SelectedSlots.Clear();
            SelectedShop = null;
            IsWriterAvailable = false;
            PendingWriteSummary = "No pending shop slot changes.";

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                SelectedSource = null;
                return;
            }

            AddSource("item", "Item Shop", Project_Service.Instance.Path_KernelItemShop, ShopTableKind.Item);
            AddSource("gear", "Gear Shop", Project_Service.Instance.Path_KernelArmsShop, ShopTableKind.Gear);

            if (LoadedSources.Count == 0)
            {
                LoadSummary = "item_shop.bin and arms_shop.bin were not found in the loaded workspace.";
                SelectedSource = null;
                return;
            }

            pendingPreferredRecordId = preferredRecordId;
            SelectedSource = LoadedSources.FirstOrDefault(source => string.Equals(source.Id, preferredSourceId, StringComparison.OrdinalIgnoreCase))
                ?? LoadedSources.First();
        }

        void AddSource(string id, string displayName, string absolutePath, ShopTableKind kind)
        {
            if (!File.Exists(absolutePath))
                return;

            string relativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, absolutePath);
            LoadedSources.Add(new ShopSourceRow(
                id,
                displayName,
                absolutePath,
                relativePath,
                kind,
                "47 fixed rows · 16 slot values each",
                kind == ShopTableKind.Item
                    ? "Guarded writer · Item or Empty only"
                    : "Guarded writer · Catalog Row or Empty only"));
        }

        void LoadSelectedSource(ShopSourceRow source, string? preferredRecordId = null)
        {
            EditSession?.Dispose();
            EditSession = null;
            IsWriterAvailable = false;
            PendingWriteSummary = "No pending shop slot changes.";
            loadedShops.Clear();
            DisplayedShops.Clear();
            DetachSelectedSlotHandlers();
            SelectedSlots.Clear();

            loadedGearCatalog = source.Kind == ShopTableKind.Gear
                ? TryLoadGearCatalog()
                : null;
            loadedItemCatalog = source.Kind == ShopTableKind.Item
                ? TryLoadItemCatalog()
                : null;

            ShopTable table = ShopTable_File.Read(File.ReadAllBytes(source.AbsolutePath), source.Kind, loadedGearCatalog);
            loadedTable = table;
            ShopRoundTripReport roundTripReport = ShopRoundTripVerifier.Verify(table.OriginalBytes, source.Kind, loadedGearCatalog);
            ShopEditGateReport editGateReport = ShopEditGateVerifier.VerifyCanonicalMutation(table.OriginalBytes, source.Kind, loadedGearCatalog);
            ShopWriterReadinessReport readinessReport = ShopWriterReadinessVerifier.VerifyFile(source.AbsolutePath, source.Kind, loadedGearCatalog);
            loadedWriterReadiness = readinessReport;

            SelectedSourceSummary = BuildSourceSummary(source, table);
            ObservedEvidenceSummary = BuildObservedEvidenceSummary(source, table, loadedGearCatalog);
            SourceConfidenceSummary = BuildSourceConfidenceSummary(source, table, loadedGearCatalog);
            RoundTripSummary = BuildRoundTripSummary(source, roundTripReport, loadedGearCatalog != null);
            EditGateSummary = BuildEditGateSummary(editGateReport);
            WriterCapabilitySummary = BuildWriterCapabilitySummary(readinessReport);
            WriterScopeSummary = BuildWriterScopeSummary(readinessReport);
            WriterRiskSummary = BuildWriterRiskSummary(readinessReport);
            IsWriterAvailable = CanEnableWriter(source, readinessReport);
            WriterAvailabilitySummary = BuildWriterAvailabilitySummary(source, readinessReport, loadedGearCatalog != null);
            WriterPolicySummary = BuildWriterPolicySummary(source, readinessReport, loadedGearCatalog != null);
            LoadSummary = $"Loaded {table.Entries.Count} rows from {source.RelativePath}.";
            RefreshItemIconCoverageSummary(source);
            RebuildRowsFromCurrentTable(preferredRecordId);
            RefreshBridgeStatusSummary(source);

            if (IsWriterAvailable)
            {
                EditSession = new ByteSnapshotEditorSession(
                    BuildFile,
                    RestoreFromBytes,
                    PersistBytes,
                    source.Kind == ShopTableKind.Item ? "item shop slot payload" : "gear shop slot payload",
                    BuildFile());
            }

            RefreshCompactSurfaceState();
            RefreshPrimaryWarningSummary();
        }

        void ApplyFilter(string? preferredRecordId = null)
        {
            IEnumerable<ShopEntryRow> filtered = loadedShops;
            string needle = FilterText.Trim();
            if (!string.IsNullOrWhiteSpace(needle))
                filtered = filtered.Where(row => MatchesSearch(row.SearchText, needle));

            List<ShopEntryRow> rows = filtered.ToList();
            DisplayedShops.Clear();
            foreach (ShopEntryRow row in rows)
                DisplayedShops.Add(row);

            FilterFeedbackSummary = BuildFilterFeedbackSummary(needle, rows.Count);
            ShowFilterFeedback = !string.IsNullOrWhiteSpace(needle) || rows.Count == 0;
            SelectedShop = rows.FirstOrDefault(row => string.Equals(row.RecordId, preferredRecordId, StringComparison.OrdinalIgnoreCase))
                ?? rows.FirstOrDefault();
        }

        void RebuildRowsFromCurrentTable(string? preferredRecordId = null)
        {
            if (loadedTable == null || SelectedSource == null)
                return;

            loadedShops.Clear();
            foreach (ShopEntry entry in loadedTable.Entries)
                loadedShops.Add(ShopEntryRow.From(SelectedSource, loadedTable, entry, loadedItemCatalog));

            ApplyFilter(preferredRecordId);
            RefreshPendingWriteSummary();
        }

        void RebuildSelectedSlots(ShopEntryRow row)
        {
            if (loadedTable == null || SelectedSource == null)
                return;

            ShopEntry? entry = loadedTable.Entries.FirstOrDefault(candidate => candidate.Index == row.EntryIndex);
            if (entry == null)
                return;

            foreach (ShopSlotEntry slot in entry.Slots)
            {
                ShopEditableSlotRow editorRow = ShopEditableSlotRow.From(
                    SelectedSource,
                    loadedTable,
                    entry,
                    slot,
                    BuildSlotOptions(SelectedSource, slot.RawValue),
                    loadedItemCatalog,
                    loadedGearCatalog,
                    IsWriterAvailable);
                editorRow.PropertyChanged += SelectedSlot_PropertyChanged;
                SelectedSlots.Add(editorRow);
            }
        }

        void DetachSelectedSlotHandlers()
        {
            foreach (ShopEditableSlotRow slot in SelectedSlots)
                slot.PropertyChanged -= SelectedSlot_PropertyChanged;
        }

        void SelectedSlot_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ShopEditableSlotRow.SelectedOption))
                return;

            if (sender is not ShopEditableSlotRow slot || slot.SelectedOption == null)
                return;

            ApplySlotMutation(slot, slot.SelectedOption);
        }

        void ApplySlotMutation(ShopEditableSlotRow slot, ShopSlotOptionRow option)
        {
            if (!slot.CanEdit || loadedTable == null || SelectedSource == null || SelectedShop == null)
                return;

            if (!ShopTable_File.IsAllowedEditableValue(SelectedSource.Kind, option.RawValue, loadedGearCatalog))
                return;

            ShopEntry? entry = loadedTable.Entries.FirstOrDefault(candidate => candidate.Index == SelectedShop.EntryIndex);
            if (entry == null)
                return;

            ShopSlotEntry? currentSlot = entry.Slots.FirstOrDefault(candidate => candidate.SlotIndex == slot.SlotIndex);
            if (currentSlot == null || currentSlot.RawValue == option.RawValue)
                return;

            loadedTable = CloneTableWithSlotMutation(loadedTable, SelectedSource.Kind, entry.Index, slot.SlotIndex, option.RawValue, loadedGearCatalog);
            RebuildRowsFromCurrentTable(SelectedShop.RecordId);
            EditSession?.NotifyPotentialMutation();
        }

        byte[] BuildFile()
        {
            return loadedTable == null
                ? Array.Empty<byte>()
                : ShopTable_File.Write(loadedTable);
        }

        void RestoreFromBytes(byte[] bytes)
        {
            if (SelectedSource == null)
                return;

            loadedTable = ShopTable_File.Read(bytes, SelectedSource.Kind, loadedGearCatalog);
            SelectedSourceSummary = BuildSourceSummary(SelectedSource, loadedTable);
            ObservedEvidenceSummary = BuildObservedEvidenceSummary(SelectedSource, loadedTable, loadedGearCatalog);
            SourceConfidenceSummary = BuildSourceConfidenceSummary(SelectedSource, loadedTable, loadedGearCatalog);
            RebuildRowsFromCurrentTable(SelectedShop?.RecordId);
        }

        void PersistBytes(byte[] bytes)
        {
            if (SelectedSource == null)
                return;

            if (loadedTable != null)
                ShopTable_File.AssertSlotOnlyDiff(loadedTable, bytes);

            File.WriteAllBytes(SelectedSource.AbsolutePath, bytes);
            loadedTable = ShopTable_File.Read(bytes, SelectedSource.Kind, loadedGearCatalog);
        }

        void RefreshPendingWriteSummary()
        {
            if (loadedTable == null)
            {
                PendingWriteSummary = "No pending shop slot changes.";
                return;
            }

            List<(int EntryIndex, int PendingCount)> pendingByRow = loadedTable.Entries
                .Select(entry => (entry.Index, PendingCount: entry.Slots.Count(slot => slot.RawValue != ReadBaselineSlotValue(loadedTable, entry.Index, slot.SlotIndex))))
                .Where(entry => entry.PendingCount > 0)
                .ToList();

            if (pendingByRow.Count == 0)
            {
                PendingWriteSummary = "No pending shop slot changes.";
                RefreshCompactSurfaceState();
                return;
            }

            int totalPending = pendingByRow.Sum(entry => entry.PendingCount);
            string preview = string.Join(", ", pendingByRow.Take(4).Select(entry => $"Shop {entry.EntryIndex:D2} ({entry.PendingCount})"));
            string suffix = pendingByRow.Count > 4 ? ", ..." : string.Empty;
            PendingWriteSummary = $"{totalPending} pending slot change(s) across {pendingByRow.Count} shop row(s): {preview}{suffix}. Save rewrites slot bytes only; word 00h and table shape stay read-only.";
            RefreshCompactSurfaceState();
        }

        IReadOnlyList<ShopSlotOptionRow> BuildSlotOptions(ShopSourceRow source, ushort currentRawValue)
        {
            List<ShopSlotOptionRow> options = source.Kind == ShopTableKind.Item
                ? BuildItemOptions()
                : BuildGearOptions();

            if (options.All(option => option.RawValue != currentRawValue))
                options.Insert(0, BuildPreserveCurrentOption(source.Kind, currentRawValue));

            return options;
        }

        List<ShopSlotOptionRow> BuildItemOptions()
        {
            List<ShopSlotOptionRow> options =
            [
                new ShopSlotOptionRow(0x0000, "[0000h] Empty", "Writes a zero sentinel into the validated empty-slot position.")
            ];

            foreach ((ushort index, string label) in Item_Dictionary.Instance.OrderBy(pair => pair.Key))
            {
                ushort rawValue = 0;
                rawValue = FfxCommon_Util.SetGameCategory(rawValue, (byte)GameCategory_Enum.Items);
                rawValue = FfxCommon_Util.SetGameIndex(rawValue, index);

                ShopItemCatalogEntry? catalogEntry = loadedItemCatalog?.EntriesByIndex.TryGetValue(index, out ShopItemCatalogEntry? entry) == true
                    ? entry
                    : null;
                string displayLabel = catalogEntry?.DisplayLabel ?? label;
                string detail = catalogEntry == null
                    ? $"Encoded Items game-index {index:X3}h."
                    : $"Encoded Items game-index {index:X3}h · icon {catalogEntry.IconId:X2}h · {catalogEntry.Description}";

                options.Add(new ShopSlotOptionRow(rawValue, $"[{rawValue:X4}h] {displayLabel}", detail));
            }

            return options;
        }

        List<ShopSlotOptionRow> BuildGearOptions()
        {
            List<ShopSlotOptionRow> options =
            [
                new ShopSlotOptionRow(0x0000, "[0000h] Empty", "Writes a zero sentinel into the validated empty-slot position.")
            ];

            if (loadedGearCatalog == null)
                return options;

            foreach ((int index, ShopGearCatalogEntry entry) in loadedGearCatalog.EntriesByIndex.OrderBy(pair => pair.Key))
                options.Add(new ShopSlotOptionRow((ushort)index, $"[{index:X4}h] {entry.DisplayLabel}", entry.Summary));

            return options;
        }

        ShopSlotOptionRow BuildPreserveCurrentOption(ShopTableKind kind, ushort rawValue)
        {
            if (rawValue == 0)
                return new ShopSlotOptionRow(0x0000, "[0000h] Empty", "Writes a zero sentinel into the validated empty-slot position.");

            if (kind == ShopTableKind.Item && ShopTable_File.TryResolveItemGameIndex(rawValue, out ushort index, out string? label))
                return new ShopSlotOptionRow(rawValue, $"[{rawValue:X4}h] {label}", $"Current on-disk encoded item game-index {index:X3}h.");

            if (kind == ShopTableKind.Gear && loadedGearCatalog != null && loadedGearCatalog.EntriesByIndex.TryGetValue(rawValue, out ShopGearCatalogEntry? entry))
                return new ShopSlotOptionRow(rawValue, $"[{rawValue:X4}h] {entry.DisplayLabel}", entry.Summary);

            return new ShopSlotOptionRow(rawValue, $"[{rawValue:X4}h] Preserve current raw", "This on-disk value stays visible even though it is outside the normal narrow writer option list.");
        }

        static bool CanEnableWriter(ShopSourceRow source, ShopWriterReadinessReport report)
        {
            return report.PublicWriterSafe
                && (source.Kind == ShopTableKind.Item || report.StructuredBridgeProven);
        }

        static string BuildWriterCapabilitySummary(ShopWriterReadinessReport report)
        {
            return report.PublicWriterSafe
                ? $"{report.Verdict} · {report.ProductionDecision}. Reader, serializer, and slot-only save/reopen proof are aligned for this narrow guarded slice."
                : $"{report.Verdict} · {report.ProductionDecision}. Reader is proven, but serializer or slot-only save/reopen proof is still incomplete for this guarded slice.";
        }

        static string BuildWriterScopeSummary(ShopWriterReadinessReport report)
        {
            return $"{report.EditableSlice} {report.ReadOnlyFields}";
        }

        static string BuildWriterRiskSummary(ShopWriterReadinessReport report)
        {
            return $"{report.ResidualRisk} {report.PromotionBlocker}";
        }

        static string BuildWriterAvailabilitySummary(ShopSourceRow source, ShopWriterReadinessReport report, bool hasLocalGearCatalog)
        {
            if (!report.PublicWriterSafe)
                return $"Writer remains blocked for {source.DisplayName}: {report.PromotionBlocker}";

            if (source.Kind == ShopTableKind.Gear && !hasLocalGearCatalog)
                return "Gear writer stays blocked until the local shop_arms.bin bridge is present in the loaded workspace.";

            return source.Kind == ShopTableKind.Item
                ? "Guarded writer is live here: edit slot payloads as Item or Empty, then save manually."
                : "Guarded writer is live here: edit slot payloads as Catalog Row or Empty, then save manually.";
        }

        static string BuildWriterPolicySummary(ShopSourceRow source, ShopWriterReadinessReport report, bool hasLocalGearCatalog)
        {
            if (!report.PublicWriterSafe)
                return $"Explorer-first. Writer remains blocked for {source.DisplayName} until the proven slot slice is stable enough.";

            if (source.Kind == ShopTableKind.Gear && !hasLocalGearCatalog)
                return "Explorer-first. Gear writer remains blocked without the local shop_arms.bin bridge.";

            return source.Kind == ShopTableKind.Item
                ? "Guarded writer. Only the 16 shop slots are writable as Item or Empty; word 00h and table shape stay read-only."
                : "Guarded writer. Only the 16 shop slots are writable as Catalog Row or Empty; no fake-safe gear-name surface is exposed.";
        }

        internal static ushort ReadBaselineSlotValue(ShopTable table, int entryIndex, int slotIndex)
        {
            int entryOffset = 0x14 + ((entryIndex - table.Header.MinIndex) * table.Header.EntryLength);
            int slotOffset = entryOffset + 0x02 + (slotIndex * 0x02);
            if (slotOffset < 0 || slotOffset + 1 >= table.OriginalBytes.Length)
                return 0;

            return (ushort)(table.OriginalBytes[slotOffset] | (table.OriginalBytes[slotOffset + 1] << 8));
        }

        static ShopTable CloneTableWithSlotMutation(ShopTable source, ShopTableKind kind, int entryIndex, int slotIndex, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            List<ShopEntry> entries = new(source.Entries.Count);
            foreach (ShopEntry entry in source.Entries)
            {
                List<ShopSlotEntry> slots = new(entry.Slots.Count);
                foreach (ShopSlotEntry slot in entry.Slots)
                {
                    ushort nextRawValue = entry.Index == entryIndex && slot.SlotIndex == slotIndex
                        ? rawValue
                        : slot.RawValue;

                    slots.Add(ShopTable_File.BuildSlotEntry(kind, slot.SlotIndex, nextRawValue, gearCatalog));
                }

                entries.Add(new ShopEntry
                {
                    Index = entry.Index,
                    RawBytes = entry.RawBytes.ToArray(),
                    UnusedPriceWord = entry.UnusedPriceWord,
                    Slots = slots
                });
            }

            return new ShopTable
            {
                Kind = source.Kind,
                OriginalBytes = source.OriginalBytes.ToArray(),
                Header = source.Header,
                Entries = entries
            };
        }

        string BuildFilterFeedbackSummary(string needle, int visibleCount)
        {
            if (SelectedSource == null)
                return "Filter is idle until a shop table is loaded.";

            if (loadedShops.Count == 0)
                return $"No shop rows are loaded for {SelectedSource.DisplayName} yet.";

            if (string.IsNullOrWhiteSpace(needle))
                return $"Showing all {visibleCount} shop rows from {SelectedSource.DisplayName}.";

            if (visibleCount > 0)
                return $"Filter '{needle}' matched {visibleCount} shop row(s) in {SelectedSource.DisplayName}.";

            if (SelectedSource.Kind == ShopTableKind.Item)
            {
                List<string> matchingItemLabels = FindMatchingItemLabels(needle);
                if (matchingItemLabels.Count > 0)
                {
                    string preview = string.Join(", ", matchingItemLabels.Take(4));
                    string suffix = matchingItemLabels.Count > 4 ? ", ..." : string.Empty;
                    return $"Filter '{needle}' matched no shop rows in {SelectedSource.DisplayName}. Matching item label(s) exist in the global item dictionary ({preview}{suffix}), but they are not present in the loaded item_shop.bin sample.";
                }
            }

            if (SelectedSource.Kind == ShopTableKind.Gear && loadedGearCatalog != null)
            {
                List<string> matchingItemLabels = FindMatchingItemLabels(needle);
                if (matchingItemLabels.Count > 0)
                {
                    string preview = string.Join(", ", matchingItemLabels.Take(4));
                    string suffix = matchingItemLabels.Count > 4 ? ", ..." : string.Empty;
                    return $"Filter '{needle}' matched global item label(s) ({preview}{suffix}), but the current source is Gear Shop. Switch to Item Shop to search shop items.";
                }

                List<string> matchingCatalogLabels = loadedGearCatalog.EntriesByIndex.Values
                    .Select(entry => entry.DisplayLabel)
                    .Where(label => MatchesSearch(label, needle))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();

                if (matchingCatalogLabels.Count > 0)
                {
                    return $"Filter '{needle}' matched no visible gear-shop rows in {SelectedSource.DisplayName}. Matching gear catalog label(s) exist in shop_arms.bin ({string.Join(", ", matchingCatalogLabels)}), but they are not present in the loaded arms_shop.bin sample.";
                }
            }

            return $"Filter '{needle}' matched no shop rows in {SelectedSource.DisplayName}.";
        }

        List<string> FindMatchingItemLabels(string needle)
        {
            IEnumerable<string> labels = loadedItemCatalog?.EntriesByIndex.Values.Select(entry => entry.DisplayLabel)
                ?? Item_Dictionary.Instance.Values;

            return labels
                .Where(label => MatchesSearch(label, needle))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static bool MatchesSearch(string haystack, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle))
                return true;

            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;

            string normalizedNeedle = NormalizeSearchValue(needle);
            if (string.IsNullOrWhiteSpace(normalizedNeedle))
                return false;

            return NormalizeSearchValue(haystack).Contains(normalizedNeedle, StringComparison.Ordinal);
        }

        static string NormalizeSearchValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new(value.Length);
            bool previousWasSeparator = true;
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator)
                {
                    builder.Append(' ');
                    previousWasSeparator = true;
                }
            }

            return builder.ToString().Trim();
        }

        static string BuildSourceSummary(ShopSourceRow source, ShopTable table)
        {
            return source.Kind == ShopTableKind.Item
                ? $"{table.Entries.Count} shop rows · 16 item slots per row · Item or Empty writer slice only."
                : $"{table.Entries.Count} shop rows · 16 gear slots per row · Catalog Row or Empty writer slice only.";
        }

        ShopGearCatalog? TryLoadGearCatalog()
        {
            string catalogPath = Project_Service.Instance.Path_KernelShopArmsCatalog;
            if (!File.Exists(catalogPath))
                return null;

            try
            {
                return ShopGearCatalog_File.Read(File.ReadAllBytes(catalogPath));
            }
            catch
            {
                return null;
            }
        }

        ShopItemCatalog? TryLoadItemCatalog()
        {
            string itemPath = Project_Service.Instance.Path_KernelItemUs;
            if (!File.Exists(itemPath))
                return null;

            try
            {
                return ShopItemCatalog_File.Read(File.ReadAllBytes(itemPath));
            }
            catch
            {
                return null;
            }
        }

        void RefreshItemIconCoverageSummary(ShopSourceRow source)
        {
            if (source.Kind != ShopTableKind.Item)
            {
                ItemIconCoverageSummary = string.Empty;
                ShowItemIconCoverageWarning = false;
                return;
            }

            if (loadedItemCatalog == null)
            {
                ItemIconCoverageSummary = "Real item icon coverage is blocked here: item.bin could not be loaded, so the lab cannot verify which resolved shop items should receive the proved original PNG set.";
                ShowItemIconCoverageWarning = true;
                return;
            }

            List<ShopItemCatalogEntry> sampleEntries = GetDistinctResolvedSampleItemEntries(loadedTable, loadedItemCatalog);
            int sampleCoveredCount = sampleEntries.Count(ShopItemIconAtlas.HasProvedAsset);
            int globalCoveredCount = loadedItemCatalog.EntriesByIndex.Values.Count(ShopItemIconAtlas.HasProvedAsset);
            string uncoveredPreview = BuildUncoveredItemPreview(sampleEntries);

            if (sampleEntries.Count == 0)
            {
                ItemIconCoverageSummary = $"No resolved non-empty item slots are active in the current sample. The lab now carries a proved IconId bridge for {ShopItemIconAtlas.ProvedAssetCount} distinct real item icon families, with global coverage at {globalCoveredCount}/{loadedItemCatalog.EntryCount} item entries.";
                ShowItemIconCoverageWarning = globalCoveredCount < loadedItemCatalog.EntryCount;
                return;
            }

            if (sampleCoveredCount == sampleEntries.Count && globalCoveredCount == loadedItemCatalog.EntryCount)
            {
                ItemIconCoverageSummary = $"Real item icon coverage is complete: {sampleCoveredCount}/{sampleEntries.Count} distinct resolved shop items in the current sample render the proved original game icon, and the loaded item catalog is covered end-to-end at {globalCoveredCount}/{loadedItemCatalog.EntryCount} entries through the verified IconId bridge.";
                ShowItemIconCoverageWarning = false;
                return;
            }

            if (sampleCoveredCount == sampleEntries.Count)
            {
                ItemIconCoverageSummary = $"The current item_shop sample is fully covered at {sampleCoveredCount}/{sampleEntries.Count} distinct resolved shop items. Broader item catalog coverage is still at {globalCoveredCount}/{loadedItemCatalog.EntryCount} entries while the remaining IconId families are verified.";
                ShowItemIconCoverageWarning = true;
                return;
            }

            ItemIconCoverageSummary = $"Partial real item icon coverage is active: {sampleCoveredCount}/{sampleEntries.Count} distinct resolved shop items in the current item_shop sample render with extracted PNGs. Remaining uncovered sample items: {uncoveredPreview}. Global coverage currently sits at {globalCoveredCount}/{loadedItemCatalog.EntryCount} item entries.";
            ShowItemIconCoverageWarning = true;
        }

        void RefreshBridgeStatusSummary(ShopSourceRow source)
        {
            if (source.Kind == ShopTableKind.Item)
            {
                BridgeStatusSummary = loadedItemCatalog == null
                    ? "Item labels are falling back to dictionary text because item.bin could not be loaded."
                    : $"Item catalog loaded: item.bin · {loadedItemCatalog.EntryCount} rows · {loadedItemCatalog.DistinctIconCount} distinct IconId values · {ShopItemIconAtlas.ProvedAssetCount} real item icon families wired in this build.";
                return;
            }

            BridgeStatusSummary = loadedGearCatalog == null
                ? "Catalog row selection is unavailable until shop_arms.bin is present."
                : $"Catalog bridge loaded: shop_arms.bin · {loadedGearCatalog.EntriesByIndex.Count} rows available · {ShopGearIconAtlas.ProvedAssetCount} real gear owner/type PNGs wired in this build.";
        }

        void RefreshPrimaryWarningSummary()
        {
            if (SelectedSource == null)
            {
                PrimaryWarningSummary = string.Empty;
                ShowPrimaryWarning = false;
                return;
            }

            if (SelectedSource.Kind == ShopTableKind.Item)
            {
                if (loadedItemCatalog == null)
                {
                    PrimaryWarningSummary = "Real item icon coverage cannot be proven until item.bin resolves cleanly in the loaded workspace.";
                    ShowPrimaryWarning = true;
                    return;
                }

                List<ShopItemCatalogEntry> sampleEntries = GetDistinctResolvedSampleItemEntries(loadedTable, loadedItemCatalog);
                int sampleCoveredCount = sampleEntries.Count(ShopItemIconAtlas.HasProvedAsset);
                if (sampleEntries.Count > 0 && sampleCoveredCount < sampleEntries.Count)
                {
                    PrimaryWarningSummary = $"Real item icon coverage is still partial in this sample: {sampleCoveredCount}/{sampleEntries.Count} distinct resolved shop items render a proved original PNG. Uncovered items keep an honest fallback badge.";
                    ShowPrimaryWarning = true;
                    return;
                }

                PrimaryWarningSummary = string.Empty;
                ShowPrimaryWarning = false;
                return;
            }

            if (loadedGearCatalog == null)
            {
                PrimaryWarningSummary = "Catalog row selection stays blocked until shop_arms.bin is present in the loaded workspace.";
                ShowPrimaryWarning = true;
                return;
            }

            PrimaryWarningSummary = string.Empty;
            ShowPrimaryWarning = false;
        }

        static List<ShopItemCatalogEntry> GetDistinctResolvedSampleItemEntries(ShopTable? table, ShopItemCatalog? itemCatalog)
        {
            if (table == null || itemCatalog == null)
                return [];

            Dictionary<int, ShopItemCatalogEntry> entriesByIndex = [];
            foreach (ShopSlotEntry slot in table.Entries.SelectMany(entry => entry.Slots))
            {
                if (slot.RawValue == 0)
                    continue;

                if (!ShopTable_File.TryResolveItemGameIndex(slot.RawValue, out ushort itemIndex, out _))
                    continue;

                if (!itemCatalog.EntriesByIndex.TryGetValue(itemIndex, out ShopItemCatalogEntry? entry))
                    continue;

                entriesByIndex[itemIndex] = entry;
            }

            return entriesByIndex.Values
                .OrderBy(entry => entry.Index)
                .ToList();
        }

        static string BuildUncoveredItemPreview(IEnumerable<ShopItemCatalogEntry> sampleEntries)
        {
            List<string> labels = sampleEntries
                .Where(entry => !ShopItemIconAtlas.HasProvedAsset(entry))
                .Select(entry => entry.DisplayLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            if (labels.Count == 0)
                return "none";

            return string.Join(", ", labels);
        }

        void RefreshCompactSurfaceState()
        {
            if (SelectedSource == null)
            {
                CompactSurfaceTitle = "Shop Slots";
                CompactSurfaceSummary = "Pick a shop row to inspect and edit its guarded slot payload.";
                CompactSurfaceGuardrail = "Only the proven slot payload slice is editable here.";
                return;
            }

            CompactSurfaceTitle = SelectedSource.Kind == ShopTableKind.Item
                ? "Item Shop Slots"
                : "Gear Shop Slots";

            if (SelectedShop == null || loadedTable == null)
            {
                CompactSurfaceSummary = SelectedSource.Kind == ShopTableKind.Item
                    ? "Choose a shop row, then edit only the 16 item slots as Item or Empty."
                    : "Choose a shop row, then edit only the 16 gear slots as Catalog Row or Empty.";
                CompactSurfaceGuardrail = SelectedSource.Kind == ShopTableKind.Item
                    ? "Main view stays compact. Structural proofs and raw contracts live in Advanced details."
                    : "Main view stays compact. Structural proofs and raw contracts live in Advanced details.";
                return;
            }

            ShopEntry? entry = loadedTable.Entries.FirstOrDefault(candidate => candidate.Index == SelectedShop.EntryIndex);
            if (entry == null)
                return;

            int filledCount = entry.Slots.Count(slot => slot.RawValue != 0);
            int pendingCount = entry.Slots.Count(slot => slot.RawValue != ReadBaselineSlotValue(loadedTable, entry.Index, slot.SlotIndex));
            CompactSurfaceSummary = pendingCount == 0
                ? $"{SelectedShop.IndexLabel} · {filledCount}/16 filled · no pending slot edits."
                : $"{SelectedShop.IndexLabel} · {filledCount}/16 filled · {pendingCount} pending slot edit(s).";
            CompactSurfaceGuardrail = SelectedSource.Kind == ShopTableKind.Item
                ? "Only the 16 slot values are editable as Item or Empty. Word 00h and every non-slot byte stay locked."
                : "Only the 16 slot values are editable as Catalog Row or Empty. This does not rename gear or edit gear models.";
        }

        static string BuildObservedEvidenceSummary(ShopSourceRow source, ShopTable table, ShopGearCatalog? gearCatalog)
        {
            List<ushort> distinctNonZero = table.Entries
                .SelectMany(entry => entry.Slots)
                .Select(slot => slot.RawValue)
                .Where(value => value != 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            List<int> categories = distinctNonZero
                .Select(value => (value & 0xF000) >> 12)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            if (source.Kind == ShopTableKind.Item)
            {
                int encodedMatches = distinctNonZero.Count(TryResolveItemGameIndex);
                if (distinctNonZero.Count == 0)
                    return "All visible item slots are zero in the current table, so there is no stronger mapping evidence yet.";

                if (categories.Count == 1 && categories[0] == (int)GameCategory_Enum.Items && encodedMatches == distinctNonZero.Count)
                {
                    return $"Current table evidence is stronger now: all {distinctNonZero.Count} distinct non-zero item slot values stay in game category Items (`0x2xxx`) and resolve cleanly through their low 12-bit indices. Encoded item game-index is now the strongest fit here; raw item-id is no longer the lead interpretation in the real sample.";
                }

                return $"Current table evidence is mixed: {encodedMatches} of {distinctNonZero.Count} distinct non-zero item slot values resolve cleanly as encoded item game indices, while the rest still need broader proof. Candidate wording stays deliberate until this survives more than the current sample set.";
            }

            if (distinctNonZero.Count == 0)
                return "All visible gear slots are zero in the current table, so there is no stronger catalog evidence yet.";

            if (categories.Count == 1 && categories[0] == 0)
            {
                if (gearCatalog != null)
                {
                    int inRangeMatches = distinctNonZero.Count(value => gearCatalog.EntriesByIndex.ContainsKey(value));

                    if (inRangeMatches == distinctNonZero.Count && HasExactNonZeroCatalogCoverage(distinctNonZero, gearCatalog))
                    {
                        return $"Current table evidence points at a raw catalog index rather than a categorized game index: all {distinctNonZero.Count} distinct non-zero gear slot values stay in category 0, all land inside the local shop_arms.bin catalog, and the current table covers every non-zero catalog index exactly as a slot candidate domain. That is strong mapping evidence for catalog shape, but not yet a production-safe naming or writing surface.";
                    }

                    if (inRangeMatches == distinctNonZero.Count)
                    {
                        return $"Current table evidence points at a raw catalog index rather than a categorized game index: all {distinctNonZero.Count} distinct non-zero gear slot values stay in category 0 and all {inRangeMatches} fall inside the local shop_arms.bin catalog range. The index bridge is stronger now, but trusted gear names/models and write safety are still missing.";
                    }

                    return $"Gear slot values stay in category 0 and {inRangeMatches} of {distinctNonZero.Count} distinct non-zero values land inside the local shop_arms.bin catalog. The catalog bridge is promising, but it is not complete enough for a writer or for production-safe labels yet.";
                }

                return $"Current table evidence points at a raw catalog index rather than a categorized game index: all {distinctNonZero.Count} distinct non-zero gear slot values stay in category 0. The external parser hints at BUYABLE_GEAR[idx], but this editor still lacks a trusted local gear catalog bridge, so the slots remain research-only.";
            }

            return "Gear slot values now show real table shape and distribution, but the catalog meaning is still not proven hard enough to unlock a writer or production labels.";
        }

        static string BuildSourceConfidenceSummary(ShopSourceRow source, ShopTable table, ShopGearCatalog? gearCatalog)
        {
            int totalSlots = table.Entries.Sum(entry => entry.Slots.Count);
            int nonZeroSlots = table.Entries.Sum(entry => entry.Slots.Count(slot => slot.RawValue != 0));
            int strongSlots = table.Entries.Sum(entry => entry.Slots.Count(slot =>
                string.Equals(slot.ConfidenceLabel, "Strong encoded-index candidate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slot.ConfidenceLabel, "Strong catalog candidate", StringComparison.OrdinalIgnoreCase)));

            if (source.Kind == ShopTableKind.Item)
            {
                return $"Structure is confirmed enough for the guarded writer slice: {totalSlots} fixed slots observed, {nonZeroSlots} non-zero, and {strongSlots} slots currently land in the strongest encoded-Items interpretation. Public production rollout still needs its own validation pass.";
            }

            string catalogClause = gearCatalog != null
                ? "Local shop_arms.bin bridge is present."
                : "Local shop_arms.bin bridge is absent.";

            return $"Structure is confirmed enough for the guarded writer slice: {totalSlots} fixed slots observed, {nonZeroSlots} non-zero, and {strongSlots} slots currently land in the strongest catalog-index interpretation. {catalogClause} Public production rollout still needs its own validation pass.";
        }

        static string BuildRoundTripSummary(ShopSourceRow source, ShopRoundTripReport roundTripReport, bool usedLocalGearCatalog)
        {
            string extraContext = source.Kind == ShopTableKind.Gear
                ? usedLocalGearCatalog
                    ? " Local shop_arms.bin bridge was available during verification."
                    : " Local shop_arms.bin bridge was not available during verification."
                : string.Empty;

            return roundTripReport.IsByteIdentical
                ? roundTripReport.Summary + " This only clears the no-edit structural gate, not an authoring gate." + extraContext
                : roundTripReport.Summary + extraContext;
        }

        static string BuildEditGateSummary(ShopEditGateReport report)
        {
            if (!report.GatePassed)
                return report.Summary;

            return report.Summary + $" Diff bytes observed: {report.DiffByteCount}. This is a private harness result, not a public editing approval.";
        }

        static bool TryResolveItemGameIndex(ushort rawValue)
        {
            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawValue);
                if (category != (byte)GameCategory_Enum.Items)
                    return false;

                ushort index = FfxCommon_Util.GetGameIndex(rawValue);
                string label = FfxCommon_Util.GetGameIndexName(category, index);
                return !string.IsNullOrWhiteSpace(label) && !string.Equals(label, "<NOT_INDEXED>", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        static bool HasExactNonZeroCatalogCoverage(IReadOnlyList<ushort> distinctNonZero, ShopGearCatalog gearCatalog)
        {
            int nonZeroCatalogEntries = gearCatalog.EntriesByIndex.Keys.Count(index => index != 0);
            if (distinctNonZero.Count != nonZeroCatalogEntries)
                return false;

            if (distinctNonZero.Count == 0 || distinctNonZero[0] != 0x0001)
                return false;

            int expectedLast = gearCatalog.EntriesByIndex.Keys.Max();
            if (distinctNonZero[^1] != expectedLast)
                return false;

            for (int i = 0; i < distinctNonZero.Count; i++)
            {
                if (distinctNonZero[i] != i + 1)
                    return false;
            }

            return true;
        }
    }

    internal sealed record ShopSourceRow(
        string Id,
        string DisplayName,
        string AbsolutePath,
        string RelativePath,
        ShopTableKind Kind,
        string Summary,
        string DetailSummary);

    internal sealed class ShopEntryRow
    {
        public required int EntryIndex { get; init; }
        public required string RecordId { get; init; }
        public required string IndexLabel { get; init; }
        public required string Title { get; init; }
        public required string PreviewSummary { get; init; }
        public required string Summary { get; init; }
        public required string Overview { get; init; }
        public required string HeaderSummary { get; init; }
        public required string ConfidenceSummary { get; init; }
        public required string FootprintSummary { get; init; }
        public required string OpaqueSummary { get; init; }
        public required string SearchText { get; init; }

        public static ShopEntryRow From(ShopSourceRow source, ShopTable table, ShopEntry entry, ShopItemCatalog? itemCatalog)
        {
            List<string> nonZeroPreview = entry.Slots
                .Where(slot => slot.RawValue != 0)
                .Take(3)
                .Select(slot => ShortLabel(source.Kind, slot.DisplayLabel, slot.RawValue, itemCatalog))
                .ToList();

            int nonZeroCount = entry.Slots.Count(slot => slot.RawValue != 0);
            int zeroCount = entry.Slots.Count - nonZeroCount;
            int distinctNonZero = entry.Slots.Where(slot => slot.RawValue != 0).Select(slot => slot.RawValue).Distinct().Count();
            int pendingCount = entry.Slots.Count(slot => slot.RawValue != ShopExplorer_DataModel.ReadBaselineSlotValue(table, entry.Index, slot.SlotIndex));
            List<int> categories = entry.Slots
                .Where(slot => slot.RawValue != 0)
                .Select(slot => (slot.RawValue & 0xF000) >> 12)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            string title = $"Shop {entry.Index:D2}";
            string previewSummary = nonZeroPreview.Count == 0
                ? "All 16 slots are empty in this row."
                : string.Join(" · ", nonZeroPreview);

            string confidenceSummary = source.Kind == ShopTableKind.Item
                ? "Item-or-empty writer slice only."
                : "Catalog-row-or-empty writer slice only.";

            string footprintSummary = $"{nonZeroCount} non-zero · {zeroCount} zero · {distinctNonZero} distinct non-zero · categories {(categories.Count == 0 ? "(none)" : string.Join(", ", categories.Select(value => $"0x{value:X}")))}";
            string pendingSummary = pendingCount == 0
                ? "No pending slot edits."
                : $"{pendingCount} pending slot edit(s) are staged in memory for this row.";

            return new ShopEntryRow
            {
                EntryIndex = entry.Index,
                RecordId = $"{source.Id}:{entry.Index}",
                IndexLabel = $"Shop {entry.Index:D2}",
                Title = title,
                PreviewSummary = previewSummary,
                Summary = pendingCount == 0
                    ? $"{nonZeroCount}/16 filled · no pending edits"
                    : $"{nonZeroCount}/16 filled · {pendingCount} pending",
                Overview = $"{source.RelativePath} · fixed row {entry.Index:D2} · 16 ushort slots preserved in place. Save path only touches slot payload bytes.",
                HeaderSummary = $"Header range {table.Header.MinIndex}..{table.Header.MaxIndex} · entry length 0x{table.Header.EntryLength:X2} · total rows {table.Header.EntryCount}.",
                ConfidenceSummary = confidenceSummary,
                FootprintSummary = footprintSummary,
                OpaqueSummary = source.Kind == ShopTableKind.Item
                    ? "The strongest fit in current samples is encoded item game indices in category Items, not raw item ids. Word 00h and every non-slot byte stay read-only."
                    : "The strongest fit in current samples is a raw buyable-gear catalog index, reinforced by the local shop_arms.bin bridge. Trusted gear names and models still remain intentionally out of scope.",
                SearchText = string.Join(" ", new[]
                {
                    source.DisplayName,
                    source.RelativePath,
                    entry.Index.ToString(),
                    entry.Index.ToString("X2"),
                    entry.UnusedPriceWord.ToString("X4"),
                    confidenceSummary,
                    footprintSummary,
                    pendingSummary,
                    string.Join(" ", entry.Slots.Select(slot => $"Slot {slot.SlotIndex:D2} {slot.RawValue:X4}h {slot.ConfidenceLabel} {slot.DisplayLabel} {slot.CandidateSummary}"))
                })
            };
        }

        static string ShortLabel(ShopTableKind kind, string displayLabel, ushort rawValue, ShopItemCatalog? itemCatalog)
        {
            if (rawValue == 0)
                return "Empty";

            if (kind == ShopTableKind.Item
                && ShopTable_File.TryResolveItemGameIndex(rawValue, out ushort itemIndex, out string? fallbackLabel))
            {
                if (itemCatalog != null && itemCatalog.EntriesByIndex.TryGetValue(itemIndex, out ShopItemCatalogEntry? catalogEntry))
                    return catalogEntry.DisplayLabel;

                return fallbackLabel ?? displayLabel;
            }

            int separatorIndex = displayLabel.IndexOf('·');
            if (separatorIndex >= 0 && separatorIndex + 1 < displayLabel.Length)
                return displayLabel[(separatorIndex + 1)..].Trim();

            return displayLabel.Trim();
        }
    }

    internal partial class ShopEditableSlotRow : ObservableObject
    {
        public required int SlotIndex { get; init; }
        public required string SlotLabel { get; init; }
        public required string RawValueLabel { get; init; }
        public required string PrimaryLabel { get; init; }
        public required string SecondaryLabel { get; init; }
        public required IReadOnlyList<ShopSlotFactChipRow> FactChips { get; init; }
        public required bool ShowFactChips { get; init; }
        public required string StateLabel { get; init; }
        public required Bitmap? VisualImage { get; init; }
        public required bool HasVisualImage { get; init; }
        public required bool ShowVisualBadgeText { get; init; }
        public required string VisualBadgeText { get; init; }
        public required string VisualBadgeTooltip { get; init; }
        public required string CoverageChipText { get; init; }
        public required bool ShowCoverageChip { get; init; }
        public required string ConfidenceLabel { get; init; }
        public required string DisplayLabel { get; init; }
        public required string DetailSummary { get; init; }
        public required string TooltipSummary { get; init; }
        public required string EditabilitySummary { get; init; }
        public required bool CanEdit { get; init; }
        public required bool IsPendingEdit { get; init; }
        public required IReadOnlyList<ShopSlotOptionRow> AvailableOptions { get; init; }

        // Read-only Spira Data Atlas evidence for this slot. Non-null only for a gear-shop slot whose CURRENT raw value
        // still matches the parser corpus (value-guarded). Null for item-shop, empty slots, and edited/mismatched gear
        // slots — the badge strip hides itself when this is null. Recomputed every time the slot row is rebuilt (which
        // happens on every edit), so an edit that changes the value drops the badge automatically.
        public AtlasEvidenceInfo? Evidence { get; init; }

        [ObservableProperty] private ShopSlotOptionRow? selectedOption;

        public static ShopEditableSlotRow From(
            ShopSourceRow source,
            ShopTable table,
            ShopEntry entry,
            ShopSlotEntry slot,
            IReadOnlyList<ShopSlotOptionRow> options,
            ShopItemCatalog? itemCatalog,
            ShopGearCatalog? gearCatalog,
            bool canEdit)
        {
            ushort baselineValue = ShopExplorer_DataModel.ReadBaselineSlotValue(table, entry.Index, slot.SlotIndex);
            bool isPendingEdit = slot.RawValue != baselineValue;
            string editabilitySummary = !canEdit
                ? "Read-only in the current context."
                : source.Kind == ShopTableKind.Item
                    ? "Editable as Item or Empty only. Word 00h and all non-slot bytes stay read-only."
                    : "Editable as Catalog Row or Empty only. Friendly gear naming stays advisory, not authoritative.";

            ShopItemCatalogEntry? itemEntry = ResolveItemCatalogEntry(slot, itemCatalog);
            ShopGearCatalogEntry? gearEntry = ResolveGearCatalogEntry(slot, gearCatalog);
            bool hasVisualImage = ShopItemIconAtlas.TryResolveBitmap(itemEntry, out Bitmap? visualImage)
                || ShopGearIconAtlas.TryResolveBitmap(gearEntry, out visualImage);
            string primaryLabel = ResolvePrimaryLabel(source.Kind, slot, itemEntry, gearEntry);
            string secondaryLabel = ResolveSecondaryLabel(source.Kind, slot, itemEntry, gearEntry, hasVisualImage);
            IReadOnlyList<ShopSlotFactChipRow> factChips = BuildFactChips(source.Kind, slot, gearEntry);
            string stateLabel = !canEdit
                ? "Read-only"
                : isPendingEdit
                    ? "Pending"
                    : slot.RawValue == 0
                        ? "Empty"
                        : "Editable";
            string visualBadgeText = BuildVisualBadgeText(source.Kind, slot, itemEntry, gearEntry);
            string visualBadgeTooltip = BuildVisualBadgeTooltip(source.Kind, slot, itemEntry, gearEntry, hasVisualImage);
            string coverageChipText = BuildCoverageChipText(source.Kind, slot, itemEntry, gearEntry, hasVisualImage);
            string tooltipSummary = BuildTooltipSummary(source.Kind, slot, itemEntry, gearEntry, editabilitySummary, hasVisualImage);

            // Value-guarded read-only Atlas evidence per shop kind. TryGetShopGearSlot / TryGetShopItemSlot resolve the
            // corpus reward for this (bank, slot) only when the slot still holds the recorded raw on-disk value (and is
            // non-empty), so editing the slot makes this null and the strip hides — no stale badge. Each kind keys its
            // own corpus dict (gear_reward_shop_crosslink.csv / item_shop_command_crosslink.csv) and detail namespace (atlas:gear / item-shop);
            // an empty slot (RawValue 0) short-circuits in the accessor.
            AtlasEvidenceInfo? evidence = source.Kind switch
            {
                ShopTableKind.Gear when SpiraDataAtlasCatalog.TryGetShopGearSlot(entry.Index, slot.SlotIndex, slot.RawValue, out SpiraDataAtlasDetailEntry? gearDetail) && gearDetail != null
                    => AtlasEvidenceInfo.ForDetail(gearDetail),
                ShopTableKind.Item when SpiraDataAtlasCatalog.TryGetShopItemSlot(entry.Index, slot.SlotIndex, slot.RawValue, out SpiraDataAtlasDetailEntry? itemDetail) && itemDetail != null
                    => AtlasEvidenceInfo.ForDetail(itemDetail),
                _ => null,
            };

            return new ShopEditableSlotRow
            {
                SlotIndex = slot.SlotIndex,
                SlotLabel = $"Slot {slot.SlotIndex:D2}",
                RawValueLabel = $"{slot.RawValue:X4}h",
                PrimaryLabel = primaryLabel,
                SecondaryLabel = secondaryLabel,
                FactChips = factChips,
                ShowFactChips = factChips.Count > 0,
                StateLabel = stateLabel,
                VisualImage = hasVisualImage ? visualImage : null,
                HasVisualImage = hasVisualImage,
                ShowVisualBadgeText = !hasVisualImage,
                VisualBadgeText = visualBadgeText,
                VisualBadgeTooltip = visualBadgeTooltip,
                CoverageChipText = coverageChipText,
                ShowCoverageChip = !string.IsNullOrWhiteSpace(coverageChipText),
                ConfidenceLabel = slot.ConfidenceLabel,
                DisplayLabel = slot.DisplayLabel,
                DetailSummary = slot.CandidateSummary,
                TooltipSummary = tooltipSummary,
                EditabilitySummary = isPendingEdit
                    ? $"{editabilitySummary} Pending raw change vs disk baseline: {baselineValue:X4}h -> {slot.RawValue:X4}h."
                    : editabilitySummary,
                CanEdit = canEdit,
                IsPendingEdit = isPendingEdit,
                AvailableOptions = options,
                Evidence = evidence,
                SelectedOption = options.FirstOrDefault(option => option.RawValue == slot.RawValue) ?? options.FirstOrDefault()
            };
        }

        static ShopItemCatalogEntry? ResolveItemCatalogEntry(ShopSlotEntry slot, ShopItemCatalog? itemCatalog)
        {
            if (slot.RawValue == 0
                || itemCatalog == null
                || !ShopTable_File.TryResolveItemGameIndex(slot.RawValue, out ushort itemIndex, out _)
                || !itemCatalog.EntriesByIndex.TryGetValue(itemIndex, out ShopItemCatalogEntry? entry))
            {
                return null;
            }

            return entry;
        }

        static ShopGearCatalogEntry? ResolveGearCatalogEntry(ShopSlotEntry slot, ShopGearCatalog? gearCatalog)
        {
            if (slot.RawValue == 0
                || gearCatalog == null
                || !gearCatalog.EntriesByIndex.TryGetValue(slot.RawValue, out ShopGearCatalogEntry? entry))
            {
                return null;
            }

            return entry;
        }

        static string ResolvePrimaryLabel(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry)
        {
            if (slot.RawValue == 0)
                return "Empty slot";

            if (kind == ShopTableKind.Item
                && ShopTable_File.TryResolveItemGameIndex(slot.RawValue, out _, out string? fallbackLabel))
            {
                if (itemEntry != null)
                    return itemEntry.DisplayLabel;

                return fallbackLabel ?? slot.DisplayLabel;
            }

            if (kind == ShopTableKind.Gear
                && gearEntry != null)
            {
                return $"{gearEntry.Equipment.Character} {FormatGearTitleType(gearEntry.Equipment.Type)}";
            }

            int separatorIndex = slot.DisplayLabel.IndexOf('·');
            if (separatorIndex >= 0 && separatorIndex + 1 < slot.DisplayLabel.Length)
                return slot.DisplayLabel[(separatorIndex + 1)..].Trim();

            return slot.DisplayLabel.Trim();
        }

        static string ResolveSecondaryLabel(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry, bool hasVisualImage)
        {
            if (slot.RawValue == 0)
                return "Writes 0000h in this slot.";

            if (kind == ShopTableKind.Item
                && itemEntry != null)
            {
                string description = itemEntry.Description?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(description)
                    ? "Item description unavailable in the loaded item catalog."
                    : description;
            }

            if (kind == ShopTableKind.Gear
                && gearEntry != null)
            {
                string descriptor = ResolveGearDescriptor(gearEntry);
                return hasVisualImage
                    ? $"Original {descriptor} icon wired."
                    : $"{descriptor} icon still unresolved.";
            }

            return slot.ConfidenceLabel;
        }

        static IReadOnlyList<ShopSlotFactChipRow> BuildFactChips(ShopTableKind kind, ShopSlotEntry slot, ShopGearCatalogEntry? gearEntry)
        {
            if (kind != ShopTableKind.Gear || slot.RawValue == 0 || gearEntry == null)
                return Array.Empty<ShopSlotFactChipRow>();

            List<ShopSlotFactChipRow> chips = new();
            string? fingerprint = TryExtractGearFingerprint(gearEntry.DisplayLabel);
            if (!string.IsNullOrWhiteSpace(fingerprint))
                chips.Add(new ShopSlotFactChipRow(fingerprint));

            chips.Add(new ShopSlotFactChipRow($"Formula {gearEntry.Equipment.Dmg_formula}"));
            chips.Add(new ShopSlotFactChipRow($"Power {gearEntry.Equipment.Power}"));
            chips.Add(new ShopSlotFactChipRow($"Crit {gearEntry.Equipment.Crit_bonus}"));
            chips.Add(new ShopSlotFactChipRow($"{gearEntry.Equipment.Slot_count} slot{(gearEntry.Equipment.Slot_count == 1 ? string.Empty : "s")}"));
            return chips;
        }

        static string BuildVisualBadgeText(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry)
        {
            if (slot.RawValue == 0)
                return "--";

            if (kind == ShopTableKind.Item
                && itemEntry != null)
            {
                return $"{itemEntry.IconId:X2}";
            }

            if (kind == ShopTableKind.Gear
                && gearEntry != null)
            {
                return gearEntry.Equipment.Type == FFXProjectEditor.FfxLib.Common.EquipmentStruct.EquipmentType_Enum.Armor ? "AR" : "WP";
            }

            return kind == ShopTableKind.Item ? "??" : "GE";
        }

        static string BuildVisualBadgeTooltip(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry, bool hasVisualImage)
        {
            if (slot.RawValue == 0)
                return "Empty slot. No icon or gear badge applies.";

            if (kind == ShopTableKind.Item
                && itemEntry != null)
            {
                return hasVisualImage
                    ? $"Workshop-proved real extracted item icon is shown here. item.bin still exposes IconId {itemEntry.IconId:X2}h as supporting evidence."
                    : $"Item resolves through item.bin IconId {itemEntry.IconId:X2}h, but no extracted real PNG is wired for this entry yet.";
            }

            if (kind == ShopTableKind.Gear
                && gearEntry != null)
            {
                return hasVisualImage
                    ? $"Workshop-proved real {ResolveGearDescriptor(gearEntry)} icon is shown here. This is the original menu symbol for this owner/type lane, not a unique per-gear sprite."
                    : $"{ResolveGearDescriptor(gearEntry)} icon is still blocked here.";
            }

            return kind == ShopTableKind.Item
                ? "Original item icon proof is still blocked for this slot."
                : "No trustworthy gear icon bridge is exposed for this slot.";
        }

        static string BuildCoverageChipText(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry, bool hasVisualImage)
        {
            if (kind == ShopTableKind.Item && slot.RawValue != 0 && itemEntry != null && !hasVisualImage)
                return "Icon uncovered";

            if (kind == ShopTableKind.Gear && slot.RawValue != 0 && gearEntry != null && !hasVisualImage)
                return "Owner icon blocked";

            return string.Empty;
        }

        static string BuildTooltipSummary(ShopTableKind kind, ShopSlotEntry slot, ShopItemCatalogEntry? itemEntry, ShopGearCatalogEntry? gearEntry, string editabilitySummary, bool hasVisualImage)
        {
            List<string> lines =
            [
                $"{slot.RawValue:X4}h · {slot.ConfidenceLabel}",
                slot.CandidateSummary,
                editabilitySummary
            ];

            if (kind == ShopTableKind.Item && itemEntry != null)
            {
                lines.Insert(1, hasVisualImage
                    ? $"item.bin · icon {itemEntry.IconId:X2}h · real PNG is wired in this build."
                    : $"item.bin · icon {itemEntry.IconId:X2}h · no real PNG is wired yet.");
                lines.Insert(2, itemEntry.Description);
            }

            if (kind == ShopTableKind.Gear
                && gearEntry != null)
            {
                lines.Insert(1, hasVisualImage
                    ? $"shop_arms.bin · real {ResolveGearDescriptor(gearEntry)} icon is wired in this build."
                    : $"shop_arms.bin · real {ResolveGearDescriptor(gearEntry)} icon proof is still blocked here.");
                lines.Insert(1, gearEntry.DetailSummary);
            }

            return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        static string FormatGearTypeLabel(ShopGearCatalogEntry gearEntry)
        {
            return gearEntry.Equipment.Type == EquipmentStruct.EquipmentType_Enum.Armor
                ? "armor-type"
                : "weapon-type";
        }

        static string BuildShortGearStatsSummary(ShopGearCatalogEntry gearEntry)
        {
            return $"formula {gearEntry.Equipment.Dmg_formula} · power {gearEntry.Equipment.Power} · crit {gearEntry.Equipment.Crit_bonus} · slots {gearEntry.Equipment.Slot_count}";
        }

        static string ResolveGearDescriptor(ShopGearCatalogEntry gearEntry)
        {
            return ShopGearIconAtlas.TryResolveDescriptor(gearEntry, out string descriptor)
                ? descriptor
                : $"{gearEntry.Equipment.Character} {FormatGearTitleType(gearEntry.Equipment.Type).ToLowerInvariant()}";
        }

        static string FormatGearTitleType(EquipmentStruct.EquipmentType_Enum type)
        {
            return type == EquipmentStruct.EquipmentType_Enum.Armor ? "Armor" : "Weapon";
        }

        static string? TryExtractGearFingerprint(string displayLabel)
        {
            int separatorIndex = displayLabel.IndexOf('·');
            if (separatorIndex < 0 || separatorIndex + 1 >= displayLabel.Length)
                return null;

            string fingerprint = displayLabel[(separatorIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint;
        }
    }

    internal sealed class ShopSlotFactChipRow
    {
        public ShopSlotFactChipRow(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    internal sealed class ShopSlotOptionRow
    {
        public ShopSlotOptionRow(ushort rawValue, string display, string detail)
        {
            RawValue = rawValue;
            Display = display;
            Detail = detail;
        }

        public ushort RawValue { get; }
        public string Display { get; }
        public string Detail { get; }
    }
}
