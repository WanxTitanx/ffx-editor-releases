using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using MagicFieldType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicKeThSchemaTests
{
    [Theory]
    [InlineData(84, "25", 25)]
    [InlineData(84, "0xFFFF", 65535)]
    [InlineData(84, "65536", -1)]
    [InlineData(84, "-1", -1)]
    [InlineData(86, "255", 255)]
    [InlineData(86, "0xFF", 255)]
    [InlineData(86, "256", -1)]
    [InlineData(86, "1.5", -1)]
    public async Task UnsignedUiOperands_SaveDecimalOrExplicitHexWithinNativeBounds(int offset, string value, int expected)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0021.dll");
        byte[] original = File.ReadAllBytes(source);
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(source));
        var field = Walk(vm.RootNodes).OfType<MagicFieldNode>().First(f =>
            f.SourceField?.OpcodeName == "pppKeTh" && f.Offset == offset && f.IsValueEditable);
        int at = vm.Document.ParsedFile!.DataSectionRawPtr + field.SourceField!.RecordOffset + offset;
        int initial = field.Width == 2 ? BitConverter.ToUInt16(original, at) : original[at];
        Assert.Equal(initial.ToString(CultureInfo.InvariantCulture), field.Value);
        field.Value = value;
        string dir = Path.Combine(TestDataPaths.RepoRoot, "work", "magic-editor-completion-20261003",
            "handler-tests", "keth-ui-" + Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(dir);
        try
        {
            string destination = Path.Combine(dir, "magic_0021.dll");
            vm.SaveFilePicker = () => Task.FromResult<string?>(destination);
            await vm.SaveCopyCommand.ExecuteAsync(null);
            if (expected < 0)
            {
                Assert.False(File.Exists(destination));
                Assert.Equal(original, vm.Document.WorkingBytes);
            }
            else
            {
                Assert.True(File.Exists(destination), string.Join(" | ", vm.Log.Select(l => l.Text)));
                byte[] expectedBytes = (byte[])original.Clone();
                byte[] operand = field.Width == 2 ? BitConverter.GetBytes((ushort)expected) : new[] { (byte)expected };
                operand.CopyTo(expectedBytes, at);
                Assert.Equal(expectedBytes, File.ReadAllBytes(destination));
            }
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static IEnumerable<MagicNode> Walk(IEnumerable<MagicNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children)) yield return child;
        }
    }

    [Fact]
    public void KeTh_ExposesThe28DisjointNativeOperands()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppKeTh"];
        Assert.Equal("0x736110", schema.HandlerAddr);
        Assert.Equal(new MagicWindow(16, 72), schema.Window);
        var expected = Enumerable.Range(0, 16).Select(i => (16 + i * 2, 2, MagicFieldType.U16))
            .Concat(Enumerable.Range(0, 9).Select(i => (48 + i * 4, 4, MagicFieldType.F32)))
            .Concat(new[] { (84, 2, MagicFieldType.U16), (86, 1, MagicFieldType.U8), (87, 1, MagicFieldType.U8) });
        Assert.Equal(expected, schema.Fields.Select(f => (f.Offset, f.Width, f.Type)));
        Assert.All(schema.Fields, f => Assert.True(
            MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, f.Offset, f.Width)));
    }

    [Fact]
    public void KeThSft_UsesExecHandlerInsteadOfConstructor()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppKeThSft"];
        Assert.Equal("0x736F50", schema.HandlerAddr);
        Assert.Equal(new MagicWindow(8, 49), schema.Window);
        Assert.Equal(21, schema.Fields.Count);
    }

    [Fact]
    public void DrawMdl_DoesNotExposeHostStateAsRecordPayload()
    {
        var schema = MagicFieldMap.LoadEmbedded().Families["pppDrawMdl"];
        Assert.False(schema.Editable);
        Assert.False(schema.PayloadConsumer);
        Assert.Null(schema.Window);
        Assert.Empty(schema.Fields);
        Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, 16, 4));
    }

    [Theory]
    [InlineData("pppKeTh", 28)]
    [InlineData("pppKeThSft", 21)]
    public void EveryNativeOperand_RoundTripsWithoutChangingOtherBytes(string family, int fieldCount)
    {
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
        string? source = null;
        int record = -1;
        foreach (string path in Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll").OrderBy(p => p))
        {
            var file = parser.Parse(path);
            if (MagicFpHandlerTable.TryExtractFromDll(file)?.NativeTableCount != 1) continue;
            var slot = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
                .FirstOrDefault(s => s.OpcodeName == family && s.NativeRecordWidth >= (family == "pppKeTh" ? 88 : 57)
                    && s.Keyframes.Count > 0);
            if (slot == null) continue;
            source = path;
            record = slot.RecordOffset;
            break;
        }
        Assert.NotNull(source);
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source!, out var error), error);
        var selected = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.RecordOffset == record && s.OpcodeName == family);
        var fields = document.ResolveKeyframeFields(selected, 0);
        Assert.Equal(fieldCount, fields.Count);
        byte[] original = File.ReadAllBytes(source!);
        byte[] expected = (byte[])original.Clone();
        foreach (var field in fields)
        {
            byte[] value = field.Type switch
            {
                MagicFieldType.U8 => new byte[] { 13 },
                MagicFieldType.U16 => BitConverter.GetBytes((ushort)42),
                MagicFieldType.F32 => BitConverter.GetBytes(1.25f),
                _ => throw new InvalidOperationException("Unexpected native operand type")
            };
            Assert.True(document.TryApplyFieldEdit(field, value, out error), error);
            value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + record + field.Offset);
        }
        Assert.Equal(expected, document.WorkingBytes);
        string dir = Path.Combine(TestDataPaths.RepoRoot, "work", "magic-editor-completion-20261003",
            "handler-tests", "keth-copy-" + Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(dir);
        try
        {
            string destination = Path.Combine(dir, Path.GetFileName(source!));
            Assert.True(document.TrySaveCopy(destination, out error), error);
            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Equal(expected, parser.Parse(destination).FileBytes);
            Assert.Equal(original, File.ReadAllBytes(source!));
        }
        finally { Directory.Delete(dir, true); }
    }
}
