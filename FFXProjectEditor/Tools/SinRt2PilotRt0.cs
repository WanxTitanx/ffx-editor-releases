using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-pilot-rt2-rt0 : operator-gated gate for the SIN Chain Builder Gate 6 (RT2 in-game pilot) PREFLIGHT.
    //
    // Proves that the Gate-6 RT2-pilot PREFLIGHT is sound and tightly scoped, WITHOUT ever touching the running game,
    // a real game/user file, or the dinput8 probe. It composes the proven Gate-5 sandbox round-trip under a strictly
    // narrower RT2 eligibility gate:
    //
    //   - SIN-006 (Turn-1 self-buff; Self only, no payload, no 0x16/0x17) + AllowRt2InGamePilot=true → PreflightReady:
    //     the sandbox round-trip applies+restores byte-identical on a COPY (original untouched, hashes prove it), and
    //     the result carries the aim (worker/entrypoint) + the on-screen effect the operator must verify. The operator
    //     verdict is Pending and RT2 is NOT confirmed — the live apply+observe is a separate operator step (RT2-pending).
    //   - SIN-006 WITHOUT the explicit RT2 permission → BLOCKED (the in-game pilot is operator-gated, and the RT2
    //     permission is SEPARATE from the sandbox permission).
    //   - SIN-009 (force/perform command; CANDIDATE payload) → BLOCKED (command payloads are not RT2-eligible).
    //   - SIN-010 (HP% guard) → BLOCKED by the narrow pilot allowlist: HP% uses MUL(0x17), but still needs its
    //     own operator proof before this RT2 preflight admits it.
    //   - filesystem guards: nothing written outside work/, every real corpus fixture hash unchanged, the bin/output
    //     dir gains no file / no .prev.bak / no monster_*.bin.
    //   - hard invariants asserted on every result: RealApplyDone=NO, Rt2Confirmed=NO, PublicApplyAllowed=NO.
    //
    // SIN stays BLOCKED for public writing. Gate 6 PREFLIGHT is not RT2-proved, not a public button, not a writer.
    internal static class SinRt2PilotRt0
    {
        const string CorpusRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";
        static readonly string[] FixtureIds = { "m201", "m000", "m134", "m282", "m124" };

        public static int Run()
        {
            Console.WriteLine("=== SIN RT2 in-game pilot PREFLIGHT RT0 (Gate 6, operator-gated, headless-safe) ===");
            int fail = 0;

            // Filesystem guard: snapshot the bin/output dir BEFORE — nothing this gate does may create a file there.
            string baseDir = AppContext.BaseDirectory;
            HashSet<string> binBefore = SnapshotFiles(baseDir);

            // Fresh, deterministic sandbox dir under work/ (cleaned before + after — leaves no residue, never committed).
            string workRoot = LocateWorkRoot();
            string workDir = Path.Combine(workRoot, "sin_pilot_rt2_rt0");
            SafeDeleteUnderWork(workDir, workRoot);
            Directory.CreateDirectory(workDir);

            List<string> fixtures = FixtureIds
                .Select(id => Path.Combine(CorpusRoot, "_" + id, id + ".bin"))
                .Where(File.Exists).ToList();
            bool corpusPresent = fixtures.Count > 0;
            Console.WriteLine(corpusPresent
                ? $"corpus fixtures available: {string.Join(", ", fixtures.Select(Path.GetFileName))}"
                : "corpus ABSENT — the sandbox proof / preflight will be SKIPPED (eligibility-only assertions still run).");

            // hash every fixture before the run; assert all unchanged afterward.
            var fixtureHashBefore = fixtures.ToDictionary(f => f, SinSandboxRestoreVerifier.Sha256);

            SinChainRecipe sin006 = SinPilotRecipes.TurnOneSelfBuff();
            SinChainRecipe sin009 = SinPilotRecipes.ForcePerformCommand();
            SinChainRecipe sin010 = SinPilotRecipes.HpGuardedAction();

            // ---------------------------------------------------------------- SIN-006: eligible → preflight ready
            Console.WriteLine();
            Console.WriteLine("---- SIN-006 · allowRt2InGamePilot=true → PREFLIGHT READY (operator observation pending) ----");
            SinRt2PilotResult? ready = null;
            string? usedFixture = null;
            foreach (string fx in fixtures)
            {
                SinRt2PilotResult r = SinRt2PilotSession.Preflight(fx, workDir, sin006, allowRt2InGamePilot: true);
                if (r.Outcome == SinRt2Outcome.PreflightReady) { ready = r; usedFixture = fx; break; }
                if (r.Outcome == SinRt2Outcome.Skipped) { ready = r; break; }
                Console.WriteLine($"  (fixture {Path.GetFileName(fx)} → {r.Outcome}: {r.BlockReason})");
            }
            ready ??= SinRt2PilotSession.Preflight(null, workDir, sin006, allowRt2InGamePilot: true);
            Console.WriteLine(ready.ToReportString());

            if (corpusPresent)
            {
                fail += T("SIN-006: outcome PREFLIGHT-READY", ready.Outcome == SinRt2Outcome.PreflightReady);
                fail += T("SIN-006: sandbox round-trip applied (on a copy)", ready.SandboxApplied);
                fail += T("SIN-006: backup proven before write", ready.BackupProven);
                fail += T("SIN-006: restore byte-identical", ready.RestoreByteIdentical);
                fail += T("SIN-006: diff is the expected conservative block (no drift)", !ready.UnexpectedDiff);
                fail += T("SIN-006: modified==0 && removed==0", ready.ActualModifiedRows == 0 && ready.ActualRemovedRows == 0);
                fail += T("SIN-006: added == expected appended block", ready.ActualAddedRows == ready.ExpectedAddedRows && ready.ActualAddedRows > 0);
                fail += T("SIN-006: edited monster re-parses clean", ready.ReParsedClean);
                fail += T("SIN-006: AiScriptLab-clean", ready.AiScriptLabClean);
                fail += T("SIN-006: revert path ready", ready.RevertReady);
                fail += T("SIN-006: aim resolved (worker/entrypoint)", ready.WorkerIndex >= 0 && ready.EntrypointIndex >= 0);
                fail += T("SIN-006: on-screen effect described", !string.IsNullOrWhiteSpace(ready.OnScreenEffectToObserve));
                fail += T("SIN-006: original copy hash UNCHANGED", ready.OriginalSha256Before == ready.OriginalSha256After);
                fail += T("SIN-006: real corpus source hash UNCHANGED", ready.CorpusSha256Before == ready.CorpusSha256After);
                // RT2-pending invariants: preflight ready is NOT a green in-game claim.
                fail += T("SIN-006: operator verdict PENDING (RT2-pending)", ready.OperatorVerdict == SinRt2OperatorVerdict.Pending);
                fail += T("SIN-006: RT2 NOT confirmed", !ready.Rt2Confirmed);
                fail += T("SIN-006: real apply NOT done", !ready.RealApplyDone);
                fail += T("SIN-006: public apply NOT allowed", !ready.PublicApplyAllowed);
            }
            else
            {
                fail += T("SIN-006: eligible (preflight skipped, corpus absent)", ready.Outcome == SinRt2Outcome.Skipped);
                fail += T("SIN-006: RT2 NOT confirmed", !ready.Rt2Confirmed);
                fail += T("SIN-006: real apply NOT done", !ready.RealApplyDone);
                Console.WriteLine("  [SKIP] sandbox proof / preflight not exercised (corpus absent).");
            }

            // ---------------------------------------------------------------- SIN-006 WITHOUT permission → BLOCKED
            Console.WriteLine();
            Console.WriteLine("---- SIN-006 · allowRt2InGamePilot=FALSE → operator-gated BLOCK ----");
            SinRt2PilotResult noPerm = SinRt2PilotSession.Preflight(usedFixture, workDir, sin006, allowRt2InGamePilot: false);
            Console.WriteLine(noPerm.ToReportString());
            fail += T("SIN-006/no-perm: BLOCKED", noPerm.Outcome == SinRt2Outcome.Blocked);
            fail += T("SIN-006/no-perm: reason cites AllowRt2InGamePilot", (noPerm.BlockReason ?? "").Contains("AllowRt2InGamePilot", StringComparison.Ordinal));
            fail += T("SIN-006/no-perm: real apply NOT done", !noPerm.RealApplyDone);
            fail += T("SIN-006/no-perm: RT2 NOT confirmed", !noPerm.Rt2Confirmed);

            // ---------------------------------------------------------------- SIN-009: candidate payload → BLOCKED
            Console.WriteLine();
            Console.WriteLine("---- SIN-009 · candidate command payload · allowRt2InGamePilot=true → NOT RT2-eligible ----");
            SinRt2PilotResult r009 = SinRt2PilotSession.Preflight(usedFixture, workDir, sin009, allowRt2InGamePilot: true);
            Console.WriteLine(r009.ToReportString());
            fail += T("SIN-009: BLOCKED", r009.Outcome == SinRt2Outcome.Blocked);
            fail += T("SIN-009: reason cites command payload / allowlist", BlockedForCmdOrAllowlist(r009.BlockReason));
            fail += T("SIN-009: real apply NOT done", !r009.RealApplyDone);

            // ---------------------------------------------------------------- SIN-010: HP% guard → BLOCKED (RT2-specific pilot pending)
            Console.WriteLine();
            Console.WriteLine("---- SIN-010 · HP% guard · allowRt2InGamePilot=true → NOT RT2-eligible ----");
            SinRt2PilotResult r010 = SinRt2PilotSession.Preflight(usedFixture, workDir, sin010, allowRt2InGamePilot: true);
            Console.WriteLine(r010.ToReportString());
            fail += T("SIN-010: BLOCKED", r010.Outcome == SinRt2Outcome.Blocked);
            fail += T("SIN-010: reason cites HP% guard or allowlist", BlockedForHpGuardOrAllowlist(r010.BlockReason));
            fail += T("SIN-010: real apply NOT done", !r010.RealApplyDone);

            // ---------------------------------------------------------------- filesystem guards
            Console.WriteLine();
            Console.WriteLine("---- filesystem guards (nothing written outside work/, game/corpus untouched) ----");
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
            Console.WriteLine($"  pilot work dir cleaned: {workDir}");

            Console.WriteLine();
            Console.WriteLine("no live-apply path: SinRt2PilotSession never writes a real game/user file and never drives the dinput8 probe.");
            Console.WriteLine("The live apply + on-screen observation are an OPERATOR-DRIVEN step; OperatorVerdict stays Pending here.");
            Console.WriteLine("RealApplyDone=NO, Rt2Confirmed=NO, PublicApplyAllowed=NO by construction.");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - SIN-006 reaches an honest RT2 PREFLIGHT (sandbox round-trip byte-clean on a copy, aim resolved, " +
                  "operator verdict Pending); SIN-006 without permission + SIN-009/010 are NOT RT2-eligible; nothing written outside " +
                  "work/. RT2 in-game = NOT run (pending operator). SIN stays BLOCKED for public writing."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");

            Console.WriteLine();
            Console.WriteLine("SIN continua BLOCKED para escrita publica.");
            Console.WriteLine("Gate 6 PREFLIGHT prova um piloto RT2 controlado, nao uma galeria de templates.");
            Console.WriteLine("Gate 6 nao e RT2-provado e nao e botao publico.");
            return fail == 0 ? 0 : 1;
        }

        static bool BlockedForCmdOrAllowlist(string? reason) =>
            (reason ?? "").Contains("command payload", StringComparison.Ordinal)
            || (reason ?? "").Contains("PerformCommand", StringComparison.Ordinal)
            || (reason ?? "").Contains("allowlist", StringComparison.Ordinal);

        static bool BlockedForHpGuardOrAllowlist(string? reason) =>
            (reason ?? "").Contains("HP% guard", StringComparison.Ordinal)
            || (reason ?? "").Contains("allowlist", StringComparison.Ordinal);

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
