// ============================================================================
// FfxSaveCatalogCodec — encode/decode a save field via a catalog entry's byte pattern
// PURPOSE : maps a raw byte sequence in the save to a catalog index (longest-prefix match scoring) and back.
// WHY     : catalog-typed fields (items/equipment) store a byte PATTERN, not an index — decoding must find the
//           best (longest) matching catalog entry so e.g. [32,1] beats [32]; re-encode writes the pattern back.
// EVIDENCE: FFXED v0.749 catalog patterns; used for item slots + gear + character listing.
// MAINT   : FindIndex's longest-prefix rule is the correctness crux — a catalog entry that is a strict prefix
//           of another MUST be listed after it (or order-agnostic by length). Never assume a match exists.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Save
{
    public static class FfxSaveCatalogCodec
    {
        public static int FindIndex(IReadOnlyList<FfxSaveCatalogEntry> catalog, ReadOnlySpan<byte> bytes)
        {
            int bestIndex = -1;
            int bestLength = -1;

            for (int i = 0; i < catalog.Count; i++)
            {
                int[] entry = catalog[i].Bytes;
                if (entry.Length == 0 || entry.Length > bytes.Length)
                    continue;

                bool match = true;
                for (int j = 0; j < entry.Length; j++)
                {
                    if ((byte)entry[j] != bytes[j])
                    {
                        match = false;
                        break;
                    }
                }

                // Prefer the longest matching prefix so e.g. [32,1] beats [32] for Hi-Potion.
                if (match && entry.Length > bestLength)
                {
                    bestLength = entry.Length;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        public static void WriteCatalogBytes(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= catalog.Count)
                return;

            int[] bytes = catalog[selectedIndex].Bytes;
            for (int i = 0; i < bytes.Length; i++)
                core.Data[offset + i] = (byte)bytes[i];
        }

        public static int ReadCatalogIndex(FfxSaveCore core, int offset, int byteCount, IReadOnlyList<FfxSaveCatalogEntry> catalog)
        {
            Span<byte> buf = stackalloc byte[byteCount];
            for (int i = 0; i < byteCount; i++)
                buf[i] = core.Data[offset + i];
            return FindIndex(catalog, buf);
        }

        public static int ReadItemCatalogIndex(FfxSaveCore core, int slotIndex)
        {
            int typeOffset = FfxSaveItems.TypeBase + slotIndex * 2;
            return FindIndex(FfxSaveRegistry.Root.ItemCatalog, core.Data.AsSpan(typeOffset, 2));
        }

        public static void WriteItemCatalogIndex(FfxSaveCore core, int slotIndex, int catalogIndex)
        {
            int typeOffset = FfxSaveItems.TypeBase + slotIndex * 2;
            WriteCatalogBytes(core, typeOffset, FfxSaveRegistry.Root.ItemCatalog, catalogIndex);
        }
    }
}
