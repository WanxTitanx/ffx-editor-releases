using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using FFXProjectEditor.Modules.AuroraFieldExplorer;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    /// <summary>
    /// Release boundary for ViewerHub composition. The listener is allowed to expose only bundled
    /// frontends, generated LocalAppData, and explicitly validated user-owned FFX data.
    /// </summary>
    public sealed class ViewerHubRuntimeLayoutTests : IDisposable
    {
        private readonly string _sandbox = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work",
            "ffx-viewer-layout-" + Guid.NewGuid().ToString("N"));
        private readonly string _appRoot;
        private readonly string _localAppData;
        private readonly string _gameRoot;

        public ViewerHubRuntimeLayoutTests()
        {
            _appRoot = Path.Combine(_sandbox, "app");
            _localAppData = Path.Combine(_sandbox, "local-app-data");
            _gameRoot = Path.Combine(_sandbox, "selected-game-data");

            foreach (string bundle in new[] { "map", "model", "magic", "noclip" })
            {
                string root = Path.Combine(_appRoot, "viewers", bundle);
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "index.html"), $"<html>{bundle}-bundle</html>");
            }

            string noclip = Path.Combine(_appRoot, "viewers", "noclip");
            Directory.CreateDirectory(Path.Combine(noclip, "static", "js"));
            Directory.CreateDirectory(Path.Combine(noclip, "static", "image"));
            File.WriteAllText(Path.Combine(noclip, "index.html"),
                "<link href=\"/noclip/static/image/logo.png\"><script src=\"/noclip/static/js/app.js\"></script>");
            File.WriteAllText(Path.Combine(noclip, "embed.html"),
                "<script src=\"/noclip/static/js/embed.js\"></script>");
            File.WriteAllText(Path.Combine(noclip, "static", "js", "app.js"), "app");
            File.WriteAllText(Path.Combine(noclip, "static", "js", "embed.js"), "embed");
            File.WriteAllBytes(Path.Combine(noclip, "static", "image", "logo.png"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(noclip, "basis_transcoder.wasm"), new byte[] { 0, 97, 115, 109 });

            CreateValidGameData(_gameRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_sandbox, recursive: true); } catch { }
        }

        [Fact]
        public void ResolveBundledViewerRoot_UsesOnlyTheAppRelativeAllowlist()
        {
            string decoy = Path.Combine(_appRoot, "RuntimeTools", "FFXMapViewerWeb");
            Directory.CreateDirectory(decoy);
            File.WriteAllText(Path.Combine(decoy, "index.html"), "repo-decoy");

            string? resolved = ViewerHubRuntimeLayout.ResolveBundledViewerRoot(_appRoot, "map");

            Assert.Equal(Path.GetFullPath(Path.Combine(_appRoot, "viewers", "map")), resolved);
            Assert.Null(ViewerHubRuntimeLayout.ResolveBundledViewerRoot(_appRoot, "work"));
            Assert.Null(ViewerHubRuntimeLayout.ResolveBundledViewerRoot(_appRoot, "../map"));
            Directory.Delete(Path.Combine(_appRoot, "viewers", "map"), recursive: true);
            Assert.Null(ViewerHubRuntimeLayout.ResolveBundledViewerRoot(_appRoot, "map"));
        }

        [Fact]
        public void BuildRoutes_UsesBundledFrontendsLocalAppDataAndValidatedGameDataOnly()
        {
            NoclipDataCapability.Report capability = NoclipDataCapability.Validate(_gameRoot);
            Assert.True(capability.Ready, string.Join(",", capability.Issues));

            ViewerHubRuntimeLayout.Route[] routes = ViewerHubRuntimeLayout
                .BuildRoutes(_appRoot, _localAppData, capability)
                .ToArray();

            Assert.Equal(Path.Combine(_appRoot, "viewers", "noclip"), RouteRoot(routes, "/noclip"));
            Assert.Equal(Path.Combine(_appRoot, "viewers", "map"), RouteRoot(routes, "/map"));
            Assert.Equal(Path.Combine(_appRoot, "viewers", "model"), RouteRoot(routes, "/model"));
            Assert.Equal(Path.Combine(_appRoot, "viewers", "magic"), RouteRoot(routes, "/magic"));
            Assert.Equal(
                Path.Combine(_localAppData, "FFXProjectEditor", "viewer-data"),
                RouteRoot(routes, "/viewer-data"));
            Assert.Equal(Path.Combine(_gameRoot, "data"), RouteRoot(routes, "/data"));
            Assert.DoesNotContain(routes, route =>
                route.Prefix.Equals("/data/FinalFantasyX/edits", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(routes, route =>
                route.Prefix.Equals("/work", StringComparison.OrdinalIgnoreCase) ||
                route.Root.Contains("RuntimeTools", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void BuildRoutes_OmitsNoclipAndDataUntilCapabilityIsReady()
        {
            var blocked = new NoclipDataCapability.Report(
                Ready: false,
                FfxDataRoot: Path.Combine(_gameRoot, "data", "FinalFantasyX"),
                Issues: new[] { "CRITICAL_FILE_INVALID:common_textures.bin" });

            ViewerHubRuntimeLayout.Route[] routes = ViewerHubRuntimeLayout
                .BuildRoutes(_appRoot, _localAppData, blocked)
                .ToArray();

            Assert.DoesNotContain(routes, route =>
                route.Prefix == "/noclip" ||
                route.Prefix.StartsWith("/data", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(routes, route => route.Prefix == "/map");
            Assert.Contains(routes, route => route.Prefix == "/viewer-data");
        }

        [Fact]
        public async Task StartServer_UsesAnEphemeralFactualPortAndExposesOnlyPlannedRoots()
        {
            string viewerData = ViewerHubRuntimeLayout.ResolveViewerDataRoot(_localAppData);
            Directory.CreateDirectory(viewerData);
            File.WriteAllText(Path.Combine(viewerData, "probe.json"), "{\"viewerData\":true}");
            NoclipDataCapability.Report capability = NoclipDataCapability.Validate(_gameRoot);
            var routes = ViewerHubRuntimeLayout.BuildRoutes(_appRoot, _localAppData, capability);
            string editWriteRoot = ViewerHubRuntimeLayout.ResolveNoclipEditsRoot(_localAppData);

            Assert.True(ViewerHubRuntimeLayout.TryStartServer(
                routes,
                editWriteRoot,
                out StudioWebServer? server));
            Assert.NotNull(server);
            try
            {
                Assert.InRange(server!.Port, 1, 65535);
                using var client = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
                };

                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/map/index.html")).StatusCode);
                using HttpResponseMessage noclipIndex = await client.GetAsync("/noclip/index.html");
                Assert.Equal(HttpStatusCode.OK, noclipIndex.StatusCode);
                string index = await noclipIndex.Content.ReadAsStringAsync();
                foreach (Match reference in Regex.Matches(index, "(?:src|href)=\\\"(?<url>/[^\\\"]+)\\\""))
                {
                    string localUrl = reference.Groups["url"].Value;
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(localUrl)).StatusCode);
                }
                Assert.Equal(HttpStatusCode.OK,
                    (await client.GetAsync("/noclip/basis_transcoder.wasm")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/noclip/embed.html")).StatusCode);
                Assert.Equal(HttpStatusCode.OK,
                    (await client.GetAsync("/data/FinalFantasyX/common_textures.bin")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/viewer-data/probe.json")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.html")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/static/js/app.js")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/work/secret.txt")).StatusCode);

                using var post = new HttpRequestMessage(HttpMethod.Post, "/api/edits/239")
                {
                    Content = new StringContent(
                        "{\"slot\":2,\"position\":[1,2,3]}",
                        Encoding.UTF8,
                        "application/json")
                };
                post.Headers.Add("Origin", $"http://127.0.0.1:{server.Port}");
                using HttpResponseMessage postResponse = await client.SendAsync(post);

                Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
                Assert.True(File.Exists(Path.Combine(editWriteRoot, "239.json")));
                Assert.False(File.Exists(Path.Combine(
                    _gameRoot,
                    "data",
                    "FinalFantasyX",
                    "edits",
                    "239.json")));
                Assert.Equal(HttpStatusCode.OK,
                    (await client.GetAsync("/data/FinalFantasyX/edits/239.json")).StatusCode);
            }
            finally
            {
                server?.Stop();
            }
        }

        [Fact]
        public async Task LiveCapabilityRevocation_StopsTheServerBeforeReturningFailure()
        {
            NoclipDataCapability.Report ready = NoclipDataCapability.Validate(_gameRoot);
            Assert.True(ready.Ready, string.Join(",", ready.Issues));
            var exposure = new ViewerHubNoclipExposureState();
            StudioWebServer? server = new StudioWebServer()
                .MapPrefix("/noclip", Path.Combine(_appRoot, "viewers", "noclip"))
                .MapPrefix("/data", Path.Combine(_gameRoot, "data"));
            Assert.True(server.Start(0), server.Status);
            int stalePort = server.Port;
            exposure.MarkActive(ready);

            var invalid = new NoclipDataCapability.Report(
                false,
                ready.FfxDataRoot,
                new[] { "CRITICAL_FILE_INVALID:common_textures.bin" });

            Assert.False(exposure.Prepare(ref server, invalid, out _));
            Assert.Null(server);
            Assert.False(await CanConnectAsync(stalePort));
        }

        [Fact]
        public async Task LiveCapabilityRootChange_StopsAAndExposesBOnlyAfterValidationAndRebuild()
        {
            string gameB = Path.Combine(_sandbox, "selected-game-data-b");
            CreateValidGameData(gameB);
            File.WriteAllBytes(
                Path.Combine(_gameRoot, "data", "FinalFantasyX", "0e", "0000.bin"),
                new byte[] { 0xA1 });
            File.WriteAllBytes(
                Path.Combine(gameB, "data", "FinalFantasyX", "0e", "0000.bin"),
                new byte[] { 0xB2 });
            NoclipDataCapability.Report readyA = NoclipDataCapability.Validate(_gameRoot);
            NoclipDataCapability.Report readyB = NoclipDataCapability.Validate(gameB);
            Assert.True(readyA.Ready, string.Join(",", readyA.Issues));
            Assert.True(readyB.Ready, string.Join(",", readyB.Issues));

            var exposure = new ViewerHubNoclipExposureState();
            StudioWebServer? server = new StudioWebServer().MapPrefix("/data", Path.Combine(_gameRoot, "data"));
            Assert.True(server.Start(0), server.Status);
            int stalePort = server.Port;
            exposure.MarkActive(readyA);
            using (var clientA = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{stalePort}") })
                Assert.Equal(new byte[] { 0xA1 }, await clientA.GetByteArrayAsync(
                    "/data/FinalFantasyX/0e/0000.bin"));

            Assert.True(exposure.Prepare(ref server, readyB, out string? dataRootB));
            Assert.Null(server);
            Assert.Equal(Path.Combine(gameB, "data"), dataRootB);
            Assert.False(await CanConnectAsync(stalePort));

            server = new StudioWebServer().MapPrefix("/data", dataRootB!);
            Assert.True(server.Start(0), server.Status);
            exposure.MarkActive(readyB);
            try
            {
                using var clientB = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
                };
                Assert.Equal(new byte[] { 0xB2 }, await clientB.GetByteArrayAsync(
                    "/data/FinalFantasyX/0e/0000.bin"));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task VendoredNoclipIndex_AllLocalSrcAndHrefTargetsAreServedUnderNoclip()
        {
            string dist = FindVendoredNoclipDist();
            var server = new StudioWebServer().MapPrefix("/noclip", dist);
            Assert.True(server.Start(0), server.Status);
            try
            {
                using var client = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
                };
                using HttpResponseMessage response = await client.GetAsync("/noclip/index.html");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string index = await response.Content.ReadAsStringAsync();

                foreach (Match reference in Regex.Matches(index, "(?:src|href)=\\\"(?<url>[^\\\"]+)\\\""))
                {
                    string target = reference.Groups["url"].Value;
                    if (target.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Assert.True(IsLocalBrowserReference(target), target);
                    string requestPath = target.StartsWith('/') ? target : "/noclip/" + target;
                    Assert.StartsWith("/noclip/", requestPath, StringComparison.Ordinal);
                    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(requestPath)).StatusCode);
                }

                Assert.Equal(HttpStatusCode.OK,
                    (await client.GetAsync("/noclip/basis_transcoder.wasm")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/noclip/embed.html")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/static/js/index.js")).StatusCode);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void BuildViewerUrl_UsesTheListenerPortAndSupportsARouteOverride()
        {
            ViewerDescriptor descriptor = ViewerHubService.Find("aurora")!;

            string url = ViewerHubRuntimeLayout.BuildViewerUrl(
                descriptor,
                factualPort: 49123,
                extraQuery: "battle=239&map=4",
                routeOverride: "#ffx/123",
                cacheBust: 99);

            Assert.Equal("http://127.0.0.1:49123/noclip/index.html?battle=239&map=4#ffx/123", url);
            Assert.DoesNotContain(StudioWebServer.DefaultPort.ToString(), url, StringComparison.Ordinal);

            string mapUrl = ViewerHubRuntimeLayout.BuildViewerUrl(
                ViewerHubService.Find("map-scene-editor")!,
                factualPort: 49124,
                extraQuery: "map=btlmap%2Ftest",
                routeOverride: null,
                cacheBust: 99);
            Assert.Equal(
                "http://127.0.0.1:49124/map/index.html?t=99&map=btlmap%2Ftest",
                mapUrl);
        }

        [Theory]
        [InlineData("/noclip/static/image/logo.png", true)]
        [InlineData("static/js/index.js", true)]
        [InlineData("./static/js/index.js", true)]
        [InlineData("https://example.invalid/a.js", false)]
        [InlineData("http://example.invalid/a.js", false)]
        [InlineData("//example.invalid/a.js", false)]
        [InlineData("file:///tmp/a.js", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("\\\\example.invalid/a.js", false)]
        [InlineData("/\\example.invalid/a.js", false)]
        [InlineData("", false)]
        [InlineData("\0", false)]
        [InlineData("java\nscript:alert(1)", false)]
        [InlineData("java\tscript:alert(1)", false)]
        [InlineData("ht\ntps://example.invalid/a", false)]
        [InlineData("/\r/example.invalid/a", false)]
        [InlineData(" \t//example.invalid/a", false)]
        [InlineData("\u0001https://example.invalid/a", false)]
        [InlineData("/noclip/static/a\u007f.js", false)]
        public void FrontendReference_UsesBrowserRelativeSemanticsAcrossHosts(string reference, bool local)
        {
            Assert.Equal(local, IsLocalBrowserReference(reference));
        }

        // HTML root-relative references belong to the current web origin; System.Uri instead
        // recognizes Unix absolute file paths on Linux. Keep authority/scheme refusal explicit,
        // and retain the caller's /noclip/ containment and real HTTP response assertions.
        private static bool IsLocalBrowserReference(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return false;
            // Browsers remove embedded TAB/CR/LF and trim leading C0/space before parsing.
            // Reject these raw spellings instead of validating a different URL than the browser.
            foreach (char character in reference)
                if (character <= ' ' || character == '\u007f' || character == '\\') return false;
            return !reference.StartsWith("//", StringComparison.Ordinal) &&
                (reference.StartsWith('/') || !Uri.TryCreate(reference, UriKind.Absolute, out _));
        }

        [Fact]
        public void ChrModelUrls_UseViewerDataInsteadOfTheRepositoryWorkTree()
        {
            Assert.Equal(
                "/viewer-data/model/phyre_chr_anim/models/m021/m021_static.gltf",
                AuroraFieldExplorer_ChrModelResolver.TryResolveModelUrl("m021"));
            Assert.DoesNotContain(
                "/work/",
                AuroraFieldExplorer_ChrModelResolver.TryResolveModelUrl("m021")!,
                StringComparison.OrdinalIgnoreCase);

            string overlay = File.ReadAllText(FindRepoFile(
                "RuntimeTools", "FFXMapViewerWeb", "field-explorer-overlay.js"));
            Assert.DoesNotContain("/work/", overlay, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/viewer-data/model/", overlay, StringComparison.Ordinal);
        }

        [Fact]
        public void AuroraBridge_DerivesItsLoopbackOriginFromThePinnedViewerOrigin()
        {
            string overlay = File.ReadAllText(FindRepoFile(
                "RuntimeTools", "FFXMapViewerWeb", "aurora-overlay.js"));

            Assert.DoesNotContain("http://127.0.0.1:${dragPort}", overlay, StringComparison.Ordinal);
            Assert.Contains("new URL(path, window.location.origin)", overlay, StringComparison.Ordinal);
        }

        [Fact]
        public void ProductCatalog_DescribesTheBundledEphemeralViewerArchitecture()
        {
            string catalog = File.ReadAllText(FindRepoFile(
                "FFXProjectEditor", "Modules", "Main", "ModuleCatalogPolicy.cs"));

            Assert.DoesNotContain("RuntimeTools/FFXMapViewerWeb", catalog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1:8769", catalog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("data/ junction", catalog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Requires the noclip.website checkout", catalog, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OS-assigned loopback port", catalog, StringComparison.Ordinal);
            Assert.Contains("validated user-selected local FFX data", catalog, StringComparison.Ordinal);
        }

        [Fact]
        public void ProductViewerMessages_DoNotAskForADeveloperCheckoutOrServeDirectory()
        {
            string resources = Path.Combine(FindRepoRoot(), "FFXProjectEditor", "Resources");
            foreach (string resx in Directory.GetFiles(resources, "Strings*.resx"))
            {
                string content = File.ReadAllText(resx);
                Assert.DoesNotContain("NOCLIP_ROOT", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Downloads/Compressed", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("serve-dir could not be mounted", content, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ProductBuild_DefaultsToShippingAllBundledViewerFrontends()
        {
            XDocument project = XDocument.Load(FindRepoFile(
                "FFXProjectEditor", "FFXProjectEditor.csproj"));
            XElement? defaultSetting = project
                .Descendants()
                .SingleOrDefault(element =>
                    element.Name.LocalName == "FFXShipProductViewers" &&
                    string.Equals(
                        (string?)element.Attribute("Condition"),
                        "'$(FFXShipProductViewers)' == ''",
                        StringComparison.Ordinal));

            Assert.NotNull(defaultSetting);
            Assert.Equal("true", defaultSetting!.Value.Trim(), ignoreCase: true);
        }

        [Fact]
        public void NoclipViewerShell_OffersAnInProductDataFolderPicker()
        {
            string xamlPath = FindRepoFile(
                "FFXProjectEditor", "Modules", "Common", "ViewerShell", "ViewerShell_Control.axaml");
            string xaml = File.ReadAllText(xamlPath);
            string codeBehind = File.ReadAllText(FindRepoFile(
                "FFXProjectEditor", "Modules", "Common", "ViewerShell", "ViewerShell_Control.axaml.cs"));

            Assert.Contains("Click=\"Button_ConfigureNoclipData\"", xaml, StringComparison.Ordinal);
            Assert.Contains("IsVisible=\"{Binding CanConfigureNoclip}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("NoclipLocator.TryConfigureRoot", codeBehind, StringComparison.Ordinal);
            Assert.Contains("AvaloniaDialog_Util.OpenFolderDialog", codeBehind, StringComparison.Ordinal);

            XElement errorBanner = Assert.Single(XDocument.Load(xamlPath)
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "Border" &&
                    string.Equals((string?)element.Attribute("Classes"), "pillDanger", StringComparison.Ordinal)));
            Assert.Equal("1", (string?)errorBanner.Attribute("Grid.Row"));
            Assert.Equal("3", (string?)errorBanner.Attribute("Grid.ColumnSpan"));

            XElement statusText = Assert.Single(errorBanner.Descendants().Where(element =>
                element.Name.LocalName == "TextBlock" &&
                string.Equals((string?)element.Attribute("Text"), "{Binding StatusText}", StringComparison.Ordinal)));
            Assert.Equal("Wrap", (string?)statusText.Attribute("TextWrapping"));
        }

        [Fact]
        public void WebViewReleasePath_DoesNotWriteAnUnboundedTempDiagnosticLog()
        {
            string webViewHost = File.ReadAllText(FindRepoFile(
                "FFXProjectEditor", "Modules", "Common", "ViewerShell", "WebView2Host.cs"));

            Assert.DoesNotContain("ffx-studio-diag.log", webViewHost, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("File.AppendAllText", webViewHost, StringComparison.Ordinal);
            Assert.Contains("DebugLog.Info(\"WebView.Navigation\"", webViewHost, StringComparison.Ordinal);
        }

        private static string RouteRoot(ViewerHubRuntimeLayout.Route[] routes, string prefix) =>
            Assert.Single(routes, route => route.Prefix == prefix).Root;

        private static string FindVendoredNoclipDist()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "ExternalLibs",
                    "NoclipViewer",
                    "dist-ffxstudio");
                if (File.Exists(Path.Combine(candidate, "index.html")))
                    return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("ExternalLibs/NoclipViewer/dist-ffxstudio was not found.");
        }

        private static string FindRepoFile(params string[] segments)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }

            throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "FFXProjectEditor", "Resources")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root was not found.");
        }

        private static void CreateValidGameData(string root)
        {
            string data = Path.Combine(root, "data", "FinalFantasyX");
            foreach (string directory in NoclipDataCapability.RequiredDirectoryNames)
            {
                string path = Path.Combine(data, directory);
                Directory.CreateDirectory(path);
                File.WriteAllBytes(Path.Combine(path, "0000.bin"), new byte[] { 0x01 });
            }

            WriteCritical(Path.Combine(data, "common_textures.bin"), 0x3C);
            WriteCritical(Path.Combine(data, "screen_shatter.bin"), 0x3C);
            WriteCritical(Path.Combine(data, "env_map_texture.bin"), 0x18);
        }

        private static void WriteCritical(string path, int offsetField)
        {
            byte[] bytes = new byte[128];
            BitConverter.GetBytes(96u).CopyTo(bytes, offsetField);
            File.WriteAllBytes(path, bytes);
        }

        private static async Task<bool> CanConnectAsync(int port)
        {
            using var client = new System.Net.Sockets.TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1));
                return client.Connected;
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException)
            {
                return false;
            }
        }
    }
}
