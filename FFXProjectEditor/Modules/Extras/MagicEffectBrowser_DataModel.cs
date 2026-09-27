using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class MagicEffectBrowser_DataModel : ObservableObject
    {
        // Master (unfiltered) lists; the observable collections are the filtered views the UI binds to.
        readonly List<Ps2MagicKernelArtifact> allKernel = new();
        readonly List<Ps2MagicPackageArtifact> allPackages = new();

        public ObservableCollection<Ps2MagicKernelArtifact> KernelArtifacts { get; } = new();
        public ObservableCollection<Ps2MagicPackageArtifact> PackageArtifacts { get; } = new();
        public ObservableCollection<Ps2MagicCrosswalkRow> CrosswalkRows { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedKernelTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedKernelPath))]
        [NotifyPropertyChangedFor(nameof(SelectedKernelSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedKernelHead))]
        [NotifyPropertyChangedFor(nameof(SelectedKernelEvidence))]
        [NotifyPropertyChangedFor(nameof(HasSelectedKernel))]
        private Ps2MagicKernelArtifact? selectedKernel;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedPackageTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedPackagePath))]
        [NotifyPropertyChangedFor(nameof(SelectedPackageSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedPackageCounts))]
        [NotifyPropertyChangedFor(nameof(SelectedPackageEvidence))]
        [NotifyPropertyChangedFor(nameof(HasSelectedPackage))]
        private Ps2MagicPackageArtifact? selectedPackage;

        [ObservableProperty] private string searchText = string.Empty;

        public string HeaderSummary => "Read-only only. This module exposes the battle/kernel lane, mag_* packages, bat_eff presentation lane, and the blocked causal bridge between them without pretending magic is solved.";
        public string MonsterMagicCreationSummary =>
            "Monster magic creation: YES for the monster lane. A new monster spell is a new row in monmagic1.bin/monmagic2.bin with its own command id, native name text, native description text, flags/formula data, and Anim1/Anim2 visual ids.";
        public string MonsterMagicTextCarrierSummary =>
            "Text carrier: for monster spells, the name and description live inside the same monmagic*.bin table/text-pool. Prism Flare (`0x60F7`) and ThundaFira (`0x60F8`) are LAB rows grown in Monster Commands 2 — the row carries the name/description; Monster AI uses the operand while Anim1/Anim2 point at magic_#### clones.";
        public string MonsterMagicZeroBoundarySummary =>
            "Boundary: 'from zero' is honest for a new monster-command row plus new ids/names/assets. A completely new animation/timeline from nothing is the Magic DLL/ASM lane, not just the monmagic row.";
        public string SourceRootSummary { get; private set; } = "Master root and ffx_ps2 root not resolved yet.";
        public string OverviewSummary { get; private set; } = "-";

        [ObservableProperty] private string kernelEmptyStateMessage = string.Empty;
        [ObservableProperty] private string packageEmptyStateMessage = string.Empty;

        public bool HasKernelEmptyState => !string.IsNullOrEmpty(KernelEmptyStateMessage);
        public bool HasPackageEmptyState => !string.IsNullOrEmpty(PackageEmptyStateMessage);

        partial void OnKernelEmptyStateMessageChanged(string value) => OnPropertyChanged(nameof(HasKernelEmptyState));
        partial void OnPackageEmptyStateMessageChanged(string value) => OnPropertyChanged(nameof(HasPackageEmptyState));

        public bool HasSelectedKernel => SelectedKernel != null;
        public bool HasSelectedPackage => SelectedPackage != null;

        public string SelectedKernelTitle => SelectedKernel?.Title ?? "Select a kernel-side artifact";
        public string SelectedKernelPath => SelectedKernel?.RelativePath ?? "-";
        public string SelectedKernelSummary => SelectedKernel?.Summary ?? "-";
        public string SelectedKernelHead => SelectedKernel?.First16Hex ?? "-";
        public string SelectedKernelEvidence => SelectedKernel == null ? "-" : $"{SelectedKernel.EvidenceLabel.ToUpperInvariant()} · {SelectedKernel.BlockedReason}";

        public string SelectedPackageTitle => SelectedPackage?.Title ?? "Select a package/presentation artifact";
        public string SelectedPackagePath => SelectedPackage?.RelativePath ?? "-";
        public string SelectedPackageSummary => SelectedPackage?.Summary ?? "-";
        // Structured counts only; the Summary card carries the file-extension breakdown so the two cards
        // no longer echo the same string.
        public string SelectedPackageCounts => SelectedPackage == null
            ? "-"
            : $"lane {SelectedPackage.Lane} · {SelectedPackage.FileCount} files total";
        public string SelectedPackageEvidence => SelectedPackage == null ? "-" : $"{SelectedPackage.EvidenceLabel.ToUpperInvariant()} · {SelectedPackage.BlockedReason}";

        public MagicEffectBrowser_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            string? masterRoot = Project_Service.Instance.ProjectPath;
            string? ffxPs2Root = Project_Service.Instance.Path_FfxPs2Root;
            SourceRootSummary = $"master={masterRoot ?? "-"}{System.Environment.NewLine}ffx_ps2={ffxPs2Root ?? "-"}";

            Ps2MagicEffectSnapshot snapshot = Ps2MagicEffectReader.Scan(masterRoot, ffxPs2Root);
            allKernel.Clear();
            allKernel.AddRange(snapshot.KernelArtifacts);
            allPackages.Clear();
            allPackages.AddRange(snapshot.PackageArtifacts);
            Replace(CrosswalkRows, snapshot.CrosswalkRows);
            OverviewSummary = snapshot.OverviewSummary;

            // Honest, actionable empty state instead of a silently blank pane (matches the PS3 viewer).
            KernelEmptyStateMessage = allKernel.Count > 0
                ? string.Empty
                : (string.IsNullOrWhiteSpace(masterRoot)
                    ? "No master folder loaded - point the editor at a project, then Refresh."
                    : "No kernel artifacts found - load a master folder containing jppc/battle/kernel (magic.bin, monmagic*.bin, command.bin).");
            PackageEmptyStateMessage = allPackages.Count > 0
                ? string.Empty
                : (string.IsNullOrWhiteSpace(ffxPs2Root)
                    ? "ffx_ps2 root not resolved - set/point the editor at the ffx_ps2 root, then Refresh."
                    : "No mag_* / bat_eff / et_battle packages found under the ffx_ps2 root.");

            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        void ApplyFilter()
        {
            IEnumerable<Ps2MagicKernelArtifact> kernelQuery = allKernel;
            IEnumerable<Ps2MagicPackageArtifact> packageQuery = allPackages;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                kernelQuery = kernelQuery.Where(item =>
                    item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || item.Lane.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
                packageQuery = packageQuery.Where(item =>
                    item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || item.Lane.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            Replace(KernelArtifacts, kernelQuery.ToList());
            Replace(PackageArtifacts, packageQuery.ToList());

            SelectedKernel = KernelArtifacts.FirstOrDefault();
            SelectedPackage = PackageArtifacts.FirstOrDefault();
        }

        static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }
}
