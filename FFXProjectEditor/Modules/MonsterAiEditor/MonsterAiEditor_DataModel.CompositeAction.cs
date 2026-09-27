using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiCompositeStepVm> CompositeSteps { get; } = new();

        [ObservableProperty] private AiCompositeStepVm? selectedCompositeStep;
        partial void OnSelectedCompositeStepChanged(AiCompositeStepVm? value)
        {
            if (value is { IsAnchor: false, SelectedAbility: not null })
                SelectedAutomationAbility = value.SelectedAbility;
            OnPropertyChanged(nameof(IsCompositeAnchorStepSelected));
            OnPropertyChanged(nameof(IsCompositeAbilityStepSelected));
            OnPropertyChanged(nameof(CanRemoveCompositeStep));
            UpdateCompositeActionPreview();
        }

        public bool IsCompositeAnchorStepSelected => SelectedCompositeStep?.IsAnchor == true;
        public bool IsCompositeAbilityStepSelected => SelectedCompositeStep?.IsAnchor == false;
        public bool CanRemoveCompositeStep => SelectedCompositeStep?.IsAnchor == false && CompositeSteps.Count > 2;

        public IReadOnlyList<AiCompositeTargetMode> CompositeTargetModes { get; } = new[]
        {
            new AiCompositeTargetMode(
                "same-target",
                Strings.F2_same_target_mode_as_action_1_bfcbc421,
                Strings.F2_current_writer_copies_the_target_and_per_ac4baf6f,
                Supported: true),
            new AiCompositeTargetMode(
                "two-alive",
                Strings.F2_alternate_between_2_living_targets_seymo_b441f0f5,
                Strings.F2_seymour_model_picks_2_living_targets_and_d0c63736,
                Supported: false),
            new AiCompositeTargetMode(
                "same-live-twice",
                Strings.F2_one_living_target_for_the_entire_sequenc_41456e74,
                Strings.F2_future_recipe_calculates_1_living_target_6f695972,
                Supported: false),
            new AiCompositeTargetMode(
                "provoke-aware",
                Strings.F2_if_provoked_target_the_provoker_83852a49,
                Strings.U_Ai_CaProvokeRecipe,
                Supported: false),
        };

        [ObservableProperty] private AiCompositeTargetMode? selectedCompositeTargetMode;
        partial void OnSelectedCompositeTargetModeChanged(AiCompositeTargetMode? value) => UpdateCompositeActionPreview();

        public IReadOnlyList<AiCompositeForbiddenPlacement> CompositeForbiddenPlacements { get; } = new[]
        {
            new AiCompositeForbiddenPlacement(
                "none",
                Strings.U_Ai_CaNoRite,
                Strings.F2_action_sequence_only_e26ed185,
                Supported: true,
                Interleaved: false),
            new AiCompositeForbiddenPlacement(
                "after-last",
                Strings.F2_after_the_last_step_0e8f2506,
                Strings.F2_uses_the_current_forbidden_rite_payload__30680894,
                Supported: true,
                Interleaved: false),
            new AiCompositeForbiddenPlacement(
                "between-each",
                Strings.F2_between_each_step_after_each_action_060061b6,
                Strings.U_Ai_CaTestedSequence,
                Supported: true,
                Interleaved: true),
            new AiCompositeForbiddenPlacement(
                "after-selected",
                Strings.U_Ai_CaAfterSelectedStep,
                Strings.U_Ai_CaPerStepPayload,
                Supported: false,
                Interleaved: false),
        };

        [ObservableProperty] private AiCompositeForbiddenPlacement? selectedCompositeForbiddenPlacement;
        partial void OnSelectedCompositeForbiddenPlacementChanged(AiCompositeForbiddenPlacement? value) => UpdateCompositeActionPreview();

        [ObservableProperty] private bool compositeRepeatSingleTarget = true;
        partial void OnCompositeRepeatSingleTargetChanged(bool value) => UpdateCompositeActionPreview();

        [ObservableProperty] private bool compositeRespectProvoke = true;
        partial void OnCompositeRespectProvokeChanged(bool value) => UpdateCompositeActionPreview();

        [ObservableProperty] private string compositeActionPreview =
            Strings.F2_select_a_real_action_as_step_1_and_add_a_c92ad196;

        [ObservableProperty] private string compositeActionSummary = Strings.U_Ai_CaIntro;

        partial void OnSelectedAutomationAbilityChanged(AiCommandOption? value)
        {
            if (SelectedCompositeStep is { IsAnchor: false } step && value != null && step.SelectedAbility == null)
                step.SelectedAbility = value;
            ApplySharedAbilityPickerToSelectedPhase(value);
            UpdateCompositeActionPreview();
        }

        void EnsureCompositeDefaults()
        {
            SelectedCompositeTargetMode ??= CompositeTargetModes.FirstOrDefault();
            SelectedCompositeForbiddenPlacement ??= CompositeForbiddenPlacements.FirstOrDefault();
            EnsureCompositeSteps();
        }

        void EnsureCompositeSteps()
        {
            if (CompositeSteps.Count == 0)
            {
                CompositeSteps.Add(new AiCompositeStepVm(1, isAnchor: true, OnCompositeStepChanged));
                CompositeSteps.Add(new AiCompositeStepVm(2, isAnchor: false, OnCompositeStepChanged)
                {
                    SelectedAbility = SelectedAutomationAbility ?? AutomationAbilityOptions.FirstOrDefault(),
                });
                SelectedCompositeStep = CompositeSteps[1];
            }
            SyncCompositeAnchorStep();
            ReindexCompositeSteps();
        }

        void SyncCompositeAnchorStep()
        {
            AiCompositeStepVm? anchor = CompositeSteps.FirstOrDefault(s => s.IsAnchor);
            if (anchor == null) return;
            anchor.AnchorTitle = SelectedAutomationAction?.HumanTitle ?? Strings.U_Ai_CaPickRealAction;
            anchor.AnchorSubtitle = SelectedAutomationAction?.WhereHuman ?? Strings.F2_click_on_step_1_to_choose_the_action_6e0e1b13;
        }

        void ReindexCompositeSteps()
        {
            for (int i = 0; i < CompositeSteps.Count; i++)
                CompositeSteps[i].Number = i + 1;
            OnPropertyChanged(nameof(CanRemoveCompositeStep));
        }

        void OnCompositeStepChanged()
        {
            OnPropertyChanged(nameof(CanRemoveCompositeStep));
            UpdateCompositeActionPreview();
        }

        public void PrepareCompositeActionRecipe()
        {
            EnsureCompositeDefaults();
            SelectedCompositeTargetMode = CompositeTargetModes.FirstOrDefault(m => m.Key == "same-target")
                                          ?? CompositeTargetModes.FirstOrDefault();
            SelectedCompositeForbiddenPlacement ??= CompositeForbiddenPlacements.FirstOrDefault(p => p.Key == "none")
                                                    ?? CompositeForbiddenPlacements.FirstOrDefault();
            AutomationStopAfterAction = true;
            AuthoringRecipeSummary =
                Strings.U_Ai_CaOwnWindow +
                Strings.U_Ai_CaUsePlusStep;
            AddAbilitySummary =
                "Composite action prepared in its own window. The current safe writer copies target/mode from action 1; two live targets/Provoke/rotation remain blocked until the dedicated Seymour writer.";
            UpdateCompositeActionPreview();
        }

        public void AddCompositeStep()
        {
            EnsureCompositeDefaults();
            AiCommandOption? ability = SelectedCompositeStep is { IsAnchor: false, SelectedAbility: not null } step
                ? step.SelectedAbility
                : SelectedAutomationAbility ?? AutomationAbilityOptions.FirstOrDefault();
            var next = new AiCompositeStepVm(CompositeSteps.Count + 1, isAnchor: false, OnCompositeStepChanged)
            {
                SelectedAbility = ability,
            };
            CompositeSteps.Add(next);
            SelectedCompositeStep = next;
            CompositeActionSummary = string.Format(Strings.U_Ai_CaStepAdded, next.Number);
            UpdateCompositeActionPreview();
        }

        public void RemoveSelectedCompositeStep()
        {
            EnsureCompositeDefaults();
            AiCompositeStepVm? step = SelectedCompositeStep;
            if (step == null || step.IsAnchor || CompositeSteps.Count <= 2)
            {
                CompositeActionSummary = Strings.F2_step_1_and_the_first_created_step_cannot_f65ca2d2;
                return;
            }

            int idx = CompositeSteps.IndexOf(step);
            CompositeSteps.Remove(step);
            ReindexCompositeSteps();
            SelectedCompositeStep = CompositeSteps[Math.Clamp(idx, 1, CompositeSteps.Count - 1)];
            CompositeActionSummary = Strings.F2_step_removed_from_the_recipe_nothing_has_b408fcca;
            UpdateCompositeActionPreview();
        }

        void UpdateCompositeActionPreview()
        {
            EnsureCompositeDefaults();

            SyncCompositeAnchorStep();
            AiActionVm? selected = SelectedAutomationAction;
            AiCompositeTargetMode? targetMode = SelectedCompositeTargetMode;
            AiCompositeForbiddenPlacement? ritePlacement = SelectedCompositeForbiddenPlacement;
            List<AiCompositeStepVm> actionSteps = CompositeSteps.Where(s => !s.IsAnchor).ToList();

            string steps = string.Join(" -> ", CompositeSteps.Select(s => s.Title));
            string target = targetMode?.Label ?? Strings.U_Ai_CaTargetModeNotChosen;
            string rite = ritePlacement?.Label ?? Strings.U_Ai_CaRiteNotChosen;
            string terminal = Strings.F2_last_step_automatically_stop_here_ret_to_45e1cbd9;
            string targetPattern = targetMode?.Key switch
            {
                "two-alive" => Strings.U_Ai_CaDistributionAB,
                "same-live-twice" => Strings.U_Ai_CaDistributionOneTarget,
                "provoke-aware" => Strings.U_Ai_CaDistributionProvoker,
                _ => string.Empty,
            };

            List<string> notes = new();
            if (selected == null)
                notes.Add(Strings.F2_you_need_to_choose_the_real_action_for_s_e8ac699f);
            else if (selected.Action.Kind != AiActionKind.Command || !selected.Action.Removable)
                notes.Add(Strings.F2_step_1_must_be_a_simple_and_safe_command_7b693770);
            if (actionSteps.Any(s => s.SelectedAbility == null))
                notes.Add(Strings.U_Ai_CaStepWithoutSkill);
            if (targetMode?.Supported == false)
                notes.Add(Strings.U_Ai_CaPlannedRecipe);
            if (ritePlacement?.Supported == false)
                notes.Add("This Forbidden Rite fit requires payload per step before saving.");

            string optionalFlags = targetMode?.Key == "two-alive" || targetMode?.Key == "provoke-aware"
                ? string.Format(Strings.U_Ai_CaFutureOptions, (CompositeRepeatSingleTarget ? "yes" : "no"), (CompositeRespectProvoke ? "yes" : "no"))
                : string.Empty;

            CompositeActionPreview =
                string.Format(Strings.U_Ai_CaCurrentRecipe, steps, target, rite) +
                $"{terminal} Passos novos: {actionSteps.Count}.{targetPattern}{optionalFlags}" +
                (notes.Count == 0 ? Strings.U_Ai_CaReadyToApply : " " + string.Join(" ", notes));
        }

        public void ApplyCompositeActionRecipe()
        {
            EnsureCompositeDefaults();

            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                CompositeActionSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_e0efd776;
                return;
            }

            AiActionVm? act = SelectedAutomationAction;
            if (act == null || act.Action.Kind != AiActionKind.Command || !act.Action.Removable)
            {
                CompositeActionSummary = Strings.F2_choose_a_simple_command_as_step_1_before_040e71b1;
                UpdateCompositeActionPreview();
                return;
            }

            List<AiCommandOption> abilities = CompositeSteps
                .Where(s => !s.IsAnchor)
                .Select(s => s.SelectedAbility)
                .Where(a => a != null)
                .Cast<AiCommandOption>()
                .ToList();
            if (abilities.Count != CompositeSteps.Count(s => !s.IsAnchor))
            {
                CompositeActionSummary = Strings.F2_there_is_a_step_without_an_ability_chose_ac4d47a6;
                UpdateCompositeActionPreview();
                return;
            }
            if (abilities.Count == 0)
            {
                CompositeActionSummary = Strings.F2_add_at_least_one_new_step_before_applyin_dec43100;
                UpdateCompositeActionPreview();
                return;
            }

            AiCompositeTargetMode targetMode = SelectedCompositeTargetMode ?? CompositeTargetModes[0];
            if (!targetMode.Supported)
            {
                CompositeActionSummary =
                    string.Format(Strings.U_Ai_CaAdvancedModel, targetMode.Label) +
                    Strings.U_Ai_CaUseSameTarget;
                UpdateCompositeActionPreview();
                return;
            }

            AiCompositeForbiddenPlacement ritePlacement = SelectedCompositeForbiddenPlacement ?? CompositeForbiddenPlacements[0];
            if (!ritePlacement.Supported)
            {
                CompositeActionSummary =
                    string.Format(Strings.U_Ai_CaNeedsPerStepPayload, ritePlacement.Label) +
                    Strings.U_Ai_CaUseAfterLastStep;
                UpdateCompositeActionPreview();
                return;
            }

            bool linkRite = ritePlacement.Key != "none";
            AiTargetOption? riteTarget = null;
            AiForbiddenStatusPreset? riteStatus = null;
            ushort riteValue = 0;
            ushort riteTargetOperand = 0;
            string riteTargetLabel = string.Empty;
            string riteError = string.Empty;
            if (linkRite && !TryGetForbiddenRitePayload(out riteTarget, out riteStatus, out riteValue, out riteError))
            {
                CompositeActionSummary = riteError;
                UpdateCompositeActionPreview();
                return;
            }

            bool linkUsesSameTarget = linkRite && riteTarget?.UseLinkedActionTarget == true;
            if (linkUsesSameTarget)
            {
                riteTargetLabel = LinkedActionTargetLabel(act.Action);
            }
            else if (linkRite && !TryResolveForbiddenRiteTarget(riteTarget!, act.Action, out riteTargetOperand, out riteTargetLabel, out riteError))
            {
                CompositeActionSummary = riteError;
                UpdateCompositeActionPreview();
                return;
            }

            ushort[] operands = abilities.Select(a => a.Operand).ToArray();
            bool interleaved = ritePlacement.Interleaved;
            const bool stopAfterSequence = true;
            int oldIdx = AutomationActions.IndexOf(act);
            List<AiInstruction>? ins = linkRite
                ? (linkUsesSameTarget
                    ? AiAutomation.InsertCommandSequenceWithLinkedChrPropertyWrite(selectedScript, act.Action, operands, riteStatus!.FieldId, riteValue, interleaved, stopAfterSequence)
                    : AiAutomation.InsertCommandSequenceWithChrPropertyWrite(selectedScript, act.Action, operands, riteTargetOperand, riteStatus!.FieldId, riteValue, interleaved, stopAfterSequence))
                : AiAutomation.InsertCommandSequence(selectedScript, act.Action, operands, stopAfterSequence);

            if (ins == null)
            {
                CompositeActionSummary = Strings.F2_could_not_generate_this_sequence_startin_80fd5c81;
                UpdateCompositeActionPreview();
                return;
            }

            string chain = string.Join(" -> ", new[] { act.Action.AbilityName }.Concat(abilities.Select(a => a.Name)));
            ApplyAndSave(ins,
                string.Format(Strings.U_Ai_CaApplied, chain, Path.GetFileName(selectedPath)) +
                Strings.U_Ai_CaLastStepRet +
                (linkRite ? LinkedForbiddenRiteNote(riteTargetLabel, riteStatus!, riteValue) : "") +
                (interleaved ? " Forbidden Rite intercalado entre/depois dos passos usando o mesmo payload. " : "") +
                Strings.U_Ai_CaLeftForSeymour,
                oldIdx);

            CompositeActionSummary = RemoveActionSummary;
            UpdateCompositeActionPreview();
        }
    }

    internal sealed class AiCompositeStepVm : ObservableObject
    {
        readonly Action changed;
        int number;
        AiCommandOption? selectedAbility;
        string anchorTitle = Strings.U_Ai_CaPickRealAction;
        string anchorSubtitle = Strings.F2_click_on_step_1_to_choose_the_action_945f2b2d;

        public AiCompositeStepVm(int number, bool isAnchor, Action changed)
        {
            this.number = number;
            IsAnchor = isAnchor;
            this.changed = changed;
        }

        public int Number
        {
            get => number;
            set
            {
                if (!SetProperty(ref number, value)) return;
                OnPropertyChanged(nameof(StepLabel));
            }
        }

        public bool IsAnchor { get; }
        public string StepLabel => string.Format(Strings.U_Ai_CaStepLabel, Number);

        public AiCommandOption? SelectedAbility
        {
            get => selectedAbility;
            set
            {
                if (!SetProperty(ref selectedAbility, value)) return;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Subtitle));
                changed();
            }
        }

        public string AnchorTitle
        {
            get => anchorTitle;
            set
            {
                if (!SetProperty(ref anchorTitle, value)) return;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string AnchorSubtitle
        {
            get => anchorSubtitle;
            set
            {
                if (!SetProperty(ref anchorSubtitle, value)) return;
                OnPropertyChanged(nameof(Subtitle));
            }
        }

        public string Title => IsAnchor ? AnchorTitle : SelectedAbility?.Name ?? Strings.U_Ai_CaPickSkill;
        public string Subtitle => IsAnchor ? AnchorSubtitle : Strings.F2_new_action_created_after_the_previous_st_44002171;
    }

    internal sealed record AiCompositeTargetMode(string Key, string Label, string Note, bool Supported)
    {
        public override string ToString() => Label;
    }

    internal sealed record AiCompositeForbiddenPlacement(string Key, string Label, string Note, bool Supported, bool Interleaved)
    {
        public override string ToString() => Label;
    }
}
