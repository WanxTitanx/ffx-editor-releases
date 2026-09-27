using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using System;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    // ── Embedded-viewer URI boundary ────────────────────────────────────────────────
    // Web content is untrusted even when it is served from loopback. Keep origin parsing
    // independent from WebView2 event objects so every spoofing edge case is unit-testable.
    // MAINT: never replace these component comparisons with textual prefix matching.

    /// <summary>
    /// Hosts a native Edge WebView2 (Evergreen runtime) INSIDE the Avalonia window via
    /// <see cref="NativeControlHost"/>. We create a child Win32 host window parented to the
    /// Avalonia top-level HWND, then parent a <see cref="CoreWebView2Controller"/> to that
    /// child window. The controller's bounds are synced to the control size so the page fills
    /// the panel. This implementation is selected only for the Windows target graph; Linux uses a
    /// separate fail-closed facade that never constructs a browser or native child surface.
    /// </summary>
    public sealed class WebView2Host : NativeControlHost
    {
        private const string HostWindowClassName = "FFXWebView2HostWindow";
        private const int GWLP_WNDPROC = -4;

        // 🐉 DEBUG LOG (v2.224.1.13): console bridge JS — redireciona console.error/warn/log do page
        // para window.chrome.webview.postMessage (capturado no OnWebMessageReceived -> DebugLog).
        private const string ConsoleBridgeScript =
            "window.__ffxConsoleBridge=(function(){if(window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage){" +
            "var o={log:console.log,warn:console.warn,error:console.error};" +
            "var s=function(lvl,args){try{var t=Array.prototype.slice.call(args).map(function(a){try{return typeof a==='string'?a:JSON.stringify(a);}catch(e){return String(a);}}).join(' ');" +
            "window.chrome.webview.postMessage({ffx:'console',level:lvl,text:t});}catch(e){}};" +
            "console.log=function(){s('log',arguments);o.log.apply(console,arguments);};" +
            "console.warn=function(){s('warn',arguments);o.warn.apply(console,arguments);};" +
            "console.error=function(){s('error',arguments);o.error.apply(console,arguments);};}})();";

        // Win32 window style constants.
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint WS_CLIPSIBLINGS = 0x04000000;

        private static readonly WndProcDelegate _wndProc = DefaultWndProc;
        private static ushort _classAtom;
        private static readonly object _classGate = new();

        private readonly WebView2InitializationFailurePublisher _initializationFailures =
            new(AppContext.BaseDirectory);

        private IntPtr _hostHwnd;
        private CoreWebView2Controller? _controller;
        private CoreWebView2? _coreWebView2;
        private string? _pendingNavigateUrl;
        private string? _pendingInjectScript;
        private string? _pendingSkinScript;
        private string? _lastAllowedNavigationUrl;
        private Uri? _pinnedOrigin;
        private bool _initializationStarted;
        private bool _sourceRecoveryInProgress;
        private bool _disposed;

        public static bool IsSupported => true;

        /// <summary>
        /// Raised when the native WebView2 environment or controller cannot be created. The
        /// exception and packaged offline-installer location are also retained in
        /// <see cref="InitializationFailure"/> so the shell can render deterministic recovery UI.
        /// </summary>
        public event EventHandler<WebView2InitializationFailedEventArgs>? InitializationFailed
        {
            add => _initializationFailures.Failed += value;
            remove => _initializationFailures.Failed -= value;
        }

        public WebView2InitializationFailedEventArgs? InitializationFailure =>
            _initializationFailures.Current;

        /// <summary>
        /// Direct viewer consumers get a recovery dialog by default. ViewerShell disables this
        /// because it already renders the same recovery as an inline panel with a retry action.
        /// </summary>
        public bool ShowBuiltInRecoveryDialog { get; set; } = true;

        /// <summary>Exact trusted origin (scheme + normalized host + port). Only HTTP loopback IPs,
        /// localhost and proper *.localhost names are accepted; invalid configuration resets the
        /// host to default-deny.</summary>
        public string AllowedOrigin
        {
            get => _pinnedOrigin?.GetLeftPart(UriPartial.Authority) ?? string.Empty;
            set
            {
                Uri? requestedOrigin = WebView2NavigationPolicy.TryPinOrigin(value);
                if (requestedOrigin != null &&
                    _pinnedOrigin != null &&
                    !WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, requestedOrigin.AbsoluteUri))
                    return;

                _pinnedOrigin = requestedOrigin;
                _lastAllowedNavigationUrl = null;
                if (_pinnedOrigin == null && _coreWebView2 != null)
                {
                    try { _coreWebView2.Stop(); } catch { }
                    if (_controller != null) _controller.IsVisible = false;
                }
            }
        }

        /// <summary>Hash desejado do app (ex.: "#ffx/monster-studio"). Reaplicado após cada navegação
        /// completar — se o app cair na home/menu (hash perdido), o shell o força de volta (o noclip
        /// trata hashchange).</summary>
        private string? _desiredHash;

        public WebView2Host()
        {
            // Re-sync the native surface whenever our viewport changes (window resize, splitter drag, scroll).
            // Deferred to Background so it runs AFTER Avalonia has repositioned/resized the native host window.
            EffectiveViewportChanged += (_, _) => Dispatcher.UIThread.Post(SyncControllerBounds, DispatcherPriority.Background);
        }

        /// <summary>
        /// Injects a script into every document created by this WebView2 (runs before page scripts).
        /// Used by the ViewerShell to apply the FFX Mod Studio "skin" over host web apps (e.g. hide
        /// the noclip logo/About/bottom bar while keeping the functional panels + 3D canvas).
        /// Safe to call before the WebView2 environment is ready; the script is applied once ready.
        /// </summary>
        public void InjectScriptOnDocumentCreated(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return;

            // Fallback GARANTIDO: guarda para aplicar via ExecuteScriptAsync no NavigationCompleted
            // (o documento já existe nesse momento — não depende do AddScriptToExecuteOnDocumentCreatedAsync,
            // que falhou silenciosamente por timing na prática).
            _pendingSkinScript = script;

            if (_coreWebView2 != null)
            {
                try { _ = _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script); }
                catch { /* swallow; injection failures must not crash the editor */ }
            }
            else
            {
                _pendingInjectScript = script;
            }
        }

        /// <summary>
        /// Navigates with the domain guard + hash enforcement: <paramref name="hashRoute"/> (ex.:
        /// "#ffx/monster-studio") is re-applied after every completed navigation, and navigations
        /// leaving the server origin are cancelled. This keeps the viewer pinned to its scene even
        /// if the hosted app falls back to its home/menu screen.
        /// </summary>
        public void NavigatePinned(string url, string hashRoute)
        {
            Uri? requestedOrigin = WebView2NavigationPolicy.TryPinOrigin(url);
            if (requestedOrigin == null)
            {
                _pinnedOrigin = null;
                _lastAllowedNavigationUrl = null;
                _desiredHash = null;
                try { _coreWebView2?.Stop(); } catch { }
                if (_controller != null) _controller.IsVisible = false;
                return;
            }

            // A host instance never crosses origins after it has been pinned. Reloads and route
            // changes on the same origin remain supported.
            if (_pinnedOrigin != null &&
                !WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, requestedOrigin.AbsoluteUri))
                return;

            _pinnedOrigin = requestedOrigin;
            _desiredHash = hashRoute;
            Navigate(url);
        }

        /// <summary>
        /// Navigates the embedded browser to <paramref name="url"/>. Safe to call before the
        /// WebView2 environment has finished initializing; the navigation is deferred.
        /// </summary>
        public void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            // Some existing preview call sites use about:blank as a native "clear" command. Keep
            // that UX without granting about: navigation to the web content or origin policy.
            if (url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                _pendingNavigateUrl = null;
                try { _coreWebView2?.Stop(); } catch { }
                if (_controller != null) _controller.IsVisible = false;
                return;
            }

            _pinnedOrigin ??= WebView2NavigationPolicy.TryPinOrigin(url);
            if (!WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, url))
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "WebView.Navigation",
                    "Blocked navigation outside the pinned viewer origin.");
                return;
            }

            _lastAllowedNavigationUrl = url;
            if (_coreWebView2 != null)
            {
                try
                {
                    if (_controller != null) _controller.IsVisible = true;
                    _coreWebView2.Navigate(url);
                }
                catch { /* swallow; runtime navigation failures must not crash the editor */ }
            }
            else
            {
                _pendingNavigateUrl = url;
                BeginInitializationIfReady();
            }
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (!OperatingSystem.IsWindows())
                return base.CreateNativeControlCore(parent);

            EnsureWindowClass();

            _hostHwnd = CreateWindowEx(
                0,
                new IntPtr(_classAtom),
                string.Empty,
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 1, 1,
                parent.Handle,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (_hostHwnd == IntPtr.Zero)
                return base.CreateNativeControlCore(parent);

            // Controller creation is deferred until the first trusted Navigate call. Besides
            // enforcing default-deny, this lets the initial viewer route select a stable isolated
            // WebView2 profile before the controller exists.
            BeginInitializationIfReady();

            return new PlatformHandle(_hostHwnd, "HWND");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _disposed = true;
            try
            {
                _controller?.Close();
            }
            catch
            {
                // ignore teardown failures
            }
            finally
            {
                _controller = null;
                _coreWebView2 = null;
                _initializationStarted = false;
            }

            if (OperatingSystem.IsWindows() && _hostHwnd != IntPtr.Zero)
            {
                DestroyWindow(_hostHwnd);
                _hostHwnd = IntPtr.Zero;
                return;
            }

            base.DestroyNativeControlCore(control);
        }

        private void BeginInitializationIfReady()
        {
            if (_disposed ||
                _initializationStarted ||
                !OperatingSystem.IsWindows() ||
                _hostHwnd == IntPtr.Zero ||
                _pinnedOrigin == null ||
                string.IsNullOrWhiteSpace(_pendingNavigateUrl))
                return;

            _initializationFailures.Clear();
            _initializationStarted = true;
            _ = InitializeWebView2Async();
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                // A writable user-data folder is required by WebView2; default to %LOCALAPPDATA%.
                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FFXProjectEditor",
                    "WebView2");
                System.IO.Directory.CreateDirectory(userDataFolder);

                CoreWebView2Environment environment = await CoreWebView2Environment
                    .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder)
                    .ConfigureAwait(true);

                if (_disposed || _hostHwnd == IntPtr.Zero)
                    return;

                CoreWebView2Controller controller;
                try
                {
                    // Multiple profiles share the application UDF plumbing but isolate cookies,
                    // localStorage, CacheStorage, permissions and service workers per viewer route.
                    // Query parameters are intentionally excluded from the profile fingerprint so
                    // battle IDs/cache-busters do not create unbounded profile directories.
                    CoreWebView2ControllerOptions options = environment.CreateCoreWebView2ControllerOptions();
                    options.ProfileName = WebView2NavigationPolicy.CreateProfileName(
                        _pendingNavigateUrl!,
                        _desiredHash);
                    options.IsInPrivateModeEnabled = false;
                    controller = await environment
                        .CreateCoreWebView2ControllerAsync(_hostHwnd, options)
                        .ConfigureAwait(true);
                }
                catch (Exception ex) when (
                    ex is NotImplementedException ||
                    ex is COMException { HResult: unchecked((int)0x80004001) } || // E_NOTIMPL
                    ex is COMException { HResult: unchecked((int)0x80004002) })   // E_NOINTERFACE
                {
                    // Older Evergreen runtimes may not expose controller options. Retain viewer
                    // availability while the URI/event boundary still applies; capability UI can
                    // surface the runtime upgrade independently.
                    controller = await environment
                        .CreateCoreWebView2ControllerAsync(_hostHwnd)
                        .ConfigureAwait(true);
                }

                if (_disposed || _hostHwnd == IntPtr.Zero)
                {
                    try { controller.Close(); } catch { }
                    return;
                }

                _controller = controller;
                _coreWebView2 = controller.CoreWebView2;

                // ── Capability hardening ───────────────────────────────────────────
                // The viewers need web messages only for the diagnostic console bridge. All
                // browser capabilities unrelated to rendering are disabled where this SDK/runtime
                // exposes them. Keep each optional setter isolated for older Evergreen runtimes.
                try { _coreWebView2.Settings.IsWebMessageEnabled = true; } catch { }
                try { _coreWebView2.Settings.AreHostObjectsAllowed = false; } catch { }
                try { _coreWebView2.Settings.AreDefaultContextMenusEnabled = false; } catch { }
                try { _coreWebView2.Settings.IsGeneralAutofillEnabled = false; } catch { }
                try { _coreWebView2.Settings.IsPasswordAutosaveEnabled = false; } catch { }
