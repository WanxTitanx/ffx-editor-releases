using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        readonly ObservableCollection<AiAdvancedFamilySurfaceRowVm> advancedEncounterAppearRows = new();

        public ObservableCollection<AiAdvancedFamilySurfaceRowVm> AdvancedEncounterAppearRows => advancedEncounterAppearRows;

        [ObservableProperty] private string advancedEncounterAppearSummary =
            "No dedicated surface for encounter-keyed appear / disable on the current monster.";
        [ObservableProperty] private string advancedEncounterAppearHonestSummary =
            "When the m211 package closes in the live detector, V2 raises its own reading for the handoff Steal -> Open -> reveal/disable. Remains PreviewReadOnly.";
        [ObservableProperty] private string advancedEncounterAppearCoverage =
            "Dedicated surface disarmed outside the encounter-keyed appear / disable package.";
        [ObservableProperty] private string advancedEncounterAppearApplySummary =
            "PreviewReadOnly: no native writer for this family is released yet.";

        public bool HasAdvancedEncounterAppearSurface => AdvancedEncounterAppearRows.Count > 0;

        void UpdateAdvancedEncounterAppearContext()
        {
            AdvancedEncounterAppearRows.Clear();

            if (selectedScript == null || !selectedScript.HasScript)
            {
                AdvancedEncounterAppearSummary =
                    "No dedicated surface for encounter-keyed appear / disable on the current monster.";
                AdvancedEncounterAppearHonestSummary =
                    "When the m211 package closes in the live detector, V2 raises its own reading for the handoff Steal -> Open -> reveal/disable. Remains PreviewReadOnly.";
                AdvancedEncounterAppearCoverage =
                    "Dedicated surface disarmed outside the encounter-keyed appear / disable package.";
                AdvancedEncounterAppearApplySummary =
                    "PreviewReadOnly: no native writer for this family is released yet.";
                RaiseAdvancedEncounterAppearProperties();
                return;
            }

            AddPreviewFamilyRows(
                advancedEncounterAppearRows,
                new[]
                {
                    ("preview-encounter-open", "Steal -> Open"),
                    ("preview-encounter-appear-disable", "Reveal / disable / handoff"),
                });

            if (AdvancedEncounterAppearRows.Count != 2)
            {
                AdvancedEncounterAppearRows.Clear();
                AdvancedEncounterAppearSummary =
                    "The detector found part of the encounter-keyed appear / disable package, but the complete handoff has not closed in this live source yet.";
                AdvancedEncounterAppearHonestSummary =
                    Strings.U_Ai_EncounterAppearNoPopup;
                AdvancedEncounterAppearCoverage =
                    "The dedicated surface only opens when the opening package and the encounter-keyed handoff appear together.";
                AdvancedEncounterAppearApplySummary =
                    "PreviewReadOnly: still without writer or apply for the encounter-keyed appear / disable package.";
                RaiseAdvancedEncounterAppearProperties();
                return;
            }

            AiAdvancedFamilySurfaceRowVm handoffRow = AdvancedEncounterAppearRows[1];
            AdvancedEncounterAppearSummary =
                "Encounter-keyed appear / disable: V2 now separates the opening Steal -> Open from the handoff that reveals the encounter-specific actor and disables the host.";
            AdvancedEncounterAppearHonestSummary =
                "This family remains PreviewReadOnly. It exists to explain the contextual reveal/disable package without pretending generic switch authoring or universal actor topology writer.";
            AdvancedEncounterAppearCoverage =
                $"{string.Join(" | ", AdvancedEncounterAppearRows.Select(row => row.Title))}. {handoffRow.Subtitle}";
            AdvancedEncounterAppearApplySummary =
                "PreviewReadOnly: the family popup organizes encounter router, Open, contextual actor reveal, CTB/targetable flags, and Copycat/Escape/Flee disable, but does not release native writing.";

            RaiseAdvancedEncounterAppearProperties();
        }

        void RaiseAdvancedEncounterAppearProperties() =>
            OnPropertyChanged(nameof(HasAdvancedEncounterAppearSurface));
    }
}
