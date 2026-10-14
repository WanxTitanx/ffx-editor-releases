using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Controls.SphereGrid;

namespace FFXProjectEditor.Modules.SphereGridExplorer
{
    internal sealed partial class SphereGridNamedOption : ObservableObject
    {
        public required int Value { get; init; }
        public required string Label { get; init; }
        public override string ToString() => Label;
    }

    internal sealed partial class SphereGridPreviewPresetOption : ObservableObject
    {
        public required string Label { get; init; }
        public required string Summary { get; init; }
        public required SphereGridPreviewVisualMode VisualMode { get; init; }
        public required double ZoomFactor { get; init; }
        public override string ToString() => Label;
    }

    internal sealed partial class SphereGridDiffRow : ObservableObject
    {
        public required string Scope { get; init; }
        public required string Change { get; init; }
    }

    internal sealed partial class SphereGridSphereTypeEditorRow : ObservableObject
    {
        public required int Index { get; init; }
        [ObservableProperty] private string jpDescription = string.Empty;
        [ObservableProperty] private string usDescription = string.Empty;
        [ObservableProperty] private string jpSimplifiedDescription = string.Empty;
        [ObservableProperty] private string usSimplifiedDescription = string.Empty;
        [ObservableProperty] private ushort behavior;
        [ObservableProperty] private ushort activates;
        [ObservableProperty] private byte range;
        [ObservableProperty] private byte specialRole;
        [ObservableProperty] private ushort reserved0x0E;
    }

    internal sealed partial class SphereGridNodeTypeEditorRow : ObservableObject
    {
        public required int Index { get; init; }
        [ObservableProperty] private string jpName = string.Empty;
        [ObservableProperty] private string usName = string.Empty;
        [ObservableProperty] private string jpSimplifiedName = string.Empty;
        [ObservableProperty] private string usSimplifiedName = string.Empty;
        [ObservableProperty] private string jpDescription = string.Empty;
        [ObservableProperty] private string usDescription = string.Empty;
        [ObservableProperty] private string jpSimplifiedDescription = string.Empty;
        [ObservableProperty] private string usSimplifiedDescription = string.Empty;
        [ObservableProperty] private ushort nodeEffectBitfield;
        [ObservableProperty] private ushort learnedMove;
        [ObservableProperty] private ushort increaseAmount;
        [ObservableProperty] private ushort appearanceType;
    }

    internal sealed partial class SphereGridLayoutNodeEditorRow : ObservableObject
    {
        public required int Index { get; init; }
        public required short PosX { get; init; }
        public required short PosY { get; init; }
        public required ushort Cluster { get; init; }
        public required int ConnectedNodeCount { get; init; }
        public required ushort RedundantContent { get; init; }
        public required ushort Unknown6 { get; init; }
        [ObservableProperty] private int contentIndex;
    }
}
