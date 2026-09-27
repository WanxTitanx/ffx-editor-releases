using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-dryrun-rt0 : read-only gate for the SIN Chain Builder Gate 2 (preview/dry-run planner).
    //
    // Builds the 3 mandatory pilot recipes in memory, plans each into a SinApplyPlan, renders a deterministic
    // preview, and asserts the plan is HONEST and preview-only:
    //   - nothing is saved and no writer/assembler is invoked (the planner has no file I/O / Rebuild path);
    //   - every plan is ApplyAllowed == false, rung == preview-only;
    //   - the honest blockers (no emitter / no AiScriptLab / no AEON / no RT2) are present;
    //   - the rung ladder is unsatisfied (no gate ran);
    //   - NO command payload is promoted to "proved" (candidates stay candidates);
    //   - the eligibility classifier blocks item/non-AI payloads and leaves AI-perform commands as candidates.
    internal static class SinDryRunRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== SIN dry-run preview planner RT0 (Gate 2, preview-only) ===");
            int fail = 0;

            IReadOnlyList<SinChainRecipe> pilots = SinPilotRecipes.All;
            fail += Expect("pilot recipe count", 3, pilots.Count);

            foreach (SinChainRecipe recipe in pilots)
            {
                Console.WriteLine();
                Console.WriteLine($"---- {recipe.Id} · {recipe.DisplayName} ----");
                SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
                string preview = SinDryRunPreview.Render(plan);
                Console.WriteLine(preview);

                fail += ExpectTrue($"{recipe.Id}: apply NOT allowed", !plan.ApplyAllowed);
                fail += ExpectTrue($"{recipe.Id}: rung == preview-only", plan.Rung == SinPromotionRung.PreviewOnly);
                fail += ExpectTrue($"{recipe.Id}: has steps", plan.Steps.Count >= 1);
                fail += ExpectTrue($"{recipe.Id}: NO proved payload (candidate not promoted)", !plan.ClaimsAnyProvedPayload);
                fail += ExpectTrue($"{recipe.Id}: planned instructions present",
                    plan.Steps.All(s => s.PlannedInstructions.Count > 0));
                fail += ExpectTrue($"{recipe.Id}: honest blockers present", HasHonestBlockers(plan));
                fail += ExpectTrue($"{recipe.Id}: rung ladder unsatisfied", plan.RequiredGates.All(g => !g.Satisfied));
                fail += ExpectTrue($"{recipe.Id}: rung ladder has >= 6 gates", plan.RequiredGates.Count >= 6);
                fail += ExpectTrue($"{recipe.Id}: preview ends 'Apply allowed: NO'",
                    preview.TrimEnd().EndsWith("Apply allowed: NO", StringComparison.Ordinal));

                // Determinism: same recipe -> identical preview.
                string preview2 = SinDryRunPreview.Render(SinDryRunPlanner.Plan(recipe));
                fail += ExpectTrue($"{recipe.Id}: deterministic preview", preview == preview2);

                // Every command payload step is candidate or blocked — never proved.
                fail += ExpectTrue($"{recipe.Id}: payloads are candidate/blocked only",
                    plan.Steps.Where(s => s.PayloadEligibility != null)
                        .All(s => s.PayloadEligibility != SinPayloadEligibility.Proved));
            }

            // Eligibility classifier honesty (LUZU advisory / GATTA blocked / DONNA numeric key).
            Console.WriteLine();
            Console.WriteLine("---- eligibility classifier ----");
            fail += ExpectElig("0x2007 Mega Phoenix (item, GATTA)", 0x2007, SinPayloadEligibility.Blocked);
            fail += ExpectElig("0x4000 Attack (MonsterMagic1)", 0x4000, SinPayloadEligibility.Candidate);
            fail += ExpectElig("0x3049 Firaga (RT2 swap was operand-only -> still candidate in a SIN block)", 0x3049, SinPayloadEligibility.Candidate);
            fail += ExpectElig("0x60AB Multi-Fira (MonsterMagic2)", 0x60AB, SinPayloadEligibility.Candidate);
            fail += ExpectElig("0x0000 non-command literal", 0x0000, SinPayloadEligibility.Blocked);

            Console.WriteLine();
            Console.WriteLine("no writer invoked: planner expands snippet templates in memory only — no AiScript_File.Rebuild/");
            Console.WriteLine("AppendGuardedAction/SpliceAiFileIntoMonster, no AiAutomation write, no monster_*.bin, no save.");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - 3 pilots plan into honest preview-only plans; nothing applied/saved; SIN stays BLOCKED for writing."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        static bool HasHonestBlockers(SinApplyPlan plan)
        {
            string joined = string.Join(" | ", plan.Blockers).ToLowerInvariant();
            return joined.Contains("no emitter")
                && joined.Contains("no aiscriptlab")
                && joined.Contains("no aeon")
                && joined.Contains("no rt2");
        }

        static int ExpectElig(string label, ushort operand, SinPayloadEligibility expected)
        {
            SinPayloadEligibility actual = SinDryRunPlanner.ClassifyPayload(operand);
            bool ok = actual == expected;
            Console.WriteLine(ok ? $"  {label,-58}: PASS {actual}" : $"  {label,-58}: FAIL expected {expected}, got {actual}");
            return ok ? 0 : 1;
        }

        static int Expect(string label, int expected, int actual)
        {
            bool ok = expected == actual;
            Console.WriteLine(ok ? $"  {label,-46}: PASS {actual}" : $"  {label,-46}: FAIL expected {expected}, got {actual}");
            return ok ? 0 : 1;
        }

        static int ExpectTrue(string label, bool condition)
        {
            Console.WriteLine(condition ? $"  {label,-58}: PASS" : $"  {label,-58}: FAIL");
            return condition ? 0 : 1;
        }
    }
}
