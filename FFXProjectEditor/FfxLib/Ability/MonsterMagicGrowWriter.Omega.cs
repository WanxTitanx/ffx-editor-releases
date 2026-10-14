using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public sealed record OmegaSpellSpec(string Key, string Name, int Table, int Donor, string Kind);
public sealed record OmegaSpellTable(byte[] Bytes, IReadOnlyDictionary<string, ushort> Commands);

public static partial class MonsterMagicGrowWriter
{
    public static IReadOnlyList<OmegaSpellSpec> OmegaSpells { get; } = Array.AsReadOnly(new[]
    {
        new OmegaSpellSpec("darkja","Omega Darkja",2,372,"attack"),
        new OmegaSpellSpec("holy","Omega Holy",0,63,"attack"),
        new OmegaSpellSpec("aeroja","Omega Aeroja",2,341,"attack"),
        new OmegaSpellSpec("firaja","Omega Firaja",0,73,"ja"),
        new OmegaSpellSpec("blizzaja","Omega Blizzaja",0,74,"ja"),
        new OmegaSpellSpec("thundaja","Omega Thundaja",0,75,"ja"),
        new OmegaSpellSpec("waterja","Omega Waterja",0,76,"ja"),
        new OmegaSpellSpec("anti_flare","Omega Anti-Flare",2,349,"attack"),
        new OmegaSpellSpec("meteor","Omega Meteor",2,292,"attack"),
        new OmegaSpellSpec("ultima","Omega Ultima",0,83,"attack"),
        new OmegaSpellSpec("bio","Omega Bio",0,77,"poison"),
        new OmegaSpellSpec("silence","Omega Silence",2,357,"silence"),
        new OmegaSpellSpec("slowga","Omega Slowga",0,57,"slow"),
        new OmegaSpellSpec("darkness","Omega Darkness",2,359,"darkness"),
        new OmegaSpellSpec("zombie","Omega Zombie",2,362,"zombie"),
        new OmegaSpellSpec("mental_break","Omega Mental Break",0,19,"mental"),
        new OmegaSpellSpec("armor_break","Omega Armor Break",0,18,"armor"),
        new OmegaSpellSpec("reflect","Omega Reflect",0,60,"reflect"),
        new OmegaSpellSpec("mighty_guard","Omega Mighty Guard",2,451,"guard"),
        new OmegaSpellSpec("regen","Omega Regen",0,62,"regen"),
        new OmegaSpellSpec("hastega","Omega Hastega",0,55,"haste"),
        new OmegaSpellSpec("focus","Omega Focus",0,28,"focus"),
        new OmegaSpellSpec("cheer","Omega Cheer",0,26,"cheer"),
        new OmegaSpellSpec("luck","Omega Luck",0,30,"luck"),
        new OmegaSpellSpec("flare","Omega Flare",0,82,"attack"),
        new OmegaSpellSpec("holy_flare","Omega Holy Flare",2,447,"attack"),
        new OmegaSpellSpec("od_varuna","Arcane Collapse",0,82,"od_varuna"),
        new OmegaSpellSpec("od_tonberry","Final Reckoning",1,96,"od_tonberry"),
        new OmegaSpellSpec("od_ultima","Fragment of the End",1,35,"od_ultima"),
    });

