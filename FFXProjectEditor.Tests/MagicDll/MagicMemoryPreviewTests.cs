// WHY: Native Magic preview must consume the accepted lease architecture through real HTTP.
// MAINT: Fixtures are synthetic/private, not corpus proof. Native-only bodies are not Windows
// evidence. The shared guard-hook collection serializes every test that uses the existing hook.
using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Resources;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Tests.MagicDll;

internal sealed class MagicPreviewFixture : IAsyncDisposable
{
    private readonly List<StudioWebServer.ExactMemoryLease> _leases = new();
    internal string Root { get; } = Path.Combine(
        FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "magic-memory-preview-" + Guid.NewGuid().ToString("N"));
    internal string MagicDirectory { get; }
    internal string ViewerRoot { get; }
    internal string SessionRoot { get; }
    internal StudioWebServer Server { get; } = new();
    internal HttpClient Client { get; }

    internal MagicPreviewFixture()
    {
        MagicDirectory = Path.Combine(Root, "selected", "data", "FinalFantasyX", "11");
        ViewerRoot = Path.Combine(Root, "viewer-output-must-stay-absent");
        SessionRoot = Path.Combine(Root, "viewer-data");
        Directory.CreateDirectory(MagicDirectory);
        try
        {
            Server.MapPrefix("/data/FinalFantasyX/11", MagicDirectory);
            Server.MapPrefix("/viewer-data", SessionRoot);
            Assert.True(Server.Start(0), Server.Status);
            Client = NewClient(Server);
        }
        catch
        {
            Server.Stop();
            Directory.Delete(Root, recursive: true);
            throw;
        }
    }

    internal static HttpClient NewClient(StudioWebServer server) => new()
    {
        BaseAddress = new Uri("http://127.0.0.1:" + server.Port),
        Timeout = TimeSpan.FromSeconds(5),
    };

