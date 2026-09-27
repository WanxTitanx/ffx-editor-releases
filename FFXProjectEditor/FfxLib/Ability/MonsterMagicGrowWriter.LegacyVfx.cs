using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ability
{
    public static partial class MonsterMagicGrowWriter
    {
        public sealed record LegacyMonsterVfxSpec(int Row, string Name, short Anim1Id, short Anim2Id);

        // Flan Flood #249 retains its RT2-proven 718/719 pair. These are the
        // subsequent monster skills through Frost Claw; gameplay is unchanged.
        public static IReadOnlyList<LegacyMonsterVfxSpec> LegacyMonsterVfxSkills { get; } = Array.AsReadOnly(new[]
        {
            new LegacyMonsterVfxSpec(250, "Gore Charge", 844, 844),
            new LegacyMonsterVfxSpec(251, "Fang Strike", 845, 845),
            new LegacyMonsterVfxSpec(252, "Snipe", 846, 847),
            new LegacyMonsterVfxSpec(253, "Feather Storm", 848, 848),
            new LegacyMonsterVfxSpec(254, "Ultra Blizzara", 849, 0),
            new LegacyMonsterVfxSpec(255, "Venom Sting", 850, 850),
            new LegacyMonsterVfxSpec(256, "Chaos Spark", 851, 0),
            new LegacyMonsterVfxSpec(257, "Maggot Burst", 852, 0),
            new LegacyMonsterVfxSpec(258, "Evil Gaze", 853, 0),
            new LegacyMonsterVfxSpec(259, "Soul Drain", 854, 854),
            new LegacyMonsterVfxSpec(260, "Thunder Charge", 855, 855),
            new LegacyMonsterVfxSpec(261, "Wild Flurry", 856, 856),
            new LegacyMonsterVfxSpec(262, "Permafrost", 857, 857),
            new LegacyMonsterVfxSpec(263, "Mana Storm", 858, 859),
            new LegacyMonsterVfxSpec(264, "Mana Storm", 860, 861),
            new LegacyMonsterVfxSpec(265, "Mana Storm", 862, 863),
            new LegacyMonsterVfxSpec(266, "Frost Claw", 864, 865),
        });

        static void ApplyLegacyMonsterVfxIfConfigured(Ability_Command command, int row)
        {
            if (row is < 250 or > 266) return;
            LegacyMonsterVfxSpec spec = LegacyMonsterVfxSkills[row - 250];
            if (!string.Equals(DecodeUs(command.NameScriptBytes), spec.Name, StringComparison.Ordinal))
                return; // An off-order authoring experiment must not acquire another skill's VFX.
            command.Anim1Id = spec.Anim1Id;
            command.Anim2Id = spec.Anim2Id;
        }
    }
}
