using System;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.FfxLib.Ability
{
    public static partial class MonsterMagicGrowWriter
    {
        public const int GagazetSinFirstCommandId = 329;
        public const int GagazetSinLastCommandId = 340;

        public sealed record GagazetSinSkillSpec(
            int CommandId, string UniId, string Name, string Description,
            int DonorRow, short Anim1Id, short Anim2Id);

        public static IReadOnlyList<GagazetSinSkillSpec> GagazetSinSkills { get; } = Array.AsReadOnly(new[]
        {
            new GagazetSinSkillSpec(329, "UNI-031", "Peak Bastion", "Protect, Shell, Regen and five stat buffs for all allies.", 301, 827, 828),
            new GagazetSinSkillSpec(330, "UNI-032", "Mountain Rend", "Devastating physical damage and Armor Break to one foe.", 303, 829, 829),
            new GagazetSinSkillSpec(331, "UNI-033", "Gagazet Miasma", "Poison, Silence, Darkness and Slow all foes.", 300, 830, 830),
            new GagazetSinSkillSpec(332, "UNI-034", "Sepulchral Avalanche", "Massive non-elemental magic damage to all foes.", 299, 831, 832),
            new GagazetSinSkillSpec(333, "UNI-035", "Undertow", "Heavy Water damage and Delay to all foes.", 299, 833, 834),
            new GagazetSinSkillSpec(334, "UNI-036", "Condemnation Bell", "Magic damage, chance of Death and Jinx x5 to one foe.", 299, 835, 835),
            new GagazetSinSkillSpec(335, "UNI-037", "Frostcleave", "Heavy physical damage to one foe.", 303, 836, 836),
            new GagazetSinSkillSpec(336, "UNI-037", "Armorbreaker", "Physical damage and Armor Break to one foe.", 303, 837, 837),
            new GagazetSinSkillSpec(337, "UNI-038", "Ronso Warcry", "Haste, Cheer and Focus all allied monsters.", 301, 838, 838),
            new GagazetSinSkillSpec(338, "UNI-038", "Glacial Pulse", "High non-elemental magic damage to all foes.", 299, 839, 839),
            new GagazetSinSkillSpec(339, "UNI-039", "Profane Vow", "Strong non-elemental magic damage to one foe.", 299, 840, 840),
            new GagazetSinSkillSpec(340, "UNI-039", "Willbreaker", "Heavy physical damage to one foe.", 303, 841, 841),
        });

        public static MonsterMagicGrowResult AppendGagazetSinSkill(byte[] bytes, int commandId)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (commandId < GagazetSinFirstCommandId || commandId > GagazetSinLastCommandId)
                throw new ArgumentOutOfRangeException(nameof(commandId));
            int count = EntryListFile.Unpack(bytes).Header.RealEntryCount;
            if (count != commandId)
                throw new InvalidDataException($"Gagazet SIN row {commandId} requires exactly {commandId} preceding rows; got {count}.");
            GagazetSinSkillSpec spec = GagazetSinSkills[commandId - GagazetSinFirstCommandId];
            return AppendMonsterSkill(bytes, monsterMagic2: true, spec.DonorRow,
                command => ApplyGagazetSinRecipe(command, spec));
        }

        static void ApplyGagazetSinRecipe(Ability_Command command, GagazetSinSkillSpec spec)
        {
            SetCalmCavernBase(command, new CalmCavernSinSkillSpec(
                spec.CommandId, spec.UniId, spec.Name, spec.Description,
                spec.DonorRow, spec.Anim1Id, spec.Anim2Id));
            switch (spec.CommandId)
            {
                case 329:
                    SetAllySupport(command, multi: true);
                    command.StatusChance.Protect = 254; command.StatusDuration.Protect = 10;
                    command.StatusChance.Shell = 254; command.StatusDuration.Shell = 10;
                    command.StatusChance.Regen = 254; command.StatusDuration.Regen = 10;
                    command.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer |
                        Ability_Command.StatBuffFlags.Aim | Ability_Command.StatBuffFlags.Focus |
                        Ability_Command.StatBuffFlags.Reflex | Ability_Command.StatBuffFlags.Luck;
                    command.StatBuffValue = 2;
                    break;
                case 330: SetPhysical(command, 72); command.StatusChance.BreakArmor = 100; break;
                case 331:
                    SetEnemyStatus(command, multi: true);
                    command.StatusChance.Poison = 100;
                    command.StatusChance.Silence = 100; command.StatusDuration.Silence = 5;
                    command.StatusChance.Darkness = 100; command.StatusDuration.Darkness = 5;
                    command.StatusChance.Slow = 100; command.StatusDuration.Slow = 5;
                    break;
                case 332: SetMagic(command, 65, multi: true); break;
                case 333:
                    SetMagic(command, 58, multi: true);
                    command.ElementFlgs = Ability_Command.ElementFlags.Water;
                    command.FlagMisc2DelayS = true;
                    break;
                case 334:
                    SetMagic(command, 42);
                    command.StatusChance.Death = 35;
                    command.StatBuffFlgs = Ability_Command.StatBuffFlags.Jinx;
                    command.StatBuffValue = 5;
                    break;
                case 335: SetPhysical(command, 54); break;
                case 336: SetPhysical(command, 48); command.StatusChance.BreakArmor = 100; break;
                case 337:
                    SetAllySupport(command, multi: true);
                    command.StatusChance.Haste = 254; command.StatusDuration.Haste = 8;
                    command.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Focus;
                    command.StatBuffValue = 2;
                    break;
                case 338: SetMagic(command, 50, multi: true); break;
                case 339: SetMagic(command, 56); break;
                case 340:
                    // Berserk/Ribbon bypass belongs to a blocked, separate ATEL design.
                    SetPhysical(command, 56);
                    break;
                default: throw new InvalidOperationException($"No Gagazet SIN recipe for row {spec.CommandId}.");
            }
        }
    }
}
