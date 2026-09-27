using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 6 LIVE glue, orchestration side. OPERATOR-GATED; writes ONLY under work/.
    //
    // Two operations, split exactly at the honesty boundary:
    //
    //   Stage      — operator-gated. Re-runs the RT2 eligibility + the proven Gate-5 sandbox round-trip, then
    //                persists the pilot artifacts under work/sin_rt2_live/: the pristine ORIGINAL monster bytes,
    //                the EDITED (SIN-006) monster bytes the sandbox proved byte-clean, and a MANIFEST with the
    //                SHA-256s, the aim, and the runbook steps. The REAL game-file swap is the OPERATOR's manual
    //                action per the runbook (checkpoints C1-C6) — this code never touches a real game file.
    //   VerifyLive — read-only. Locates the target monster in the CURRENT battle and classifies its live AiFile
    //                byte-exactly as ORIGINAL / EDITED / divergent (SinRt2LiveProbe). This is the structural
    //                "did the grown script actually load?" proof; the on-screen effect remains the operator's call.
    //
    // Why no live RAM write: SIN-006 grows the script, and a grown script cannot be injected into the fixed live
    // region (buffer allocated at exact file size; VM structures pre-sized from original counts). The grow must ride
    // the reload path. Any code that claimed to "apply SIN-006 live via the probe" would be lying. See
    // SinRt2LiveProbe header + docs/ai/SIN_YU_YEVON_RT2_OPERATOR_RUNBOOK_2026-06-10.md.
    public static class SinRt2LiveSession
    {
        public sealed record StageResult(
            bool Staged, string Reason,
            string? OriginalPath, string? EditedPath, string? ManifestPath,
            string? OriginalSha256, string? EditedSha256,
            int WorkerIndex, int EntrypointIndex, string? OnScreenEffect)
        {
            public bool RealApplyDone => false;   // invariant — staging is not applying
            public bool Rt2Confirmed => false;    // invariant
        }

        /// <summary>Stage the SIN-006 pilot artifacts under work/. Operator-gated by the SAME AllowRt2InGamePilot
        /// permission as the preflight; refuses anything that is not RT2-eligible. Writes ONLY *.original.bin /
        /// *.sin006.bin / MANIFEST.txt inside <paramref name="stageDir"/> (which MUST be under work/).</summary>
        public static StageResult Stage(string? corpusFixturePath, string stageDir, SinChainRecipe recipe, bool allowRt2InGamePilot)
        {
            ArgumentNullException.ThrowIfNull(stageDir);
            ArgumentNullException.ThrowIfNull(recipe);

            SinRt2Eligibility elig = SinRt2PilotGate.Evaluate(recipe, allowRt2InGamePilot);
            if (!elig.Permitted)
                return new StageResult(false, $"not RT2-eligible: {elig.Reason}", null, null, null, null, null, -1, -1, null);

            if (string.IsNullOrEmpty(corpusFixturePath) || !File.Exists(corpusFixturePath))
                return new StageResult(false, "corpus fixture absent — nothing staged", null, null, null, null, null, -1, -1, null);

            // proven Gate-5 round-trip supplies the byte-clean edited image (and proves backup/restore on a copy).
            SinSandboxApplyResult sandbox = SinSandboxApplySession.Run(corpusFixturePath, stageDir, recipe, allowSandboxApply: true);
            if (sandbox.Outcome != SinSandboxOutcome.Applied || sandbox.EditedMonsterBytes is null)
                return new StageResult(false, $"sandbox proof not clean (outcome={sandbox.Outcome}: {sandbox.BlockReason}) — staging refused",
                    null, null, null, null, null, -1, -1, null);

            string dir = GuardStageDir(stageDir);
            string id = Path.GetFileNameWithoutExtension(corpusFixturePath);
            string originalPath = Path.Combine(dir, id + ".original.bin");
            string editedPath = Path.Combine(dir, id + ".sin006.bin");
            string manifestPath = Path.Combine(dir, "MANIFEST.txt");

            byte[] originalBytes = File.ReadAllBytes(corpusFixturePath);
            File.WriteAllBytes(originalPath, originalBytes);
            File.WriteAllBytes(editedPath, sandbox.EditedMonsterBytes);
            string shaOriginal = SinSandboxRestoreVerifier.Sha256Bytes(originalBytes);
            string shaEdited = SinSandboxRestoreVerifier.Sha256Bytes(sandbox.EditedMonsterBytes);

            string onScreen =
                $"Monster's first turn (OnTurn → worker {sandbox.WorkerIndex}, entrypoint {sandbox.EntrypointIndex}): " +
                "Haste (0x38) + Protect (0x31) + Shell (0x30) icons must appear on the MONSTER itself; party unaffected. HYPOTHESIS until observed.";

            var manifest = new StringBuilder();
            manifest.AppendLine("SIN GATE 6 — RT2 LIVE PILOT STAGING (operator-gated; real swap is YOUR manual step)");
            manifest.AppendLine($"recipe          : {recipe.Id} · {recipe.DisplayName}");
            manifest.AppendLine($"source fixture  : {corpusFixturePath}");
            manifest.AppendLine($"original image  : {Path.GetFileName(originalPath)}  SHA-256 {shaOriginal}");
            manifest.AppendLine($"edited  image   : {Path.GetFileName(editedPath)}  SHA-256 {shaEdited}");
            manifest.AppendLine($"aim             : {sandbox.WorkerResolution}");
            manifest.AppendLine($"diff (proved)   : +{sandbox.ActualAddedRows} ~{sandbox.ActualModifiedRows} -{sandbox.ActualRemovedRows}");
            manifest.AppendLine($"on-screen       : {onScreen}");
            manifest.AppendLine("next steps      : follow docs/ai/SIN_YU_YEVON_RT2_OPERATOR_RUNBOOK_2026-06-10.md — six OK checkpoints,");
            manifest.AppendLine("                  manual file swap (backup FIRST), battle, observe, RESTORE IMMEDIATELY, verify SHA.");
            manifest.AppendLine("verify live     : FFXProjectEditor --sin-pilot-rt2-verify  (read-only: proves which image is loaded in RAM)");
            manifest.AppendLine("invariants      : RealApplyDone=NO · Rt2Confirmed=NO · staging is not applying · SIN stays BLOCKED for public writing");
            File.WriteAllText(manifestPath, manifest.ToString());

            return new StageResult(true,
                "staged (sandbox round-trip proven byte-clean; real swap remains the operator's runbook step)",
                originalPath, editedPath, manifestPath, shaOriginal, shaEdited,
                sandbox.WorkerIndex, sandbox.EntrypointIndex, onScreen);
        }

        public sealed record VerifyResult(SinRt2LiveStatus Status, IReadOnlyList<(SinRt2LiveTarget Target, SinRt2LiveAiClassification Ai)> Targets, string Summary);

        /// <summary>Read-only live verification: which image (original vs SIN-006) is loaded for the staged monster
        /// in the CURRENT battle? Never writes RAM or disk. Honest skip when the game/battle is unavailable.</summary>
        public static VerifyResult VerifyLive(string stageDir)
        {
            string dir = Path.GetFullPath(stageDir);
            string? originalPath = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.original.bin").FirstOrDefault() : null;
            string? editedPath = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.sin006.bin").FirstOrDefault() : null;
            SinRt2LiveStatus status = SinRt2LiveProbe.Status();

            if (originalPath is null || editedPath is null)
                return new VerifyResult(status, Array.Empty<(SinRt2LiveTarget, SinRt2LiveAiClassification)>(),
                    $"staging artifacts not found under {dir} — run --sin-pilot-rt2-stage first.");

            string id = Path.GetFileNameWithoutExtension(originalPath); // "m201.original" -> strip again
            id = Path.GetFileNameWithoutExtension(id);
            if (id.Length < 2 || !int.TryParse(id.AsSpan(1), out int monsterNumber))
                return new VerifyResult(status, Array.Empty<(SinRt2LiveTarget, SinRt2LiveAiClassification)>(),
                    $"could not derive the monster number from '{id}'.");

            byte[]? originalAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(originalPath));
            byte[]? editedAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(editedPath));
            if (originalAi is null || editedAi is null)
                return new VerifyResult(status, Array.Empty<(SinRt2LiveTarget, SinRt2LiveAiClassification)>(),
                    "staged image has no AI partition (should be impossible) — re-stage.");

            if (!status.ReadyForVerify)
                return new VerifyResult(status, Array.Empty<(SinRt2LiveTarget, SinRt2LiveAiClassification)>(), status.Summary);

            IReadOnlyList<SinRt2LiveTarget> targets = SinRt2LiveProbe.LocateMonster(monsterNumber);
            if (targets.Count == 0)
                return new VerifyResult(status, Array.Empty<(SinRt2LiveTarget, SinRt2LiveAiClassification)>(),
                    $"no live battle slot matches m{monsterNumber:D3} — enter the right encounter first.");

            var rows = targets
                .Select(t => (t, SinRt2LiveProbe.ClassifyLiveAi(t.ScriptChunks, originalAi, editedAi)))
                .ToList();
            string summary = string.Join(" | ", rows.Select(r => $"slot {r.t.Slot}: {r.Item2.State}"));
            return new VerifyResult(status, rows, summary);
        }

        // stage dir must live under the repo work/ tree (same contract as the Gate-5 session, independently enforced).
        static string GuardStageDir(string stageDir)
        {
            string full = Path.GetFullPath(stageDir);
            string workRoot = ResolveWorkRoot();
            if (!full.StartsWith(workRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, workRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"stage dir '{full}' is NOT under work/ ('{workRoot}') — refused");
            if (full.Contains("FFX Extracted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("stage dir points into the extracted game corpus — refused");
            Directory.CreateDirectory(full);
            return full;
        }

        static string ResolveWorkRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                bool isRepo = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                              || (Directory.Exists(Path.Combine(dir.FullName, "FFXProjectEditor"))
                                  && Directory.Exists(Path.Combine(dir.FullName, "docs")));
                if (isRepo) return Path.GetFullPath(Path.Combine(dir.FullName, "work"));
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("could not locate the repo work/ root from " + AppContext.BaseDirectory);
        }
    }
}
