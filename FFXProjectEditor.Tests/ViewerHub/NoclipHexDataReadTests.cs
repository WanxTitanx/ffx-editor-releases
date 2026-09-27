// WHY: A Linux NoClip request must reach existing hex-cased extraction bytes without changing them.
// MAINT: Use real loopback GET/HEAD; native-only refusal cases are not Windows runtime evidence.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class NoclipHexDataReadTests : IDisposable
{
    private const string Route = "/data/FinalFantasyX/0e/abcd.bin";
    private readonly string _root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "noclip-hex-" + Guid.NewGuid().ToString("N"));
    private readonly string _directory;
    private readonly StudioWebServer _server = new();
    private readonly HttpClient _client;

    public NoclipHexDataReadTests()
    {
        string data = Path.Combine(_root, "data");
        _directory = Path.Combine(data, "FinalFantasyX", "0e");
        TestDirectory.CreatePrivate(_directory);
        _server.MapPrefix("/data", data);
        Assert.True(_server.Start(0), _server.Status);
        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public void Dispose()
    {
        G.BeforeHandleOperationForTests = null;
        _client.Dispose();
        _server.Stop();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("ABCD", "abcd")]
    [InlineData("aBcD", "abcd")]
    [InlineData("abcd", "ABCD")]
    [InlineData("0123", "0123")]
    public async Task HexCase_GetAndHeadReadExistingBytesWithoutMutation(string stored, string requested)
    {
        byte[] bytes = { 0xAB, 0xCD, 0x12, 0x34 };
        string path = Path.Combine(_directory, stored + ".bin");
        File.WriteAllBytes(path, bytes);
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string route = "/data/FinalFantasyX/0e/" + requested + ".bin";
        Assert.Equal(bytes, await _client.GetByteArrayAsync(route));
        using var request = new HttpRequestMessage(HttpMethod.Head, route);
        using var head = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal((long)bytes.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Native_AmbiguousSpellingsRefuseEvenWhenExactNameExists()
    {
        if (!L.IsSupported) return;
        File.WriteAllBytes(Path.Combine(_directory, "abcd.bin"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_directory, "ABCD.bin"), new byte[] { 2 });
        using var response = await _client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task Native_HardlinkedSpellingsAreStillAmbiguous()
    {
        if (!L.IsSupported) return;
        string first = Path.Combine(_directory, "abcd.bin");
        string alias = Path.Combine(_directory, "ABCD.bin");
        File.WriteAllBytes(first, new byte[] { 1 });
        var info = new ProcessStartInfo("ln") { UseShellExecute = false };
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(first);
        info.ArgumentList.Add(alias);
        using var link = Process.Start(info)!;
        Assert.True(link.WaitForExit(5000), "Private hardlink fixture creation timed out.");
        Assert.Equal(0, link.ExitCode);
        using var directory = L.OpenRoot(_directory);
        using var firstHandle = L.OpenRead(directory, "abcd.bin");
        using var aliasHandle = L.OpenRead(directory, "ABCD.bin");
        Assert.Equal(L.Observe(firstHandle, L.RegularFileType).Identity,
            L.Observe(aliasHandle, L.RegularFileType).Identity);
        using var response = await _client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task DirectorySpellingKeepsTheExistingFilesystemCaseRule()
    {
        string upperDirectory = Path.Combine(Path.GetDirectoryName(_directory)!, "0F");
        TestDirectory.CreatePrivate(upperDirectory);
        File.WriteAllBytes(Path.Combine(upperDirectory, "ABCD.bin"), new byte[] { 1 });
        using var response = await _client.GetAsync("/data/FinalFantasyX/0f/abcd.bin");
        Assert.Equal(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Single(Directory.GetFiles(upperDirectory));
    }

    [Fact]
    public async Task Native_UnsafeAliasCannotBeHiddenByASafeSpelling()
    {
        if (!L.IsSupported) return;
        string outside = Path.Combine(_root, "outside.bin");
        File.WriteAllBytes(outside, new byte[] { 9 });
        File.WriteAllBytes(Path.Combine(_directory, "abcd.bin"), new byte[] { 1 });
        File.CreateSymbolicLink(Path.Combine(_directory, "ABCD.bin"), outside);
        using var response = await _client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(outside));
    }

    [Fact]
    public async Task Native_ParentChangeDuringProbeRefuses()
    {
        if (!L.IsSupported) return;
        File.WriteAllBytes(Path.Combine(_directory, "abcd.bin"), new byte[] { 1 });
        bool changed = false;
        G.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (!changed && operation == "open-read-relative" && Path.GetFileName(path) == "Abcd.bin")
            {
                changed = true;
                File.WriteAllBytes(Path.Combine(_directory, "added.txt"), new byte[] { 7 });
            }
        };
        using var response = await _client.GetAsync(Route);
        Assert.True(changed, "The synchronized namespace-change hook must execute.");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Native_FourHexLettersUseSixteenProbesAndOneFinalRead()
    {
        if (!L.IsSupported) return;
        File.WriteAllBytes(Path.Combine(_directory, "aBcD.bin"), new byte[] { 1 });
        int probes = 0;
        G.BeforeHandleOperationForTests = (operation, _) =>
        {
            if (operation == "open-read-relative") probes++;
        };
        Assert.Equal(new byte[] { 1 }, await _client.GetByteArrayAsync(Route));
        Assert.Equal(17, probes);
    }

    [Theory]
    [InlineData("Texture.bin", "texture.bin")]
    [InlineData("ABCD.BIN", "abcd.BIN")]
    public async Task OtherNamesKeepTheExistingFilesystemCaseRule(string stored, string requested)
    {
        File.WriteAllBytes(Path.Combine(_directory, stored), new byte[] { 1 });
        using var response = await _client.GetAsync("/data/FinalFantasyX/0e/" + requested);
        Assert.Equal(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExplicitExactFileKeepsPrecedenceOverHexPrefixFallback()
    {
        File.WriteAllBytes(Path.Combine(_directory, "ABCD.bin"), new byte[] { 1 });
        string exact = Path.Combine(_root, "exact.bin");
        File.WriteAllBytes(exact, new byte[] { 2 });
        Assert.True(_server.TryMapExactFile(Route, exact));
        Assert.Equal(new byte[] { 2 }, await _client.GetByteArrayAsync(Route));
        Assert.True(_server.TryUnmapExactFile(Route, exact));
        Assert.Equal(new byte[] { 1 }, await _client.GetByteArrayAsync(Route));
    }
}
