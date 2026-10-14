using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicTimingEditorTests
{
    [Fact]
    public void Parser_SectionLifetimeDoesNotLimitItsSerializedStructure()
    {
        using var fixture = new TimingFixture();
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        BitConverter.GetBytes(4096u).CopyTo(bytes, fixture.DataRaw + fixture.SectionOffset);
        File.WriteAllBytes(fixture.Source, bytes);
        var parsed = new MagicDllParser().Parse(fixture.Source);
        Assert.Single(parsed.Roots);
        Assert.NotEmpty(parsed.Roots[0].Programs.SelectMany(p => p.Slots));
        Assert.Equal(bytes, parsed.Serialize());
    }

    [Fact]
    public async Task Open_ExposesSectionAndProgramTimesAsEditableFrames()
    {
        using var fixture = new TimingFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var timings = Walk(vm.RootNodes).Where(n => n.TypeLabel == "q12").ToArray();
        Assert.Contains(timings, n => n.IsValueEditable && n.NumericValue == 120);
        Assert.Contains(timings, n => n.IsValueEditable && n.NumericValue == 60);
        Assert.Contains(timings, n => n.IsValueEditable && n.NumericValue == 0);
        Assert.False(vm.IsDirty);
    }

    internal static IEnumerable<MagicNode> Walk(IEnumerable<MagicNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children)) yield return child;
        }
    }

    [Fact]
    public async Task EditProgramDuration_SaveCopy_ChangesOnlyItsQ12Word()
    {
        using var fixture = new TimingFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var node = Walk(vm.RootNodes).OfType<MagicTimingNode>()
            .Single(n => n.Source.Kind == MagicTimingKind.ProgramDuration);
        node.Value = "30";
        Assert.True(vm.IsDirty);
        vm.SlotFilter = "pppSclMove";
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(fixture.Destination), string.Join(" | ", vm.Log.Select(e => e.Text)));
        byte[] original = File.ReadAllBytes(fixture.Source), saved = File.ReadAllBytes(fixture.Destination);
        int at = fixture.DataRaw + fixture.ProgramOffset + 20;
        Assert.Equal(30 * 4096u, BitConverter.ToUInt32(saved, at));
        Assert.Equal(original[..at], saved[..at]);
        Assert.Equal(original[(at + 4)..], saved[(at + 4)..]);
        Assert.Equal(original, File.ReadAllBytes(fixture.Destination + ".bak"));
        Assert.False(node.IsDirty);
        vm.RefreshCloneDiffCommand.Execute(null);
        Assert.Contains("ProgramDuration", vm.CloneDiffDetail);
    }

    [Fact]
    public void SectionAndProgramDuration_CanBeReducedTogetherToOneFrame()
    {
        using var fixture = new TimingFixture();
        var document = Load(fixture);
        var section = document.TimingFields.Single(f => f.Kind == MagicTimingKind.SectionDuration);
        var duration = document.TimingFields.Single(f => f.Kind == MagicTimingKind.ProgramDuration);
        Assert.False(document.TryApplyTimingEdits(new[] { (section, 4096u) }, out _));
        Assert.False(document.IsDirty);
        Assert.True(document.TryApplyTimingEdits(new[] { (section, 4096u), (duration, 4096u) },
            out string error), error);
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        var parsed = new MagicDllParser().Parse(fixture.Destination);
        Assert.Single(parsed.Roots);
        Assert.Equal(1.0, parsed.Roots[0].Programs[0].LifetimeFrames);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4097u)]
    [InlineData(0x70000000u)]
    [InlineData(0x7FFFFFFFu)]
    [InlineData(0xFFFFFFFFu)]
    public void FractionalOrSpecialNewTicks_AreRejectedAtomically(uint tick)
    {
        using var fixture = new TimingFixture();
        var document = Load(fixture);
        var duration = document.TimingFields.Single(f => f.Kind == MagicTimingKind.ProgramDuration);
        byte[] before = document.WorkingBytes!;
        Assert.False(document.TryApplyTimingEdits(new[] { (duration, tick) }, out _));
        Assert.Equal(before, document.WorkingBytes);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void ProtectedSentinelAndForeignDocumentFields_AreRejected()
    {
        using var fixture = new TimingFixture();
        var first = Load(fixture); var second = Load(fixture);
        var special = first.TimingFields.Single(f => f.Kind == MagicTimingKind.LoopEnd);
        Assert.False(special.IsEditable);
        Assert.False(first.TryApplyTimingEdits(new[] { (special, 4096u) }, out _));
        var foreign = second.TimingFields.Single(f => f.Kind == MagicTimingKind.ProgramDuration);
        Assert.False(first.TryApplyTimingEdits(new[] { (foreign, 30 * 4096u) }, out _));
        Assert.False(first.IsDirty);
    }

    [Fact]
    public void InvalidTiming_RollsBackValidPayloadEditsInTheSameTransaction()
    {
        using var fixture = new TimingFixture();
        var document = Load(fixture);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppSclMove");
        var field = document.ResolveAllFields(slot).First(f => f.Offset == 16);
        var duration = document.TimingFields.Single(f => f.Kind == MagicTimingKind.ProgramDuration);
        byte[] before = document.WorkingBytes!;
        Assert.False(document.TryApplyDocumentEdits(new[] { (field, BitConverter.GetBytes(9.5f)) },
            new[] { (duration, 0u) }, out _));
        Assert.Equal(before, document.WorkingBytes);
        Assert.Equal(document.ShaBefore, document.ShaAfter);
    }

    private static MagicDllDocument_Wrapper Load(TimingFixture fixture)
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(fixture.Source, out string error), error);
        return document;
    }

    [Fact]
    public void FiniteLoopPair_IsValidatedAndWrittenTogether()
    {
        using var fixture = new TimingFixture();
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        BitConverter.GetBytes(10 * 4096u).CopyTo(bytes, fixture.DataRaw + fixture.ProgramOffset + 24);
        BitConverter.GetBytes(20 * 4096u).CopyTo(bytes, fixture.DataRaw + fixture.ProgramOffset + 28);
        File.WriteAllBytes(fixture.Source, bytes);
        var document = Load(fixture);
        var start = document.TimingFields.Single(f => f.Kind == MagicTimingKind.LoopStart);
        var end = document.TimingFields.Single(f => f.Kind == MagicTimingKind.LoopEnd);
        Assert.False(document.TryApplyTimingEdits(new[] { (start, 25 * 4096u) }, out _));
        Assert.False(document.IsDirty);
        Assert.True(document.TryApplyTimingEdits(new[] { (start, 25 * 4096u), (end, 30 * 4096u) }, out var error), error);
    }

    [Fact]
    public void KeyframeCollisionAndTruncation_AreRejectedWithoutPartialWrites()
    {
        using var fixture = new TimingFixture();
        var initial = Load(fixture);
        var slot = initial.ParsedFile!.Roots[0].Programs[0].Slots.First(s => s.OpcodeName == "pppSclMove");
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        int start = fixture.SectionOffset + 0x4800;
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, fixture.DataRaw + slot.SlotAbs + 6);
        BitConverter.GetBytes(0x4800u).CopyTo(bytes, fixture.DataRaw + slot.SlotAbs + 8);
        BitConverter.GetBytes(10 * 4096u).CopyTo(bytes, fixture.DataRaw + start + 64);
        BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes, fixture.DataRaw + start + 128);
        File.WriteAllBytes(fixture.Source, bytes);
        var document = Load(fixture);
        var key = document.TimingFields.Single(f => f.Kind == MagicTimingKind.KeyframeTime && f.DataOffset == start + 64);
        var life = document.TimingFields.Single(f => f.Kind == MagicTimingKind.ProgramDuration);
        Assert.False(document.TryApplyTimingEdits(new[] { (key, 0u) }, out _));
        Assert.False(document.TryApplyTimingEdits(new[] { (life, 5 * 4096u) }, out _));
        Assert.False(document.IsDirty);
        Assert.True(document.TryApplyTimingEdits(new[] { (key, 4 * 4096u), (life, 5 * 4096u) }, out var error), error);
    }

    internal sealed class TimingFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(TestDataPaths.RepoRoot, "work",
            "magic-editor-completion-20261003", "timing-fixtures", Guid.NewGuid().ToString("N"));
        internal string Source { get; }
        internal string Destination => Path.Combine(_directory, "magic_edited.dll");
        internal int SectionOffset { get; }
        internal int ProgramOffset { get; }
        internal int DataRaw { get; }

        internal TimingFixture()
        {
            TestDirectory.CreatePrivate(_directory);
            Source = MagicDllTestFixture.Write(_directory, "magic_0021.dll");
            var document = new MagicDllDocument_Wrapper();
            Assert.True(document.TryLoad(Source, out string error), error);
            var file = document.ParsedFile!;
            var program = file.Roots[0].Programs[0];
            DataRaw = file.DataSectionRawPtr;
            SectionOffset = program.SectionAbs;
            ProgramOffset = program.ProgramAbs;
            byte[] bytes = File.ReadAllBytes(Source);
            void Write(int at, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, DataRaw + at);
            Write(SectionOffset, 120 * 4096);
            Write(SectionOffset + 4, 0);
            Write(ProgramOffset + 16, 0);
            Write(ProgramOffset + 20, 60 * 4096);
            Write(ProgramOffset + 24, 0x70000000);
            Write(ProgramOffset + 28, 0x70000000);
            foreach (var slot in program.Slots)
                Write(slot.RecordOffset + slot.NativeRecordWidth * slot.KeyframeCount, 0xFFFFF000);
            File.WriteAllBytes(Source, bytes);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
