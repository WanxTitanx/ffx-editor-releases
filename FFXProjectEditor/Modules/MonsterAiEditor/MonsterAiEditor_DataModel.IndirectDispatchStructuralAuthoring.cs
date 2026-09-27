using FFXProjectEditor.FfxLib.Ai;
using System;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        public bool CanCloneSelectedAdvancedPhaseUnit =>
            SelectedAdvancedPhaseUnit != null
            && SelectedAdvancedPhaseUnit.CanCloneRoute
            && advancedPhaseUnitsById.ContainsKey(SelectedAdvancedPhaseUnit.UnitId);

        public bool CloneSelectedAdvancedPhaseUnitRoute()
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedPhaseUnitsSummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (SelectedAdvancedPhaseUnit == null)
            {
                AdvancedPhaseUnitsSummary = Strings.F2_select_a_route_in_v2_before_duplicating__ec7ef6f6;
                return false;
            }

            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedPhaseUnitsSummary = Strings.F2_the_current_monster_is_not_ready_for_str_fb49d66c;
                return false;
            }

            if (!advancedPhaseUnitsById.TryGetValue(SelectedAdvancedPhaseUnit.UnitId, out AiIndirectDispatchUnit? sourceUnit))
            {
                AdvancedPhaseUnitsSummary = Strings.F2_the_selected_route_was_not_found_again_i_b2dc4073;
                return false;
            }

            if (!AiAutomation.TryCloneAndChainIndirectDispatchRoute(
                    selectedScript,
                    new AiIndirectDispatchRouteCloneRequest(sourceUnit.UnitId),
                    out AiIndirectDispatchRouteCloneResult? cloneResult,
                    out string error)
                || cloneResult == null)
            {
                AdvancedPhaseUnitsSummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                    cloneResult.EditedAiFileBytes,
                    selectedScript,
                    selectedScript.OriginalAiFileBytes.Length,
                    out _,
                    out string validationReason))
            {
                AdvancedPhaseUnitsSummary = $"Validacao estrutural bloqueou a clonagem: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, cloneResult.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedPhaseUnitsSummary = $"Save grow-aware abortado na clonagem estrutural: {ex.Message}";
                return false;
            }

            lastAdvancedCloneSourceUnitId = sourceUnit.UnitId;
            lastAdvancedCloneSourceUnitIndex = sourceUnit.UnitIndex;
            lastAdvancedCloneNewUnitId = cloneResult.NewUnitId;
            lastAdvancedCloneNewUnitIndex = cloneResult.NewRouteIndex;
            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();
            SelectedAdvancedPhaseUnit =
                AdvancedPhaseUnits.FirstOrDefault(unit => unit.UnitId.Equals(cloneResult.NewUnitId, StringComparison.OrdinalIgnoreCase))
                ?? SelectedAdvancedPhaseUnit;

            string summary =
                $"✅ {cloneResult.Summary} " +
                $"Novo unitId: {cloneResult.NewUnitId}. " +
                $"{cloneResult.ChangedBytes.Count} byte(s) mudaram no AiFile; save grow-aware com .prev.bak criado. " +
                $"{(reloaded ? "Readback ok." : "Readback parcial; use Refresh.")} Rota nova pronta para retocar habilidade/alvo/proximo passo. RT2 ainda recomendado.";

            AdvancedPhaseUnitsSummary = summary;
            AdvancedPhaseModeSummary =
                $"V2 avancado: clone estrutural aplicado para {cloneResult.NewUnitId}. " +
                Strings.F2_you_can_now_open_the_new_route_and_chang_62de1bbe;
            RemoveActionSummary = summary;
            return true;
        }
    }
}
