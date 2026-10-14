using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    // AI behavior library — reusable, parameterized snippets that expand to ATEL instructions and feed the
    // AI Assembler (Rebuild for linear; AppendGuardedAction for conditional). Every byte sequence MIRRORS a
    // proven corpus idiom (see docs/reverse/FFX_AI_BYTECODE_OPCODE_TABLE_PROVEN_2026-06-04.md):
    //
    //   setStatField (0x70AB):     PUSHII <field>  PUSHII/PUSHF <value>  CALLPOPA 70AB        (m014, dozens of times)
    //   writeChrProperty (0x7018): PUSHII <chr>    PUSHII <field>  PUSHII <value>  CALLPOPA 7018  (m014 @0x265)
    //   performCommand (0x700B):   PUSHII/PUSHV <target>  PUSHII <commandId>  CALLPOPA 700B     (m014 @0x2A1)
    //   forcePerformCommand(705A): PUSHII <target>  PUSHII <commandId>  CALLPOPA 705A           (m100)
    //   RNG 1-in-K gate:           CALL GetRandomValue(00A9)  PUSHII K  MOD  PUSHII 0  EQ        (m014 @0x2A7)
    //
    // commandId = (category<<12)|id (cat 0x3000 character/black-magic, 0x4000 monster/aeon) — see AiCommandId.
    // The owner/self target sentinel 0xFFF3 (-13 as int16) is the proven self reference (m100, m163).
    //
    // SCOPE / HONESTY: LINEAR snippets are straight-line (no new branch) and insert via the RT0-proven Rebuild.
    // GUARDED snippets add a real branch, so the editor expands them through AiScript_File.AppendGuardedAction
    // (which grows the worker's jump-table). Their STRUCTURE is proven offline (the AiFile re-parses, the walk
    // closes, nothing dangles); their in-game BEHAVIOUR (stack balance, target semantics of a given command) must
    // be confirmed via the DINPUT8 probe (RT2) before a specific template is treated as production. Snippets that
    // need a per-monster anchor we have NOT byte-confirmed (current-HP field id, a turn counter) are deliberately
    // NOT shipped here — they are documented as pending in docs/ai/FFX_AI_ASSEMBLER_PRODUCTIZED_2026-06-05.md.

    public enum AiSnippetKind { Linear, GuardedAction }

    /// <summary>Parameters an expanding snippet may consume. CommandOperand = a full (cat&lt;&lt;12)|id command id
    /// (from AiCommandId); Value/Value2 = generic numeric args (field id, status value, func id, modulus...).</summary>
    public readonly record struct AiSnippetArgs(ushort CommandOperand, ushort Value, ushort Value2);

    public sealed class AiSnippet
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public required string Description { get; init; }
        public AiSnippetKind Kind { get; init; }
        public bool UsesCommand { get; init; }
        public bool UsesValue { get; init; }
        public bool UsesValue2 { get; init; }
        public string ValueLabel { get; init; } = "value";
        public string Value2Label { get; init; } = "value 2";
        public AiCommandCategory? PreferCategory { get; init; }

        /// <summary>Linear expansion: a straight-line instruction list (Offset = -1; inserted via Rebuild).</summary>
        public Func<AiSnippetArgs, List<AiInstruction>>? Linear { get; init; }
        /// <summary>Guarded expansion: (guard leaving ONE bool, action) for AiScript_File.AppendGuardedAction.</summary>
        public Func<AiSnippetArgs, (List<AiInstruction> guard, List<AiInstruction> action)>? Guarded { get; init; }

        public List<AiInstruction> ExpandLinear(AiSnippetArgs a) =>
            Linear?.Invoke(a) ?? throw new InvalidOperationException($"snippet '{Id}' is not linear.");
        public (List<AiInstruction> guard, List<AiInstruction> action) ExpandGuarded(AiSnippetArgs a) =>
            Guarded?.Invoke(a) ?? throw new InvalidOperationException($"snippet '{Id}' is not guarded.");
    }

    public static class AiSnippetLibrary
    {
        /// <summary>Proven self/owner target sentinel (-13 as int16). Used as the default chr arg.</summary>
        public const ushort SelfRef = 0xFFF3;
        /// <summary>Proven LastAttacker target sentinel (-17 as int16). The monstro que bateu por último.</summary>
        public const ushort LastAttackerRef = 0xFFEF;

        // opcodes
        const byte PUSHII = 0xAE, CALLPOPA = 0xD8, CALL = 0xB5, MOD = 0x18, EQ = 0x06, LT = 0x0B, DIV = 0x17, MUL = 0x16;
        // chr-property fields (IDA + corpus + FFXDataParser proven): 0x00=HP (stat_hp), 0x02=maxHP (stat_maxhp).
        const ushort FieldNearDeath = 0x0119;
        // calls
        const ushort WriteChrProperty = 0x7018, ReadChrProperty = 0x700F, SetStatField = 0x70AB, PerformCommand = 0x700B,
                     ForcePerformCommand = 0x705A, GetRandomValue = 0x00A9, IsCounterattackAllowed = 0x70E0;

        static AiInstruction Op(byte opcode, ushort operand = 0) => new AiInstruction
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
        };

        static List<AiInstruction> List(params AiInstruction[] items) => items.ToList();

        static readonly List<AiSnippet> _all = Build();
        public static IReadOnlyList<AiSnippet> All => _all;
        public static AiSnippet? ById(string id) => _all.FirstOrDefault(s => s.Id == id);

        static List<AiSnippet> Build() => new()
        {
            // ---- LINEAR (straight-line; insert via Rebuild) ----
            new AiSnippet
            {
                Id = "force-cmd-self", Name = "Force command (owner ref)", Category = "Action",
                Description = "PUSHII 0xFFF3 (self/owner) · PUSHII <command> · CALLPOPA 705A. Forces the action chosen by global id — the monster doesn't need to 'have' it. Default target = self ref (command target semantics: confirm via RT2).",
                Kind = AiSnippetKind.Linear, UsesCommand = true, PreferCategory = AiCommandCategory.Monster,
                Linear = a => List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, ForcePerformCommand)),
            },
            new AiSnippet
            {
                Id = "perform-cmd-self", Name = "Perform command (owner ref)", Category = "Action",
                Description = "PUSHII 0xFFF3 (self/owner) · PUSHII <command> · CALLPOPA 700B. Same as 'force', but via normal performCommand (enters the action queue).",
                Kind = AiSnippetKind.Linear, UsesCommand = true, PreferCategory = AiCommandCategory.Character,
                Linear = a => List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, PerformCommand)),
            },
            new AiSnippet
            {
                Id = "grant-field-self", Name = "Grant chr field = value (self)", Category = "Status/Stat",
                Description = "PUSHII 0xFFF3 (self) · PUSHII <field> · PUSHII <value> · CALLPOPA 7018 (writeChrProperty). Ex.: field 0x38 StatusHaste, 0x31 StatusProtect, 0x32 StatusReflect; value 1 = ligar.",
                Kind = AiSnippetKind.Linear, UsesValue = true, UsesValue2 = true,
                ValueLabel = "field id (hex)", Value2Label = "value",
                Linear = a => List(Op(PUSHII, SelfRef), Op(PUSHII, a.Value), Op(PUSHII, a.Value2), Op(CALLPOPA, WriteChrProperty)),
            },
            new AiSnippet
            {
                Id = "set-stat-field", Name = "Set stat field = value", Category = "Status/Stat",
                Description = "PUSHII <field> · PUSHII <value> · CALLPOPA 70AB (setStatField). Stat/motion tuning (e.g.: field 0xDA stat_round). No target (applies to own descriptor).",
                Kind = AiSnippetKind.Linear, UsesValue = true, UsesValue2 = true,
                ValueLabel = "field id (hex)", Value2Label = "value",
                Linear = a => List(Op(PUSHII, a.Value), Op(PUSHII, a.Value2), Op(CALLPOPA, SetStatField)),
            },
            new AiSnippet
            {
                Id = "raw-void-call", Name = "Raw void CALL (no args)", Category = "Advanced",
                Description = "CALLPOPA <funcid>. Escape hatch: calls any native void by func-id (high nibble = namespace). No args on stack — use only with calls that don't require arguments.",
                Kind = AiSnippetKind.Linear, UsesValue = true, ValueLabel = "func-id (hex)",
                Linear = a => List(Op(CALLPOPA, a.Value)),
            },

            // ---- GUARDED (adds a branch; expands via AppendGuardedAction -> grows the worker jump-table) ----
            new AiSnippet
            {
                Id = "guard-rng-force-cmd", Name = "RNG 1-in-K: force command", Category = "Conditional",
                Description = "guard: CALL GetRandomValue · PUSHII <K> · MOD · PUSHII 0 · EQ  (≈1/K chance)  →  action: force <command>. Same idiom as m014 (alternate action). Hook into an entrypoint (e.g.: onTurn) of the combat worker.",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, UsesValue = true,
                ValueLabel = "K (1-in-K, e.g. 2)", PreferCategory = AiCommandCategory.Monster,
                Guarded = a =>
                {
                    ushort k = a.Value == 0 ? (ushort)2 : a.Value;
                    var guard = List(Op(CALL, GetRandomValue), Op(PUSHII, k), Op(MOD), Op(PUSHII, 0), Op(EQ));
                    var action = List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, ForcePerformCommand));
                    return (guard, action);
                },
            },
            new AiSnippet
            {
                Id = "guard-chrprop-lt-force-cmd", Name = "If chr property (self) BELOW N: force command", Category = "Conditional",
                Description = "guard: PUSHII self · PUSHII <field> · CALL readChrProperty(700F) · PUSHII <N> · LT  (true when the property is < N)  →  action: force <command>. The corpus' #1 condition idiom (readChrProperty feeds 980 branches). E.g.: enrage when a field drops below a threshold. ⚠️ The HP field-id is NOT byte-proven here — supply a field you know (proven status fields: 0x38 Haste, 0x31 Protect, 0x32 Reflect). Structure proven offline; readChrProperty's arity + in-game effect = RT2 (confirm via the probe).",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, UsesValue = true, UsesValue2 = true,
                ValueLabel = "field id (hex)", Value2Label = "limiar N (abaixo disso dispara)", PreferCategory = AiCommandCategory.Monster,
                Guarded = a =>
                {
                    var guard = List(Op(PUSHII, SelfRef), Op(PUSHII, a.Value), Op(CALL, ReadChrProperty), Op(PUSHII, a.Value2), Op(LT));
                    var action = List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, ForcePerformCommand));
                    return (guard, action);
                },
            },
            new AiSnippet
            {
                Id = "guard-hp-below-pct-force-cmd", Name = "If HP below 50%: force command (NearDeath)", Category = "Conditional",
                Description = "guard: readChrProperty(self, NearDeath=0x0119) → returns 1 if HP < 50%, 0 otherwise. Binary-confirmed: case 281 of FFX_Battle_AggregateActorProperty (0x7B2DD0) checks currentHp < maxHp/2. This native predicate has a fixed 50% threshold. Configurable percentage guards use numeric HP/maxHP instead.  →  action: force <command>.",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, UsesValue = false,
                PreferCategory = AiCommandCategory.Monster,
                Guarded = a =>
                {
                    var guard = List(
                        Op(PUSHII, SelfRef), Op(PUSHII, FieldNearDeath), Op(CALL, ReadChrProperty));
                    var action = List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, ForcePerformCommand));
                    return (guard, action);
                },
            },
            new AiSnippet
            {
                Id = "guard-always-force-cmd", Name = "On event: force command (counter/phase)", Category = "Conditional",
                Description = "guard: PUSHII 1 (always) → action: force <command>. Hook into the event entrypoint (e.g.: onHit = idx 3 of the combat worker) to 'counterattack'/act whenever the event fires. The original handler continues running afterward.",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, PreferCategory = AiCommandCategory.Monster,
                Guarded = a =>
                {
                    var guard = List(Op(PUSHII, 1));
                    var action = List(Op(PUSHII, SelfRef), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, ForcePerformCommand));
                    return (guard, action);
                },
            },
            new AiSnippet
            {
                Id = "guard-always-counter-cmd", Name = "Counter: perform command on target (always)", Category = "Conditional",
                Description = "guard: PUSHII 1 (always) → action: PUSHII <target> · PUSHII <command> · CALLPOPA performCommand (700B). Counterattacks whenever the entrypoint fires, respecting the CTB queue. Target via Value2 (default LastAttacker 0xFFEF). Hook into entrypoint 3 (onHit). Idiom proven on Skoll m014 (SIN Counter March).",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, UsesValue2 = true,
                Value2Label = "target sentinel (0xFFEF=LastAttacker, 0xFFF2=FrontlineChars, 0xFFF3=Self)",
                PreferCategory = AiCommandCategory.Character,
                Guarded = a =>
                {
                    ushort target = a.Value2 == 0 ? LastAttackerRef : a.Value2;
                    var guard = List(Op(PUSHII, 1));
                    var action = List(Op(PUSHII, target), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, PerformCommand));
                    return (guard, action);
                },
            },
            new AiSnippet
            {
                Id = "guard-counterattack-cmd", Name = "CounterAttack: perform command (canônico)", Category = "Conditional",
                Description = "guard: CALL isCounterattackAllowed (70E0) → action: PUSHII <target> · PUSHII <command> · CALLPOPA performCommand (700B). Canonical game gate (all 9 vanilla counter monsters use this). Target via Value2 (default LastAttacker 0xFFEF). Hook into entrypoint 3 (onHit).",
                Kind = AiSnippetKind.GuardedAction, UsesCommand = true, UsesValue2 = true,
                Value2Label = "target sentinel (0xFFEF=LastAttacker, 0xFFF2=FrontlineChars, 0xFFF3=Self)",
                PreferCategory = AiCommandCategory.Character,
                Guarded = a =>
                {
                    ushort target = a.Value2 == 0 ? LastAttackerRef : a.Value2;
                    var guard = List(Op(CALL, IsCounterattackAllowed));
                    var action = List(Op(PUSHII, target), Op(PUSHII, a.CommandOperand), Op(CALLPOPA, PerformCommand));
                    return (guard, action);
                },
            },
        };
    }
}
