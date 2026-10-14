using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    // 🌅 Drag-to-place bridge — the missing web→editor leg.
    //
    // The MapViewer is served by a STATIC python server (one-way C#→web). This adds the reverse leg so the viewer
    // can POST a monster anchor's NEW X/Y/Z back when the user drags it. We use a raw TcpListener on an OS-assigned
    // FREE loopback port (bind port 0) — NOT HttpListener — for two reasons proven on this box: (1) HttpListener
    // needs a urlacl/admin reservation; (2) there is a swarm of stray `python -m http.server` instances squatting
    // 8765-89xx, so a fixed port collides (the browser POST hit a python server → "501 Unsupported method POST" →
    // the editor never saw the drag → "não salva"). Port 0 sidesteps both. We speak just enough HTTP/1.1 to answer
    // the CORS preflight + POST /drag. The chosen Port is handed to the viewer via the deep-link (&drag=<port>).
    internal static class AuroraDragBridge
    {
        public static int Port { get; private set; }   // 0 until started
        public static string RequestToken { get; private set; } = string.Empty;
        public readonly record struct Coord(float X, float Y, float Z);

        // Raised (on a background thread) when the viewer's overlay clicks its own "Salvar posições" button.
        // The Aurora data model subscribes and marshals the actual write to the UI thread.
        public static Action<string>? SaveRequested;

        private static readonly object Gate = new();
        private static TcpListener? _listener;
        private static CancellationTokenSource? _shutdown;
        private static long _listenerGeneration;
        private static string? _allowedOrigin;
        internal const int MaxConcurrentClients = 4;
        private static readonly SemaphoreSlim ClientSlots = new(MaxConcurrentClients, MaxConcurrentClients);
        private static readonly ConcurrentDictionary<TcpClient, byte> ActiveClients = new();
        internal static Func<TcpClient, Task>? AfterAcceptBeforeRegistrationForTests { get; set; }
        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 64 * 1024;

        // battleId -> ("role|index" -> coord)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Coord>> Drags =
            new(StringComparer.Ordinal);

        public static bool IsRunning => _listener != null && Port != 0;

        /// <summary>Start the loopback listener once on a free OS-assigned port. Best-effort.</summary>
        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_listener != null) return;
                try
                {
                    TcpListener l = new(IPAddress.Loopback, 0); // 0 => OS picks a free port (no collision, no admin)
                    l.Start();
                    Port = ((IPEndPoint)l.LocalEndpoint).Port;
                    RequestToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    var shutdown = new CancellationTokenSource();
                    long generation = ++_listenerGeneration;
                    _shutdown = shutdown;
                    _listener = l;
                    _ = Task.Run(() => AcceptLoopAsync(l, shutdown.Token, generation));
                }
                catch
                {
                    _shutdown?.Dispose();
                    _shutdown = null;
                    _listener = null;
                    Port = 0;
                    RequestToken = string.Empty;
                }
            }
        }

        /// <summary>Pins the only browser origin allowed to call the write bridge.</summary>
        public static bool SetAllowedOrigin(string viewerUrl)
        {
            if (!Uri.TryCreate(viewerUrl, UriKind.Absolute, out Uri? uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsLoopback ||
                !string.IsNullOrEmpty(uri.UserInfo))
                return false;

            _allowedOrigin = uri.GetLeftPart(UriPartial.Authority);
            return true;
        }

        public static void Stop()
        {
            lock (Gate)
            {
                _listenerGeneration++;
                try { _shutdown?.Cancel(); } catch { }
                try { _listener?.Stop(); } catch { }
                foreach (TcpClient client in ActiveClients.Keys)
                    try { client.Dispose(); } catch { }
                _shutdown?.Dispose();
                _shutdown = null;
                _listener = null;
                Port = 0;
                RequestToken = string.Empty;
                _allowedOrigin = null;
            }
        }

        private static async Task AcceptLoopAsync(
            TcpListener l,
            CancellationToken cancellationToken,
            long generation)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool ownsSlot = false;
                TcpClient? client = null;
                try
                {
                    // Capacity is acquired before accept/spawn, so slow clients cannot create an
                    // unbounded queue of accepted sockets or tasks waiting on the semaphore.
                    await ClientSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                    ownsSlot = true;
                    client = await l.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    if (AfterAcceptBeforeRegistrationForTests is { } afterAccept)
                        await afterAccept(client).ConfigureAwait(false);

                    TcpClient accepted;
                    lock (Gate)
                    {
                        // Stop and post-accept registration share this generation handshake. If
                        // Stop won the lock first, the stale socket is never made live; if this
                        // registration won first, Stop sees it in ActiveClients and disposes it.
                        if (generation != _listenerGeneration ||
                            !ReferenceEquals(_listener, l) ||
                            cancellationToken.IsCancellationRequested)
                        {
                            client.Dispose();
                            client = null;
                            break;
                        }

                        ActiveClients[client] = 0;
                        accepted = client;
                        client = null;
                        ownsSlot = false;
                    }
                    _ = Task.Run(() => HandleClientWithSlot(accepted), CancellationToken.None);
                }
                catch (OperationCanceledException) { client?.Dispose(); break; }
                catch (ObjectDisposedException) { client?.Dispose(); break; }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    client?.Dispose();
                    break;
                }
                catch { client?.Dispose(); }
                finally { if (ownsSlot) ClientSlots.Release(); }
            }
        }

        private static void HandleClientWithSlot(TcpClient client)
        {
            try { HandleClient(client); }
            catch { /* one bad request never kills the loop */ }
            finally
            {
                ActiveClients.TryRemove(client, out _);
                client.Dispose();
                ClientSlots.Release();
            }
        }

        internal static int ActiveClientCountForTests => ActiveClients.Count;

        internal static async Task WaitForActiveClientCountForTests(int expected, TimeSpan timeout)
        {
            long deadline = Environment.TickCount64 + checked((long)timeout.TotalMilliseconds);
            while (ActiveClients.Count != expected && Environment.TickCount64 < deadline)
                await Task.Delay(20).ConfigureAwait(false);
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
                // --- read request headers (until CRLFCRLF) ---
                client.ReceiveTimeout = 10_000;
                client.SendTimeout = 10_000;
                List<byte> buf = new();
                int b, headerEnd = -1;
                while ((b = ns.ReadByte()) != -1)
                {
                    buf.Add((byte)b);
                    int n = buf.Count;
                    if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10) { headerEnd = n; break; }
                    if (n > MaxHeaderBytes) break; // runaway header guard
                }
                if (headerEnd < 0) return;

                string[] lines = Encoding.ASCII.GetString(buf.ToArray(), 0, headerEnd).Split("\r\n");
                string[] requestLine = (lines.Length > 0 ? lines[0] : "").Split(' ');
                string method = requestLine.Length > 0 ? requestLine[0].ToUpperInvariant() : "";
                string requestTarget = requestLine.Length > 1 ? requestLine[1] : "";
                string path = Uri.TryCreate("http://bridge.invalid" + requestTarget, UriKind.Absolute, out Uri? parsedTarget)
                    ? parsedTarget.AbsolutePath
                    : string.Empty;

                int contentLength = 0;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines)
                {
                    int ci = line.IndexOf(':');
                    if (ci <= 0) continue;
                    string name = line.Substring(0, ci).Trim();
                    string value = line.Substring(ci + 1).Trim();
                    if (!headers.TryAdd(name, value))
                    {
                        WriteResponse(ns, 400, "bad request", null);
                        return;
                    }
                }

                if (headers.TryGetValue("Content-Length", out string? lengthText) &&
                    (!int.TryParse(lengthText, out contentLength) || contentLength < 0 || contentLength > MaxBodyBytes))
                {
                    WriteResponse(ns, 413, "payload too large", null);
                    return;
                }

                if (!IsAuthorized(method, path, headers, out string? responseOrigin))
                {
                    WriteResponse(ns, 403, "forbidden", null);
                    return;
                }

                // --- read body (Content-Length bytes) ---
                string body = "";
                if (contentLength > 0)
                {
                    byte[] bodyBuf = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int r = ns.Read(bodyBuf, read, contentLength - read);
                        if (r <= 0) break;
                        read += r;
                    }
                    body = Encoding.UTF8.GetString(bodyBuf, 0, read);
                }

                if (method == "OPTIONS") { WriteResponse(ns, 204, "", responseOrigin); return; }

                if (!headers.TryGetValue("Content-Type", out string? contentType) ||
                    !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(ns, 415, "application/json required", responseOrigin);
                    return;
                }

                if (method == "POST" && string.Equals(path, "/save", StringComparison.Ordinal))
                {
                    string battleId = "";
                    try { using JsonDocument doc = JsonDocument.Parse(body); battleId = doc.RootElement.GetProperty("battleId").GetString() ?? ""; }
                    catch { WriteResponse(ns, 400, "bad json", responseOrigin); return; }
                    if (!IsValidIdentifier(battleId, 64))
                    {
                        WriteResponse(ns, 400, "bad battle id", responseOrigin);
                        return;
                    }
                    try { SaveRequested?.Invoke(battleId); } catch { }
                    WriteResponse(ns, 200, "save-requested", responseOrigin);
                    return;
                }

                if (method == "POST" && string.Equals(path, "/drag", StringComparison.Ordinal))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(body);
                        JsonElement e = doc.RootElement;
                        string battleId = e.GetProperty("battleId").GetString() ?? "";
                        string role = e.GetProperty("role").GetString() ?? "";
                        int index = e.GetProperty("index").GetInt32();
                        float x = (float)e.GetProperty("x").GetDouble();
                        float y = (float)e.GetProperty("y").GetDouble();
                        float z = (float)e.GetProperty("z").GetDouble();
                        if (!IsValidIdentifier(battleId, 64) || !IsValidIdentifier(role, 32) ||
                            index is < 0 or > 4096 || !IsFiniteAndBounded(x) || !IsFiniteAndBounded(y) || !IsFiniteAndBounded(z))
                        {
                            WriteResponse(ns, 400, "invalid coordinates", responseOrigin);
                            return;
                        }
                        Drags.GetOrAdd(battleId, _ => new ConcurrentDictionary<string, Coord>(StringComparer.Ordinal))
                            [$"{role}|{index}"] = new Coord(x, y, z);
                        WriteResponse(ns, 200, "ok", responseOrigin);
                    }
                    catch { WriteResponse(ns, 400, "bad json", responseOrigin); }
                    return;
                }

                WriteResponse(ns, 404, "not found", responseOrigin);
            }
        }

        internal static bool IsAuthorized(string method, string path, IReadOnlyDictionary<string, string> headers, out string? responseOrigin)
        {
            responseOrigin = null;
            string? allowedOrigin = _allowedOrigin;
            if (string.IsNullOrEmpty(allowedOrigin) || string.IsNullOrEmpty(RequestToken) ||
                !headers.TryGetValue("Origin", out string? origin) ||
                !string.Equals(origin, allowedOrigin, StringComparison.Ordinal))
                return false;

            if (!string.Equals(path, "/drag", StringComparison.Ordinal) &&
                !string.Equals(path, "/save", StringComparison.Ordinal))
                return false;

            if (method == "OPTIONS")
            {
                if (!headers.TryGetValue("Access-Control-Request-Method", out string? requestedMethod) ||
                    !string.Equals(requestedMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
                    !headers.TryGetValue("Access-Control-Request-Headers", out string? requestedHeaders) ||
                    requestedHeaders.IndexOf("x-ffx-studio-token", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                responseOrigin = allowedOrigin;
                return true;
            }

            if (method != "POST" || !headers.TryGetValue("X-FFX-Studio-Token", out string? token) ||
                !TokenEquals(token, RequestToken))
                return false;

            responseOrigin = allowedOrigin;
            return true;
        }

        private static bool TokenEquals(string supplied, string expected)
        {
            if (supplied.Length != expected.Length) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(expected));
        }

        private static bool IsFiniteAndBounded(float value) =>
            float.IsFinite(value) && Math.Abs(value) <= 1_000_000f;

        private static bool IsValidIdentifier(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
            foreach (char c in value)
                if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return false;
            return true;
        }

        private static void WriteResponse(NetworkStream ns, int status, string body, string? allowedOrigin)
        {
            string reason = status switch
            {
                200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden",
                404 => "Not Found", 405 => "Method Not Allowed", 413 => "Payload Too Large",
                415 => "Unsupported Media Type", _ => "Error"
            };
            byte[] payload = Encoding.UTF8.GetBytes(body);
            StringBuilder sb = new();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            if (!string.IsNullOrEmpty(allowedOrigin))
            {
                sb.Append("Access-Control-Allow-Origin: ").Append(allowedOrigin).Append("\r\n");
                sb.Append("Access-Control-Allow-Methods: POST, OPTIONS\r\n");
                sb.Append("Access-Control-Allow-Headers: Content-Type, X-FFX-Studio-Token\r\n");
                sb.Append("Vary: Origin\r\n");
            }
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\n");
            sb.Append("Content-Type: text/plain\r\n");
            sb.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            if (payload.Length > 0) ns.Write(payload, 0, payload.Length);
            ns.Flush();
        }

        /// <summary>Snapshot of the buffered drags for a battle ("role|index" -> coord). Empty if none.</summary>
        public static IReadOnlyDictionary<string, Coord> Snapshot(string? battleId)
        {
            if (!string.IsNullOrEmpty(battleId) && Drags.TryGetValue(battleId, out ConcurrentDictionary<string, Coord>? d))
                return new Dictionary<string, Coord>(d);
            return new Dictionary<string, Coord>();
        }

        public static void Clear(string? battleId)
        {
            if (!string.IsNullOrEmpty(battleId)) Drags.TryRemove(battleId, out _);
        }
    }
}
