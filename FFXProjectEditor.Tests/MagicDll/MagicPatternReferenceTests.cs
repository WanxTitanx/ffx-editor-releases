using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicPatternReferenceTests
{
    [Fact]
    public void VertexAttend_RejectsPatternOutsideNativeAux4WithoutChangingBytes()
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll"), out var error), error);
        var root = document.ParsedFile!.Roots.First(r => r.Programs.Any(p => p.Slots.Any(s => s.OpcodeName == "pppVertexAttend")));
        var slot = root.Programs.SelectMany(p => p.Slots).First(s => s.OpcodeName == "pppVertexAttend");
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 12);
        byte[] original = document.WorkingBytes!;
        Assert.False(document.TryApplyFieldEdit(field, BitConverter.GetBytes((short)root.Aux4Count), out _));
        Assert.Equal(original, document.WorkingBytes);
        Assert.False(document.TryApplyFieldEdit(field, BitConverter.GetBytes(short.MaxValue), out _));
        Assert.Equal(original, document.WorkingBytes);
    }

    [Fact]
    public void VertexAttend_DisabledSentinelChangesOnlyPatternWordAndSavesCopy()
    {
        var document = new MagicDllDocument_Wrapper();
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll");
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).First(s => s.OpcodeName == "pppVertexAttend");
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 12);
        byte[] original = document.WorkingBytes!, expected = (byte[])original.Clone();
        byte[] disabled = BitConverter.GetBytes((short)-1);
        disabled.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        Assert.True(document.TryApplyFieldEdit(field, disabled, out error), error);
        Assert.Equal(expected, document.WorkingBytes);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(document.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void VertexAttend_OriginalValidatedPatternCanBeRestoredAfterDisabling()
    {
        var document = new MagicDllDocument_Wrapper();
        string error = string.Empty;
        FFXProjectEditor.FfxLib.MagicDll.MagicSlot? slot = null;
        foreach (int id in new[] { 3, 32, 40, 46, 48, 50, 54, 94 })
        {
            Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"), out error), error);
            byte[] bytes = document.WorkingBytes!;
            slot = document.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
                .FirstOrDefault(s => s.OpcodeName == "pppVertexAttend" &&
                    BitConverter.ToInt16(bytes, document.ParsedFile.DataSectionRawPtr + s.RecordOffset + 12) >= 0);
            if (slot != null) break;
        }
        Assert.NotNull(slot);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 12);
        byte[] original = document.WorkingBytes!;
        byte[] word = original.AsSpan(document.ParsedFile.DataSectionRawPtr + field.RecordOffset + 12, 2).ToArray();
        Assert.True(BitConverter.ToInt16(word) >= 0);
        Assert.True(document.TryApplyFieldEdit(field, BitConverter.GetBytes((short)-1), out error), error);
        Assert.True(document.TryApplyFieldEdit(field, word, out error), error);
        Assert.Equal(original, document.WorkingBytes);
    }

    [Fact]
    public void PatternSnapshot_RejectsChangedAux4LayoutEvenForDisabledReference()
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll"), out var error), error);
        var file = document.ParsedFile!;
        var geometry = new FFXProjectEditor.FfxLib.MagicDll.MagicGeometrySnapshot(file);
        var snapshot = new FFXProjectEditor.FfxLib.MagicDll.MagicPatternReferenceSnapshot(file, geometry);
        var root = file.Roots.First(r => r.Programs.Any(p => p.Slots.Any(s => s.OpcodeName == "pppVertexAttend")));
        var slot = root.Programs.SelectMany(p => p.Slots).First(s => s.OpcodeName == "pppVertexAttend");
        byte[] changed = (byte[])file.FileBytes.Clone();
        changed[file.DataSectionRawPtr + root.RootAbs + 12] ^= 1;
        Assert.False(snapshot.TryValidate(changed, slot.RecordOffset, -1));
        Assert.NotEqual(changed, file.FileBytes);
    }
}
