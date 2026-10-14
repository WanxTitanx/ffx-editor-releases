using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    /// <summary>
    /// 🐉 Prova do StudioWebServer in-process: health, roteamento por host virtual *.localhost,
    /// estáticos + MIME, path traversal guard e 404 honesto.
    /// </summary>
    public class StudioWebServerTests : IDisposable
    {
        private readonly StudioWebServer _server;
        private readonly string _root;
        private readonly string _writeRoot;
        private int TestPort => _server.Port;

        public StudioWebServerTests()
        {
            _root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "viewerhub-tests-" + Guid.NewGuid().ToString("N"));
            _writeRoot = _root + "-writes";
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "index.html"), "<html><body>viewerhub-ok</body></html>");
            File.WriteAllText(Path.Combine(_root, "app.js"), "console.log('hub');");
            Directory.CreateDirectory(Path.Combine(_root, "nested"));
            File.WriteAllBytes(Path.Combine(_root, "nested", "deep.bin"), new byte[] { 1, 2, 3, 4 });

            _server = new StudioWebServer(_writeRoot)
                .MapHost("test.localhost", _root);
            Assert.True(_server.Start(0), _server.Status);
            Assert.InRange(_server.Port, 1, 65535);
        }

        public void Dispose()
        {
            if (Environment.GetEnvironmentVariable("FFX_KEEP_TEST_FIXTURES") == "1") return;
            _server.Stop();
            try { Directory.Delete(_root, recursive: true); } catch { }
            try { Directory.Delete(_writeRoot, recursive: true); } catch { }
        }

        [Fact]
        public async Task Health_ReturnsOkJson()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.Host = "test.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"ok\"", body);
            Assert.Contains("test.localhost", body);
        }

        [Fact]
        public async Task HostRouting_ServesIndexForKnownHost()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/index.html");
            req.Headers.Host = "test.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("text/html", resp.Content.Headers.ContentType!.MediaType);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("viewerhub-ok", body);
        }

        [Fact]
        public async Task HostRouting_UnknownHost_Returns404()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/index.html");
            req.Headers.Host = "evil.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task EditsPost_GravaSidecarEMergePorSlot()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };

            // 1º POST: slot 3 com position/heading/scale
            using (var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239")
            { Content = new StringContent("{\"slot\":3,\"position\":[10,-2,0.25],\"heading\":1.57,\"scale\":2}", System.Text.Encoding.UTF8, "application/json") })
            {
                AddSameOrigin(post);
                var resp = await client.SendAsync(post);
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                Assert.Contains("\"ok\":true", await resp.Content.ReadAsStringAsync());
            }

            // 2º POST: mesmo encId, outro slot → merge não perde o slot 3
            using (var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239")
            { Content = new StringContent("{\"slot\":5,\"position\":[4,5,6]}", System.Text.Encoding.UTF8, "application/json") })
            {
                AddSameOrigin(post);
                var resp = await client.SendAsync(post);
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            }

            Assert.False(File.Exists(Path.Combine(
                _root, "data", "FinalFantasyX", "edits", "239.json")));
            Assert.True(File.Exists(Path.Combine(_writeRoot, "239.json")));

            // GET devolve o sidecar gravado com os 2 slots
            using (var get = new HttpRequestMessage(HttpMethod.Get, "/data/FinalFantasyX/edits/239.json"))
            {
                get.Headers.Host = "test.localhost";
                var resp = await client.SendAsync(get);
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                string body = await resp.Content.ReadAsStringAsync();
                Assert.Contains("\"3\"", body);
                Assert.Contains("10", body);
                Assert.Contains("\"5\"", body);
                Assert.Contains("1.57", body);
            }
        }

        [Fact]
        public async Task EditsPost_EncIdInvalido_Returns400()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/abc!def")
            { Content = new StringContent("{\"slot\":0}", System.Text.Encoding.UTF8, "application/json") };
            AddSameOrigin(post);
            var resp = await client.SendAsync(post);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task EditsPost_EncounterIdIsBoundedToTheCanonicalUnsigned16BitDomain()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using (var accepted = new HttpRequestMessage(HttpMethod.Post, "/api/edits/65535")
            {
                Content = new StringContent("{\"slot\":0}", System.Text.Encoding.UTF8, "application/json")
            })
            {
                AddSameOrigin(accepted);
                using HttpResponseMessage response = await client.SendAsync(accepted);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }

            using (var rejected = new HttpRequestMessage(HttpMethod.Post, "/api/edits/65536")
            {
                Content = new StringContent("{\"slot\":0}", System.Text.Encoding.UTF8, "application/json")
            })
            {
                AddSameOrigin(rejected);
                using HttpResponseMessage response = await client.SendAsync(rejected);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
            }

            Assert.True(File.Exists(Path.Combine(_writeRoot, "65535.json")));
            Assert.False(File.Exists(Path.Combine(_writeRoot, "65536.json")));
        }

        [Fact]
        public async Task EditsPost_SlotInvalido_Returns400()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239")
            { Content = new StringContent("{\"slot\":9}", System.Text.Encoding.UTF8, "application/json") };
            AddSameOrigin(post);
            var resp = await client.SendAsync(post);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task EditsPost_SemBody_Returns400()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239") { Content = new StringContent("", System.Text.Encoding.UTF8, "application/json") };
            AddSameOrigin(post);
            var resp = await client.SendAsync(post);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task LoopbackAlias_ServesForPlainIpHost()
        {
            // Fluxo REAL do hub após o spike: o WebView2 navega para http://127.0.0.1:8769 (sem DNS).
            // O alias loopback deve rotear para o único host mapeado.
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/index.html");
            req.Headers.Host = $"127.0.0.1:{TestPort}";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("viewerhub-ok", body);
        }

        [Fact]
        public async Task PrefixRouting_ServesAppUnderPrefix()
        {
            // F2: multi-app numa porta única — "/magic/" roteia para o diretório do app, "/" é o fallback.
            string appDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "viewerhub-prefix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "index.html"), "<html>app-under-prefix</html>");
            try
            {
                var srv = new StudioWebServer().MapPrefix("/magic/", appDir);
                Assert.True(srv.Start(0), srv.Status);
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{srv.Port}") };
                var resp = await client.GetAsync("/magic/index.html");
                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                string body = await resp.Content.ReadAsStringAsync();
                Assert.Contains("app-under-prefix", body);
                srv.Stop();
            }
            finally { try { Directory.Delete(appDir, recursive: true); } catch { } }
        }

        [Fact]
        public async Task Static_ServesJsWithJsMime()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/app.js");
            req.Headers.Host = "test.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("text/javascript", resp.Content.Headers.ContentType!.MediaType);
        }

        [Fact]
        public async Task Static_ServesBinary_OctetStream()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/nested/deep.bin");
            req.Headers.Host = "test.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("application/octet-stream", resp.Content.Headers.ContentType!.MediaType);
            byte[] body = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal(4, body.Length);
        }

        [Fact]
        public async Task PathTraversal_IsBlocked()
        {
            // HttpClient normaliza "/../" e até "%2e%2e" ANTES de enviar — o guard do servidor precisa
            // ser exercitado com bytes crus (TcpClient): a request chega literal, sem normalização.
            using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", TestPort);
            var stream = tcp.GetStream();
            byte[] req = System.Text.Encoding.ASCII.GetBytes(
                "GET /../Windows/win.ini HTTP/1.1\r\nHost: test.localhost\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(req);
            var buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer);
            string response = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            Assert.StartsWith("HTTP/1.1 403", response);
        }

        [Fact]
        public async Task MissingFile_Returns404()
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{TestPort}") };
            using var req = new HttpRequestMessage(HttpMethod.Get, "/nao-existe.js");
            req.Headers.Host = "test.localhost";
            var resp = await client.SendAsync(req);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task EditsOverlay_FallsBackToSelectedDataUntilAnExactLocalEditExists()
        {
            string selectedData = Path.Combine(_root, "selected-data");
            string selectedEdits = Path.Combine(selectedData, "FinalFantasyX", "edits");
            string selectedFile = Path.Combine(selectedEdits, "777.json");
            string localEdits = Path.Combine(_root, "local-edits");
            Directory.CreateDirectory(selectedEdits);
            File.WriteAllText(selectedFile, "{\"selected\":true}");

            var server = new StudioWebServer(localEdits).MapPrefix("/data", selectedData);
            try
            {
                Assert.True(server.Start(0), server.Status);
                using var client = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
                };

                string before = await client.GetStringAsync(
                    "/data/FinalFantasyX/edits/777.json");
                Assert.Contains("selected", before, StringComparison.Ordinal);

                using var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/777")
                {
                    Content = new StringContent(
                        "{\"slot\":2,\"position\":[1,2,3]}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                post.Headers.Host = $"127.0.0.1:{server.Port}";
                post.Headers.Add("Origin", $"http://127.0.0.1:{server.Port}");
                Assert.Equal(
                    System.Net.HttpStatusCode.OK,
                    (await client.SendAsync(post)).StatusCode);

                string after = await client.GetStringAsync(
                    "/data/FinalFantasyX/edits/777.json");
                Assert.DoesNotContain("selected", after, StringComparison.Ordinal);
                Assert.Contains("\"2\"", after, StringComparison.Ordinal);
            }
            finally
            {
                server.Stop();
                try { Directory.Delete(localEdits, recursive: true); } catch { }
            }
        }

        private void AddSameOrigin(HttpRequestMessage request)
        {
            request.Headers.Host = $"127.0.0.1:{TestPort}";
            request.Headers.Add("Origin", $"http://127.0.0.1:{TestPort}");
        }
    }
}
