using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record NativeBossSpell(string Key, string Name, string Description,
    int DonorId, bool CharacterDonor, Ability_Command.ElementFlags Element);

public sealed record NativeBossSpellAppend(byte[] Bytes, int Row, ushort Operand);

public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<NativeBossSpell> NativeBossSpells { get; } = Array.AsReadOnly(new[]
    {
        new NativeBossSpell("Darkra", "Darkra", "Deals dark-element magic damage to one target.", 69, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("Darkga", "Darkga", "Deals heavy dark-element magic damage to one target.", 73, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("HolyFira", "Holy Fira", "Deals holy-element magic damage to one target.", 69, true, Ability_Command.ElementFlags.Holy),
        new NativeBossSpell("AntiFira", "Anti-Fira", "Dark-element counterpart of Fira.", 69, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("AntiThundara", "Anti-Thundara", "Dark-element counterpart of Thundara.", 71, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("AntiWatera", "Anti-Watera", "Dark-element counterpart of Watera.", 72, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("AntiBlizzara", "Anti-Blizzara", "Dark-element counterpart of Blizzara.", 70, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("AntiFlareNatus", "Anti-Flare", "Dark-element counterpart of Seymour Natus's Flare.", 82, true, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("AntiFlareFlux", "Anti-Flare", "Dark-element counterpart of Seymour Flux's Flare.", 121, false, Ability_Command.ElementFlags.Dark),
        new NativeBossSpell("Requiem", "Requiem", "Seymour's own Overdrive. Deals magic damage to all opponents.", 227, true, 0),
    });

    /// <summary>Append a monster command from an exact character/monster donor.
    /// Keep every existing record and the complete old text prefix byte-identical.</summary>
    public static NativeBossSpellAppend AppendNativeBossSpell(byte[] monsterCommands, byte[] characterCommands,
        NativeBossSpell recipe, short animation1, short animation2)
    {
        ArgumentNullException.ThrowIfNull(monsterCommands);
        ArgumentNullException.ThrowIfNull(characterCommands);
        ArgumentNullException.ThrowIfNull(recipe);
        if (!NativeBossSpells.Contains(recipe)) throw new ArgumentException("Unknown native boss spell.", nameof(recipe));
        if (animation1 <= 0 || animation2 < 0) throw new ArgumentOutOfRangeException(nameof(animation1));
        EntryListFile original = EntryListFile.Unpack(monsterCommands);
        int count = original.Header.RealEntryCount;
        if (original.Header.EntrySize != MonMagicEntrySize || count <= 0 || count >= 0x1000
            || original.Header.EntryTableFileOffset != 0x14 || original.Header.PreviousFileCount != 0
            || original.FirstFile.Length != count * MonMagicEntrySize)
            throw new InvalidDataException("Invalid monster command table or exhausted command ID domain.");
        byte[] donorBytes = recipe.CharacterDonor ? characterCommands : monsterCommands;
        int donorSize = recipe.CharacterDonor ? 0x60 : MonMagicEntrySize;
        if (EntryListFile.Unpack(donorBytes).Header.EntrySize != donorSize)
            throw new InvalidDataException("Donor table has the wrong record width.");
        var donors = Ability_Command.ReadList(donorBytes, hasExtraInfo: recipe.CharacterDonor);
        if (recipe.DonorId < 0 || recipe.DonorId >= donors.Count) throw new InvalidDataException("Missing spell donor.");
        Ability_Command command = CloneCommand(donors[recipe.DonorId]);
        if (command.DamageFormula != DamageFormula_Enum.Magic || command.AttackPower == 0)
            throw new InvalidDataException("The selected donor is not a damaging magic command.");
        if (Ability_Command.ReadList(monsterCommands, false).Any(existing =>
            DecodeUs(existing.NameScriptBytes).Trim().Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)
            && existing.ElementFlgs == recipe.Element && existing.AttackPower == command.AttackPower))
            throw new InvalidOperationException($"{recipe.Key} already exists; use the immutable pre-round table.");
        command.NameScriptBytes = EncodeUs(recipe.Name);
        command.DescriptionScriptBytes = EncodeUs(recipe.Description);
        command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
        command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
        command.NameScriptId = command.DescriptionScriptId = 0;
        command.JapaneseOnlyText1ScriptId = command.JapaneseOnlyText2ScriptId = 0;
        command.ElementFlgs = recipe.Element;
        command.Anim1Id = animation1;
        command.Anim2Id = animation2;
        // The authored AI owns the gauge reset. MP/OD costs for player menu actions
        // must not prevent the monster's counter/finisher from resolving.
        command.CostMp = 0;
        command.CostOverdrive = 0;
        byte[] row = BuildNewRow(command, original.SecondFile, out byte[] text);
        byte[] bytes = EntryListFile.Pack(MonMagicEntrySize, checked((short)(count + 1)),
            original.FirstFile.Concat(row).ToArray(), text);
        monsterCommands.AsSpan(0, 10).CopyTo(bytes);
        var reread = Ability_Command.ReadList(bytes, hasExtraInfo: false);
        EntryListFile packed = EntryListFile.Unpack(bytes);
        byte[] rewrite = Ability_Command.WriteList(reread, hasExtraInfo: false);
        bool rowsPreserved = packed.FirstFile.AsSpan(0, original.FirstFile.Length).SequenceEqual(original.FirstFile);
        bool textPreserved = packed.SecondFile.AsSpan(0, original.SecondFile.Length).SequenceEqual(original.SecondFile);
        if (reread.Count != count + 1 || !rowsPreserved || !textPreserved || !rewrite.AsSpan().SequenceEqual(bytes))
        {
            int firstDifference = Enumerable.Range(0, Math.Min(rewrite.Length, bytes.Length))
                .FirstOrDefault(index => rewrite[index] != bytes[index], -1);
            throw new InvalidDataException($"{recipe.Key}: readback count={reread.Count}/{count + 1}; rows={rowsPreserved}; text={textPreserved}; rewrite={rewrite.Length}/{bytes.Length}; first difference=0x{firstDifference:X}.");
        }
        return new(bytes, count, checked((ushort)(0x6000 | count)));
    }
}
