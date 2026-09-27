using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static FFXProjectEditor.FfxLib.Ai.Sin.SinPlanCheckSeverity;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 3 (AiScriptLab validation bridge). READ-ONLY.
    //
    // Takes a Gate-2 SinApplyPlan (preview-only) and runs AiScriptLab/AiValidator-compatible STRUCTURAL checks
    // over the instructions the planner WOULD emit — WITHOUT loading a monster, WITHOUT Rebuild/Splice, WITHOUT
    // touching disk and WITHOUT applying anything. It answers exactly one question:
    //
    //   "Is this preview-only plan STRUCTURALLY acceptable (the bytecode it would emit is well-formed), and what
    //    are the honest blockers/warnings/errors that still stand between it and an apply?"
    //
    // It is NOT a writer, NOT a save, NOT an AEON diff, NOT an apply. ApplyAcceptable is FALSE by construction at
    // this gate (the plan's ApplyAllowed is hard-wired false and the blockers stay intact). "Clean for DESIGN is
    // not unlocked for WRITING" — and clean STRUCTURE is not unlocked for writing either. SIN stays BLOCKED.
    //
    // Why offline (no monster): the full AiValidator.Validate(source, proposed) + AiScript_Diff need a real
    // AiScriptFile (worker entrypoints, jump-table, Rebuild dry-run). Those are the NEXT rungs (offline-emittable /
    // AEON-reviewed) and are deferred. This bridge runs the subset that is SOUND on a loose instruction stream —
    // the same opcode/operand-bit/encoding/stack-balance model AiValidator itself uses (AiScript_File + AiStackModel):
    //   1. every opcode is recognized (AiScript_File.IsKnownOpcode);
    //   2. HasOperand agrees with the 0x80 size rule (AiScript_File.IsOperandBearing) — else Emit() mis-sizes;
    //   3. per-instruction RT0: Emit() re-decodes to the same opcode + operand;
    //   4. statement-level stack balance via AiStackModel (linear body nets 0; a guard leaves EXACTLY one bool);
    //   5. the semantic-integrity finding the dry-run surfaced (the DIV/MUL opcode mismatch) is reported HONESTLY
    //      as a blocker — byte layout and net stack balance alone do not prove arithmetic semantics.
    //
    // The plan's own honest blockers and the unsatisfied rung ladder are folded in, so the result can never lie
    // green: a structurally-clean plan is still apply-BLOCKED until every rung above preview-only has actually run.

    /// <summary>Severity of one validation check. Error = malformed bytecode (would break Rebuild / the VM).
    /// Blocker = the honest "distance to Apply" (no RT2, no backup, candidate payload, semantic mismatch …) — a
    /// reason the plan cannot be applied even though its bytes are well-formed. Warning = advisory. Info = a passed
    /// check.</summary>
    public enum SinPlanCheckSeverity { Info, Warning, Error, Blocker }

    /// <summary>One validation finding: a stable Code (for gate assertions) + a human Message.</summary>
    public sealed record SinPlanCheck(SinPlanCheckSeverity Severity, string Code, string Message);

    /// <summary>The read-only result of validating one SinApplyPlan. Mirrors AiValidationReport's vocabulary but at
    /// the plan level. StructurallyValid == no Error; ApplyAcceptable is false at Gate 3 by construction.</summary>
    public sealed class SinPlanValidationResult
    {
        public required string RecipeId { get; init; }
        public required string RecipeDisplayName { get; init; }
        public required IReadOnlyList<SinPlanCheck> Checks { get; init; }

        /// <summary>FALSE at Gate 3 by construction — the plan's ApplyAllowed is hard-wired false and blockers
        /// stand. The validator NEVER raises this; it only reports what the plan declares.</summary>
        public required bool ApplyAcceptable { get; init; }

        public IEnumerable<SinPlanCheck> Errors => Checks.Where(c => c.Severity == Error);
        public IEnumerable<SinPlanCheck> Blockers => Checks.Where(c => c.Severity == Blocker);
        public IEnumerable<SinPlanCheck> Warnings => Checks.Where(c => c.Severity == Warning);
        public IEnumerable<SinPlanCheck> Infos => Checks.Where(c => c.Severity == Info);

        /// <summary>The bytecode the plan WOULD emit is well-formed (no structural Error). Mirrors
        /// AiValidator.IsValid. A semantic blocker (e.g. DIV/MUL) does NOT make a plan structurally invalid —
        /// the bytes still re-parse; that is exactly why such a finding is a Blocker, not an Error.</summary>
        public bool StructurallyValid => !Errors.Any();

        public int ErrorCount => Errors.Count();
        public int BlockerCount => Blockers.Count();
        public int WarningCount => Warnings.Count();

        /// <summary>True if any check carries this stable code (gate-assertion helper).</summary>
        public bool HasCode(string code) => Checks.Any(c => c.Code == code);
        public bool HasCode(string code, SinPlanCheckSeverity severity) =>
            Checks.Any(c => c.Code == code && c.Severity == severity);

        public string ToReportString()
        {
            string verdict = !StructurallyValid
                ? "STRUCTURALLY INVALID — would break Rebuild/the VM"
                : ApplyAcceptable
                    ? "apply-acceptable (UNEXPECTED at Gate 3)"
                    : "structurally valid · apply BLOCKED (preview-only)";

            var sb = new StringBuilder();
            sb.AppendLine($"=== SIN plan validation: {RecipeId} · {RecipeDisplayName} ===");
            sb.AppendLine($"verdict: {verdict}");
            sb.AppendLine($"errors {ErrorCount} · blockers {BlockerCount} · warnings {WarningCount} · infos {Infos.Count()}");
            foreach (SinPlanCheck c in Errors.Concat(Blockers).Concat(Warnings).Concat(Infos))
            {
                char tag = c.Severity switch { Error => 'E', Blocker => 'B', Warning => 'W', _ => 'i' };
                sb.AppendLine($"[{tag}] ({c.Code}) {c.Message}");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>The low-level AiScriptLab/AiValidator-compatible checks for a LOOSE instruction stream (no
    /// AiScriptFile). Pure: reads only AiScript_File's static tables + AiStackModel; never writes, never throws.</summary>
    public static class SinAiScriptLabBridge
    {
        /// <summary>Opcode validity + operand-flag consistency + per-instruction RT0 (mirrors AiValidator §1).</summary>
        public static void CheckEncoding(IReadOnlyList<AiInstruction> ops, string where, List<SinPlanCheck> outChecks)
        {
            foreach (AiInstruction i in ops)
            {
                if (!AiScript_File.IsKnownOpcode(i.Opcode))
                    outChecks.Add(new SinPlanCheck(Error, "opcode-unknown",
                        $"{where}: opcode 0x{i.Opcode:X2} ({AiScript_File.Mnemonic(i.Opcode)}) is not one of the 48 proven — the VM has no handler."));

                if (i.HasOperand != AiScript_File.IsOperandBearing(i.Opcode))
                    outChecks.Add(new SinPlanCheck(Error, "operand-flag",
                        $"{where}: opcode 0x{i.Opcode:X2} HasOperand={i.HasOperand} contradicts the 0x80 size rule — Emit() would mis-size the stream."));

                // per-instruction RT0: the bytes Emit() produces must decode back to the same opcode + operand.
                byte[] b = i.Emit();
                bool bearing = (b[0] & 0x80) != 0;
                if (b.Length != i.Length || bearing != i.HasOperand)
                    outChecks.Add(new SinPlanCheck(Error, "encoding-rt0",
                        $"{where}: re-encode of 0x{i.Opcode:X2} disagrees with the size rule (len {b.Length}, bearing {bearing})."));
                else if (bearing)
                {
                    ushort back = (ushort)(b[1] | (b[2] << 8));
                    if (back != i.Operand)
                        outChecks.Add(new SinPlanCheck(Error, "encoding-rt0",
                            $"{where}: operand round-trip failed (0x{i.Operand:X4} -> 0x{back:X4})."));
                }
            }
        }

        /// <summary>Simulate VM stack depth through a statement stream (start depth 0). HONEST: it stops asserting
        /// past an op whose effect/arity is not modelled (so it never guesses). Mirrors AiValidator's Level-1
        /// SimulateUnderflows but absolute (these are self-contained proven statements, not a worker block).</summary>
        public static (bool confident, int depth, bool underflow) SimulateDepth(IReadOnlyList<AiInstruction> ops)
        {
            int depth = 0;
            bool confident = true, underflow = false;
            foreach (AiInstruction i in ops)
            {
                int? delta;
                if (i.Opcode == 0xB5 || i.Opcode == 0xD8)
                {
                    int? args = AiStackModel.CallArgCount(i.Operand);
                    delta = args == null ? (int?)null : (i.Opcode == 0xB5 ? 1 - args.Value : -args.Value);
                }
                else delta = AiStackModel.NetNonCall(i.Opcode);

                if (delta == null) { confident = false; continue; }
                if (!confident) continue;
                depth += delta.Value;
                if (depth < 0) { underflow = true; depth = 0; }
            }
            return (confident, depth, underflow);
        }
    }

    public static class SinPlanValidator
    {
        // Native interpreter @0x864180, cases @0x864918 (MUL) / @0x86495B (DIV).
        // See FFX_EVENTVM_OPS_2026-09-17; older SIN notes reversed these two opcodes.
        const byte OpDiv = 0x17;   // '/'
        const byte OpMul = 0x16;   // '*'

        /// <summary>Validate a Gate-2 SinApplyPlan. Read-only: no monster, no Rebuild, no disk, no apply.</summary>
        public static SinPlanValidationResult Validate(SinApplyPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var checks = new List<SinPlanCheck>();

            // --- A. honesty invariants from the plan itself (Gate-2 contract; lying-green guards) ---
            checks.Add(plan.ApplyAllowed
                ? new SinPlanCheck(Error, "apply-allowed", "Gate-2 contract violation: plan.ApplyAllowed is true — a dry-run plan must never allow apply.")
                : new SinPlanCheck(Info, "apply-allowed", "plan.ApplyAllowed == false (preview-only, as required)."));

            checks.Add(plan.Rung != SinPromotionRung.PreviewOnly
                ? new SinPlanCheck(Error, "rung", $"plan rung is {plan.Rung} but no gate above preview-only has run — promotion without a gate.")
                : new SinPlanCheck(Info, "rung", "plan rung == preview-only (no gate promoted it)."));

            if (plan.ClaimsAnyProvedPayload)
                checks.Add(new SinPlanCheck(Error, "proved-payload",
                    "a step claims a Proved command payload — the dry-run must never promote a candidate to proved."));

            // --- B. per-step structural checks (AiScriptLab/AiValidator-compatible, offline) ---
            for (int s = 0; s < plan.Steps.Count; s++)
                CheckStep(plan.Steps[s], s + 1, plan.Steps.Count, checks);

            // --- C. fold the plan's own honest blockers + the unsatisfied rung ladder ---
            foreach (string b in plan.Blockers)
                checks.Add(new SinPlanCheck(Blocker, "plan-blocker", b));
            int unmet = plan.RequiredGates.Count(g => !g.Satisfied);
            if (unmet > 0)
                checks.Add(new SinPlanCheck(Blocker, "rung-ladder",
                    $"rung ladder unsatisfied: {unmet}/{plan.RequiredGates.Count} gates not run " +
                    "(offline-emittable / AiScriptLab-clean / AEON-reviewed / backup-ready / RT2-proved / template-public)."));

            // --- D. apply-acceptability: FALSE by construction at Gate 3 (and we report which clause kept it false) ---
            bool structurallyValid = !checks.Any(c => c.Severity == Error);
            bool anyBlocker = checks.Any(c => c.Severity == Blocker);
            bool applyAcceptable = plan.ApplyAllowed
                                   && structurallyValid
                                   && !anyBlocker
                                   && plan.Rung >= SinPromotionRung.BackupReady;
            checks.Add(applyAcceptable
                ? new SinPlanCheck(Warning, "apply-acceptable", "apply-acceptable computed TRUE — UNEXPECTED at Gate 3 (investigate).")
                : new SinPlanCheck(Info, "apply-acceptable",
                    "apply NOT acceptable: ApplyAllowed=false + blockers stand + rung below backup-ready (correct for Gate 3)."));

            return new SinPlanValidationResult
            {
                RecipeId = plan.RecipeId,
                RecipeDisplayName = plan.RecipeDisplayName,
                Checks = checks,
                ApplyAcceptable = applyAcceptable,
            };
        }

        static void CheckStep(SinPlannedStep step, int idx, int total, List<SinPlanCheck> checks)
        {
            string where = $"step {idx}/{total}";
            bool guarded = step.Lowering == AiSnippetKind.GuardedAction;
            IReadOnlyList<AiInstruction> guard = step.GuardOps;
            IReadOnlyList<AiInstruction> body = step.BodyOps;

            if (guard.Count == 0 && body.Count == 0)
                checks.Add(new SinPlanCheck(Warning, "no-ops",
                    $"{where}: no raw planned instructions to validate (unresolved lowering — only the step blockers below apply)."));

            // 1-3. encoding (opcode set + 0x80 size rule + per-instruction RT0)
            SinAiScriptLabBridge.CheckEncoding(guard, $"{where} guard", checks);
            SinAiScriptLabBridge.CheckEncoding(body, $"{where} body", checks);

            // 4. statement-level stack balance
            if (guarded)
            {
                if (guard.Count > 0)
                {
                    (bool conf, int depth, bool under) = SinAiScriptLabBridge.SimulateDepth(guard);
                    if (under)
                        checks.Add(new SinPlanCheck(Error, "stack-underflow", $"{where}: guard underflows the stack."));
                    if (!conf)
                        checks.Add(new SinPlanCheck(Warning, "stack-unverified",
                            $"{where}: guard has an unknown-arity op — stack balance not fully verifiable offline (honest stop)."));
                    else if (depth != 1)
                        checks.Add(new SinPlanCheck(Error, "guard-bool",
                            $"{where}: guard leaves {depth} value(s) but D7/POPXNCJMP needs EXACTLY 1 bool."));
                    else
                        checks.Add(new SinPlanCheck(Info, "guard-bool",
                            $"{where}: guard leaves exactly 1 bool for D7/POPXNCJMP (net +1)."));
                }
                CheckBodyBalance(body, $"{where}: guarded action", checks);

                // branch shape: the D7→rejoin / B0→original jump-table slots resolve only against a real worker.
                checks.Add(new SinPlanCheck(Blocker, "jump-slots",
                    $"{where}: guarded block adds D7→rejoin + B0→original; the jump-table slots resolve only against a real worker (not validated offline)."));
            }
            else
            {
                CheckBodyBalance(body, $"{where}: linear body", checks);
            }

            // 5. semantic integrity: the SHINRYU DIV/MUL finding (explicit Gate-3 requirement)
            CheckDivMulIntegrity(step, where, checks);

            // 6. payload eligibility (candidate/blocked stay blocked; proved is an error)
            if (step.PayloadEligibility is SinPayloadEligibility elig)
            {
                checks.Add(elig switch
                {
                    SinPayloadEligibility.Candidate => new SinPlanCheck(Blocker, "payload-candidate",
                        $"{where}: command payload is a CANDIDATE (AI-perform category proved; the command's in-game effect is RT2-pending) — not authoring-ready."),
                    SinPayloadEligibility.Blocked => new SinPlanCheck(Blocker, "payload-blocked",
                        $"{where}: command payload is BLOCKED (item / non-AI category) — cannot be applied."),
                    _ => new SinPlanCheck(Error, "payload-proved",
                        $"{where}: command payload marked Proved — the dry-run must never promote to proved."),
                });
            }

            // fold the step's own honest blockers
            foreach (string b in step.StepBlockers)
                checks.Add(new SinPlanCheck(Blocker, "step-blocker", $"{where}: {b}"));
        }

        static void CheckBodyBalance(IReadOnlyList<AiInstruction> body, string where, List<SinPlanCheck> checks)
        {
            if (body.Count == 0) return;
            (bool conf, int depth, bool under) = SinAiScriptLabBridge.SimulateDepth(body);
            if (under)
                checks.Add(new SinPlanCheck(Error, "stack-underflow",
                    $"{where} underflows the stack — a CALLPOPA argument PUSH is missing."));
            else if (!conf)
                checks.Add(new SinPlanCheck(Warning, "stack-unverified",
                    $"{where} has an unknown-arity op — not fully verifiable offline (honest stop)."));
            else if (depth != 0)
                checks.Add(new SinPlanCheck(Error, "stack-balance",
                    $"{where} leaves net {depth} on the stack (expected 0 — every statement pushes its args then CALLPOPA consumes them)."));
            else
                checks.Add(new SinPlanCheck(Info, "stack-balance",
                    $"{where} balances (net 0 — statements self-consume)."));
        }

        // The SHINRYU finding, handled EXPLICITLY and HONESTLY as a regression guard:
        //   Proven table: 0x17 = DIV, 0x16 = MUL (census 345 monsters + IDA FFX_Atel_InterpretWorkerOpcodes
        //   @0x864180 + FFXDataParser). The HP% snippet must emit MUL(0x16), not DIV(0x17). The bytes for DIV and
        //   MUL both re-parse and share AiStackModel net -1, so structural checks alone cannot catch a regression;
        //   this explicit check keeps the authoring surface honest.
        static void CheckDivMulIntegrity(SinPlannedStep step, string where, List<SinPlanCheck> checks)
        {
            IReadOnlyList<AiInstruction> guard = step.GuardOps;
            if (guard.Count == 0) return;   // only the guarded (HP%) pilot has arithmetic in a guard
            bool hasDiv = guard.Any(i => i.Opcode == OpDiv);
            bool hasMul = guard.Any(i => i.Opcode == OpMul);

            if (hasDiv && !hasMul)
            {
                checks.Add(new SinPlanCheck(Warning, "divmul-integrity",
                    $"{where}: guard emits opcode 0x17 (DIV per the proven table) where the percentage idiom intends MUL (0x16). " +
                    "Structurally valid (0x17 is a real opcode; the stack balances — DIV and MUL share net -1), so semantic validation is required in addition to structural checks."));
                checks.Add(new SinPlanCheck(Blocker, "divmul-integrity",
                    $"{where}: HP% guard arithmetic opcode mismatch — the snippet emitted DIV(0x17) instead of MUL(0x16). " +
                    "Proven direction 0x17=DIV / 0x16=MUL (census + IDA@0x864180 + FFXDataParser). Fix the emitter and re-run MonsterAiEditor/AiScriptLab gates before apply."));
            }
            else if (hasMul && !hasDiv)
            {
                checks.Add(new SinPlanCheck(Info, "divmul-integrity",
                    $"{where}: guard uses 0x16 (MUL) for the percentage idiom — consistent with the proven table."));
            }
        }
    }
}
