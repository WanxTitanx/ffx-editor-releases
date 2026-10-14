using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicPrefixEditorTests
{
    private static MagicDllDocument_Wrapper Load(int id)
    {
        var doc = new MagicDllDocument_Wrapper();
        Assert.True(doc.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:D4}.dll"), out var error), error);
        return doc;
    }

    [Theory]
    [InlineData(22, "pppKeGrvTgt")]
    [InlineData(70, "pppKeLnsLp")]
    public void ProvenFloat4_SavesOnlyItsWord_WhileGenericR6StaysClosed(int id, string opcode)
    {
        var doc = Load(id);
        var field = doc.PrefixOperands.First(f => f.Opcode == opcode && f.IsEditable);
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == opcode && s.Keyframes.Any(k => k.RecordOffset == field.RecordOffset));
        int index = slot.Keyframes.ToList().FindIndex(k => k.RecordOffset == field.RecordOffset);
        var generic = doc.ResolveKeyframeFields(slot, index).Single(f => f.Offset == 4);
        Assert.False(doc.TryApplyFieldEdit(generic, BitConverter.GetBytes(0.375f), out _));
        byte[] original = File.ReadAllBytes(doc.SourcePath!), expected = (byte[])original.Clone();
        BitConverter.GetBytes(0.375f).CopyTo(expected, doc.ParsedFile.DataSectionRawPtr + field.DataOffset);
        Assert.True(doc.TryApplyPrefixEdits(new[] { (field, BitConverter.SingleToUInt32Bits(0.375f)) }, out var error), error);
        Assert.Equal(expected, doc.WorkingBytes);
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(doc.SourcePath!));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
    }

    [Theory]
    [InlineData(0x7F800000u)]
    [InlineData(0xFF800000u)]
    [InlineData(0x7FC00000u)]
    public void Float4_RejectsNonFiniteValuesWithoutMutation(uint bits)
    {
        var doc = Load(22);
        var field = doc.PrefixOperands.First(f => f.Kind == MagicPrefixOperandKind.Float && f.IsEditable);
        byte[] before = doc.WorkingBytes!;
        Assert.False(doc.TryApplyPrefixEdits(new[] { (field, bits) }, out _));
        Assert.Equal(before, doc.WorkingBytes);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void DrawReferences_OfferOnlySameTopologyAndTextureBinding()
    {
        var doc = Load(40);
        var field = doc.PrefixOperands.First(f => f.Kind == MagicPrefixOperandKind.DrawResource &&
            f.IsEditable && f.ResourceChoices.Count(i => i != MagicPrefixSnapshot.DisabledResource) > 1);
        uint compatible = field.ResourceChoices.First(i => i != field.OriginalBits && i != MagicPrefixSnapshot.DisabledResource);
        byte[] expected = doc.WorkingBytes!;
        BitConverter.GetBytes(compatible).CopyTo(expected, doc.ParsedFile!.DataSectionRawPtr + field.DataOffset);
        Assert.True(doc.TryApplyPrefixEdits(new[] { (field, compatible) }, out var error), error);
        Assert.Equal(expected, doc.WorkingBytes);
        var invalid = new[] { 0u, 1u, 2u, 3u, 4u, uint.MaxValue }
            .First(i => !field.ResourceChoices.Contains(i));
        byte[] before = doc.WorkingBytes!;
        Assert.False(doc.TryApplyPrefixEdits(new[] { (field, invalid) }, out _));
        Assert.Equal(before, doc.WorkingBytes);
        Assert.True(doc.TryApplyPrefixEdits(new[] { (field, MagicPrefixSnapshot.DisabledResource) }, out error), error);
    }

    [Fact]
    public void ForeignOrConflictingPrefixEdits_RollBackOtherDocumentChanges()
    {
        var doc = Load(22);
        var field = doc.PrefixOperands.First(f => f.Kind == MagicPrefixOperandKind.Float && f.IsEditable);
        var foreign = Load(22).PrefixOperands.First(f => f.DataOffset == field.DataOffset);
        var payload = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .Where(s => s.OpcodeName == "pppScale").SelectMany(doc.ResolveAllFields).First(f => f.Offset == 16);
        var geometry = doc.GeometryFields.First();
        byte[] before = doc.WorkingBytes!;
        Assert.False(doc.TryApplyDocumentEdits(new[] { (payload, BitConverter.GetBytes(3f)) },
            Array.Empty<(MagicTimingField, uint)>(), new[] { (geometry, (short)5) },
            new[] { (foreign, 0u) }, out _));
        Assert.Equal(before, doc.WorkingBytes);
        Assert.False(doc.TryApplyPrefixEdits(new[] { (field, 0u), (field, 1u) }, out _));
        Assert.Equal(before, doc.WorkingBytes);
    }

    [Fact]
    public async Task ViewModel_PrefixValueSurvivesFilterSaveAndRevert()
    {
        using var fixture = new MagicGeometryEditorTests.GeometryFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0022.dll")));
        var node = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicPrefixNode>()
            .First(n => n.Source.Kind == MagicPrefixOperandKind.Float && n.IsValueEditable);
        Assert.True(node.IsNumeric);
        Assert.False(node.IsChoice);
        string original = node.Value;
        node.Value = "0.375";
        Assert.True(vm.IsDirty);
        vm.SlotFilter = "no-matching-slot";
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(fixture.Destination), string.Join(" | ", vm.Log.Select(l => l.Text)));
        Assert.False(node.IsDirty);
        vm.RefreshCloneDiffCommand.Execute(null);
        Assert.Contains(node.Source.Opcode, vm.CloneDiffDetail);
        await vm.RevertCommand.ExecuteAsync(null);
        vm.SlotFilter = "";
        var reverted = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicPrefixNode>()
            .First(n => n.Source.DataOffset == node.Source.DataOffset);
        Assert.Equal(original, reverted.Value);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ResourceChoiceViewModel_HasOneControlAndTracksSelection()
    {
        var field = Load(3).PrefixOperands.First(f => f.Kind == MagicPrefixOperandKind.DrawResource && f.IsEditable);
        var node = new MagicPrefixNode(field);
        Assert.True(node.IsChoice);
        Assert.False(node.IsNumeric);
        Assert.False(node.UseValueBox);
        node.SelectedChoice = node.Choices.First(c => c.Index == MagicPrefixSnapshot.DisabledResource);
        Assert.Equal(MagicPrefixSnapshot.DisabledResource.ToString(), node.Value);
        node.ResetToOriginal();
        Assert.Equal(field.OriginalBits, node.SelectedChoice!.Index);
        Assert.False(node.IsDirty);
    }

    [Fact]
    public void PrefixSnapshot_RejectsStructuralMutationAndForgedOperands()
    {
        var doc = Load(22);
        var file = doc.ParsedFile!;
        var snapshot = new MagicPrefixSnapshot(file, new MagicGeometrySnapshot(file));
        var field = snapshot.Fields.First(f => f.Kind == MagicPrefixOperandKind.Float && f.IsEditable);
        byte[] bytes = doc.WorkingBytes!;
        bytes[file.DataSectionRawPtr + file.Roots[0].RootAbs + 8] ^= 1;
        Assert.False(snapshot.TryApply(bytes, new[] { (field, 0u) }, out _, out _));
        var forged = new MagicPrefixOperand(field.RecordOffset, field.Kind, field.Opcode,
            field.OriginalBits, true, Array.Empty<uint>(), null);
        Assert.False(snapshot.TryApply(doc.WorkingBytes!, new[] { (forged, 0u) }, out _, out _));
    }

    [Fact]
    public void GeometryAndPrefixFloat_CanBeAppliedInOneAtomicTransaction()
    {
        var doc = Load(22);
        var prefix = doc.PrefixOperands.First(f => f.Kind == MagicPrefixOperandKind.Float && f.IsEditable);
        var point = doc.GeometryFields.First();
        byte[] expected = doc.WorkingBytes!;
        BitConverter.GetBytes((short)12).CopyTo(expected, doc.ParsedFile!.DataSectionRawPtr + point.DataOffset);
        BitConverter.GetBytes(0.25f).CopyTo(expected, doc.ParsedFile.DataSectionRawPtr + prefix.DataOffset);
        Assert.True(doc.TryApplyDocumentEdits(Array.Empty<(MagicField, byte[])>(),
            Array.Empty<(MagicTimingField, uint)>(), new[] { (point, (short)12) },
            new[] { (prefix, BitConverter.SingleToUInt32Bits(0.25f)) }, out var error), error);
        Assert.Equal(expected, doc.WorkingBytes);
    }

    [Fact]
    public void Corpus_PrefixNoopPreservesAllBytes_AndReportsNativeChoices()
    {
        var parser = new MagicDllParser();
        var reports = new List<object>();
        int files = 0;
        foreach (string source in Directory.EnumerateFiles(TestDataPaths.MagicCorpus, "magic_*.dll").Order())
        {
            var file = parser.Parse(source);
            var geometry = new MagicGeometrySnapshot(file);
            var prefix = new MagicPrefixSnapshot(file, geometry);
            Assert.True(prefix.TryApply(file.FileBytes, prefix.Fields.Where(f => f.IsEditable)
                .Select(f => (f, f.OriginalBits)).ToArray(), out var candidate, out var error), error);
            Assert.Equal(file.FileBytes, candidate);
            Assert.Equal(file.FileBytes, File.ReadAllBytes(source));
            reports.Add(new { file.DllName, Sha256 = Convert.ToHexString(SHA256.HashData(file.FileBytes)),
                Fields = prefix.Fields.Select(f => new { f.DataOffset, f.Opcode, f.Kind, f.OriginalBits,
                    f.IsEditable, f.ResourceChoices, f.Error }) });
            files++;
        }
        Assert.True(files >= 705);
        File.WriteAllText(TestDataPaths.ReportPath("magic-prefix-coverage.json"),
            JsonSerializer.Serialize(new { Files = files, reports }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
