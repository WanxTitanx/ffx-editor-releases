using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 2 (preview/dry-run) IR.
    //
    // This is the CONCRETE, minimal subset of the Gate-1 design IR
    // (docs/ai/SIN_YEVON_CHAIN_IR_SPEC_2026-06-10.md §3) that the three pilot recipes actually need. It is
    // authoring-INTENT only: a Recipe says WHAT the user wants, never HOW bytes get written. Lowering to the
    // proven snippet templates + planning happens in SinDryRunPlanner, and even that NEVER writes — it produces a
    // read-only SinApplyPlan (preview-only).
    //
    // HONESTY (binding, mirrors the spec §6):
    //   - The authoring key is ALWAYS numeric (command operand / field id / target sentinel). DisplayName is
    //     cosmetic (DONNA: never a join key).
    //   - A Recipe can DECLARE nothing higher than PreviewOnly; rungs only rise from a gate that actually ran.
    //   - "Clean for DESIGN is not unlocked for WRITING." SIN stays BLOCKED for writing.

    /// <summary>The worker event/entrypoint a node hooks. Only OnTurn/CombatHandler has a proven resolver today
    /// (AiWorkerMapping.TryResolveCombatOnTurn); the rest are frontier (needs-RT2) and the planner flags them.</summary>
    public enum SinEvent { Init, Main, OnTurn, OnHit, OnDeath, OnTargeted }

    /// <summary>The trigger: which worker event the node engages. Tier reuses AiSinPresetTier (A/B/C/Lab).</summary>
    public sealed record SinTrigger(SinEvent Event)
    {
        /// <summary>Only OnTurn resolves to (workerIndex, entrypointIndex) via the proven CombatHandler resolver.</summary>
        public bool HasProvenResolver => Event == SinEvent.OnTurn;
    }

    /// <summary>The boolean guard — leaves EXACTLY one bool on the stack. Only the corpus-proven forms are
    /// modelled (Always, HpBelowPercent). Other conditions stay frontier and are not part of any pilot yet.</summary>
    public abstract record SinCondition
    {
        public sealed record Always : SinCondition;
        /// <summary>HP% &lt; Percent. Guard form is corpus-proven (HP=0x00 read 306×, maxHP=0x02 137×; MUL/LT).</summary>
        public sealed record HpBelowPercent(int Percent) : SinCondition;

        public static readonly SinCondition AlwaysInstance = new Always();
    }

    /// <summary>The effect a node performs. Only the three primitives that have a proven, free, no-item emitter
    /// path are modelled (PerformCommand via 0x700B/0x705A, GrantChrProperty via 0x7018). Item operands (GATTA),
    /// motion (0x70A8) and facing (0x7032) are deliberately NOT representable here.</summary>
    public abstract record SinAction
    {
        /// <summary>Force (0x705A) or perform (0x700B) a command. CommandOperand = (cat&lt;&lt;12)|id (numeric authoring key).</summary>
        public sealed record PerformCommand(ushort CommandOperand, ushort Target, bool Force) : SinAction;

        public sealed record PerformCommandOnRandomFrontlineChr(ushort CommandOperand, bool Force) : SinAction;

        /// <summary>Grant a chr-property/status on an explicit actor: writeChrProperty (0x7018) [chr][field][value].</summary>
        public sealed record GrantChrProperty(ushort Target, ushort FieldId, ushort Value) : SinAction;
    }

    /// <summary>One node of the chain: when (Trigger) + if (Condition) + do (Actions, a combo of 1..N). A combo of
    /// &gt;1 action lowers to a sequence of linear triplets; a non-Always condition lowers to a guarded block.</summary>
    public sealed record SinChainNode
    {
        public required SinTrigger Trigger { get; init; }
        public required SinCondition Condition { get; init; }
        public required IReadOnlyList<SinAction> Actions { get; init; } // combo, >= 1
        public string? Note { get; init; }                              // cosmetic author annotation
    }

    /// <summary>An authorable SIN recipe: intent + ordered nodes + cosmetic metadata. Mirrors AiSinPresetEntry
    /// (the read-only catalog) but is shaped to be PLANNED into a SinApplyPlan. It is still preview-only.</summary>
    public sealed record SinChainRecipe
    {
        public required string Id { get; init; }            // catalog id (SIN-006) or authoring GUID
        public required string DisplayName { get; init; }   // COSMETIC (DONNA) — never an authoring key
        public required AiSinPresetTier Tier { get; init; }
        public required int Threat { get; init; }           // 1..10
        public required IReadOnlyList<SinChainNode> Nodes { get; init; }
        public string? Intent { get; init; }                // one-line author intent (cosmetic)
        /// <summary>Entrypoint override: 0=init, 1=main, 2=onTurn, 3=onHit, etc. null = auto (onTurn/CombatHandler).</summary>
        public int? DesiredEntrypoint { get; init; }
    }
}
