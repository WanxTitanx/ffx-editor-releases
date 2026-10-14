using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record YunalescaSpell(string Name, int Donor, bool Character, string Kind, int Index);

public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<YunalescaSpell> YunalescaSpells { get; } = Array.AsReadOnly(new[]
    {
        new YunalescaSpell("Yuna Silence", 56, true, "status", 0),
        new YunalescaSpell("Yuna Sleep", 56, true, "status", 1),
        new YunalescaSpell("Yuna Darkness", 56, true, "status", 2),
        new YunalescaSpell("Yuna Poison", 56, true, "status", 3),
        new YunalescaSpell("Yuna Slow", 56, true, "status", 4),
        new YunalescaSpell("Yuna Zombie", 56, true, "status", 5),
        new YunalescaSpell("Yuna Delay", 56, true, "status", 6),
        new YunalescaSpell("Yuna Curse", 56, true, "status", 7),
        new YunalescaSpell("Anti-Fira Fury", 345, false, "fury", 0),
        new YunalescaSpell("Anti-Thundara Fury", 346, false, "fury", 1),
        new YunalescaSpell("Anti-Watera Fury", 347, false, "fury", 2),
        new YunalescaSpell("Anti-Blizzara Fury", 348, false, "fury", 3),
        new YunalescaSpell("Dark Wind", 60, true, "wind", 0),
        new YunalescaSpell("Dark Might Guard", 59, true, "guard", 0),
        new YunalescaSpell("Hyper Dark Guard", 60, true, "hyper", 0),
        new YunalescaSpell("Darkja", 343, false, "ja", 0),
    });

    public static NativeBossSpellAppend AppendYunalescaSpell(byte[] monsters, byte[] characters,
        YunalescaSpell recipe, short first, short second)
    {
        ArgumentNullException.ThrowIfNull(monsters); ArgumentNullException.ThrowIfNull(characters);
        if (!YunalescaSpells.Contains(recipe) || first <= 0 || second < 0) throw new ArgumentException("Invalid Yunalesca recipe.");
        var original = EntryListFile.Unpack(monsters); int count = original.Header.RealEntryCount;
        if (original.Header.EntrySize != MonMagicEntrySize || original.Header.EntryTableFileOffset != 20
            || original.Header.PreviousFileCount != 0 || original.FirstFile.Length != count * MonMagicEntrySize || count >= 0x1000)
            throw new InvalidDataException("Invalid monster command extent.");
        var rows = Ability_Command.ReadList(monsters, false);
        if (rows.Any(c => DecodeUs(c.NameScriptBytes).Trim() == recipe.Name)) throw new InvalidOperationException("Yunalesca skill already exists.");
        var donors = recipe.Character ? Ability_Command.ReadList(characters, true) : rows;
        if (recipe.Donor >= donors.Count) throw new InvalidDataException("Missing Yunalesca donor.");
        var c = CloneCommand(donors[recipe.Donor]);
        c.NameScriptBytes = EncodeUs(recipe.Name);
        c.DescriptionScriptBytes = EncodeUs(recipe.Kind == "fury" ? "Dark magic. Three to six random hits are authored by Yunalesca's AI."
            : recipe.Kind == "ja" ? "Dark magic stronger than Darkga." : "Yunalesca's native phase ability.");
        c.JapaneseOnlyText1ScriptBytes = c.JapaneseOnlyText2ScriptBytes = [];
        c.NameScriptId = c.DescriptionScriptId = c.JapaneseOnlyText1ScriptId = c.JapaneseOnlyText2ScriptId = 0;
        c.Anim1Id = first; c.Anim2Id = second; c.CostMp = c.CostOverdrive = c.OverdriveCategory = 0;
        c.CharacterUser = Character_Enum.None; c.CasterAnimId = 3; c.HitCount = 1; c.ShatterChance = 0;
        c.SubMenuCategorization = c.SubSubMenuCategorization = 0;
        if (recipe.Kind is "fury" or "ja")
        {
            if (c.DamageFormula != DamageFormula_Enum.Magic || c.ElementFlgs != Ability_Command.ElementFlags.Dark
                || c.AttackPower != (recipe.Kind == "fury" ? 24 : 42))
                throw new InvalidDataException("Anti-ra/Darkga donor changed.");
            c.AttackPower = (byte)(recipe.Kind == "fury" ? 18 : 60);
            if (recipe.Kind == "fury")
                ConfigureYunalescaFury(c, Ability_Command.ReadList(characters, true)[YunalescaFuryDonors[recipe.Index]], first, 3);
        }
        else
        {
            c.StatusChance = new(); c.StatusDuration = new(); c.StatusFlgs = 0;
            c.StatBuffFlgs = 0; c.StatBuffValue = 0; c.SpecialBuffFlgs = 0;
            // Like native Protect/Regen: zero-power magic still enters the
            // native status/Delay delivery path without direct HP damage.
            c.ElementFlgs = 0; c.DamageFormula = DamageFormula_Enum.Magic;
            c.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp; c.AttackPower = 0; c.AttackCritBonus = 0;
            c.DamageFlgs = Ability_Command.DamageFlags.Magical;
            c.Misc2Flgs = 0; c.Misc3Flgs = Ability_Command.Misc3Flags.AffectedBySilence;
            c.Misc4Flgs = 0; c.MoveRank = 3;
            c.Misc1Flgs = Ability_Command.Misc1Flags.UseInCombat | Ability_Command.Misc1Flags.DisplayMoveName;
            c.TargetFlgs = (Ability_Command.TargetFlags)(recipe.Kind == "status" ? 0x33 : 0xB1);
            if (recipe.Kind == "status")
            {
                switch (recipe.Index)
                {
                    case 0: c.StatusChance.Silence = 100; c.StatusDuration.Silence = 3; break;
                    case 1: c.StatusChance.Sleep = 100; c.StatusDuration.Sleep = 3; break;
                    case 2: c.StatusChance.Darkness = 100; c.StatusDuration.Darkness = 3; break;
                    case 3: c.StatusChance.Poison = 100; break;
                    case 4: c.StatusChance.Slow = 100; c.StatusDuration.Slow = 255; break;
                    case 5: c.StatusChance.Zombie = 100; break;
                    case 6: c.FlagMisc2DelayS = true; break;
                    case 7: c.StatusFlgs = Ability_Command.StatusFlags.Curse; break;
                }
            }
            if (recipe.Kind is "wind" or "hyper")
            {
                // TargetHP/TargetMaxHP (5/8) are zeroed by native Gravity immunity
                // even when Heals is set. WielderMaxHP (16) is /10 and bypasses
                // that specific gate; these commands are always cast on self.
                c.DamageFormula = DamageFormula_Enum.WielderMaxHp;
                c.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
                c.DamageFlgs |= Ability_Command.DamageFlags.Heals | Ability_Command.DamageFlags.BreaksDamageLimit;
                c.AttackPower = (byte)(recipe.Kind == "wind" ? 1 : 2); // 10% / 20%.
                c.StatusChance.Regen = 255; c.StatusDuration.Regen = 254;
            }
            if (recipe.Kind is "guard" or "hyper")
            {
                c.StatusChance.Protect = c.StatusChance.Shell = 255;
                c.StatusChance.NulBlaze = c.StatusChance.NulFrost = c.StatusChance.NulShock = c.StatusChance.NulTide = 255;
                // Native Dispel skips duration 255; 254 stays removable.
                c.StatusDuration.Protect = c.StatusDuration.Shell = 254;
                c.StatusDuration.NulBlaze = c.StatusDuration.NulFrost = c.StatusDuration.NulShock = c.StatusDuration.NulTide = 254;
            }
            if (recipe.Kind == "hyper") { c.StatusChance.Haste = 255; c.StatusDuration.Haste = 254; }
        }
        byte[] row = BuildNewRow(c, original.SecondFile, out byte[] text);
        byte[] result = EntryListFile.Pack(MonMagicEntrySize, checked((short)(count + 1)), original.FirstFile.Concat(row).ToArray(), text);
        monsters.AsSpan(0, 10).CopyTo(result);
        var packed = EntryListFile.Unpack(result); var reread = Ability_Command.ReadList(result, false);
        if (!packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || reread.Count != count + 1 || !Ability_Command.WriteList(reread, false).AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Yunalesca append failed prefix/text/round-trip preservation.");
        return new(result, count, checked((ushort)(0x6000 | count)));
    }
}
