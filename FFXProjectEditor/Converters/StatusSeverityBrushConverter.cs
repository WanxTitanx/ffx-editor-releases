using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace FFXProjectEditor.Converters
{
    // Jarvis-UI (Sprint F 2026-06-20, OPT-F7; reusado em SaveEditor OPT-C1): mapeia o enum-ish string de
    // severidade ("None"/"Info"/"Warning"/"Danger") para o Brush de token correspondente do StudioTokens
    // (TextMutedBrush para None, WarningBrush para Warning, DangerBrush para Danger; Info cai no default do
    // tema para não gritar). Resolve via Resources da App p/ nunca duplicar hex de token. Presentation-only.
    public sealed class StatusSeverityBrushConverter : IValueConverter
    {
        public static readonly StatusSeverityBrushConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string severity = value as string ?? "None";
            string key = severity switch
            {
                "Danger" => "DangerBrush",
                "Warning" => "WarningBrush",
                _ => "TextMutedBrush",
            };

            // Resolve o token do StudioTokens via Resources da App (Avalonia 11). Fallback estático só pra
            // nunca devolver null p/ o Foreground — cor roubada do TextMutedBrush.
            if (Application.Current?.Resources.TryGetValue(key, out object? brush) == true && brush is IBrush resolved)
                return resolved;

            return new SolidColorBrush(Color.Parse("#9EB0C2"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
