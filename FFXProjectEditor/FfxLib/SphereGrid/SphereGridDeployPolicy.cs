using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.SphereGrid;

/// <summary>
/// Guards live project deployment of Sphere Grid layouts.
///
/// Deployment is allowed when:
///  1. Counts are untouched (Safe Transplant Mode), OR
///  2. The final topology is within the proven game-compatible caps
///     (≤860 nodes; ≤1,021 Standard/Original or ≤934 Expert links — see
///     <see cref="SphereGridGameCompatibility"/>), which the engine loads safely; OR
///  3. The user explicitly opts into a clean no-hook runtime test
///     (builder.AllowRuntimeTestDeploy) — disposable save + RT2 observation required.
///
/// Historical note (corrected 2026-08-05/06 after live RE): the previously-cited "render ceiling =
/// FFX_Menu2D_InitBatchBuffers_NoTextureFallback@0x681DB0 buffers 861×48" was a DRIFT — 0x681DB0 is
/// actually the abmap/menu batch TEXTURE initializer (not the sphere render buffer). The "no-hook
/// 861/882 crashes on exit" finding came from RT2 runs of the retired True New Node v5 hook stack
/// (manifest/sidecar/SEH), never from a clean editor-only deploy. Today's engine is header-driven
/// (NodeCount flows dat header → lpamng @ dword_2305834), the static LpAbilityMapEngine holds
/// 1024 nodes / 1024 links / 128 clusters, and the native save holds 1280/1280 — so counts ≤ the
/// game-compatible caps need no hook. See docs/reverse/ZW_SPHERE_GRID_VALIDATION_2026-08-05.md.
/// </summary>
public static class SphereGridDeployPolicy
{
    /// <summary>
    /// True when the builder's current topology may be deployed to the project.
    /// <paramref name="isExpert"/> selects the Expert (934) vs Standard/Original (1021) link cap.
    /// </summary>
    public static bool CanDeployToProject(SphereGridLayoutBuilder builder, bool isExpert = false)
    {
        if (!builder.HasRuntimeUnprovenTopologyCountChange)
            return true; // Safe Transplant Mode: counts preserved
        if (builder.AllowRuntimeTestDeploy)
            return true; // explicit clean no-hook RT2 escape
        // Proven game-compatible caps (ZW-tested + our RE): safe deploy without hook.
        return HasGameCompatibleCounts(builder, isExpert);
    }

    public static bool HasGameCompatibleCounts(SphereGridLayoutBuilder builder, bool isExpert) =>
        SphereGridGameCompatibility.IsWithinNodeCap(builder.NodeCount) &&
        SphereGridGameCompatibility.IsWithinLinkCap(builder.LinkCount, isExpert);

    public static string BlockReason(SphereGridLayoutBuilder builder, bool isExpert = false)
    {
        string node = builder.NodeCount > SphereGridGameCompatibility.MaximumGameCompatibleNodes
            ? string.Format(Strings.U_Bb_NodeCapOver, builder.NodeCount, SphereGridGameCompatibility.MaximumGameCompatibleNodes)
            : "";
        string link = builder.LinkCount > SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert)
            ? string.Format(Strings.U_Bb_LinkCapOver, builder.LinkCount, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert))
            : "";
        string over = string.Join(", ", new[] { node, link }.Where(x => x.Length > 0));
        if (over.Length > 0)
        {
            return string.Format(Strings.U_Bb_NotGameCompatible, over,
                SphereGridGameCompatibility.MaximumGameCompatibleNodes,
                SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert));
        }
        return string.Format(Strings.U_Bb_RuntimeUnproven,
               builder.SeededClusterCount, builder.SeededNodeCount, builder.SeededLinkCount,
               builder.ClusterCount, builder.NodeCount, builder.LinkCount) +
               " " + Strings.F2_enable_allow_rt2_test_deploy_only_with_d_5dfbdc9a;
    }
}

