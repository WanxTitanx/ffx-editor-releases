using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-aeon-preview-rt0 : read-only gate for the SIN Chain Builder Gate 4 (AEON diff preview).
    //
    // Builds the 3 mandatory pilot recipes in memory, plans each into a Gate-2 SinApplyPlan, validates each via the
    // Gate-3 SinPlanValidator, then renders the Gate-4 SinAeonDiffPreview and asserts the preview is HONEST:
    //   - it shows a diff, it does NOT apply one: ApplyAllowed == false for every preview;
    //   - it RESPECTS Gate 3: structurally-valid carries over, ApplyAcceptable stays false (no candidate promoted);
    //   - the diff target is a synthetic/control fixture (no monster loaded) → every change is ADDED; MODIFIED == 0
    //     and REMOVED == 0 (resolving them needs the real worker = the later apply/backup gate);
    //   - the HP% recipe (SIN-010) emits MUL(0x17), does NOT print the old unresolved 0x16 DIV blocker notice,
    //     and Gate 3 carries only the divmul-integrity INFO while real blockers still keep apply disabled;
    //   - the preview is deterministic (same recipe -> identical text);
    //   - a filesystem guard confirms NOTHING was written: no new file, no .prev.bak backup, no monster_*.bin.
    //
    // VERDICT line ends the run. SIN stays BLOCKED for writing — Gate 4 shows a diff preview, it does not apply one.
    internal static class SinAeonPreviewRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== SIN AEON diff preview RT0 (Gate 4, read-only, preview-only) ===");
            int fail = 0;

            // Filesystem guard: snapshot the output dir BEFORE — nothing this gate does may create a file there.
            string baseDir = AppContext.BaseDirectory;
            HashSet<string> before = SnapshotFiles(baseDir);

            IReadOnlyList<SinChainRecipe> pilots = SinPilotRecipes.All;
            fail += Expect("pilot recipe count", 3, pilots.Count);

            foreach (SinChainRecipe recipe in pilots)
            {
                Console.WriteLine();
                Console.WriteLine($"---- {recipe.Id} · {recipe.DisplayName} ----");

                SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
                SinPlanValidationResult validation = SinPlanValidator.Validate(plan);
                SinAeonDiffPreview preview = SinAeonDiffPlanner.Plan(plan, validation);
                string text = SinAeonPreviewFormatter.Render(preview);
                Console.WriteLine(text);

                // Core Gate-4 contract: a diff PREVIEW, never an apply.
                fail += ExpectTrue($"{recipe.Id}: apply NOT allowed", !preview.ApplyAllowed);
                fail += ExpectTrue($"{recipe.Id}: structurally valid carried from Gate 3",
                    preview.StructurallyValid && validation.StructurallyValid);
                fail += ExpectTrue($"{recipe.Id}: ApplyAcceptable stays false (Gate 3 respected)", !validation.ApplyAcceptable);
                fail += ExpectTrue($"{recipe.Id}: diff-target is synthetic/control fixture",
                    preview.Steps.Count > 0 && preview.Steps.All(s => s.DiffTarget == "synthetic/control fixture"));
                fail += ExpectTrue($"{recipe.Id}: ADDED >= 1 (proposes instructions)", preview.TotalAdded >= 1);
                fail += ExpectTrue($"{recipe.Id}: MODIFIED == 0 (no real worker diffed)", preview.TotalModified == 0);
                fail += ExpectTrue($"{recipe.Id}: REMOVED == 0 (no real worker diffed)", preview.TotalRemoved == 0);
                fail += ExpectTrue($"{recipe.Id}: NO candidate promoted to authoring-ready",
                    !plan.ClaimsAnyProvedPayload && !validation.HasCode("proved-payload") && !validation.HasCode("payload-proved"));
                fail += ExpectTrue($"{recipe.Id}: preview ends 'Apply allowed: NO'",
                    text.TrimEnd().EndsWith("Apply allowed: NO", StringComparison.Ordinal));

                // Determinism: same recipe -> identical preview text (via the convenience overload, full chain).
                string text2 = SinAeonPreviewFormatter.Render(SinAeonDiffPlanner.Plan(recipe));
                fail += ExpectTrue($"{recipe.Id}: deterministic preview", text == text2);

                bool isGuarded = plan.Steps.Any(s => s.Lowering == AiSnippetKind.GuardedAction);
                if (isGuarded)
                {
                    // SIN-010 (HP%): MUL(0x17) is the correct contract. The preview must not surface the old
                    // DIV/MUL blocker notice, but real blockers must still be visible in Gate 3.
                    SinPlannedStep g = plan.Steps.First(s => s.Lowering == AiSnippetKind.GuardedAction);
                    bool emitsDiv = g.GuardOps.Any(i => i.Opcode == 0x16);
                    bool emitsMul = g.GuardOps.Any(i => i.Opcode == 0x17);
                    fail += ExpectTrue($"{recipe.Id}: guard emits MUL(0x17)", emitsMul);
                    fail += ExpectTrue($"{recipe.Id}: guard does NOT emit old DIV(0x16) regression path", !emitsDiv);
                    fail += ExpectTrue($"{recipe.Id}: HP% old DIV/MUL blocker NOT surfaced in preview", !preview.HpPercentDivMulBlocked);
                    fail += ExpectTrue($"{recipe.Id}: old '0x16 DIV vs intended HP% multiply' notice absent",
                        !text.Contains("unresolved 0x16 DIV vs intended HP% multiply semantics", StringComparison.Ordinal));
                    fail += ExpectTrue($"{recipe.Id}: old 'No RT2 proof.' notice absent", !text.Contains("No RT2 proof.", StringComparison.Ordinal));
                    fail += ExpectTrue($"{recipe.Id}: Gate 3 has divmul-integrity INFO, not BLOCKER",
                        validation.HasCode("divmul-integrity", SinPlanCheckSeverity.Info)
                        && !validation.HasCode("divmul-integrity", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: real blocker payload-candidate present",
                        validation.HasCode("payload-candidate", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: real blocker jump-slots present",
                        validation.HasCode("jump-slots", SinPlanCheckSeverity.Blocker));
                    fail += ExpectTrue($"{recipe.Id}: real blocker step-blocker present",
                        validation.HasCode("step-blocker", SinPlanCheckSeverity.Blocker));
                }
                else
                {
                    fail += ExpectTrue($"{recipe.Id}: no HP% DIV/MUL notice (linear, no guard)", !preview.HpPercentDivMulBlocked);
                }
            }

            // Filesystem guard: snapshot AFTER — assert no file appeared, no backup, no monster written.
            HashSet<string> after = SnapshotFiles(baseDir);
            List<string> created = after.Except(before).ToList();
            bool noNewFiles = created.Count == 0;
            bool noBak = !after.Any(f => f.EndsWith(".prev.bak", StringComparison.OrdinalIgnoreCase));
            bool noMonster = !created.Any(f => Path.GetFileName(f).StartsWith("monster_", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine();
            Console.WriteLine("---- filesystem guard (no write / no backup) ----");
            fail += ExpectTrue("no new file created in output dir", noNewFiles);
            fail += ExpectTrue("no .prev.bak backup created", noBak);
            fail += ExpectTrue("no monster_*.bin written", noMonster);
            if (!noNewFiles)
                Console.WriteLine("  created: " + string.Join(", ", created.Select(Path.GetFileName)));

            Console.WriteLine();
            Console.WriteLine("no apply path: SinAeonDiffPlanner reads only the SinApplyPlan + SinPlanValidationResult (both read-only) —");
            Console.WriteLine("no monster_*.bin load, no AiScript_File.Rebuild/AppendGuardedAction/Splice, no AiScript_Diff against a real worker, no backup, no save.");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - 3 pilots render an honest AEON diff preview (synthetic target, ADDED-only); nothing applied/saved/backed-up; HP% emits MUL(0x17) with no old divmul blocker; SIN stays BLOCKED for writing on real blockers."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");

            Console.WriteLine();
            Console.WriteLine("SIN continua BLOCKED para escrita.");
            Console.WriteLine("Gate 4 mostra diff preview, nao aplica diff.");
            return fail == 0 ? 0 : 1;
        }

        static HashSet<string> SnapshotFiles(string dir) =>
            new(Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

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
