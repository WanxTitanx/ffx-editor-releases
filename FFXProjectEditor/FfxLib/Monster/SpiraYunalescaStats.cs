using System;
using System.IO;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Monster;

public static class SpiraYunalescaStats
{
    public const int MonsterId = 130, Tier = 7;
    public static byte[] Apply(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var monster = Monster_File.Read(source);
        if (monster.StatSheetFile?.MonsterId != MonsterId || !monster.Write().AsSpan().SequenceEqual(source))
            throw new InvalidDataException("Expected a preserve-readable Yunalesca m130.");
        var s = monster.StatSheetFile;
        s.Hp = SpiraNativeBossStats.ScaleHp(s.Hp, Tier);
        s.HpOverkill = SpiraNativeBossStats.ScaleHp(s.HpOverkill, Tier);
        s.Strength = SpiraNativeBossStats.ScaleAttribute(s.Strength, Tier);
        s.Defense = SpiraNativeBossStats.ScaleAttribute(s.Defense, Tier);
        s.Magic = SpiraNativeBossStats.ScaleAttribute(s.Magic, Tier);
        s.MagicDefense = SpiraNativeBossStats.ScaleAttribute(s.MagicDefense, Tier);
        s.Agility = SpiraNativeBossStats.ScaleAttribute(s.Agility, Tier);
        s.Luck = SpiraNativeBossStats.ScaleAttribute(s.Luck, Tier);
        s.Evasion = SpiraNativeBossStats.ScaleAttribute(s.Evasion, Tier);
        s.Accuracy = SpiraNativeBossStats.ScaleAttribute(s.Accuracy, Tier);
        var e = s.ElementalWeakness;
        e.Absorb &= ~(Element_Flags.Dark | Element_Flags.Holy);
        e.Resist &= ~(Element_Flags.Dark | Element_Flags.Holy);
        e.Weak = (e.Weak & ~Element_Flags.Dark) | Element_Flags.Holy;
        e.Immune = (e.Immune & ~Element_Flags.Holy) | Element_Flags.Dark;
        if (monster.LootFile is not { } loot) throw new InvalidDataException("Missing Yunalesca rewards.");
        loot.Ap = Math.Max(loot.Ap, (ushort)60000);
        loot.ApOverkill = ushort.MaxValue;
        loot.Gil = Math.Max(loot.Gil, (ushort)60000);
        byte[] result = monster.Write();
        if (result.Length != source.Length || !Monster_File.Read(result).Write().AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Yunalesca stats/rewards failed preserve-write checks.");
        return result;
    }
}
