using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace FFXProjectEditor;

public class BattleModel_Template : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<BattleModel_Template, string>(nameof(Label));

    public static readonly StyledProperty<Modules.BattleModelPicker_Wrapper> PickerProperty =
        AvaloniaProperty.Register<BattleModel_Template, Modules.BattleModelPicker_Wrapper>(nameof(Picker));

    public static readonly StyledProperty<int> BorderThicknessProperty =
        AvaloniaProperty.Register<BattleModel_Template, int>(nameof(BorderThickness), defaultValue: 1);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Modules.BattleModelPicker_Wrapper Picker
    {
        get => GetValue(PickerProperty);
        set => SetValue(PickerProperty, value);
    }

    public int BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (Picker != null)
            Picker.RefreshOptions();
    }
}

public sealed class BattleModelHexConverter : IValueConverter
{
    public static BattleModelHexConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is short shortValue)
            return $"{shortValue:X4}";

        return "0000";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = (value as string)?.Trim() ?? string.Empty;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (int.TryParse(text, NumberStyles.HexNumber, culture, out int parsed) && parsed is >= 0 and <= 0xFFFF)
            return (short)parsed;

        return AvaloniaProperty.UnsetValue;
    }
}
