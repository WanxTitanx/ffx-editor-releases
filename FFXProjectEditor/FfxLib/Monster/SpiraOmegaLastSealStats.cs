using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Monster;

public static class SpiraOmegaLastSealStats
{
    public static byte[] ApplyBoss(byte[] source)
    {
        var m = ReadOmega(source);
        var s = m.StatSheetFile;
        s.Hp = 3_000_000; s.Mp = 999;
        s.Strength = 190; s.Defense = 160; s.Magic = 180; s.MagicDefense = 170;
        s.Agility = 140; s.Luck = 100; s.Evasion = 0; s.Accuracy = 220;
        ClearAutosAndExposeSensor(s);
        // The body keeps its normal Break immunity. The battle controller opens
        // Armor/Mental Break only during the four-action exposure window.
        s.StatusResistance.BreakArmor = s.StatusResistance.BreakMental = 255;
        s.SensorScriptBytes = Encode("Defeat the Custodians to break Omega's seals.");
        s.ScanScriptBytes = Encode("Sacred guards seal physical damage; profane guards seal magic. Break both seals to expose Omega. The Last Seal awakens at 25% HP.");
        s.OriginalSectionBytes = null!;
        return Finish(m);
    }

    public static byte[] CreateGuard(byte[] omega, int id)
    {
        if (id is < 911 or > 914) throw new ArgumentOutOfRangeException(nameof(id));
        var m = ReadOmega(omega);
        var s = m.StatSheetFile;
        bool sacred = id is 911 or 913, echo = id >= 913;
        s.MonsterId = checked((short)id); s.ModelId = 0x1063;
        s.Hp = echo ? 75_000u : 150_000u; s.Mp = 999; s.HpOverkill = 15_000;
        s.Strength = 110; s.Defense = 100; s.Magic = 110; s.MagicDefense = 110;
        s.Agility = 85; s.Luck = 30; s.Evasion = 0; s.Accuracy = 150;
        ClearAutosAndExposeSensor(s);
        s.ArenaId = -1; s.ForcedAction = 0;
        s.Prop_ImmunityFractionalDamage = true; s.Prop_ImmunityLife = true;
        s.Prop_ImmunityBribe_Maybe = true; s.Prop_ImmunitySlice_Maybe = true;
        s.Prop_ImmunityPhysicalDamage = s.Prop_ImmunityMagicDamage = s.Prop_ImmunityAllDamage = false;
        s.Immunity_Eject = true;
        s.StatusResistance.Death = s.StatusResistance.Petrify = 255;
        s.StatusResistance.Slow = s.StatusResistance.Silence = s.StatusResistance.Darkness = 0;
        s.StatusResistance.BreakPower = s.StatusResistance.BreakMagic = 0;
        s.StatusResistance.BreakArmor = s.StatusResistance.BreakMental = 0;
        s.ElementalWeakness.Absorb = sacred ? Element_Flags.Holy : Element_Flags.Dark;
        s.ElementalWeakness.Weak = sacred ? Element_Flags.Dark : Element_Flags.Holy;
        s.ElementalWeakness.Resist = s.ElementalWeakness.Immune = 0;
        s.Abilities = new ushort[16];
        ushort attack = sacred ? (ushort)0x61EA : (ushort)0x61EB;
        ushort spell = echo ? (sacred ? (ushort)0x61EC : (ushort)0x61ED)
            : (sacred ? (ushort)0x61DD : (ushort)0x61CB);
        s.Abilities[0] = attack; s.Abilities[1] = spell;
        if (!echo) s.Abilities[2] = sacred ? (ushort)0x61E7 : (ushort)0x61E8;
        s.NameScriptBytes = Encode((sacred ? "Sacred " : "Profane ") + (echo ? "Echo" : "Custodian"));
        s.SensorScriptBytes = Encode(sacred ? "Seals physical damage to Omega. Weak to Dark." : "Seals magical damage to Omega. Weak to Holy.");
        s.ScanScriptBytes = Encode(echo ? "A fragment of the Last Seal. Defeat it to weaken Supernova. It cannot be reconstructed."
            : "Break its seal by defeating it. Silence prevents magic and reconstruction. Slow and Breaks are effective.");
        s.JapaneseOnlyText1ScriptBytes = s.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
        s.NameScriptId = s.SensorScriptId = s.ScanScriptId = s.JapaneseOnlyText1ScriptId = s.JapaneseOnlyText2ScriptId = 0;
        s.OriginalSectionBytes = null!;
        // Zero every acquisition path, including otherwise-unused equipment
        // pools. Keep the native section size and trailing unknown bytes intact.
        byte[] loot = m.LootFile.WriteSingle();
        if (loot.Length < 278) throw new InvalidDataException("Short Omega reward section.");
        Array.Clear(loot, 0, 274);
        foreach (int offset in new[] { 6, 12, 14, 16, 18, 24, 26, 28, 30, 36, 38, 42 })
        { loot[offset] = 0xFF; loot[offset + 1] = 0xFF; }
        for (int offset = 50; offset < 274; offset++) loot[offset] = 0xFF;
        m.LootFile = Monster_Loot.ReadSingle(loot);
        return Finish(m);
    }

    static void ClearAutosAndExposeSensor(Monster_StatSheet s)
    {
        s.AutoStatus_Flags1 = 0; s.AutoStatus_Flags2 = 0; s.AutoStatus_Flags3 = 0;
        s.Prop_ImmunitySensor = s.Prop_ImmunityScanAgain_Maybe = s.Immunity_Scan = false;
    }

    static Monster_File ReadOmega(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var m = Monster_File.Read(source);
        if (m.StatSheetFile.MonsterId != 100 || m.StatSheetFile.ModelId != 0x1063 || !m.Write().SequenceEqual(source))
            throw new InvalidDataException("Expected round-trippable Omega m100 with its native model.");
        return m;
    }

    static byte[] Finish(Monster_File monster)
    {
        byte[] result = monster.Write();
        if (!Monster_File.Read(result).Write().SequenceEqual(result))
            throw new InvalidDataException("Last Seal monster did not round-trip.");
        return result;
    }

    static byte[] Encode(string value) => value.Select(c => FfxEncoding.UsEncoder.TryGetValue(c, out byte b)
        ? b : throw new InvalidDataException("Unencodable Last Seal monster text.")).ToArray();
}
