using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-sandbox-rt0 : operator-gated gate for the SIN Chain Builder Gate 5 (backup/apply SANDBOX).
    //
    // Proves that a validated + AEON-reviewed SIN plan can be applied and undone in a throwaway sandbox COPY, with a
    // backup taken before the write, without ever touching the original (or the real game), and that a recipe with a
    // real blocker never applies — not even in the sandbox:
    //
    //   - SIN-006 (Turn-1 self-buff; Self target, no command payload, no 0x16/0x17) is ELIGIBLE → real apply to a
    //     sandbox copy of a real monster, real AiScript_Diff (only the intended ADDED block), .prev.bak backup,
    //     byte-identical restore, and a hash proof that the original (and the real corpus source) never changed.
    //   - SIN-006 WITHOUT the explicit sandbox permission → BLOCKED (the sandbox apply is operator-gated).
    //   - SIN-009 (force/perform command; CANDIDATE payload) → BLOCKED — conservative decision (a candidate payload
    //     is not honest enough for a sandbox write; documented, not forced green).
    //   - SIN-010 (HP% guarded) → emits MUL(0x17) and must NOT be blocked by the old 0x16 DIV/MUL regression
    //     contract; it still stays blocked by real blockers (candidate payload, jump-slots, step-blocker).
    //   - an UNEXPECTED diff (a tampered edit) is rejected by the same conservative-diff predicate the session uses.
    //   - filesystem guards: nothing is written outside work/; the real corpus fixture hash is unchanged; the bin
    //     output dir gains no files / no .prev.bak / no monster_*.bin.
    //
    // SIN stays BLOCKED for public writing. Gate 5 applies only to a sandbox/copy and is NOT RT2, NOT a public button.
    internal static class SinSandboxApplyRt0
    {
        const string CorpusRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";
        static readonly string[] FixtureIds = { "m201", "m000", "m134", "m282", "m124" };

        public static int Run()
        {
            Console.WriteLine("=== SIN backup/apply SANDBOX RT0 (Gate 5, operator-gated, sandbox-only) ===");
            int fail = 0;

            // Filesystem guard: snapshot the bin/output dir BEFORE — nothing this gate does may create a file there.
            string baseDir = AppContext.BaseDirectory;
            HashSet<string> binBefore = SnapshotFiles(baseDir);

            // Fresh, deterministic sandbox dir under work/ (cleaned before + after — leaves no residue, never committed).
            string workRoot = LocateWorkRoot();
            string workDir = Path.Combine(workRoot, "sin_apply_sandbox_rt0");
            SafeDeleteUnderWork(workDir, workRoot);
            Directory.CreateDirectory(workDir);

            List<string> fixtures = FixtureIds
                .Select(id => Path.Combine(CorpusRoot, "_" + id, id + ".bin"))
                .Where(File.Exists).ToList();
            bool corpusPresent = fixtures.Count > 0;
            Console.WriteLine(corpusPresent
                ? $"corpus fixtures available: {string.Join(", ", fixtures.Select(Path.GetFileName))}"
                : "corpus ABSENT — the real sandbox apply will be SKIPPED (eligibility-only assertions still run).");

            // hash every fixture before the run; assert all unchanged afterward.
            var fixtureHashBefore = fixtures.ToDictionary(f => f, SinSandboxRestoreVerifier.Sha256);

            SinChainRecipe sin006 = SinPilotRecipes.TurnOneSelfBuff();
            SinChainRecipe sin009 = SinPilotRecipes.ForcePerformCommand();
            SinChainRecipe sin010 = SinPilotRecipes.HpGuardedAction();

            // ---------------------------------------------------------------- SIN-006: eligible → real sandbox apply
            Console.WriteLine();
            Console.WriteLine("---- SIN-006 · eligible · allowSandboxApply=true ----");
            SinSandboxApplyResult? applied = null;
            string? usedFixture = null;
            foreach (string fx in fixtures)
            {
                SinSandboxApplyResult r = SinSandboxApplySession.Run(fx, workDir, sin006, allowSandboxApply: true);
                if (r.Outcome == SinSandboxOutcome.Applied) { applied = r; usedFixture = fx; break; }
                if (r.Outcome == SinSandboxOutcome.Skipped) { applied = r; break; }
                Console.WriteLine($"  (fixture {Path.GetFileName(fx)} → {r.Outcome}: {r.BlockReason})");
            }
            applied ??= SinSandboxApplySession.Run(null, workDir, sin006, allowSandboxApply: true);
            Console.WriteLine(applied.ToReportString());

            if (corpusPresent)
            {
                fail += T("SIN-006: outcome APPLIED", applied.Outcome == SinSandboxOutcome.Applied);
                fail += T("SIN-006: applied to SANDBOX", applied.AppliedToSandbox);
                fail += T("SIN-006: NOT applied to original", !applied.AppliedToOriginal);
                fail += T("SIN-006: backup created BEFORE write", applied.BackupCreated);
                fail += T("SIN-006: diff is the expected conservative block (no drift)", !applied.UnexpectedDiff);
                fail += T("SIN-006: modified==0 && removed==0", applied.ActualModifiedRows == 0 && applied.ActualRemovedRows == 0);
                fail += T("SIN-006: added == expected appended block", applied.ActualAddedRows == applied.ExpectedAddedRows && applied.ActualAddedRows > 0);
                fail += T("SIN-006: edited monster re-parses clean", applied.ReParsedClean);
                fail += T("SIN-006: AiScriptLab-clean (ValidateRebuilt)", applied.AiScriptLabClean);
                fail += T("SIN-006: restore byte-identical", applied.RestoreByteIdentical);
                fail += T("SIN-006: original copy hash UNCHANGED", applied.OriginalSha256Before == applied.OriginalSha256After);
                fail += T("SIN-006: real corpus source hash UNCHANGED", applied.CorpusSha256Before == applied.CorpusSha256After);
                fail += T("SIN-006: sandbox post-restore == original pre-apply", applied.SandboxSha256PostRestore == applied.OriginalSha256Before);
                fail += T("SIN-006: sandbox post-apply != original (it changed)", applied.SandboxSha256PostApply != applied.OriginalSha256Before);
                fail += T("SIN-006: public apply NOT allowed", !applied.PublicApplyAllowed);
                fail += T("SIN-006: RT2 NOT run", !applied.Rt2);
                fail += T("SIN-006: sandbox target is under work/", applied.SandboxTarget != null
                    && Path.GetFullPath(applied.SandboxTarget).StartsWith(Path.GetFullPath(workRoot), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                fail += T("SIN-006: eligible (apply skipped, corpus absent)", applied.Outcome == SinSandboxOutcome.Skipped);
                Console.WriteLine("  [SKIP] real apply not exercised (corpus absent).");
            }

            // ---------------------------------------------------------------- SIN-006 WITHOUT permission → BLOCKED
            Console.WriteLine();
            Console.WriteLine("---- SIN-006 · allowSandboxApply=FALSE → operator-gated BLOCK ----");
            SinSandboxApplyResult noPerm = SinSandboxApplySession.Run(usedFixture, workDir, sin006, allowSandboxApply: false);
            Console.WriteLine(noPerm.ToReportString());
            fail += T("SIN-006/no-perm: BLOCKED", noPerm.Outcome == SinSandboxOutcome.Blocked);
            fail += T("SIN-006/no-perm: reason cites AllowSandboxApply", (noPerm.BlockReason ?? "").Contains("AllowSandboxApply", StringComparison.Ordinal));
            fail += T("SIN-006/no-perm: NOT applied to sandbox", !noPerm.AppliedToSandbox);

            // ---------------------------------------------------------------- SIN-009: candidate payload → BLOCKED
            Console.WriteLine();
            Console.WriteLine("---- SIN-009 · candidate payload · allowSandboxApply=true → conservative BLOCK ----");
            SinSandboxApplyResult r009 = SinSandboxApplySession.Run(usedFixture, workDir, sin009, allowSandboxApply: true);
            Console.WriteLine(r009.ToReportString());
            fail += T("SIN-009: BLOCKED", r009.Outcome == SinSandboxOutcome.Blocked);
            fail += T("SIN-009: reason cites candidate payload", (r009.BlockReason ?? "").Contains("payload-candidate", StringComparison.Ordinal));
            fail += T("SIN-009: NOT applied to sandbox", !r009.AppliedToSandbox);
            fail += T("SIN-009: no backup created", !r009.BackupCreated);

            // ---------------------------------------------------------------- SIN-010: MUL(0x17), old divmul blocker absent → still BLOCKED
            Console.WriteLine();
            Console.WriteLine("---- SIN-010 · HP% guard emits MUL(0x17) · allowSandboxApply=true → real-blocker BLOCK ----");
            SinSandboxApplyResult r010 = SinSandboxApplySession.Run(usedFixture, workDir, sin010, allowSandboxApply: true);
            Console.WriteLine(r010.ToReportString());
            fail += T("SIN-010: BLOCKED", r010.Outcome == SinSandboxOutcome.Blocked);
            string r010Reason = r010.BlockReason ?? "";
            fail += T("SIN-010: reason does NOT cite old 0x16 DIV vs intended HP% multiply blocker",
                !r010Reason.Contains("0x16 DIV vs intended HP% multiply", StringComparison.Ordinal));
            fail += T("SIN-010: reason does NOT cite divmul-integrity blocker",
                !r010Reason.Contains("divmul-integrity", StringComparison.Ordinal));
            fail += T("SIN-010: reason cites candidate payload", r010Reason.Contains("payload-candidate", StringComparison.Ordinal));
            fail += T("SIN-010: reason cites jump-slots", r010Reason.Contains("jump-slots", StringComparison.Ordinal));
            fail += T("SIN-010: reason cites step-blocker", r010Reason.Contains("step-blocker", StringComparison.Ordinal));
            fail += T("SIN-010: NOT applied to sandbox", !r010.AppliedToSandbox);
            fail += T("SIN-010: no backup created", !r010.BackupCreated);

            // ---------------------------------------------------------------- negative test: unexpected diff rejected
            Console.WriteLine();
            Console.WriteLine("---- negative test: an UNEXPECTED (tampered) diff is rejected ----");
            if (corpusPresent && usedFixture != null)
            {
                fail += NegativeDiffTest(usedFixture);
            }
            else
            {
                Console.WriteLine("  [SKIP] negative diff test needs the corpus.");
            }

            // ---------------------------------------------------------------- filesystem guards
            Console.WriteLine();
            Console.WriteLine("---- filesystem guards (nothing written outside work/) ----");
            HashSet<string> binAfter = SnapshotFiles(baseDir);
            List<string> binCreated = binAfter.Except(binBefore).ToList();
            fail += T("no new file created in bin/output dir", binCreated.Count == 0);
            fail += T("no .prev.bak in bin/output dir", !binAfter.Any(f => f.EndsWith(".prev.bak", StringComparison.OrdinalIgnoreCase)));
            fail += T("no monster_*.bin in bin/output dir", !binCreated.Any(f => Path.GetFileName(f).StartsWith("monster_", StringComparison.OrdinalIgnoreCase)));
            if (binCreated.Count > 0) Console.WriteLine("  created: " + string.Join(", ", binCreated.Select(Path.GetFileName)));

            bool fixturesUntouched = fixtures.All(f => SinSandboxRestoreVerifier.Sha256(f) == fixtureHashBefore[f]);
            fail += T("every real corpus fixture hash UNCHANGED (read-only)", fixturesUntouched);

            // any .prev.bak that exists is strictly inside work/
            bool bakOnlyInWork = !Directory.Exists(workDir)
                || Directory.EnumerateFiles(workDir, "*.prev.bak", SearchOption.AllDirectories)
                    .All(p => Path.GetFullPath(p).StartsWith(Path.GetFullPath(workRoot), StringComparison.OrdinalIgnoreCase));
            fail += T("any .prev.bak lives only under work/", bakOnlyInWork);

            // ---------------------------------------------------------------- cleanup (no residue, never committed)
            SafeDeleteUnderWork(workDir, workRoot);
            Console.WriteLine($"  sandbox dir cleaned: {workDir}");

            Console.WriteLine();
            Console.WriteLine("no public-write path: SinSandboxApplySession writes ONLY a copy under work/ (path-guarded), only after a");
            Console.WriteLine(".prev.bak backup, never the original/game file; AppliedToOriginal=NO, public apply=NO, RT2=NO by construction.");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - SIN-006 applies+restores byte-identical in a sandbox copy (original untouched); SIN-009/010 blocked by real blockers; HP% emits MUL(0x17) with no old divmul blocker; nothing written outside work/. SIN stays BLOCKED for public writing."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");

            Console.WriteLine();
            Console.WriteLine("SIN continua BLOCKED para escrita publica.");
            Console.WriteLine("Gate 5 aplica somente em sandbox/copia controlada.");
            Console.WriteLine("Gate 5 nao e RT2 e nao e botao publico.");
            return fail == 0 ? 0 : 1;
        }

        // Tamper instruction[0] (length-preserving) → Rebuild → the diff has a Modified row at a stable offset → the
        // conservative predicate must REJECT it (this is the guard that blocks an unexpected diff from committing).
        // Fixture-independent: works whether or not the script contains any PUSHII.
        static int NegativeDiffTest(string fixturePath)
        {
            int fail = 0;
            byte[] monster = File.ReadAllBytes(fixturePath);
            byte[]? aiBlob = AiScript_File.SliceAiFileFromMonster(monster);
            if (aiBlob == null) { Console.WriteLine("  [SKIP] fixture has no AI partition."); return 0; }
            AiScriptFile script = AiScript_File.Read(aiBlob);
            if (script.Instructions.Count == 0) { Console.WriteLine("  [SKIP] empty script."); return 0; }

            AiInstruction i0 = script.Instructions[0];
            AiInstruction t0 = i0.HasOperand
                // operand-bearing: flip a low operand bit (same 3-byte length)
                ? new AiInstruction { Offset = i0.Offset, Opcode = i0.Opcode, HasOperand = true, Operand = (ushort)(i0.Operand ^ 0x0001), OperandKind = i0.OperandKind }
                // 1-byte op: swap to a different known 1-byte opcode (EQ 0x06, or LT 0x0B if it is already EQ)
                : new AiInstruction { Offset = i0.Offset, Opcode = i0.Opcode == 0x06 ? (byte)0x0B : (byte)0x06, HasOperand = false, Operand = 0, OperandKind = AiOperandKind.None };

            var tampered = new List<AiInstruction> { t0 };
            for (int n = 1; n < script.Instructions.Count; n++) tampered.Add(script.Instructions[n]);

            byte[] tamperedAi = AiScript_File.Rebuild(script, tampered);
            AiScriptFile tamperedScript = AiScript_File.Read(tamperedAi);

            IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(script, tamperedScript);
            bool conservative = SinSandboxApplySession.IsConservativeAddedOnlyDiff(
                diff, script.Instructions.Count, tamperedScript.Instructions.Count,
                out int added, out int modified, out int removed);
            Console.WriteLine($"  tampered diff: +{added} ~{modified} -{removed}  → conservative={conservative}");
            fail += T("tampered diff is NOT conservative (would block apply)", !conservative);
            fail += T("tampered diff shows drift (modified or removed >= 1)", modified + removed >= 1);
            return fail;
        }

        static string LocateWorkRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                bool isRepo = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                              || (Directory.Exists(Path.Combine(dir.FullName, "FFXProjectEditor"))
                                  && Directory.Exists(Path.Combine(dir.FullName, "docs")));
                if (isRepo) return Path.GetFullPath(Path.Combine(dir.FullName, "work"));
                dir = dir.Parent;
            }
            // fallback: a work/ under the base dir (still keeps writes inside an output-local work/).
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "work"));
        }

        static void SafeDeleteUnderWork(string dir, string workRoot)
        {
            string full = Path.GetFullPath(dir);
            if (!full.StartsWith(Path.GetFullPath(workRoot), StringComparison.OrdinalIgnoreCase)) return; // never delete outside work/
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }

        static HashSet<string> SnapshotFiles(string dir) =>
            new(Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        static int T(string label, bool condition)
        {
            Console.WriteLine(condition ? $"  {label,-62}: PASS" : $"  {label,-62}: FAIL");
            return condition ? 0 : 1;
        }
    }
}
