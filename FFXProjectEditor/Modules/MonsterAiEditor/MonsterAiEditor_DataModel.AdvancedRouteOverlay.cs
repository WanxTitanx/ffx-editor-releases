using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        int? lastAdvancedRouteBundleConsumerCallOffset;

        public ObservableCollection<AiAdvancedRouteConsumerVm> AdvancedRouteConsumers { get; } = new();

        [ObservableProperty] private AiAdvancedRouteConsumerVm? selectedAdvancedRouteConsumer;
        partial void OnSelectedAdvancedRouteConsumerChanged(AiAdvancedRouteConsumerVm? value)
        {
            lastAdvancedRouteBundleConsumerCallOffset = value?.CallOffset;
            OnPropertyChanged(nameof(HasAdvancedRouteBundle));
            OnPropertyChanged(nameof(CanApplyAdvancedRouteForbiddenRite));
            OnPropertyChanged(nameof(CanApplyAdvancedRouteSecondCast));
            UpdateAdvancedRouteBundleSummary();
        }

        [ObservableProperty] private AiCommandOption? selectedAdvancedRouteSecondAbility;
        partial void OnSelectedAdvancedRouteSecondAbilityChanged(AiCommandOption? value)
        {
            OnPropertyChanged(nameof(CanApplyAdvancedRouteSecondCast));
            UpdateAdvancedRouteBundleSummary();
        }

        [ObservableProperty] private AiTargetOption? selectedAdvancedRouteForbiddenTarget;
        partial void OnSelectedAdvancedRouteForbiddenTargetChanged(AiTargetOption? value)
        {
            OnPropertyChanged(nameof(CanApplyAdvancedRouteForbiddenRite));
            UpdateAdvancedRouteBundleSummary();
        }

        [ObservableProperty] private AiForbiddenStatusPreset? selectedAdvancedRouteForbiddenStatus;
        partial void OnSelectedAdvancedRouteForbiddenStatusChanged(AiForbiddenStatusPreset? value)
        {
            if (value != null)
                AdvancedRouteForbiddenValue = value.DefaultValue.ToString(CultureInfo.InvariantCulture);

            OnPropertyChanged(nameof(CanApplyAdvancedRouteForbiddenRite));
            UpdateAdvancedRouteBundleSummary();
        }

        [ObservableProperty] private string advancedRouteForbiddenValue = "1";
        partial void OnAdvancedRouteForbiddenValueChanged(string value) => UpdateAdvancedRouteBundleSummary();

        [ObservableProperty] private string advancedRouteBundleSummary =
            "Escolha uma rota e um consumer para gravar bundle por cima do performCommand indireto certo.";

        [ObservableProperty] private string advancedRouteBundlePreview =
            "This route's bundle reuses the consumer in focus: second cast and Forbidden Rite enter right after the selected performCommand.";

        [ObservableProperty] private string advancedRouteBundleHonestSummary =
            "Here the write is by indirect consumer. Guard, switch, topology and recipe v2 remain outside this pass.";

        [ObservableProperty] private string advancedRouteBundleStatus =
            "Escolha um consumer da rota para aplicar Forbidden Rite ou encadear um segundo cast.";

        public bool HasAdvancedRouteBundle => SelectedAdvancedPhaseUnit != null && AdvancedRouteConsumers.Count > 0;
        public bool CanApplyAdvancedRouteForbiddenRite =>
            SelectedAdvancedRouteConsumer != null
            && SelectedAdvancedRouteForbiddenTarget != null
            && SelectedAdvancedRouteForbiddenStatus != null;
        public bool CanApplyAdvancedRouteSecondCast =>
            SelectedAdvancedRouteConsumer != null
            && SelectedAdvancedRouteSecondAbility != null;

        void UpdateAdvancedRouteBundleContext()
        {
            string neutralStatus =
                "Escolha um consumer da rota para aplicar Forbidden Rite ou encadear um segundo cast.";
            AdvancedRouteConsumers.Clear();

            if (SelectedAdvancedPhaseUnit == null
                || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out AiIndirectDispatchUnit? unit))
            {
                SelectedAdvancedRouteConsumer = null;
                AdvancedRouteBundleSummary =
                    Strings.F2_choose_a_route_on_the_structural_map_to_629ef622;
                AdvancedRouteBundlePreview =
                    Strings.F2_v2_uses_the_selected_consumer_as_the_ins_b9e3adbe;
                AdvancedRouteBundleHonestSummary =
                    Strings.F2_this_panel_does_not_create_new_topology_265a62b9;
                AdvancedRouteBundleStatus = neutralStatus;
                OnPropertyChanged(nameof(HasAdvancedRouteBundle));
                OnPropertyChanged(nameof(CanApplyAdvancedRouteForbiddenRite));
                OnPropertyChanged(nameof(CanApplyAdvancedRouteSecondCast));
                return;
            }

            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots =
                AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers);
            IReadOnlyDictionary<int, AiDetectedAction> actionsByCall = selectedScript != null && selectedScript.HasScript
                ? AiAutomation.DetectActions(selectedScript)
                    .Where(action => action.Kind == AiActionKind.Command)
                    .GroupBy(action => action.CallOffset)
                    .ToDictionary(group => group.Key, group => group.First())
                : new Dictionary<int, AiDetectedAction>();

            foreach (AiIndirectDispatchConsumer consumer in unit.Consumers.OrderBy(consumer => consumer.CallOffset))
            {
                AdvancedRouteConsumers.Add(BuildAdvancedRouteConsumerVm(unit, consumer, targetSlots, actionsByCall));
            }

            SelectedAdvancedRouteSecondAbility ??= SelectedAutomationAbility ?? AutomationAbilityOptions.FirstOrDefault();
            SelectedAdvancedRouteForbiddenTarget ??=
                ForbiddenRiteTargets.FirstOrDefault(target => target.UseLinkedActionTarget)
                ?? ForbiddenRiteTargets.FirstOrDefault();
            SelectedAdvancedRouteForbiddenStatus ??= ForbiddenStatusPresets.FirstOrDefault();

            AiAdvancedRouteConsumerVm? preferred = FindPreferredAdvancedRouteConsumer(unit);
            SelectedAdvancedRouteConsumer =
                preferred == null
                    ? AdvancedRouteConsumers.FirstOrDefault()
                    : AdvancedRouteConsumers.FirstOrDefault(candidate => candidate.CallOffset == preferred.CallOffset)
                      ?? AdvancedRouteConsumers.FirstOrDefault();

            AdvancedRouteBundleStatus = SelectedAdvancedRouteConsumer == null
                ? string.Format(Strings.U_Ai_RouteNoEditableConsumer, unit.UnitIndex)
                : neutralStatus;
            OnPropertyChanged(nameof(HasAdvancedRouteBundle));
            OnPropertyChanged(nameof(CanApplyAdvancedRouteForbiddenRite));
            OnPropertyChanged(nameof(CanApplyAdvancedRouteSecondCast));
            UpdateAdvancedRouteBundleSummary();
        }

        void UpdateAdvancedRouteBundleSummary()
        {
            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedRouteBundleSummary =
                    Strings.F2_choose_a_route_on_the_structural_map_to_a25b6c99;
                AdvancedRouteBundlePreview =
                    Strings.F2_the_v2_uses_the_selected_consumer_as_the_44687572;
                AdvancedRouteBundleHonestSummary =
                    Strings.F2_this_panel_does_not_create_new_topology_265a62b9;
                return;
            }

            if (SelectedAdvancedRouteConsumer == null)
            {
                AdvancedRouteBundleSummary =
                    string.Format(Strings.U_Ai_RouteChooseConsumer, SelectedAdvancedPhaseUnit.Title);
                AdvancedRouteBundlePreview =
                    Strings.F2_the_buttons_below_always_act_on_a_concre_2610c158;
                AdvancedRouteBundleHonestSummary =
                    Strings.F2_without_a_selected_consumer_the_v2_does_fd3f3c4d;
                return;
            }

            string routeLabel = SelectedAdvancedPhaseUnit.Title;
            string secondCast = SelectedAdvancedRouteSecondAbility?.Name ?? Strings.F2_choose_the_ability_for_the_second_cast_2870678c;
            string riteTarget = SelectedAdvancedRouteForbiddenTarget == null
                ? Strings.F2_choose_the_target_for_forbidden_rite_796ef64e
                : SelectedAdvancedRouteForbiddenTarget.UseLinkedActionTarget
                    ? Strings.F2_same_target_as_the_consumer_2291f0aa
                    : SelectedAdvancedRouteForbiddenTarget.Label;
            string riteStatus = SelectedAdvancedRouteForbiddenStatus == null
                ? Strings.F2_choose_the_status_e7a2b213
                : $"{SelectedAdvancedRouteForbiddenStatus.Name} ({ParseU16Loose(AdvancedRouteForbiddenValue)})";

            AdvancedRouteBundleSummary =
                $"{routeLabel} · {SelectedAdvancedRouteConsumer.Title} · {SelectedAdvancedRouteConsumer.CallSummary}";
            AdvancedRouteBundlePreview =
                string.Format(Strings.U_Ai_RouteSecondCastPreview, secondCast, riteStatus, riteTarget,
                    SelectedAdvancedRouteConsumer.PayloadSummary, SelectedAdvancedRouteConsumer.TargetSummary);
            AdvancedRouteBundleHonestSummary =
                Strings.F2_each_button_stacks_right_after_the_chose_b5029835;
        }

        AiAdvancedRouteConsumerVm? FindPreferredAdvancedRouteConsumer(AiIndirectDispatchUnit unit)
        {
            if (SelectedAdvancedVariableFocusRow != null)
            {
                string? variableName =
                    SelectedAdvancedVariableFocusRow.EditorLaunchContext?.VariableName
                    ?? SelectedPhaseVariableAudit?.VariableName;
                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    AiAdvancedRouteConsumerVm? focusMatch =
                        SelectedAdvancedVariableFocusRow.IsCommandRole
                            ? AdvancedRouteConsumers.FirstOrDefault(candidate =>
                                candidate.CommandVariableName.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                            : SelectedAdvancedVariableFocusRow.IsTargetRole
                                ? AdvancedRouteConsumers.FirstOrDefault(candidate =>
                                    candidate.TargetVariableName.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                                : null;
                    if (focusMatch != null)
                        return focusMatch;
                }
            }

            if (lastAdvancedRouteBundleConsumerCallOffset.HasValue)
            {
                AiAdvancedRouteConsumerVm? remembered = AdvancedRouteConsumers.FirstOrDefault(candidate =>
                    candidate.CallOffset == lastAdvancedRouteBundleConsumerCallOffset.Value);
                if (remembered != null)
                    return remembered;
            }

            AiIndirectDispatchConsumer? firstConsumer = unit.Consumers.OrderBy(consumer => consumer.CallOffset).FirstOrDefault();
            return firstConsumer == null
                ? null
                : AdvancedRouteConsumers.FirstOrDefault(candidate => candidate.CallOffset == firstConsumer.CallOffset);
        }

        AiAdvancedRouteConsumerVm BuildAdvancedRouteConsumerVm(
            AiIndirectDispatchUnit unit,
            AiIndirectDispatchConsumer consumer,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots,
            IReadOnlyDictionary<int, AiDetectedAction> actionsByCall)
        {
            AiIndirectDispatchWrite? payloadWrite = unit.PayloadWrites.FirstOrDefault(write =>
                write.VariableIndex == consumer.CommandVariableIndex);
            AiIndirectDispatchTargetSlotSurface? targetSlot = targetSlots.FirstOrDefault(slot =>
                slot.VariableIndex == consumer.TargetVariableIndex);
            actionsByCall.TryGetValue(consumer.CallOffset, out AiDetectedAction? action);

            string payloadSummary = payloadWrite == null
                ? string.Format(Strings.U_Ai_RoutePayloadIn, consumer.CommandVariableName)
                : $"{consumer.CommandVariableName} -> {payloadWrite.ValueSummary}";
            string targetSummary = targetSlot == null
                ? string.Format(Strings.U_Ai_RouteTargetIn, consumer.TargetVariableName)
                : $"{consumer.TargetVariableName} -> {targetSlot.ValueSummary}";
            string callMode = action == null
                ? Strings.U_Ai_RouteIndirectPerform
                : action.ForcePerform ? "forcePerformCommand" : "performCommand";
            string technicalSummary =
                $"{consumer.CommandVariableName} + {consumer.TargetVariableName} -> 0x{consumer.CallOffset:X4}";

            return new AiAdvancedRouteConsumerVm(
                consumer.Label,
                payloadSummary,
                targetSummary,
                $"consumer @0x{consumer.CallOffset:X4} · {callMode}",
                technicalSummary,
                consumer.CallOffset,
                consumer.CommandVariableIndex,
                consumer.CommandVariableName,
                consumer.TargetVariableIndex,
                consumer.TargetVariableName);
        }

        public void ApplyAdvancedRouteForbiddenRite()
        {
            if (!TryResolveAdvancedRouteBundleOperation(
                    out AiIndirectDispatchUnit _,
                    out AiAdvancedRouteConsumerVm consumerVm,
                    out AiIndirectDispatchConsumer consumer,
                    out AiDetectedAction action,
                    out int reselectIndex,
                    out string error))
            {
                AdvancedRouteBundleStatus = error;
                RemoveActionSummary = error;
                return;
            }

            AiTargetOption? target = SelectedAdvancedRouteForbiddenTarget;
            AiForbiddenStatusPreset? status = SelectedAdvancedRouteForbiddenStatus;
            if (target == null || status == null)
            {
                AdvancedRouteBundleStatus = Strings.F2_choose_the_target_and_status_of_forbidde_4e8fc721;
                RemoveActionSummary = AdvancedRouteBundleStatus;
                return;
            }

            if (!AiAdvancedNumericInput.TryParse(AdvancedRouteForbiddenValue, out ushort value))
            {
                AdvancedRouteBundleStatus = RemoveActionSummary = Strings.AiAdvancedInvalidNumber;
                return;
            }
            List<AiInstruction>? changed = target.UseLinkedActionTarget
                ? AiAutomation.InsertChrPropertyWriteAfterActionUsingActionTarget(
                    selectedScript!,
                    action,
                    status.FieldId,
                    value)
                : AiAutomation.InsertChrPropertyWriteAfterAction(
                    selectedScript!,
                    action,
                    target.Operand,
                    status.FieldId,
                    value);
            if (changed == null)
            {
                AdvancedRouteBundleStatus =
                    string.Format(Strings.U_Ai_RouteForbiddenRiteFailed, consumerVm.Title);
                RemoveActionSummary = AdvancedRouteBundleStatus;
                return;
            }

            string targetLabel = target.UseLinkedActionTarget ? Strings.F2_same_target_as_the_consumer_2291f0aa : target.Label;
            string okMessage =
                string.Format(Strings.U_Ai_RouteForbiddenRiteOk, consumerVm.Title, SelectedAdvancedPhaseUnit!.Title,
                    status.Name, value, targetLabel, consumer.CallOffset);
            ApplyAndSave(changed, okMessage, reselectIndex);
            lastAdvancedRouteBundleConsumerCallOffset = consumer.CallOffset;
            UpdateAdvancedRouteBundleContext();
            AdvancedRouteBundleStatus = RemoveActionSummary;
        }

        public void ApplyAdvancedRouteSecondCast()
        {
            if (!TryResolveAdvancedRouteBundleOperation(
                    out AiIndirectDispatchUnit _,
                    out AiAdvancedRouteConsumerVm consumerVm,
                    out AiIndirectDispatchConsumer consumer,
                    out AiDetectedAction action,
                    out int reselectIndex,
                    out string error))
            {
                AdvancedRouteBundleStatus = error;
                RemoveActionSummary = error;
                return;
            }

            AiCommandOption? ability = SelectedAdvancedRouteSecondAbility;
            if (ability == null)
            {
                AdvancedRouteBundleStatus = Strings.F2_choose_the_ability_for_the_second_cast_b_f96de57a;
                RemoveActionSummary = AdvancedRouteBundleStatus;
                return;
            }

            List<AiInstruction>? changed = AiAutomation.InsertSecondCommandAfterActionUsingActionTarget(
                selectedScript!,
                action,
                ability.Operand);
            if (changed == null)
            {
                AdvancedRouteBundleStatus =
                    string.Format(Strings.U_Ai_RouteSecondCastFailed, consumerVm.Title);
                RemoveActionSummary = AdvancedRouteBundleStatus;
                return;
            }

            string okMessage =
                string.Format(Strings.U_Ai_RouteSecondCastOk, consumerVm.Title, SelectedAdvancedPhaseUnit!.Title,
                    ability.Name, consumer.CallOffset);
            ApplyAndSave(changed, okMessage, reselectIndex);
            lastAdvancedRouteBundleConsumerCallOffset = consumer.CallOffset;
            UpdateAdvancedRouteBundleContext();
            AdvancedRouteBundleStatus = RemoveActionSummary;
        }

        bool TryResolveAdvancedRouteBundleOperation(
            out AiIndirectDispatchUnit unit,
            out AiAdvancedRouteConsumerVm consumerVm,
            out AiIndirectDispatchConsumer consumer,
            out AiDetectedAction action,
            out int reselectIndex,
            out string error)
        {
            unit = default!;
            consumerVm = default!;
            consumer = default!;
            action = default!;
            reselectIndex = -1;
            error = string.Empty;

            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                error = Strings.F2_select_a_monster_with_a_real_aifile_befo_8be7fca0;
                return false;
            }

            if (SelectedAdvancedPhaseUnit == null
                || !advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out unit))
            {
                error = Strings.F2_choose_a_valid_route_in_v2_before_applyi_acf4ef1a;
                return false;
            }

            AiAdvancedRouteConsumerVm? selectedConsumerVm = SelectedAdvancedRouteConsumer;
            consumerVm = selectedConsumerVm ?? default!;
            if (selectedConsumerVm == null)
            {
                error = Strings.F2_choose_a_consumer_from_the_route_before_e9585b13;
                return false;
            }

            int consumerCallOffset = selectedConsumerVm.CallOffset;
            consumer = unit.Consumers.FirstOrDefault(candidate => candidate.CallOffset == consumerCallOffset) ?? default!;
            if (consumer == null)
            {
                error = $"Nao reencontrei o consumer '{consumerVm.Title}' na rota atual.";
                return false;
            }

            action = AiAutomation.DetectActions(selectedScript)
                .FirstOrDefault(candidate =>
                    candidate.Kind == AiActionKind.Command
                    && candidate.CallOffset == consumerCallOffset) ?? default!;
            if (action == null)
            {
                error = string.Format(Strings.U_Ai_RouteIndirectPerformNotFound, consumerVm.Title);
                return false;
            }

            reselectIndex = AutomationActions
                .Select((vm, index) => new { vm, index })
                .FirstOrDefault(entry => entry.vm.Action.CallOffset == consumerCallOffset)?.index ?? -1;
            return true;
        }
    }

    internal sealed class AiAdvancedRouteConsumerVm
    {
        public AiAdvancedRouteConsumerVm(
            string title,
            string payloadSummary,
            string targetSummary,
            string callSummary,
            string technicalSummary,
            int callOffset,
            ushort commandVariableIndex,
            string commandVariableName,
            ushort targetVariableIndex,
            string targetVariableName)
        {
            Title = title;
            PayloadSummary = payloadSummary;
            TargetSummary = targetSummary;
            CallSummary = callSummary;
            TechnicalSummary = technicalSummary;
            CallOffset = callOffset;
            CommandVariableIndex = commandVariableIndex;
            CommandVariableName = commandVariableName;
            TargetVariableIndex = targetVariableIndex;
            TargetVariableName = targetVariableName;
        }

        public string Title { get; }
        public string PayloadSummary { get; }
        public string TargetSummary { get; }
        public string CallSummary { get; }
        public string TechnicalSummary { get; }
        public int CallOffset { get; }
        public ushort CommandVariableIndex { get; }
        public string CommandVariableName { get; }
        public ushort TargetVariableIndex { get; }
        public string TargetVariableName { get; }
    }
}
