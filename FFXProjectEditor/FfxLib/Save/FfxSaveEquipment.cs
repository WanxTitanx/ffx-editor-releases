// ============================================================================
// FfxSaveEquipment — equipment inventory view of the save (weapons/armor slots)
// PURPOSE : maps the 17628-based equipment inventory into labeled slots (fixed list + special aeon/character
//           gear) and reads/writes the FfxSaveEquipmentSnapshot fields per 22-byte slot.
// WHY     : gear lives in a packed array with a handful of special (aeon/Seymour) slots off the regular band.
// EVIDENCE: FFXED v0.749 offsets; slot layout 22B (type/attach/capacity/4 autos).
// MAINT   : the 12->178 jump (484 offset) and special rel-offs are layout-derived — keep them in sync with
//           the real save when adding slots. Never write without a loaded catalog for display.
// ============================================================================
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed class FfxSaveEquipmentSlotInfo
    {
        public string Label { get; init; } = string.Empty;
        public int RelativeOffset { get; init; }

        public int AbsoluteBase => FfxSaveEquipment.InventoryBase + RelativeOffset;
    }

    public static class FfxSaveEquipment
    {
        public const int InventoryBase = 17628;
        public const int SlotStride = 22;

        public static IReadOnlyList<FfxSaveEquipmentSlotInfo> BuildSlotList()
        {
            var slots = new List<FfxSaveEquipmentSlotInfo>(200);

            for (int i = 0; i < 12; i++)
                slots.Add(new FfxSaveEquipmentSlotInfo { Label = $"Slot {i + 1}", RelativeOffset = i * SlotStride });

            for (int i = 12; i < 178; i++)
                slots.Add(new FfxSaveEquipmentSlotInfo { Label = $"Slot {i + 1}", RelativeOffset = 484 + i * SlotStride });

            (string label, int rel)[] special =
            [
                ("Seymour Weapon", 572), ("Seymour Armor", 594),
                ("Valefor Weapon", 264), ("Valefor Armor", 286),
                ("Ifrit Weapon", 308), ("Ifrit Armor", 330),
                ("Ixion Weapon", 352), ("Ixion Armor", 374),
                ("Shiva Weapon", 396), ("Shiva Armor", 418),
                ("Bahamut Weapon", 440), ("Bahamut Armor", 462),
                ("Anima Weapon", 484), ("Anima Armor", 506),
                ("Yojimbo Weapon", 528), ("Yojimbo Armor", 550),
                ("Cindy Weapon", 616), ("Cindy Armor", 638),
                ("Sandy Weapon", 660), ("Sandy Armor", 682),
                ("Mindy Weapon", 704), ("Mindy Armor", 726),
            ];

            foreach ((string label, int rel) in special)
                slots.Add(new FfxSaveEquipmentSlotInfo { Label = label, RelativeOffset = rel });

            return slots;
        }

        static int Field(int slotBase, int absoluteForSlot0) => slotBase + (absoluteForSlot0 - InventoryBase);
    }

    public sealed class FfxSaveEquipmentSnapshot
    {
        public int SlotIndex { get; init; }
        public string Label { get; init; } = string.Empty;
        public int SlotBase { get; init; }

        public bool Occupied { get; set; }
        public int EquipCharacter { get; set; }
        public int AppearanceIndex { get; set; }
        public int EquippedOn { get; set; }
        public int Type { get; set; }
        public int DamageFormula { get; set; }
        public int AttackPower { get; set; }
        public int Critical { get; set; }
        public int AutoCapacity { get; set; }
        public int Auto1 { get; set; }
        public int Auto2 { get; set; }
        public int Auto3 { get; set; }
        public int Auto4 { get; set; }

        public static FfxSaveEquipmentSnapshot Read(FfxSaveCore core, FfxSaveEquipmentSlotInfo slot, int index)
        {
            int b = slot.AbsoluteBase;
            return new FfxSaveEquipmentSnapshot
            {
                SlotIndex = index,
                Label = slot.Label,
                SlotBase = b,
                Occupied = core.Data[b + 2] != 0,
                EquipCharacter = core.ReadInt32Le(b + 4, 1),
                AppearanceIndex = core.ReadInt32Le(b, 1),
                EquippedOn = (sbyte)core.Data[b + 6],
                Type = core.Data[b + 5],
                DamageFormula = core.Data[b + 8],
                AttackPower = core.Data[b + 9],
                Critical = core.Data[b + 10],
                AutoCapacity = core.Data[b + 11],
                Auto1 = core.ReadInt32Le(b + 15, 2),
                Auto2 = core.ReadInt32Le(b + 17, 2),
                Auto3 = core.ReadInt32Le(b + 19, 2),
                Auto4 = core.ReadInt32Le(b + 21, 2),
            };
        }

        public void Write(FfxSaveCore core)
        {
            int b = SlotBase;
            core.Data[b + 2] = (byte)(Occupied ? 1 : 0);
            core.WriteInt32Le(b + 4, EquipCharacter, 1);
            core.WriteInt32Le(b, AppearanceIndex, 1);
            core.Data[b + 6] = (byte)EquippedOn;
            core.Data[b + 5] = (byte)Type;
            core.Data[b + 8] = (byte)DamageFormula;
            core.Data[b + 9] = (byte)AttackPower;
            core.Data[b + 10] = (byte)Critical;
            core.Data[b + 11] = (byte)AutoCapacity;
            core.WriteInt32Le(b + 15, Auto1, 2);
            core.WriteInt32Le(b + 17, Auto2, 2);
            core.WriteInt32Le(b + 19, Auto3, 2);
            core.WriteInt32Le(b + 21, Auto4, 2);
        }
    }
}
