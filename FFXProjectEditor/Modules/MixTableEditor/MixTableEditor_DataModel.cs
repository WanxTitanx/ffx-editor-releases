using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Battle;
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

namespace FFXProjectEditor.Modules.MixTableEditor
{
    internal partial class MixTableEditor_DataModel : ObservableObject
    {
        MixTable? loadedTable;

        public ObservableCollection<MixOriginRow> LoadedOrigins { get; } = new();
        public ObservableCollection<MixOriginRow> DisplayedOrigins { get; } = new();
        public ObservableCollection<MixResultRow> LoadedResults { get; } = new();
        public ObservableCollection<MixResultRow> DisplayedResults { get; } = new();

        public int OriginCount => LoadedOrigins.Count;
        public int TotalNonEmptyResults => LoadedOrigins.Sum(o => o.NonEmptyCount);
        public string CoveragePercent
        {
            get
            {
                if (LoadedOrigins.Count == 0) return "0%";
                int totalSlots = LoadedOrigins.Count * 112;
                if (totalSlots == 0) return "0%";
                double pct = (double)TotalNonEmptyResults / totalSlots * 100.0;
                return $"{pct:F1}%";
            }
        }

        [ObservableProperty] private string originFilterText = string.Empty;
        [ObservableProperty] private string resultFilterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading prepare.bin...";
        [ObservableProperty] private string scopeSummary = "Safe scope: edit existing mix results only. Jarvis preserves the original 112x112 matrix shape and rewrites only the proven 16-bit result fields.";
        [ObservableProperty] private string selectedOriginSummary = "Select a base mix item to inspect its result matrix.";
        [ObservableProperty] private string selectedResultSummary = "Select a partner item to edit a specific mix outcome.";
        [ObservableProperty] private MixOriginRow? selectedOrigin;
        [ObservableProperty] private MixResultRow? selectedResult;
        [ObservableProperty] private AtlasEvidenceInfo? selectedMixEvidence;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public List<string> CategoryOptions => new GameCategory_Converter().Options.Values.ToList();

        public MixTableEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnOriginFilterTextChanged(string value) => ApplyOriginFilter();
        partial void OnResultFilterTextChanged(string value) => ApplyResultFilter();

        partial void OnSelectedOriginChanged(MixOriginRow? value)
        {
            SelectedOriginSummary = value == null
                ? "Select a base mix item to inspect its result matrix."
                : $"{value.OriginLabel} ({value.OriginRawGameIndex:X4}h) · {value.NonEmptyCount} non-empty combinations.";

            LoadResultsForSelectedOrigin(value, SelectedResult?.PartnerIndex);
        }

        partial void OnSelectedResultChanged(MixResultRow? value)
        {
            SelectedResultSummary = value == null
                ? "Select a partner item to edit a specific mix outcome."
                : value.Formula;

            RefreshSelectedMixEvidence();
        }

        // Read-only Spira Data Atlas evidence for the selected mix combination, shown only when a stable crosslink
        // exists. prepare.bin is a lower-triangular 112x112 matrix, so each unordered item pair {a,b} maps to one
        // canonical corpus row; TryGetMixCombination reconstructs the id from (origin, partner) and value-guards on the
        // current RawResult. Editing the outcome (or an empty/upper-triangle cell with raw 0, or a pair the corpus never
        // recorded) breaks the match, the accessor returns null, and the strip hides itself — no guessing, no stale badge.
        void RefreshSelectedMixEvidence()
        {
            MixOriginRow? origin = SelectedOrigin;
            MixResultRow? result = SelectedResult;
            SelectedMixEvidence = origin != null
                && result != null
                && SpiraDataAtlasCatalog.TryGetMixCombination(origin.Index, result.PartnerIndex, result.RawResult, out SpiraDataAtlasDetailEntry? detail)
                && detail != null
                    ? AtlasEvidenceInfo.ForDetail(detail)
                    : null;
        }

