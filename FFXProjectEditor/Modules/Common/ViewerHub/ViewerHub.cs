using System;
using System.Collections.Generic;
using System.IO;

using FFXProjectEditor.Modules.Common.ViewerShell;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    // ── Release runtime layout ──────────────────────────────────────────────────────
    // Frontend code is immutable and app-relative. The only writable static root is the
    // per-user viewer-data directory; imported game data is exposed only after validation.
    internal static class ViewerHubRuntimeLayout
    {
        private static readonly HashSet<string> BundledViewers = new(StringComparer.OrdinalIgnoreCase)
        {
            "map", "model", "magic", "noclip",
        };

        internal sealed record Route(string Prefix, string Root);

        internal static string? ResolveBundledViewerRoot(string appBaseDirectory, string bundleDirectory)
        {
            if (string.IsNullOrWhiteSpace(appBaseDirectory) ||
                !BundledViewers.Contains(bundleDirectory))
                return null;

            try
            {
                string root = Path.GetFullPath(Path.Combine(
                    appBaseDirectory, "viewers", bundleDirectory.ToLowerInvariant()));
                return File.Exists(Path.Combine(root, "index.html")) ? root : null;
            }
            catch
            {
                return null;
            }
        }

        internal static string ResolveViewerDataRoot(string localAppDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(localAppDataDirectory))
                throw new ArgumentException("LocalAppData is required.", nameof(localAppDataDirectory));

            return Path.GetFullPath(Path.Combine(
                localAppDataDirectory, "FFXProjectEditor", "viewer-data"));
        }

        internal static string ResolveNoclipEditsRoot(string localAppDataDirectory) =>
            NoclipOverlayStore.ResolveEditsRoot(ResolveViewerDataRoot(localAppDataDirectory));

        internal static IReadOnlyList<Route> BuildRoutes(
            string appBaseDirectory,
            string localAppDataDirectory,
            NoclipDataCapability.Report? noclipCapability)
        {
            var routes = new List<Route>();
            AddBundle(routes, appBaseDirectory, "map", "/map");
            AddBundle(routes, appBaseDirectory, "model", "/model");
            AddBundle(routes, appBaseDirectory, "magic", "/magic");

            // Generated glTF/catalog/overlay data has one explicit writable home. Never map the
            // repository, RuntimeTools, work/, the current directory, or a caller-provided path.
            routes.Add(new Route("/viewer-data", ResolveViewerDataRoot(localAppDataDirectory)));

            if (noclipCapability is { Ready: true, FfxDataRoot: not null })
            {
                string? noclipFrontend = ResolveBundledViewerRoot(appBaseDirectory, "noclip");
                string? dataRoot = Directory.GetParent(noclipCapability.FfxDataRoot)?.FullName;
                if (noclipFrontend != null &&
                    dataRoot != null &&
                    Directory.Exists(dataRoot))
                {
                    // The vendored release build uses assetPrefix=/noclip/. No global /static or
                    // generic root fallback is needed, so the whole frontend stays namespaced.
                    routes.Add(new Route("/noclip", noclipFrontend));
                    routes.Add(new Route("/data", Path.GetFullPath(dataRoot)));
                }
            }

            return routes;
        }

        internal static bool TryStartServer(
            IReadOnlyList<Route> routes,
            out StudioWebServer? server)
            => TryStartServer(routes, null, out server);

        internal static bool TryStartServer(
            IReadOnlyList<Route> routes,
            string? editWriteRoot,
            out StudioWebServer? server)
        {
            server = editWriteRoot == null
                ? new StudioWebServer()
                : new StudioWebServer(editWriteRoot);
            try
            {
                foreach (Route route in routes)
                    server.MapPrefix(route.Prefix, route.Root);

                // Port 0 delegates selection to Windows. Consumers must use server.Port.
                if (server.Start(0))
                    return true;
            }
            catch
            {
                // Invalid/missing roots fail without falling back to a source checkout.
            }

            server.Stop();
            server = null;
            return false;
        }

        internal static string BuildViewerUrl(
            ViewerDescriptor descriptor,
            int factualPort,
            string? extraQuery,
            string? routeOverride,
            long cacheBust)
        {
            if (factualPort is <= 0 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(factualPort));

            string path = descriptor.Path ?? "/index.html";
            string url = $"http://{descriptor.Host}:{factualPort}{path}";

            if (!descriptor.RequiresNoclip)
                url += (path.Contains('?') ? "&" : "?") + "t=" + cacheBust;

            string query = extraQuery?.TrimStart('?', '&') ?? string.Empty;
            if (query.Length > 0)
                url += (url.Contains('?') ? "&" : "?") + query;

            url += routeOverride ?? descriptor.Route;
            return url;
        }

        internal static string BundlePrefix(string bundleDirectory) => bundleDirectory.ToLowerInvariant() switch
        {
            "map" => "/map",
            "model" => "/model",
            "magic" => "/magic",
            "noclip" => "/noclip",
            _ => throw new ArgumentOutOfRangeException(nameof(bundleDirectory)),
        };

        private static void AddBundle(
            ICollection<Route> routes,
            string appBaseDirectory,
            string bundleDirectory,
            string prefix)
        {
            string? root = ResolveBundledViewerRoot(appBaseDirectory, bundleDirectory);
            if (root != null)
                routes.Add(new Route(prefix, root));
        }
    }

    // ── Live NoClip route lease ───────────────────────────────────────────────────
    // A failed or changed capability must revoke the listener that owns /noclip, /data, and exact
    // overlay routes before the caller returns. Rebuild happens only after the replacement root has
    // passed the full capability validation.
    internal sealed class ViewerHubNoclipExposureState
    {
        private string? _activeDataRoot;

        internal bool Prepare(
            ref StudioWebServer? server,
            NoclipDataCapability.Report capability,
            out string? dataRoot)
        {
            dataRoot = TryResolveDataRoot(capability);
            if (dataRoot == null)
            {
                Revoke(ref server);
                return false;
            }

            if (_activeDataRoot != null && !PathsEqual(_activeDataRoot, dataRoot))
                Revoke(ref server);
            return true;
        }

        internal void MarkActive(NoclipDataCapability.Report capability)
        {
            _activeDataRoot = TryResolveDataRoot(capability) ??
                throw new InvalidOperationException("Only a validated NoClip capability can own live routes.");
        }

        private void Revoke(ref StudioWebServer? server)
        {
            server?.Stop();
            server = null;
            _activeDataRoot = null;
        }

        /// <summary>Test-only: stops the shared listener and clears the active root so suite
        /// ordering cannot leak a running server into unrelated assertions.</summary>
        internal void RevokeForTests(ref StudioWebServer? server) => Revoke(ref server);

        private static string? TryResolveDataRoot(NoclipDataCapability.Report capability)
        {
            if (!capability.Ready || string.IsNullOrWhiteSpace(capability.FfxDataRoot))
                return null;
            try
            {
                DirectoryInfo? parent = Directory.GetParent(Path.GetFullPath(capability.FfxDataRoot));
                return parent == null ? null : Path.GetFullPath(parent.FullName);
            }
            catch
            {
                return null;
            }
        }

        private static bool PathsEqual(string left, string right) => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// Single in-process entry point for bundled 3D viewers. The service never discovers a source
    /// checkout and never publishes repo/work. Every returned URL uses the listener's factual port.
    /// </summary>
    public static class ViewerHubService
    {
        private static readonly Dictionary<string, ViewerDescriptor> _descs =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BundledViewerDirectories =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "map", "model", "magic", "noclip",
            };
        private static readonly object _sync = new();
        private static readonly ViewerHubNoclipExposureState _noclipExposure = new();
        private static StudioWebServer? _server;

        public static string StatusText { get; private set; } = "hub parado";

        static ViewerHubService()
        {
            RegisterViewer(new ViewerDescriptor(
                Id: "monster-studio",
                Title: "Monster Studio (PS2 3D)",
                Host: "127.0.0.1",
                Route: "#ffx/monster-studio",
                BundleDirectory: "noclip",
                RequiresNoclip: true,
                Path: "/noclip/index.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "magic-studio",
                Title: "Magic Studio (372 magias)",
                Host: "127.0.0.1",
                Route: "#ffx/magic-studio",
                BundleDirectory: "noclip",
                RequiresNoclip: true,
                Path: "/noclip/index.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "magic-viewer",
                Title: "Magic Viewer (DLLs PS3)",
                Host: "127.0.0.1",
                Route: "",
                BundleDirectory: "magic",
                RequiresNoclip: false,
                Path: "/magic/index.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "model-viewer",
                Title: "Model Viewer (HD)",
                Host: "127.0.0.1",
                Route: "",
                BundleDirectory: "model",
                RequiresNoclip: false,
                Path: "/model/index.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "map-scene-editor",
                Title: "Map Scene Editor",
                Host: "127.0.0.1",
                Route: "",
                BundleDirectory: "map",
                RequiresNoclip: false,
                Path: "/map/index.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "model-preview",
                Title: "Model Preview (fusion/T-pose)",
                Host: "127.0.0.1",
                Route: "",
                BundleDirectory: "model",
                RequiresNoclip: false,
                Path: "/model/model-preview.html"));
            RegisterViewer(new ViewerDescriptor(
                Id: "aurora",
                Title: "Aurora (Battle Preview 3D)",
                Host: "127.0.0.1",
                Route: "#ffx/battle-preview",
                BundleDirectory: "noclip",
                RequiresNoclip: true,
                Path: "/noclip/index.html",
                Tools: new[] { "battle", "actors", "override", "scenes", "editor" }));
        }

        public static void RegisterViewer(ViewerDescriptor desc)
        {
            if (!BundledViewerDirectories.Contains(desc.BundleDirectory))
                throw new ArgumentOutOfRangeException(
                    nameof(desc),
                    desc.BundleDirectory,
                    "Viewer bundle directory is not allowlisted.");

            _descs[desc.Id] = desc;
        }

        public static IReadOnlyDictionary<string, ViewerDescriptor> Viewers => _descs;

        public static ViewerDescriptor? Find(string id) =>
            _descs.TryGetValue(id, out ViewerDescriptor? descriptor) ? descriptor : null;

        public static StudioWebServer? Server => _server;

        internal static string ViewerDataRoot => ViewerHubRuntimeLayout.ResolveViewerDataRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        internal static string? ResolveBundledViewerRoot(string bundleDirectory) =>
            ViewerHubRuntimeLayout.ResolveBundledViewerRoot(AppContext.BaseDirectory, bundleDirectory);

        /// <summary>
        /// Starts the loopback server if necessary and builds a URL from its factual dynamic port.
        /// <paramref name="routeOverride"/> supports NoClip scene routes without reconstructing a
        /// loopback URL in UI callers.
        /// </summary>
        /// <param name="forExternalBrowser">True when the caller will open the URL in the system
        /// browser instead of an embedded host (the Linux/macOS path — see ExternalBrowserLauncher
        /// and ViewerShell.NavigateOrOpenExternal). The loopback server is platform-neutral; only
        /// the WebView2 embed is Windows-only. Embed-only callers keep the default so an optional
        /// viewer cannot leave a hidden listener behind a blank surface.</param>
        public static string? BuildUrl(
            string descId,
            string? extraQuery = null,
            string? routeOverride = null,
            bool forExternalBrowser = false)
        {
            if (!_descs.TryGetValue(descId, out ViewerDescriptor? desc))
            {
                StatusText = $"viewer desconhecido: {descId}";
                FFXProjectEditor.Diagnostics.DebugLog.Warn("Hub.BuildUrl", $"viewer desconhecido: {descId}");
                return null;
            }

            // The loopback listener exists only to feed a viewer surface. On a platform without
            // a security-equivalent browser backend, refuse before resolving data or starting the
            // server — unless the caller declared a real external-browser consumer.
            if (!WebView2Host.IsSupported && !forExternalBrowser)
            {
                StatusText = Strings.U_Vh_EmbeddedViewerUnavailableTitle;
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "Hub.BuildUrl",
                    $"Embedded viewer unavailable on this platform (desc={descId}); server not started.");
                return null;
            }

            if (!EnsureServer(desc) || _server == null)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "Hub.BuildUrl", $"EnsureServer falhou ({descId}): {StatusText}");
                return null;
            }

            string url = ViewerHubRuntimeLayout.BuildViewerUrl(
                desc,
                _server.Port,
                extraQuery,
                routeOverride,
                DateTime.UtcNow.Ticks);
            FFXProjectEditor.Diagnostics.DebugLog.Info("Hub.BuildUrl", $"desc={descId} url={url}");
            return url;
        }

        /// <summary>Test-only: stops the shared loopback listener and revokes the NoClip route
        /// lease. Production never calls this — the hub intentionally outlives viewer windows.</summary>
        internal static void StopServerForTests()
        {
            lock (_sync)
                _noclipExposure.RevokeForTests(ref _server);
        }

        // ── Task 7 compatibility contract ────────────────────────────────────────
        // Task 8 no longer builds a junction-backed serve directory, but this pure validation
        // helper remains covered to prove that any legacy or test binding is compared by its
        // final target. It never retargets, repairs, deletes, or writes the selected extraction.
        internal static bool IsNoclipServeDirBoundToRoot(string? serveDir, string noclipRoot)
        {
            if (string.IsNullOrWhiteSpace(serveDir) || string.IsNullOrWhiteSpace(noclipRoot))
                return false;

            try
            {
                string? servedData = ResolveFinalDirectory(Path.Combine(serveDir, "data"));
                string? expectedData = ResolveFinalDirectory(Path.Combine(noclipRoot, "data"));
                if (servedData == null || expectedData == null)
                    return false;

                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return string.Equals(servedData, expectedData, comparison);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string? ResolveFinalDirectory(string path)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            if (!directory.Exists)
                return null;

            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo? target = directory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo targetDirectory || !targetDirectory.Exists)
                    return null;
                directory = targetDirectory;
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.FullName));
        }

        private static bool EnsureServer(ViewerDescriptor desc)
        {
            lock (_sync)
            {
                string? requestedRoot = desc.BundleDirectory.Equals("noclip", StringComparison.OrdinalIgnoreCase)
                    ? ServeDirBuilder.BuildNoclipServeDir()
                    : ResolveBundledViewerRoot(desc.BundleDirectory);
                if (requestedRoot == null)
                {
                    StatusText = Strings.F2_noclip_serve_dir_could_not_be_mounted_di_09e23195;
                    FFXProjectEditor.Diagnostics.DebugLog.Error(
                        "Hub.EnsureServer",
                        $"bundled viewer ausente: viewers/{desc.BundleDirectory}/index.html (desc={desc.Id})");
                    return false;
                }

                NoclipDataCapability.Report? capability = null;
                if (desc.RequiresNoclip)
                {
                    string? configuredRoot = NoclipLocator.Find();
                    capability = NoclipDataCapability.Validate(configuredRoot);
                    if (!capability.Ready && !NoclipDataBootstrap.IsBootstrapRoot(configuredRoot))
                    {
                        // A configured-but-incomplete root must not block a ready managed
                        // bootstrap — the viewer still works while the user's extraction is partial.
                        NoclipDataCapability.Report managed =
                            NoclipDataCapability.Validate(NoclipDataBootstrap.BootstrapRoot);
                        if (managed.Ready)
                            capability = managed;
                    }
                    if (!_noclipExposure.Prepare(ref _server, capability, out _))
                    {
                        // Kick the managed bootstrap in the background: the skeleton seed plus
                        // fetch-through makes the viewer usable on clean machines without
                        // blocking this lock on network I/O. The next BuildUrl re-validates.
                        _ = NoclipDataBootstrap.EnsureDataAsync();
                        StatusText = Strings.U_Vh_NoclipDataPreparing;
                        FFXProjectEditor.Diagnostics.DebugLog.Error(
                            "Hub.EnsureServer",
                            $"NoClip data capability bloqueada ({desc.Id}): {string.Join(",", capability.Issues)} — bootstrap iniciado");
                        return false;
                    }
                }

                if (_server is not { IsRunning: true })
                {
                    IReadOnlyList<ViewerHubRuntimeLayout.Route> routes = ViewerHubRuntimeLayout.BuildRoutes(
                        AppContext.BaseDirectory,
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        capability);
                    string editWriteRoot = NoclipOverlayStore.ResolveEditsRoot(ViewerDataRoot);
                    if (!ViewerHubRuntimeLayout.TryStartServer(
                        routes,
                        editWriteRoot,
                        out _server) || _server == null)
                    {
                        StatusText = Strings.U_Vh_ServerStopped;
                        return false;
                    }
                    EnableFetchThroughIfManaged(_server, capability);
                }
                else
                {
                    // A viewer installed/configured after another viewer started can be added safely;
                    // route registration is synchronized inside StudioWebServer.
                    _server.MapPrefix(
                        ViewerHubRuntimeLayout.BundlePrefix(desc.BundleDirectory),
                        requestedRoot);
                    if (capability is { Ready: true, FfxDataRoot: not null })
                    {
                        string dataRoot = Directory.GetParent(capability.FfxDataRoot)!.FullName;
                        _server.MapPrefix("/data", dataRoot);
                        EnableFetchThroughIfManaged(_server, capability);
                    }
                }

                if (capability is { Ready: true })
                    _noclipExposure.MarkActive(capability);

                StatusText = _server.Status;
                return true;
            }
        }

        // Fetch-through is only ever enabled for the editor-managed bootstrap root. A
        // user-selected extraction stays strictly read-only — missing files there 404.
        private static void EnableFetchThroughIfManaged(
            StudioWebServer server,
            NoclipDataCapability.Report? capability)
        {
            if (capability is not { Ready: true, FfxDataRoot: not null })
                return;

            string? dataRoot = Directory.GetParent(capability.FfxDataRoot)?.FullName;
            if (dataRoot != null && NoclipDataBootstrap.IsBootstrapDataRoot(dataRoot))
                server.EnableNoclipFetchThrough(dataRoot);
        }
    }
}
