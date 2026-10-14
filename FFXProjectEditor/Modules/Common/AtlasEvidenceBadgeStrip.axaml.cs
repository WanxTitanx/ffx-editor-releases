using Avalonia;
using Avalonia.Controls;

namespace FFXProjectEditor.Modules.Common;

// Reusable read-only evidence strip: a row of Atlas evidence badges + a short origin line + an optional
// "where it appears in the game" expander. Bind the Evidence property to an AtlasEvidenceInfo (or null to hide).
// It never writes, applies, or authorizes anything — it only explains provenance and limits.
public partial class AtlasEvidenceBadgeStrip : UserControl
{
    public static readonly StyledProperty<AtlasEvidenceInfo?> EvidenceProperty =
        AvaloniaProperty.Register<AtlasEvidenceBadgeStrip, AtlasEvidenceInfo?>(nameof(Evidence));

    public AtlasEvidenceInfo? Evidence
    {
        get => GetValue(EvidenceProperty);
        set => SetValue(EvidenceProperty, value);
    }

    public AtlasEvidenceBadgeStrip()
    {
        InitializeComponent();
        IsVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EvidenceProperty)
            return;

        AtlasEvidenceInfo? info = Evidence;
        Root.DataContext = info;
        IsVisible = info != null && (info.HasBadges || info.HasWhere || info.HasOrigin);
    }
}
