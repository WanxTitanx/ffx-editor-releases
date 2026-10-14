using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicFieldPermissionTests
{
    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("\"true\"", false)]
    [InlineData("null", false)]
    public void ExplicitFieldPermission_IsRespectedAndMalformedValuesAreReadOnly(string editable, bool expected)
    {
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        string path = Path.ChangeExtension(fixture.Destination, ".json");
        File.WriteAllText(path, "{\"pppPermissionTest\":{\"payload_consumer\":true,\"editable\":true," +
            "\"window\":{\"start\":8,\"width\":16},\"fields\":[" +
            "{\"name\":\"guarded\",\"offset\":8,\"width\":4,\"type\":\"s32\",\"editable\":" + editable + "}," +
            "{\"name\":\"ordinary\",\"offset\":16,\"width\":4,\"type\":\"f32\"}]}}");
        var schema = MagicFieldMap.Load(path).Families["pppPermissionTest"];
        Assert.Equal(expected, MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
        Assert.True(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 16, 4));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 12, 4));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 16, 2));
    }

    [Fact]
    public void WritableAlias_CannotOverwriteAnOverlappingReadOnlyOperand()
    {
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        string path = Path.ChangeExtension(fixture.Destination, ".json");
        File.WriteAllText(path, "{\"pppPermissionTest\":{\"payload_consumer\":true,\"editable\":true," +
            "\"window\":{\"start\":8,\"width\":4},\"fields\":[" +
            "{\"name\":\"alias\",\"offset\":8,\"width\":4,\"type\":\"s32\"}," +
            "{\"name\":\"protected\",\"offset\":10,\"width\":2,\"type\":\"s16\",\"editable\":false}]}}");
        var schema = MagicFieldMap.Load(path).Families["pppPermissionTest"];
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 8, 4));
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 10, 2));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(28)]
    public void BornEmitter_ProgramAndStateReferencesAreTypedReadOnly(int offset)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppKeBornRnd5"];
        var spec = Assert.Single(schema.Fields, f => f.Offset == offset);
        Assert.Equal(NativeType.S32, spec.Type);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, offset, spec.Width));
    }

    [Fact]
    public void RealEmitter_RejectsReferenceWritesAtomicallyWhileAllowingItsControlBytes()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0009.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppKeBornRnd5");
        var fields = document.ResolveKeyframeFields(slot, 0);
        var control = fields.Single(f => f.Offset == 12);
        byte[] original = document.WorkingBytes!;
        foreach (var reference in fields.Where(f => f.Offset >= 16))
        {
            Assert.False(document.TryApplyFieldEdits(new[]
            {
                (control, new byte[] { 1 }), (reference, BitConverter.GetBytes(int.MaxValue))
            }, out _));
            Assert.Equal(original, document.WorkingBytes);
        }
        Assert.True(document.TryApplyFieldEdit(control, new byte[] { 1 }, out error), error);
        byte[] expected = (byte[])original.Clone();
        expected[document.ParsedFile.DataSectionRawPtr + control.RecordOffset + control.Offset] = 1;
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }
}
