using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.AuroraChamber;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class AuroraDragBridgeSecurityTests
{
    [Fact]
    public void Authorization_RequiresExactOriginRouteMethodAndCapabilityToken()
    {
        AuroraDragBridge.Stop();
        AuroraDragBridge.EnsureStarted();
        try
        {
            Assert.True(AuroraDragBridge.IsRunning);
            Assert.True(AuroraDragBridge.SetAllowedOrigin("http://127.0.0.1:43123/map/index.html"));

            var valid = new Dictionary<string, string>
            {
                ["Origin"] = "http://127.0.0.1:43123",
                ["X-FFX-Studio-Token"] = AuroraDragBridge.RequestToken,
            };
            Assert.True(AuroraDragBridge.IsAuthorized("POST", "/drag", valid, out string? origin));
            Assert.Equal("http://127.0.0.1:43123", origin);

            valid["Origin"] = "http://evil.127.0.0.1:43123";
            Assert.False(AuroraDragBridge.IsAuthorized("POST", "/drag", valid, out _));
            valid["Origin"] = "http://127.0.0.1:43123";
            valid["X-FFX-Studio-Token"] = "wrong";
            Assert.False(AuroraDragBridge.IsAuthorized("POST", "/drag", valid, out _));
            valid["X-FFX-Studio-Token"] = AuroraDragBridge.RequestToken;
            Assert.False(AuroraDragBridge.IsAuthorized("POST", "/drag/extra", valid, out _));
            Assert.False(AuroraDragBridge.IsAuthorized("GET", "/drag", valid, out _));

            var preflight = new Dictionary<string, string>
            {
                ["Origin"] = "http://127.0.0.1:43123",
                ["Access-Control-Request-Method"] = "POST",
                ["Access-Control-Request-Headers"] = "content-type, x-ffx-studio-token",
            };
            Assert.True(AuroraDragBridge.IsAuthorized("OPTIONS", "/save", preflight, out _));
        }
        finally
        {
            AuroraDragBridge.Stop();
        }
    }

    [Fact]
    public async Task SlowClients_AreBoundedBeforeHandlerTasksAndShutdownCancelsThem()
    {
        AuroraDragBridge.Stop();
        AuroraDragBridge.EnsureStarted();
        var clients = new List<TcpClient>();
        try
        {
            for (int index = 0; index < AuroraDragBridge.MaxConcurrentClients + 8; index++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, AuroraDragBridge.Port);
                clients.Add(client);
            }

            await AuroraDragBridge.WaitForActiveClientCountForTests(
                AuroraDragBridge.MaxConcurrentClients,
                System.TimeSpan.FromSeconds(3));

            Assert.Equal(AuroraDragBridge.MaxConcurrentClients, AuroraDragBridge.ActiveClientCountForTests);
            Assert.True(AuroraDragBridge.IsRunning);

            AuroraDragBridge.Stop();
            await AuroraDragBridge.WaitForActiveClientCountForTests(0, System.TimeSpan.FromSeconds(3));
            Assert.Equal(0, AuroraDragBridge.ActiveClientCountForTests);
        }
        finally
        {
            foreach (TcpClient client in clients) client.Dispose();
            AuroraDragBridge.Stop();
        }
    }

    [Fact]
    public async Task Stop_AfterAcceptBeforeRegistrationDisposesTheStaleGenerationClient()
    {
        AuroraDragBridge.Stop();
        var acceptedSocket = new TaskCompletionSource<Socket>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAccept = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        AuroraDragBridge.AfterAcceptBeforeRegistrationForTests = client =>
        {
            acceptedSocket.TrySetResult(client.Client);
            return releaseAccept.Task;
        };

        using var caller = new TcpClient();
        try
        {
            AuroraDragBridge.EnsureStarted();
            await caller.ConnectAsync(IPAddress.Loopback, AuroraDragBridge.Port);
            Socket accepted = await acceptedSocket.Task.WaitAsync(System.TimeSpan.FromSeconds(3));

            AuroraDragBridge.Stop();
            releaseAccept.TrySetResult();

            Assert.True(await Task.Run(() => System.Threading.SpinWait.SpinUntil(
                () => accepted.SafeHandle.IsClosed,
                System.TimeSpan.FromSeconds(3))));
            Assert.Equal(0, AuroraDragBridge.ActiveClientCountForTests);
            Assert.False(AuroraDragBridge.IsRunning);
        }
        finally
        {
            releaseAccept.TrySetResult();
            AuroraDragBridge.AfterAcceptBeforeRegistrationForTests = null;
            AuroraDragBridge.Stop();
        }
    }
}
