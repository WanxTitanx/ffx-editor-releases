using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record SinAirshipTable(byte[] Bytes, SinAirshipCommands Commands);
public static partial class MonsterMagicGrowWriter
{
    public static SinAirshipTable AppendSinAirshipSpells(byte[] source, byte[]? englishReference = null)
    {
        var old = EntryListFile.Unpack(source); var rows = Ability_Command.ReadList(source, false);
        var reference = EntryListFile.Unpack(englishReference ?? source);
        var referenceRows = Ability_Command.ReadList(englishReference ?? source, false);
        if (old.Header.EntrySize != MonMagicEntrySize || old.Header.EntryTableFileOffset != 20
            || old.Header.PreviousFileCount != 0 || rows.Count != 408)
            throw new InvalidDataException("Expected the 408-row Evrae stage.");
        byte[] result = source;
        var arms = new List<(ushort, ushort)>(); var t5 = new List<(ushort, ushort)>(); var t6 = new List<(ushort, ushort)>();
        foreach (var recipe in SpiraSinAirshipWriter.Pool)
        {
            var c = CloneCommand(rows[recipe.Original & 0xFFF]);
            int donor = recipe.Original & 0xFFF;
            // Keep translated names/descriptions. Identity comes from the EN
            // binding plus identical command payload, never decoding JP as EN.
            if (referenceRows.Count <= donor || DecodeUs(referenceRows[donor].NameScriptBytes).Trim() != recipe.Name
                || !old.FirstFile.AsSpan(donor * MonMagicEntrySize + 16, 5)
                    .SequenceEqual(reference.FirstFile.AsSpan(donor * MonMagicEntrySize + 16, 5))
                // Legacy jppc Ward Stack uses caster 33 instead of 3. This
                // explicitly owned field is normalized below for every locale.
                || !old.FirstFile.AsSpan(donor * MonMagicEntrySize + 22, MonMagicEntrySize - 22)
                    .SequenceEqual(reference.FirstFile.AsSpan(donor * MonMagicEntrySize + 22, MonMagicEntrySize - 22))
                || c.FlagDamagePhysical
                || c.StatusChance.Death != 0 || c.StatusChance.Petrify != 0 || c.Anim1Id <= 0)
                throw new InvalidDataException("The selected magical SIN donor changed: " + recipe.Name);
            // Both arm models cast Gravija with caster animation 3; Genais uses
            // ordinary Waterga. Keep the donor effect/damage but use that motion.
            c.CasterAnimId = 3; c.CostMp = c.CostOverdrive = c.OverdriveCategory = 0;
            c.NameScriptId = c.DescriptionScriptId = c.JapaneseOnlyText1ScriptId = c.JapaneseOnlyText2ScriptId = 0;
            var d = c.StatusDuration;
            if (d.Protect == 255) d.Protect = 254;
            if (d.Shell == 255) d.Shell = 254;
            if (d.Regen == 255) d.Regen = 254;
            if (d.Haste == 255) d.Haste = 254;
            if (d.Reflect == 255) d.Reflect = 254;
            if (d.NulBlaze == 255) d.NulBlaze = 254;
            if (d.NulFrost == 255) d.NulFrost = 254;
            if (d.NulShock == 255) d.NulShock = 254;
            if (d.NulTide == 255) d.NulTide = 254;
            var before = EntryListFile.Unpack(result);
            byte[] row = BuildNewRow(c, before.SecondFile, out byte[] text);
            ushort command = checked((ushort)(0x6000 | before.Header.RealEntryCount));
            result = EntryListFile.Pack(MonMagicEntrySize, checked((short)(before.Header.RealEntryCount + 1)),
                before.FirstFile.Concat(row).ToArray(), text);
            source.AsSpan(0, 10).CopyTo(result);
            (recipe.Tier <= 4 ? arms : recipe.Tier == 5 ? t5 : t6).Add((command, recipe.Target));
        }
        var final = EntryListFile.Unpack(result);
        if (final.Header.RealEntryCount != 435 || !final.FirstFile.AsSpan(0, old.FirstFile.Length).SequenceEqual(old.FirstFile)
            || !final.SecondFile.AsSpan(0, old.SecondFile.Length).SequenceEqual(old.SecondFile)
            || !Ability_Command.WriteList(Ability_Command.ReadList(result, false), false).AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Sin spell copies failed preservation/round-trip validation.");
        return new(result, new(arms, t5, t6));
    }
}
