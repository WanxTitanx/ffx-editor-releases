using CommunityToolkit.Mvvm.ComponentModel;

namespace FFXProjectEditor.Modules.StringExplorer
{
    internal partial class StringExplorerRecord : ObservableObject
    {
        [ObservableProperty] private string indexLabel = string.Empty;
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private string summary = string.Empty;
        [ObservableProperty] private string primaryLabel = string.Empty;
        [ObservableProperty] private string primaryText = string.Empty;
        [ObservableProperty] private string secondaryLabel = string.Empty;
        [ObservableProperty] private string secondaryText = string.Empty;
        [ObservableProperty] private string tertiaryLabel = string.Empty;
        [ObservableProperty] private string tertiaryText = string.Empty;
        [ObservableProperty] private string quaternaryLabel = string.Empty;
        [ObservableProperty] private string quaternaryText = string.Empty;
        [ObservableProperty] private string detailsLabel = string.Empty;
        [ObservableProperty] private string detailsText = string.Empty;
        [ObservableProperty] private string searchBlob = string.Empty;

        public bool IsMonsterLocalizationRecord { get; init; }
        public int MonsterIndex { get; init; }
        public int SourceEntryIndex { get; init; }
        public bool HasQuaternary => !string.IsNullOrWhiteSpace(QuaternaryLabel);
        public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsLabel);

        partial void OnQuaternaryLabelChanged(string value) => OnPropertyChanged(nameof(HasQuaternary));
        partial void OnDetailsLabelChanged(string value) => OnPropertyChanged(nameof(HasDetails));
    }

    internal sealed class StringExplorerDiffRow
    {
        public required string IndexLabel { get; init; }
        public required string Title { get; init; }
        public required string FieldsSummary { get; init; }
    }
}
