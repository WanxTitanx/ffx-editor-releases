using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Ps3;

namespace FFXProjectEditor.FfxLib.Ability;

/// <summary>Change animation pairs only; opaque rows, text and gameplay survive exactly.</summary>
public static class SpiraSpellVisualRepairWriter
{
    public static byte[] RepairPlayer(byte[] source) => Repair(source, true,
        [(63, 100, Ability_Command.ElementFlags.Holy, 146, 146, HolyActorFallbackAdapter.OwnedMagicId, HolyActorFallbackAdapter.OwnedMagicId),
         (367, 12, Ability_Command.ElementFlags.Wind, 866, 0, 866, 898),
         (368, 24, Ability_Command.ElementFlags.Wind, 867, 0, 867, 899),
         (369, 42, Ability_Command.ElementFlags.Wind, 868, 0, 868, 900)]);

    public static byte[] RepairMonsters(byte[] source) => Repair(source, false,
        [(341, 60, Ability_Command.ElementFlags.Wind, 869, 0, 869, 901),
         (356, 12, Ability_Command.ElementFlags.Dark, 897, 0, 897, 897)]);

    static byte[] Repair(byte[] source, bool extra,
        (int Row, byte Power, Ability_Command.ElementFlags Element, short Old1, short Old2, short New1, short New2)[] changes)
    {
        ArgumentNullException.ThrowIfNull(source);
        int size = extra ? 0x60 : 0x5C;
        if (source.Length < 20 || source[0] != 1 || source.AsSpan(1, 7).ToArray().Any(b => b != 0)
            || BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(8)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(12)) != size
            || BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(16)) != 20)
            throw new InvalidDataException("Unsupported command table header.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(10)) + 1;
        if (count <= changes.Max(c => c.Row) || 20 + count * size >= source.Length
            || BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(14)) != count * size)
            throw new InvalidDataException("Missing rows, invalid bounds or text pool.");
        var split = EntryListFile.Unpack(source);
        if (split.FirstFile.Length != count * size) throw new InvalidDataException("Table extent changed.");
        var rows = Ability_Command.ReadList(source, extra);
        byte[] result = (byte[])source.Clone();
        var mutable = new HashSet<int>();
        foreach (var c in changes)
        {
            var row = rows[c.Row];
            bool old = row.Anim1Id == c.Old1 && row.Anim2Id == c.Old2;
            bool done = row.Anim1Id == c.New1 && row.Anim2Id == c.New2;
            if ((!old && !done) || row.AttackPower != c.Power || row.ElementFlgs != c.Element
                || row.DamageFormula != DamageFormula_Enum.Magic || row.HitCount != 1
                || !row.FlagDamageMagical || !row.FlagDamageTypeHp)
                throw new InvalidDataException($"Spell row {c.Row} no longer matches the reviewed recipe.");
            int offset = 20 + c.Row * size + 16;
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset), c.New1);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(offset + 2), c.New2);
            for (int i = 0; i < 4; i++) mutable.Add(offset + i);
        }
        for (int i = 0; i < source.Length; i++)
            if (!mutable.Contains(i) && source[i] != result[i]) throw new InvalidDataException("Unowned bytes changed.");
        var reread = Ability_Command.ReadList(result, extra);
        foreach (var c in changes)
            if (reread[c.Row].Anim1Id != c.New1 || reread[c.Row].Anim2Id != c.New2)
                throw new InvalidDataException("Animation readback failed.");
        return result;
    }
}
