using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record UltimaSpellTable(byte[] Bytes, IReadOnlyDictionary<string, ushort> Commands);

public static partial class MonsterMagicGrowWriter
{
    public static readonly string[] UltimaSpellKeys =
        { "darkAttack", "holyAttack", "break", "holyFlare", "secret", "darkMeteor", "holyMeteor", "guard" };

    public static UltimaSpellTable AppendUltima(byte[] source, byte[] characters, byte[] monsterOne,
        IReadOnlyList<(short First, short Second)> effects)
    {
        var original = EntryListFile.Unpack(source);
        var rows = Ability_Command.ReadList(source, false);
        var players = Ability_Command.ReadList(characters, true);
        var monsters = Ability_Command.ReadList(monsterOne, false);
        if (original.Header.EntrySize != MonMagicEntrySize || rows.Count != 444 || effects.Count != 8 || players.Count <= 114 || monsters.Count <= 127)
            throw new InvalidDataException("Ultima requires the exact 444-row post-Inside-Sin baseline and eight effect pairs.");
        string[] names = { "Dark Attack", "Holy Attack", "Ultima Break", "Holy Flare", "Ultima Secret", "Dark Meteor", "Holy Meteor", "Mighty Guard" };
        string[] descriptions = {
            "Physical Dark damage to one opponent.", "Physical Holy damage to one opponent.",
            "Attempts Confuse, Silence and Petrify together. Respects status resistance.",
            "Heavy Holy magic damage to one opponent.", "Grants Haste and one stack of Cheer, Focus and Luck.",
            "Dark-element Meteor.", "Holy-element Meteor.", "Grants Protect and Shell to all allied monsters."
        };
        Ability_Command[] donors = { monsters[127], monsters[127], rows[79], players[82], players[54], rows[292], rows[292], players[114] };
        var commands = new Dictionary<string, ushort>();
        byte[] data = original.FirstFile.ToArray(), text = original.SecondFile.ToArray();
        for (int i = 0; i < donors.Length; i++)
        {
            var c = CloneCommand(donors[i]);
            c.NameScriptBytes = EncodeUs(names[i]); c.DescriptionScriptBytes = EncodeUs(descriptions[i]);
            c.JapaneseOnlyText1ScriptBytes = c.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            c.NameScriptId = c.DescriptionScriptId = c.JapaneseOnlyText1ScriptId = c.JapaneseOnlyText2ScriptId = 0;
            c.CharacterUser = Character_Enum.None; c.CostMp = c.CostOverdrive = c.OverdriveCategory = 0;
            c.SubMenuCategorization = c.SubSubMenuCategorization = 0;
            c.Anim1Id = effects[i].First; c.Anim2Id = effects[i].Second;
            c.StatBuffFlgs = 0; c.StatBuffValue = 0; c.SpecialBuffFlgs = 0;
            c.StatusChance = new(); c.StatusDuration = new(); c.StatusFlgs = 0; c.ShatterChance = 0;
            c.Misc1Flgs |= Ability_Command.Misc1Flags.DisplayMoveName;
            if (i is 0 or 1)
            {
                c.ElementFlgs = i == 0 ? Ability_Command.ElementFlags.Dark : Ability_Command.ElementFlags.Holy;
                if (c.DamageFormula != DamageFormula_Enum.Normal || !c.DamageFlgs.HasFlag(Ability_Command.DamageFlags.Physical))
                    throw new InvalidDataException("Ultima's native physical donor changed.");
            }
            if (i == 2)
            {
                c.StatusChance.Confuse = c.StatusChance.Silence = c.StatusChance.Petrify = 100;
                c.StatusDuration.Silence = 3;
            }
            if (i is 3 or 5 or 6)
            {
                c.ElementFlgs = i == 5 ? Ability_Command.ElementFlags.Dark : Ability_Command.ElementFlags.Holy;
                if (c.DamageFormula != DamageFormula_Enum.Magic || c.AttackPower != 60)
                    throw new InvalidDataException("Flare/Meteor donor power or damage formula changed.");
            }
            if (i is 4 or 7)
            {
                c.TargetFlgs = (Ability_Command.TargetFlags)(i == 7 ? 0xB5 : 0xB1);
                c.DamageFormula = DamageFormula_Enum.Magic; c.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
                c.DamageFlgs = Ability_Command.DamageFlags.Magical; c.AttackPower = 0;
                c.ElementFlgs = 0; c.HitCount = 1; c.MoveRank = 3; c.CasterAnimId = 3;
                c.Misc1Flgs = Ability_Command.Misc1Flags.UseInCombat | Ability_Command.Misc1Flags.DisplayMoveName;
                c.Misc2Flgs = 0; c.Misc3Flgs = Ability_Command.Misc3Flags.AffectedBySilence; c.Misc4Flgs = 0;
                if (i == 4)
                {
                    c.StatusChance.Haste = 255; c.StatusDuration.Haste = 254;
                    c.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Focus | Ability_Command.StatBuffFlags.Luck;
                    c.StatBuffValue = 1;
                }
                else
                {
                    c.StatusChance.Protect = c.StatusChance.Shell = 255;
                    c.StatusDuration.Protect = c.StatusDuration.Shell = 254;
                }
            }
            commands.Add(UltimaSpellKeys[i], checked((ushort)(0x6000 | rows.Count)));
            byte[] record = BuildNewRow(c, text, out byte[] newText);
            text = newText; data = data.Concat(record).ToArray();
            rows.Add(c);
        }
        byte[] output = EntryListFile.Pack(MonMagicEntrySize, 452, data, text);
        source.AsSpan(0, 10).CopyTo(output);
        var packed = EntryListFile.Unpack(output);
        if (packed.Header.RealEntryCount != 452
            || !packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || !Ability_Command.WriteList(Ability_Command.ReadList(output, false), false).AsSpan().SequenceEqual(output))
            throw new InvalidDataException("Ultima append changed an existing command entry.");
        return new(output, commands);
    }
}
