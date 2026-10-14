using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicGeometryEditorTests
{
    [Fact]
    public void Points_AreSignedTriples_AndNullMeshRemainsValid()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        var resource = Assert.Single(doc.GeometryResources);
        Assert.Equal(2u, resource.PointCount);
        Assert.Null(resource.Error);
        Assert.Equal(new short[] { -32768, 12, 32767, 4, -5, 6 },
            resource.Points.SelectMany(p => p.Components).Select(f => f.OriginalValue));
    }

    [Fact]
    public void GeometryWrite_ChangesOnlyOneSignedOperand_AndPreservesOriginal()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        byte[] original = File.ReadAllBytes(fixture.Source);
        var field = doc.GeometryResources[0].Points[1].Components[1];
        Assert.True(doc.TryApplyGeometryEdits(new[] { (field, (short)-1234) }, out var error), error);
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        byte[] expected = (byte[])original.Clone();
        BitConverter.GetBytes((short)-1234).CopyTo(expected, fixture.Raw + field.DataOffset);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(original, File.ReadAllBytes(fixture.Source));
        Assert.Equal(original, File.ReadAllBytes(fixture.Destination + ".bak"));
    }

    [Theory]
    [InlineData(0u, 2u)]
    [InlineData(0x5FFEu, 2u)]
    [InlineData(0x5700u, uint.MaxValue)]
    [InlineData(0x40u, 2u)]
    [InlineData(0x840u, 2u)]
    [InlineData(0x5701u, 2u)]
    public void InvalidOrOverlappingPointRanges_AreNotIssued(uint relative, uint count)
    {
        using var fixture = new GeometryFixture();
        fixture.Write(fixture.Descriptor + 20, relative);
        fixture.Write(fixture.Descriptor, count);
        var resource = Assert.Single(fixture.Load().GeometryResources);
        Assert.NotNull(resource.Error);
        Assert.Empty(resource.Points);
    }

    [Fact]
    public void SharedArrays_UseOneOperand_AndRejectConflictingEdits()
    {
        using var fixture = new GeometryFixture();
        fixture.AddAlias(0);
        var doc = fixture.Load();
        var a = doc.GeometryResources[0].Points[0].Components[0];
        var b = doc.GeometryResources[1].Points[0].Components[0];
        Assert.Same(a, b);
        Assert.False(doc.TryApplyGeometryEdits(new[] { (a, (short)10), (b, (short)11) }, out _));
        Assert.False(doc.IsDirty);
        Assert.True(doc.TryApplyGeometryEdits(new[] { (a, (short)10), (b, (short)10) }, out var error), error);
    }

    [Fact]
    public void MisalignedAliases_AreRejectedForBothOwners()
    {
        using var fixture = new GeometryFixture();
        fixture.AddAlias(2);
        Assert.All(fixture.Load().GeometryResources, r =>
        {
            Assert.NotNull(r.Error);
            Assert.Empty(r.Points);
        });
    }

    [Fact]
    public void MeshLayout_ValidatesPrimitiveIndicesAndSeparatePointAndNormalBuffers()
    {
        using var fixture = new GeometryFixture();
        fixture.AddMesh();
        Assert.Equal(6, fixture.Load().GeometryFields.Count);
        fixture.Write(0x5630 + 16, 2); // First quad vertex index, outside the two-point array.
        Assert.Empty(fixture.Load().GeometryFields);
    }

    [Fact]
    public void PointOnlyResourceFollowingShorterNormalBuffer_RemainsEditable()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0021.dll");
        var doc = new MagicDllDocument_Wrapper();
        Assert.True(doc.TryLoad(source, out var error), error);
        var resource = doc.GeometryResources.Single(r => r.DescriptorIndex == 2);
        Assert.Null(resource.Error);
        Assert.Equal(42, resource.Points.Count);
        byte[] expected = doc.WorkingBytes!;
        var field = resource.Points[0].Components[0];
        BitConverter.GetBytes((short)17).CopyTo(expected, doc.ParsedFile!.DataSectionRawPtr + field.DataOffset);
        Assert.True(doc.TryApplyGeometryEdits(new[] { (field, (short)17) }, out error), error);
        Assert.Equal(expected, doc.WorkingBytes);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(uint.MaxValue)]
    public void NormalBufferCountMustFitItsIndependentAllocationBoundary(uint count)
    {
        using var fixture = new GeometryFixture();
        fixture.AddMesh();
        fixture.Write(fixture.Descriptor + 4, count);
        Assert.Empty(fixture.Load().GeometryFields);
    }

    [Fact]
    public void PointPointerCannotCutIntoPrimitiveData()
    {
        using var fixture = new GeometryFixture();
        fixture.AddMesh();
        fixture.Write(fixture.Descriptor + 20, 0x5640);
        fixture.Write(0x5608, 0x40); // Both pointers agree, but the primitive extends past them.
        Assert.Empty(fixture.Load().GeometryFields);
    }

    [Fact]
    public void FailedLoad_DropsAllPreviouslyIssuedGeometry()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        Assert.NotEmpty(doc.GeometryFields);
        Assert.False(doc.TryLoad(Path.Combine(Path.GetDirectoryName(fixture.Source)!, "missing.dll"), out _));
        Assert.Empty(doc.GeometryFields);
        Assert.Empty(doc.GeometryResources);
    }

    [Fact]
    public void LargeButInBoundsArray_CannotExpandAnUnboundedEditorGraph()
    {
        byte[] data = new byte[0x100 + 6 * 100_001];
        BitConverter.GetBytes(100_001u).CopyTo(data, 32);
        BitConverter.GetBytes(0x100u).CopyTo(data, 52);
        var descriptor = new MagicDescriptor(0, 32, 100_001, 0, 0, 0, 0, 0x100, 0, 0);
        var root = new MagicDllRoot(0, 0x31, 0, 0, 1, 0, 0, 32, 32, 64, 68,
            Array.Empty<int>(), Array.Empty<int>(), new[] { descriptor },
            Array.Empty<MagicProgram>(), Array.Empty<int>());
        var file = new MagicDllFile("synthetic.dll", data, data, 0, data.Length, 0,
            Array.Empty<MagicPeSection>(), new[] { root });
        var snapshot = new MagicGeometrySnapshot(file);
        Assert.Empty(snapshot.Fields);
        Assert.NotNull(Assert.Single(snapshot.Resources).Error);
    }

    [Fact]
    public void ForeignField_RollsBackMixedPayloadAndTiming()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        var foreign = fixture.Load().GeometryResources[0].Points[0].Components[0];
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppScale");
        var payload = doc.ResolveAllFields(slot).First(f => f.Offset >= 16);
        var time = doc.TimingFields.First(f => f.Kind == MagicTimingKind.ProgramDuration && f.IsEditable);
        byte[] before = doc.WorkingBytes!;
        Assert.False(doc.TryApplyDocumentEdits(new[] { (payload, BitConverter.GetBytes(2f)) },
            new[] { (time, 50u * 4096) }, new[] { (foreign, (short)50) }, out _));
        Assert.Equal(before, doc.WorkingBytes);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void StructuralMutationOrForgedField_IsRejectedByTheCapturedSnapshot()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        var snapshot = new MagicGeometrySnapshot(doc.ParsedFile!);
        var field = snapshot.Fields[0];
        byte[] working = doc.WorkingBytes!;
        BitConverter.GetBytes(3u).CopyTo(working, fixture.Raw + fixture.Descriptor);
        Assert.False(snapshot.TryApply(working, new[] { (field, (short)7) }, out _, out _));
        var forged = new MagicGeometryField(field.DataOffset, field.Axis, field.OriginalValue);
        Assert.False(snapshot.TryApply(doc.WorkingBytes!, new[] { (forged, (short)7) }, out _, out _));
        Assert.True(snapshot.TryApply(doc.WorkingBytes!, new[] { (field, (short)7) }, out _, out var error), error);
    }

    [Fact]
    public void ValidPayloadTimingAndGeometry_AreCommittedTogether()
    {
        using var fixture = new GeometryFixture();
        var doc = fixture.Load();
        var geometry = doc.GeometryFields.First();
        var slot = doc.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppScale");
        var payload = doc.ResolveAllFields(slot).First(f => f.Offset >= 16);
        var time = doc.TimingFields.First(f => f.Kind == MagicTimingKind.ProgramDuration && f.IsEditable);
        byte[] expected = doc.WorkingBytes!;
        BitConverter.GetBytes(2f).CopyTo(expected, fixture.Raw + payload.RecordOffset + payload.Offset);
        BitConverter.GetBytes(50u * 4096).CopyTo(expected, fixture.Raw + time.DataOffset);
        BitConverter.GetBytes((short)50).CopyTo(expected, fixture.Raw + geometry.DataOffset);
        Assert.True(doc.TryApplyDocumentEdits(new[] { (payload, BitConverter.GetBytes(2f)) },
            new[] { (time, 50u * 4096) }, new[] { (geometry, (short)50) }, out var error), error);
        Assert.Equal(expected, doc.WorkingBytes);
    }

    [Theory]
    [InlineData("32768")]
    [InlineData("-32769")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    public async Task InvalidCoordinate_AbortsSaveBeforeAnyFileIsCreated(string value)
    {
        using var fixture = new GeometryFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicGeometryNode>().First().Value = value;
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.False(File.Exists(fixture.Destination));
        Assert.True(vm.IsDirty);
        Assert.Contains(vm.Log, e => e.Level == MagicLogLevel.Error);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(69)]
    [InlineData(515)]
    public void RealDll_PointEditSurvivesReparseWithoutChangingAnyOtherByte(int id)
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:D4}.dll");
        var doc = new MagicDllDocument_Wrapper();
        Assert.True(doc.TryLoad(source, out var error), error);
        byte[] original = File.ReadAllBytes(source), expected = (byte[])original.Clone();
        Assert.NotEmpty(doc.GeometryFields);
        var field = doc.GeometryFields.Last();
        short value = field.OriginalValue == short.MaxValue ? (short)0 : (short)(field.OriginalValue + 1);
        BitConverter.GetBytes(value).CopyTo(expected, doc.ParsedFile!.DataSectionRawPtr + field.DataOffset);
        Assert.True(doc.TryApplyGeometryEdits(new[] { (field, value) }, out error), error);
        using var fixture = new GeometryFixture();
        Assert.True(doc.TrySaveCopy(fixture.Destination, out error), error);
        Assert.Equal(expected, File.ReadAllBytes(fixture.Destination));
        Assert.Equal(expected, new MagicDllParser().Parse(fixture.Destination).Serialize());
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void Corpus_GeometryNoopPreservesEveryByte_AndReportsUnsupportedLayouts()
    {
        var parser = new MagicDllParser();
        var reports = new List<object>();
        int count = 0, fields = 0;
        foreach (string source in Directory.EnumerateFiles(TestDataPaths.MagicCorpus, "magic_*.dll").Order())
        {
            var file = parser.Parse(source);
            var snapshot = new MagicGeometrySnapshot(file);
            Assert.True(snapshot.TryApply(file.FileBytes,
                snapshot.Fields.Select(f => (f, f.OriginalValue)).ToArray(), out var candidate, out var error), error);
            Assert.Equal(file.FileBytes, candidate);
            Assert.Equal(file.FileBytes, File.ReadAllBytes(source));
            fields += snapshot.Fields.Count;
            count++;
            reports.Add(new { file.DllName, Sha256 = Convert.ToHexString(SHA256.HashData(file.FileBytes)),
                Fields = snapshot.Fields.Count,
                Resources = snapshot.Resources.Select(r => new { r.DescriptorIndex, r.RootOffset,
                    r.PointCount, r.PointDataOffset, EditablePoints = r.Points.Count, r.Error }) });
        }
        Assert.True(count >= 705);
        Assert.True(fields > 0);
        File.WriteAllText(TestDataPaths.ReportPath("magic-geometry-coverage.json"),
            JsonSerializer.Serialize(new { Files = count, Coordinates = fields, reports },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task ViewModel_EditFilterSaveDiffAndRevert_IncludeGeometry()
    {
        using var fixture = new GeometryFixture();
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(fixture.Source));
        var node = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicGeometryNode>().First();
        node.Value = "-123";
        Assert.True(vm.IsDirty);
        vm.SlotFilter = "no-matching-slot";
        vm.SaveFilePicker = () => Task.FromResult<string?>(fixture.Destination);
        await vm.SaveCopyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(fixture.Destination), string.Join(" | ", vm.Log.Select(l => l.Text)));
        Assert.False(node.IsDirty);
        Assert.Equal((short)-123, BitConverter.ToInt16(File.ReadAllBytes(fixture.Destination),
            fixture.Raw + node.Source.DataOffset));
        vm.RefreshCloneDiffCommand.Execute(null);
        Assert.Contains(".data+0x5700", vm.CloneDiffDetail);
        await vm.RevertCommand.ExecuteAsync(null);
        var reverted = MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicGeometryNode>().First();
        Assert.Equal("-32768", reverted.Value);
        Assert.False(vm.IsDirty);
    }

    internal sealed class GeometryFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(TestDataPaths.RepoRoot, "work",
            "magic-editor-completion-20261003", "geometry-fixtures", Guid.NewGuid().ToString("N"));
        internal string Source { get; }
        internal string Destination => Path.Combine(_directory, "magic_edited.dll");
        internal int Raw { get; }
        internal int Descriptor => 0x5400;

        internal GeometryFixture()
        {
            TestDirectory.CreatePrivate(_directory);
            Source = MagicDllTestFixture.Write(_directory, "magic_0021.dll");
            var doc = new MagicDllDocument_Wrapper();
            Assert.True(doc.TryLoad(Source, out var error), error);
            Raw = doc.ParsedFile!.DataSectionRawPtr;
            byte[] bytes = File.ReadAllBytes(Source);
            BitConverter.GetBytes((ushort)1).CopyTo(bytes, Raw + 8);
            BitConverter.GetBytes((uint)Descriptor).CopyTo(bytes, Raw + 20);
            BitConverter.GetBytes(0x5480u).CopyTo(bytes, Raw + 24);
            BitConverter.GetBytes(0x5484u).CopyTo(bytes, Raw + 28);
            BitConverter.GetBytes(2u).CopyTo(bytes, Raw + Descriptor);
            BitConverter.GetBytes(0x5700u).CopyTo(bytes, Raw + Descriptor + 20);
            short[] points = { -32768, 12, 32767, 4, -5, 6 };
            for (int i = 0; i < points.Length; i++)
                BitConverter.GetBytes(points[i]).CopyTo(bytes, Raw + 0x5700 + 2 * i);
            foreach (var p in doc.ParsedFile.Roots.SelectMany(r => r.Programs))
            {
                BitConverter.GetBytes(120u * 4096).CopyTo(bytes, Raw + p.SectionAbs);
                BitConverter.GetBytes(60u * 4096).CopyTo(bytes, Raw + p.ProgramAbs + 20);
                foreach (var s in p.Slots)
                    BitConverter.GetBytes(0xFFFFF000u).CopyTo(bytes,
                        Raw + s.RecordOffset + s.NativeRecordWidth * s.KeyframeCount);
            }
            File.WriteAllBytes(Source, bytes);
        }

        internal void Write(int at, uint value)
        {
            byte[] bytes = File.ReadAllBytes(Source);
            BitConverter.GetBytes(value).CopyTo(bytes, Raw + at);
            File.WriteAllBytes(Source, bytes);
        }

        internal void AddAlias(uint delta)
        {
            byte[] bytes = File.ReadAllBytes(Source);
            BitConverter.GetBytes((ushort)2).CopyTo(bytes, Raw + 8);
            Array.Copy(bytes, Raw + Descriptor, bytes, Raw + Descriptor + 32, 32);
            BitConverter.GetBytes(0x5700u + delta).CopyTo(bytes, Raw + Descriptor + 52);
            File.WriteAllBytes(Source, bytes);
        }

        internal void AddMesh()
        {
            byte[] bytes = File.ReadAllBytes(Source);
            void U32(int at, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, Raw + at);
            U32(Descriptor + 4, 2);
            U32(Descriptor + 24, 0x5720);
            U32(Descriptor + 28, 0x5600);
            U32(0x5604, 0x20);
            U32(0x5608, 0x100);
            U32(0x560C, 0x120);
            U32(0x5610, 0x00020001); // One primitive, two vertices.
            bytes[Raw + 0x5621] = 6; // Textured quad, native stride 40.
            BitConverter.GetBytes((ushort)1).CopyTo(bytes, Raw + 0x5622);
            BitConverter.GetBytes((ushort)1).CopyTo(bytes, Raw + 10);
            U32(0x5480, 0x5730); // Independent start of an auxiliary resource after the normals.
            BitConverter.GetBytes((short)4096).CopyTo(bytes, Raw + 0x5724);
            BitConverter.GetBytes((short)4096).CopyTo(bytes, Raw + 0x5728);
            File.WriteAllBytes(Source, bytes);
        }

        internal MagicDllDocument_Wrapper Load()
        {
            var doc = new MagicDllDocument_Wrapper();
            Assert.True(doc.TryLoad(Source, out var error), error);
            return doc;
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
