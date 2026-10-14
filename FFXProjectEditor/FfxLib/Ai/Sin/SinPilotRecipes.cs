using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // The three mandatory pilot recipes for Gate 2 (preview/dry-run). Each maps to a real read-only catalog entry
    // (AiSinPresetCatalog, cosmetic DisplayName) + an ISARU corpus pattern + a proven AiSnippetLibrary form. They
    // are authoring INTENT only; planning them produces a preview-only SinApplyPlan. None is a writer.
    //
    // Authoring keys are numeric: command operands via AiCommandId.Encode*, status fields by hex, target = Self
    // sentinel 0xFFF3. DisplayName is pulled from the catalog when present (DONNA: cosmetic, never a join key).

    public static class SinPilotRecipes
    {
        // Status duration fields (AiChrPropertyNames): Shell 0x30, Protect 0x31, Haste 0x38.
        const ushort FieldShell = 0x0030, FieldProtect = 0x0031, FieldHaste = 0x0038;
        // A plain, dictionary-verified monster ability used as the forced-command payload (MonsterMagic1 "Attack").
        // Classified as a CANDIDATE payload — illustrative, never promoted to proved.
        static readonly ushort CmdMonsterAttack = AiCommandId.EncodeMonster(0x000);

        public static IReadOnlyList<SinChainRecipe> All => new[]
        {
            TurnOneSelfBuff(),
            ForcePerformCommand(),
            HpGuardedAction(),
        };

        /// <summary>Pilot 1 — Turn-1 self-buff/status: on the first turn, grant Haste/Protect/Shell on self.
        /// SIN-006 "Véu de Bevelle" (ISARU-T1-01). Lowers Linear: 3× grant-field-self via Rebuild.</summary>
        public static SinChainRecipe TurnOneSelfBuff() => new()
        {
            Id = "SIN-006",
            DisplayName = Display("SIN-006", Strings.U_Ai_SinVeilOfBevelle),
            Tier = AiSinPresetTier.A,
            Threat = 3,
            Intent = "On the opening turn, grant Haste + Protect + Shell on self (status opener).",
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = "corpus: m282 Th'uban opener / m134 Biran Mighty Guard",
                    Actions = new SinAction[]
                    {
                        new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldHaste, 255),
                        new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldProtect, 255),
                        new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldShell, 255),
                    },
                },
            },
        };

        /// <summary>Pilot 2 — Force/perform selected monster command: whenever the event fires, force the picked
        /// command. SIN-009 "Mandato do Maester" (ISARU-T1-06). Lowers Linear: force-cmd-self (0x705A).</summary>
        public static SinChainRecipe ForcePerformCommand() => new()
        {
            Id = "SIN-009",
            DisplayName = Display("SIN-009", "Mandato do Maester"),
            Tier = AiSinPresetTier.A,
            Threat = 4,
            Intent = "On the event, force the selected monster command on self (payload 0x705A; command id is candidate).",
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = "forcePerformCommand 0x705A observed in 28 monsters (m124 Seymour, m132 BFA, m276 Nemesis)",
                    Actions = new SinAction[]
                    {
                        new SinAction.PerformCommand(CmdMonsterAttack, AiSnippetLibrary.SelfRef, Force: true),
                    },
                },
            },
        };

        /// <summary>Pilot 3 — HP &lt; X% guarded action: when HP drops below 50%, force the picked command (enrage).
        /// SIN-010 "Farplane Toll" (ISARU-T3-01). Lowers Guarded: guard-hp-below-pct-force-cmd.</summary>
        public static SinChainRecipe HpGuardedAction() => new()
        {
            Id = "SIN-010",
            DisplayName = Display("SIN-010", "Farplane Toll"),
            Tier = AiSinPresetTier.A,
            Threat = 6,
            Intent = "When HP < 50%, force the selected command (enrage). Guard form proved offline; effect = RT2.",
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = new SinCondition.HpBelowPercent(50),
                    Note = "Th'uban buff + HP-gate → Attack; readChrProperty(HP/maxHP) guard corpus-proved",
                    Actions = new SinAction[]
                    {
                        new SinAction.PerformCommand(CmdMonsterAttack, AiSnippetLibrary.SelfRef, Force: true),
                    },
                },
            },
        };

        // Cosmetic DisplayName from the read-only catalog when the id is present; else the spec fallback.
        static string Display(string id, string fallback) =>
            AiSinPresetCatalog.All.FirstOrDefault(e => e.Id == id)?.Name ?? fallback;
    }
}
