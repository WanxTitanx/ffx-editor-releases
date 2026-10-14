// StudioWebServerMemoryTests.cs
// WHY: B2i1 needs native loopback evidence for immutable, bounded exact-memory routes without
// introducing a filesystem surrogate or changing existing exact-file assertions.
// MAINT: Keep these cases at the HTTP boundary; direct private-method tests cannot prove headers,
// fallback, or response bytes.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    // ── Exclusive loopback/lifetime integration tests ──
    // These tests intentionally suspend an in-flight loopback response. Running them beside the
    // full suite can starve the continuation past the bounded timeout even when isolated runs pass.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class StudioWebServerLoopbackLifetimeTestCollection
    {
        public const string Name = "StudioWebServer loopback lifetime tests";
    }

    /// <summary>
    /// Exercises exact in-memory routes through the real loopback HTTP boundary. The fixture keeps
    /// a prefix-backed file at the same request path so every lease transition proves its fallback.
    /// </summary>
    [Collection(StudioWebServerLoopbackLifetimeTestCollection.Name)]
    public sealed partial class StudioWebServerMemoryTests : IDisposable
    {
        private const string ExactPath = "/data/FinalFantasyX/0e/00ef.bin";
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        private static readonly byte[] FallbackBytes = { 0xFA, 0x11, 0xBA, 0xCC };

        private readonly string _root;
        private readonly string _selectedDataRoot;
        private readonly StudioWebServer _server;

        public StudioWebServerMemoryTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "viewerhub-memory-tests-" + Guid.NewGuid().ToString("N"));
            _selectedDataRoot = Path.Combine(_root, "selected-data");
            string fallbackPath = Path.Combine(
                _selectedDataRoot,
                "FinalFantasyX",
                "0e",
                "00ef.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
            File.WriteAllBytes(fallbackPath, FallbackBytes);

            _server = new StudioWebServer().MapPrefix("/data", _selectedDataRoot);
            Assert.True(_server.Start(0), _server.Status);
            Assert.InRange(_server.Port, 1, 65535);
        }

        public void Dispose()
        {
            _server.Stop();
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void LoopbackLifetimeFixture_UsesExclusiveCollection()
        {
            var membership = Assert.Single(typeof(StudioWebServerMemoryTests).GetCustomAttributesData()
                .Where(value => value.AttributeType == typeof(CollectionAttribute)));
            Assert.Equal(
                StudioWebServerLoopbackLifetimeTestCollection.Name,
                Assert.Single(membership.ConstructorArguments).Value);

            CollectionDefinitionAttribute? definition =
                typeof(StudioWebServerLoopbackLifetimeTestCollection)
                    .GetCustomAttribute<CollectionDefinitionAttribute>();
            Assert.NotNull(definition);
            Assert.True(definition!.DisableParallelization);
        }

        [Fact]
        public async Task ExactMemory_CopiesCallerBytes_ServesSecurityHeaders_AndFallsBackAfterDispose()
        {
            string[] treeBefore = SnapshotTree(_root);
            byte[] callerBytes = { 0x10, 0x20, 0x30, 0x40 };
            byte[] expectedBytes = callerBytes.ToArray();

            Assert.True(_server.TryMapExactBytes(ExactPath, callerBytes, out var lease));
            StudioWebServer.ExactMemoryLease activeLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            callerBytes.AsSpan().Fill(0xEE);

            using (HttpResponseMessage response = await SendAsync(HttpMethod.Get, ExactPath))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
                Assert.Equal((long)expectedBytes.Length, response.Content.Headers.ContentLength);
                Assert.Equal(expectedBytes, await response.Content.ReadAsByteArrayAsync());
                AssertSecurityHeaders(response);
            }

            Assert.True(activeLease.IsActive);
            Assert.Equal(treeBefore, SnapshotTree(_root));

            activeLease.Dispose();
            Assert.False(activeLease.IsActive);
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
            Assert.Equal(treeBefore, SnapshotTree(_root));
        }

        [Fact]
        public async Task ExactMemory_FileThenMemory_DisposeRevealsFile_ThenUnmapRevealsPrefix()
        {
            byte[] fileBytes = { 0xF1, 0xF2 };
            byte[] memoryBytes = { 0xA1, 0xA2, 0xA3 };
            string filePath = WriteExactFile("file-then-memory.bin", fileBytes);

            Assert.True(_server.TryMapExactFile(ExactPath, filePath));
            Assert.True(_server.TryMapExactBytes(ExactPath, memoryBytes, out var lease));
            StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);

            Assert.Equal(memoryBytes, await GetBytesAsync(ExactPath));
            memoryLease.Dispose();
            Assert.Equal(fileBytes, await GetBytesAsync(ExactPath));
            Assert.True(_server.TryUnmapExactFile(ExactPath, filePath));
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_MemoryThenFile_UnmapRevealsMemory_ThenDisposeRevealsPrefix()
        {
            byte[] memoryBytes = { 0xB1, 0xB2, 0xB3 };
            byte[] fileBytes = { 0xC1, 0xC2 };
            string filePath = WriteExactFile("memory-then-file.bin", fileBytes);

            Assert.True(_server.TryMapExactBytes(ExactPath, memoryBytes, out var lease));
            StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            Assert.True(_server.TryMapExactFile(ExactPath, filePath));

            Assert.Equal(fileBytes, await GetBytesAsync(ExactPath));
            Assert.True(_server.TryUnmapExactFile(ExactPath, filePath));
            Assert.Equal(memoryBytes, await GetBytesAsync(ExactPath));
            memoryLease.Dispose();
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_StaleExactFileAboveMemoryFallsBackToMemoryThenPrefix()
        {
            byte[] memoryBytes = { 0xD1, 0xD2, 0xD3 };
            string filePath = WriteExactFile("stale-above-memory.bin", new byte[] { 0xE1 });
            Assert.True(_server.TryMapExactBytes(ExactPath, memoryBytes, out var lease));
            StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            Assert.True(_server.TryMapExactFile(ExactPath, filePath));
            File.Delete(filePath);

            Assert.Equal(memoryBytes, await GetBytesAsync(ExactPath));
            Assert.False(_server.TryUnmapExactFile(ExactPath, filePath));

            memoryLease.Dispose();
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_FileRemapDeduplicatesOnlyFileOwnerAmidMemoryOwners()
        {
            byte[] olderBytes = { 0x71 };
            byte[] newestBytes = { 0x72, 0x73 };
            byte[] fileBytes = { 0x74, 0x75, 0x76 };
            string filePath = WriteExactFile("remapped-amid-memory.bin", fileBytes);

            Assert.True(_server.TryMapExactBytes(ExactPath, olderBytes, out var older));
            Assert.True(_server.TryMapExactFile(ExactPath, filePath));
            Assert.True(_server.TryMapExactBytes(ExactPath, newestBytes, out var newest));
            Assert.True(_server.TryMapExactFile(ExactPath, filePath));
            StudioWebServer.ExactMemoryLease olderLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(older);
            StudioWebServer.ExactMemoryLease newestLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(newest);

            Assert.Equal(fileBytes, await GetBytesAsync(ExactPath));
            Assert.True(_server.TryUnmapExactFile(ExactPath, filePath));
            Assert.False(_server.TryUnmapExactFile(ExactPath, filePath));
            Assert.Equal(newestBytes, await GetBytesAsync(ExactPath));

            newestLease.Dispose();
            Assert.Equal(olderBytes, await GetBytesAsync(ExactPath));
            olderLease.Dispose();
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_DisposingOlderLeaseIsIdempotentAndLeavesNewestOwner()
        {
            byte[] olderBytes = { 0x01, 0x02 };
            byte[] newestBytes = { 0x03, 0x04, 0x05 };
            Assert.True(_server.TryMapExactBytes(ExactPath, olderBytes, out var older));
            Assert.True(_server.TryMapExactBytes(ExactPath, newestBytes, out var newest));
            StudioWebServer.ExactMemoryLease olderLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(older);
            StudioWebServer.ExactMemoryLease newestLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(newest);

            olderLease.Dispose();
            olderLease.Dispose();

            Assert.False(olderLease.IsActive);
            Assert.True(newestLease.IsActive);
            Assert.Equal(newestBytes, await GetBytesAsync(ExactPath));
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(
                    usage,
                    ownedBytes: newestBytes.Length,
                    ownedEntries: 1,
                    activeReaders: 0,
                    retiredBytes: 0,
                    retiredEntries: 0),
                "the newest owner to have no completed-response reader");

            newestLease.Dispose();
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_OwnershipQueryIsServerSpecificNonMutatingAndIndependentOfRunningState()
        {
            byte[] payload = { 0xA1, 0xB2, 0xC3 };
            Assert.True(_server.TryMapExactBytes(ExactPath, payload, out var lease));
            StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);

            var otherServer = new StudioWebServer().MapPrefix("/data", _selectedDataRoot);
            Assert.True(otherServer.Start(0), otherServer.Status);
            try
            {
                Assert.Equal(payload, await GetBytesAsync(_server, ExactPath));
                Assert.Equal(FallbackBytes, await GetBytesAsync(otherServer, ExactPath));
                await AssertUsageEventuallyAsync(
                    usage => UsageEquals(usage, payload.Length, 1, 0, 0, 0),
                    "the ownership-query setup reader to finish");

                StudioWebServer.ExactMemoryUsageSnapshot ownerUsageBefore =
                    _server.GetExactMemoryUsageForTests();
                StudioWebServer.ExactMemoryUsageSnapshot otherUsageBefore =
                    otherServer.GetExactMemoryUsageForTests();

                Assert.True(memoryLease.IsOwnedBy(_server));
                Assert.False(memoryLease.IsOwnedBy(otherServer));
                Assert.Equal(ownerUsageBefore, _server.GetExactMemoryUsageForTests());
                Assert.Equal(otherUsageBefore, otherServer.GetExactMemoryUsageForTests());
                Assert.Equal(payload, await GetBytesAsync(_server, ExactPath));
                Assert.Equal(FallbackBytes, await GetBytesAsync(otherServer, ExactPath));
                await AssertUsageEventuallyAsync(
                    usage => usage.Equals(ownerUsageBefore),
                    "the post-query route reader to finish");
                Assert.Equal(otherUsageBefore, otherServer.GetExactMemoryUsageForTests());

                _server.Stop();
                Assert.False(_server.IsRunning);
                Assert.True(memoryLease.IsOwnedBy(_server));
                Assert.False(memoryLease.IsOwnedBy(otherServer));
                Assert.Equal(ownerUsageBefore, _server.GetExactMemoryUsageForTests());
                Assert.Equal(otherUsageBefore, otherServer.GetExactMemoryUsageForTests());

                memoryLease.Dispose();
                Assert.False(memoryLease.IsOwnedBy(_server));
                Assert.False(memoryLease.IsOwnedBy(otherServer));
                AssertUsage(_server.GetExactMemoryUsageForTests(), 0, 0, 0, 0, 0);
                Assert.True(_server.Start(0), _server.Status);
                Assert.Equal(FallbackBytes, await GetBytesAsync(_server, ExactPath));
            }
            finally
            {
                otherServer.Stop();
                memoryLease.Dispose();
            }
        }


        [Fact]
        public async Task ExactMemory_HeadCapturesMetadataWithoutPayloadReaderOrWriteHook()
        {
            const string jsonPath = "/data/FinalFantasyX/0e/metadata.json";
            byte[] payload = { (byte)'{', (byte)'}', (byte)'\n' };
            Assert.True(_server.TryMapExactBytes(jsonPath, payload, out var lease));
            using StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            _server.BeforeMemoryWriteForTests = _ =>
                Task.FromException(new InvalidOperationException("HEAD must not invoke the GET hook."));

            using HttpResponseMessage response = await SendAsync(HttpMethod.Head, jsonPath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal((long)payload.Length, response.Content.Headers.ContentLength);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            AssertSecurityHeaders(response);
            AssertUsage(
                _server.GetExactMemoryUsageForTests(),
                ownedBytes: payload.Length,
                ownedEntries: 1,
                activeReaders: 0,
                retiredBytes: 0,
                retiredEntries: 0);
        }

        [Fact]
        public async Task ExactMemory_InvalidEmptyAndPerEntryLimitRequestsDoNotMutateRoutesOrUsage()
        {
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: 4,
                maxAggregateBytes: 64,
                maxEntries: 8);
            StudioWebServer.ExactMemoryUsageSnapshot empty = _server.GetExactMemoryUsageForTests();

            Assert.False(_server.TryMapExactBytes(ExactPath, ReadOnlySpan<byte>.Empty, out var emptyLease));
            Assert.Null(emptyLease);
            Assert.False(_server.TryMapExactBytes(
                "data/no-leading-slash.bin",
                new byte[] { 1 },
                out var invalidLease));
            Assert.Null(invalidLease);
            Assert.False(_server.TryMapExactBytes(
                "/data/%2e%2e/escape.bin",
                new byte[] { 1 },
                out var encodedLease));
            Assert.Null(encodedLease);
            Assert.False(_server.TryMapExactBytes(ExactPath, new byte[5], out var oversizedLease));
            Assert.Null(oversizedLease);
            Assert.Equal(empty, _server.GetExactMemoryUsageForTests());
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));

            Assert.Throws<InvalidOperationException>(() =>
                _server.LowerExactMemoryLimitsForTests(5, 64, 8));
        }

        [Fact]
        public async Task ExactMemory_ConcurrentAdmissionsHonorEntryLimitWithByteHeadroom()
        {
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: 4,
                maxAggregateBytes: 64,
                maxEntries: 1);
            using var gate = new ManualResetEventSlim(initialState: false);

            Task<(bool Mapped, StudioWebServer.ExactMemoryLease? Lease)>[] attempts =
                Enumerable.Range(0, 8)
                    .Select(index => Task.Run(() =>
                    {
                        gate.Wait();
                        bool mapped = _server.TryMapExactBytes(
                            $"/data/concurrent-{index}.bin",
                            new byte[] { (byte)index, 1, 2, 3 },
                            out var lease);
                        return (mapped, lease);
                    }))
                    .ToArray();

            gate.Set();
            (bool Mapped, StudioWebServer.ExactMemoryLease? Lease)[] results =
                await Task.WhenAll(attempts).WaitAsync(TestTimeout);

            (bool Mapped, StudioWebServer.ExactMemoryLease? Lease) winner =
                Assert.Single(results.Where(result => result.Mapped));
            Assert.All(results.Where(result => !result.Mapped), result => Assert.Null(result.Lease));
            AssertUsage(_server.GetExactMemoryUsageForTests(), 4, 1, 0, 0, 0);

            Assert.NotNull(winner.Lease);
            winner.Lease!.Dispose();
            AssertUsage(_server.GetExactMemoryUsageForTests(), 0, 0, 0, 0, 0);
        }

        private HttpClient CreateClient() => new()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}"),
            Timeout = TestTimeout,
        };

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
        {
            using HttpClient client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            return await client.SendAsync(request);
        }

        private Task<byte[]> GetBytesAsync(string path) =>
            GetBytesAsync(_server, path);

        private static async Task<byte[]> GetBytesAsync(StudioWebServer server, string path)
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"),
                Timeout = TestTimeout,
            };
            return await client.GetByteArrayAsync(path);
        }

        private string WriteExactFile(string name, byte[] bytes)
        {
            string directory = Path.Combine(_root, "exact-files");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string[] SnapshotTree(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                    Path.GetRelativePath(root, path) + "|" +
                    new FileInfo(path).Length + "|" +
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .ToArray();

        private static void AssertSecurityHeaders(HttpResponseMessage response)
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(
                "nosniff",
                Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(
                "default-src 'self' data: blob:; connect-src 'self'; img-src 'self' data: blob:; " +
                "media-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' blob:; " +
                "worker-src 'self' blob:; child-src 'self' blob:; frame-src 'self' blob:; " +
                "object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
                Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        }

        private static void AssertUsage(
            StudioWebServer.ExactMemoryUsageSnapshot usage,
            long ownedBytes,
            int ownedEntries,
            int activeReaders,
            long retiredBytes,
            int retiredEntries)
        {
            Assert.Equal(ownedBytes, usage.OwnedBytes);
            Assert.Equal(ownedEntries, usage.OwnedEntries);
            Assert.Equal(activeReaders, usage.ActiveReaders);
            Assert.Equal(retiredBytes, usage.RetiredBytes);
            Assert.Equal(retiredEntries, usage.RetiredEntries);
        }

        private async Task AssertUsageEventuallyAsync(
            Func<StudioWebServer.ExactMemoryUsageSnapshot, bool> predicate,
            string expectation)
        {
            long started = Stopwatch.GetTimestamp();
            while (true)
            {
                StudioWebServer.ExactMemoryUsageSnapshot usage =
                    _server.GetExactMemoryUsageForTests();
                if (predicate(usage))
                    return;
                if (Stopwatch.GetElapsedTime(started) >= TestTimeout)
                {
                    Assert.Fail($"Timed out waiting for {expectation}. Last usage: {usage}.");
                    return;
                }

                await Task.Yield();
            }
        }

        private static bool UsageEquals(
            StudioWebServer.ExactMemoryUsageSnapshot usage,
            long ownedBytes,
            int ownedEntries,
            int activeReaders,
            long retiredBytes,
            int retiredEntries) =>
            usage.OwnedBytes == ownedBytes &&
            usage.OwnedEntries == ownedEntries &&
            usage.ActiveReaders == activeReaders &&
            usage.RetiredBytes == retiredBytes &&
            usage.RetiredEntries == retiredEntries;
    }
}
