using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 5 (backup/apply sandbox) ELIGIBILITY evaluator. READ-ONLY decision.
    //
    // Answers exactly one question, WITHOUT touching a byte on disk:
    //   "Is this plan allowed to be applied to a SANDBOX COPY (never the original, never public authoring)?"
    //
    // A sandbox apply is a separate, explicit, operator-gated permission that is DECOUPLED from public authoring:
    // SinPlanValidationResult.ApplyAcceptable stays FALSE (public authoring is still blocked) even when a sandbox
    // apply is permitted. The permission to write a throwaway copy is NOT the permission to ship.
    //
    // The conditions mirror the PENANCE Gate-5 contract (the ten preconditions). Conditions 1-8 are decided here;
    // conditions 9 (target is a copy under work/) and 10 (backup created before the write) are enforced by
    // SinSandboxApplySession at apply-time and are surfaced as session guards.
    //
    // Honest blocker model: every Gate-2 plan carries blockers. MOST of them are the "scaffolding" blockers that
    // Gate 5 is precisely in the business of clearing (no emitter yet / no backup yet / no AEON-against-real yet /
    // no RT2 yet / no monster loaded / the unsatisfied rung ladder). Those do NOT block a sandbox apply. What DOES
    // block it is any blocker OUTSIDE that expected scaffolding set — a real per-step problem: a candidate/blocked
    // command payload, the HP% arithmetic regression guard (DIV 0x17 where MUL 0x16 is required), an
    // offline-unverifiable guarded jump-slot, a non-Self RT2-pending target. Those keep the recipe out of the sandbox. "Clean for DESIGN nao e unlocked for
    // WRITING" — and not for sandbox writing either, unless the recipe is genuinely free of real blockers.

    /// <summary>The read-only verdict: may this plan be applied to a sandbox copy, and (if not) why not.</summary>
    public sealed record SinSandboxEligibility(bool Permitted, string Reason, IReadOnlyList<string> FailedConditions)
    {
        /// <summary>True when the HP% MUL(0x16) regression guard is the (or a) reason the recipe is refused.
        /// Drives the mandatory HP% regression notice in the result.</summary>
        public bool HpPercentMulRegressionGuard { get; init; }
        /// <summary>Compatibility alias for older callers; the contract is now a regression guard, not an expected DIV/MUL blocker.</summary>
        public bool BlockedByDivMul => HpPercentMulRegressionGuard;
    }

    public static class SinSandboxApplyGate
    {
        // The ONLY blocker codes that are "expected scaffolding" (things Gate 5 itself resolves). Any blocker whose
        // code is NOT in this set is a real, recipe-level reason to refuse the sandbox apply.
        //   plan-blocker : the six fixed GlobalBlockers (no emitter/validation/AEON/RT2/backup/monster yet).
        //   rung-ladder  : the unsatisfied promotion ladder above preview-only.
        internal static readonly HashSet<string> ModBakeIgnorableBlockerCodes = new(StringComparer.Ordinal)
        {
            "payload-candidate",
            "jump-slots",
            "step-blocker",
        };

        internal static readonly HashSet<string> ExpectedScaffoldingBlockerCodes = new(StringComparer.Ordinal)
        {
            "plan-blocker",
            "rung-ladder",
        };

        /// <summary>Evaluate conditions 1-8. Pure: reads only the plan/validation/preview + the explicit sandbox
        /// permission flag. Never writes, never loads a monster.</summary>
        public static SinSandboxEligibility Evaluate(
            SinApplyPlan plan,
            SinPlanValidationResult validation,
            SinAeonDiffPreview preview,
            bool allowSandboxApply)
        {
            var failed = new List<string>();

            // 1. a plan exists.
            if (plan is null) failed.Add("no SinApplyPlan");

            // 2. structurally valid (the bytecode it would emit is well-formed).
            if (validation is null) failed.Add("no SinPlanValidationResult");
            else if (!validation.StructurallyValid) failed.Add("plan is NOT structurally valid (would break Rebuild/the VM)");

            // 3. public authoring stays blocked: ApplyAcceptable must remain false (Gate-3 contract).
            if (validation is not null && validation.ApplyAcceptable)
                failed.Add("validation.ApplyAcceptable is TRUE — public authoring must stay blocked at the sandbox gate");

            // 4. a SEPARATE, explicit sandbox permission (decoupled from public ApplyAcceptable).
            if (!allowSandboxApply)
                failed.Add("AllowSandboxApply is false — no explicit operator sandbox permission");

            // 5. an AEON diff preview exists.
            if (preview is null) failed.Add("no SinAeonDiffPreview");

            // 6. the diff preview is the expected/conservative shape (ADDED-only, well-formed, never apply-allowed).
            if (preview is not null &&
                !(preview.StructurallyValid && !preview.ApplyAllowed
                  && preview.TotalModified == 0 && preview.TotalRemoved == 0 && preview.TotalAdded >= 1))
                failed.Add("AEON preview is not the expected conservative ADDED-only shape");

            // 7. + 8. no critical (non-scaffolding) blocker — this also covers a tripped HP% MUL regression guard.
            bool hpMulRegressionGuard = false;
            if (validation is not null)
            {
                List<SinPlanCheck> critical = validation.Blockers
                    .Where(c => !ExpectedScaffoldingBlockerCodes.Contains(c.Code))
                    .OrderByDescending(c => c.Code == "divmul-integrity")   // surface the mandatory HP% MUL regression guard first
                    .ToList();
                foreach (SinPlanCheck c in critical)
                {
                    if (c.Code == "divmul-integrity")
                    {
                        hpMulRegressionGuard = true;
                        failed.Add("HP% arithmetic regression guard tripped: emitted DIV(0x17) where MUL(0x16) is required — " +
                                   "fix the emitter and re-run gates before sandbox apply");
                    }
                    else
                    {
                        failed.Add($"critical blocker [{c.Code}]: {c.Message}");
                    }
                }
            }

            bool permitted = failed.Count == 0;
            string reason = permitted
                ? "all sandbox preconditions met (structurally valid · public authoring still blocked · explicit sandbox permission · AEON ADDED-only · no critical blocker)"
                : string.Join("; ", failed);

            return new SinSandboxEligibility(permitted, reason, failed) { HpPercentMulRegressionGuard = hpMulRegressionGuard };
        }
    }
}
