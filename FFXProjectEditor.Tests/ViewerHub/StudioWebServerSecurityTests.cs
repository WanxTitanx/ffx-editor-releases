using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    /// <summary>
    /// Regression tests for the Studio viewer server's loopback trust boundary.
    /// These tests intentionally use raw HTTP where HttpClient would normalize an attack path.
    /// </summary>
    [Collection(FileSystemReparseGuardHookCollection.Name)]
    public sealed class StudioWebServerSecurityTests : IDisposable
    {
        private readonly StudioWebServer _server;
        private readonly string _root;
        private readonly string _writeRoot;

        public StudioWebServerSecurityTests()
        {
            _root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "viewerhub-security-" + Guid.NewGuid().ToString("N"));
            _writeRoot = _root + "-writes";
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "index.html"), "<html>secure-viewer</html>");
            File.WriteAllText(Path.Combine(_root, "unknown.payload"), "must-not-be-sniffed");

            _server = new StudioWebServer(_writeRoot)
                .MapHost("secure.localhost", _root)
                .MapPrefix("/data/FinalFantasyX/edits", _writeRoot);
            Assert.True(_server.Start(0), _server.Status);
        }

        public void Dispose()
        {
            _server.Stop();
            try { Directory.Delete(_root, recursive: true); } catch { }
            try { Directory.Delete(_writeRoot, recursive: true); } catch { }
            try { File.Delete(_writeRoot); } catch { }
            try { Directory.Delete(_root + "-secrets", recursive: true); } catch { }
            try { Directory.Delete(_root + "-outside", recursive: true); } catch { }
        }

        [Fact]
        public async Task DynamicPort_IsFactualAndReachableOnLoopback()
        {
            Assert.InRange(_server.Port, 1, 65535);
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"\"port\":{_server.Port}", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task OccupiedPort_FailsWithoutDisturbingOwner()
        {
            var owner = new TcpListener(IPAddress.Loopback, 0);
            owner.Start();
            int port = ((IPEndPoint)owner.LocalEndpoint).Port;
            var contender = new StudioWebServer();
            try
            {
                Assert.False(contender.Start(port));

                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                Assert.True(probe.Connected);
            }
            finally
            {
                contender.Stop();
                owner.Stop();
            }
        }

        [Fact]
        public async Task Responses_HaveSafeHeadersAndNoCorsWildcard()
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/index.html");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'self' data: blob:", csp, StringComparison.Ordinal);
            Assert.Contains("connect-src 'self'", csp, StringComparison.Ordinal);
            Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(csp, @"(^|[\s;])\*($|[\s;])"), csp);
            Assert.DoesNotContain("http:", csp, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https:", csp, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnknownExtension_UsesOctetStreamWithNosniff()
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/unknown.payload");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        }

        [Fact]
        public async Task Head_ReturnsMetadataWithoutBody()
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Head, "/index.html");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Content.Headers.ContentLength > 0);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task UnsupportedMethod_Returns405WithExplicitAllowHeader()
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, "/index.html");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Contains("GET", response.Content.Headers.Allow);
            Assert.Contains("HEAD", response.Content.Headers.Allow);
        }

        [Fact]
        public async Task HealthRoute_IsExact()
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/healthcheck");
            request.Headers.Host = "secure.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PrefixRoute_MatchesBaseAndRejectsLookalike()
        {
            string prefixRoot = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "viewerhub-prefix-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(prefixRoot);
            File.WriteAllText(Path.Combine(prefixRoot, "index.html"), "prefix-ok");
            var prefixServer = new StudioWebServer().MapPrefix("magic", prefixRoot);
            try
            {
                Assert.True(prefixServer.Start(0), prefixServer.Status);
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{prefixServer.Port}") };

                using HttpResponseMessage exact = await client.GetAsync("/magic");
                using HttpResponseMessage lookalike = await client.GetAsync("/magicx/index.html");

                Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, lookalike.StatusCode);
            }
            finally
            {
                prefixServer.Stop();
                try { Directory.Delete(prefixRoot, recursive: true); } catch { }
            }
        }

        [Theory]
        [InlineData("/../{SIBLING}/secret.txt")]
        [InlineData("/%2e%2e/{SIBLING}/secret.txt")]
        [InlineData("/%252e%252e/{SIBLING}/secret.txt")]
        public async Task TraversalVariants_AreRejected(string attackTemplate)
        {
            string sibling = _root + "-secrets";
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "secret.txt"), "outside-secret");
            string attack = attackTemplate.Replace("{SIBLING}", Path.GetFileName(sibling), StringComparison.Ordinal);

            string response = await SendRawAsync(
                $"GET {attack} HTTP/1.1\r\nHost: secure.localhost\r\nConnection: close\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 403", response);
            Assert.DoesNotContain("outside-secret", response);
        }

        [Fact]
        public async Task AlternateDataStreamSyntax_IsRejected()
        {
            string response = await SendRawAsync(
                "GET /index.html::$DATA HTTP/1.1\r\nHost: secure.localhost\r\nConnection: close\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 403", response);
        }

        [Fact]
        public async Task ReparsePointEscape_IsRejected()
        {
            string outside = _root + "-outside";
            string link = Path.Combine(_root, "linked");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "reparse-secret");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (
                OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
            {
                // Directory junctions do not require Developer Mode and exercise the same reparse guard.
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/J");
                startInfo.ArgumentList.Add(link);
                startInfo.ArgumentList.Add(outside);
                using Process process = Process.Start(startInfo)!;
                Assert.True(process.WaitForExit(5000), "mklink /J timed out");
                Assert.Equal(0, process.ExitCode);
            }

            try
            {
                string response = await SendRawAsync(
                    "GET /linked/secret.txt HTTP/1.1\r\nHost: secure.localhost\r\nConnection: close\r\n\r\n");

                Assert.StartsWith("HTTP/1.1 403", response);
                Assert.DoesNotContain("reparse-secret", response);
            }
            finally
            {
                try { Directory.Delete(link); } catch { }
            }
        }

        [Fact]
        public async Task StaticRead_PathSwapAfterValidationCannotEscapeTheOpenedHandle()
        {
            string safeBackup = _root + "-safe-backup";
            string outside = _root + "-swap-outside";
            string poison = _root + "-swap-link";
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "index.html"), "swapped-outside-secret");
            CreateDirectoryLink(poison, outside);
            int swapped = 0;

            FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
            {
                if (operation != "open-read" ||
                    !string.Equals(path, Path.Combine(_root, "index.html"), StringComparison.OrdinalIgnoreCase) ||
                    Interlocked.Exchange(ref swapped, 1) != 0)
                    return;

                Directory.Move(_root, safeBackup);
                Directory.Move(poison, _root);
            };

            try
            {
                using var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, "/index.html");
                request.Headers.Host = "secure.localhost";

                using HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain("swapped-outside-secret", body, StringComparison.Ordinal);
                Assert.Equal(1, Volatile.Read(ref swapped));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                try { if (Directory.Exists(_root)) Directory.Delete(_root); } catch { }
                if (Directory.Exists(safeBackup)) Directory.Move(safeBackup, _root);
                try { if (Directory.Exists(poison)) Directory.Delete(poison); } catch { }
                try { Directory.Delete(outside, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task HeaderNameMustMatchExactly_XHostCannotImpersonateHost()
        {
            string response = await SendRawAsync(
                "GET /index.html HTTP/1.1\r\nX-Host: secure.localhost\r\nConnection: close\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 400", response);
            Assert.DoesNotContain("secure-viewer", response);
        }

        [Fact]
        public async Task OversizedAndAmbiguousRequests_AreRejectedBeforeBodyRead()
        {
            string oversizedHeader = "GET /index.html HTTP/1.1\r\nHost: secure.localhost\r\nX-Fill: " +
                new string('a', 17_000) + "\r\n\r\n";
            string oversizedResponse = await SendRawAsync(oversizedHeader);
            Assert.StartsWith("HTTP/1.1 431", oversizedResponse);

            string duplicateLengthResponse = await SendRawAsync(
                "POST /api/edits/239 HTTP/1.1\r\nHost: secure.localhost\r\n" +
                "Content-Type: application/json\r\nContent-Length: 10\r\nContent-Length: 10\r\n\r\n");
            Assert.StartsWith("HTTP/1.1 400", duplicateLengthResponse);

            string oversizedBodyResponse = await SendRawAsync(
                $"POST /api/edits/239 HTTP/1.1\r\nHost: 127.0.0.1:{_server.Port}\r\n" +
                $"Origin: http://127.0.0.1:{_server.Port}\r\n" +
                "Content-Type: application/json\r\nContent-Length: 65537\r\n\r\n");
            Assert.StartsWith("HTTP/1.1 413", oversizedBodyResponse);
        }

        [Fact]
        public async Task EditsPost_UnknownHostCannotWrite()
        {
            using var client = CreateClient();
            using var request = JsonPost("/api/edits/239", "{\"slot\":1}");
            request.Headers.Host = "evil.localhost";

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(_root, "data", "FinalFantasyX", "edits", "239.json")));
            Assert.False(File.Exists(Path.Combine(_writeRoot, "239.json")));
        }

        [Fact]
        public async Task EditsPost_RejectsCrossOriginAndNonJsonContent()
        {
            using var client = CreateClient();
            using (var crossOrigin = JsonPost("/api/edits/239", "{\"slot\":1}"))
            {
                crossOrigin.Headers.Host = $"127.0.0.1:{_server.Port}";
                crossOrigin.Headers.Add("Origin", $"http://evil.localhost:{_server.Port}");
                using HttpResponseMessage response = await client.SendAsync(crossOrigin);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using (var text = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239"))
            {
                AddSameOrigin(text);
                text.Content = new StringContent("{\"slot\":1}", Encoding.UTF8, "text/plain");
                using HttpResponseMessage response = await client.SendAsync(text);
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            }
        }

        [Fact]
        public async Task EditsPost_SameOriginJsonRemainsCompatible()
        {
            using var client = CreateClient();
            using var request = JsonPost("/api/edits/239", "{\"slot\":1,\"position\":[1,2,3]}");
            AddSameOrigin(request);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(File.Exists(Path.Combine(_writeRoot, "239.json")));
            Assert.False(File.Exists(Path.Combine(
                _root, "data", "FinalFantasyX", "edits", "239.json")));
        }

        [Fact]
        public async Task EditsPost_OriginIsMandatoryAndMustMatchTheLoopbackHostAndPort()
        {
            using var client = CreateClient();

            using (var missingOrigin = JsonPost("/api/edits/239", "{\"slot\":1}"))
            {
                missingOrigin.Headers.Host = $"127.0.0.1:{_server.Port}";
                using HttpResponseMessage response = await client.SendAsync(missingOrigin);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using (var aliasMismatch = JsonPost("/api/edits/239", "{\"slot\":1}"))
            {
                aliasMismatch.Headers.Host = $"localhost:{_server.Port}";
                aliasMismatch.Headers.Add("Origin", $"http://127.0.0.1:{_server.Port}");
                using HttpResponseMessage response = await client.SendAsync(aliasMismatch);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            Assert.False(File.Exists(Path.Combine(_writeRoot, "239.json")));
        }

        [Fact]
        public async Task InternalWriteFailure_DoesNotLeakFilesystemPath()
        {
            File.WriteAllText(_writeRoot, "blocks-directory");
            using var client = CreateClient();
            using var request = JsonPost("/api/edits/239", "{\"slot\":1}");
            AddSameOrigin(request);

            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(_root, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("blocks-directory", body, StringComparison.OrdinalIgnoreCase);
        }

        private HttpClient CreateClient() => new() { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}") };

        private static HttpRequestMessage JsonPost(string path, string json) => new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private void AddSameOrigin(HttpRequestMessage request)
        {
            request.Headers.Host = $"127.0.0.1:{_server.Port}";
            request.Headers.Add("Origin", $"http://127.0.0.1:{_server.Port}");
        }

        private async Task<string> SendRawAsync(string request)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _server.Port);
            using NetworkStream stream = client.GetStream();
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);

            using var response = new MemoryStream();
            try
            {
                await stream.CopyToAsync(response);
            }
            catch (IOException) when (response.Length > 0)
            {
                // Windows can reset a connection when the server rejects an oversized request while
                // unread attack bytes remain queued. The already received HTTP response is authoritative.
            }
            return Encoding.UTF8.GetString(response.ToArray());
        }

        private static void CreateDirectoryLink(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }
            catch (Exception ex) when (
                OperatingSystem.IsWindows() && ex is UnauthorizedAccessException or IOException)
            {
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/J");
                startInfo.ArgumentList.Add(link);
                startInfo.ArgumentList.Add(target);
                using Process process = Process.Start(startInfo)!;
                Assert.True(process.WaitForExit(5000), "mklink /J timed out");
                Assert.Equal(0, process.ExitCode);
            }
        }
    }
}
