using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    /// <summary>Cor do badge por tom: ok=verde, warn=âmbar, info=azul, muted=cinza.</summary>
    public sealed class AuroraBadgeColorConverter : IValueConverter
    {
        private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#2FBF71"));
        private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#E8A33D"));
        private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#3D8BE8"));
        private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6E8A"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                "ok" => Ok,
                "warn" => Warn,
                "info" => Info,
                "muted" => Muted,
                _ => Info,
            };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
