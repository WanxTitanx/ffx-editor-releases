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
        static readonly string[] AdvancedAnimaPayloadNames = { "Oblivion", "Boost", "Pain" };

        readonly ObservableCollection<AiAdvancedAnimaPayloadRowVm> advancedAnimaPayloadRows = new();
        readonly ObservableCollection<AiAdvancedAnimaOdThresholdEvidenceVm> advancedAnimaOdThresholdRows = new();
        AiIndirectDispatchEditorLaunchContext? advancedAnimaPayloadLaunchContext;
        AiIndirectDispatchEditorLaunchContext? advancedAnimaTargetLaunchContext;

        public ObservableCollection<AiAdvancedAnimaPayloadRowVm> AdvancedAnimaPayloadRows => advancedAnimaPayloadRows;
        public ObservableCollection<AiAdvancedAnimaOdThresholdEvidenceVm> AdvancedAnimaOdThresholdRows => advancedAnimaOdThresholdRows;

        [ObservableProperty] private string advancedAnimaSummary =
            "No dedicated Anima payload picker for the current monster.";
        [ObservableProperty] private string advancedAnimaHonestSummary =
            "When Anima is in focus, V2 shows only the actual row-only payload/target clip. The full state machine remains outside this surface.";
        [ObservableProperty] private string advancedAnimaCoverage =
            "Dedicated surface disarmed outside m125.";
        [ObservableProperty] private string advancedAnimaApplySummary =
            "No dedicated Anima popup armed for the current monster.";
        [ObservableProperty] private string advancedAnimaOdThresholdApplySummary =
            "Narrow Anima OD threshold writer unavailable outside m125.";

        public bool HasAdvancedAnimaSurface => AdvancedAnimaPayloadRows.Count > 0;
        public bool HasAdvancedAnimaOdThresholdEvidence => AdvancedAnimaOdThresholdRows.Count > 0;
        public bool CanOpenAdvancedAnimaPayloadEditor => AdvancedAnimaPayloadLaunchContext != null;
        public bool CanOpenAdvancedAnimaTargetEditor => AdvancedAnimaTargetLaunchContext != null;
        public AiIndirectDispatchEditorLaunchContext? AdvancedAnimaPayloadLaunchContext => advancedAnimaPayloadLaunchContext;
        public AiIndirectDispatchEditorLaunchContext? AdvancedAnimaTargetLaunchContext => advancedAnimaTargetLaunchContext;

        void UpdateAdvancedAnimaContext()
        {
            AdvancedAnimaPayloadRows.Clear();
            AdvancedAnimaOdThresholdRows.Clear();
            advancedAnimaPayloadLaunchContext = null;
            advancedAnimaTargetLaunchContext = null;

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 125)
            {
                AdvancedAnimaSummary = "No dedicated Anima payload picker for the current monster.";
                AdvancedAnimaHonestSummary =
                    "When Anima is in focus, V2 shows only the actual row-only payload/target clip. The full state machine remains outside this surface.";
                AdvancedAnimaCoverage = "Dedicated surface disarmed outside m125.";
                AdvancedAnimaApplySummary = "Narrow Anima writer unavailable outside m125.";
                RaiseAdvancedAnimaProperties();
                return;
            }

            AiIndirectDispatchUnit? unit = advancedPhaseUnitsById.Values
                .FirstOrDefault(IsAdvancedAnimaUnit);

            if (unit == null)
            {
                AdvancedAnimaSummary =
                    "m125 in focus, but the narrow Anima payload picker did not close on this source's live detector.";
                AdvancedAnimaHonestSummary =
                    "Without dispatch-support-0008-0007-0, V2 does not create its own popup or promoted writer for Anima.";
                AdvancedAnimaCoverage =
                    "The dedicated Anima surface only opens when the real Oblivion / Boost / Pain clip closes.";
                AdvancedAnimaApplySummary =
                    "No narrow Anima unit was recognized in this parse.";
                RaiseAdvancedAnimaProperties();
                return;
            }

            List<AiIndirectDispatchWrite> payloadWrites = unit.PayloadWrites
                .Where(write => AdvancedAnimaPayloadNames.Any(name =>
                    write.ValueSummary.Contains(name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(write => write.Offset)
                .ToList();

            foreach (AiIndirectDispatchWrite write in payloadWrites)
            {
                AdvancedAnimaPayloadRows.Add(new AiAdvancedAnimaPayloadRowVm(
                    write.RoleSummary,
                    write.ValueSummary,
                    $"0x{write.Offset:X4}",
                    string.Format(Strings.AiAnimaPayloadDetail, write.VariableName)));
            }

            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots =
                AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers);

            string targetSummary = targetSlots.Count == 0
                ? "contextual target not yet materialized on the surface"
                : string.Join(" · ", targetSlots.Select(slot =>
                    $"{slot.SlotLabel}: {slot.ValueSummary}"));

            advancedAnimaPayloadLaunchContext = BuildAdvancedAnimaPayloadLaunchContext(unit);
            advancedAnimaTargetLaunchContext = BuildAdvancedAnimaTargetLaunchContext(unit, targetSlots);

            AdvancedAnimaSummary = string.Format(Strings.AiAdvancedAnimaActionsSummary, AdvancedAnimaPayloadRows.Count);
            AdvancedAnimaHonestSummary = Strings.AiAdvancedAnimaScope;
            AdvancedAnimaCoverage =
                $"{unit.GuardSummary} · {targetSummary} · unitId {unit.UnitId}.";
            AdvancedAnimaApplySummary = advancedAnimaPayloadLaunchContext != null
                ? Strings.AiAdvancedAnimaEditHint
                : Strings.U_Ai_AnimaPopupNoFocus;

            UpdateAdvancedAnimaOdThresholdContext();
            RaiseAdvancedAnimaProperties();
        }

        void UpdateAdvancedAnimaOdThresholdContext()
        {
            AdvancedAnimaOdThresholdRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.U_Ai_SelectMonsterWithAiFile;
                OnPropertyChanged(nameof(HasAdvancedAnimaOdThresholdEvidence));
                return;
            }

            if (!AiAnimaOdThresholdWriter.TryBuildDescriptors(selectedScript, out IReadOnlyList<AiAnimaOdThresholdDescriptor> descriptors, out _))
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.U_Ai_AnimaOdGateNotProven;
                OnPropertyChanged(nameof(HasAdvancedAnimaOdThresholdEvidence));
                return;
            }

            foreach (AiAnimaOdThresholdDescriptor descriptor in descriptors)
                AdvancedAnimaOdThresholdRows.Add(new AiAdvancedAnimaOdThresholdEvidenceVm(descriptor));

            AdvancedAnimaOdThresholdApplySummary = AdvancedAnimaOdThresholdRows.Count > 0
                ? Strings.AiAdvancedOverdriveReady
                : Strings.U_Ai_AnimaOdGateNotProven;
            OnPropertyChanged(nameof(HasAdvancedAnimaOdThresholdEvidence));
        }

        public bool ApplyAdvancedAnimaOdThresholdEdit(AiAdvancedAnimaOdThresholdEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedAnimaOdThresholdRows.Contains(row))
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null || selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_f4dc07df;
                return false;
            }

            if (!AiAdvancedNumericInput.TryParse(row.MaximumText, out ushort maximum))
            {
                AdvancedAnimaOdThresholdApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            var request = new AiAnimaOdThresholdPatchRequest(
                row.MaximumInstructionOffset, row.CurrentMaximum, maximum, row.CurrentAttackThreshold);

            if (!AiAnimaOdThresholdWriter.TryApplyPatch(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiAnimaOdThresholdEditResult? result, out string error) || result == null)
            {
                AdvancedAnimaOdThresholdApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result.EditedAiFileBytes, selectedScript, selectedScript.OriginalAiFileBytes.Length, out _, out string validationReason))
            {
                AdvancedAnimaOdThresholdApplySummary = $"Validacao estrutural bloqueou o patch: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedAnimaOdThresholdApplySummary = $"Save abortado: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string summary = $"✅ {row.RoleLabel} salvo em {Path.GetFileName(selectedPath)} ({result.ChangedBytes.Count} byte(s) no diff, .prev.bak criado). {(reloaded ? "Readback ok." : "Readback parcial.")} RT2 ainda recomendado.";
            AdvancedAnimaOdThresholdApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        static bool IsAdvancedAnimaUnit(AiIndirectDispatchUnit unit)
        {
            if (unit.UnitId.Equals("dispatch-support-0008-0007-0", StringComparison.OrdinalIgnoreCase))
                return true;

            return AdvancedAnimaPayloadNames.All(name =>
                unit.PayloadWrites.Any(write => write.ValueSummary.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }

        static AiIndirectDispatchEditorLaunchContext? BuildAdvancedAnimaPayloadLaunchContext(AiIndirectDispatchUnit unit)
        {
            AiIndirectDispatchEditableSlot? editableSlot = unit.EditableSlots
                .OrderBy(slot => slot.PushInstructionOffset)
                .FirstOrDefault();
            if (editableSlot == null)
                return null;

            AiIndirectDispatchWrite? matchingWrite = unit.PayloadWrites
                .FirstOrDefault(write => write.Offset == editableSlot.PushInstructionOffset)
                ?? unit.PayloadWrites.FirstOrDefault();
            if (matchingWrite == null)
                return null;

            return BuildAdvancedEditorLaunchContext(
                unit,
                matchingWrite.VariableName,
                AiIndirectDispatchEditorFocusKind.CommandSlot,
                "payload picker da Anima",
                editableSlot.RoleKey,
                editableSlot.PushInstructionOffset);
        }

        static AiIndirectDispatchEditorLaunchContext? BuildAdvancedAnimaTargetLaunchContext(
            AiIndirectDispatchUnit unit,
            IReadOnlyList<AiIndirectDispatchTargetSlotSurface> targetSlots)
        {
            AiIndirectDispatchEditableTargetSlot? editableTarget = unit.EditableTargetSlots
                .Where(slot => slot.CanEdit)
                .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .FirstOrDefault();
            if (editableTarget == null)
                return null;

            AiIndirectDispatchTargetSlotSurface? matchingSurface = targetSlots.FirstOrDefault(slot =>
                    slot.VariableIndex == editableTarget.VariableIndex
                    && slot.SourceInstructionOffset == editableTarget.SourceInstructionOffset)
                ?? targetSlots.FirstOrDefault();
            if (matchingSurface == null)
                return null;

            return BuildAdvancedEditorLaunchContext(
                unit,
                matchingSurface.VariableName,
                AiIndirectDispatchEditorFocusKind.TargetSlot,
                "alvo contextual da Anima",
                editableTarget.RoleKey,
                editableTarget.SourceInstructionOffset);
        }

        bool TryBuildAdvancedAnimaFamilyDialogSnapshot(out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
        {
            snapshot = null;
            if (!HasAdvancedAnimaSurface)
                return false;

            string payloadSubtitle = AdvancedAnimaPayloadRows.Count == 0
                ? Strings.U_Ai_AnimaNoPayloadRowOnly
                : string.Join(" | ", AdvancedAnimaPayloadRows.Select(row => row.PayloadSummary));
            string payloadOffsets = AdvancedAnimaPayloadRows.Count == 0
                ? Strings.U_Ai_AnimaOffsetNotIsolated
                : string.Join(" | ", AdvancedAnimaPayloadRows.Select(row => row.OffsetSummary));
            string payloadDetail = AdvancedAnimaPayloadRows.Count == 0
                ? "The Anima row-only clip could not materialize the payloads in this read."
                : string.Join(" ", AdvancedAnimaPayloadRows.Select(row => $"{row.SlotLabel}: {row.DetailSummary}"));

            var rows = new List<AiAdvancedFamilySurfaceRowVm>
            {
                BuildAdvancedFamilySurfaceRow(
                    "Payload picker",
                    payloadSubtitle,
                    payloadDetail,
                    payloadOffsets,
                    Strings.U_Ai_AnimaOpenPayloadPopup,
                    advancedAnimaPayloadLaunchContext),
                BuildAdvancedFamilySurfaceRow(
                    "Alvo contextual",
                    advancedAnimaTargetLaunchContext != null
                        ? Strings.U_Ai_AnimaTargetSurfaceProven
                        : Strings.U_Ai_AnimaObservedContextualTarget,
                    AdvancedAnimaCoverage,
                    "m125",
                    Strings.U_Ai_AnimaOpenTargetPopup,
                    advancedAnimaTargetLaunchContext),
                BuildAdvancedFamilySurfaceRow(
                    "OD / state machine maior",
                    "read-only documental",
                    "showOverdriveBar, usedCommand, runBtlSceneA/B, CurrentTurnDelay, and beat 0x6051 remain outside this narrow writer.",
                    "OD + BtlScene + 0x6051",
                    "Leitura dedicada",
                    null),
            };

            snapshot = new AiAdvancedFamilySurfaceDialogSnapshot(
                AdvancedFamilySurfaceKeys.Anima,
                "Anima - payload picker",
                "MONSTER AI - M125 - ANIMA PAYLOAD PICKER",
                AdvancedAnimaSummary,
                AdvancedAnimaHonestSummary,
                AdvancedAnimaCoverage,
                AdvancedAnimaApplySummary,
                rows);
            return true;
        }

        void RaiseAdvancedAnimaProperties()
        {
            OnPropertyChanged(nameof(HasAdvancedAnimaSurface));
            OnPropertyChanged(nameof(HasAdvancedAnimaOdThresholdEvidence));
            OnPropertyChanged(nameof(CanOpenAdvancedAnimaPayloadEditor));
            OnPropertyChanged(nameof(CanOpenAdvancedAnimaTargetEditor));
            OnPropertyChanged(nameof(AdvancedAnimaPayloadLaunchContext));
            OnPropertyChanged(nameof(AdvancedAnimaTargetLaunchContext));
        }
    }

    internal sealed partial class AiAdvancedAnimaOdThresholdEvidenceVm : ObservableObject
    {
        public AiAdvancedAnimaOdThresholdEvidenceVm(AiAnimaOdThresholdDescriptor descriptor)
        {
            CurrentMaximum = descriptor.CurrentMaximum;
            CurrentAttackThreshold = descriptor.CurrentAttackThreshold;
            MaximumInstructionOffset = descriptor.MaximumInstructionOffset;
            CurrentThresholdSummary = string.Format(Strings.AiAdvancedOverdriveCurrent, CurrentMaximum, CurrentAttackThreshold);
            WriterSummary = string.Format(Strings.AiAdvancedOverdriveEvidence,
                descriptor.SetterCallOffset, descriptor.ComparisonInstructionOffset, descriptor.AttackInstructionOffset);
            MaximumText = CurrentMaximum.ToString();
        }

        public string RoleLabel => Strings.AiAdvancedOverdriveMaximum;
        public ushort CurrentMaximum { get; }
        public ushort CurrentAttackThreshold { get; }
        public int MaximumInstructionOffset { get; }
        public string CurrentThresholdSummary { get; }
        public string WriterSummary { get; }
        [ObservableProperty] private string maximumText = string.Empty;
    }

    internal sealed class AiAdvancedAnimaPayloadRowVm
    {
        public AiAdvancedAnimaPayloadRowVm(
            string slotLabel,
            string payloadSummary,
            string offsetSummary,
            string detailSummary)
        {
            SlotLabel = slotLabel;
            PayloadSummary = payloadSummary;
            OffsetSummary = offsetSummary;
            DetailSummary = detailSummary;
        }

        public string SlotLabel { get; }
        public string PayloadSummary { get; }
        public string OffsetSummary { get; }
        public string DetailSummary { get; }
    }
}
