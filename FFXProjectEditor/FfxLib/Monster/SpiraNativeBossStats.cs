using System;
using System.IO;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Monster;

/// <summary>One-time package authoring using the integer S.I.N. scaling curve.
/// This writes resources; it neither enables nor depends on the runtime SIN toggle.</summary>
public static class SpiraNativeBossStats
{
    public static int TierFor(int monsterId) => monsterId switch
    {
        124 => 4, 212 => 3, 119 => 4, 126 => 5, 142 => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(monsterId), "Not a boss in this round."),
    };

    public static uint ScaleHp(uint value, int tier)
    {
        ValidateTier(tier);
        if (value > int.MaxValue) throw new InvalidDataException("HP exceeds the supported signed actor domain.");
        return (uint)Math.Min((ulong)value * (uint)(100 + 10 * tier) / 100, int.MaxValue);
    }

    public static byte ScaleAttribute(byte value, int tier)
    {
        ValidateTier(tier);
        return (byte)Math.Min(value * (100 + 5 * tier) / 100 + tier, 255);
    }

    public static byte[] Apply(byte[] source, int monsterId)
    {
        ArgumentNullException.ThrowIfNull(source);
        int tier = TierFor(monsterId);
        if (source.Length < 0x34) throw new InvalidDataException("Truncated monster header.");
        int start = BitConverter.ToInt32(source, 0x0C), end = source.Length;
        foreach (int offset in new[] { 0x10, 0x14, 0x18, 0x1C })
        {
            int next = BitConverter.ToInt32(source, offset);
            if (next > start && next < end) end = next;
        }
        if (start < 0x30 || end <= start || end > source.Length)
            throw new InvalidDataException("Invalid StatSheet bounds.");
        byte[] section = source.AsSpan(start, end - start).ToArray();
        Monster_StatSheet stats = Monster_StatSheet.ReadSingle(section);
        if (stats.MonsterId != monsterId)
            throw new InvalidDataException($"Expected monster m{monsterId:D3}; found {stats.MonsterId}.");
        if (!section.AsSpan().SequenceEqual(stats.WriteSingle()))
            throw new InvalidDataException("The unmodified StatSheet does not round-trip.");
        stats.Hp = ScaleHp(stats.Hp, tier);
        stats.HpOverkill = ScaleHp(stats.HpOverkill, tier);
        stats.Strength = ScaleAttribute(stats.Strength, tier);
        stats.Defense = ScaleAttribute(stats.Defense, tier);
        stats.Magic = ScaleAttribute(stats.Magic, tier);
        stats.MagicDefense = ScaleAttribute(stats.MagicDefense, tier);
        stats.Agility = ScaleAttribute(stats.Agility, tier);
        stats.Luck = ScaleAttribute(stats.Luck, tier);
        stats.Evasion = ScaleAttribute(stats.Evasion, tier);
        stats.Accuracy = ScaleAttribute(stats.Accuracy, tier);
        if (monsterId is 124 or 126 or 142)
        {
            SetAffinity(stats, Element_Flags.Dark, absorb: true);
            if (monsterId is 126 or 142) SetAffinity(stats, Element_Flags.Holy, absorb: false);
        }
        if (monsterId == 119)
        {
            SetAffinity(stats, Element_Flags.Holy, absorb: true);
            SetAffinity(stats, Element_Flags.Dark, absorb: false);
        }
        byte[] edited = stats.WriteSingle();
        if (edited.Length != section.Length) throw new InvalidDataException("StatSheet changed size.");
        byte[] result = (byte[])source.Clone();
        edited.CopyTo(result, start);
        return result;
    }

    static void SetAffinity(Monster_StatSheet stats, Element_Flags element, bool absorb)
    {
        var affinity = stats.ElementalWeakness;
        affinity.Absorb &= ~element;
        affinity.Immune &= ~element;
        affinity.Resist &= ~element;
        affinity.Weak &= ~element;
        if (absorb) affinity.Absorb |= element;
        else affinity.Weak |= element;
    }

    static void ValidateTier(int tier)
    {
        if (tier < 1 || tier > 10) throw new ArgumentOutOfRangeException(nameof(tier));
    }
}
