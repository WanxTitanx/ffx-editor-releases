using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicLightSchemaTests
{
    [Theory]
    [InlineData("pppFpPointLight", 12, "4:F32,8:U8,12:F32")]
    [InlineData("pppFpPointLightModel", 16, "4:F32,8:U8,12:F32,16:U32")]
    [InlineData("pppFpPointLightModelScl", 16, "4:F32,8:U8,12:F32,16:U32")]
    [InlineData("pppFpPointLightVsf", 14, "4:F32,8:U8,12:F32,16:S16")]
    [InlineData("pppFpPointLightVsfScl", 14, "4:F32,8:U8,12:F32,16:S16")]
    [InlineData("pppNeiLightEikyo", 24, "4:F32,8:F32,12:F32,16:F32,20:F32,24:F32")]
    [InlineData("pppNeiPointLight", 12, "4:F32,8:F32,12:F32")]
    public void NativeLightSchema_UsesRecordParametersAndProtectsResourceReferences(string family, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(4, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        if (family.Contains("Model") || family.Contains("Vsf"))
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 16, schema.Fields.Last().Width));
    }

    [Fact]
    public void CharacterLight_SubTransformSelectorsAreTypedReadOnly()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppNeiChrPointLight"];
        Assert.Equal(Enumerable.Range(2, 10).Select(i => i * 4), schema.Fields.Select(f => f.Offset));
        Assert.All(schema.Fields, field =>
        {
            Assert.Equal(NativeType.S32, field.Type);
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, field.Offset, field.Width));
        });
    }

    [Fact]
    public void RealCharacterLight_RejectsEveryProgramAndStateSelectorWithoutMutatingTheSource()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0430.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppNeiChrPointLight");
        byte[] original = document.WorkingBytes!;
        var fields = document.ResolveKeyframeFields(slot, 0);
        Assert.Equal(10, fields.Count);
        foreach (var field in fields)
        {
            Assert.False(document.TryApplyFieldEdit(field, BitConverter.GetBytes(int.MaxValue), out _));
            Assert.Equal(original, document.WorkingBytes);
        }
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppNeiLightEikyo", 5)]
    [InlineData("pppNeiPointLight", 2)]
    public void RealLightAccumulator_MixedPrefixAndPayloadSaveChangesOnlyNativeFloatOperands(string family, int payloadCount)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0430.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(slot, 0).Where(f => f.Offset >= 8).ToArray();
        Assert.Equal(payloadCount, fields.Length);
        var prefix = document.PrefixOperands.Single(f => f.Opcode == family && f.RecordOffset == slot.RecordOffset);
        Assert.True(prefix.IsEditable);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        var edits = fields.Select(f => (f, BitConverter.GetBytes(0.375f))).ToArray();
        foreach (var (field, bytes) in edits) bytes.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        BitConverter.GetBytes(0.625f).CopyTo(expected, document.ParsedFile.DataSectionRawPtr + prefix.DataOffset);
        Assert.True(document.TryApplyDocumentEdits(edits, Array.Empty<(MagicTimingField, uint)>(),
            Array.Empty<(MagicGeometryField, short)>(), new[] { (prefix, BitConverter.SingleToUInt32Bits(0.625f)) }, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppDrawFilter", 8, 30, "4:U32,8:U32,12:U32,16:U32,20:U32,24:U32,28:S32,32:S32,36:U8,37:U8")]
    [InlineData("pppDrawRain", 4, 52, "4:F32,8:U32,16:F32,20:F32,24:F32,32:F32,36:F32,40:F32,48:F32,52:F32")]
    public void DelegatedRenderLayouts_IncludeTheirActualRecordReads(string family, int start, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.True(schema.PayloadConsumer);
        Assert.True(schema.Editable);
        Assert.Equal(new MagicWindow(start, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
    }
}
