using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FFXProjectEditor.Modules.BlitzballPrizeStructEditor
{
    // Presentation-only: tints a row's left stripe by family so prize-index rows and roll-threshold
    // (odds) rows are glanceable in the interleaved, offset-sorted list. Maps PrizeStructRow.IsPrize
    // to a token-aligned brush — no data, parsing, or writer logic is involved.
    public sealed class PrizeFamilyBrushConverter : IValueConverter
    {
        // Bool-driven: a row's IsPrize -> teal (prize) / warm (odds).
        public static readonly PrizeFamilyBrushConverter Instance = new();
        // Fixed-family: tree-node header dots whose family is known by position (ignore the bound value).
        public static readonly FixedBrush Prize = new(PrizeBrush);
        public static readonly FixedBrush Odds = new(OddsBrush);

        internal static readonly IBrush PrizeBrush = new SolidColorBrush(Color.Parse("#5DD0B4")); // teal — prize index
        internal static readonly IBrush OddsBrush = new SolidColorBrush(Color.Parse("#F4A261"));  // warm — roll odds

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool isPrize && isPrize ? PrizeBrush : OddsBrush;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Always returns one family brush regardless of the bound value — for headers whose family is fixed.
    public sealed class FixedBrush : IValueConverter
    {
        readonly IBrush brush;
        public FixedBrush(IBrush brush) => this.brush = brush;
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => brush;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
