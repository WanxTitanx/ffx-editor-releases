using FFXProjectEditor.Resources;
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FFXProjectEditor.Converters;

/// <summary>
/// Resolve um IconKey (string, ex.: "IconMonster") para a <see cref="Geometry"/> correspondente
/// em <c>StudioIcons.axaml</c>. Permite que o Dashboard data-bound renderize PathIcon.Data a partir
/// de <c>ModuleCatalogEntry.IconKey</c> — binding direto <c>{DynamicResource {Binding IconKey}}</c>
/// não funciona em Avalonia para PathIcon.Data.
/// </summary>
/// <remarks>
/// Jarvus-UI v2.160.0.0 (§17 Workspace Ready). Se a chave não resolver, cai num fallback nulo
/// (o PathIcon renderiza vazio); o check de consistência (grep IconKey × StudioIcons) garante que
/// isso não acontece em build normal.
/// </remarks>
public class IconKeyToGeometryConverter : IValueConverter
{
    /// <summary>Instância singleton para uso em XAML via <c>{StaticResource IconKeyToGeometry}</c>.</summary>
    public static readonly IconKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return null;

        return ResolveGeometry(key);
    }

    /// <summary>
    /// Resolve uma chave de recurso para uma <see cref="Geometry"/>, percorrendo o Resources do
    /// app E seus <c>MergedDictionaries</c> (onde StudioIcons.axaml vive). Avalonia 11:
    /// <c>Resources.TryGetValue</c> sozinho NÃO percorre os merged dicts; cada entrada de
    /// <c>MergedDictionaries</c> é <c>IResourceProvider</c>, resolvida via <c>TryGetResource</c>.
    /// Compartilhado com <c>Main_Window.MakeModuleRailButton</c>.
    /// </summary>
    internal static Geometry? ResolveGeometry(string key)
    {
        var app = Application.Current;
        if (app is null)
            return null;

        if (app.Resources.TryGetValue(key, out var direct) && direct is Geometry g0)
            return g0;

        // Avalonia 11: MergedDictionaries é IEnumerable<IResourceProvider>; cada provider exige
        // (key, ThemeVariant?, out value). Passamos o ActualThemeVariant do app.
        var theme = app.ActualThemeVariant;
        foreach (var provider in app.Resources.MergedDictionaries)
        {
            if (provider.TryGetResource(key, theme, out var merged) && merged is Geometry g1)
                return g1;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException(Strings.U_Cv_OneWay);
}
