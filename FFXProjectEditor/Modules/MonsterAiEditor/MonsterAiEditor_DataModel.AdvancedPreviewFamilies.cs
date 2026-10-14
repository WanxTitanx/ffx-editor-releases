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
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedRoundScriptedBossRows = new();
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedTonberryCameraRoutingRows = new();
        readonly ObservableCollection<AiAdvancedRoundScriptedBossEvidenceVm> advancedRoundScriptedBossEvidenceRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedRoundScriptedBossRows => advancedRoundScriptedBossRows;
        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedTonberryCameraRoutingRows => advancedTonberryCameraRoutingRows;
        public ObservableCollection<AiAdvancedRoundScriptedBossEvidenceVm> AdvancedRoundScriptedBossEvidenceRows => advancedRoundScriptedBossEvidenceRows;

        [ObservableProperty] private string advancedRoundScriptedBossSummary =
            Strings.U_Ai_PreviewNoRoundBossSurface;
        [ObservableProperty] private string advancedRoundScriptedBossHonestSummary =
            Strings.U_Ai_PreviewRoundHonest;
        [ObservableProperty] private string advancedRoundScriptedBossCoverage =
            Strings.U_Ai_PreviewRoundDisarmed;
        [ObservableProperty] private string advancedRoundScriptedBossApplySummary =
            Strings.U_Ai_PreviewNoNativeWriter;
        [ObservableProperty] private string advancedRoundScriptedBossWriterApplySummary =
            Strings.U_Ai_PreviewRoundWriterUnavailable;

        [ObservableProperty] private string advancedTonberryCameraRoutingSummary =
            Strings.U_Ai_PreviewNoTonberrySurface;
        [ObservableProperty] private string advancedTonberryCameraRoutingHonestSummary =
            Strings.U_Ai_PreviewTonberryHonest;
        [ObservableProperty] private string advancedTonberryCameraRoutingCoverage =
            Strings.U_Ai_PreviewTonberryDisarmed;
        [ObservableProperty] private string advancedTonberryCameraRoutingApplySummary =
            Strings.U_Ai_PreviewNoNativeWriter;

        public bool HasAdvancedRoundScriptedBossSurface => AdvancedRoundScriptedBossRows.Count > 0;
        public bool HasAdvancedRoundScriptedBossEvidence => AdvancedRoundScriptedBossEvidenceRows.Count > 0;
        public bool HasAdvancedTonberryCameraRoutingSurface => AdvancedTonberryCameraRoutingRows.Count > 0;

        void UpdateAdvancedRoundScriptedBossContext()
        {
            AdvancedRoundScriptedBossRows.Clear();
            AdvancedRoundScriptedBossEvidenceRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 238)
            {
                AdvancedRoundScriptedBossSummary = Strings.U_Ai_PfNoRoundBoss;
                AdvancedRoundScriptedBossHonestSummary = Strings.U_Ai_PfHonestNoFocus;
                AdvancedRoundScriptedBossCoverage = Strings.U_Ai_PfDisarmed;
                AdvancedRoundScriptedBossApplySummary = Strings.U_Ai_PfNoNativeWriter;
                RaiseAdvancedPreviewFamilyProperties();
                return;
            }

            AddPreviewFamilyRows(
                advancedRoundScriptedBossRows,
                new[]
                {
                    ("preview-round-landing", "Landing / recovery"),
                    ("preview-round-crawl", Strings.U_Ai_PfOpeningCrawl),
                    ("preview-round-sonic-boom", Strings.U_Ai_PfRoundSonicBoom),
                    ("preview-round-aeon-punish", Strings.U_Ai_PfContextualAntiAeon),
                    ("preview-round-finisher", "Finisher + camera choreography"),
                });

            if (AdvancedRoundScriptedBossRows.Count != 5)
            {
                AdvancedRoundScriptedBossRows.Clear();
                AdvancedRoundScriptedBossSummary = Strings.U_Ai_PfPackageNotClosed;
                AdvancedRoundScriptedBossHonestSummary =
                    Strings.U_Ai_PreviewFiveBeatsMissing;
                AdvancedRoundScriptedBossCoverage =
                    Strings.U_Ai_PreviewRoundCoverage;
                AdvancedRoundScriptedBossApplySummary =
                    Strings.U_Ai_PreviewRoundApply;
                RaiseAdvancedPreviewFamilyProperties();
                return;
            }

            AiAdvancedFamilySurfaceRowVm firstRow = AdvancedRoundScriptedBossRows[0];
            AdvancedRoundScriptedBossSummary =
                "Round-scripted-boss do m238: landing, crawl, sonic boom, anti-aeon e finisher ganharam uma leitura dedicada da familia.";
            AdvancedRoundScriptedBossHonestSummary =
                "Esta surface existe para explicar o pacote camera-heavy do m238 sem empurrar a familia para o molde Seymour. Continua PreviewReadOnly no source live.";
            AdvancedRoundScriptedBossCoverage =
                $"{string.Join(" | ", AdvancedRoundScriptedBossRows.Select(row => row.Title))}. {firstRow.Subtitle}";
            AdvancedRoundScriptedBossApplySummary =
                Strings.U_Ai_PreviewRoundReadOnlyPopup;

            UpdateAdvancedRoundScriptedBossWriterContext();
            RaiseAdvancedPreviewFamilyProperties();
        }

        void UpdateAdvancedTonberryCameraRoutingContext()
        {
            AdvancedTonberryCameraRoutingRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber is not (223 or 224))
            {
                AdvancedTonberryCameraRoutingSummary = Strings.U_Ai_PfNoTonberry;
                AdvancedTonberryCameraRoutingHonestSummary = Strings.U_Ai_PfTonberryHonest;
                AdvancedTonberryCameraRoutingCoverage = Strings.U_Ai_PfTonberryDisarmed;
                AdvancedTonberryCameraRoutingApplySummary = Strings.U_Ai_PfNoNativeWriter;
                RaiseAdvancedPreviewFamilyProperties();
                return;
            }

            AddPreviewFamilyRows(
                advancedTonberryCameraRoutingRows,
                new[]
                {
                    ("preview-tonberry-direct-pressure", Strings.U_Ai_PfDirectPressure),
                    ("preview-tonberry-position-cycle", Strings.U_Ai_PfApproachCycle),
                    ("preview-tonberry-counter-window", Strings.U_Ai_PfRetaliationWindow),
                    ("preview-tonberry-camera-routing", Strings.U_Ai_PfCameraRouting),
                });

            if (AdvancedTonberryCameraRoutingRows.Count != 4)
            {
                AdvancedTonberryCameraRoutingRows.Clear();
                AdvancedTonberryCameraRoutingSummary = Strings.U_Ai_PfTonberryNotClosed;
                AdvancedTonberryCameraRoutingHonestSummary =
                    Strings.U_Ai_PreviewFourBeatsMissing;
                AdvancedTonberryCameraRoutingCoverage =
                    Strings.U_Ai_PreviewTonberryCoverage;
                AdvancedTonberryCameraRoutingApplySummary =
                    Strings.U_Ai_PreviewTonberryApply;
                RaiseAdvancedPreviewFamilyProperties();
                return;
            }

            AiAdvancedFamilySurfaceRowVm firstRow = AdvancedTonberryCameraRoutingRows[0];
            AdvancedTonberryCameraRoutingSummary =
                Strings.U_Ai_PreviewTonberrySummary;
            AdvancedTonberryCameraRoutingHonestSummary =
                Strings.U_Ai_PreviewTonberrySurface;
            AdvancedTonberryCameraRoutingCoverage =
                $"{string.Join(" | ", AdvancedTonberryCameraRoutingRows.Select(row => row.Title))}. {firstRow.Subtitle}";
            AdvancedTonberryCameraRoutingApplySummary =
                Strings.U_Ai_PreviewTonberryReadOnlyPopup;

            RaiseAdvancedPreviewFamilyProperties();
        }

        void UpdateAdvancedRoundScriptedBossWriterContext()
        {
            AdvancedRoundScriptedBossEvidenceRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedRoundScriptedBossEvidence));
                return;
            }

            if (!AiRoundScriptedBossWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiRoundScriptedBossDescriptor> descriptors, out _))
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.U_Ai_PreviewRoundBeatsNotProven;
                OnPropertyChanged(nameof(HasAdvancedRoundScriptedBossEvidence));
                return;
            }

            foreach (AiRoundScriptedBossDescriptor descriptor in descriptors)
                AdvancedRoundScriptedBossEvidenceRows.Add(new AiAdvancedRoundScriptedBossEvidenceVm(descriptor));

            AdvancedRoundScriptedBossWriterApplySummary = AdvancedRoundScriptedBossEvidenceRows.Count > 0
                ? string.Format(Strings.U_Ai_PreviewM238WriterArmed, AdvancedRoundScriptedBossEvidenceRows.Count)
                : Strings.U_Ai_PreviewRoundBeatsNotProven;
            OnPropertyChanged(nameof(HasAdvancedRoundScriptedBossEvidence));
        }

        public bool ApplyAdvancedRoundScriptedBossEdit(AiAdvancedRoundScriptedBossEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedRoundScriptedBossEvidenceRows.Contains(row))
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.U_Ai_PfNeedAiFileM238;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.CommandText, out ushort newCommand))
            {
                AdvancedRoundScriptedBossWriterApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiRoundScriptedBossPatchRequest(
                row.BeatName,
                row.CommandInstructionOffset,
                row.CurrentCommand,
                newCommand,
                row.LandingRepriseOffset,
                row.CurrentLandingReprise,
                row.CurrentLandingReprise);

            if (!AiRoundScriptedBossWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiRoundScriptedBossEditResult? result, out string error) || result == null)
            {
                AdvancedRoundScriptedBossWriterApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedRoundScriptedBossWriterApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedRoundScriptedBossWriterApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.BeatName} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedRoundScriptedBossWriterApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        void AddPreviewFamilyRows(
            ObservableCollection<AiAdvancedFamilySurfaceRowVm> rows,
            IReadOnlyList<(string UnitId, string Title)> definitions)
        {
            foreach ((string unitId, string title) in definitions)
            {
                AiIndirectDispatchUnit? unit = advancedPhaseUnitsById.Values.FirstOrDefault(candidate =>
                    candidate.UnitId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
                if (unit == null)
                    continue;

                rows.Add(BuildAdvancedFamilySurfaceRow(
                    unit,
                    title,
                    Strings.U_Ai_PreviewBeatReading));
            }
        }

        void RaiseAdvancedPreviewFamilyProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedRoundScriptedBossSurface));
            OnPropertyChanged(nameof(HasAdvancedRoundScriptedBossEvidence));
            OnPropertyChanged(nameof(HasAdvancedTonberryCameraRoutingSurface));
        }
    }

    internal sealed partial class AiAdvancedRoundScriptedBossEvidenceVm : ObservableObject
    {
        public AiAdvancedRoundScriptedBossEvidenceVm(AiRoundScriptedBossDescriptor descriptor)
        {
            BeatName = descriptor.BeatName;
            RoleLabel = descriptor.RoleLabel;
            CurrentCommand = descriptor.CurrentCommand;
            CommandInstructionOffset = descriptor.CommandInstructionOffset;
            LandingRepriseOffset = descriptor.LandingRepriseOffset;
            CurrentLandingReprise = descriptor.CurrentLandingReprise;
            CurrentCommandSummary = descriptor.CurrentCommandSummary;
            WriterSummary = $"Writer experimental: patch raw em 0x{descriptor.CommandInstructionOffset:X4} (comando 0x{descriptor.CurrentCommand:X4}).";
            CommandText = $"0x{descriptor.CurrentCommand:X4}";
        }

        public string BeatName { get; }
        public string RoleLabel { get; }
        public ushort CurrentCommand { get; }
        public int CommandInstructionOffset { get; }
        public int? LandingRepriseOffset { get; }
        public ushort? CurrentLandingReprise { get; }
        public string CurrentCommandSummary { get; }
        public string WriterSummary { get; }

        [ObservableProperty] private string commandText = string.Empty;
    }
}
