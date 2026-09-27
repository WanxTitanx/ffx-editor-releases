using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.Core;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Tests.MagicDll;
using Xunit;

namespace FFXProjectEditor.Tests.Resources;

// ── Deterministic culture/profile isolation regression ──
// Directly exercise old callers under a sentinel preference, not probabilistic scheduler timing.
// MAINT: these tests intentionally set globals and therefore belong to the exclusive collection.
[Collection(GlobalUiCultureTestCollection.Name)]
public sealed class UiCultureIsolationTests
{
    [Fact]
    public void LocalScopes_RestoreNestedCulture_WithoutChangingDefaultOrPreferences()
    {
        using var state = new GlobalState();
        using (var outer = TestUiCultureScope.English())
        {
            Assert.Equal("", CultureInfo.CurrentUICulture.Name);
            Assert.Same(state.ExpectedDefault, CultureInfo.DefaultThreadCurrentUICulture);
            var inner = new TestUiCultureScope(CultureInfo.GetCultureInfo("de-DE"));
            Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
            inner.Dispose();
            Assert.Equal("", CultureInfo.CurrentUICulture.Name);
            inner.Dispose();
            Assert.Equal("", CultureInfo.CurrentUICulture.Name);
        }
        state.AssertUnchanged();
    }

    [Fact]
    public async Task LocalScopes_FlowAcrossAwait_WithoutChangingSiblingOrParent()
    {
        using var state = new GlobalState();
        var firstReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Read(string language, TaskCompletionSource<bool> ready, Task other)
        {
            using var scope = new TestUiCultureScope(CultureInfo.GetCultureInfo(language));
            ready.SetResult(true);
            await other.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(language, CultureInfo.CurrentUICulture.Name);
            return Strings.Get("U_Ai_AtelTopLevelReturnUnsupported");
        }
        var results = await Task.WhenAll(
            Task.Run(() => Read("pt-BR", firstReady, secondReady.Task)),
            Task.Run(() => Read("en-US", secondReady, firstReady.Task)));
        Assert.Contains("return no topo", results[0], StringComparison.Ordinal);
        Assert.Contains("top-level return", results[1], StringComparison.Ordinal);
        state.AssertUnchanged();
    }

    [Fact]
    public void NullLocalCulture_RefusesWithoutStateChanges()
    {
        using var state = new GlobalState();
        Assert.Throws<ArgumentNullException>(() => new TestUiCultureScope(null!));
        state.AssertUnchanged();
    }

    [Fact]
    public void SelectorTests_AreExcludedFromParallelCollections()
    {
        var attribute = Assert.Single(typeof(StringsTests).GetCustomAttributesData()
            .Where(value => value.AttributeType == typeof(CollectionAttribute)));
        Assert.Equal(GlobalUiCultureTestCollection.Name, Assert.Single(attribute.ConstructorArguments).Value);
        var definition = typeof(GlobalUiCultureTestCollection).GetCustomAttribute<CollectionDefinitionAttribute>();
        Assert.NotNull(definition);
        Assert.True(definition!.DisableParallelization);
    }

    [Fact]
    public void SelectorFixture_RestoresIncomingConfigAndCulturesAfterRealPersistenceTest()
    {
        using var state = new GlobalState();
        using (var selector = new StringsTests()) selector.SetLanguage_PersistsAndApplies();
        state.AssertUnchanged();
    }

    [Theory]
    [InlineData("sphere")]
    [InlineData("parser")]
    [InlineData("grow")]
    [InlineData("preview")]
    public void NonSelectorAssertions_DoNotPersistLanguageOrChangeGlobalState(string caller)
    {
        using var state = new GlobalState();
        switch (caller)
        {
            case "sphere": new SphereGridGameCompatibilityTests().CapacityText_ShowsCaps(); break;
            case "parser": new MagicDllParserTests().TryParse_MissingFile_ReturnsFalse_NoThrow(); break;
            case "grow": new MagicDllGrowTests().Grow_SemDocumento_RetornaFalse(); break;
            case "preview":
                using (var preview = new OperationPreviewTests())
                    preview.BuildFromFiles_BeforeEqualsAfter_AllLayersEmpty();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(caller));
        }
        state.AssertUnchanged();
    }

    private sealed class GlobalState : IDisposable
    {
        private readonly string? _previousEnvironment = Environment.GetEnvironmentVariable("FFX_UI_LANG");
        private readonly string? _previousConfig = Strings.ConfigDirectoryOverride;
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly string _root = Directory.CreateTempSubdirectory("ffx-culture-isolation-").FullName;
        internal CultureInfo ExpectedCulture { get; } = CultureInfo.GetCultureInfo("pt-BR");
        internal CultureInfo ExpectedDefault { get; } = CultureInfo.GetCultureInfo("fr-FR");

        internal GlobalState()
        {
            File.WriteAllText(Path.Combine(_root, "ui_lang.config"), "sentinel-preference");
            Environment.SetEnvironmentVariable("FFX_UI_LANG", "ja");
            Strings.ConfigDirectoryOverride = _root;
            CultureInfo.DefaultThreadCurrentUICulture = ExpectedDefault;
            CultureInfo.CurrentUICulture = ExpectedCulture;
        }

        internal void AssertUnchanged()
        {
            Assert.Equal("ja", Environment.GetEnvironmentVariable("FFX_UI_LANG"));
            Assert.Equal("sentinel-preference", File.ReadAllText(Path.Combine(_root, "ui_lang.config")));
            Assert.Equal(_root, Strings.ConfigDirectoryOverride);
            Assert.Same(ExpectedCulture, CultureInfo.CurrentUICulture);
            Assert.Same(ExpectedDefault, CultureInfo.DefaultThreadCurrentUICulture);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FFX_UI_LANG", _previousEnvironment);
            Strings.ConfigDirectoryOverride = _previousConfig;
            CultureInfo.DefaultThreadCurrentUICulture = _previousDefault;
            CultureInfo.CurrentUICulture = _previousCulture;
            Directory.Delete(_root, recursive: true);
        }
    }
}
