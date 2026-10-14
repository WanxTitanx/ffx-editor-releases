using System;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;
using UiType = FFXProjectEditor.Modules.MagicDllEditor.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicLoopSchemaTests
{
    [Theory]
    [InlineData("pppMoveLoop", NativeType.F32)]
    [InlineData("pppSclMoveLoop", NativeType.F32)]
    [InlineData("pppPointLoop", NativeType.F32)]
    [InlineData("pppScaleLoop", NativeType.F32)]
    [InlineData("pppAngMoveLoop", NativeType.S32)]
    [InlineData("pppAngleLoop", NativeType.S32)]
    public void VectorLoops_UseThreeNativeComponentsAndLeaveTheFourthLaneUntouched(string family, NativeType type)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new[] { 16, 20, 24 }, schema.Fields.Select(f => f.Offset));
        Assert.All(schema.Fields, f => Assert.Equal(type, f.Type));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 28, 4));
    }

    [Theory]
    [InlineData("pppDrawMdlLoop", 30, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U16,34:U8,35:U8,36:U8,37:U8")]
    [InlineData("pppDrawMdlLoopZ", 31, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U16,34:U8,35:U8,36:U8,37:U8,38:U8")]
    [InlineData("pppDrawMdlLoopDisPos", 31, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U16,34:U8,35:U8,36:U8,37:U8,38:U8")]
    [InlineData("pppDrawMdlCameraLoop", 34, "4:U32,8:F32,12:F32,16:F32,20:F32,24:F32,28:F32,32:U8,33:U8,34:U8,35:U8,36:U16,38:U8,39:U8,40:U8,41:U8")]
    public void DrawLoops_ExposeCompleteNativeInitialValuesControlsAndWordOverride(string family, int width, string operands)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        Assert.Equal(new MagicWindow(8, width), schema.Window);
        Assert.Equal(operands, string.Join(",", schema.Fields.Select(f => $"{f.Offset}:{f.Type}")));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        Assert.All(schema.Fields.Where(f => f.Offset >= 8), f =>
            Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)));
    }

    [Theory]
    [InlineData("pppDrawMdlLoop", 32, 34)]
    [InlineData("pppDrawMdlLoopZ", 32, 34)]
    [InlineData("pppDrawMdlLoopDisPos", 32, 34)]
    [InlineData("pppDrawMdlCameraLoop", 36, 38)]
    public void WordOverride_DecodesBothBytesWithoutConsumingItsNeighbor(string family, int wordOffset, int controlOffset)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        var word = schema.Fields.Single(f => f.Offset == wordOffset);
        var control = schema.Fields.Single(f => f.Offset == controlOffset);
        byte[] record = new byte[48];
        record[wordOffset] = 0x34;record[wordOffset + 1] = 0x12;record[controlOffset] = 0x56;
        Assert.Equal(0x1234u, MagicField.CreateResolved(word, family, record, 0, MagicSlot.ComputeSha256Hex(record)).ValueUInt);
        Assert.Equal(0x56u, MagicField.CreateResolved(control, family, record, 0, MagicSlot.ComputeSha256Hex(record)).ValueUInt);
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
    public void LoopUi_DistinguishesInitializationFromKeyframeImpulses(string cultureName)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var schema = MagicFieldMap.LoadEmbedded().Families["pppDrawMdlCameraLoop"];
            var nodes = new[] { 8, 12, 36, 38 }.Select(offset =>
            {
                var spec = schema.Fields.Single(f => f.Offset == offset);
                var field = MagicField.CreateResolved(spec, schema.Opcode, new byte[48], 0, "");
                var type = spec.Type switch
                {
                    NativeType.F32 => UiType.F32,
                    NativeType.U16 => UiType.U16,
                    _ => UiType.U8
                };
                return new MagicFieldNode(spec.Name, type, offset, spec.Width, spec.Semantics, "") { SourceField = field };
            }).ToArray();
            Assert.NotEqual(nodes[0].Label, nodes[1].Label);
            Assert.NotEqual(nodes[0].SemanticText, nodes[1].SemanticText);
            Assert.All(nodes, node =>
            {
                Assert.NotEqual(node.Name, node.Label);
                Assert.DoesNotContain("{0}", node.Label);
                Assert.NotEqual(node.Semantic, node.SemanticText);
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
