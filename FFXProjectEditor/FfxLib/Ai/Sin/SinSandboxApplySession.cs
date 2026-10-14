using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 5 (backup/apply sandbox) ORCHESTRATOR. SANDBOX-ONLY, operator-gated.
    //
    // Takes a recipe, runs the read-only chain (Gate 2 plan -> Gate 3 validate -> Gate 4 AEON preview), decides
    // sandbox eligibility (SinSandboxApplyGate), and — ONLY if eligible AND only against a throwaway COPY under
    // work/ — performs the real, byte-safe round-trip:
    //
    //   load corpus copy -> AppendGuardedAction(always-true guard, planned linear body) -> SpliceAiFileIntoMonsterGrow
    //   -> re-read (re-parses clean) -> real AiScript_Diff.Compare (only the intended ADDED block, no drift)
    //   -> AiValidator.ValidateRebuilt (AiScriptLab-clean) -> commit to the SANDBOX copy only (backup taken first)
    //   -> verify the original is byte-identical -> restore the sandbox backup -> verify byte-identical.
    //
    // Honesty / safety, structural:
    //   - the ORIGINAL is never written. Two "originals" are protected and hash-checked: the read-only corpus source
    //     on disk, and a pristine reference copy in the sandbox. AppliedToOriginal is hard-wired NO.
    //   - the only file ever WRITTEN is the sandbox copy under work/ (path-guarded), and only after its .prev.bak
    //     backup exists.
    //   - files are created ONLY on the happy path: eligibility + emit + the conservative-diff check all pass in
    //     memory before a single byte hits disk. A blocked/failed/rejected recipe leaves NO files.
    //   - public authoring stays blocked (PublicApplyAllowed = NO) and RT2 is never run (RT2 = NO).
    //
    // The linear body is realized via AppendGuardedAction with an ALWAYS-TRUE guard (PUSHII 1): it is the only clean,
    // self-relocating method to prepend ops at an entrypoint and fall through to the original handler. It appends at
    // the end of code, so existing instruction offsets are stable and AiScript_Diff reports a clean ADDED-only block
    // (+2 jump slots, +1 D7, +1 B0). This shape divergence from a pure linear insert is DISCLOSED, not hidden, and
    // is structural only — the in-game behaviour of the inserted block is RT2 territory and is NOT claimed here.
    //
    // Backup/apply sandbox nao e authoring publico.
    public static class SinSandboxApplySession
    {
        const byte PUSHII = 0xAE;

        /// <summary>Run one sandbox-apply attempt. <paramref name="corpusFixturePath"/> is a real monster .bin read
        /// READ-ONLY (may live on the extracted-game drive); it is copied into <paramref name="workDir"/> (which MUST
        /// be under work/) and only the copy is ever written. Returns a structured, inspectable result.</summary>
        public static SinSandboxApplyResult Run(
            string? corpusFixturePath,
            string workDir,
            SinChainRecipe recipe,
            bool allowSandboxApply)
        {
            ArgumentNullException.ThrowIfNull(workDir);
            ArgumentNullException.ThrowIfNull(recipe);

            // --- read-only chain (reuse Gate 2/3/4) ---
            SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
            SinPlanValidationResult validation = SinPlanValidator.Validate(plan);
            SinAeonDiffPreview preview = SinAeonDiffPlanner.Plan(plan, validation);

            string validationSummary = SummariseValidation(validation);
            string aeonLabel = AeonLabel(preview);

            SinSandboxEligibility elig = SinSandboxApplyGate.Evaluate(plan, validation, preview, allowSandboxApply);

            SinSandboxApplyResult Base(SinSandboxOutcome outcome, string? reason, IReadOnlyList<string>? notes = null) =>
                new()
                {
                    RecipeId = recipe.Id,
                    RecipeDisplayName = recipe.DisplayName,
                    Outcome = outcome,
                    ValidationSummary = validationSummary,
                    AeonPreviewLabel = aeonLabel,
                    BlockReason = reason,
                    AppliedToOriginal = false,
                    PublicApplyAllowed = false,
                    Rt2 = false,
                    Notes = notes ?? new List<string>(),
                };

            // --- conditions 1-8: eligibility ---
            if (!elig.Permitted)
                return Base(SinSandboxOutcome.Blocked, elig.Reason);

            // --- corpus present? (the real apply needs a real monster fixture, read-only) ---
            if (string.IsNullOrEmpty(corpusFixturePath) || !File.Exists(corpusFixturePath))
                return Base(SinSandboxOutcome.Skipped,
                    "eligibility PASSED but no monster fixture corpus is present — real sandbox apply skipped " +
                    "(run on a machine with the extracted corpus to exercise the harness)");

            byte[] corpusBytes;
            try { corpusBytes = File.ReadAllBytes(corpusFixturePath); }
            catch (Exception ex) { return Base(SinSandboxOutcome.Skipped, $"could not read fixture: {ex.Message}"); }

            // --- emit in memory (real codec). No file is written until this succeeds. ---
            EmitOutcome emit;
            try { emit = EmitEligible(corpusBytes, plan); }
            catch (Exception ex) { return Base(SinSandboxOutcome.EmitFailed, $"codec emit threw {ex.GetType().Name}: {ex.Message}"); }
            if (!emit.Ok)
                return Base(SinSandboxOutcome.EmitFailed, emit.Error);

            // --- real AEON diff + AiScriptLab-clean (still in memory) ---
            IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(emit.OriginalScript!, emit.EditedScript!);
            bool conservative = IsConservativeAddedOnlyDiff(
                diff, emit.OriginalScript!.Instructions.Count, emit.EditedScript!.Instructions.Count,
                out int added, out int modified, out int removed);
            bool reparsed = emit.EditedScript!.HasScript && emit.EditedScript!.CodeWalkClosedExactly
                            && emit.EditedScript!.UnknownOpcodes.Count == 0;
            AiValidationReport rebuiltReport = AiValidator.ValidateRebuilt(emit.EditedAiFile!, emit.OriginalAiFile!.Length);
            bool labClean = rebuiltReport.IsValid;

            var notes = new List<string>(emit.Notes)
            {
                "linear body realized via AppendGuardedAction(always-true guard PUSHII 1) — appends at end of code, " +
                "+2 jump slots; structural diff is clean ADDED-only. In-game placement/behaviour is RT2 (not claimed).",
            };

            if (!conservative || !reparsed || !labClean)
            {
                return Base(SinSandboxOutcome.DiffRejected,
                    $"post-apply check failed (added={added} modified={modified} removed={removed}; " +
                    $"reparsed={reparsed}; aiScriptLabClean={labClean}) — refused; nothing committed to the sandbox",
                    notes);
            }

            // --- FILE PHASE: now (and only now) touch disk, under work/ only ---
            string full;
            try { full = GuardWorkDir(workDir); }
            catch (Exception ex) { return Base(SinSandboxOutcome.Blocked, $"filesystem guard refused the work dir: {ex.Message}"); }

            string id = Path.GetFileNameWithoutExtension(corpusFixturePath);
            string sourcePath = Path.Combine(full, id + ".source.bin");   // pristine reference — never written after creation
            string sandboxPath = Path.Combine(full, id + ".sandbox.bin"); // the working copy — the ONLY file we write

            GuardWriteTarget(sourcePath);
            GuardWriteTarget(sandboxPath);

            string corpusBefore = SinSandboxRestoreVerifier.Sha256(corpusFixturePath);

            // create the two copies (source = pristine ref, sandbox = working copy). Both == corpus initially.
            File.WriteAllBytes(sourcePath, corpusBytes);
            File.WriteAllBytes(sandboxPath, corpusBytes);
            string originalBefore = SinSandboxRestoreVerifier.Sha256(sourcePath);

            // backup the SANDBOX copy BEFORE any apply (rung-4: backup-ready).
            SinSandboxBackup backup = SinSandboxBackup.Create(sandboxPath);

            // commit: write the edited monster to the SANDBOX copy only.
            File.WriteAllBytes(sandboxPath, emit.EditedMonster!);
            string sandboxPostApply = SinSandboxRestoreVerifier.Sha256(sandboxPath);

            // the original (pristine ref + the real corpus source) must be untouched.
            string originalAfter = SinSandboxRestoreVerifier.Sha256(sourcePath);
            string corpusAfter = SinSandboxRestoreVerifier.Sha256(corpusFixturePath);
            bool originalUntouched = originalAfter == originalBefore && corpusAfter == corpusBefore;
            bool sandboxChanged = sandboxPostApply != originalBefore;

            // restore the sandbox from its backup; it must come back byte-identical to the pre-apply image.
            backup.Restore();
            string sandboxPostRestore = SinSandboxRestoreVerifier.Sha256(sandboxPath);
            bool restoreByteIdentical = SinSandboxRestoreVerifier.ByteIdentical(sandboxPath, backup.BackupPath)
                                        && sandboxPostRestore == originalBefore
                                        && backup.BackupMatchesPreImage();

            if (!originalUntouched)
                notes.Add("WARNING: original hash changed — investigate (should be impossible; original is never written).");
            if (!sandboxChanged)
                notes.Add("WARNING: sandbox hash did not change after apply — investigate.");

            return new SinSandboxApplyResult
            {
                RecipeId = recipe.Id,
                RecipeDisplayName = recipe.DisplayName,
                Outcome = SinSandboxOutcome.Applied,
                ValidationSummary = validationSummary,
                AeonPreviewLabel = aeonLabel,
                SandboxTarget = sandboxPath,
                FixtureSource = corpusFixturePath,
                WorkerIndex = emit.WorkerIndex,
                EntrypointIndex = emit.EntrypointIndex,
                WorkerResolution = emit.WorkerResolution,
                BackupCreated = true,
                AppliedToOriginal = false,
                AppliedToSandbox = true,
                UnexpectedDiff = !conservative,
                RestoreByteIdentical = restoreByteIdentical && originalUntouched && sandboxChanged,
                PublicApplyAllowed = false,
                Rt2 = false,
                ExpectedAddedRows = emit.ExpectedAdded,
                ActualAddedRows = added,
                ActualModifiedRows = modified,
                ActualRemovedRows = removed,
                ReParsedClean = reparsed,
                AiScriptLabClean = labClean,
                EditedMonsterBytes = emit.EditedMonster,
                OriginalSha256Before = originalBefore,
                OriginalSha256After = originalAfter,
                CorpusSha256Before = corpusBefore,
                CorpusSha256After = corpusAfter,
                SandboxSha256PostApply = sandboxPostApply,
                SandboxSha256PostRestore = sandboxPostRestore,
                BackupSha256 = backup.PreImageSha256,
                Notes = notes,
            };
        }

        // ---- the real codec emit (in memory; no disk) ----

        sealed class EmitOutcome
        {
            public bool Ok;
            public string? Error;
            public byte[]? OriginalAiFile;
            public byte[]? EditedAiFile;
            public AiScriptFile? OriginalScript;
            public AiScriptFile? EditedScript;
            public byte[]? EditedMonster;
            public int ExpectedAdded;
            public int WorkerIndex = -1;
            public int EntrypointIndex = -1;
            public string WorkerResolution = "";
            public List<string> Notes = new();
        }

        static EmitOutcome EmitEligible(byte[] monsterBin, SinApplyPlan plan, bool stopAfterAction = false,
            IReadOnlyList<AiInstruction>? guardOverride = null,
            IReadOnlyList<AiInstruction>? actionPrefixOverride = null,
            int? entrypointOverride = null)
        {
            if (plan.Steps.Count != 1)
                return Fail(new EmitOutcome(), $"pilot expects exactly one node; plan has {plan.Steps.Count} steps");
            SinPlannedStep step = plan.Steps[0];
            if (step.Lowering != AiSnippetKind.Linear)
                return Fail(new EmitOutcome(), $"sandbox emit only supports a linear body; step lowering is {step.Lowering}");
            return EmitEligibleBody(monsterBin, step.BodyOps, stopAfterAction, guardOverride,
                actionPrefixOverride, entrypointOverride, plan.Steps.Count);
        }

        static EmitOutcome EmitEligibleBody(
            byte[] monsterBin,
            IReadOnlyList<AiInstruction> bodyOps,
            bool stopAfterAction = false,
            IReadOnlyList<AiInstruction>? guardOverride = null,
            IReadOnlyList<AiInstruction>? actionPrefixOverride = null,
            int? entrypointOverride = null,
            int expectedStepCount = 1)
        {
            var outc = new EmitOutcome();

            if (expectedStepCount != 1)
                return Fail(outc, $"pilot expects exactly one node; plan has {expectedStepCount} steps");
            if (bodyOps.Count == 0)
                return Fail(outc, "step has no planned linear body ops");

            byte[]? origAi = AiScript_File.SliceAiFileFromMonster(monsterBin);
            if (origAi is null)
                return Fail(outc, "fixture has no AI partition");
            AiScriptFile origScript = AiScript_File.Read(origAi);
            if (!origScript.HasScript)
                return Fail(outc, "fixture AI script is empty/degenerate");

            // resolve OnTurn / CombatHandler (proven resolver) with a documented structural fallback.
            int wi, ei; string resolution;
            if (AiWorkerMapping.TryResolveCombatOnTurn(monsterBin, origScript, out AiEventHook hook, out string err))
            {
                wi = hook.WorkerIndex;
                ei = entrypointOverride ?? hook.EntrypointIndex;
                if (ei < 0 || ei >= origScript.Workers[wi].Entrypoints.Count)
                    return Fail(outc, $"entrypoint override {ei} out of range for worker {wi}");

                resolution = entrypointOverride is null
                    ? $"OnTurn/CombatHandler resolved via AiWorkerMapping.TryResolveCombatOnTurn → worker {wi}, entrypoint {ei}"
                    : $"CombatHandler worker resolved via OnTurn map → worker {wi}, explicit entrypoint {ei}";
            }
            else
            {
                AiWorker? w = origScript.Workers.FirstOrDefault(x => x.InferredType == "CombatHandler" && x.Entrypoints.Count > 0)
                              ?? origScript.Workers.FirstOrDefault(x => x.Entrypoints.Count > 0);
                if (w is null)
                    return Fail(outc, $"no worker with an entrypoint (resolver said: {err})");
                wi = w.Index; ei = 0;
                resolution = $"OnTurn resolver failed ({err}); STRUCTURAL fallback → worker {wi} ({w.InferredType}), entrypoint 0 (placement is structural-only, not RT2)";
                outc.Notes.Add(resolution);
            }

            var guard = guardOverride?.ToList() ?? new List<AiInstruction> { MkOp(PUSHII, 1) };
            var action = bodyOps.ToList();
            if (actionPrefixOverride is { Count: > 0 })
                action.InsertRange(0, actionPrefixOverride.Select(Clone));
            int expectedAdded = guard.Count + 1 /* D7 POPXNCJMP */ + action.Count + (stopAfterAction ? 1 : 1);

            byte[] newAi = AiScript_File.AppendGuardedAction(origScript, wi, ei, guard, action, stopAfterAction);
            byte[] editedMonster = AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBin, newAi);
            byte[]? reAi = AiScript_File.SliceAiFileFromMonster(editedMonster);
            if (reAi is null)
                return Fail(outc, "edited monster lost its AI partition (should be impossible)");
            AiScriptFile editedScript = AiScript_File.Read(reAi);

            outc.Ok = true;
            outc.OriginalAiFile = origAi;
            outc.EditedAiFile = newAi;
            outc.OriginalScript = origScript;
            outc.EditedScript = editedScript;
            outc.EditedMonster = editedMonster;
            outc.ExpectedAdded = expectedAdded;
            outc.WorkerIndex = wi;
            outc.EntrypointIndex = ei;
            outc.WorkerResolution = resolution;
            return outc;
        }

        static EmitOutcome Fail(EmitOutcome o, string error) { o.Ok = false; o.Error = error; return o; }

        /// <summary>Mod authoring emit — raw ATEL action body without a SinChainRecipe plan.</summary>
        public static SinMonsterEmitResult TryEmitRawAction(
            byte[] monsterBin,
            string recipeId,
            IReadOnlyList<AiInstruction> action,
            int? entrypointOverride = null,
            IReadOnlyList<AiInstruction>? guardOverride = null,
            bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(recipeId);
            ArgumentNullException.ThrowIfNull(action);
            if (action.Count == 0)
                return SinMonsterEmitResult.Fail(recipeId, "empty action body");

            byte[]? origAi = AiScript_File.SliceAiFileFromMonster(monsterBin);
            if (origAi is null)
                return SinMonsterEmitResult.Fail(recipeId, "fixture has no AI partition");
            AiScriptFile origScript = AiScript_File.Read(origAi);

            EmitOutcome emit;
            try
            {
                emit = EmitEligibleBody(monsterBin, action, stopAfterAction, guardOverride,
                    entrypointOverride: entrypointOverride);
            }
            catch (Exception ex)
            {
                return SinMonsterEmitResult.Fail(recipeId, $"emit threw {ex.GetType().Name}: {ex.Message}");
            }

            if (!emit.Ok)
                return SinMonsterEmitResult.Fail(recipeId, emit.Error ?? "emit failed");

            IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(emit.OriginalScript!, emit.EditedScript!);
            bool conservative = IsConservativeAddedOnlyDiff(
                diff, emit.OriginalScript!.Instructions.Count, emit.EditedScript!.Instructions.Count,
                out int added, out int modified, out int removed);
            bool reparsed = emit.EditedScript!.HasScript && emit.EditedScript!.CodeWalkClosedExactly
                            && emit.EditedScript!.UnknownOpcodes.Count == 0;
            AiValidationReport rebuiltReport = AiValidator.ValidateRebuilt(emit.EditedAiFile!, emit.OriginalAiFile!.Length);
            bool labClean = rebuiltReport.IsValid;

            if (!conservative || !reparsed || !labClean)
            {
                return SinMonsterEmitResult.Fail(recipeId,
                    $"post-emit check failed (+{added} ~{modified} -{removed}; reparsed={reparsed}; labClean={labClean})");
            }

            return new SinMonsterEmitResult
            {
                Ok = true,
                RecipeId = recipeId,
                EditedMonster = emit.EditedMonster,
                WorkerIndex = emit.WorkerIndex,
                EntrypointIndex = emit.EntrypointIndex,
                WorkerResolution = emit.WorkerResolution,
                AddedRows = added,
                Notes = emit.Notes,
            };
        }

        /// <summary>Mod authoring emit — same codec as sandbox apply, but in-memory only (no work/ guards).
        /// Used by <c>--sin-curse-bake</c> to patch mod-folder monster bins with backup.</summary>
        public static SinMonsterEmitResult TryEmitInMemory(
            byte[] monsterBin,
            SinChainRecipe recipe,
            bool modAuthoringBake = false,
            bool stopAfterAction = false,
            IReadOnlyList<AiInstruction>? guardOverride = null,
            IReadOnlyList<AiInstruction>? actionPrefixOverride = null,
            int? entrypointOverride = null)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(recipe);

            SinApplyPlan plan = SinDryRunPlanner.Plan(recipe);
            SinPlanValidationResult validation = SinPlanValidator.Validate(plan);
            if (!validation.StructurallyValid)
            {
                string msg = validation.Errors.FirstOrDefault()?.Message ?? "structurally invalid";
                return SinMonsterEmitResult.Fail(recipe.Id, msg);
            }

            foreach (SinPlanCheck blocker in validation.Blockers.Where(c =>
                         !SinSandboxApplyGate.ExpectedScaffoldingBlockerCodes.Contains(c.Code)
                         && !(modAuthoringBake && SinSandboxApplyGate.ModBakeIgnorableBlockerCodes.Contains(c.Code))))
            {
                return SinMonsterEmitResult.Fail(recipe.Id, $"blocker [{blocker.Code}]: {blocker.Message}");
            }

            int? resolvedEntrypoint = plan.DesiredEntrypoint ?? entrypointOverride;
            EmitOutcome emit;
            try { emit = EmitEligible(monsterBin, plan, stopAfterAction, guardOverride, actionPrefixOverride, resolvedEntrypoint); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(recipe.Id, $"emit threw {ex.GetType().Name}: {ex.Message}"); }
            if (!emit.Ok)
                return SinMonsterEmitResult.Fail(recipe.Id, emit.Error ?? "emit failed");

            IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(emit.OriginalScript!, emit.EditedScript!);
            bool conservative = IsConservativeAddedOnlyDiff(
                diff, emit.OriginalScript!.Instructions.Count, emit.EditedScript!.Instructions.Count,
                out int added, out int modified, out int removed);
            bool reparsed = emit.EditedScript!.HasScript && emit.EditedScript!.CodeWalkClosedExactly
                            && emit.EditedScript!.UnknownOpcodes.Count == 0;
            AiValidationReport rebuiltReport = AiValidator.ValidateRebuilt(emit.EditedAiFile!, emit.OriginalAiFile!.Length);
            bool labClean = rebuiltReport.IsValid;

            if (!conservative || !reparsed || !labClean)
            {
                return SinMonsterEmitResult.Fail(recipe.Id,
                    $"post-emit check failed (+{added} ~{modified} -{removed}; reparsed={reparsed}; labClean={labClean})");
            }

            return new SinMonsterEmitResult
            {
                Ok = true,
                RecipeId = recipe.Id,
                EditedMonster = emit.EditedMonster,
                WorkerIndex = emit.WorkerIndex,
                EntrypointIndex = emit.EntrypointIndex,
                WorkerResolution = emit.WorkerResolution,
                AddedRows = added,
                Notes = emit.Notes,
            };
        }

        static AiInstruction MkOp(byte opcode, ushort operand) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        static AiInstruction Clone(AiInstruction i) => new()
        {
            Offset = -1,
            Opcode = i.Opcode,
            HasOperand = i.HasOperand,
            Operand = i.Operand,
            OperandKind = i.OperandKind,
        };

        // ---- the conservative-diff predicate (shared with the RT0 negative test) ----

        /// <summary>A diff is the expected conservative block iff there are ZERO modified and ZERO removed rows and
        /// the added rows exactly equal the net instruction growth (so nothing existing drifted). An appended block
        /// (AppendGuardedAction) keeps existing offsets stable, so this holds; a mid-stream insert or any tamper
        /// produces modified/removed rows and fails it.</summary>
        public static bool IsConservativeAddedOnlyDiff(
            IReadOnlyList<AiDiffEntry> diff, int originalInstrCount, int editedInstrCount,
            out int added, out int modified, out int removed)
        {
            added = diff.Count(d => d.Type == AiDiffType.Added);
            modified = diff.Count(d => d.Type == AiDiffType.Modified);
            removed = diff.Count(d => d.Type == AiDiffType.Removed);
            int growth = editedInstrCount - originalInstrCount;
            return modified == 0 && removed == 0 && added > 0 && added == growth;
        }

        // ---- filesystem guards (condition 9: target is a copy under work/) ----

        /// <summary>Ensure the work dir is under the repo work/ tree and not in the extracted corpus; create it.
        /// Returns the resolved absolute path.</summary>
        static string GuardWorkDir(string workDir)
        {
            string full = Path.GetFullPath(workDir);
            string workRoot = ResolveWorkRoot();
            if (!full.StartsWith(workRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"work dir '{full}' is NOT under work/ ('{workRoot}')");
            if (full.Contains("FFX Extracted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("work dir points into the extracted game corpus");
            Directory.CreateDirectory(full);
            return full;
        }

        /// <summary>A write target must be under work/, not in the extracted corpus, and a copy (".source.bin" /
        /// ".sandbox.bin"). Denies any path that smells like a real game/project file.</summary>
        static void GuardWriteTarget(string path)
        {
            string full = Path.GetFullPath(path);
            string workRoot = ResolveWorkRoot();
            if (!full.StartsWith(workRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"write target '{full}' is NOT under work/ — refused");
            if (full.Contains("FFX Extracted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"write target '{full}' points into the extracted corpus — refused");
            string name = Path.GetFileName(full);
            if (!name.EndsWith(".source.bin", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".sandbox.bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"write target '{name}' is not a recognised sandbox copy name — refused");
        }

        static string ResolveWorkRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                bool hasWork = Directory.Exists(Path.Combine(dir.FullName, "work"));
                bool isRepo = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                              || (Directory.Exists(Path.Combine(dir.FullName, "FFXProjectEditor"))
                                  && Directory.Exists(Path.Combine(dir.FullName, "docs")));
                if (hasWork && isRepo)
                    return Path.GetFullPath(Path.Combine(dir.FullName, "work"));
                if (isRepo)
                    return Path.GetFullPath(Path.Combine(dir.FullName, "work"));
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("could not locate the repo work/ root from " + AppContext.BaseDirectory);
        }

        // ---- text helpers ----

        static string SummariseValidation(SinPlanValidationResult v) =>
            !v.StructurallyValid ? "structurally-invalid"
            : v.ApplyAcceptable ? "structurally-valid, apply-acceptable (UNEXPECTED)"
            : "structurally-valid, apply-blocked";

        static string AeonLabel(SinAeonDiffPreview p)
        {
            if (p.HpPercentDivMulBlocked) return "warning/blocker";
            bool conservative = p.StructurallyValid && !p.ApplyAllowed
                                && p.TotalModified == 0 && p.TotalRemoved == 0 && p.TotalAdded >= 1;
            return conservative ? "expected" : "unexpected";
        }
    }
}
