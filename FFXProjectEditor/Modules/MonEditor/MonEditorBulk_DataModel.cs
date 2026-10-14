using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal partial class MonEditorBulk_DataModel : ObservableObject
    {
        readonly MonEditorSelector_DataModel selectorDM;

        public ObservableCollection<MonEditorSelector_DataModel.MonsterListEntry> SelectedMonsterEntries { get; } = new();
        public ObservableCollection<AbilitySlotOption> AbilitySlotOptions { get; } = new();
        public ObservableCollection<MonEditorSelector_DataModel.MonsterListEntry> SourceMonsterOptions { get; }

        [ObservableProperty] private string selectionSummary = "No monsters are staged for bulk editing.";
        [ObservableProperty] private string lastOperationSummary = "No bulk write executed yet.";
        [ObservableProperty] private GameIndex_Wrapper pendingAbility = GameIndex_Wrapper.Wrap(0);
        [ObservableProperty] private AbilitySlotOption? selectedAbilitySlot;
        [ObservableProperty] private MonEditorSelector_DataModel.MonsterListEntry? selectedSourceMonster;

        [ObservableProperty] private bool copyMenuAbilities = true;
        [ObservableProperty] private bool copyProperties;
        [ObservableProperty] private bool copyElementalWeaknesses;
        [ObservableProperty] private bool copyStatusResistances;
        [ObservableProperty] private bool copyAutoStatusesAndImmunities;
        [ObservableProperty] private bool copyIdentifiers;
        [ObservableProperty] private bool copyLootEconomyAndDrops;
        [ObservableProperty] private bool copyLootStealAndBribe;
        [ObservableProperty] private bool copyLootGearRewards;

        public List<string> CategoryOptions => new GameCategory_Converter().Options.Values.ToList();
        public int SelectedMonsterCount => SelectedMonsterEntries.Count;
        public bool HasSelectedMonsters => SelectedMonsterEntries.Count > 0;
        public bool HasCopyBlockSelection =>
            CopyMenuAbilities ||
            CopyProperties ||
            CopyElementalWeaknesses ||
            CopyStatusResistances ||
            CopyAutoStatusesAndImmunities ||
            CopyIdentifiers ||
            CopyLootEconomyAndDrops ||
            CopyLootStealAndBribe ||
            CopyLootGearRewards;

        public string CopyBlocksSummary => BuildCopyBlocksSummary();

        public MonEditorBulk_DataModel(MonEditorSelector_DataModel selectorDM)
        {
            this.selectorDM = selectorDM;
            SourceMonsterOptions = selectorDM.LoadedMonsters;

            BuildAbilitySlotOptions();
            SelectedAbilitySlot = AbilitySlotOptions.FirstOrDefault();

            selectorDM.BatchSelectionChanged += SelectorDM_BatchSelectionChanged;
            RefreshSelection();
        }

        partial void OnCopyMenuAbilitiesChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyPropertiesChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyElementalWeaknessesChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyStatusResistancesChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyAutoStatusesAndImmunitiesChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyIdentifiersChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyLootEconomyAndDropsChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyLootStealAndBribeChanged(bool value) => NotifyCopySelectionChanged();
        partial void OnCopyLootGearRewardsChanged(bool value) => NotifyCopySelectionChanged();

        void SelectorDM_BatchSelectionChanged()
        {
            RefreshSelection();
        }

        public void ApplyPendingAbilityToSelected()
        {
            if (!HasSelectedMonsters || SelectedAbilitySlot == null)
            {
                LastOperationSummary = "Stage at least one monster and pick a target slot before applying a batch ability.";
                return;
            }

            ushort packedAbility = PendingAbility.Unwrap();
            string abilityName = ResolveAbilityDisplayName(PendingAbility);
            int successCount = 0;

            foreach (MonEditorSelector_DataModel.MonsterListEntry entry in SelectedMonsterEntries)
            {
                Monster_File? monsterFile = TryLoadMonsterFile(entry, out string monsterPath);
                if (monsterFile?.StatSheetFile == null)
                {
                    continue;
                }

                ApplyAbilityToStatSheet(monsterFile.StatSheetFile, SelectedAbilitySlot, packedAbility);
                File.WriteAllBytes(monsterPath, monsterFile.Write());
                successCount++;
            }

            LastOperationSummary = successCount == 0
                ? "No monster stat sheets were writable for the requested batch ability operation."
                : $"Applied {SelectedAbilitySlot.DisplayLabel} = {abilityName} ({packedAbility:X4}h) across {successCount} monster(s).";
        }

        public void ClearPendingAbilitySlotForSelected()
        {
            if (!HasSelectedMonsters || SelectedAbilitySlot == null)
            {
                LastOperationSummary = "Stage at least one monster and pick a target slot before clearing a batch ability.";
                return;
            }

            int successCount = 0;

            foreach (MonEditorSelector_DataModel.MonsterListEntry entry in SelectedMonsterEntries)
            {
                Monster_File? monsterFile = TryLoadMonsterFile(entry, out string monsterPath);
                if (monsterFile?.StatSheetFile == null)
                {
                    continue;
                }

                ApplyAbilityToStatSheet(monsterFile.StatSheetFile, SelectedAbilitySlot, 0);
                File.WriteAllBytes(monsterPath, monsterFile.Write());
                successCount++;
            }

            LastOperationSummary = successCount == 0
                ? "No monster stat sheets were writable for the clear-slot operation."
                : $"Cleared {SelectedAbilitySlot.DisplayLabel} across {successCount} monster(s).";
        }

        public void ApplySourceBlocksToSelected()
        {
            if (!HasSelectedMonsters)
            {
                LastOperationSummary = "Stage at least one monster before copying blocks.";
                return;
            }

            if (SelectedSourceMonster == null)
            {
                LastOperationSummary = "Pick a source monster before copying stat-sheet blocks.";
                return;
            }

            if (!HasCopyBlockSelection)
            {
                LastOperationSummary = "Enable at least one copy block before running the bulk copy.";
                return;
            }

            Monster_File? sourceFile = TryLoadMonsterFile(SelectedSourceMonster, out _);
            if (sourceFile?.StatSheetFile == null)
            {
                LastOperationSummary = $"Could not load a stat sheet from source monster {SelectedSourceMonster.Name}.";
                return;
            }

            int successCount = 0;

            foreach (MonEditorSelector_DataModel.MonsterListEntry entry in SelectedMonsterEntries)
            {
                Monster_File? targetFile = TryLoadMonsterFile(entry, out string monsterPath);
                if (targetFile?.StatSheetFile == null)
                {
                    continue;
                }

                CopySelectedBlocks(sourceFile, targetFile);
                File.WriteAllBytes(monsterPath, targetFile.Write());
                successCount++;
            }

            LastOperationSummary = successCount == 0
                ? "No writable monster stat sheets were found for the bulk copy operation."
                : $"Copied {BuildCopyBlockNamesOnly()} from {SelectedSourceMonster.Name} into {successCount} monster(s).";
        }

        void RefreshSelection()
        {
            SelectedMonsterEntries.Clear();
            foreach (MonEditorSelector_DataModel.MonsterListEntry entry in selectorDM.GetBatchSelectedMonsters())
            {
                SelectedMonsterEntries.Add(entry);
            }

            if (SelectedSourceMonster == null || !SourceMonsterOptions.Contains(SelectedSourceMonster))
            {
                SelectedSourceMonster = SelectedMonsterEntries.FirstOrDefault() ?? SourceMonsterOptions.FirstOrDefault();
            }

            if (!HasSelectedMonsters)
            {
                SelectionSummary = Strings.F2_no_monsters_staged_use_the_checkboxes_on_76e4f6b3;
            }
            else
            {
                string preview = string.Join(", ", SelectedMonsterEntries.Take(4).Select(entry => entry.ShortName));
                if (SelectedMonsterEntries.Count > 4)
                {
                    preview += $", +{SelectedMonsterEntries.Count - 4} more";
                }

                SelectionSummary = $"{SelectedMonsterEntries.Count} staged monster(s): {preview}";
            }

            OnPropertyChanged(nameof(SelectedMonsterCount));
            OnPropertyChanged(nameof(HasSelectedMonsters));
            OnPropertyChanged(nameof(CopyBlocksSummary));
        }

        void BuildAbilitySlotOptions()
        {
            AbilitySlotOptions.Clear();
            AbilitySlotOptions.Add(new AbilitySlotOption(-1, true, "Forced Ability"));

            for (int i = 0; i < 16; i++)
            {
                AbilitySlotOptions.Add(new AbilitySlotOption(i, false, $"Ability Slot {i + 1}"));
            }
        }

        void NotifyCopySelectionChanged()
        {
            OnPropertyChanged(nameof(CopyBlocksSummary));
            OnPropertyChanged(nameof(HasCopyBlockSelection));
        }

        string BuildCopyBlocksSummary()
        {
            List<string> blocks = new();

            if (CopyMenuAbilities) blocks.Add("menu abilities");
            if (CopyProperties) blocks.Add("properties");
            if (CopyElementalWeaknesses) blocks.Add("elemental weaknesses");
            if (CopyStatusResistances) blocks.Add("status resistances");
            if (CopyAutoStatusesAndImmunities) blocks.Add("auto statuses and immunities");
            if (CopyIdentifiers) blocks.Add("identifiers");
            if (CopyLootEconomyAndDrops) blocks.Add("loot economy / drops");
            if (CopyLootStealAndBribe) blocks.Add("loot steal / bribe");
            if (CopyLootGearRewards) blocks.Add("loot gear rewards");

            return blocks.Count == 0
                ? Strings.F2_no_safe_blocks_selected_54404968
                : $"Selected blocks: {string.Join(", ", blocks)}";
        }

        string BuildCopyBlockNamesOnly()
        {
            List<string> blocks = new();

            if (CopyMenuAbilities) blocks.Add("menu abilities");
            if (CopyProperties) blocks.Add("properties");
            if (CopyElementalWeaknesses) blocks.Add("elemental weaknesses");
            if (CopyStatusResistances) blocks.Add("status resistances");
            if (CopyAutoStatusesAndImmunities) blocks.Add("auto statuses and immunities");
            if (CopyIdentifiers) blocks.Add("identifiers");
            if (CopyLootEconomyAndDrops) blocks.Add("loot economy / drops");
            if (CopyLootStealAndBribe) blocks.Add("loot steal / bribe");
            if (CopyLootGearRewards) blocks.Add("loot gear rewards");

            return blocks.Count == 0 ? Strings.F2_no_blocks_dcbbbfd2 : string.Join(", ", blocks);
        }

        static void ApplyAbilityToStatSheet(Monster_StatSheet statSheet, AbilitySlotOption slot, ushort packedAbility)
        {
            if (slot.IsForced)
            {
                statSheet.ForcedAction = packedAbility;
                return;
            }

            if (slot.SlotIndex >= 0 && slot.SlotIndex < statSheet.Abilities.Length)
            {
                statSheet.Abilities[slot.SlotIndex] = packedAbility;
            }
        }

        static string ResolveAbilityDisplayName(GameIndex_Wrapper wrapper)
        {
            try
            {
                string name = FfxCommon_Util.GetGameIndexName(wrapper.Category, wrapper.Index);
                return string.IsNullOrWhiteSpace(name) ? "<EMPTY>" : name;
            }
            catch
            {
                return "<INVALID_CATEGORY>";
            }
        }

        void CopySelectedBlocks(Monster_File source, Monster_File target)
        {
            Monster_StatSheet sourceStatSheet = source.StatSheetFile;
            Monster_StatSheet targetStatSheet = target.StatSheetFile;

            if (CopyMenuAbilities)
            {
                targetStatSheet.ForcedAction = sourceStatSheet.ForcedAction;
                targetStatSheet.Abilities = sourceStatSheet.Abilities.ToArray();
            }

            if (CopyProperties)
            {
                targetStatSheet.Property_Flags = sourceStatSheet.Property_Flags;
                targetStatSheet.PoisonDamage = sourceStatSheet.PoisonDamage;
            }

            if (CopyElementalWeaknesses)
            {
                targetStatSheet.ElementalWeakness = CloneElementalWeakness(sourceStatSheet.ElementalWeakness);
            }

            if (CopyStatusResistances)
            {
                targetStatSheet.StatusResistance = CloneStatusByteList(sourceStatSheet.StatusResistance);
            }

            if (CopyAutoStatusesAndImmunities)
            {
                targetStatSheet.AutoStatus_Flags1 = sourceStatSheet.AutoStatus_Flags1;
                targetStatSheet.AutoStatus_Flags2 = sourceStatSheet.AutoStatus_Flags2;
                targetStatSheet.AutoStatus_Flags3 = sourceStatSheet.AutoStatus_Flags3;
                targetStatSheet.ExtraImmunities_Flags = sourceStatSheet.ExtraImmunities_Flags;
            }

            if (CopyIdentifiers)
            {
                targetStatSheet.MonsterId = sourceStatSheet.MonsterId;
                targetStatSheet.ModelId = sourceStatSheet.ModelId;
                targetStatSheet.CtbIconId = sourceStatSheet.CtbIconId;
                targetStatSheet.DoomCount = sourceStatSheet.DoomCount;
                targetStatSheet.ArenaId = sourceStatSheet.ArenaId;
                targetStatSheet.ArenaIdPadding = sourceStatSheet.ArenaIdPadding;
                targetStatSheet.Model2Id = sourceStatSheet.Model2Id;
            }

            if (source.LootFile == null || target.LootFile == null)
            {
                return;
            }

            if (CopyLootEconomyAndDrops)
            {
                target.LootFile.Gil = source.LootFile.Gil;
                target.LootFile.Ap = source.LootFile.Ap;
                target.LootFile.ApOverkill = source.LootFile.ApOverkill;
                target.LootFile.RonsoRageId = source.LootFile.RonsoRageId;
                target.LootFile.Drop1Chance = source.LootFile.Drop1Chance;
                target.LootFile.Drop2Chance = source.LootFile.Drop2Chance;
                target.LootFile.GearChance = source.LootFile.GearChance;
                target.LootFile.Drop1Id = source.LootFile.Drop1Id;
                target.LootFile.Drop1RareId = source.LootFile.Drop1RareId;
                target.LootFile.Drop2Id = source.LootFile.Drop2Id;
                target.LootFile.Drop2RareId = source.LootFile.Drop2RareId;
                target.LootFile.Drop1Count = source.LootFile.Drop1Count;
                target.LootFile.Drop1RareCount = source.LootFile.Drop1RareCount;
                target.LootFile.Drop2Count = source.LootFile.Drop2Count;
                target.LootFile.Drop2RareCount = source.LootFile.Drop2RareCount;
                target.LootFile.DropOverkillId = source.LootFile.DropOverkillId;
                target.LootFile.DropOverkillRareId = source.LootFile.DropOverkillRareId;
                target.LootFile.DropOverkill2Id = source.LootFile.DropOverkill2Id;
                target.LootFile.DropOverkill2RareId = source.LootFile.DropOverkill2RareId;
                target.LootFile.DropOverkillCount = source.LootFile.DropOverkillCount;
                target.LootFile.DropOverkillRareCount = source.LootFile.DropOverkillRareCount;
                target.LootFile.DropOverkill2Count = source.LootFile.DropOverkill2Count;
                target.LootFile.DropOverkill2RareCount = source.LootFile.DropOverkill2RareCount;
            }

            if (CopyLootStealAndBribe)
            {
                target.LootFile.StealChance = source.LootFile.StealChance;
                target.LootFile.StealId = source.LootFile.StealId;
                target.LootFile.StealRareId = source.LootFile.StealRareId;
                target.LootFile.StealCount = source.LootFile.StealCount;
                target.LootFile.StealRareCount = source.LootFile.StealRareCount;
                target.LootFile.BribeId = source.LootFile.BribeId;
                target.LootFile.BribeCount = source.LootFile.BribeCount;
            }

            if (CopyLootGearRewards)
            {
                target.LootFile.GearFormula = source.LootFile.GearFormula;
                target.LootFile.GearCrit = source.LootFile.GearCrit;
                target.LootFile.GearAttack = source.LootFile.GearAttack;
                target.LootFile.GearSlotCount = source.LootFile.GearSlotCount;
                target.LootFile.GearAbilityCount = source.LootFile.GearAbilityCount;
                target.LootFile.ZanmatoLevel = source.LootFile.ZanmatoLevel;
                target.LootFile.TidusAbilities = CloneLootGearAbilities(source.LootFile.TidusAbilities);
                target.LootFile.YunaAbilities = CloneLootGearAbilities(source.LootFile.YunaAbilities);
                target.LootFile.AuronAbilities = CloneLootGearAbilities(source.LootFile.AuronAbilities);
                target.LootFile.KimahriAbilities = CloneLootGearAbilities(source.LootFile.KimahriAbilities);
                target.LootFile.WakkaAbilities = CloneLootGearAbilities(source.LootFile.WakkaAbilities);
                target.LootFile.LuluAbilities = CloneLootGearAbilities(source.LootFile.LuluAbilities);
                target.LootFile.RikkuAbilities = CloneLootGearAbilities(source.LootFile.RikkuAbilities);
            }
        }

        static Monster_Loot.LootGearAbilities CloneLootGearAbilities(Monster_Loot.LootGearAbilities source)
        {
            Monster_Loot.LootGearAbilities clone = new();

            for (int i = 0; i < clone.WeaponAbilities.Length; i++)
            {
                clone.WeaponAbilities[i] = source.WeaponAbilities[i];
                clone.ArmorAbilities[i] = source.ArmorAbilities[i];
            }

            return clone;
        }

        static ElementalWeaknessData CloneElementalWeakness(ElementalWeaknessData source)
        {
            return new ElementalWeaknessData
            {
                Absorb = source.Absorb,
                Immune = source.Immune,
                Resist = source.Resist,
                Weak = source.Weak
            };
        }

        static StatusByteList CloneStatusByteList(StatusByteList source)
        {
            return new StatusByteList
            {
                Death = source.Death,
                Zombie = source.Zombie,
                Petrify = source.Petrify,
                Poison = source.Poison,
                BreakPower = source.BreakPower,
                BreakMagic = source.BreakMagic,
                BreakArmor = source.BreakArmor,
                BreakMental = source.BreakMental,
                Confuse = source.Confuse,
                Berserk = source.Berserk,
                Provoke = source.Provoke,
                Threaten = source.Threaten,
                Sleep = source.Sleep,
                Silence = source.Silence,
                Darkness = source.Darkness,
                Shell = source.Shell,
                Protect = source.Protect,
                Reflect = source.Reflect,
                NulTide = source.NulTide,
                NulBlaze = source.NulBlaze,
                NulShock = source.NulShock,
                NulFrost = source.NulFrost,
                Regen = source.Regen,
                Haste = source.Haste,
                Slow = source.Slow
            };
        }

        static Monster_File? TryLoadMonsterFile(MonEditorSelector_DataModel.MonsterListEntry entry, out string monsterPath)
        {
            monsterPath = Project_Service.Instance.GetPathMon(entry.Index);
            if (!File.Exists(monsterPath))
            {
                return null;
            }

            return Monster_File.Read(File.ReadAllBytes(monsterPath));
        }

        public sealed class AbilitySlotOption
        {
            public int SlotIndex { get; }
            public bool IsForced { get; }
            public string DisplayLabel { get; }

            public AbilitySlotOption(int slotIndex, bool isForced, string displayLabel)
            {
                SlotIndex = slotIndex;
                IsForced = isForced;
                DisplayLabel = displayLabel;
            }
        }
    }
}
