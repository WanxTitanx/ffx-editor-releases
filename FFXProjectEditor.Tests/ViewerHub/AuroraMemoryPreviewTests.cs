// WHY: Exercise the native Aurora producer through real loopback HTTP and private inputs.
// MAINT: Non-Linux returns are not runtime evidence. Never point this fixture at corpus/profile data.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using TestIo = FFXProjectEditor.Tests.MagicDll.MagicPreviewFixture;

namespace FFXProjectEditor.Tests.ViewerHub;

internal sealed class AuroraMemoryFixture : IAsyncDisposable
{
    internal const string Route = "/data/FinalFantasyX/0e/00ef.bin";
    private readonly List<StudioWebServer.ExactMemoryLease> _leases = new();
    internal string Root { get; } = Path.Combine(
        FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "aurora-memory-" + Guid.NewGuid().ToString("N"));
    internal string SelectedDirectory { get; }
    internal string SelectedPath { get; }
    internal string Battle { get; }
    internal string ViewerRoot { get; }
    internal StudioWebServer Server { get; } = new();
    internal AuroraMemoryPreviewSession Session { get; } = new();
    internal HttpClient Client { get; }

    internal AuroraMemoryFixture()
    {
        SelectedDirectory = Path.Combine(Root, "selected", "data", "FinalFantasyX", "0e");
        SelectedPath = Path.Combine(SelectedDirectory, "00EF.bin");
        Battle = Path.Combine(Root, "battle", "btl_test.bin");
        ViewerRoot = Path.Combine(Root, "viewer-output-must-not-exist");
        TestDirectory.CreatePrivate(SelectedDirectory);
        TestDirectory.CreatePrivate(Path.GetDirectoryName(Battle)!);
        File.WriteAllBytes(SelectedPath, new byte[] { 0x10 });
        File.WriteAllBytes(Battle, new byte[] { 0xA1, 0xA2 });
        try
        {
            Server.MapPrefix("/data/FinalFantasyX/0e", SelectedDirectory);
            Assert.True(Server.Start(0), Server.Status);
            Client = TestIo.NewClient(Server);
        }
        catch
        {
            Server.Stop();
            Directory.Delete(Root, recursive: true);
            throw;
        }
    }

