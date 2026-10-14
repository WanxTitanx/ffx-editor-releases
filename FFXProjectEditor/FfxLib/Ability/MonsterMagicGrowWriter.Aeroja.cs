using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability;

public static partial class MonsterMagicGrowWriter
{
    public const int AerojaRow = 341;
    public const int AerojaMonsterId = 334; // Intended recipient; AI routing is a separate contract.
    public const short AerojaMagicId = 869;
    public const short AerojaImpactMagicId = 901;

    public static MonsterMagicGrowResult AppendAeroja(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length<20 || source[0]!=1 || source.Skip(1).Take(7).Any(b=>b!=0) ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(8))!=0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(10))!=AerojaRow-1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(12))!=MonMagicEntrySize ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(14))!=AerojaRow*MonMagicEntrySize ||
            BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(16))!=20 ||
            source.Length<=20+AerojaRow*MonMagicEntrySize)
            throw new InvalidDataException("Aeroja requires exactly 341 complete monmagic2 rows and a text pool.");

        var result=AppendMonsterSkill(source,true,211,command=>
        {
            // Dark Sonic Wings supplies the existing aeon caster animation. The
            // common spell recipe clears its physical/Delay/OD/status payload.
            SetCalmCavernBase(command,new CalmCavernSinSkillSpec(AerojaRow,"DARK-VALEFOR",
                "Aeroja","Deals massive wind damage to all foes.",211,AerojaMagicId,AerojaImpactMagicId,3));
            SetMagic(command,60,multi:true);
            command.ElementFlgs=Ability_Command.ElementFlags.Wind;
            command.CharacterUser=Character_Enum.None;
            command.CasterAnimId=3;
            command.MoveRank=3;
            command.SubMenuCategorization=0;
            command.SubSubMenuCategorization=0;
            command.StealGil=false;
        });
        if (!result.Pass || result.NewId!=AerojaRow || result.Operand!=0x6155)
            throw new InvalidDataException("Aeroja append failed its preservation/readback contract.");
        return result;
    }
}
