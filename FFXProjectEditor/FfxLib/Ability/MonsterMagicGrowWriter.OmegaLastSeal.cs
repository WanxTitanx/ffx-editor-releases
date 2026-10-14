using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record OmegaLastSealSpell(string Key, string Name, int Table, int Donor);

public static partial class MonsterMagicGrowWriter
{
    // The controller consumes these IDs. Append only: regional Omega T7/OD rows
    // remain byte-identical and are deliberately not repurposed for the boss.
    public static IReadOnlyList<OmegaLastSealSpell> OmegaLastSealSpells { get; } = Array.AsReadOnly(new[]
    {
        new OmegaLastSealSpell("rupture", "Omega Rupture", 1, 34),
        new OmegaLastSealSpell("reprisal", "Reprisal", 1, 34),
        new OmegaLastSealSpell("reconstitution", "Reconstitution", 0, 59),
        new OmegaLastSealSpell("concentration", "Concentration", 0, 59),
        new OmegaLastSealSpell("last_seal", "The Last Seal", 0, 59),
        new OmegaLastSealSpell("supernova", "Supernova", 1, 103),
        new OmegaLastSealSpell("protectga", "Custodian Protectga", 0, 59),
        new OmegaLastSealSpell("shellga", "Custodian Shellga", 0, 58),
        new OmegaLastSealSpell("dispelga", "Omega Dispelga", 0, 61),
        new OmegaLastSealSpell("holy_attack", "Holy Attack", 1, 271),
        new OmegaLastSealSpell("dark_attack", "Dark Attack", 1, 271),
        new OmegaLastSealSpell("holy_echo_fury", "Holy Echo Fury", 0, 65),
        new OmegaLastSealSpell("dark_echo_fury", "Dark Echo Fury", 0, 65),
    });

