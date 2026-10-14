using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Services;
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
        static readonly string[] AdvancedCompanionActivationBattleIds =
        [
            "maca03_20",
            "maca03_21",
            "maca03_22",
            "mcyt00_20",
            "mcyt00_21",
            "mcyt00_22",
        ];

        public ObservableCollection<AiAdvancedCompanionActivationPackageVm> AdvancedCompanionActivationPackages { get; } = new();
        public ObservableCollection<BattleCompanionActivationRow> AdvancedCompanionActivationRows { get; } = new();

        [ObservableProperty] private AiAdvancedCompanionActivationPackageVm? selectedAdvancedCompanionActivationPackage;
        partial void OnSelectedAdvancedCompanionActivationPackageChanged(AiAdvancedCompanionActivationPackageVm? value)
        {
            AdvancedCompanionActivationRows.Clear();

            if (value == null)
            {
                AdvancedCompanionActivationDetailTitle = "Summon-handoff / appear";
                AdvancedCompanionActivationSummary =
                    "The lane m213 appears here as a proven battle package, not as a fake route for Seymour.";
                AdvancedCompanionActivationCoverage =
                    "No package selected. When there is a battle-backed footprint, the narrow reading appears here.";
                AdvancedCompanionActivationGuardrail =
                    Strings.U_Ai_M213SpawnStillRefuted;
            }
            else
            {
                AdvancedCompanionActivationDetailTitle = $"{value.BattleId} · {value.StatusLabel}";
                AdvancedCompanionActivationSummary = value.HumanSummary;
                AdvancedCompanionActivationCoverage =
                    $"{value.BattleTokenLabel} · {value.CoverageLabel} · {value.FormationSummary}";
                AdvancedCompanionActivationGuardrail = value.Guardrail;
                foreach (BattleCompanionActivationRow row in value.Rows)
                    AdvancedCompanionActivationRows.Add(row);
            }

            AdvancedCompanionActivationApplySummary = GetAdvancedCompanionActivationApplySummary(value);

            OnPropertyChanged(nameof(HasAdvancedCompanionActivationPackages));
            OnPropertyChanged(nameof(HasSelectedAdvancedCompanionActivationPackage));
            OnPropertyChanged(nameof(CanOpenAdvancedCompanionActivationEditor));
            OnPropertyChanged(nameof(ShowAdvancedCompanionActivationPlaceholder));
        }

        [ObservableProperty] private string advancedCompanionActivationDetailTitle = "Summon-handoff / appear";
        [ObservableProperty] private string advancedCompanionActivationSummary =
            "The lane m213 appears here as a proven battle package, not as a fake route for Seymour.";
        [ObservableProperty] private string advancedCompanionActivationCoverage =
            "No package selected. When there is a battle-backed footprint, the narrow reading appears here.";
        [ObservableProperty] private string advancedCompanionActivationGuardrail =
            Strings.U_Ai_M213SpawnStillRefuted;

        public bool HasAdvancedCompanionActivationPackages => AdvancedCompanionActivationPackages.Count > 0;
        public bool HasSelectedAdvancedCompanionActivationPackage => SelectedAdvancedCompanionActivationPackage != null;
        public bool CanOpenAdvancedCompanionActivationEditor => SelectedAdvancedCompanionActivationPackage != null;
        public bool ShowAdvancedCompanionActivationPlaceholder => !HasAdvancedCompanionActivationPackages;

        void UpdateAdvancedCompanionActivationContext()
        {
            AdvancedCompanionActivationPackages.Clear();

            if (!Project_Service.Instance.IsProjectLoaded
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 213
                || !Directory.Exists(Project_Service.Instance.Path_Btl))
            {
                SelectedAdvancedCompanionActivationPackage = null;
                OnPropertyChanged(nameof(HasAdvancedCompanionActivationPackages));
                OnPropertyChanged(nameof(HasSelectedAdvancedCompanionActivationPackage));
                OnPropertyChanged(nameof(CanOpenAdvancedCompanionActivationEditor));
                OnPropertyChanged(nameof(ShowAdvancedCompanionActivationPlaceholder));
                return;
            }

            foreach (string battleId in AdvancedCompanionActivationBattleIds)
            {
                string battlePath;
                try { battlePath = Project_Service.Instance.GetPathBattle(battleId); }
                catch { continue; }

                if (!File.Exists(battlePath))
                    continue;

                BattleCompanionActivation_File activation = BattleCompanionActivation_File.ReadFromBattleBin(
                    battleId,
                    File.ReadAllBytes(battlePath),
                    ResolveMonsterBinForAdvancedCompanionActivation);

                AdvancedCompanionActivationPackages.Add(new AiAdvancedCompanionActivationPackageVm(activation));
            }

            AiAdvancedCompanionActivationPackageVm? preferred =
                AdvancedCompanionActivationPackages.FirstOrDefault(package => package.HasRecognizedPackage)
                ?? AdvancedCompanionActivationPackages.FirstOrDefault(package => package.IsDriftSentinel)
                ?? AdvancedCompanionActivationPackages.FirstOrDefault();
            SelectedAdvancedCompanionActivationPackage = preferred;

            OnPropertyChanged(nameof(HasAdvancedCompanionActivationPackages));
            OnPropertyChanged(nameof(HasSelectedAdvancedCompanionActivationPackage));
            OnPropertyChanged(nameof(CanOpenAdvancedCompanionActivationEditor));
            OnPropertyChanged(nameof(ShowAdvancedCompanionActivationPlaceholder));
        }

        static byte[]? ResolveMonsterBinForAdvancedCompanionActivation(int monsterId)
        {
            try
            {
                string path = Project_Service.Instance.GetPathMon(monsterId);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class AiAdvancedCompanionActivationPackageVm
    {
        public AiAdvancedCompanionActivationPackageVm(BattleCompanionActivation_File activation)
        {
            BattleId = activation.BattleId;
            BattleTokenLabel = activation.BattleTokenLabel;
            FormationSummary = activation.FormationSummary;
            CoverageLabel = string.IsNullOrWhiteSpace(activation.ActivationCoverageSummary)
                ? activation.FormationSummary
                : activation.ActivationCoverageSummary;
            HumanSummary = activation.HumanSummary;
            Rows = activation.Rows;
            WriterPolicy = activation.WriterPolicy;
            HasRecognizedPackage = activation.HasRecognizedPackage;
            IsDriftSentinel =
                activation.Notes.Any(note => note.Contains("drift/collision sentinel", StringComparison.OrdinalIgnoreCase))
                || activation.HumanSummary.Contains("drift/collision", StringComparison.OrdinalIgnoreCase);

            WriterPolicy = HasRecognizedPackage
                ? "battle-backed narrow writer · only slot1/slot2 of the formation · 0x408A still without universal writer"
                : IsDriftSentinel
                    ? Strings.U_Ai_M213WriterBlocked
                    : Strings.U_Ai_M213PartialPackage;

            StatusLabel = HasRecognizedPackage
                ? Strings.U_Ai_M213ProvenPackage
                : IsDriftSentinel
                    ? "drift sentinel"
                    : activation.Rows.Count > 0 || activation.SelectedBattleTokenKnown || activation.FormationHasPreseededCompanions
                        ? Strings.U_Ai_M213Partial
                        : Strings.U_Ai_M213NoPackage;

            NotesSummary = activation.Notes.Count == 0
                ? Strings.U_Ai_M213NoExtraNotes
                : string.Join(" ", activation.Notes);

            Guardrail = HasRecognizedPackage
                ? Strings.U_Ai_M213NarrowWriter
                : $"read-only · {NotesSummary}";
        }

        public string BattleId { get; }
        public string BattleTokenLabel { get; }
        public string FormationSummary { get; }
        public string CoverageLabel { get; }
        public string HumanSummary { get; }
        public IReadOnlyList<BattleCompanionActivationRow> Rows { get; }
        public string NotesSummary { get; }
        public string Guardrail { get; }
        public string WriterPolicy { get; }
        public string StatusLabel { get; }
        public bool HasRecognizedPackage { get; }
        public bool IsDriftSentinel { get; }
    }
}
