using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicTailSchemaTests
{
    [Theory]
    [InlineData("pppKeShpTail3", 32, 28, 32)]
    [InlineData("pppKeShpTail3X", 32, 28, 32)]
    [InlineData("pppKeShpTailPht", 48, 96, 28)]
    public void TailSchema_UsesSignedNativePvaAndIncludesProvenDrawOperands(string family, int coefficients, int segmentOffset, int firstFloat)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        var pva = schema.Fields.Where(f => f.Offset >= coefficients && f.Offset < coefficients + 48).ToArray();
        Assert.Equal(24, pva.Length);
        Assert.All(pva, field => { Assert.Equal(NativeType.S16, field.Type); Assert.Equal(2, field.Width); });
        Assert.Equal(NativeType.U32, schema.Fields.Single(f => f.Offset == 4).Type);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        Assert.Equal(NativeType.U32, schema.Fields.Single(f => f.Offset == 8).Type);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
        Assert.Equal(NativeType.U8, schema.Fields.Single(f => f.Offset == segmentOffset).Type);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, segmentOffset, 1));
        Assert.Equal(NativeType.F32, schema.Fields.Single(f => f.Offset == 12).Type);
        Assert.True(schema.Fields.Single(f => f.Offset == 12).RequiresNonZero);
        Assert.Equal(firstFloat == 28 ? NativeType.F32 : NativeType.S16, schema.Fields.Single(f => f.Offset == firstFloat).Type);
    }

    [Theory]
    [InlineData("pppKeShpTail3", 4, 28)]
    [InlineData("pppKeShpTail3X", 354, 28)]
    [InlineData("pppKeShpTailPht", 307, 96)]
    public void RealTail_RejectsZeroDivisorAndUncheckedSegmentCountWithoutChangingBytes(string family, int id, int segmentOffset)
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"), out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(slot, 0);
        var divisor = fields.Single(f => f.Offset == 12);
        var count = fields.Single(f => f.Offset == segmentOffset);
        var original = document.WorkingBytes!.ToArray();
        var hash = document.ShaAfter;
        foreach (float invalid in new[] { 0f, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.False(document.TryApplyFieldEdit(divisor, BitConverter.GetBytes(invalid), out _));
            Assert.Equal(original, document.WorkingBytes);
            Assert.Equal(hash, document.ShaAfter);
        }
        Assert.False(document.TryApplyFieldEdit(count, new byte[] { 0 }, out _));
        Assert.Equal(original, document.WorkingBytes);
        Assert.Equal(hash, document.ShaAfter);
    }

    [Theory]
    [InlineData("pppKeShpTail3", 4, 32)]
    [InlineData("pppKeShpTail3X", 354, 32)]
    [InlineData("pppKeShpTailPht", 307, 48)]
    public void RealTail_SignedCoefficientSavePreservesEveryOtherByteAndTheSource(string family, int id, int offset)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == offset);
        Assert.Equal(NativeType.S16, field.Type);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        byte[] value = BitConverter.GetBytes((short)-123);
        value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        Assert.True(document.TryApplyFieldEdit(field, value, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
