using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    // ── Embedded-viewer URI boundary ────────────────────────────────────────────────
    // Web content is untrusted even when it is served from loopback. Keep origin parsing
    // independent from browser event objects so every spoofing edge case remains shared and
    // unit-testable on every supported desktop target.
    // MAINT: never replace these component comparisons with textual prefix matching.
    internal static class WebView2NavigationPolicy
    {
        public static Uri? TryPinOrigin(string? candidate)
        {
            if (!TryParseTrustedLoopbackHttpUri(candidate, out Uri? uri))
                return null;

            var origin = new UriBuilder(Uri.UriSchemeHttp, uri.DnsSafeHost, uri.Port, "/")
            {
                Query = string.Empty,
                Fragment = string.Empty,
                UserName = string.Empty,
                Password = string.Empty,
            };
            return origin.Uri;
        }

        public static bool IsAllowed(Uri? pinnedOrigin, string? candidate)
        {
            if (pinnedOrigin == null ||
                TryPinOrigin(pinnedOrigin.AbsoluteUri) == null ||
                !TryParseTrustedLoopbackHttpUri(candidate, out Uri? target))
                return false;

            return string.Equals(pinnedOrigin.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pinnedOrigin.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                   pinnedOrigin.Port == target.Port;
        }

        public static bool IsAllowedResource(Uri? pinnedOrigin, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) ||
                candidate.AsSpan().Trim().Length != candidate.Length)
                return false;

            if (IsAllowed(pinnedOrigin, candidate))
                return true;

            // Data URLs and blobs created by the pinned origin are local renderer payloads, not
            // network egress. A blob still carries its creator origin, which must match exactly.
            if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? dataUri) &&
                       string.Equals(dataUri.Scheme, "data", StringComparison.OrdinalIgnoreCase);
            if (candidate.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                return IsAllowed(pinnedOrigin, candidate.Substring("blob:".Length));

            return false;
        }

        internal static bool ShouldCancelTopLevelNavigation(Uri? pinnedOrigin, string? candidate) =>
            !IsAllowed(pinnedOrigin, candidate);

        internal static bool ShouldCancelFrameNavigation(Uri? pinnedOrigin, string? candidate) =>
            !IsAllowed(pinnedOrigin, candidate);

        internal static bool ShouldAcceptWebMessage(Uri? pinnedOrigin, string? source) =>
            IsAllowed(pinnedOrigin, source);

        internal static bool ShouldCancelDownload() => true;

        public static string CreateProfileName(string navigationUri, string? hashRoute)
        {
            if (!TryParseTrustedLoopbackHttpUri(navigationUri, out Uri? uri))
                throw new ArgumentException("A trusted loopback HTTP URI is required.", nameof(navigationUri));

            string route = string.IsNullOrWhiteSpace(hashRoute) ? uri.Fragment : hashRoute;
            // The loopback server intentionally uses an ephemeral port. Persist one WebView2
            // profile per viewer route, not one profile per application restart.
            string seed = $"http://{uri.IdnHost.ToLowerInvariant()}{uri.AbsolutePath}|{route.TrimStart('#')}";
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            return "FFXViewer-" + Convert.ToHexString(digest.AsSpan(0, 12));
        }

        private static bool TryParseTrustedLoopbackHttpUri(string? candidate, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(candidate) ||
                candidate.AsSpan().Trim().Length != candidate.Length ||
                HasExplicitUserInfoDelimiter(candidate) ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed) ||
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                parsed.Port <= 0 ||
                !IsTrustedLoopbackHost(parsed.DnsSafeHost))
                return false;

            uri = parsed;
            return true;
        }

        private static bool HasExplicitUserInfoDelimiter(string candidate)
        {
            int schemeEnd = candidate.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0)
                return false;

            ReadOnlySpan<char> authority = candidate.AsSpan(schemeEnd + 3);
            int authorityEnd = authority.IndexOfAny('/', '?', '#');
            if (authorityEnd >= 0)
                authority = authority[..authorityEnd];
            return authority.IndexOf('@') >= 0;
        }

        private static bool IsTrustedLoopbackHost(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress? address))
                return IPAddress.IsLoopback(address);

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            const string LocalhostSuffix = ".localhost";
            return host.Length > LocalhostSuffix.Length &&
                   host.EndsWith(LocalhostSuffix, StringComparison.OrdinalIgnoreCase) &&
                   host.Split('.').All(static label => label.Length > 0);
        }
    }
}
