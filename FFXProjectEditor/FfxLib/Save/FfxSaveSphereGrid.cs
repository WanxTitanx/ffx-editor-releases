// ============================================================================
// FfxSaveSphereGrid — Sphere Grid save state (legacy FFXED-model + native table views)
// PURPOSE : legacy editor-model node/activation access (8748-based, 860 nodes) plus the native runtime table
//           base (8684) — two offset models for the same save region.
// WHY     : the GAME reads node N at save[8684+2N] (native, index 0..1279); the FFXED editable model is the
//           +64 skewed slice at 8748. Prefer runtime-table offsets for any node >= 860.
// EVIDENCE: RE docs/reverse/FFX_SPHEREGRID_SAVE_ORCHESTRATOR_RE_2026-06-19.md Session 3; Ghidra/Fahrenheit
//           0xA53DE0/0x785000 (docs/FFX_GHIDRA_FAHRENHEIT_SYMBOL_IMPORT) confirm the engine type.
// MAINT   : NodeCount != engine limit — native max is 1279 before colliding with the link region
//           (see FfxSaveSphereGridRuntimeTable). Never gate new extra nodes on legacy NodeCount.
// ============================================================================
using System;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Legacy FFXED-model Sphere Grid save offsets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <b>GAME</b> runtime state table aliases <c>SaveData+8684</c> (see
    /// <see cref="FfxSaveSphereGridRuntimeTable"/>); the node for index N lives at
    /// <c>save[8684 + 2*N]</c> (content) / <c>+1</c> (status), proven by RE
    /// (docs/reverse/FFX_SPHEREGRID_SAVE_ORCHESTRATOR_RE_2026-06-19.md, Session 3) and cross-confirmed
    /// by the Fahrenheit/Ghidra symbol tables (<c>eiAbmDataInit(SaveSphereGrid*)</c> @ 0xA53DE0 and
    /// <c>MsGetSaveAbilityMap</c> @ 0x785000 — docs/reverse/FFX_GHIDRA_FAHRENHEIT_SYMBOL_IMPORT_2026-07-31.md).
    /// </para>
    /// <para>
    /// The constants below are the <b>+64-byte skewed FFXED legacy editor model</b>
    /// (<c>8748 = 8684 + 64</c>), used for the vanilla 860-node grid authoring surfaces.
    /// The native save itself supports node indices <c>0..1279</c>
    /// (<see cref="FfxSaveSphereGridRuntimeTable.MaxNodeIndexBeforeLinks"/>) before colliding
    /// with the link-state region — do NOT treat <see cref="NodeCount"/> as the engine limit.
    /// </para>
    /// </remarks>
    public static class FfxSaveSphereGrid
    {
        public const int NodeBase = 8748;
        public const int NodeLast = 10466;
        public const int NodeStride = 2;
        public const int ActivationBase = 11308;
        public const int ActivationLast = 12188;
        public const int NodeCount = (NodeLast - NodeBase) / NodeStride + 1;

        /// <summary>Native runtime state table base inside the 25848-byte save payload.</summary>
        public const int NativeTableBase = FfxSaveSphereGridRuntimeTable.TableBaseOffset;

        /// <summary>Highest node index the native save stores without colliding with the link region.</summary>
        public const int NativeMaxNodeIndex = FfxSaveSphereGridRuntimeTable.MaxNodeIndexBeforeLinks;
    }

    public sealed class FfxSaveSphereGridNodeSnapshot
    {
        public int NodeIndex { get; init; }
        public int Offset => FfxSaveSphereGrid.NodeBase + NodeIndex * FfxSaveSphereGrid.NodeStride;
        public int Type { get; set; }
        public int Value { get; set; }

        private static void GuardIndex(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= FfxSaveSphereGrid.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(nodeIndex),
                    $"node {nodeIndex} outside legacy FFXED model (0..{FfxSaveSphereGrid.NodeCount - 1}); use FfxSaveSphereGridRuntimeTable for native/extra nodes.");
        }

        public static FfxSaveSphereGridNodeSnapshot Read(FfxSaveCore core, int nodeIndex)
        {
            GuardIndex(nodeIndex);
            int off = FfxSaveSphereGrid.NodeBase + nodeIndex * FfxSaveSphereGrid.NodeStride;
            return new FfxSaveSphereGridNodeSnapshot
            {
                NodeIndex = nodeIndex,
                Type = (sbyte)core.Data[off],
                Value = core.Data[off + 1],
            };
        }

        public void Write(FfxSaveCore core)
        {
            GuardIndex(NodeIndex);
            int off = Offset;
            core.Data[off] = (byte)Type;
            core.Data[off + 1] = (byte)Value;
        }

        public bool ReadActivation(FfxSaveCore core, int characterBit)
        {
            GuardIndex(NodeIndex);
            return core.ReadBit(FfxSaveSphereGrid.ActivationBase + NodeIndex, characterBit);
        }

        public void WriteActivation(FfxSaveCore core, int characterBit, bool value)
        {
            GuardIndex(NodeIndex);
            core.WriteBit(FfxSaveSphereGrid.ActivationBase + NodeIndex, characterBit, value);
        }
    }
}
