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
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedSupportAccumulatorRows = new();
        readonly ObservableCollection<AiAdvancedMortibodyAccumulatorEvidenceVm> advancedMortibodyAccumulatorEvidenceRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedSupportAccumulatorRows => advancedSupportAccumulatorRows;
        public ObservableCollection<AiAdvancedMortibodyAccumulatorEvidenceVm> AdvancedMortibodyAccumulatorEvidenceRows => advancedMortibodyAccumulatorEvidenceRows;

        [ObservableProperty] private string advancedSupportAccumulatorSummary =
            "No dedicated surface for the support accumulator on the current monster.";
        [ObservableProperty] private string advancedSupportAccumulatorHonestSummary =
            "When m127 closes in the live detector, the V2 shows the score/accumulator layer and only opens the indirect editor proven in the real row-only excerpts. It is not Seymour clone nor universal writer.";
        [ObservableProperty] private string advancedSupportAccumulatorCoverage =
            "Dedicated surface disarmed outside m127.";
        [ObservableProperty] private string advancedSupportAccumulatorApplySummary =
            "No narrow lane of the support accumulator armed in the current monster.";
        [ObservableProperty] private string advancedMortibodyAccumulatorWriterApplySummary =
            "Writer narrow of Mortibody unavailable outside m127.";

        public bool HasAdvancedSupportAccumulatorSurface => AdvancedSupportAccumulatorRows.Count > 0;
        public bool HasAdvancedMortibodyAccumulatorEvidence => AdvancedMortibodyAccumulatorEvidenceRows.Count > 0;

        void UpdateAdvancedSupportAccumulatorContext()
        {
            AdvancedSupportAccumulatorRows.Clear();
            AdvancedMortibodyAccumulatorEvidenceRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 127)
            {
                AdvancedSupportAccumulatorSummary = "No dedicated surface of the support accumulator in the current monster.";
                AdvancedSupportAccumulatorHonestSummary =
                    "When m127 closes in the live detector, V2 shows the score/accumulator layer and only opens the indirect editor proven in the real row-only snippets. It's not a Seymour clone nor universal writer.";
                AdvancedSupportAccumulatorCoverage = "Dedicated surface disarmed outside m127.";
                AdvancedSupportAccumulatorApplySummary =
                    "No narrow lane of the support accumulator armed in the current monster.";
                RaiseAdvancedSupportAccumulatorProperties();
                return;
            }

            AiIndirectDispatchUnit? supportSwitchUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-mortibody-support-switch", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? absorptionUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-mortibody-absorption", StringComparison.OrdinalIgnoreCase));

            if (supportSwitchUnit == null || absorptionUnit == null)
            {
                AdvancedSupportAccumulatorSummary =
                    "m127 in focus, but the preview-mortibody-* package did not close completely in this live source.";
                AdvancedSupportAccumulatorHonestSummary =
                    "Without the two proven beats, V2 does not bring up the dedicated surface. The m290 collision remains blocked outside this card.";
                AdvancedSupportAccumulatorCoverage =
                    "The dedicated surface of Mortibody only opens on m127 when support-switch and absorption appear together.";
                AdvancedSupportAccumulatorApplySummary =
                    Strings.U_Ai_SupportNoLane;
                RaiseAdvancedSupportAccumulatorProperties();
                return;
            }

            AddAdvancedSupportAccumulatorRow(
                supportSwitchUnit,
                "Support switch",
                Strings.U_Ai_SupportOpenSwitchPopup);
            AddAdvancedSupportAccumulatorRow(
                absorptionUnit,
                "Absorption follow-up",
                Strings.U_Ai_SupportOpenFollowupPopup);

            AdvancedSupportAccumulatorSummary =
                "Support accumulator of Mortibody: the score of Shell/Haste/Nul* and the follow-up of absorption became separated as proper lanes.";
            AdvancedSupportAccumulatorHonestSummary =
                "The dedicated surface exists only for m127. m290 continues to be treated as collision/sub-actor and does not inherit this card until reconciliation closes.";
            AdvancedSupportAccumulatorCoverage =
                $"{supportSwitchUnit.UnitId} · {absorptionUnit.UnitId}. {supportSwitchUnit.GuardSummary}";
            AdvancedSupportAccumulatorApplySummary =
                AdvancedSupportAccumulatorRows.Any(row => row.CanOpenEditor)
                    ? "The narrow popup only opens in the already proven row-only snippet. The score/accumulator layer and the rest of the state machine remain explained, not universalized."
                    : "The m127 beats became visible, but no row-only snippet opened a popup in this reading.";

            UpdateAdvancedMortibodyAccumulatorWriterContext();
            RaiseAdvancedSupportAccumulatorProperties();
        }

        void UpdateAdvancedMortibodyAccumulatorWriterContext()
        {
            AdvancedMortibodyAccumulatorEvidenceRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedMortibodyAccumulatorEvidence));
                return;
            }

            if (!AiMortibodySupportAccumulatorWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiMortibodyAccumulatorDescriptor> descriptors, out _))
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.U_Ai_SupportAccumulatorsNotProven;
                OnPropertyChanged(nameof(HasAdvancedMortibodyAccumulatorEvidence));
                return;
            }

            foreach (AiMortibodyAccumulatorDescriptor descriptor in descriptors)
                AdvancedMortibodyAccumulatorEvidenceRows.Add(new AiAdvancedMortibodyAccumulatorEvidenceVm(descriptor));

            AdvancedMortibodyAccumulatorWriterApplySummary = AdvancedMortibodyAccumulatorEvidenceRows.Count > 0
                ? string.Format(Strings.U_Ai_SupportWriterArmed, AdvancedMortibodyAccumulatorEvidenceRows.Count)
                : Strings.U_Ai_SupportAccumulatorsNotProven;
            OnPropertyChanged(nameof(HasAdvancedMortibodyAccumulatorEvidence));
        }

        public bool ApplyAdvancedMortibodyAccumulatorEdit(AiAdvancedMortibodyAccumulatorEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedMortibodyAccumulatorEvidenceRows.Contains(row))
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_e24d81cf;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.ScoreText, out ushort newScore))
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiMortibodyAccumulatorPatchRequest(
                row.VariableName,
                row.ScoreInstructionOffset,
                row.CurrentScoreValue,
                newScore);

            if (!AiMortibodySupportAccumulatorWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiMortibodyAccumulatorEditResult? result, out string error) || result == null)
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedMortibodyAccumulatorWriterApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.VariableName} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedMortibodyAccumulatorWriterApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        void AddAdvancedSupportAccumulatorRow(
            AiIndirectDispatchUnit unit,
            string title,
            string openButtonLabel)
        {
            AdvancedSupportAccumulatorRows.Add(new AiAdvancedFamilySurfaceRowVm(
                title,
                BuildAdvancedFamilyUnitSubtitle(unit),
                BuildAdvancedFamilyUnitDetail(unit),
                unit.OffsetSummary,
                openButtonLabel,
                TryBuildAdvancedPreferredUnitLaunchContext(unit, title)));
        }

        void RaiseAdvancedSupportAccumulatorProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedSupportAccumulatorSurface));
            OnPropertyChanged(nameof(HasAdvancedMortibodyAccumulatorEvidence));
        }
    }

    internal sealed partial class AiAdvancedMortibodyAccumulatorEvidenceVm : ObservableObject
    {
        public AiAdvancedMortibodyAccumulatorEvidenceVm(AiMortibodyAccumulatorDescriptor descriptor)
        {
            VariableName = descriptor.VariableName;
            RoleLabel = descriptor.RoleLabel;
            CurrentScoreValue = descriptor.CurrentScoreValue;
            ScoreInstructionOffset = descriptor.ScoreInstructionOffset;
            CurrentScoreSummary = descriptor.CurrentScoreSummary;
            WriterSummary = $"Writer experimental: patch raw em 0x{descriptor.ScoreInstructionOffset:X4} (score {descriptor.CurrentScoreValue}).";
            ScoreText = descriptor.CurrentScoreValue.ToString();
        }

        public string VariableName { get; }
        public string RoleLabel { get; }
        public ushort CurrentScoreValue { get; }
        public int ScoreInstructionOffset { get; }
        public string CurrentScoreSummary { get; }
        public string WriterSummary { get; }

        [ObservableProperty] private string scoreText = string.Empty;
    }
}
