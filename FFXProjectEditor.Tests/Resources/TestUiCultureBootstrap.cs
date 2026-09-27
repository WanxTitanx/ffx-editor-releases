using System.Globalization;
using System.Runtime.CompilerServices;

namespace FFXProjectEditor.Tests.Resources;

// Product diagnostics are localized through CurrentUICulture, while legacy unit assertions use
// the neutral EN wording. Establish that deterministic test-process baseline without calling
// Strings.SetLanguage (which would write the real user's preference file).
internal static class TestUiCultureBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
