using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    /// <summary>
    /// Linux facade for the embedded-viewer API used by the shared XAML surfaces. No currently
    /// vetted Linux browser backend satisfies the Windows navigation/resource/message boundary,
    /// so this control stays hidden and reports one typed, recoverable capability failure instead
    /// of creating a browser, native child handle, external process, or misleading blank surface.
    /// </summary>
    public sealed class WebView2Host : Control, IDisposable
    {
        private readonly WebView2InitializationFailurePublisher _initializationFailures =
            new(AppContext.BaseDirectory);
        private readonly object _failureGate = new();
        private Uri? _pinnedOrigin;
        private bool _disposed;
        private bool _failurePublished;

        public static bool IsSupported => false;

        public event EventHandler<WebView2InitializationFailedEventArgs>? InitializationFailed
        {
            add => _initializationFailures.Failed += value;
            remove => _initializationFailures.Failed -= value;
        }

        public WebView2InitializationFailedEventArgs? InitializationFailure =>
            _initializationFailures.Current;

        public bool ShowBuiltInRecoveryDialog { get; set; } = true;

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
            }
        }

        public WebView2Host()
        {
            IsVisible = false;
        }

        public void InjectScriptOnDocumentCreated(string script)
        {
            // Intentionally empty. Retaining or executing script on an unavailable backend would
            // create false capability state; Navigate publishes the user-visible failure instead.
        }

        public void NavigatePinned(string url, string hashRoute)
        {
            Uri? requestedOrigin = WebView2NavigationPolicy.TryPinOrigin(url);
            if (requestedOrigin == null)
            {
                _pinnedOrigin = null;
                return;
            }

            if (_pinnedOrigin != null &&
                !WebView2NavigationPolicy.IsAllowed(_pinnedOrigin, requestedOrigin.AbsoluteUri))
                return;

            _pinnedOrigin = requestedOrigin;
            Navigate(url);
        }

        public void Navigate(string url)
        {
            if (_disposed || string.IsNullOrWhiteSpace(url))
                return;

            // Existing callers use about:blank only as a clear command. It remains harmless and
            // must not manufacture a platform failure after a panel is intentionally cleared.
            if (url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                IsVisible = false;
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

            PublishPlatformUnavailableOnce();
        }

        public void Dispose()
        {
            _disposed = true;
            IsVisible = false;
        }

        private void PublishPlatformUnavailableOnce()
        {
            lock (_failureGate)
            {
                if (_failurePublished)
                    return;

                _failurePublished = true;
            }

            // Publish outside the gate because subscribers may synchronously inspect or dispose
            // the host. The flag still guarantees one platform-unavailable notification.
            WebView2InitializationFailedEventArgs failure =
                _initializationFailures.PublishPlatformUnavailable();
            FFXProjectEditor.Diagnostics.DebugLog.Warn(
                "WebView.Initialization",
                "Embedded viewer navigation refused because no supported Linux backend is available.");
            if (ShowBuiltInRecoveryDialog)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _ = WebView2RecoveryDialog.ShowAsync(this, failure);
                });
            }
        }
    }
}
