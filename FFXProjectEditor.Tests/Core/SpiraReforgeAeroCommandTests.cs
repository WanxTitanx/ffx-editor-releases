using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SpiraReforgeAeroCommandTests
{
    private static byte[] Input()
    {
        byte[] vanilla = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Battle", "command.bin"));
        var rows = Ability_Command.ReadList(vanilla, true);
        Assert.Equal(320, rows.Count);
        return CommandBinGrowCore.AppendRows(vanilla,
            Enumerable.Range(0, 47).Select(_ => rows[66].CloneDeep(true)).ToArray()).GrownBytes;
    }

    [Fact]
    public void AppendsPlayerWindTiersAndPreservesAllPriorPayloadAndText()
    {
        byte[] source = Input(), immutable = source.ToArray();
        var result = SpiraReforgeAeroCommandWriter.Append(source);
        Assert.True(result.Pass);
        Assert.Equal(370, result.NewEntryCount);
        Assert.Equal(immutable, source);
        var old = EntryListFile.Unpack(source);
        var current = EntryListFile.Unpack(result.GrownBytes);
        Assert.Equal(old.FirstFile, current.FirstFile.Take(old.FirstFile.Length).ToArray());
        Assert.Equal(old.SecondFile, current.SecondFile.Take(old.SecondFile.Length).ToArray());
        var rows = Ability_Command.ReadList(result.GrownBytes, true);
        int[] mp = [4,8,16], power = [12,24,42];
        for (int tier=0; tier<3; tier++)
        {
            var row = rows[367+tier];
            Assert.Equal(SpiraReforgeAeroCommandWriter.Names[tier], FfxEncoding.DecodeScript(row.NameScriptBytes).GetString(FfxEncoding.UsDecoder,true));
            Assert.Equal(866+tier, row.Anim1Id);
            Assert.Equal(898+tier,row.Anim2Id);
            Assert.Equal(mp[tier],row.CostMp);
            Assert.Equal(power[tier],row.AttackPower);
            Assert.Equal(Ability_Command.ElementFlags.Wind,row.ElementFlgs);
            Assert.Equal(DamageFormula_Enum.Magic,row.DamageFormula);
            Assert.Equal(Character_Enum.Lulu,row.CharacterUser);
            Assert.Equal(1,row.SubMenuCategorization);
            Assert.Equal(1,row.HitCount);
            Assert.Equal(0,row.CostOverdrive);
        }
        Assert.Equal(result.GrownBytes,Ability_Command.WriteList(rows,true));
        Assert.Throws<InvalidDataException>(()=>SpiraReforgeAeroCommandWriter.Append(result.GrownBytes));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(10)] [InlineData(12)] [InlineData(14)] [InlineData(16)]
    public void RejectsMalformedHeaderBeforeGeneralReader(int offset)
    {
        byte[] source = Input(); source[offset] ^= 255;
        Assert.Throws<InvalidDataException>(()=>SpiraReforgeAeroCommandWriter.Append(source));
    }

    [Fact]
    public void RejectsMissingAndTruncatedInput()
    {
        Assert.Throws<ArgumentNullException>(()=>SpiraReforgeAeroCommandWriter.Append(null!));
        Assert.Throws<InvalidDataException>(()=>SpiraReforgeAeroCommandWriter.Append(new byte[19]));
        Assert.Throws<InvalidDataException>(()=>SpiraReforgeAeroCommandWriter.Append(Input()[..100]));
    }
}
