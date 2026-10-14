using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 4 (AEON diff preview). READ-ONLY / PREVIEW-ONLY.
    //
    // A SinAeonDiffPreview is the read-only answer to:
    //   "IF this validated plan were emitted against a real/controlled script, what diff would AEON show?"
    // It mirrors the AEON differ's vocabulary (AiScript_Diff: Added/Modified/Removed/Unchanged) but at the PLAN
    // level — built from a Gate-2 SinApplyPlan + its Gate-3 SinPlanValidationResult, WITHOUT loading a monster,
    // WITHOUT Rebuild/Splice, WITHOUT diffing against a real worker, WITHOUT a backup and WITHOUT writing a byte.
    //
    // Because no monster is loaded, there is no real insertion point and therefore no real before/after worker to
    // diff: every planned instruction is an ADDED row against a SYNTHETIC/CONTROL FIXTURE (no vanilla text at the
    // planned insertion point). MODIFIED/REMOVED are 0 by construction at this gate — resolving them needs the real
    // worker, which belongs to the later apply/backup gate. The model can still REPRESENT Modified/Removed honestly
    // so a future gate that DOES diff a real script can reuse the same shape; this gate just never produces them.
    //
    // ApplyAllowed is hard-wired false. "Clean for DESIGN nao e unlocked for WRITING" — and a clean diff PREVIEW is
    // not unlocked for writing either. SIN stays BLOCKED.

    /// <summary>One AEON-style change classification (mirrors AiDiffType). Context = a `;` annotation/marker line
    /// from the planned listing (shown for readability, never counted as a change).</summary>
    public enum SinAeonChangeKind { Added, Modified, Removed, Unchanged, Context }

    /// <summary>One row of the hypothetical diff: a change kind + the vanilla/current text (null for an ADDED
    /// instruction — there is nothing at the planned insertion point) + the proposed text.</summary>
    public sealed record SinAeonDiffPreviewRow
    {
        public required SinAeonChangeKind Kind { get; init; }
        /// <summary>Vanilla/current text at the diff position. Null for ADDED rows (no instruction there yet) and
        /// for Context rows. Populated only when a real before-image exists (a later gate's job).</summary>
        public string? VanillaOrCurrentText { get; init; }
        public required string ProposedText { get; init; }
        public bool IsContext => Kind == SinAeonChangeKind.Context;
    }

    /// <summary>The planned diff for one lowered step: which worker it would hook, the lowering/snippet, the diff
    /// target (synthetic at this gate), the classified rows, and the step's honest blockers/payload eligibility.</summary>
    public sealed record SinAeonDiffPreviewStep
    {
        public required string TriggerLabel { get; init; }
        public required string WorkerResolution { get; init; }
        public required AiSnippetKind Lowering { get; init; }
        public required string SnippetId { get; init; }
        /// <summary>"synthetic/control fixture" at Gate 4 — no real monster/worker is diffed.</summary>
        public required string DiffTarget { get; init; }
        public required string DiffTargetReason { get; init; }
        /// <summary>Step-level classification (ADDED when the step proposes >=1 instruction; Unchanged when the
        /// lowering produced nothing to diff).</summary>
        public required SinAeonChangeKind StepChangeKind { get; init; }
        public required IReadOnlyList<SinAeonDiffPreviewRow> Rows { get; init; }
        public required IReadOnlyList<string> StepBlockers { get; init; }
        public SinPayloadEligibility? PayloadEligibility { get; init; }

        public int Added => Rows.Count(r => r.Kind == SinAeonChangeKind.Added);
        public int Modified => Rows.Count(r => r.Kind == SinAeonChangeKind.Modified);
        public int Removed => Rows.Count(r => r.Kind == SinAeonChangeKind.Removed);
        /// <summary>Count of proposed instructions (the ADDED rows; Context annotation lines are excluded).</summary>
        public int PlannedInstructionCount => Added;
    }

    /// <summary>The full read-only AEON diff preview for a recipe. Preview-only by construction (ApplyAllowed=false).</summary>
    public sealed record SinAeonDiffPreview
    {
        public required string RecipeId { get; init; }
        public required string RecipeDisplayName { get; init; }
        public required AiSinPresetTier Tier { get; init; }
        public required int Threat { get; init; }
        /// <summary>One-line verdict carried over from Gate 3 (e.g. "structurally-valid, apply-blocked").</summary>
        public required string ValidationSummary { get; init; }
        /// <summary>Carried from the Gate-3 result: the bytecode the plan would emit is well-formed.</summary>
        public required bool StructurallyValid { get; init; }
        /// <summary>Hard-wired false: Gate 4 shows a diff, it does not apply one.</summary>
        public required bool ApplyAllowed { get; init; }
        /// <summary>True when Gate 3 tripped the HP% arithmetic regression guard: DIV(0x17) appeared where the
        /// HP% idiom requires MUL(0x16). The formatter prints the mandatory HP% notice.</summary>
        public required bool HpPercentMulRegressionGuard { get; init; }
        /// <summary>Compatibility alias for older callers; the contract is now a regression guard, not an expected DIV/MUL blocker.</summary>
        public bool HpPercentDivMulBlocked => HpPercentMulRegressionGuard;
        public required IReadOnlyList<SinAeonDiffPreviewStep> Steps { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required IReadOnlyList<string> Blockers { get; init; }

        public int TotalAdded => Steps.Sum(s => s.Added);
        public int TotalModified => Steps.Sum(s => s.Modified);
        public int TotalRemoved => Steps.Sum(s => s.Removed);
    }
}
