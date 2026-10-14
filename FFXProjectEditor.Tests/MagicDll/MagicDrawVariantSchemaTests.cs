using System;
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

public class MagicDrawVariantSchemaTests
{
    [Theory]
    [InlineData("pppDrawMdlCamera", 34, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U8,33:U8,34:U16,36:U8,37:U8,38:U8,39:U8,40:U8,41:U8")]
    [InlineData("pppDrawMdlInf", 137, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:F32,36:F32,40:F32,64:F32,68:F32,72:F32,80:F32,84:F32,88:F32,96:F32,100:F32,104:F32,112:F32,116:F32,120:F32,128:U8,129:U8,130:U16,132:U8,133:U8,134:U8,135:U8,136:U8,137:U8,140:F32,144:U8")]
    [InlineData("pppDrawMdlRev", 5, "4:U32,8:U8,9:U8,10:U8,11:U8,12:U8")]
    [InlineData("pppDrawMdlSea", 80, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:F32,36:F32,40:F32,64:F32,68:F32,72:F32,80:U8,81:U8,82:U16,84:U8,85:U8,86:U8,87:U8")]
    [InlineData("pppDrawMdlSemi", 2, "4:U32,8:U16")]
    public void MdlSchema_ContainsOnlyNativeRecordOperands(string family, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(8, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        Assert.DoesNotContain(schema.Fields, f => f.Offset == 148 && family == "pppDrawMdlRev");
    }

    [Fact]
    public void PSimSchema_ExposesVerifiedScalarsAndPreservesServiceReferences()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppDrawMdlPSim"];
        Assert.Equal(new MagicWindow(8, 122), schema.Window);
        Assert.All(new[] { 122, 124, 126, 128 }, offset =>
        {
            var reference = Assert.Single(schema.Fields, f => f.Offset == offset);
            Assert.Equal(NativeType.S16, reference.Type);
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, offset, 2));
        });
        // The gravity service overwrites its input vector before inserting it.
        Assert.All(new[] { 48, 52, 56 }, offset =>
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, offset, 4)));
        Assert.Equal(NativeType.U16, Assert.Single(schema.Fields, f => f.Offset == 32).Type);
        Assert.Equal(NativeType.F32, Assert.Single(schema.Fields, f => f.Offset == 36).Type);
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 36, 4));
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 120, 1));
    }

    [Fact]
    public void RealSemi_TextureWordSavePreservesSourceAndEveryOtherByte()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0153.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppDrawMdlSemi");
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 8);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        byte[] replacement = BitConverter.GetBytes((ushort)0x1234);
        replacement.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        Assert.True(document.TryApplyFieldEdit(field, replacement, out error), error);
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
    public void NativeVariantUi_LocalizesEditableAndInspectedOperands(string cultureName)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var item in new[] { ("pppDrawMdlSemi", 8), ("pppDrawMdlPSim", 124), ("pppDrawShapeField", 8) })
            {
                var schema = MagicFieldMap.LoadEmbedded().Families[item.Item1];
                var spec = schema.Fields.Single(f => f.Offset == item.Item2);
                var field = MagicField.CreateResolved(spec, schema.Opcode, new byte[256], 0, "");
                var type = spec.Type switch { NativeType.U16 => UiType.U16, NativeType.S16 => UiType.S16, _ => UiType.S32 };
                var node = new MagicFieldNode(spec.Name, type, spec.Offset, spec.Width, spec.Semantics, "",
                    schemaAllowsWrite: MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, spec.Offset, spec.Width)) { SourceField = field };
                Assert.NotEqual(spec.Name, node.Label);
                Assert.DoesNotContain("{0}", node.Label);
                Assert.NotEqual(spec.Semantics, node.SemanticText);
                Assert.Equal(item.Item2 == 8 && item.Item1 == "pppDrawMdlSemi", node.IsValueEditable);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
