using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.BattleTracker
{
    public class BattleTrackerAliveColor_Converter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool active = value switch
            {
                bool booleanValue => booleanValue,
                int intValue => intValue == 1,
                _ => true,
            };

            string key = active ? "TextPrimaryBrush" : "TextMutedBrush";
            if (Application.Current?.Resources.TryGetValue(key, out object? brush) == true && brush is IBrush resolved)
                return resolved;

            return new SolidColorBrush(Color.Parse(active ? "#E8EEF4" : "#9EB0C2"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
