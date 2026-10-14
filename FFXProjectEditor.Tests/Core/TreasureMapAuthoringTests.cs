using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Modules.TreasureMapEditor;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class TreasureMapAuthoringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "treasure-authoring-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_root, "jppc/battle/kernel/takara.bin");

    public TreasureMapAuthoringTests()
    {
        byte[] catalog = new byte[0x14 + 4 * 498];
        catalog[0] = 1;
        Put16(catalog, 0x0A, 497);
        Put16(catalog, 0x0C, 4);
        Put16(catalog, 0x0E, 498 * 4);
        Put32(catalog, 0x10, 0x14);
        for (int i = 0; i < 498; i++)
        {
            catalog[0x14 + i * 4] = 2;
            catalog[0x15 + i * 4] = 1;
            Put16(catalog, 0x16 + i * 4, 0x2000);
        }
        Write(CatalogPath, catalog);
        AddField("bjyt10", 204);
        AddField("bjyt11", 206);
    }

    [Fact]
    public void Scanner_UsesWorkerDescriptorsAndCurrentConstantPools()
    {
        var scan = EventTreasureScanner.Scan(EventPath("bjyt10"));
        Assert.Equal(2, scan.WorkerCount);
        Assert.Equal(2, scan.Candidates.Count);
        Assert.Equal(new[] { 204 }, scan.Candidates[0].TreasureIds);
        Assert.Equal(new[] { 205 }, scan.Candidates[1].TreasureIds);
        Assert.Equal(0, scan.Candidates[0].WorkerIndex);
        Assert.Equal(1, scan.Candidates[1].WorkerIndex);
        Assert.Equal(new EventPosition(39.9587f, 0, -40.6447f, scan.Candidates[0].Positions.Single().ScriptOffset, 0), scan.Candidates[0].Positions.Single());
        Assert.Equal(-39f, scan.Candidates[1].Positions.Single().X);
    }

    [Fact]
    public void Scanner_DoesNotTreatRuntimeValuesOrStaleStackAsConstants()
    {
        byte[] atel = MakeAtel(204, runtimePosition: true, runtimeReward: true);
        string path = Path.Combine(_root, "negative.ebp");
        Write(path, WrapEvent(atel));
        var scan = EventTreasureScanner.Scan(path);
        Assert.Single(scan.Candidates);
        Assert.Equal(new[] { 205 }, scan.Candidates[0].TreasureIds);
        Assert.Empty(scan.Candidates[0].Positions);
    }

    [Fact]
    public void Scanner_RejectsInvalidFunctionTable()
    {
        byte[] atel = MakeAtel(204);
        Put32(atel, 0x60 + 0x20, int.MaxValue);
        string path = Path.Combine(_root, "invalid.ebp");
        Write(path, WrapEvent(atel));
        Assert.Throws<InvalidDataException>(() => EventTreasureScanner.Scan(path));
    }

    [Fact]
    public void Scanner_RejectsTruncatedInstructions()
    {
        byte[] atel = MakeAtel(204);
        Put32(atel, 0, BitConverter.ToInt32(atel, 0) - 2);
        string path = Path.Combine(_root, "truncated.ebp");
        Write(path, WrapEvent(atel));
        Assert.Throws<InvalidDataException>(() => EventTreasureScanner.Scan(path));
    }

    [Fact]
    public void Scanner_DoesNotBorrowMissingConstantsFromAnotherWorker()
    {
        byte[] atel = MakeAtel(204);
        Put16(atel, 0x60 + 6, 0); // The other worker still has a usable pool at the same indices.
        string path = Path.Combine(_root, "missing-pool.ebp");
        Write(path, WrapEvent(atel));
        var scan = EventTreasureScanner.Scan(path);
        Assert.Empty(scan.Candidates.Single(c => c.WorkerIndex == 0).Positions);
        Assert.Single(scan.Candidates.Single(c => c.WorkerIndex == 1).Positions);
    }

    [Fact]
    public void Projection_DoesNotApplyMeshQuantizationScaleToWorldCoordinates()
    {
        var catalog = TreasureCatalog.Read(CatalogPath);
        var fields = FieldAssetDiscovery.ScanMaster(_root);
        var candidate = new EventTreasureCandidate(EventPath("bjyt10"), "bjyt1000", "bjyt10", 1,
            new[] { 204 }, new[] { new EventPosition(39.9587f, 0, -40.6447f, 123, 0) }, Array.Empty<int>(), false);
        var index = new TreasureMapIndex(catalog, fields,
            new[] { new EventTreasureScanResult(candidate.EventPath, candidate.EventId, new[] { candidate }, 2, 10) }, Array.Empty<TreasureScanFailure>());
        var location = Assert.Single(ChestLocationIndexBuilder.Build(index).Locations);
        Assert.Equal(3.99587f, location.GuideX!.Value, 4);
        Assert.Equal(-4.06447f, location.GuideZ!.Value, 4);
    }

    [Fact]
    public async Task Edits_SurviveFieldNavigationAndSaveOnlyChangedRecordBytes()
    {
        var vm = await LoadModel();
        byte[] original = File.ReadAllBytes(CatalogPath);
        vm.SelectedField = vm.Fields.Single(f => f.Asset.FieldId == "bjyt10");
        var row = vm.Chests.First();
        int id = row.ActiveReward!.Id;
        row.Quantity = 7;
        row.SelectedReward = row.RewardOptions.Single(r => r.EncodedId == 0x2001);
        vm.SelectedField = vm.Fields.Single(f => f.Asset.FieldId == "bjyt11");
        Assert.True(vm.IsDirty);
        vm.SelectedField = vm.Fields.Single(f => f.Asset.FieldId == "bjyt10");
        Assert.Equal(7, vm.Chests.First().Quantity);
        vm.Save();
        var saved = TreasureCatalog.Read(CatalogPath).Records[id];
        Assert.Equal(7, saved.Quantity);
        Assert.Equal(0x2001, saved.Type);
        byte[] expected = (byte[])original.Clone();
        expected[saved.FileOffset + 1] = 7;
        Put16(expected, saved.FileOffset + 2, 0x2001);
        Assert.Equal(expected, File.ReadAllBytes(CatalogPath));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task KindChange_RefreshesRewardOptions_AndDiscardUsesLastSavedValues()
    {
        var vm = await LoadModel();
        vm.SelectedField = vm.Fields.First();
        var row = vm.Chests.First();
        row.SelectedKind = TreasureKind.KeyItem;
        Assert.All(row.RewardOptions, option => Assert.InRange(option.EncodedId, 0xA000, 0xAFFF));
        row.SelectedReward = row.RewardOptions.First();
        row.Quantity = 3;
        vm.Save();
        Assert.False(row.HasChanges);
        row.Quantity = 9;
        vm.Discard();
        Assert.Equal(3, row.Quantity);
        Assert.Equal(TreasureKind.KeyItem, row.SelectedKind);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task UnresolvedPositions_RemainEditableWithoutCenterMarkers()
    {
        Write(EventPath("bjyt10"), WrapEvent(MakeAtel(204, runtimePosition: true)));
        var vm = await LoadModel();
        vm.SelectedField = vm.Fields.First();
        Assert.Equal(2, vm.Chests.Count);
        Assert.Empty(vm.MapChests);
        Assert.All(vm.Chests, r => { Assert.False(r.HasRealPosition); Assert.True(r.CanEdit); });
        vm.Chests[1].Quantity = 11;
        vm.Save();
        Assert.Equal(11, TreasureCatalog.Read(CatalogPath).Records[205].Quantity);
    }

    [Fact]
    public async Task SharedCatalogEntry_UpdatesEveryVisibleReference()
    {
        byte[] atel = MakeAtel(204);
        Put32(atel, 0xF0, 204); // Two event workers grant the same catalog row.
        Write(EventPath("bjyt10"), WrapEvent(atel));
        var vm = await LoadModel();
        vm.SelectedField = vm.Fields.First();
        Assert.Equal(2, vm.Chests.Count);
        vm.Chests[0].Quantity = 6;
        Assert.Equal(6, vm.Chests[1].Quantity);
        vm.Chests[1].Quantity = 8;
        Assert.Equal(8, vm.Chests[0].Quantity);
        vm.Save();
        Assert.Equal(8, TreasureCatalog.Read(CatalogPath).Records[204].Quantity);
        Assert.All(vm.Chests, r => Assert.False(r.HasChanges));
    }

    [Fact]
    public async Task UnknownRewardName_PreservesRawIdWhenEditingQuantity()
    {
        byte[] original = File.ReadAllBytes(CatalogPath);
        Put16(original, 0x14 + 204 * 4 + 2, 0x20FF);
        Write(CatalogPath, original);
        var vm = await LoadModel(); vm.SelectedField = vm.Fields.First();
        var row = vm.Chests.Single(r => r.ActiveReward!.Id == 204);
        Assert.Equal(0x20FF, row.SelectedReward!.EncodedId);
        row.Quantity = 12;
        vm.Save();
        original[0x14 + 204 * 4 + 1] = 12;
        Assert.Equal(original, File.ReadAllBytes(CatalogPath));
    }

    [Fact]
    public async Task IncompleteKindChange_CannotSaveOrNavigateAway()
    {
        var vm = await LoadModel(); vm.SelectedField = vm.Fields.First();
        byte[] original = File.ReadAllBytes(CatalogPath);
        vm.Chests.First().SelectedKind = TreasureKind.Equipment;
        Assert.True(vm.IsDirty);
        Assert.False(vm.CanSave);
        Assert.False(vm.CanChangeField);
        Assert.Throws<InvalidOperationException>(() => vm.Save());
        Assert.Equal(original, File.ReadAllBytes(CatalogPath));
        vm.Discard();
        Assert.True(vm.CanChangeField);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task ConditionalRewards_ExposeAndSaveEachCatalogEntry()
    {
        Write(EventPath("bjyt10"), WrapEvent(MakeAtel(204, conditionalReward: true)));
        var vm = await LoadModel(); vm.SelectedField = vm.Fields.First();
        Assert.Equal(new[] { 204, 205, 214 }, vm.Chests.Select(r => r.ActiveReward!.Id).Order());
        vm.Chests.Single(r => r.ActiveReward!.Id == 204).Quantity = 5;
        vm.Chests.Single(r => r.ActiveReward!.Id == 214).Quantity = 8;
        vm.Save();
        var catalog = TreasureCatalog.Read(CatalogPath);
        Assert.Equal(5, catalog.Records[204].Quantity);
        Assert.Equal(8, catalog.Records[214].Quantity);
        Assert.Equal(1, catalog.Records[205].Quantity);
    }

    [Fact]
    public async Task MapStates_ShowOnlyTheirOwnMarkersAndShareEdits()
    {
        byte[] map = MakeMap();
        byte[] twoStates = map.Concat(map.Skip(0x50)).ToArray();
        Put16(twoStates, map.Length + 10, 1);
        Write(Path.Combine(_root, "jppc/map/bjyt/bjyt10/bin/mapout.vpa"), twoStates);
        var vm = await LoadModel(); vm.SelectedField = vm.Fields.First();
        Assert.Equal(2, vm.ModelCount);
        Assert.Equal(2, vm.MapChests.Count);
        Assert.All(vm.MapChests, row => Assert.Equal(0, row.Location.ModelIndex));
        vm.Chests.First().Quantity = 4;
        vm.NextModel(1);
        Assert.Equal(2, vm.MapChests.Count);
        Assert.All(vm.MapChests, row => Assert.Equal(1, row.Location.ModelIndex));
        Assert.Equal(4, vm.Chests.First().Quantity);
        vm.Discard();
        vm.NextModel(-1);
        Assert.Equal(1, vm.Chests.First().Quantity);
    }

    private async Task<TreasureMapEditor_DataModel> LoadModel()
    {
        var vm = new TreasureMapEditor_DataModel(_root);
        await vm.Initialization;
        Assert.False(vm.IsScanning);
        Assert.Equal(2, vm.Fields.Count);
        return vm;
    }

    private void AddField(string field, int firstId)
    {
        Write(EventPath(field), WrapEvent(MakeAtel(firstId)));
        Write(Path.Combine(_root, $"jppc/map/bjyt/{field}/bin/mapout.vpa"), MakeMap());
    }

    private string EventPath(string field) => Path.Combine(_root, $"jppc/event/obj/bj/{field}00/{field}00.ebp");

    private static byte[] MakeAtel(int firstId, bool runtimePosition = false, bool runtimeReward = false, bool conditionalReward = false)
    {
        const int codeStart = 0x200;
        byte[] data = new byte[0x500];
        Put32(data, 0x30, codeStart);
        Put16(data, 0x34, 2);
        Put16(data, 0x36, 1); // Actor count is deliberately different from worker count.
        var code = new List<byte>();
        void Op(int opcode, int arg) { code.Add((byte)(opcode | 0x80)); code.Add((byte)arg); code.Add((byte)(arg >> 8)); }
        for (int worker = 0; worker < 2; worker++)
        {
            int header = 0x60 + worker * 0x40, ints = 0xE0 + worker * 0x10, floats = 0x100 + worker * 0x10, functions = 0x140 + worker * 0x10;
            Put32(data, 0x38 + worker * 4, header);
            Put16(data, header, 1);
            Put16(data, header + 4, 1);
            Put16(data, header + 6, 3);
            Put16(data, header + 8, 2);
            Put32(data, header + 0x18, ints);
            Put32(data, header + 0x1C, floats);
            Put32(data, header + 0x20, functions);
            Put32(data, ints, firstId + worker);
            Put32(data, floats, BitConverter.SingleToInt32Bits(worker == 0 ? 39.9587f : -39f));
            Put32(data, floats + 4, 0);
            Put32(data, floats + 8, BitConverter.SingleToInt32Bits(-40.6447f));
            Put32(data, functions, code.Count);
            Op(0x2F, 0);
            Op(0x2F, 1);
            Op(runtimePosition ? 0x1F : 0x2F, 2);
            Op(0x58, 0x13);
            code.Add(0x3C);
            Put32(data, functions + 4, code.Count);
            Op(0x2E, 1);
            Op(0x2D, 0); // PUSHI indexes THIS worker's integer pool.
            if (runtimeReward && worker == 0) Op(0x1F, 0);
            Op(0x58, 0x15B);
            if (conditionalReward && worker == 0)
            {
                Op(0x2E, 1);
                Op(0x2E, firstId + 10);
                Op(0x58, 0x15B);
            }
            code.Add(0x3C);
        }
        Put32(data, 0, code.Count);
        code.CopyTo(data, codeStart);
        return data;
    }

    private static byte[] WrapEvent(byte[] atel)
    {
        byte[] bytes = new byte[0x10 + atel.Length];
        "EV01"u8.CopyTo(bytes);
        Put32(bytes, 4, 0x10);
        Put32(bytes, 8, bytes.Length);
        Put32(bytes, 12, -1);
        atel.CopyTo(bytes, 0x10);
        return bytes;
    }

    private static byte[] MakeMap()
    {
        byte[] bytes = new byte[0x50 + 16 + 0x150];
        "MAP1"u8.CopyTo(bytes);
        Put32(bytes, 0x10 + 11 * 4, 0x50);
        "YNGM"u8.CopyTo(bytes.AsSpan(0x50));
        Put32(bytes, 0x54, 0x15);
        const int blob = 0x68, fixedData = blob + 0x60;
        Put32(bytes, 0x64, 0x60);
        Put32(bytes, blob + 8, 0x40);
        Put16(bytes, blob + 0x12, 3);
        Put16(bytes, blob + 0x20, 0xFFFF);
        Put16(bytes, blob + 0x40, -4000);
        Put16(bytes, blob + 0x44, -4000);
        Put16(bytes, blob + 0x46, 4000);
        Put16(bytes, blob + 0x4A, 4000);
        foreach (int axis in new[] { 0, 8 })
        {
            Put32(bytes, fixedData + 0x70 + axis, BitConverter.SingleToInt32Bits(-10));
            Put32(bytes, fixedData + 0x80 + axis, BitConverter.SingleToInt32Bits(10));
        }
        foreach (int diagonal in new[] { 0, 20, 40 }) Put32(bytes, fixedData + 0xA0 + diagonal, BitConverter.SingleToInt32Bits(.025f));
        Put32(bytes, fixedData + 0xA0 + 60, BitConverter.SingleToInt32Bits(1));
        return bytes;
    }

    private static void Put16(byte[] bytes, int offset, int value) => BitConverter.GetBytes(unchecked((ushort)value)).CopyTo(bytes, offset);
    private static void Put32(byte[] bytes, int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    public void Dispose() => Directory.Delete(_root, true);
}