#if !DEBUG
                try { _coreWebView2.Settings.AreDevToolsEnabled = false; } catch { }
#endif

                // ── Navigation and browser-event boundary ──────────────────────────
                _coreWebView2.NavigationStarting += (_, e) =>
                {
                    if (WebView2NavigationPolicy.ShouldCancelTopLevelNavigation(_pinnedOrigin, e.Uri))
                    {
                        e.Cancel = true;
                        return;
                    }

                    _lastAllowedNavigationUrl = e.Uri;
                };
                _coreWebView2.FrameNavigationStarting += (_, e) =>
                {
                    if (WebView2NavigationPolicy.ShouldCancelFrameNavigation(_pinnedOrigin, e.Uri))
                        e.Cancel = true;
                };
                _coreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                _coreWebView2.PermissionRequested += (_, e) =>
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    e.SavesInProfile = false;
                    e.Handled = true;
                };
                _coreWebView2.DownloadStarting += (_, e) =>
                {
                    e.Cancel = WebView2NavigationPolicy.ShouldCancelDownload();
                    e.Handled = true;
                };
                _coreWebView2.ServerCertificateErrorDetected += (_, e) =>
                    e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
                try
                {
                    _coreWebView2.AddWebResourceRequestedFilter(
                        "*",
                        CoreWebView2WebResourceContext.All);
                    _coreWebView2.WebResourceRequested += OnWebResourceRequested;
                }
                catch
                {
                    // The Studio server also sends a deny-by-default CSP. Older Evergreen
                    // runtimes that cannot install this event filter therefore remain offline.
                }
                _coreWebView2.SourceChanged += (_, _) => EnforceCurrentSource();

                // 🐉 DEBUG LOG (v2.224.1.13): WebView console bridge — captura console.error/warn do JS
                // hospedado (noclip/MapViewer) e roteia para o DebugLog (os bugs do viewer são JS).
                _coreWebView2.WebMessageReceived += OnWebMessageReceived;
                try { _ = _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ConsoleBridgeScript); } catch { }
                try { _ = _coreWebView2.ExecuteScriptAsync(ConsoleBridgeScript); } catch { }

                // Ensure-hash: se o app cair na home/menu (hash vazio/perdido), força o hash do desc
                // de volta — o noclip trata hashchange e navega para a cena.
                _coreWebView2.NavigationCompleted += async (_, _) =>
                {
                    // SKIN (fallback garantido): o documento já existe aqui — aplica o script da skin
                    // via ExecuteScriptAsync (cobre o caso em que o AddScriptToExecuteOnDocumentCreatedAsync
                    // não foi aplicado ao documento atual).
                    if (!string.IsNullOrEmpty(_pendingSkinScript))
                    {
                        try { await _coreWebView2.ExecuteScriptAsync(_pendingSkinScript); } catch { }
                    }

                    if (!string.IsNullOrEmpty(_desiredHash))
                    {
                        try
                        {
                            string cur = await _coreWebView2.ExecuteScriptAsync("window.location.hash");
                            string wanted = _desiredHash;
                            if (cur.Trim('"') != wanted)
                            {
                                string hashNoPound = wanted.StartsWith('#') ? wanted.Substring(1) : wanted;
                                await _coreWebView2.ExecuteScriptAsync($"window.location.hash = '{hashNoPound}';");
                            }
                        }
                        catch { /* best-effort */ }
                    }

#if DEBUG
                    // Debug-only DOM probe. DebugLog is itself conditional and owns bounded local
                    // diagnostics; Release builds perform neither this JS query nor a temp-file write.
                    try
                    {
                        string probe = await _coreWebView2.ExecuteScriptAsync(
                            "JSON.stringify({skin: !!document.getElementById('ffx-studio-skin'), hash: window.location.hash, title: document.title, about: !!document.querySelector('div[data-title=\"About\"]'), games: !!document.querySelector('div[data-title=\"Games\"]')})");
                        FFXProjectEditor.Diagnostics.DebugLog.Info("WebView.Navigation", probe);
                    }
                    catch { /* diagnóstico best-effort */ }
#endif
                };

                if (!string.IsNullOrWhiteSpace(_pendingInjectScript))
                {
                    string script = _pendingInjectScript!;
                    _pendingInjectScript = null;
                    try { await _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script); } catch { }
                }

                SyncControllerBounds();
                controller.IsVisible = true;

                if (!string.IsNullOrWhiteSpace(_pendingNavigateUrl))
                {
                    string url = _pendingNavigateUrl!;
                    _pendingNavigateUrl = null;
                    Navigate(url);
                }
            }
            catch (Exception ex)
            {
                _initializationStarted = false;
                try { _controller?.Close(); } catch { }
                _controller = null;
                _coreWebView2 = null;
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "WebView.Initialization",
                    "WebView2 initialization failed; the viewer remains available for an explicit retry.",
                    ex);
                WebView2InitializationFailedEventArgs failure = _initializationFailures.Publish(ex);
                if (ShowBuiltInRecoveryDialog)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _ = WebView2RecoveryDialog.ShowAsync(this, failure);
                    });
                }
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size result = base.ArrangeOverride(finalSize);
            // Defer: Avalonia repositions/sizes the native host window during/after arrange, so read it next tick.
            Dispatcher.UIThread.Post(SyncControllerBounds, DispatcherPriority.Background);
            return result;
        }

        private void EnforceCurrentSource()
        {
            if (_coreWebView2 == null || _sourceRecoveryInProgress)
                return;

            string source = _coreWebView2.Source;
            if (WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, source))
            {
                _lastAllowedNavigationUrl = source;
                return;
            }

            _sourceRecoveryInProgress = true;
            try
            {
                _coreWebView2.Stop();
                if (WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, _lastAllowedNavigationUrl))
                    _coreWebView2.Navigate(_lastAllowedNavigationUrl!);
            }
            catch
            {
                // Source recovery is defense-in-depth after NavigationStarting. A runtime failure
                // must leave the view stopped rather than relaxing the origin policy.
            }
            finally
            {
                _sourceRecoveryInProgress = false;
            }
        }

        private void OnWebResourceRequested(
            object? sender,
            CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (WebView2NavigationPolicy.IsAllowedResource(_pinnedOrigin, args.Request.Uri))
                return;

            try
            {
                CoreWebView2? core = _coreWebView2;
                if (core != null)
                {
                    args.Response = core.Environment.CreateWebResourceResponse(
                        null,
                        403,
                        "Forbidden",
                        "Cache-Control: no-store\r\nContent-Type: text/plain\r\n");
                }
            }
            catch
            {
                try { _coreWebView2?.Stop(); } catch { }
            }
        }

        private void SyncControllerBounds()
        {
            if (_controller == null)
                return;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(SyncControllerBounds);
                return;
            }

            try
            {
                if (!OperatingSystem.IsWindows() || _hostHwnd == IntPtr.Zero)
                    return;

                // Size the native host window to fill its parent (Avalonia's attachment, sized to this control), then
                // set the WebView controller to the host's ACTUAL client rect. Using the real native rect (no
                // RenderScaling math) + re-syncing on every resize is what makes the page fill the panel and track it.
                // PROVEN on screen (the gallery fills the editor panel at scale=1; host==parent==panel).
                IntPtr parent = GetParent(_hostHwnd);
                if (parent != IntPtr.Zero && GetClientRect(parent, out RECT pr))
                {
                    int pw = pr.Right - pr.Left;
                    int ph = pr.Bottom - pr.Top;
                    if (pw > 0 && ph > 0)
                        SetWindowPos(_hostHwnd, IntPtr.Zero, 0, 0, pw, ph, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
                }

                if (GetClientRect(_hostHwnd, out RECT hr))
                {
                    int hw = Math.Max(0, hr.Right - hr.Left);
                    int hh = Math.Max(0, hr.Bottom - hr.Top);
                    _controller.Bounds = new System.Drawing.Rectangle(0, 0, hw, hh);
                }
            }
            catch
            {
                // bounds sync best-effort
            }
        }

        private static void EnsureWindowClass()
        {
            lock (_classGate)
            {
                if (_classAtom != 0)
                    return;

                var wndClass = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    style = 0,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = GetModuleHandle(null),
                    hIcon = IntPtr.Zero,
                    hCursor = IntPtr.Zero,
                    hbrBackground = IntPtr.Zero,
                    lpszMenuName = null,
                    lpszClassName = HostWindowClassName,
                    hIconSm = IntPtr.Zero
                };

                _classAtom = RegisterClassEx(ref wndClass);
            }
        }

        private static IntPtr DefaultWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
            => DefWindowProc(hWnd, msg, wParam, lParam);

        // ---- Win32 interop ----

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            IntPtr lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        // 🐉 DEBUG LOG (v2.224.1.13): console bridge handler — os console.error/warn do JS hospedado
        // viram DebugLog (categoria WebView.Console). Parse leve do JSON {ffx,level,text}.
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (!WebView2NavigationPolicy.ShouldAcceptWebMessage(_pinnedOrigin, e.Source))
                    return;

                string json = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(json) || !json.Contains("\"ffx\":\"console\"", StringComparison.Ordinal))
                    return;
                string level = "log";
                int li = json.IndexOf("\"level\":\"", StringComparison.Ordinal);
                if (li >= 0)
                {
                    int start = li + 9;
                    int end = json.IndexOf('"', start);
                    if (end > start) level = json.Substring(start, end - start);
                }
                int ti = json.IndexOf("\"text\":\"", StringComparison.Ordinal);
                string text = ti >= 0 ? json.Substring(ti + 8) : json;
                if (text.EndsWith("\"}", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
                if (level == "error")
                    FFXProjectEditor.Diagnostics.DebugLog.Error("WebView.Console", text);
                else if (level == "warn")
                    FFXProjectEditor.Diagnostics.DebugLog.Warn("WebView.Console", text);
                else
                    FFXProjectEditor.Diagnostics.DebugLog.Info("WebView.Console", text);
            }
            catch { /* parse failures never crash the editor */ }
        }
    }
}
