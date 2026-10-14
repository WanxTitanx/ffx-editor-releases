using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FFXProjectEditor.Modules.BlitzballPrizeExplorer
{
    // Presentation-only: maps the honest Atlas evidence vocabulary to a token-aligned brush so the
    // grid/detail evidence pill is legible at a glance. Mirrors the colour logic the badge strip implies
    // (proved-candidate = teal, partial = amber, metadata-only = cool blue, blocked = danger). Any other
    // status — including the weak "parser-corpus" bucket — falls to the neutral grey default on purpose,
    // so uncategorized evidence is never visually upgraded to proved/blocked. No data change.
    public sealed class EvidenceStatusBrushConverter : IValueConverter
    {
        public static readonly EvidenceStatusBrushConverter Instance = new();

        static readonly IBrush Proved = new SolidColorBrush(Color.Parse("#5DD0B4"));
        static readonly IBrush Partial = new SolidColorBrush(Color.Parse("#F4C26B"));
        static readonly IBrush Metadata = new SolidColorBrush(Color.Parse("#7EC8FF"));
        static readonly IBrush Blocked = new SolidColorBrush(Color.Parse("#FF8C7A"));
        static readonly IBrush Other = new SolidColorBrush(Color.Parse("#9EB0C2"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => (value as string) switch
            {
                "proved-candidate" => Proved,
                "partial" => Partial,
                "metadata-only" => Metadata,
                "blocked" => Blocked,
                _ => Other,
            };

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
