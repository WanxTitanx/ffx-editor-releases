using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 2 (preview/dry-run) output.
    //
    // A SinApplyPlan is the read-only, inspectable answer to:
    //   "IF this recipe were authorized, these are the steps/instructions/snippets I would TRY to build."
    // It applies NOTHING. There is deliberately NO field holding spliced monster bytes, no file path, no writer
    // handle — the plan stops at PLANNED instructions (snippet template expansions) plus honest blockers/gates.
    //
    // The whole point of the plan is honesty made structural: every step carries its evidence (command
    // eligibility, field/target shape, pattern source) and every plan carries the blockers + the rung ladder
    // it still has to climb. ApplyAllowed is hard-wired false for Gate 2.

    /// <summary>The promotion ladder (spec §5). A plan rises ONE rung per gate that actually ran. Gate 2 caps
    /// every plan at PreviewOnly.</summary>
    public enum SinPromotionRung
    {
        DesignOnly,
        PreviewOnly,
        OfflineEmittable,
        AiScriptLabClean,
        AeonReviewed,
        BackupReady,
        Rt2Proved,
        TemplatePublic,
    }

    /// <summary>How honest a payload is for SIN authoring. Proved = byte+RT2; Candidate = AI-perform category
    /// proven but the specific effect is RT2-pending; Blocked = item/metadata-only (GATTA) or non-AI category.</summary>
    public enum SinPayloadEligibility { Proved, Candidate, Blocked }

    public enum SinBadgeSeverity { Proof, Corpus, Caution, Blocked, Neutral }

    /// <summary>One evidence chip on a step (mirrors the AiBibleEvidence engine vocabulary). Detail explains the
    /// provenance in plain language; Severity drives colour/intent in any future UI.</summary>
    public sealed record SinEvidenceBadge(string Token, SinBadgeSeverity Severity, string Detail);

    /// <summary>A gate the plan must pass before it could ever write. Satisfied is false for every Gate-2 plan
    /// (this gate runs none of them); the list is the honest "distance to the Apply button".</summary>
    public sealed record SinRequiredGate(string Name, string What, bool Satisfied);

    /// <summary>One lowered node: the worker/trigger it would hook, the proven snippet it composes, the branch +
    /// stack shape, the planned instruction listing, and the evidence/blockers specific to it.</summary>
    public sealed record SinPlannedStep
    {
        public required string TriggerLabel { get; init; }
        public required string WorkerResolution { get; init; }
        public required AiSnippetKind Lowering { get; init; }
        public required string SnippetId { get; init; }
        public required string BranchShape { get; init; }
        public required string StackShape { get; init; }
        public required IReadOnlyList<string> PlannedInstructions { get; init; }
        public required IReadOnlyList<SinEvidenceBadge> Evidence { get; init; }
        public required IReadOnlyList<string> StepBlockers { get; init; }
        /// <summary>Worst payload eligibility seen in the step (null when the step has no command payload).</summary>
        public SinPayloadEligibility? PayloadEligibility { get; init; }

        // ---- Gate 3 (validation bridge) hook: the RAW instructions the planner lowered to. ----
        // The PlannedInstructions strings are for humans (and include synthetic D7/B0 placeholders whose jump
        // slots resolve only at apply-time). These two lists hold the actual AiInstructions — the bytes that
        // WOULD be emitted — so a validator can run AiScriptLab/AiValidator-compatible opcode/encoding/stack
        // checks against EXACTLY what the planner produced (single source of truth, no re-derivation). Optional /
        // additive: default empty, so every Gate-2 construction site keeps compiling and the dry-run is unchanged.

        /// <summary>Raw guard instructions: the boolean condition that must leave EXACTLY one bool for
        /// D7/POPXNCJMP. Empty for a linear step (no guard).</summary>
        public IReadOnlyList<AiInstruction> GuardOps { get; init; } = Array.Empty<AiInstruction>();

        /// <summary>Raw body instructions: the linear triplet sequence (linear step) or the guarded action
        /// (guarded step) — the proven-snippet expansions, with no synthetic D7/B0.</summary>
        public IReadOnlyList<AiInstruction> BodyOps { get; init; } = Array.Empty<AiInstruction>();
    }

    /// <summary>The full read-only plan for a recipe. Preview-only by construction.</summary>
    public sealed record SinApplyPlan
    {
        public required string RecipeId { get; init; }
        public required string RecipeDisplayName { get; init; }
        public required AiSinPresetTier Tier { get; init; }
        public required int Threat { get; init; }
        public required SinPromotionRung Rung { get; init; }
        public required bool ApplyAllowed { get; init; }
        public required IReadOnlyList<SinPlannedStep> Steps { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required IReadOnlyList<string> Blockers { get; init; }
        public required IReadOnlyList<SinRequiredGate> RequiredGates { get; init; }
        /// <summary>Entrypoint override from recipe. null = auto-detect (onTurn/CombatHandler).</summary>
        public int? DesiredEntrypoint { get; init; }

        /// <summary>True if ANY step claims a Proved command payload. Gate 2 must keep this false</summary> (no candidate
        /// gets promoted to authoring-ready by the dry-run).</summary>
        public bool ClaimsAnyProvedPayload => Steps.Any(s => s.PayloadEligibility == SinPayloadEligibility.Proved);

        /// <summary>True if ANY step routes a Blocked payload (item/non-AI). The plan keeps it but never applies.</summary>
        public bool HasBlockedPayload => Steps.Any(s => s.PayloadEligibility == SinPayloadEligibility.Blocked);
    }
}
