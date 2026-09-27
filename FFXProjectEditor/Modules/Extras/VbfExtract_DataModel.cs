using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class VbfExtract_DataModel : ObservableObject
    {
        CancellationTokenSource? runCancellation;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolExists))]
        [NotifyPropertyChangedFor(nameof(ReadyToExtract))]
        [NotifyPropertyChangedFor(nameof(ProbeSummary))]
        [NotifyPropertyChangedFor(nameof(CommandPreview))]
        private string toolPath = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VbfExists))]
        [NotifyPropertyChangedFor(nameof(ReadyToExtract))]
        [NotifyPropertyChangedFor(nameof(ProbeSummary))]
        [NotifyPropertyChangedFor(nameof(CommandPreview))]
        private string vbfPath = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DictionaryExists))]
        [NotifyPropertyChangedFor(nameof(ReadyToExtract))]
        [NotifyPropertyChangedFor(nameof(ProbeSummary))]
        [NotifyPropertyChangedFor(nameof(CommandPreview))]
        private string dictionaryPath = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OutputRootExists))]
        [NotifyPropertyChangedFor(nameof(ReadyToExtract))]
        [NotifyPropertyChangedFor(nameof(ProbeSummary))]
        [NotifyPropertyChangedFor(nameof(CommandPreview))]
        private string outputRoot = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReadyToExtract))]
        [NotifyPropertyChangedFor(nameof(CanCancel))]
        private bool isRunning;

        [ObservableProperty] private string selectedArchiveLabel = "FFX_Data";
        [ObservableProperty] private string statusText = "Ready. Detect FFX_Data, choose an output root, then extract.";
        [ObservableProperty] private string logText = string.Empty;

        public string HeaderSummary =>
            "Extract the user's own FFX/FFX-2 VBF archives through the local vbfextract backend, so the editor can prepare the ffx_ps2/ps3data source trees it already knows how to browse.";

        public string BoundarySummary =>
            "Read-only archive extraction. This does not repack or modify VBF files, does not bypass the external-file-loader workflow, and does not vendor the VBFExtract source.";

        public bool ToolExists => File.Exists(ToolPath);
        public bool VbfExists => File.Exists(VbfPath);
        public bool DictionaryExists => File.Exists(DictionaryPath);
        public bool OutputRootExists => Directory.Exists(OutputRoot);
        public bool ReadyToExtract => !IsRunning && CurrentProbe().Ready;
        public bool CanCancel => IsRunning;

        public string ProbeSummary => CurrentProbe().Summary;
        public string CommandPreview => VbfExtract_Service.BuildCommandPreview(CurrentProbe());

        public VbfExtract_DataModel()
        {
            ApplyProbe(VbfExtract_Service.DetectFfx());
        }

        public void DetectFfx() => ApplyProbe(VbfExtract_Service.DetectFfx());
        public void DetectFfx2() => ApplyProbe(VbfExtract_Service.DetectFfx2());
        public void DetectMetaMenu() => ApplyProbe(VbfExtract_Service.DetectMetaMenu());

        public void RefreshProbe()
        {
            VbfExtractProbe probe = CurrentProbe();
            StatusText = probe.Ready
                ? $"Ready: {probe.DictionarySummary}, VBF {probe.SizeSummary}."
                : "Not ready. Check the missing tool, VBF, dictionary, or output path.";
            NotifyDerived();
        }

        public async Task StartExtractionAsync()
        {
            if (IsRunning)
                return;

            VbfExtractProbe probe = CurrentProbe();
            if (!probe.Ready)
            {
                StatusText = "Cannot extract yet. Tool, VBF, dictionary, and output root must be valid.";
                NotifyDerived();
                return;
            }

            IsRunning = true;
            LogText = string.Empty;
            StatusText = $"Extracting {SelectedArchiveLabel}. This can take a long time.";
            runCancellation = new CancellationTokenSource();

            try
            {
                Progress<string> progress = new(line => AppendLog(line));
                VbfExtractRunResult result = await VbfExtract_Service.RunAsync(probe, progress, runCancellation.Token);
                StatusText = result.Pass ? "Extraction finished: " + result.Summary : "Extraction finished with warnings: " + result.Summary;
            }
            catch (OperationCanceledException)
            {
                StatusText = "Extraction canceled.";
                AppendLog("Canceled by user.");
            }
            catch (Exception ex)
            {
                StatusText = "Extraction failed: " + ex.Message;
                AppendLog("FAIL: " + ex);
            }
            finally
            {
                runCancellation?.Dispose();
                runCancellation = null;
                IsRunning = false;
                NotifyDerived();
            }
        }

        public void Cancel()
        {
            runCancellation?.Cancel();
        }

        public void SetToolPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                ToolPath = path;
            RefreshProbe();
        }

        public void SetVbfPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                VbfPath = path;
                SelectedArchiveLabel = Path.GetFileNameWithoutExtension(path);
            }
            RefreshProbe();
        }

        public void SetDictionaryPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                DictionaryPath = path;
            RefreshProbe();
        }

        public void SetOutputRoot(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                OutputRoot = path;
            RefreshProbe();
        }

        void ApplyProbe(VbfExtractProbe probe)
        {
            SelectedArchiveLabel = probe.Label;
            ToolPath = probe.ToolPath;
            VbfPath = probe.VbfPath;
            DictionaryPath = probe.DictionaryPath;
            OutputRoot = probe.OutputRoot;
            StatusText = probe.Ready
                ? $"Detected {probe.Label}: {probe.DictionarySummary}, VBF {probe.SizeSummary}."
                : $"Detected {probe.Label}, but one or more paths are missing.";
            NotifyDerived();
        }

        VbfExtractProbe CurrentProbe() =>
            VbfExtract_Service.BuildProbe(SelectedArchiveLabel, ToolPath, VbfPath, DictionaryPath, OutputRoot);

        void AppendLog(string line)
        {
            LogText = string.IsNullOrWhiteSpace(LogText)
                ? line
                : LogText + Environment.NewLine + line;
        }

        void NotifyDerived()
        {
            OnPropertyChanged(nameof(ToolExists));
            OnPropertyChanged(nameof(VbfExists));
            OnPropertyChanged(nameof(DictionaryExists));
            OnPropertyChanged(nameof(OutputRootExists));
            OnPropertyChanged(nameof(ReadyToExtract));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(ProbeSummary));
            OnPropertyChanged(nameof(CommandPreview));
        }
    }
}
