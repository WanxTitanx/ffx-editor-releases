using System;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability
{
    public static partial class MonsterMagicGrowWriter
    {
        public const int CalmCavernSinFirstCommandId = 304;
        public const int CalmCavernSinLastCommandId = 328;

        public sealed record CalmCavernSinSkillSpec(
            int CommandId, string UniId, string Name, string Description,
            int DonorRow, short Anim1Id, short Anim2Id);

        // UNI-015..023 have two commands each; UNI-024..030 have one. The native
        // monster action and all target sequencing remain the responsibility of AI.
        public static IReadOnlyList<CalmCavernSinSkillSpec> CalmCavernSinSkills { get; } = Array.AsReadOnly(new[]
        {
            new CalmCavernSinSkillSpec(304, "UNI-015", "Arcane Pulse", "Non-elemental magic damage to one foe.", 299, 800, 800),
            new CalmCavernSinSkillSpec(305, "UNI-015", "Arcane Wave", "Light non-elemental magic damage to all foes.", 299, 801, 801),
            new CalmCavernSinSkillSpec(306, "UNI-016", "Flanking Bite", "Physical damage to one foe.", 303, 802, 802),
            new CalmCavernSinSkillSpec(307, "UNI-016", "Guardbreaker", "Physical damage and Armor Break to one foe.", 303, 803, 803),
            new CalmCavernSinSkillSpec(308, "UNI-017", "Veil", "Protect and Shell one allied monster.", 301, 804, 804),
            new CalmCavernSinSkillSpec(309, "UNI-017", "Dragging Strike", "Physical damage and Delay to one foe.", 303, 805, 805),
            new CalmCavernSinSkillSpec(310, "UNI-018", "Shadow Thrust", "Non-elemental physical damage to one foe.", 303, 806, 806),
            new CalmCavernSinSkillSpec(311, "UNI-018", "Blindside", "Physical damage with a chance of Darkness.", 303, 807, 807),
            new CalmCavernSinSkillSpec(312, "UNI-019", "Arcane Voice", "Non-elemental magic damage to one foe.", 299, 808, 808),
            new CalmCavernSinSkillSpec(313, "UNI-019", "Cutting Silence", "Magic damage with a chance of Silence.", 299, 809, 809),
            new CalmCavernSinSkillSpec(314, "UNI-020", "Slow", "Slow one foe.", 141, 810, 810),
            new CalmCavernSinSkillSpec(315, "UNI-020", "Rhythm Impact", "Physical damage to the selected foe.", 303, 811, 811),
            new CalmCavernSinSkillSpec(316, "UNI-021", "Quaking Tail", "Physical damage to all foes.", 303, 812, 812),
            new CalmCavernSinSkillSpec(317, "UNI-021", "Petrifying Gaze", "Chance of Petrify on one foe.", 300, 813, 813),
            new CalmCavernSinSkillSpec(318, "UNI-022", "Demolishing Fist", "Heavy physical damage to one foe.", 303, 814, 814),
            new CalmCavernSinSkillSpec(319, "UNI-022", "Colossus Quake", "Physical damage to all foes.", 303, 815, 815),
            new CalmCavernSinSkillSpec(320, "UNI-023", "Toxic Mist", "Magic damage and Poison to all foes.", 299, 816, 816),
            new CalmCavernSinSkillSpec(321, "UNI-023", "Foul Breath", "Independent chances of Darkness, Silence and Slow.", 300, 817, 817),
            new CalmCavernSinSkillSpec(322, "UNI-024", "Sepulchral Blessing", "Protect, Shell and Regen all allied monsters.", 301, 818, 818),
            new CalmCavernSinSkillSpec(323, "UNI-025", "Wraith Restoration", "Heal all allied monsters and cure Poison, Silence and Slow.", 64, 819, 819),
            new CalmCavernSinSkillSpec(324, "UNI-026", "Wraith Ascension", "Haste, Cheer and Focus all allied monsters.", 301, 820, 820),
            new CalmCavernSinSkillSpec(325, "UNI-027", "Netherburst", "Heavy non-elemental magic damage to all foes.", 299, 821, 822),
            new CalmCavernSinSkillSpec(326, "UNI-028", "Despairing Breath", "Poison, Silence and Darkness all foes.", 300, 823, 823),
            new CalmCavernSinSkillSpec(327, "UNI-029", "Gravitic Shackles", "Slow and Delay all foes.", 141, 824, 824),
            new CalmCavernSinSkillSpec(328, "UNI-030", "Soul Drain", "Heavy non-elemental magic damage; absorb dealt HP.", 299, 825, 826),
        });

        public static MonsterMagicGrowResult AppendCalmCavernSinSkill(byte[] bytes, int commandId)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (commandId < CalmCavernSinFirstCommandId || commandId > CalmCavernSinLastCommandId)
                throw new ArgumentOutOfRangeException(nameof(commandId));
            int count = EntryListFile.Unpack(bytes).Header.RealEntryCount;
            if (count != commandId)
                throw new InvalidDataException($"Calm/Cavern SIN row {commandId} requires exactly {commandId} preceding rows; got {count}.");
            CalmCavernSinSkillSpec spec = CalmCavernSinSkills[commandId - CalmCavernSinFirstCommandId];
            return AppendMonsterSkill(bytes, monsterMagic2: true, spec.DonorRow,
                command => ApplyCalmCavernSinRecipe(command, spec));
        }

        static void ApplyCalmCavernSinRecipe(Ability_Command command, CalmCavernSinSkillSpec spec)
        {
            SetCalmCavernBase(command, spec);
            switch (spec.CommandId)
            {
                case 304: SetMagic(command, 38); break;
                case 305: SetMagic(command, 24, multi: true); break;
                case 306: SetPhysical(command, 40); break;
                case 307: SetPhysical(command, 34); command.StatusChance.BreakArmor = 100; break;
                case 308:
                    SetAllySupport(command);
                    command.StatusChance.Protect = 254; command.StatusDuration.Protect = 6;
                    command.StatusChance.Shell = 254; command.StatusDuration.Shell = 6;
                    break;
                case 309: SetPhysical(command, 36); command.FlagMisc2DelayS = true; break;
                case 310: SetPhysical(command, 45); break;
                case 311:
                    SetPhysical(command, 32);
                    command.StatusChance.Darkness = 75; command.StatusDuration.Darkness = 3;
                    break;
                case 312: SetMagic(command, 40); break;
                case 313:
                    SetMagic(command, 32);
                    command.StatusChance.Silence = 80; command.StatusDuration.Silence = 3;
                    break;
                case 314: SetSlow(command, multi: false, delay: false); break;
                case 315: SetPhysical(command, 44); break;
                case 316: SetPhysical(command, 40, multi: true); break;
                case 317: SetEnemyStatus(command); command.StatusChance.Petrify = 65; break;
                case 318: SetPhysical(command, 60); break;
                case 319: SetPhysical(command, 36, multi: true); break;
                case 320: SetMagic(command, 34, multi: true); command.StatusChance.Poison = 80; break;
                case 321:
                    SetEnemyStatus(command);
                    // Each status rolls separately: zero to three may land after resistance.
                    command.StatusChance.Darkness = 50; command.StatusDuration.Darkness = 3;
                    command.StatusChance.Silence = 50; command.StatusDuration.Silence = 3;
                    command.StatusChance.Slow = 50; command.StatusDuration.Slow = 3;
                    break;
                case 322:
                    SetAllySupport(command, multi: true);
                    command.StatusChance.Protect = 254; command.StatusDuration.Protect = 8;
                    command.StatusChance.Shell = 254; command.StatusDuration.Shell = 8;
                    command.StatusChance.Regen = 254; command.StatusDuration.Regen = 8;
                    break;
                case 323:
                    SetAllySupport(command, multi: true);
                    command.DamageFormula = DamageFormula_Enum.Healing;
                    command.AttackPower = 72;
                    command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
                    command.DamageFlgs = Ability_Command.DamageFlags.Magical |
                        Ability_Command.DamageFlags.Heals | Ability_Command.DamageFlags.CleansesStatuses;
                    command.PreviewFlgs = Ability_Command.PreviewFlags.Active |
                        Ability_Command.PreviewFlags.HealHp | Ability_Command.PreviewFlags.HealStatuses;
                    command.StatusChance.Poison = 254;
                    command.StatusChance.Silence = 254; command.StatusDuration.Silence = 254;
                    command.StatusChance.Slow = 254; command.StatusDuration.Slow = 254;
                    break;
                case 324:
                    SetAllySupport(command, multi: true);
                    command.StatusChance.Haste = 254; command.StatusDuration.Haste = 8;
                    command.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Focus;
                    command.StatBuffValue = 2;
                    break;
                case 325: SetMagic(command, 54, multi: true); break;
                case 326:
                    SetEnemyStatus(command, multi: true);
                    command.StatusChance.Poison = 100;
                    command.StatusChance.Silence = 100; command.StatusDuration.Silence = 4;
                    command.StatusChance.Darkness = 100; command.StatusDuration.Darkness = 4;
                    break;
                case 327: SetSlow(command, multi: true, delay: true); break;
                case 328: SetMagic(command, 52); command.FlagMisc2AbsorbDamage = true; break;
                default: throw new InvalidOperationException($"No Calm/Cavern recipe for row {spec.CommandId}.");
            }
        }

        static void SetCalmCavernBase(Ability_Command command, CalmCavernSinSkillSpec spec)
        {
            command.NameScriptBytes = EncodeUs(spec.Name);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs(spec.Description);
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            command.MenuFlgs = 0;
            command.TargetFlgs = EnemyTarget(multi: false);
            command.Misc1Flgs = Ability_Command.Misc1Flags.UseInCombat | Ability_Command.Misc1Flags.DisplayMoveName;
            command.Misc2Flgs = 0;
            command.Misc3Flgs = 0;
            command.Misc4Flgs = 0;
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.AttackPower = 0;
            command.AttackAccuracy = 100;
            command.AttackCritBonus = 0;
            command.HitCount = 1;
            command.CostMp = 0;
            command.CostOverdrive = 0;
            command.ShatterChance = 0;
            command.ElementFlgs = 0;
            command.StatusChance = new StatusByteList();
            command.StatusDuration = new StatusDurationByteList();
            command.StatusFlgs = 0;
            command.StatBuffFlgs = 0;
            command.StatBuffValue = 0;
            command.SpecialBuffFlgs = 0;
            command.OverdriveCategory = 0;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
            command.Anim1Id = spec.Anim1Id;
            command.Anim2Id = spec.Anim2Id;
        }

        static Ability_Command.TargetFlags EnemyTarget(bool multi) =>
            Ability_Command.TargetFlags.Enabled | Ability_Command.TargetFlags.Enemies |
            Ability_Command.TargetFlags.EitherTeam | Ability_Command.TargetFlags.LongRange |
            (multi ? Ability_Command.TargetFlags.Multi : 0);

        static Ability_Command.TargetFlags AllyTarget(bool multi) =>
            Ability_Command.TargetFlags.Enabled | Ability_Command.TargetFlags.EitherTeam |
            Ability_Command.TargetFlags.LongRange |
            (multi ? Ability_Command.TargetFlags.Multi : 0);

        static void SetPhysical(Ability_Command command, byte power, bool multi = false)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = power;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.TargetFlgs = EnemyTarget(multi);
            command.FlagMisc1AffectedByDarkness = true;
        }

        static void SetMagic(Ability_Command command, byte power, bool multi = false)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = power;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.TargetFlgs = EnemyTarget(multi);
            command.FlagMisc1AffectedByReflect = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
        }

        static void SetAllySupport(Ability_Command command, bool multi = false)
        {
            command.TargetFlgs = AllyTarget(multi);
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
        }

        static void SetEnemyStatus(Ability_Command command, bool multi = false)
        {
            command.TargetFlgs = EnemyTarget(multi);
            command.FlagMisc1AffectedByReflect = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
        }

        static void SetSlow(Ability_Command command, bool multi, bool delay)
        {
            SetEnemyStatus(command, multi);
            command.DamageFormula = DamageFormula_Enum.TargetTickCounter;
            command.AttackPower = 16;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Ctb;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 254;
            command.FlagMisc2DelayS = delay;
        }
    }
}
