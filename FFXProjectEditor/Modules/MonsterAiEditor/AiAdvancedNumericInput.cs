using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

/// <summary>Input contract for advanced authoring. An invalid value is never a request to write zero.</summary>
internal static class AiAdvancedNumericInput
{
    public static bool TryParse(string? text, out ushort value)
    {
        text = (text ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        return ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
