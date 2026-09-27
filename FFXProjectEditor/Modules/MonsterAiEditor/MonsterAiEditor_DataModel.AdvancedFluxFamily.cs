using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private string advancedFluxHostBeatSummary =
            "Seymour Flux host beat unavailable outside m142.";
        [ObservableProperty] private string advancedFluxCompanionFollowUpSummary =
            "Mortiorchis companion / follow-up unavailable outside m142.";
        [ObservableProperty] private string advancedFluxWriterScopeSummary =
            "Raw native thresholds unavailable outside m142.";

        public bool HasAdvancedFluxFamilySurface { get; private set; }

        void UpdateAdvancedFluxFamilyContext()
        {
            HasAdvancedFluxFamilySurface = false;

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 142)
            {
                AdvancedFluxHostBeatSummary = "Seymour Flux host beat unavailable outside m142.";
                AdvancedFluxCompanionFollowUpSummary = "Mortiorchis companion / follow-up unavailable outside m142.";
                AdvancedFluxWriterScopeSummary = "Raw native thresholds unavailable outside m142.";
                OnPropertyChanged(nameof(HasAdvancedFluxFamilySurface));
                return;
            }

            List<AiIndirectDispatchUnit> fluxUnits = advancedPhaseUnitsById.Values
                .Where(unit => unit.UnitId.StartsWith("preview-flux-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(unit => unit.UnitIndex)
                .ToList();

            if (fluxUnits.Count == 0)
            {
                AdvancedFluxHostBeatSummary =
                    "m142 in focus, but the live detector didn't close the preview-flux-* beats in this source.";
                AdvancedFluxCompanionFollowUpSummary =
                    "Without the host beats, V2 doesn't attempt to summarize Mortiorchis by approximation.";
                AdvancedFluxWriterScopeSummary =
                    "Raw native thresholds remain stuck to the Flux parse; without the host beats, the section doesn't load.";
                OnPropertyChanged(nameof(HasAdvancedFluxFamilySurface));
                return;
            }

            HasAdvancedFluxFamilySurface = true;

            AiIndirectDispatchUnit? lanceCycle = fluxUnits.FirstOrDefault(unit =>
                unit.UnitId.Contains("preview-flux-lance-cycle", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? dispelCross = fluxUnits.FirstOrDefault(unit =>
                unit.UnitId.Contains("preview-flux-dispel-cross", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? selfBuff = fluxUnits.FirstOrDefault(unit =>
                unit.UnitId.Contains("preview-flux-self-buff", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? antiAeon = fluxUnits.FirstOrDefault(unit =>
                unit.UnitId.Contains("preview-flux-anti-aeon", StringComparison.OrdinalIgnoreCase));

            var hostBeatParts = new List<string>();
            if (lanceCycle != null)
                hostBeatParts.Add($"Lance cycle: {lanceCycle.GuardSummary}");
            if (dispelCross != null)
                hostBeatParts.Add($"Dispel/Cross window: {dispelCross.GuardSummary}");
            if (antiAeon != null)
                hostBeatParts.Add("Contextual anti-aeon still lives as an auxiliary host beat.");
            AdvancedFluxHostBeatSummary =
                hostBeatParts.Count == 0
                    ? Strings.U_Ai_FluxHostBeatNotMaterialized
                    : Strings.U_Ai_FluxHostBeatPrefix + string.Join(" ", hostBeatParts);

            var companionNotes = fluxUnits
                .SelectMany(unit => unit.CompanionEffects)
                .Where(note =>
                    note.Contains("Mortiorchis", StringComparison.OrdinalIgnoreCase)
                    || note.Contains("Cross Cleave", StringComparison.OrdinalIgnoreCase)
                    || note.Contains("Full-Life", StringComparison.OrdinalIgnoreCase)
                    || note.Contains("companheiro", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            AdvancedFluxCompanionFollowUpSummary =
                companionNotes.Count == 0
                    ? "Mortiorchis companion / follow-up: the host remains coupled to the companion, but the fine summary has no extra notes in this parse."
                    : "Companion / follow-up do Mortiorchis: " + string.Join(" ", companionNotes);

            string thresholdHeadline = selfBuff == null
                ? "Raw native thresholds: the self-buff beat was not materialized in this reading."
                : Strings.U_Ai_FluxRawThresholds;
            AdvancedFluxWriterScopeSummary =
                thresholdHeadline +
                Strings.U_Ai_FluxWriterRowOnly;

            OnPropertyChanged(nameof(HasAdvancedFluxFamilySurface));
        }
    }
}
