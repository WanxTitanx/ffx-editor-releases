using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// Calm Lands UNI cards: two skill payloads and dedicated VFX exist for each card.
    /// WithRecipe attaches the shared post-action writer, explicit targets and signature restrictions.
    /// </summary>
    internal static class CalmLandsSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            Draft(
                "UNI-015",
                "Coro Arcano / Arcane Chorus",
                threat: 3,
                primitives: "NON-ELEMENTAL-MAGIC + SINGLE-TARGET + PARTY-MAGIC-PRESSURE",
                summary: "T3 universal arcane pressure without elemental affinity or an ally buff.",
                preview: "Skill 1 — Pulso Arcano / Arcane Pulse: moderate non-elemental magic damage to one random party member.\nSkill 2 — Onda Arcana / Arcane Wave: low non-elemental magic damage to the whole party.",
                nextGate: "Keep both as distinct commands and allow the preset across the Calm Lands roster without elemental or monster-family restrictions."),
            Draft(
                "UNI-016",
                "Marcha da Matilha / Pack March",
                threat: 3,
                primitives: "PHYSICAL-HIT + ARMOR-BREAK-HIT + FOCUSED-HUNT",
                summary: "T3 physical pack pressure; the original monster action is preserved before two added attacks.",
                preview: "Skill 1 — Mordida de Cerco / Flanking Bite: moderate physical damage to one random party member.\nSkill 2 — Rasga-Guarda / Guardbreaker: a second physical hit against that target that lowers its defense.",
                nextGate: "Keep the defense reduction on the second hit and restrict assignment to physical attackers or pack hunters."),
            Draft(
                "UNI-017",
                "Formação de Guarda / Guard Formation",
                threat: 3,
                primitives: "VEIL + RANDOM-ALLY + PROTECT + SHELL + DAMAGE + DELAY",
                summary: "T3 defensive pair: protect one allied monster, then delay one party member.",
                preview: "Skill 1 — Véu / Veil: grants Protect and Shell to one randomly selected allied monster.\nSkill 2 — Golpe de Arrasto / Dragging Strike: physical damage to one random party member and inflicts Delay.",
                nextGate: "Veil selects one random allied monster, including the caster when it is in the valid target set; preserve the native action before both skills."),
            Draft(
                "UNI-018",
                "Golpe Cego / Blindside",
                threat: 4,
                primitives: "PHYSICAL-DAMAGE + NON-ELEMENTAL-DAMAGE + DARKNESS",
                summary: "T4 two-hit pressure with no setup buff; the second hit adds Darkness.",
                preview: "Skill 1 — Estocada Sombria / Shadow Thrust: non-elemental physical damage to one random party member.\nSkill 2 — Golpe Cego / Blindside: a second physical hit that can inflict Darkness.",
                nextGate: "Keep Darkness on the second skill only and use a single target for the status-bearing hit."),
            Draft(
                "UNI-019",
                "Canto Mudo / Silent Canticle",
                threat: 4,
                primitives: "NON-ELEMENTAL-MAGIC + MAGIC-DAMAGE + SILENCE",
                summary: "T4 arcane control through two separate magic skills, without an ally buff.",
                preview: "Skill 1 — Voz Arcana / Arcane Voice: non-elemental magic damage to one random party member.\nSkill 2 — Silêncio Cortante / Cutting Silence: a second non-elemental magic hit that can inflict Silence.",
                nextGate: "Keep the damage and Silence commands separate; only the second skill applies Silence to its enemy target."),
            Draft(
                "UNI-020",
                "Ruptura do Ritmo / Rhythm Break",
                threat: 4,
                primitives: "SLOW + FOLLOW-UP-PHYSICAL-DAMAGE + SAME-TARGET",
                summary: "T4 tempo control followed by a physical punish; no setup buff.",
                preview: "Skill 1 — Lentidão / Slow: inflicts Slow on one random party member.\nSkill 2 — Impacto de Ritmo / Rhythm Impact: physical damage to the same target.",
                nextGate: "Use the target selected by Lentidão for the follow-up; do not replace the monster's native action."),
            Draft(
                "UNI-021",
                "Anacondaur — Cauda e Pedra / Tail and Stone",
                threat: 4,
                primitives: "MINI-BOSS-SIGNATURE + PARTY-PHYSICAL-DAMAGE + PETRIFY",
                summary: "T4 signature UNI restricted to the Anacondaur mini-boss.",
                preview: "Skill 1 — Cauda Sísmica / Quaking Tail: physical damage to the whole party.\nSkill 2 — Olhar Pétreo / Petrifying Gaze: attempts to Petrify one random party member.",
                nextGate: "Bind only to the selected Calm Lands Anacondaur mini-boss entry; keep the status attempt on one target."),
            Draft(
                "UNI-022",
                "Ogre — Punho do Colosso / Colossus Fist",
                threat: 4,
                primitives: "MINI-BOSS-SIGNATURE + HEAVY-PHYSICAL-HIT + PARTY-PHYSICAL-HIT",
                summary: "T4 signature UNI restricted to the Ogre mini-boss.",
                preview: "Skill 1 — Punho Demolidor / Demolishing Fist: heavy physical damage to one random party member.\nSkill 2 — Abalo do Colosso / Colossus Quake: lower physical damage to the whole party.",
                nextGate: "Bind only to the selected Calm Lands Ogre mini-boss entry; keep the second hit below the single-target strike in potency."),
            Draft(
                "UNI-023",
                "Malboro — Hálito Pestilento / Pestilent Breath",
                threat: 4,
                primitives: "MINI-BOSS-SIGNATURE + POISON + INDEPENDENT-STATUS-ROLLS",
                summary: "T4 status signature UNI restricted to the Malboro mini-boss.",
                preview: "Skill 1 — Névoa Tóxica / Toxic Mist: non-elemental magic damage and Poison to the whole party.\nSkill 2 — Hálito Impuro / Foul Breath: independent 50% rolls for Darkness, Silence and Slow on one random party member; zero to three may land before resistance.",
                nextGate: "Bind only to the selected Calm Lands Malboro mini-boss entry; keep the three independent status rolls in one Foul Breath command."),
        }.Select(SinPostActionPresetCatalog.WithRecipe).ToArray();

        static AiSinPresetEntry Draft(
            string id,
            string name,
            int threat,
            string primitives,
            string summary,
            string preview,
            string nextGate) => new()
            {
                Id = id,
                Name = name,
                Scope = AiSinPresetScope.Universal,
                Tier = AiSinPresetTier.B,
                Maturity = "design",
                Threat = threat,
                CatalogOrder = 0,
                Primitives = primitives,
                Summary = summary,
                Preview = preview,
                Risk = "Two skill payloads and dedicated VFX exist in monmagic2. AI must preserve the native action, choose targets and fire both skills; no AI bytes are emitted.",
                NextGate = nextGate,
            };
    }
}
