using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private string scaleFactorText =
            SinScaleOpener.DefaultUniformScale.ToString(CultureInfo.InvariantCulture);

        [ObservableProperty] private string uniformScaleSummary =
            "scaleOwnSize (ATEL 0x7028): no uniform scale detected — insert uses 1.0 × factor; existing scale multiplies the pool.";

        public void RefreshUniformScaleSummary()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            {
                UniformScaleSummary = Strings.U_Ai_ScaleSelectMonster;
                return;
            }

            if (!SinScaleOpener.ScriptHasScaleOwnSize(selectedScript))
            {
                UniformScaleSummary =
                    "No scaleOwnSize in script — Apply inserts PUSHF×3 after DeathAnimation (Flan default). Implicit ×1.0 factor.";
                return;
            }

            var sites = SinScaleOpener.EnumerateUniformScaleOwnSize(selectedScript);
            if (sites.Count == 0)
            {
                UniformScaleSummary = Strings.U_Ai_ScaleNoUniformBlock;
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            string vals = string.Join(", ", sites.Select(s => $"pool[{s.PoolIndex}]={s.Value.ToString(ci)}×"));
            UniformScaleSummary = string.Format(Strings.U_Ai_ScaleCurrent, vals);
        }

        public void ApplyUniformScaleMultiplier(float factor)
        {
            ScaleFactorText = factor.ToString(CultureInfo.InvariantCulture);
            ApplyUniformScale();
        }

        public void ApplyUniformScale()
        {
            if (!PresetReady())
                return;

            if (!TryParseScaleFactor(ScaleFactorText, out float factor, out string parseErr))
            {
                PresetSummary = parseErr;
                return;
            }

            if (HasPendingAuthoringEdits)
            { PresetSummary = Strings.AiAdvancedPendingEdits; return; }

            SinMonsterEmitResult emit;
            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath!);
                AiScriptFile script = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);
                emit = SinScaleOpener.ScriptHasScaleOwnSize(script)
                    ? SinScaleOpener.TryReplaceUniformScaleOwnSize(monster, factor)
                    : SinScaleOpener.TryEmitInlineAfterDeathAnimation(monster, factor);
                if (!emit.Ok)
                {
                    PresetSummary = string.Format(Strings.U_Ai_ScaleAbortedEmit, emit.Error);
                    return;
                }
                WriteMonsterWithBackup(emit.EditedMonster!);
            }
            catch (Exception ex)
            {
                PresetSummary = string.Format(Strings.U_Ai_ScaleAborted, ex.Message);
                return;
            }

            if (!ReloadSelectedFromDisk())
            { PresetSummary = string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath)); return; }
            RefreshUniformScaleSummary();
            PresetSummary =
                string.Format(Strings.U_Ai_ScaleApplied, factor.ToString(CultureInfo.InvariantCulture), Path.GetFileName(selectedPath), emit.WorkerResolution) + " " +
                Strings.U_Ai_ScaleSaved;
        }

        static bool TryParseScaleFactor(string text, out float factor, out string error)
        {
            factor = 0f;
            error = string.Empty;
            if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out factor)
                && !float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out factor))
            {
                error = Strings.U_Ai_ScaleFactorInvalid;
                return false;
            }
            if (!float.IsFinite(factor))
            {
                error = Strings.U_Ai_ScaleFactorInvalid;
                return false;
            }
            if (factor <= 0f)
            {
                error = Strings.F2_factor_must_be_0_69167ab9;
                return false;
            }
            return true;
        }
    }
}
