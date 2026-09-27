using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.MagicDllEditor;

// ====================================================================================
// Converters do Magic DLL Editor (Jarvis-PPP-C2C3 · 2026-08-01).
// Usa APENAS recursos do tema (StudioTokens.axaml) — nenhuma cor inventada.
// ====================================================================================

/// <summary>
/// Converte MagicLogLevel → pincel do tema: Info = TextMutedBrush, Warn = WarningBrush,
/// Error = DangerBrush. Fallback = TextMutedBrush se o recurso não resolver.
/// </summary>
public sealed class MagicLogLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            MagicLogLevel.Warn => "WarningBrush",
            MagicLogLevel.Error => "DangerBrush",
            _ => "TextMutedBrush",
        };

        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
