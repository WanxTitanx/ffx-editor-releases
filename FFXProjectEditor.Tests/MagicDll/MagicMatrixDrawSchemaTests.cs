using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicMatrixDrawSchemaTests
{
    [Theory]
    [InlineData("pppDrawShape", 8, 2, "8:U16")]
    [InlineData("pppDrawMdl2", 8, 1, "8:U8")]
    [InlineData("pppDrawMdlSemi2", 8, 3, "8:U16,10:U8")]
    [InlineData("pppDrawMdlSemi3", 8, 7, "8:U16,10:U8,11:U8,12:U8,13:U8,14:U8")]
    [InlineData("pppKeMdlDtt", 8, 14, "8:F32,12:F32,16:F32,20:U8,21:U8")]
    [InlineData("pppKeLnsLp", 4, 10, "4:F32,8:F32,12:U8,13:U8")]
    [InlineData("pppKeGrvTgt", 4, 8, "4:F32,8:F32")]
    [InlineData("pppKeMdlTfd", 8, 21, "8:F32,12:F32,16:F32,20:S32,24:S32,28:U8")]
    [InlineData("pppKeMdlTfdUv", 8, 48, "8:F32,12:F32,16:F32,20:S32,24:S32,28:U8,32:F32,36:F32,40:F32,44:F32,48:F32,52:F32")]
    public void Schema_MatchesNativeRecordReads(string family, int start, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.True(schema.PayloadConsumer);
        Assert.True(schema.Editable);
        Assert.Equal(new MagicWindow(start, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.All(schema.Fields, field =>
        {
            int expectedWidth = field.Type is NativeType.U8 ? 1 : field.Type is NativeType.U16 ? 2 : 4;
            Assert.Equal(expectedWidth, field.Width);
            Assert.Equal(field.Offset >= 8,
                MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, field.Offset, field.Width));
        });
    }

    [Theory]
    [InlineData("pppMatrixScl")]
    [InlineData("pppKeMatSN")]
    public void HostOnlyMatrixOperations_HaveNoEditablePayload(string family)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.False(schema.PayloadConsumer);
        Assert.False(schema.Editable);
        Assert.Null(schema.Window);
        Assert.Empty(schema.Fields);
    }

    [Fact]
    public void TfdUv_UsesItsNativeDrawHandler()
    {
        Assert.Equal("0x749A20", MagicFieldMap.LoadEmbedded().Families["pppKeMdlTfdUv"].HandlerAddr);
    }

    [Theory]
    [InlineData("pppDrawShape", 3, 1)]
    [InlineData("pppDrawMdl2", 5, 1)]
    [InlineData("pppDrawMdlSemi2", 98, 2)]
    [InlineData("pppDrawMdlSemi3", 193, 6)]
    [InlineData("pppKeMdlDtt", 4, 5)]
    [InlineData("pppKeLnsLp", 70, 3)]
    [InlineData("pppKeGrvTgt", 22, 1)]
    [InlineData("pppKeMdlTfd", 198, 6)]
    [InlineData("pppKeMdlTfdUv", 515, 12)]
    public void NativePayloadFields_SaveCopyChangesOnlyTheAuthorizedOperandBytes(string family, int id, int writableCount)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == family && s.Keyframes.Count > 0);
        var fields = document.ResolveKeyframeFields(slot, 0);
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(schema.Fields.Count, fields.Count);
        var writable = fields.Where(f => MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)).ToArray();
        Assert.Equal(writableCount, writable.Length);
        byte[] original = File.ReadAllBytes(source);
        byte[] expected = (byte[])original.Clone();
        var edits = new List<(FFXProjectEditor.FfxLib.MagicDll.MagicField, byte[])>();
        foreach (var field in writable)
        {
            byte[] value = field.Type switch
            {
                NativeType.F32 => BitConverter.GetBytes(0.375f),
                NativeType.S32 => BitConverter.GetBytes(17),
                NativeType.U16 => BitConverter.GetBytes((ushort)7),
                NativeType.U8 => new byte[] { 3 },
                _ => throw new InvalidOperationException("Unexpected operand type")
            };
            edits.Add((field, value));
            value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        }
        foreach (var field in fields.Where(f => f.Offset < 8))
        {
            Assert.False(document.TryApplyFieldEdit(field, new byte[field.Width], out _));
            Assert.Equal(original, document.WorkingBytes);
        }
        Assert.True(document.TryApplyFieldEdits(edits, out error), error);
        string dir = Path.Combine(TestDataPaths.RepoRoot, "work", "magic-editor-completion-20261003",
            "matrix-draw-tests", Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(dir);
        try
        {
            string destination = Path.Combine(dir, Path.GetFileName(source));
            Assert.True(document.TrySaveCopy(destination, out error), error);
            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Equal(original, File.ReadAllBytes(source));
            var reloaded = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() }).Parse(destination);
            Assert.Equal(expected, reloaded.Serialize());
            Assert.Equal(document.ParsedFile.Roots.Sum(r => r.TotalSlots), reloaded.Roots.Sum(r => r.TotalSlots));
        }
        finally { Directory.Delete(dir, true); }
    }
}