    internal static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(L.IsSupported, "This native run requires the supported Linux x86-64 ABI.");
        return true;
    }

    internal AuroraBattleOverlayResult Stage(
        string? battle = null, int id = 0xEF,
        AuroraMemoryPreviewSession? session = null, StudioWebServer? server = null)
    {
        var result = Aurora3DLauncher.StageBattleOverride(
            battle ?? Battle, id, SelectedDirectory, ViewerRoot,
            session ?? Session, server ?? Server);
        Track(result);
        return result;
    }

    internal void Track(AuroraBattleOverlayResult result)
    {
        if (result.MemoryLease is { } lease) _leases.Add(lease);
    }

    internal async Task AssertMemoryAsync(AuroraBattleOverlayResult result, byte[] bytes)
    {
        Assert.True(result.Success, result.Message);
        Assert.Null(result.StagedPath);
        Assert.NotNull(result.MemorySession);
        var lease = Assert.IsType<StudioWebServer.ExactMemoryLease>(result.MemoryLease);
        Assert.True(lease.IsActive);
        Assert.True(lease.IsOwnedBy(Server));
        Assert.Equal(Route, result.RequestPath);
        Assert.Equal(result.RequestPath, lease.RequestPath);
        Assert.True(Aurora3DLauncher.TryRegisterBattleOverlay(Server, result));
        using var get = await Client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore == true);
        Assert.Equal("application/octet-stream", get.Content.Headers.ContentType?.MediaType);
        byte[] actual = await get.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, actual);
        Assert.Equal(TestIo.Hash(bytes), TestIo.Hash(actual));
        using var request = new HttpRequestMessage(HttpMethod.Head, Route);
        using var head = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal((long)bytes.Length, head.Content.Headers.ContentLength);
        Assert.True(head.Headers.CacheControl?.NoStore == true);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        AssertNoOutput();
    }

    internal void AssertNoOutput()
    {
        Assert.False(Directory.Exists(ViewerRoot));
        Assert.Empty(Directory.GetFiles(Root, "*.aurora3d.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories));
    }

    internal static void HardLink(string existing, string created) =>
        Assert.Equal(0, Link(existing, created));

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string created);

    public async ValueTask DisposeAsync()
    {
        G.BeforeHandleOperationForTests = null;
        Session.Dispose();
        foreach (var lease in _leases) lease.Dispose();
        Client.Dispose();
        Server.Stop();
        try { await TestIo.AwaitUsageAsync(Server, 0, 0); }
        finally { Directory.Delete(Root, recursive: true); }
    }
}

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class AuroraMemoryPreviewTests
{
    [Fact]
    public async Task Native_ReadSnapshot_IsImmutableAndCreatesNoOutput()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string[] files = TestIo.Files(f.Root);
        byte[] source = File.ReadAllBytes(f.Battle);
        string vanillaHash = TestIo.Hash(File.ReadAllBytes(f.SelectedPath));
        var result = f.Stage();
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
        Assert.Equal(source, File.ReadAllBytes(f.Battle));
        File.WriteAllBytes(f.Battle, new byte[] { 0xB0 });
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
        Assert.Equal(vanillaHash, TestIo.Hash(File.ReadAllBytes(f.SelectedPath)));
        Assert.Equal(files, TestIo.Files(f.Root));
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, null, result));
        Assert.False(result.MemoryLease!.IsActive);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Theory]
    [InlineData("00EF.bin")]
    [InlineData("00ef.bin")]
    [InlineData("00eF.bin")]
    public async Task Native_SelectedHexSpelling_MapsOnlyCanonicalLowercase(string spelling)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        if (spelling != "00EF.bin") File.Move(f.SelectedPath, Path.Combine(f.SelectedDirectory, spelling));
        var result = f.Stage();
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
        // The uppercase HTTP request is not the exact memory route; frozen NoClip fallback handles it.
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync("/data/FinalFantasyX/0e/00EF.bin"));
        Assert.Single(Directory.GetFiles(f.SelectedDirectory));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("symlink")]
    [InlineData("ambiguous")]
    [InlineData("ambiguous-hardlink")]
    public async Task Native_SelectedRefusal_DoesNotPublishOrMutate(string kind)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string sentinel = Path.Combine(f.Root, "sentinel.bin");
        File.WriteAllBytes(sentinel, new byte[] { 0xE1 });
        if (kind is "missing" or "symlink") File.Delete(f.SelectedPath);
        if (kind == "symlink") File.CreateSymbolicLink(f.SelectedPath, sentinel);
        if (kind == "ambiguous")
            File.WriteAllBytes(Path.Combine(f.SelectedDirectory, "00ef.bin"), new byte[] { 0xE2 });
        if (kind == "ambiguous-hardlink")
            AuroraMemoryFixture.HardLink(f.SelectedPath, Path.Combine(f.SelectedDirectory, "00ef.bin"));
        string[] files = TestIo.Files(f.Root);
        var result = f.Stage();
        Assert.False(result.Success);
        Assert.Null(result.MemoryLease);
        Assert.Null(result.StagedPath);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0xE1 }, File.ReadAllBytes(sentinel));
        Assert.Equal(files, TestIo.Files(f.Root));
        f.AssertNoOutput();
        using var response = await f.Client.GetAsync(AuroraMemoryFixture.Route);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("leaf-link")]
    [InlineData("parent-link")]
    [InlineData("directory")]
    [InlineData("wrong-case")]
    [InlineData("dot-component")]
    public async Task Native_BattleSourceRefusal_NeverUsesPathnameReadFallback(string kind)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string path = f.Battle;
        if (kind == "leaf-link")
        {
            path = Path.Combine(f.Root, "alias.bin");
            File.CreateSymbolicLink(path, f.Battle);
        }
        if (kind == "parent-link")
        {
            string alias = Path.Combine(f.Root, "alias-parent");
            Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(f.Battle)!);
            path = Path.Combine(alias, Path.GetFileName(f.Battle));
        }
        if (kind == "directory") path = Path.GetDirectoryName(f.Battle)!;
        if (kind == "wrong-case") path = Path.Combine(Path.GetDirectoryName(f.Battle)!, "BTL_TEST.bin");
        if (kind == "dot-component") path = Path.GetDirectoryName(f.Battle) + "/../battle/btl_test.bin";
        var result = f.Stage(path);
        Assert.False(result.Success);
        Assert.Equal(Strings.U_Au_MemoryPreviewUnavailable, result.Message);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0xA1, 0xA2 }, File.ReadAllBytes(f.Battle));
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        f.AssertNoOutput();
    }

    [Fact]
    public async Task Native_StableReadOnlyHardlinks_AreNotMistakenForOwnedOutputs()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string alias = Path.Combine(f.Root, "hardlinked-battle.bin");
        AuroraMemoryFixture.HardLink(f.Battle, alias);
        string selectedAlias = Path.Combine(f.Root, "hardlinked-vanilla.bin");
        AuroraMemoryFixture.HardLink(f.SelectedPath, selectedAlias);
        var result = f.Stage(alias);
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
        File.WriteAllBytes(f.Battle, new byte[] { 0xC0 });
        Assert.Equal(new byte[] { 0xC0 }, File.ReadAllBytes(alias));
        Assert.Equal(new byte[] { 0x10 }, File.ReadAllBytes(selectedAlias));
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
    }

    [Theory]
    [InlineData("in-place")]
    [InlineData("name-swap")]
    [InlineData("hardlink-write")]
    public async Task Native_SourceChangedDuringSelectedProbe_RefusesSnapshot(string kind)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string alias = Path.Combine(f.Root, "alias.bin");
        if (kind == "hardlink-write") AuroraMemoryFixture.HardLink(f.Battle, alias);
        int changed = 0;
        G.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (operation != "open-read-relative" ||
                !path.StartsWith(f.SelectedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Interlocked.Exchange(ref changed, 1) != 0) return;
            if (kind == "name-swap") File.Move(f.Battle, f.Battle + ".held");
            File.WriteAllBytes(kind == "hardlink-write" ? alias : f.Battle, new byte[] { 0xE0, 0xE1 });
        };
        try
        {
            var result = f.Stage();
            Assert.Equal(1, changed);
            Assert.False(result.Success);
            Assert.Null(result.MemoryLease);
        }
        finally { G.BeforeHandleOperationForTests = null; }
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        f.AssertNoOutput();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task Native_InvalidId_RefusesBeforeOpeningInput(int id)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        int opens = 0;
        G.BeforeHandleOperationForTests = (_, _) => opens++;
        try
        {
            var result = f.Stage(id: id);
            Assert.False(result.Success);
            Assert.Equal(0, opens);
        }
        finally { G.BeforeHandleOperationForTests = null; }
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(67108865L)]
    public async Task Native_SizeRefusal_PrecedesPayloadAllocation(long length)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        // A real sparse private file, not an invented span or a huge caller array.
        using (var stream = new FileStream(f.Battle, FileMode.Open, FileAccess.Write))
            stream.SetLength(length);
        int selectedProbes = 0;
        G.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (operation == "open-read-relative" && path.StartsWith(
                    f.SelectedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                selectedProbes++;
        };
        AuroraBattleOverlayResult result;
        long allocated;
        _ = Strings.U_Au_MemoryPreviewUnavailable; // Exclude first-use resource initialization.
        try
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            result = f.Stage();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally { G.BeforeHandleOperationForTests = null; }
        Assert.False(result.Success);
        Assert.Equal(0, selectedProbes);
        Assert.True(allocated < 4 * 1024 * 1024, $"Refusal allocated {allocated} bytes.");
        Assert.Equal(length, new FileInfo(f.Battle).Length);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        f.AssertNoOutput();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_MissingOrStoppedServer_HasNoStagingFallback(bool stopped)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        if (stopped) f.Server.Stop();
        var result = Aurora3DLauncher.StageBattleOverride(
            f.Battle, 0xEF, f.SelectedDirectory, f.ViewerRoot, f.Session,
            stopped ? f.Server : null);
        f.Track(result);
        Assert.False(result.Success);
        Assert.Equal(Strings.U_Au_MemoryPreviewUnavailable, result.Message);
        f.AssertNoOutput();
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        if (stopped) Assert.True(f.Server.Start(0), f.Server.Status);
        using var client = TestIo.NewClient(f.Server);
        Assert.Equal(new byte[] { 0x10 }, await client.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Fact]
    public async Task Native_FullServer_RefusesWithSpareBytesAndCanAcquireAfterRelease()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        f.Server.LowerExactMemoryLimitsForTests(64, 256, 1);
        var first = f.Stage();
        await f.AssertMemoryAsync(first, new byte[] { 0xA1, 0xA2 });
        var refused = f.Stage();
        Assert.False(refused.Success);
        Assert.Equal(Strings.U_Au_MemoryPreviewUnavailable, refused.Message);
        await TestIo.AwaitUsageAsync(f.Server, 2, 1);
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, f.Server, first));
        var next = f.Stage();
        await f.AssertMemoryAsync(next, new byte[] { 0xA1, 0xA2 });
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("path")]
    [InlineData("failure")]
    [InlineData("session-missing")]
    [InlineData("session-wrong")]
    public async Task Native_MalformedRegistration_RefusesButCapabilityReleaseIsSafe(string kind)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        using var other = new AuroraMemoryPreviewSession();
        string sentinel = Path.Combine(f.Root, "not-owned-staging.bin");
        File.WriteAllBytes(sentinel, new byte[] { 0xEE });
        var result = f.Stage();
        var malformed = kind switch
        {
            "mixed" => result with { StagedPath = sentinel },
            "path" => result with { RequestPath = "/data/FinalFantasyX/0e/00f0.bin" },
            "failure" => result with { Success = false },
            "session-missing" => result with { MemorySession = null },
            _ => result with { MemorySession = other }
        };
        Assert.False(Aurora3DLauncher.TryRegisterBattleOverlay(f.Server, malformed));
        Assert.True(Aurora3DLauncher.TryRegisterBattleOverlay(f.Server, result));
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, null, malformed));
        Assert.False(result.MemoryLease!.IsActive);
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, null, result));
        Assert.Equal(new byte[] { 0xEE }, File.ReadAllBytes(sentinel));
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Fact]
    public async Task Native_WrongServerCannotRegister_ReleaseStillConsumesOriginalLease()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        var other = new StudioWebServer().MapPrefix("/data/FinalFantasyX/0e", f.SelectedDirectory);
        try
        {
            Assert.True(other.Start(0), other.Status);
            using var client = TestIo.NewClient(other);
            var result = f.Stage();
            Assert.False(Aurora3DLauncher.TryRegisterBattleOverlay(other, result));
            await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
            Assert.Equal(new byte[] { 0x10 }, await client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            await TestIo.AwaitUsageAsync(other, 0, 0);
            Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, other, result));
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
            Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        }
        finally { other.Stop(); }
    }

    [Fact]
    public async Task Native_SessionReferenceCap_PrunesAndClosesWithoutMaskingServerLimit()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        var other = new StudioWebServer();
        try
        {
            Assert.True(other.Start(0), other.Status);
            var acquired = new List<AuroraBattleOverlayResult>();
            for (int i = 0; i < 32; i++)
            {
                var result = f.Stage(server: i < 16 ? f.Server : other);
                Assert.True(result.Success, result.Message);
                acquired.Add(result);
            }
            Assert.False(f.Stage().Success); // A has 16 entries/32 bytes, not 32 entries or 128MiB.
            acquired[0].MemoryLease!.Dispose();
            Assert.True(f.Stage().Success); // A disposed reference cannot exhaust the family forever.
            Assert.Equal(32, Aurora3DLauncher.ClearStagedOverrides(f.Session, out _));
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
            await TestIo.AwaitUsageAsync(other, 0, 0);
            Assert.True(f.Stage().Success); // Clear is reusable.
            f.Session.Dispose();
            Assert.False(f.Stage().Success); // Dispose is terminal for the session.
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        }
        finally { other.Stop(); }
    }

    [Fact]
    public async Task Native_AuroraFamilyCleanup_PreservesMagicAndUnenrolledSameRouteOwner()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        string magicRoot = Path.Combine(f.Root, "magic", "11");
        TestDirectory.CreatePrivate(magicRoot);
        File.WriteAllBytes(Path.Combine(magicRoot, "0015.bin"), new byte[] { 0x15 });
        f.Server.MapPrefix("/data/FinalFantasyX/11", magicRoot);
        var magic = MagicOverrideService.StageMagicOverride(
            0x15, new byte[] { 0xD1 }, magicRoot, f.ViewerRoot, f.Server);
        using var magicLease = magic.MemoryLease;
        Assert.True(magic.Success, magic.Message);
        var aurora = f.Stage();
        Assert.True(aurora.Success, aurora.Message);
        Assert.True(f.Server.TryMapExactBytes(AuroraMemoryFixture.Route, new byte[] { 0xC1 }, out var foreign));
        using (foreign)
        {
            Assert.Equal(1, Aurora3DLauncher.RestoreAllOverrides(f.Session, out _));
            Assert.False(aurora.MemoryLease!.IsActive);
            Assert.True(magicLease!.IsActive);
            Assert.True(foreign!.IsActive);
            Assert.Equal(new byte[] { 0xC1 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            Assert.Equal(new byte[] { 0xD1 }, await f.Client.GetByteArrayAsync("/data/FinalFantasyX/11/0015.bin"));
            await TestIo.AwaitUsageAsync(f.Server, 2, 2);
        }
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        f.AssertNoOutput();
    }
}
