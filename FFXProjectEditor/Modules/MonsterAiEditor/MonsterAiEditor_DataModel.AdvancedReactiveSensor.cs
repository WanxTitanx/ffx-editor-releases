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
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedReactiveSensorRows = new();
        readonly ObservableCollection<AiAdvancedReactiveSensorEvidenceVm> advancedReactiveSensorEvidenceRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedReactiveSensorRows => advancedReactiveSensorRows;
        public ObservableCollection<AiAdvancedReactiveSensorEvidenceVm> AdvancedReactiveSensorEvidenceRows => advancedReactiveSensorEvidenceRows;

        [ObservableProperty] private string advancedReactiveSensorSummary =
            "No dedicated surface for the reactive sensor on the current monster.";
        [ObservableProperty] private string advancedReactiveSensorHonestSummary =
            "When the reactive family closes, V2 raises its own card for the sensor and only releases next-state when the structural literal is proven. Property read, topology and scene call remain outside.";
        [ObservableProperty] private string advancedReactiveSensorCoverage =
            "Disarmed dedicated surface outside the supported reactive subgroup.";
        [ObservableProperty] private string advancedReactiveSensorApplySummary =
            "No armed reactive sensor narrow lane on the current monster.";
        [ObservableProperty] private string advancedReactiveSensorWriterApplySummary =
            "Writer narrow do reactive sensor indisponivel.";

        public bool HasAdvancedReactiveSensorSurface => AdvancedReactiveSensorRows.Count > 0;
        public bool HasAdvancedReactiveSensorEvidence => AdvancedReactiveSensorEvidenceRows.Count > 0;

        void UpdateAdvancedReactiveSensorContext()
        {
            AdvancedReactiveSensorRows.Clear();
            AdvancedReactiveSensorEvidenceRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber is not (106 or 118 or 150 or 154))
            {
                AdvancedReactiveSensorSummary = "No dedicated reactive sensor surface on the current monster.";
                AdvancedReactiveSensorHonestSummary =
                    "When the reactive family closes, V2 raises its own card for the sensor and only releases next-state when the structural literal is proven. Property read, topology and scene call remain outside.";
                AdvancedReactiveSensorCoverage = "Disarmed dedicated surface outside the supported reactive subgroup.";
                AdvancedReactiveSensorApplySummary =
                    "No armed reactive sensor narrow lane on the current monster.";
                RaiseAdvancedReactiveSensorProperties();
                return;
            }

            AiIndirectDispatchUnit? unit = advancedPhaseUnitsById.Values.FirstOrDefault(candidate =>
                candidate.UnitId.StartsWith("preview-reactive-", StringComparison.OrdinalIgnoreCase));
            if (unit == null)
            {
                AdvancedReactiveSensorSummary =
                    string.Format(Strings.U_Ai_ReactiveInFocus, monsterNumber);
                AdvancedReactiveSensorHonestSummary =
                    "Without the proven reactive package, V2 does not invent its own card or narrow writer.";
                AdvancedReactiveSensorCoverage =
                    "The mature subgroup (m118/m150/m154) only raises when the detector finds the sensor and the aftermath literal.";
                AdvancedReactiveSensorApplySummary =
                    Strings.U_Ai_ReactiveNoPackage;
                RaiseAdvancedReactiveSensorProperties();
                return;
            }

            string title = unit.PayloadWrites
                .Select(write => write.RoleSummary)
                .FirstOrDefault(role => role.StartsWith("ultimo ", StringComparison.OrdinalIgnoreCase))
                ?? Strings.U_Ai_ReactiveSensorLabel;
            string roleLabel = unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
                ? Strings.U_Ai_ReactiveOpenNextStatePopup
                : "Leitura read-only";
            AiIndirectDispatchEditorLaunchContext? launchContext = unit.EditableNextStateOffset.HasValue
                ? BuildAdvancedEditorLaunchContext(
                    unit,
                    unit.PayloadWrites.FirstOrDefault()?.VariableName ?? "sensor reativo",
                    AiIndirectDispatchEditorFocusKind.NextState,
                    "estado reativo",
                    "next-state",
                    unit.EditableNextStateOffset.Value)
                : null;

            AdvancedReactiveSensorRows.Add(new AiAdvancedFamilySurfaceRowVm(
                title,
                BuildAdvancedFamilyUnitSubtitle(unit),
                BuildAdvancedFamilyUnitDetail(unit),
                unit.OffsetSummary,
                roleLabel,
                launchContext));

            bool matureSubgroup = unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate;
            AdvancedReactiveSensorSummary = matureSubgroup
                ? "Mature reactive sensor: the damageFormula subgroup now gains its own card and opens the next-state narrow when the structural literal closes."
                : "Reactive sensor in reconciliation: the damageType subgroup appears here as a dedicated read, but remains read-only.";
            AdvancedReactiveSensorHonestSummary = matureSubgroup
                ? "The writer remains narrow and family-specific: only the aftermath/state literal becomes a popup. readMoveProperty, usedCommand, topology and scene call remain explained, not authored."
                : "m106 remains outside the promotion path authoring. The dedicated card here only documents the sensor and aftermath, without promising apply.";
            AdvancedReactiveSensorCoverage =
                $"{unit.UnitId} · {unit.GuardSummary} · {unit.NextStateSummary}";
            AdvancedReactiveSensorApplySummary = launchContext != null
                ? "The popup opens focused on the proven next-state of this sensor. Outside this literal, the family remains structural/read-only."
                : "This sensor still has no narrow popup: the reconciliation of the less mature subgroup has not yet closed.";

            UpdateAdvancedReactiveSensorWriterContext();
            RaiseAdvancedReactiveSensorProperties();
        }

        void UpdateAdvancedReactiveSensorWriterContext()
        {
            AdvancedReactiveSensorEvidenceRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedReactiveSensorEvidence));
                return;
            }

            if (!AiReactiveSensorWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiReactiveSensorDescriptor> descriptors, out _))
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.U_Ai_ReactiveSensorNotProven;
                OnPropertyChanged(nameof(HasAdvancedReactiveSensorEvidence));
                return;
            }

            foreach (AiReactiveSensorDescriptor descriptor in descriptors)
                AdvancedReactiveSensorEvidenceRows.Add(new AiAdvancedReactiveSensorEvidenceVm(descriptor));

            AdvancedReactiveSensorWriterApplySummary = AdvancedReactiveSensorEvidenceRows.Count > 0
                ? string.Format(Strings.U_Ai_ReactiveWriterArmed, AdvancedReactiveSensorEvidenceRows.Count)
                : Strings.U_Ai_ReactiveSensorNotProven;
            OnPropertyChanged(nameof(HasAdvancedReactiveSensorEvidence));
        }

        public bool ApplyAdvancedReactiveSensorEdit(AiAdvancedReactiveSensorEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedReactiveSensorEvidenceRows.Contains(row))
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_380c1d75;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.StateText, out ushort newStateValue))
            {
                AdvancedReactiveSensorWriterApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiReactiveSensorPatchRequest(
                row.VariableName,
                row.StateWriteOffset,
                row.CurrentStateValue,
                newStateValue);

            if (!AiReactiveSensorWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiReactiveSensorEditResult? result, out string error) || result == null)
            {
                AdvancedReactiveSensorWriterApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedReactiveSensorWriterApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedReactiveSensorWriterApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.VariableName} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedReactiveSensorWriterApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        void RaiseAdvancedReactiveSensorProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedReactiveSensorSurface));
            OnPropertyChanged(nameof(HasAdvancedReactiveSensorEvidence));
        }
    }

    internal sealed partial class AiAdvancedReactiveSensorEvidenceVm : ObservableObject
    {
        public AiAdvancedReactiveSensorEvidenceVm(AiReactiveSensorDescriptor descriptor)
        {
            VariableName = descriptor.VariableName;
            PropertyName = descriptor.PropertyName;
            RoleLabel = descriptor.RoleLabel;
            CurrentStateValue = descriptor.CurrentStateValue;
            StateWriteOffset = descriptor.StateWriteOffset;
            CurrentStateSummary = descriptor.CurrentStateSummary;
            WriterSummary = $"Writer experimental: patch raw em 0x{descriptor.StateWriteOffset:X4} (state {descriptor.CurrentStateValue}).";
            StateText = descriptor.CurrentStateValue.ToString();
        }

        public string VariableName { get; }
        public string PropertyName { get; }
        public string RoleLabel { get; }
        public ushort CurrentStateValue { get; }
        public int StateWriteOffset { get; }
        public string CurrentStateSummary { get; }
        public string WriterSummary { get; }

        [ObservableProperty] private string stateText = string.Empty;
    }
}
