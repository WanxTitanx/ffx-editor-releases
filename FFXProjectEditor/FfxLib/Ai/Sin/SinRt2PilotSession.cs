using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 6 (RT2 in-game pilot) PREFLIGHT orchestrator. OPERATOR-GATED, HEADLESS-SAFE.
    //
    // Gate 6 asks: "Does the already-sandbox-proved SIN-006 cutout produce the expected effect in a REAL battle, and
    // come back to the original state with no residue?" That question can ONLY be answered by a human operator
    // watching the screen. This class does the part that CAN be done safely and headlessly — the PREFLIGHT — and
    // stops at the boundary where a human + the live game are required:
    //
    //   RT2 eligibility (SinRt2PilotGate)        -> the cutout is SIN-006-only, Self-only, no payload, no divmul, and
    //                                               the operator gave the SEPARATE AllowRt2InGamePilot permission.
    //   Gate-5 sandbox round-trip (REUSED)       -> SinSandboxApplySession proves emit + conservative diff + backup +
    //                                               apply-to-copy + restore byte-identical, original UNTOUCHED (hashes).
    //   stage the pilot artifact                 -> capture the aim (worker/entrypoint) and the on-screen effect the
    //                                               operator must verify, with the revert path proven ready.
    //   STOP                                     -> the live apply (probe injection into the running process / loaded
    //                                               copy) + the on-screen observation are an OPERATOR-DRIVEN step that
    //                                               is intentionally NOT implemented here. OperatorVerdict stays
    //                                               Pending → the honest result is RT2-pending, never a green claim.
    //
    // Nothing in this class writes a real game/user file or drives the dinput8 probe. The only disk it touches is the
    // throwaway copy under work/ created by the reused Gate-5 session (which restores it byte-identical and which the
    // RT0 gate cleans up). Sandbox-proved + preflight-ready is STILL NOT RT2-proved and STILL NOT public authoring.
    public static class SinRt2PilotSession
    {
        /// <summary>Run one RT2-pilot PREFLIGHT attempt. <paramref name="corpusFixturePath"/> is a real monster .bin
        /// read READ-ONLY; the reused Gate-5 session copies it under <paramref name="workDir"/> (MUST be under work/)
        /// and only ever writes the copy. Returns a structured, inspectable result whose OperatorVerdict is Pending.</summary>
        public static SinRt2PilotResult Preflight(
            string? corpusFixturePath,
            string workDir,
            SinChainRecipe recipe,
            bool allowRt2InGamePilot)
        {
            ArgumentNullException.ThrowIfNull(workDir);
            ArgumentNullException.ThrowIfNull(recipe);

            SinRt2Eligibility elig = SinRt2PilotGate.Evaluate(recipe, allowRt2InGamePilot);

            SinRt2PilotResult Base(SinRt2Outcome outcome, string? reason, IReadOnlyList<string>? notes = null) =>
                new()
                {
                    RecipeId = recipe.Id,
                    RecipeDisplayName = recipe.DisplayName,
                    Outcome = outcome,
                    EligibilityReason = elig.Reason,
                    BlockReason = reason,
                    FixtureSource = corpusFixturePath,
                    RealApplyDone = false,
                    Rt2Confirmed = false,
                    PublicApplyAllowed = false,
                    OperatorVerdict = SinRt2OperatorVerdict.Pending,
                    Notes = notes ?? new List<string>(),
                };

            // --- RT2 eligibility (strictly narrower than the sandbox gate) ---
            if (!elig.Permitted)
                return Base(SinRt2Outcome.Blocked, elig.Reason);

            // --- reuse the PROVEN Gate-5 sandbox round-trip for the safety proof (copy-only, restores byte-identical) ---
            // The sandbox permission is implied by the (stricter) RT2 permission we already required above; we do NOT
            // re-expose it as a separate knob here.
            SinSandboxApplyResult sandbox = SinSandboxApplySession.Run(corpusFixturePath, workDir, recipe, allowSandboxApply: true);

            var notes = new List<string>(sandbox.Notes)
            {
                "RT2 in-game apply + on-screen observation are an OPERATOR-DRIVEN step and are NOT performed here; " +
                "this preflight only proves the cutout is safe to stage. OperatorVerdict stays Pending (RT2-pending).",
            };

            switch (sandbox.Outcome)
            {
                case SinSandboxOutcome.Skipped:
                    return Base(SinRt2Outcome.Skipped,
                        "RT2-eligible, but no monster fixture corpus is present — sandbox proof / preflight skipped " +
                        "(run on a machine with the extracted corpus to exercise the harness)", notes);

                case SinSandboxOutcome.Applied:
                    break; // the only outcome from which a preflight can proceed.

                default:
                    return Base(SinRt2Outcome.SandboxProofFailed,
                        $"underlying Gate-5 sandbox round-trip did not reach a clean Applied result " +
                        $"(outcome={sandbox.Outcome}: {sandbox.BlockReason}) — RT2 preflight refuses to proceed", notes);
            }

            // --- sandbox round-trip is clean: stage the RT2 pilot artifact (aim + on-screen expectation) ---
            string onScreen =
                $"On the monster's first turn (OnTurn → worker {sandbox.WorkerIndex}, entrypoint {sandbox.EntrypointIndex}), " +
                "Haste (0x38), Protect (0x31) and Shell (0x30) should turn ON on the monster ITSELF (Self 0xFFF3). " +
                "Verify the three status icons appear on the monster at battle start; nothing should target or affect the party. " +
                "Placement/timing (the appended always-true-guarded block firing at the right OnTurn moment) and the actual " +
                "writeChrProperty 0x7018 semantics are the HYPOTHESES this pilot exists to confirm — they are NOT yet proven.";

            return new SinRt2PilotResult
            {
                RecipeId = recipe.Id,
                RecipeDisplayName = recipe.DisplayName,
                Outcome = SinRt2Outcome.PreflightReady,
                EligibilityReason = elig.Reason,
                FixtureSource = corpusFixturePath,
                WorkerIndex = sandbox.WorkerIndex,
                EntrypointIndex = sandbox.EntrypointIndex,
                WorkerResolution = sandbox.WorkerResolution,
                SandboxApplied = sandbox.AppliedToSandbox,
                BackupProven = sandbox.BackupCreated,
                RestoreByteIdentical = sandbox.RestoreByteIdentical,
                UnexpectedDiff = sandbox.UnexpectedDiff,
                ExpectedAddedRows = sandbox.ExpectedAddedRows,
                ActualAddedRows = sandbox.ActualAddedRows,
                ActualModifiedRows = sandbox.ActualModifiedRows,
                ActualRemovedRows = sandbox.ActualRemovedRows,
                ReParsedClean = sandbox.ReParsedClean,
                AiScriptLabClean = sandbox.AiScriptLabClean,
                OriginalSha256Before = sandbox.OriginalSha256Before,
                OriginalSha256After = sandbox.OriginalSha256After,
                CorpusSha256Before = sandbox.CorpusSha256Before,
                CorpusSha256After = sandbox.CorpusSha256After,
                OnScreenEffectToObserve = onScreen,
                // the revert path is the same proven Gate-5 .prev.bak/byte-identical restore the sandbox just demonstrated.
                RevertReady = sandbox.BackupCreated && sandbox.RestoreByteIdentical,
                OperatorVerdict = SinRt2OperatorVerdict.Pending,
                RealApplyDone = false,
                Rt2Confirmed = false,
                PublicApplyAllowed = false,
                Notes = notes,
            };
        }
    }
}
