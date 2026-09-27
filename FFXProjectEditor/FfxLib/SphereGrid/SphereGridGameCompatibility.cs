using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    /// <summary>
    /// Game-compatible capacity guardrail for the Sphere Grid LAYOUT (abmap dat0X/dat1X).
    ///
    /// Sourced from empirical in-game testing by Dragoon803 (ZanarkandWorkshop v0.4.1) and
    /// cross-checked against our own RE (see docs/reverse/ZW_SPHERE_GRID_VALIDATION_2026-08-05.md):
    ///  - The game can LOAD more than 860 nodes but corrupts its Sphere Grid visual memory and
    ///    crashes on close above it — 860 is the safe game-compatible ceiling (render buffer ceiling ~861).
    ///  - Link totals are tested safe at 1,021 (Standard/Original) and 934 (Expert); the static
    ///    LpAbilityMapEngine holds 1024 links, so these stay under it.
    ///  - Each node is limited to 5 usable links so every displayed connection remains usable for
    ///    movement/activation in game.
    /// </summary>
    public static class SphereGridGameCompatibility
    {
        public const int MaximumGameCompatibleNodes = 860;
        public const int MaximumGameCompatibleStandardLinks = 1021;
        public const int MaximumGameCompatibleExpertLinks = 934;
        public const int MaximumUsableLinksPerNode = 5;

        /// <summary>Total link cap for a given grid kind (Original/Standard share 1021).</summary>
        public static int GetGameCompatibleLinkLimit(bool isExpert) =>
            isExpert
                ? MaximumGameCompatibleExpertLinks
                : MaximumGameCompatibleStandardLinks;

        public static bool IsWithinNodeCap(int nodeCount) =>
            nodeCount <= MaximumGameCompatibleNodes;

        public static bool IsWithinLinkCap(int linkCount, bool isExpert) =>
            linkCount <= GetGameCompatibleLinkLimit(isExpert);

        /// <summary>Return the number of links connected to a node (undirected).</summary>
        public static int LinkDegree(IReadOnlyList<SphereGridLinkEntry> links, int nodeIndex)
        {
            int degree = 0;
            foreach (SphereGridLinkEntry link in links)
            {
                if (link.Node1 == nodeIndex || link.Node2 == nodeIndex)
                    degree++;
            }
            return degree;
        }

        /// <summary>True if adding a link (a↔b) would keep both endpoints at ≤ 5 connections.</summary>
        public static bool EndpointsHaveUsableCapacity(
            IReadOnlyList<SphereGridLinkEntry> links, int nodeA, int nodeB)
        {
            return LinkDegree(links, nodeA) < MaximumUsableLinksPerNode &&
                   LinkDegree(links, nodeB) < MaximumUsableLinksPerNode;
        }

        public static string NodeCapacityText(int nodeCount) =>
            string.Format(Strings.U_Bb_GridNodes, nodeCount, MaximumGameCompatibleNodes);

        public static string LinkCapacityText(int linkCount, bool isExpert) =>
            string.Format(Strings.U_Bb_GridLinks, linkCount, GetGameCompatibleLinkLimit(isExpert));
    }
}
