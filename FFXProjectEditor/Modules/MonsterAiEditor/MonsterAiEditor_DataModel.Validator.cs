using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // VALIDATE surface — a pre-flight that runs AiValidator over the current AI Assembler edit and shows a
    // structured report (opcode-known, walk-closes, no dangling branch/entrypoint, operand ranges, RT0 self-check,
    // grow/shrink) BEFORE Save. SaveAssembler is made defensive: it refuses to write when the report has Errors,
    // so the user sees exactly which branch dangles / which opcode is bad instead of an opaque Rebuild exception.
    internal partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private string validationReport =
            "Validate: runs AiValidator in dry-run (Rebuild dry-run + checks opcodes/branches/ranges/RT0) — without writing to disk.";

        public void ValidateAssembler()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            {
                ValidationReport = Strings.U_Ai_ValidateSelectRealAiFile;
                return;
            }
            AiValidationReport report = AiValidator.Validate(selectedScript, BuildAssemblerInstrList());
            ValidationReport = report.ToReportString();
            AssemblerSummary = report.IsValid
                ? string.Format(Strings.U_Ai_ValidationPassed, report.WarningCount)
                : string.Format(Strings.U_Ai_ValidationFailed, report.ErrorCount);
        }
    }
}
