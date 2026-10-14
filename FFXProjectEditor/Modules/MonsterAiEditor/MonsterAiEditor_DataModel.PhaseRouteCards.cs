using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        readonly Dictionary<string, AiIndirectDispatchUnit> draftIndirectUnitsById = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<AiPhaseRouteCardVm> PhaseDraftRouteCards { get; } = new();
        public ObservableCollection<AiPhaseRouteCardVm> PhaseConditionPreviewRouteCards { get; } = new();
        public ObservableCollection<AiPhaseRouteCardVm> PhaseVariableEvidenceRouteCards { get; } = new();

        void RebuildVarEvidenceRouteCards(AiPhaseVariableAuditRow row, List<AiPhaseVarLinkedBlockVm> blocks)
        {
            PhaseVariableEvidenceRouteCards.Clear();

            int blockNumber = 0;
            foreach (AiPhaseVarLinkedBlockVm block in blocks)
            {
                blockNumber++;
                PhaseVariableEvidenceRouteCards.Add(BuildVarEvidenceRouteCard(block, blockNumber));

                int unitNumber = 0;
                foreach (AiPhaseVarIndirectDispatchLinkVm unit in block.IndirectDispatchUnits)
                {
                    unitNumber++;
                    PhaseVariableEvidenceRouteCards.Add(BuildIndirectEvidenceRouteCard(unit, blockNumber, unitNumber));
                }
            }

            if (PhaseVariableEvidenceRouteCards.Count == 0)
            {
                PhaseVariableEvidenceRouteCards.Add(new AiPhaseRouteCardVm(
                    "evidence:empty",
                    Strings.U_Ai_PrcNoEvidence,
                    string.Format(Strings.U_Ai_PrcNoRoute, row.VariableName),
                    AiPhaseRouteCardSourceKind.VariableEvidence,
                    new[] { Strings.U_Ai_Empty },
                    new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowChosen, Strings.U_Ai_PrcNoBlock, Strings.U_Ai_PrcNoBlockDetail),
                    new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcWhatItDoes, Strings.U_Ai_PrcNoPayload, Strings.U_Ai_PrcNoPayloadDetail),
                    new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowTarget, Strings.U_Ai_PrcNoTarget, Strings.U_Ai_PrcNoTargetDetail),
                    new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowState, Strings.U_Ai_PrcNoWrite, Strings.U_Ai_PrcNoWriteDetail),
                    Strings.U_Ai_PrcSwitchVar));
            }
        }

        AiPhaseRouteCardVm BuildVarEvidenceRouteCard(AiPhaseVarLinkedBlockVm block, int blockNumber)
        {
            string howChosenSummary = string.IsNullOrWhiteSpace(block.GuardSummary)
                ? block.HookLabel
                : $"{block.HookLabel} · {block.GuardSummary}";
            string howChosenDetails = block.TechnicalSummary;

            string whatItDoesSummary = FirstNonEmpty(
                block.EffectSummary,
                block.WriteSummary,
                block.NextStateSummary,
                Strings.U_Ai_PrcNoEffect);
            string whatItDoesDetails = JoinNonEmpty(
                block.HasEffectSummary ? string.Format(Strings.U_Ai_PrcEffect, block.EffectSummary) : null,
                block.HasWriteSummary ? $"Writes: {block.WriteSummary}" : null,
                block.HasNoteSummary ? $"Notas: {block.NoteSummary}" : null);

            string targetSummary = block.HasConsumerSummary
                ? block.ConsumerSummary
                : Strings.U_Ai_PrcTargetUnresolved;
            string targetDetails = block.HasConsumerSummary
                ? string.Format(Strings.U_Ai_PrcConsumers, block.ConsumerSummary)
                : Strings.U_Ai_PrcNoTargetDetail2;

            string stateSummary = FirstNonEmpty(
                block.HasNextStateSummary ? block.NextStateSummary : null,
                block.HasWriteSummary ? block.WriteSummary : null,
                Strings.U_Ai_PrcNoStateWrite);
            string stateDetails = JoinNonEmpty(
                block.HasWriteSummary ? string.Format(Strings.U_Ai_PrcWritesState, block.WriteSummary) : null,
                block.HasNextStateSummary ? string.Format(Strings.U_Ai_PrcNextState, block.NextStateSummary) : null,
                block.HasIndirectDispatchUnits ? string.Format(Strings.U_Ai_PrcIndirectFamilies, block.IndirectDispatchUnits.Count) : null);

            var badges = new List<string>();
            if (!string.IsNullOrWhiteSpace(block.Badge))
                badges.Add(block.Badge);
            if (block.HasIndirectDispatchUnits)
                badges.Add(Strings.U_Ai_PrcIndirect);
            if (badges.Count == 0)
                badges.Add(Strings.U_Ai_PrcEvidence);

            return new AiPhaseRouteCardVm(
                $"evidence:block:{blockNumber}",
                string.Format(Strings.U_Ai_PrcBlockLinked, blockNumber),
                block.HookLabel,
                AiPhaseRouteCardSourceKind.VariableEvidence,
                badges,
                new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowChosen, howChosenSummary, howChosenDetails),
                new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcWhatItDoes, whatItDoesSummary, whatItDoesDetails),
                new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowTarget, targetSummary, targetDetails),
                new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowState, stateSummary, stateDetails),
                FirstNonEmpty(block.OffsetSummary, block.TechnicalSummary, block.HookLabel));
        }

        AiPhaseRouteCardVm BuildIndirectEvidenceRouteCard(AiPhaseVarIndirectDispatchLinkVm unit, int blockNumber, int unitNumber)
        {
            string whatItDoesSummary = FirstNonEmpty(unit.PayloadSummary, Strings.U_Ai_PrcIndirectDispatch);
            string stateDetails = JoinNonEmpty(
                unit.HasNextStateSummary ? $"Next state: {unit.NextStateSummary}" : null,
                unit.HasNoteSummary ? $"Notas: {unit.NoteSummary}" : null,
                unit.CanOpenEditor ? Strings.F2_mvp_row_only_editable_via_the_specialize_016d7862 : Strings.U_Ai_PrcReadOnlyCut);

            var badges = new List<string> { Strings.U_Ai_PrcIndirect };
            if (!string.IsNullOrWhiteSpace(unit.TierLabel))
                badges.Add(unit.TierLabel);
            if (!string.IsNullOrWhiteSpace(unit.RoleSummary))
                badges.Add(Strings.U_Ai_PrcRole);

            return new AiPhaseRouteCardVm(
                $"evidence:block:{blockNumber}:indirect:{unitNumber}",
                unit.RouteTitle,
                FirstNonEmpty(unit.RoleSummary, Strings.F2_indirect_family_linked_to_this_var_70eb0fbf),
                AiPhaseRouteCardSourceKind.IndirectEvidence,
                badges,
                new AiPhaseRouteQuadrantVm(Strings.F2_how_it_was_chosen_e2392801, unit.GuardSummary, unit.GuardSummary),
                new AiPhaseRouteQuadrantVm(Strings.F2_what_it_does_ca032b58, whatItDoesSummary, whatItDoesSummary),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_resolves_target_9e585916,
                    unit.HasConsumerSummary ? unit.ConsumerSummary : Strings.U_Ai_PrcIndirectConsumer,
                    unit.HasConsumerSummary ? string.Format(Strings.U_Ai_PrcConsumers, unit.ConsumerSummary) : "O alvo sai do pacote indireto/consumers desta familia."),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_leaves_the_state_2eb60554,
                    FirstNonEmpty(unit.NextStateSummary, unit.NoteSummary, Strings.U_Ai_PrcNoNextState),
                    stateDetails),
                unit.CanOpenEditor ? Strings.U_Ai_PrcOpenEditor : Strings.U_Ai_PrcReadOnlyUnit,
                unit.CanOpenEditor ? unit.UnitId : null);
        }

        void RebuildPhaseDraftRouteCards()
        {
            PhaseDraftRouteCards.Clear();
            draftIndirectUnitsById.Clear();
            foreach (AiPhaseDraftStepVm step in PhaseDraftSteps)
                PhaseDraftRouteCards.Add(BuildDraftRouteCard(step));

            AppendIndirectDraftRouteCards();
            RefreshSelectedDraftIndirectRoute();
            OnPropertyChanged(nameof(IsPhaseDraftEmpty));
        }

        void AppendIndirectDraftRouteCards()
        {
            if (SelectedPhaseVariableAudit == null ||
                selectedScript == null ||
                !selectedScript.HasScript ||
                string.IsNullOrWhiteSpace(selectedPath) ||
                !File.Exists(selectedPath) ||
                !TryGetRuntimeVariableIndex(SelectedPhaseVariableAudit, out ushort runtimeIndex))
            {
                return;
            }

            byte[] monsterBin;
            try
            {
                monsterBin = File.ReadAllBytes(selectedPath);
            }
            catch
            {
                return;
            }

            HashSet<string> existingRouteIds = PhaseDraftRouteCards
                .Select(card => card.RouteId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (AiIndirectDispatchUnit unit in DetectPromotedIndirectDispatchUnits(monsterBin)
                         .Where(unit => IsDraftIndirectUnitForCounter(unit, runtimeIndex))
                         .OrderBy(unit => unit.UnitIndex))
            {
                string routeId = $"draft-indirect:{unit.UnitId}";
                if (existingRouteIds.Contains(routeId))
                    continue;

                draftIndirectUnitsById[unit.UnitId] = unit;
                PhaseDraftRouteCards.Add(BuildDraftIndirectRouteCard(unit));
            }
        }

        void RefreshSelectedDraftIndirectRoute()
        {
            if (SelectedDraftIndirectRoute == null)
                return;

            if (!draftIndirectUnitsById.TryGetValue(SelectedDraftIndirectRoute.UnitId, out AiIndirectDispatchUnit? unit))
            {
                SelectedDraftIndirectRoute = null;
                return;
            }

            SelectedDraftIndirectRoute = BuildDraftIndirectRouteFocus(unit);
        }

        public void SelectDraftIndirectRoute(string unitId)
        {
            if (!draftIndirectUnitsById.TryGetValue(unitId, out AiIndirectDispatchUnit? unit))
                return;

            SelectedPhaseDraftStep = null;
            SelectedDraftIndirectRoute = BuildDraftIndirectRouteFocus(unit);
        }

        bool IsDraftIndirectUnitForCounter(AiIndirectDispatchUnit unit, ushort runtimeIndex) =>
            unit.PayloadWrites.Any(write =>
                write.VariableIndex == runtimeIndex &&
                write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase));

        AiPhaseRouteCardVm BuildDraftRouteCard(AiPhaseDraftStepVm step)
        {
            string howChosenSummary = JoinNonEmpty(
                step.TriggerSummary,
                step.GuardModeSummary,
                step.UseHpBelowGuard ? step.HpGuardSummary : null,
                step.UseChance ? step.ChanceSummary : null);

            string howChosenDetails = JoinNonEmpty(
                $"Trigger: {step.TriggerSummary}",
                $"Guarda: {step.GuardModeSummary}",
                step.UseHpBelowGuard ? step.HpGuardSummary : null,
                step.UseChance ? string.Format(Strings.U_Ai_PrcChance, step.ChanceSummary) : Strings.U_Ai_PrcNoChance);

            string whatItDoesDetails = JoinNonEmpty(
                step.StopHere ? Strings.F2_stop_here_after_executing_a847ecef : Strings.F2_continue_to_the_next_phase_after_executi_cc7fd517,
                step.UseForbiddenRite ? step.ForbiddenRiteSummary : null);

            string stateSummary = step.VarAdvances.Count == 0
                ? (step.IsFinalPhase ? Strings.U_Ai_PrcFinalNoWrite : Strings.U_Ai_PrcNoExtraWrite)
                : step.VarAdvanceSummary;
            string stateDetails = JoinNonEmpty(
                step.AdvanceRuleSummary,
                step.IsFinalPhase ? Strings.F2_last_phase_ends_the_rotation_853316f8 : null,
                step.StopHere ? Strings.F2_blocks_continuation_after_this_step_ce7fdcbc : Strings.F2_allows_proceeding_to_the_next_step_2955d34c);

            var badges = new List<string>();
            badges.Add(step.IsFinalPhase ? Strings.U_Ai_PrcFinal : Strings.U_Ai_PrcPhase);
            if (step.UseChance)
                badges.Add("chance");
            if (step.UseHpBelowGuard)
                badges.Add("hp-guard");
            if (!string.IsNullOrWhiteSpace(step.ConditionSummary))
                badges.Add("condicao");
            if (step.UseForbiddenRite)
                badges.Add("rite");
            if (step.VarAdvances.Count > 0)
                badges.Add("escrita");
            if (step.TriggerKind == AiPhaseTriggerKind.OnHit)
                badges.Add("onHit");
            else if (step.TriggerKind != AiPhaseTriggerKind.OnTurn)
                badges.Add("hook");

            return new AiPhaseRouteCardVm(
                $"draft:{step.Number}",
                step.StepLabel,
                step.Title,
                AiPhaseRouteCardSourceKind.Draft,
                badges,
                new AiPhaseRouteQuadrantVm(Strings.F2_how_it_was_chosen_e2392801, howChosenSummary, howChosenDetails),
                new AiPhaseRouteQuadrantVm(Strings.F2_what_it_does_ca032b58, step.Title, whatItDoesDetails),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_resolves_target_9e585916,
                    step.TargetSummary,
                    step.SelectedTarget?.Label ?? Strings.F2_target_not_yet_selected_9d6127d4),
                new AiPhaseRouteQuadrantVm(Strings.F2_how_it_leaves_the_state_2eb60554, stateSummary, stateDetails),
                FirstNonEmpty(step.Subtitle, string.Format(Strings.U_Ai_PrcPhaseLabel, step.Number)));
        }

        AiPhaseRouteCardVm BuildDraftIndirectRouteCard(AiIndirectDispatchUnit unit)
        {
            string payloadSummary = string.Join("; ", unit.PayloadWrites.Select(write =>
                $"{write.RoleSummary}: {write.ValueSummary}"));
            string abilityHeadline = string.Join(" / ", unit.PayloadWrites
                .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                .Select(write => write.ValueSummary)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            string storageSummary = string.Join("; ", unit.PayloadWrites.Select(write =>
                $"{write.VariableName} <- {write.ValueSummary}"));
            string consumerSummary = string.Join("; ", unit.Consumers.Select(consumer =>
                $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName}"));
            string consumerDetails = string.IsNullOrWhiteSpace(consumerSummary)
                ? Strings.U_Ai_PrcRealTargets
                : string.Format(Strings.U_Ai_PrcRealConsumers, consumerSummary);
            string payloadDetails = JoinNonEmpty(
                storageSummary,
                Strings.F2_the_abilities_of_this_phase_do_not_stay_83f8743f,
                string.Join("; ", unit.CompanionEffects.Where(effect => !string.IsNullOrWhiteSpace(effect))));
            string noteSummary = JoinNonEmpty(
                unit.WarningSummary,
                string.Join("; ", unit.CompanionEffects.Where(effect => !string.IsNullOrWhiteSpace(effect))));
            string stateSummary = FirstNonEmpty(unit.NextStateSummary, Strings.U_Ai_PrcIndirectNextState);
            string stateDetails = JoinNonEmpty(
                unit.NextStateSummary,
                unit.WarningSummary,
                Strings.F2_current_simple_draft_writer_does_not_rew_d3ce05a6);

            return new AiPhaseRouteCardVm(
                $"draft-indirect:{unit.UnitId}",
                string.Format(Strings.U_Ai_PrcIndirectFamily, unit.UnitIndex + 1),
                FirstNonEmpty(abilityHeadline, payloadSummary, Strings.U_Ai_PrcIndirectPayload),
                AiPhaseRouteCardSourceKind.IndirectEvidence,
                new[] { Strings.U_Ai_PrcIndirect, unit.CapabilityLabel },
                new AiPhaseRouteQuadrantVm(Strings.U_Ai_PrcHowChosen, unit.GuardSummary, JoinNonEmpty(string.Format(Strings.U_Ai_PrcHookReal, unit.HookKind), unit.GuardSummary, unit.OffsetSummary)),
                new AiPhaseRouteQuadrantVm(Strings.F2_what_it_does_ca032b58, FirstNonEmpty(abilityHeadline, payloadSummary), payloadDetails),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_resolves_target_9e585916,
                    string.IsNullOrWhiteSpace(consumerSummary) ? Strings.U_Ai_PrcIndirectTargets : consumerSummary,
                    consumerDetails),
                new AiPhaseRouteQuadrantVm(Strings.F2_how_it_leaves_the_state_2eb60554, stateSummary, JoinNonEmpty(stateDetails, noteSummary)),
                FirstNonEmpty(Strings.F2_edit_via_the_slots_of_the_indirect_famil_d0f9af6b, unit.WarningSummary, unit.OffsetSummary, unit.CapabilityLabel),
                unit.UnitId);
        }

        AiDraftIndirectRouteFocusVm BuildDraftIndirectRouteFocus(AiIndirectDispatchUnit unit)
        {
            string payloadSummary = string.Join("; ", unit.PayloadWrites.Select(write =>
                $"{write.RoleSummary}: {write.ValueSummary}"));
            string storageSummary = string.Join(Environment.NewLine, unit.PayloadWrites.Select(write =>
                $"{write.VariableName} <- {write.ValueSummary}"));
            string consumerSummary = unit.Consumers.Count == 0
                ? "No readable consumer."
                : string.Join(Environment.NewLine, unit.Consumers.Select(consumer =>
                    $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName} -> 0x{consumer.CallOffset:X4}"));
            string notes = JoinNonEmpty(
                unit.WarningSummary,
                string.Join(Environment.NewLine, unit.CompanionEffects.Where(effect => !string.IsNullOrWhiteSpace(effect))));
            string structuralSummary =
                "Shape real: priv0020 escolhe a rota; priv0024/0028/002C/0030 guardam payload de comando; " +
                "priv0014/0018/001C store indirect target; the consumers trigger the final performCommand; next-state closes the cycle.";
            string targetingSummary = unit.Consumers.Count == 0
                ? "The final target is not fixed in this read. In complex families, it may come from an indirect slot, calculation, or random roll."
                : "The final target does not reside in the simple phase. It comes from the consumers/slots of this family and may remain indirect, calculated, or random.";

            return new AiDraftIndirectRouteFocusVm(
                unit.UnitId,
                string.Format(Strings.U_Ai_PrcIndirectFamily, unit.UnitIndex + 1),
                unit.GuardSummary,
                payloadSummary,
                storageSummary,
                consumerSummary,
                unit.NextStateSummary,
                notes,
                unit.CapabilityLabel,
                unit.OffsetSummary,
                structuralSummary,
                targetingSummary);
        }

        void RebuildConditionPreviewRouteCards()
        {
            PhaseConditionPreviewRouteCards.Clear();
            if (PhaseConditionClauses.Count == 0)
                return;

            List<AiPhaseConditionClauseVm> clauses = PhaseConditionClauses.ToList();
            string howChosenSummary = string.Join(" ", clauses.Select((clause, index) =>
                index == 0 ? clause.ExpressionText : $"{clause.Join} {clause.ExpressionText}"));
            string howChosenDetails = clauses.Count == 1
                ? Strings.F2_single_condition_the_stored_block_only_p_0a011ebe
                : string.Format(Strings.U_Ai_PrcMultivarCondition, clauses.Count);

            string whatItDoesSummary = Strings.U_Ai_PrcGuardsBlock;
            string whatItDoesDetails = string.Join("; ", clauses.Select(clause => clause.ExpressionText));

            var badges = new List<string> { "condicao" };
            if (clauses.Count > 1)
                badges.Add("multivar");
            if (clauses.Any(clause => clause.Operator is "==" or "!="))
                badges.Add("match");
            if (clauses.Any(clause => clause.Operator is ">" or "<" or ">=" or "<="))
                badges.Add("range");

            PhaseConditionPreviewRouteCards.Add(new AiPhaseRouteCardVm(
                "condition-preview",
                Strings.U_Ai_PrcFreeMultivar,
                howChosenSummary,
                AiPhaseRouteCardSourceKind.ConditionPreview,
                badges,
                new AiPhaseRouteQuadrantVm(Strings.F2_how_it_was_chosen_e2392801, howChosenSummary, howChosenDetails),
                new AiPhaseRouteQuadrantVm(Strings.F2_what_it_does_ca032b58, whatItDoesSummary, whatItDoesDetails),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_resolves_target_9e585916,
                    Strings.F2_n_a_pure_condition_6e3744ff,
                    Strings.F2_the_target_comes_from_the_phase_action_t_c8c41d3d),
                new AiPhaseRouteQuadrantVm(
                    Strings.F2_how_it_leaves_state_77ee7ad4,
                    Strings.U_Ai_PrcPureCondition,
                    Strings.U_Ai_PrcNoWriteNoNext),
                string.Format(Strings.U_Ai_PrcStructuralClauses, clauses.Count)));
        }

        static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        static string JoinNonEmpty(params string?[] values) =>
            string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    internal sealed class AiPhaseVarIndirectDispatchLinkVm
    {
        public AiPhaseVarIndirectDispatchLinkVm(
            string unitId,
            string routeTitle,
            string roleSummary,
            string payloadSummary,
            string consumerSummary,
            string nextStateSummary,
            string noteSummary,
            string tierLabel,
            string guardSummary,
            bool canOpenEditor)
        {
            UnitId = unitId;
            RouteTitle = routeTitle;
            RoleSummary = roleSummary;
            PayloadSummary = payloadSummary;
            ConsumerSummary = consumerSummary;
            NextStateSummary = nextStateSummary;
            NoteSummary = noteSummary;
            TierLabel = tierLabel;
            GuardSummary = guardSummary;
            CanOpenEditor = canOpenEditor;
        }

        public string UnitId { get; }
        public string RouteTitle { get; }
        public string RoleSummary { get; }
        public string PayloadSummary { get; }
        public bool HasPayloadSummary => !string.IsNullOrWhiteSpace(PayloadSummary);
        public string ConsumerSummary { get; }
        public bool HasConsumerSummary => !string.IsNullOrWhiteSpace(ConsumerSummary);
        public string NextStateSummary { get; }
        public bool HasNextStateSummary => !string.IsNullOrWhiteSpace(NextStateSummary);
        public string NoteSummary { get; }
        public bool HasNoteSummary => !string.IsNullOrWhiteSpace(NoteSummary);
        public string TierLabel { get; }
        public string GuardSummary { get; }
        public bool CanOpenEditor { get; }
    }

    internal sealed class AiDraftIndirectRouteFocusVm
    {
        public AiDraftIndirectRouteFocusVm(
            string unitId,
            string title,
            string guardSummary,
            string payloadSummary,
            string storageSummary,
            string consumerSummary,
            string nextStateSummary,
            string notes,
            string capabilityLabel,
            string offsetSummary,
            string structuralSummary,
            string targetingSummary)
        {
            UnitId = unitId;
            Title = title;
            GuardSummary = guardSummary;
            PayloadSummary = payloadSummary;
            StorageSummary = storageSummary;
            ConsumerSummary = consumerSummary;
            NextStateSummary = nextStateSummary;
            Notes = notes;
            CapabilityLabel = capabilityLabel;
            OffsetSummary = offsetSummary;
            StructuralSummary = structuralSummary;
            TargetingSummary = targetingSummary;
        }

        public string UnitId { get; }
        public string Title { get; }
        public string GuardSummary { get; }
        public string PayloadSummary { get; }
        public string StorageSummary { get; }
        public string ConsumerSummary { get; }
        public string NextStateSummary { get; }
        public string Notes { get; }
        public string CapabilityLabel { get; }
        public string OffsetSummary { get; }
        public string StructuralSummary { get; }
        public string TargetingSummary { get; }
    }

    internal enum AiPhaseRouteCardSourceKind
    {
        Draft,
        ConditionPreview,
        VariableEvidence,
        IndirectEvidence,
    }

    internal sealed class AiPhaseRouteQuadrantVm
    {
        public AiPhaseRouteQuadrantVm(string title, string summary, string details, bool isPartial = false)
        {
            Title = title;
            Summary = summary;
            Details = details;
            IsPartial = isPartial;
        }

        public string Title { get; }
        public string Summary { get; }
        public string Details { get; }
        public bool IsPartial { get; }
        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    }

    internal sealed partial class AiPhaseRouteCardVm : ObservableObject
    {
        [ObservableProperty] private bool isExpanded;

        public AiPhaseRouteCardVm(
            string routeId,
            string title,
            string subtitle,
            AiPhaseRouteCardSourceKind sourceKind,
            IReadOnlyList<string> badges,
            AiPhaseRouteQuadrantVm howChosen,
            AiPhaseRouteQuadrantVm whatItDoes,
            AiPhaseRouteQuadrantVm howItTargets,
            AiPhaseRouteQuadrantVm howItLeavesState,
            string footer,
            string? indirectUnitId = null)
        {
            RouteId = routeId;
            Title = title;
            Subtitle = subtitle;
            SourceKind = sourceKind;
            Badges = badges;
            HowChosen = howChosen;
            WhatItDoes = whatItDoes;
            HowItTargets = howItTargets;
            HowItLeavesState = howItLeavesState;
            Footer = footer;
            IndirectUnitId = indirectUnitId;
        }

        public string RouteId { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public AiPhaseRouteCardSourceKind SourceKind { get; }
        public IReadOnlyList<string> Badges { get; }
        public AiPhaseRouteQuadrantVm HowChosen { get; }
        public AiPhaseRouteQuadrantVm WhatItDoes { get; }
        public AiPhaseRouteQuadrantVm HowItTargets { get; }
        public AiPhaseRouteQuadrantVm HowItLeavesState { get; }
        public string Footer { get; }
        public string? IndirectUnitId { get; }
        public bool CanOpenIndirectEditor => !string.IsNullOrWhiteSpace(IndirectUnitId);
    }
}
