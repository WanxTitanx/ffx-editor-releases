using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record InsideSinSpell(string Key, string Name, string Description, int Donor,
    bool MonsterOneDonor = false);
public sealed record InsideSinSpellTable(byte[] Bytes, IReadOnlyDictionary<string, ushort> Commands);

public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<InsideSinSpell> InsideSinSpells { get; } = Array.AsReadOnly(new[]
    {
        new InsideSinSpell("pulse", "Abyssal Pulse", "Dark magic damage to all opponents.", 73),
        new InsideSinSpell("rupture", "Rupture", "Non-elemental magic damage and Slow to one opponent.", 82),
        new InsideSinSpell("bastion", "Bastion", "Grants the user Protect, Shell and Regen.", 59),
        new InsideSinSpell("venom", "Venom Hex", "Dark magic damage, Poison and Silence to one opponent.", 56),
        new InsideSinSpell("shatter", "Shattering Blow", "Physical damage and Armor Break to one opponent.", 69),
        new InsideSinSpell("aegis", "Aegis", "Grants the user Protect, Shell, Reflect and Regen.", 59),
        new InsideSinSpell("barbatos", "Oblivion Cannon", "Heavy physical damage, Armor Break and Delay to all opponents.", 73),
        new InsideSinSpell("behemoth", "King's Ruin", "Four random physical impacts with Armor Break and Delay.", 126),
        new InsideSinSpell("malboro", "Plague Bloom", "Dark magic damage, Zombie, Curse and Slow to all opponents.", 83, true),
    });

    public static InsideSinSpellTable AppendInsideSin(byte[] source, byte[] characters, byte[] monsterOne,
        IReadOnlyList<(short First, short Second)> effects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(monsterOne);
        ArgumentNullException.ThrowIfNull(effects);
        var original = EntryListFile.Unpack(source);
        int firstRow = original.Header.RealEntryCount;
        if (original.Header.EntrySize != MonMagicEntrySize || original.Header.EntryTableFileOffset != 20
            || original.Header.PreviousFileCount != 0 || firstRow < 435 || firstRow + InsideSinSpells.Count >= 0x1000
            || original.FirstFile.Length != firstRow * MonMagicEntrySize || effects.Count != InsideSinSpells.Count)
            throw new InvalidDataException("Unsupported Inside Sin command table or visual map.");
        var existing = Ability_Command.ReadList(source, false);
        if (existing.Any(c => InsideSinSpells.Any(s => DecodeUs(c.NameScriptBytes).Trim() == s.Name)))
            throw new InvalidOperationException("Inside Sin commands already exist; use the immutable baseline.");
        var player = Ability_Command.ReadList(characters, true);
        var monster = Ability_Command.ReadList(monsterOne, false);
        var commands = new Dictionary<string, ushort>();
        byte[] data = original.FirstFile.ToArray(), text = original.SecondFile.ToArray();
        for (int i = 0; i < InsideSinSpells.Count; i++)
        {
            var spec = InsideSinSpells[i]; var fx = effects[i];
            var donors = spec.MonsterOneDonor ? monster : player;
            if (spec.Donor >= donors.Count || fx.First <= 0 || fx.Second < 0)
                throw new InvalidDataException("Missing Inside Sin donor/visual.");
            var donor = donors[spec.Donor]; var c = CloneCommand(donor);
            c.NameScriptBytes = EncodeUs(spec.Name); c.DescriptionScriptBytes = EncodeUs(spec.Description);
            c.JapaneseOnlyText1ScriptBytes = c.JapaneseOnlyText2ScriptBytes = [];
            c.NameScriptId = c.DescriptionScriptId = c.JapaneseOnlyText1ScriptId = c.JapaneseOnlyText2ScriptId = 0;
            c.Anim1Id = fx.First; c.Anim2Id = fx.Second;
            c.CharacterUser = Character_Enum.None; c.CasterAnimId = 3;
            c.CostMp = c.CostOverdrive = c.OverdriveCategory = 0;
            c.SubMenuCategorization = c.SubSubMenuCategorization = 0;
            c.MenuFlgs = 0; c.MoveRank = 3; c.HitCount = 1;
            c.StatusChance = new(); c.StatusDuration = new(); c.StatusFlgs = 0;
            c.StatBuffFlgs = 0; c.StatBuffValue = 0; c.SpecialBuffFlgs = 0;
            c.ShatterChance = 0; c.AttackCritBonus = 0; c.AttackAccuracy = 100;
            c.Misc1Flgs = Ability_Command.Misc1Flags.UseInCombat | Ability_Command.Misc1Flags.DisplayMoveName;
            c.Misc2Flgs = 0; c.Misc3Flgs = Ability_Command.Misc3Flags.AffectedBySilence;
            c.Misc4Flgs = 0; c.PreviewFlgs = 0; c.StealGil = false;
            c.TargetFlgs = (Ability_Command.TargetFlags)0x33;
            c.DamageFormula = DamageFormula_Enum.Magic; c.AttackPower = 24;
            c.DamageFlgs = Ability_Command.DamageFlags.Magical;
            c.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            c.ElementFlgs = Ability_Command.ElementFlags.Dark;
            switch (spec.Key)
            {
                case "pulse": c.TargetFlgs = (Ability_Command.TargetFlags)0x87; break;
                case "rupture":
                    c.ElementFlgs = 0; c.AttackPower = 30;
                    c.StatusChance.Slow = 75; c.StatusDuration.Slow = 3; break;
                case "bastion":
                case "aegis":
                    c.TargetFlgs = (Ability_Command.TargetFlags)0xB1; c.ElementFlgs = 0; c.AttackPower = 0;
                    c.StatusChance.Protect = c.StatusChance.Shell = c.StatusChance.Regen = 255;
                    // Duration 255 is skipped by native Dispel; 254 remains removable.
                    c.StatusDuration.Protect = c.StatusDuration.Shell = c.StatusDuration.Regen = 254;
                    if (spec.Key == "aegis") { c.StatusChance.Reflect = 255; c.StatusDuration.Reflect = 254; }
                    break;
                case "venom":
                    c.StatusChance.Poison = 75; c.StatusChance.Silence = 50; c.StatusDuration.Silence = 3; break;
                case "shatter":
                case "barbatos":
                case "behemoth":
                    c.DamageFormula = DamageFormula_Enum.Normal; c.DamageFlgs = Ability_Command.DamageFlags.Physical;
                    c.ElementFlgs = 0; c.Misc3Flgs = 0; c.StatusChance.BreakArmor = 75;
                    if (spec.Key != "shatter") { c.TargetFlgs = (Ability_Command.TargetFlags)0x87; c.FlagMisc2DelayS = true; }
                    if (spec.Key == "barbatos") c.AttackPower = 60;
                    if (spec.Key == "behemoth")
                    {
                        if (!donor.FlagMisc2RandomTargets || donor.HitCount != 16 || donor.TargetFlgs != (Ability_Command.TargetFlags)0x87)
                            throw new InvalidDataException("Native Thunder Fury impact contract changed.");
                        c.TargetsAllowed = donor.TargetsAllowed; c.FlagMisc2RandomTargets = true;
                        c.HitCount = 4; c.Anim2Id = 0;
                    }
                    break;
                case "malboro":
                    c.TargetFlgs = (Ability_Command.TargetFlags)0x87; c.CasterAnimId = donor.CasterAnimId;
                    c.AttackPower = 42; c.StatusChance.Zombie = 100; c.StatusChance.Slow = 100;
                    c.StatusDuration.Slow = 3; c.StatusFlgs = Ability_Command.StatusFlags.Curse; break;
            }
            if (spec.Key is "barbatos" or "behemoth" or "malboro") c.FlagMisc4EmptyOverdrive = true;
            byte[] row = BuildNewRow(c, text, out byte[] newText); text = newText;
            data = data.Concat(row).ToArray(); commands.Add(spec.Key, checked((ushort)(0x6000 | (firstRow + i))));
        }
        byte[] output = EntryListFile.Pack(MonMagicEntrySize, checked((short)(firstRow + InsideSinSpells.Count)), data, text);
        source.AsSpan(0, 10).CopyTo(output);
        var packed = EntryListFile.Unpack(output); var reread = Ability_Command.ReadList(output, false);
        if (!packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || reread.Count != firstRow + InsideSinSpells.Count || !Ability_Command.WriteList(reread, false).AsSpan().SequenceEqual(output))
            throw new InvalidDataException("Inside Sin append failed record/text/round-trip preservation.");
        return new(output, commands);
    }
}
