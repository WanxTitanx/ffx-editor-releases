using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class PresentationContainerBrowser_DataModel : ObservableObject
    {
        readonly List<Ps2PresentationContainerRecord> allRecords = [];

        public ObservableCollection<Ps2PresentationContainerRecord> Records { get; } = new();
        public ObservableCollection<string> ExtensionOptions { get; } = new(["All"]);
        public ObservableCollection<string> CohortOptions { get; } = new(["All"]);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedPath))]
        [NotifyPropertyChangedFor(nameof(SelectedSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedSignature))]
        [NotifyPropertyChangedFor(nameof(SelectedHeadHex))]
        [NotifyPropertyChangedFor(nameof(SelectedCompanions))]
        [NotifyPropertyChangedFor(nameof(SelectedEvidence))]
        private Ps2PresentationContainerRecord? selectedRecord;

        [ObservableProperty]
        private string selectedExtensionFilter = "All";

        [ObservableProperty]
        private string selectedCohortFilter = "All";

        [ObservableProperty]
        private string searchText = string.Empty;

        public string HeaderSummary => "Read-only browser for .vpa/.ebp/.omd/.sps2. Cohorts and signatures are strong; deep semantics stay blocked.";
        public string SourceRootSummary { get; private set; } = "Detecting ffx_ps2 root...";
        public string OverviewSummary { get; private set; } = "-";
        public string SelectedTitle => SelectedRecord?.Name ?? "Select a presentation container";
        public string SelectedPath => SelectedRecord?.RelativePath ?? "-";
        public string SelectedSummary => SelectedRecord == null ? "-" : $"{SelectedRecord.Domain} · {SelectedRecord.Cohort} · {SelectedRecord.SizeSummary}";
        public string SelectedSignature => SelectedRecord?.Signature ?? "-";
        public string SelectedHeadHex => SelectedRecord?.First16Hex ?? "-";
        public string SelectedCompanions => SelectedRecord?.CompanionSummary ?? "-";
        public string SelectedEvidence => SelectedRecord == null ? "-" : $"{SelectedRecord.EvidenceLabel.ToUpperInvariant()} · {SelectedRecord.BlockedReason}";

        public PresentationContainerBrowser_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            allRecords.Clear();
            Records.Clear();
            ExtensionOptions.Clear();
            CohortOptions.Clear();
            ExtensionOptions.Add("All");
            CohortOptions.Add("All");

            string? ffxPs2Root = Project_Service.Instance.Path_FfxPs2Root;
            if (string.IsNullOrWhiteSpace(ffxPs2Root))
            {
                SourceRootSummary = "FFX PS2 root was not resolved from the current project.";
                OverviewSummary = "-";
                SelectedRecord = null;
                return;
            }

            SourceRootSummary = ffxPs2Root;
            IReadOnlyList<Ps2PresentationContainerRecord> inventory = Ps2PresentationContainerReader.Scan(ffxPs2Root);
            allRecords.AddRange(inventory);

            foreach (string ext in allRecords.Select(record => record.Extension).Distinct().OrderBy(value => value))
                ExtensionOptions.Add(ext);
            foreach (string cohort in allRecords.Select(record => record.Cohort).Distinct().OrderBy(value => value))
                CohortOptions.Add(cohort);

            OverviewSummary = string.Join(" · ", allRecords
                .GroupBy(record => record.Extension)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key} {group.Count():N0}"));
            ApplyFilters();
        }

        partial void OnSelectedExtensionFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedCohortFilterChanged(string value) => ApplyFilters();
        partial void OnSearchTextChanged(string value) => ApplyFilters();

        void ApplyFilters()
        {
            IEnumerable<Ps2PresentationContainerRecord> query = allRecords;

            if (!string.Equals(SelectedExtensionFilter, "All", System.StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.Extension, SelectedExtensionFilter, System.StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedCohortFilter, "All", System.StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.Cohort, SelectedCohortFilter, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(record =>
                    record.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                    || record.RelativePath.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                    || record.Cohort.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                    || record.Domain.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
            }

            Replace(Records, query.OrderBy(record => record.RelativePath, System.StringComparer.OrdinalIgnoreCase).ToList());
            SelectedRecord = Records.FirstOrDefault();
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }
}
