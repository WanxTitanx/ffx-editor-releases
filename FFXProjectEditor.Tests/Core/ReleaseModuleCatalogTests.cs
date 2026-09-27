using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FFXProjectEditor.Core;
using FFXProjectEditor.Modules.Main;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

/// <summary>
/// Locks the reviewed public module closure without deleting research-only source.
/// </summary>
[Collection("AiFeatureGate")]
public sealed class ReleaseModuleCatalogTests : IDisposable
{
    // The ai-assistant entry is opt-in: Public includes it only while the flag is on.
    // Pointing the gate at a temp file keeps the count deterministic on any machine —
    // the host's real feature-flags.json must not leak into the assertion.
    readonly string _file = Path.Combine(Path.GetTempPath(), "flags-" + Guid.NewGuid().ToString("N") + ".json");

    public ReleaseModuleCatalogTests()
    {
        AiFeatureGate.SettingsPathOverride = _file;
        AiFeatureGate.ResetForTests();
    }

    public void Dispose()
    {
        AiFeatureGate.SettingsPathOverride = null;
        AiFeatureGate.ResetForTests();
        try { File.Delete(_file); } catch { }
    }

    private static readonly string[] MustShipIds =
    {
        "home",
        "monster-editor",
        "magic-dll-editor",
        "battle-commands-hub",
        "items-hub",
        "customizations-hub",
        "stats-hub",
        "enemy-design-hub",
        "sphere-grid-hub",
        "treasure-map",
        "text-hub",
        "blitzball",
        "save-editor",
        "encounters-hub",
        "battle-explorer",
        "inventory-tracker",
        "arena-tracker",
        "aurora-chamber",
        "thunder-plains",
        "textures-tm2",
        "bin-ftc-atlas",
        "project-pipeline",
        "presentation-containers",
        "albhed-dictionary",
    };

    private static readonly string[] OptionalDegradedIds =
    {
        "runtime-dll-manager",
        "aurora-field-explorer",
        "vbf-extract",
        "ps2-audio",
        "monster-studio-web",
        "magic-studio-web",
    };

    // ai-assistant lives in Registry.All but only enters Public while AiFeatureGate is on —
    // on this fixture the gate is off, so it sits with the not-public baseline.
    private static readonly string[] NotPublicBaselineIds =
    {
        "ai-assistant",
        "map-scene-editor",
        "live-battle-lab",
        "aurora-overlay-lab",
        "battle-tracker",
        "debug-menu",
        "magic-dll-browser",
        "phyre-package-io",
        "battle-corpus-crosswalk",
    };

    [Fact]
    public void ReviewedClassification_HasExactCountsAndNoOverlap()
    {
        Assert.Equal(24, MustShipIds.Length);
        Assert.Equal(6, OptionalDegradedIds.Length);
        Assert.Equal(9, NotPublicBaselineIds.Length);

        string[] classifiedIds = MustShipIds
            .Concat(OptionalDegradedIds)
            .Concat(NotPublicBaselineIds)
            .ToArray();
        Assert.Equal(39, classifiedIds.Length);
        Assert.Equal(classifiedIds.Length, classifiedIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PublicCatalog_EqualsMustShipAndOptionalDegradedExactly()
    {
        string[] publicIds = GetPublicCatalog().Select(entry => entry.Id).ToArray();
        string[] expectedPublicIds = MustShipIds.Concat(OptionalDegradedIds).ToArray();

        Assert.Equal(publicIds.Length, publicIds.Distinct(StringComparer.Ordinal).Count());
        AssertExactSet(expectedPublicIds, publicIds);
        Assert.All(
            NotPublicBaselineIds,
            id => Assert.DoesNotContain(id, publicIds, StringComparer.Ordinal));
    }

    [Fact]
    public void RegistryAll_EqualsPublicPlusNotPublicBaselineExactly()
    {
        string[] allIds = ModuleRegistry.All.Select(entry => entry.Id).ToArray();
        string[] publicIds = GetPublicCatalog().Select(entry => entry.Id).ToArray();
        string[] expectedAllIds = MustShipIds
            .Concat(OptionalDegradedIds)
            .Concat(NotPublicBaselineIds)
            .ToArray();

        Assert.Equal(allIds.Length, allIds.Distinct(StringComparer.Ordinal).Count());
        AssertExactSet(expectedAllIds, allIds);
        Assert.All(publicIds, id => Assert.Contains(id, allIds, StringComparer.Ordinal));
        Assert.All(NotPublicBaselineIds, id => Assert.Contains(id, allIds, StringComparer.Ordinal));
        AssertExactSet(NotPublicBaselineIds, allIds.Except(publicIds, StringComparer.Ordinal));
    }

    private static IReadOnlyList<ModuleCatalogPolicy.ModuleCatalogEntry> GetPublicCatalog()
    {
        PropertyInfo? property = typeof(ModuleRegistry).GetProperty(
            "Public",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IReadOnlyList<ModuleCatalogPolicy.ModuleCatalogEntry>>(
            property!.GetValue(null));
    }

    private static void AssertExactSet(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(id => id, StringComparer.Ordinal),
            actual.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void PublicCatalog_GatesAiAssistantAndKeepsLabsOut()
    {
        string[] labs =
        {
            "map-scene-editor",
            "live-battle-lab",
            "aurora-overlay-lab",
            "battle-tracker",
            "debug-menu",
            "magic-dll-browser",
            "phyre-package-io",
            "battle-corpus-crosswalk",
        };

        string[] gateOff = GetPublicCatalog().Select(entry => entry.Id).ToArray();
        Assert.All(labs, id => Assert.DoesNotContain(id, gateOff, StringComparer.Ordinal));
        Assert.DoesNotContain("ai-assistant", gateOff, StringComparer.Ordinal);
        Assert.Contains("aurora-chamber", gateOff, StringComparer.Ordinal);
        Assert.Contains("magic-dll-editor", gateOff, StringComparer.Ordinal);
        Assert.Contains("magic-studio-web", gateOff, StringComparer.Ordinal);
        Assert.Equal(30, gateOff.Length);

        AiFeatureGate.Enabled = true;
        string[] gateOn = GetPublicCatalog().Select(entry => entry.Id).ToArray();
        Assert.Contains("ai-assistant", gateOn, StringComparer.Ordinal);
        Assert.Equal(31, gateOn.Length);
    }
}
