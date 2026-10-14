using System.Collections.Generic;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 6 (RT2 in-game pilot) structured result. OPERATOR-GATED, PREFLIGHT-ONLY here.
    //
    // The read-only, inspectable answer to one RT2-pilot PREFLIGHT attempt. It records what the preflight proved (the
    // cutout is RT2-pilot-eligible and its sandbox round-trip is byte-clean, so a live pilot is SAFE to stage), the
    // exact aim a live pilot would use (worker/entrypoint), the on-screen effect the operator must look for, and the
    // operator verdict — which is Pending until a human actually observes a real battle.
    //
    // Hard invariants, by construction (this layer never touches the game or a real file):
    //   - RealApplyDone   = NO   — no real monster_*.bin / game memory was ever written by this layer.
    //   - Rt2Confirmed    = NO   — on-screen behaviour is only proven by an operator, never headlessly.
    //   - PublicApplyAllowed = NO — sandbox-proved + preflight-ready is still not public authoring.
    //   - OperatorVerdict defaults to Pending. A PASS preflight with OperatorVerdict=Pending is the honest
    //     "RT2-pending" state — it is NOT a green claim that SIN-006 works in-game.

    public enum SinRt2Outcome
    {
        /// <summary>Recipe not eligible for an RT2 pilot (wrong id / wrong shape / no operator permission) — nothing staged.</summary>
        Blocked,
        /// <summary>Eligible, but the corpus fixture was absent — the sandbox proof / preflight was skipped.</summary>
        Skipped,
        /// <summary>Eligible, but the underlying Gate-5 sandbox round-trip did not reach a clean Applied result — preflight fails honestly.</summary>
        SandboxProofFailed,
        /// <summary>Eligible + sandbox-proved byte-clean. The cutout is staged for an operator-driven live pilot; awaiting on-screen observation.</summary>
        PreflightReady,
    }

    /// <summary>The operator's on-screen verdict for the live pilot. Defaults to Pending — only a human flips it.</summary>
    public enum SinRt2OperatorVerdict
    {
        Pending,
        ConfirmedOnScreen,
        NotObserved,
    }

    public sealed class SinRt2PilotResult
    {
        public required string RecipeId { get; init; }
        public required string RecipeDisplayName { get; init; }
        public required SinRt2Outcome Outcome { get; init; }
        public required string EligibilityReason { get; init; }

        public string? BlockReason { get; init; }
        public string? FixtureSource { get; init; }

        // aim: where a live pilot would inject (resolved from the Gate-5 sandbox proof).
        public int WorkerIndex { get; init; } = -1;
        public int EntrypointIndex { get; init; } = -1;
        public string? WorkerResolution { get; init; }

        // what the underlying Gate-5 sandbox round-trip proved (reused, not re-derived).
        public bool SandboxApplied { get; init; }
        public bool BackupProven { get; init; }
        public bool RestoreByteIdentical { get; init; }
        public bool UnexpectedDiff { get; init; }
        public int ExpectedAddedRows { get; init; }
        public int ActualAddedRows { get; init; }
        public int ActualModifiedRows { get; init; }
        public int ActualRemovedRows { get; init; }
        public bool ReParsedClean { get; init; }
        public bool AiScriptLabClean { get; init; }

        // hashes carried from the sandbox proof (the original / corpus must be UNCHANGED).
        public string? OriginalSha256Before { get; init; }
        public string? OriginalSha256After { get; init; }
        public string? CorpusSha256Before { get; init; }
        public string? CorpusSha256After { get; init; }

        // RT2-pilot specifics.
        /// <summary>The on-screen effect the operator must confirm in a real battle (plain language, RT2 = the only judge).</summary>
        public string? OnScreenEffectToObserve { get; init; }
        /// <summary>A revert path (the Gate-5 .prev.bak / re-extraction) is proven ready before any live apply.</summary>
        public bool RevertReady { get; init; }
        public SinRt2OperatorVerdict OperatorVerdict { get; init; } = SinRt2OperatorVerdict.Pending;

        // hard invariants.
        public bool RealApplyDone { get; init; }            // ALWAYS false — this layer never writes a real file/game
        public bool Rt2Confirmed { get; init; }             // ALWAYS false until an operator observes on-screen
        public bool PublicApplyAllowed { get; init; }       // ALWAYS false — invariant

        public IReadOnlyList<string> Notes { get; init; } = new List<string>();

        public string ToReportString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Recipe: {RecipeId} · {RecipeDisplayName}");
            sb.AppendLine("Gate: rt2-in-game-pilot (PREFLIGHT, operator-gated)");
            sb.AppendLine($"Eligibility: {EligibilityReason}");

            if (Outcome == SinRt2Outcome.PreflightReady)
            {
                sb.AppendLine("RT2 pilot preflight: READY (operator observation pending)");
                sb.AppendLine($"Fixture (read-only): {FixtureSource}");
                sb.AppendLine($"Aim: {WorkerResolution}");
                sb.AppendLine($"Sandbox round-trip applied: {YesNo(SandboxApplied)}");
                sb.AppendLine($"Backup proven before write: {YesNo(BackupProven)}");
                sb.AppendLine($"Restore byte-identical: {YesNo(RestoreByteIdentical)}");
                sb.AppendLine($"Unexpected diff: {YesNo(UnexpectedDiff)}");
                sb.AppendLine($"Diff rows: +{ActualAddedRows} ~{ActualModifiedRows} -{ActualRemovedRows} (expected +{ExpectedAddedRows} ~0 -0)");
                sb.AppendLine($"Re-parsed clean: {YesNo(ReParsedClean)}");
                sb.AppendLine($"AiScriptLab clean: {YesNo(AiScriptLabClean)}");
                sb.AppendLine($"Revert path ready: {YesNo(RevertReady)}");
                sb.AppendLine($"Operator verdict: {OperatorVerdict}");
                sb.AppendLine($"On-screen to observe: {OnScreenEffectToObserve}");
                sb.AppendLine($"Real apply done: {YesNo(RealApplyDone)}");
                sb.AppendLine($"RT2 confirmed: {YesNo(Rt2Confirmed)}");
                sb.AppendLine($"Public apply allowed: {YesNo(PublicApplyAllowed)}");
                sb.AppendLine($"  corpus source SHA  before/after : {CorpusSha256Before} / {CorpusSha256After}");
                sb.AppendLine($"  original copy SHA  before/after : {OriginalSha256Before} / {OriginalSha256After}");
            }
            else
            {
                string verb = Outcome switch
                {
                    SinRt2Outcome.Blocked => "RT2 pilot preflight: BLOCKED (not RT2-eligible)",
                    SinRt2Outcome.Skipped => "RT2 pilot preflight: SKIPPED (corpus fixture absent)",
                    SinRt2Outcome.SandboxProofFailed => "RT2 pilot preflight: FAILED (sandbox round-trip not clean)",
                    _ => "RT2 pilot preflight: NOT READY",
                };
                sb.AppendLine(verb);
                sb.AppendLine($"Reason: {BlockReason}");
                sb.AppendLine($"Real apply done: {YesNo(RealApplyDone)}");
                sb.AppendLine($"RT2 confirmed: {YesNo(Rt2Confirmed)}");
                sb.AppendLine($"Public apply allowed: {YesNo(PublicApplyAllowed)}");
            }

            foreach (string n in Notes)
                sb.AppendLine($"  note: {n}");

            return sb.ToString().TrimEnd();
        }

        static string YesNo(bool b) => b ? "YES" : "NO";
    }
}
