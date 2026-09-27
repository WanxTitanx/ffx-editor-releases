using System;
using FFXProjectEditor.Modules.Common.ViewerShell;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    /// <summary>
    /// Security contract for the embedded viewer boundary. These tests deliberately exercise only
    /// pure URI/profile policy code; WebView2 event wiring is covered by build-time API checking.
    /// </summary>
    public sealed class WebView2NavigationPolicyTests
    {
        [Theory]
        [InlineData("http://127.0.0.1:8769/index.html")]
        [InlineData("http://127.42.0.9:8769/index.html")]
        [InlineData("http://localhost:8769/index.html")]
        [InlineData("http://noclip.localhost:8769/index.html")]
        [InlineData("http://viewer.deep.localhost:8769/index.html")]
        [InlineData("http://[::1]:8769/index.html")]
        public void PinOrigin_AcceptsOnlySupportedHttpLoopbackOrigins(string candidate)
        {
            Uri? origin = WebView2NavigationPolicy.TryPinOrigin(candidate);

            Assert.NotNull(origin);
            Assert.Equal("http", origin!.Scheme);
            Assert.Equal("/", origin.AbsolutePath);
            Assert.Empty(origin.UserInfo);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" http://127.0.0.1:8769/index.html")]
        [InlineData("http://127.0.0.1:8769/index.html ")]
        [InlineData("/index.html")]
        [InlineData("https://127.0.0.1:8769/index.html")]
        [InlineData("http://example.com:8769/index.html")]
        [InlineData("http://noclip.localhost.example.com:8769/index.html")]
        [InlineData("http://user@127.0.0.1:8769/index.html")]
        [InlineData("http://@127.0.0.1:8769/index.html")]
        [InlineData("http://0.0.0.0:8769/index.html")]
        [InlineData("http://[::]:8769/index.html")]
        [InlineData("file:///C:/viewer/index.html")]
        [InlineData("data:text/html,viewer")]
        [InlineData("javascript:alert(1)")]
        [InlineData("about:blank")]
        public void PinOrigin_RejectsUntrustedOrAmbiguousOrigins(string candidate)
        {
            Assert.Null(WebView2NavigationPolicy.TryPinOrigin(candidate));
        }

        [Fact]
        public void IsAllowed_DefaultDeniesWithoutPinnedOrigin()
        {
            Assert.False(WebView2NavigationPolicy.IsAllowed(
                pinnedOrigin: null,
                candidate: "http://127.0.0.1:8769/index.html"));
        }

        [Theory]
        [InlineData("http://127.0.0.1:8769/index.html")]
        [InlineData("http://127.0.0.1:8769/static/app.js")]
        [InlineData("http://127.0.0.1:8769/data/FinalFantasyX/a.bin?x=1#route")]
        public void IsAllowed_AcceptsAnyPathOnlyOnTheExactPinnedOrigin(string candidate)
        {
            Uri origin = WebView2NavigationPolicy.TryPinOrigin(
                "http://127.0.0.1:8769/index.html")!;

            Assert.True(WebView2NavigationPolicy.IsAllowed(origin, candidate));
        }

        [Theory]
        [InlineData("https://127.0.0.1:8769/index.html")]
        [InlineData("http://127.0.0.1:8770/index.html")]
        [InlineData("http://localhost:8769/index.html")]
        [InlineData("http://127.0.0.1:8769@evil.example/index.html")]
        [InlineData("http://127.0.0.1.evil.example:8769/index.html")]
        [InlineData("http://evil.example/?next=http://127.0.0.1:8769")]
        [InlineData("file:///C:/Windows/win.ini")]
        [InlineData("data:text/html,viewer")]
        [InlineData("javascript:alert(1)")]
        [InlineData("about:blank")]
        public void IsAllowed_RejectsPortSchemeHostUserInfoAndExternalSchemes(string candidate)
        {
            Uri origin = WebView2NavigationPolicy.TryPinOrigin(
                "http://127.0.0.1:8769/index.html")!;

            Assert.False(WebView2NavigationPolicy.IsAllowed(origin, candidate));
        }

        [Theory]
        [InlineData("http://noclip.localhost:8769/index.html", true)]
        [InlineData("http://NOCLIP.LOCALHOST:8769/static/app.js", true)]
        [InlineData("http://evilnoclip.localhost:8769/index.html", false)]
        [InlineData("http://noclip.localhost.evil.example:8769/index.html", false)]
        [InlineData("http://other.localhost:8769/index.html", false)]
        public void IsAllowed_LocalhostNamesStillRequireAnExactHost(string candidate, bool expected)
        {
            Uri origin = WebView2NavigationPolicy.TryPinOrigin(
                "http://noclip.localhost:8769/index.html")!;

            Assert.Equal(expected, WebView2NavigationPolicy.IsAllowed(origin, candidate));
        }

        [Theory]
        [InlineData("http://127.0.0.1:8769/static/app.js", true)]
        [InlineData("data:image/png;base64,AA==", true)]
        [InlineData("blob:http://127.0.0.1:8769/1d93260e-5ddd-4ca7-9ee2-2dc7fd7fb6bc", true)]
        [InlineData("blob:https://evil.example/1d93260e-5ddd-4ca7-9ee2-2dc7fd7fb6bc", false)]
        [InlineData("https://evil.example/collect", false)]
        [InlineData("http://127.0.0.1:8770/collect", false)]
        [InlineData("file:///C:/Windows/win.ini", false)]
        [InlineData("javascript:fetch('https://evil.example')", false)]
        public void IsAllowedResource_AllowsOnlyPinnedSelfAndNonNetworkLocalPayloads(
            string candidate,
            bool expected)
        {
            Uri origin = WebView2NavigationPolicy.TryPinOrigin(
                "http://127.0.0.1:8769/index.html")!;

            Assert.Equal(expected, WebView2NavigationPolicy.IsAllowedResource(origin, candidate));
        }

        [Fact]
        public void CreateProfileName_IsStableAcrossQueriesButSeparatesViewerRoutes()
        {
            string firstBattle = WebView2NavigationPolicy.CreateProfileName(
                "http://127.0.0.1:8769/index.html?battle=1&t=100#ffx/battle-preview",
                "#ffx/battle-preview");
            string secondBattle = WebView2NavigationPolicy.CreateProfileName(
                "http://127.0.0.1:8769/index.html?battle=2&t=200#ffx/battle-preview",
                "#ffx/battle-preview");
            string sameBattleAfterRestart = WebView2NavigationPolicy.CreateProfileName(
                "http://127.0.0.1:49152/index.html?battle=3&t=300#ffx/battle-preview",
                "#ffx/battle-preview");
            string monsterStudio = WebView2NavigationPolicy.CreateProfileName(
                "http://127.0.0.1:8769/index.html?t=300#ffx/monster-studio",
                "#ffx/monster-studio");
            string mapViewer = WebView2NavigationPolicy.CreateProfileName(
                "http://127.0.0.1:8769/map/index.html?t=400",
                hashRoute: null);

            Assert.Equal(firstBattle, secondBattle);
            Assert.Equal(firstBattle, sameBattleAfterRestart);
            Assert.NotEqual(firstBattle, monsterStudio);
            Assert.NotEqual(firstBattle, mapViewer);
            Assert.Matches("^FFXViewer-[0-9A-F]{24}$", firstBattle);
        }

        [Fact]
        public void EventDecisions_FailClosedAndShareTheExactPinnedOriginPolicy()
        {
            Uri origin = WebView2NavigationPolicy.TryPinOrigin(
                "http://127.0.0.1:8769/index.html")!;

            Assert.False(WebView2NavigationPolicy.ShouldCancelTopLevelNavigation(
                origin, "http://127.0.0.1:8769/map/index.html"));
            Assert.True(WebView2NavigationPolicy.ShouldCancelTopLevelNavigation(
                origin, "https://evil.example/"));
            Assert.False(WebView2NavigationPolicy.ShouldCancelFrameNavigation(
                origin, "http://127.0.0.1:8769/frame.html"));
            Assert.True(WebView2NavigationPolicy.ShouldCancelFrameNavigation(
                origin, "http://127.0.0.1:8770/frame.html"));
            Assert.True(WebView2NavigationPolicy.ShouldAcceptWebMessage(
                origin, "http://127.0.0.1:8769/index.html"));
            Assert.False(WebView2NavigationPolicy.ShouldAcceptWebMessage(
                origin, "https://evil.example/"));
            Assert.True(WebView2NavigationPolicy.ShouldCancelDownload());
        }
    }
}
