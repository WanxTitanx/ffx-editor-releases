// ============================================================================
// FfxSaveItems — items/key-items view of the save
// PURPOSE : exposes the 112 inventory slots (type base 16140, qty base 16652) + gil (15752) and the key-item
//           bit flags lifted from the save registry.
// WHY     : item presence is a catalog index (via FfxSaveCatalogCodec) separate from quantity; key items are
//           single bits in a flags region (FfxSaveRegistry.Root.KeyItemBits).
// EVIDENCE: FFXED v0.749 offsets; catalog decode validated in FfxSaveCatalogCodec.
// MAINT   : DisplayName resolves through the registry item catalog — keep the catalog current for names.
// ============================================================================
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FFXProjectEditor.FfxLib.Save
{
    public static class FfxSaveItems
    {
        public const int TypeBase = 16140;
        public const int QuantityBase = 16652;
        public const int SlotCount = 112;
        public const int GilOffset = 15752;
    }

    public sealed partial class FfxSaveItemSlotSnapshot : ObservableObject
    {
        public int SlotIndex { get; init; }
        public string Label { get; init; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        private int catalogIndex;

        [ObservableProperty]
        private int quantity;

        public string DisplayName
        {
            get
            {
                var catalog = FfxSaveRegistry.Root.ItemCatalog;
                if (CatalogIndex >= 0 && CatalogIndex < catalog.Count)
                    return catalog[CatalogIndex].Label;
                return $"Unknown ({CatalogIndex})";
            }
        }

        public static FfxSaveItemSlotSnapshot Read(FfxSaveCore core, int slotIndex)
        {
            return new FfxSaveItemSlotSnapshot
            {
                SlotIndex = slotIndex,
                Label = $"Slot {slotIndex + 1}",
                CatalogIndex = FfxSaveCatalogCodec.ReadItemCatalogIndex(core, slotIndex),
                Quantity = core.ReadInt32Le(FfxSaveItems.QuantityBase + slotIndex, 1),
            };
        }

        public void Write(FfxSaveCore core)
        {
            FfxSaveCatalogCodec.WriteItemCatalogIndex(core, SlotIndex, CatalogIndex);
            core.WriteInt32Le(FfxSaveItems.QuantityBase + SlotIndex, Quantity, 1);
        }
    }

    public sealed class FfxSaveKeyItemSnapshot
    {
        public string Label { get; init; } = string.Empty;
        public int Offset { get; init; }
        public int Bit { get; init; }
        public bool Value { get; set; }

        public static IEnumerable<FfxSaveKeyItemSnapshot> ReadAll(FfxSaveCore core)
        {
            foreach (FfxSaveRegistryField field in FfxSaveRegistry.Root.KeyItemBits)
            {
                yield return new FfxSaveKeyItemSnapshot
                {
                    Label = field.Label,
                    Offset = field.Offset,
                    Bit = field.Bit,
                    Value = core.ReadBit(field.Offset, field.Bit),
                };
            }
        }

        public void Write(FfxSaveCore core) => core.WriteBit(Offset, Bit, Value);
    }
}
