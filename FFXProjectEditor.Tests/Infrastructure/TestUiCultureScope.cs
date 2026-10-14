using System;
using System.Globalization;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── Per-execution-context UI language for assertions ──
// This does not call the product preference writer or change default thread culture.
// MAINT: own/dispose the scope in the same logical test flow, nested in LIFO order.
internal sealed class TestUiCultureScope : IDisposable
{
    private readonly CultureInfo _previous;
    private bool _disposed;

    internal TestUiCultureScope(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
    }

    internal static TestUiCultureScope English() => new(CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CultureInfo.CurrentUICulture = _previous;
    }
}
