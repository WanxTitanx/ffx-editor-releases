using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Ability;

/// <summary>Append-only wind tier; preserves all existing extended commands.</summary>
public static class SpiraReforgeAeroCommandWriter
{
    public const int FirstRow = 367;
    public const int FirstMagicId = 866;
    public const int FirstImpactMagicId = 898;
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "Aero", "Aerora", "Aeroga" });

    public static CommandGrowAppendResult Append(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length < 20) throw new InvalidDataException("Truncated command.bin header.");
        // Validate the small fixed header before the general reader allocates row buffers.
        if (source[0] != 1 || source.Skip(1).Take(7).Any(b => b != 0) ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(8)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(10)) != FirstRow - 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(12)) != 0x60 ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(14)) != FirstRow * 0x60 ||
            BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(16)) != 20 || source.Length <= 20 + FirstRow * 0x60)
            throw new InvalidDataException("Aero requires 367 complete command rows of 0x60 bytes and a text pool.");
        var rows = Ability_Command.ReadList(source, hasExtraInfo: true);
        int[] donors = [66, 69, 73]; // Fire, Fira, Firaga, PC command layout.
        byte[] power = [12, 24, 42], mp = [4, 8, 16];
        string[] descriptions = ["Deals minor wind damage to one enemy.",
            "Deals moderate wind damage to one enemy.", "Deals major wind damage to one enemy."];
        var additions = new List<Ability_Command>();
        for (int tier = 0; tier < Names.Count; tier++)
        {
            var row = rows[donors[tier]].CloneDeep(hasExtraInfo: true);
            if (row.DamageFormula != DamageFormula_Enum.Magic || row.HitCount != 1)
                throw new InvalidDataException("The player-magic donor contract changed.");
            row.NameScriptBytes = Encode(Names[tier]);
            row.DescriptionScriptBytes = Encode(descriptions[tier]);
            row.JapaneseOnlyText1ScriptBytes = [];
            row.JapaneseOnlyText2ScriptBytes = [];
            row.CharacterUser = Character_Enum.Lulu;
            row.SubMenuCategorization = SpiraReforgeExtendedCommandRecipes.BlackMagicSubMenu;
            row.SubSubMenuCategorization = SpiraReforgeExtendedCommandRecipes.BlackMagicSubMenu;
            row.CostOverdrive = 0;
            row.OverdriveCategory = 0;
            row.CostMp = mp[tier];
            row.AttackPower = power[tier];
            row.ElementFlgs = Ability_Command.ElementFlags.Wind;
            row.Anim1Id = (short)(FirstMagicId + tier);
            row.Anim2Id = (short)(FirstImpactMagicId + tier);
            row.ExtraInfo.OrderingIndexInMenu = (byte)(80 + tier);
            additions.Add(row);
        }
        var result = CommandBinGrowCore.AppendRows(source, additions, FirstRow);
        if (!result.Pass || result.NewEntryCount != 370)
            throw new InvalidDataException("Aero append failed preservation/readback checks.");
        return result;
    }

    private static byte[] Encode(string text) => FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;
}
