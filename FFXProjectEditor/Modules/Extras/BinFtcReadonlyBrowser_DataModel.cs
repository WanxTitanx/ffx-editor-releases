using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class BinFtcReadonlyBrowser_DataModel : ObservableObject
    {
        public ObservableCollection<Ps2BinFtcBucketSummary> Buckets { get; } = new();
        public ObservableCollection<Ps2BinFtcRecord> Files { get; } = new();
        public ObservableCollection<string> RootOptions { get; } = new(["All"]);
        public ObservableCollection<string> ClassOptions { get; } = new(["All"]);
        public ObservableCollection<string> ExtensionOptions { get; } = new(["All", ".bin", ".ftc"]);
        public ObservableCollection<string> SensitivityOptions { get; } = new(["All", "Dangerous", "Sensitive"]);

        IReadOnlyList<Ps2BinFtcRecord> allRecords = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedBucketSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedFileName))]
        [NotifyPropertyChangedFor(nameof(SelectedFilePath))]
        [NotifyPropertyChangedFor(nameof(SelectedFileClass))]
        [NotifyPropertyChangedFor(nameof(SelectedFileBucket))]
        [NotifyPropertyChangedFor(nameof(SelectedFileHeader))]
        [NotifyPropertyChangedFor(nameof(SelectedFileDwords))]
        [NotifyPropertyChangedFor(nameof(SelectedFileSidecar))]
        [NotifyPropertyChangedFor(nameof(SelectedSignatureGroup))]
        [NotifyPropertyChangedFor(nameof(SelectedFtcHeaderInspector))]
        [NotifyPropertyChangedFor(nameof(SelectedSidecarGraph))]
        [NotifyPropertyChangedFor(nameof(SelectedFileEvidence))]
        [NotifyPropertyChangedFor(nameof(SelectedFileSensitivity))]
        [NotifyPropertyChangedFor(nameof(SelectedFileWarnings))]
        private Ps2BinFtcRecord? selectedFile;

        [ObservableProperty]
        private Ps2BinFtcBucketSummary? selectedBucket;

        [ObservableProperty]
        private string selectedRootFilter = "All";

        [ObservableProperty]
        private string selectedClassFilter = "All";

        [ObservableProperty]
        private string selectedExtensionFilter = "All";

        [ObservableProperty]
        private string selectedSensitivityFilter = "All";

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string sourceRootSummary = "Detecting master root...";

        public string HeaderSummary => "Read-only BIN/FTC atlas: bucket, signature, sidecar, and sensitivity first. No parser-final fantasy, no write path, no silent promotion.";
        public string OverviewSummary { get; private set; } = "-";
        public string SelectedBucketSummary => SelectedBucket == null
            ? "Select a bucket to narrow the atlas."
            : $"{SelectedBucket.Bucket} · {SelectedBucket.Count} files · {SelectedBucket.EvidenceLabel.ToUpperInvariant()} · {SelectedBucket.SensitivityLabel}";
        public string SelectedFileName => SelectedFile?.Name ?? "Select one BIN or FTC file";
        public string SelectedFilePath => SelectedFile?.RelativePath ?? "-";
        public string SelectedFileClass => SelectedFile == null ? "-" : $"{SelectedFile.Root} / {SelectedFile.ClassName} / {SelectedFile.Extension}";
        public string SelectedFileBucket => SelectedFile?.Bucket ?? "-";
        public string SelectedFileHeader => SelectedFile?.First16Hex ?? "-";
        public string SelectedFileDwords => SelectedFile?.DwordSummary ?? "-";
        public string SelectedFileSidecar => SelectedFile?.SidecarSummary ?? "-";
        public string SelectedSignatureGroup => SelectedFile?.SignatureGroup ?? "-";
        public string SelectedFtcHeaderInspector
        {
            get
            {
                if (SelectedFile == null)
                    return "-";

                Ps2BinFtcRecord? ftcRecord = ResolveFtcInspectorRecord();
                if (ftcRecord == null)
                    return "No FTC lane is attached to this selection.";

                return $"{ftcRecord.FtcMagic} · {ftcRecord.First16Hex}";
            }
        }
        public string SelectedSidecarGraph
        {
            get
            {
                if (SelectedFile == null)
                    return "-";

                Ps2BinFtcRecord? pair = ResolvePairRecord();
                if (pair == null)
                    return $"{SelectedFile.RelativePath}{Environment.NewLine}(no same-basename sidecar pair detected)";

                return $"{SelectedFile.RelativePath}{Environment.NewLine}<->{Environment.NewLine}{pair.RelativePath}";
            }
        }
        public string SelectedFileEvidence => SelectedFile == null ? "-" : $"{SelectedFile.EvidenceLabel.ToUpperInvariant()} · {SelectedFile.WriteStatus}";
        public string SelectedFileSensitivity => SelectedFile?.SensitivityLabel ?? "-";
        public string SelectedFileWarnings => SelectedFile?.SensitivityBanner ?? "-";

        public BinFtcReadonlyBrowser_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            string? masterRoot = Project_Service.Instance.ProjectPath;
            if (string.IsNullOrWhiteSpace(masterRoot))
            {
                SourceRootSummary = "Master root is not available.";
                allRecords = [];
                Buckets.Clear();
                Files.Clear();
                OverviewSummary = "-";
                return;
            }

            SourceRootSummary = masterRoot;
            Ps2BinFtcAtlasSnapshot snapshot = Ps2BinFtcAtlas.Scan(masterRoot);
            allRecords = snapshot.Records;

            Replace(Buckets, snapshot.BucketSummaries);
            Replace(RootOptions, (new List<string> { "All" }).Concat(allRecords.Select(record => record.Root).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToList());
            Replace(ClassOptions, (new List<string> { "All" }).Concat(allRecords.Select(record => record.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToList());

            OverviewSummary = $"{snapshot.BinCount:N0} BIN · {snapshot.FtcCount:N0} FTC · {snapshot.FtcxCount:N0} FTCX · {snapshot.NonFtcxCount:N0} non-FTCX · {snapshot.PairedCount:N0} paired";
            ApplyFilters();
        }

        partial void OnSelectedBucketChanged(Ps2BinFtcBucketSummary? value) => ApplyFilters();
        partial void OnSelectedRootFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedClassFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedExtensionFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedSensitivityFilterChanged(string value) => ApplyFilters();
        partial void OnSearchTextChanged(string value) => ApplyFilters();

        void ApplyFilters()
        {
            IEnumerable<Ps2BinFtcRecord> query = allRecords;

            if (SelectedBucket != null)
                query = query.Where(record => string.Equals(record.Bucket, SelectedBucket.Bucket, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedRootFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.Root, SelectedRootFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedClassFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.ClassName, SelectedClassFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedExtensionFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.Extension, SelectedExtensionFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedSensitivityFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(record => string.Equals(record.SensitivityLabel, SelectedSensitivityFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(record =>
                    record.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || record.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || record.Bucket.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || record.SignatureGroup.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            List<Ps2BinFtcRecord> filtered = query
                .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Replace(Files, filtered);
            SelectedFile = Files.FirstOrDefault();
        }

        Ps2BinFtcRecord? ResolvePairRecord()
        {
            if (SelectedFile == null || string.IsNullOrWhiteSpace(SelectedFile.PairedSidecarPath))
                return null;

            return allRecords.FirstOrDefault(record =>
                string.Equals(record.RelativePath, SelectedFile.PairedSidecarPath, StringComparison.OrdinalIgnoreCase));
        }

        Ps2BinFtcRecord? ResolveFtcInspectorRecord()
        {
            if (SelectedFile == null)
                return null;

            if (string.Equals(SelectedFile.Extension, ".ftc", StringComparison.OrdinalIgnoreCase))
                return SelectedFile;

            Ps2BinFtcRecord? pair = ResolvePairRecord();
            return pair != null && string.Equals(pair.Extension, ".ftc", StringComparison.OrdinalIgnoreCase)
                ? pair
                : null;
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }
}
