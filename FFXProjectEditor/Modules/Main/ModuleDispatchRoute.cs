using System;
using System.Linq;

namespace FFXProjectEditor.Modules.Main;

// ============================================================================
// ModuleDispatchRoute - typed catalog-to-dispatch authority (D4)
// PURPOSE : one authoritative route table shared by production navigation and
//           contract tests, so the public catalog and the dispatch graph can
//           never drift apart silently.
// WHY     : the old private switch in Main_Window.Dispatch was invisible to
//           tests; a module could be added to ModuleRegistry.Public without a
//           route (or vice versa) and only manual clicking would notice.
// MAINT   : add/remove routes ONLY in Main_Window.BuildDispatchRoutes so UI and
//           tests observe the same table. Public visibility is derived from
//           ModuleRegistry.Public — never hand-maintained here.
// ============================================================================

/// <summary>Whether a dispatch route backs a public-baseline module or an internal/research surface.</summary>
internal enum ModuleRouteVisibility
{
    /// <summary>Routed and part of ModuleRegistry.Public (the module proof matrix covers these).</summary>
    Public,

    /// <summary>Routed utility or research surface that is not part of the public baseline.</summary>
    InternalResearch,
}

/// <summary>One typed navigation route: stable id + window-bound handler.
/// Visibility is computed per access from ModuleRegistry.Public so it can never
/// diverge from the catalog when a gate (e.g. AiFeatureGate) flips after the
/// static route table was first built.</summary>
internal sealed record ModuleDispatchRoute(
    string Id,
    Action<Main_Window> Invoke)
{
    internal ModuleRouteVisibility Visibility =>
        ModuleRegistry.Public.Any(entry => entry.Id == Id)
            ? ModuleRouteVisibility.Public
            : ModuleRouteVisibility.InternalResearch;

    internal const string DefaultModuleId = "home";

    internal static bool IsKnownPublicOrInternal(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId)
        && Main_Window.DispatchRoutes.ContainsKey(moduleId);

    internal static string ResolveRestoredId(string? moduleId) =>
        IsKnownPublicOrInternal(moduleId) ? moduleId! : DefaultModuleId;
}
