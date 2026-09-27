using System.Collections.Generic;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // Friendly names for AI TARGET sentinels — the literal pushed before a performCommand id (#8). These are the
    // FFX `btlActor` enum (cross-referenced from the public FFX ATEL RE, Karifean/FFXDataParser ScriptConstants —
    // same provenance as the func-name + chr-property tables). Self (0xFFF3) is independently byte/RT2-proven; the
    // rest are corpus-consistent (offensive spells target Character#1-3/Self, monster heals target AllMonsters; see
    // docs/reverse/FFX_AI_TARGET_ENCODING_AND_CONDITION_GETTERS_2026-06-07.md).
    //
    // "enemy/ally" is from the MONSTER's point of view: a monster's enemies are the player party (FrontlineChars /
    // Character#N), its allies are the other monsters (AllMonsters). Labels keep the canonical name in parens.
    public static class AiTargetNames
    {
        /// <summary>The standard, useful target sentinels offered in the "🎯 Mudar alvo" dropdown, in a sensible
        /// order. Operand -> friendly label.</summary>
        public static readonly IReadOnlyList<(ushort Operand, string Label)> Standard = new (ushort, string)[]
        {
            (0xFFF3, Strings.F2_self_proven_d0fb6a1b),
            (0xFFF2, Strings.F2_enemies_all_characters_frontlinechars_e90a2e98),
            (0xFFFA, Strings.U_Ai_TargetEnemyChar1),
            (0xFFF9, Strings.U_Ai_TargetEnemyChar2),
            (0xFFF8, Strings.U_Ai_TargetEnemyChar3),
            (0xFFF1, Strings.F2_allies_all_aeons_allaeons_02f7ed3b),
(0xFFEC, Strings.U_Ai_TargetSingleActor),
(0xFFE9, Strings.U_Ai_TargetNonAeons),
            (0xFFEF, Strings.U_Ai_TargetLastAttacker),
            (0xFFFD, Strings.U_Ai_TargetCurrentAction),
            (0xFFFC, Strings.U_Ai_TargetImmediate),
            (0xFFFB, Strings.F2_all_actors_both_sides_allactors_79e0b35e),
            (0xFFFE, Strings.F2_lab_active_actor_owner_activeactors_rt2__675675eb),
            (0xFFF0, Strings.U_Ai_TargetPredefinedGroup),
            (0xFFFF, Strings.F2_none_clear_null_6de9d263),
        };

        // Full btlActor name map (for labelling a target value seen in a script, incl. non-standard ones).
        static readonly Dictionary<ushort, string> _names = new()
        {
            [0xFFE9] = "NonAeonActors", [0xFFEB] = "ActiveNonAeonNonOD", [0xFFEC] = "SingleActor",
            [0xFFEF] = "LastAttacker", [0xFFF0] = "PredefinedGroup", [0xFFF1] = "AllAeons",
            [0xFFF2] = "FrontlineChars", [0xFFF3] = "Self", [0xFFF4] = "CharReserve#4",
            [0xFFF5] = "CharReserve#3", [0xFFF6] = "CharReserve#2", [0xFFF7] = "CharReserve#1",
            [0xFFF8] = "Character#3", [0xFFF9] = "Character#2", [0xFFFA] = "Character#1",
            [0xFFFB] = "AllActors", [0xFFFC] = "TargetActorsNow", [0xFFFD] = "TargetActors",
            [0xFFFE] = "ActiveActors", [0xFFFF] = "Null", [0x00FF] = "None",
        };

        /// <summary>Canonical btlActor name for a target sentinel, or null. For an actor-id ref (0x1000+n) returns
        /// "MonsterType=NNNN"; for a small positive (0x13+) "Monster#n".</summary>
        public static string? Get(ushort operand)
        {
            if (_names.TryGetValue(operand, out string? n)) return n;
            if (operand >= 0x1000 && operand < 0x1FFF) return $"MonsterType={(operand - 0x1000):X4}";
            if (operand >= 0x0013 && operand < 0x00FF) return $"Monster#{operand - 0x0013}";
            return null;
        }
    }
}
