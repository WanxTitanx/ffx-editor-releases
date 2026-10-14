using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.AutoAbilityEditor
{
    public class SeverityColorConverter : IValueConverter
    {
        public static readonly SeverityColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int chance)
            {
                if (chance == 0) return new SolidColorBrush(Color.Parse("#5A6B7A")); // Muted
                if (chance < 128) return new SolidColorBrush(Color.Parse("#FFD54F")); // Yellow/Amber
                if (chance <= 200) return new SolidColorBrush(Color.Parse("#FFA726")); // Orange
                return new SolidColorBrush(Color.Parse("#EF5350")); // Red
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public partial class AbilityStatusBar_Control : UserControl
    {
        public static readonly StyledProperty<int> ValueProperty =
            AvaloniaProperty.Register<AbilityStatusBar_Control, int>(nameof(Value));

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly StyledProperty<string> LabelProperty =
            AvaloniaProperty.Register<AbilityStatusBar_Control, string>(nameof(Label));

        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public AbilityStatusBar_Control()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
