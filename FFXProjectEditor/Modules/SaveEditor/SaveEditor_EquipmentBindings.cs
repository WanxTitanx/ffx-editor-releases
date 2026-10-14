using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Save;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.SaveEditor
{
    internal partial class SaveEditor_EquipmentBindings : ObservableObject
    {
        FfxSaveEquipmentSnapshot snapshot = null!;
        int slotBase;

        public ObservableCollection<string> EquipCharacterLabels { get; } = new();
        public ObservableCollection<string> WeaponLabels { get; } = new();
        public ObservableCollection<string> EquippedOnLabels { get; } = new();
        public ObservableCollection<string> AppearanceLabels { get; } = new();
        public ObservableCollection<string> DamageFormulaLabels { get; } = new();
        public ObservableCollection<string> AutoCapacityLabels { get; } = new();
        public ObservableCollection<string> AutoAbilityLabels { get; } = new();

        [ObservableProperty] private bool occupied;
        [ObservableProperty] private int equipCharacterIndex;
        [ObservableProperty] private int weaponIndex;
        [ObservableProperty] private int equippedOnIndex;
        [ObservableProperty] private int type;
        [ObservableProperty] private int appearanceIndex;
        [ObservableProperty] private int damageFormulaIndex;
        [ObservableProperty] private int attackPower;
        [ObservableProperty] private int critical;
        [ObservableProperty] private int autoCapacityIndex;
        [ObservableProperty] private int auto1Index;
        [ObservableProperty] private int auto2Index;
        [ObservableProperty] private int auto3Index;
        [ObservableProperty] private int auto4Index;

        public static SaveEditor_EquipmentBindings Load(FfxSaveCore core, FfxSaveEquipmentSnapshot snap)
        {
            var row = new SaveEditor_EquipmentBindings
            {
                snapshot = snap,
                slotBase = snap.SlotBase,
                Occupied = snap.Occupied,
                Type = snap.Type,
                AttackPower = snap.AttackPower,
                Critical = snap.Critical,
            };

            PopulateLabels(FfxSaveRegistry.EquipCharacterCatalog, row.EquipCharacterLabels);
            PopulateLabels(FfxSaveRegistry.EquippedOnCatalog, row.EquippedOnLabels);
            PopulateLabels(FfxSaveRegistry.AppearanceCatalog, row.AppearanceLabels);
            PopulateLabels(FfxSaveRegistry.DamageFormulaCatalog, row.DamageFormulaLabels);
            PopulateLabels(FfxSaveRegistry.AutoCapacityCatalog, row.AutoCapacityLabels);
            PopulateLabels(FfxSaveRegistry.AutoAbilityCatalog, row.AutoAbilityLabels);

            row.EquipCharacterIndex = CatalogIndexFromByte(core, snap.SlotBase + 4, FfxSaveRegistry.EquipCharacterCatalog, snap.EquipCharacter);
            row.RefreshWeaponLabels();
            row.WeaponIndex = row.ReadWeaponIndex(core);
            row.EquippedOnIndex = CatalogIndexFromSignedByte(core, snap.SlotBase + 6, FfxSaveRegistry.EquippedOnCatalog, snap.EquippedOn);
            row.AppearanceIndex = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 13, 2, FfxSaveRegistry.AppearanceCatalog));
            row.DamageFormulaIndex = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 8, 1, FfxSaveRegistry.DamageFormulaCatalog));
            row.AutoCapacityIndex = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 11, 1, FfxSaveRegistry.AutoCapacityCatalog));
            row.Auto1Index = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 15, 2, FfxSaveRegistry.AutoAbilityCatalog));
            row.Auto2Index = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 17, 2, FfxSaveRegistry.AutoAbilityCatalog));
            row.Auto3Index = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 19, 2, FfxSaveRegistry.AutoAbilityCatalog));
            row.Auto4Index = Math.Max(0, FfxSaveCatalogCodec.ReadCatalogIndex(core, snap.SlotBase + 21, 2, FfxSaveRegistry.AutoAbilityCatalog));

            return row;
        }

        public void Write(FfxSaveCore core, FfxSaveEquipmentSnapshot snap)
        {
            int b = slotBase;
            core.Data[b + 2] = (byte)(Occupied ? 1 : 0);
            WriteCatalogByte(core, b + 4, FfxSaveRegistry.EquipCharacterCatalog, EquipCharacterIndex);
            WriteWeapon(core);
            core.Data[b + 5] = (byte)Type;
            WriteCatalogByte(core, b + 6, FfxSaveRegistry.EquippedOnCatalog, EquippedOnIndex);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 8, FfxSaveRegistry.DamageFormulaCatalog, DamageFormulaIndex);
            core.Data[b + 9] = (byte)AttackPower;
            core.Data[b + 10] = (byte)Critical;
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 11, FfxSaveRegistry.AutoCapacityCatalog, AutoCapacityIndex);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 13, FfxSaveRegistry.AppearanceCatalog, AppearanceIndex);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 15, FfxSaveRegistry.AutoAbilityCatalog, Auto1Index);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 17, FfxSaveRegistry.AutoAbilityCatalog, Auto2Index);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 19, FfxSaveRegistry.AutoAbilityCatalog, Auto3Index);
            FfxSaveCatalogCodec.WriteCatalogBytes(core, b + 21, FfxSaveRegistry.AutoAbilityCatalog, Auto4Index);

            snap.Occupied = Occupied;
            snap.EquipCharacter = ReadEquipCharacterByte(core);
            snap.AppearanceIndex = core.Data[b];
            snap.EquippedOn = (sbyte)core.Data[b + 6];
            snap.Type = Type;
            snap.DamageFormula = core.Data[b + 8];
            snap.AttackPower = AttackPower;
            snap.Critical = Critical;
            snap.AutoCapacity = core.Data[b + 11];
            snap.Auto1 = core.ReadInt32Le(b + 15, 2);
            snap.Auto2 = core.ReadInt32Le(b + 17, 2);
            snap.Auto3 = core.ReadInt32Le(b + 19, 2);
            snap.Auto4 = core.ReadInt32Le(b + 21, 2);
        }

        partial void OnEquipCharacterIndexChanged(int value) => RefreshWeaponLabels();

        void RefreshWeaponLabels()
        {
            WeaponLabels.Clear();
            int charIdx = EquipCharacterIndex < FfxSaveRegistry.EquipCharacterCatalog.Count
                ? FfxSaveRegistry.EquipCharacterCatalog[EquipCharacterIndex].Bytes.FirstOrDefault()
                : 7;
            foreach (FfxSaveCatalogEntry entry in FfxSaveRegistry.GetWeaponCatalogForCharacter(charIdx))
                WeaponLabels.Add(entry.Label);
            if (WeaponIndex >= WeaponLabels.Count)
                WeaponIndex = Math.Max(0, WeaponLabels.Count - 1);
        }

        int ReadWeaponIndex(FfxSaveCore core)
        {
            int charIdx = EquipCharacterIndex < FfxSaveRegistry.EquipCharacterCatalog.Count
                ? FfxSaveRegistry.EquipCharacterCatalog[EquipCharacterIndex].Bytes.FirstOrDefault()
                : 7;
            var catalog = FfxSaveRegistry.GetWeaponCatalogForCharacter(charIdx);
            int fromBytes = FfxSaveCatalogCodec.ReadCatalogIndex(core, slotBase + 1, 2, catalog);
            if (fromBytes >= 0)
                return fromBytes;
            int idx = core.Data[slotBase];
            return idx < catalog.Count ? idx : 0;
        }

        void WriteWeapon(FfxSaveCore core)
        {
            int charIdx = EquipCharacterIndex < FfxSaveRegistry.EquipCharacterCatalog.Count
                ? FfxSaveRegistry.EquipCharacterCatalog[EquipCharacterIndex].Bytes.FirstOrDefault()
                : 7;
            var catalog = FfxSaveRegistry.GetWeaponCatalogForCharacter(charIdx);
            if (WeaponIndex < 0 || WeaponIndex >= catalog.Count)
                return;

            core.Data[slotBase] = (byte)WeaponIndex;
            FfxSaveCatalogCodec.WriteCatalogBytes(core, slotBase + 1, catalog, WeaponIndex);
        }

        int ReadEquipCharacterByte(FfxSaveCore core) =>
            EquipCharacterIndex < FfxSaveRegistry.EquipCharacterCatalog.Count
                ? FfxSaveRegistry.EquipCharacterCatalog[EquipCharacterIndex].Bytes.FirstOrDefault()
                : core.Data[slotBase + 4];

        static void PopulateLabels(IReadOnlyList<FfxSaveCatalogEntry> catalog, ObservableCollection<string> target)
        {
            target.Clear();
            foreach (FfxSaveCatalogEntry entry in catalog)
                target.Add(entry.Label);
        }

        static int CatalogIndexFromByte(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int fallback)
        {
            int idx = FfxSaveCatalogCodec.ReadCatalogIndex(core, offset, 1, catalog);
            if (idx >= 0)
                return idx;

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Bytes.Length > 0 && catalog[i].Bytes[0] == fallback)
                    return i;
            }

            return 0;
        }

        static int CatalogIndexFromSignedByte(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int fallback)
        {
            sbyte raw = (sbyte)core.Data[offset];
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Bytes.Length > 0 && (sbyte)catalog[i].Bytes[0] == raw)
                    return i;
            }

            return Math.Clamp(fallback + 1, 0, Math.Max(0, catalog.Count - 1));
        }

        static void WriteCatalogByte(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int index)
        {
            if (index < 0 || index >= catalog.Count)
                return;

            int[] bytes = catalog[index].Bytes;
            core.Data[offset] = bytes.Length > 0 ? (byte)bytes[0] : (byte)0;
        }
    }
}
