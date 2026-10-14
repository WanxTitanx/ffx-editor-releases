using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicResourceOperatorSchemaTests
{
    [Theory]
    [InlineData("pppKeThTp", 4, 20, "4:F32,8:F32,12:F32,16:S32,20:S32", "8,12")]
    [InlineData("pppKeThTp2", 4, 20, "4:F32,8:F32,12:F32,16:S32,20:S32", "8,12")]
    [InlineData("pppKeHitChkPxB", 4, 9, "4:S32,8:S32,12:U8", "12")]
    [InlineData("pppKeThHitBorn", 4, 9, "4:S32,8:S32,12:U8", "12")]
    [InlineData("pppPointRAp", 4, 25, "4:F32,8:F32,12:S32,16:S32,24:S32,28:U8", "8,28")]
    [InlineData("pppVertexApDisPos", 4, 5, "4:U16,6:U8,7:U8,8:U8", "8")]
    [InlineData("pppKeHmgEff", 4, 12, "4:F32,8:S32,12:S32", "")]
    [InlineData("pppKeMdlTfd2", 8, 22, "4:U32,8:F32,12:F32,16:F32,20:S32,24:S32,28:U8,29:U8", "8,12,16,20,24,28,29")]
    [InlineData("pppKeMdlTfd3", 8, 26, "4:U32,8:F32,12:F32,16:F32,20:S32,24:S32,28:U8,29:U8,30:U8,31:U8,32:U8,33:U8", "8,12,16,20,24,28,29,30,31,32,33")]
    [InlineData("pppKeMdlTfdUv2", 8, 49, "4:U32,8:F32,12:F32,16:F32,20:S32,24:S32,28:U8,32:F32,36:F32,40:F32,44:F32,48:F32,52:F32,56:U8", "8,12,16,20,24,28,32,36,40,44,48,52,56")]
    [InlineData("pppKeMdlTfdUv3", 8, 53, "4:U32,8:F32,12:F32,16:F32,20:S32,24:S32,28:U8,32:F32,36:F32,40:F32,44:F32,48:F32,52:F32,56:U8,57:U8,58:U8,59:U8,60:U8", "8,12,16,20,24,28,32,36,40,44,48,52,56,57,58,59,60")]
    [InlineData("pppKeOfsPt", 16, 32, "16:F32,20:F32,24:F32,32:F32,36:F32,40:F32,44:U32", "16,20,24,32,36,40")]
    public void NativeOperator_UsesCompleteTypedReadsAndPreservesReferenceOperands(string family, int start, int width, string operands, string writable)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(start, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.Equal(writable, string.Join(",", schema.Fields.Where(f => MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)).Select(f => f.Offset)));
    }

    [Theory]
    [InlineData("pppKeMdlTfdUv2", "0x749D60")]
    [InlineData("pppKeOfsPt", "0x75CE20")]
    public void MissingHandlerAddresses_AreResolvedFromNativeCallbackTables(string family, string address)
    {
        Assert.Equal(address, MagicFieldMap.LoadEmbedded().Families[family].HandlerAddr);
    }

    [Theory]
    [InlineData("pppKeThTp", 5, 16)]
    [InlineData("pppKeThHitBorn", 203, 8)]
    [InlineData("pppPointRAp", 22, 12)]
    [InlineData("pppKeHmgEff", 74, 8)]
    public void RealOperator_RejectsUncheckedNativeProgramAndStateReferences(string family, int id, int offset)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == offset);
        Assert.Equal(NativeType.S32, field.Type);
        byte[] original = document.WorkingBytes!;
        Assert.False(document.TryApplyFieldEdit(field, BitConverter.GetBytes(int.MaxValue), out _));
        Assert.Equal(original, document.WorkingBytes);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppKeThTp", 5)]
    [InlineData("pppKeHmgEff", 74)]
    [InlineData("pppPointRAp", 22)]
    public void RealNativeFloatPrefix_UsesTheDedicatedContractAndPreservesSource(string family, int id)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var prefix = document.PrefixOperands.First(f => f.Opcode == family && f.IsEditable);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        BitConverter.GetBytes(0.375f).CopyTo(expected, document.ParsedFile!.DataSectionRawPtr + prefix.DataOffset);
        Assert.True(document.TryApplyPrefixEdits(new[] { (prefix, BitConverter.SingleToUInt32Bits(0.375f)) }, out error), error);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(document.FieldMap!.Families[family], 4, 4));
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppKeMdlTfd2", 304)]
    [InlineData("pppKeMdlTfd3", 313)]
    [InlineData("pppKeMdlTfdUv3", 202)]
    public void RealModelVariant_SaveCopyConfinesTheEditToItsNativePvaOperand(string family, int id)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 16);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        BitConverter.GetBytes(0.375f).CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + 16);
        Assert.True(document.TryApplyFieldEdit(field, BitConverter.GetBytes(0.375f), out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
