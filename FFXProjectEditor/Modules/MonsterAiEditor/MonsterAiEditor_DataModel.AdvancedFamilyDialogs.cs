using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        public bool TryBuildAdvancedFamilySurfaceDialog(
            string familyKey,
            out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
        {
            if (familyKey.Equals(AdvancedFamilySurfaceKeys.Anima, StringComparison.OrdinalIgnoreCase))
                return TryBuildAdvancedAnimaFamilyDialogSnapshot(out snapshot);

            if (familyKey.StartsWith(AdvancedFamilySurfaceKeys.GenericUnitPreviewPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string unitId = familyKey[AdvancedFamilySurfaceKeys.GenericUnitPreviewPrefix.Length..];
                return TryBuildAdvancedGenericUnitDialogSnapshot(unitId, out snapshot);
            }

            snapshot = familyKey switch
            {
                AdvancedFamilySurfaceKeys.ElementalCluster => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Omnis - elemental cluster",
                    "MONSTER AI - M131 - ELEMENTAL CLUSTER",
                    AdvancedElementalClusterSummary,
                    AdvancedElementalClusterHonestSummary,
                    AdvancedElementalClusterCoverage,
                    AdvancedElementalClusterApplySummary,
                    AdvancedElementalClusterRows.ToList()),
                AdvancedFamilySurfaceKeys.SupportAccumulator => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Mortibody - support accumulator",
                    "MONSTER AI - M127 - SUPPORT ACCUMULATOR",
                    AdvancedSupportAccumulatorSummary,
                    AdvancedSupportAccumulatorHonestSummary,
                    AdvancedSupportAccumulatorCoverage,
                    AdvancedSupportAccumulatorApplySummary,
                    AdvancedSupportAccumulatorRows.ToList()),
                AdvancedFamilySurfaceKeys.Mortiorchis => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Mortiorchis - companion contextual",
                    "MONSTER AI - M143 - COMPANION CONTEXTUAL",
                    AdvancedMortiorchisSummary,
                    AdvancedMortiorchisHonestSummary,
                    AdvancedMortiorchisCoverage,
                    AdvancedMortiorchisApplySummary,
                    AdvancedMortiorchisRows.ToList()),
                AdvancedFamilySurfaceKeys.ReactiveSensor => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Reactive sensor",
                    "MONSTER AI - REACTIVE SENSOR",
                    AdvancedReactiveSensorSummary,
                    AdvancedReactiveSensorHonestSummary,
                    AdvancedReactiveSensorCoverage,
                    AdvancedReactiveSensorApplySummary,
                    AdvancedReactiveSensorRows.ToList()),
                AdvancedFamilySurfaceKeys.RoundScriptedBoss => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "m238 - round-scripted-boss",
                    "MONSTER AI - M238 - ROUND-SCRIPTED-BOSS",
                    AdvancedRoundScriptedBossSummary,
                    AdvancedRoundScriptedBossHonestSummary,
                    AdvancedRoundScriptedBossCoverage,
                    AdvancedRoundScriptedBossApplySummary,
                    AdvancedRoundScriptedBossRows.ToList()),
                AdvancedFamilySurfaceKeys.TonberryCameraRouting => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Tonberry - camera routing boss",
                    "MONSTER AI - TONBERRY CAMERA ROUTING",
                    AdvancedTonberryCameraRoutingSummary,
                    AdvancedTonberryCameraRoutingHonestSummary,
                    AdvancedTonberryCameraRoutingCoverage,
                    AdvancedTonberryCameraRoutingApplySummary,
                    AdvancedTonberryCameraRoutingRows.ToList()),
                AdvancedFamilySurfaceKeys.EncounterKeyedAppearDisable => BuildAdvancedFamilySurfaceDialogSnapshot(
                    familyKey,
                    "Encounter-keyed appear / disable",
                    "MONSTER AI - M211 - ENCOUNTER-KEYED APPEAR / DISABLE",
                    AdvancedEncounterAppearSummary,
                    AdvancedEncounterAppearHonestSummary,
                    AdvancedEncounterAppearCoverage,
                    AdvancedEncounterAppearApplySummary,
                    AdvancedEncounterAppearRows.ToList()),
                _ => null,
            };

            return snapshot != null;
        }

        public bool TryBuildAdvancedFamilySurfaceDialogForUnit(
            string? unitId,
            out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(unitId)
                || !advancedPhaseUnitsById.TryGetValue(unitId, out AiIndirectDispatchUnit? unit))
            {
                return false;
            }

            string? familyKey = ResolveAdvancedFamilySurfaceKeyForUnit(unitId);
            if (familyKey != null && TryBuildAdvancedFamilySurfaceDialog(familyKey, out snapshot))
                return true;

            return unit.CapabilityTier != AiIndirectDispatchCapabilityTier.AuthoringCandidate
                && TryBuildAdvancedGenericUnitDialogSnapshot(unitId, out snapshot);
        }

        string? ResolveAdvancedFamilySurfaceKeyForUnit(string unitId)
        {
            if (unitId.Equals("dispatch-support-0008-0007-0", StringComparison.OrdinalIgnoreCase)
                && TryGetSelectedMonsterNumber(out int monsterNumber)
                && monsterNumber == 125)
            {
                return AdvancedFamilySurfaceKeys.Anima;
            }

            if (unitId.StartsWith("preview-omnis-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.ElementalCluster;
            if (unitId.StartsWith("preview-mortibody-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.SupportAccumulator;
            if (unitId.StartsWith("preview-mortiorchis-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.Mortiorchis;
            if (unitId.StartsWith("preview-reactive-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.ReactiveSensor;
            if (unitId.StartsWith("preview-round-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.RoundScriptedBoss;
            if (unitId.StartsWith("preview-tonberry-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.TonberryCameraRouting;
            if (unitId.StartsWith("preview-encounter-", StringComparison.OrdinalIgnoreCase))
                return AdvancedFamilySurfaceKeys.EncounterKeyedAppearDisable;

            return null;
        }

        bool TryBuildAdvancedGenericUnitDialogSnapshot(
            string unitId,
            out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
        {
            snapshot = null;
            if (!advancedPhaseUnitsById.TryGetValue(unitId, out AiIndirectDispatchUnit? unit))
                return false;

            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots =
                AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers);
            List<AiAdvancedFamilySurfaceRowVm> rows = new()
            {
                new AiAdvancedFamilySurfaceRowVm(
                    "Payload",
                    BuildGenericPayloadPreview(unit),
                    BuildGenericPayloadDetail(unit),
                    BuildGenericOffsetPreview(unit.PayloadWrites.Select(write => write.Offset)),
                    string.Empty,
                    null),
                new AiAdvancedFamilySurfaceRowVm(
                    "Alvo",
                    BuildGenericTargetPreview(targetSlots),
                    BuildGenericTargetDetail(targetSlots),
                    BuildGenericOffsetPreview(targetSlots.Select(slot => slot.SourceInstructionOffset)),
                    string.Empty,
                    null),
                new AiAdvancedFamilySurfaceRowVm(
                    "Consumers",
                    BuildGenericConsumerPreview(unit),
                    BuildGenericConsumerDetail(unit),
                    BuildGenericOffsetPreview(unit.Consumers.Select(consumer => consumer.CallOffset)),
                    string.Empty,
                    null),
                new AiAdvancedFamilySurfaceRowVm(
                    "Next-state",
                    string.IsNullOrWhiteSpace(unit.NextStateSummary) ? "sem next-state legivel" : unit.NextStateSummary,
                    BuildGenericNextStateDetail(unit),
                    unit.EditableNextStateOffset.HasValue ? $"0x{unit.EditableNextStateOffset.Value:X4}" : "-",
                    string.Empty,
                    null),
            };

            string familyLabel = DescribeAdvancedFamilyLabel(
                unit,
                unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
            string windowLabel = unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                ? $"rota {unit.UnitIndex}"
                : $"pacote {unit.UnitIndex}";
            snapshot = BuildAdvancedFamilySurfaceDialogSnapshot(
                $"{AdvancedFamilySurfaceKeys.GenericUnitPreviewPrefix}{unit.UnitId}",
                $"{windowLabel} - {familyLabel}",
                $"MONSTER AI - {windowLabel.ToUpperInvariant()}",
                $"{unit.CapabilityLabel} · {unit.GuardSummary}",
                unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                    ? Strings.U_Ai_FamilyDialogsHonestFallback
                    : "Honest fallback of the current package: payload, target, consumers and next-state remain clickable even when the family is still in PreviewReadOnly.",
                unit.OffsetSummary,
                unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                    ? Strings.F2_use_this_fallback_to_review_the_entire_r_bb75ba51
                    : Strings.F2_use_this_fallback_to_open_the_full_packa_f3fa0395,
                rows);
            return snapshot != null;
        }

        static string BuildGenericPayloadPreview(AiIndirectDispatchUnit unit)
        {
            IReadOnlyList<AiIndirectDispatchWrite> payloadWrites = unit.PayloadWrites
                .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
            return payloadWrites.Count == 0
                ? "sem payload legivel"
                : string.Join(" | ", payloadWrites.Select(write => write.ValueSummary));
        }

        static string BuildGenericPayloadDetail(AiIndirectDispatchUnit unit)
        {
            IReadOnlyList<string> parts = unit.PayloadWrites
                .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(write => $"{write.RoleSummary}: {write.VariableName} <- {write.ValueSummary}")
                .ToList();
            return parts.Count == 0
                ? "No indirect payload was readable in this package."
                : string.Join(" · ", parts);
        }

        static string BuildGenericTargetPreview(IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots) =>
            targetSlots.Count == 0
                ? "sem alvo legivel"
                : string.Join(" | ", targetSlots.Take(3).Select(slot => $"{slot.VariableName} <- {slot.ValueSummary}"));

        static string BuildGenericTargetDetail(IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots)
        {
            IReadOnlyList<string> parts = targetSlots
                .Take(4)
                .Select(slot => $"{slot.SlotLabel}: {slot.DetailSummary}")
                .ToList();
            return parts.Count == 0
                ? "No target slot was materialized in this package."
                : string.Join(" · ", parts);
        }

        static string BuildGenericConsumerPreview(AiIndirectDispatchUnit unit) =>
            unit.Consumers.Count == 0
                ? "sem consumer legivel"
                : string.Join(" | ", unit.Consumers.Take(3).Select(consumer => consumer.Label));

        static string BuildGenericConsumerDetail(AiIndirectDispatchUnit unit)
        {
            IReadOnlyList<string> parts = unit.Consumers
                .Take(4)
                .Select(consumer => $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName}")
                .ToList();
            return parts.Count == 0
                ? "No indirect performCommand was materialized in this package."
                : string.Join(" · ", parts);
        }

        static string BuildGenericNextStateDetail(AiIndirectDispatchUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit.NextStateSummary))
                return unit.NextStateSummary;

            return unit.EditableNextStateOffset.HasValue
                ? string.Format(Strings.U_Ai_FamilyDialogsNextStateNoHeadline, unit.EditableNextStateOffset.Value)
                : "No next-state authoring was proven in this package.";
        }

        static string BuildGenericOffsetPreview(IEnumerable<int> offsets)
        {
            List<string> values = offsets
                .Where(offset => offset >= 0)
                .Distinct()
                .Take(4)
                .Select(offset => $"0x{offset:X4}")
                .ToList();
            return values.Count == 0 ? "-" : string.Join(" | ", values);
        }

        static AiAdvancedFamilySurfaceDialogSnapshot? BuildAdvancedFamilySurfaceDialogSnapshot(
            string familyKey,
            string windowTitle,
            string headerLabel,
            string summary,
            string honestSummary,
            string coverage,
            string applySummary,
            IReadOnlyList<AiAdvancedFamilySurfaceRowVm> rows)
        {
            if (rows.Count == 0)
                return null;

            return new AiAdvancedFamilySurfaceDialogSnapshot(
                familyKey,
                windowTitle,
                headerLabel,
                summary,
                honestSummary,
                coverage,
                applySummary,
                rows);
        }
    }

    internal static class AdvancedFamilySurfaceKeys
    {
        public const string Anima = "anima-payload-picker";
        public const string ElementalCluster = "elemental-cluster";
        public const string SupportAccumulator = "support-accumulator";
        public const string Mortiorchis = "mortiorchis-companion";
        public const string ReactiveSensor = "reactive-sensor";
        public const string RoundScriptedBoss = "round-scripted-boss";
        public const string TonberryCameraRouting = "tonberry-camera-routing";
        public const string EncounterKeyedAppearDisable = "encounter-keyed-appear-disable";
        public const string GenericUnitPreviewPrefix = "generic-unit-preview:";
    }

    internal sealed record AiAdvancedFamilySurfaceDialogSnapshot(
        string FamilyKey,
        string WindowTitle,
        string HeaderLabel,
        string Summary,
        string HonestSummary,
        string Coverage,
        string ApplySummary,
        IReadOnlyList<AiAdvancedFamilySurfaceRowVm> Rows);
}
