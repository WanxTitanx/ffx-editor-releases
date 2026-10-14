using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicLensSchemaTests
{
    [Theory]
    [InlineData("pppKeLnsArnd", 8, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32")]
    [InlineData("pppKeLnsArndT", 8, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32")]
    [InlineData("pppKeLnsClm", 15, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,20:U16,22:U8")]
    [InlineData("pppKeLnsClmT", 15, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,20:U16,22:U8")]
    [InlineData("pppKeLnsCrn", 12, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,16:U16,18:U16")]
    [InlineData("pppKeLnsCrnT", 12, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,16:U16,18:U16")]
    [InlineData("pppKeLnsFls", 12, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,16:U16,18:U16")]
    [InlineData("pppKeLnsFlsT", 12, "4:U32,8:U8,9:U8,10:U8,11:U8,12:F32,16:U16,18:U16")]
    public void NativeLensSchema_IncludesInitializerResourceAndControlLoads(string family, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(8, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
    }

    [Theory]
    [InlineData("pppKeLnsArnd", 106)]
    [InlineData("pppKeLnsArndT", 710)]
    [InlineData("pppKeLnsClm", 70)]
    [InlineData("pppKeLnsClmT", 710)]
    [InlineData("pppKeLnsCrn", 106)]
    [InlineData("pppKeLnsCrnT", 710)]
    [InlineData("pppKeLnsFls", 119)]
    [InlineData("pppKeLnsFlsT", 710)]
    public void RealLensRecords_SaveCopyChangesOnlyVerifiedOperands(string family, int id)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(slot, 0);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        var edits = fields.Where(f => f.Offset >= 8).Select(f =>
        {
            byte[] value = f.Type switch
            {
                NativeType.F32 => BitConverter.GetBytes(0.375f),
                NativeType.U16 => BitConverter.GetBytes((ushort)7),
                _ => new byte[] { 1 }
            };
            value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + f.RecordOffset + f.Offset);
            return (f, value);
        }).ToArray();
        Assert.True(document.TryApplyFieldEdits(edits, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("pppKeLnsClm", 70, 20)]
    [InlineData("pppKeLnsClmT", 710, 20)]
    [InlineData("pppKeLnsCrn", 106, 18)]
    [InlineData("pppKeLnsCrnT", 710, 18)]
    [InlineData("pppKeLnsFls", 119, 16)]
    [InlineData("pppKeLnsFlsT", 710, 16)]
    public void RealLensDivisor_CannotBecomeZeroAndCancelsTheWholeEditBatch(string family, int id, int offset)
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"), out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(slot, 0);
        var divisor = fields.Single(f => f.Offset == offset);
        var scalar = fields.Single(f => f.Offset == 12);
        byte[] original = document.WorkingBytes!;
        string sha = document.ShaAfter;
        Assert.True(document.FieldMap!.Families[family].Fields.Single(f => f.Offset == offset).RequiresNonZero);
        Assert.False(document.TryApplyFieldEdits(new[]
        {
            (scalar, BitConverter.GetBytes(0.375f)), (divisor, BitConverter.GetBytes((ushort)0))
        }, out _));
        Assert.Equal(original, document.WorkingBytes);
        Assert.Equal(sha, document.ShaAfter);
    }
}
