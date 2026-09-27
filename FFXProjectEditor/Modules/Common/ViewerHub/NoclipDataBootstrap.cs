using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// NOCLIP DATA BOOTSTRAP — seeds the editor-managed NoClip data root on clean machines.
    /// The bundled viewer needs data/FinalFantasyX with the 12 core CDN directories (each with at
    /// least one canonical NNNN.bin) plus the 3 critical root bins. Instead of downloading the full
    /// ~2.7 GB extraction up front, the bootstrap creates the skeleton — every core dir seeded with
    /// its first CDN file plus the critical files — and the StudioWebServer fetch-through lazily
    /// pulls any additional file the viewer requests, cached into this same root.
    ///
    /// SAFETY: writes only inside <see cref="BootstrapRoot"/> (LocalAppData), never into a
    /// user-selected extraction. All writes are atomic (tmp + move), size-bounded, and the root
    /// carries an <see cref="ManagedMarkerName"/> marker so fetch-through can tell our writable
    /// root apart from user-owned data.
    /// </summary>
    public static class NoclipDataBootstrap
    {
        public const string CdnBase = "https://z.noclip.website/FinalFantasyX";
        public const string ManagedMarkerName = ".ffx-editor-managed";

        private const long MaxFetchBytes = 256L * 1024 * 1024;
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

        private static readonly Regex IndexFileEntry = new(
            "href=\"(?<name>[0-9a-zA-Z_]{1,64}\\.bin)\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HttpClient SharedClient = CreateClient();
        private static readonly SemaphoreSlim SingleFlight = new(1, 1);

        // Repeated viewer clicks must not queue serial multi-timeout seed attempts when the CDN
        // is unreachable: the last failed report is replayed for a short cooldown window.
        private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(2);
        private static NoclipDataCapability.Report? _lastFailedReport;
        private static long _lastFailedAtUtc;

        public static string BootstrapRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "noclip-data");

        public static string BootstrapFfxDataRoot => Path.Combine(
            BootstrapRoot, "data", "FinalFantasyX");

        /// <summary>True when <paramref name="noclipRoot"/> is the editor-managed bootstrap root.</summary>
        public static bool IsBootstrapRoot(string? noclipRoot)
        {
            if (string.IsNullOrWhiteSpace(noclipRoot))
                return false;
            try
            {
                string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(noclipRoot));
                string managed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(BootstrapRoot));
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return string.Equals(candidate, managed, comparison);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the mapped /data root (parent of FinalFantasyX) is the bootstrap root's data dir.</summary>
        public static bool IsBootstrapDataRoot(string? dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot))
                return false;
            try
            {
                string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
                string managedData = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.Combine(BootstrapRoot, "data")));
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return string.Equals(candidate, managedData, comparison);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures a usable NoClip data root exists. Returns the capability report of the best
        /// available root: the configured one when already valid, otherwise the bootstrap root after
        /// seeding. Never overwrites valid user data; partially-seeded files are completed.
        /// </summary>
        public static async Task<NoclipDataCapability.Report> EnsureDataAsync(
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            NoclipDataCapability.Report? recentFailure = _lastFailedReport;
            if (recentFailure != null &&
                DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastFailedAtUtc), DateTimeKind.Utc) < FailureCooldown)
            {
                // A previously validated user root is rechecked inside the flight below; the
                // cooldown only short-circuits the seed attempt itself.
                string? recheck = NoclipLocator.Find();
                NoclipDataCapability.Report recheckReport = NoclipDataCapability.Validate(recheck);
                if (!recheckReport.Ready)
                    return recentFailure;
            }

            await SingleFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // A fully-configured user root always wins — bootstrap only fills the gap.
                string? configured = NoclipLocator.Find();
                NoclipDataCapability.Report report = NoclipDataCapability.Validate(configured);
                if (report.Ready)
                {
                    _lastFailedReport = null;
                    return report;
                }

                // A queued caller may have arrived behind a just-failed seed attempt — replay the
                // cooldown inside the flight too so serial clicks never stack network timeouts.
                NoclipDataCapability.Report? inFlightFailure = _lastFailedReport;
                if (inFlightFailure != null &&
                    DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastFailedAtUtc), DateTimeKind.Utc) < FailureCooldown)
                    return inFlightFailure;

                status?.Report(Strings.U_Vh_NoclipDataPreparing);
                DebugLog.Info("Hub.NoclipBootstrap", "Seeding editor-managed NoClip data root…");

                string ffxData = BootstrapFfxDataRoot;
                Directory.CreateDirectory(ffxData);

                foreach (string directory in NoclipDataCapability.RequiredDirectoryNames)
                    Directory.CreateDirectory(Path.Combine(ffxData, directory));

                // Critical files: re-download when missing or when the plausibility check fails.
                foreach (string critical in new[] { "common_textures.bin", "screen_shatter.bin", "env_map_texture.bin" })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target = Path.Combine(ffxData, critical);
                    int offsetField = critical == "env_map_texture.bin" ? 0x18 : 0x3C;
                    if (File.Exists(target) && NoclipDataCapability.HasPlausibleOffset(ffxData, target, offsetField))
                        continue;
                    if (!await DownloadToFileAsync(critical, target, cancellationToken).ConfigureAwait(false))
                        DebugLog.Warn("Hub.NoclipBootstrap", $"critical file download failed: {critical}");
                }

                // Seed each core directory with its first canonical file so the capability gate
                // sees real data; the rest streams through fetch-through on demand.
                foreach (string directory in NoclipDataCapability.RequiredDirectoryNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string dirPath = Path.Combine(ffxData, directory);
                    if (ContainsCanonicalBin(dirPath))
                        continue;

                    string? seedName = await FirstIndexFileAsync(directory, cancellationToken).ConfigureAwait(false);
                    if (seedName == null)
                    {
                        DebugLog.Warn("Hub.NoclipBootstrap", $"CDN index empty or unreachable for {directory}/");
                        continue;
                    }

                    await DownloadToFileAsync(
                        $"{directory}/{seedName}",
                        Path.Combine(dirPath, seedName),
                        cancellationToken).ConfigureAwait(false);
                }

                File.WriteAllText(Path.Combine(ffxData, ManagedMarkerName),
                    $"bootstrap {DateTime.UtcNow:O}{Environment.NewLine}");
                NoclipLocator.ResetCache();

                report = NoclipDataCapability.Validate(BootstrapRoot);
                if (report.Ready)
                {
                    _lastFailedReport = null;
                }
                else
                {
                    _lastFailedReport = report;
                    Interlocked.Exchange(ref _lastFailedAtUtc, DateTime.UtcNow.Ticks);
                }
                DebugLog.Info("Hub.NoclipBootstrap",
                    report.Ready
                        ? $"Bootstrap root ready at {BootstrapRoot}."
                        : $"Bootstrap incomplete: {string.Join(",", report.Issues)}");
                return report;
            }
            finally
            {
                SingleFlight.Release();
            }
        }

        internal static bool ContainsCanonicalBin(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*.bin", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Any(name => name is { Length: 8 } &&
                        ushort.TryParse(
                            name.AsSpan(0, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out _));
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string?> FirstIndexFileAsync(string directory, CancellationToken cancellationToken)
        {
            try
            {
                string html = await SharedClient
                    .GetStringAsync($"{CdnBase}/{directory}/", cancellationToken)
                    .ConfigureAwait(false);
                return IndexFileEntry.Matches(html)
                    .Select(match => match.Groups["name"].Value)
                    .FirstOrDefault(name => name.Length == 8);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                DebugLog.Warn("Hub.NoclipBootstrap", $"index fetch failed for {directory}/: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> DownloadToFileAsync(
            string relativePath,
            string destination,
            CancellationToken cancellationToken)
        {
            string staging = destination + ".bootstrap-tmp";
            try
            {
                using HttpResponseMessage response = await SharedClient
                    .GetAsync($"{CdnBase}/{relativePath}", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return false;
                if (response.Content.Headers.ContentLength is > MaxFetchBytes)
                    return false;

                await using (Stream source = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream target = new(staging, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long written = await CopyBoundedAsync(source, target, cancellationToken).ConfigureAwait(false);
                    if (written < 0)
                        return false;
                }

                File.Move(staging, destination, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                DebugLog.Warn("Hub.NoclipBootstrap", $"download {relativePath} failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); } catch { }
            }
        }

        private static async Task<long> CopyBoundedAsync(Stream source, Stream target, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(
                buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxFetchBytes)
                    return -1;
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return total;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = FetchTimeout };
            // The noclip CDN (Cloudflare) rejects the default .NET User-Agent with 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            return client;
        }
    }
}
