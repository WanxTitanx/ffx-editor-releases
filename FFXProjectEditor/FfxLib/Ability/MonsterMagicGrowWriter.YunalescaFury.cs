using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record YunalescaFuryTable(byte[] Bytes, IReadOnlyList<ushort> FuryOperands);

public static partial class MonsterMagicGrowWriter
{
    // Fira/Thundara/Watera/Blizzara Fury. Their native effects consume HitCount.
    public static IReadOnlyList<int> YunalescaFuryDonors { get; } = Array.AsReadOnly(new[] { 124, 126, 127, 125 });

    static void ConfigureYunalescaFury(Ability_Command command, Ability_Command donor, short effect, byte hits)
    {
        if (donor.HitCount != 16 || !donor.FlagMisc2RandomTargets || donor.TargetFlgs != (Ability_Command.TargetFlags)0x87
            || donor.DamageFormula != DamageFormula_Enum.Magic || hits is < 3 or > 6 || effect <= 0)
            throw new InvalidDataException("The native Fury donor/impact contract changed.");
        command.Anim1Id = effect; command.Anim2Id = 0;
        command.TargetFlgs = donor.TargetFlgs; command.TargetsAllowed = donor.TargetsAllowed;
        command.Misc1Flgs = donor.Misc1Flgs;
        command.Misc2Flgs = donor.Misc2Flgs & ~Ability_Command.Misc2Flags.MenuLeft;
        command.Misc3Flgs = donor.Misc3Flgs; command.Misc4Flgs = donor.Misc4Flgs;
        command.HitCount = hits; command.CasterAnimId = donor.CasterAnimId;
        command.AttackPower = 18; command.DamageFormula = DamageFormula_Enum.Magic;
        command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
        command.DamageFlgs = Ability_Command.DamageFlags.Magical;
        command.ElementFlgs = Ability_Command.ElementFlags.Dark;
        command.CostMp = command.CostOverdrive = command.OverdriveCategory = 0;
    }

    /// <summary>Upgrade the deployed 373-row packet without rescaling the boss or
    /// changing unrelated rows. Add twelve variants to represent 3..6 hits in a
    /// single native command, avoiding the four-command packet limit.</summary>
    public static YunalescaFuryTable RepairYunalescaFury(byte[] source, byte[] characters,
        IReadOnlyList<short> furyEffects)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(characters);
        var before = EntryListFile.Unpack(source);
        if (before.Header.EntrySize != MonMagicEntrySize || before.Header.RealEntryCount != 373
            || before.Header.EntryTableFileOffset != 20 || before.Header.PreviousFileCount != 0
            || furyEffects.Count != 4 || furyEffects.Distinct().Count() != 4)
            throw new InvalidDataException("Expected the original 373-row Yunalesca packet and four owned effects.");
        var rows = Ability_Command.ReadList(source, false);
        var donors = Ability_Command.ReadList(characters, true);
        if (donors.Count <= YunalescaFuryDonors.Max()) throw new InvalidDataException("Missing native Fury donors.");
        for (int spell = 0; spell < 4; spell++)
        {
            var row = rows[365 + spell];
            if (row.AttackPower != 18 || row.ElementFlgs != Ability_Command.ElementFlags.Dark
                || !DecodeUs(row.NameScriptBytes).Contains("Fury", StringComparison.Ordinal))
                throw new InvalidDataException("Yunalesca Fury base row changed.");
            ConfigureYunalescaFury(row, donors[YunalescaFuryDonors[spell]], furyEffects[spell], 3);
        }
        foreach (int id in new[] { 369, 371 })
        {
            var row = rows[id];
            if (row.DamageFormula is not (DamageFormula_Enum.TargetMaxHp or DamageFormula_Enum.WielderMaxHp)
                || !row.FlagDamageHeals || row.ElementFlgs != 0)
                throw new InvalidDataException("Yunalesca support heal changed.");
            row.DamageFormula = DamageFormula_Enum.WielderMaxHp;
            row.AttackPower = (byte)(id == 369 ? 1 : 2);
            row.StatusDuration.Regen = 254;
        }
        foreach (int id in new[] { 370, 371 })
        {
            // Steam 25501027 VA 0x78B2E6: Dispel cannot remove duration 255.
            var duration = rows[id].StatusDuration;
            duration.Protect = duration.Shell = 254;
            duration.NulBlaze = duration.NulFrost = duration.NulShock = duration.NulTide = 254;
        }
        rows[371].StatusDuration.Haste = 254;
        byte[] repaired = Ability_Command.WriteList(rows, false);
        var middle = EntryListFile.Unpack(repaired);
        if (!middle.SecondFile.AsSpan().SequenceEqual(before.SecondFile))
            throw new InvalidDataException("Repair moved the original text pool.");
        var ownedRows = new HashSet<int> { 365, 366, 367, 368, 369, 370, 371 };
        for (int i = 0; i < 373; i++)
            if (!ownedRows.Contains(i) && !before.FirstFile.AsSpan(i * MonMagicEntrySize, MonMagicEntrySize)
                .SequenceEqual(middle.FirstFile.AsSpan(i * MonMagicEntrySize, MonMagicEntrySize)))
                throw new InvalidDataException("Repair changed an unowned ability.");
        var operands = new List<ushort>();
        for (int spell = 0; spell < 4; spell++)
        {
            operands.Add((ushort)(0x6000 | (365 + spell)));
            for (byte hits = 4; hits <= 6; hits++)
            {
                var packed = EntryListFile.Unpack(repaired);
                var variant = CloneCommand(rows[365 + spell]); variant.HitCount = hits;
                // Same display name/visual, different native hit count.
                byte[] row = BuildNewRow(variant, packed.SecondFile, out byte[] pool);
                operands.Add((ushort)(0x6000 | packed.Header.RealEntryCount));
                repaired = EntryListFile.Pack(MonMagicEntrySize, (short)(packed.Header.RealEntryCount + 1),
                    packed.FirstFile.Concat(row).ToArray(), pool);
                source.AsSpan(0, 10).CopyTo(repaired);
            }
        }
        var final = EntryListFile.Unpack(repaired);
        if (final.Header.RealEntryCount != 385 || !final.SecondFile.AsSpan(0, before.SecondFile.Length).SequenceEqual(before.SecondFile)
            || !Ability_Command.WriteList(Ability_Command.ReadList(repaired, false), false).AsSpan().SequenceEqual(repaired))
            throw new InvalidDataException("Fury variant append failed preservation/readback.");
        return new(repaired, operands);
    }
}
