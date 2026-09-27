using System;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    public enum WebView2InitializationFailureKind
    {
        RuntimeInitializationFailed,
        PlatformUnavailable,
    }

    // ── Embedded-viewer failure boundary ───────────────────────────────────────────
    // Browser creation cannot be exercised reliably in unit tests. This small publisher preserves
    // typed observable state while keeping the Windows installer path exclusive to runtime failures
    // on the Windows implementation. Unsupported platforms never receive an installer action.
    public sealed class WebView2InitializationFailedEventArgs : EventArgs
    {
        internal WebView2InitializationFailedEventArgs(
            Exception exception,
            WebView2InitializationFailureKind kind,
            string? installerPath,
            bool installerAvailable)
        {
            Exception = exception;
            Kind = kind;
            InstallerPath = installerPath;
            InstallerAvailable = installerAvailable;
        }

        public Exception Exception { get; }

        public WebView2InitializationFailureKind Kind { get; }

        public string? InstallerPath { get; }

        public bool InstallerAvailable { get; }
    }

    internal sealed class WebView2InitializationFailurePublisher
    {
        internal const string InstallerFileName =
            "MicrosoftEdgeWebView2RuntimeInstallerX64.exe";

        private readonly string _applicationBaseDirectory;

        public WebView2InitializationFailurePublisher(string applicationBaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(applicationBaseDirectory))
                throw new ArgumentException(
                    "An application base directory is required.",
                    nameof(applicationBaseDirectory));

            _applicationBaseDirectory = Path.GetFullPath(applicationBaseDirectory);
        }

        public event EventHandler<WebView2InitializationFailedEventArgs>? Failed;

        public WebView2InitializationFailedEventArgs? Current { get; private set; }

        public WebView2InitializationFailedEventArgs Publish(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            string installerPath = Path.GetFullPath(Path.Combine(
                _applicationBaseDirectory,
                "prerequisites",
                "webview2",
                InstallerFileName));
            var failure = new WebView2InitializationFailedEventArgs(
                exception,
                WebView2InitializationFailureKind.RuntimeInitializationFailed,
                installerPath,
                File.Exists(installerPath));

            Current = failure;
            Failed?.Invoke(this, failure);
            return failure;
        }

        public WebView2InitializationFailedEventArgs PublishPlatformUnavailable()
        {
            var failure = new WebView2InitializationFailedEventArgs(
                new PlatformNotSupportedException(
                    "No embedded viewer backend satisfies the Studio security boundary on this platform."),
                WebView2InitializationFailureKind.PlatformUnavailable,
                installerPath: null,
                installerAvailable: false);

            Current = failure;
            Failed?.Invoke(this, failure);
            return failure;
        }

        public void Clear() => Current = null;
    }
}
