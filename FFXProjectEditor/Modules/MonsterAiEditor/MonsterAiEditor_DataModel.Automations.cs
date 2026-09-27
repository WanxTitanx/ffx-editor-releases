using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // 1-CLICK AUTOMATIONS surface — the "the software's AI assembles the bytecode" layer the owner asked for.
    // This partial is a THIN layperson-first VM: all the real logic lives in FfxLib/Ai/AiAutomation (pure +
    // gate-tested, --ai3). The free-edit AI Assembler + Behavior Library cards stay intact as the "advanced" mode;
    // a "modo técnico" toggle relabels the action list with the real opcodes/hex for nerdier users.
    //
    // HONESTY: the STRUCTURE of every automation is offline-proven (the AiFile re-parses, the walk closes, nothing
    // dangles + AiValidator double-checks). The in-game BEHAVIOUR of an ADDED ability depends on which entrypoint it
    // hooks (only the structure is byte-proven, 692/692), so AddAbility reports the auto-picked worker/entrypoint and
    // flags the result RT2-pending (confirm in-game via the probe). For fine control a nerd uses the Behavior Library.
    internal partial class MonsterAiEditor_DataModel
    {
        const byte ContextPushIiOpcode = 0xAE;
        const byte CallOpcode = 0xB5;
        const byte CallPopaOpcode = 0xD8;
        const ushort ReadChrPropertyOperand = 0x700F;
        const ushort UsedCommandOperand = 0x7019;
        const ushort RemoveCommandOperand = 0x7038;
        const ushort RunBtlSceneAOperand = 0x703C;
        const ushort RunBtlSceneBOperand = 0x7097;
        const ushort TalkCommandOperand = 0x3105;
        const ushort Special1CommandOperand = 0x6001;
        const ushort SeymourDismissAnimaOperand = 0x6051;
        const ushort LastDamageTakenHpFieldOperand = 0x00A6;

        // When ON, the friendly action list also shows the raw mnemonic/hex ("nome real" — for nerdier users).
        [ObservableProperty] private bool showTechnicalNames;
        partial void OnShowTechnicalNamesChanged(bool value) => RebuildAutomationActions();

        // ---- ➕ Add ability ----
        const ushort YunalescaNulAllCommand = 0x312D; // Character command.bin id 301: Cindy's real NulAll spell.
        // Friendly-labelled categories (ToString = Label). Strings.F2_all_6a720856 is the default fast lane; category-specific filters
        // remain for people who already know which game command table they want.
        public IReadOnlyList<AiAbilityCategoryChoice> AutomationAbilityCategories { get; } = new[]
        {
            new AiAbilityCategoryChoice(null, Strings.F2_all_character_monmagic1_monmagic2_93fb82e9),
            new AiAbilityCategoryChoice(AiCommandCategory.Character, Strings.U_Ai_CatCharacter),
            new AiAbilityCategoryChoice(AiCommandCategory.Monster,  Strings.U_Ai_CatMonster1),
            new AiAbilityCategoryChoice(AiCommandCategory.Monster2, Strings.U_Ai_CatMonster2),
        };
        public ObservableCollection<AiCommandOption> AutomationAbilityOptions { get; } = new();

        [ObservableProperty] private AiAbilityCategoryChoice? selectedAbilityCategory;
        [ObservableProperty] private AiCommandOption? selectedAutomationAbility;
        // Type-to-filter the (300+ entries) ability dropdown by name or hex — mirrors InstructionFilter.
        [ObservableProperty] private string abilitySearch = string.Empty;
        [ObservableProperty] private string abilityFilterSummary = string.Empty;
        [ObservableProperty] private bool automationWhenRandom;   // false = sempre, true = às vezes (1-em-K)
        [ObservableProperty] private string automationRandomK = "2";
        [ObservableProperty] private bool automationStopAfterAction;
        [ObservableProperty] private string addAbilitySummary =
            Strings.F2_choose_a_skill_target_and_condition_if_a_16a6d46e;
        [ObservableProperty] private string authoringRecipeSummary =
            Strings.F2_quick_presets_set_up_the_controls_below__87424fe3;

        public IReadOnlyList<AiTargetOption> AuthoringTargetOptions { get; } =
            new[]
            {
                new AiTargetOption(0, Strings.F2_random_living_frontline_simple_flan_b7c3e770, TargetRecipeKind: AiTargetRecipeKind.FindAliveFrontlineAny),
                new AiTargetOption(0, Strings.F2_lowest_hp_living_frontline_shred_hp_4ff59096, TargetRecipeKind: AiTargetRecipeKind.FindAliveFrontlineLowestHp),
            }
            .Concat(AiTargetNames.Standard.Select(t => new AiTargetOption(t.Operand, t.Label)))
            .ToList();
        [ObservableProperty] private AiTargetOption? selectedAuthoringTarget;

        // ---- ➖ Remove / edit action (commands + buffs/status + stat tunes, with a type filter — #6) ----
        public ObservableCollection<AiActionVm> AutomationActions { get; } = new();
        [ObservableProperty] private AiActionVm? selectedAutomationAction;
        [ObservableProperty] private bool canCopySelectedAction;
        partial void OnSelectedAutomationActionChanged(AiActionVm? value)
        {
            CanCopySelectedAction = value?.Action.Kind == AiActionKind.Command && value.Action.Removable;
            UpdateRenameAvailability();
            RebuildTargetOptions();
            RebuildBuffStatEditor();   // inline status/value editor for the selected buff/stat — see .InlineEdit.cs
            SyncBehaviorSelectionFromAction(value);   // highlight the matching "Como este monstro pensa" row — see .Behavior.cs
            UpdateCompositeActionPreview();
            OnPropertyChanged(nameof(CanEditSelectedIndirectDispatchUnit));
        }
        readonly List<AiDetectedAction> _allDetectedActions = new();
        readonly List<AiDetectedBranchAction> _allDetectedBranchActions = new();
        public ObservableCollection<AiDispatchTableRowVm> DispatchTableRows { get; } = new();
        public ObservableCollection<AiDispatchVarCorpusEntryVm> DispatchVarCorpusEntries { get; } = new();
        public ObservableCollection<AiSeymourTechNoteVm> SeymourSceneTechNotes { get; } = new();
        public ObservableCollection<AiIndirectDispatchUnitVm> IndirectDispatchUnits { get; } = new();
        public ObservableCollection<AiPhaseVarLinkedBlockVm> FineAiPhaseVariableBlocks { get; } = new();

        // Type filter for the action list (#6): show all, or just commands / buffs / stats.
        public IReadOnlyList<AiActionTypeFilter> AutomationActionFilters { get; } = new[]
        {
            new AiActionTypeFilter(Strings.F2_all_6a720856, null),
            new AiActionTypeFilter(Strings.U_Ai_FilterCommands, AiActionKind.Command),
            new AiActionTypeFilter(Strings.AiEditorFilterBuffs, AiActionKind.Buff),
            new AiActionTypeFilter(Strings.AiEditorFilterStats, AiActionKind.Stat),
        };
        [ObservableProperty] private AiActionTypeFilter? selectedActionFilter;
        partial void OnSelectedActionFilterChanged(AiActionTypeFilter? value) => ApplyActionFilter();

        [ObservableProperty] private string removeActionSummary =
            Strings.F2_lists_all_actions_the_monster_already_ex_51515c65;

        // ---- ✏ Renomear comando sem nome (rótulo amigável persistido — #7) ----
        [ObservableProperty] private string renameCommandLabel = string.Empty;
        [ObservableProperty] private bool canRenameSelected;
        [ObservableProperty] private string renameSummary =
            Strings.F2_give_a_friendly_name_to_a_command_id_wit_6188e099;

        // ---- 🎯 Mudar alvo (#8) — só 'si mesmo' é provado; os demais são alvos COPIADOS de outra ação deste monstro ----
        public ObservableCollection<AiTargetOption> TargetOptions { get; } = new();
        [ObservableProperty] private AiTargetOption? selectedTargetOption;
        [ObservableProperty] private bool canChangeTarget;
        [ObservableProperty] private string targetSummary =
            Strings.F2_changes_the_target_of_the_selected_comma_3e253488;

        static string CommandLabelsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "ai-command-labels.json");

        // ---- 🛡 Self-buff ----
        public IReadOnlyList<AiBuffPreset> SelfBuffPresets => AiAutomation.SelfBuffPresets;
        const ushort OverdriveAnyPositiveStatusField = 0xFFFF;
        public IReadOnlyList<AiBuffPreset> OverdriveSelfStatusPresets { get; } =
            new[] { new AiBuffPreset(Strings.F2_any_positive_status_a955c905, OverdriveAnyPositiveStatusField) }
                .Concat(AiAutomation.SelfBuffPresets)
                .ToList();
        [ObservableProperty] private AiBuffPreset selectedSelfBuff;
        [ObservableProperty] private string selfBuffSummary =
            Strings.F2_makes_the_monster_apply_a_status_to_itse_33e1c499;

        // ---- 🩸 Forbidden Rite ----
        public IReadOnlyList<AiTargetOption> ForbiddenRiteTargets { get; } = new[]
        {
            new AiTargetOption(0, Strings.F2_same_target_as_the_linked_skill_0edd0638, UseLinkedActionTarget: true),
            new AiTargetOption(0xFFFA, Strings.U_Ai_TargetChar1),
            new AiTargetOption(0xFFF9, Strings.U_Ai_TargetChar2),
            new AiTargetOption(0xFFF8, Strings.U_Ai_TargetChar3),
            new AiTargetOption(0xFFF2, Strings.F2_all_active_characters_frontlinechars_e1fa2f54),
            new AiTargetOption(0xFFFD, Strings.U_Ai_TargetCurrentAction),
            new AiTargetOption(0xFFFC, Strings.U_Ai_TargetImmediateAction),
            new AiTargetOption(0xFFEF, Strings.U_Ai_TargetLastAttacker),
            new AiTargetOption(0xFFF0, Strings.U_Ai_TargetPredefinedGroup),
        };
        public IReadOnlyList<AiForbiddenStatusPreset> ForbiddenStatusPresets { get; } = new[]
        {
            new AiForbiddenStatusPreset("Poison", 0x0005, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Petrify", 0x0006, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Zombie", 0x0007, Strings.U_Ai_Rt2ProvenVsRibbon),
            new AiForbiddenStatusPreset("Power Break", 0x0025, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Magic Break", 0x0026, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Armor Break", 0x0027, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Mental Break", 0x0028, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Confuse", 0x0029, Strings.U_Ai_Rt2ProvenVsRibbon),
            new AiForbiddenStatusPreset("Berserk", 0x002A, Strings.F2_tested_in_game_by_the_operator_anti_ribb_33410740),
            new AiForbiddenStatusPreset("Provoke", 0x002B, Strings.F2_advanced_direct_mapped_field_own_in_game_651af680),
            new AiForbiddenStatusPreset("Threaten", 0x002C, Strings.F2_advanced_direct_mapped_field_own_in_game_651af680),
            new AiForbiddenStatusPreset("Sleep", 0x002D, Strings.F2_duration_tested_in_game_by_the_operator__0eaac372, 255),
            new AiForbiddenStatusPreset("Silence", 0x002E, Strings.U_Ai_DurationRt2Proven, 255),
            new AiForbiddenStatusPreset("Darkness", 0x002F, Strings.U_Ai_DurationRt2Proven, 255),
            new AiForbiddenStatusPreset("Slow", 0x0039, Strings.F2_duration_tested_in_game_by_the_operator__0eaac372, 255),
            new AiForbiddenStatusPreset("Curse", 0x0099, Strings.U_Ai_Rt2ProvenVsRibbon),
            new AiForbiddenStatusPreset("Doom", 0x009D, Strings.U_Ai_Rt2ProvenCounters),
            new AiForbiddenStatusPreset("Doom counter init", 0x009F, Strings.U_Ai_Rt2ProvenDefault5, 5),
            new AiForbiddenStatusPreset("Doom counter current", 0x00A0, Strings.U_Ai_Rt2ProvenDefault5, 5),
        };
        [ObservableProperty] private AiTargetOption? selectedForbiddenRiteTarget;
        partial void OnSelectedForbiddenRiteTargetChanged(AiTargetOption? value)
        {
            UpdateForbiddenRiteLinkSummary();
            UpdateCompositeActionPreview();
        }
        [ObservableProperty] private AiForbiddenStatusPreset? selectedForbiddenStatus;
        partial void OnSelectedForbiddenStatusChanged(AiForbiddenStatusPreset? value)
        {
            if (value != null) ForbiddenStatusValue = value.DefaultValue.ToString(CultureInfo.InvariantCulture);
            UpdateForbiddenRiteLinkSummary();
            UpdateCompositeActionPreview();
        }
        [ObservableProperty] private string forbiddenStatusValue = "1";
        partial void OnForbiddenStatusValueChanged(string value)
        {
            UpdateForbiddenRiteLinkSummary();
            UpdateCompositeActionPreview();
        }
        [ObservableProperty] private bool linkForbiddenRiteToBehavior;
        partial void OnLinkForbiddenRiteToBehaviorChanged(bool value)
        {
            UpdateForbiddenRiteLinkSummary();
            UpdateCompositeActionPreview();
        }
        [ObservableProperty] private string forbiddenRiteLinkSummary =
            Strings.F2_optional_check_to_apply_forbidden_rite_r_b8b0f273;
        [ObservableProperty] private string forbiddenRiteSummary =
            Strings.F2_applies_anti_ribbon_status_directly_to_t_a99546f8;

        const ushort AiSelfTarget = 0xFFF3;

        readonly List<YunalescaConditionPart> _yunalescaConditions = new();

        [ObservableProperty] private string yunalescaHpPercentInput = "50";
        [ObservableProperty] private string yunalescaConditionBadge = Strings.F2_onturn_always_default_7d8317b0;
        [ObservableProperty] private string yunalescaConditionSummary =
            Strings.F2_build_conditions_in_the_add_swap_behavio_bce84ee5;
        [ObservableProperty] private string yunalescaConditionChain =
            Strings.F2_no_condition_chosen_the_default_is_ontur_05edb8c5;

        // ---- 📊 Field/stat plant ----
        readonly IReadOnlyList<AiFieldPlantOption> _allPlantFields = AiChrPropertyNames.Names
            .Select(kv => new AiFieldPlantOption(kv.Key, kv.Value))
            .OrderBy(o => FieldPlantRank(o.FieldId))
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        public ObservableCollection<AiFieldPlantOption> PlantFieldOptions { get; } = new();
        [ObservableProperty] private AiFieldPlantOption? selectedPlantField;
        [ObservableProperty] private string plantFieldSearch = string.Empty;
        [ObservableProperty] private string plantFieldValue = "1";
        [ObservableProperty] private string plantFieldSummary =
            Strings.F2_choose_any_btlactorproperty_and_insert_a_3ddcebed;

        // ---- 🔋 Overdrive gauge LAB ----
        [ObservableProperty] private string overdriveGaugeMax = "100";
        [ObservableProperty] private string overdriveGaugeCurrent = "100";
        [ObservableProperty] private string overdriveGaugeSummary =
            Strings.F2_installs_showoverdrivebar_overdrivemax_o_8ef56844;

        // ---- 🔋 Overdrive autoral LAB ----
        public IReadOnlyList<OverdriveStartModeChoice> OverdriveStartModeChoices { get; } = new[]
        {
            new OverdriveStartModeChoice(OverdriveStartModeKind.Empty, Strings.U_Ai_Empty),
            new OverdriveStartModeChoice(OverdriveStartModeKind.Full, Strings.U_Ai_Full),
            new OverdriveStartModeChoice(OverdriveStartModeKind.Custom, Strings.F2_custom_value_779ceaae),
        };
        public IReadOnlyList<OverdriveFinishModeChoice> OverdriveFinishModeChoices { get; } = new[]
        {
            new OverdriveFinishModeChoice(OverdriveFinishModeKind.Single, Strings.U_Ai_OdOneSkill),
            new OverdriveFinishModeChoice(OverdriveFinishModeKind.Sequence, Strings.U_Ai_OdSkillSequence),
        };
        public ObservableCollection<AiCommandOption> OverdriveFinisherOptions { get; } = new();
        public ObservableCollection<OverdriveFinisherStep> OverdriveFinisherSequence { get; } = new();
        public string ForbiddenOverdriveModeHint => Strings.U_Ai_OdChargeHint;

        [ObservableProperty] private bool overdriveRecipeShowBar = true;
        [ObservableProperty] private OverdriveStartModeChoice? selectedOverdriveStartMode;
        partial void OnSelectedOverdriveStartModeChanged(OverdriveStartModeChoice? value) =>
            IsOverdriveStartCustom = value?.Kind == OverdriveStartModeKind.Custom;
        [ObservableProperty] private bool isOverdriveStartCustom;
        [ObservableProperty] private string overdriveRecipeCustomStart = "100";
        [ObservableProperty] private bool overdriveChargePerTurn = true;
        [ObservableProperty] private string overdriveChargePerTurnAmount = "10";
        [ObservableProperty] private bool overdriveChargeOnHit;
        [ObservableProperty] private string overdriveChargeOnHitAmount = "20";
        [ObservableProperty] private bool overdriveChargeHpBelow;
        [ObservableProperty] private string overdriveChargeHpPercent = "50";
        [ObservableProperty] private string overdriveChargeHpAmount = "15";
        [ObservableProperty] private bool overdriveChargeChance;
        [ObservableProperty] private string overdriveChargeChanceK = "3";
        [ObservableProperty] private string overdriveChargeChanceAmount = "25";
        [ObservableProperty] private bool overdriveChargeDamageTakenPercent;
        [ObservableProperty] private string overdriveChargeDamageTakenPercentThreshold = "25";
        [ObservableProperty] private string overdriveChargeDamageTakenPercentAmount = "30";
        [ObservableProperty] private bool overdriveChargeAfterSelectedAction;
        [ObservableProperty] private string overdriveChargeAfterSelectedActionAmount = "10";
        [ObservableProperty] private bool overdriveChargeEveryNTurns;
        [ObservableProperty] private string overdriveChargeEveryNTurnsInterval = "3";
        [ObservableProperty] private string overdriveChargeEveryNTurnsAmount = "10";
        [ObservableProperty] private bool overdriveChargeHpRange;
        [ObservableProperty] private string overdriveChargeHpRangeMin = "25";
        [ObservableProperty] private string overdriveChargeHpRangeMax = "50";
        [ObservableProperty] private string overdriveChargeHpRangeAmount = "15";
        [ObservableProperty] private bool overdriveChargeSelfStatus;
        [ObservableProperty] private AiBuffPreset selectedOverdriveSelfStatus;
        [ObservableProperty] private string overdriveChargeSelfStatusAmount = "20";
        [ObservableProperty] private bool overdriveChargePartyDead;
        [ObservableProperty] private string overdriveChargePartyDeadAmount = "20";
        [ObservableProperty] private bool overdriveChargeLastAttacker;
        [ObservableProperty] private string overdriveChargeLastAttackerAmount = "10";
        [ObservableProperty] private bool overdriveChargeDamageZero;
        [ObservableProperty] private string overdriveChargeDamageZeroAmount = "10";
        [ObservableProperty] private bool overdriveChargePhysicalHitLab;
        [ObservableProperty] private string overdriveChargePhysicalHitAmount = "10";
        [ObservableProperty] private bool overdriveChargeMagicalHitLab;
        [ObservableProperty] private string overdriveChargeMagicalHitAmount = "10";
        [ObservableProperty] private bool overdriveChargeReducedHitLab;
        [ObservableProperty] private bool overdriveChargeStatusSufferedLab;
        [ObservableProperty] private bool overdriveChargeHealedLab;
        [ObservableProperty] private string overdriveFinisherSearch = string.Empty;
        partial void OnOverdriveFinisherSearchChanged(string value) => ReloadOverdriveFinisherOptions();
        [ObservableProperty] private AiCommandOption? selectedOverdriveFinisherAbility;
        [ObservableProperty] private OverdriveFinisherStep? selectedOverdriveFinisherStep;
        [ObservableProperty] private OverdriveFinishModeChoice? selectedOverdriveFinishMode;
        [ObservableProperty] private bool overdriveUseWhenFullOutsideTurn;
        [ObservableProperty] private bool overdriveResetAfterFinish = true;
        [ObservableProperty] private bool overdriveCleanExistingRecipe = true;
        [ObservableProperty] private string overdriveExistingSummary =
            Strings.F2_select_a_monster_to_check_if_overdrive_i_11ea8ad7;
        [ObservableProperty] private string overdriveRecipeSummary =
            Strings.F2_overdrive_setup_at_combat_start_forbidde_d59ec09a;

        // ---- 🎭 Presets de comportamento (#10) — composições 1-clique das automações JÁ provadas ----
        [ObservableProperty] private string presetSummary =
            Strings.F2_yunalesca_applies_ready_made_status_bles_c9d5aadf;

        // ---- 📋 Copiar IA de outro monstro / ♻️ Restaurar original ----
        public IEnumerable<MonsterAiRow> CopyableSources => Monsters.Where(m => m.HasScript);
        [ObservableProperty] private MonsterAiRow? copySource;
        [ObservableProperty] private string copyRestoreSummary =
            Strings.F2_copies_the_entire_ai_from_another_monste_577382c0;

        [ObservableProperty] private string behaviorSummary = Strings.F2_select_a_monster_to_see_the_summary_06e412bf;
        [ObservableProperty] private bool hasSeymourDispatchData;
        [ObservableProperty] private string dispatchTableSummary =
            Strings.F2_no_indirect_dispatch_table_detected_in_t_5941afe3;
        [ObservableProperty] private string dispatchInterpretationSummary =
            Strings.F2_when_the_table_exists_this_card_explains_331b4d7c;
        [ObservableProperty] private string seymourSceneTechSummary =
            Strings.F2_when_the_read_matches_the_seymour_packag_bc308510;
        [ObservableProperty] private bool hasIndirectDispatchUnits;
        [ObservableProperty] private string indirectDispatchUnitSummary =
            Strings.F2_when_this_block_appears_it_shows_indirec_4e43cc47;
        [ObservableProperty] private bool hasFineAiPhaseVariableContext;
        [ObservableProperty] private string fineAiPhaseVariableSummary =
            Strings.F2_open_the_phase_manager_and_select_a_var__c67a2c84;

        void SeedAutomations()
        {
            AiCommandLabels.Load(CommandLabelsPath);   // #7 — user-renamed command ids, remembered across sessions
            SelectedAbilityCategory = AutomationAbilityCategories.FirstOrDefault();
            SelectedActionFilter = AutomationActionFilters.FirstOrDefault();
            SelectedSelfBuff = SelfBuffPresets.FirstOrDefault();
            SelectedOverdriveSelfStatus = OverdriveSelfStatusPresets.FirstOrDefault();
            ReloadAutomationAbilities();
            ReloadPlantFields();
            SelectedPlantField = PlantFieldOptions.FirstOrDefault(o => o.FieldId == 0x0014)
                                 ?? PlantFieldOptions.FirstOrDefault();
            SelectedOverdriveStartMode = OverdriveStartModeChoices.FirstOrDefault(o => o.Kind == OverdriveStartModeKind.Full)
                                         ?? OverdriveStartModeChoices.FirstOrDefault();
            SelectedOverdriveFinishMode = OverdriveFinishModeChoices.FirstOrDefault(o => o.Kind == OverdriveFinishModeKind.Sequence)
                                           ?? OverdriveFinishModeChoices.FirstOrDefault();
            ReloadOverdriveFinisherOptions();
            SeedDefaultOverdriveFinishers();
            SelectedAuthoringTarget = AuthoringTargetOptions.FirstOrDefault();
            SelectedForbiddenRiteTarget = ForbiddenRiteTargets.FirstOrDefault(t => !t.UseLinkedActionTarget)
                                          ?? ForbiddenRiteTargets.FirstOrDefault();
            SelectedForbiddenStatus = ForbiddenStatusPresets.FirstOrDefault();
            EnsureCompositeDefaults();
            UpdateCompositeActionPreview();
            UpdateForbiddenRiteLinkSummary();
            SeedSinCatalog();
        }

        // Called from UpdateSelected whenever the selected monster/script changes.
        void RebuildAutomationChoices() => RebuildAutomationActions();

        partial void OnSelectedAbilityCategoryChanged(AiAbilityCategoryChoice? value) => ReloadAutomationAbilities();
        partial void OnAbilitySearchChanged(string value) => ReloadAutomationAbilities();
        partial void OnPlantFieldSearchChanged(string value) => ReloadPlantFields();

        void ReloadOverdriveFinisherOptions()
        {
            ushort? keep = SelectedOverdriveFinisherAbility?.Operand;
            string f = (OverdriveFinisherSearch ?? string.Empty).Trim();
            IEnumerable<AiCommandOption> q = AiCommandId.AllOptions();
            if (!string.IsNullOrWhiteSpace(f))
            {
                string compact = f.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                q = q.Where(o =>
                    o.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                    || o.Hex.Contains(compact, StringComparison.OrdinalIgnoreCase)
                    || o.Display.Contains(f, StringComparison.OrdinalIgnoreCase));
            }

            var rows = q.OrderByDescending(IsOverdriveLikeCommand)
                        .ThenBy(o => o.Category)
                        .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                        .Take(240)
                        .ToList();
            OverdriveFinisherOptions.Clear();
            foreach (AiCommandOption row in rows) OverdriveFinisherOptions.Add(row);
            SelectedOverdriveFinisherAbility = rows.FirstOrDefault(o => o.Operand == keep)
                                               ?? rows.FirstOrDefault(IsOverdriveLikeCommand)
                                               ?? rows.FirstOrDefault();
        }

        void SeedDefaultOverdriveFinishers()
        {
            if (OverdriveFinisherSequence.Count > 0) return;
            AiTargetOption? defaultTarget = SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault();
            foreach (string name in new[] { "Heavenly Strike", "Diamond Dust" })
            {
                AiCommandOption? opt = AiCommandId.AllOptions()
                    .Where(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(o => o.Category != AiCommandCategory.Character)
                    .ThenBy(o => o.Category)
                    .FirstOrDefault();
                if (opt != null && OverdriveFinisherSequence.All(x => x.Ability.Operand != opt.Operand))
                    OverdriveFinisherSequence.Add(new OverdriveFinisherStep(opt, AuthoringTargetOptions.ToList(), defaultTarget));
            }
        }

        static bool IsOverdriveLikeCommand(AiCommandOption option)
        {
            string n = option.Name;
            return option.Category != AiCommandCategory.Character && (
                n.Contains("Diamond Dust", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Mega Flare", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Hellfire", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Thor", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Energy", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Oblivion", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Zanmato", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Wakizashi", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Passado", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Delta Attack", StringComparison.OrdinalIgnoreCase));
        }

        // Repopulate the ability dropdown for the chosen category, filtered by the search box (name or hex), keeping
        // the current selection if it survives the filter. 320 char / ~300 mon1 / ~200 mon2 entries — search matters.
        void ReloadAutomationAbilities()
        {
            ushort? keep = SelectedAutomationAbility?.Operand;
            string f = (AbilitySearch ?? string.Empty).Trim();
            IReadOnlyList<AiCommandOption> all = SelectedAbilityCategory?.Category is AiCommandCategory cat
                ? AiCommandId.OptionsFor(cat)
                : AiCommandId.AllOptions();
            AutomationAbilityOptions.Clear();
            foreach (AiCommandOption o in all)
                if (f.Length == 0 || MatchesAbility(o, f))
                    AutomationAbilityOptions.Add(o);
            SelectedAutomationAbility = AutomationAbilityOptions.FirstOrDefault(o => o.Operand == keep)
                                        ?? AutomationAbilityOptions.FirstOrDefault();
            AbilityFilterSummary = f.Length == 0
                ? $"{all.Count} habilidades"
                : $"{AutomationAbilityOptions.Count}/{all.Count} · \"{f}\"";
            SyncPhaseAbilityOptions();
        }

        static bool MatchesAbility(AiCommandOption o, string f) =>
            o.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || o.Hex.Contains(f, StringComparison.OrdinalIgnoreCase);

        static int FieldPlantRank(ushort fieldId) => fieldId switch
        {
            0x0089 => 0, // showOverdriveBar
            0x0014 => 1, // OverdriveMax
            0x0013 => 2, // OverdriveCurrent
            0x0012 => 3, // OverdriveMode
            0x0077 => 4, // DullHitReactionToPhys
            0x0078 => 5, // DullHitReactionToMag
            _ => 20,
        };

        void ReloadPlantFields()
        {
            ushort? keep = SelectedPlantField?.FieldId;
            string f = (PlantFieldSearch ?? string.Empty).Trim();
            PlantFieldOptions.Clear();
            foreach (AiFieldPlantOption o in _allPlantFields)
                if (f.Length == 0 || o.Matches(f))
                    PlantFieldOptions.Add(o);
            SelectedPlantField = PlantFieldOptions.FirstOrDefault(o => o.FieldId == keep)
                                 ?? PlantFieldOptions.FirstOrDefault();
        }

        static ushort ParseU16Loose(string? s)
            => TryParseU16Loose(s, out ushort value) ? value : (ushort)0;

        static bool TryParseU16Loose(string? s, out ushort value)
        {
            s = (s ?? string.Empty).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            if (ushort.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
            return ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        bool TryResolveOnTurnHook(out AiEventHook hook, out string error)
        {
            hook = default;
            error = string.Empty;
            if (selectedScript == null || selectedPath == null)
            {
                error = Strings.F2_no_aifile_selected_4180056c;
                return false;
            }
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                if (AiWorkerMapping.TryResolveCombatOnTurn(monster, selectedScript, out hook, out string why))
                    return true;
                error = string.Format(Strings.U_Ai_NoOnTurnEvent, why);
                return false;
            }
            catch (Exception ex)
            {
                error = $"Falha lendo o WorkerFile: {ex.Message}";
                return false;
            }
        }

        bool TryResolveOnHitHook(out AiEventHook hook, out string error)
        {
            hook = default;
            error = string.Empty;
            if (selectedScript == null || selectedPath == null)
            {
                error = Strings.F2_no_aifile_selected_4180056c;
                return false;
            }
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                if (AiWorkerMapping.TryResolveCombatOnHit(monster, selectedScript, out hook, out string why))
                    return true;
                error = string.Format(Strings.U_Ai_NoOnHitEvent, why);
                return false;
            }
            catch (Exception ex)
            {
                error = $"Falha lendo o WorkerFile: {ex.Message}";
                return false;
            }
        }

        bool TryResolveAuthoringHook(out AiEventHook hook, out string label, out string error)
        {
            hook = default;
            label = string.Empty;
            error = string.Empty;
            if (selectedScript == null || selectedPath == null)
            {
                error = Strings.F2_no_aifile_selected_4180056c;
                return false;
            }

            if (SelectedTemplateWorker != null
                && SelectedTemplateWorker.Index >= 0
                && SelectedTemplateWorker.Index < selectedScript.Workers.Count
                && SelectedTemplateEntrypoint >= 0
                && SelectedTemplateEntrypoint < SelectedTemplateWorker.EntrypointCount)
            {
                hook = new AiEventHook(SelectedTemplateWorker.Index, SelectedTemplateEntrypoint);
                label = HookLabel(hook, Strings.U_Ai_SelectedTrigger);
                return true;
            }

            if (TryResolveOnTurnHook(out hook, out string onTurnError))
            {
                label = HookLabel(hook, "onTurn real");
                return true;
            }

            AiWorker? fallback = AiAutomation.PickCombatWorker(selectedScript);
            if (fallback != null)
            {
                int entrypoint = AiAutomation.PickMainEntrypoint(selectedScript, fallback);
                hook = new AiEventHook(fallback.Index, entrypoint);
                label = HookLabel(hook, $"fallback estrutural; onTurn inconclusivo ({onTurnError})");
                return true;
            }

            error = string.Format(Strings.U_Ai_NoEditableEntrypoint, onTurnError);
            return false;
        }

        string HookLabel(AiEventHook hook, string prefix)
        {
            string wtype = (selectedScript != null && hook.WorkerIndex >= 0 && hook.WorkerIndex < selectedScript.Workers.Count)
                ? selectedScript.Workers[hook.WorkerIndex].InferredType ?? "?"
                : "?";
            return string.Format(Strings.U_Ai_AutoWorkerEntrypoint, prefix, hook.WorkerIndex, wtype, hook.EntrypointIndex);
        }

        public void AddYunalescaConditionOnTurn() =>
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.OnTurn, "onTurn"));

        public void AddYunalescaConditionBattleStart() =>
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.BattleStart, Strings.U_Ai_Start));

        public void AddYunalescaConditionAlways() =>
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.Always, Strings.F2_always_6656018a));

        public void AddYunalescaConditionHpPercent()
        {
            if (!TryParseU16Loose(YunalescaHpPercentInput, out ushort percent))
            { PresetSummary = Strings.AiAdvancedInvalidNumber; return; }
            percent = (ushort)Math.Clamp(percent == 0 ? 50 : (int)percent, 1, 100);
            YunalescaHpPercentInput = percent.ToString(CultureInfo.InvariantCulture);
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.HpBelow, $"HP < {percent}%", percent));
        }

        public void AddYunalescaConditionOnHit() =>
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.OnHit, "onHit"));

        public void AddYunalescaConditionAnyHit() =>
            AddYunalescaCondition(new YunalescaConditionPart(YunalescaConditionKind.AnyHit, Strings.F2_any_hit_75e19af2));

        void AddYunalescaCondition(YunalescaConditionPart part)
        {
            NormalizeYunalescaConditionBeforeAdd(part);
            _yunalescaConditions.Add(part);
            RefreshYunalescaConditionPreview();
            PresetSummary = string.Format(Strings.U_Ai_ConditionAddedToRitual, part.Label);
        }

        void NormalizeYunalescaConditionBeforeAdd(YunalescaConditionPart part)
        {
            bool isEvent = part.Kind is YunalescaConditionKind.OnTurn or YunalescaConditionKind.BattleStart or YunalescaConditionKind.OnHit;
            if (isEvent)
            {
                _yunalescaConditions.RemoveAll(c => c.Kind is YunalescaConditionKind.OnTurn or YunalescaConditionKind.BattleStart or YunalescaConditionKind.OnHit);
                if (part.Kind != YunalescaConditionKind.OnHit)
                    _yunalescaConditions.RemoveAll(c => c.Kind == YunalescaConditionKind.AnyHit);
                return;
            }

            if (part.Kind == YunalescaConditionKind.Always)
            {
                _yunalescaConditions.RemoveAll(c => c.Kind is YunalescaConditionKind.Always or YunalescaConditionKind.HpBelow);
                return;
            }

            if (part.Kind == YunalescaConditionKind.HpBelow)
            {
                _yunalescaConditions.RemoveAll(c => c.Kind is YunalescaConditionKind.Always or YunalescaConditionKind.HpBelow);
                return;
            }

            if (part.Kind == YunalescaConditionKind.AnyHit)
            {
                _yunalescaConditions.RemoveAll(c => c.Kind is YunalescaConditionKind.OnTurn or YunalescaConditionKind.BattleStart or YunalescaConditionKind.OnHit or YunalescaConditionKind.AnyHit);
                _yunalescaConditions.Add(new YunalescaConditionPart(YunalescaConditionKind.OnHit, "onHit"));
            }
        }

        // Compatibility wrappers for older buttons/prototypes.
        public void ChooseYunalescaConditionOnTurnAlways()
        {
            AddYunalescaConditionOnTurn();
            AddYunalescaConditionAlways();
        }

        public void ChooseYunalescaConditionHpBelow50()
        {
            YunalescaHpPercentInput = "50";
            AddYunalescaConditionHpPercent();
        }

        public void ChooseYunalescaConditionBattleStart() =>
            AddYunalescaConditionBattleStart();

        public void ChooseYunalescaConditionOnHitAny()
        {
            AddYunalescaConditionOnHit();
            AddYunalescaConditionAnyHit();
        }

        public void ClearYunalescaCondition()
        {
            if (_yunalescaConditions.Count == 0)
            {
                PresetSummary = Strings.F2_no_pre_selected_condition_to_clear_nothi_724da31e;
                return;
            }

            YunalescaConditionPart removed = _yunalescaConditions[^1];
            _yunalescaConditions.RemoveAt(_yunalescaConditions.Count - 1);
            RefreshYunalescaConditionPreview();
            PresetSummary = string.Format(Strings.U_Ai_LastConditionRemoved, removed.Label);
        }

        public void PrepareTargetRecipe()
        {
            SelectAuthoringTarget(o => o.TargetRecipeKind == AiTargetRecipeKind.FindAliveFrontlineAny);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeTarget +
                Strings.F2_use_simple_flan_for_random_living_charac_aba427de;
            AddAbilitySummary =
                Strings.F2_choose_a_target_in_the_dropdown_and_an_a_2b996934;
        }

        public void PrepareSimpleActionRecipe()
        {
            _yunalescaConditions.Clear();
            RefreshYunalescaConditionPreview();
            AutomationWhenRandom = false;
            AutomationStopAfterAction = true;
            SelectAuthoringTarget(o => o.TargetRecipeKind == AiTargetRecipeKind.FindAliveFrontlineAny);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeSimpleAction +
                Strings.F2_self_buff_status_is_still_better_handled_4ec4fe64;
            AddAbilitySummary =
                Strings.F2_simple_action_prepared_choose_the_abilit_58c10e12;
        }

        public void PrepareSimpleRouletteRecipe()
        {
            _yunalescaConditions.Clear();
            RefreshYunalescaConditionPreview();
            AutomationWhenRandom = true;
            AutomationRandomK = string.IsNullOrWhiteSpace(AutomationRandomK) ? "2" : AutomationRandomK;
            AutomationStopAfterAction = true;
            SelectAuthoringTarget(o => o.TargetRecipeKind == AiTargetRecipeKind.FindAliveFrontlineAny);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeRoulette +
                Strings.F2_this_is_local_chance_not_a_summed_100_ta_b035119f;
            AddAbilitySummary =
                Strings.F2_simple_roulette_prepared_adjust_k_if_you_042f1edc;
        }

        public void PrepareCycleRecipe()
        {
            _yunalescaConditions.Clear();
            RefreshYunalescaConditionPreview();
            AutomationWhenRandom = true;
            AutomationRandomK = string.IsNullOrWhiteSpace(AutomationRandomK) ? "2" : AutomationRandomK;
            AutomationStopAfterAction = false;
            SelectAuthoringTarget(o => o.TargetRecipeKind == AiTargetRecipeKind.FindAliveFrontlineAny);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeCycle +
                Strings.F2_this_is_the_basis_of_cycles_like_flame_d_063545ab;
            AddAbilitySummary =
                Strings.F2_cycle_prepared_chance_enabled_and_stop_h_b1d95e87;
        }

        public void PrepareReactionRecipe()
        {
            _yunalescaConditions.Clear();
            AddYunalescaConditionAnyHit();
            AutomationWhenRandom = false;
            AutomationStopAfterAction = true;
            SelectAuthoringTarget(o => o.Operand == 0xFFEF);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeReaction +
                Strings.F2_this_is_perfect_for_counterattack_pollen_2ac5fd83;
            AddAbilitySummary =
                Strings.F2_reaction_prepared_choose_the_ability_sta_9124a0cd;
        }

        public void PrepareTurnStateRecipe()
        {
            _yunalescaConditions.Clear();
            RefreshYunalescaConditionPreview();
            AutomationWhenRandom = false;
            AutomationStopAfterAction = true;
            SelectAuthoringTarget(o => o.Operand == 0xFFF3);
            AuthoringRecipeSummary =
                Strings.U_Ai_RecipeStateBetweenTurns +
                Strings.F2_for_now_prepare_the_visual_step_here_and_906146cb;
            AddAbilitySummary =
                Strings.F2_state_between_turns_prepared_as_conserva_fe9a8420;
        }

        void SelectAuthoringTarget(Func<AiTargetOption, bool> predicate)
        {
            SelectedAuthoringTarget = AuthoringTargetOptions.FirstOrDefault(predicate)
                                      ?? SelectedAuthoringTarget
                                      ?? AuthoringTargetOptions.FirstOrDefault();
        }

        void ClearYunalescaConditionAfterApply()
        {
            _yunalescaConditions.Clear();
            RefreshYunalescaConditionPreview();
        }

        void RefreshYunalescaConditionPreview()
        {
            if (_yunalescaConditions.Count == 0)
            {
                YunalescaConditionBadge = Strings.F2_onturn_always_default_7d8317b0;
                YunalescaConditionChain = Strings.F2_no_condition_selected_when_applied_uses__6cd877f6;
                YunalescaConditionSummary = Strings.F2_build_conditions_before_clicking_a_rite__19ebdd5b;
                return;
            }

            string chain = string.Join(" + ", _yunalescaConditions.Select(c => c.Label));
            YunalescaConditionBadge = chain;
            YunalescaConditionChain = chain;
            YunalescaConditionSummary =
                string.Format(Strings.U_Ai_NextRitual, ResolveYunalescaHookKindLabel(), ResolveYunalescaGuardLabel());
        }

        bool TryBuildYunalescaGuard(out List<AiInstruction> guard, out string guardLabel, out string error)
        {
            guard = new List<AiInstruction>();
            guardLabel = Strings.F2_always_6656018a;
            error = string.Empty;
            try
            {
                YunalescaConditionPart? hp = LastCondition(YunalescaConditionKind.HpBelow);
                if (hp?.Percent is ushort percent)
                {
                    guard = AiAutomation.BuildHpBelowPercentGuard(percent);
                    guardLabel = $"HP < {percent}%";
                }
                else
                {
                    guard = AiAutomation.BuildAlwaysGuard();
                    guardLabel = Strings.F2_always_6656018a;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = string.Format(Strings.U_Ai_CouldNotBuildYunalesca, ex.Message);
                return false;
            }
        }

        bool TryResolveYunalescaConditionHook(out AiEventHook hook, out string hookLabel, out string error)
        {
            hook = default;
            hookLabel = string.Empty;
            error = string.Empty;

            YunalescaHookKind hookKind = ResolveYunalescaHookKind();
            if (hookKind == YunalescaHookKind.BattleStart)
                return TryResolveCombatEntrypointForYunalesca(0, Strings.U_Ai_BattleStartLab, out hook, out hookLabel, out error);
            if (hookKind == YunalescaHookKind.OnHit)
            {
                if (!TryResolveOnHitHook(out hook, out error))
                    return false;
                hookLabel = HookLabel(hook, "onHit real");
                return true;
            }

            if (TryResolveOnTurnHook(out hook, out error))
            {
                hookLabel = HookLabel(hook, "onTurn real");
                return true;
            }
            return false;
        }

        YunalescaConditionPart? LastCondition(YunalescaConditionKind kind)
        {
            for (int i = _yunalescaConditions.Count - 1; i >= 0; i--)
                if (_yunalescaConditions[i].Kind == kind)
                    return _yunalescaConditions[i];
            return null;
        }

        YunalescaHookKind ResolveYunalescaHookKind()
        {
            for (int i = _yunalescaConditions.Count - 1; i >= 0; i--)
            {
                if (_yunalescaConditions[i].Kind == YunalescaConditionKind.OnTurn)
                    return YunalescaHookKind.OnTurn;
                if (_yunalescaConditions[i].Kind == YunalescaConditionKind.BattleStart)
                    return YunalescaHookKind.BattleStart;
                if (_yunalescaConditions[i].Kind == YunalescaConditionKind.OnHit)
                    return YunalescaHookKind.OnHit;
            }

            return _yunalescaConditions.Any(c => c.Kind == YunalescaConditionKind.AnyHit)
                ? YunalescaHookKind.OnHit
                : YunalescaHookKind.OnTurn;
        }

        string ResolveYunalescaHookKindLabel() =>
            ResolveYunalescaHookKind() switch
            {
                YunalescaHookKind.BattleStart => Strings.U_Ai_Start,
                YunalescaHookKind.OnHit => "onHit",
                _ => "onTurn",
            };

        string ResolveYunalescaGuardLabel()
        {
            YunalescaConditionPart? hp = LastCondition(YunalescaConditionKind.HpBelow);
            return hp?.Percent is ushort percent ? $"HP < {percent}%" : Strings.F2_always_6656018a;
        }

        bool TryResolveCombatEntrypointForYunalesca(int entrypointIndex, string prefix, out AiEventHook hook, out string hookLabel, out string error)
        {
            hook = default;
            hookLabel = string.Empty;
            error = string.Empty;
            if (selectedScript == null || selectedPath == null)
            {
                error = Strings.F2_no_aifile_selected_4180056c;
                return false;
            }

            int workerIndex;
            if (TryResolveOnTurnHook(out AiEventHook onTurnHook, out _))
            {
                workerIndex = onTurnHook.WorkerIndex;
            }
            else
            {
                AiWorker? fallback = AiAutomation.PickCombatWorker(selectedScript);
                if (fallback == null)
                {
                    error = "Could not find editable CombatHandler/worker for LAB condition.";
                    return false;
                }
                workerIndex = fallback.Index;
            }

            if (workerIndex < 0 || workerIndex >= selectedScript.Workers.Count)
            {
                error = string.Format(Strings.U_Ai_WorkerInvalidLab, workerIndex);
                return false;
            }
            AiWorker worker = selectedScript.Workers[workerIndex];
            if (entrypointIndex < 0 || entrypointIndex >= worker.Entrypoints.Count)
            {
                error = string.Format(Strings.U_Ai_WorkerNoEntrypoint, workerIndex, worker.InferredType ?? "?", entrypointIndex, prefix);
                return false;
            }

            hook = new AiEventHook(workerIndex, entrypointIndex);
            hookLabel = HookLabel(hook, prefix);
            return true;
        }

        void UpdateForbiddenRiteLinkSummary()
        {
            AiTargetOption? target = SelectedForbiddenRiteTarget;
            AiForbiddenStatusPreset? status = SelectedForbiddenStatus;
            string value = string.IsNullOrWhiteSpace(ForbiddenStatusValue) ? "?" : ForbiddenStatusValue.Trim();
            string payload = target != null && status != null
                ? $"{status.Name} = {value} em {target.Label}"
                : Strings.F2_select_target_status_in_the_forbidden_ri_c2c46f24;
            ForbiddenRiteLinkSummary = LinkForbiddenRiteToBehavior
                ? Strings.U_Ai_RiteDoesNotEditSpell +
                  string.Format(Strings.U_Ai_CurrentPayload, payload) +
                  Strings.F2_use_same_target_as_linked_skill_when_you_dabafd03
                : target?.UseLinkedActionTarget == true
                    ? Strings.F2_off_same_target_as_linked_skill_only_wor_beabdd52
                    : string.Format(Strings.U_Ai_RiteOff, payload);
        }

        bool TryGetForbiddenRitePayload(out AiTargetOption target, out AiForbiddenStatusPreset status, out ushort value, out string error)
        {
            target = SelectedForbiddenRiteTarget ?? default!;
            status = SelectedForbiddenStatus ?? default!;
            value = 0;
            error = string.Empty;
            if (SelectedForbiddenRiteTarget == null)
            {
                error = Strings.F2_select_a_target_in_the_forbidden_rite_be_4e6078d1;
                return false;
            }
            if (SelectedForbiddenStatus == null)
            {
                error = Strings.F2_select_a_status_in_the_forbidden_rite_be_cc423a76;
                return false;
            }
            if (!TryParseU16Loose(ForbiddenStatusValue, out value))
            { error = Strings.AiAdvancedInvalidNumber; return false; }
            return true;
        }

        bool TryResolveForbiddenRiteTarget(AiTargetOption target, AiDetectedAction? linkedAction, out ushort operand, out string label, out string error, AiTargetOption? newAbilityTarget = null)
        {
            operand = target.Operand;
            label = target.Label;
            error = string.Empty;
            if (!target.UseLinkedActionTarget)
                return true;

            if (newAbilityTarget != null)
            {
                operand = newAbilityTarget.Operand;
                label = string.Format(Strings.U_Ai_SameTargetNewSkill, newAbilityTarget.Label);
                return true;
            }

            if (linkedAction == null)
            {
                error = Strings.F2_same_target_as_linked_skill_requires_a_s_e4ecad99;
                return false;
            }
            if (linkedAction.Kind != AiActionKind.Command || linkedAction.TargetPushOffset < 0)
            {
                error = Strings.F2_same_target_as_linked_skill_only_applies_51afc7c0;
                return false;
            }
            if (!linkedAction.TargetIsLiteral)
            {
                error = string.Format(Strings.U_Ai_ComputedTargetNotLiteral, linkedAction.AbilityName);
                return false;
            }

            operand = linkedAction.TargetOperand;
            string named = AiTargetNames.Get(operand) ?? string.Format(Strings.U_Ai_TargetLiteral, (short)operand);
            label = string.Format(Strings.U_Ai_SameTargetAs, linkedAction.AbilityName, named);
            return true;
        }

        static string LinkedActionTargetLabel(AiDetectedAction action)
        {
            if (action.TargetOpcode == 0x9F)
                return $"mesmo alvo calculado de '{action.AbilityName}'";
            if (action.TargetIsLiteral)
            {
                string named = AiTargetNames.Get(action.TargetOperand) ?? string.Format(Strings.U_Ai_TargetLiteral, (short)action.TargetOperand);
                return string.Format(Strings.U_Ai_SameTargetAs, action.AbilityName, named);
            }
            return $"mesmo alvo de '{action.AbilityName}'";
        }

        bool TryResolveForbiddenRiteTargetsForBatch(AiTargetOption target, IReadOnlyList<AiDetectedAction> actions, out Dictionary<int, ushort> operandsByCall, out string label, out string error)
        {
            operandsByCall = new Dictionary<int, ushort>();
            label = target.UseLinkedActionTarget
                ? Strings.F2_same_target_of_each_linked_ability_141111e1
                : target.Label;
            error = string.Empty;

            foreach (AiDetectedAction action in actions)
            {
                if (!TryResolveForbiddenRiteTarget(target, action, out ushort operand, out _, out error))
                    return false;
                operandsByCall[action.CallOffset] = operand;
            }
            return true;
        }

        string LinkedForbiddenRiteNote(AiTargetOption target, AiForbiddenStatusPreset status, ushort value) =>
            LinkedForbiddenRiteNote(target.Label, status, value);

        string LinkedForbiddenRiteNote(string targetLabel, AiForbiddenStatusPreset status, ushort value) =>
            string.Format(Strings.U_Ai_LinkedRiteNote, status.Name, value, targetLabel);

        static readonly string GuardedChainContinuationNote = Strings.U_Ai_GuardedChainNote;
        static readonly string StopAfterActionNote = Strings.U_Ai_StopAfterActionNote;

        // ── ➕ Copiar ação selecionada e trocar somente a habilidade ─────────────────────────────────────
        public void AddAbility()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                AddAbilitySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_a833771d;
                return;
            }
            AiCommandOption? ability = SelectedAutomationAbility;
            if (ability == null) { AddAbilitySummary = Strings.F2_choose_an_ability_from_the_list_65237ed5; return; }
            AiActionVm? template = SelectedAutomationAction;
            bool hasTemplate = template != null && template.Action.Kind == AiActionKind.Command && template.Action.Removable;
            AiTargetOption? newAbilityTarget = hasTemplate ? null : SelectedAuthoringTarget;
            if (!hasTemplate && newAbilityTarget == null)
            {
                AddAbilitySummary = Strings.F2_choose_the_target_for_the_new_ability_be_fc34c522;
                return;
            }

            if (AutomationWhenRandom && !TryParseU16Loose(AutomationRandomK, out _))
            { AddAbilitySummary = Strings.AiAdvancedInvalidNumber; return; }
            int k = ParseU16Loose(AutomationRandomK);
            bool linkRite = LinkForbiddenRiteToBehavior;
            bool hasAuthoringCondition = _yunalescaConditions.Count > 0;
            AiTargetOption? riteTarget = null;
            AiForbiddenStatusPreset? riteStatus = null;
            ushort riteValue = 0;
            ushort riteTargetOperand = 0;
            string riteTargetLabel = string.Empty;
            string riteError = string.Empty;
            if (linkRite && !TryGetForbiddenRitePayload(out riteTarget, out riteStatus, out riteValue, out riteError))
            {
                AddAbilitySummary = riteError;
                return;
            }
            bool linkUsesSameTarget = linkRite && riteTarget?.UseLinkedActionTarget == true;
            byte[] newAi;
            AiEventHook hook = default;
            string hookLabel = "";
            List<AiInstruction>? conditionGuard = null;
            string conditionGuardLabel = AutomationWhenRandom ? $"chance 1 em {(k < 2 ? 2 : k)}" : Strings.F2_always_6656018a;
            string conditionLabel = hasAuthoringCondition ? YunalescaConditionBadge : string.Empty;
            bool appendsToHook = hasAuthoringCondition || !hasTemplate;
            if (appendsToHook)
            {
                if (hasAuthoringCondition)
                {
                    if (!TryResolveYunalescaConditionHook(out hook, out hookLabel, out string hookError))
                    {
                        AddAbilitySummary = hookError;
                        return;
                    }
                    if (!TryBuildYunalescaGuard(out List<AiInstruction> builtGuard, out string guardLabel, out string guardError))
                    {
                        AddAbilitySummary = guardError;
                        return;
                    }
                    if (AutomationWhenRandom && guardLabel != Strings.F2_always_6656018a)
                    {
                        AddAbilitySummary =
                            Strings.U_Ai_HpOrChanceOnly +
                            Strings.F2_the_hp_chance_combination_will_be_entere_02015e90;
                        return;
                    }
                    conditionGuard = AutomationWhenRandom
                        ? AiAutomation.BuildRandomGuard(k)
                        : builtGuard;
                    conditionGuardLabel = AutomationWhenRandom ? $"chance 1 em {(k < 2 ? 2 : k)}" : guardLabel;
                }
                else if (!TryResolveAuthoringHook(out hook, out hookLabel, out string hookError))
                {
                    AddAbilitySummary = hookError;
                    return;
                }
            }
            if (linkUsesSameTarget)
            {
                riteTargetLabel = hasTemplate
                    ? LinkedActionTargetLabel(template!.Action)
                    : string.Format(Strings.U_Ai_SameTargetNewSkill, newAbilityTarget!.Label);
            }
            else if (linkRite && !TryResolveForbiddenRiteTarget(riteTarget!, hasTemplate ? template!.Action : null, out riteTargetOperand, out riteTargetLabel, out riteError, newAbilityTarget))
            {
                AddAbilitySummary = riteError;
                return;
            }
            try
            {
                if (hasTemplate)
                {
                    if (hasAuthoringCondition)
                    {
                        newAi = linkRite
                            ? (linkUsesSameTarget
                                ? AiAutomation.AddAbilityFromActionTemplateWithLinkedChrPropertyWriteWithGuard(selectedScript, template!.Action, ability.Operand, riteStatus!.FieldId, riteValue, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction)
                                : AiAutomation.AddAbilityFromActionTemplateWithChrPropertyWriteWithGuard(selectedScript, template!.Action, ability.Operand, riteTargetOperand, riteStatus!.FieldId, riteValue, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction))
                            : AiAutomation.AddAbilityFromActionTemplateWithGuard(selectedScript, template!.Action, ability.Operand, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction);
                    }
                    else
                    {
                        newAi = linkRite
                            ? (linkUsesSameTarget
                                ? AiAutomation.InsertCommandAfterActionWithLinkedChrPropertyWrite(selectedScript, template!.Action, ability.Operand, riteStatus!.FieldId, riteValue, AutomationWhenRandom, k, AutomationStopAfterAction)
                                : AiAutomation.InsertCommandAfterActionWithChrPropertyWrite(selectedScript, template!.Action, ability.Operand, riteTargetOperand, riteStatus!.FieldId, riteValue, AutomationWhenRandom, k, AutomationStopAfterAction))
                            : AiAutomation.InsertCommandAfterAction(selectedScript, template!.Action, ability.Operand, AutomationWhenRandom, k, AutomationStopAfterAction);
                    }
                }
                else
                {
                    AiTargetRecipe recipe = newAbilityTarget!.ToRecipe();
                    if (hasAuthoringCondition)
                    {
                        newAi = linkRite
                            ? (linkUsesSameTarget
                                ? AiAutomation.AddAbilityWithLinkedChrPropertyWriteWithGuard(selectedScript, ability.Operand, recipe, riteStatus!.FieldId, riteValue, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction)
                                : AiAutomation.AddAbilityWithChrPropertyWriteWithGuard(selectedScript, ability.Operand, recipe, riteTargetOperand, riteStatus!.FieldId, riteValue, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction))
                            : AiAutomation.AddAbilityWithGuard(selectedScript, ability.Operand, recipe, conditionGuard!, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction);
                    }
                    else
                    {
                        newAi = linkRite
                            ? (linkUsesSameTarget
                                ? AiAutomation.AddAbilityWithLinkedChrPropertyWrite(selectedScript, ability.Operand, recipe, riteStatus!.FieldId, riteValue, AutomationWhenRandom, k, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction)
                                : AiAutomation.AddAbilityWithChrPropertyWrite(selectedScript, ability.Operand, recipe, riteTargetOperand, riteStatus!.FieldId, riteValue, AutomationWhenRandom, k, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction))
                            : AiAutomation.AddAbility(selectedScript, ability.Operand, AutomationWhenRandom, k, hook.WorkerIndex, hook.EntrypointIndex, recipe, AutomationStopAfterAction);
                    }
                }
            }
            catch (Exception ex) { AddAbilitySummary = string.Format(Strings.U_Ai_CouldNotBuildSkill, ex.Message); return; }

            string when = hasAuthoringCondition
                ? string.Format(Strings.U_Ai_WhenConditionPasses, conditionLabel, conditionGuardLabel)
                : (AutomationWhenRandom
                    ? string.Format(Strings.U_Ai_SometimesEvery, (k < 2 ? 2 : k))
                    : (hasTemplate ? Strings.F2_right_after_the_selected_action_b3ba1b98 : Strings.F2_whenever_the_combat_behavior_runs_ee4455fd));
            if (!SaveNewAi(newAi, out string err)) { AddAbilitySummary = err; return; }
            string shape = hasTemplate
                ? (hasAuthoringCondition
                    ? string.Format(Strings.U_Ai_CreatedUsingTemplate, template!.Action.AbilityName, ModeName(template.Action))
                    : string.Format(Strings.U_Ai_InsertedBelowTemplate, template!.Action.AbilityName, ModeName(template.Action)))
                : string.Format(Strings.U_Ai_NoTemplateNormalQueue, newAbilityTarget!.Label);
            string templateContext = hasTemplate && template!.HasRunContextHint
                ? (hasAuthoringCondition
                    ? string.Format(Strings.U_Ai_TemplateHadContext, template.RunContextHint)
                    : string.Format(Strings.U_Ai_TemplateContextInherited, template.RunContextHint))
                : "";
            string placement = hasTemplate
                ? (hasAuthoringCondition
                    ? string.Format(Strings.U_Ai_HookedAt, hookLabel)
                    : Strings.U_Ai_EntrypointNotRepointed)
                : string.Format(Strings.U_Ai_HookedAt, hookLabel);
            string growNote = hasTemplate && !AutomationWhenRandom && !hasAuthoringCondition
                ? Strings.U_Ai_OffsetsRelinked
                : Strings.U_Ai_JumpTableGrew;
            string conditionClearNote = hasAuthoringCondition
                ? Strings.U_Ai_ConditionCleared
                : "";
            if (hasAuthoringCondition) ClearYunalescaConditionAfterApply();
            AddAbilitySummary =
                $"✅ '{ability.Name}' criada {shape}; {when}. " +
                $"{placement}{growNote} e salvo em {Path.GetFileName(selectedPath)} (grow-aware). " +
                (linkRite ? LinkedForbiddenRiteNote(riteTargetLabel, riteStatus!, riteValue) : "") +
                (AutomationStopAfterAction ? StopAfterActionNote : "") +
                templateContext +
                conditionClearNote +
                ((!hasTemplate || hasAuthoringCondition) ? GuardedChainContinuationNote : "") +
                Strings.F2_released_for_authoring_testing_confirm_t_c9a485cd;
        }

        static string ModeName(AiDetectedAction action) =>
            action.ForcePerform ? "forcePerformCommand" : "performCommand";

        static string ContextNoteForSamePlace(AiActionVm? action)
        {
            if (action?.HasRunContextHint != true) return "";
            return $"⚠️ Contexto mantido: {action.RunContextHint}. ";
        }

        string ContextNoteForActions(IReadOnlyList<AiDetectedAction> actions)
        {
            if (actions.Count == 0) return "";
            Dictionary<int, string> hints = BuildRunContextHints();
            List<string> active = actions
                .Select(a => hints.TryGetValue(a.CallOffset, out string? hint) ? hint : null)
                .Where(hint => !string.IsNullOrWhiteSpace(hint))
                .Select(hint => hint!)
                .Distinct()
                .Take(3)
                .ToList();
            if (active.Count == 0) return "";
            string joined = string.Join(" | ", active);
            return $" ⚠️ Contexto do lote mantido: {joined}.";
        }

        // ── ➖ Tirar ação ────────────────────────────────────────────────────────────────────────────────
        public void RemoveSelectedAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_33cb756b;
                return;
            }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.F2_select_an_action_in_the_list_to_remove_1e7cfb1d; return; }
            if (!act.Action.Removable)
            {
                RemoveActionSummary = string.Format(Strings.U_Ai_NotSafeToRemove, act.Action.AbilityName);
                return;
            }

            List<AiInstruction> kept = AiAutomation.InstructionsWithout(selectedScript, act.Action);

            // Pre-flight: a jump/entrypoint pointing AT a dropped instruction would dangle — the validator says which.
            AiValidationReport check = AiValidator.Validate(selectedScript, kept);
            if (!check.IsValid)
            {
                RemoveActionSummary = string.Format(Strings.U_Ai_CannotRemoveAlone, act.Action.AbilityName, check.Errors.FirstOrDefault()?.Message);
                return;
            }

            byte[] rebuilt;
            try { rebuilt = AiScript_File.Rebuild(selectedScript, kept); }
            catch (Exception ex) { RemoveActionSummary = string.Format(Strings.U_Ai_RebuildRejectedRemoval, ex.Message); return; }

            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                bool grew = rebuilt.Length != selectedScript.OriginalAiFileBytes.Length;
                byte[] outBin = grew
                    ? AiScript_File.SpliceAiFileIntoMonsterGrow(monster, rebuilt)
                    : AiScript_File.SpliceAiFileIntoMonster(monster, rebuilt);
                WriteMonsterWithBackup(outBin);
            }
            catch (Exception ex) { RemoveActionSummary = $"Save abortado no splice: {ex.Message}"; return; }

            string removed = act.Action.AbilityName;
            ReloadSelectedFromDisk();
            RemoveActionSummary = string.Format(Strings.U_Ai_ActionRemoved, removed, Path.GetFileName(selectedPath));
        }

        // ── ▲▼ Mover ação (reordenar) ────────────────────────────────────────────────────────────────────
        public void MoveSelectedAction(bool up)
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_739379ad;
                return;
            }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.F2_select_an_action_in_the_list_to_move_5cde7a97; return; }

            int oldIdx = AutomationActions.IndexOf(act);
            List<AiInstruction>? reordered = AiAutomation.MoveActionInstructions(selectedScript, act.Action, up);
            if (reordered == null)
            {
                RemoveActionSummary = up
                    ? Strings.F2_already_the_first_action_or_the_neighbor_e6da0e3b
                    : Strings.F2_already_the_last_action_or_the_neighbori_363d3d4c;
                return;
            }
            ApplyAndSave(reordered,
                string.Format(Strings.U_Ai_ActionMoved, act.Action.AbilityName, (up ? "↑ para cima" : "↓ para baixo"), Path.GetFileName(selectedPath)),
                up ? oldIdx - 1 : oldIdx + 1);
        }

        // ── Mover ação para o gatilho escolhido (worker/entrypoint) ───────────────────────────────────────
        public void MoveSelectedActionToHook()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_90dd6735;
                return;
            }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.U_Ai_SelectActionToMove; return; }
            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { RemoveActionSummary = hookError; return; }

            byte[] newAi;
            try { newAi = AiAutomation.MoveActionToHook(selectedScript, act.Action, hook.WorkerIndex, hook.EntrypointIndex); }
            catch (Exception ex) { RemoveActionSummary = string.Format(Strings.U_Ai_CouldNotMoveAction, act.Action.AbilityName, ex.Message); return; }

            string moved = act.Action.AbilityName;
            if (!SaveNewAi(newAi, out string err)) { RemoveActionSummary = err; return; }
            RemoveActionSummary =
                string.Format(Strings.U_Ai_ActionMovedToHook, moved, hookLabel, Path.GetFileName(selectedPath));
        }

        // ── ✏ Trocar a habilidade da ação selecionada pela escolhida no picker de cima ───────────────────
        public void ChangeSelectedActionToPickedAbility()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_c22d6e41;
                return;
            }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.F2_select_the_action_you_want_to_swap_in_th_ee5dbb05; return; }
            if (act.Action.Kind != AiActionKind.Command)
            { RemoveActionSummary = Strings.F2_ability_swap_is_only_valid_for_command_a_fef51118; return; }
            AiCommandOption? to = SelectedAutomationAbility;
            if (to == null) { RemoveActionSummary = Strings.F2_choose_the_target_ability_in_the_picker__b4ea6af3; return; }

            bool linkRite = LinkForbiddenRiteToBehavior;
            AiTargetOption? riteTarget = null;
            AiForbiddenStatusPreset? riteStatus = null;
            ushort riteValue = 0;
            ushort riteTargetOperand = 0;
            string riteTargetLabel = string.Empty;
            string riteError = string.Empty;
            if (linkRite && !TryGetForbiddenRitePayload(out riteTarget, out riteStatus, out riteValue, out riteError))
            {
                RemoveActionSummary = riteError;
                return;
            }
            bool linkUsesSameTarget = linkRite && riteTarget?.UseLinkedActionTarget == true;
            if (linkUsesSameTarget)
            {
                riteTargetLabel = LinkedActionTargetLabel(act.Action);
            }
            else if (linkRite && !TryResolveForbiddenRiteTarget(riteTarget!, act.Action, out riteTargetOperand, out riteTargetLabel, out riteError))
            {
                RemoveActionSummary = riteError;
                return;
            }
            int oldIdx = AutomationActions.IndexOf(act);
            List<AiInstruction>? changed = linkRite
                ? (linkUsesSameTarget
                    ? AiAutomation.ChangeActionAndInsertChrPropertyWriteUsingActionTarget(selectedScript, act.Action, to.Operand, riteStatus!.FieldId, riteValue)
                    : AiAutomation.ChangeActionAndInsertChrPropertyWrite(selectedScript, act.Action, to.Operand, riteTargetOperand, riteStatus!.FieldId, riteValue))
                : AiAutomation.ChangeActionInstructions(selectedScript, act.Action, to.Operand);
            if (changed == null) { RemoveActionSummary = Strings.F2_could_not_locate_the_command_for_this_ac_f9a38645; return; }
            ApplyAndSave(changed,
                string.Format(Strings.U_Ai_ActionSwapped, act.Action.AbilityName, to.Name, Path.GetFileName(selectedPath), (linkRite ? " (with linked effect)" : " (1 operand, same size)"), ContextNoteForSamePlace(act)) +
                (linkRite ? LinkedForbiddenRiteNote(riteTargetLabel, riteStatus!, riteValue) : "") +
                "⚠️ Confirme in-game (RT2).",
                oldIdx);
        }

        // Shared tail for reorder/change: validate -> Rebuild -> splice -> save -> reload -> reselect by index.
        void ApplyAndSave(List<AiInstruction> newList, string okMsg, int reselectIndex)
        {
            AiValidationReport check = AiValidator.Validate(selectedScript!, newList);
            if (!check.IsValid)
            {
                RemoveActionSummary = string.Format(Strings.U_Ai_BlockedByValidation, check.Errors.FirstOrDefault()?.Message);
                return;
            }
            byte[] rebuilt;
            try { rebuilt = AiScript_File.Rebuild(selectedScript!, newList); }
            catch (Exception ex) { RemoveActionSummary = $"Rebuild rejeitou: {ex.Message}"; return; }
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath!);
                bool grew = rebuilt.Length != selectedScript!.OriginalAiFileBytes.Length;
                byte[] outBin = grew
                    ? AiScript_File.SpliceAiFileIntoMonsterGrow(monster, rebuilt)
                    : AiScript_File.SpliceAiFileIntoMonster(monster, rebuilt);
                WriteMonsterWithBackup(outBin);
            }
            catch (Exception ex) { RemoveActionSummary = $"Save abortado no splice: {ex.Message}"; return; }

            ReloadSelectedFromDisk();
            if (reselectIndex >= 0 && reselectIndex < AutomationActions.Count)
                SelectedAutomationAction = AutomationActions[reselectIndex];
            RemoveActionSummary = okMsg;
        }

        // ── ⧉ Duplicar · ⚡ modo fila/agora · 🛡 Buff · ↶ Desfazer (backup) ───────────────────────────────
        public void DuplicateSelectedAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_ae79c0cd; return; }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.U_Ai_SelectActionToDuplicate; return; }
            List<AiInstruction>? dup = AiAutomation.DuplicateActionInstructions(selectedScript, act.Action);
            if (dup == null) { RemoveActionSummary = string.Format(Strings.U_Ai_NotSimpleAction, act.Action.AbilityName); return; }
            int oldIdx = AutomationActions.IndexOf(act);
            ApplyAndSave(dup,
                string.Format(Strings.U_Ai_ActionDuplicated, act.Action.AbilityName, Path.GetFileName(selectedPath), ContextNoteForSamePlace(act)),
                oldIdx);
        }

        public void ToggleForceSelectedAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_d75fc417; return; }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null) { RemoveActionSummary = Strings.U_Ai_SelectAction; return; }
            if (act.Action.Kind != AiActionKind.Command)
            { RemoveActionSummary = Strings.F2_queue_now_mode_only_applies_to_command_a_505bc10b; return; }
            List<AiInstruction>? t = AiAutomation.ToggleForceInstructions(selectedScript, act.Action);
            if (t == null) { RemoveActionSummary = Strings.F2_i_did_not_find_the_call_for_this_action_b8e76233; return; }
            int oldIdx = AutomationActions.IndexOf(act);
            string mode = act.Action.ForcePerform ? Strings.U_Ai_ModeNormalQueue : Strings.U_Ai_ModeForced;
            ApplyAndSave(t, string.Format(Strings.U_Ai_ActionNowMode, act.Action.AbilityName, mode, Path.GetFileName(selectedPath), ContextNoteForSamePlace(act)), oldIdx);
        }

        public void AddSelfBuffAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { SelfBuffSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_817132ae; return; }
            if (AutomationWhenRandom && !TryParseU16Loose(AutomationRandomK, out _))
            { SelfBuffSummary = Strings.AiAdvancedInvalidNumber; return; }
            int k = ParseU16Loose(AutomationRandomK);
            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { SelfBuffSummary = hookError; return; }
            byte[] newAi;
            int wi = hook.WorkerIndex, ei = hook.EntrypointIndex;
            try { newAi = AiAutomation.AddSelfBuff(selectedScript, SelectedSelfBuff.FieldId, AutomationWhenRandom, k, wi, ei); }
            catch (Exception ex) { SelfBuffSummary = string.Format(Strings.U_Ai_CouldNotBuildBuff, ex.Message); return; }
            string when = AutomationWhenRandom ? string.Format(Strings.U_Ai_SometimesEveryShort, (k < 2 ? 2 : k)) : "always";
            if (!SaveNewAi(newAi, out string err)) { SelfBuffSummary = err; return; }
            SelfBuffSummary = string.Format(Strings.U_Ai_BuffApplied, SelectedSelfBuff.Name, when, hookLabel, Path.GetFileName(selectedPath));
        }

        public void AddForbiddenStatusLabAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { ForbiddenRiteSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_e09ca548; return; }
            AiTargetOption? target = SelectedForbiddenRiteTarget;
            if (target == null) { ForbiddenRiteSummary = Strings.F2_choose_a_simple_target_for_the_forbidden_53c5794e; return; }
            if (target.UseLinkedActionTarget)
            {
                ForbiddenRiteSummary = Strings.F2_same_target_as_the_linked_ability_only_w_9a178688;
                return;
            }
            AiForbiddenStatusPreset? status = SelectedForbiddenStatus;
            if (status == null) { ForbiddenRiteSummary = Strings.F2_choose_a_direct_anti_ribbon_status_death_76a41b48; return; }

            if (!TryParseU16Loose(ForbiddenStatusValue, out ushort value))
            { ForbiddenRiteSummary = Strings.AiAdvancedInvalidNumber; return; }
            if (AutomationWhenRandom && !TryParseU16Loose(AutomationRandomK, out _))
            { ForbiddenRiteSummary = Strings.AiAdvancedInvalidNumber; return; }
            int k = ParseU16Loose(AutomationRandomK);
            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { ForbiddenRiteSummary = hookError; return; }

            byte[] newAi;
            int wi = hook.WorkerIndex, ei = hook.EntrypointIndex;
            try { newAi = AiAutomation.AddChrPropertyWrite(selectedScript, target.Operand, status.FieldId, value, AutomationWhenRandom, k, wi, ei); }
            catch (Exception ex) { ForbiddenRiteSummary = string.Format(Strings.U_Ai_CouldNotBuildRite, ex.Message); return; }
            string when = AutomationWhenRandom ? string.Format(Strings.U_Ai_SometimesEveryShort, (k < 2 ? 2 : k)) : "always";
            if (!SaveNewAi(newAi, out string err)) { ForbiddenRiteSummary = err; return; }
            // Dev note: this writer emits writeChrProperty(target, field, value):
            // PUSHII <target> · PUSHII <field> · PUSHII <value> · CALLPOPA 7018.
            // Keep this shape hidden from the user-facing surface; it is implementation evidence for project maintainers.
            ForbiddenRiteSummary =
                string.Format(Strings.U_Ai_AntiRibbonApplied, target.Label, status.Name, value, when, Path.GetFileName(selectedPath));
        }

        public void AddSetStatFieldAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { PlantFieldSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_86d49d6b; return; }
            AiFieldPlantOption? field = SelectedPlantField;
            if (field == null) { PlantFieldSummary = Strings.U_Ai_PickBtlActorField; return; }
            if (!TryParseU16Loose(PlantFieldValue, out ushort value))
            { PlantFieldSummary = Strings.AiAdvancedInvalidNumber; return; }
            if (AutomationWhenRandom && !TryParseU16Loose(AutomationRandomK, out _))
            { PlantFieldSummary = Strings.AiAdvancedInvalidNumber; return; }
            int k = ParseU16Loose(AutomationRandomK);
            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { PlantFieldSummary = hookError; return; }
            byte[] newAi;
            int wi = hook.WorkerIndex, ei = hook.EntrypointIndex;
            try { newAi = AiAutomation.AddSetStatField(selectedScript, field.FieldId, value, AutomationWhenRandom, k, wi, ei); }
            catch (Exception ex) { PlantFieldSummary = string.Format(Strings.U_Ai_CouldNotBuildSetStatField, ex.Message); return; }
            string when = AutomationWhenRandom ? string.Format(Strings.U_Ai_WhenMaybe, k < 2 ? 2 : k) : "always";
            if (!SaveNewAi(newAi, out string err)) { PlantFieldSummary = err; return; }
            PlantFieldSummary =
                string.Format(Strings.U_Ai_PlantedSetStatField, field.Name, value, when, hookLabel, Path.GetFileName(selectedPath)) +
                Strings.U_Ai_SetStatFieldShape;
        }

        public void AddOverdriveGaugeAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { OverdriveGaugeSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_91412ee7; return; }

            ushort max = ParseU16Loose(OverdriveGaugeMax);
            if (max == 0)
            {
                OverdriveGaugeSummary = Strings.F2_overdrivemax_must_be_greater_than_zero_u_8f1fc18a;
                return;
            }

            if (!TryParseU16Loose(OverdriveGaugeCurrent, out ushort current))
            { OverdriveGaugeSummary = Strings.AiAdvancedInvalidNumber; return; }
            if (current > max)
            {
                current = max;
                OverdriveGaugeCurrent = current.ToString(CultureInfo.InvariantCulture);
            }

            string fallbackNote = "";
            if (!TryResolveCombatEntrypointForYunalesca(0, Strings.U_Ai_BattleStartLab, out AiEventHook hook, out string hookLabel, out string hookError)
                && !TryResolveAuthoringHook(out hook, out hookLabel, out hookError))
            { OverdriveGaugeSummary = hookError; return; }
            else if (!hookLabel.StartsWith(Strings.U_Ai_Start, StringComparison.OrdinalIgnoreCase))
            {
                fallbackNote = Strings.U_Ai_SetupOutsideBattleStart;
            }

            byte[] newAi;
            try
            {
                newAi = AiAutomation.AddOverdriveGaugeSetup(
                    selectedScript,
                    max,
                    current,
                    setMode: false,
                    mode: 0,
                    hook.WorkerIndex,
                    hook.EntrypointIndex,
                    showBar: true);
            }
            catch (Exception ex)
            {
                OverdriveGaugeSummary = string.Format(Strings.U_Ai_CouldNotBuildOdBar, ex.Message);
                return;
            }

            if (!SaveNewAi(newAi, out string err)) { OverdriveGaugeSummary = err; return; }
            OverdriveGaugeSummary =
                string.Format(Strings.U_Ai_OverdriveInstalled, hookLabel, max, current) +
                string.Format(Strings.U_Ai_AutoSaveOk, Path.GetFileName(selectedPath), fallbackNote);
        }

        public void AddOverdriveFinisherToSequence()
        {
            AiCommandOption? ability = SelectedOverdriveFinisherAbility;
            if (ability == null)
            {
                OverdriveRecipeSummary = Strings.U_Ai_PickSkillForOdSequence;
                return;
            }
            AiTargetOption? defaultTarget = SelectedAuthoringTarget ?? AuthoringTargetOptions.FirstOrDefault();
            var step = new OverdriveFinisherStep(ability, AuthoringTargetOptions.ToList(), defaultTarget);
            OverdriveFinisherSequence.Add(step);
            SelectedOverdriveFinisherStep = step;
            OverdriveRecipeSummary = string.Format(Strings.U_Ai_AddedToOdSequence, ability.Name);
        }

        public void RemoveSelectedOverdriveFinisher()
        {
            OverdriveFinisherStep? step = SelectedOverdriveFinisherStep;
            if (step == null)
            {
                OverdriveRecipeSummary = Strings.U_Ai_SelectOdStepToRemove;
                return;
            }
            int index = OverdriveFinisherSequence.IndexOf(step);
            if (index >= 0) OverdriveFinisherSequence.RemoveAt(index);
            SelectedOverdriveFinisherStep = OverdriveFinisherSequence.ElementAtOrDefault(Math.Min(index, OverdriveFinisherSequence.Count - 1));
            OverdriveRecipeSummary = Strings.F2_step_removed_from_sequence_nothing_has_b_1c5cf5e1;
        }

        public void MoveSelectedOverdriveFinisher(bool up)
        {
            OverdriveFinisherStep? step = SelectedOverdriveFinisherStep;
            if (step == null) return;
            int index = OverdriveFinisherSequence.IndexOf(step);
            int next = up ? index - 1 : index + 1;
            if (index < 0 || next < 0 || next >= OverdriveFinisherSequence.Count) return;
            OverdriveFinisherSequence.Move(index, next);
            SelectedOverdriveFinisherStep = step;
        }

        public void ApplyOverdriveRecipeAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { OverdriveRecipeSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_a9680042; return; }

            // Validate only enabled sources. Disabled inputs are drafts, while
            // an invalid active value must never become zero or a default guard.
            var numbers = new List<string> { OverdriveGaugeMax };
            void Include(bool enabled, params string[] values) { if (enabled) numbers.AddRange(values); }
            Include(SelectedOverdriveStartMode?.Kind == OverdriveStartModeKind.Custom, OverdriveRecipeCustomStart);
            Include(OverdriveChargePerTurn, OverdriveChargePerTurnAmount);
            Include(OverdriveChargeOnHit, OverdriveChargeOnHitAmount);
            Include(OverdriveChargeAfterSelectedAction, OverdriveChargeAfterSelectedActionAmount);
            Include(OverdriveChargeChance, OverdriveChargeChanceAmount, OverdriveChargeChanceK);
            Include(OverdriveChargeHpBelow, OverdriveChargeHpAmount, OverdriveChargeHpPercent);
            Include(OverdriveChargeHpRange, OverdriveChargeHpRangeAmount, OverdriveChargeHpRangeMin, OverdriveChargeHpRangeMax);
            Include(OverdriveChargeEveryNTurns, OverdriveChargeEveryNTurnsAmount, OverdriveChargeEveryNTurnsInterval);
            Include(OverdriveChargeSelfStatus, OverdriveChargeSelfStatusAmount);
            Include(OverdriveChargePartyDead, OverdriveChargePartyDeadAmount);
            Include(OverdriveChargeDamageTakenPercent, OverdriveChargeDamageTakenPercentAmount, OverdriveChargeDamageTakenPercentThreshold);
            Include(OverdriveChargeDamageZero, OverdriveChargeDamageZeroAmount);
            Include(OverdriveChargeLastAttacker, OverdriveChargeLastAttackerAmount);
            Include(OverdriveChargePhysicalHitLab, OverdriveChargePhysicalHitAmount);
            Include(OverdriveChargeMagicalHitLab, OverdriveChargeMagicalHitAmount);
            if (numbers.Any(text => !TryParseU16Loose(text, out _)))
            { OverdriveRecipeSummary = Strings.AiAdvancedInvalidNumber; return; }

            ushort max = ParseU16Loose(OverdriveGaugeMax);
            if (max == 0)
            {
                OverdriveRecipeSummary = Strings.F2_max_must_be_greater_than_zero_use_100_fo_49ca81b6;
                return;
            }

            ushort start = ResolveOverdriveStart(max);
            string balanceWarning = BuildOverdriveBalanceWarning(max);
            List<(ushort command, AiTargetRecipe target)> finishers = ResolveOverdriveFinishers();
            if (finishers.Count == 0)
            {
                OverdriveRecipeSummary = Strings.F2_add_at_least_one_ability_to_the_overdriv_3ba853d7;
                return;
            }

            if (!TryResolveCombatEntrypointForYunalesca(0, Strings.U_Ai_BattleStart, out AiEventHook setupHook, out string setupLabel, out string setupError))
            {
                OverdriveRecipeSummary = string.Format(Strings.U_Ai_NeedStartEntrypoint, setupError);
                return;
            }
            if (!TryResolveOnTurnHook(out AiEventHook onTurnHook, out string onTurnError))
            {
                OverdriveRecipeSummary = string.Format(Strings.U_Ai_NeedOnTurn, onTurnError);
                return;
            }

            var notes = new List<string>();
            AiScriptFile working = selectedScript;
            byte[] workingAi = selectedScript.OriginalAiFileBytes;

            bool ApplyStep(Func<AiScriptFile, byte[]> step, string label)
            {
                try
                {
                    workingAi = step(working);
                    working = AiScript_File.Read(workingAi);
                    notes.Add(label);
                    return true;
                }
                catch (Exception ex)
                {
                    OverdriveRecipeSummary = string.Format(Strings.U_Ai_RecipeStoppedAt, label, ex.Message);
                    return false;
                }
            }

            if (OverdriveCleanExistingRecipe)
            {
                try
                {
                    int cleanMinStart = ResolveOverdriveCleanMinStart(working);
                    workingAi = AiAutomation.StripGeneratedOverdriveLab(working, out int stripped, cleanMinStart);
                    if (stripped > 0)
                    {
                        working = AiScript_File.Read(workingAi);
                        notes.Add(string.Format(Strings.U_Ai_ClearedPreviousOd, stripped));
                    }
                }
                catch (Exception ex)
                {
                    OverdriveRecipeSummary = string.Format(Strings.U_Ai_FailedClearingOd, ex.Message);
                    return;
                }
            }

            if (OverdriveChargeAfterSelectedAction)
            {
                AiDetectedAction? selectedAction = SelectedAutomationAction?.Action;
                if (selectedAction is not { Kind: AiActionKind.Command, Removable: true })
                {
                    OverdriveRecipeSummary = Strings.F2_charge_after_selected_action_requires_a__82ffc622;
                    return;
                }

                ushort amount = ParseU16Loose(OverdriveChargeAfterSelectedActionAmount);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_after_selected_action_must_have_a_104e6acc; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeAfterAction(s, selectedAction, amount),
                        string.Format(Strings.U_Ai_AfterSelectedAction, selectedAction.AbilityName, amount)))
                    return;
            }

            // AppendGuardedAction chains newest-first. Apply onTurn pieces in reverse runtime order.
            if (!ApplyStep(s => AiAutomation.AddOverdriveFinisherSequence(
                    s, finishers, OverdriveResetAfterFinish,
                    onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex, stopAfterAction: true),
                    string.Format(Strings.U_Ai_WhenFullSkills, finishers.Count, (OverdriveResetAfterFinish ? " + reset" : ""))))
                return;

            if (!ApplyStep(s => AiAutomation.AddOverdriveClampToMax(
                    s, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                    "clamp Max at onTurn"))
                return;

            if (OverdriveChargeChance)
            {
                ushort amount = ParseU16Loose(OverdriveChargeChanceAmount);
                int k = ParseU16Loose(OverdriveChargeChanceK);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_by_chance_must_have_a_value_great_944bfda4; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildRandomGuard(k), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"chance 1 em {(k < 2 ? 2 : k)}: +{amount}"))
                    return;
            }

            if (OverdriveChargeHpBelow)
            {
                ushort amount = ParseU16Loose(OverdriveChargeHpAmount);
                ushort percent = ParseU16Loose(OverdriveChargeHpPercent);
                percent = (ushort)Math.Clamp(percent == 0 ? 50 : (int)percent, 1, 100);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_by_low_hp_must_have_a_value_great_55dc7472; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildHpBelowPercentGuard(percent), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"HP < {percent}%: +{amount}"))
                    return;
            }

            if (OverdriveChargeHpRange)
            {
                ushort amount = ParseU16Loose(OverdriveChargeHpRangeAmount);
                ushort minPercent = ParseU16Loose(OverdriveChargeHpRangeMin);
                ushort maxPercent = ParseU16Loose(OverdriveChargeHpRangeMax);
                minPercent = (ushort)Math.Clamp((int)minPercent, 0, 99);
                maxPercent = (ushort)Math.Clamp(maxPercent == 0 ? 100 : (int)maxPercent, 1, 100);
                if (maxPercent <= minPercent) maxPercent = (ushort)Math.Min(100, minPercent + 1);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_by_hp_range_must_have_a_value_gre_b30f7343; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildHpBetweenPercentGuard(minPercent, maxPercent), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"HP entre {minPercent}% e {maxPercent}%: +{amount}"))
                    return;
            }

            if (OverdriveChargeSelfStatus)
            {
                AiBuffPreset status = SelectedOverdriveSelfStatus;
                if (string.IsNullOrWhiteSpace(status.Name))
                {
                    OverdriveRecipeSummary = Strings.F2_choose_a_monster_status_field_for_the_ch_4a52cccd;
                    return;
                }

                ushort amount = ParseU16Loose(OverdriveChargeSelfStatusAmount);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_by_status_on_monster_must_have_a__168500f4; return; }
                IReadOnlyList<AiInstruction> statusGuard = status.FieldId == OverdriveAnyPositiveStatusField
                    ? AiAutomation.BuildAnyPositiveSelfStatusGuard()
                    : AiAutomation.BuildSelfFieldGreaterThanGuard(status.FieldId, 0);
                string statusLabel = status.FieldId == OverdriveAnyPositiveStatusField
                    ? Strings.F2_any_positive_status_7c97a32c
                    : status.Name;
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, statusGuard, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        string.Format(Strings.U_Ai_StatusOnMonster, statusLabel, amount)))
                    return;
            }

            if (OverdriveChargePartyDead)
            {
                ushort amount = ParseU16Loose(OverdriveChargePartyDeadAmount);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_party_condition_must_have_a_v_d02dc27d; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildFrontlineDeadExistsGuard(), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"party/alvo: algum personagem morto: +{amount}"))
                    return;
            }

            if (OverdriveChargeEveryNTurns)
            {
                ushort amount = ParseU16Loose(OverdriveChargeEveryNTurnsAmount);
                ushort interval = ParseU16Loose(OverdriveChargeEveryNTurnsInterval);
                interval = (ushort)Math.Clamp(interval == 0 ? 3 : (int)interval, 2, 999);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_every_n_turns_must_have_a_value_g_88234cb5; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildEveryNTurnsGuard(interval), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"a cada {interval} turno(s)/contador: +{amount}"))
                    return;
            }

            if (OverdriveChargePerTurn)
            {
                ushort amount = ParseU16Loose(OverdriveChargePerTurnAmount);
                if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_turn_must_have_a_value_greate_dad5f0ac; return; }
                if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                        s, amount, AiAutomation.BuildAlwaysGuard(), onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex),
                        $"por turno: +{amount}"))
                    return;
            }

            bool needsHitHook = OverdriveChargeOnHit
                                || OverdriveChargeDamageTakenPercent
                                || OverdriveChargeDamageZero
                                || OverdriveChargeLastAttacker
                                || OverdriveChargePhysicalHitLab
                                || OverdriveChargeMagicalHitLab
                                || OverdriveUseWhenFullOutsideTurn;
            if (needsHitHook)
            {
                if (!TryResolveOnHitHook(out AiEventHook hitHook, out string hitError))
                {
                    OverdriveRecipeSummary = string.Format(Strings.U_Ai_DidNotApplyOnHit, hitError);
                    return;
                }
                string hitLabel = HookLabel(hitHook, "onHit real");
                if (OverdriveUseWhenFullOutsideTurn)
                {
                    if (!ApplyStep(s => AiAutomation.AddOverdriveFinisherSequence(
                            s, finishers, OverdriveResetAfterFinish,
                            hitHook.WorkerIndex, hitHook.EntrypointIndex, stopAfterAction: true),
                            string.Format(Strings.U_Ai_WhenFullOffTurn, hitLabel, finishers.Count, (OverdriveResetAfterFinish ? " + reset" : ""))))
                        return;
                }
                if (!ApplyStep(s => AiAutomation.AddOverdriveClampToMax(
                        s, hitHook.WorkerIndex, hitHook.EntrypointIndex),
                        "clamp to Max on onHit"))
                    return;
                if (OverdriveChargeOnHit)
                {
                    ushort amount = ParseU16Loose(OverdriveChargeOnHitAmount);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_on_hit_received_must_have_a_value_3dc939b9; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount, AiAutomation.BuildAlwaysGuard(), hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            $"{hitLabel}: +{amount}"))
                        return;
                }
                if (OverdriveChargeDamageTakenPercent)
                {
                    ushort amount = ParseU16Loose(OverdriveChargeDamageTakenPercentAmount);
                    ushort percent = ParseU16Loose(OverdriveChargeDamageTakenPercentThreshold);
                    percent = (ushort)Math.Clamp(percent == 0 ? 25 : (int)percent, 1, 100);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_damage_received_must_have_a_v_8a7907c9; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount, AiAutomation.BuildLastDamageTakenAtLeastPercentMaxHpGuard(percent), hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            string.Format(Strings.U_Ai_DamageTakenPercent, percent, amount)))
                        return;
                }
                if (OverdriveChargeDamageZero)
                {
                    ushort amount = ParseU16Loose(OverdriveChargeDamageZeroAmount);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_zero_avoided_damage_must_have_d2b142f1; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount, AiAutomation.BuildLastDamageTakenEqualsGuard(0), hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            string.Format(Strings.U_Ai_RetaliationZero, hitLabel, amount)))
                        return;
                }
                if (OverdriveChargeLastAttacker)
                {
                    ushort amount = ParseU16Loose(OverdriveChargeLastAttackerAmount);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_last_attacker_must_have_a_val_7db50c65; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount,
                            AiAutomation.BuildActorFieldGreaterThanGuard(AiAutomation.LastAttackerTarget, AiAutomation.ChrFieldIsAlive, 0),
                            hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            string.Format(Strings.U_Ai_LastAttackerAlive, hitLabel, amount)))
                        return;
                }
                if (OverdriveChargePhysicalHitLab)
                {
                    ushort amount = ParseU16Loose(OverdriveChargePhysicalHitAmount);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_physical_hit_must_have_a_valu_1b593bdb; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount,
                            AiAutomation.BuildUsedCommandDamageTypeGuard(AiAutomation.DamageTypePhysical),
                            hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            string.Format(Strings.U_Ai_RetaliationPhysical, hitLabel, amount)))
                        return;
                }
                if (OverdriveChargeMagicalHitLab)
                {
                    ushort amount = ParseU16Loose(OverdriveChargeMagicalHitAmount);
                    if (amount == 0) { OverdriveRecipeSummary = Strings.F2_charge_per_magical_hit_must_have_a_value_abded02c; return; }
                    if (!ApplyStep(s => AiAutomation.AddOverdriveChargeSource(
                            s, amount,
                            AiAutomation.BuildUsedCommandDamageTypeGuard(AiAutomation.DamageTypeMagical),
                            hitHook.WorkerIndex, hitHook.EntrypointIndex),
                            string.Format(Strings.U_Ai_RetaliationMagical, hitLabel, amount)))
                        return;
                }
            }

            AddForbiddenResearchNotes(notes);

            if (!ApplyStep(s => AiAutomation.AddOverdriveGaugeSetup(
                    s, max, start, setMode: false, mode: 0,
                    setupHook.WorkerIndex, setupHook.EntrypointIndex, showBar: OverdriveRecipeShowBar),
                    $"{setupLabel}: setup {(OverdriveRecipeShowBar ? "com barra" : "sem barra")} {start}/{max}"))
                return;

            if (!SaveNewAi(workingAi, out string err)) { OverdriveRecipeSummary = err; return; }
            string seq = string.Join(" -> ", finishers.Select(f => AiCommandId.Decode(f.command).Name));
            OverdriveRecipeSummary =
                string.Format(Strings.U_Ai_OdRecipeApplied, string.Join(" · ", notes), seq) +
                string.Format(Strings.U_Ai_OdSavedBackup, Path.GetFileName(selectedPath)) +
                balanceWarning;
        }

        public void ClearOverdriveLabAction()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                OverdriveRecipeSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_34c1484a;
                return;
            }

            byte[] cleaned;
            int stripped;
            try
            {
                cleaned = AiAutomation.StripGeneratedOverdriveLab(selectedScript, out stripped, ResolveOverdriveCleanMinStart(selectedScript));
            }
            catch (Exception ex)
            {
                OverdriveRecipeSummary = string.Format(Strings.U_Ai_CouldNotClearOd, ex.Message);
                return;
            }

            if (stripped <= 0)
            {
                OverdriveRecipeSummary = Strings.F2_no_editor_generated_overdrive_block_foun_099ed952;
                return;
            }

            if (!SaveNewAi(cleaned, out string err)) { OverdriveRecipeSummary = err; return; }
            OverdriveRecipeSummary = string.Format(Strings.U_Ai_OdCleared, stripped, Path.GetFileName(selectedPath));
        }

        ushort ResolveOverdriveStart(ushort max)
        {
            ushort value = SelectedOverdriveStartMode?.Kind switch
            {
                OverdriveStartModeKind.Empty => 0,
                OverdriveStartModeKind.Custom => ParseU16Loose(OverdriveRecipeCustomStart),
                _ => max,
            };
            return value > max ? max : value;
        }

        string BuildOverdriveBalanceWarning(ushort max)
        {
            var charges = new List<ushort>();
            if (OverdriveChargePerTurn) charges.Add(ParseU16Loose(OverdriveChargePerTurnAmount));
            if (OverdriveChargeOnHit) charges.Add(ParseU16Loose(OverdriveChargeOnHitAmount));
            if (OverdriveChargeHpBelow) charges.Add(ParseU16Loose(OverdriveChargeHpAmount));
            if (OverdriveChargeChance) charges.Add(ParseU16Loose(OverdriveChargeChanceAmount));
            if (OverdriveChargeDamageTakenPercent) charges.Add(ParseU16Loose(OverdriveChargeDamageTakenPercentAmount));
            if (OverdriveChargeAfterSelectedAction) charges.Add(ParseU16Loose(OverdriveChargeAfterSelectedActionAmount));
            if (OverdriveChargeEveryNTurns) charges.Add(ParseU16Loose(OverdriveChargeEveryNTurnsAmount));
            if (OverdriveChargeHpRange) charges.Add(ParseU16Loose(OverdriveChargeHpRangeAmount));
            if (OverdriveChargeSelfStatus) charges.Add(ParseU16Loose(OverdriveChargeSelfStatusAmount));
            if (OverdriveChargePartyDead) charges.Add(ParseU16Loose(OverdriveChargePartyDeadAmount));
            if (OverdriveChargeLastAttacker) charges.Add(ParseU16Loose(OverdriveChargeLastAttackerAmount));
            if (OverdriveChargeDamageZero) charges.Add(ParseU16Loose(OverdriveChargeDamageZeroAmount));
            if (OverdriveChargePhysicalHitLab) charges.Add(ParseU16Loose(OverdriveChargePhysicalHitAmount));
            if (OverdriveChargeMagicalHitLab) charges.Add(ParseU16Loose(OverdriveChargeMagicalHitAmount));
            ushort biggest = charges.Where(v => v > 0).DefaultIfEmpty((ushort)0).Max();
            if (max <= 1)
                return Strings.U_Ai_MaxOneWarning;
            if (biggest >= max)
                return string.Format(Strings.U_Ai_ChargeExceedsMax, biggest, max);
            return string.Empty;
        }

        void AddForbiddenResearchNotes(List<string> notes)
        {
            var marked = new List<string>();
            if (OverdriveChargeReducedHitLab) marked.Add("reduziu/bloqueou dano");
            if (OverdriveChargeStatusSufferedLab) marked.Add("status sofrido");
            if (OverdriveChargeHealedLab) marked.Add(Strings.U_Ai_HealingReceived);
            if (marked.Count > 0)
                notes.Add(string.Format(Strings.U_Ai_MarkedForResearch, string.Join(", ", marked)));
        }

        List<(ushort command, AiTargetRecipe target)> ResolveOverdriveFinishers()
        {
            IEnumerable<OverdriveFinisherStep> source = OverdriveFinisherSequence;
            if (SelectedOverdriveFinishMode?.Kind == OverdriveFinishModeKind.Single)
                source = source.Take(1);
            return source
                .Select(s => (s.Ability.Operand, s.SelectedTarget?.ToRecipe() ?? AuthoringTargetOptions.FirstOrDefault()?.ToRecipe() ?? new AiTargetRecipe(AiTargetRecipeKind.Literal, 0xFFF2)))
                .Distinct()
                .ToList();
        }

        // ── 🎭 Presets (#10) — compõem AddAbility/AddSelfBuff provados. Cada passo é um grow validado+salvo. ──────
        bool PresetReady()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { PresetSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_4b1f5829; return false; }
            return true;
        }

        bool TrySelfBuffField(ushort fieldId)
        {
            return TryApplyYunalescaSelfBuffs(Strings.U_Ai_QuickBuff, new[] { fieldId });
        }

        bool TryApplyYunalescaSelfBuffs(string presetName, IReadOnlyList<ushort> fieldIds)
        {
            if (!TryResolveYunalescaConditionHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { PresetSummary = $"Preset abortado: {hookError}"; return false; }
            if (!TryBuildYunalescaGuard(out List<AiInstruction> guard, out string guardLabel, out string guardError))
            { PresetSummary = $"Preset abortado: {guardError}"; return false; }
            string conditionLabel = YunalescaConditionBadge;
            byte[] ai;
            try { ai = AiAutomation.AddChrPropertyWritesWithGuard(selectedScript!, AiSelfTarget, 1, fieldIds, guard, hook.WorkerIndex, hook.EntrypointIndex); }
            catch (Exception ex) { PresetSummary = $"Preset abortado montando {presetName}: {ex.Message}"; return false; }
            if (!SaveNewAi(ai, out string err)) { PresetSummary = $"Preset abortado: {err}"; return false; }
            string buffs = string.Join(" + ", fieldIds.Select(BuffNameForField));
            ClearYunalescaConditionAfterApply();
            PresetSummary =
                string.Format(Strings.U_Ai_PresetApplied, presetName, Path.GetFileName(selectedPath), buffs, conditionLabel, guardLabel, hookLabel) +
                GuardedChainContinuationNote +
                " ⚠️ Confirme in-game (RT2).";
            return true;
        }

        bool TryApplyYunalescaQueuedAbility(string presetName, ushort commandOperand, string commandName)
        {
            if (ResolveYunalescaHookKind() == YunalescaHookKind.BattleStart)
            {
                PresetSummary =
                    string.Format(Strings.U_Ai_PresetAbortedSpell, ResolveYunalescaHookKindLabel(), commandName) +
                    Strings.F2_rt2_showed_that_commands_spells_on_this__ced03759;
                return false;
            }
            if (!TryResolveYunalescaConditionHook(out AiEventHook hook, out string hookLabel, out string hookError))
            { PresetSummary = $"Preset abortado: {hookError}"; return false; }
            if (!TryBuildYunalescaGuard(out List<AiInstruction> guard, out string guardLabel, out string guardError))
            { PresetSummary = $"Preset abortado: {guardError}"; return false; }
            string conditionLabel = YunalescaConditionBadge;

            byte[] ai;
            try { ai = AiAutomation.AddQueuedAbilityWithGuard(selectedScript!, commandOperand, guard, hook.WorkerIndex, hook.EntrypointIndex); }
            catch (Exception ex) { PresetSummary = $"Preset abortado montando {presetName}: {ex.Message}"; return false; }
            if (!SaveNewAi(ai, out string err)) { PresetSummary = $"Preset abortado: {err}"; return false; }

            ClearYunalescaConditionAfterApply();
            PresetSummary =
                string.Format(Strings.U_Ai_PresetAppliedSpell, presetName, Path.GetFileName(selectedPath), commandName, commandOperand, conditionLabel, guardLabel, hookLabel) +
                "Does not write the four Nuls via writeChrProperty. The pre-condition was cleared for the next ritual." +
                GuardedChainContinuationNote +
                " ⚠️ Confirme in-game (RT2).";
            return true;
        }

        string BuffNameForField(ushort fieldId)
        {
            foreach (AiBuffPreset p in AiAutomation.SelfBuffPresets)
                if (p.FieldId == fieldId) return p.Name;
            return $"0x{fieldId:X2}";
        }

        bool _lastForcePickedAbilityUsedHookAppend;

        bool TryForcePickedAbility(bool random, int k)
        {
            _lastForcePickedAbilityUsedHookAppend = false;
            AiCommandOption? ability = SelectedAutomationAbility;
            if (ability == null) { PresetSummary = Strings.F2_choose_a_skill_in_the_picker_above_for_t_0be6d5d7; return false; }
            AiActionVm? template = SelectedAutomationAction;
            bool hasTemplate = template != null && template.Action.Kind == AiActionKind.Command && template.Action.Removable;
            _lastForcePickedAbilityUsedHookAppend = !hasTemplate;
            AiEventHook hook = default;
            if (!hasTemplate && !TryResolveAuthoringHook(out hook, out _, out string hookError))
            { PresetSummary = $"Preset abortado: {hookError}"; return false; }
            byte[] ai;
            try
            {
                ai = hasTemplate
                    ? AiAutomation.InsertCommandAfterAction(selectedScript!, template!.Action, ability.Operand, random, k, AutomationStopAfterAction)
                    : AiAutomation.AddAbility(selectedScript!, ability.Operand, random, k, hook.WorkerIndex, hook.EntrypointIndex, AutomationStopAfterAction);
            }
            catch (Exception ex) { PresetSummary = string.Format(Strings.U_Ai_PresetAbortedBuilding, ex.Message); return false; }
            if (!SaveNewAi(ai, out string err)) { PresetSummary = string.Format(Strings.U_Ai_PresetAborted, err); return false; }
            return true;
        }

        public void ApplyPresetDefensive()   // Protect + Haste em si (sempre)
        {
            if (!PresetReady()) return;
            TryApplyYunalescaSelfBuffs("Defensivo P+H", new ushort[] { 0x31, 0x38 });
        }

        void ApplyPresetSingleBuff(string presetName, ushort fieldId)
        {
            if (!PresetReady()) return;
            TryApplyYunalescaSelfBuffs(presetName, new[] { fieldId });
        }

        public void ApplyPresetHaste() => ApplyPresetSingleBuff("Haste inicial", 0x38);

        public void ApplyPresetProtect() => ApplyPresetSingleBuff("Protect inicial", 0x31);

        public void ApplyPresetShell() => ApplyPresetSingleBuff("Shell inicial", 0x30);

        public void ApplyPresetReflect() => ApplyPresetSingleBuff("Reflect inicial", 0x32);

        public void ApplyPresetRegen() => ApplyPresetSingleBuff("Regen inicial", 0x37);

        public void ApplyPresetNulBlaze() => ApplyPresetSingleBuff("NulBlaze inicial", 0x34);

        public void ApplyPresetNulTide() => ApplyPresetSingleBuff("NulTide inicial", 0x33);

        public void ApplyPresetNulShock() => ApplyPresetSingleBuff("NulShock inicial", 0x35);

        public void ApplyPresetNulFrost() => ApplyPresetSingleBuff("NulFrost inicial", 0x36);

        public void ApplyPresetNulAll()
        {
            if (!PresetReady()) return;
            TryApplyYunalescaQueuedAbility("NulAll inicial", YunalescaNulAllCommand, "NulAll");
        }

        public void ApplyPresetAggressive()   // forçar a habilidade escolhida, sempre
        {
            if (!PresetReady()) return;
            if (!TryForcePickedAbility(random: false, k: 0)) return;
            PresetSummary = string.Format(Strings.U_Ai_PresetAggressive, Path.GetFileName(selectedPath), SelectedAutomationAbility?.Name) +
                (AutomationStopAfterAction ? StopAfterActionNote : "") +
                (_lastForcePickedAbilityUsedHookAppend ? GuardedChainContinuationNote : "") +
                " ⚠️ Confirme in-game (RT2).";
        }

        public void ApplyPresetEnrage()   // forçar a escolhida (sempre) + Haste em si
        {
            if (!PresetReady()) return;
            if (!TryForcePickedAbility(random: false, k: 0)) return;
            if (!TrySelfBuffField(0x38)) return;   // Haste
            PresetSummary = string.Format(Strings.U_Ai_PresetEnrage, Path.GetFileName(selectedPath), SelectedAutomationAbility?.Name) +
                (AutomationStopAfterAction ? StopAfterActionNote : "") +
                (_lastForcePickedAbilityUsedHookAppend ? GuardedChainContinuationNote : "") +
                " ⚠️ Confirme in-game (RT2).";
        }

        public void ApplyPresetAlternate()   // revezar (1-em-2) a habilidade escolhida
        {
            if (!PresetReady()) return;
            if (!TryForcePickedAbility(random: true, k: 2)) return;
            PresetSummary = string.Format(Strings.U_Ai_PresetRotate, Path.GetFileName(selectedPath), SelectedAutomationAbility?.Name) +
                (AutomationStopAfterAction ? StopAfterActionNote : "") +
                (_lastForcePickedAbilityUsedHookAppend ? GuardedChainContinuationNote : "") +
                " ⚠️ Confirme in-game (RT2).";
        }

        public void ApplyPresetAlternate3()   // revezar (1-em-3) a habilidade escolhida
        {
            if (!PresetReady()) return;
            if (!TryForcePickedAbility(random: true, k: 3)) return;
            PresetSummary = string.Format(Strings.U_Ai_PresetRotateThird, Path.GetFileName(selectedPath), SelectedAutomationAbility?.Name) +
                (AutomationStopAfterAction ? StopAfterActionNote : "") +
                (_lastForcePickedAbilityUsedHookAppend ? GuardedChainContinuationNote : "") +
                " ⚠️ Confirme in-game (RT2).";
        }

        public void ApplyPresetDuplicate()
        {
            DuplicateSelectedAction();
            PresetSummary = RemoveActionSummary.StartsWith("✅", StringComparison.Ordinal)
                ? string.Format(Strings.U_Ai_PresetRepeatAction, RemoveActionSummary)
                : RemoveActionSummary;
        }

        public void ApplyPresetMultiCast()
        {
            InsertSecondAbility();
            PresetSummary = RemoveActionSummary.StartsWith("✅", StringComparison.Ordinal)
                ? string.Format(Strings.U_Ai_PresetTwoActionSeq, RemoveActionSummary)
                : RemoveActionSummary;
        }

        public void RestoreBackup()
        {
            if (selectedPath == null || selectedScript == null) { RemoveActionSummary = Strings.F2_no_monster_selected_0941a3d3; return; }
            string bak = selectedPath + ".prev.bak";
            if (!File.Exists(bak)) { RemoveActionSummary = Strings.F2_no_backup_prev_bak_the_backup_is_created_f4aedd79; return; }
            try { AiAdvancedFileWriter.RestoreBackup(selectedPath, selectedScript.OriginalAiFileBytes); }
            catch (Exception ex) { RemoveActionSummary = string.Format(Strings.U_Ai_FailedRestoringBackup, ex.Message); return; }
            bool reloaded = ReloadSelectedFromDisk();
            RemoveActionSummary = reloaded
                ? string.Format(Strings.U_Ai_Undone, Path.GetFileName(selectedPath))
                : string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath));
        }

        // ── 📋 Copiar IA de outro monstro · ♻️ Restaurar IA original ─────────────────────────────────────
        public void CopyAiFromSource()
        {
            if (selectedScript == null || selectedPath == null) { CopyRestoreSummary = Strings.F2_select_the_destination_monster_first_666c09b0; return; }
            if (CopySource == null) { CopyRestoreSummary = Strings.F2_choose_the_source_monster_from_whom_to_c_2664efed; return; }
            if (string.Equals(CopySource.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            { CopyRestoreSummary = Strings.F2_source_and_destination_are_the_same_mons_b8db3e82; return; }

            byte[]? srcAi;
            try { srcAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(CopySource.Path)); }
            catch (Exception ex) { CopyRestoreSummary = string.Format(Strings.U_Ai_FailedReadingSource, ex.Message); return; }
            if (srcAi == null) { CopyRestoreSummary = string.Format(Strings.U_Ai_NoAiFileToCopy, CopySource.Id); return; }

            string srcId = CopySource.Id, srcName = CopySource.MonsterName;
            if (!SaveNewAi(srcAi, out string err)) { CopyRestoreSummary = err; return; }
            CopyRestoreSummary = $"📋 IA de {srcId} {srcName} copiada para {Path.GetFileName(selectedPath)} (stats/loot deste mantidos). Backup .prev.bak guardado. ⚠️ Confirme in-game (RT2).";
        }

        public void RestoreOriginalAi()
        {
            if (selectedScript == null || selectedPath == null || selectedMonster == null)
            { CopyRestoreSummary = Strings.U_Ai_SelectMonster; return; }
            string? refPath = ReferenceMonsterPath(selectedMonster.Id);
            if (refPath == null)
            { CopyRestoreSummary = Strings.U_Ai_VanillaNotFound; return; }

            byte[]? refAi;
            try { refAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(refPath)); }
            catch (Exception ex) { CopyRestoreSummary = string.Format(Strings.U_Ai_FailedReadingOriginal, ex.Message); return; }
            if (refAi == null) { CopyRestoreSummary = "The original monster has no AiFile."; return; }

            string id = selectedMonster.Id;
            if (!SaveNewAi(refAi, out string err)) { CopyRestoreSummary = err; return; }
            CopyRestoreSummary = string.Format(Strings.U_Ai_AiRestoredOriginal, id);
        }

        // The vanilla monster file in the extracted reference tree: <ffx_ps2>\ffx\master\jppc\battle\mon\_mNNN\mNNN.bin.
        string? ReferenceMonsterPath(string monsterId)
        {
            string? root = FFXProjectEditor.Services.Project_Service.Instance.Path_FfxPs2Root;
            if (string.IsNullOrWhiteSpace(root)) return null;
            string path = Path.Combine(root, "ffx", "master", "jppc", "battle", "mon", "_" + monsterId, monsterId + ".bin");
            return IsIndependentAiReference(path, selectedPath) ? path : null;
        }

        internal static bool IsIndependentAiReference(string referencePath, string? workingPath)
        {
            if (string.IsNullOrWhiteSpace(workingPath) || !File.Exists(referencePath)) return false;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(Path.GetFullPath(referencePath), Path.GetFullPath(workingPath), comparison)) return false;
            // A symlink alias cannot serve as proof of an independent vanilla reference.
            return !FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard.ContainsReparsePointInExistingChain(referencePath) &&
                !FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard.ContainsReparsePointInExistingChain(workingPath);
        }

        // Copy the current monster file to <path>.prev.bak (one-level undo) before overwriting it.
        void WriteMonsterWithBackup(byte[] outBin, bool applyingAssembler = false)
        {
            if (!applyingAssembler && HasPendingAssemblerEdits)
                throw new InvalidOperationException(Strings.AiAdvancedPendingEdits);
            byte[] editedAi = AiScript_File.SliceAiFileFromMonster(outBin)
                ?? throw new InvalidDataException(Strings.U_Ai_AslNoAiPartition);
            // All callers edit the AI partition. Re-splice into the latest
            // container to retain unrelated monster changes, with a fresh AI
            // precondition, mandatory backup and atomic file promotion.
            AiAdvancedFileWriter.Save(selectedPath!, selectedScript!.OriginalAiFileBytes, editedAi);
        }

        // Save an APPENDED/grown AiFile (from AppendGuardedAction): structural validate -> grow-splice -> write+backup
        // -> reload. Returns false (with a message in err) on validation/IO failure.
        bool SaveNewAi(byte[] newAi, out string err)
        {
            err = string.Empty;
            AiValidationReport vr = AiValidator.ValidateRebuilt(newAi, selectedScript!.OriginalAiFileBytes.Length);
            if (!vr.IsValid) { err = string.Format(Strings.U_Ai_StructuralValidationFailed, vr.Errors.FirstOrDefault()?.Message); return false; }
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath!);
                WriteMonsterWithBackup(AiScript_File.SpliceAiFileIntoMonsterGrow(monster, newAi));
            }
            catch (Exception ex) { err = $"Save abortado no splice: {ex.Message}"; return false; }
            if (!ReloadSelectedFromDisk())
            { err = string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath)); return false; }
            return true;
        }

        string BuildBehaviorSummary()
        {
            if (selectedScript == null || !selectedScript.HasScript)
                return Strings.F2_no_ai_script_in_this_monster_be755c8f;
            if (_allDetectedActions.Count == 0)
                return string.Format(Strings.U_Ai_NoActionRecognized, selectedScript.Workers.Count);

            var cmds  = _allDetectedActions.Where(a => a.Kind == AiActionKind.Command).ToList();
            var buffs = _allDetectedActions.Where(a => a.Kind == AiActionKind.Buff).ToList();
            var stats = _allDetectedActions.Where(a => a.Kind == AiActionKind.Stat).ToList();

            var parts = new List<string>();
            if (cmds.Count > 0)
            {
                var names = cmds.Take(6).Select(a => AiCommandLabels.Get(a.CommandOperand) ?? a.AbilityName);
                string list = string.Join(", ", names) + (cmds.Count > 6 ? $" +{cmds.Count - 6}" : "");
                parts.Add($"⚔ {list}");
            }
            if (buffs.Count > 0)
            {
                var names = buffs.Take(3).Select(a => a.AbilityName);
                parts.Add($"🛡 {string.Join(", ", names)}" + (buffs.Count > 3 ? $" +{buffs.Count - 3}" : ""));
            }
            if (stats.Count > 0)
                parts.Add($"📊 {stats.Count} stat(s)");

            int wcount = selectedScript.Workers.Count;
            string wlabel = wcount == 1 ? "1 worker" : $"{wcount} workers";
            return $"{wlabel}  ·  {string.Join("  ·  ", parts)}";
        }

        // Re-detect ALL actions (commands + buffs + stats) for the selected monster, then show them through the
        // current type filter. Cheap to re-run on selection / technical-name toggle / rename.
        void RebuildAutomationActions()
        {
            ResetBatchSelectionScopeIfNeeded();
            _allDetectedActions.Clear();
            _allDetectedBranchActions.Clear();
            if (selectedScript != null && selectedScript.HasScript)
            {
                _allDetectedActions.AddRange(AiAutomation.DetectActions(selectedScript));
                if (selectedPath != null && File.Exists(selectedPath))
                {
                    try
                    {
                        byte[] monsterBin = File.ReadAllBytes(selectedPath);
                        _allDetectedBranchActions.AddRange(AiAutomation.DetectBranchSensitiveActions(monsterBin, selectedScript));
                    }
                    catch
                    {
                        // Advisory/read-only layer only. If branch detection fails, keep the legacy action reader alive.
                    }
                }
            }
            RebuildSeymourDispatchReadOnly();
            ReconcileBatchSelectionWithDetectedActions();
            ApplyActionFilter();
            RebuildBehaviorGroups();   // "Como este monstro pensa" view — see .Behavior.cs (needs _allDetectedActions)
        }

        void ApplyActionFilter()
        {
            AutomationActions.Clear();
            SelectedAutomationAction = null;
            AiActionKind? kind = SelectedActionFilter?.Kind;
            Dictionary<int, string> comboHints = BuildComboHints();
            Dictionary<int, string> runContextHints = BuildRunContextHints();
            foreach (AiDetectedAction a in _allDetectedActions)
                if (kind == null || a.Kind == kind)
                {
                    comboHints.TryGetValue(a.CallOffset, out string? comboHint);
                    runContextHints.TryGetValue(a.CallOffset, out string? runContextHint);
                    var vm = new AiActionVm(a, WorkerLabel(a.WorkerIndex), ShowTechnicalNames || IsDevKitMode, OnActionBatchSelectionChanged, comboHint, runContextHint);
                    vm.SetBatchSelectedFromModel(IsActionBatchSelected(a));
                    AutomationActions.Add(vm);
                }
            SelectedAutomationAction = AutomationActions.FirstOrDefault();

            int cmd = _allDetectedActions.Count(a => a.Kind == AiActionKind.Command);
            int buff = _allDetectedActions.Count(a => a.Kind == AiActionKind.Buff);
            int stat = _allDetectedActions.Count(a => a.Kind == AiActionKind.Stat);
            RemoveActionSummary = _allDetectedActions.Count == 0
                ? Strings.U_Ai_NoRecognizableAction
                : string.Format(Strings.U_Ai_ShowingActions, AutomationActions.Count, cmd, buff, stat);
            BehaviorSummary = BuildBehaviorSummary();
            UpdateOverdriveExistingSummary();
        }

        void RebuildSeymourDispatchReadOnly()
        {
            DispatchTableRows.Clear();
            DispatchVarCorpusEntries.Clear();
            SeymourSceneTechNotes.Clear();
            IndirectDispatchUnits.Clear();
            HasSeymourDispatchData = false;
            HasIndirectDispatchUnits = false;
            DispatchTableSummary = "No indirect dispatch table detected for the current monster.";
            DispatchInterpretationSummary =
                Strings.F2_when_the_table_exists_this_card_explains_38c52536;
            SeymourSceneTechSummary =
                Strings.F2_when_the_read_hits_seymour_this_block_ex_43865607;
            IndirectDispatchUnitSummary =
                Strings.F2_when_this_block_appears_it_shows_the_ind_8f245a5d;

            if (selectedScript == null || !selectedScript.HasScript)
            {
                RebuildFineAiPhaseVariableCrossLink();
                return;
            }

            if (!TryBuildSeymourDispatchTable(out List<SeymourDispatchScanRow> rows, out SeymourDispatchVarIndexes vars))
            {
                RebuildFineAiPhaseVariableCrossLink();
                return;
            }

            foreach (SeymourDispatchScanRow row in rows)
            {
                DispatchTableRows.Add(new AiDispatchTableRowVm(
                    string.Format(Strings.U_Ai_PhaseLabel, row.PhaseIndex),
                    FormatCommandOperand(row.NormalCastCommand),
                    FormatCommandOperand(row.AeonCastCommand),
                    FormatCommandOperand(row.MultiCastCommand),
                    FormatCommandOperand(row.PairCastCommand),
                    $"proximo estado -> {row.NextState}",
                    $"0x{row.StartOffset:X4}..0x{row.EndOffset:X4}"));
            }

            int normalCall = FindIndirectPerformCallOffset(vars.SingleTargetSlot, vars.NormalCommandSlot);
            int aeonCall = FindIndirectPerformCallOffset(vars.SingleTargetSlot, vars.AeonCommandSlot);
            int multiOneCall = FindIndirectPerformCallOffset(vars.MultiTargetOneSlot, vars.MultiCommandSlot);
            int multiTwoCall = FindIndirectPerformCallOffset(vars.MultiTargetTwoSlot, vars.PairCommandSlot);
            int multiGate = FindLiteralCompareOffset(vars.MultiGateVar, 255);
            int openerGate = FindLiteralCompareOffset(vars.OpenerGateVar, 0);

            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0020",
                Strings.U_Ai_ElementalRotationIndex,
                "each table row writes the next state in this slot"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0024",
                Strings.U_Ai_SlotCmdUnitCast,
                normalCall >= 0 ? $"executado via PUSHV ... performCommand em 0x{normalCall:X4}" : "executado via PUSHV ... performCommand"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0028",
                Strings.U_Ai_SlotCmdAeonCast,
                aeonCall >= 0 ? $"executado via PUSHV ... performCommand em 0x{aeonCall:X4}" : "executado via PUSHV ... performCommand"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv002C",
                Strings.U_Ai_SlotCmdMulti1,
                multiOneCall >= 0 ? $"1o PUSHV do multi-cast em 0x{multiOneCall:X4}" : "1o PUSHV do multi-cast"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0030",
                Strings.U_Ai_SlotCmdMulti2,
                multiTwoCall >= 0 ? $"2o PUSHV do multi-cast em 0x{multiTwoCall:X4}" : "2o PUSHV do multi-cast"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0014",
                Strings.U_Ai_PrSlotTargetUnit,
                "target calculated before normal performCommand / aeon branch"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv0018",
                Strings.U_Ai_SlotTargetMulti1,
                "target calculated before the 1st PUSHV of multi-cast"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv001C",
                Strings.U_Ai_SlotTargetMulti2,
                "target calculated before the 2nd PUSHV of multi-cast"));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "priv003C",
                Strings.U_Ai_GateOpenerOneShot,
                openerGate >= 0 ? string.Format(Strings.U_Ai_OpeningGuardAt, openerGate) : Strings.U_Ai_OpeningGuardShell));
            DispatchVarCorpusEntries.Add(new AiDispatchVarCorpusEntryVm(
                "battleVar0014",
                "gate that goes up for multi-cast",
                multiGate >= 0 ? string.Format(Strings.U_Ai_ComparedWith255, multiGate) : "compared with 255 before multi-cast"));

            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "performCommand (0x700B)",
                Strings.F2_0x6051_is_not_a_special_engine_opcode_it_dfc74844));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "runBtlSceneA (0x703C)",
                Strings.F2_safe_human_reading_contextual_scripted_c_1016f3d3));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "runBtlSceneB (0x7097)",
                Strings.F2_safe_human_reading_presentation_handoff__6b1a043f));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "Special 1 (0x6001)",
                Strings.F2_replace_mystery_summon_with_phase_handof_226e6f5c));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "Seymour dismisses Anima! (0x6051)",
                Strings.F2_visual_beat_of_anima_s_dismiss_continues_297fa913));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                Strings.U_Ai_SceneGateN26,
                Strings.F2_native_scene_menu_gate_runbtlscenea_b_pu_c33161c7));
            SeymourSceneTechNotes.Add(new AiSeymourTechNoteVm(
                "0x7A4E80",
                Strings.F2_clears_the_actor_s_command_state_do_not__a3fa9334));

            DispatchTableSummary =
                string.Format(Strings.U_Ai_DispatchTableDetected, rows.Count);
            DispatchInterpretationSummary =
                Strings.F2_honest_reading_this_is_rotation_by_state_899be5b8;
            SeymourSceneTechSummary =
                Strings.F2_read_only_technical_panel_explains_seymo_ee3c51f3;
            RebuildIndirectDispatchUnitsReadOnly();
            HasSeymourDispatchData = true;
            RebuildFineAiPhaseVariableCrossLink();
        }

        void RebuildIndirectDispatchUnitsReadOnly()
        {
            IndirectDispatchUnits.Clear();
            HasIndirectDispatchUnits = false;

            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                return;

            try
            {
                byte[] monsterBin = File.ReadAllBytes(selectedPath);
                IReadOnlyList<AiIndirectDispatchUnit> units = DetectPromotedIndirectDispatchUnits(monsterBin);
                foreach (AiIndirectDispatchUnit unit in units)
                    IndirectDispatchUnits.Add(new AiIndirectDispatchUnitVm(unit));

                HasIndirectDispatchUnits = IndirectDispatchUnits.Count > 0;
                if (HasIndirectDispatchUnits)
                {
                    int candidateCount = units.Count(u => u.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
                    IndirectDispatchUnitSummary =
                        string.Format(Strings.U_Ai_AuthoringPreview, units.Count, candidateCount);
                }

                OnPropertyChanged(nameof(CanEditSelectedIndirectDispatchUnit));
            }
            catch
            {
                // Advisory/read-only surface only.
            }
        }

        void RebuildFineAiPhaseVariableCrossLink()
        {
            FineAiPhaseVariableBlocks.Clear();
            HasFineAiPhaseVariableContext = SelectedPhaseVariableAudit != null;

            if (SelectedPhaseVariableAudit == null)
            {
                FineAiPhaseVariableSummary =
                    Strings.F2_open_the_phase_manager_and_select_a_var__50cf2ee4;
                return;
            }

            foreach (AiPhaseVarLinkedBlockVm block in PhaseVariableEvidenceBlocks)
                FineAiPhaseVariableBlocks.Add(block);

            AiDispatchVarCorpusEntryVm? corpus = DispatchVarCorpusEntries.FirstOrDefault(entry =>
                entry.VariableName.Equals(SelectedPhaseVariableAudit.RawVariableName, StringComparison.OrdinalIgnoreCase));

            FineAiPhaseVariableSummary = corpus == null
                ? string.Format(Strings.U_Ai_PhaseVarMemory, SelectedPhaseVariableAudit.VariableName, PhaseVariableEvidenceSummary)
                : string.Format(Strings.U_Ai_PhaseVarMemoryWithRole, SelectedPhaseVariableAudit.VariableName, corpus.RoleSummary, PhaseVariableEvidenceSummary);
        }

        bool TryBuildSeymourDispatchTable(
            out List<SeymourDispatchScanRow> rows,
            out SeymourDispatchVarIndexes vars)
        {
            rows = new List<SeymourDispatchScanRow>();
            vars = default;

            if (selectedScript == null || !selectedScript.HasScript)
                return false;

            if (!TryFindVariableIndexByName("priv0020", out ushort phaseVar)
                || !TryFindVariableIndexByName("priv0024", out ushort normalCmdVar)
                || !TryFindVariableIndexByName("priv0028", out ushort aeonCmdVar)
                || !TryFindVariableIndexByName("priv002C", out ushort multiCmdVar)
                || !TryFindVariableIndexByName("priv0030", out ushort pairCmdVar)
                || !TryFindVariableIndexByName("priv0014", out ushort singleTargetVar)
                || !TryFindVariableIndexByName("priv0018", out ushort multiTargetOneVar)
                || !TryFindVariableIndexByName("priv001C", out ushort multiTargetTwoVar)
                || !TryFindVariableIndexByName("priv003C", out ushort openerGateVar)
                || !TryFindVariableIndexByName("battleVar0014", out ushort multiGateVar))
            {
                return false;
            }

            IReadOnlyList<AiInstruction> instructions = selectedScript.Instructions;
            for (int i = 0; i <= instructions.Count - 10; i++)
            {
                if (!TryReadDispatchTableRow(
                        instructions,
                        i,
                        normalCmdVar,
                        aeonCmdVar,
                        multiCmdVar,
                        pairCmdVar,
                        phaseVar,
                        out SeymourDispatchScanRow row))
                {
                    continue;
                }

                row = row with { PhaseIndex = rows.Count };
                rows.Add(row);
                i += 9;
            }

            if (rows.Count < 4)
                return false;

            vars = new SeymourDispatchVarIndexes(
                phaseVar,
                normalCmdVar,
                aeonCmdVar,
                multiCmdVar,
                pairCmdVar,
                singleTargetVar,
                multiTargetOneVar,
                multiTargetTwoVar,
                multiGateVar,
                openerGateVar);
            return true;
        }

        bool TryReadDispatchTableRow(
            IReadOnlyList<AiInstruction> instructions,
            int startIndex,
            ushort normalCmdVar,
            ushort aeonCmdVar,
            ushort multiCmdVar,
            ushort pairCmdVar,
            ushort phaseVar,
            out SeymourDispatchScanRow row)
        {
            row = default;
            int endIndex = startIndex + 9;
            if (endIndex >= instructions.Count)
                return false;

            if (!IsPushLiteralCommand(instructions[startIndex], out ushort normalCastCommand)
                || !IsPopVar(instructions[startIndex + 1], normalCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 2], out ushort aeonCastCommand)
                || !IsPopVar(instructions[startIndex + 3], aeonCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 4], out ushort multiCastCommand)
                || !IsPopVar(instructions[startIndex + 5], multiCmdVar)
                || !IsPushLiteralCommand(instructions[startIndex + 6], out ushort pairCastCommand)
                || !IsPopVar(instructions[startIndex + 7], pairCmdVar)
                || !IsPushLiteral(instructions[startIndex + 8], out ushort nextState)
                || !IsPopVar(instructions[startIndex + 9], phaseVar))
            {
                return false;
            }

            row = new SeymourDispatchScanRow(
                0,
                instructions[startIndex].Offset,
                instructions[startIndex + 9].Offset,
                normalCastCommand,
                aeonCastCommand,
                multiCastCommand,
                pairCastCommand,
                nextState);
            return true;
        }

        bool TryFindVariableIndexByName(string variableName, out ushort index)
        {
            index = 0;
            if (selectedScript == null)
                return false;

            AiVariable? variable = selectedScript.Variables.FirstOrDefault(v =>
                v.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase));
            if (variable == null)
                return false;

            if (variable.Index < 0 || variable.Index > ushort.MaxValue)
                return false;

            index = (ushort)variable.Index;
            return true;
        }

        static bool IsPushLiteralCommand(AiInstruction instruction, out ushort operand)
        {
            operand = instruction.Operand;
            return instruction.Opcode == 0xAE && AiCommandId.IsCommandOperand(operand);
        }

        static bool IsPushLiteral(AiInstruction instruction, out ushort operand)
        {
            operand = instruction.Operand;
            return instruction.Opcode == 0xAE;
        }

        static bool IsPopVar(AiInstruction instruction, ushort variableIndex) =>
            instruction.Opcode == 0xA0 && instruction.Operand == variableIndex;

        int FindIndirectPerformCallOffset(ushort targetVarIndex, ushort commandVarIndex)
        {
            if (selectedScript == null)
                return -1;

            IReadOnlyList<AiInstruction> instructions = selectedScript.Instructions;
            for (int i = 2; i < instructions.Count; i++)
            {
                AiInstruction call = instructions[i];
                if (call.Opcode != 0xD8 || call.Operand != AiAutomation.PerformCommand)
                    continue;

                if (instructions[i - 2].Opcode == 0x9F
                    && instructions[i - 2].Operand == targetVarIndex
                    && instructions[i - 1].Opcode == 0x9F
                    && instructions[i - 1].Operand == commandVarIndex)
                {
                    return call.Offset;
                }
            }

            return -1;
        }

        int FindLiteralCompareOffset(ushort variableIndex, ushort literal)
        {
            if (selectedScript == null)
                return -1;

            IReadOnlyList<AiInstruction> instructions = selectedScript.Instructions;
            for (int i = 0; i <= instructions.Count - 3; i++)
            {
                if (!AiVarConditionBuilder.TryReadImmediateComparison(instructions, i, out AiVarConditionClause clause)
                    || clause.VariableIndex != variableIndex || clause.Value != unchecked((short)literal))
                    continue;
                return instructions[i].Offset;
            }

            return -1;
        }

        static string FormatCommandOperand(ushort operand)
        {
            AiCommandOption? option = AiCommandId.OptionFor(operand);
            if (option != null)
                return $"{option.Name} [0x{operand:X4}]";

            AiCommandDecode decode = AiCommandId.Decode(operand);
            return $"{decode.Name} [0x{operand:X4}]";
        }

        void UpdateOverdriveExistingSummary()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            {
                OverdriveExistingSummary = Strings.F2_select_a_monster_to_check_if_overdrive_i_11ea8ad7;
                return;
            }

            int fieldAccesses = AiAutomation.CountOverdriveFieldAccesses(selectedScript);
            int generatedBlocks = AiAutomation.CountGeneratedOverdriveLabBlocks(selectedScript, ResolveOverdriveCleanMinStart(selectedScript));
            if (fieldAccesses == 0)
            {
                OverdriveExistingSummary = Strings.F2_overdrive_detected_no_overdrive_read_wri_726f063a;
                return;
            }

            string generated = generatedBlocks > 0
                ? string.Format(Strings.U_Ai_BlocksLookLabRecipe, generatedBlocks)
                : Strings.U_Ai_NoEditorOdBlock;
            OverdriveExistingSummary = string.Format(Strings.U_Ai_OdDetected, fieldAccesses, generated);
        }

        int ResolveOverdriveCleanMinStart(AiScriptFile current)
        {
            // Prefer the extracted reference monster as the "do not touch vanilla" boundary. Any generated LAB block
            // appended after that code length can be replaced; native Overdrive code before it is preserved.
            try
            {
                if (selectedMonster != null)
                {
                    string? refPath = ReferenceMonsterPath(selectedMonster.Id);
                    if (refPath != null)
                    {
                        byte[]? refAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(refPath));
                        if (refAi != null)
                            return AiScript_File.Read(refAi).CodeLength;
                    }
                }
            }
            catch { /* status-only fallback below */ }

            // Backup is a weaker but useful boundary for custom/modded monsters.
            try
            {
                if (selectedPath != null)
                {
                    string bak = selectedPath + ".prev.bak";
                    if (File.Exists(bak))
                    {
                        byte[]? bakAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(bak));
                        if (bakAi != null)
                        {
                            int code = AiScript_File.Read(bakAi).CodeLength;
                            if (code <= current.CodeLength) return code;
                        }
                    }
                }
            }
            catch { /* safe fallback below */ }

            return current.CodeLength;
        }

        Dictionary<int, string> BuildComboHints()
        {
            var hints = new Dictionary<int, string>();
            if (selectedScript == null || !selectedScript.HasScript || _allDetectedActions.Count < 2)
                return hints;

            var actions = _allDetectedActions
                .Where(a => a.Kind == AiActionKind.Command && a.Removable && a.TargetPushOffset >= 0)
                .OrderBy(ActionStartOffset)
                .ToList();

            for (int i = 0; i < actions.Count;)
            {
                var run = new List<AiDetectedAction> { actions[i] };
                int j = i + 1;
                while (j < actions.Count && IsDirectComboStep(run[^1], actions[j]))
                {
                    run.Add(actions[j]);
                    j++;
                }

                if (run.Count > 1)
                {
                    for (int n = 0; n < run.Count; n++)
                    {
                        AiDetectedAction action = run[n];
                        string targetNote = n + 1 < run.Count ? $" ({ComboTargetNote(action, run[n + 1])})" : "";
                        string text = n switch
                        {
                            0 => string.Format(Strings.U_Ai_DirectSeqContinue, n + 1, run.Count, run[n + 1].AbilityName, targetNote),
                            _ when n == run.Count - 1 => string.Format(Strings.U_Ai_DirectSeqCloses, n + 1, run.Count),
                            _ => string.Format(Strings.U_Ai_DirectSeqFromAbove, n + 1, run.Count, run[n + 1].AbilityName, targetNote),
                        };
                        hints[action.CallOffset] = text;
                    }
                }

                i = Math.Max(j, i + 1);
            }

            return hints;
        }

        int ActionStartOffset(AiDetectedAction action) =>
            action.RemoveOffsets.Count > 0 ? action.RemoveOffsets.Min() : action.CallOffset;

        bool IsDirectComboStep(AiDetectedAction first, AiDetectedAction second)
        {
            if (selectedScript == null) return false;
            if (first.WorkerIndex != second.WorkerIndex) return false;
            if (first.PerformOperand != second.PerformOperand) return false;

            AiInstruction? firstCall = selectedScript.Instructions.FirstOrDefault(i => i.Offset == first.CallOffset);
            if (firstCall == null) return false;

            return first.CallOffset + firstCall.Length == ActionStartOffset(second);
        }

        static string ComboTargetNote(AiDetectedAction first, AiDetectedAction second) =>
            first.TargetOpcode == second.TargetOpcode
            && first.TargetOperand == second.TargetOperand
            && first.TargetIsLiteral == second.TargetIsLiteral
                ? Strings.F2_same_target_1baead9d
                : "alvo diferente/recalculado";

        Dictionary<int, string> BuildRunContextHints()
        {
            var hints = new Dictionary<int, string>();
            if (selectedScript == null || !selectedScript.HasScript) return hints;
            Dictionary<int, string> branchHints = BuildBranchSensitiveHints();

            if (!TryResolveOnTurnHook(out AiEventHook hook, out _))
            {
                foreach (AiDetectedAction action in _allDetectedActions)
                    hints[action.CallOffset] = "onTurn real not confirmed: test in game before trusting";
                MergeBranchHints(hints, branchHints);
                return hints;
            }

            Dictionary<int, RunPathContext> onTurnReach = BuildOnTurnReachability(hook);
            foreach (AiDetectedAction action in _allDetectedActions)
            {
                if (action.WorkerIndex != hook.WorkerIndex)
                {
                    string workerType = WorkerTypeOf(action.WorkerIndex);
                    hints[action.CallOffset] = string.Format(Strings.U_Ai_OtherTriggerWorker, workerType);
                    continue;
                }

                if (!onTurnReach.TryGetValue(action.CallOffset, out var context))
                {
                    hints[action.CallOffset] = TryDescribeAlternateEntrypointContext(action, hook, out string alternate)
                        ? alternate
                        : "off the main onTurn path detected";
                    continue;
                }

                if (context.Risk > 0)
                {
                    string guard = string.IsNullOrWhiteSpace(context.GuardLabel)
                        ? "condicao anterior"
                        : context.GuardLabel;
                    hints[action.CallOffset] = context.Random
                        ? string.Format(Strings.U_Ai_ChanceCondition, guard)
                        : string.Format(Strings.U_Ai_ConditionOnly, guard);
                }
            }

            MergeBranchHints(hints, branchHints);
            return hints;
        }

        Dictionary<int, string> BuildBranchSensitiveHints()
        {
            var hints = new Dictionary<int, string>();
            if (_allDetectedBranchActions.Count == 0)
                return hints;

            foreach (IGrouping<int, AiDetectedBranchAction> group in _allDetectedBranchActions
                .GroupBy(a => a.CallOffset)
                .OrderBy(g => g.Key))
            {
                List<AiDetectedBranchAction> ordered = group
                    .OrderBy(a => a.HookKind, StringComparer.Ordinal)
                    .ThenBy(a => a.GuardSummary, StringComparer.Ordinal)
                    .ThenBy(a => a.CommandSummary, StringComparer.Ordinal)
                    .ThenBy(a => a.TargetSummary, StringComparer.Ordinal)
                    .ToList();

                string summary = FormatBranchSensitiveGroup(ordered);
                if (string.IsNullOrWhiteSpace(summary))
                    continue;

                hints[group.Key] = summary;
            }

            return hints;
        }

        static string FormatBranchSensitiveGroup(IReadOnlyList<AiDetectedBranchAction> actions)
        {
            if (actions.Count == 0)
                return string.Empty;

            if (actions.Count <= 2)
            {
                List<string> direct = actions
                    .Select(FormatBranchSensitiveHint)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (direct.Count == 0)
                    return string.Empty;

                return $"{Strings.U_Ai_ReaderBranchSensitive}{Environment.NewLine}{string.Join($"{Environment.NewLine}{Environment.NewLine}", direct)}";
            }

            AiDetectedBranchAction lead = actions[0];
            List<string> labels = actions.SelectMany(a => a.HighLevelHints).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> commands = actions.Select(a => CompactBranchCommandSummary(a.CommandSummary)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> targets = actions.Select(a => CompactBranchTargetSummary(a.TargetSummary)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> guards = actions
                .Select(a => a.GuardSummary)
                .Where(g => !string.IsNullOrWhiteSpace(g) && g != "direta" && g != "condicao do script")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lines = new List<string>
            {
                string.Format(Strings.U_Ai_ReaderBranchLine, lead.HookKind, SummarizeBranchConfidence(actions), actions.Count)
            };

            if (labels.Count > 0)
                lines.Add($"  labels: {JoinCompactList(labels, 4)}");

            lines.Add($"  cmds: {JoinCompactList(commands, 3)}");
            lines.Add($"  alvos: {JoinCompactList(targets, 2)}");

            string? sharedCommandOrigin = SharedBranchProvenance(actions.Select(a => a.CommandProvenance));
            if (!ShouldHideBranchProvenance(sharedCommandOrigin ?? string.Empty))
                lines.Add($"  origem cmd: {sharedCommandOrigin}");

            string? sharedTargetOrigin = SharedBranchProvenance(actions.Select(a => a.TargetProvenance));
            if (!ShouldHideBranchProvenance(sharedTargetOrigin ?? string.Empty))
                lines.Add($"  origem alvo: {sharedTargetOrigin}");

            if (guards.Count > 0)
                lines.Add($"  guards: {JoinCompactList(guards, 3)}");

            return string.Join(Environment.NewLine, lines);
        }

        static string FormatBranchSensitiveHint(AiDetectedBranchAction action)
        {
            var lines = new List<string>
            {
                $"• {action.HookKind} [{action.Confidence}]"
            };

            if (!string.IsNullOrWhiteSpace(action.GuardSummary)
                && action.GuardSummary != "direta"
                && action.GuardSummary != "condicao do script")
                lines.Add($"  guard: {action.GuardSummary}");

            lines.Add($"  cmd: {action.CommandSummary}");
            if (!ShouldHideBranchProvenance(action.CommandProvenance))
                lines.Add($"  origem cmd: {action.CommandProvenance}");

            lines.Add($"  alvo: {action.TargetSummary}");
            if (!ShouldHideBranchProvenance(action.TargetProvenance))
                lines.Add($"  origem alvo: {action.TargetProvenance}");

            if (action.HighLevelHints.Count > 0)
                lines.Add($"  labels: {string.Join(", ", action.HighLevelHints)}");

            return string.Join(Environment.NewLine, lines);
        }

        static void MergeBranchHints(Dictionary<int, string> baseHints, Dictionary<int, string> branchHints)
        {
            foreach ((int callOffset, string branchHint) in branchHints)
            {
                if (baseHints.TryGetValue(callOffset, out string? existing) && !string.IsNullOrWhiteSpace(existing))
                    baseHints[callOffset] = $"{existing}{Environment.NewLine}{branchHint}";
                else
                    baseHints[callOffset] = branchHint;
            }
        }

        static bool ShouldHideBranchProvenance(string provenance) =>
            string.IsNullOrWhiteSpace(provenance)
            || provenance.Equals("literal direto", StringComparison.OrdinalIgnoreCase);

        static string CompactBranchCommandSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return "comando calculado";

            int hex = summary.IndexOf(" [0x", StringComparison.OrdinalIgnoreCase);
            return hex > 0 ? summary[..hex] : summary;
        }

        static string CompactBranchTargetSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return "alvo calculado";

            return summary
                .Replace(Strings.F2_live_target_from_the_front_line_26c9788d, Strings.F2_live_front_line_0a7a0770, StringComparison.OrdinalIgnoreCase)
                .Replace(Strings.U_Ai_TargetLastAttacker, "LastAttacker", StringComparison.OrdinalIgnoreCase);
        }

        static string JoinCompactList(IReadOnlyList<string> items, int visibleCount)
        {
            if (items.Count == 0)
                return Strings.F2_none_71f8e797;

            List<string> visible = items.Take(visibleCount).ToList();
            return items.Count > visible.Count
                ? $"{string.Join(", ", visible)} +{items.Count - visible.Count}"
                : string.Join(", ", visible);
        }

        static string SummarizeBranchConfidence(IReadOnlyList<AiDetectedBranchAction> actions)
        {
            List<string> values = actions.Select(a => a.Confidence).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return values.Count == 1 ? values[0] : string.Join(" / ", values);
        }

        static string? SharedBranchProvenance(IEnumerable<string> values)
        {
            List<string> distinct = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        bool TryDescribeAlternateEntrypointContext(AiDetectedAction action, AiEventHook onTurnHook, out string text)
        {
            text = string.Empty;
            if (selectedScript == null) return false;
            if (action.WorkerIndex < 0 || action.WorkerIndex >= selectedScript.Workers.Count) return false;

            AiWorker worker = selectedScript.Workers[action.WorkerIndex];
            for (int entrypointIndex = 0; entrypointIndex < worker.Entrypoints.Count; entrypointIndex++)
            {
                if (action.WorkerIndex == onTurnHook.WorkerIndex && entrypointIndex == onTurnHook.EntrypointIndex)
                    continue;

                var reach = BuildReachability(new AiEventHook(action.WorkerIndex, entrypointIndex));
                if (!reach.TryGetValue(action.CallOffset, out RunPathContext context)) continue;

                string entry = HumanEntrypointLabel(entrypointIndex);
                string guard = string.IsNullOrWhiteSpace(context.GuardLabel) ? TryDirectGuardLabel(action) : context.GuardLabel;
                List<string> labels = DetectAdvancedActionContextLabels(action, entrypointIndex);
                string labelPrefix = labels.Count == 0 ? string.Empty : $"{string.Join(", ", labels)}: ";
                text = string.IsNullOrWhiteSpace(guard) || guard == "direta"
                    ? string.Format(Strings.U_Ai_TriggerNonNormalTurn, labelPrefix, entry)
                    : string.Format(Strings.U_Ai_TriggerNonNormalTurnGuard, labelPrefix, entry, guard);
                return true;
            }

            return false;
        }

        string TryDirectGuardLabel(AiDetectedAction action) =>
            TryDescribeGuardForAction(action, out AiGuardDescription guard) ? guard.Label : string.Empty;

        static string HumanEntrypointLabel(int entrypointIndex) => entrypointIndex switch
        {
            0 => Strings.U_Ai_EntrypointInit,
            3 => Strings.F2_onhit_upon_receiving_hit_77d684a6,
            _ => $"entrypoint {entrypointIndex}",
        };

        List<string> DetectAdvancedActionContextLabels(AiDetectedAction action, int entrypointIndex)
        {
            var labels = new List<string>();
            string entry = HumanEntrypointLabel(entrypointIndex);
            bool isOnHitEntry = entry.Contains("onHit", StringComparison.OrdinalIgnoreCase);
            bool isInitEntry = entrypointIndex == 0;

            if (!isOnHitEntry && !isInitEntry)
                labels.Add("entrypoint auxiliar contextual");

            if (LooksLikeTalkOrchestration(action))
                labels.Add("trigger-command talk orchestration");

            if (LooksLikeContextualScriptedCut(action))
                labels.Add("corte contextual/scriptado");

            if (LooksLikePresentationHandoff(action))
                labels.Add(Strings.U_Ai_PresentationHandoff);

            if (LooksLikeDamageGatedTransition(action, isOnHitEntry))
                labels.Add("damage-gated transition");

            if (LooksLikePhaseStateMachine(action))
            {
                labels.Add("phase-state machine");
                labels.Add(Strings.U_Ai_PhaseHandoff);
                labels.Add(Strings.U_Ai_PresentationHandoff);
            }

            return labels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool LooksLikeTalkOrchestration(AiDetectedAction action) =>
            ActionWindowContainsCall(action.CallOffset, UsedCommandOperand, 28, 4)
            && ActionWindowContainsLiteral(action.CallOffset, TalkCommandOperand, 28, 4)
            && ActionWindowContainsCall(action.CallOffset, RunBtlSceneAOperand, 28, 4);

        bool LooksLikeDamageGatedTransition(AiDetectedAction action, bool isOnHitEntry) =>
            isOnHitEntry
            && (ActionWindowContainsLiteral(action.CallOffset, LastDamageTakenHpFieldOperand, 56, 4)
                || ActionWindowContainsCall(action.CallOffset, ReadChrPropertyOperand, 56, 4))
            && (action.ForcePerform || action.CommandOperand == Special1CommandOperand);

        bool LooksLikeContextualScriptedCut(AiDetectedAction action) =>
            ActionWindowContainsCall(action.CallOffset, RunBtlSceneAOperand, 24, 4);

        bool LooksLikePresentationHandoff(AiDetectedAction action) =>
            ActionWindowContainsCall(action.CallOffset, RunBtlSceneBOperand, 24, 4);

        bool LooksLikePhaseStateMachine(AiDetectedAction action) =>
            action.ForcePerform
            && action.CommandOperand == Special1CommandOperand
            && (ActionWindowContainsCall(action.CallOffset, RemoveCommandOperand, 24, 2)
                || ActionWindowContainsLiteral(action.CallOffset, TalkCommandOperand, 24, 2));

        bool ActionWindowContainsCall(int callOffset, ushort operand, int lookBackInstructions, int lookAheadInstructions)
        {
            foreach (AiInstruction instruction in EnumerateActionWindow(callOffset, lookBackInstructions, lookAheadInstructions))
            {
                if ((instruction.Opcode == CallOpcode || instruction.Opcode == CallPopaOpcode) && instruction.Operand == operand)
                    return true;
            }

            return false;
        }

        bool ActionWindowContainsLiteral(int callOffset, ushort operand, int lookBackInstructions, int lookAheadInstructions) =>
            EnumerateActionWindow(callOffset, lookBackInstructions, lookAheadInstructions)
                .Any(instruction => instruction.Opcode == ContextPushIiOpcode && instruction.Operand == operand);

        IEnumerable<AiInstruction> EnumerateActionWindow(int callOffset, int lookBackInstructions, int lookAheadInstructions)
        {
            if (selectedScript == null)
                yield break;

            IReadOnlyList<AiInstruction> instructions = selectedScript.Instructions;
            int index = -1;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == callOffset)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                yield break;

            int start = Math.Max(0, index - lookBackInstructions);
            int end = Math.Min(instructions.Count - 1, index + lookAheadInstructions);
            for (int i = start; i <= end; i++)
                yield return instructions[i];
        }

        Dictionary<int, RunPathContext> BuildOnTurnReachability(AiEventHook hook) => BuildReachability(hook);

        Dictionary<int, RunPathContext> BuildReachability(AiEventHook hook)
        {
            var reached = new Dictionary<int, RunPathContext>();
            if (selectedScript == null || hook.WorkerIndex < 0 || hook.WorkerIndex >= selectedScript.Workers.Count)
                return reached;

            AiWorker worker = selectedScript.Workers[hook.WorkerIndex];
            if (hook.EntrypointIndex < 0 || hook.EntrypointIndex >= worker.Entrypoints.Count)
                return reached;

            var nodes = new Dictionary<int, (AiInstruction Instruction, int Next, int Index)>();
            for (int i = 0; i < selectedScript.Instructions.Count; i++)
            {
                AiInstruction instruction = selectedScript.Instructions[i];
                int relative = instruction.Offset - selectedScript.ScriptStart;
                int next = i + 1 < selectedScript.Instructions.Count
                    ? selectedScript.Instructions[i + 1].Offset - selectedScript.ScriptStart
                    : selectedScript.CodeLength;
                nodes[relative] = (instruction, next, i);
            }

            var pending = new Stack<(int Relative, int Risk, bool Random, string GuardLabel)>();
            var visited = new Dictionary<int, RunPathContext>();
            Push(worker.Entrypoints[hook.EntrypointIndex], 0, false, string.Empty);

            while (pending.Count > 0)
            {
                var state = pending.Pop();
                if (!nodes.TryGetValue(state.Relative, out var node)) continue;

                int absolute = selectedScript.ScriptStart + state.Relative;
                if (!reached.TryGetValue(absolute, out var old)
                    || state.Risk < old.Risk
                    || (state.Risk == old.Risk && state.Random && !old.Random)
                    || (state.Risk == old.Risk && !string.IsNullOrWhiteSpace(state.GuardLabel) && string.IsNullOrWhiteSpace(old.GuardLabel)))
                    reached[absolute] = new RunPathContext(state.Risk, state.Random, state.GuardLabel);

                AiInstruction instruction = node.Instruction;
                if (instruction.Opcode == 0xB0)
                {
                    if (TryJumpTarget(instruction, out int target))
                        Push(target, state.Risk, state.Random, state.GuardLabel);
                    continue;
                }

                if (instruction.Opcode == 0xD6 || instruction.Opcode == 0xD7)
                {
                    if (TryLiteralCondition(node.Index, out bool condition))
                    {
                        bool jumpTaken = instruction.Opcode == 0xD6 ? condition : !condition;
                        if (jumpTaken)
                        {
                            if (TryJumpTarget(instruction, out int target))
                                Push(target, state.Risk, state.Random, state.GuardLabel);
                        }
                        else if (node.Next < selectedScript.CodeLength)
                        {
                            Push(node.Next, state.Risk, state.Random, state.GuardLabel);
                        }
                        continue;
                    }

                    int branchRisk = Math.Max(state.Risk, 1);
                    AiGuardDescription guard = DescribeGuardBeforeBranch(node.Index);
                    bool branchRandom = state.Random || guard.Random || LooksRandomGuard(node.Index);
                    string guardedLabel = CombineGuardLabels(state.GuardLabel, guard.Label);
                    if (TryJumpTarget(instruction, out int conditionalTarget))
                    {
                        string jumpLabel = instruction.Opcode == 0xD6 ? guardedLabel : state.GuardLabel;
                        Push(conditionalTarget, branchRisk, branchRandom, jumpLabel);
                    }
                    if (node.Next < selectedScript.CodeLength)
                    {
                        string nextLabel = instruction.Opcode == 0xD7 ? guardedLabel : state.GuardLabel;
                        Push(node.Next, branchRisk, branchRandom, nextLabel);
                    }
                    continue;
                }

                if (instruction.Opcode == 0x2C)
                {
                    int branchRisk = Math.Max(state.Risk, 1);
                    if (node.Next < selectedScript.CodeLength)
                        Push(node.Next, branchRisk, state.Random, state.GuardLabel);
                    continue;
                }

                if (instruction.Opcode == 0x3C || instruction.Opcode == 0x40) continue;
                if (node.Next < selectedScript.CodeLength)
                    Push(node.Next, state.Risk, state.Random, state.GuardLabel);
            }

            return reached;

            void Push(int relative, int risk, bool random, string guardLabel)
            {
                if (relative < 0 || relative >= selectedScript!.CodeLength) return;
                if (visited.TryGetValue(relative, out var old)
                    && (old.Risk < risk || (old.Risk == risk && (old.Random || !random) && (!string.IsNullOrWhiteSpace(old.GuardLabel) || string.IsNullOrWhiteSpace(guardLabel)))))
                    return;
                visited[relative] = new RunPathContext(risk, random, guardLabel);
                pending.Push((relative, risk, random, guardLabel));
            }

            bool TryJumpTarget(AiInstruction instruction, out int target)
            {
                target = 0;
                if (instruction.Operand >= worker.JumpTargets.Count) return false;
                target = worker.JumpTargets[instruction.Operand];
                return true;
            }

            bool TryLiteralCondition(int instructionIndex, out bool condition)
            {
                condition = false;
                if (instructionIndex <= 0) return false;
                AiInstruction previous = selectedScript!.Instructions[instructionIndex - 1];
                if (previous.Opcode != OpPushii) return false;
                if (previous.Operand == 0) { condition = false; return true; }
                if (previous.Operand == 1) { condition = true; return true; }
                return false;
            }

            bool LooksRandomGuard(int instructionIndex)
            {
                for (int i = instructionIndex - 1; i >= Math.Max(0, instructionIndex - 8); i--)
                {
                    AiInstruction previous = selectedScript!.Instructions[i];
                    if ((previous.Opcode == OpCall || previous.Opcode == OpCallpopa) && previous.Operand == GetRandomValueId)
                        return true;
                }
                return false;
            }
        }

        static string CombineGuardLabels(string outer, string inner)
        {
            if (string.IsNullOrWhiteSpace(outer)) return inner;
            if (string.IsNullOrWhiteSpace(inner)) return outer;
            if (outer.Equals(inner, StringComparison.OrdinalIgnoreCase)) return outer;
            return $"{outer} + {inner}";
        }

        // ── ✏ Renomear comando (rótulo amigável persistido — #7) ────────────────────────────────────────────
        void UpdateRenameAvailability()
        {
            AiActionVm? sel = SelectedAutomationAction;
            CanRenameSelected = sel != null && sel.Action.Kind == AiActionKind.Command;
            if (CanRenameSelected)
                RenameCommandLabel = AiCommandLabels.Get(sel!.Action.CommandOperand) ?? string.Empty;
        }

        // ── 🎯 Mudar alvo (#8) — self provado + alvos copiados-por-exemplo deste monstro ─────────────────────
        void RebuildTargetOptions()
        {
            TargetOptions.Clear();
            AiActionVm? sel = SelectedAutomationAction;
            CanChangeTarget = sel != null && sel.Action.Kind == AiActionKind.Command && sel.Action.TargetPushOffset >= 0;
            if (!CanChangeTarget) return;

            var seen = new HashSet<ushort>();
            // Standard NAMED target modes — the btlActor sentinels (Self / enemies / allies / specific char…) via
            // AiTargetNames (#8). Self (-13) is byte-proven; the rest are public-RE + corpus-consistent.
            foreach ((ushort op, string label) in AiTargetNames.Standard)
                if (seen.Add(op)) TargetOptions.Add(new AiTargetOption(op, label));
            // Any other literal target THIS monster already uses (e.g. a specific MonsterType actor ref), now named.
            foreach (AiDetectedAction a in _allDetectedActions.Where(a => a.Kind == AiActionKind.Command && a.TargetIsLiteral))
                if (seen.Add(a.TargetOperand))
                {
                    string named = AiTargetNames.Get(a.TargetOperand) ?? string.Format(Strings.U_Ai_TargetLiteral, (short)a.TargetOperand);
                    TargetOptions.Add(new AiTargetOption(a.TargetOperand, string.Format(Strings.U_Ai_UsedBy, named, a.AbilityName)));
                }

            SelectedTargetOption = TargetOptions.FirstOrDefault(o => o.Operand == sel!.Action.TargetOperand)
                                   ?? TargetOptions.FirstOrDefault();
        }

        const ushort AiAutomationSelfTarget = 0xFFF3;

        public void ChangeSelectedActionTarget()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { TargetSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_7dbced7a; return; }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null || act.Action.Kind != AiActionKind.Command || act.Action.TargetPushOffset < 0)
            { TargetSummary = Strings.U_Ai_SelectCommandWithTarget; return; }
            AiTargetOption? to = SelectedTargetOption;
            if (to == null) { TargetSummary = Strings.F2_choose_a_target_from_the_list_1bd70735; return; }

            int oldIdx = AutomationActions.IndexOf(act);
            List<AiInstruction>? changed = AiAutomation.ChangeTargetInstructions(selectedScript, act.Action, to.Operand);
            if (changed == null) { TargetSummary = "I did not locate the target slot of this action."; return; }
            string abil = act.Action.AbilityName;
            string convertNote = act.Action.TargetIsLiteral ? "" : Strings.U_Ai_TargetWasComputed;
            ApplyAndSave(changed,
                string.Format(Strings.U_Ai_TargetChanged, abil, to.Label, Path.GetFileName(selectedPath), convertNote),
                oldIdx);
            TargetSummary = RemoveActionSummary;
        }

        // ── Caminho legado de segunda ação — inserir a habilidade escolhida logo após a ação, com o mesmo alvo/modo
        public void InsertSecondAbility()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { RemoveActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_0f215032; return; }
            AiActionVm? act = SelectedAutomationAction;
            if (act == null || act.Action.Kind != AiActionKind.Command || !act.Action.Removable)
            { RemoveActionSummary = Strings.F2_select_a_simple_command_action_to_insert_ff969a68; return; }
            AiCommandOption? to = SelectedAutomationAbility;
            if (to == null) { RemoveActionSummary = Strings.F2_choose_the_2nd_ability_in_the_picker_abo_9de9253a; return; }

            bool linkRite = LinkForbiddenRiteToBehavior;
            AiTargetOption? riteTarget = null;
            AiForbiddenStatusPreset? riteStatus = null;
            ushort riteValue = 0;
            ushort riteTargetOperand = 0;
            string riteTargetLabel = string.Empty;
            string riteError = string.Empty;
            if (linkRite && !TryGetForbiddenRitePayload(out riteTarget, out riteStatus, out riteValue, out riteError))
            {
                RemoveActionSummary = riteError;
                return;
            }
            bool linkUsesSameTarget = linkRite && riteTarget?.UseLinkedActionTarget == true;
            if (linkUsesSameTarget)
            {
                riteTargetLabel = LinkedActionTargetLabel(act.Action);
            }
            else if (linkRite && !TryResolveForbiddenRiteTarget(riteTarget!, act.Action, out riteTargetOperand, out riteTargetLabel, out riteError))
            {
                RemoveActionSummary = riteError;
                return;
            }
            int oldIdx = AutomationActions.IndexOf(act);
            List<AiInstruction>? ins = linkRite
                ? (linkUsesSameTarget
                    ? AiAutomation.InsertSecondCommandWithLinkedChrPropertyWrite(selectedScript, act.Action, to.Operand, riteStatus!.FieldId, riteValue)
                    : AiAutomation.InsertSecondCommandWithChrPropertyWrite(selectedScript, act.Action, to.Operand, riteTargetOperand, riteStatus!.FieldId, riteValue))
                : AiAutomation.InsertSecondCommand(selectedScript, act.Action, to.Operand);
            if (ins == null) { RemoveActionSummary = Strings.F2_could_not_insert_after_this_action_c04d4e51; return; }
            ApplyAndSave(ins,
                string.Format(Strings.U_Ai_InsertedAfter, to.Name, act.Action.AbilityName, Path.GetFileName(selectedPath), ContextNoteForSamePlace(act)) +
                (linkRite ? LinkedForbiddenRiteNote(riteTargetLabel, riteStatus!, riteValue) : "") +
                Strings.F2_experimental_confirm_in_game_if_both_res_1cfc8828,
                oldIdx);
        }

        public void RenameSelectedCommand()
        {
            AiActionVm? sel = SelectedAutomationAction;
            if (sel == null || sel.Action.Kind != AiActionKind.Command)
            { RenameSummary = Strings.F2_select_a_command_action_magic_ability_fr_07219660; return; }

            ushort op = sel.Action.CommandOperand;
            AiCommandLabels.Set(op, RenameCommandLabel);
            AiCommandLabels.Save(CommandLabelsPath);

            string shown = string.IsNullOrWhiteSpace(RenameCommandLabel) ? Strings.F2_the_default_name_label_removed_45dcc9ef : $"'{RenameCommandLabel.Trim()}'";
            int callOffset = sel.Action.CallOffset;
            RebuildAutomationActions();
            SelectedAutomationAction = AutomationActions.FirstOrDefault(v => v.Action.CallOffset == callOffset)
                                       ?? AutomationActions.FirstOrDefault();
            RenameSummary = string.Format(Strings.U_Ai_CommandRenamed, op, shown);
        }

        string WorkerLabel(int workerIndex)
        {
            if (selectedScript == null || workerIndex < 0 || workerIndex >= selectedScript.Workers.Count) return "?";
            AiWorker w = selectedScript.Workers[workerIndex];
            return $"worker {w.Index} · {w.InferredType ?? "?"}";
        }

        // After a structural write, reload the monster from disk so every surface re-derives from the saved bytes.
        bool ReloadSelectedFromDisk()
        {
            if (selectedPath == null) return false;
            MonsterAiRow? reloaded = MonsterAiRow.TryLoad(selectedPath);
            if (reloaded?.Script == null) return false;

            int monsterIndex = -1, displayedIndex = -1;
            for (int i = 0; i < Monsters.Count; i++)
                if (string.Equals(Monsters[i].Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                { monsterIndex = i; break; }
            for (int i = 0; i < DisplayedMonsters.Count; i++)
                if (string.Equals(DisplayedMonsters[i].Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                { displayedIndex = i; break; }
            if (monsterIndex >= 0) Monsters[monsterIndex] = reloaded;
            if (displayedIndex >= 0) DisplayedMonsters[displayedIndex] = reloaded;
            SelectedMonster = reloaded;
            return true;
        }
    }

    internal readonly record struct RunPathContext(int Risk, bool Random, string GuardLabel);

    // VM wrapper over a detected action. The friendly Label is what the layperson reads; in "modo técnico" it also
    // carries the raw mnemonics + hex. The underlying AiDetectedAction holds the offsets the 1-click removal drops.
    internal sealed class AiActionVm : ObservableObject
    {
        readonly Action<AiActionVm, bool>? onBatchSelectionChanged;
        bool isBatchSelected;
        bool suppressBatchSelectionCallback;

        static readonly IBrush SafeBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x5E, 0x45));
        static readonly IBrush PendingBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0x49, 0x66));
        static readonly IBrush AdvancedBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x27, 0x30));

        public AiActionVm(AiDetectedAction action, string workerLabel, bool technical, Action<AiActionVm, bool>? onBatchSelectionChanged = null, string? comboHint = null, string? runContextHint = null)
        {
            Action = action;
            WorkerLabel = workerLabel;
            Technical = technical;
            this.onBatchSelectionChanged = onBatchSelectionChanged;
            ComboHint = comboHint ?? string.Empty;
            RunContextHint = runContextHint ?? string.Empty;
        }

        public AiDetectedAction Action { get; }
        public string WorkerLabel { get; }
        public bool Technical { get; }
        public string ComboHint { get; }
        public bool HasComboHint => !string.IsNullOrWhiteSpace(ComboHint);
        public string RunContextHint { get; }
        public bool HasRunContextHint => !string.IsNullOrWhiteSpace(RunContextHint);
        public string RunContextHintBadge => ShortBadgeText(RunContextHint, 96);
        public bool HasRunContextHintTooltip => !string.Equals(RunContextHintBadge, RunContextHint, StringComparison.Ordinal);
        public bool CanBatchSelect => Action.Removable;

        public bool IsBatchSelected
        {
            get => isBatchSelected;
            set
            {
                bool next = value && CanBatchSelect;
                if (!SetProperty(ref isBatchSelected, next)) return;
                if (!suppressBatchSelectionCallback)
                    onBatchSelectionChanged?.Invoke(this, next);
            }
        }

        public void SetBatchSelectedFromModel(bool value)
        {
            suppressBatchSelectionCallback = true;
            IsBatchSelected = value && CanBatchSelect;
            suppressBatchSelectionCallback = false;
        }

        string PerformName => Action.ForcePerform ? "forcePerformCommand" : "performCommand";
        string Icon => Action.Kind switch { AiActionKind.Buff => "🛡", AiActionKind.Stat => "📊", _ => "⚔" };
        string TypeTag => Action.Kind switch { AiActionKind.Buff => "buff/status", AiActionKind.Stat => "stat", _ => Strings.U_Ai_TypeCommand };
        string HumanType => Action.Kind switch { AiActionKind.Buff => "status/buff", AiActionKind.Stat => "stat", _ => Strings.U_Ai_HdKindCommand };
        string CommandCandidateTech => Action.CommandCandidates == null || Action.CommandCandidates.Count == 0
            ? string.Empty
            : $"  -> {string.Join(", ", Action.CommandCandidates.Take(3).Select(c => $"0x{c:X4}"))}{(Action.CommandCandidates.Count > 3 ? $" +{Action.CommandCandidates.Count - 3}" : "")}";
        string TechSuffix => Action.Kind switch
        {
            AiActionKind.Command => Action.CommandIsLiteral
                ? $"PUSHII 0x{Action.CommandOperand:X4} · CALLPOPA 0x{Action.PerformOperand:X4} ({PerformName})  @0x{Action.CallOffset:X4}"
                : $"PUSHV var(cmd) · CALLPOPA 0x{Action.PerformOperand:X4} ({PerformName})  @0x{Action.CallOffset:X4}{CommandCandidateTech}",
            AiActionKind.Buff => $"writeChrProperty {FieldTechName(Action.FieldId)} = {Action.FieldValue}  @0x{Action.CallOffset:X4}",
            AiActionKind.Stat => $"setStatField {FieldTechName(Action.FieldId)} = {Action.FieldValue}  @0x{Action.CallOffset:X4}",
            _ => "",
        };

        static string FieldTechName(ushort fieldId)
            => AiChrPropertyNames.Get(fieldId) is string name ? $"{name} (0x{fieldId:X4})" : string.Format(Strings.U_Ai_FieldLabel, fieldId);

        string TargetTag => Action.Kind == AiActionKind.Command && Action.TargetIsLiteral
            ? string.Format(Strings.U_Ai_TargetTag, AiTargetNames.Get(Action.TargetOperand) ?? ((short)Action.TargetOperand).ToString())
            : "";

        string HumanTarget => Action.Kind == AiActionKind.Command && Action.TargetIsLiteral
            ? AiTargetNames.Get(Action.TargetOperand) ?? string.Format(Strings.U_Ai_TargetLiteral, (short)Action.TargetOperand)
            : Action.Kind == AiActionKind.Command && Action.TargetOpcode == 0x9F
                ? Strings.U_Ai_TargetViaVar
                : Strings.F2_target_calculated_by_script_32f61fc9;

        public string HumanTitle => Action.Kind switch
        {
            AiActionKind.Command => string.Format(Strings.AiActionUses, Action.AbilityName),
            AiActionKind.Buff when Action.FieldValue == 0 => string.Format(Strings.AiActionRemoves, Action.AbilityName),
            AiActionKind.Buff => string.Format(Strings.AiActionApplies, Action.AbilityName),
            AiActionKind.Stat => string.Format(Strings.AiActionAdjusts, Action.AbilityName),
            _ => Action.AbilityName,
        };

        public string HumanSubtitle => Action.Kind switch
        {
            AiActionKind.Command => $"{(Action.CommandIsLiteral ? HumanType : Strings.U_Ai_IndirectSkillViaVar)} · {HumanTarget} · {(Action.ForcePerform ? Strings.U_Ai_ExecutesNow : Strings.U_Ai_EntersNormalQueue)}",
            AiActionKind.Buff => string.Format(Strings.U_Ai_ActorStatusField, Action.FieldValue),
            AiActionKind.Stat => string.Format(Strings.U_Ai_StatFieldValue, Action.FieldValue),
            _ => TypeTag,
        };

        public string WhereHuman => string.Format(Strings.AiActionLocation, WorkerLabel, Action.CallOffset);
        public string SafetyLabel => Action.Removable
            ? Strings.U_Ai_SafeSingleAction
            : Action.Kind == AiActionKind.Command && !Action.CommandIsLiteral
                ? Strings.U_Ai_IndirectRead
                : Strings.U_Ai_AdvancedComplexBlock;
        public string SafetyClass => Action.Removable ? "safe" : "advanced";
        public IBrush SafetyBrush => Action.Removable
            ? SafeBrush
            : Action.Kind == AiActionKind.Command ? PendingBrush : AdvancedBrush;
        public IBrush RunContextBrush { get; } = new SolidColorBrush(Color.FromRgb(0xB8, 0x7A, 0x2B));

        public string Label => Technical
            ? $"{Icon} {Action.AbilityName}   ·   {TechSuffix}"
            : $"{Icon} {HumanTitle}";
        public string Sublabel =>
            Technical
                ? $"{TypeTag} · {WorkerLabel}{TargetTag}{(Action.Removable ? "" : Strings.U_Ai_OneClickRemovalUnavailable)}"
                : $"{HumanSubtitle} · {WhereHuman} · {SafetyLabel}";

        static string ShortBadgeText(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxChars)
                return text;

            int keep = Math.Max(0, maxChars - 5);
            return text[..keep].TrimEnd() + "(...)";
        }
    }

    internal sealed record SeymourDispatchVarIndexes(
        ushort PhaseVar,
        ushort NormalCommandSlot,
        ushort AeonCommandSlot,
        ushort MultiCommandSlot,
        ushort PairCommandSlot,
        ushort SingleTargetSlot,
        ushort MultiTargetOneSlot,
        ushort MultiTargetTwoSlot,
        ushort MultiGateVar,
        ushort OpenerGateVar);

    internal sealed record SeymourDispatchScanRow(
        int PhaseIndex,
        int StartOffset,
        int EndOffset,
        ushort NormalCastCommand,
        ushort AeonCastCommand,
        ushort MultiCastCommand,
        ushort PairCastCommand,
        ushort NextState);

    internal sealed class AiDispatchTableRowVm
    {
        public AiDispatchTableRowVm(
            string phaseLabel,
            string singleCastLabel,
            string aeonCastLabel,
            string multiCastLabel,
            string pairCastLabel,
            string nextStateLabel,
            string offsetSummary)
        {
            PhaseLabel = phaseLabel;
            SingleCastLabel = singleCastLabel;
            AeonCastLabel = aeonCastLabel;
            MultiCastLabel = multiCastLabel;
            PairCastLabel = pairCastLabel;
            NextStateLabel = nextStateLabel;
            OffsetSummary = offsetSummary;
        }

        public string PhaseLabel { get; }
        public string SingleCastLabel { get; }
        public string AeonCastLabel { get; }
        public string MultiCastLabel { get; }
        public string PairCastLabel { get; }
        public string NextStateLabel { get; }
        public string OffsetSummary { get; }
    }

    internal sealed class AiDispatchVarCorpusEntryVm
    {
        public AiDispatchVarCorpusEntryVm(string variableName, string roleSummary, string evidenceSummary)
        {
            VariableName = variableName;
            RoleSummary = roleSummary;
            EvidenceSummary = evidenceSummary;
        }

        public string VariableName { get; }
        public string RoleSummary { get; }
        public string EvidenceSummary { get; }
    }

    internal sealed class AiIndirectDispatchUnitVm
    {
        public AiIndirectDispatchUnitVm(AiIndirectDispatchUnit unit)
        {
            SourceUnit = unit;
            UnitId = unit.UnitId;
            Title = $"rota {unit.UnitIndex}";
            HookLabel = unit.HookKind;
            GuardSummary = unit.GuardSummary;
            NextStateSummary = unit.NextStateSummary;
            TierLabel = unit.CapabilityLabel;
            OffsetSummary = unit.OffsetSummary;
            WarningSummary = unit.WarningSummary;
            PayloadSummary = string.Join(Environment.NewLine, unit.PayloadWrites.Select(write =>
                $"{write.VariableName}: {write.ValueSummary} ({write.RoleSummary})"));
            ConsumerSummary = string.Join(Environment.NewLine, unit.Consumers.Select(consumer =>
                $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName} -> 0x{consumer.CallOffset:X4}"));
            CompanionSummary = string.Join(Environment.NewLine, unit.CompanionEffects);
        }

        public AiIndirectDispatchUnit SourceUnit { get; }
        public string UnitId { get; }
        public string Title { get; }
        public string HookLabel { get; }
        public string GuardSummary { get; }
        public string NextStateSummary { get; }
        public string TierLabel { get; }
        public string PayloadSummary { get; }
        public string ConsumerSummary { get; }
        public string CompanionSummary { get; }
        public string OffsetSummary { get; }
        public string WarningSummary { get; }
        public bool CanEdit => SourceUnit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                               && (SourceUnit.EditableSlots.Count > 0
                                   || SourceUnit.EditableTargetSlots.Any(slot => slot.CanEdit)
                                   || SourceUnit.EditableNextStateOffset.HasValue);
    }

    internal sealed class AiSeymourTechNoteVm
    {
        public AiSeymourTechNoteVm(string title, string detail)
        {
            Title = title;
            Detail = detail;
        }

        public string Title { get; }
        public string Detail { get; }
    }

    // A type filter for the action list (#6). ToString() is the label so a plain ComboBox shows it. Kind == null
    // means "show all kinds".
    internal sealed record AiActionTypeFilter(string Label, AiActionKind? Kind)
    {
        public override string ToString() => Label;
    }

    // A target option for the "🎯 Mudar alvo" picker (#8). Operand is the literal target sentinel; ToString() = the
    // friendly label ("Si mesmo (self)" or "como '<ability>' (alvo N)").
    // A single step in the Overdrive finisher sequence: one ability + its own target.
    internal sealed class OverdriveFinisherStep
    {
        public AiCommandOption Ability { get; }
        public IReadOnlyList<AiTargetOption> TargetOptions { get; }
        public AiTargetOption? SelectedTarget { get; set; }
        public string Display => Ability.Display;

        public OverdriveFinisherStep(AiCommandOption ability, IReadOnlyList<AiTargetOption> targetOptions, AiTargetOption? target)
        {
            Ability = ability;
            TargetOptions = targetOptions;
            SelectedTarget = target;
        }
    }

    // A friendly-labelled target option for the authoring ComboBox. ToString() = Label so a plain ComboBox
    // (no ItemTemplate) shows the label.
    internal sealed record AiTargetOption(
        ushort Operand,
        string Label,
        bool UseLinkedActionTarget = false,
        AiTargetRecipeKind TargetRecipeKind = AiTargetRecipeKind.Literal)
    {
        public bool IsComputedRecipe => TargetRecipeKind != AiTargetRecipeKind.Literal;
        public AiTargetRecipe ToRecipe() => new(TargetRecipeKind, Operand);
        public override string ToString() => Label;
    }

    // A friendly-labelled command category for the ability picker. ToString() is the label so a plain ComboBox
    // (no ItemTemplate) shows "Habilidade de monstro 2 (…)" instead of the bare enum name.
    internal sealed record AiAbilityCategoryChoice(AiCommandCategory? Category, string Label)
    {
        public override string ToString() => Label;
    }

    internal sealed record AiForbiddenStatusPreset(string Name, ushort FieldId, string Note, ushort DefaultValue = 1)
    {
        public string Hex => FieldId.ToString("X4");
        public string Display => $"{Name} · 0x{Hex} · v={DefaultValue} · {Note}";
        public override string ToString() => Display;
    }

    internal enum YunalescaConditionKind
    {
        OnTurn,
        BattleStart,
        Always,
        HpBelow,
        OnHit,
        AnyHit,
    }

    internal enum YunalescaHookKind
    {
        OnTurn,
        BattleStart,
        OnHit,
    }

    internal sealed record YunalescaConditionPart(YunalescaConditionKind Kind, string Label, ushort? Percent = null);

    internal sealed record AiFieldPlantOption(ushort FieldId, string Name)
    {
        public string Hex => FieldId.ToString("X4");
        public string Display => $"{Name} · 0x{Hex}";
        public bool Matches(string query) =>
            Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Hex.Contains(query.Replace("0x", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        public override string ToString() => Display;
    }

    internal enum OverdriveStartModeKind { Empty, Full, Custom }

    internal sealed record OverdriveStartModeChoice(OverdriveStartModeKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    internal enum OverdriveFinishModeKind { Single, Sequence }

    internal sealed record OverdriveFinishModeChoice(OverdriveFinishModeKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
