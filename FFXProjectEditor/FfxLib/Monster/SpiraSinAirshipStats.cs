using System;
using System.IO;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Monster;

public static class SpiraSinAirshipStats
{
    public static byte[] Apply(byte[] source, int id)
    {
        if (id is not (136 or 137 or 138 or 139)) throw new ArgumentOutOfRangeException(nameof(id));
        var monster = Monster_File.Read(source); var stats = monster.StatSheetFile;
        if (stats.MonsterId != id) throw new InvalidDataException("Unexpected Sin part.");
        const Element_Flags elements = Element_Flags.Holy | Element_Flags.Dark;
        if ((stats.ElementalWeakness.Absorb & elements) == elements)
            throw new InvalidOperationException("This native Sin part has already been patched.");
        int tier = id == 139 ? 7 : id is 136 or 137 ? 6 : 0;
        if (tier != 0)
        {
            stats.Hp = SpiraNativeBossStats.ScaleHp(stats.Hp, tier);
            stats.HpOverkill = SpiraNativeBossStats.ScaleHp(stats.HpOverkill, tier);
            stats.Strength = SpiraNativeBossStats.ScaleAttribute(stats.Strength, tier);
            stats.Defense = SpiraNativeBossStats.ScaleAttribute(stats.Defense, tier);
            stats.Magic = SpiraNativeBossStats.ScaleAttribute(stats.Magic, tier);
            stats.MagicDefense = SpiraNativeBossStats.ScaleAttribute(stats.MagicDefense, tier);
            stats.Agility = SpiraNativeBossStats.ScaleAttribute(stats.Agility, tier);
            stats.Luck = SpiraNativeBossStats.ScaleAttribute(stats.Luck, tier);
            stats.Evasion = SpiraNativeBossStats.ScaleAttribute(stats.Evasion, tier);
            stats.Accuracy = SpiraNativeBossStats.ScaleAttribute(stats.Accuracy, tier);
        }
        stats.ElementalWeakness.Absorb |= elements;
        stats.ElementalWeakness.Immune &= ~elements;
        stats.ElementalWeakness.Resist &= ~elements;
        stats.ElementalWeakness.Weak &= ~elements;
        byte[] bytes = stats.WriteSingle();
        int offset = BitConverter.ToInt32(source, 12);
        byte[] original = Monster_File.Read(source).StatSheetFile.WriteSingle();
        if (bytes.Length != original.Length || !source.AsSpan(offset, original.Length).SequenceEqual(original))
            throw new InvalidDataException("Sin StatSheet did not round-trip.");
        byte[] result = (byte[])source.Clone(); bytes.CopyTo(result, offset); return result;
    }
}
