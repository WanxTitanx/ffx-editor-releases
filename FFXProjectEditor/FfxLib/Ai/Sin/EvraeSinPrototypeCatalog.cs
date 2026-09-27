using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>Editor-only Boss draft. Evrae remains outside the UNI pool and mod boss CSV.</summary>
    internal static class EvraeSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            new AiSinPresetEntry
            {
                Id = "BOSS-EVRAE-001",
                Name = "Evrae — Ciclo de Distância",
                Scope = AiSinPresetScope.Boss,
                Tier = AiSinPresetTier.C,
                Maturity = "design",
                Threat = 5,
                CatalogOrder = 100,
                Primitives = "DISTANCE + TELEGRAPH + POISON-BREATH + PHOTON-SPRAY + STONE-GAZE + SWOOPING-SCYTHE",
                Summary = "Threat T5: ciclo de distância com telegraph, Poison Breath, petrificação e perseguição.",
                Preview = "Perto: golpes físicos/Stone Gaze conforme o contador; Inhale telegrapha Poison Breath. Longe: Photon Spray multi-hit. A 1/3 HP: Haste; se longe, Swooping Scythe fecha distância e pode punir petrificação.",
                Risk = "Boss-only e editor-only. São ataques existentes do Evrae; ordem, contador, limiar de HP e leitura de distância precisam ser confirmados no m119 do alvo.",
                NextGate = "Partir do ISARU Evrae Distance Dive (BattleDistance 0x001E / isOnFrontline), verificar contador 6 e fase de 1/3 HP, e desenhar as transições sem emitir bytes.",
            },
        };
    }
}