    public static OmegaSpellTable AppendOmega(byte[] source, byte[] characters, byte[] monsterOne,
        IReadOnlyList<(short First, short Second)> effects)
    {
        var original = EntryListFile.Unpack(source);
        var existing = Ability_Command.ReadList(source, false);
        var player = Ability_Command.ReadList(characters, true);
        var monsters = Ability_Command.ReadList(monsterOne, false);
        if (existing.Count != 452 || original.Header.EntrySize != MonMagicEntrySize || effects.Count != OmegaSpells.Count)
            throw new InvalidDataException("Omega requires the exact 452-row post-Ultima baseline and all effect pairs.");
        byte[] data = original.FirstFile.ToArray(), text = original.SecondFile.ToArray();
        var commands = new Dictionary<string, ushort>();
        for (int i=0;i<OmegaSpells.Count;i++)
        {
            var spec=OmegaSpells[i];var donor=(spec.Table==0?player:spec.Table==1?monsters:existing)[spec.Donor];
            var c=CloneCommand(donor);
            c.NameScriptBytes=EncodeUs(spec.Name);c.DescriptionScriptBytes=EncodeUs(Description(spec));
            c.JapaneseOnlyText1ScriptBytes=c.JapaneseOnlyText2ScriptBytes=Array.Empty<byte>();
            c.NameScriptId=c.DescriptionScriptId=c.JapaneseOnlyText1ScriptId=c.JapaneseOnlyText2ScriptId=0;
            c.CharacterUser=Character_Enum.None;c.CostMp=c.CostOverdrive=c.OverdriveCategory=0;
            c.SubMenuCategorization=c.SubSubMenuCategorization=0;
            c.Anim1Id=effects[i].First;c.Anim2Id=effects[i].Second;
            // Cloned effects retain the donor's native caster protocol. Forcing a
            // spell cast here strands weapon Break effects before their impact.
            c.CasterAnimId=donor.CasterAnimId;
            c.StatusChance=new();c.StatusDuration=new();c.StatusFlgs=0;c.StatBuffFlgs=0;c.StatBuffValue=0;c.SpecialBuffFlgs=0;
            c.ShatterChance=0;c.StealGil=false;c.AttackCritBonus=0;c.PreviewFlgs=0;
            c.Misc1Flgs=Ability_Command.Misc1Flags.UseInCombat|Ability_Command.Misc1Flags.DisplayMoveName;
            c.Misc2Flgs=0;c.Misc3Flgs=Ability_Command.Misc3Flags.AffectedBySilence;c.Misc4Flgs=0;
            c.MoveRank=3;c.HitCount=1;c.DamageFormula=DamageFormula_Enum.Magic;
            c.DamageTypeFlgs=Ability_Command.DamageTypeFlags.Hp;c.DamageFlgs=Ability_Command.DamageFlags.Magical;
            c.TargetFlgs=(Ability_Command.TargetFlags)(donor.FlagTargetMulti?0x87:0xB3);
            bool support=spec.Kind is "reflect" or "guard" or "regen" or "haste" or "focus" or "cheer" or "luck";
            if(support)
            {
                c.AttackPower=0;c.ElementFlgs=0;c.TargetFlgs=(Ability_Command.TargetFlags)(spec.Kind is "guard" or "haste"?0xB5:0xB1);
                // Positive Omega support must reach allies even if they have Reflect.
                // Duration 254 preserves native Dispel; never use auto-status flags.
                switch(spec.Kind)
                {
                    case "reflect":c.StatusChance.Reflect=255;c.StatusDuration.Reflect=254;break;
                    case "guard":c.StatusChance.Protect=c.StatusChance.Shell=255;c.StatusDuration.Protect=c.StatusDuration.Shell=254;break;
                    case "regen":c.StatusChance.Regen=255;c.StatusDuration.Regen=254;break;
                    case "haste":c.StatusChance.Haste=255;c.StatusDuration.Haste=254;break;
                    case "focus":c.StatBuffFlgs=Ability_Command.StatBuffFlags.Focus;c.StatBuffValue=1;c.Misc3Flgs=0;break;
                    case "cheer":c.StatBuffFlgs=Ability_Command.StatBuffFlags.Cheer;c.StatBuffValue=1;c.Misc3Flgs=0;break;
                    case "luck":c.StatBuffFlgs=Ability_Command.StatBuffFlags.Luck;c.StatBuffValue=1;c.Misc3Flgs=0;break;
                }
            }
            else
            {
                c.FlagMisc1AffectedByReflect=true;
                switch(spec.Kind)
                {
                    case "ja":c.AttackPower=60;break;
                    case "poison":c.AttackPower=0;c.ElementFlgs=0;c.StatusChance.Poison=100;break;
                    case "silence":c.AttackPower=0;c.ElementFlgs=0;c.StatusChance.Silence=100;c.StatusDuration.Silence=3;break;
                    case "slow":c.AttackPower=0;c.ElementFlgs=0;c.StatusChance.Slow=100;c.StatusDuration.Slow=254;c.TargetFlgs=(Ability_Command.TargetFlags)0x87;break;
                    case "darkness":c.AttackPower=0;c.ElementFlgs=0;c.StatusChance.Darkness=100;c.StatusDuration.Darkness=3;break;
                    case "zombie":c.AttackPower=0;c.ElementFlgs=0;c.StatusChance.Zombie=100;break;
                    case "mental":c.AttackPower=20;c.ElementFlgs=0;c.StatusChance.BreakMental=100;break;
                    case "armor":c.AttackPower=20;c.ElementFlgs=0;c.StatusChance.BreakArmor=100;break;
                    case "od_varuna":
                        c.AttackPower=24;c.ElementFlgs=0;c.TargetFlgs=(Ability_Command.TargetFlags)0x87;
                        c.HitCount=4;c.TargetsAllowed=player[126].TargetsAllowed;c.FlagMisc2RandomTargets=true;c.StatusChance.BreakMental=50;break;
                    case "od_tonberry":
                        c.AttackPower=1;c.ElementFlgs=0;c.TargetFlgs=(Ability_Command.TargetFlags)0x87;
                        c.DamageFormula=DamageFormula_Enum.IgnoreDefense;c.DamageFlgs=Ability_Command.DamageFlags.Physical;
                        c.FlagMisc2DelayS=true;c.Misc3Flgs=0;break;
                    case "od_ultima":
                        c.AttackPower=42;c.ElementFlgs=0;c.TargetFlgs=(Ability_Command.TargetFlags)0x87;
                        c.StatusChance.Confuse=c.StatusChance.Silence=50;c.StatusDuration.Silence=3;break;
                }
                if(spec.Kind.StartsWith("od_",StringComparison.Ordinal))
                {
                    c.FlagMisc1AffectedByReflect=false;c.FlagMisc4EmptyOverdrive=true;c.Misc3Flgs=0;
                }
            }
            ApplyOmegaNativeProtocol(c, spec, donor, player[60]);
            byte[] record=BuildNewRow(c,text,out byte[] nextText);text=nextText;data=data.Concat(record).ToArray();
            commands.Add(spec.Key,checked((ushort)(0x6000|(452+i))));
        }
        byte[] result=EntryListFile.Pack(MonMagicEntrySize,checked((short)(452+OmegaSpells.Count)),data,text);source.AsSpan(0,10).CopyTo(result);
        var packed=EntryListFile.Unpack(result);var reread=Ability_Command.ReadList(result,false);
        if(!packed.FirstFile.AsSpan(0,original.FirstFile.Length).SequenceEqual(original.FirstFile)
            ||!packed.SecondFile.AsSpan(0,original.SecondFile.Length).SequenceEqual(original.SecondFile)
            ||reread.Count!=481||!Ability_Command.WriteList(reread,false).AsSpan().SequenceEqual(result))
            throw new InvalidDataException("Omega append violated old rows, text or round-trip contract.");
        return new(result,commands);
    }

