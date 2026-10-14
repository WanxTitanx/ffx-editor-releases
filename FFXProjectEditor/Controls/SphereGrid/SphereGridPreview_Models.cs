namespace FFXProjectEditor.Controls.SphereGrid;

public enum SphereGridPreviewVisualMode
{
    HybridGame = 0,
    DebugColors = 1,
    LabelOverlay = 2,
    BackgroundOnly = 3
}

public enum SphereGridPreviewBackgroundKind
{
    None = 0,
    Original = 1,
    Standard = 2,
    Expert = 3
}

public sealed class SphereGridNodeVisualInfo
{
    public required int ContentIndex { get; init; }
    public required ushort AppearanceType { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortLabel { get; init; }
    public required bool IsEmpty { get; init; }
    public required bool IsLock { get; init; }
    public required int LockLevel { get; init; }
}

public sealed class SphereGridNodeInvokedEventArgs : System.EventArgs
{
    public SphereGridNodeInvokedEventArgs(int nodeIndex)
    {
        NodeIndex = nodeIndex;
    }

    public int NodeIndex { get; }
}
