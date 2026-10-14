using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class NoclipBootstrapTests : IDisposable
{
    private readonly string _root;
    private readonly StudioWebServer _server;

    public NoclipBootstrapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ffx-noclip-fetch-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(_root, "data", "FinalFantasyX");
        Directory.CreateDirectory(Path.Combine(data, "0c"));
        Directory.CreateDirectory(Path.Combine(data, "edits"));
        File.WriteAllBytes(Path.Combine(data, "edits", "1.json"), new byte[] { 0x7B, 0x7D });

        _server = new StudioWebServer();
        _server.MapPrefix("/data", Path.Combine(_root, "data"));
        Assert.True(_server.Start(0), _server.Status);
    }

    public void Dispose()
    {
        _server.Stop();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void CdnRelativePath_AcceptsOnlyCanonicalNoclipShapes()
    {
        Assert.True(StudioWebServer.TryBuildNoclipCdnRelativePath(
            "FinalFantasyX/0c/0001.bin", out string? file));
        Assert.Equal("0c/0001.bin", file);
        Assert.True(StudioWebServer.TryBuildNoclipCdnRelativePath(
            "FinalFantasyX/common_textures.bin", out file));
        Assert.Equal("common_textures.bin", file);

        // The local overlay area, traversal, wrong roots and non-bin payloads never fetch.
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("FinalFantasyX/edits/1.json", out _));
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("FinalFantasyX/0c/evil.exe", out _));
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("FinalFantasyX/zz/0000.bin", out _));
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("FinalFantasyX/0c/sub/0000.bin", out _));
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("OtherGame/0c/0000.bin", out _));
        Assert.False(StudioWebServer.TryBuildNoclipCdnRelativePath("FinalFantasyX/0c/", out _));
    }

    [Fact]
    public async Task FetchThrough_BlockedShapesReturn404WithoutTouchingTheNetwork()
    {
        _server.EnableNoclipFetchThrough(Path.Combine(_root, "data"));

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}") };

        // Shape-blocked paths must 404 immediately — they can never reach the CDN.
        using HttpResponseMessage edits = await client.GetAsync("/data/FinalFantasyX/edits/9.json");
        Assert.Equal(HttpStatusCode.NotFound, edits.StatusCode);

        using HttpResponseMessage traversal = await client.GetAsync("/data/FinalFantasyX/0c/evil.exe");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
    }

    [Fact]
    public async Task MissingFile_WithoutFetchThrough_Returns404()
    {
        // Fetch-through disabled: even a canonical noclip shape is an honest 404, no network call.
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}") };
        using HttpResponseMessage response = await client.GetAsync("/data/FinalFantasyX/0c/9999.bin");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void AutoDiscovery_FindsRealExtractionsUnderDownloads()
    {
        string downloads = Path.Combine(Path.GetTempPath(), "ffx-downloads-" + Guid.NewGuid().ToString("N"));
        string real = Path.Combine(downloads, "noclip-data");
        Directory.CreateDirectory(Path.Combine(real, "data", "FinalFantasyX"));
        Directory.CreateDirectory(Path.Combine(downloads, "unrelated"));
        try
        {
            string[] found = NoclipLocator.EnumerateAutoDiscoveryRoots(downloads).ToArray();
            Assert.Contains(Path.GetFullPath(real), found);
            Assert.DoesNotContain(Path.GetFullPath(Path.Combine(downloads, "unrelated")), found);
        }
        finally
        {
            Directory.Delete(downloads, recursive: true);
        }
    }

    [Fact]
    public void BootstrapRoot_IdentityIsExact()
    {
        Assert.True(NoclipDataBootstrap.IsBootstrapRoot(NoclipDataBootstrap.BootstrapRoot));
        Assert.True(NoclipDataBootstrap.IsBootstrapDataRoot(
            Path.Combine(NoclipDataBootstrap.BootstrapRoot, "data")));
        Assert.False(NoclipDataBootstrap.IsBootstrapRoot(Path.GetTempPath()));
        Assert.False(NoclipDataBootstrap.IsBootstrapDataRoot(Path.GetTempPath()));
        Assert.False(NoclipDataBootstrap.IsBootstrapRoot(null));
    }
}
