using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 4 AEON diff planner (READ-ONLY).
    //
    // Turns a Gate-2 SinApplyPlan + its Gate-3 SinPlanValidationResult into a SinAeonDiffPreview — the hypothetical
    // diff AEON would show IF the validated plan were emitted against a real/controlled script. It builds NOTHING on
    // disk and invokes NO writer/Rebuild/Splice/AiScript_Diff: it reads ONLY the two read-only inputs (the plan and
    // its validation result), which themselves never touched a monster.
    //
    // Honesty made structural:
    //   - it RESPECTS Gate 3: a structurally-valid plan can still be apply-blocked; a structural error shows up in
    //     the summary; ApplyAllowed stays false; a tripped HP% MUL(0x16) regression guard is carried through, never normalized as expected;
    //   - because no monster is loaded there is no real before-image, so every planned instruction is an ADDED row
    //     against a SYNTHETIC/CONTROL FIXTURE; MODIFIED/REMOVED are 0 (resolving them is the apply/backup gate's job);
    //   - it never promotes a candidate payload to proved (it only carries the plan/validation classifications).

    public static class SinAeonDiffPlanner
    {
        const string SyntheticTarget = "synthetic/control fixture";
        const string SyntheticReason =
            "real insertion point belongs to later apply/backup gate — no monster loaded; worker offset + jump-table slots resolve only at apply-time";

        /// <summary>Build the AEON diff preview from a plan and its Gate-3 validation. Read-only; never writes.</summary>
        public static SinAeonDiffPreview Plan(SinApplyPlan plan, SinPlanValidationResult validation)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(validation);

            var steps = plan.Steps.Select(BuildStep).ToList();

            bool hpMulRegressionGuard = validation.HasCode("divmul-integrity", SinPlanCheckSeverity.Blocker);
            string summary = !validation.StructurallyValid
                ? "structurally-invalid — would break Rebuild/the VM"
                : validation.ApplyAcceptable
                    ? "structurally-valid, apply-acceptable (UNEXPECTED at Gate 4 — investigate)"
                    : "structurally-valid, apply-blocked";

            return new SinAeonDiffPreview
            {
                RecipeId = plan.RecipeId,
                RecipeDisplayName = plan.RecipeDisplayName,
                Tier = plan.Tier,
                Threat = plan.Threat,
                ValidationSummary = summary,
                StructurallyValid = validation.StructurallyValid,
                ApplyAllowed = false,   // hard-wired: Gate 4 shows a diff, it does not apply one.
                HpPercentMulRegressionGuard = hpMulRegressionGuard,
                Steps = steps,
                Warnings = plan.Warnings,
                Blockers = BuildBlockers(plan),
            };
        }

        /// <summary>Convenience: run the whole read-only chain recipe -> plan -> validate -> AEON preview.</summary>
        public static SinAeonDiffPreview Plan(SinChainRecipe recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
            SinPlanValidationResult validation = SinPlanValidator.Validate(plan);
            return Plan(plan, validation);
        }

        // Build one step's diff rows from the planner's deterministic human listing. A `;`-prefixed line is a
        // Context annotation (guard/action marker) — shown but not counted; every other line is a proposed ADDED
        // instruction with no vanilla text at the planned insertion point.
        static SinAeonDiffPreviewStep BuildStep(SinPlannedStep step)
        {
            var rows = new List<SinAeonDiffPreviewRow>(step.PlannedInstructions.Count);
            foreach (string line in step.PlannedInstructions)
            {
                bool isComment = line.TrimStart().StartsWith(";", StringComparison.Ordinal);
                rows.Add(new SinAeonDiffPreviewRow
                {
                    Kind = isComment ? SinAeonChangeKind.Context : SinAeonChangeKind.Added,
                    VanillaOrCurrentText = isComment ? null : "<none at planned insertion point>",
                    ProposedText = line,
                });
            }

            int added = rows.Count(r => r.Kind == SinAeonChangeKind.Added);
            return new SinAeonDiffPreviewStep
            {
                TriggerLabel = step.TriggerLabel,
                WorkerResolution = step.WorkerResolution,
                Lowering = step.Lowering,
                SnippetId = step.SnippetId,
                DiffTarget = SyntheticTarget,
                DiffTargetReason = SyntheticReason,
                StepChangeKind = added > 0 ? SinAeonChangeKind.Added : SinAeonChangeKind.Unchanged,
                Rows = rows,
                StepBlockers = step.StepBlockers,
                PayloadEligibility = step.PayloadEligibility,
            };
        }

        // The honest "distance to a real diff/apply": the Gate-4-specific blockers first (this gate diffs nothing
        // real and applies nothing), then the plan's own carried-through blockers (no emitter / no AEON-against-real
        // / no RT2 / no backup …). Deduped so the result can never lie green.
        static IReadOnlyList<string> BuildBlockers(SinApplyPlan plan)
        {
            var list = new List<string>
            {
                "AEON diff is preview-only — diffed against a synthetic/control fixture, NOT a real worker",
                "no real apply — no writer / Rebuild / Splice / AiScript_Diff against a real script is reachable from this gate",
                "no backup gate — no .prev.bak of any target was (or could be) created here",
                "no RT2 — in-game behaviour unconfirmed",
            };
            list.AddRange(plan.Blockers);
            return list.Distinct().ToList();
        }
    }
}
