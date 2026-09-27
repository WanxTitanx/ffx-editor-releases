using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Save;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.SaveEditor
{
    internal partial class SaveEditor_CharacterBitRow : ObservableObject
    {
        readonly int fieldBaseOffset;
        readonly int fieldBitIndex;
        readonly int characterStrideAdd;

        public string Label { get; }

        [ObservableProperty] private bool isSet;

        public SaveEditor_CharacterBitRow(string label, int fieldBaseOffset, int fieldBitIndex, int characterIndex)
        {
            Label = label;
            this.fieldBaseOffset = fieldBaseOffset;
            this.fieldBitIndex = fieldBitIndex;
            characterStrideAdd = FfxSaveCharacterOffsets.CharacterStride * characterIndex;
        }

        public void Load(FfxSaveCore core) =>
            IsSet = core.ReadSaveBit(fieldBaseOffset, fieldBitIndex, characterStrideAdd);

        public void Write(FfxSaveCore core) =>
            core.WriteSaveBit(fieldBaseOffset, fieldBitIndex, IsSet, characterStrideAdd);
    }

    internal partial class SaveEditor_CharacterOdModeRow : ObservableObject
    {
        readonly int bitOffset;
        readonly int bit;
        readonly int countOffset;
        readonly int characterStrideAdd;

        public string Label { get; }

        [ObservableProperty] private bool enabled;
        [ObservableProperty] private string count = "0";

        public SaveEditor_CharacterOdModeRow(string label, int bitOffset, int bit, int countOffset, int characterIndex)
        {
            Label = label;
            this.bitOffset = bitOffset;
            this.bit = bit;
            this.countOffset = countOffset;
            characterStrideAdd = FfxSaveCharacterOffsets.CharacterStride * characterIndex;
        }

        public void Load(FfxSaveCore core)
        {
            Enabled = core.ReadSaveBit(bitOffset, bit, characterStrideAdd);
            Count = core.ReadInt32Le(countOffset + characterStrideAdd, 2).ToString();
        }

        public void Write(FfxSaveCore core)
        {
            core.WriteSaveBit(bitOffset, bit, Enabled, characterStrideAdd);
            if (int.TryParse(Count, out int value))
                core.WriteInt32Le(countOffset + characterStrideAdd, value, 2);
        }
    }

    internal partial class SaveEditor_PartySlotRow : ObservableObject
    {
        readonly int slotIndex;

        public string Label => $"Slot {slotIndex + 1}";

        public ObservableCollection<string>? MemberLabels { get; set; }

        [ObservableProperty] private int selectedIndex;

        public SaveEditor_PartySlotRow(int slotIndex) => this.slotIndex = slotIndex;

        public void Load(FfxSaveCore core)
        {
            int offset = FfxSaveCharacterFieldCatalog.PartySlotOffsetBase + slotIndex;
            SelectedIndex = SaveEditor_CharacterBindings.CatalogIndexFromBytePublic(
                core, offset, FfxSaveCharacterFieldCatalog.PartyMemberCatalog, core.Data[offset]);
        }

        public void Write(FfxSaveCore core)
        {
            SaveEditor_CharacterBindings.WriteCatalogBytePublic(
                core, FfxSaveCharacterFieldCatalog.PartySlotOffsetBase + slotIndex,
                FfxSaveCharacterFieldCatalog.PartyMemberCatalog, SelectedIndex);
        }
    }

    internal partial class SaveEditor_CharacterBindings : ObservableObject
    {
        FfxSaveCharacterSnapshot snapshot = null!;
        int characterIndex;

        public ObservableCollection<string> CharacterLabels { get; } = new();
        public ObservableCollection<string> ActivationLabels { get; } = new();
        public ObservableCollection<string> OverdriveModeLabels { get; } = new();
        public ObservableCollection<string> PartyMemberLabels { get; } = new();
        public ObservableCollection<SaveEditor_CharacterBitRow> Abilities { get; } = new();
        public ObservableCollection<SaveEditor_CharacterOdModeRow> OverdriveModes { get; } = new();
        public ObservableCollection<SaveEditor_CharacterBitRow> Overdrives { get; } = new();
        public ObservableCollection<SaveEditor_CharacterBitRow> SpecialAbilities { get; } = new();
        public ObservableCollection<SaveEditor_PartySlotRow> PartySlots { get; } = new();

        [ObservableProperty] private int selectedCharacterIndex;
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private int activationIndex;
        [ObservableProperty] private int overdriveModeIndex;
        [ObservableProperty] private int baseHp;
        [ObservableProperty] private int baseMp;
        [ObservableProperty] private int baseStrength;
        [ObservableProperty] private int baseDefense;
        [ObservableProperty] private int baseMagic;
        [ObservableProperty] private int baseMagicDefense;
        [ObservableProperty] private int baseAgility;
        [ObservableProperty] private int baseLuck;
        [ObservableProperty] private int baseEvasion;
        [ObservableProperty] private int baseAccuracy;
        [ObservableProperty] private int currentHp;
        [ObservableProperty] private int currentMp;
        [ObservableProperty] private int abilityPoints;
        [ObservableProperty] private int sphereLevel;
        [ObservableProperty] private int sphereLevelMax;
        [ObservableProperty] private int overdriveGauge;
        [ObservableProperty] private int overdriveGaugeMax;
        [ObservableProperty] private int enemiesDefeated;
        [ObservableProperty] private int poisonDamagePercent;
        [ObservableProperty] private int affection;

        public bool ShowAffection => characterIndex < 7;

        public static SaveEditor_CharacterBindings Load(FfxSaveCore core, FfxSaveCharacterSnapshot snap)
        {
            var row = new SaveEditor_CharacterBindings
            {
                snapshot = snap,
                characterIndex = snap.Index,
                selectedCharacterIndex = snap.Index,
            };

            foreach (string label in FfxSaveCharacterOffsets.CharacterNames)
                row.CharacterLabels.Add(label);

            PopulateLabels(FfxSaveCharacterFieldCatalog.ActivationCatalog, row.ActivationLabels);
            PopulateLabels(FfxSaveRegistry.GetCatalog("c0007hArr5"), row.OverdriveModeLabels);
            PopulateLabels(FfxSaveCharacterFieldCatalog.PartyMemberCatalog, row.PartyMemberLabels);

            row.LoadScalars(core, snap);
            row.LoadParty(core);
            row.LoadBitGroups(core);
            return row;
        }

        public void ReloadForCharacter(FfxSaveCore core, FfxSaveCharacterSnapshot snap)
        {
            snapshot = snap;
            characterIndex = snap.Index;
            SelectedCharacterIndex = snap.Index;
            LoadScalars(core, snap);
            LoadBitGroups(core);
        }

        void LoadScalars(FfxSaveCore core, FfxSaveCharacterSnapshot snap)
        {
            int stride = FfxSaveCharacterOffsets.CharacterStride * characterIndex;
            Name = snap.Name;
            BaseHp = snap.BaseHp;
            BaseMp = snap.BaseMp;
            BaseStrength = snap.BaseStrength;
            BaseDefense = snap.BaseDefense;
            BaseMagic = snap.BaseMagic;
            BaseMagicDefense = snap.BaseMagicDefense;
            BaseAgility = snap.BaseAgility;
            BaseLuck = snap.BaseLuck;
            BaseEvasion = snap.BaseEvasion;
            BaseAccuracy = snap.BaseAccuracy;
            CurrentHp = snap.CurrentHp;
            CurrentMp = snap.CurrentMp;
            AbilityPoints = snap.AbilityPoints;
            SphereLevel = snap.SphereLevel;
            SphereLevelMax = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(22088, characterIndex), 1);
            OverdriveGauge = snap.OverdriveGauge;
            OverdriveGaugeMax = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(22086, characterIndex), 1);
            EnemiesDefeated = snap.EnemiesDefeated;
            PoisonDamagePercent = snap.PoisonDamagePercent;
            Affection = snap.Affection;
            ActivationIndex = CatalogIndexFromByte(core, FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.Activation, characterIndex),
                FfxSaveCharacterFieldCatalog.ActivationCatalog, snap.Activation);
            OverdriveModeIndex = CatalogIndexFromByte(core, FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveMode, characterIndex),
                FfxSaveRegistry.GetCatalog("c0007hArr5"), snap.OverdriveMode);
        }

        void LoadParty(FfxSaveCore core)
        {
            if (PartySlots.Count == 0)
            {
                for (int slot = 0; slot < FfxSaveCharacterFieldCatalog.PartySlotUiCount; slot++)
                    PartySlots.Add(new SaveEditor_PartySlotRow(slot));
            }

            foreach (SaveEditor_PartySlotRow row in PartySlots)
            {
                row.MemberLabels = PartyMemberLabels;
                row.Load(core);
            }
        }

        void LoadBitGroups(FfxSaveCore core)
        {
            ReloadCollection(Abilities, FfxSaveCharacterFieldCatalog.AbilityFields, core);
            ReloadOdModes(core);
            ReloadCollection(Overdrives, FfxSaveCharacterFieldCatalog.OverdriveFields, core);
            ReloadCollection(SpecialAbilities, FfxSaveCharacterFieldCatalog.SpecialAbilityFields, core);
        }

        void ReloadCollection(ObservableCollection<SaveEditor_CharacterBitRow> target, IEnumerable<FfxSaveRegistryField> fields, FfxSaveCore core)
        {
            if (target.Count == 0)
            {
                foreach (FfxSaveRegistryField field in fields)
                {
                    var row = new SaveEditor_CharacterBitRow(field.Label, field.Offset, field.Bit, characterIndex);
                    row.Load(core);
                    target.Add(row);
                }
                return;
            }

            int i = 0;
            foreach (FfxSaveRegistryField field in fields)
            {
                if (i >= target.Count)
                    break;
                var row = target[i];
                if (row.Label != field.Label)
                {
                    target.Clear();
                    ReloadCollection(target, fields, core);
                    return;
                }
                row.Load(core);
                i++;
            }
        }

        void ReloadOdModes(FfxSaveCore core)
        {
            if (OverdriveModes.Count == 0)
            {
                foreach (var (label, bitOffset, bit, countOffset) in FfxSaveCharacterFieldCatalog.OverdriveModeFields)
                {
                    var row = new SaveEditor_CharacterOdModeRow(label, bitOffset, bit, countOffset, characterIndex);
                    row.Load(core);
                    OverdriveModes.Add(row);
                }
                return;
            }

            for (int i = 0; i < OverdriveModes.Count; i++)
                OverdriveModes[i].Load(core);
        }

        public void Write(FfxSaveCore core, FfxSaveCharacterSnapshot snap)
        {
            int index = characterIndex;
            snap.BaseHp = BaseHp;
            snap.BaseMp = BaseMp;
            snap.BaseStrength = BaseStrength;
            snap.BaseDefense = BaseDefense;
            snap.BaseMagic = BaseMagic;
            snap.BaseMagicDefense = BaseMagicDefense;
            snap.BaseAgility = BaseAgility;
            snap.BaseLuck = BaseLuck;
            snap.BaseEvasion = BaseEvasion;
            snap.BaseAccuracy = BaseAccuracy;
            snap.CurrentHp = CurrentHp;
            snap.CurrentMp = CurrentMp;
            snap.AbilityPoints = AbilityPoints;
            snap.SphereLevel = SphereLevel;
            snap.OverdriveGauge = OverdriveGauge;
            snap.EnemiesDefeated = EnemiesDefeated;
            snap.PoisonDamagePercent = PoisonDamagePercent;
            snap.Affection = Affection;

            WriteCatalogByte(core, FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.Activation, index),
                FfxSaveCharacterFieldCatalog.ActivationCatalog, ActivationIndex, out int activationByte);
            snap.Activation = activationByte;

            WriteCatalogByte(core, FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveMode, index),
                FfxSaveRegistry.GetCatalog("c0007hArr5"), OverdriveModeIndex, out int odModeByte);
            snap.OverdriveMode = odModeByte;

            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseHp, index), BaseHp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMp, index), BaseMp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseStrength, index), BaseStrength, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseDefense, index), BaseDefense, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagic, index), BaseMagic, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagicDefense, index), BaseMagicDefense, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAgility, index), BaseAgility, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseLuck, index), BaseLuck, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseEvasion, index), BaseEvasion, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAccuracy, index), BaseAccuracy, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentHp, index), CurrentHp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentMp, index), CurrentMp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.AbilityPoints, index), AbilityPoints, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.SphereLevel, index), SphereLevel, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(22088, index), SphereLevelMax, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveGauge, index), OverdriveGauge, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(22086, index), OverdriveGaugeMax, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.EnemiesDefeated, index), EnemiesDefeated, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.PoisonDamagePercent, index), PoisonDamagePercent, 1);
            core.WriteFfxString(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CharacterName, index), Name);

            if (index < 7)
                core.WriteInt32Le(FfxSaveCharacterOffsets.AffectionBase + (index << 2), Affection, 4);

            for (int slot = 0; slot < PartySlots.Count; slot++)
                PartySlots[slot].Write(core);

            foreach (SaveEditor_CharacterBitRow row in Abilities)
                row.Write(core);
            foreach (SaveEditor_CharacterOdModeRow row in OverdriveModes)
                row.Write(core);
            foreach (SaveEditor_CharacterBitRow row in Overdrives)
                row.Write(core);
            foreach (SaveEditor_CharacterBitRow row in SpecialAbilities)
                row.Write(core);
        }

        partial void OnSelectedCharacterIndexChanged(int value)
        {
            if (value == characterIndex)
                return;

            CharacterIndexChangeHandler?.Invoke(value);
        }

        public Action<int>? CharacterIndexChangeHandler { get; set; }

        static void PopulateLabels(IReadOnlyList<FfxSaveCatalogEntry> catalog, ObservableCollection<string> target)
        {
            target.Clear();
            foreach (FfxSaveCatalogEntry entry in catalog)
                target.Add(entry.Label);
        }

        public static int CatalogIndexFromBytePublic(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int fallback) =>
            CatalogIndexFromByte(core, offset, catalog, fallback);

        public static void WriteCatalogBytePublic(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int index) =>
            WriteCatalogByte(core, offset, catalog, index, out _);

        static int CatalogIndexFromByte(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int fallback)
        {
            byte raw = core.Data[offset];
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Bytes.Length > 0 && (sbyte)catalog[i].Bytes[0] == (sbyte)raw)
                    return i;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Bytes.Length > 0 && catalog[i].Bytes[0] == fallback)
                    return i;
            }

            return 0;
        }

        static void WriteCatalogByte(FfxSaveCore core, int offset, IReadOnlyList<FfxSaveCatalogEntry> catalog, int index, out int written)
        {
            written = 0;
            if (index < 0 || index >= catalog.Count)
                return;

            int[] bytes = catalog[index].Bytes;
            core.Data[offset] = bytes.Length > 0 ? (byte)bytes[0] : (byte)0;
            written = bytes.Length > 0 ? bytes[0] : 0;
        }
    }
}
