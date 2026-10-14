using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerShell;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    /// <summary>
    /// Exercises the WebView2 initialization-failure boundary without creating a native WebView2
    /// environment or controller. Runtime installation remains an explicit human action.
    /// </summary>
    public sealed class WebView2InitializationFailureTests
    {
        [Fact]
        public void Publish_SetsObservableStateAndRaisesFailureEvent()
        {
            string baseDirectory = Path.Combine(
                Path.GetTempPath(),
                $"FFX-WebView2-Missing-{Guid.NewGuid():N}");
            var publisher = new WebView2InitializationFailurePublisher(baseDirectory);
            var exception = new InvalidOperationException("runtime unavailable");
            WebView2InitializationFailedEventArgs? observed = null;
            publisher.Failed += (_, args) => observed = args;

            WebView2InitializationFailedEventArgs published = publisher.Publish(exception);

            Assert.Same(published, publisher.Current);
            Assert.Same(published, observed);
            Assert.Same(exception, published.Exception);
            Assert.Equal(WebView2InitializationFailureKind.RuntimeInitializationFailed, published.Kind);
            Assert.False(published.InstallerAvailable);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    "prerequisites",
                    "webview2",
                    "MicrosoftEdgeWebView2RuntimeInstallerX64.exe")),
                published.InstallerPath);
        }

        [Fact]
        public void PublishPlatformUnavailable_HasNoInstallerAndRaisesOnlyOnceWhenGuardedByCurrentState()
        {
            var publisher = new WebView2InitializationFailurePublisher(AppContext.BaseDirectory);
            int failureCount = 0;
            publisher.Failed += (_, _) => failureCount++;

            WebView2InitializationFailedEventArgs first = publisher.PublishPlatformUnavailable();
            WebView2InitializationFailedEventArgs second =
                publisher.Current ?? publisher.PublishPlatformUnavailable();

            Assert.Same(first, second);
            Assert.Equal(1, failureCount);
            Assert.Equal(WebView2InitializationFailureKind.PlatformUnavailable, first.Kind);
            Assert.IsType<PlatformNotSupportedException>(first.Exception);
            Assert.Null(first.InstallerPath);
            Assert.False(first.InstallerAvailable);
            Assert.False(WebView2RecoveryDialog.TryRevealInstaller(first, out string error));
            Assert.Equal("INSTALLER_ACTION_UNAVAILABLE", error);
        }

        [Fact]
        public void Publish_ReportsBundledOfflineInstallerWhenPresent()
        {
            string baseDirectory = Directory.CreateTempSubdirectory("FFX-WebView2-Recovery-").FullName;
            string installerDirectory = Path.Combine(baseDirectory, "prerequisites", "webview2");
            string installerPath = Path.Combine(
                installerDirectory,
                "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");

            try
            {
                Directory.CreateDirectory(installerDirectory);
                File.WriteAllText(installerPath, "test fixture only");
                var publisher = new WebView2InitializationFailurePublisher(baseDirectory);

                WebView2InitializationFailedEventArgs published = publisher.Publish(
                    new InvalidOperationException("runtime unavailable"));

                Assert.True(published.InstallerAvailable);
                Assert.Equal(Path.GetFullPath(installerPath), published.InstallerPath);
            }
            finally
            {
                if (File.Exists(installerPath))
                    File.Delete(installerPath);
                if (Directory.Exists(installerDirectory))
                    Directory.Delete(installerDirectory);
                string prerequisitesDirectory = Path.Combine(baseDirectory, "prerequisites");
                if (Directory.Exists(prerequisitesDirectory))
                    Directory.Delete(prerequisitesDirectory);
                if (Directory.Exists(baseDirectory))
                    Directory.Delete(baseDirectory);
            }
        }

        [Fact]
        public void Clear_RemovesCurrentFailureWithoutPublishingAnotherFailure()
        {
            var publisher = new WebView2InitializationFailurePublisher(AppContext.BaseDirectory);
            int failureCount = 0;
            publisher.Failed += (_, _) => failureCount++;
            publisher.Publish(new InvalidOperationException("runtime unavailable"));

            publisher.Clear();

            Assert.Null(publisher.Current);
            Assert.Equal(1, failureCount);
        }

        [Fact]
        public void ViewerShellState_InitializationFailureForcesErrorAndRecoveryCta()
        {
            var model = new ViewerShell_DataModel();

            model.ShowWebView2InitializationFailure(
                statusText: "WebView2 could not start",
                recoveryMessage: "Install the bundled runtime, then reload.",
                installerAvailable: true);

            Assert.True(model.IsError);
            Assert.False(model.IsRunning);
            Assert.False(model.IsStarting);
            Assert.True(model.HasWebView2InitializationFailure);
            Assert.True(model.CanShowWebView2Installer);
            Assert.Equal("WebView2 could not start", model.StatusText);
            Assert.Equal("Install the bundled runtime, then reload.", model.WebView2RecoveryMessage);
        }

        [Fact]
        public void ViewerShellState_PlatformUnavailableUsesGenericTitleAndNoInstallerCta()
        {
            var model = new ViewerShell_DataModel();

            model.ShowWebView2InitializationFailure(
                statusText: "Embedded viewer unavailable",
                recoveryMessage: "Native editor features remain available.",
                installerAvailable: false,
                showInstallerAction: false);

            Assert.True(model.HasWebView2InitializationFailure);
            Assert.False(model.CanShowWebView2Installer);
            Assert.False(model.HasWebView2InstallerAction);
            Assert.Equal("Embedded viewer unavailable", model.EmbeddedViewerFailureTitle);
            Assert.Equal("Native editor features remain available.", model.WebView2RecoveryMessage);
        }

        [Fact]
        public void Host_ExposesFailureEventAndStateWithoutNativeWebViewConstruction()
        {
            var eventInfo = typeof(WebView2Host).GetEvent(nameof(WebView2Host.InitializationFailed));
            var stateProperty = typeof(WebView2Host).GetProperty(nameof(WebView2Host.InitializationFailure));

            Assert.NotNull(eventInfo);
            Assert.Equal(
                typeof(EventHandler<WebView2InitializationFailedEventArgs>),
                eventInfo!.EventHandlerType);
            Assert.NotNull(stateProperty);
            Assert.Equal(typeof(WebView2InitializationFailedEventArgs), stateProperty!.PropertyType);
            Assert.False(stateProperty.CanWrite);
        }

        [Fact]
        public void Host_DefaultsToBuiltInRecoveryDialogForDirectViewerConsumers()
        {
            var host = new WebView2Host();

            Assert.True(host.ShowBuiltInRecoveryDialog);
            Assert.NotNull(typeof(WebView2Host).GetProperty(
                nameof(WebView2Host.ShowBuiltInRecoveryDialog)));
        }
    }
}
