using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicKeyframeEditorTests
{
    [Fact]
    public async Task OpenTimeline_ExposesOperandsFromEveryKeyframe()
    {
        using var fixture = new TimelineFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));

        for (int index = 0; index < 3; index++)
        {
            var fields = Walk(vm.RootNodes).OfType<MagicFieldNode>()
                .Where(n => n.SourceField?.RecordOffset == fixture.RecordOffset + 32 * index)
                .ToArray();
            Assert.Equal(4, fields.Length);
            Assert.All(fields, n => Assert.True(n.IsValueEditable));
            Assert.Equal(index + 1.0, fields[0].NumericValue);
        }
        Assert.False(vm.Document.IsDirty);
    }

    [Fact]
    public async Task EditLaterKeyframe_SaveCopy_ChangesOnlySelectedOperand()
    {
        using var fixture = new TimelineFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var field = Walk(vm.RootNodes).OfType<MagicFieldNode>().FirstOrDefault(n =>
            n.SourceField?.RecordOffset == fixture.RecordOffset + 32 && n.Offset == 16);
        Assert.NotNull(field);
        field!.Value = "7.5";
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(fixture.Destination), string.Join(" | ", vm.Log.Select(n => n.Text)));
        byte[] original = File.ReadAllBytes(fixture.Source);
        byte[] saved = File.ReadAllBytes(fixture.Destination);
        int at = fixture.DataRaw + fixture.RecordOffset + 32 + 16;
        Assert.Equal(7.5f, BitConverter.ToSingle(saved, at));
        Assert.Equal(original[..at], saved[..at]);
        Assert.Equal(original[(at + 4)..], saved[(at + 4)..]);
        Assert.Equal(original, File.ReadAllBytes(fixture.Destination + ".bak"));
        Assert.False(field.IsDirty);
        vm.RefreshCloneDiffCommand.Execute(null);
        Assert.Contains($"record @0x{fixture.RecordOffset + 32:X}", vm.CloneDiffDetail);
    }

    [Fact]
    public async Task FilterAfterEditing_PreservesHiddenPendingOperand()
    {
        using var fixture = new TimelineFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var field = Walk(vm.RootNodes).OfType<MagicFieldNode>().First(n =>
            n.SourceField?.RecordOffset == fixture.RecordOffset && n.Offset == 16);
        field.Value = "9.5";
        vm.SlotFilter = "pppScale";
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(fixture.Destination), string.Join(" | ", vm.Log.Select(n => n.Text)));
        Assert.Equal(9.5f, BitConverter.ToSingle(File.ReadAllBytes(fixture.Destination),
            fixture.DataRaw + fixture.RecordOffset + 16));
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
    public async Task OpenAnotherDocument_DropsOldPendingBuffersAndAnchors()
    {
        using var first = new TimelineFixture();
        using var second = new TimelineFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(first.Source));
        var oldField = Walk(vm.RootNodes).OfType<MagicFieldNode>().First(n =>
            n.SourceField?.RecordOffset == first.RecordOffset && n.Offset == 16);
        oldField.Value = "99";
        Assert.True(vm.IsDirty);
        Assert.True(await vm.OpenFileAsync(second.Source));
        Assert.False(vm.IsDirty);
        var field = Walk(vm.RootNodes).OfType<MagicFieldNode>().First(n =>
            n.SourceField?.RecordOffset == second.RecordOffset && n.Offset == 16);
        Assert.Equal("1", field.Value);
        vm.SaveFilePicker = () => Task.FromResult<string?>(second.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.Equal(File.ReadAllBytes(second.Source), File.ReadAllBytes(second.Destination));
    }

    [Fact]
    public async Task InvalidPendingValue_DoesNotPartiallyApplyEarlierFields()
    {
        using var fixture = new TimelineFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var fields = Walk(vm.RootNodes).OfType<MagicFieldNode>()
            .Where(n => n.SourceField?.RecordOffset == fixture.RecordOffset).ToArray();
        fields[0].Value = "9.5";
        fields[1].Value = "invalid";
        byte[] previous = vm.Document.WorkingBytes!;
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.False(File.Exists(fixture.Destination));
        Assert.Equal(previous, vm.Document.WorkingBytes);
    }

    [Fact]
    public void RejectedLateAnchor_RollsBackTheWholeInMemoryTransaction()
    {
        using var fixture = new TimelineFixture();
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(fixture.Source, out string error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs)
            .SelectMany(p => p.Slots).First(s => s.RecordOffset == fixture.RecordOffset);
        var field = document.ResolveKeyframeFields(slot, 0)[0];
        var spec = new FFXProjectEditor.FfxLib.MagicDll.MagicFieldSpec(field.Name, field.Offset,
            field.Width, field.Type, field.Semantics);
        var forged = FFXProjectEditor.FfxLib.MagicDll.MagicField.CreateResolved(spec,
            slot.OpcodeName!, slot.Keyframes[0].Record, fixture.RecordOffset + 4096,
            slot.Keyframes[0].RecordSha256);
        byte[] before = document.WorkingBytes!;
        string sha = document.ShaAfter;
        Assert.False(document.TryApplyFieldEdits(new[]
        {
            (field, BitConverter.GetBytes(8.5f)),
            (forged, BitConverter.GetBytes(9.5f)),
        }, out _));
        Assert.Equal(before, document.WorkingBytes);
        Assert.Equal(sha, document.ShaAfter);
        Assert.False(document.IsDirty);
    }

    private sealed class TimelineFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(TestDataPaths.RepoRoot, "work",
            "magic-editor-completion-20261003", "fixtures", Guid.NewGuid().ToString("N"));
        internal string Source { get; }
        internal string Destination => Path.Combine(_directory, "magic_edited.dll");
        internal int RecordOffset { get; }
        internal int DataRaw { get; }

        internal TimelineFixture()
        {
            TestDirectory.CreatePrivate(_directory);
            Source = MagicDllTestFixture.Write(_directory, "magic_0021.dll");
            var document = new MagicDllDocument_Wrapper();
            Assert.True(document.TryLoad(Source, out string error), error);
            var file = document.ParsedFile!;
            var program = file.Roots.SelectMany(r => r.Programs)
                .First(p => p.Slots.Any(s => s.OpcodeName == "pppSclMove"));
            var slot = program.Slots.First(s => s.OpcodeName == "pppSclMove");
            DataRaw = file.DataSectionRawPtr;
            RecordOffset = program.SectionAbs + 0x4800;
            byte[] bytes = File.ReadAllBytes(Source);
            BitConverter.GetBytes((ushort)32).CopyTo(bytes, DataRaw + slot.SlotAbs + 4);
            BitConverter.GetBytes((ushort)3).CopyTo(bytes, DataRaw + slot.SlotAbs + 6);
            BitConverter.GetBytes(0x4800u).CopyTo(bytes, DataRaw + slot.SlotAbs + 8);
            for (int index = 0; index < 3; index++)
            {
                int at = DataRaw + RecordOffset + 32 * index;
                BitConverter.GetBytes((uint)(index * 0x1000)).CopyTo(bytes, at);
                for (int component = 16; component < 32; component += 4)
                    BitConverter.GetBytes((float)(index + 1)).CopyTo(bytes, at + component);
            }
            BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes, DataRaw + RecordOffset + 96);
            File.WriteAllBytes(Source, bytes);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
