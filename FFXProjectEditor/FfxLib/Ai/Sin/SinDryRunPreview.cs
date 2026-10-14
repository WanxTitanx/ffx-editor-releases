using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 2 deterministic, read-only text rendering of a SinApplyPlan.
    //
    // Pure formatting: same plan in -> same string out (no time/random/env). This is the auditable artifact a
    // human (or a CLI gate) reads to see EXACTLY what the recipe WOULD try to build — and that it is preview-only.

    public static class SinDryRunPreview
    {
        public static string Render(SinApplyPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append($"Recipe: {plan.RecipeId} · {plan.RecipeDisplayName}\n");
            sb.Append($"Tier: {plan.Tier} · Threat: {plan.Threat}/10\n");
            sb.Append($"Status: preview-only (rung: {Rung(plan.Rung)})\n\n");

            sb.Append("Would emit:\n");
            for (int s = 0; s < plan.Steps.Count; s++)
            {
                SinPlannedStep step = plan.Steps[s];
                sb.Append($"- step {s + 1}/{plan.Steps.Count}\n");
                sb.Append($"  - worker: {step.TriggerLabel}\n");
                sb.Append($"  - worker resolution: {step.WorkerResolution}\n");
                sb.Append($"  - lowering: {step.Lowering}  (snippet: {step.SnippetId})\n");
                sb.Append($"  - branch shape: {step.BranchShape}\n");
                sb.Append($"  - stack shape: {step.StackShape}\n");
                if (step.PayloadEligibility is SinPayloadEligibility elig)
                    sb.Append($"  - payload eligibility: {elig.ToString().ToLowerInvariant()}\n");
                sb.Append("  - planned instructions:\n");
                foreach (string line in step.PlannedInstructions)
                    sb.Append($"      {line}\n");
                sb.Append("  - evidence:\n");
                foreach (SinEvidenceBadge b in step.Evidence)
                    sb.Append($"      [{b.Severity}] {b.Token}: {b.Detail}\n");
                if (step.StepBlockers.Count > 0)
                {
                    sb.Append("  - step blockers:\n");
                    foreach (string blk in step.StepBlockers)
                        sb.Append($"      - {blk}\n");
                }
            }

            sb.Append("\n- blockers:\n");
            foreach (string b in plan.Blockers)
                sb.Append($"  - {b}\n");

            if (plan.Warnings.Count > 0)
            {
                sb.Append("- warnings:\n");
                foreach (string w in plan.Warnings)
                    sb.Append($"  - {w}\n");
            }

            sb.Append("- required gates (rung ladder; none satisfied at Gate 2):\n");
            foreach (SinRequiredGate g in plan.RequiredGates)
                sb.Append($"  - [{(g.Satisfied ? "x" : " ")}] {g.Name}: {g.What}\n");

            sb.Append($"\nApply allowed: {(plan.ApplyAllowed ? "YES" : "NO")}\n");
            return sb.ToString();
        }

        static string Rung(SinPromotionRung r) => r switch
        {
            SinPromotionRung.DesignOnly => "design-only",
            SinPromotionRung.PreviewOnly => "preview-only",
            SinPromotionRung.OfflineEmittable => "offline-emittable",
            SinPromotionRung.AiScriptLabClean => "AiScriptLab-clean",
            SinPromotionRung.AeonReviewed => "AEON-reviewed",
            SinPromotionRung.BackupReady => "backup-ready",
            SinPromotionRung.Rt2Proved => "RT2-proved",
            SinPromotionRung.TemplatePublic => "template-public",
            _ => r.ToString(),
        };
    }
}
