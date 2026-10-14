using System;
using System.Linq;
using System.IO;
using FFXProjectEditor.Tests.Infrastructure;
using System.Text;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;
using NativeType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicRandomPackedSchemaTests
{
    [Theory]
    [InlineData("pppRandCV", false)]
    [InlineData("pppRandUpCV", false)]
    [InlineData("pppRandDownCV", false)]
    [InlineData("pppSRandCV", false)]
    [InlineData("pppSRandUpCV", false)]
    [InlineData("pppSRandDownCV", false)]
    [InlineData("pppRandHCV", true)]
    [InlineData("pppRandUpHCV", true)]
    [InlineData("pppRandDownHCV", true)]
    [InlineData("pppSRandHCV", true)]
    [InlineData("pppSRandUpHCV", true)]
    [InlineData("pppSRandDownHCV", true)]
    public void PackedSchema_MatchesFourSignedComponentsAndOneMode(string opcode, bool wide)
    {
        var schema = MagicFieldMap.LoadEmbedded().Families[opcode];
        int[] offsets = wide ? new[] { 4, 8, 10, 12, 14, 16 } : new[] { 4, 8, 9, 10, 11, 12 };
        Assert.Equal(offsets, schema.Fields.Select(f => f.Offset));
        Assert.Equal(NativeType.S32, schema.Fields[0].Type);
        Assert.All(schema.Fields.Skip(1).Take(4), f =>
        {
            Assert.Equal(wide ? NativeType.S16 : NativeType.S8, f.Type);
            Assert.Equal(wide ? 2 : 1, f.Width);
        });
        Assert.Equal(NativeType.U8, schema.Fields.Last().Type);
        Assert.Equal(new MagicWindow(4, wide ? 13 : 9), schema.Window);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 4, 4));
    }

    [Theory]
    [InlineData(0x80, -128)]
    [InlineData(0xFF, -1)]
    [InlineData(0x7F, 127)]
    public void SignedByteDecoder_PreservesNegativeAmplitudes(int bits, int value)
    {
        Assert.True(MagicField.TryParseType("s8", out var type));
        byte[] record = new byte[16];
        record[8] = (byte)bits;
        var field = MagicField.CreateResolved(new MagicFieldSpec("amplitude", 8, 1, type, ""),
            "pppRandCV", record, 0, MagicSlot.ComputeSha256Hex(record));
        Assert.Equal(value, field.ValueInt);
        Assert.Null(field.ValueUInt);
        Assert.Equal("s8", field.TypeName);
    }

    [Theory]
    [InlineData("pppRandHCV", 106)]
    [InlineData("pppRandUpHCV", 22)]
    [InlineData("pppRandDownHCV", 149)]
    [InlineData("pppSRandHCV", 275)]
    [InlineData("pppSRandUpHCV", 472)]
    [InlineData("pppSRandDownHCV", 96)]
    public void ObservedHalfwordFamily_SaveCopyChangesOnlyItsNativeComponents(string opcode, int id)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:D4}.dll");
        var doc = new MagicDllDocument_Wrapper();
        Assert.True(doc.TryLoad(source, out var error), error);
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == opcode);
        var fields = doc.ResolveAllFields(slot);
        Assert.Equal(new[] { 4, 8, 10, 12, 14, 16 }, fields.Select(f => f.Offset));
        var edits = fields.Where(f => f.Offset >= 8).Select(f => (f,
            f.Offset == 16 ? new byte[] { 1 } : BitConverter.GetBytes((short)-123))).ToArray();
        byte[] original = doc.WorkingBytes!, expected = (byte[])original.Clone();
        foreach (var (field, bytes) in edits)
            bytes.CopyTo(expected, doc.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        Assert.True(doc.TryApplyFieldEdits(edits, out error), error);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(doc.TryApplyFieldEdit(fields[0], BitConverter.GetBytes(0), out _));
        Assert.Equal(expected, doc.WorkingBytes);
    }

    [Theory]
    [InlineData("-128", true, 0x80)]
    [InlineData("-1", true, 0xFF)]
    [InlineData("127", true, 0x7F)]
    [InlineData("128", false, 0)]
    [InlineData("-129", false, 0)]
    public async Task SignedByteEditor_SavesOnlyAnInRangeNativeByte(string value, bool accepted, int bits)
    {
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        byte[] original = File.ReadAllBytes(fixture.Source);
        var loaded = new MagicDllParser().Parse(fixture.Source);
        var rdata = loaded.Sections.Single(s => s.Name == ".rdata");
        byte[] oldName = Encoding.ASCII.GetBytes("pppRandFV\0");
        int at = original.AsSpan(rdata.RawPtr, rdata.RawSize).IndexOf(oldName);
        Assert.True(at >= 0);
        Encoding.ASCII.GetBytes("pppRandCV\0").CopyTo(original, rdata.RawPtr + at);
        File.WriteAllBytes(fixture.Source, original);
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var field = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicFieldNode>()
            .First(n => n.SourceField?.OpcodeName == "pppRandCV" && n.Offset == 8);
        Assert.Equal(FFXProjectEditor.Modules.MagicDllEditor.MagicFieldType.S8, field.Type);
        Assert.True(field.IsNumeric);
        Assert.Equal(-128, field.MinValue);
        Assert.Equal(127, field.MaxValue);
        field.Value = value;
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.Equal(accepted, File.Exists(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(fixture.Source));
        if (accepted)
        {
            byte[] expected = (byte[])original.Clone();
            expected[fixture.Raw + field.SourceField!.RecordOffset + 8] = (byte)bits;
            Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        }
    }
}