    internal string Selected(string name, params byte[] bytes)
    {
        string path = Path.Combine(MagicDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal MagicOverrideResult Stage(int id, byte[] bytes)
    {
        MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
            id, bytes, MagicDirectory, ViewerRoot, Server);
        Track(result);
        return result;
    }

    internal void Track(MagicOverrideResult result)
    {
        if (result.MemoryLease is { } lease)
            _leases.Add(lease);
    }

    internal StudioWebServer.ExactMemoryLease Map(string route, params byte[] bytes)
    {
        Assert.True(Server.TryMapExactBytes(route, bytes, out var lease));
        Assert.NotNull(lease);
        _leases.Add(lease!);
        return lease!;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static string[] Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

    internal void AssertNoOutput()
    {
        Assert.False(Directory.Exists(ViewerRoot));
        Assert.Empty(Directory.GetFiles(Root, "*.magic3d.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories));
    }

    internal async Task AssertMemoryAsync(MagicOverrideResult result, byte[] expected)
    {
        Assert.True(result.Success, result.Message);
        Assert.Null(result.StagedPath);
        Assert.NotNull(result.MemoryLease);
        Assert.True(result.MemoryLease!.IsActive);
        Assert.True(result.MemoryLease.IsOwnedBy(Server));
        Assert.Equal(result.RequestPath, result.MemoryLease.RequestPath);
        Assert.Equal(Hash(expected), result.Sha256);
        Assert.True(MagicOverrideService.TryRegisterPreview(Server, result));
        using var get = await Client.GetAsync(result.RequestPath);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/octet-stream", get.Content.Headers.ContentType?.MediaType);
        Assert.True(get.Headers.CacheControl?.NoStore == true);
        byte[] actual = await get.Content.ReadAsByteArrayAsync();
        Assert.Equal(expected, actual);
        Assert.Equal(result.Sha256, Hash(actual));
        using var request = new HttpRequestMessage(HttpMethod.Head, result.RequestPath);
        using var head = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal((long)expected.Length, head.Content.Headers.ContentLength);
        Assert.Equal("application/octet-stream", head.Content.Headers.ContentType?.MediaType);
        Assert.True(head.Headers.CacheControl?.NoStore == true);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        AssertNoOutput();
    }

    internal static async Task AwaitUsageAsync(
        StudioWebServer server, long bytes, int entries)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            var state = server.GetExactMemoryUsageForTests();
            if (state.OwnedBytes == bytes && state.OwnedEntries == entries &&
                state.ActiveReaders == 0 && state.RetiredBytes == 0 && state.RetiredEntries == 0)
                return;
            await Task.Delay(10);
        }
        Assert.Fail("Usage predicate deadline exceeded: " +
            server.GetExactMemoryUsageForTests());
    }

    public async ValueTask DisposeAsync()
    {
        G.BeforeHandleOperationForTests = null;
        foreach (var lease in _leases)
            lease.Dispose();
        Client.Dispose();
        Server.Stop();
        try { await AwaitUsageAsync(Server, 0, 0); }
        finally { Directory.Delete(Root, recursive: true); }
    }
}

// These bodies are consumed by the ORIGINAL three test identities on Linux.
internal static class MagicPreviewNativeAssertions
{
    internal static async Task PreserveSourceAsync()
    {
        Assert.True(L.IsSupported, "This native lane requires the Linux x64 read backend.");
        await using var f = new MagicPreviewFixture();
        byte[] vanilla = { 0x56, 0x41, 0x4E, 0x49, 0x4C, 0x4C, 0x41 };
        byte[] custom = { 0x43, 0x55, 0x53, 0x54, 0x4F, 0x4D };
        string selected = f.Selected("00AF.bin", vanilla);
        string source = Path.Combine(f.Root, "magic_00AF.dll");
        File.WriteAllBytes(source, custom);
        string[] beforeFiles = MagicPreviewFixture.Files(f.Root);
        string sourceHash = MagicPreviewFixture.Hash(File.ReadAllBytes(source));
        string selectedHash = MagicPreviewFixture.Hash(File.ReadAllBytes(selected));
        MagicOverrideResult result = f.Stage(0xAF, File.ReadAllBytes(source));
        Assert.Equal("/data/FinalFantasyX/11/00af.bin", result.RequestPath);
        await f.AssertMemoryAsync(result, custom);
        Assert.Equal(sourceHash, MagicPreviewFixture.Hash(File.ReadAllBytes(source)));
        Assert.Equal(selectedHash, MagicPreviewFixture.Hash(File.ReadAllBytes(selected)));
        Assert.Equal(beforeFiles, MagicPreviewFixture.Files(f.Root));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, result));
        Assert.False(result.MemoryLease!.IsActive);
        Assert.Equal(vanilla, await f.Client.GetByteArrayAsync(result.RequestPath));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
    }

