using Xunit;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── Exclusive process-global UI culture/config tests ──
// Only preference tests may mutate default thread culture, the config override or FFX_UI_LANG.
// DisableParallelization also excludes every OTHER collection while these tests run.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalUiCultureTestCollection
{
    public const string Name = "Process-global UI culture tests";
}
