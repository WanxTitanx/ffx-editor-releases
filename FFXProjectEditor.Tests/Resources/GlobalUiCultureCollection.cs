using Xunit;

namespace FFXProjectEditor.Tests.Resources;

// Strings.SetLanguage changes process-wide UI culture and its default for newly created threads.
// Running this collection beside product tests makes their localized diagnostic assertions flaky.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalUiCultureCollection
{
    public const string Name = "Global UI culture";
}