    internal static async Task PublicSnapshotsAsync()
    {
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        string source = MagicDllTestFixture.Write(Path.Combine(f.Root, "source"), "magic_0021.dll");
        byte[] expected = File.ReadAllBytes(source);
        var wrapper = new MagicDllDocument_Wrapper();
        Assert.True(wrapper.TryLoad(source, out string error), error);
        byte[] working = wrapper.WorkingBytes!;
        byte[] original = wrapper.SourceBytes!;
        working[0] ^= 0xFF;
        original[1] ^= 0xFF;
        f.Selected("0015.bin", 0x56); // Filename 0021 is decimal ID 21, i.e. hex 0015.
        string[] beforeFiles = MagicPreviewFixture.Files(f.Root);
        MagicOverrideResult result = f.Stage(wrapper.MagicId, wrapper.WorkingBytes!);
        Assert.Equal("/data/FinalFantasyX/11/0015.bin", result.RequestPath);
        await f.AssertMemoryAsync(result, expected);
        Assert.Equal(expected, wrapper.WorkingBytes);
        Assert.Equal(expected, wrapper.SourceBytes);
        Assert.Equal(expected, File.ReadAllBytes(source));
        Assert.Equal(beforeFiles, MagicPreviewFixture.Files(f.Root));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, result));
        Assert.Equal(new byte[] { 0x56 }, await f.Client.GetByteArrayAsync(result.RequestPath));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
    }

    internal static async Task SeparateOwnersAsync()
    {
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Selected("0021.bin", 0x56);
        MagicOverrideResult first = f.Stage(0x21, new byte[] { 0xA1 });
        MagicOverrideResult second = f.Stage(0x21, new byte[] { 0xB2 });
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.NotSame(first.MemoryLease, second.MemoryLease);
        Assert.Null(first.StagedPath);
        Assert.Null(second.StagedPath);
        Assert.True(MagicOverrideService.TryRegisterPreview(f.Server, first));
        Assert.True(MagicOverrideService.TryRegisterPreview(f.Server, second));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 2, 2);
        Assert.Equal(new byte[] { 0xB2 }, await f.Client.GetByteArrayAsync(second.RequestPath));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, first));
        Assert.False(first.MemoryLease!.IsActive);
        Assert.True(second.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0xB2 }, await f.Client.GetByteArrayAsync(second.RequestPath));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, first));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, second));
        Assert.Equal(new byte[] { 0x56 }, await f.Client.GetByteArrayAsync(second.RequestPath));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
        Assert.Single(Directory.GetFiles(f.MagicDirectory));
    }
}

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class MagicMemoryPreviewTests
{
    private const string Route = "/data/FinalFantasyX/11/00af.bin";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_MissingOrStoppedServer_RefusesBeforeSelectedRead(bool provideStopped)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Selected("00AF.bin", 0x56);
        f.Server.Stop();
        bool probed = false;
        G.BeforeHandleOperationForTests = (_, _) => probed = true;
        try
        {
            var result = MagicOverrideService.StageMagicOverride(
                0xAF, new byte[] { 1 }, f.MagicDirectory, f.ViewerRoot,
                provideStopped ? f.Server : null);
            f.Track(result);
            Assert.False(result.Success);
            Assert.Equal(Strings.U_Md_MemoryPreviewUnavailable, result.Message);
            Assert.Null(result.MemoryLease);
            Assert.False(probed);
            f.AssertNoOutput();
        }
        finally { G.BeforeHandleOperationForTests = null; }
    }

    [Theory]
    [InlineData(-1, "bytes")]
    [InlineData(65536, "bytes")]
    [InlineData(0, "empty")]
    [InlineData(0, "null")]
    public async Task Native_InvalidInput_IsRejectedBeforeSelectedRead(int id, string input)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        byte[] bytes = input == "null" ? null! :
            input == "empty" ? Array.Empty<byte>() : new byte[] { 1 };
        bool probed = false;
        G.BeforeHandleOperationForTests = (_, _) => probed = true;
        try
        {
            var result = f.Stage(id, bytes);
            Assert.False(result.Success);
            Assert.Null(result.MemoryLease);
            Assert.Null(result.StagedPath);
            Assert.Equal(input == "bytes"
                ? string.Format(Strings.U_Md_OverrideBinMissing, id)
                : Strings.U_Md_OverrideEmptyDll, result.Message);
            Assert.False(probed);
            f.AssertNoOutput();
        }
        finally { G.BeforeHandleOperationForTests = null; }
    }

    [Fact]
    public async Task Native_Over64MiB_RefusesBeforeCopyOrSelectedRead()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Selected("00AF.bin", 0x56);
        f.Server.LowerExactMemoryLimitsForTests(1, 16, 4);
        var warm = f.Stage(0xAF, new byte[] { 1 });
        Assert.True(warm.Success, warm.Message);
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, warm));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
        // One REAL bounded caller allocation; it is outside the measured producer interval.
        // No fabricated array length, no huge selected file and no server-cap mutation.
        byte[] source = new byte[64 * 1024 * 1024 + 1];
        bool probed = false;
        G.BeforeHandleOperationForTests = (_, _) => probed = true;
        try
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = f.Stage(0xAF, source);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(result.Success);
            Assert.Equal(Strings.U_Md_MemoryPreviewUnavailable, result.Message);
            Assert.Null(result.MemoryLease);
            Assert.True(allocated < 1024 * 1024,
                "Rejected producer allocated " + allocated + " bytes; a payload copy is forbidden.");
            Assert.False(probed);
            Assert.Equal(0, source[0]);
            GC.KeepAlive(source);
            f.AssertNoOutput();
        }
        finally { G.BeforeHandleOperationForTests = null; }
    }

    [Fact]
    public async Task Native_CapacityRefusal_PreservesAnotherOwnerWithByteHeadroom()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Server.LowerExactMemoryLimitsForTests(1024, 4096, 1);
        f.Selected("00AF.bin", 0x56);
        var owner = f.Map(Route, 0xA1);
        var rejected = f.Stage(0xAF, new byte[] { 0xB2 });
        Assert.False(rejected.Success);
        Assert.Equal(Strings.U_Md_MemoryPreviewUnavailable, rejected.Message);
        Assert.Null(rejected.MemoryLease);
        Assert.True(owner.IsActive);
        Assert.Equal(new byte[] { 0xA1 }, await f.Client.GetByteArrayAsync(Route));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 1, 1);
        f.AssertNoOutput();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("missing-directory")]
    [InlineData("leaf-link")]
    [InlineData("ancestor-link")]
    [InlineData("ambiguous")]
    public async Task Native_SelectedRefusal_NeverCreatesAnOwnerOrOutput(string kind)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        string selected = f.Selected("00AF.bin", 0x56);
        string directory = f.MagicDirectory;
        string outside = Path.Combine(f.Root, "outside.bin");
        File.WriteAllBytes(outside, new byte[] { 0x99 });
        if (kind is "missing" or "leaf-link") File.Delete(selected);
        if (kind == "leaf-link") File.CreateSymbolicLink(selected, outside);
        if (kind == "missing-directory") directory = Path.Combine(f.Root, "absent");
        if (kind == "ambiguous") f.Selected("00af.bin", 0x66);
        string? link = null;
        if (kind == "ancestor-link")
        {
            link = Path.Combine(f.Root, "selected-alias");
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(f.MagicDirectory)!);
            directory = Path.Combine(link, "11");
        }
        try
        {
            var result = MagicOverrideService.StageMagicOverride(
                0xAF, new byte[] { 1 }, directory, f.ViewerRoot, f.Server);
            f.Track(result);
            Assert.False(result.Success);
            Assert.Null(result.MemoryLease);
            Assert.Null(result.StagedPath);
            Assert.Equal(kind == "missing"
                ? string.Format(Strings.U_Md_OverrideBinMissing, 0xAF)
                : Strings.U_Md_MemoryPreviewUnavailable, result.Message);
            Assert.Equal(new byte[] { 0x99 }, File.ReadAllBytes(outside));
            if (kind is not ("missing" or "leaf-link"))
                Assert.Equal(new byte[] { 0x56 }, File.ReadAllBytes(selected));
            if (kind == "ambiguous")
                Assert.Equal(new byte[] { 0x66 }, File.ReadAllBytes(Path.Combine(f.MagicDirectory, "00af.bin")));
            await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
            f.AssertNoOutput();
        }
        finally
        {
            if (link != null) Directory.Delete(link);
            if (kind == "leaf-link") File.Delete(selected);
        }
    }

    [Fact]
    public async Task Native_ReadBarrierMutation_HashesAndMapsTheSameOwnedSnapshot()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Selected("00AF.bin", 0x56);
        byte[] caller = { 0xA1, 0xB2, 0xC3 };
        byte[] expected = { 0xA1, 0xB2, 0xC3 };
        bool entered = false;
        G.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (entered || operation != "open-read-relative" ||
                !string.Equals(Path.GetDirectoryName(path), f.MagicDirectory, StringComparison.Ordinal))
                return;
            entered = true; // Synchronous barrier after the producer copy and before hash/map.
            Array.Fill(caller, (byte)0xEE);
        };
        MagicOverrideResult result;
        try { result = f.Stage(0xAF, caller); }
        finally { G.BeforeHandleOperationForTests = null; }
        Assert.True(entered);
        Assert.Equal(new byte[] { 0xEE, 0xEE, 0xEE }, caller);
        await f.AssertMemoryAsync(result, expected);
        Array.Fill(caller, (byte)0xDD);
        Assert.Equal(expected, await f.Client.GetByteArrayAsync(Route));
        Assert.Equal(new byte[] { 0x56 }, File.ReadAllBytes(Path.Combine(f.MagicDirectory, "00AF.bin")));
    }

    [Fact]
    public async Task Native_RegisterRejectsWrongServer_ReleaseUsesOnlyTheSuppliedCapability()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var a = new MagicPreviewFixture();
        await using var b = new MagicPreviewFixture();
        a.Selected("00AF.bin", 0x56);
        b.Selected("00AF.bin", 0x66);
        var first = a.Stage(0xAF, new byte[] { 0xA1 });
        var second = b.Stage(0xAF, new byte[] { 0xB2 });
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        var beforeA = a.Server.GetExactMemoryUsageForTests();
        var beforeB = b.Server.GetExactMemoryUsageForTests();
        Assert.False(MagicOverrideService.TryRegisterPreview(b.Server, first));
        Assert.True(MagicOverrideService.TryRegisterPreview(a.Server, first));
        Assert.True(MagicOverrideService.TryRegisterPreview(a.Server, first));
        Assert.Equal(beforeA, a.Server.GetExactMemoryUsageForTests());
        Assert.Equal(beforeB, b.Server.GetExactMemoryUsageForTests());
        Assert.Equal(new byte[] { 0xA1 }, await a.Client.GetByteArrayAsync(Route));
        Assert.Equal(new byte[] { 0xB2 }, await b.Client.GetByteArrayAsync(Route));
        Assert.True(MagicOverrideService.TryReleasePreview(a.ViewerRoot, b.Server, first));
        Assert.False(first.MemoryLease!.IsActive);
        Assert.True(second.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0x56 }, await a.Client.GetByteArrayAsync(Route));
        Assert.Equal(new byte[] { 0xB2 }, await b.Client.GetByteArrayAsync(Route));
        await MagicPreviewFixture.AwaitUsageAsync(a.Server, 0, 0);
        await MagicPreviewFixture.AwaitUsageAsync(b.Server, 1, 1);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("missing-path")]
    [InlineData("different-path")]
    [InlineData("mixed")]
    [InlineData("disposed")]
    [InlineData("no-owner")]
    public async Task Native_MalformedRegistration_RefusesWithoutFollowingAStagedPath(string kind)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        string selected = f.Selected("00AF.bin", 0x56);
        var valid = f.Stage(0xAF, new byte[] { 0xA1 });
        Assert.True(valid.Success, valid.Message);
        var malformed = kind switch
        {
            "failed" => valid with { Success = false },
            "missing-path" => valid with { RequestPath = null },
            "different-path" => valid with { RequestPath = "/data/FinalFantasyX/11/00b0.bin" },
            "mixed" => valid with { StagedPath = selected },
            "no-owner" => valid with { MemoryLease = null },
            _ => valid,
        };
        if (kind == "disposed") valid.MemoryLease!.Dispose();
        var before = f.Server.GetExactMemoryUsageForTests();
        Assert.False(MagicOverrideService.TryRegisterPreview(f.Server, malformed));
        Assert.Equal(before, f.Server.GetExactMemoryUsageForTests());
        if (kind != "no-owner")
        {
            Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, malformed));
            Assert.False(valid.MemoryLease!.IsActive);
            Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, malformed));
        }
        else
        {
            Assert.False(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, malformed));
            Assert.True(valid.MemoryLease!.IsActive);
            Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, valid));
        }
        Assert.Equal(new byte[] { 0x56 }, File.ReadAllBytes(selected));
        Assert.Equal(new byte[] { 0x56 }, await f.Client.GetByteArrayAsync(Route));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
    }

    [Fact]
    public async Task Native_StopPreservesLease_RegisterRequiresRunningAndRestartKeepsOwner()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(L.IsSupported);
        await using var f = new MagicPreviewFixture();
        f.Selected("00AF.bin", 0x56);
        var result = f.Stage(0xAF, new byte[] { 0xA1 });
        Assert.True(result.Success, result.Message);
        f.Server.Stop();
        Assert.True(result.MemoryLease!.IsActive);
        Assert.True(result.MemoryLease.IsOwnedBy(f.Server));
        Assert.False(MagicOverrideService.TryRegisterPreview(f.Server, result));
        Assert.True(f.Server.Start(0), f.Server.Status);
        Assert.True(MagicOverrideService.TryRegisterPreview(f.Server, result));
        using var client = MagicPreviewFixture.NewClient(f.Server);
        Assert.Equal(new byte[] { 0xA1 }, await client.GetByteArrayAsync(Route));
        Assert.True(MagicOverrideService.TryReleasePreview(f.ViewerRoot, null, result));
        Assert.Equal(new byte[] { 0x56 }, await client.GetByteArrayAsync(Route));
        await MagicPreviewFixture.AwaitUsageAsync(f.Server, 0, 0);
    }

    [Fact]
    public async Task FileVariant_MapsAndUnmapsButCannotDeleteAnUnownedFile()
    {
        await using var f = new MagicPreviewFixture();
        f.Selected("00AF.bin", 0x56);
        string file = Path.Combine(f.Root, "real-exact-file.bin");
        File.WriteAllBytes(file, new byte[] { 0xA1 });
        var result = new MagicOverrideResult(true, "test file", Route, file);
        Assert.True(MagicOverrideService.TryRegisterPreview(f.Server, result));
        Assert.Equal(new byte[] { 0xA1 }, await f.Client.GetByteArrayAsync(Route));
        Assert.False(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, result));
        Assert.Equal(new byte[] { 0x56 }, await f.Client.GetByteArrayAsync(Route));
        Assert.Equal(new byte[] { 0xA1 }, File.ReadAllBytes(file));
        Assert.False(MagicOverrideService.TryReleasePreview(f.ViewerRoot, f.Server, result));
    }

    [Theory]
    [InlineData("en", "in memory")]
    [InlineData("pt", "em memória")]
    public void MemoryMessages_UseResourcesWithoutBackupClaims(string language, string phrase)
    {
        var manager = new ResourceManager("FFXProjectEditor.Resources.Strings", typeof(Strings).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        string applied = manager.GetString("U_Md_MemoryPreviewApplied", culture)!;
        string unavailable = manager.GetString("U_Md_MemoryPreviewUnavailable", culture)!;
        Assert.False(string.IsNullOrWhiteSpace(unavailable));
        Assert.Contains(phrase, applied, StringComparison.Ordinal);
        Assert.Contains("00af", string.Format(culture, applied, 0xAF), StringComparison.Ordinal);
        Assert.DoesNotContain(".magic3d.bak", applied, StringComparison.Ordinal);
        Assert.DoesNotContain(".magic3d.bak", unavailable, StringComparison.Ordinal);
        // No Strings.SetLanguage/ConfigDirectoryOverride or process culture replacement.
    }
}
