using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicEmitterPhysicsSchemaTests
{
    [Theory]
    [InlineData("pppKeBornRnd", 24, "4:S16,6:S16,8:U8,9:U8,10:U8,12:S32,16:S32,20:S32,24:S32", "8,9,10")]
    [InlineData("pppKeBornRnd2", 28, "4:U16,12:U8,13:U8,14:U8,16:S32,20:S32,24:S32,28:S32", "12,13,14")]
    [InlineData("pppKeBornRnd3", 24, "4:U16,6:U8,7:U8,8:U8,12:S32,16:S32,20:S32,24:S32", "8")]
    [InlineData("pppKeBornRnd6", 24, "4:U16,6:U8,7:U8,8:U8,12:S32,16:S32,20:S32,24:S32", "8")]
    [InlineData("pppEiWfacc", 20, "4:S32,8:S32,12:S32,16:S32,20:F32", "20")]
    [InlineData("pppKeGrvEff", 8, "4:S32,8:S32", "")]
    [InlineData("pppKeLnsLpT", 13, "4:F32,8:F32,12:U8,13:U8,14:U8,15:U8,16:U8", "8,12,13,14,15,16")]
    public void NativeSchema_DistinguishesParametersFromProgramAndStateReferences(string family, int width, string operands, string editable)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(4, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.Equal(editable, string.Join(",", schema.Fields.Where(f =>
            MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)).Select(f => f.Offset)));
    }

    [Theory]
    [InlineData("pppKeBornRnd2", 69, "12,13,14")]
    [InlineData("pppKeBornRnd3", 5, "8")]
    [InlineData("pppKeBornRnd6", 3, "8")]
    public void RealEmitter_ControlWritesPreserveReferencesAndSource(string family, int id, string offsets)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(slot, 0);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        foreach (var reference in fields.Where(f => f.Type == NativeType.S32))
        {
            Assert.False(document.TryApplyFieldEdit(reference, BitConverter.GetBytes(int.MaxValue), out _));
            Assert.Equal(original, document.WorkingBytes);
        }
        var edits = new List<(MagicField, byte[])>();
        foreach (int offset in offsets.Split(',').Select(int.Parse))
        {
            var field = fields.Single(f => f.Offset == offset);
            edits.Add((field, new byte[] { 1 }));
            expected[document.ParsedFile.DataSectionRawPtr + field.RecordOffset + offset] = 1;
        }
        Assert.True(document.TryApplyFieldEdits(edits, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void RealGrvEff_ProgramAndStateOffsetsCannotBeWrittenAsFloats()
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, "magic_0022.dll"), out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppKeGrvEff");
        byte[] original = document.WorkingBytes!;
        Assert.All(document.ResolveKeyframeFields(slot, 0), field =>
        {
            Assert.Equal(NativeType.S32, field.Type);
            Assert.False(document.TryApplyFieldEdit(field, BitConverter.GetBytes(0.375f), out _));
            Assert.Equal(original, document.WorkingBytes);
        });
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(0.375f, true)]
    [InlineData(-0.375f, true)]
    public void NativeForceDivisor_RejectsZeroAndSpecialValuesWithoutMutatingTheDocument(float value, bool accepted)
    {
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        var parsed = new MagicDllParser().Parse(fixture.Source);
        var rdata = parsed.Sections.Single(s => s.Name == ".rdata");
        int at = bytes.AsSpan(rdata.RawPtr, rdata.RawSize).IndexOf(Encoding.ASCII.GetBytes("pppSRandFV\0"));
        Assert.True(at >= 0);
        Encoding.ASCII.GetBytes("pppEiWfacc\0").CopyTo(bytes, rdata.RawPtr + at);
        File.WriteAllBytes(fixture.Source, bytes);
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(fixture.Source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppEiWfacc");
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 20);
        var spec = document.FieldMap!.Families["pppEiWfacc"].Fields.Single(f => f.Offset == 20);
        Assert.True(spec.RequiresNonZero);
        byte[] original = document.WorkingBytes!;
        string sha = document.ShaAfter;
        Assert.Equal(accepted, document.TryApplyFieldEdit(field, BitConverter.GetBytes(value), out error));
        if (!accepted)
        {
            Assert.Equal(original, document.WorkingBytes);
            Assert.Equal(sha, document.ShaAfter);
        }
        else
        {
            byte[] expected = (byte[])original.Clone();
            BitConverter.GetBytes(value).CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + 20);
            Assert.Equal(expected, document.WorkingBytes);
        }
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Source));
    }

    [Fact]
    public void RealSpringShift_UsesDedicatedFloatPrefixContractWithoutOpeningGenericR6()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0070.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var prefix = document.PrefixOperands.First(f => f.Opcode == "pppKeLnsLpSft" && f.IsEditable);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        BitConverter.GetBytes(0.375f).CopyTo(expected, document.ParsedFile!.DataSectionRawPtr + prefix.DataOffset);
        Assert.True(document.TryApplyPrefixEdits(new[] { (prefix, BitConverter.SingleToUInt32Bits(0.375f)) }, out error), error);
        Assert.Equal(expected, document.WorkingBytes);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(document.FieldMap!.Families["pppKeLnsLpSft"], 4, 4));
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
