using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>
    /// In-place identity patches for Kimahri Ronso Rage Overdrive rows (#104–115).
    /// Blue Mage clones are built from unpatched vanilla donors before these run.
    /// Boss-vs-mob splits and Self-Destruct survive-at-1HP need runtime hooks for full fidelity.
    /// </summary>
    public static class KimahriOverdriveCommandPatch
    {
        public const byte LongBuffDuration = 12;
        public const byte PositiveStatBuffStacks = 2;

        const byte GuaranteedChance = 254;
        const byte StrongChance = 200;
        const byte ModerateChance = 150;
        const byte LightChance = 120;
        const byte ShortRegenDuration = 8;
        const byte ShortStatusDuration = 6;

        public static void ApplyAll(List<Ability_Command> list)
        {
            if (list.Count < 116)
                throw new InvalidOperationException($"Kimahri OD patch expects at least 116 rows, got {list.Count}.");

            PatchJump(list[104]);
            PatchFireBreath(list[105]);
            PatchSeedCannon(list[106]);
            PatchSelfDestruct(list[107]);
            PatchThrustKick(list[108]);
            PatchStoneBreath(list[109]);
            PatchAquaBreath(list[110]);
            PatchDoom(list[111]);
            PatchWhiteWind(list[112]);
            PatchBadBreath(list[113]);
            PatchMightyGuard(list[114]);
            PatchNova(list[115]);
        }

        static void PatchJump(Ability_Command row)
        {
            row.AttackPower = 48;
            row.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            row.FlagMisc2DelayS = true;
        }

        static void PatchFireBreath(Ability_Command row)
        {
            row.AttackPower = 36;
            row.FlagTargetMulti = true;
            row.FlagElementFire = true;
            row.StatusChance.BreakArmor = LightChance;
        }

        static void PatchSeedCannon(Ability_Command row)
        {
            row.AttackPower = 55;
            row.AttackAccuracy = 100;
            row.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            row.FlagMisc3Piercing = true;
            row.StatusChance.BreakArmor = LightChance;
        }

        static void PatchSelfDestruct(Ability_Command row)
        {
            row.AttackPower = 45;
            row.FlagMisc3DestroyCaster = false;
            row.DamageFormula = DamageFormula_Enum.WielderHighHp;
            row.FlagDamagePhysical = true;
        }

        static void PatchThrustKick(Ability_Command row)
        {
            row.AttackPower = 50;
            row.FlagStatusEject = true;
            row.ShatterChance = 100;
            row.StatusChance.BreakArmor = ModerateChance;
            row.FlagMisc2DelayL = true;
        }

        static void PatchStoneBreath(Ability_Command row)
        {
            row.FlagTargetMulti = true;
            row.StatusChance.Petrify = GuaranteedChance;
            row.StatusChance.Slow = StrongChance;
            row.StatusDuration.Slow = ShortStatusDuration;
            row.StatusChance.BreakMental = LightChance;
        }

        static void PatchAquaBreath(Ability_Command row)
        {
            row.AttackPower = 39;
            row.FlagTargetMulti = true;
            row.FlagElementWater = true;
            row.StatusChance.BreakMental = GuaranteedChance;
        }

        static void PatchDoom(Ability_Command row)
        {
            row.FlagStatusDoom = true;
            row.FlagStatusCurse = true;
            row.FlagMisc2DelayL = true;
            row.DamageFormula = DamageFormula_Enum.TargetHp;
            row.AttackPower = 32;
            row.FlagDamageMagical = true;
        }

        static void PatchWhiteWind(Ability_Command row)
        {
            row.AttackPower = 60;
            row.FlagTargetMulti = true;
            row.FlagDamageHeals = true;
            row.FlagDamageCleansesStatuses = true;
            row.DamageFormula = DamageFormula_Enum.Healing;
            row.StatusChance.Regen = GuaranteedChance;
            row.StatusDuration.Regen = ShortRegenDuration;
        }

        static void PatchBadBreath(Ability_Command row)
        {
            row.AttackPower = 20;
            row.FlagTargetMulti = true;
            row.FlagDamageMagical = true;
            row.StatusChance.Poison = StrongChance;
            row.StatusChance.Sleep = ModerateChance;
            row.StatusDuration.Sleep = ShortStatusDuration;
            row.StatusChance.Silence = ModerateChance;
            row.StatusDuration.Silence = ShortStatusDuration;
            row.StatusChance.Darkness = ModerateChance;
            row.StatusDuration.Darkness = ShortStatusDuration;
            row.StatusChance.Slow = LightChance;
            row.StatusDuration.Slow = ShortStatusDuration;
            row.FlagStatusCurse = true;
        }

        static void PatchMightyGuard(Ability_Command row)
        {
            row.FlagTargetMulti = true;
            SetLongBuffStatuses(row, protect: true, shell: true, haste: true);
            SetPositiveStatBuffs(
                row,
                Ability_Command.StatBuffFlags.Cheer
                    | Ability_Command.StatBuffFlags.Aim
                    | Ability_Command.StatBuffFlags.Focus
                    | Ability_Command.StatBuffFlags.Reflex
                    | Ability_Command.StatBuffFlags.Luck,
                PositiveStatBuffStacks);
        }

        static void PatchNova(Ability_Command row)
        {
            row.AttackPower = 70;
            row.DamageFormula = DamageFormula_Enum.Magic;
            row.FlagDamageMagical = true;
            row.FlagDamageBreaksDamageLimit = false;
            row.FlagDamageSupressBreakDamageLimit = false;
        }

        static void SetLongBuffStatuses(
            Ability_Command command,
            bool protect = false,
            bool shell = false,
            bool haste = false)
        {
            if (protect)
            {
                command.StatusChance.Protect = GuaranteedChance;
                command.StatusDuration.Protect = LongBuffDuration;
            }

            if (shell)
            {
                command.StatusChance.Shell = GuaranteedChance;
                command.StatusDuration.Shell = LongBuffDuration;
            }

            if (haste)
            {
                command.StatusChance.Haste = GuaranteedChance;
                command.StatusDuration.Haste = LongBuffDuration;
            }
        }

        static void SetPositiveStatBuffs(
            Ability_Command command,
            Ability_Command.StatBuffFlags flags,
            byte stacks)
        {
            command.StatBuffFlgs |= flags;
            command.StatBuffValue = stacks;
        }
    }
}
