// StudioWebServerMemoryTests.Lifetime.cs
// WHY: Reader lifetime must remain deterministic across lease retirement, I/O failure, and Stop.
// MAINT: Synchronize through the server's test hook; do not replace these gates with sleeps or
// production-sized allocations.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub
{
    public sealed partial class StudioWebServerMemoryTests
    {
        [Fact]
        public async Task ExactMemory_DisposedInFlightOwnerStaysChargedUntilLastReaderFinishes()
        {
            byte[] payload = { 0x31, 0x32, 0x33, 0x34 };
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: 8,
                maxAggregateBytes: 16,
                maxEntries: 4);
            var entered = NewSignal();
            var release = NewSignal();
            _server.BeforeMemoryWriteForTests = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            };

            Assert.True(_server.TryMapExactBytes(ExactPath, payload, out var lease));
            StudioWebServer.ExactMemoryLease activeLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            Task<byte[]> inFlight = GetBytesAsync(ExactPath);
            await entered.Task.WaitAsync(TestTimeout);

            activeLease.Dispose();
            Assert.False(activeLease.IsActive);
            AssertUsage(
                _server.GetExactMemoryUsageForTests(),
                ownedBytes: payload.Length,
                ownedEntries: 1,
                activeReaders: 1,
                retiredBytes: payload.Length,
                retiredEntries: 1);
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));

            release.TrySetResult();
            Assert.Equal(payload, await inFlight.WaitAsync(TestTimeout));
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(usage, 0, 0, 0, 0, 0),
                "the completed in-flight reader to release its retired owner");

            Assert.True(_server.TryMapExactBytes(ExactPath, new byte[] { 9 }, out var replacement));
            Assert.IsType<StudioWebServer.ExactMemoryLease>(replacement).Dispose();
        }

        [Fact]
        public async Task ExactMemory_RetiredBytesBlockAdmissionWhileEntrySlotsRemain()
        {
            byte[] payload = { 0x81, 0x82, 0x83, 0x84 };
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: 4,
                maxAggregateBytes: 4,
                maxEntries: 8);
            var entered = NewSignal();
            var release = NewSignal();
            _server.BeforeMemoryWriteForTests = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            };

            Assert.True(_server.TryMapExactBytes(ExactPath, payload, out var lease));
            StudioWebServer.ExactMemoryLease activeLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            Task<byte[]> request = GetBytesAsync(ExactPath);
            await entered.Task.WaitAsync(TestTimeout);
            activeLease.Dispose();

            AssertUsage(_server.GetExactMemoryUsageForTests(), 4, 1, 1, 4, 1);
            Assert.False(_server.TryMapExactBytes(
                "/data/spare-entry.bin",
                new byte[] { 1 },
                out var blocked));
            Assert.Null(blocked);

            release.TrySetResult();
            Assert.Equal(payload, await request.WaitAsync(TestTimeout));
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(usage, 0, 0, 0, 0, 0),
                "the retired-byte charge to clear after its reader exits");
        }

        [Fact]
        public async Task ExactMemory_RetiredEntryBlocksAdmissionWhileByteBudgetRemains()
        {
            byte[] payload = { 0x91 };
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: 4,
                maxAggregateBytes: 64,
                maxEntries: 1);
            var entered = NewSignal();
            var release = NewSignal();
            _server.BeforeMemoryWriteForTests = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            };

            Assert.True(_server.TryMapExactBytes(ExactPath, payload, out var lease));
            StudioWebServer.ExactMemoryLease activeLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            Task<byte[]> request = GetBytesAsync(ExactPath);
            await entered.Task.WaitAsync(TestTimeout);
            activeLease.Dispose();

            AssertUsage(_server.GetExactMemoryUsageForTests(), 1, 1, 1, 1, 1);
            Assert.False(_server.TryMapExactBytes(
                "/data/spare-bytes.bin",
                new byte[] { 2 },
                out var blocked));
            Assert.Null(blocked);

            release.TrySetResult();
            Assert.Equal(payload, await request.WaitAsync(TestTimeout));
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(usage, 0, 0, 0, 0, 0),
                "the retired-entry charge to clear after its reader exits");
        }

        [Fact]
        public async Task ExactMemory_InjectedWriteFailureStillReleasesRetiredReaderAndCharge()
        {
            byte[] payload = { 0x41, 0x42, 0x43 };
            _server.LowerExactMemoryLimitsForTests(
                maxPerEntryBytes: payload.Length,
                maxAggregateBytes: payload.Length,
                maxEntries: 1);
            var entered = NewSignal();
            var fail = NewSignal();
            _server.BeforeMemoryWriteForTests = async cancellationToken =>
            {
                entered.TrySetResult();
                await fail.Task.WaitAsync(cancellationToken);
                throw new IOException("Injected exact-memory write failure.");
            };

            Assert.True(_server.TryMapExactBytes(ExactPath, payload, out var lease));
            StudioWebServer.ExactMemoryLease activeLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            var trace = new RawTransportTrace();
            Task<RawTransportResult> request = SendSingleRawGetAsync(_server, ExactPath, trace);
            await AssertHookEnteredBeforeRequestCompletesAsync(entered.Task, request, trace);
            activeLease.Dispose();
            AssertUsage(
                _server.GetExactMemoryUsageForTests(),
                ownedBytes: payload.Length,
                ownedEntries: 1,
                activeReaders: 1,
                retiredBytes: payload.Length,
                retiredEntries: 1);

            fail.TrySetResult();
            await AssertRawConnectionClosedWithoutResponseAsync(request);
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(usage, 0, 0, 0, 0, 0),
                "the failed writer to release its retired owner");
            _server.BeforeMemoryWriteForTests = null;
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        [Fact]
        public async Task ExactMemory_StopCancelsReaderButRetainsUndisposedMemoryAndFileOwnersAcrossRestart()
        {
            byte[] fileBytes = { 0x51, 0x52 };
            byte[] memoryBytes = { 0x61, 0x62, 0x63 };
            string filePath = WriteExactFile("stop-restart.bin", fileBytes);
            var entered = NewSignal();
            var canceled = NewSignal();

            Assert.True(_server.TryMapExactFile(ExactPath, filePath));
            Assert.True(_server.TryMapExactBytes(ExactPath, memoryBytes, out var lease));
            StudioWebServer.ExactMemoryLease memoryLease =
                Assert.IsType<StudioWebServer.ExactMemoryLease>(lease);
            _server.BeforeMemoryWriteForTests = async cancellationToken =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    canceled.TrySetResult();
                }
            };

            var trace = new RawTransportTrace();
            Task<RawTransportResult> request = SendSingleRawGetAsync(_server, ExactPath, trace);
            await AssertHookEnteredBeforeRequestCompletesAsync(entered.Task, request, trace);
            _server.Stop();
            await canceled.Task.WaitAsync(TestTimeout);
            await AssertRawConnectionClosedWithoutResponseAsync(request);

            Assert.True(memoryLease.IsActive);
            await AssertUsageEventuallyAsync(
                usage => UsageEquals(
                    usage,
                    ownedBytes: memoryBytes.Length,
                    ownedEntries: 1,
                    activeReaders: 0,
                    retiredBytes: 0,
                    retiredEntries: 0),
                "Stop cancellation to release the reader while preserving the live owner");

            _server.BeforeMemoryWriteForTests = null;
            Assert.True(_server.Start(0), _server.Status);
            Assert.Equal(memoryBytes, await GetBytesAsync(ExactPath));

            memoryLease.Dispose();
            Assert.Equal(fileBytes, await GetBytesAsync(ExactPath));
            Assert.True(_server.TryUnmapExactFile(ExactPath, filePath));
            Assert.Equal(FallbackBytes, await GetBytesAsync(ExactPath));
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task<RawTransportResult> SendSingleRawGetAsync(
            StudioWebServer server,
            string path,
            RawTransportTrace trace)
        {
            const int maxResponseBytes = 64 * 1024;
            using var deadline = new CancellationTokenSource(TestTimeout);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            byte[] response = new byte[maxResponseBytes];
            int received = 0;
            bool reachedEof = false;
            Exception? failure = null;
            try
            {
                trace.Enter(RawTransportStage.Connect);
                await client.ConnectAsync("127.0.0.1", server.Port, deadline.Token);
                using NetworkStream network = client.GetStream();
                byte[] request = Encoding.ASCII.GetBytes(
                    $"GET {path} HTTP/1.1\r\n" +
                    $"Host: 127.0.0.1:{server.Port}\r\n" +
                    "Connection: close\r\n\r\n");
                trace.Enter(RawTransportStage.Write);
                await network.WriteAsync(request.AsMemory(), deadline.Token);
                trace.Enter(RawTransportStage.Flush);
                await network.FlushAsync(deadline.Token);

                while (received < response.Length)
                {
                    trace.Enter(RawTransportStage.Read);
                    int read = await network.ReadAsync(
                        response.AsMemory(received, response.Length - received),
                        deadline.Token);
                    if (read == 0)
                    {
                        reachedEof = true;
                        break;
                    }

                    received += read;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (!reachedEof && failure == null)
                failure = new InvalidDataException("Raw response exceeded the bounded test buffer.");

            Array.Resize(ref response, received);
            return new RawTransportResult(response, reachedEof, failure, trace.Describe());
        }

        private static async Task AssertHookEnteredBeforeRequestCompletesAsync(
            Task hookEntered,
            Task<RawTransportResult> request,
            RawTransportTrace trace)
        {
            try
            {
                await Task.WhenAny(hookEntered, request).WaitAsync(TestTimeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail(
                    $"Timed out waiting for the exact-memory write hook. " +
                    $"requestStatus={request.Status}; stages={trace.Describe()}.");
                return;
            }

            if (hookEntered.IsCompleted)
            {
                await hookEntered;
                return;
            }

            RawTransportResult premature = await request;
            Assert.Fail(
                "Raw request completed before the exact-memory write hook. " +
                DescribeRawResult(premature));
        }

        private static async Task AssertRawConnectionClosedWithoutResponseAsync(
            Task<RawTransportResult> request)
        {
            RawTransportResult result = await request.WaitAsync(TestTimeout);

            Assert.True(
                result.ReachedEof || result.Failure is IOException or SocketException,
                DescribeRawResult(result));
            Assert.Empty(result.ResponseBytes);
        }

        private static string DescribeRawResult(RawTransportResult result) =>
            $"stages={result.Stages}; reachedEof={result.ReachedEof}; " +
            $"responseBytes={result.ResponseBytes.Length}; " +
            $"failure={result.Failure?.GetType().Name ?? "<none>"}: {result.Failure?.Message ?? "<none>"}";

        private enum RawTransportStage
        {
            Connect,
            Write,
            Flush,
            Read,
        }

        private sealed class RawTransportTrace
        {
            private readonly ConcurrentQueue<RawTransportStage> _stages = new();
            private int _lastStage = -1;

            internal void Enter(RawTransportStage stage)
            {
                int current = (int)stage;
                if (Interlocked.Exchange(ref _lastStage, current) != current)
                    _stages.Enqueue(stage);
            }

            internal string Describe() => _stages.IsEmpty
                ? "<not-started>"
                : string.Join(" -> ", _stages);
        }

        private sealed record RawTransportResult(
            byte[] ResponseBytes,
            bool ReachedEof,
            Exception? Failure,
            string Stages);
    }
}
