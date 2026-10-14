// ============================================================================
// FfxSaveFormat — container formats recognized for FFX saves (FFXED v0.749 compatible)
// PURPOSE : enum distinguishing raw PS2 / PSU / memory-card / PC .ffx / VME / BIN wrappers.
// WHY     : the in-memory payload is always FfxSaveCore.DataSize bytes; this enum tells the codec how
//           to read/write each outer container (footer/pad/labels differ per format).
// EVIDENCE: ports FFXED v0.749 offsets; each container round-trip validated in FfxSaveRegistry tests.
// MAINT   : adding a container = new enum value + a case in every codec switch + a fixture.
// ============================================================================
namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Container formats understood by FFXED v0.749 (fuzzymillipede) and this port.
    /// The in-memory payload is always <see cref="FfxSaveCore.DataSize"/> bytes.
    /// </summary>
    public enum FfxSaveFormat
    {
        /// <summary>Raw PS2 save blob (exactly 25848 bytes).</summary>
        RawPs2 = 1,

        /// <summary>PSU memory-card image; payload embedded in a 25848-byte block.</summary>
        Psu = 2,

        /// <summary>PS2 memory card (.ps2 / mymc) — not implemented in native UI yet.</summary>
        MemoryCard = 4,

        /// <summary>PC .ffx save: 25848 bytes + 1032-byte footer.</summary>
        PcFfx = 8,

        /// <summary>VME export: 4-byte header + payload + 0xFF + 1023 pad.</summary>
        Vme = 16,

        /// <summary>BIN export: payload + 40-byte metadata + 984 pad (from PSU source).</summary>
        Bin = 32,
    }
}
