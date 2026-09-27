using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Services;
using System;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private string advancedCompanionActivationApplySummary =
            "No battle-backed writer armed in the current package.";

        string GetAdvancedCompanionActivationApplySummary(AiAdvancedCompanionActivationPackageVm? package)
        {
            if (package == null)
                return Strings.U_Ai_M213NoPackageSelected;

            if (!package.HasRecognizedPackage)
            {
                return package.IsDriftSentinel
                    ? "Drift sentinel detected: the writer m213 is blocked here to not promote corpus collision."
                    : Strings.U_Ai_M213PartialReadOnly;
            }

            return
                string.Format(Strings.U_Ai_M213WriterReady, package.BattleId) +
                "without rewriting the host's ATEL nor faking the universal semantics of 0x408A.";
        }

        public bool ApplyAdvancedCompanionActivationEdit(
            AiAdvancedCompanionActivationPackageVm? package,
            int slot1MonsterId,
            int slot2MonsterId)
        {
            if (package == null)
            {
                AdvancedCompanionActivationApplySummary = "No m213 package was selected for apply.";
                return false;
            }

            if (!package.HasRecognizedPackage)
            {
                AdvancedCompanionActivationApplySummary = GetAdvancedCompanionActivationApplySummary(package);
                return false;
            }

            string battlePath;
            try { battlePath = Project_Service.Instance.GetPathBattle(package.BattleId); }
            catch (Exception ex)
            {
                AdvancedCompanionActivationApplySummary = $"Nao consegui resolver a battle {package.BattleId}: {ex.Message}";
                return false;
            }

            if (!File.Exists(battlePath))
            {
                AdvancedCompanionActivationApplySummary = string.Format(Strings.U_Ai_M213BattleNotAccessible, package.BattleId);
                return false;
            }

            try
            {
                byte[] originalBattleBytes = File.ReadAllBytes(battlePath);
                Battle_File battle = Battle_File.Read(package.BattleId, originalBattleBytes);
                BattleCompanionActivation_File activation = BattleCompanionActivation_File.ReadFromBattleBin(
                    package.BattleId,
                    originalBattleBytes,
                    ResolveMonsterBinForAdvancedCompanionActivation);

                var request = new BattleCompanionActivationWriteRequest(
                    package.BattleId,
                    slot1MonsterId,
                    slot2MonsterId);

                if (!BattleCompanionActivationWriter.TryApplyCompanionFormationPatch(
                        battle,
                        activation,
                        request,
                        out BattleCompanionActivationWriteResult? result,
                        out string writerError)
                    || result == null)
                {
                    AdvancedCompanionActivationApplySummary = writerError;
                    return false;
                }

                FormationSlotWriter.SaveResult save = FormationSlotWriter.WriteLooseFile(
                    battlePath,
                    battle.OriginalBytes,
                    result.EditedBattleBytes,
                    battle.FormationSlotsOffset,
                    Battle_File.FormationSlotsLength);

                if (!save.Ok)
                {
                    AdvancedCompanionActivationApplySummary = save.Message;
                    return false;
                }

                string preferredBattleId = package.BattleId;
                UpdateAdvancedCompanionActivationContext();
                SelectedAdvancedCompanionActivationPackage = AdvancedCompanionActivationPackages.FirstOrDefault(candidate =>
                    candidate.BattleId.Equals(preferredBattleId, StringComparison.OrdinalIgnoreCase))
                    ?? AdvancedCompanionActivationPackages.FirstOrDefault();

                string slotSummary = string.Join(
                    " · ",
                    result.SlotEdits.Select(edit =>
                        $"slot{edit.SlotIndex}=m{edit.NewMonsterId:D3}"));
                string summary =
                    string.Format(Strings.U_Ai_M213SavedWithNewCompanions, package.BattleId, slotSummary) +
                    string.Format(Strings.U_Ai_M213GuardrailKept, save.Message);

                AdvancedCompanionActivationApplySummary = summary;
                RemoveActionSummary = summary;
                return true;
            }
            catch (Exception ex)
            {
                AdvancedCompanionActivationApplySummary = string.Format(Strings.U_Ai_M213BattleBackedWriterFailed, ex.Message);
                return false;
            }
        }
    }
}