    // A zero-power HP spell still enters the damage pipeline. Native positive
    // statuses and CTB spells have different protocols, and weapon Break effects
    // require physical damage to match caster24. Keep those protocols together;
    // targets, probabilities, durations, theme and intended power stay authored.
    internal static void ApplyOmegaNativeProtocol(Ability_Command command, OmegaSpellSpec spec,
        Ability_Command donor, Ability_Command noDamageMagic)
    {
        bool support = spec.Kind is "reflect" or "guard" or "regen" or "haste" or "focus" or "cheer" or "luck";
        bool statusOnly = spec.Kind is "poison" or "silence" or "slow" or "darkness" or "zombie";
        bool weaponBreak = spec.Kind is "mental" or "armor";
        if (!support && !statusOnly && !weaponBreak) return;
        // The custom monster guard/status donors were authored from the same
        // generic HP template. Use the original player Reflect damage protocol
        // for these pure spells rather than inheriting that template again.
        var protocol = spec.Table == 0 ? donor : noDamageMagic;
        command.DamageFormula = protocol.DamageFormula;
        command.DamageTypeFlgs = protocol.DamageTypeFlgs;
        command.DamageFlgs = protocol.DamageFlgs;
        if (weaponBreak)
        {
            command.Misc3Flgs = donor.Misc3Flgs;
            command.FlagMisc1AffectedByReflect = donor.FlagMisc1AffectedByReflect;
        }
    }
    static string Description(OmegaSpellSpec s)=>s.Kind switch
    {
        "guard"=>"Grants Protect and Shell to all allied monsters. Removable by Dispel.",
        "haste"=>"Grants Haste to all allied monsters. Removable by Dispel.",
        "od_varuna"=>"Four random non-elemental magic hits. Each may inflict Mental Break.",
        "od_tonberry"=>"Physical damage to the party, ignoring numerical Defense, with native Delay.",
        "od_ultima"=>"Non-elemental magic to the party. May cause Confuse and Silence.",
        _=>"A power shaped by the currents of Omega Ruins.",
    };

    public static byte[] PromoteUltimaGuardToGroup(byte[] source)
    {
        var before=EntryListFile.Unpack(source);var rows=Ability_Command.ReadList(source,false);
        if(rows.Count!=452||rows[451].AttackPower!=0||rows[451].StatusChance.Protect!=255||rows[451].StatusChance.Shell!=255)
            throw new InvalidDataException("Unexpected Ultima Mighty Guard baseline.");
        byte[] description=EncodeUs("Grants Protect and Shell to all allied monsters.");
        if(rows[451].TargetFlgs==(Ability_Command.TargetFlags)0xB5&&rows[451].DescriptionScriptBytes.SequenceEqual(description))return source.ToArray();
        var guard=CloneCommand(rows[451]);guard.TargetFlgs=(Ability_Command.TargetFlags)0xB5;guard.DescriptionScriptBytes=description;
        byte[] row=BuildNewRow(guard,before.SecondFile,out byte[] text),data=before.FirstFile.ToArray();
        row.CopyTo(data,451*MonMagicEntrySize);
        byte[] output=EntryListFile.Pack(MonMagicEntrySize,452,data,text);source.AsSpan(0,10).CopyTo(output);
        var after=EntryListFile.Unpack(output);
        if(!after.FirstFile.AsSpan(0,451*MonMagicEntrySize).SequenceEqual(before.FirstFile.AsSpan(0,451*MonMagicEntrySize))
            ||!after.SecondFile.AsSpan(0,before.SecondFile.Length).SequenceEqual(before.SecondFile))
            throw new InvalidDataException("Group-guard edit changed other command records or old text.");
        return output;
    }
}
