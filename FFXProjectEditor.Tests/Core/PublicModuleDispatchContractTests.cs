using FFXProjectEditor.Modules.Main;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

// ============================================================================
// PublicModuleDispatchContractTests - catalog/dispatch closure (D4)
// WHY: ModuleRegistry.Public is the frozen public baseline and Main_Window
//      .DispatchRoutes is the single production authority. These contracts
//      prove every public module has exactly one Public route, that no route
//      claims Public visibility outside the catalog, and that research-only
//      ids can never leak into the public matrix. No C# source text parsing.
// ============================================================================
public sealed class PublicModuleDispatchContractTests
{
    [Fact]
    public void Every_Public_Module_Has_Exactly_One_Route()
    {
        IReadOnlyDictionary<string, ModuleDispatchRoute> routes = Main_Window.DispatchRoutes;
        foreach (ModuleCatalogPolicy.ModuleCatalogEntry entry in ModuleRegistry.Public)
        {
            ModuleDispatchRoute? route = Assert.Contains(entry.Id, routes);
            Assert.Equal(entry.Id, route.Id);
            Assert.Equal(ModuleRouteVisibility.Public, route.Visibility);
            Assert.NotNull(route.Invoke);
        }
    }

    [Fact]
    public void Public_Route_Set_Exactly_Matches_The_Public_Catalog()
    {
        HashSet<string> catalog = ModuleRegistry.Public
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> routed = Main_Window.DispatchRoutes.Values
            .Where(route => route.Visibility == ModuleRouteVisibility.Public)
            .Select(route => route.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Superset(catalog, routed);
        Assert.Superset(routed, catalog);
        Assert.True(catalog.Count >= 30, $"public baseline shrank to {catalog.Count}");
    }

    [Fact]
    public void Registry_NonPublic_Routes_Are_Never_Marked_Public()
    {
        HashSet<string> publicIds = ModuleRegistry.Public
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ModuleCatalogPolicy.ModuleCatalogEntry entry in ModuleRegistry.All)
        {
            if (!publicIds.Contains(entry.Id))
            {
                Assert.False(Main_Window.DispatchRoutes.TryGetValue(entry.Id, out var route)
                             && route.Visibility == ModuleRouteVisibility.Public,
                    $"{entry.Id} is not in the public baseline but has a Public route");
            }
        }
    }

    [Fact]
    public void Route_Ids_Are_Unique()
    {
        string[] ids = Main_Window.DispatchRoutes.Values.Select(r => r.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Unknown_Id_Has_No_Route()
    {
        Assert.False(Main_Window.DispatchRoutes.ContainsKey("definitely-not-a-module"));
    }

    [Theory]
    [InlineData("monster-editor")]
    [InlineData("home")]
    public void Known_PublicOrInternal_Id_IsAcceptedByProductionValidator(string moduleId)
    {
        Assert.True(ModuleDispatchRoute.IsKnownPublicOrInternal(moduleId));
    }

    [Theory]
    [InlineData("definitely-not-a-module")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_Restored_Id_IsRejectedByProductionValidator(string? moduleId)
    {
        Assert.False(ModuleDispatchRoute.IsKnownPublicOrInternal(moduleId));
    }

    [Fact]
    public void Unknown_Restored_Id_FallsBackToDefaultModule()
    {
        Assert.Equal(
            ModuleDispatchRoute.DefaultModuleId,
            ModuleDispatchRoute.ResolveRestoredId("definitely-not-a-module"));
        Assert.Equal("monster-editor", ModuleDispatchRoute.ResolveRestoredId("monster-editor"));
    }

    [Fact]
    public void InternalResearch_Routes_Still_Behave_As_Navigation_Surfaces()
    {
        // Routes outside the public catalog must exist as InternalResearch (e.g. home
        // workspace navigation), never silently promoted.
        Assert.Contains(Main_Window.DispatchRoutes.Values,
            route => route.Visibility == ModuleRouteVisibility.InternalResearch);
    }
}
