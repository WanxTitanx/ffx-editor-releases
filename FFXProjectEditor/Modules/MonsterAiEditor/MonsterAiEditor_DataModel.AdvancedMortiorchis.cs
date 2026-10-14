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
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedMortiorchisRows = new();
        readonly ObservableCollection<AiAdvancedMortiorchisCompanionEvidenceVm> advancedMortiorchisCompanionEvidenceRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedMortiorchisRows => advancedMortiorchisRows;
        public ObservableCollection<AiAdvancedMortiorchisCompanionEvidenceVm> AdvancedMortiorchisCompanionEvidenceRows => advancedMortiorchisCompanionEvidenceRows;

        [ObservableProperty] private string advancedMortiorchisSummary =
            Strings.U_Ai_MortiorchisNoSurface;
        [ObservableProperty] private string advancedMortiorchisHonestSummary =
            Strings.U_Ai_MortiorchisHonestSummary;
        [ObservableProperty] private string advancedMortiorchisCoverage =
            Strings.U_Ai_MortiorchisDisarmed;
        [ObservableProperty] private string advancedMortiorchisApplySummary =
            Strings.U_Ai_MortiorchisNoLane;
        [ObservableProperty] private string advancedMortiorchisCompanionWriterApplySummary =
            "Writer narrow do Mortiorchis indisponivel fora do m143.";

        public bool HasAdvancedMortiorchisSurface => AdvancedMortiorchisRows.Count > 0;
        public bool HasAdvancedMortiorchisCompanionEvidence => AdvancedMortiorchisCompanionEvidenceRows.Count > 0;

        void UpdateAdvancedMortiorchisContext()
        {
            AdvancedMortiorchisRows.Clear();
            AdvancedMortiorchisCompanionEvidenceRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 143)
            {
                AdvancedMortiorchisSummary = Strings.U_Ai_MortNoSurface;
                AdvancedMortiorchisHonestSummary = Strings.U_Ai_MortHonestNoFocus;
                AdvancedMortiorchisCoverage = Strings.U_Ai_MortDisarmed;
                AdvancedMortiorchisApplySummary = Strings.U_Ai_MortNoNarrowLane;
                RaiseAdvancedMortiorchisProperties();
                return;
            }

            UpdateAdvancedMortiorchisCompanionWriterContext();

            AiIndirectDispatchUnit? bodyUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-mortiorchis-body-handoff", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? absorptionUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-mortiorchis-absorption", StringComparison.OrdinalIgnoreCase));

            if (bodyUnit == null || absorptionUnit == null)
            {
                AdvancedMortiorchisSummary = Strings.U_Ai_MortPackageNotClosed;
                AdvancedMortiorchisHonestSummary = Strings.U_Ai_MortNoBothBeats;
                AdvancedMortiorchisCoverage = Strings.U_Ai_MortSurfaceTogether;
                AdvancedMortiorchisApplySummary = Strings.U_Ai_MortNoLaneHere;
                RaiseAdvancedMortiorchisProperties();
                return;
            }

            AdvancedMortiorchisRows.Add(BuildAdvancedFamilySurfaceRow(
                bodyUnit,
                "Body handoff",
                Strings.U_Ai_MortOpenBodyPopup));
            AdvancedMortiorchisRows.Add(BuildAdvancedFamilySurfaceRow(
                absorptionUnit,
                "Mortibsorption follow-up",
                Strings.U_Ai_MortOpenFollowupPopup));

            AdvancedMortiorchisSummary = Strings.U_Ai_MortCompanionContext;
            AdvancedMortiorchisHonestSummary = Strings.U_Ai_MortPopupNarrow;
            AdvancedMortiorchisCoverage =
                $"{bodyUnit.UnitId} | {absorptionUnit.UnitId}. {absorptionUnit.GuardSummary}";
            AdvancedMortiorchisApplySummary =
                AdvancedMortiorchisRows.Any(row => row.CanOpenEditor)
                    ? Strings.U_Ai_MortPromotedBeats
                    : Strings.U_Ai_MortNoPopupRead;

            RaiseAdvancedMortiorchisProperties();
        }

        void UpdateAdvancedMortiorchisCompanionWriterContext()
        {
            AdvancedMortiorchisCompanionEvidenceRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedMortiorchisCompanionEvidence));
                return;
            }

            if (!AiMortiorchisCompanionWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiMortiorchisCompanionDescriptor> descriptors, out _))
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.U_Ai_MortiorchisCompanionNotProven;
                OnPropertyChanged(nameof(HasAdvancedMortiorchisCompanionEvidence));
                return;
            }

            foreach (AiMortiorchisCompanionDescriptor descriptor in descriptors)
                AdvancedMortiorchisCompanionEvidenceRows.Add(new AiAdvancedMortiorchisCompanionEvidenceVm(descriptor));

            AdvancedMortiorchisCompanionWriterApplySummary = AdvancedMortiorchisCompanionEvidenceRows.Count > 0
                ? string.Format(Strings.U_Ai_MortiorchisWriterArmed, AdvancedMortiorchisCompanionEvidenceRows.Count)
                : Strings.U_Ai_MortiorchisCompanionNotProven;
            OnPropertyChanged(nameof(HasAdvancedMortiorchisCompanionEvidence));
        }

        public bool ApplyAdvancedMortiorchisCompanionEdit(AiAdvancedMortiorchisCompanionEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedMortiorchisCompanionEvidenceRows.Contains(row))
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.U_Ai_MortNeedAiFile;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.CommandText, out ushort newCommand))
            {
                AdvancedMortiorchisCompanionWriterApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiMortiorchisCompanionPatchRequest(
                row.BeatName,
                row.CommandInstructionOffset,
                row.CurrentCommand,
                newCommand,
                row.GateWriteOffset,
                row.CurrentGateValue,
                row.CurrentGateValue);

            if (!AiMortiorchisCompanionWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiMortiorchisCompanionEditResult? result, out string error) || result == null)
            {
                AdvancedMortiorchisCompanionWriterApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedMortiorchisCompanionWriterApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedMortiorchisCompanionWriterApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.BeatName} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedMortiorchisCompanionWriterApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        void RaiseAdvancedMortiorchisProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedMortiorchisSurface));
            OnPropertyChanged(nameof(HasAdvancedMortiorchisCompanionEvidence));
        }
    }

    internal sealed partial class AiAdvancedMortiorchisCompanionEvidenceVm : ObservableObject
    {
        public AiAdvancedMortiorchisCompanionEvidenceVm(AiMortiorchisCompanionDescriptor descriptor)
        {
            BeatName = descriptor.BeatName;
            RoleLabel = descriptor.RoleLabel;
            CurrentCommand = descriptor.CurrentCommand;
            CommandInstructionOffset = descriptor.CommandInstructionOffset;
            CurrentCommandSummary = descriptor.CurrentCommandSummary;
            GateWriteOffset = descriptor.GateWriteOffset;
            CurrentGateValue = descriptor.CurrentGateValue;
            WriterSummary = $"Writer experimental: patch raw em 0x{descriptor.CommandInstructionOffset:X4} (comando 0x{descriptor.CurrentCommand:X4}).";
            CommandText = $"0x{descriptor.CurrentCommand:X4}";
        }

        public string BeatName { get; }
        public string RoleLabel { get; }
        public ushort CurrentCommand { get; }
        public int CommandInstructionOffset { get; }
        public string CurrentCommandSummary { get; }
        public int? GateWriteOffset { get; }
        public ushort? CurrentGateValue { get; }
        public string WriterSummary { get; }

        [ObservableProperty] private string commandText = string.Empty;
    }
}
