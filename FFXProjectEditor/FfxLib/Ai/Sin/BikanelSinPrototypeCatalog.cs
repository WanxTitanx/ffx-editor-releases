using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// Editor-only prototypes recovered from the old Bikanel UNI buttons.
    /// Six skill payloads exist in monmagic2. WithRecipe attaches their post-action authoring contract.
    /// UNI-010 replays the resolved native call once and needs no command row or runtime hook.
    /// </summary>
    internal static class BikanelSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            Draft(
                "UNI-009", "Manto de Areia", "CUSTOM-SKILL + POST-ACTION + PROTECT + EVASION + CHEER",
                "Conjura Protect + Evasion + Cheer depois de outra ação do monstro.",
                "Após cada ação executada no turno: ativar Manto de Areia em Self.",
                "Skill 0x612A criada: Protect 254/6, Cheer + Reflex 1; clone visual 0760. Reaplicar após cada ação reduz o valor de Dispel e dificulta acertar. O gatilho pós-ação ainda não foi ligado.",
                "Provar a ordem pós-ação e a proteção contra reentrada, depois validar efeitos e counterplay em RT2."),
            Draft(
                "UNI-010", "Miragem", "ACTION-REPLAY + POST-ACTION",
                "Duplica a ação do monstro.",
                "Repetir uma vez o comando e o alvo usados pela ação nativa; sem skill visual adicional e sem repetição recursiva.",
                "O writer captura os argumentos da chamada nativa em variáveis privadas próprias e repete somente essa chamada.",
                "Validar a cadência em batalha; o bake percorre somente as chamadas originais e recusa reaplicação."),
            Draft(
                "UNI-011", "Suga", "HP-DRAIN + TARGET(frontline)",
                "Drena HP da linha de frente.",
                "Usar uma ação parecida com Drain contra um alvo vivo da linha de frente.",
                "Skill 0x612B criada com Magic P20 e AbsorbDamage; clones visuais 0761/0762. Recuperação de HP e seleção FrontlineChars ainda precisam de RT2.",
                "Validar o drain efetivo e o alvo vivo da linha de frente antes de ligar a AI."),
            Draft(
                "UNI-012", "Tempestade de Areia", "AOE + DELAY + DARK + SILENCE + JINX",
                "Aplica Delay, Dark, Silence e Jinx em área.",
                "Criar uma nova skill que aplique o pacote de quatro efeitos em área.",
                "Skill 0x612C criada sem dano, com DelayS, Darkness 100/3, Silence 100/3 e Jinx 1 em área; clone visual 0763. A interação de DelayS com NoDamage segue sem prova RT2.",
                "Validar Delay, os três debuffs, alcance e resistências em batalha antes de ligar a AI."),
            Draft(
                "UNI-013A", "Silêncio — Protect aliados", "CUSTOM-SKILL + POSITIVE-PARTY-BUFF",
                "Skill positiva separada: aplica Protect aos aliados.",
                "Criar uma skill própria de Protect para a party aliada.",
                "Skill 0x612D criada: Protect 254/6 em alvo multi aliado; clone visual 0764. A seleção real de AllMonsters segue sem RT2.",
                "Provar alvo e duração do Protect em grupo antes de ligar a AI."),
            Draft(
                "UNI-013B", "Silêncio — Silence party", "CUSTOM-SKILL + NEGATIVE-PARTY-STATUS",
                "Skill negativa separada: aplica Silence à party inimiga.",
                "Criar uma skill própria de Silence para a party inimiga.",
                "Skill 0x612E criada: Silence 100/3 em alvo multi inimigo; clones visuais 0765/0766. Continua separada do Protect de 013A.",
                "Provar alvo e duração de Silence em grupo antes de ligar a AI."),
            Draft(
                "UNI-014", "Estilhaço", "CUSTOM-SKILL + AOE-DAMAGE + SILENCE",
                "Skill nova: dano em área + Silence; removido o dobro contra MACHINA.",
                "Criar uma skill de dano em área que também aplique Silence, sem condição de tipo MACHINA.",
                "Skill 0x612F criada: dano físico P34 em área + Silence 80/3, sem bônus MACHINA; clone visual 0779. Cristais visíveis no preview (12/12 texturas), com dois handlers ainda parciais no viewer.",
                "Validar dano, Silence, alcance e aparência final em batalha antes de ligar a AI."),
        }.Select(SinPostActionPresetCatalog.WithRecipe).ToArray();

        static AiSinPresetEntry Draft(
            string id,
            string name,
            string primitives,
            string summary,
            string preview,
            string risk,
            string nextGate) => new()
        {
            Id = id,
            Name = name,
            Scope = AiSinPresetScope.Universal,
            Tier = AiSinPresetTier.B,
            Maturity = "design",
            Threat = 3, // Provisional Bikanel-area threat; per-recipe tuning is not specified.
            Primitives = primitives,
            Summary = summary,
            Preview = preview,
            Risk = $"T3 é só uma estimativa da área; protótipo editor-only para AI, sem gate de execução e fora de universal.csv. {risk}",
            NextGate = $"{nextGate} Os payloads não promovem o protótipo automaticamente ao catálogo do mod.",
        };
    }
}
