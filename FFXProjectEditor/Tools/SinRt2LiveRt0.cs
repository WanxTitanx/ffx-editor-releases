using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    // --sin-pilot-rt2-live-rt0 : headless self-test for the SIN Gate-6 LIVE glue (stage + verify), plus the two
    // operator commands --sin-pilot-rt2-stage / --sin-pilot-rt2-verify.
    //
    // The glue's honest contract (see SinRt2LiveProbe/SinRt2LiveSession headers): SIN-006 grows the script, so the
    // grown image enters the game via the RELOAD path (operator's manual file swap per the runbook) — NEVER an
    // in-place live RAM write. Stage persists the proven artifacts under work/sin_rt2_live/; VerifyLive is read-only
    // RAM introspection that proves which image is loaded. This RT0 gate proves all of that headless:
    //
    //   - SIN-006 + AllowRt2InGamePilot=true + corpus → STAGED: *.original.bin / *.sin006.bin / MANIFEST.txt exist,
    //     SHA-256s match the manifest claims, edited != original, the edited image re-parses clean and its AI slice
    //     grew by exactly the proven +15 block.
    //   - SIN-006 WITHOUT the permission → refused, zero files.
    //   - SIN-009 / SIN-010 → refused (not RT2-eligible), zero files.
    //   - VerifyLive with the game absent → honest not-ready status, no throw, no write.
    //   - filesystem guards: nothing outside work/, corpus hash unchanged, rt0 dir cleaned (no residue).
    //
    // SIN stays BLOCKED for public writing. Staging is not applying; verify is read-only; RT2 stays operator-judged.
    internal static class SinRt2LiveRt0
    {
        const string CorpusRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";
        static readonly string[] FixtureIds = { "m201", "m000", "m134", "m282", "m124" };

        /// <summary>The operator staging dir (kept between runs — it IS the deliverable of --sin-pilot-rt2-stage).</summary>
        internal static string OperatorStageDir => Path.Combine(LocateWorkRoot(), "sin_rt2_live");

        public static int Run()
        {
            Console.WriteLine("=== SIN RT2 LIVE glue RT0 (Gate 6 stage+verify, operator-gated, headless-safe) ===");
            int fail = 0;

            string baseDir = AppContext.BaseDirectory;
            HashSet<string> binBefore = SnapshotFiles(baseDir);

            string workRoot = LocateWorkRoot();
            string rt0Dir = Path.Combine(workRoot, "sin_rt2_live_rt0");
            SafeDeleteUnderWork(rt0Dir, workRoot);

            string? fixture = FixtureIds
                .Select(id => Path.Combine(CorpusRoot, "_" + id, id + ".bin"))
                .FirstOrDefault(File.Exists);
            string? fixtureShaBefore = fixture is null ? null : SinSandboxRestoreVerifier.Sha256(fixture);
            Console.WriteLine(fixture is null
                ? "corpus ABSENT — stage assertions will be SKIPPED (eligibility-refusal assertions still run)."
                : $"corpus fixture: {Path.GetFileName(fixture)}");

            SinChainRecipe sin006 = SinPilotRecipes.TurnOneSelfBuff();

            // ---------------------------------------------------------------- SIN-006: staged
            Console.WriteLine();
            Console.WriteLine("---- SIN-006 · allowRt2InGamePilot=true → STAGE ----");
            SinRt2LiveSession.StageResult staged = SinRt2LiveSession.Stage(fixture, rt0Dir, sin006, allowRt2InGamePilot: true);
            Console.WriteLine($"  staged={staged.Staged} :: {staged.Reason}");
            if (fixture is not null)
            {
                fail += T("SIN-006: staged", staged.Staged);
                fail += T("SIN-006: original artifact exists", staged.OriginalPath != null && File.Exists(staged.OriginalPath));
                fail += T("SIN-006: edited artifact exists", staged.EditedPath != null && File.Exists(staged.EditedPath));
                fail += T("SIN-006: manifest exists", staged.ManifestPath != null && File.Exists(staged.ManifestPath));
                if (staged.Staged && staged.OriginalPath != null && staged.EditedPath != null)
                {
                    byte[] orig = File.ReadAllBytes(staged.OriginalPath);
                    byte[] edit = File.ReadAllBytes(staged.EditedPath);
                    fail += T("SIN-006: original SHA matches claim", SinSandboxRestoreVerifier.Sha256Bytes(orig) == staged.OriginalSha256);
                    fail += T("SIN-006: edited SHA matches claim", SinSandboxRestoreVerifier.Sha256Bytes(edit) == staged.EditedSha256);
                    fail += T("SIN-006: edited != original", staged.EditedSha256 != staged.OriginalSha256);
                    fail += T("SIN-006: original artifact == corpus bytes", SinSandboxRestoreVerifier.Sha256Bytes(orig) == fixtureShaBefore);

                    byte[]? origAi = AiScript_File.SliceAiFileFromMonster(orig);
                    byte[]? editAi = AiScript_File.SliceAiFileFromMonster(edit);
                    bool parses = origAi != null && editAi != null;
                    fail += T("SIN-006: both staged images carry an AI partition", parses);
                    if (parses)
                    {
                        AiScriptFile o = AiScript_File.Read(origAi!);
                        AiScriptFile e = AiScript_File.Read(editAi!);
                        bool clean = e.HasScript && e.CodeWalkClosedExactly && e.UnknownOpcodes.Count == 0;
                        fail += T("SIN-006: edited AI re-parses clean", clean);
                        fail += T("SIN-006: AI grew by the proven +15 block", e.Instructions.Count - o.Instructions.Count == 15);
                    }
                    fail += T("SIN-006: aim resolved", staged.WorkerIndex >= 0 && staged.EntrypointIndex >= 0);
                    fail += T("SIN-006: staging is NOT applying (RealApplyDone=NO)", !staged.RealApplyDone);
                    fail += T("SIN-006: RT2 NOT confirmed", !staged.Rt2Confirmed);
                }
            }
            else
            {
                fail += T("SIN-006: honest corpus-absent refusal", !staged.Staged && staged.Reason.Contains("corpus", StringComparison.OrdinalIgnoreCase));
            }

            // ---------------------------------------------------------------- refusals
            Console.WriteLine();
            Console.WriteLine("---- refusals (no permission / SIN-009 / SIN-010) → zero files ----");
            string refuseDir = Path.Combine(workRoot, "sin_rt2_live_rt0_refuse");
            SafeDeleteUnderWork(refuseDir, workRoot);
            SinRt2LiveSession.StageResult noPerm = SinRt2LiveSession.Stage(fixture, refuseDir, sin006, allowRt2InGamePilot: false);
            SinRt2LiveSession.StageResult r009 = SinRt2LiveSession.Stage(fixture, refuseDir, SinPilotRecipes.ForcePerformCommand(), allowRt2InGamePilot: true);
            SinRt2LiveSession.StageResult r010 = SinRt2LiveSession.Stage(fixture, refuseDir, SinPilotRecipes.HpGuardedAction(), allowRt2InGamePilot: true);
            fail += T("no-perm: refused citing AllowRt2InGamePilot", !noPerm.Staged && noPerm.Reason.Contains("AllowRt2InGamePilot", StringComparison.Ordinal));
            fail += T("SIN-009: refused (not RT2-eligible)", !r009.Staged);
            fail += T("SIN-010: refused (not RT2-eligible)", !r010.Staged);
            fail += T("refusals left zero files", !Directory.Exists(refuseDir) || !Directory.EnumerateFileSystemEntries(refuseDir).Any());
            SafeDeleteUnderWork(refuseDir, workRoot);

            // ---------------------------------------------------------------- read-only verify, game absent
            Console.WriteLine();
            Console.WriteLine("---- VerifyLive (headless: game absent → honest not-ready, no throw, no write) ----");
            SinRt2LiveSession.VerifyResult verify = SinRt2LiveSession.VerifyLive(rt0Dir);
            Console.WriteLine($"  status: {verify.Status.Summary}");
            Console.WriteLine($"  summary: {verify.Summary}");
            // honest either way: on a dev box without the game, ReadyForVerify must be false; if a game IS running
            // in battle, verify legitimately proceeds (still read-only). Assert no-throw + zero targets w/o battle.
            fail += T("verify returned without throwing", true);
            if (!verify.Status.ReadyForVerify)
                fail += T("verify (not ready): zero targets, honest summary", verify.Targets.Count == 0 && !string.IsNullOrWhiteSpace(verify.Summary));

            // ---------------------------------------------------------------- filesystem guards + cleanup
            Console.WriteLine();
            Console.WriteLine("---- filesystem guards ----");
            HashSet<string> binAfter = SnapshotFiles(baseDir);
            fail += T("no new file created in bin/output dir", !binAfter.Except(binBefore).Any());
            if (fixture is not null)
                fail += T("real corpus fixture hash UNCHANGED", SinSandboxRestoreVerifier.Sha256(fixture) == fixtureShaBefore);
            SafeDeleteUnderWork(rt0Dir, workRoot);
            Console.WriteLine($"  rt0 dir cleaned: {rt0Dir}");

            Console.WriteLine();
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - LIVE glue stages the proven SIN-006 artifacts (operator-gated) and verifies read-only; " +
                  "refusals leave zero files; no real file/game/RAM write path exists. RT2 stays operator-judged; " +
                  "SIN stays BLOCKED for public writing."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");
            Console.WriteLine();
            Console.WriteLine("SIN continua BLOCKED para escrita publica.");
            Console.WriteLine("Stage nao e apply; verify e read-only; o swap real e passo manual do runbook.");
            return fail == 0 ? 0 : 1;
        }

        /// <summary>--sin-pilot-rt2-stage : operator command. Stages SIN-006 to work/sin_rt2_live (KEPT) and prints
        /// the runbook next-steps. Operator-gated by the explicit flag the caller just typed.</summary>
        public static int RunStage(string? monsterId = null)
        {
            Console.WriteLine("=== SIN RT2 pilot STAGING (operator command — staging is NOT applying) ===");
            // optional operator-chosen target (e.g. "m226"); falls back to the default fixture list.
            string? fixture = monsterId is not null
                ? Path.Combine(CorpusRoot, "_" + monsterId, monsterId + ".bin")
                : FixtureIds
                    .Select(id => Path.Combine(CorpusRoot, "_" + id, id + ".bin"))
                    .FirstOrDefault(File.Exists);
            if (monsterId is not null && !File.Exists(fixture))
            {
                Console.WriteLine($"NOT staged: fixture '{fixture}' not found in the corpus.");
                return 1;
            }
            SinRt2LiveSession.StageResult r = SinRt2LiveSession.Stage(
                fixture, OperatorStageDir, SinPilotRecipes.TurnOneSelfBuff(), allowRt2InGamePilot: true);
            Console.WriteLine(r.Staged
                ? $"STAGED.\n  original : {r.OriginalPath}\n           SHA {r.OriginalSha256}\n  edited   : {r.EditedPath}\n           SHA {r.EditedSha256}\n  manifest : {r.ManifestPath}\n  aim      : worker {r.WorkerIndex}, entrypoint {r.EntrypointIndex}\n  observe  : {r.OnScreenEffect}\n\nNEXT: follow docs/ai/SIN_YU_YEVON_RT2_OPERATOR_RUNBOOK_2026-06-10.md (6 OK checkpoints, backup FIRST, manual swap, battle, observe, RESTORE IMMEDIATELY)."
                : $"NOT staged: {r.Reason}");
            Console.WriteLine("SIN continua BLOCKED para escrita publica.");
            return r.Staged ? 0 : 1;
        }

        /// <summary>--sin-pilot-rt2-verify : operator command. READ-ONLY: which image is loaded in the live battle?</summary>
        public static int RunVerify()
        {
            Console.WriteLine("=== SIN RT2 pilot LIVE VERIFY (read-only — never writes RAM or disk) ===");
            SinRt2LiveSession.VerifyResult v = SinRt2LiveSession.VerifyLive(OperatorStageDir);
            Console.WriteLine($"status : {v.Status.Summary}");
            foreach ((SinRt2LiveTarget t, SinRt2LiveAiClassification ai) in v.Targets)
                Console.WriteLine($"  slot {t.Slot} · id 0x{t.RawId:X4} · HP {t.CurrentHp}/{t.MaxHp} · ScrChunks 0x{t.ScriptChunks:X8}\n    -> {ai.State}: {ai.Detail}");
            Console.WriteLine($"summary: {v.Summary}");
            Console.WriteLine(Strings.U_Rt2_LiveRt0Reminder);
            return 0;
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
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "work"));
        }

        static void SafeDeleteUnderWork(string dir, string workRoot)
        {
            string full = Path.GetFullPath(dir);
            if (!full.StartsWith(Path.GetFullPath(workRoot), StringComparison.OrdinalIgnoreCase)) return;
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
