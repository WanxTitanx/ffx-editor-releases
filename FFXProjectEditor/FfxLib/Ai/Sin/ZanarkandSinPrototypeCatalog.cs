using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>Editor-only Zanarkand UNI drafts; the guard-pair entry has no skill command.</summary>
    internal static class ZanarkandSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            new AiSinPresetEntry
            {
                Id = "UNI-040",
                Name = "Toque dos Caídos / Fallen Touch",
                Scope = AiSinPresetScope.Universal,
                Tier = AiSinPresetTier.C,
                Maturity = "design",
                Threat = 6,
                CatalogOrder = 0,
                Primitives = "ZANARKAND + m198 + m200 + ATEL-ON-DAMAGE + ZOMBIE + NO-SKILL",
                Summary = "T6 Zanarkand guard-pair UNI: no skill command; the ATEL side effect adds Zombie after their damaging hits.",
                Preview = "Sem skill. Efeito ATEL pós-dano: quando m198 Fallen Monk ou m200 Fallen Monk (Flamethrower) causar dano, aplica Zombie ao personagem atingido.",
                Risk = "O writer usa o Forbidden Rite existente, com comparação de HP antes/depois da ação nativa para selecionar os personagens atingidos.",
                NextGate = "Restrita a m198 e m200. Validar em batalha a sequência nova, incluindo ataques que erram e dano em área.",
            },
        }.Select(SinPostActionPresetCatalog.WithRecipe).ToArray();
    }
}
