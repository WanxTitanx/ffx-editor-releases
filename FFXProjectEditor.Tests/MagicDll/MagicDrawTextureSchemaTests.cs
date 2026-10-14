using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;
using UiType = FFXProjectEditor.Modules.MagicDllEditor.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicDrawTextureSchemaTests
{
    [Theory]
    [InlineData("pppDrawMdlTs", 8, 24, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32")]
    [InlineData("pppDrawMdlTs2", 8, 25, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U8")]
    [InlineData("pppDrawMdlTs3", 8, 29, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U8,33:U8,34:U8,35:U8,36:U8")]
    [InlineData("pppDrawMdl3", 8, 5, "4:U32,8:U8,9:U8,10:U8,11:U8,12:U8")]
    [InlineData("pppDrawShapeX", 8, 7, "4:U32,8:U16,12:U8,13:U8,14:U8")]
    [InlineData("pppKeZCrct", 16, 33, "16:F32,20:F32,24:F32,28:F32,32:F32,36:F32,40:F32,44:F32,48:U8")]
    [InlineData("pppKeZCrctShp", 16, 33, "16:F32,20:F32,24:F32,28:F32,32:F32,36:F32,40:F32,44:F32,48:U8")]
    [InlineData("pppVertexApLc", 4, 5, "4:U16,6:U8,7:U8,8:U8")]
    public void Schema_MatchesPayloadReadsRatherThanHostState(string family, int start, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.True(schema.PayloadConsumer);
        Assert.True(schema.Editable);
        Assert.Equal(new MagicWindow(start, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.All(schema.Fields, field =>
        {
            Assert.Equal(field.Type is NativeType.U8 ? 1 : field.Type is NativeType.U16 ? 2 : 4, field.Width);
            Assert.Equal(field.Offset >= 8,
                MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, field.Offset, field.Width));
        });
    }

    [Theory]
    [InlineData("pppDrawMdlTs", 3, 6)]
    [InlineData("pppDrawMdlTs2", 5, 7)]
    [InlineData("pppDrawMdlTs3", 6, 11)]
    [InlineData("pppDrawMdl3", 6, 5)]
    [InlineData("pppDrawShapeX", 6, 4)]
    [InlineData("pppKeZCrct", 6, 9)]
    [InlineData("pppKeZCrctShp", 6, 9)]
    [InlineData("pppVertexApLc", 4, 1)]
    public void RealNativeFields_SaveAndReloadOnlyTheirOperandBytes(string family, int id, int count)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == family && s.Keyframes.Count > 0);
        var fields = document.ResolveKeyframeFields(slot, 0);
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        var writable = fields.Where(f => MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)).ToArray();
        Assert.Equal(count, writable.Length);
        byte[] original = File.ReadAllBytes(source), expected = (byte[])original.Clone();
        foreach (var field in fields.Where(f => f.Offset < 8))
        {
            Assert.False(document.TryApplyFieldEdit(field, new byte[field.Width], out _));
            Assert.Equal(original, document.WorkingBytes);
        }
        var edits = new List<(MagicField Field, byte[] Value)>();
        foreach (var field in writable)
        {
            byte[] value = field.Type switch
            {
                NativeType.F32 => BitConverter.GetBytes(0.375f),
                NativeType.U16 => BitConverter.GetBytes((ushort)7),
                NativeType.U8 => new byte[] { 1 },
                _ => throw new InvalidOperationException("Unexpected native operand type")
            };
            edits.Add((field, value));
            value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        }
        Assert.True(document.TryApplyFieldEdits(edits, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void ReviewedNativeFields_UseLocalizedLabelsAndHints(string cultureName)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var document = new MagicDllDocument_Wrapper();
            Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, "magic_0006.dll"), out var error), error);
            foreach (string family in new[] { "pppKeZCrct", "pppKeZCrctShp", "pppDrawMdlTs3", "pppDrawMdl3", "pppDrawShapeX" })
            {
                var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
                    .First(s => s.OpcodeName == family);
                foreach (var field in document.ResolveKeyframeFields(slot, 0).Where(f => f.Offset >= 8))
                {
                    var type = field.Type switch
                    {
                        NativeType.F32 => UiType.F32,
                        NativeType.U8 => UiType.U8,
                        NativeType.U16 => UiType.U16,
                        _ => throw new InvalidOperationException("Unexpected native UI type")
                    };
                    var node = new MagicFieldNode(field.Name, type, field.Offset, field.Width,
                        field.Semantics, "", schemaAllowsWrite: true) { SourceField = field };
                    Assert.NotEqual(field.Name, node.Label);
                    Assert.NotEqual(field.Semantics, node.SemanticText);
                    Assert.DoesNotContain("{0}", node.Label);
                    Assert.False(string.IsNullOrWhiteSpace(node.SemanticText));
                    if (cultureName == "pt")
                        Assert.DoesNotContain("native", node.SemanticText, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
