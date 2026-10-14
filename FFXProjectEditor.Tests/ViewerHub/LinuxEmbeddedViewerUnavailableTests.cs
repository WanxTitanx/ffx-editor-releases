using System;
using System.IO;
using Avalonia.Controls;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.Common.ViewerShell;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Unsupported embedded-viewer behavior ─────────────────────────────────────────
// These assertions execute on every desktop target. On Linux they prove the real facade refuses
// navigation deterministically; the paired Windows expectations prevent the test from disguising
// an accidental platform swap behind an OperatingSystem early-return or a skipped test.
[Collection(StudioWebServerLoopbackLifetimeTestCollection.Name)]
public sealed class LinuxEmbeddedViewerUnavailableTests
{
    [Fact]
    public void HostCapability_MatchesCurrentPlatformAndLinuxNavigationFailsExactlyOnce()
    {
        var host = new WebView2Host { ShowBuiltInRecoveryDialog = false };
        int failureCount = 0;
        WebView2InitializationFailedEventArgs? observed = null;
        host.InitializationFailed += (_, failure) =>
        {
            failureCount++;
            observed = failure;
        };

        host.AllowedOrigin = "http://127.0.0.1:49152/";
        Assert.Equal("http://127.0.0.1:49152", host.AllowedOrigin);
        host.InjectScriptOnDocumentCreated("window.__ffx_test = true;");
        host.NavigatePinned("http://127.0.0.1:49152/index.html", "#ffx/monster-studio");
        host.Navigate("http://127.0.0.1:49152/index.html?retry=1");

        Assert.Equal(OperatingSystem.IsWindows(), WebView2Host.IsSupported);
        Assert.Equal(
            WebView2Host.IsSupported,
            typeof(NativeControlHost).IsAssignableFrom(typeof(WebView2Host)));
        Assert.Equal(WebView2Host.IsSupported ? 0 : 1, failureCount);
        Assert.Equal(WebView2Host.IsSupported, host.IsVisible);
        Assert.Equal(
            WebView2Host.IsSupported ? null : WebView2InitializationFailureKind.PlatformUnavailable,
            observed?.Kind);
        Assert.True(WebView2Host.IsSupported || observed is
        {
            InstallerAvailable: false,
            InstallerPath: null,
            Exception: PlatformNotSupportedException,
        });

        host.Navigate("about:blank");
        Assert.Equal(WebView2Host.IsSupported ? 0 : 1, failureCount);
    }

    [Fact]
    public void BuildUrl_ForExternalBrowserStartsLoopbackServerOnEveryDesktopPlatform()
    {
        // Linux/macOS viewers consume the same loopback URL through the system browser
        // (ViewerShell.NavigateOrOpenExternal / AuroraChamber ExternalBrowserLauncher). The
        // embed-only refusal must not fire for callers that declared an external consumer.
        try
        {
            string? url = ViewerHubService.BuildUrl("magic-viewer", forExternalBrowser: true);

            Assert.NotNull(url);
            Assert.StartsWith("http://127.0.0.1:", url);
            Assert.Contains("/magic/index.html", url);
            Assert.True(ViewerHubService.Server is { IsRunning: true });
        }
        finally
        {
            ViewerHubService.StopServerForTests();
        }
    }

    [Fact]
    public void BuildUrl_OnUnsupportedPlatformReturnsBeforeCreatingLoopbackServer()
    {
        StudioWebServer? before = ViewerHubService.Server;

        string? url = WebView2Host.IsSupported
            ? null
            : ViewerHubService.BuildUrl("magic-viewer");

        Assert.True(WebView2Host.IsSupported || url == null);
        Assert.True(WebView2Host.IsSupported || ReferenceEquals(before, ViewerHubService.Server));
        Assert.True(WebView2Host.IsSupported || ViewerHubService.Server == null);
        Assert.True(WebView2Host.IsSupported ||
            ViewerHubService.StatusText == Strings.U_Vh_EmbeddedViewerUnavailableTitle);
    }

    [Fact]
    public void MonsterEditor_FixedCatalogReportsExplicitPlatformOrRouteUnavailableStatus()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m000.bin");
        string projectRoot = Directory.CreateTempSubdirectory("ffx-c1-moneditor-").FullName;
        string master = Directory.CreateDirectory(Path.Combine(projectRoot, "master")).FullName;
        string? savedProjectPath = Project_Service.Instance.ProjectPath;
        try
        {
            // Production reaches MonEditor only through GetPathMon after a project is loaded. The
            // empty master supplies that real precondition while intentionally omitting optional
            // English localization sidecars, so this test remains focused on viewer degradation.
            Project_Service.Instance.ProjectPath = master;
            Monster_File source = Monster_File.Read(File.ReadAllBytes(fixture));
            source.StatSheetFile.ModelId = 0x3001;
            source.StatSheetFile.Model2Id = 0x3001;

            // Poison the process-global status with a deterministic unrelated failure. The model
            // must derive its own state instead of copying whichever ViewerHub call ran last.
            Assert.Null(ViewerHubService.BuildUrl("__c1_unknown_viewer__"));
            var model = new MonEditor_DataModel(source, fixture, new MonEditorSelector_DataModel());
            string expectedStatus = WebView2Host.IsSupported
                ? Strings.U_Vh_ModelPreviewRouteUnavailable
                : Strings.U_Vh_EmbeddedViewerUnavailableTitle;

            Assert.Equal("s001", model.ModelPreviewCatalogId);
            Assert.Null(model.ModelPreviewUrl);
            Assert.Equal(expectedStatus, model.ModelPreviewStatus);

            model.MonsterStatSheet.ModelId = 0x3002;
            model.MonsterStatSheet.Model2Id = 0x3002;
            Assert.Equal("s002", model.ModelPreviewCatalogId);
            Assert.Null(model.ModelPreviewUrl);
            Assert.Equal(expectedStatus, model.ModelPreviewStatus);
        }
        finally
        {
            Project_Service.Instance.ProjectPath = savedProjectPath;
            Directory.Delete(projectRoot, recursive: true);
        }
    }
}
