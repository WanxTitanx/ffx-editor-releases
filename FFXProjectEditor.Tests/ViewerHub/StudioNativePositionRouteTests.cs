using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class StudioNativePositionRouteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-position-route-" + Guid.NewGuid().ToString("N"));
    private readonly StudioWebServer _server = new();
    private readonly HttpClient _client = new();
    private readonly string _origin;

    public StudioNativePositionRouteTests()
    {
        Directory.CreateDirectory(_root);
        _server.MapPrefix("/noclip", _root);
        Assert.True(_server.Start(0), _server.Status);
        _origin = $"http://127.0.0.1:{_server.Port}";
    }

    [Fact]
    public async Task NativeRoute_RequiresBothSameOriginAndCurrentCapability()
    {
        int calls = 0;
        string token = _server.ConfigureNativePositions((_, _) =>
        {
            calls++;
            return Task.FromResult((true, "{\"ok\":true}"));
        });
        using var missing = await Send("/positions", "wrong", _origin);
        using var crossOrigin = await Send("/positions", token, "http://127.0.0.1:1");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        Assert.Equal(0, calls);
        using var valid = await Send("/position-session", token, _origin);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NativeRoute_WaitsForWriteAndReportsFailureInsteadOfPrematureSuccess()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string token = _server.ConfigureNativePositions(async (route, _) =>
        {
            Assert.Equal("/positions", route);
            entered.SetResult();
            await release.Task;
            return (false, "{\"ok\":false,\"message\":\"write rejected\"}");
        });
        try
        {
            Task<HttpResponseMessage> pending = Send("/positions", token, _origin);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            release.SetResult();
            using var response = await pending;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("write rejected", await response.Content.ReadAsStringAsync());
        }
        finally { release.TrySetResult(); }
    }

    private Task<HttpResponseMessage> Send(string route, string token, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _origin + "/api/aurora" + route);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-FFX-Studio-Token", token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return _client.SendAsync(request);
    }

    public void Dispose()
    {
        _client.Dispose(); _server.Stop(); Directory.Delete(_root, true);
    }
}
