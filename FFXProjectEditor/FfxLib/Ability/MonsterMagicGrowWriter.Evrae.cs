using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record EvraeHolyTable(byte[] Bytes, EvraeHolyCommands Commands);
public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<int> EvraeRaDonors { get; } = Array.AsReadOnly(new[] { 69, 71, 72, 70 });
    public static IReadOnlyList<int> EvraeBasicDonors { get; } = Array.AsReadOnly(new[] { 66, 67, 68, 65 });
    public static IReadOnlyList<int> EvraeBasicFuryDonors { get; } = Array.AsReadOnly(new[] { 121, 122, 123, 120 });

    public static EvraeHolyTable AppendEvraeHoly(byte[] monsters, byte[] characters,
        IReadOnlyList<(short First, short Second)> singleEffects,
        IReadOnlyList<short> raEffects, IReadOnlyList<short> basicEffects)
    {
        var original = EntryListFile.Unpack(monsters);
        var oldRows = Ability_Command.ReadList(monsters, false);
        var player = Ability_Command.ReadList(characters, true);
        if (original.Header.EntrySize != MonMagicEntrySize || original.Header.EntryTableFileOffset != 20
            || original.Header.PreviousFileCount != 0 || oldRows.Count != 385 || player.Count <= 127
            || singleEffects.Count != 3 || raEffects.Count != 4 || basicEffects.Count != 4
            || singleEffects.Any(v => v.First <= 0 || v.Second <= 0)
            || raEffects.Concat(basicEffects).Any(v => v <= 0))
            throw new InvalidDataException("Expected the current 385-row packet and fourteen owned effects.");
        if (DecodeUs(oldRows[344].NameScriptBytes).Trim() != "Holy Fira"
            || oldRows[344].AttackPower != 24 || oldRows[344].ElementFlgs != Ability_Command.ElementFlags.Holy)
            throw new InvalidDataException("The existing Holy Fira binding changed.");
        string[] raNames = { "Fira", "Thundara", "Watera", "Blizzara" };
        string[] basicNames = { "Fire", "Thunder", "Water", "Blizzard" };
        byte[] result = monsters;
        ushort Append(Ability_Command command, string name)
        {
            var before = EntryListFile.Unpack(result);
            command.NameScriptBytes = EncodeUs(name);
            command.DescriptionScriptBytes = EncodeUs("Holy-element magic used by Evrae.");
            command.JapaneseOnlyText1ScriptBytes = command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            command.NameScriptId = command.DescriptionScriptId = command.JapaneseOnlyText1ScriptId = command.JapaneseOnlyText2ScriptId = 0;
            command.CostMp = command.CostOverdrive = command.OverdriveCategory = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Holy;
            command.CasterAnimId = 3;
            byte[] row = BuildNewRow(command, before.SecondFile, out byte[] text);
            result = EntryListFile.Pack(MonMagicEntrySize, checked((short)(before.Header.RealEntryCount + 1)),
                before.FirstFile.Concat(row).ToArray(), text);
            monsters.AsSpan(0, 10).CopyTo(result);
            return checked((ushort)(0x6000 | before.Header.RealEntryCount));
        }
        Ability_Command Spell(int donor, byte power)
        {
            var command = CloneCommand(player[donor]);
            if (command.DamageFormula != DamageFormula_Enum.Magic || command.AttackPower != power || command.HitCount != 1)
                throw new InvalidDataException("Native elemental spell donor changed.");
            return command;
        }
        void Fury(Ability_Command command, int donorIndex, short effect, byte hits)
        {
            var donor = player[donorIndex];
            if (donor.HitCount != 16 || !donor.FlagMisc2RandomTargets || donor.TargetFlgs != (Ability_Command.TargetFlags)0x87)
                throw new InvalidDataException("Native Fury donor changed.");
            command.Anim1Id = effect; command.Anim2Id = 0;
            command.TargetFlgs = donor.TargetFlgs; command.TargetsAllowed = donor.TargetsAllowed;
            command.Misc1Flgs = donor.Misc1Flgs;
            command.Misc2Flgs = donor.Misc2Flgs & ~Ability_Command.Misc2Flags.MenuLeft;
            command.Misc3Flgs = donor.Misc3Flgs; command.Misc4Flgs = donor.Misc4Flgs;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.HitCount = hits;
        }
        var singles = new List<ushort> { 0x6158 };
        for (int i = 1; i < 4; i++)
        {
            var c = Spell(EvraeRaDonors[i], 24); c.Anim1Id = singleEffects[i - 1].First; c.Anim2Id = singleEffects[i - 1].Second;
            singles.Add(Append(c, "Holy " + raNames[i]));
        }
        var ras = new List<ushort>(); var basics = new List<ushort>();
        for (int i = 0; i < 4; i++)
            for (byte hits = 3; hits <= 6; hits++)
            {
                var c = Spell(EvraeRaDonors[i], 24); Fury(c, YunalescaFuryDonors[i], raEffects[i], hits);
                ras.Add(Append(c, "Holy " + raNames[i] + " Fury"));
            }
        for (int i = 0; i < 4; i++)
        {
            var c = Spell(EvraeBasicDonors[i], 12); Fury(c, EvraeBasicFuryDonors[i], basicEffects[i], 6);
            basics.Add(Append(c, "Holy " + basicNames[i] + " Fury"));
        }
        var final = EntryListFile.Unpack(result);
        if (final.Header.RealEntryCount != 408
            || !final.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !final.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || !Ability_Command.WriteList(Ability_Command.ReadList(result, false), false).AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Evrae append failed prefix/text/round-trip preservation.");
        return new(result, new(singles, ras, basics));
    }
}
