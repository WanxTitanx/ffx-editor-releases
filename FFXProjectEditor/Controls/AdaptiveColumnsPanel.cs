using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace FFXProjectEditor.Controls;

/// <summary>Form groups wrap against their actual content width, including nested sidebars.</summary>
public sealed class AdaptiveColumnsPanel : UniformGrid
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(MinColumnWidth), 300);
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveColumnsPanel, int>(nameof(MaxColumns), 2);
    public double MinColumnWidth { get => GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    static AdaptiveColumnsPanel() => AffectsMeasure<AdaptiveColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty);
    protected override Size MeasureOverride(Size availableSize)
    {
        int count = double.IsFinite(availableSize.Width)
            ? (int)Math.Clamp(Math.Floor(availableSize.Width / Math.Max(1, MinColumnWidth)), 1, Math.Max(1, MaxColumns))
            : Math.Max(1, MaxColumns);
        SetCurrentValue(ColumnsProperty, count);
        return base.MeasureOverride(availableSize);
    }
}
