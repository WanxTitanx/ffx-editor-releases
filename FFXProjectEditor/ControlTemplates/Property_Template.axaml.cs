using Avalonia;
using Avalonia.Controls.Primitives;

namespace FFXProjectEditor;

public class Property_Template : TemplatedControl
{
    public static readonly StyledProperty<string> Property_LabelProperty = AvaloniaProperty.Register<TemplatedControl, string>(nameof(Property_Label), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public string Property_Label
    {
        get => GetValue(Property_LabelProperty);
        set => SetValue(Property_LabelProperty, value);
    }
    public static readonly StyledProperty<string> Property_ValueProperty = AvaloniaProperty.Register<TemplatedControl, string>(nameof(Property_Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public string Property_Value
    {
        get => GetValue(Property_ValueProperty);
        set => SetValue(Property_ValueProperty, value);
    }
    public static readonly StyledProperty<int> BorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(BorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public int BorderWidth
    {
        get => GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }
    public static readonly StyledProperty<int> ValueBorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(ValueBorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, defaultValue:100);
    public int ValueBorderWidth
    {
        get => GetValue(ValueBorderWidthProperty);
        set => SetValue(ValueBorderWidthProperty, value);
    }

    public static readonly StyledProperty<bool> ShowProgressBarProperty = AvaloniaProperty.Register<TemplatedControl, bool>(nameof(ShowProgressBar), defaultValue: false);
    public bool ShowProgressBar
    {
        get => GetValue(ShowProgressBarProperty);
        set => SetValue(ShowProgressBarProperty, value);
    }

    public static readonly StyledProperty<double> ProgressBarMaximumProperty = AvaloniaProperty.Register<TemplatedControl, double>(nameof(ProgressBarMaximum), defaultValue: 255.0);
    public double ProgressBarMaximum
    {
        get => GetValue(ProgressBarMaximumProperty);
        set => SetValue(ProgressBarMaximumProperty, value);
    }
}