        public void RefreshFromDisk() => LoadFromDisk();
        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        public void ClearSelectedResult()
        {
            if (SelectedResult == null)
                return;

            SelectedResult.SetEmpty();
        }

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedTable = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearAll("Project root not loaded.");
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelPrepare))
            {
                ClearAll("jppc/battle/kernel/prepare.bin not found in the loaded workspace.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelPrepare);
            loadedTable = MixTable_File.Read(bytes);
            LoadOrigins(loadedTable, SelectedOrigin?.Index);

            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                "mix table",
                BuildFile());
        }

        void ClearAll(string message)
        {
            ClearOrigins();
            ClearResults();
            LoadSummary = message;
        }

        void ClearOrigins()
        {
            LoadedOrigins.Clear();
            DisplayedOrigins.Clear();
            SelectedOrigin = null;
            SelectedOriginSummary = "Select a base mix item to inspect its result matrix.";
        }

        void ClearResults()
        {
            foreach (MixResultRow row in LoadedResults)
                row.PropertyChanged -= ResultChanged;

            LoadedResults.Clear();
            DisplayedResults.Clear();
            SelectedResult = null;
            SelectedResultSummary = "Select a partner item to edit a specific mix outcome.";
        }

        void LoadOrigins(MixTable table, int? preserveSelectionIndex)
        {
            LoadedOrigins.Clear();
            DisplayedOrigins.Clear();

            foreach (MixOriginEntry entry in table.Entries)
                LoadedOrigins.Add(MixOriginRow.Wrap(entry));

            ApplyOriginFilter();
            SelectedOrigin = preserveSelectionIndex.HasValue
                ? LoadedOrigins.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedOrigins.FirstOrDefault();

            LoadSummary = $"Loaded {LoadedOrigins.Count} mix origins from prepare.bin. Each origin carries {table.Header.EntryLength / 2} partner result slots.";
        }

        void LoadResultsForSelectedOrigin(MixOriginRow? origin, int? preservePartnerIndex)
        {
            ClearResults();

            if (origin == null)
                return;

            for (int partnerIndex = 0; partnerIndex < origin.Results.Length; partnerIndex++)
            {
                MixResultRow row = MixResultRow.Wrap(origin, partnerIndex);
                row.PropertyChanged += ResultChanged;
                LoadedResults.Add(row);
            }

            ApplyResultFilter();
            SelectedResult = preservePartnerIndex.HasValue
                ? LoadedResults.FirstOrDefault(row => row.PartnerIndex == preservePartnerIndex.Value)
                : LoadedResults.FirstOrDefault();
        }

        void ApplyOriginFilter()
        {
            DisplayedOrigins.Clear();
            string normalized = OriginFilterText.Trim();

            foreach (MixOriginRow row in LoadedOrigins)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedOrigins.Add(row);
            }

            if (SelectedOrigin != null && !DisplayedOrigins.Contains(SelectedOrigin))
                SelectedOrigin = DisplayedOrigins.FirstOrDefault();
        }

        void ApplyResultFilter()
        {
            DisplayedResults.Clear();
            string normalized = ResultFilterText.Trim();

            foreach (MixResultRow row in LoadedResults)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedResults.Add(row);
            }

            if (SelectedResult != null && !DisplayedResults.Contains(SelectedResult))
                SelectedResult = DisplayedResults.FirstOrDefault();
        }

        byte[] BuildFile()
        {
            if (loadedTable == null)
                return Array.Empty<byte>();

            loadedTable = new MixTable
            {
                OriginalBytes = loadedTable.OriginalBytes,
                Header = loadedTable.Header,
                Entries = LoadedOrigins.Select(row => row.ToEntry()).ToList()
            };

            return MixTable_File.Write(loadedTable);
        }

        void RestoreFromBytes(byte[] bytes)
        {
            int? preserveOriginIndex = SelectedOrigin?.Index;
            int? preservePartnerIndex = SelectedResult?.PartnerIndex;

            loadedTable = MixTable_File.Read(bytes);
            LoadOrigins(loadedTable, preserveOriginIndex);

            if (SelectedOrigin != null)
                LoadResultsForSelectedOrigin(SelectedOrigin, preservePartnerIndex);
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelPrepare, bytes);
            loadedTable = MixTable_File.Read(bytes);
        }

        void ResultChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (SelectedOrigin != null)
                SelectedOriginSummary = $"{SelectedOrigin.OriginLabel} ({SelectedOrigin.OriginRawGameIndex:X4}h) · {SelectedOrigin.NonEmptyCount} non-empty combinations.";

            if (sender is MixResultRow row && ReferenceEquals(row, SelectedResult))
            {
                SelectedResultSummary = row.Formula;

                // Re-evaluate the read-only Atlas badge when the selected combination's result word changes, so editing
                // the outcome (or clearing it) hides/updates the evidence instead of leaving a stale reward badge.
                if (e.PropertyName == nameof(MixResultRow.RawResult))
                    RefreshSelectedMixEvidence();
            }
        }

        internal sealed class MixOriginRow : ObservableObject
        {
            public required int Index { get; init; }
            public required ushort OriginRawGameIndex { get; init; }
            public required string OriginLabel { get; init; }
            public required byte[] RawBytes { get; init; }
            public required ushort[] Results { get; init; }

            public string OriginCode => $"{OriginRawGameIndex:X4}h";
            public int NonEmptyCount => Results.Count(result => result != 0);
            public string Summary => $"{NonEmptyCount} non-empty combinations";
            public double NonEmptyBarWidth => Math.Min(200.0, NonEmptyCount * 200.0 / 112.0);
            public string SearchBlob => $"{Index:D3} {OriginCode} {OriginLabel} {Summary}";

            public static MixOriginRow Wrap(MixOriginEntry entry)
            {
                return new MixOriginRow
                {
                    Index = entry.Index,
                    OriginRawGameIndex = entry.OriginRawGameIndex,
                    OriginLabel = entry.OriginLabel,
                    RawBytes = entry.RawBytes.ToArray(),
                    Results = entry.Results.ToArray()
                };
            }

            public MixOriginEntry ToEntry()
            {
                return new MixOriginEntry
                {
                    Index = Index,
                    OriginRawGameIndex = OriginRawGameIndex,
                    OriginLabel = OriginLabel,
                    RawBytes = RawBytes.ToArray(),
                    Results = Results.ToArray()
                };
            }

            public void NotifySummaryChanged()
            {
                OnPropertyChanged(nameof(NonEmptyCount));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(NonEmptyBarWidth));
                OnPropertyChanged(nameof(SearchBlob));
            }
        }

        internal sealed class MixResultRow : ObservableObject
        {
            readonly MixOriginRow owner;

            public required int PartnerIndex { get; init; }
            public required ushort PartnerRawGameIndex { get; init; }
            public required string PartnerLabel { get; init; }
            public required GameIndex_Wrapper ResultRef { get; init; }

public string PartnerCode => $"{PartnerRawGameIndex:X4}h";
            public ushort RawResult => ResultRef.Unwrap();
            public string ResultCode => $"{RawResult:X4}h";
            public bool IsEmpty => RawResult == 0;
            public string ResultLabel => MixTable_File.ResolveGameLabel(RawResult);
            public string Formula => $"{owner.OriginLabel} + {PartnerLabel} = {ResultLabel}";
            public string SearchBlob => $"{PartnerIndex:D3} {PartnerCode} {PartnerLabel} {ResultCode} {ResultLabel}";

            MixResultRow(MixOriginRow owner)
            {
                this.owner = owner;
            }

            public static MixResultRow Wrap(MixOriginRow owner, int partnerIndex)
            {
                ushort result = owner.Results[partnerIndex];
                MixResultRow row = new(owner)
                {
                    PartnerIndex = partnerIndex,
                    PartnerRawGameIndex = MixTable_File.ResolveItemGameIndex(partnerIndex),
                    PartnerLabel = MixTable_File.ResolveGameLabel(MixTable_File.ResolveItemGameIndex(partnerIndex)),
                    ResultRef = GameIndex_Wrapper.Wrap(result)
                };

                row.ResultRef.PropertyChanged += row.NestedResultChanged;
                return row;
            }

            public void SetEmpty()
            {
                ResultRef.Category = 0;
                ResultRef.Index = 0;
                SyncBack();
            }

            void NestedResultChanged(object? sender, PropertyChangedEventArgs e)
            {
                SyncBack();
            }

            void SyncBack()
            {
                owner.Results[PartnerIndex] = ResultRef.Unwrap();
                owner.NotifySummaryChanged();
                NotifyComputedChanged();
            }

void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(RawResult));
                OnPropertyChanged(nameof(ResultCode));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ResultLabel));
                OnPropertyChanged(nameof(Formula));
                OnPropertyChanged(nameof(SearchBlob));
            }
        }
    }
}
