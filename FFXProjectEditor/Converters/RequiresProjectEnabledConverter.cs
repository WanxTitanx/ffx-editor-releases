using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FFXProjectEditor.Converters;

/// <summary>
/// Habilita um card/tile de módulo conforme <c>RequiresProject</c> × <c>IsProjectLoaded</c>.
/// Retorna <c>false</c> (desabilitado) só quando o módulo exige projeto E nenhum está carregado.
/// </summary>
/// <remarks>
/// Jarvus-UI v2.160.0.0 (§17). Uso via MultiBinding:
/// <code>
/// &lt;MultiBinding Converter="{x:Static conv:RequiresProjectEnabledConverter.Instance}"&gt;
///   &lt;Binding Path="RequiresProject" /&gt;
///   &lt;Binding Path="(main:Main_DataModel.ProjService).IsProjectLoaded" /&gt;
/// &lt;/MultiBinding&gt;
/// </code>
/// Aceita também um valor único (bool RequiresProject) tratando como "sempre habilitado" — útil
/// quando o caller já pré-filtrou.
/// </remarks>
public class RequiresProjectEnabledConverter : IMultiValueConverter
{
    /// <summary>Instância singleton para uso em XAML.</summary>
    public static readonly RequiresProjectEnabledConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = RequiresProject (bool), values[1] = IsProjectLoaded (bool).
        bool requiresProject = values.Count > 0 && values[0] is bool b && b;
        bool projectLoaded = values.Count > 1 && values[1] is bool loaded && loaded;

        // Habilitado quando: não exige projeto, OU projeto carregado.
        return !requiresProject || projectLoaded;
    }
}
