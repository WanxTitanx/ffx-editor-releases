using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        AiAdvancedFamilySurfaceRowVm BuildAdvancedFamilySurfaceRow(
            string title,
            string subtitle,
            string detailSummary,
            string offsetSummary,
            string openButtonLabel,
            AiIndirectDispatchEditorLaunchContext? launchContext) =>
            new(
                title,
                subtitle,
                detailSummary,
                offsetSummary,
                openButtonLabel,
                launchContext);

        AiAdvancedFamilySurfaceRowVm BuildAdvancedFamilySurfaceRow(
            AiIndirectDispatchUnit unit,
            string title,
            string openButtonLabel,
            string? fallbackVariableName = null) =>
            BuildAdvancedFamilySurfaceRow(
                title,
                BuildAdvancedFamilyUnitSubtitle(unit),
                BuildAdvancedFamilyUnitDetail(unit),
                unit.OffsetSummary,
                openButtonLabel,
                TryBuildAdvancedPreferredUnitLaunchContext(unit, title, fallbackVariableName));

        AiIndirectDispatchEditorLaunchContext? TryBuildAdvancedPreferredUnitLaunchContext(
            AiIndirectDispatchUnit unit,
            string fallbackRoleLabel,
            string? fallbackVariableName = null)
        {
            AiIndirectDispatchEditableSlot? payloadSlot = unit.EditableSlots
                .OrderBy(slot => slot.PushInstructionOffset)
                .FirstOrDefault();
            if (payloadSlot != null)
            {
                string variableName =
                    fallbackVariableName
                    ?? unit.PayloadWrites
                        .OrderBy(write => Math.Abs(write.Offset - payloadSlot.PushInstructionOffset))
                        .Select(write => write.VariableName)
                        .FirstOrDefault()
                    ?? "payload";
                return BuildAdvancedEditorLaunchContext(
                    unit,
                    variableName,
                    AiIndirectDispatchEditorFocusKind.CommandSlot,
                    fallbackRoleLabel,
                    payloadSlot.RoleKey,
                    payloadSlot.PushInstructionOffset);
            }

            AiIndirectDispatchEditableTargetSlot? targetSlot = unit.EditableTargetSlots
                .Where(slot => slot.CanEdit)
                .OrderBy(slot => slot.SourceInstructionOffset < 0 ? int.MaxValue : slot.SourceInstructionOffset)
                .FirstOrDefault();
            if (targetSlot != null)
            {
                return BuildAdvancedEditorLaunchContext(
                    unit,
                    targetSlot.VariableName,
                    AiIndirectDispatchEditorFocusKind.TargetSlot,
                    fallbackRoleLabel,
                    targetSlot.RoleKey,
                    targetSlot.SourceInstructionOffset);
            }

            if (unit.EditableNextStateOffset.HasValue)
            {
                string variableName =
                    fallbackVariableName
                    ?? unit.PayloadWrites.FirstOrDefault()?.VariableName
                    ?? "next-state";
                return BuildAdvancedEditorLaunchContext(
                    unit,
                    variableName,
                    AiIndirectDispatchEditorFocusKind.NextState,
                    fallbackRoleLabel,
                    "next-state",
                    unit.EditableNextStateOffset.Value);
            }

            return null;
        }

        static string BuildAdvancedFamilyUnitSubtitle(AiIndirectDispatchUnit unit)
        {
            string payloadSummary = unit.PayloadWrites.Count == 0
                ? "sem payload resumido"
                : string.Join(" | ", unit.PayloadWrites.Take(3).Select(write => write.ValueSummary));
            return $"{unit.GuardSummary} | {payloadSummary}";
        }

        static string BuildAdvancedFamilyUnitDetail(AiIndirectDispatchUnit unit)
        {
            string aftermath = string.Join(
                " ",
                unit.CompanionEffects
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4));

            if (string.IsNullOrWhiteSpace(aftermath))
                return unit.NextStateSummary;

            return $"{unit.NextStateSummary} {aftermath}";
        }
    }

    internal sealed class AiAdvancedFamilySurfaceRowVm
    {
        public AiAdvancedFamilySurfaceRowVm(
            string title,
            string subtitle,
            string detailSummary,
            string offsetSummary,
            string openButtonLabel,
            AiIndirectDispatchEditorLaunchContext? launchContext)
        {
            Title = title;
            Subtitle = subtitle;
            DetailSummary = detailSummary;
            OffsetSummary = offsetSummary;
            OpenButtonLabel = openButtonLabel;
            LaunchContext = launchContext;
        }

        public string Title { get; }
        public string Subtitle { get; }
        public string DetailSummary { get; }
        public string OffsetSummary { get; }
        public string OpenButtonLabel { get; }
        public AiIndirectDispatchEditorLaunchContext? LaunchContext { get; }
        public bool CanOpenEditor => LaunchContext != null;
    }
}
