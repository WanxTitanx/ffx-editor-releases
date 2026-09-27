using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// Mt. Gagazet skill payloads and dedicated VFX exist for six T5 one-skill
    /// and three T6 two-skill cards. AI assignment and SIN gates remain pending.
    /// </summary>
    internal static class GagazetSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            Draft(
                "UNI-031",
                "Bastião dos Picos / Peak Bastion",
                threat: 5,
                primitives: "GAGAZET-T5 + ALL-MONSTERS + PROTECT + SHELL + REGEN + CHEER-2 + AIM-2 + FOCUS-2 + REFLEX-2 + LUCK-2",
                summary: "Uma skill T5 de suporte extremo: protege e fortalece todo o grupo de monstros.",
                preview: "Skill única (nome da UNI): concede Protect, Shell e Regen, mais Cheer +2, Aim +2, Focus +2, Reflex +2 e Luck +2 a todos os monstros aliados.",
                nextGate: "Manter os cinco buffs de atributo em duas cargas cada, junto de Protect, Shell e Regen; não adicionar Auto-Life nem outro cast."),
            Draft(
                "UNI-032",
                "Ruptura da Montanha / Mountain Rend",
                threat: 5,
                primitives: "GAGAZET-T5 + SINGLE-TARGET + DEVASTATING-PHYSICAL + ARMOR-BREAK",
                summary: "Finalizador físico T5: um cast pode colocar um personagem da party em perigo imediato.",
                preview: "Skill única (nome da UNI): causa dano físico devastador a um personagem aleatório e reduz sua defesa.",
                nextGate: "Manter como uma única skill física de alta potência contra um alvo, sem comando de acompanhamento."),
            Draft(
                "UNI-033",
                "Miasma de Gagazet / Gagazet Miasma",
                threat: 5,
                primitives: "GAGAZET-T5 + ALL-PARTY + POISON + SILENCE + DARKNESS + SLOW",
                summary: "Controle extremo T5: um único cast afeta vários papéis da party ao mesmo tempo.",
                preview: "Skill única (nome da UNI): aplica Poison, Silence, Darkness e Slow à party inteira.",
                nextGate: "Manter os quatro status nesta única skill, sem outro cast de status em seguida."),
            Draft(
                "UNI-034",
                "Avalanche Sepulcral / Sepulchral Avalanche",
                threat: 5,
                primitives: "GAGAZET-T5 + ALL-PARTY + NON-ELEMENTAL-MAGIC + EXTREME-POWER",
                summary: "Dano bruto T5: magia universal não elemental, sem depender de fraqueza elemental.",
                preview: "Skill única (nome da UNI): causa dano mágico não elemental massivo à party inteira.",
                nextGate: "Manter a skill universal e não elemental; o perigo vem da potência deste único cast."),
            Draft(
                "UNI-035",
                "Corrente Submersa / Undertow",
                threat: 5,
                primitives: "GAGAZET-T5 + AQUATIC-SUBAREA + WATER-DAMAGE + DELAY",
                summary: "Ameaça T5 do trecho aquático, compatível com o grupo de três monstros da subárea, não com uma espécie isolada.",
                preview: "Skill única (nome da UNI): causa dano de água pesado à party inteira e aplica Delay.",
                nextGate: "Manter como opção do grupo aquático; não vincular a um único monstro."),
            Draft(
                "UNI-036",
                "Sino da Condenação / Condemnation Bell",
                threat: 5,
                primitives: "GAGAZET-T5 + SINGLE-TARGET-DAMAGE + DEATH-CHANCE + JYNX-5",
                summary: "Ataque T5 punitivo: dano, chance de Death e cinco cargas de Jynx no mesmo alvo.",
                preview: "Skill única (nome da UNI): causa dano a um alvo, tem chance de aplicar Death e acrescenta 5 cargas de Jynx ao alvo.",
                nextGate: "Resolver dano, tentativa de Death e cinco cargas de Jynx no mesmo cast; a chance exata fica para balanceamento."),
            Draft(
                "UNI-037",
                "Investida do Desfiladeiro / Ravine Onslaught",
                threat: 6,
                primitives: "GAGAZET-T6 + TWO-SKILL-CHAIN + HEAVY-PHYSICAL + ARMOR-BREAK",
                summary: "Par T6 de skills físicas fortes: cada golpe fica abaixo de uma singularidade T5, mas a sequência pressiona muito.",
                preview: "Skill 1 — Corte de Geada / Frostcleave: causa dano físico pesado a um personagem.\nSkill 2 — Quebra de Armadura / Armorbreaker: segundo golpe físico que reduz a defesa do alvo.",
                nextGate: "Preservar a ação nativa do monstro e acrescentar estas duas skills; não vincular o par a uma espécie."),
            Draft(
                "UNI-038",
                "Coro da Avalanche / Avalanche Chorus",
                threat: 6,
                primitives: "GAGAZET-T6 + TWO-SKILL-CHAIN + GROUP-BUFF + NON-ELEMENTAL-MAGIC",
                summary: "Par T6 de preparação e punição: fortalece o grupo de monstros antes de uma magia universal forte.",
                preview: "Skill 1 — Comando dos Ronso / Ronso Warcry: concede Haste, Cheer e Focus a todos os monstros aliados.\nSkill 2 — Pulsação Glacial / Glacial Pulse: causa dano mágico não elemental alto à party inteira.",
                nextGate: "Manter cada skill individualmente abaixo da potência de uma singularidade T5; o perigo vem do turno combinado."),
            Draft(
                "UNI-039",
                "Juramento Sem Égide / Unwarded Oath",
                threat: 6,
                primitives: "GAGAZET-T6 + TWO-SKILL-CHAIN + ATEL-DIRECT-STATUS + BERSERK + RIBBON-BYPASS + PROOF-BYPASS",
                summary: "Ameaça T6 com duas skills e um efeito de status separado em ATEL; Berserk não faz parte do payload de nenhuma delas.",
                preview: "Skill 1 — Voto Profano / Profane Vow: causa dano mágico não elemental forte a um personagem.\nSkill 2 — Corte de Vontade / Willbreaker: golpe físico pesado em um personagem.\nEfeito ATEL ligado à Skill 2 — Selo de Frenesi / Frenzy Seal: tenta aplicar Frenesi / Berserk a um personagem aleatório, com intenção de ignorar Ribbon/Proof; o status vem do script, não do payload da skill.",
                risk: "As duas skills têm payload e VFX próprios, sem Berserk nelas. O writer pós-ação reutiliza o Forbidden Rite existente para aplicar Berserk após Willbreaker.",
                nextGate: "Validar em batalha a nova sequência, a cadência e o alvo do Forbidden Rite."),
        }.Select(SinPostActionPresetCatalog.WithRecipe).ToArray();

        static AiSinPresetEntry Draft(
            string id,
            string name,
            int threat,
            string primitives,
            string summary,
            string preview,
            string nextGate,
            string risk = "Skill payloads and dedicated VFX exist in monmagic2. AI must preserve the native monster action and fire the single T5 skill or two T6 skills. No AI bytes are written, and the UNI is not bound to one monster.") => new()
            {
                Id = id,
                Name = name,
                Scope = AiSinPresetScope.Universal,
                Tier = AiSinPresetTier.C,
                Maturity = "design",
                Threat = threat,
                CatalogOrder = 0,
                Primitives = primitives,
                Summary = $"Monte Gagazet / Mt. Gagazet — {summary}",
                Preview = preview,
                Risk = risk,
                NextGate = nextGate,
            };
    }
}
