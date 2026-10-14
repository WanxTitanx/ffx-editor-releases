using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicLegacyResidualContractTests
{
    [Fact]
    public void DrawMdlBs_SharedNativeCallbackCannotExposeHostMatrixOffsetsAsRecordFields()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppDrawMdlBS"];
        Assert.Equal("0x737830", schema.HandlerAddr);
        Assert.Empty(schema.Fields);
        Assert.Null(schema.Window);
        Assert.False(schema.Editable);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 144, 4));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 148, 4));
    }

    [Fact]
    public void PointAp_DeclaresTwoProtectedReferencesAndTheNativeDelayByte()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppPointAp"];
        Assert.Equal("4:S32,8:S32,12:U8", string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 12, 1));
    }

    [Theory]
    [InlineData("pppColor")]
    [InlineData("pppColAccele")]
    [InlineData("pppColMove")]
    public void WordColorImpulse_ExposesFourSeparateNativeWrappingWords(string family)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new[] { 8, 10, 12, 14 }, schema.Fields.Select(f => f.Offset));
        Assert.All(schema.Fields, f => { Assert.Equal(NativeType.U16, f.Type); Assert.Equal(2, f.Width); });
    }

    [Theory]
    [InlineData("pppColor", 3, 14)]
    [InlineData("pppPointAp", 70, 12)]
    public void RealLegacyOperand_EditAndSaveCopyPreserveAdjacentChannelsAndSource(string family, int id, int offset)
    {
        var document = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == offset);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        byte[] value = field.Width == 2 ? BitConverter.GetBytes((ushort)0xFF85) : new byte[] { 7 };
        value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + offset);
        Assert.True(document.TryApplyFieldEdit(field, value, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