    public static OmegaSpellTable AppendOmegaLastSeal(byte[] source, byte[] characters,
        byte[] monsterOne, IReadOnlyList<(short First, short Second)> effects)
    {
        var original = EntryListFile.Unpack(source);
        var existing = Ability_Command.ReadList(source, false);
        var player = Ability_Command.ReadList(characters, true);
        var monsters = Ability_Command.ReadList(monsterOne, false);
        if (existing.Count != 481 || original.Header.EntrySize != MonMagicEntrySize
            || effects.Count != OmegaLastSealSpells.Count || effects.Any(e => e.First < 0 || e.Second < 0))
            throw new InvalidDataException("Last Seal requires the exact 481-row baseline and valid effect pairs.");

        byte[] data = original.FirstFile.ToArray(), text = original.SecondFile.ToArray();
        var commands = new Dictionary<string, ushort>();
        for (int i = 0; i < OmegaLastSealSpells.Count; i++)
        {
            var spec = OmegaLastSealSpells[i];
            var donor = (spec.Table == 0 ? player : monsters)[spec.Donor];
            var c = CloneCommand(donor);
            c.NameScriptBytes = EncodeUs(spec.Name);
            c.DescriptionScriptBytes = EncodeUs("A power bound to Omega Weapon's Last Seal.");
            c.JapaneseOnlyText1ScriptBytes = c.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            c.NameScriptId = c.DescriptionScriptId = c.JapaneseOnlyText1ScriptId = c.JapaneseOnlyText2ScriptId = 0;
            c.CharacterUser = Character_Enum.None;
            c.CostMp = c.CostOverdrive = c.OverdriveCategory = 0;
            c.SubMenuCategorization = c.SubSubMenuCategorization = 0;
            c.Anim1Id = effects[i].First; c.Anim2Id = effects[i].Second;
            c.CasterAnimId = donor.CasterAnimId; c.MoveRank = 3; c.HitCount = 1;
            c.ShatterChance = 0; c.StealGil = false; c.AttackCritBonus = 0; c.PreviewFlgs = 0;
            // Dispel's removal mask is native data, not a list of status grants.
            if (spec.Key != "dispelga")
            {
                c.StatusChance = new(); c.StatusDuration = new(); c.StatusFlgs = 0;
            }
            c.StatBuffFlgs = 0; c.StatBuffValue = 0; c.SpecialBuffFlgs = 0;
            c.Misc1Flgs = Ability_Command.Misc1Flags.UseInCombat | Ability_Command.Misc1Flags.DisplayMoveName;
            c.Misc2Flgs = 0;
            c.Misc3Flgs = spec.Key == "dispelga" ? donor.Misc3Flgs : Ability_Command.Misc3Flags.AffectedBySilence;
            c.Misc4Flgs = 0;
            c.DamageFormula = DamageFormula_Enum.Magic;
            c.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            c.DamageFlgs = Ability_Command.DamageFlags.Magical;
            c.TargetFlgs = (Ability_Command.TargetFlags)0xB3;
            c.ElementFlgs = 0; c.AttackPower = 0;
            switch (spec.Key)
            {
                case "rupture":
                    c.DamageFormula = DamageFormula_Enum.NoDamage;
                    c.StatusChance.Silence = c.StatusChance.Slow = c.StatusChance.Darkness = 100;
                    c.StatusDuration.Silence = c.StatusDuration.Darkness = 3;
                    c.StatusDuration.Slow = 254;
                    break;
                case "reprisal": c.AttackPower = 32; break;
                case "supernova":
                    c.AttackPower = 40; c.TargetFlgs = (Ability_Command.TargetFlags)0x87;
                    c.Misc3Flgs = 0;
                    break;
                case "reconstitution": case "concentration": case "last_seal":
                    c.DamageFormula = DamageFormula_Enum.NoDamage; c.DamageTypeFlgs = 0; c.DamageFlgs = 0;
                    c.TargetFlgs = (Ability_Command.TargetFlags)0xB1; c.Misc3Flgs = 0;
                    c.Anim1Id = c.Anim2Id = 0; c.CasterAnimId = 0;
                    break;
                case "protectga": case "shellga":
                    c.DamageFormula = DamageFormula_Enum.NoDamage; c.DamageTypeFlgs = 0; c.DamageFlgs = 0;
                    c.TargetFlgs = (Ability_Command.TargetFlags)0xB5;
                    if (spec.Key == "protectga") { c.StatusChance.Protect = 255; c.StatusDuration.Protect = 254; }
                    else { c.StatusChance.Shell = 255; c.StatusDuration.Shell = 254; }
                    break;
                case "dispelga":
                    c.DamageFormula = DamageFormula_Enum.NoDamage; c.DamageTypeFlgs = 0;
                    c.DamageFlgs = donor.DamageFlgs;
                    c.TargetFlgs = (Ability_Command.TargetFlags)0x87;
                    break;
                case "holy_attack": case "dark_attack":
                    c.DamageFormula = DamageFormula_Enum.Normal; c.AttackPower = 16;
                    c.DamageFlgs = Ability_Command.DamageFlags.Physical;
                    c.Misc3Flgs = 0; c.CasterAnimId = donor.CasterAnimId;
                    c.FlagMisc1AffectedByDarkness = true;
                    c.FlagMisc1HitCalcType = donor.FlagMisc1HitCalcType;
                    c.ElementFlgs = spec.Key == "holy_attack" ? Ability_Command.ElementFlags.Holy : Ability_Command.ElementFlags.Dark;
                    break;
                case "holy_echo_fury": case "dark_echo_fury":
                    c.AttackPower = 18; c.HitCount = 4; c.TargetFlgs = (Ability_Command.TargetFlags)0x87;
                    c.TargetsAllowed = player[126].TargetsAllowed; c.FlagMisc2RandomTargets = true;
                    c.ElementFlgs = spec.Key == "holy_echo_fury" ? Ability_Command.ElementFlags.Holy : Ability_Command.ElementFlags.Dark;
                    break;
                default: throw new InvalidDataException("Unknown Last Seal command.");
            }
            byte[] record = BuildNewRow(c, text, out byte[] nextText);
            text = nextText; data = data.Concat(record).ToArray();
            commands.Add(spec.Key, checked((ushort)(0x6000 | (481 + i))));
        }
        byte[] result = EntryListFile.Pack(MonMagicEntrySize, checked((short)(481 + OmegaLastSealSpells.Count)), data, text);
        source.AsSpan(0, 10).CopyTo(result);
        var packed = EntryListFile.Unpack(result);
        if (!packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || !Ability_Command.WriteList(Ability_Command.ReadList(result, false), false).SequenceEqual(result))
            throw new InvalidDataException("Last Seal append changed existing rows/text or failed round-trip.");
        return new(result, commands);
    }
}
