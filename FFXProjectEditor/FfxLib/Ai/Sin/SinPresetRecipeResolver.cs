using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // Maps catalog preset ids → SinChainRecipe for offline mod bake. Pilots are exact; others are honest synths
    // from catalog primitives (BUFF / CMD / HP%) until the Chain Builder ships full recipes.
    public static class SinPresetRecipeResolver
    {
        const ushort FieldShell = 0x0030, FieldProtect = 0x0031, FieldHaste = 0x0038;
        const ushort FieldRegen = 0x0037, FieldNulFrost = 0x0036;
        const ushort FieldNulBlaze = 0x0034, FieldNulShock = 0x0035;
        const ushort FrontlineChars = 0xFFF2;
        const ushort AllMonsters = 0xFFF1;
        static readonly ushort CmdMonsterAttack = AiCommandId.EncodeMonster(0x000);
        static readonly ushort CmdBlizzara = AiCommandId.EncodeChar(0x046);
        static readonly ushort CmdWatera = AiCommandId.EncodeChar(0x048);
        static readonly ushort CmdCure = AiCommandId.EncodeChar(43);
        static readonly ushort CmdWhiteWind = AiCommandId.EncodeMonster(90);
        static readonly ushort CmdHaste = AiCommandId.EncodeChar(0x036);
        static readonly ushort CmdSlow = AiCommandId.EncodeChar(0x038);

        static readonly ushort CmdFrostFloodWeave = AiCommandId.EncodeMonster2(268);
        static readonly ushort CmdShorelineBreak = AiCommandId.EncodeMonster2(269);
        static readonly ushort CmdSinSalve = AiCommandId.EncodeMonster2(270);
        static readonly ushort CmdMistChorus = AiCommandId.EncodeMonster2(271);
        static readonly ushort CmdCounterMarch = AiCommandId.EncodeMonster2(272);

        public sealed record ResolveResult(SinChainRecipe? Recipe, string Source, string? BlockReason)
        {
            public bool Ok => Recipe is not null;
        }

        public static ResolveResult Resolve(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
                return new(null, "", "empty preset id");

            AiSinPresetEntry? entry = AiSinPresetCatalog.Find(presetId);
            string pilotId = entry?.ResolvePilotId() ?? presetId;

            SinChainRecipe? pilot = SinPilotRecipes.All.FirstOrDefault(r =>
                r.Id.Equals(pilotId, StringComparison.OrdinalIgnoreCase));
            if (pilot is not null)
            {
                if (entry is not null && !pilot.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                    return new(CloneWithCatalogMeta(pilot, entry), "pilot", null);
                return new(pilot, "pilot", null);
            }

            if (entry is null)
                return new(null, "", $"unknown preset '{presetId}'");

            if (!entry.IsBakeReady)
                return new(null, "", $"preset '{entry.Id}' is {entry.Maturity} — offline bake not wired yet");

            string prim = entry.Primitives;
            if (prim.Contains("COPYAI", StringComparison.OrdinalIgnoreCase))
                return new(null, "", "COPYAI requires full script swap — not bakeable yet");
            if (prim.Contains("RIBBON-BYPASS", StringComparison.OrdinalIgnoreCase)
                || prim.Contains("DIRECT-STATUS", StringComparison.OrdinalIgnoreCase))
                return new(null, "", "C-frontier status bypass — RT2 lab only");

            if (IsWardStack(entry))
                return new(WardStack(entry), "synth-ward-stack", null);

            if (entry.Id.Equals("UNI-003", StringComparison.OrdinalIgnoreCase))
                return new(HasteEnrage(entry), "synth-uni-003", null);

            if (entry.Id.Equals("UNI-002", StringComparison.OrdinalIgnoreCase))
                return new(CounterAttack(entry), "synth-uni-002", null);

            if (entry.Id.Equals("UNI-005", StringComparison.OrdinalIgnoreCase))
                return new(FrostFloodWeave(entry), "synth-uni-005", null);
            if (entry.Id.Equals("UNI-006", StringComparison.OrdinalIgnoreCase))
                return new(ShorelineBreak(entry), "synth-uni-006", null);
            if (entry.Id.Equals("UNI-007", StringComparison.OrdinalIgnoreCase))
                return new(SinSalve(entry), "synth-uni-007", null);
            if (entry.Id.Equals("UNI-008", StringComparison.OrdinalIgnoreCase))
                return new(MistChorus(entry), "synth-uni-008", null);

            if (prim.Contains("HP%", StringComparison.OrdinalIgnoreCase) && prim.Contains("CMD", StringComparison.OrdinalIgnoreCase))
                return new(HpGuardedSynth(entry), "synth-hp-cmd", null);

            if (prim.Contains("BUFF", StringComparison.OrdinalIgnoreCase)
                && !prim.Contains("HP%", StringComparison.OrdinalIgnoreCase))
                return new(SelfBuffSynth(entry), "synth-buff", null);

            if (prim.Contains("CMD", StringComparison.OrdinalIgnoreCase)
                && !prim.Contains("MC", StringComparison.OrdinalIgnoreCase)
                && !prim.Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                return new(ForceCmdSynth(entry), "synth-cmd", null);

            return new(null, "", $"primitives '{prim}' not mapped for offline bake");
        }

        static bool IsWardStack(AiSinPresetEntry entry) =>
            entry.Id.Equals("UNI-004", StringComparison.OrdinalIgnoreCase)
            || entry.LegacyProtoId?.Equals("SIN-016", StringComparison.OrdinalIgnoreCase) == true;

        static SinChainRecipe CloneWithCatalogMeta(SinChainRecipe pilot, AiSinPresetEntry entry) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = pilot.Nodes,
        };

        static SinChainRecipe FrostFloodWeave(AiSinPresetEntry entry) => CmdNode(
            entry,
            "Macalania dual cast: Blizzara then Watera on the front row",
            new SinAction.PerformCommand(CmdFrostFloodWeave, FrontlineChars, Force: true));

        static SinChainRecipe ShorelineBreak(AiSinPresetEntry entry) => CmdNode(
            entry,
            "Watera forced on FrontlineChars (whole front row)",
            new SinAction.PerformCommand(CmdShorelineBreak, FrontlineChars, Force: true));

        static SinChainRecipe SinSalve(AiSinPresetEntry entry) => GuardedCmdNode(
            entry,
            hpBelow: 50,
            "Cure on self when HP < 50% (NearDeath threshold)",
            new SinAction.PerformCommand(CmdSinSalve, AiSnippetLibrary.SelfRef, Force: true));

        static SinChainRecipe MistChorus(AiSinPresetEntry entry) => GuardedCmdNode(
            entry,
            hpBelow: 50,
            "White Wind on all allied monsters when hurt",
            new SinAction.PerformCommand(CmdMistChorus, AllMonsters, Force: true));

        static SinChainRecipe CmdNode(AiSinPresetEntry entry, string note, params SinAction[] actions) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = note,
                    Actions = actions,
                },
            },
        };

        static SinChainRecipe GuardedCmdNode(AiSinPresetEntry entry, int hpBelow, string note, params SinAction[] actions) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = new SinCondition.HpBelowPercent(hpBelow),
                    Note = note,
                    Actions = actions,
                },
            },
        };

        static SinChainRecipe HasteEnrage(AiSinPresetEntry entry) => GuardedCmdNode(
            entry,
            hpBelow: 50,
            "Haste self, then Slow one random living frontline character when HP drops below 50%",
            new SinAction.PerformCommand(CmdHaste, AiSnippetLibrary.SelfRef, Force: true),
            new SinAction.PerformCommandOnRandomFrontlineChr(CmdSlow, Force: true));

        // UNI-002 CounterAttack: revida com ataque físico ao ser atingido (entrypoint 3 = onHit).
        static SinChainRecipe CounterAttack(AiSinPresetEntry entry)
        {
            var recipe = CmdNode(
                entry,
                Strings.F2_counterattack_counter_with_counter_march_ff28f0f0,
                new SinAction.PerformCommand(CmdCounterMarch, AiSnippetLibrary.SelfRef, Force: true));
            return recipe with { DesiredEntrypoint = 3 };
        }

        static readonly ushort CmdWardStack = AiCommandId.EncodeMonster2(0x10B);
        static SinChainRecipe WardStack(AiSinPresetEntry entry) => CmdNode(
            entry,
            "Shell+Regen+NulBlaze+NulShock via skill 0x610B",
            new SinAction.PerformCommand(CmdWardStack, AiSnippetLibrary.SelfRef, Force: true));

        static SinChainRecipe SelfBuffSynth(AiSinPresetEntry entry)
        {
            var actions = new List<SinAction>();
            if (entry.Summary.Contains("Haste", StringComparison.OrdinalIgnoreCase)
                || entry.Preview.Contains("Haste", StringComparison.OrdinalIgnoreCase))
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldHaste, 255));
            if (entry.Summary.Contains("Protect", StringComparison.OrdinalIgnoreCase)
                || entry.Preview.Contains("Protect", StringComparison.OrdinalIgnoreCase))
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldProtect, 255));
            if (entry.Summary.Contains("Shell", StringComparison.OrdinalIgnoreCase)
                || entry.Preview.Contains("Shell", StringComparison.OrdinalIgnoreCase))
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldShell, 255));
            if (entry.Summary.Contains("Regen", StringComparison.OrdinalIgnoreCase)
                || entry.Preview.Contains("Regen", StringComparison.OrdinalIgnoreCase))
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldRegen, 255));

            if (actions.Count == 0)
            {
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldProtect, 255));
                actions.Add(new SinAction.GrantChrProperty(AiSnippetLibrary.SelfRef, FieldShell, 255));
            }

            return SelfBuffRecipe(entry, actions.ToArray());
        }

        static SinChainRecipe SelfBuffRecipe(AiSinPresetEntry entry, params SinAction[] actions) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = "synth opener from catalog BUFF primitives",
                    Actions = actions,
                },
            },
        };

        static SinChainRecipe ForceCmdSynth(AiSinPresetEntry entry) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = SinCondition.AlwaysInstance,
                    Note = "synth force-cmd (candidate Attack payload)",
                    Actions = new SinAction[]
                    {
                        new SinAction.PerformCommand(CmdMonsterAttack, AiSnippetLibrary.SelfRef, Force: true),
                    },
                },
            },
        };

        static SinChainRecipe HpGuardedSynth(AiSinPresetEntry entry) => new()
        {
            Id = entry.Id,
            DisplayName = entry.Name,
            Tier = entry.Tier,
            Threat = entry.Threat,
            Intent = entry.Summary,
            Nodes = new[]
            {
                new SinChainNode
                {
                    Trigger = new SinTrigger(SinEvent.OnTurn),
                    Condition = new SinCondition.HpBelowPercent(50),
                    Note = "synth HP<50% guarded force-cmd",
                    Actions = new SinAction[]
                    {
                        new SinAction.PerformCommand(CmdMonsterAttack, AiSnippetLibrary.SelfRef, Force: true),
                    },
                },
            },
        };
    }
}
