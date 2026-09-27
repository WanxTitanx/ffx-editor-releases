using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 2 dry-run planner (READ-ONLY).
    //
    // Turns a SinChainRecipe into a SinApplyPlan WITHOUT touching a single byte on disk and WITHOUT invoking any
    // byte emitter. It "lowers" each node only as far as choosing the proven AiSnippetLibrary template and
    // EXPANDING it in memory (the snippet library returns an AiInstruction list — pure, no I/O). It deliberately
    // does NOT call AiScript_File.Rebuild / AppendGuardedAction / Splice, does NOT load a monster_*.bin, does NOT
    // run AiValidator; only pure guard construction is used from AiAutomation. Those are downstream gates and are reported as blockers.
    //
    // Why stop at template expansion: it is enough to PREVIEW the shape (instructions, branch, stack, payload)
    // honestly, while the actual relocation/validation/diff/backup/RT2 remain future, gated work. This keeps
    // Gate 2 strictly preview-only: "if this were authorized, this is what I would try to build."

    public static class SinDryRunPlanner
    {
        /// <summary>Plan a recipe into a read-only SinApplyPlan. Never writes; never loads a monster.</summary>
        public static SinApplyPlan Plan(SinChainRecipe recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);

            var steps = new List<SinPlannedStep>(recipe.Nodes.Count);
            var warnings = new List<string>();
            foreach (SinChainNode node in recipe.Nodes)
                steps.Add(PlanNode(node, warnings));

            return new SinApplyPlan
            {
                RecipeId = recipe.Id,
                RecipeDisplayName = recipe.DisplayName,
                Tier = recipe.Tier,
                Threat = recipe.Threat,
                Rung = SinPromotionRung.PreviewOnly,   // Gate 2 caps here; only a real gate raises it.
                ApplyAllowed = false,                  // hard-wired for the whole gate.
                Steps = steps,
                Warnings = warnings.Distinct().ToList(),
                Blockers = GlobalBlockers,
                RequiredGates = RungLadder,
                DesiredEntrypoint = recipe.DesiredEntrypoint,
            };
        }

        // ---- per-node lowering (preview-only) ----

        static SinPlannedStep PlanNode(SinChainNode node, List<string> warnings)
        {
            (string triggerLabel, string workerResolution, IReadOnlyList<string> triggerBlockers) = ResolveTrigger(node.Trigger);

            // Lowering rule (spec §3.1): Always + single-or-combo of linear actions -> Linear (Rebuild insert);
            // a non-Always condition -> guarded block (AppendGuardedAction). Chance is not modelled in the pilots.
            bool guarded = node.Condition is not SinCondition.Always;

            return guarded
                ? PlanGuarded(node, triggerLabel, workerResolution, triggerBlockers, warnings)
                : PlanLinear(node, triggerLabel, workerResolution, triggerBlockers, warnings);
        }

        static SinPlannedStep PlanLinear(SinChainNode node, string triggerLabel, string workerResolution,
            IReadOnlyList<string> triggerBlockers, List<string> warnings)
        {
            var lines = new List<string>();
            var bodyOps = new List<AiInstruction>();
            var evidence = new List<SinEvidenceBadge>();
            var blockers = new List<string>(triggerBlockers);
            var snippetIds = new List<string>();
            SinPayloadEligibility? worstPayload = null;

            foreach (SinAction action in node.Actions)
            {
                (string snippetId, List<AiInstruction> instrs, SinPayloadEligibility? elig) =
                    LowerLinearAction(action, evidence, blockers, warnings);
                snippetIds.Add(snippetId);
                worstPayload = Worse(worstPayload, elig);
                bodyOps.AddRange(instrs);
                foreach (AiInstruction i in instrs) lines.Add(SinInstructionRender.Line(i));
            }

            return new SinPlannedStep
            {
                TriggerLabel = triggerLabel,
                WorkerResolution = workerResolution,
                Lowering = AiSnippetKind.Linear,
                SnippetId = string.Join(" + ", snippetIds),
                BranchShape = node.Actions.Count > 1
                    ? $"linear (Rebuild insert) — combo of {node.Actions.Count} triplets, in order, no new branch"
                    : "linear (Rebuild insert) — no new branch",
                StackShape = "each statement pushes its args then CALLPOPA consumes them (net stack delta 0 per triplet)",
                PlannedInstructions = lines,
                Evidence = evidence,
                StepBlockers = blockers.Distinct().ToList(),
                PayloadEligibility = worstPayload,
                BodyOps = bodyOps,   // raw ops for the Gate-3 validator (no synthetic placeholders here)
            };
        }

        static SinPlannedStep PlanGuarded(SinChainNode node, string triggerLabel, string workerResolution,
            IReadOnlyList<string> triggerBlockers, List<string> warnings)
        {
            var lines = new List<string>();
            var evidence = new List<SinEvidenceBadge>();
            var blockers = new List<string>(triggerBlockers);

            if (node.Condition is not SinCondition.HpBelowPercent hp)
            {
                // No other guarded condition is part of the pilots; refuse to fabricate a shape.
                blockers.Add("guarded condition has no proven snippet form (only HpBelowPercent is a pilot)");
                return new SinPlannedStep
                {
                    TriggerLabel = triggerLabel, WorkerResolution = workerResolution,
                    Lowering = AiSnippetKind.GuardedAction, SnippetId = "(none)",
                    BranchShape = "unresolved — no proven guard form", StackShape = "n/a",
                    PlannedInstructions = Array.Empty<string>(), Evidence = evidence,
                    StepBlockers = blockers, PayloadEligibility = null,
                };
            }

            var perform = node.Actions.OfType<SinAction.PerformCommand>().FirstOrDefault();
            ushort commandOperand = perform?.CommandOperand ?? 0;

            int percent = Math.Clamp(hp.Percent, 1, 100);
            string snippetId;
            List<AiInstruction> guard;
            if (percent == 50)
            {
                // Keep the original, separately proven 50% predicate unchanged.
                evidence.Add(new SinEvidenceBadge("field shape", SinBadgeSeverity.Proof,
                    "NearDeath field 0x0119 returns true below 50%; RT2-proved on Skoll m014"));
                evidence.Add(new SinEvidenceBadge("pattern source", SinBadgeSeverity.Proof,
                    "Skoll m014 Low HP Rush binary diff + in-game RT2"));
                AiSnippet snippet = AiSnippetLibrary.ById("guard-hp-below-pct-force-cmd")
                    ?? throw new InvalidOperationException("expected NearDeath snippet missing.");
                (guard, _) = snippet.ExpandGuarded(new AiSnippetArgs(commandOperand, 0, 0));
                snippetId = snippet.Id;
            }
            else
            {
                // Pure instruction construction only; this does not apply a plan
                // or change sandbox/runtime eligibility for the recipe.
                guard = AiAutomation.BuildHpBelowPercentGuard((ushort)percent);
                snippetId = "numeric-hp-percent";
                evidence.Add(new SinEvidenceBadge("numeric HP fields", SinBadgeSeverity.Proof,
                    "readChrProperty 0/2 = HP/maxHP; native accessors 79ADE0/79AE00; integer guard verified offline"));
                warnings.Add("Configured HP percentage uses the numeric HP guard; runtime validation remains required.");
            }

            var act = new List<AiInstruction>();
            var actionSnippets = new List<string>();
            SinPayloadEligibility? payload = null;
            foreach (SinAction action in node.Actions)
            {
                (string actionSnippet, List<AiInstruction> actionOps, SinPayloadEligibility? eligibility) =
                    LowerLinearAction(action, evidence, blockers, warnings);
                actionSnippets.Add(actionSnippet);
                act.AddRange(actionOps);
                payload = Worse(payload, eligibility);
            }

            lines.Add($"; guard (leaves 1 bool): HP% < {percent}");
            lines.AddRange(guard.Select(SinInstructionRender.Line));
            lines.Add("D7      POPXNCJMP  → rejoin            ; skip the action when the guard is false (jump slot resolved at apply-time)");
            lines.Add($"; action ({node.Actions.Count} action(s), when guard true):");
            lines.AddRange(act.Select(SinInstructionRender.Line));
            lines.Add("B0      JMP        → original-entry    ; rejoin: run the original handler afterwards (jump slot resolved at apply-time)");

            blockers.Add("guarded block grows the worker jump-table by 2 slots (grow-aware splice) — needs the real worker");

            return new SinPlannedStep
            {
                TriggerLabel = triggerLabel,
                WorkerResolution = workerResolution,
                Lowering = AiSnippetKind.GuardedAction,
                SnippetId = $"{snippetId} + {string.Join(" + ", actionSnippets)}",
                BranchShape = "guard · D7 POPXNCJMP→rejoin · action · B0 JMP→original  (AppendGuardedAction; +2 jump-table slots)",
                StackShape = "guard leaves exactly 1 bool (consumed by D7); action pushes target+command then CALLPOPA 705A (net 0)",
                PlannedInstructions = lines,
                Evidence = evidence,
                StepBlockers = blockers.Distinct().ToList(),
                PayloadEligibility = payload,
                GuardOps = guard,   // raw guard ops (leaves 1 bool for D7) for the Gate-3 validator
                BodyOps = act,      // raw action ops (net 0) — the synthetic D7/B0 stay text-only (jump slots resolve at apply-time)
            };
        }

        // Map a single linear action to its proven snippet + expanded instructions (in memory).
        static (string snippetId, List<AiInstruction> instrs, SinPayloadEligibility? payload) LowerLinearAction(
            SinAction action, List<SinEvidenceBadge> evidence, List<string> blockers, List<string> warnings)
        {
            switch (action)
            {
                case SinAction.GrantChrProperty g:
                {
                    if (g.Target != AiSnippetLibrary.SelfRef)
                    {
                        blockers.Add($"GrantChrProperty target 0x{g.Target:X4} has no proven snippet (only Self 0xFFF3)");
                        warnings.Add("non-Self GrantChrProperty target is frontier (RT2-pending)");
                    }
                    AddTargetEvidence(evidence, blockers, g.Target);
                    string? fieldName = AiChrPropertyNames.Get(g.FieldId);
                    evidence.Add(new SinEvidenceBadge("field shape", SinBadgeSeverity.Corpus,
                        $"chr field 0x{g.FieldId:X2}{(fieldName != null ? $" ({fieldName})" : "")} via writeChrProperty 0x7018 — corpus-observed; status EFFECT in-game = RT2"));
                    evidence.Add(new SinEvidenceBadge("pattern source", SinBadgeSeverity.Corpus,
                        "ISARU parser-corpus (Turn-1 self-buff, e.g. Th'uban opener / Biran Mighty Guard) — read-only"));
                    AiSnippet sn = Snippet("grant-field-self");
                    List<AiInstruction> instrs = sn.ExpandLinear(new AiSnippetArgs(0, g.FieldId, g.Value));
                    return (sn.Id, instrs, null); // status grant has no command payload
                }
                case SinAction.PerformCommand c:
                {
                    SinPayloadEligibility elig = ClassifyPayload(c.CommandOperand);
                    AddPayloadEvidence(evidence, blockers, c.CommandOperand, elig);
                    AddTargetEvidence(evidence, blockers, c.Target);
                    evidence.Add(new SinEvidenceBadge("pattern source", SinBadgeSeverity.Corpus,
                        "ISARU parser-corpus (force/perform command idiom) — read-only, NOT RT2"));
                    string id = c.Force ? "force-cmd-self" : "perform-cmd-self";
                    AiSnippet sn = Snippet(id);
                    List<AiInstruction> instrs = sn.ExpandLinear(new AiSnippetArgs(c.CommandOperand, 0, 0));
                    return (sn.Id, instrs, elig);
                }
                case SinAction.PerformCommandOnRandomFrontlineChr c:
                {
                    SinPayloadEligibility elig = ClassifyPayload(c.CommandOperand);
                    AddPayloadEvidence(evidence, blockers, c.CommandOperand, elig);
                    evidence.Add(new SinEvidenceBadge("target", SinBadgeSeverity.Proof,
                        "findMatchingChr(FrontlineChars, isAlive, 0, Any) — byte-proved on Skoll m014"));
                    evidence.Add(new SinEvidenceBadge("pattern source", SinBadgeSeverity.Proof,
                        "Skoll m014 Low HP Rush binary diff + in-game RT2"));
                    ushort call = c.Force ? (ushort)0x705A : (ushort)0x700B;
                    var instrs = new List<AiInstruction>
                    {
                        Op(0xAE, 0xFFF2), Op(0xAE, 0x0004), Op(0xAE, 0), Op(0xAE, 0),
                        Op(0xB5, 0x7010), Op(0xAE, c.CommandOperand), Op(0xD8, call),
                    };
                    return ("find-alive-frontline+force-cmd", instrs, elig);
                }
                default:
                    blockers.Add($"action {action.GetType().Name} has no proven linear snippet");
                    return ("(none)", new List<AiInstruction>(), null);
            }
        }

        // ---- evidence helpers (honest classification only — never invents "proved") ----

        /// <summary>Classify a command payload for SIN authoring. NEVER returns Proved: even an operand whose
        /// raw RT2 swap was observed (e.g. Firaga) is only a Candidate INSIDE a SIN-authored block — that block's
        /// in-game effect has not been RT2-proved. Item operands (GATTA, cat 0x2000) and non-AI categories are
        /// Blocked (IsKnownAiPerformOperandCategory == false).</summary>
        public static SinPayloadEligibility ClassifyPayload(ushort operand)
        {
            if ((operand & AiCommandId.CatMask) == (ushort)AiCommandMetadataCategory.Item)
                return SinPayloadEligibility.Blocked; // item-space (GATTA) stays blocked for generic SIN
            if (AiCommandMetadataCatalog.TryGet(operand, out AiCommandMetadataEntry? entry))
                return entry.IsKnownAiPerformOperandCategory ? SinPayloadEligibility.Candidate : SinPayloadEligibility.Blocked;
            if (AiCommandId.IsCommandOperand(operand))
                return SinPayloadEligibility.Candidate; // cat 3/4/6 operand (LUZU advisory) but absent from metadata
            return SinPayloadEligibility.Blocked;
        }

        static void AddPayloadEvidence(List<SinEvidenceBadge> evidence, List<string> blockers, ushort operand, SinPayloadEligibility elig)
        {
            AiCommandDecode dec = AiCommandId.Decode(operand);
            string name = dec.IsKnown ? dec.Name : $"0x{operand:X4}";
            (SinBadgeSeverity sev, string detail) = elig switch
            {
                SinPayloadEligibility.Candidate => (SinBadgeSeverity.Caution,
                    $"command 0x{operand:X4} ({name}) — AI-perform category proved; payload 0x705A/0x700B free (LUZU advisory), but this command's effect = RT2-pending (candidate, not proved)"),
                SinPayloadEligibility.Blocked => (SinBadgeSeverity.Blocked,
                    $"command 0x{operand:X4} ({name}) — BLOCKED (item/metadata-only or non-AI category; not a free AI payload)"),
                _ => (SinBadgeSeverity.Proof, $"command 0x{operand:X4} ({name})"),
            };
            evidence.Add(new SinEvidenceBadge("command eligibility", sev, detail));
            if (elig == SinPayloadEligibility.Blocked)
                blockers.Add($"payload 0x{operand:X4} is blocked-non-AI-payload — cannot be applied");
        }

        static void AddTargetEvidence(List<SinEvidenceBadge> evidence, List<string> blockers, ushort target)
        {
            string? name = AiTargetNames.Get(target);
            if (target == AiSnippetLibrary.SelfRef)
                evidence.Add(new SinEvidenceBadge("target", SinBadgeSeverity.Proof,
                    "target Self (0xFFF3) — byte/RT2-proved sentinel"));
            else
            {
                evidence.Add(new SinEvidenceBadge("target", SinBadgeSeverity.Caution,
                    $"target 0x{target:X4}{(name != null ? $" ({name})" : "")} — corpus-consistent but RT2-pending (only Self is byte-proved)"));
                blockers.Add($"target 0x{target:X4} is RT2-pending (only Self 0xFFF3 is byte-proved)");
            }
        }

        static SinPayloadEligibility? Worse(SinPayloadEligibility? a, SinPayloadEligibility? b)
        {
            if (a is null) return b;
            if (b is null) return a;
            // Blocked is worst, then Candidate, then Proved.
            return (SinPayloadEligibility)Math.Max((int)a.Value, (int)b.Value);
        }

        static AiSnippet Snippet(string id) => AiSnippetLibrary.ById(id)
            ?? throw new InvalidOperationException($"expected proven snippet '{id}' missing from AiSnippetLibrary.");

        static AiInstruction Op(byte opcode, ushort operand) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        // ---- trigger resolution (only OnTurn/CombatHandler has a proven resolver) ----

        static (string label, string resolution, IReadOnlyList<string> blockers) ResolveTrigger(SinTrigger trigger)
        {
            if (trigger.HasProvenResolver)
                return ("OnTurn · CombatHandler (inferred)",
                    "would resolve to (workerIndex, entrypointIndex) via AiWorkerMapping.TryResolveCombatOnTurn — no monster loaded in dry-run",
                    Array.Empty<string>());

            return ($"{trigger.Event} · (no proven resolver)",
                $"event {trigger.Event} has no proven worker resolver — frontier (needs-RT2)",
                new[] { $"trigger {trigger.Event} is frontier — only OnTurn/CombatHandler resolves today" });
        }

        // ---- honest, fixed blockers + the rung ladder still to climb (none satisfied at Gate 2) ----

        static readonly IReadOnlyList<string> GlobalBlockers = new[]
        {
            "no emitter yet — dry-run only; no bytes are assembled or spliced",
            "no AiScriptLab validation yet — RT0/structural standalone gate not run for this plan",
            "no AEON diff yet — AiScript_Diff envelope not computed (no target monster)",
            "no RT2 yet — in-game behaviour unconfirmed (DINPUT8 probe)",
            "no backup writer yet — no .prev.bak path for any target",
            "no monster loaded — worker index + jump-table slots resolve only at apply-time",
        };

        static readonly IReadOnlyList<SinRequiredGate> RungLadder = new[]
        {
            new SinRequiredGate("offline-emittable",
                "Dry-run emitter assembles bytes that re-parse (Rebuild/AppendGuardedAction against a loaded monster)", false),
            new SinRequiredGate("AiScriptLab-clean",
                "Standalone RT0/structural gate passes (--aiasm-rt0 / AiScriptLab --ai2/--ai3)", false),
            new SinRequiredGate("AEON-reviewed",
                "AiScript_Diff shows only the intended block — no drift", false),
            new SinRequiredGate("backup-ready",
                "Writable .prev.bak of the target monster_*.bin exists", false),
            new SinRequiredGate("RT2-proved",
                "Behaviour confirmed on-screen via the DINPUT8 probe, for the cited cutout only", false),
            new SinRequiredGate("template-public",
                "Corpus-normalized + RT2 → public button (still badged honestly)", false),
        };
    }

    /// <summary>Renders a planned AiInstruction as a single preview line (no offset — these are synthetic
    /// templates, not placed in a real file). Annotates command operands with the decoded command name.</summary>
    internal static class SinInstructionRender
    {
        public static string Line(AiInstruction i)
        {
            string raw = Convert.ToHexString(i.Emit());
            string mnem = AiScript_File.Mnemonic(i.Opcode);
            string gloss = AiScript_File.OperandGloss(i);
            if (i.Opcode == 0xAE && AiCommandId.IsCommandOperand(i.Operand))
            {
                AiCommandDecode dec = AiCommandId.Decode(i.Operand);
                gloss += $"  (cmd: {(dec.IsKnown ? dec.Name : $"0x{i.Operand:X4}")})";
            }
            return $"{raw,-6}  {mnem,-10} {gloss}".TrimEnd();
        }
    }
}
