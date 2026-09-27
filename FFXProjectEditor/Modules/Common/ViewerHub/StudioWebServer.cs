using FFXProjectEditor.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// Loopback-only static HTTP server shared by the Studio viewers.
    ///
    /// SECURITY: a fixed port is only a compatibility default, never an ownership signal. The server
    /// does not inspect or terminate another process when a bind fails. Callers may pass port 0 and must
    /// use <see cref="Port"/> after <see cref="Start(int)"/> to obtain the factual listener port.
    /// </summary>
    public sealed partial class StudioWebServer
    {
        public const int DefaultPort = 8769;

        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxRequestTargetChars = 4096;
        private const int MaxEditBodyBytes = 64 * 1024;
        private const long MaxStaticFileBytes = 512L * 1024 * 1024;
        private const int MaxConcurrentClients = 32;
        private const int ListenBacklog = 64;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly object _lifecycleLock = new();
        private readonly object _routeLock = new();
        private readonly Dictionary<string, string> _hostRoots = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> _prefixRoots = new();
        private readonly SemaphoreSlim _clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);
        private readonly SemaphoreSlim _editWriteLock = new(1, 1);
        private readonly string? _editWriteRoot;
        private sealed record NativePositionRoute(string Token, Func<string, string, Task<(bool Ok, string Json)>> Handler);
        private NativePositionRoute? _nativePositionRoute;

        internal string ConfigureNativePositions(Func<string, string, Task<(bool Ok, string Json)>> handler)
        {
            var current = Volatile.Read(ref _nativePositionRoute);
            string token = current?.Handler == handler ? current.Token : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Volatile.Write(ref _nativePositionRoute, new NativePositionRoute(token, handler));
            return token;
        }

        internal void ClearNativePositions(object owner)
        {
            var current = Volatile.Read(ref _nativePositionRoute);
            if (current?.Handler.Target == owner)
                Interlocked.CompareExchange(ref _nativePositionRoute, null, current);
        }

        // ── NoClip data fetch-through ───────────────────────────────────────────────────────
        // Enabled only for the editor-managed bootstrap data root: a 404 under
        // data/FinalFantasyX/<hexdir>/<name>.bin lazily pulls the file from the official noclip
        // CDN, writes it atomically into the bootstrap root, then serves it. User-selected data
        // roots never get this path — the editor never mutates a user extraction. Per-path
        // single-flight plus a negative cache keep repeated viewer requests from hammering the CDN.
        private volatile string? _noclipFetchThroughDataRoot;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _noclipFetchLocks =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _noclipFetchMisses =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient NoclipFetchClient = CreateNoclipFetchClient();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private long _startedAt;

        public bool IsRunning { get; private set; }
        public string Status { get; private set; } = Strings.U_Vh_ServerStopped;
        public int Port { get; private set; } = DefaultPort;

        public StudioWebServer()
        {
        }

        /// <summary>
        /// Creates a server with one fixed, explicit filesystem capability for
        /// <c>POST /api/edits/&lt;encounter&gt;</c>. Static <c>/data</c> mappings never grant write
        /// authority and cannot replace this constructor argument.
        /// </summary>
        public StudioWebServer(string editWriteRoot)
        {
            if (string.IsNullOrWhiteSpace(editWriteRoot))
                throw new ArgumentException("The edit write root is required.", nameof(editWriteRoot));
            _editWriteRoot = Path.GetFullPath(editWriteRoot);
            RegisterExistingEditOverlays();
        }

        /// <summary>
        /// Re-registers only canonical top-level sidecars created in the per-user overlay root.
        /// Selected /data remains the fallback for every encounter without a local edit.
        /// </summary>
        private void RegisterExistingEditOverlays()
        {
            if (_editWriteRoot == null ||
                !Directory.Exists(_editWriteRoot) ||
                FileSystemReparseGuard.ContainsReparsePointInExistingChain(_editWriteRoot))
                return;

            foreach (string file in Directory.EnumerateFiles(
                _editWriteRoot,
                "*.json",
                SearchOption.TopDirectoryOnly))
            {
                string encounterId = Path.GetFileNameWithoutExtension(file);
                if (!NoclipOverlayStore.TryNormalizeEncounterId(encounterId, out string canonicalEncounterId) ||
                    !string.Equals(encounterId, canonicalEncounterId, StringComparison.Ordinal) ||
                    FileSystemReparseGuard.ContainsReparsePointInExistingChain(file))
                    continue;

                TryMapExactFile($"/data/FinalFantasyX/edits/{canonicalEncounterId}.json", file);
            }
        }

        // ── Route registration ───────────────────────────────────────────────────────────────
        // Roots are canonicalized once. URL prefixes are stored without a trailing slash so an
        // exact base request (/magic) and descendants (/magic/app.js) share one boundary-safe test.

        public StudioWebServer MapHost(string host, string serveRoot)
        {
            string normalizedHost = NormalizeMappedHost(host);
            string normalizedRoot = Path.GetFullPath(serveRoot ?? throw new ArgumentNullException(nameof(serveRoot)));
            lock (_routeLock)
                _hostRoots[normalizedHost] = normalizedRoot;
            return this;
        }

        /// <summary>
        /// Maps an explicit URL prefix to a static root. Prefixes are segment-boundary matched;
        /// mapping <c>/magic</c> never matches <c>/magic-other</c>. Mapping <c>/</c> remains an
        /// explicit caller-selected fallback and does not create a special <c>/work</c> route.
        /// </summary>
        public StudioWebServer MapPrefix(string prefix, string serveRoot)
        {
            string normalizedPrefix = NormalizeMappedPrefix(prefix);
            string normalizedRoot = Path.GetFullPath(serveRoot ?? throw new ArgumentNullException(nameof(serveRoot)));

            lock (_routeLock)
            {
                int existing = _prefixRoots.FindIndex(pair =>
                    string.Equals(pair.Key, normalizedPrefix, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                    _prefixRoots.RemoveAt(existing);
                _prefixRoots.Add(new KeyValuePair<string, string>(normalizedPrefix, normalizedRoot));
                _prefixRoots.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }
            return this;
        }

        /// <summary>
        /// Enables CDN fetch-through for one mapped /data root — the editor-managed bootstrap
        /// directory only. Callers decide ownership; this server never enables it implicitly and
        /// never writes into any other root.
        /// </summary>
        public void EnableNoclipFetchThrough(string dataRoot)
        {
            try
            {
                string canonical = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(dataRoot ?? throw new ArgumentNullException(nameof(dataRoot))));
                _noclipFetchThroughDataRoot = canonical;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException("The fetch-through data root must be a valid path.", nameof(dataRoot), ex);
            }
        }

        /// <summary>
        /// Registers one canonical request path for one existing regular file. Exact overlays are
        /// case-sensitive and never broaden a directory capability. If the staged file later goes
        /// away, route resolution falls back to the normal prefix/host mapping.
        /// </summary>
        public bool TryMapExactFile(string requestPath, string filePath)
        {
            if (!TryNormalizeMappedExactPath(requestPath, out string normalizedPath) ||
                string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string? directory = Path.GetDirectoryName(fullPath);
                string fileName = Path.GetFileName(fullPath);
                if (directory == null || fileName.Length == 0 ||
                    !TryBuildContainedPath(directory, fileName, out string verifiedPath) ||
                    !string.Equals(
                        verifiedPath,
                        fullPath,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                    return false;

                FileSystemReparseGuard.VerifiedOpenResult openResult =
                    FileSystemReparseGuard.TryOpenVerifiedRead(
                        directory,
                        fullPath,
                        out FileSystemReparseGuard.VerifiedReadFile? verifiedFile);
                if (openResult != FileSystemReparseGuard.VerifiedOpenResult.Success || verifiedFile == null)
                    return false;
                FileSystemReparseGuard.FileIdentity identity;
                using (verifiedFile)
                    identity = verifiedFile.Identity;

                lock (_routeLock)
                {
                    if (!_exactOwners.TryGetValue(normalizedPath, out List<ExactOwner>? owners))
                    {
                        owners = new List<ExactOwner>();
                        _exactOwners.Add(normalizedPath, owners);
                    }

                    StringComparison comparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                    owners.RemoveAll(owner =>
                        owner is FileExactOwner fileOwner &&
                        string.Equals(fileOwner.FullPath, fullPath, comparison));
                    owners.Add(new FileExactOwner(fullPath, identity));
                }
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Releases only the expected exact-file owner. Removing a non-current owner leaves the
        /// current route untouched; removing the current owner restores the preceding live owner.
        /// </summary>
        public bool TryUnmapExactFile(string requestPath, string expectedFilePath)
        {
            if (!TryNormalizeMappedExactPath(requestPath, out string normalizedPath) ||
                string.IsNullOrWhiteSpace(expectedFilePath))
                return false;

            try
            {
                string expectedFullPath = Path.GetFullPath(expectedFilePath);
                StringComparison pathComparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                lock (_routeLock)
                {
                    if (!_exactOwners.TryGetValue(normalizedPath, out List<ExactOwner>? owners))
                        return false;

                    int ownerIndex = owners.FindLastIndex(owner =>
                        owner is FileExactOwner fileOwner &&
                        string.Equals(fileOwner.FullPath, expectedFullPath, pathComparison));
                    if (ownerIndex < 0)
                        return false;

                    owners.RemoveAt(ownerIndex);
                    if (owners.Count == 0)
                        _exactOwners.Remove(normalizedPath);
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException)
            {
                return false;
            }
        }

        public IReadOnlyDictionary<string, string> Hosts
        {
            get
            {
                lock (_routeLock)
                    return new Dictionary<string, string>(_hostRoots, StringComparer.OrdinalIgnoreCase);
            }
        }

        // ── Listener lifecycle ────────────────────────────────────────────────────────────────
        // A failed bind is a normal, non-destructive failure. In particular, there is deliberately
        // no PID lookup or process reclaim. Port 0 asks Windows for an available loopback port.

        public bool Start(int port = DefaultPort)
        {
            lock (_lifecycleLock)
            {
                if (IsRunning)
                    return true;
                if (port is < 0 or > 65535)
                {
                    Status = string.Format(Strings.U_Vh_ServerPortBusy, port);
                    return false;
                }

                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start(ListenBacklog);
                }
                catch (SocketException)
                {
                    listener.Stop();
                    Status = string.Format(Strings.U_Vh_ServerPortBusy, port);
                    return false;
                }

                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _startedAt = Environment.TickCount64;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                CancellationToken listenerToken = _cts.Token;
                IsRunning = true;
                Status = string.Format(Strings.U_Vh_ServerRunning, Port, string.Join(", ", Hosts.Keys));
                _ = Task.Run(() => AcceptLoop(listener, listenerToken));
                return true;
            }
        }

        public void Stop()
        {
            Volatile.Write(ref _nativePositionRoute, null);
            CancellationTokenSource? cts;
            TcpListener? listener;
            lock (_lifecycleLock)
            {
                IsRunning = false;
                cts = _cts;
                listener = _listener;
                _cts = null;
                _listener = null;
                Status = Strings.U_Vh_ServerStopped;
            }

            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            cts?.Dispose();
        }

        private async Task AcceptLoop(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                    await _clientSlots.WaitAsync(cancellationToken);
                    _ = HandleClientWithSlot(client, cancellationToken);
                    client = null;
                }
                catch (OperationCanceledException)
                {
                    client?.Dispose();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    client?.Dispose();
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    client?.Dispose();
                    break;
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private async Task HandleClientWithSlot(TcpClient client, CancellationToken serverToken)
        {
            try
            {
                await HandleClient(client, serverToken);
            }
            finally
            {
                client.Dispose();
                _clientSlots.Release();
            }
        }

        // ── HTTP request boundary ─────────────────────────────────────────────────────────────
        // This is intentionally a small HTTP/1.0-1.1 subset: one request per connection, exact
        // Host header parsing, no transfer encoding, bounded headers/body, and a real async deadline.

        private async Task HandleClient(TcpClient client, CancellationToken serverToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            deadline.CancelAfter(RequestTimeout);
            CancellationToken requestToken = deadline.Token;

            try
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[MaxHeaderBytes + 4];
                (int headerEnd, int bytesRead, bool tooLarge) = await ReadHeaderAsync(stream, buffer, requestToken);
                if (tooLarge)
                {
                    Respond(stream, 431, "text/plain", Utf8("request headers too large"), false);
                    return;
                }
                if (headerEnd < 0)
                {
                    if (bytesRead > 0)
                        Respond(stream, 400, "text/plain", Utf8("bad request"), false);
                    return;
                }

                if (!TryParseRequestHead(buffer, headerEnd, out RequestHead? request) || request == null)
                {
                    Respond(stream, 400, "text/plain", Utf8("bad request"), false);
                    return;
                }

                if (!request.Headers.TryGetValue("Host", out string? hostHeader) ||
                    !TryNormalizeRequestHost(hostHeader, out string host))
                {
                    Respond(stream, 400, "text/plain", Utf8("bad request"), false);
                    return;
                }

                if (!IsKnownRequestHost(host))
                {
                    Respond(stream, 404, "text/plain", Utf8("not found"), request.Method == "HEAD");
                    return;
                }

                if (request.Headers.ContainsKey("Transfer-Encoding"))
                {
                    Respond(stream, 400, "text/plain", Utf8("bad request"), false);
                    return;
                }

                if (!TryDecodeRequestTarget(request.Target, out string pathOnly))
                {
                    Respond(stream, 403, "text/plain", Utf8("forbidden"), request.Method == "HEAD");
                    return;
                }

                switch (request.Method)
                {
                    case "GET":
                    case "HEAD":
                        if (request.Headers.TryGetValue("Content-Length", out string? getLength) && getLength != "0")
                        {
                            Respond(stream, 400, "text/plain", Utf8("bad request"), request.Method == "HEAD");
                            return;
                        }
                        await HandleReadRequest(stream, host, pathOnly, request.Method == "HEAD", requestToken);
                        return;

                    case "POST":
                        if (pathOnly is "/api/aurora/position-session" or "/api/aurora/positions")
                        {
                            await HandleNativePositionPost(stream, request, host, pathOnly, buffer, headerEnd, bytesRead, requestToken);
                            return;
                        }
                        if (!pathOnly.StartsWith("/api/edits/", StringComparison.OrdinalIgnoreCase))
                        {
                            Respond(stream, 405, "text/plain", Utf8("method not allowed"), false, "GET, HEAD");
                            return;
                        }
                        if (_editWriteRoot == null)
                        {
                            Respond(stream, 404, "application/json", Utf8("{\"ok\":false,\"error\":\"not found\"}"), false);
                            return;
                        }
                        if (!IsAllowedOrigin(request, host))
                        {
                            Respond(stream, 403, "application/json", Utf8("{\"ok\":false,\"error\":\"forbidden\"}"), false);
                            return;
                        }
                        if (!HasJsonContentType(request))
                        {
                            Respond(stream, 415, "application/json", Utf8("{\"ok\":false,\"error\":\"application/json required\"}"), false);
                            return;
                        }
                        await HandleEditsPost(stream, request, pathOnly, _editWriteRoot, buffer, headerEnd,
                            bytesRead, requestToken);
                        return;

                    default:
                        Respond(stream, 405, "text/plain", Utf8("method not allowed"), false, "GET, HEAD, POST");
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                // Deadline or shutdown closes the connection without disclosing internal state.
            }
            catch
            {
                // A malformed client or I/O failure must never terminate the editor or reveal paths.
            }
        }

        private async Task HandleReadRequest(NetworkStream stream, string host, string pathOnly,
            bool isHead, CancellationToken cancellationToken)
        {
            if (string.Equals(pathOnly, "/health", StringComparison.OrdinalIgnoreCase))
            {
                byte[] health = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    status = "ok",
                    port = Port,
                    uptimeMs = Environment.TickCount64 - _startedAt,
                    hosts = Hosts.Keys
                });
                Respond(stream, 200, "application/json", health, isHead);
                return;
            }

            if (await TryServeExactOwnerAsync(stream, pathOnly, isHead, cancellationToken))
                return;

            if (!TryResolveRoot(host, pathOnly, out string? root, out string relativePath) || root == null)
            {
                Respond(stream, 404, "text/plain", Utf8("not found"), isHead);
                return;
            }

            await ServeFile(stream, root, relativePath, pathOnly, isHead, cancellationToken);
        }

        private static async Task<(int HeaderEnd, int BytesRead, bool TooLarge)> ReadHeaderAsync(
            NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
                if (count <= 0)
                    break;
                read += count;
                int headerEnd = IndexOfHeaderEnd(buffer, read);
                if (headerEnd >= 0)
                {
                    if (headerEnd + 4 > MaxHeaderBytes)
                        return (-1, read, true);
                    return (headerEnd, read, false);
                }
            }
            return (-1, read, read >= buffer.Length);
        }

        private static bool TryParseRequestHead(byte[] buffer, int headerEnd, out RequestHead? request)
        {
            request = null;
            string text = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            string[] lines = text.Split("\r\n", StringSplitOptions.None);
            if (lines.Length == 0)
                return false;

            string[] requestParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length != 3 || requestParts[1].Length is 0 or > MaxRequestTargetChars ||
                (requestParts[2] != "HTTP/1.1" && requestParts[2] != "HTTP/1.0"))
                return false;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    return false;

                string name = line.Substring(0, colon);
                string value = line.Substring(colon + 1).Trim();
                if (!IsHeaderName(name) || value.Any(ch => ch is '\r' or '\n' or '\0') ||
                    !headers.TryAdd(name, value))
                    return false;
            }

            request = new RequestHead(requestParts[0].ToUpperInvariant(), requestParts[1], headers);
            return true;
        }

        private static bool IsHeaderName(string name)
        {
            if (name.Length == 0)
                return false;
            foreach (char ch in name)
            {
                if (!(char.IsAsciiLetterOrDigit(ch) || ch is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or
                    '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                    return false;
            }
            return true;
        }

        // ── Mutable edit route ────────────────────────────────────────────────────────────────
        // This remains the sole write route for compatibility with the NoClip viewer. It is exact,
        // JSON-only, host-scoped, bounded, rejects cross-origin browser requests, and never reflects
        // an exception or filesystem path. No generic file-write route is exposed.

        private async Task HandleNativePositionPost(NetworkStream stream, RequestHead request, string host, string path,
            byte[] buffer, int headerEnd, int bytesRead, CancellationToken cancellationToken)
        {
            var route = Volatile.Read(ref _nativePositionRoute);
            if (route == null || !IsAllowedOrigin(request, host) ||
                !request.Headers.TryGetValue("X-FFX-Studio-Token", out string? token) || token.Length != route.Token.Length ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(route.Token)))
            {
                Respond(stream, 403, "application/json", Utf8(JsonSerializer.Serialize(new { ok = false, message = Strings.U_Noclip_Reopen })), false);
                return;
            }
            if (!HasJsonContentType(request))
            {
                Respond(stream, 415, "application/json", Utf8("{\"ok\":false,\"message\":\"application/json required\"}"), false);
                return;
            }
            if (!request.Headers.TryGetValue("Content-Length", out string? lengthText) ||
                !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out int length) || length <= 0 || length > MaxEditBodyBytes)
            {
                Respond(stream, 400, "application/json", Utf8("{\"ok\":false,\"message\":\"invalid body length\"}"), false);
                return;
            }
            int start = headerEnd + 4;
            int received = Math.Max(0, bytesRead - start);
            if (received > length)
            {
                Respond(stream, 400, "application/json", Utf8("{\"ok\":false,\"message\":\"invalid body\"}"), false);
                return;
            }
            try
            {
                byte[] body = new byte[length];
                if (received > 0) Array.Copy(buffer, start, body, 0, received);
                while (received < length)
                {
                    int count = await stream.ReadAsync(body.AsMemory(received, length - received), cancellationToken);
                    if (count <= 0) throw new InvalidDataException("Truncated position request");
                    received += count;
                }
                // The callback validates project, battle and revision and only returns after the write.
                var result = await route.Handler(path.Substring("/api/aurora".Length), Encoding.UTF8.GetString(body));
                Respond(stream, result.Ok ? 200 : 409, "application/json", Utf8(result.Json), false);
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error("aurora-chamber.NativeSave", "Native position request failed", ex);
                Respond(stream, 400, "application/json", Utf8(JsonSerializer.Serialize(new { ok = false, message = Strings.U_Noclip_SaveError })), false);
            }
        }

        private async Task HandleEditsPost(NetworkStream stream, RequestHead request, string pathOnly,
            string editWriteRoot, byte[] buffer, int headerEnd, int bytesRead, CancellationToken cancellationToken)
        {
            try
            {
                string encId = pathOnly.Substring("/api/edits/".Length);
                if (!NoclipOverlayStore.TryNormalizeEncounterId(encId, out string canonicalEncId))
                {
                    Respond(stream, 400, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"" + Strings.U_Vh_EncIdInvalid + "\"}"), false);
                    return;
                }
                encId = canonicalEncId;

                if (!request.Headers.TryGetValue("Content-Length", out string? lengthText) ||
                    !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out int contentLength) ||
                    contentLength <= 0)
                {
                    Respond(stream, 400, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"body required\"}"), false);
                    return;
                }
                if (contentLength > MaxEditBodyBytes)
                {
                    Respond(stream, 413, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"body too large\"}"), false);
                    return;
                }

                int bodyStart = headerEnd + 4;
                int bufferedBody = Math.Max(0, bytesRead - bodyStart);
                if (bufferedBody > contentLength)
                {
                    Respond(stream, 400, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"bad request\"}"), false);
                    return;
                }

                byte[] body = new byte[contentLength];
                if (bufferedBody > 0)
                    Array.Copy(buffer, bodyStart, body, 0, bufferedBody);
                int received = bufferedBody;
                while (received < contentLength)
                {
                    int count = await stream.ReadAsync(body.AsMemory(received, contentLength - received), cancellationToken);
                    if (count <= 0)
                        break;
                    received += count;
                }
                if (received != contentLength)
                {
                    Respond(stream, 400, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"truncated body\"}"), false);
                    return;
                }

                if (!TryParseEdit(body, out int slot, out double? dx, out double? dy, out double? dz,
                    out double? heading, out double? scale))
                {
                    Respond(stream, 400, "application/json",
                        Utf8("{\"ok\":false,\"error\":\"invalid edit payload\"}"), false);
                    return;
                }

                await _editWriteLock.WaitAsync(cancellationToken);
                try
                {
                    string stagedEdit = NoclipOverlayStore.WriteEditToRoot(
                        editWriteRoot,
                        encId,
                        slot,
                        dx,
                        dy,
                        dz,
                        heading,
                        scale);
                    if (!TryMapExactFile(
                        $"/data/FinalFantasyX/edits/{encId}.json",
                        stagedEdit))
                        throw new IOException("The local NoClip edit overlay could not be registered.");
                }
                finally
                {
                    _editWriteLock.Release();
                }

                Respond(stream, 200, "application/json",
                    Utf8($"{{\"ok\":true,\"file\":\"{encId}.json\",\"slot\":{slot}}}"), false);
            }
            catch (JsonException)
            {
                Respond(stream, 400, "application/json",
                    Utf8("{\"ok\":false,\"error\":\"" + Strings.U_Vh_JsonInvalid + "\"}"), false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                Respond(stream, 500, "application/json",
                    Utf8("{\"ok\":false,\"error\":\"internal error\"}"), false);
            }
        }

        private static bool TryParseEdit(byte[] body, out int slot, out double? dx, out double? dy,
            out double? dz, out double? heading, out double? scale)
        {
            slot = -1;
            dx = dy = dz = heading = scale = null;

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("slot", out JsonElement slotElement) ||
                slotElement.ValueKind != JsonValueKind.Number || !slotElement.TryGetInt32(out slot) ||
                slot is < 0 or > 7)
                return false;

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("slot" or "position" or "heading" or "scale"))
                    return false;
            }

            if (root.TryGetProperty("position", out JsonElement position))
            {
                if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 3 ||
                    !TryReadFiniteNumber(position[0], 10_000_000, out double x) ||
                    !TryReadFiniteNumber(position[1], 10_000_000, out double y) ||
                    !TryReadFiniteNumber(position[2], 10_000_000, out double z))
                    return false;
                dx = x;
                dy = y;
                dz = z;
            }

            if (root.TryGetProperty("heading", out JsonElement headingElement))
            {
                if (!TryReadFiniteNumber(headingElement, 1_000_000, out double value))
                    return false;
                heading = value;
            }

            if (root.TryGetProperty("scale", out JsonElement scaleElement))
            {
                if (!TryReadFiniteNumber(scaleElement, 1000, out double value) || value <= 0)
                    return false;
                scale = value;
            }

            return true;
        }

        private static bool TryReadFiniteNumber(JsonElement element, double maxAbsolute, out double value)
        {
            value = 0;
            return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) &&
                double.IsFinite(value) && Math.Abs(value) <= maxAbsolute;
        }

        private static bool HasJsonContentType(RequestHead request)
        {
            if (!request.Headers.TryGetValue("Content-Type", out string? contentType))
                return false;
            int semicolon = contentType.IndexOf(';');
            string mediaType = (semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType).Trim();
            return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAllowedOrigin(RequestHead request, string requestHost)
        {
            if (!request.Headers.TryGetValue("Origin", out string? originText))
                return false;
            if (!IsLoopbackHost(requestHost))
                return false;

            string expectedOrigin = $"http://{requestHost}:{Port}";
            if (!string.Equals(originText, expectedOrigin, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!Uri.TryCreate(originText, UriKind.Absolute, out Uri? origin) ||
                !string.Equals(origin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                origin.UserInfo.Length != 0 || origin.Port != Port ||
                !string.Equals(origin.AbsolutePath, "/", StringComparison.Ordinal) ||
                origin.Query.Length != 0 || origin.Fragment.Length != 0)
                return false;

            string originHost = origin.IdnHost.TrimEnd('.').ToLowerInvariant();
            return string.Equals(originHost, requestHost, StringComparison.OrdinalIgnoreCase);
        }

        // ── Route resolution ──────────────────────────────────────────────────────────────────

        private bool TryResolveRoot(string host, string path, out string? root, out string relativePath)
        {
            lock (_routeLock)
            {
                foreach (KeyValuePair<string, string> pair in _prefixRoots)
                {
                    if (pair.Key == "/")
                    {
                        root = pair.Value;
                        relativePath = path.TrimStart('/');
                        return true;
                    }

                    if (string.Equals(path, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        root = pair.Value;
                        relativePath = string.Empty;
                        return true;
                    }

                    string boundary = pair.Key + "/";
                    if (path.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
                    {
                        root = pair.Value;
                        relativePath = path.Substring(boundary.Length);
                        return true;
                    }
                }

                if (_hostRoots.TryGetValue(host, out string? hostRoot))
                {
                    root = hostRoot;
                    relativePath = path.TrimStart('/');
                    return true;
                }

                if (IsLoopbackHost(host) && _hostRoots.Count == 1)
                {
                    KeyValuePair<string, string> onlyHost = _hostRoots.First();
                    root = onlyHost.Value;
                    relativePath = path.TrimStart('/');
                    return true;
                }
            }

            root = null;
            relativePath = string.Empty;
            return false;
        }

        private bool IsKnownRequestHost(string host)
        {
            if (IsLoopbackHost(host))
                return true;
            lock (_routeLock)
                return _hostRoots.ContainsKey(host);
        }

        private bool TryNormalizeRequestHost(string value, out string host)
        {
            host = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
                !Uri.TryCreate("http://" + value + "/", UriKind.Absolute, out Uri? uri) ||
                uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                return false;

            if (HasExplicitPort(value) && uri.Port != Port)
                return false;

            host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            return host.Length > 0;
        }

        private static bool HasExplicitPort(string authority)
        {
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                int closingBracket = authority.IndexOf(']');
                return closingBracket >= 0 && closingBracket + 1 < authority.Length &&
                    authority[closingBracket + 1] == ':';
            }
            return authority.Contains(':');
        }

        private static bool IsLoopbackHost(string host) =>
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeMappedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is required.", nameof(host));

            string normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (normalized.Length == 0 || normalized.Any(char.IsWhiteSpace) ||
                normalized.IndexOfAny(new[] { '/', '\\', ':', '@', '?', '#' }) >= 0)
                throw new ArgumentException("Host must not include a scheme, port, or path.", nameof(host));
            return normalized;
        }

        private static string NormalizeMappedPrefix(string prefix)
        {
            string candidate = string.IsNullOrWhiteSpace(prefix) ? "/" : prefix.Trim();
            if (!candidate.StartsWith('/'))
                candidate = "/" + candidate;
            if (candidate.IndexOfAny(new[] { '\\', ':', '?', '#', '%' }) >= 0)
                throw new ArgumentException("Prefix contains an unsafe URL character.", nameof(prefix));

            var segments = new List<string>();
            foreach (string segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment is "." or "..")
                    throw new ArgumentException("Prefix contains a traversal segment.", nameof(prefix));
                segments.Add(segment);
            }
            return segments.Count == 0 ? "/" : "/" + string.Join('/', segments);
        }

        private static bool TryNormalizeMappedExactPath(string path, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || path.Length > MaxRequestTargetChars ||
                !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) ||
                path.EndsWith('/') || path.IndexOfAny(new[] { '\\', ':', '?', '#', '%', '\0' }) >= 0 ||
                path.Any(char.IsControl))
                return false;

            string[] segments = path.Split('/', StringSplitOptions.None);
            if (segments.Length < 2 || segments.Skip(1).Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
                return false;

            normalizedPath = path;
            return true;
        }

        private static bool TryDecodeRequestTarget(string target, out string decodedPath)
        {
            decodedPath = string.Empty;
            if (target.Length == 0 || target.Length > MaxRequestTargetChars || !target.StartsWith('/') ||
                target.StartsWith("//", StringComparison.Ordinal) || target.Contains('#'))
                return false;

            int query = target.IndexOf('?');
            string rawPath = query >= 0 ? target.Substring(0, query) : target;
            if (rawPath.Contains('\\') || rawPath.Contains('\0'))
                return false;

            for (int i = 0; i < rawPath.Length; i++)
            {
                if (rawPath[i] != '%')
                    continue;
                if (i + 2 >= rawPath.Length || !IsHex(rawPath[i + 1]) || !IsHex(rawPath[i + 2]))
                    return false;
                int value = (HexValue(rawPath[i + 1]) << 4) | HexValue(rawPath[i + 2]);
                if (value is (byte)'.' or (byte)'/' or (byte)'\\' or (byte)':' or (byte)'%')
                    return false;
                i += 2;
            }

            try
            {
                decodedPath = Uri.UnescapeDataString(rawPath);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!decodedPath.StartsWith('/') || decodedPath.Contains('\\') || decodedPath.Contains(':') ||
                decodedPath.Contains('\0') || decodedPath.Any(ch => char.IsControl(ch)))
                return false;

            foreach (string segment in decodedPath.Split('/', StringSplitOptions.None))
            {
                if (segment is "." or "..")
                    return false;
            }
            return true;
        }

        private static bool IsHex(char value) =>
            value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static int HexValue(char value) => value <= '9'
            ? value - '0'
            : char.ToLowerInvariant(value) - 'a' + 10;

        // ── Static file containment and response ─────────────────────────────────────────────
        // Path.GetRelativePath provides a segment boundary check that textual StartsWith cannot.
        // Every existing path component, including the mapped root, must be free of reparse points.

        private async Task ServeFile(NetworkStream network, string root, string relativePath,
            string requestPath, bool isHead, CancellationToken cancellationToken)
        {
            if (relativePath.Length == 0)
                relativePath = "index.html";
            if (!TryBuildContainedPath(root, relativePath, out string full))
            {
                Respond(network, 403, "text/plain", Utf8("forbidden"), isHead);
                return;
            }

            if (Directory.Exists(full))
            {
                string directoryIndex = Path.Combine(relativePath, "index.html");
                if (!TryBuildContainedPath(root, directoryIndex, out full))
                {
                    Respond(network, 403, "text/plain", Utf8("forbidden"), isHead);
                    return;
                }
            }

            if (!TryBuildContainedPath(root, Path.GetRelativePath(Path.GetFullPath(root), full), out full))
            {
                Respond(network, 403, "text/plain", Utf8("forbidden"), isHead);
                return;
            }

            try
            {
                FileSystemReparseGuard.VerifiedOpenResult openResult =
                    NoclipHexDataRead.TryOpen(
                        root, full, requestPath,
                        out FileSystemReparseGuard.VerifiedReadFile? verifiedFile);
                if (openResult == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
                {
                    // Managed fetch-through: a genuinely absent file under the bootstrap root is
                    // pulled from the pinned CDN on demand (clean-machine noclip bootstrap).
                    // Case mismatches are owned by TryOpen's bounded ASCII-hex rule
                    // (00ef.bin ↔ 00EF.BIN) — this only runs when the file is truly absent.
                    string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                    string? fetchRoot = _noclipFetchThroughDataRoot;
                    if (fetchRoot != null &&
                        IsSameCanonicalPath(canonicalRoot, fetchRoot) &&
                        await TryFetchNoclipDataAsync(root, relativePath, cancellationToken))
                    {
                        openResult = NoclipHexDataRead.TryOpen(
                            root, full, requestPath, out verifiedFile);
                    }
                }
                if (openResult == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
                {
                    Respond(network, 404, "text/plain", Utf8("not found"), isHead);
                    return;
                }
                if (openResult != FileSystemReparseGuard.VerifiedOpenResult.Success || verifiedFile == null)
                {
                    Respond(network, 403, "text/plain", Utf8("forbidden"), isHead);
                    return;
                }
                using (verifiedFile)
                    await ServeOpenedFile(network, verifiedFile, full, isHead, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                Respond(network, 403, "text/plain", Utf8("forbidden"), isHead);
            }
            catch (IOException)
            {
                Respond(network, 404, "text/plain", Utf8("not found"), isHead);
            }
        }

        private static async Task ServeOpenedFile(
            NetworkStream network,
            FileSystemReparseGuard.VerifiedReadFile verifiedFile,
            string contentPath,
            bool isHead,
            CancellationToken cancellationToken)
        {
            FileStream file = verifiedFile.Stream;
            if (file.Length > MaxStaticFileBytes)
            {
                Respond(network, 413, "text/plain", Utf8("file too large"), isHead);
                return;
            }

            WriteResponseHeaders(network, 200, MimeFor(contentPath), file.Length, null);
            if (!isHead)
                await file.CopyToAsync(network, 64 * 1024, cancellationToken);
            await network.FlushAsync(cancellationToken);
        }

        private static bool TryBuildContainedPath(string root, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrEmpty(root) || Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
                relativePath.Contains('\0'))
                return false;

            try
            {
                string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
                string relative = Path.GetRelativePath(canonicalRoot, candidate);
                if (Path.IsPathRooted(relative) || relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                    ContainsReparsePoint(canonicalRoot, candidate))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool ContainsReparsePoint(string canonicalRoot, string candidate)
        {
            if (IsExistingReparsePoint(canonicalRoot))
                return true;

            string relative = Path.GetRelativePath(canonicalRoot, candidate);
            string current = canonicalRoot;
            foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (IsExistingReparsePoint(current))
                    return true;
            }
            return false;
        }

        private static bool IsExistingReparsePoint(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        // ── NoClip CDN fetch-through ─────────────────────────────────────────────────────────
        // Only FinalFantasyX/<2-hex-dir>/<name>.bin and the three root bins are fetchable. The
        // segment shape check keeps /data/FinalFantasyX/edits/ (the local overlay area) and any
        // other directory permanently unfetchable regardless of what exists upstream.

        private static bool IsSameCanonicalPath(string a, string b) => string.Equals(
            a, b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        internal static bool TryBuildNoclipCdnRelativePath(string relativePath, out string cdnRelative)
        {
            cdnRelative = string.Empty;
            const string prefix = "FinalFantasyX/";
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string rest = relativePath.Substring(prefix.Length);
            if (rest is "common_textures.bin" or "screen_shatter.bin" or "env_map_texture.bin")
            {
                cdnRelative = rest;
                return true;
            }

            int slash = rest.IndexOf('/');
            if (slash != 2 || !IsHexSegment(rest.AsSpan(0, 2)))
                return false;

            string name = rest.Substring(3);
            if (name.Length is 0 or > 68 || name.IndexOf('/') >= 0 ||
                !name.EndsWith(".bin", StringComparison.Ordinal))
                return false;
            foreach (char ch in name.AsSpan(0, name.Length - 4))
            {
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                    return false;
            }

            cdnRelative = rest;
            return true;
        }

        private static bool IsHexSegment(ReadOnlySpan<char> segment)
        {
            if (segment.Length != 2)
                return false;
            foreach (char ch in segment)
            {
                if (!(ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                    return false;
            }
            return true;
        }

        private async Task<bool> TryFetchNoclipDataAsync(
            string root,
            string relativePath,
            CancellationToken cancellationToken)
        {
            if (!TryBuildNoclipCdnRelativePath(relativePath, out string cdnRelative))
                return false;
            if (_noclipFetchMisses.ContainsKey(cdnRelative))
                return false;

            SemaphoreSlim gate = _noclipFetchLocks.GetOrAdd(cdnRelative, _ => new SemaphoreSlim(1, 1));
            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                string destination = Path.Combine(
                    root,
                    string.Join(Path.DirectorySeparatorChar, relativePath.Split('/')));
                if (File.Exists(destination))
                    return true;

                using HttpResponseMessage response = await NoclipFetchClient
                    .GetAsync(
                        NoclipDataBootstrap.CdnBase + "/" + cdnRelative,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _noclipFetchMisses.TryAdd(cdnRelative, 0);
                    return false;
                }
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentLength > MaxStaticFileBytes)
                    return false;

                string? directory = Path.GetDirectoryName(destination);
                if (directory == null)
                    return false;
                Directory.CreateDirectory(directory);

                string staging = destination + ".fetch-tmp";
                long total = 0;
                try
                {
                    // Both streams must be disposed before File.Move: on Windows a handle opened
                    // without FILE_SHARE_DELETE makes MoveFileEx fail with ERROR_SHARING_VIOLATION.
                    // (POSIX allows rename of open files, which is why this only broke on Windows.)
                    await using (Stream source = await response.Content
                        .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (FileStream target = new(
                        staging, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        while ((read = await source.ReadAsync(
                            buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > MaxStaticFileBytes)
                                return false;
                            await target.WriteAsync(
                                buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        }

                        target.Flush();
                    }

                    File.Move(staging, destination, overwrite: true);
                    FFXProjectEditor.Diagnostics.DebugLog.Info(
                        "Hub.NoclipFetch", $"fetched {cdnRelative} ({total} bytes)");
                    return true;
                }
                finally
                {
                    try { if (File.Exists(staging)) File.Delete(staging); } catch { }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        private static HttpClient CreateNoclipFetchClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            // The noclip CDN (Cloudflare) rejects the default .NET User-Agent with 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            return client;
        }

        private static void Respond(NetworkStream stream, int status, string contentType, byte[] body,
            bool isHead, string? allow = null)
        {
            WriteResponseHeaders(stream, status, contentType, body.LongLength, allow);
            if (!isHead && body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static void WriteResponseHeaders(NetworkStream stream, int status, string contentType,
            long contentLength, string? allow)
        {
            string reason = status switch
            {
                200 => "OK",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                413 => "Content Too Large",
                415 => "Unsupported Media Type",
                431 => "Request Header Fields Too Large",
                500 => "Internal Server Error",
                503 => "Service Unavailable",
                _ => "Error"
            };

            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            header.Append("Content-Type: ").Append(WithCharset(contentType)).Append("\r\n");
            header.Append("Content-Length: ").Append(contentLength.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            header.Append("Connection: close\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("X-Content-Type-Options: nosniff\r\n");
            header.Append("Referrer-Policy: no-referrer\r\n");
            header.Append("X-Frame-Options: DENY\r\n");
            header.Append("Cross-Origin-Resource-Policy: same-origin\r\n");
            header.Append("Content-Security-Policy: default-src 'self' data: blob:; connect-src 'self'; img-src 'self' data: blob:; media-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' blob:; worker-src 'self' blob:; child-src 'self' blob:; frame-src 'self' blob:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n");
            if (!string.IsNullOrEmpty(allow))
                header.Append("Allow: ").Append(allow).Append("\r\n");
            header.Append("\r\n");

            byte[] bytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string WithCharset(string contentType) =>
            contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
                ? contentType + "; charset=utf-8"
                : contentType;

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

        private static int IndexOfHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i < length - 3; i++)
            {
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
                    return i;
            }
            return -1;
        }

        private static readonly Dictionary<string, string> MimeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html", [".htm"] = "text/html", [".js"] = "text/javascript", [".mjs"] = "text/javascript",
            [".css"] = "text/css", [".json"] = "application/json", [".png"] = "image/png", [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon", [".wasm"] = "application/wasm", [".bin"] = "application/octet-stream",
            [".dat"] = "application/octet-stream", [".dds"] = "application/octet-stream", [".chr"] = "application/octet-stream",
            [".mgrp"] = "application/octet-stream", [".vbf"] = "application/octet-stream", [".bmp"] = "image/bmp",
            [".ttf"] = "font/ttf", [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".mp3"] = "audio/mpeg",
            [".ogg"] = "audio/ogg", [".wav"] = "audio/wav", [".txt"] = "text/plain", [".xml"] = "application/xml",
            [".glb"] = "model/gltf-binary", [".gltf"] = "model/gltf+json", [".map"] = "application/octet-stream"
        };

        private static string MimeFor(string path)
        {
            string extension = Path.GetExtension(path);
            return MimeMap.TryGetValue(extension, out string? mime) ? mime : "application/octet-stream";
        }

        private sealed class RequestHead
        {
            public RequestHead(string method, string target, Dictionary<string, string> headers)
            {
                Method = method;
                Target = target;
                Headers = headers;
            }

            public string Method { get; }
            public string Target { get; }
            public Dictionary<string, string> Headers { get; }
        }
    }
}
