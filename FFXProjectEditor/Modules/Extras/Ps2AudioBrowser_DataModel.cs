using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using FFXProjectEditor.Services.Extras;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class Ps2AudioBrowser_DataModel : ObservableObject
    {
        readonly List<Ps2WdBankEntry> allBanks = [];

        public ObservableCollection<Ps2WdBankEntry> Banks { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedPath))]
        [NotifyPropertyChangedFor(nameof(SelectedHeader))]
        [NotifyPropertyChangedFor(nameof(SelectedStatus))]
        [NotifyPropertyChangedFor(nameof(SelectedSamples))]
        private Ps2WdBankEntry? selected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedSampleTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedSampleBytes))]
        [NotifyPropertyChangedFor(nameof(SelectedSampleEnvelope))]
        private Ps2WdSample? selectedSample;

        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private string sourceRootSummary = "Detecting ffx_ps2 root...";
        [ObservableProperty] private string overviewSummary = "-";
        [ObservableProperty] private string vgmSummary = "-";
        [ObservableProperty] private string playbackStatus = "Idle.";
        [ObservableProperty] private string selectedMetadata = "Select a bank, then 'Probe' to ask vgmstream for decode metadata.";

        [ObservableProperty] private double volumeMusic = 0.75;
        [ObservableProperty] private double volumeFx = 0.65;
        [ObservableProperty] private double volumePreview = 0.80;

        public string HeaderSummary =>
            "Read-only browser of PS2 .wd sound banks (Square WD header). Parses the proved descriptor layout " +
            "(id / programs / samples + per-sample body offsets and ADSR), validated by the reverse harness at " +
            "843/843 no-edit byte identity. PlayStation 4-bit ADPCM is decoded by the external vgmstream oracle.";

        public string ReadonlyScopeSummary =>
            "Allowed now: list banks, show sample descriptors and byte ranges, ask vgmstream for metadata, and export/play " +
            "decoded WAV to a folder you pick. Blocked: any writer/repack into the .wd, codec reimplementation, or claim " +
            "that the ~25 vgmstream-blocked variants (e.g. SDBse) are solved.";

        public string CountSummary => $"{Banks.Count} visible / {allBanks.Count} banks";
        public string SelectedTitle => Selected?.Name ?? "Select a sound bank";
        public string SelectedPath => Selected?.FullPath ?? "-";
        public string SelectedHeader => Selected?.HeaderSummary ?? "-";
        public string SelectedStatus => Selected == null ? "-" : $"{Selected.Status} · {Selected.SizeSummary}";
        public IReadOnlyList<Ps2WdSample> SelectedSamples => Selected?.Samples ?? Array.Empty<Ps2WdSample>();

        public string SelectedSampleTitle => SelectedSample?.Title ?? "-";
        public string SelectedSampleBytes => SelectedSample?.ByteRange ?? "-";
        public string SelectedSampleEnvelope => SelectedSample?.Envelope ?? "-";

        public Ps2AudioBrowser_DataModel()
        {
            Refresh();
        }

        public void SetRoot(string path)
        {
            Project_Service.FfxPs2RootOverride = string.IsNullOrWhiteSpace(path) ? null : path;
            Project_Service.Instance.NotifyFfxPs2RootChanged();
            Refresh();
        }

        public void Refresh()
        {
            allBanks.Clear();
            Banks.Clear();
            Selected = null;
            SelectedSample = null;

            VgmSummary = Ps2VgmStream_Service.IsAvailable
                ? $"vgmstream ready: {Ps2VgmStream_Service.Locate()}"
                : "vgmstream-cli.exe not found — descriptors still load; use 'Set vgmstream...' to enable WAV decode.";

            string? root = Project_Service.Instance.Path_FfxPs2Root;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                SourceRootSummary = "ffx_ps2 root not detected. Use 'Set Root...' to point at your extracted ffx_ps2 folder (the one containing ffx\\proj\\sound\\wave).";
                OverviewSummary = "-";
                OnPropertyChanged(nameof(CountSummary));
                return;
            }

            Ps2WdAudioSnapshot snapshot = Ps2WdAudioReader.Scan(root);
            allBanks.AddRange(snapshot.Banks);
            SourceRootSummary = root;
            OverviewSummary = allBanks.Count == 0
                ? "0 .wd banks here. If you loaded the Steam install (data\\mods\\ffx_ps2), click 'Set Root...' and point at your EXTRACTED ffx_ps2 folder instead."
                : snapshot.OverviewSummary;
            ApplyFilter();
        }

        public void RefreshVgmSummary()
        {
            VgmSummary = Ps2VgmStream_Service.IsAvailable
                ? $"vgmstream ready: {Ps2VgmStream_Service.Locate()}"
                : "vgmstream-cli.exe not found — use 'Set vgmstream...' to enable WAV decode.";
        }

        public void ProbeSelectedMetadata()
        {
            if (Selected == null)
            {
                SelectedMetadata = "Select a bank first.";
                return;
            }
            SelectedMetadata = Ps2VgmStream_Service.Metadata(Selected.FullPath);
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnSelectedChanged(Ps2WdBankEntry? value)
        {
            SelectedSample = value?.Samples.FirstOrDefault();
            SelectedMetadata = "Select a bank, then 'Probe' to ask vgmstream for decode metadata.";
        }

        void ApplyFilter()
        {
            IEnumerable<Ps2WdBankEntry> query = allBanks;
            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(b => b.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            Banks.Clear();
            foreach (Ps2WdBankEntry bank in query)
                Banks.Add(bank);

            OnPropertyChanged(nameof(CountSummary));
            Selected = Banks.FirstOrDefault();
        }
    }
}
