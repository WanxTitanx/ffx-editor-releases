using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicDrawShapeVariantSchemaTests
{
    [Theory]
    [InlineData("pppDrawShapeField", false, false)]
    [InlineData("pppDrawShapeFieldRev", false, true)]
    [InlineData("pppDrawShapeFieldGlobal", false, false)]
    [InlineData("pppDrawShapeFieldSpd", true, false)]
    [InlineData("pppDrawShapeCamera", true, false)]
    [InlineData("pppDrawShapeCameraDisPos", true, false)]
    public void ShapeSchema_ContainsTheCompleteNativeHelperOperandLayout(string family, bool compact, bool reverse)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[family];
        var expected = new List<(int Offset, NativeType Type)> { (4, NativeType.U32), (8, NativeType.S32), (12, NativeType.U16) };
        AddTriples(expected, new[] { 16 });expected.Add((32, NativeType.U8));
        AddTriples(expected, compact
            ? new[] { family == "pppDrawShapeFieldSpd" ? 64 : 48, family == "pppDrawShapeFieldSpd" ? 80 : 64, 80, 96, 112, 128, 144, 160, 192, 208 }
            : new[] { 64, 80, 96, 112, 128, 144, 160, 176, 208, 224 });
        int controls = compact ? 176 : 192;
        for (int offset = controls; offset <= controls + 4; offset++) expected.Add((offset, NativeType.U8));
        expected.Add((compact ? 224 : 240, NativeType.U8));
        if (reverse) expected.Add((241, NativeType.U8));
        expected = expected.Distinct().OrderBy(f => f.Offset).ToList();
        Assert.Equal(expected, schema.Fields.Select(f => (f.Offset, f.Type)).ToList());
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
        // This value sizes unchecked allocations and iteration; arbitrary editing is not a numeric contract.
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 12, 2));
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, compact ? 224 : 240, 1));
        Assert.Equal(new MagicWindow(8, (reverse ? 242 : compact ? 225 : 241) - 8), schema.Window);
    }

    private static void AddTriples(List<(int Offset, NativeType Type)> values, IEnumerable<int> starts)
    {
        foreach (int start in starts)
            for (int lane = 0; lane < 3; lane++) values.Add((start + 4 * lane, NativeType.F32));
    }
}
