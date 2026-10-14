using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicResidualPayloadSchemaTests
{
    [Theory]
    [InlineData("pppDrawShapeRev", "4:U32,8:U16,12:U8,13:U8,14:U8,15:U8", "8,12,13,14,15")]
    [InlineData("pppEiZCrctDisPos", "16:F32,20:F32,24:F32,32:F32,36:F32,40:F32,48:U8", "16,20,24,32,36,40,48")]
    [InlineData("pppKeShpDtt", "4:F32,8:F32,12:F32,16:F32,20:F32,24:U8,25:U8", "8,12,16,20,24,25")]
    [InlineData("pppKeShpTail", "4:U32,8:U16,16:S16,18:S16,20:S16,22:S16,24:F32,28:F32,32:U16", "16,18,20,22,24,28")]
    [InlineData("pppKeShpTailX", "4:U32,8:U16,16:S16,18:S16,20:S16,22:S16,24:F32,28:F32,32:U16,34:U8,35:U8,36:U8", "16,18,20,22,24,28,34,35,36")]
    [InlineData("pppKeShpTail2", "4:U32,8:U16,12:F32,16:F32,20:U8,21:U8,22:U8,23:U8,24:U8,25:U8,26:U8,27:U8,28:F32,32:U16,34:U8", "12,16,20,21,22,23,24,25,26,27,28,34")]
    [InlineData("pppKeShpTail2X", "4:U32,8:U16,12:F32,16:F32,20:U8,21:U8,22:U8,23:U8,24:U8,25:U8,26:U8,27:U8,28:F32,32:U16,34:U8,35:U8,36:U8,37:U8", "12,16,20,21,22,23,24,25,26,27,28,34,35,36,37")]
    [InlineData("pppNeiDrawMdlPointLight", "4:U32,8:S32,12:S32,16:S32,20:S32,24:S32,28:S32,32:S32,36:S32,40:S32,44:S32", "")]
    [InlineData("pppNeiDrawMdlTsPointLight", "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:S32,36:S32,40:S32,44:S32,48:S32,52:S32,56:S32,60:S32,64:S32,68:S32", "8,12,16,20,24,28")]
    public void ReviewedResidualFamily_ContainsOnlyProvenTypedOperandsAndWritePermissions(string family, string fields, string writable)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.True(schema.PayloadConsumer);
        Assert.Equal(fields, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.Equal(writable, string.Join(",", schema.Fields.Where(f => MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)).Select(f => f.Offset)));
    }

    [Theory]
    [InlineData("pppDrawShapeRev", 446, 13)]
    [InlineData("pppKeShpDtt", 42, 8)]
    [InlineData("pppKeShpTail", 22, 24)]
    [InlineData("pppKeShpTailX", 483, 24)]
    [InlineData("pppKeShpTail2", 6, 16)]
    [InlineData("pppKeShpTail2X", 42, 16)]
    [InlineData("pppNeiDrawMdlTsPointLight", 519, 12)]
    public void RealResidualFamily_SaveCopyChangesExactlyTheSelectedOperand(string family, int id, int offset)
    {
        var document = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == offset);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        byte[] value = field.Type == NativeType.F32 ? BitConverter.GetBytes(0.375f) : new byte[] { 1 };
        value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + offset);
        Assert.True(document.TryApplyFieldEdit(field, value, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppNeiDrawMdlPointLight", 519, 8)]
    [InlineData("pppNeiDrawMdlTsPointLight", 519, 32)]
    [InlineData("pppKeShpTail", 22, 32)]
    [InlineData("pppKeShpTail2X", 42, 32)]
    public void RealResidualFamily_RejectsNativeReferencesAndUnboundedCounts(string family, int id, int offset)
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"), out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == offset);
        byte[] original = document.WorkingBytes!;
        Assert.False(document.TryApplyFieldEdit(field, new byte[field.Width], out _));
        Assert.Equal(original, document.WorkingBytes);
    }

    [Fact]
    public void Tail2_ZeroWidthIsNativeGuardedWhileTail2XRequiresNonZeroWidth()
    {
        var families = MagicFieldMap.LoadEmbedded().Families;
        Assert.False(families["pppKeShpTail2"].Fields.Single(f => f.Offset == 12).RequiresNonZero);
        Assert.True(families["pppKeShpTail2X"].Fields.Single(f => f.Offset == 12).RequiresNonZero);
    }

    [Fact]
    public void RealShapeTrail_FloatImpulsePrefixUsesItsOwnedWriterAndPreservesAllOtherBytes()
    {
        var document = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0042.dll");
        Assert.True(document.TryLoad(source, out var error), error);
        var field = document.PrefixOperands.First(p => p.Opcode == "pppKeShpDtt" && p.IsEditable);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        BitConverter.GetBytes(0.375f).CopyTo(expected, document.ParsedFile!.DataSectionRawPtr + field.DataOffset);
        Assert.True(document.TryApplyPrefixEdits(new[] { (field, BitConverter.SingleToUInt32Bits(0.375f)) }, out error), error);
        Assert.Equal(expected, document.WorkingBytes);
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(document.FieldMap!.Families["pppKeShpDtt"], 4, 4));
    }
}
