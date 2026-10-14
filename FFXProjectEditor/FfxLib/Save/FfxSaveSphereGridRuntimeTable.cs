// ============================================================================
// FfxSaveSphereGridRuntimeTable — native runtime-table view of Sphere Grid state in the save
// PURPOSE : typed access to the engine's SG runtime table aliasing SaveData+8684 (nodes 0..1279, link bytes,
//           cursor/aux region) that the orchestrator memcpy round-trips to disk.
// WHY     : the game bulk-copies g_FFX_SphereGridRuntimeStateTable to/from the payload; editing the native
//           table is the only way to persist extra (>= vanilla 860) nodes without a hook.
// EVIDENCE: RE docs/reverse/FFX_SPHEREGRID_SAVE_ORCHESTRATOR_RE_2026-06-19.md Session 3; confirmed by
//           Fahrenheit/Ghidra imports (eiAbmDataInit @0xA53DE0, MsGetSaveAbilityMap @0x785000).
// MAINT   : node index 1280 starts colliding with the link region (table+0xA00 = save 11244) — guard with
//           NodeFitsNatively before any write. Check volatile save-region semantics before touching.
// ============================================================================
using System;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Native runtime-table view of the Sphere Grid state inside the 25848-byte
    /// save payload, as proven by RE (docs/reverse/FFX_SPHEREGRID_SAVE_ORCHESTRATOR_RE_2026-06-19.md,
    /// Session 3). The in-game runtime table g_FFX_SphereGridRuntimeStateTable
    /// (0x112EC7C) aliases SaveData+8684; the orchestrator bulk-memcpy round-trips
    /// it to disk. Node N is at table[2*N] = save[8684 + 2*N], so the 861st node
    /// (index 860) lives at save[10404/10405] — inside the payload, below the link
    /// region (table+0xA00 = save 11244). This is the offset the GAME reads/writes
    /// (A49590/A5BB70), distinct from the FFXED +64 skew slice at 8748 used by the
    /// legacy editor node model.
    /// </summary>
    /// <remarks>
    /// Cross-confirmed by the Fahrenheit/Ghidra symbol tables (imported 2026-07-31,
    /// docs/reverse/FFX_GHIDRA_FAHRENHEIT_SYMBOL_IMPORT_2026-07-31.md):
    /// the game function at 0xA53DE0 is `eiAbmDataInit(SaveSphereGrid *sphere_grid)`
    /// (our FFX_SphereGrid_InitRuntimeStateFromAbmapResources) and 0x785000 is
    /// `MsGetSaveAbilityMap() -> SaveSphereGrid *` — the save block is a real,
    /// named engine type. Native capacity: node indices 0..1279 (table+0x0..0xA00),
    /// link bytes 0..1279 (table+0xA00..0xF00), cursor/aux region +0xF00..+0x1320.
    /// </remarks>
    public static class FfxSaveSphereGridRuntimeTable
    {
        public const int TableBaseOffset = 8684;   // SaveData+8684 = runtime table[0]
        public const int TableSpanBytes = 0x1320;  // 4896 B (8684..13579)
        public const int LinkRegionOffset = 0xA00; // table+0xA00 = save 11244
        public const int CursorRegionOffset = 0xF00; // table+0xF00 = save 12524
        public const int VanillaNodeCapacity = 860; // game indices 0..859 are vanilla
        public const int MaxNodeIndexBeforeLinks = (LinkRegionOffset / 2) - 1; // 1279

        public static int NodeContentOffset(int nodeIndex) => TableBaseOffset + nodeIndex * 2;
        public static int NodeStatusOffset(int nodeIndex) => TableBaseOffset + nodeIndex * 2 + 1;
        public static int LinkStateOffset(int linkIndex) => TableBaseOffset + LinkRegionOffset + linkIndex;

        public static bool IsExtraNode(int nodeIndex) =>
            nodeIndex >= VanillaNodeCapacity && nodeIndex <= MaxNodeIndexBeforeLinks;

        /// <summary>True when the node index can be stored natively without colliding with the link region.</summary>
        public static bool NodeFitsNatively(int nodeIndex) =>
            nodeIndex >= 0 && nodeIndex <= MaxNodeIndexBeforeLinks;

        public static (int Content, int Status) ReadNode(FfxSaveCore core, int nodeIndex)
        {
            if (!NodeFitsNatively(nodeIndex))
                throw new ArgumentOutOfRangeException(nameof(nodeIndex),
                    $"node {nodeIndex} collides with the link region (max {MaxNodeIndexBeforeLinks}).");
            int off = NodeContentOffset(nodeIndex);
            return (core.Data[off], core.Data[off + 1]);
        }

        public static void WriteNode(FfxSaveCore core, int nodeIndex, int content, int status)
        {
            if (!NodeFitsNatively(nodeIndex))
                throw new ArgumentOutOfRangeException(nameof(nodeIndex),
                    $"node {nodeIndex} collides with the link region (max {MaxNodeIndexBeforeLinks}).");
            int off = NodeContentOffset(nodeIndex);
            core.Data[off] = (byte)(content & 0xFF);
            core.Data[off + 1] = (byte)(status & 0xFF);
        }

        /// <summary>
        /// Classify a raw save offset within the SG runtime-table span for diff/diagnostic output.
        /// </summary>
        public static string DescribeOffset(int saveOffset)
        {
            if (saveOffset < TableBaseOffset || saveOffset >= TableBaseOffset + TableSpanBytes)
                return "outside-sg-table";
            int rel = saveOffset - TableBaseOffset;
            if (rel < LinkRegionOffset)
            {
                int node = rel / 2;
                string half = (rel % 2 == 0) ? "content" : "status";
                string band = node < VanillaNodeCapacity ? "vanilla-node" : "extra-node";
                return $"{band}[{node}].{half}";
            }
            if (rel < CursorRegionOffset)
                return $"link[{rel - LinkRegionOffset}]";
            return $"cursor/aux[+0x{rel:X}]";
        }
    }
}
