// StudioWebServer.ExactMemory.cs
// WHY: Native Linux previews need an ephemeral, immutable exact route that never stages payload
// bytes on disk. The existing exact route stack remains the sole route-selection authority.
// MAINT: Reader acquisition/release and budget accounting must stay under _routeLock. Never expose
// owner tokens through HTTP, logs, paths, or caller-visible diagnostics.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    public sealed partial class StudioWebServer
    {
        private const int DefaultMaxExactMemoryEntryBytes = 64 * 1024 * 1024;
        private const long DefaultMaxExactMemoryAggregateBytes = 128L * 1024 * 1024;
        private const int DefaultMaxExactMemoryEntries = 32;

        // ── Canonical exact-route ownership ──────────────────────────────────────────────────
        // File and memory registrations intentionally share one LIFO stack. A memory lease owns
        // only its private token; payload bytes remain server-owned until every acquired GET exits.
        private abstract class ExactOwner
        {
        }

        private sealed class FileExactOwner : ExactOwner
        {
            public FileExactOwner(
                string fullPath,
                FileSystemReparseGuard.FileIdentity identity)
            {
                FullPath = fullPath;
                Identity = identity;
            }

            public string FullPath { get; }
            public FileSystemReparseGuard.FileIdentity Identity { get; }
        }

        private sealed class MemoryExactOwner : ExactOwner
        {
            public MemoryExactOwner(object token, byte[] payload)
            {
                Token = token;
                Payload = payload;
                Length = payload.Length;
            }

            public object Token { get; }
            public byte[]? Payload { get; set; }
            public int Length { get; }
            public int ActiveReaders { get; set; }
            public bool IsRetired { get; set; }
            public bool HasRetiredCharge { get; set; }
        }

        private readonly Dictionary<string, List<ExactOwner>> _exactOwners =
            new(StringComparer.Ordinal);
        private long _ownedExactMemoryBytes;
        private int _ownedExactMemoryEntries;
        private int _activeExactMemoryReaders;
        private long _retiredExactMemoryBytes;
        private int _retiredExactMemoryEntries;
        private int _maxExactMemoryEntryBytes = DefaultMaxExactMemoryEntryBytes;
        private long _maxExactMemoryAggregateBytes = DefaultMaxExactMemoryAggregateBytes;
        private int _maxExactMemoryEntries = DefaultMaxExactMemoryEntries;

        /// <summary>
        /// Represents one exact in-memory route registration. Disposing the lease removes only this
        /// registration and never retains the server-owned payload in the lease object.
        /// </summary>
        public sealed class ExactMemoryLease : IDisposable
        {
            private LeaseState? _state;

            internal ExactMemoryLease(StudioWebServer server, string requestPath, object token)
            {
                RequestPath = requestPath;
                _state = new LeaseState(server, token);
            }

            public string RequestPath { get; }

            public bool IsActive
            {
                get
                {
                    LeaseState? state = Volatile.Read(ref _state);
                    return state != null &&
                        state.Server.IsExactMemoryOwnerActive(RequestPath, state.Token);
                }
            }

            internal bool IsOwnedBy(StudioWebServer server)
            {
                LeaseState? state = Volatile.Read(ref _state);
                return state != null && ReferenceEquals(state.Server, server);
            }

            public void Dispose()
            {
                LeaseState? state = Interlocked.Exchange(ref _state, null);
                state?.Server.ReleaseExactMemoryOwner(RequestPath, state.Token);
            }

            private sealed class LeaseState
            {
                public LeaseState(StudioWebServer server, object token)
                {
                    Server = server;
                    Token = token;
                }

                public StudioWebServer Server { get; }
                public object Token { get; }
            }
        }

        /// <summary>
        /// Copies bytes into a bounded server-owned exact route. The request path is validated by
        /// the same canonical rule used by exact-file mappings.
        /// </summary>
        public bool TryMapExactBytes(
            string requestPath,
            ReadOnlySpan<byte> source,
            out ExactMemoryLease? lease)
        {
            lease = null;
            if (!TryNormalizeMappedExactPath(requestPath, out string normalizedPath) ||
                source.IsEmpty)
                return false;

            lock (_routeLock)
            {
                int payloadLength = source.Length;
                if (payloadLength > _maxExactMemoryEntryBytes ||
                    _ownedExactMemoryEntries >= _maxExactMemoryEntries ||
                    payloadLength > _maxExactMemoryAggregateBytes - _ownedExactMemoryBytes)
                    return false;

                // Reserve before copying. Because admission, reservation, copy, and registration
                // share this lock, concurrent callers cannot allocate uncharged payload copies.
                _ownedExactMemoryBytes += payloadLength;
                _ownedExactMemoryEntries++;
                byte[]? ownedPayload = null;
                MemoryExactOwner? owner = null;
                List<ExactOwner>? owners = null;
                bool createdStack = false;
                try
                {
                    ownedPayload = source.ToArray();
                    object token = new();
                    owner = new MemoryExactOwner(token, ownedPayload);
                    var newLease = new ExactMemoryLease(this, normalizedPath, token);
                    if (!_exactOwners.TryGetValue(normalizedPath, out owners))
                    {
                        owners = new List<ExactOwner>();
                        _exactOwners.Add(normalizedPath, owners);
                        createdStack = true;
                    }

                    owners.Add(owner);
                    lease = newLease;
                    return true;
                }
                catch
                {
                    if (owner != null && owners != null)
                        owners.Remove(owner);
                    if (createdStack && owners is { Count: 0 })
                        _exactOwners.Remove(normalizedPath);
                    if (ownedPayload != null)
                        Array.Clear(ownedPayload, 0, ownedPayload.Length);
                    _ownedExactMemoryBytes -= payloadLength;
                    _ownedExactMemoryEntries--;
                    throw;
                }
            }
        }

        // ── Exact owner serving and lifetime ─────────────────────────────────────────────────

        private async Task<bool> TryServeExactOwnerAsync(
            NetworkStream network,
            string requestPath,
            bool isHead,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                ExactOwner owner;
                byte[]? acquiredPayload = null;
                int memoryLength = 0;
                lock (_routeLock)
                {
                    if (!_exactOwners.TryGetValue(requestPath, out List<ExactOwner>? owners) ||
                        owners.Count == 0)
                        return false;

                    owner = owners[^1];
                    if (owner is MemoryExactOwner memoryOwner)
                    {
                        memoryLength = memoryOwner.Length;
                        if (!isHead)
                        {
                            acquiredPayload = memoryOwner.Payload;
                            if (acquiredPayload == null)
                            {
                                RemoveExactOwnerLocked(requestPath, owner);
                                continue;
                            }

                            memoryOwner.ActiveReaders++;
                            _activeExactMemoryReaders++;
                        }
                    }
                }

                if (owner is MemoryExactOwner acquiredMemoryOwner)
                {
                    try
                    {
                        await ServeExactMemoryAsync(
                            network,
                            requestPath,
                            memoryLength,
                            acquiredPayload,
                            isHead,
                            cancellationToken);
                        return true;
                    }
                    finally
                    {
                        if (!isHead)
                            ReleaseExactMemoryReader(acquiredMemoryOwner);
                    }
                }

                var fileOwner = (FileExactOwner)owner;
                string? root = Path.GetDirectoryName(fileOwner.FullPath);
                FileSystemReparseGuard.VerifiedReadFile? verifiedFile = null;
                if (root != null &&
                    FileSystemReparseGuard.TryOpenVerifiedRead(
                        root,
                        fileOwner.FullPath,
                        out verifiedFile) == FileSystemReparseGuard.VerifiedOpenResult.Success &&
                    verifiedFile != null &&
                    verifiedFile.Identity == fileOwner.Identity)
                {
                    using (verifiedFile)
                        await ServeOpenedFile(
                            network,
                            verifiedFile,
                            Path.GetFileName(verifiedFile.FullPath),
                            isHead,
                            cancellationToken);
                    return true;
                }

                verifiedFile?.Dispose();
                lock (_routeLock)
                    RemoveExactOwnerLocked(requestPath, owner);
            }
        }

        private async Task ServeExactMemoryAsync(
            NetworkStream network,
            string requestPath,
            int contentLength,
            byte[]? payload,
            bool isHead,
            CancellationToken cancellationToken)
        {
            if (!isHead)
            {
                Func<CancellationToken, Task>? beforeWrite = BeforeMemoryWriteForTests;
                if (beforeWrite != null)
                    await beforeWrite(cancellationToken);
            }

            WriteResponseHeaders(network, 200, MimeFor(requestPath), contentLength, null);
            if (!isHead && payload != null)
                await network.WriteAsync(payload.AsMemory(0, contentLength), cancellationToken);
            await network.FlushAsync(cancellationToken);
        }

        private bool IsExactMemoryOwnerActive(string requestPath, object token)
        {
            lock (_routeLock)
            {
                return _exactOwners.TryGetValue(requestPath, out List<ExactOwner>? owners) &&
                    owners.Exists(owner =>
                        owner is MemoryExactOwner memoryOwner &&
                        ReferenceEquals(memoryOwner.Token, token));
            }
        }

        private void ReleaseExactMemoryOwner(string requestPath, object token)
        {
            lock (_routeLock)
            {
                if (!_exactOwners.TryGetValue(requestPath, out List<ExactOwner>? owners))
                    return;

                int index = owners.FindLastIndex(owner =>
                    owner is MemoryExactOwner memoryOwner &&
                    ReferenceEquals(memoryOwner.Token, token));
                if (index < 0)
                    return;

                var memoryOwner = (MemoryExactOwner)owners[index];
                owners.RemoveAt(index);
                if (owners.Count == 0)
                    _exactOwners.Remove(requestPath);

                memoryOwner.IsRetired = true;
                if (memoryOwner.ActiveReaders == 0)
                {
                    FinalizeExactMemoryOwnerLocked(memoryOwner);
                }
                else
                {
                    memoryOwner.HasRetiredCharge = true;
                    _retiredExactMemoryBytes += memoryOwner.Length;
                    _retiredExactMemoryEntries++;
                }
            }
        }

        private void ReleaseExactMemoryReader(MemoryExactOwner owner)
        {
            lock (_routeLock)
            {
                if (owner.ActiveReaders <= 0)
                    throw new InvalidOperationException("Exact-memory reader accounting underflow.");

                owner.ActiveReaders--;
                _activeExactMemoryReaders--;
                if (owner.IsRetired && owner.ActiveReaders == 0)
                    FinalizeExactMemoryOwnerLocked(owner);
            }
        }

        private void FinalizeExactMemoryOwnerLocked(MemoryExactOwner owner)
        {
            byte[]? payload = owner.Payload;
            if (payload == null)
                return;

            Array.Clear(payload, 0, payload.Length);
            owner.Payload = null;

            if (owner.HasRetiredCharge)
            {
                _retiredExactMemoryBytes -= owner.Length;
                _retiredExactMemoryEntries--;
                owner.HasRetiredCharge = false;
            }

            _ownedExactMemoryBytes -= owner.Length;
            _ownedExactMemoryEntries--;
        }

        private void RemoveExactOwnerLocked(string requestPath, ExactOwner owner)
        {
            if (!_exactOwners.TryGetValue(requestPath, out List<ExactOwner>? owners))
                return;

            int index = owners.FindLastIndex(candidate => ReferenceEquals(candidate, owner));
            if (index >= 0)
                owners.RemoveAt(index);
            if (owners.Count == 0)
                _exactOwners.Remove(requestPath);
        }

        // ── Deterministic test seams ─────────────────────────────────────────────────────────
        // These internal hooks are visible only to the test assembly. Limits can only move down
        // from production defaults/current values and cannot be lowered beneath owned usage.
        internal Func<CancellationToken, Task>? BeforeMemoryWriteForTests { get; set; }

        internal readonly record struct ExactMemoryUsageSnapshot(
            long OwnedBytes,
            int OwnedEntries,
            int ActiveReaders,
            long RetiredBytes,
            int RetiredEntries);

        internal ExactMemoryUsageSnapshot GetExactMemoryUsageForTests()
        {
            lock (_routeLock)
            {
                return new ExactMemoryUsageSnapshot(
                    _ownedExactMemoryBytes,
                    _ownedExactMemoryEntries,
                    _activeExactMemoryReaders,
                    _retiredExactMemoryBytes,
                    _retiredExactMemoryEntries);
            }
        }

        internal void LowerExactMemoryLimitsForTests(
            int maxPerEntryBytes,
            long maxAggregateBytes,
            int maxEntries)
        {
            if (maxPerEntryBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPerEntryBytes));
            if (maxAggregateBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAggregateBytes));
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            if (maxPerEntryBytes > maxAggregateBytes)
                throw new ArgumentException(
                    "The per-entry limit cannot exceed the aggregate limit.",
                    nameof(maxPerEntryBytes));

            lock (_routeLock)
            {
                if (maxPerEntryBytes > _maxExactMemoryEntryBytes ||
                    maxAggregateBytes > _maxExactMemoryAggregateBytes ||
                    maxEntries > _maxExactMemoryEntries)
                    throw new InvalidOperationException("Exact-memory test limits can only be lowered.");
                if (_ownedExactMemoryEntries != 0)
                    throw new InvalidOperationException(
                        "Exact-memory test limits cannot change while payloads are owned.");

                _maxExactMemoryEntryBytes = maxPerEntryBytes;
                _maxExactMemoryAggregateBytes = maxAggregateBytes;
                _maxExactMemoryEntries = maxEntries;
            }
        }
    }
}
