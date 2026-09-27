using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    /// <summary>
    /// Maps in-game sphere cost (Power/Mana/Speed/Ability/Key locks) to
    /// <c>panel.bin</c> <see cref="SphereGridNodeTypeEntry.NodeEffectBitfield"/> (+0x10)
    /// and lock rows via <see cref="SphereGridNodeTypeEntry.AppearanceType"/> (+0x16).
    /// </summary>
    public enum SphereGridNodeSphereRequirementKind
    {
        Custom = -1,
        AbilitySphere = 0,
        PowerSphere = 1,
        ManaSphere = 2,
        SpeedSphere = 3,
        LockLevel1 = 10,
        LockLevel2 = 11,
        LockLevel3 = 12,
        LockLevel4 = 13,
    }

    public readonly struct SphereGridNodeSphereRequirementApplyResult
    {
        public ushort NodeEffectBitfield { get; init; }
        public ushort AppearanceType { get; init; }
        public bool ClearLearnedMove { get; init; }
        public bool ClearIncreaseAmount { get; init; }
    }

    public static class SphereGridNodeSphereRequirement
    {
        public const ushort AbilityEffectBitfield = 0x0400;
        public const ushort AbilityAppearanceType = 0x000F;
        public const ushort StatAppearanceType = 0x000C;

        sealed record Preset(
            SphereGridNodeSphereRequirementKind Kind,
            ushort Effect,
            ushort Appearance,
            string Label,
            bool ClearsLearnedMove);

        static readonly Preset[] Presets =
        [
            new(SphereGridNodeSphereRequirementKind.AbilitySphere, AbilityEffectBitfield, AbilityAppearanceType,
                "Ability Sphere · skill teach (0400h)", false),
            new(SphereGridNodeSphereRequirementKind.PowerSphere, 0x0001, StatAppearanceType,
                "Power Sphere · STR+ (0001h)", false),
            new(SphereGridNodeSphereRequirementKind.ManaSphere, 0x0004, StatAppearanceType,
                "Mana Sphere · MAG+ (0004h)", false),
            new(SphereGridNodeSphereRequirementKind.SpeedSphere, 0x0010, StatAppearanceType,
                "Speed Sphere · AGI+ (0010h)", false),
            new(SphereGridNodeSphereRequirementKind.LockLevel1, 0, 0x0012,
                "Key Sphere Lv1 · lock row (Appr 12h)", true),
            new(SphereGridNodeSphereRequirementKind.LockLevel2, 0, 0x0011,
                "Key Sphere Lv2 · lock row (Appr 11h)", true),
            new(SphereGridNodeSphereRequirementKind.LockLevel3, 0, 0x0000,
                "Key Sphere Lv3 · lock row (Appr 00h)", true),
            new(SphereGridNodeSphereRequirementKind.LockLevel4, 0, 0x0010,
                "Key Sphere Lv4 · lock row (Appr 10h)", true),
        ];

        public static IReadOnlyList<(int Value, string Label)> GetDropdownOptions()
        {
            List<(int, string)> options = Presets
                .Select(preset => ((int)preset.Kind, preset.Label))
                .ToList();
            options.Add(((int)SphereGridNodeSphereRequirementKind.Custom, "Custom · keep current hex"));
            return options;
        }

        public static SphereGridNodeSphereRequirementKind DetectKind(ushort effect, ushort appearance)
        {
            foreach (Preset preset in Presets)
            {
                if (preset.Kind >= SphereGridNodeSphereRequirementKind.LockLevel1)
                {
                    if (appearance == preset.Appearance && effect == preset.Effect)
                        return preset.Kind;
                    continue;
                }

                if (effect == preset.Effect)
                    return preset.Kind;
            }

            return SphereGridNodeSphereRequirementKind.Custom;
        }

        public static string FormatShortLabel(ushort effect, ushort appearance)
        {
            SphereGridNodeSphereRequirementKind kind = DetectKind(effect, appearance);
            if (kind == SphereGridNodeSphereRequirementKind.Custom)
                return $"Custom {effect:X4}/{appearance:X4}";

            Preset preset = Presets.First(row => row.Kind == kind);
            int separator = preset.Label.IndexOf('·');
            return separator > 0 ? preset.Label[..separator].Trim() : preset.Label;
        }

        public static bool TryGetApplyResult(
            SphereGridNodeSphereRequirementKind kind,
            out SphereGridNodeSphereRequirementApplyResult result)
        {
            result = default;
            if (kind == SphereGridNodeSphereRequirementKind.Custom)
                return false;

            Preset? preset = Presets.FirstOrDefault(row => row.Kind == kind);
            if (preset == null)
                return false;

            result = new SphereGridNodeSphereRequirementApplyResult
            {
                NodeEffectBitfield = preset.Effect,
                AppearanceType = preset.Appearance,
                ClearLearnedMove = preset.ClearsLearnedMove,
                ClearIncreaseAmount = preset.ClearsLearnedMove,
            };
            return true;
        }
    }
}
