using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SpiraReforgeAerojaTests
{
    private static byte[] Input()
    {
        var rows=Ability_Command.ReadList(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Battle","monmagic2.bin")),false);
        Assert.InRange(rows.Count,212,341);
        while(rows.Count<341)rows.Add(rows[211].CloneDeep(false));
        return Ability_Command.WriteList(rows,false);
    }

    [Fact]
    public void AddsMonsterWindSpellWithoutChangingEarlierRowsOrTexts()
    {
        byte[] original=Input(),copy=original.ToArray();
        var result=MonsterMagicGrowWriter.AppendAeroja(original);
        Assert.True(result.Pass);Assert.Equal(342,result.NewEntryCount);Assert.Equal(0x6155,result.Operand);
        Assert.Equal(copy,original);
        var before=EntryListFile.Unpack(original);var after=EntryListFile.Unpack(result.GrownBytes);
        Assert.Equal(before.FirstFile,after.FirstFile.Take(before.FirstFile.Length));
        Assert.Equal(before.SecondFile,after.SecondFile.Take(before.SecondFile.Length));
        var rows=Ability_Command.ReadList(result.GrownBytes,false);var row=rows[341];
        Assert.Equal("Aeroja",FfxEncoding.DecodeScript(row.NameScriptBytes).GetString(FfxEncoding.UsDecoder,true));
        Assert.Equal(Ability_Command.ElementFlags.Wind,row.ElementFlgs);
        Assert.Equal(DamageFormula_Enum.Magic,row.DamageFormula);
        Assert.Equal(Ability_Command.DamageFlags.Magical,row.DamageFlgs);
        Assert.True(row.TargetFlgs.HasFlag(Ability_Command.TargetFlags.Multi));
        Assert.True(row.TargetFlgs.HasFlag(Ability_Command.TargetFlags.Enemies));
        Assert.Equal(60,row.AttackPower);Assert.Equal(1,row.HitCount);Assert.Equal(0,row.CostMp);
        Assert.Equal(0,row.CostOverdrive);Assert.Equal(3,row.CasterAnimId);
        Assert.Equal(869,row.Anim1Id);Assert.Equal(901,row.Anim2Id);
        Assert.False(row.FlagMisc2DelayS);Assert.Equal(0,(int)row.StatusFlgs);
        Assert.Equal(0,(int)row.StatBuffFlgs);Assert.Equal(0,(int)row.SpecialBuffFlgs);
        Assert.Equal(0,row.StatusChance.Death);Assert.Equal(0,row.StatusChance.Poison);
        Assert.Equal(result.GrownBytes,Ability_Command.WriteList(rows,false));
        Assert.Throws<InvalidDataException>(()=>MonsterMagicGrowWriter.AppendAeroja(result.GrownBytes));
    }

    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(10)] [InlineData(12)] [InlineData(14)] [InlineData(16)]
    public void RejectsUnexpectedTableHeader(int offset)
    {
        byte[] data=Input();data[offset]^=255;
        Assert.Throws<InvalidDataException>(()=>MonsterMagicGrowWriter.AppendAeroja(data));
    }

    [Fact]
    public void RejectsMissingOrTruncatedInput()
    {
        Assert.Throws<ArgumentNullException>(()=>MonsterMagicGrowWriter.AppendAeroja(null!));
        Assert.Throws<InvalidDataException>(()=>MonsterMagicGrowWriter.AppendAeroja(new byte[10]));
        Assert.Throws<InvalidDataException>(()=>MonsterMagicGrowWriter.AppendAeroja(Input()[..100]));
    }
}
