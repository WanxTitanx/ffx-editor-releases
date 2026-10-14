using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal sealed class TextureAssetRow
    {
        public required Ps2Tim2Metadata Metadata { get; init; }
        public string Name => Metadata.Name;
        public string RelativePath => Metadata.RelativePath;
        public string PreviewState => Metadata.PreviewStateLabel;
        public string Evidence => Metadata.EvidenceLabel.ToUpperInvariant();
        public string Domain => Metadata.Provenance.Domain;
        public string Bundle => Metadata.Provenance.BundleHint;
        public string Dimensions => Metadata.DimensionSummary;
        public string Palette => Metadata.PaletteSummary;
        public string Size => $"{Metadata.FileSize:N0} bytes";
        public string WarningSummary => string.Join(" · ", Metadata.Warnings);
    }

    internal sealed class SupportTextureAssetRow
    {
        public required Ps2TextureSupportMetadata Metadata { get; init; }
        public string Name => Metadata.Name;
        public string RelativePath => Metadata.RelativePath;
        public string Extension => Metadata.Extension;
        public string Domain => Metadata.Provenance.Domain;
        public string Bundle => Metadata.Provenance.BundleHint;
        public string Cohort => Metadata.Cohort;
        public string Evidence => Metadata.EvidenceLabel.ToUpperInvariant();
        public string PairSummary => Metadata.PairSummary;
        public string WarningSummary => string.Join(" · ", Metadata.Warnings);
    }

    internal partial class TextureFamilyBrowser_DataModel : ObservableObject
    {
        readonly List<TextureAssetRow> allAssets = [];
        readonly List<SupportTextureAssetRow> allSupportAssets = [];

        public ObservableCollection<TextureAssetRow> Assets { get; } = new();
        public ObservableCollection<SupportTextureAssetRow> SupportAssets { get; } = new();
        public ObservableCollection<string> PreviewStateOptions { get; } = new(["All", "Native Preview", "Experimental Preview", "Metadata Only", "Blocked"]);
        public ObservableCollection<string> DomainOptions { get; } = new(["All"]);
        public ObservableCollection<string> SupportExtensionOptions { get; } = new(["All"]);
        public ObservableCollection<string> SupportDomainOptions { get; } = new(["All"]);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedName))]
        [NotifyPropertyChangedFor(nameof(SelectedRelativePath))]
        [NotifyPropertyChangedFor(nameof(SelectedPreviewState))]
        [NotifyPropertyChangedFor(nameof(SelectedEvidence))]
        [NotifyPropertyChangedFor(nameof(SelectedDomain))]
        [NotifyPropertyChangedFor(nameof(SelectedBundle))]
        [NotifyPropertyChangedFor(nameof(SelectedDimensions))]
        [NotifyPropertyChangedFor(nameof(SelectedPaletteSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedFileSize))]
        [NotifyPropertyChangedFor(nameof(SelectedVariant))]
        [NotifyPropertyChangedFor(nameof(SelectedHeaderSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedWarnings))]
        [NotifyPropertyChangedFor(nameof(SelectedBlockedReason))]
        [NotifyPropertyChangedFor(nameof(SelectedProvenance))]
        [NotifyPropertyChangedFor(nameof(SelectedPreviewStatusSummary))]
        private TextureAssetRow? selectedAsset;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedPreviewStateFilter = "All";

        [ObservableProperty]
        private string selectedDomainFilter = "All";

        [ObservableProperty]
        private Bitmap? selectedPreviewBitmap;

        [ObservableProperty]
        private string sourceRootSummary = "Detecting FFX PS2 source root...";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedSupportName))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportRelativePath))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportExtension))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportDomain))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportCohort))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportEvidence))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportPairSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportHeadHex))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportWarnings))]
        [NotifyPropertyChangedFor(nameof(SelectedSupportBlockedReason))]
        private SupportTextureAssetRow? selectedSupportAsset;

        [ObservableProperty]
        private string supportSearchText = string.Empty;

        [ObservableProperty]
        private string selectedSupportExtensionFilter = "All";

        [ObservableProperty]
        private string selectedSupportDomainFilter = "All";

        public string HeaderSummary => "Read-only TM2 surface first: inventory, provenance, preview state, and brutally honest guardrails before any palette or writer fantasy.";
        public string AssetCountSummary => $"{Assets.Count} visible / {allAssets.Count} indexed";
        public string SupportAssetCountSummary => $"{SupportAssets.Count} visible / {allSupportAssets.Count} indexed";
        public string SelectedName => SelectedAsset?.Name ?? "Select a TM2 asset";
        public string SelectedRelativePath => SelectedAsset?.RelativePath ?? "-";
        public string SelectedPreviewState => SelectedAsset?.Metadata.PreviewStateLabel ?? "-";
        public string SelectedEvidence => SelectedAsset?.Evidence ?? "-";
        public string SelectedDomain => SelectedAsset?.Domain ?? "-";
        public string SelectedBundle => SelectedAsset?.Bundle ?? "-";
        public string SelectedDimensions => SelectedAsset?.Dimensions ?? "-";
        public string SelectedPaletteSummary => SelectedAsset?.Palette ?? "-";
        public string SelectedFileSize => SelectedAsset == null ? "-" : $"{SelectedAsset.Metadata.FileSize:N0} bytes";
        public string SelectedVariant => SelectedAsset?.Metadata.Variant ?? "-";
        public string SelectedWarnings => SelectedAsset == null ? "-" : string.Join(Environment.NewLine, SelectedAsset.Metadata.Warnings);
        public string SelectedBlockedReason => string.IsNullOrWhiteSpace(SelectedAsset?.Metadata.BlockedReason) ? "-" : SelectedAsset.Metadata.BlockedReason!;
        public string SelectedProvenance => SelectedAsset == null
            ? "-"
            : $"{SelectedAsset.Metadata.Provenance.Domain} · {SelectedAsset.Metadata.Provenance.BundleHint} · {SelectedAsset.Metadata.Provenance.RelationSummary}";
        public string SelectedSupportName => SelectedSupportAsset?.Name ?? "Select a support-family asset";
        public string SelectedSupportRelativePath => SelectedSupportAsset?.RelativePath ?? "-";
        public string SelectedSupportExtension => SelectedSupportAsset?.Extension ?? "-";
        public string SelectedSupportDomain => SelectedSupportAsset == null
            ? "-"
            : $"{SelectedSupportAsset.Domain} · {SelectedSupportAsset.Bundle}";
        public string SelectedSupportCohort => SelectedSupportAsset?.Cohort ?? "-";
        public string SelectedSupportEvidence => SelectedSupportAsset == null ? "-" : $"{SelectedSupportAsset.Evidence} · {SelectedSupportAsset.Metadata.BlockedReason}";
        public string SelectedSupportPairSummary => SelectedSupportAsset?.PairSummary ?? "-";
        public string SelectedSupportHeadHex => SelectedSupportAsset?.Metadata.First16Hex ?? "-";
        public string SelectedSupportWarnings => SelectedSupportAsset == null ? "-" : string.Join(Environment.NewLine, SelectedSupportAsset.Metadata.Warnings);
        public string SelectedSupportBlockedReason => SelectedSupportAsset?.Metadata.BlockedReason ?? "-";
        public string SelectedPreviewStatusSummary => SelectedAsset == null
            ? "Pick one asset to inspect its preview state."
            : SelectedAsset.Metadata.PreviewState switch
            {
                Ps2TexturePreviewState.Native => "Native preview path is enabled for this indexed TIM2 cohort.",
                Ps2TexturePreviewState.Experimental => "Experimental preview path is enabled with persistent guardrails.",
                Ps2TexturePreviewState.MetadataOnly => "Metadata only: useful facts, no honest raster preview yet.",
                _ => "Blocked: this branch stays out of preview until the missing decode proof exists."
            };

        public string SelectedHeaderSummary
        {
            get
            {
                if (SelectedAsset == null)
                    return "-";

                Ps2Tim2Header header = SelectedAsset.Metadata.Header;
                return $"0x04={header.U32_0x04} · 0x0C={header.U32_0x0C} · 0x10={header.BlockSizeLike_0x10} · 0x14={header.PaletteBytes_0x14} · 0x18={header.ImageBytes_0x18} · 0x1C={header.PictureHeaderSize_0x1C} · 0x1E={header.ColorCount_0x1E} · 0x23={header.Bppish_0x23} · 0x24/0x26={header.Width_0x24}x{header.Height_0x26}";
            }
        }

        public TextureFamilyBrowser_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            allAssets.Clear();
            allSupportAssets.Clear();
            Assets.Clear();
            SupportAssets.Clear();
            DomainOptions.Clear();
            DomainOptions.Add("All");
            SupportExtensionOptions.Clear();
            SupportDomainOptions.Clear();
            SupportExtensionOptions.Add("All");
            SupportDomainOptions.Add("All");

            string? ffxPs2Root = Project_Service.Instance.Path_FfxPs2Root;
            if (string.IsNullOrWhiteSpace(ffxPs2Root))
            {
                SourceRootSummary = "FFX PS2 root was not detected from the current master workspace.";
                SelectedAsset = null;
                SelectedPreviewBitmap = null;
                return;
            }

            SourceRootSummary = ffxPs2Root;
            IReadOnlyList<Ps2Tim2Metadata> inventory = Ps2Tim2Reader.Scan(ffxPs2Root);
            foreach (Ps2Tim2Metadata metadata in inventory)
                allAssets.Add(new TextureAssetRow { Metadata = metadata });

            IReadOnlyList<Ps2TextureSupportMetadata> supportInventory = Ps2TextureSupportReader.Scan(ffxPs2Root);
            foreach (Ps2TextureSupportMetadata metadata in supportInventory)
                allSupportAssets.Add(new SupportTextureAssetRow { Metadata = metadata });

            foreach (string domain in allAssets.Select(asset => asset.Domain).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                DomainOptions.Add(domain);
            foreach (string ext in allSupportAssets.Select(asset => asset.Extension).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                SupportExtensionOptions.Add(ext);
            foreach (string domain in allSupportAssets.Select(asset => asset.Domain).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                SupportDomainOptions.Add(domain);

            ApplyFilters();
            ApplySupportFilters();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilters();
        partial void OnSelectedPreviewStateFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedDomainFilterChanged(string value) => ApplyFilters();

        partial void OnSelectedAssetChanged(TextureAssetRow? value)
        {
            SelectedPreviewBitmap = value == null
                ? null
                : Ps2Tim2Reader.TryBuildPreviewBitmap(value.Metadata);
        }

        partial void OnSupportSearchTextChanged(string value) => ApplySupportFilters();
        partial void OnSelectedSupportExtensionFilterChanged(string value) => ApplySupportFilters();
        partial void OnSelectedSupportDomainFilterChanged(string value) => ApplySupportFilters();

        void ApplyFilters()
        {
            IEnumerable<TextureAssetRow> query = allAssets;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(asset =>
                    asset.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || asset.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || asset.Bundle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(SelectedPreviewStateFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(asset => string.Equals(asset.PreviewState, SelectedPreviewStateFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedDomainFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(asset => string.Equals(asset.Domain, SelectedDomainFilter, StringComparison.OrdinalIgnoreCase));

            List<TextureAssetRow> filtered = query
                .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assets.Clear();
            foreach (TextureAssetRow asset in filtered)
                Assets.Add(asset);

            SelectedAsset = Assets.FirstOrDefault();
        }

        void ApplySupportFilters()
        {
            IEnumerable<SupportTextureAssetRow> query = allSupportAssets;

            if (!string.IsNullOrWhiteSpace(SupportSearchText))
            {
                query = query.Where(asset =>
                    asset.Name.Contains(SupportSearchText, StringComparison.OrdinalIgnoreCase)
                    || asset.RelativePath.Contains(SupportSearchText, StringComparison.OrdinalIgnoreCase)
                    || asset.Cohort.Contains(SupportSearchText, StringComparison.OrdinalIgnoreCase)
                    || asset.Bundle.Contains(SupportSearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(SelectedSupportExtensionFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(asset => string.Equals(asset.Extension, SelectedSupportExtensionFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(SelectedSupportDomainFilter, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(asset => string.Equals(asset.Domain, SelectedSupportDomainFilter, StringComparison.OrdinalIgnoreCase));

            List<SupportTextureAssetRow> filtered = query
                .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SupportAssets.Clear();
            foreach (SupportTextureAssetRow asset in filtered)
                SupportAssets.Add(asset);

            SelectedSupportAsset = SupportAssets.FirstOrDefault();
        }
    }
}
