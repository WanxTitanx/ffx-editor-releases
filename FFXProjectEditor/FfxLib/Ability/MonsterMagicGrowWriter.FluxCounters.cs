using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record FluxCounterSpell(string Name, int DonorRow, bool CharacterDonor, byte Power);

public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<FluxCounterSpell> FluxCounterSpells { get; } = Array.AsReadOnly(new[]
    {
        new FluxCounterSpell("Anti-Fire", 57, false, 16),
        new FluxCounterSpell("Anti-Water", 60, false, 16),
        new FluxCounterSpell("Anti-Blizzard", 58, false, 16),
        new FluxCounterSpell("Anti-Thunder", 59, false, 16),
        new FluxCounterSpell("Anti-Aero", 367, true, 12),
    });

    public static NativeBossSpellAppend AppendFluxCounterSpell(byte[] monsterCommands, byte[] characterCommands,
        FluxCounterSpell recipe, short animation1, short animation2)
    {
        ArgumentNullException.ThrowIfNull(monsterCommands); ArgumentNullException.ThrowIfNull(characterCommands);
        if (!FluxCounterSpells.Contains(recipe)) throw new ArgumentException("Unknown Flux counter recipe.", nameof(recipe));
        if (animation1 <= 0 || animation2 < 0) throw new ArgumentOutOfRangeException(nameof(animation1));
        var original = EntryListFile.Unpack(monsterCommands);
        int count = original.Header.RealEntryCount;
        if (original.Header.EntrySize != MonMagicEntrySize || original.Header.EntryTableFileOffset != 20
            || original.Header.PreviousFileCount != 0 || count < 61 || count >= 0x1000
            || original.FirstFile.Length != count * MonMagicEntrySize)
            throw new InvalidDataException("Invalid monmagic2 table.");
        var monsters = Ability_Command.ReadList(monsterCommands, false);
        var donors = recipe.CharacterDonor ? Ability_Command.ReadList(characterCommands, true) : monsters;
        if (recipe.DonorRow >= donors.Count) throw new InvalidDataException("Missing exact spell donor.");
        var donor = donors[recipe.DonorRow];
        if (donor.DamageFormula != DamageFormula_Enum.Magic || donor.AttackPower != recipe.Power || donor.HitCount != 1
            || !donor.FlagDamageMagical || !donor.FlagDamageTypeHp)
            throw new InvalidDataException("The donor's damage contract changed.");
        if (monsters.Any(row => DecodeUs(row.NameScriptBytes).Trim().Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(recipe.Name + " already exists; use the immutable input.");
        var command = CloneCommand(donor);
        command.NameScriptBytes = EncodeUs(recipe.Name);
        command.DescriptionScriptBytes = EncodeUs("Dark-element magic damage to all opponents. Magical-damage counter.");
        command.JapaneseOnlyText1ScriptBytes = command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
        command.NameScriptId = command.DescriptionScriptId = command.JapaneseOnlyText1ScriptId = command.JapaneseOnlyText2ScriptId = 0;
        command.ElementFlgs = Ability_Command.ElementFlags.Dark;
        command.Anim1Id = animation1; command.Anim2Id = animation2;
        command.CostMp = 0; command.CostOverdrive = 0;
        if (recipe.CharacterDonor)
        {
            // Target-mask 0x3B is the actual Mortibody group spell contract.
            // Do not substitute the player's optional multi-target menu flag.
            command.TargetFlgs = monsters[57].TargetFlgs;
            command.TargetsAllowed = monsters[57].TargetsAllowed;
        }
        byte[] rowBytes = BuildNewRow(command, original.SecondFile, out byte[] pool);
        byte[] result = EntryListFile.Pack(MonMagicEntrySize, checked((short)(count + 1)),
            original.FirstFile.Concat(rowBytes).ToArray(), pool);
        monsterCommands.AsSpan(0, 10).CopyTo(result);
        var packed = EntryListFile.Unpack(result);
        var reread = Ability_Command.ReadList(result, false);
        if (!packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile)
            || !packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile)
            || reread.Count != count + 1 || !Ability_Command.WriteList(reread, false).AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Counter command failed preservation/readback checks.");
        return new(result, count, checked((ushort)(0x6000 | count)));
    }
}
