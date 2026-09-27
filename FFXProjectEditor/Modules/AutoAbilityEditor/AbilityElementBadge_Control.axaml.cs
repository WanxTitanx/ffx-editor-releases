using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.AutoAbilityEditor
{
    public class ElementActiveBorderConverter : IValueConverter
    {
        public static readonly ElementActiveBorderConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
                return new SolidColorBrush(Color.Parse("#E25822")); // Just an example, for accurate colors we'll use DataContext later, but let's use a nice gold for active #D4A040 or teal #2A9D8F
            return new SolidColorBrush(Color.Parse("#264052")); // PanelStrokeBrush
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class ElementActiveTextConverter : IValueConverter
    {
        public static readonly ElementActiveTextConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
                return new SolidColorBrush(Color.Parse("#F1F5F9")); // TextPrimaryBrush
            return new SolidColorBrush(Color.Parse("#8BA3B5")); // TextMutedBrush
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public partial class AbilityElementBadge_Control : UserControl
    {
        public static readonly StyledProperty<string> LabelProperty =
            AvaloniaProperty.Register<AbilityElementBadge_Control, string>(nameof(Label));

        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly StyledProperty<string> ElementColorResourceProperty =
            AvaloniaProperty.Register<AbilityElementBadge_Control, string>(nameof(ElementColorResource), "AbilityElementFireBrush");

        public string ElementColorResource
        {
            get => GetValue(ElementColorResourceProperty);
            set => SetValue(ElementColorResourceProperty, value);
        }
        
        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<AbilityElementBadge_Control, bool>(nameof(IsActive));

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public AbilityElementBadge_Control()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
