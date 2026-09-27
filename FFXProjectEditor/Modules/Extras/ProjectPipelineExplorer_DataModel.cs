using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class ProjectPipelineExplorer_DataModel : ObservableObject
    {
        public ObservableCollection<Ps2CdIndexRecord> CdIndexRecords { get; } = new();
        public ObservableCollection<Ps2DescriptorRecord> DescriptorRecords { get; } = new();
        public ObservableCollection<Ps2AbmapRecord> AbmapRecords { get; } = new();
        public ObservableCollection<Ps2PipelineEdge> Edges { get; } = new();
        public ObservableCollection<string> BlockedActions { get; } = new();
        public ObservableCollection<string> GlobalWarnings { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedCdIndexTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedCdIndexSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedCdIndexHead))]
        [NotifyPropertyChangedFor(nameof(SelectedCdIndexWarnings))]
        [NotifyPropertyChangedFor(nameof(SelectedCdIndexPaths))]
        private Ps2CdIndexRecord? selectedCdIndex;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedDescriptorTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedDescriptorSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedDescriptorHead))]
        [NotifyPropertyChangedFor(nameof(SelectedDescriptorPreview))]
        [NotifyPropertyChangedFor(nameof(SelectedDescriptorWarnings))]
        private Ps2DescriptorRecord? selectedDescriptor;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedAbmapTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedAbmapSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedAbmapHead))]
        [NotifyPropertyChangedFor(nameof(SelectedAbmapReferences))]
        [NotifyPropertyChangedFor(nameof(SelectedAbmapWarnings))]
        private Ps2AbmapRecord? selectedAbmap;

        [ObservableProperty]
        private string sourceRootSummary = "Detecting ffx_ps2 root...";

        [ObservableProperty]
        private string overviewSummary = "-";

        public string HeaderSummary => "Project/Pipeline Explorer: read-only chain for cdrom.*, descriptor triplets, eiichi_abmap_data, and ABMap lineage without pretending these are final editable assets.";
        public string SelectedCdIndexTitle => SelectedCdIndex?.Label ?? "Select one cdrom triplet";
        public string SelectedCdIndexSummary => SelectedCdIndex == null
            ? "-"
            : $"{SelectedCdIndex.TripletSummary}{Environment.NewLine}id prefix: {SelectedCdIndex.IdPrefixSummary}{Environment.NewLine}fid u16: {SelectedCdIndex.FidU16Count} · ffff: {SelectedCdIndex.FidFFFFCount}";
        public string SelectedCdIndexHead => SelectedCdIndex?.Id.HeadHex ?? "-";
        public string SelectedCdIndexWarnings => SelectedCdIndex?.WarningSummary ?? "-";
        public string SelectedCdIndexPaths => SelectedCdIndex == null
            ? "-"
            : $"id: {SelectedCdIndex.Id.Path}{Environment.NewLine}fid: {SelectedCdIndex.Fid.Path}{Environment.NewLine}mdg: {SelectedCdIndex.Mdg.Path}";

        public string SelectedDescriptorTitle => SelectedDescriptor?.Label ?? "Select one descriptor";
        public string SelectedDescriptorSummary => SelectedDescriptor == null
            ? "-"
            : $"{SelectedDescriptor.Probe.SizeSummary} · parsed rows {SelectedDescriptor.ParsedItemCount} · {SelectedDescriptor.Status}";
        public string SelectedDescriptorHead => SelectedDescriptor?.Probe.HeadHex ?? "-";
        public string SelectedDescriptorPreview => SelectedDescriptor?.Probe.StringPreview ?? "-";
        public string SelectedDescriptorWarnings => SelectedDescriptor?.WarningSummary ?? "-";

        public string SelectedAbmapTitle => SelectedAbmap?.Label ?? "Select one support/carrier record";
        public string SelectedAbmapSummary => SelectedAbmap == null
            ? "-"
            : $"{SelectedAbmap.Probe.SizeSummary} · refs {SelectedAbmap.References.Count} · {SelectedAbmap.Status}";
        public string SelectedAbmapHead => SelectedAbmap?.Probe.HeadHex ?? "-";
        public string SelectedAbmapReferences => SelectedAbmap?.ReferenceSummary ?? "-";
        public string SelectedAbmapWarnings => SelectedAbmap?.WarningSummary ?? "-";

        public ProjectPipelineExplorer_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            string? ffxPs2Root = Project_Service.Instance.Path_FfxPs2Root;
            SourceRootSummary = string.IsNullOrWhiteSpace(ffxPs2Root) ? "FFX PS2 root was not detected from the current master workspace." : ffxPs2Root;
            Ps2ProjectPipelineSnapshot snapshot;
            try
            {
                snapshot = Ps2ProjectPipelineReader.Read(ffxPs2Root);
            }
            catch (Exception ex)
            {
                Replace(CdIndexRecords, []);
                Replace(DescriptorRecords, []);
                Replace(AbmapRecords, []);
                Replace(Edges, []);
                Replace(BlockedActions,
                [
                    "Snapshot failed before data could be collected.",
                    "Read-only only. Do not treat this module failure as permission to write, repack, or bypass provenance."
                ]);
                Replace(GlobalWarnings,
                [
                    $"PROJECT PIPELINE SNAPSHOT FAILED: {ex.GetType().Name} - {ex.Message}",
                    "This surface stays read-only and degraded when the cold project tree cannot be safely walked."
                ]);
                OverviewSummary = "0 triplets · 0 descriptors · 0 support/carrier rows · 0 graph edges";
                SelectedCdIndex = null;
                SelectedDescriptor = null;
                SelectedAbmap = null;
                return;
            }

            Replace(CdIndexRecords, snapshot.CdIndexRecords);
            Replace(DescriptorRecords, snapshot.Descriptors);
            Replace(AbmapRecords, snapshot.AbmapRecords);
            Replace(Edges, snapshot.Edges);
            Replace(BlockedActions, snapshot.BlockedActions);
            Replace(GlobalWarnings, snapshot.Warnings.Select(warning => warning.Summary).ToList());

            OverviewSummary = $"{CdIndexRecords.Count} triplets · {DescriptorRecords.Count} descriptors · {AbmapRecords.Count} support/carrier rows · {Edges.Count} graph edges";
            SelectedCdIndex = CdIndexRecords.FirstOrDefault();
            SelectedDescriptor = DescriptorRecords.FirstOrDefault();
            SelectedAbmap = AbmapRecords.FirstOrDefault();
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }
}
