using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Extras
{
    internal sealed partial class PhyrePackageIo_DataModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SourceExists))]
        [NotifyPropertyChangedFor(nameof(CanInspect))]
        private string sourcePath = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReplacementExists))]
        private string replacementPath = string.Empty;

        [ObservableProperty] private string outputPath = string.Empty;
        [ObservableProperty] private string outputFolder = string.Empty;
        [ObservableProperty] private string statusText = "Idle.";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InspectionSummary))]
        [NotifyPropertyChangedFor(nameof(KindLabel))]
        [NotifyPropertyChangedFor(nameof(CanExtractDds))]
        [NotifyPropertyChangedFor(nameof(CanImportDds))]
        [NotifyPropertyChangedFor(nameof(CanImportCompiledPackage))]
        [NotifyPropertyChangedFor(nameof(BoundarySummary))]
        private Ps3PhyrePackageInspection? inspection;

        public ObservableCollection<Ps3PhyreMarkerCount> MarkerCounts { get; } = new();
        public ObservableCollection<Ps3PhyreStringEntry> InterestingStrings { get; } = new();

        public string HeaderSummary =>
            "Native Phyre package I/O for FFX HD PC companion assets. DDS can be extracted/repacked as same-shape mip0; DAE/AGS/FX packages can be inspected, extracted to manifest, and replaced with already-compiled .phyre packages with backups.";

        public string SourceExists => File.Exists(SourcePath) ? "yes" : "no";
        public string ReplacementExists => File.Exists(ReplacementPath) ? "yes" : "no";
        public bool CanInspect => File.Exists(SourcePath);
        public bool CanExtractDds => Inspection?.CanDdsPayloadRoundTrip == true;
        public bool CanImportDds => CanExtractDds && File.Exists(ReplacementPath);
        public bool CanImportCompiledPackage => Inspection?.CanCompiledPackageImport == true && File.Exists(ReplacementPath);
        public string InspectionSummary => Inspection?.Summary ?? "No package inspected yet.";
        public string KindLabel => Inspection?.KindLabel ?? "-";

        public string BoundarySummary => Inspection?.Kind switch
        {
            Ps3PhyrePackageKind.DdsTexture => "DDS path is native: extract DDS mip0 and import DDS/raw mip0 into the existing .dds.phyre container. It preserves the Phyre header and touches only mip0.",
            Ps3PhyrePackageKind.DaeModel => "DAE path is protected package import: the editor can inspect/extract metadata and install an already-compiled .dae.phyre. It does not yet compile glTF/FBX/DAE source into Phyre.",
            Ps3PhyrePackageKind.AgsTextureAnimation => "AGS path is protected package import only. Treat animation/control semantics as research until a dedicated compiler exists.",
            Ps3PhyrePackageKind.FxShader => "FX path is protected package import only. Treat shader semantics as compiled blob territory.",
            Ps3PhyrePackageKind.OtherPhyre => "Generic Phyre path is inspect/extract/protected package import only.",
            _ => "Choose a .dds.phyre, .dae.phyre, .ags.phyre, .fx.phyre or other RYHPT .phyre file."
        };

        public PhyrePackageIo_DataModel()
        {
            SourcePath = ResolveDefaultSource();
            OutputFolder = Path.Combine(Directory.GetCurrentDirectory(), "work", "phyre_package_io");
            OutputPath = string.IsNullOrWhiteSpace(SourcePath)
                ? string.Empty
                : Ps3PhyrePackageIo.BuildDefaultOutputPath(SourcePath, ".edited");
            if (File.Exists(SourcePath))
                InspectSource();
        }

        partial void OnSourcePathChanged(string value)
        {
            OutputPath = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Ps3PhyrePackageIo.BuildDefaultOutputPath(value, ".edited");
            OnPropertyChanged(nameof(SourceExists));
            OnPropertyChanged(nameof(CanInspect));
        }

        partial void OnReplacementPathChanged(string value)
        {
            OnPropertyChanged(nameof(ReplacementExists));
            OnPropertyChanged(nameof(CanImportDds));
            OnPropertyChanged(nameof(CanImportCompiledPackage));
        }

        public void SetSourcePath(string path) => SourcePath = path;
        public void SetReplacementPath(string path) => ReplacementPath = path;
        public void SetOutputPath(string path) => OutputPath = path;
        public void SetOutputFolder(string path) => OutputFolder = path;

        public void InspectSource()
        {
            try
            {
                Inspection = Ps3PhyrePackageIo.Inspect(SourcePath);
                Replace(MarkerCounts, Inspection.MarkerCounts.Where(m => m.Count > 0));
                Replace(InterestingStrings, Inspection.InterestingStrings.Take(128));
                StatusText = $"Inspected: {Inspection.Summary}";
            }
            catch (Exception ex)
            {
                Inspection = null;
                MarkerCounts.Clear();
                InterestingStrings.Clear();
                StatusText = $"Inspect failed: {ex.Message}";
            }
        }

        public void ExtractPackage()
        {
            try
            {
                if (Inspection == null)
                    InspectSource();
                Ps3PhyrePackageExtractResult result = Ps3PhyrePackageIo.ExtractPackage(SourcePath, OutputFolder);
                Inspection = result.Inspection;
                StatusText = result.ExtractedDdsPath == null
                    ? $"Extracted package manifest: {result.OutputDirectory}"
                    : $"Extracted package manifest + DDS: {result.OutputDirectory}";
            }
            catch (Exception ex)
            {
                StatusText = $"Extract failed: {ex.Message}";
            }
        }

        public void ExtractDds(string outputPath)
        {
            try
            {
                Ps3PhyreExtractResult result = Ps3MagicTextureWriter.ExtractMip0Dds(SourcePath, outputPath);
                StatusText = $"Extracted DDS mip0: {result.SourceLayout.Summary} -> {result.OutputPath}";
            }
            catch (Exception ex)
            {
                StatusText = $"DDS extract failed: {ex.Message}";
            }
        }

        public void ImportDdsPayload()
        {
            try
            {
                Ps3PhyrePackageImportResult result = Ps3PhyrePackageIo.ImportDdsPayload(SourcePath, ReplacementPath, OutputPath);
                StatusText = $"Imported DDS payload -> {result.OutputPath}. {result.Note}" + (result.BackupPath == null ? string.Empty : $" Backup: {result.BackupPath}");
                Inspection = result.OutputInspection;
            }
            catch (Exception ex)
            {
                StatusText = $"DDS import failed: {ex.Message}";
            }
        }

        public void ImportCompiledPackage()
        {
            try
            {
                Ps3PhyrePackageImportResult result = Ps3PhyrePackageIo.ImportCompiledPackage(SourcePath, ReplacementPath, OutputPath);
                StatusText = $"Imported compiled package -> {result.OutputPath}. {result.Note}" + (result.BackupPath == null ? string.Empty : $" Backup: {result.BackupPath}");
                Inspection = result.OutputInspection;
                Replace(MarkerCounts, Inspection.MarkerCounts.Where(m => m.Count > 0));
                Replace(InterestingStrings, Inspection.InterestingStrings.Take(128));
            }
            catch (Exception ex)
            {
                StatusText = $"Compiled package import failed: {ex.Message}";
            }
        }

        static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }

        static string ResolveDefaultSource()
        {
            string? ps3Root = Project_Service.Instance.Path_Ps3DataRoot;
            if (!string.IsNullOrWhiteSpace(ps3Root) && Directory.Exists(ps3Root))
            {
                string? projectDds = Directory.EnumerateFiles(ps3Root, "*.dds.phyre", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(projectDds))
                    return projectDds;
            }

            string? root = PortablePathResolver.Ps3DataRoot;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Directory.EnumerateFiles(root, "*.dds.phyre", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;

            return string.Empty;
        }
    }
}
