using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class Ps2RsdModelBrowser_DataModel : ObservableObject
    {
        readonly List<Ps2RsdModelEntry> allEntries = [];

        public ObservableCollection<Ps2RsdModelEntry> Entries { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedPath))]
        [NotifyPropertyChangedFor(nameof(SelectedGeometry))]
        [NotifyPropertyChangedFor(nameof(SelectedLinkage))]
        [NotifyPropertyChangedFor(nameof(SelectedTextures))]
        [NotifyPropertyChangedFor(nameof(SelectedManifest))]
        [NotifyPropertyChangedFor(nameof(SelectedStatus))]
        private Ps2RsdModelEntry? selected;

        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private string sourceRootSummary = "Detecting ffx_ps2 root...";
        [ObservableProperty] private string overviewSummary = "-";

        public string HeaderSummary =>
            "Read-only browser of PS2 RSD model bundles (MatEditor toolchain). Parses the proved ASCII manifest " +
            "@RSD940102 plus the sibling @PLY940102 mesh and @MAT990928 material, and resolves the .tm2 textures. " +
            "Structural truth only - no 3D render, no writer, no source mutation.";

        public string ReadonlyScopeSummary =>
            "Allowed now: list RSD bundles, show PLY geometry counts (Vertices / Normals / Polygons), MAT material count, " +
            "and TEX -> .tm2 linkage resolution. Blocked: 3D rendering (see ModelViewer), vertex/material editing, any writer.";

        public string CountSummary => $"{Entries.Count} visible / {allEntries.Count} bundles";
        public string SelectedTitle => Selected?.Name ?? "Select a model bundle";
        public string SelectedPath => Selected?.FullPath ?? "-";
        public string SelectedGeometry => Selected == null ? "-" : $"{Selected.GeometrySummary} · materials {Selected.MaterialCount}";
        public string SelectedLinkage => Selected?.LinkageSummary ?? "-";
        public string SelectedTextures => Selected?.TextureSummary ?? "-";
        public string SelectedManifest => Selected == null
            ? "-"
            : $"PLY={Selected.PlyName} · MAT={Selected.MatName} · GRP={Selected.GrpName} · VGR={Selected.VgrName}";
        public string SelectedStatus => Selected == null ? "-" : $"{Selected.Status} · lane {Selected.Lane}";

        public Ps2RsdModelBrowser_DataModel()
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
            allEntries.Clear();
            Entries.Clear();
            Selected = null;

            string? root = Project_Service.Instance.Path_FfxPs2Root;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                SourceRootSummary = "ffx_ps2 root not detected. Use 'Set Root...' to point at your extracted ffx_ps2 folder (the one containing ffx\\yonishi_data).";
                OverviewSummary = "-";
                OnPropertyChanged(nameof(CountSummary));
                return;
            }

            Ps2RsdModelSnapshot snapshot = Ps2RsdModelReader.Scan(root);
            allEntries.AddRange(snapshot.Entries);
            SourceRootSummary = root;
            OverviewSummary = allEntries.Count == 0
                ? "0 RSD bundles here - this root has no .rsd. If you loaded the Steam install (data\\mods\\ffx_ps2), click 'Set Root...' and point at your EXTRACTED ffx_ps2 folder instead."
                : snapshot.OverviewSummary;
            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        void ApplyFilter()
        {
            IEnumerable<Ps2RsdModelEntry> query = allEntries;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(entry =>
                    entry.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || entry.Lane.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            Entries.Clear();
            foreach (Ps2RsdModelEntry entry in query)
                Entries.Add(entry);

            OnPropertyChanged(nameof(CountSummary));
            Selected = Entries.FirstOrDefault();
        }
    }
}
