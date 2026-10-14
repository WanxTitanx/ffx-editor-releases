using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicRandomScalarSchemaTests
{
    [Theory]
    [InlineData("pppRandFloat", NativeType.F32, 4, 12)]
    [InlineData("pppRandUpFloat", NativeType.F32, 4, 12)]
    [InlineData("pppRandDownFloat", NativeType.F32, 4, 12)]
    [InlineData("pppRandChar", NativeType.U8, 1, 9)]
    [InlineData("pppRandUpChar", NativeType.U8, 1, 9)]
    [InlineData("pppRandDownChar", NativeType.U8, 1, 9)]
    [InlineData("pppRandShort", NativeType.U16, 2, 10)]
    [InlineData("pppRandUpShort", NativeType.U16, 2, 10)]
    [InlineData("pppRandDownShort", NativeType.U16, 2, 10)]
    [InlineData("pppRandInt", NativeType.U32, 4, 12)]
    [InlineData("pppRandUpInt", NativeType.U32, 4, 12)]
    [InlineData("pppRandDownInt", NativeType.U32, 4, 12)]
    public void ScalarSchema_MatchesNativeAmplitudeWidthAndMode(string opcode, NativeType type, int width, int mode)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[opcode];
        Assert.Equal(new[] { 4, 8, mode }, schema.Fields.Select(f => f.Offset));
        Assert.Equal(NativeType.S32, schema.Fields[0].Type);
        Assert.Equal(type, schema.Fields[1].Type);
        Assert.Equal(width, schema.Fields[1].Width);
        Assert.Equal(NativeType.U8, schema.Fields[2].Type);
        Assert.Equal(new MagicWindow(4, mode - 3), schema.Window);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
    }

    [Fact]
    public void UpFloat_UsesItsActualNativeHandler()
    {
        Assert.Equal("0x72F760", MagicFieldMap.LoadEmbedded().Families["pppRandUpFloat"].HandlerAddr);
    }

    [Theory]
    [InlineData("pppRandChar", 255u, 9)]
    [InlineData("pppRandShort", 65535u, 10)]
    [InlineData("pppRandInt", uint.MaxValue, 12)]
    public void UnsignedAmplitudeDecode_UsesTheFullNativeWidthWithoutConsumingTheMode(string opcode, uint expected, int modeAt)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[opcode];
        var amplitude = schema.Fields.Single(f => f.Offset == 8);
        var mode = schema.Fields.Single(f => f.Offset == modeAt);
        byte[] record = new byte[16];
        Array.Fill(record, (byte)0xFF, 8, amplitude.Width);
        record[modeAt] = 1;
        var field = MagicField.CreateResolved(amplitude, opcode, record, 0, MagicSlot.ComputeSha256Hex(record));
        var flag = MagicField.CreateResolved(mode, opcode, record, 0, MagicSlot.ComputeSha256Hex(record));
        Assert.Equal(expected, field.ValueUInt);
        Assert.Equal(1u, flag.ValueUInt);
    }

    [Fact]
    public void RealScalarFloat_SaveCopyPreservesSourceAndEveryOtherByte()
    {
        var doc = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0045.dll");
        Assert.True(doc.TryLoad(source, out var error), error);
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppRandFloat");
        var fields = doc.ResolveAllFields(slot);
        Assert.Equal(new[] { 4, 8, 12 }, fields.Select(f => f.Offset));
        byte[] original = doc.WorkingBytes!, expected = (byte[])original.Clone();
        var amplitude = fields.Single(f => f.Offset == 8);
        var mode = fields.Single(f => f.Offset == 12);
        BitConverter.GetBytes(0.625f).CopyTo(expected, doc.ParsedFile.DataSectionRawPtr + slot.RecordOffset + 8);
        expected[doc.ParsedFile.DataSectionRawPtr + slot.RecordOffset + 12] = 1;
        Assert.True(doc.TryApplyFieldEdits(new[] { (amplitude, BitConverter.GetBytes(0.625f)), (mode, new byte[] { 1 }) }, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
