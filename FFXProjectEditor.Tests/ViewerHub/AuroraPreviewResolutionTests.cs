using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class AuroraPreviewResolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-resolution-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MapViewer_ShipsItsSceneSelectionModuleWithTheImporter()
    {
        string map = Path.Combine(AppContext.BaseDirectory, "viewers", "map");
        Assert.Contains("./assetSelection.js", File.ReadAllText(Path.Combine(map, "app.js")));
        Assert.True(File.Exists(Path.Combine(map, "assetSelection.js")));
    }

    [Fact]
    public void EditedBattle_ResolvesByNoclipTableFormationOrderWithoutAContentMatch()
    {
        string btl = Path.Combine(_root, "btl");
        string enc = Path.Combine(_root, "data", "FinalFantasyX", "0e");
        Write(Path.Combine(btl, "azit03_00", "azit03_00.bin"), new byte[] { 1, 2, 3 });
        Write(Path.Combine(enc, "00e4.bin"), new byte[] { 4, 5, 6 });
        Write(Path.Combine(enc, "00e5.bin"), new byte[] { 7, 8, 9 });
        Write(Path.Combine(_root, "data", "FinalFantasyX", "0d", "0000.bin"), EncounterTable());

        EncounterIndexBridge index = EncounterIndexBridge.Build(btl, enc);

        Assert.True(index.TryResolve("azit03_00", out int id));
        // Formation 00 is the second slot, not formationOffset + formationId.
        Assert.Equal(0xE5, id);
        Assert.False(index.TryResolve("azit03_99", out _));
    }

    [Fact]
    public void MissingNoclipTable_PreservesExactContentResolution()
    {
        string btl = Path.Combine(_root, "btl");
        string enc = Path.Combine(_root, "0e");
        byte[] bytes = { 1, 2, 3 };
        Write(Path.Combine(btl, "azit03_00", "azit03_00.bin"), bytes);
        Write(Path.Combine(enc, "00e4.bin"), bytes);
        Assert.True(EncounterIndexBridge.Build(btl, enc).TryResolve("azit03_00", out int id));
        Assert.Equal(0xE4, id);
    }

    [Fact]
    public async Task LowercaseEncounterFile_CanStageCurrentProjectBytesOnLinux()
    {
        string enc = Path.Combine(_root, "0e");
        string original = Path.Combine(enc, "00ef.bin");
        string current = Path.Combine(_root, "battle.bin");
        Write(original, new byte[] { 1, 2, 3 });
        Write(current, new byte[] { 4, 5, 6 });

        if (OperatingSystem.IsLinux())
        {
            // Linux stages the override as an exact-memory lease on a running server —
            // no file is written under the viewer root.
            var server = new FFXProjectEditor.Modules.Common.ViewerHub.StudioWebServer();
            try
            {
                Assert.True(server.Start(0), server.Status);
                var result = Aurora3DLauncher.StageBattleOverride(
                    current, 0xEF, enc, Path.Combine(_root, "viewer"), server);

                Assert.True(result.Success, result.Message);
                Assert.NotNull(result.RequestPath);
                using (var client = new System.Net.Http.HttpClient())
                {
                    byte[] served = await client.GetByteArrayAsync(
                        $"http://127.0.0.1:{server.Port}{result.RequestPath}");
                    Assert.Equal(new byte[] { 4, 5, 6 }, served);
                }
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(original));
                result.MemoryLease?.Dispose();
            }
            finally { server.Stop(); }
            return;
        }

        var staged = Aurora3DLauncher.StageBattleOverride(current, 0xEF, enc, Path.Combine(_root, "viewer"));

        Assert.True(staged.Success, staged.Message);
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(staged.StagedPath!));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(original));
    }

    private static byte[] EncounterTable()
    {
        byte[] bytes = new byte[0x1E + 11];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x10);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 0x1E);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x10), 363);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x14), 0xE4);
        Encoding.ASCII.GetBytes("azit03").CopyTo(bytes, 0x16);
        bytes[0x1E] = 2;
        bytes[0x1F] = 1;
        bytes[0x20] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x21), 0x41E);
        bytes[0x24] = 2;
        bytes[0x25] = 2;
        bytes[0x26] = 1;
        bytes[0x27] = 0;
        bytes[0x28] = 1;
        return bytes;
    }

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
