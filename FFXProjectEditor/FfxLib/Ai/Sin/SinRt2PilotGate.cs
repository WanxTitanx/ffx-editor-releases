using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 6 (RT2 in-game pilot) ELIGIBILITY evaluator. READ-ONLY decision.
    //
    // Answers exactly one question, WITHOUT touching a byte on disk and WITHOUT touching the running game:
    //   "Is this recipe allowed to enter an OPERATOR-GATED RT2 in-game pilot (the cited cutout only, reversible)?"
    //
    // RT2 eligibility is STRICTLY NARROWER than the Gate-5 sandbox eligibility. Gate 5 proved a validated plan can
    // be applied+undone byte-identically on a throwaway COPY. Gate 6 asks whether that already-sandbox-proved cutout
    // may be observed in a real battle by a human operator. Because an in-game pilot is the first time SIN bytes run
    // on-screen, the bar is the highest in the chain:
    //
    //   1. an EXPLICIT, SEPARATE operator permission (AllowRt2InGamePilot) — decoupled from AllowSandboxApply and
    //      from public authoring. Sandbox permission is NOT pilot permission.
    //   2. the recipe id is on the tiny RT2 allowlist (today: SIN-006 ONLY). A backronym/displayname can never be the
    //      key (DONNA); the numeric/id allowlist is.
    //   3. the recipe's INTRINSIC shape is the proven-safe pilot shape (belt-and-suspenders against a mislabeled id):
    //         - exactly one node;
    //         - Trigger = OnTurn with the proven CombatHandler resolver (the only event with a resolver today);
    //         - Condition = Always (NO HP% guard → no HP%-specific RT2/operator proof needed for this pilot);
    //         - every Action is GrantChrProperty on the Self sentinel (NO command payload, NO non-Self target).
    //
    // Anything else — SIN-009 (PerformCommand candidate payload), SIN-010 (HpBelowPercent → RT2-pending HP% guard), a non-Self
    // target, a multi-node chain, a non-OnTurn trigger — is REFUSED here, before the sandbox round-trip is even run.
    //
    // This gate does NOT grant a real write and does NOT run the probe. A PASS here only means "this cutout is allowed
    // to be staged into an operator-driven RT2 pilot." The actual on-screen apply+observe is a separate operator step
    // (SinRt2PilotSession stages the preflight; the live probe injection + observation is operator-gated and NOT done
    // headlessly). "Clean for DESIGN nao e unlocked for WRITING" — and sandbox-proved is not RT2-proved.

    /// <summary>The read-only verdict: may this recipe enter an operator-gated RT2 in-game pilot, and (if not) why not.</summary>
    public sealed record SinRt2Eligibility(bool Permitted, string Reason, IReadOnlyList<string> FailedConditions)
    {
        /// <summary>True when the HP% MUL(0x16) regression guard or HP%-specific RT2/operator proof gate is relevant.</summary>
        public bool HpPercentMulRegressionGuard { get; init; }
        /// <summary>Compatibility alias for older callers; the contract is now a regression/proof guard, not an expected DIV/MUL blocker.</summary>
        public bool BlockedByDivMul => HpPercentMulRegressionGuard;
        /// <summary>True when a command payload (PerformCommand candidate) is a reason the recipe is refused.</summary>
        public bool BlockedByCommandPayload { get; init; }
        /// <summary>True when a non-Self target is a reason the recipe is refused.</summary>
        public bool BlockedByNonSelfTarget { get; init; }
    }

    public static class SinRt2PilotGate
    {
        /// <summary>The ONLY recipe ids allowed to enter an RT2 in-game pilot today. SIN-006 ("Véu de Bevelle") is the
        /// single pilot Gate 5 marked eligible and sandbox-applied. SIN-009/SIN-010 are deliberately absent.</summary>
        public static readonly IReadOnlySet<string> Rt2AllowlistIds = new HashSet<string>(StringComparer.Ordinal) { "SIN-006" };

        /// <summary>Evaluate RT2-pilot eligibility. Pure: reads only the recipe + the explicit pilot permission flag.
        /// Never loads a monster, never writes, never touches the game.</summary>
        public static SinRt2Eligibility Evaluate(SinChainRecipe recipe, bool allowRt2InGamePilot)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            var failed = new List<string>();
            bool hpMulRegressionGuard = false, cmdPayload = false, nonSelf = false;

            // 1. explicit, separate operator pilot permission (NOT the sandbox flag, NOT public authoring).
            if (!allowRt2InGamePilot)
                failed.Add("AllowRt2InGamePilot is false — no explicit operator in-game-pilot permission (separate from sandbox)");

            // 2. the recipe id is on the RT2 allowlist (today: SIN-006 only).
            if (!Rt2AllowlistIds.Contains(recipe.Id))
                failed.Add($"recipe id '{recipe.Id}' is not on the RT2 pilot allowlist ({string.Join(", ", Rt2AllowlistIds)})");

            // 3. intrinsic shape must be the proven-safe pilot shape (belt-and-suspenders against a mislabeled id).
            if (recipe.Nodes.Count != 1)
                failed.Add($"RT2 pilot requires exactly one node; recipe has {recipe.Nodes.Count}");
            else
            {
                SinChainNode node = recipe.Nodes[0];

                if (node.Trigger.Event != SinEvent.OnTurn || !node.Trigger.HasProvenResolver)
                    failed.Add($"trigger {node.Trigger.Event} has no proven resolver — only OnTurn/CombatHandler is RT2-aimable today");

                if (node.Condition is not SinCondition.Always)
                {
                    if (node.Condition is SinCondition.HpBelowPercent)
                    {
                        hpMulRegressionGuard = true;
                        failed.Add("condition is an HP% guard — MUL(0x16) is the required multiply opcode; " +
                                   "HP%-guarded RT2 authoring still needs its own operator proof before this pilot gate can admit it");
                    }
                    else
                    {
                        failed.Add($"condition {node.Condition.GetType().Name} is not Always — only an Always opener is RT2-eligible today");
                    }
                }

                if (node.Actions.Count == 0)
                    failed.Add("node has no actions");

                foreach (SinAction action in node.Actions)
                {
                    switch (action)
                    {
                        case SinAction.GrantChrProperty grant:
                            if (grant.Target != AiSnippetLibrary.SelfRef)
                            {
                                nonSelf = true;
                                failed.Add($"GrantChrProperty target 0x{grant.Target:X4} is not the Self sentinel 0x{AiSnippetLibrary.SelfRef:X4} — only Self is RT2-proved");
                            }
                            break;
                        case SinAction.PerformCommand perform:
                            cmdPayload = true;
                            failed.Add($"action is a command payload (PerformCommand operand 0x{perform.CommandOperand:X4}, force={perform.Force}) — " +
                                       "AI-perform payloads are CANDIDATE only and are not RT2-eligible (SIN-009 stays blocked)");
                            break;
                        default:
                            failed.Add($"action {action.GetType().Name} is not a Self chr-property grant — not RT2-eligible");
                            break;
                    }
                }
            }

            bool permitted = failed.Count == 0;
            string reason = permitted
                ? "all RT2-pilot preconditions met (explicit pilot permission · id on allowlist · single OnTurn/Always node · " +
                  "Self-only chr-property grants · no command payload · no HP% RT2-pending guard)"
                : string.Join("; ", failed);

            return new SinRt2Eligibility(permitted, reason, failed)
            {
                HpPercentMulRegressionGuard = hpMulRegressionGuard,
                BlockedByCommandPayload = cmdPayload,
                BlockedByNonSelfTarget = nonSelf,
            };
        }
    }
}
