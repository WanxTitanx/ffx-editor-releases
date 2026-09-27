using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// Sunken Cave / Cavern skill payloads and dedicated VFX exist. Each UNI still
    /// needs AI to add one skill after the native action; no mini-boss signature pool.
    /// </summary>
    internal static class StolenFaythSinPrototypeCatalog
    {
        public static IReadOnlyList<AiSinPresetEntry> All { get; } = new[]
        {
            Draft(
                "UNI-024",
                "Véu dos Perdidos / Veil of the Lost",
                primitives: "AREA-T4 + PROTECT + SHELL + REGEN + ALL-MONSTERS",
                summary: "T4 defensive anchor: one skill makes the entire monster group much harder to remove.",
                preview: "One skill — Bênção Sepulcral / Sepulchral Blessing: grants Protect, Shell, and Regen to all allied monsters.",
                risk: "Area-specific design draft. This is one compound skill, not a triple-cast sequence.",
                nextGate: "Assign only through the Sunken Cave / Cavern of the Stolen Fayth area pool; no mini-boss-specific binding."),
            Draft(
                "UNI-025",
                "Renovação Sepulcral / Sepulchral Renewal",
                primitives: "AREA-T4 + MASS-HEAL + STATUS-CLEANSE + ALL-MONSTERS",
                summary: "T4 recovery skill that can reset a dangerous monster group in one action.",
                preview: "One skill — Restauração dos Mortos / Wraith Restoration: restores a large amount of HP to all allied monsters and removes Poison, Silence, and Slow.",
                risk: "Area-specific design draft. Strong recovery is concentrated into one skill; it adds no follow-up command.",
                nextGate: "Keep the recovery and cleanse in this single skill; do not attach a mini-boss-only chance."),
            Draft(
                "UNI-026",
                "Fúria dos Fayth / Faythbound Fury",
                primitives: "AREA-T4 + HASTE + CHEER + FOCUS + ALL-MONSTERS",
                summary: "T4 offensive support skill that turns the whole group into a lethal threat.",
                preview: "One skill — Ascensão Espectral / Wraith Ascension: grants Haste, Cheer, and Focus to all allied monsters.",
                risk: "Area-specific design draft. The group buff is intentionally concentrated in one action and raises the danger of every surviving monster.",
                nextGate: "Keep as one support command; the area roster should control how often this pool assignment appears."),
            Draft(
                "UNI-027",
                "Pulso do Túmulo / Grave Pulse",
                primitives: "AREA-T4 + NON-ELEMENTAL-MAGIC + HIGH-POWER + ALL-PARTY",
                summary: "T4 lethal offense: one powerful universal non-elemental magic attack.",
                preview: "One skill — Ruptura do Além / Netherburst: high non-elemental magic damage to the whole party.",
                risk: "Area-specific design draft. This is the primary raw-damage option and does not depend on monster element or target weakness.",
                nextGate: "Keep its potency high but non-elemental; assign from the Cave pool rather than by mini-boss identity."),
            Draft(
                "UNI-028",
                "Miasma da Ruína / Ruinous Miasma",
                primitives: "AREA-T4 + POISON + SILENCE + DARKNESS + ALL-PARTY",
                summary: "T4 control skill that disables several party roles in one cast.",
                preview: "One skill — Hálito de Desespero / Despairing Breath: inflicts Poison, Silence, and Darkness on the whole party.",
                risk: "Area-specific design draft. The severity comes from several status effects in one skill, not repeated casts.",
                nextGate: "Keep this a single area skill; status resistances remain the intended counterplay."),
            Draft(
                "UNI-029",
                "Peso dos Fayth / Weight of the Fayth",
                primitives: "AREA-T4 + SLOW + DELAY + ALL-PARTY",
                summary: "T4 CTB-control skill that can severely restrict the party's next turns.",
                preview: "One skill — Grilhões Gravitacionais / Gravitic Shackles: inflicts Slow and Delay on the whole party.",
                risk: "Area-specific design draft. Both control effects resolve through one skill; no extra attack follows it.",
                nextGate: "Keep the Slow-plus-Delay package as one command and do not bind it to a specific mini-boss."),
            Draft(
                "UNI-030",
                "Devorador de Almas / Soul Devourer",
                primitives: "AREA-T4 + SINGLE-TARGET + NON-ELEMENTAL-DAMAGE + HP-DRAIN",
                summary: "T4 finisher that threatens one character while sustaining the monster that uses it.",
                preview: "One skill — Dreno de Alma / Soul Drain: heavy non-elemental magic damage to one party member and restores HP to the monster based on damage dealt.",
                risk: "Area-specific design draft. High single-target damage and healing are combined into one action.",
                nextGate: "Keep it available across the area pool; do not create an exclusive version for a mini-boss."),
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
                Threat = 4,
                CatalogOrder = 0,
                Primitives = primitives,
                Summary = $"Sunken Cave / Cavern of the Stolen Fayth — {summary}",
                Preview = preview,
                Risk = $"One skill payload and dedicated VFX exist in monmagic2. {risk} AI assignment is still pending; mini-boss Overdrives remain separate, and no AI bytes are emitted.",
                NextGate = nextGate,
            };
    }
}
