using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

internal partial class MonsterAiEditor_DataModel
{
    public bool CanBakeSelectedUni => SinPostActionPresetCatalog.Find(SelectedSinPreset?.Id) != null;

    public bool ApplySinPostActionPreset(string presetId)
    {
        if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
        { SinCatalogSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_b163f007; return false; }
        if (HasPendingAuthoringEdits)
        { SinCatalogSummary = Strings.AiAdvancedPendingEdits; return false; }
        try
        {
            SinPostActionPreset recipe = SinPostActionPresetCatalog.Find(presetId)
                ?? throw new InvalidOperationException($"No post-action UNI recipe for '{presetId}'.");
            if (recipe.Skills.Count > 0)
            {
                Project_Service project = Project_Service.Instance;
                if (!project.IsProjectLoaded || !File.Exists(project.Path_KernelMonMagic2Us))
                { SinCatalogSummary = Strings.U_Ai_UniProjectRequired; return false; }
                var rows = Ability_Command.ReadList(File.ReadAllBytes(project.Path_KernelMonMagic2Us), hasExtraInfo: false);
                foreach (SinUniSkill skill in recipe.Skills)
                {
                    int index = skill.Operand & 0x0FFF;
                    string name = index < rows.Count
                        ? FfxEncoding.DecodeScript(rows[index].NameScriptBytes).GetString(FfxEncoding.UsDecoder).Trim()
                        : string.Empty;
                    if (!name.Equals(skill.Name, StringComparison.Ordinal))
                    {
                        SinCatalogSummary = string.Format(Strings.U_Ai_UniSkillMissing, skill.Name, skill.Operand);
                        return false;
                    }
                }
            }
            byte[] monster = File.ReadAllBytes(selectedPath);
            if (!AiScript_File.SliceAiFileFromMonster(monster)!.SequenceEqual(selectedScript.OriginalAiFileBytes))
                throw new InvalidOperationException("AI changed on disk; reload the monster before baking.");
            if (!SinPostActionPresetWriter.TryBuild(monster, Path.GetFileNameWithoutExtension(selectedPath), presetId,
                    out SinPostActionEdit? edit, out string error))
                throw new InvalidOperationException(error);
            // Reuse the editor's stale-file check, mandatory backup and atomic promotion.
            AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, edit!.AiFile);
            if (!ReloadSelectedFromDisk())
                throw new InvalidOperationException(string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath)));
            SinCatalogSummary = string.Format(Strings.U_Ai_UniBakeApplied, presetId, edit.NativeActionCount);
            return true;
        }
        catch (Exception ex)
        {
            SinCatalogSummary = string.Format(Strings.U_Ai_UniBakeAborted, ex.Message);
            return false;
        }
    }
}
