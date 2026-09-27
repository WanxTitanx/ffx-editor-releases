using System.Text;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 4 deterministic, read-only text rendering of a SinAeonDiffPreview.
    //
    // Pure formatting: same preview in -> same string out (no time/random/env). This is the auditable artifact a
    // human (or the --sin-aeon-preview-rt0 gate) reads to see EXACTLY which diff the recipe WOULD show — and that
    // it is preview-only (it ends "Apply allowed: NO"). For the HP% pilot it prints the regression/proof notice
    // so a legacy DIV(0x17) emission can never be treated as expected; HP% multiply must be MUL(0x16).

    public static class SinAeonPreviewFormatter
    {
        public static string Render(SinAeonDiffPreview preview)
        {
            var sb = new StringBuilder();
            sb.Append($"Recipe: {preview.RecipeId} · {preview.RecipeDisplayName}\n");
            sb.Append($"Tier: {preview.Tier} · Threat: {preview.Threat}/10\n");
            sb.Append("Status: AEON-preview-only\n");
            sb.Append($"Validation: {preview.ValidationSummary}\n\n");

            sb.Append("Would diff:\n");
            for (int s = 0; s < preview.Steps.Count; s++)
            {
                SinAeonDiffPreviewStep step = preview.Steps[s];
                sb.Append($"- step {s + 1}/{preview.Steps.Count}\n");
                sb.Append($"  - worker: {step.TriggerLabel}\n");
                sb.Append($"  - worker resolution: {step.WorkerResolution}\n");
                sb.Append($"  - lowering: {step.Lowering}  (snippet: {step.SnippetId})\n");
                sb.Append($"  - diff-target: {step.DiffTarget}\n");
                sb.Append($"  - reason: {step.DiffTargetReason}\n");
                sb.Append($"  - change kind: {step.StepChangeKind.ToString().ToUpperInvariant()}\n");
                sb.Append($"  - planned instruction count: {step.PlannedInstructionCount}\n");
                if (step.PayloadEligibility is SinPayloadEligibility elig)
                    sb.Append($"  - payload eligibility: {elig.ToString().ToLowerInvariant()}\n");
                sb.Append("  - vanilla/current: <none at planned insertion point>\n");
                sb.Append("  - proposed:\n");
                foreach (SinAeonDiffPreviewRow row in step.Rows)
                {
                    string tag = row.Kind switch
                    {
                        SinAeonChangeKind.Added => "+ ",
                        SinAeonChangeKind.Modified => "~ ",
                        SinAeonChangeKind.Removed => "- ",
                        _ => "  ",   // Context / Unchanged
                    };
                    sb.Append($"      {tag}{row.ProposedText}\n");
                }
                if (step.StepBlockers.Count > 0)
                {
                    sb.Append("  - step blockers:\n");
                    foreach (string b in step.StepBlockers)
                        sb.Append($"      - {b}\n");
                }
            }

            sb.Append("\nAEON classification:\n");
            sb.Append($"- ADDED: {preview.TotalAdded}\n");
            sb.Append($"- MODIFIED: {preview.TotalModified}\n");
            sb.Append($"- REMOVED: {preview.TotalRemoved}\n");

            // Mandatory HP% notice (SIN-010): DIV(0x17) is a regression if it appears; MUL(0x16) is expected.
            if (preview.HpPercentMulRegressionGuard)
            {
                sb.Append("\nHP% guard arithmetic:\n");
                sb.Append("Regression guard: HP% emitted DIV(0x17) where MUL(0x16) is required.\n");
                sb.Append("MUL(0x16) is the expected opcode for HP% multiply.\n");
                sb.Append("No public apply from this preview gate.\n");
                sb.Append("RT2/operator gate still needs proof for HP% guarded authoring.\n");
            }

            sb.Append("\nBlockers:\n");
            foreach (string b in preview.Blockers)
                sb.Append($"- {b}\n");

            if (preview.Warnings.Count > 0)
            {
                sb.Append("\nWarnings:\n");
                foreach (string w in preview.Warnings)
                    sb.Append($"- {w}\n");
            }

            sb.Append($"\nApply allowed: {(preview.ApplyAllowed ? "YES" : "NO")}\n");
            return sb.ToString();
        }
    }
}
