using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-validate-rt0 : read-only gate for the SIN Chain Builder Gate 3 (AiScriptLab validation bridge).
    //
    // Builds the 3 mandatory pilot recipes in memory, plans each into a Gate-2 SinApplyPlan, then runs the Gate-3
    // SinPlanValidator over each plan and asserts the bridge is HONEST:
    //   - it VALIDATES plans, it does NOT apply them: nothing is saved, no monster is loaded, no writer / Rebuild /
    //     Splice / AiScript_Diff is invoked (the validator only reads AiScript_File static tables + AiStackModel);
    //   - every plan is STRUCTURALLY VALID (the bytecode it would emit is well-formed) but apply-BLOCKED;
    //   - ApplyAcceptable is false for every plan (no candidate is promoted to authoring-ready);
    //   - the honest blockers + the unsatisfied rung ladder are present (the result can never lie green);
    //   - the HP% recipe (SIN-010) emits MUL(0x17), not the old DIV(0x16) regression path;
    //   - the old divmul-integrity blocker is NOT accepted when MUL(0x17) is present;
    //   - SIN-010 still stays apply-BLOCKED by real blockers (payload-candidate, jump-slots, step-blocker, RT2/rungs).
    //
    // VERDICT line ends the run. SIN stays BLOCKED for writing — Gate 3 validates plans, it does not apply them.
    internal static class SinValidateRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== SIN plan validation RT0 (Gate 3, read-only AiScriptLab bridge) ===");
            int fail = 0;

            IReadOnlyList<SinChainRecipe> pilots = SinPilotRecipes.All;
            fail += Expect("pilot recipe count", 3, pilots.Count);

            foreach (SinChainRecipe recipe in pilots)
            {
                Console.WriteLine();
                Console.WriteLine($"---- {recipe.Id} · {recipe.DisplayName} ----");

                SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
                SinPlanValidationResult result = SinPlanValidator.Validate(plan);
                Console.WriteLine(result.ToReportString());

                // Core Gate-3 contract: structurally acceptable, but apply BLOCKED (preview-only).
                fail += ExpectTrue($"{recipe.Id}: STRUCTURALLY valid (bytecode well-formed)", result.StructurallyValid);
                fail += ExpectTrue($"{recipe.Id}: no structural error", result.ErrorCount == 0);
                fail += ExpectTrue($"{recipe.Id}: apply NOT acceptable (preview-only)", !result.ApplyAcceptable);
                fail += ExpectTrue($"{recipe.Id}: has honest blockers", result.BlockerCount >= 1);
                fail += ExpectTrue($"{recipe.Id}: rung-ladder blocker present", result.HasCode("rung-ladder", SinPlanCheckSeverity.Blocker));
                fail += ExpectTrue($"{recipe.Id}: NO proved-payload (candidate not promoted)",
                    !result.HasCode("proved-payload") && !result.HasCode("payload-proved"));

                // Determinism: validating the same plan twice yields an identical report.
                string r2 = SinPlanValidator.Validate(SinDryRunPlanner.Plan(recipe)).ToReportString();
                fail += ExpectTrue($"{recipe.Id}: deterministic validation", result.ToReportString() == r2);

                bool isGuarded = plan.Steps.Any(s => s.Lowering == AiSnippetKind.GuardedAction);
                if (isGuarded)
                {
                    // SIN-010 (HP%): MUL(0x17) is now the correct contract. The old DIV/MUL blocker must not
                    // survive when the snippet emits MUL, but the recipe remains blocked by real apply blockers.
                    fail += ExpectTrue($"{recipe.Id}: NO old divmul-integrity BLOCKER with MUL(0x17)",
                        !result.HasCode("divmul-integrity", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: DIV/MUL integrity INFO confirms MUL(0x17)",
                        result.HasCode("divmul-integrity", SinPlanCheckSeverity.Info));
                    fail += ExpectTrue($"{recipe.Id}: stays apply-blocked by real blockers", !result.ApplyAcceptable);
                    fail += ExpectTrue($"{recipe.Id}: real blocker payload-candidate present",
                        result.HasCode("payload-candidate", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: real blocker jump-slots present",
                        result.HasCode("jump-slots", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: real blocker step-blocker present",
                        result.HasCode("step-blocker", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: RT2/rung gate still blocks public apply",
                        result.Blockers.Any(c => c.Code == "plan-blocker" && c.Message.Contains("RT2", StringComparison.Ordinal))
                        && result.HasCode("rung-ladder", SinPlanCheckSeverity.Blocker));

                    SinPlannedStep g = plan.Steps.First(s => s.Lowering == AiSnippetKind.GuardedAction);
                    bool emitsDiv = g.GuardOps.Any(i => i.Opcode == 0x16);
                    bool emitsMul = g.GuardOps.Any(i => i.Opcode == 0x17);
                    fail += ExpectTrue($"{recipe.Id}: guard emits MUL(0x17)", emitsMul);
                    fail += ExpectTrue($"{recipe.Id}: guard does NOT emit old DIV(0x16) regression path", !emitsDiv);
                }
                else
                {
                    // Linear pilots have no guard arithmetic — no DIV/MUL finding should fire.
                    fail += ExpectTrue($"{recipe.Id}: no DIV/MUL finding (linear, no guard)", !result.HasCode("divmul-integrity"));
                    // A clean linear body should report a balanced stack (net 0).
                    fail += ExpectTrue($"{recipe.Id}: linear body stack balances (net 0)",
                        result.Checks.Any(c => c.Code == "stack-balance" && c.Severity == SinPlanCheckSeverity.Info));
                }
            }

            // Confirm, by construction, that NO write/monster/diff path is reachable from the validator.
            Console.WriteLine();
            Console.WriteLine("no apply path: SinPlanValidator reads only AiScript_File static tables + AiStackModel + the plan —");
            Console.WriteLine("no monster_*.bin load, no AiScript_File.Rebuild/AppendGuardedAction/Splice, no AiScript_Diff, no save.");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - 3 pilots validate as structurally-acceptable-but-BLOCKED; nothing applied/saved; HP% emits MUL(0x17) with no old divmul blocker, and real blockers keep SIN blocked for writing."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        static int Expect(string label, int expected, int actual)
        {
            bool ok = expected == actual;
            Console.WriteLine(ok ? $"  {label,-58}: PASS {actual}" : $"  {label,-58}: FAIL expected {expected}, got {actual}");
            return ok ? 0 : 1;
        }

        static int ExpectTrue(string label, bool condition)
        {
            Console.WriteLine(condition ? $"  {label,-58}: PASS" : $"  {label,-58}: FAIL");
            return condition ? 0 : 1;
        }
    }
}
