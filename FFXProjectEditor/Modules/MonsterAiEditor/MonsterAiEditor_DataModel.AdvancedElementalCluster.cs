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
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedElementalClusterRows = new();
        readonly ObservableCollection<AiAdvancedOmnisClusterEvidenceVm> advancedOmnisClusterEvidenceRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedElementalClusterRows => advancedElementalClusterRows;
        public ObservableCollection<AiAdvancedOmnisClusterEvidenceVm> AdvancedOmnisClusterEvidenceRows => advancedOmnisClusterEvidenceRows;

        [ObservableProperty] private string advancedElementalClusterSummary =
            "No dedicated surface for the elemental cluster in the current monster.";
        [ObservableProperty] private string advancedElementalClusterHonestSummary =
            "When m131 closes in the live detector, V2 raises a family-specific card and reuses only the proven narrow popup. The entire cluster does not become a universal writer.";
        [ObservableProperty] private string advancedElementalClusterCoverage =
            "Dedicated surface disarmed outside m131.";
        [ObservableProperty] private string advancedElementalClusterApplySummary =
            "No narrow lane of the elemental cluster armed in the current monster.";
        [ObservableProperty] private string advancedOmnisClusterWriterApplySummary =
            "Narrow writer of Omnis unavailable outside m131.";

        public bool HasAdvancedElementalClusterSurface => AdvancedElementalClusterRows.Count > 0;
        public bool HasAdvancedOmnisClusterEvidence => AdvancedOmnisClusterEvidenceRows.Count > 0;

        void UpdateAdvancedElementalClusterContext()
        {
            AdvancedElementalClusterRows.Clear();
            AdvancedOmnisClusterEvidenceRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 131)
            {
                AdvancedElementalClusterSummary = "No dedicated surface for the elemental cluster in the current monster.";
                AdvancedElementalClusterHonestSummary =
                    "When m131 closes in the live detector, V2 raises a family-specific card and reuses only the proven narrow popup. The entire cluster does not become a universal writer.";
                AdvancedElementalClusterCoverage = "Dedicated surface disarmed outside m131.";
                AdvancedElementalClusterApplySummary =
                    "No narrow lane of the elemental cluster armed in the current monster.";
                RaiseAdvancedElementalClusterProperties();
                return;
            }

            UpdateAdvancedOmnisClusterWriterContext();

            AiIndirectDispatchUnit? elementalUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-omnis-elemental", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? dispelUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-omnis-dispel-break", StringComparison.OrdinalIgnoreCase));
            AiIndirectDispatchUnit? ultimaUnit = advancedPhaseUnitsById.Values.FirstOrDefault(unit =>
                unit.UnitId.Equals("preview-omnis-ultima-break", StringComparison.OrdinalIgnoreCase));

            if (elementalUnit == null || dispelUnit == null || ultimaUnit == null)
            {
                AdvancedElementalClusterSummary =
                    "m131 in focus, but the preview-omnis-* package did not close entirely in this live source.";
                AdvancedElementalClusterHonestSummary =
                    "Without the three proven beats, V2 does not invent its own popup nor promoted writer for the elemental cluster.";
                AdvancedElementalClusterCoverage =
                    "The dedicated surface only raises when elemental barrage, Dispel break, and Ultima break appear together.";
                AdvancedElementalClusterApplySummary =
                    Strings.U_Ai_ClusterNoLane;
                RaiseAdvancedElementalClusterProperties();
                return;
            }

            AddAdvancedElementalClusterRow(
                elementalUnit,
                Strings.U_Ai_ClusterBarrage,
                Strings.U_Ai_ClusterOpenPackagePopup);
            AddAdvancedElementalClusterRow(
                dispelUnit,
                "Dispel break",
                Strings.U_Ai_ClusterOpenBreakPopup);
            AddAdvancedElementalClusterRow(
                ultimaUnit,
                "Ultima break",
                Strings.U_Ai_ClusterOpenUltimaPopup);

            AdvancedElementalClusterSummary =
                "Elemental cluster for Omnis: elemental barrage, Dispel break and Ultima break proved to be three distinct beats.";
            AdvancedElementalClusterHonestSummary =
                "This surface remains family-specific and narrow: it summarizes the three beats and only reuses the proven indirect editor when the package has a clear row-only slot. Writes of elemental status, 0x60F0 and larger aftermath remain outside.";
            AdvancedElementalClusterCoverage =
                $"{elementalUnit.UnitId} · {dispelUnit.UnitId} · {ultimaUnit.UnitId}. {elementalUnit.GuardSummary}";
            AdvancedElementalClusterApplySummary =
                AdvancedElementalClusterRows.Any(row => row.CanOpenEditor)
                    ? "The promoted direct beats can open the narrow popup for the literal payload/target of that cast. The rest of the cluster remains structural reading."
                    : "The cluster beats became visible, but no row-only cut opened a popup in this reading.";

            RaiseAdvancedElementalClusterProperties();
        }

        void UpdateAdvancedOmnisClusterWriterContext()
        {
            AdvancedOmnisClusterEvidenceRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedOmnisClusterEvidence));
                return;
            }

            if (!AiOmnisClusterWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiOmnisClusterDescriptor> descriptors, out _))
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.U_Ai_ClusterOmnisNotProven;
                OnPropertyChanged(nameof(HasAdvancedOmnisClusterEvidence));
                return;
            }

            foreach (AiOmnisClusterDescriptor descriptor in descriptors)
                AdvancedOmnisClusterEvidenceRows.Add(new AiAdvancedOmnisClusterEvidenceVm(descriptor));

            AdvancedOmnisClusterWriterApplySummary = AdvancedOmnisClusterEvidenceRows.Count > 0
                ? string.Format(Strings.U_Ai_ClusterOmnisWriterArmed, AdvancedOmnisClusterEvidenceRows.Count)
                : Strings.U_Ai_ClusterOmnisNotProven;
            OnPropertyChanged(nameof(HasAdvancedOmnisClusterEvidence));
        }

        public bool ApplyAdvancedOmnisClusterEdit(AiAdvancedOmnisClusterEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedOmnisClusterEvidenceRows.Contains(row))
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_f05283ed;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.CommandText, out ushort newCommand))
            {
                AdvancedOmnisClusterWriterApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiOmnisClusterPatchRequest(
                row.BeatName,
                row.CommandInstructionOffset,
                row.CurrentCommand,
                newCommand,
                row.StateWriteOffset,
                row.CurrentStateValue,
                row.CurrentStateValue);

            if (!AiOmnisClusterWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiOmnisClusterEditResult? result, out string error) || result == null)
            {
                AdvancedOmnisClusterWriterApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedOmnisClusterWriterApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedOmnisClusterWriterApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.BeatName} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedOmnisClusterWriterApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        void AddAdvancedElementalClusterRow(
            AiIndirectDispatchUnit unit,
            string title,
            string openButtonLabel)
        {
            AdvancedElementalClusterRows.Add(new AiAdvancedFamilySurfaceRowVm(
                title,
                BuildAdvancedFamilyUnitSubtitle(unit),
                BuildAdvancedFamilyUnitDetail(unit),
                unit.OffsetSummary,
                openButtonLabel,
                TryBuildAdvancedPreferredUnitLaunchContext(unit, title)));
        }

        void RaiseAdvancedElementalClusterProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedElementalClusterSurface));
            OnPropertyChanged(nameof(HasAdvancedOmnisClusterEvidence));
        }
    }

    internal sealed partial class AiAdvancedOmnisClusterEvidenceVm : ObservableObject
    {
        public AiAdvancedOmnisClusterEvidenceVm(AiOmnisClusterDescriptor descriptor)
        {
            BeatName = descriptor.BeatName;
            RoleLabel = descriptor.RoleLabel;
            CurrentCommand = descriptor.CurrentCommand;
            CommandInstructionOffset = descriptor.CommandInstructionOffset;
            CurrentCommandSummary = descriptor.CurrentCommandSummary;
            StateWriteOffset = descriptor.StateWriteOffset;
            CurrentStateValue = descriptor.CurrentStateValue;
            WriterSummary = $"Writer experimental: patch raw em 0x{descriptor.CommandInstructionOffset:X4} (comando 0x{descriptor.CurrentCommand:X4}).";
            CommandText = $"0x{descriptor.CurrentCommand:X4}";
        }

        public string BeatName { get; }
        public string RoleLabel { get; }
        public ushort CurrentCommand { get; }
        public int CommandInstructionOffset { get; }
        public string CurrentCommandSummary { get; }
        public int? StateWriteOffset { get; }
        public ushort? CurrentStateValue { get; }
        public string WriterSummary { get; }

        [ObservableProperty] private string commandText = string.Empty;
    }
}
