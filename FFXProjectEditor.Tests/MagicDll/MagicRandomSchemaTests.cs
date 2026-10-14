using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicRandomSchemaTests
{
    [Theory]
    [InlineData("pppRandFV", false)]
    [InlineData("pppRandUpFV", false)]
    [InlineData("pppRandDownFV", false)]
    [InlineData("pppRandIV", true)]
    [InlineData("pppRandUpIV", true)]
    [InlineData("pppRandDownIV", true)]
    [InlineData("pppSRandFV", false)]
    [InlineData("pppSRandUpFV", false)]
    [InlineData("pppSRandDownFV", false)]
    public void RandomVectorSchema_MatchesNativeReadsAndSignedDestination(string opcode, bool integer)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[opcode];
        Assert.True(schema.Editable);
        Assert.True(schema.PayloadConsumer);
        Assert.Equal(new MagicWindow(4, 29), schema.Window);
        Assert.Equal(new[] { 4, 16, 20, 24, 32 }, schema.Fields.Select(f => f.Offset));
        Assert.Equal(NativeType.S32, schema.Fields[0].Type);
        Assert.All(schema.Fields.Where(f => f.Offset is 16 or 20 or 24),
            f => Assert.Equal(integer ? NativeType.S32 : NativeType.F32, f.Type));
        Assert.Equal(NativeType.U8, schema.Fields.Last().Type);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
    }

    [Fact]
    public void UpIntegerFamily_UsesTheRealHandlerAndNotTheNearbyConversionHelper()
    {
        Assert.Equal("0x731430", MagicFieldMap.LoadEmbedded().Families["pppRandUpIV"].HandlerAddr);
    }

    [Theory]
    [InlineData("pppRandFV", 21, false)]
    [InlineData("pppRandUpFV", 4, false)]
    [InlineData("pppRandDownFV", 4, false)]
    [InlineData("pppRandIV", 3, true)]
    [InlineData("pppRandUpIV", 201, true)]
    [InlineData("pppRandDownIV", 304, true)]
    [InlineData("pppSRandFV", 3, false)]
    [InlineData("pppSRandUpFV", 3, false)]
    [InlineData("pppSRandDownFV", 22, false)]
    public void NativeAmplitudeFields_SaveOnlyTheirFourOperands(string opcode, int id, bool integer)
    {
        var doc = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:D4}.dll");
        Assert.True(doc.TryLoad(source, out var error), error);
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == opcode);
        var fields = doc.ResolveAllFields(slot);
        Assert.DoesNotContain(fields, f => f.Offset is 8 or 12);
        var edits = fields.Where(f => f.Offset >= 8).Select(f => (f, f.Offset == 32
            ? new byte[] { 1 } : integer ? BitConverter.GetBytes(-123) : BitConverter.GetBytes(0.375f))).ToArray();
        Assert.Equal(4, edits.Length);
        byte[] original = doc.WorkingBytes!, expected = (byte[])original.Clone();
        foreach (var (field, bytes) in edits)
            bytes.CopyTo(expected, doc.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        Assert.True(doc.TryApplyFieldEdits(edits, out error), error);
        Assert.Equal(expected, doc.WorkingBytes);
        Assert.Equal(original, File.ReadAllBytes(source));
        var destination = fields.Single(f => f.Offset == 4);
        Assert.False(doc.TryApplyFieldEdit(destination, BitConverter.GetBytes(0), out _));
        Assert.Equal(expected, doc.WorkingBytes);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
