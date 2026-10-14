using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Cooperative directory locking and exact lease lifecycle ──
// Independent open descriptions must contend even inside one capability. A separate Python
// process verifies kernel interop; these locks intentionally do not deny ordinary same-UID I/O.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxDirectoryWriteLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentLeases_ContendUntilRelease(bool sameCapability)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var first = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var other = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        var contender = sameCapability ? first : other;
        using var held = first.AcquireWriteLock(TimeSpan.Zero);
        var watch = Stopwatch.StartNew();
        Assert.Throws<IOException>(() => contender.AcquireWriteLock(TimeSpan.FromMilliseconds(75)));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(65));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        held.Dispose();
        using var acquired = contender.AcquireWriteLock(TimeSpan.Zero);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root)); // No lock sidecar artifacts.
    }

    [Fact]
    public async Task WaitingLease_AcquiresAfterRelease_WithoutHoldingTheManagedGate()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var held = output.AcquireWriteLock(TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        using var resumeContender = new ManualResetEventSlim();
        var contentionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IDisposable>? waiting = null;
        Task<LinuxOwnedOutputDirectory.Snapshot?>? gateProbe = null;
        LinuxDirectoryWriteLock.AfterContentionForTests = () =>
        {
            contentionReached.TrySetResult();
            if (!resumeContender.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Test did not release the synchronized native contender.");
        };
        try
        {
            waiting = Task.Factory.StartNew(
                () => output.AcquireWriteLock(TimeSpan.FromSeconds(10), cancellation.Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await contentionReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Freeze a real EWOULDBLOCK retry, then require an operation that actually takes
            // the same _gate to finish before either the contender or held lease is released.
            gateProbe = Task.Factory.StartNew(() => output.ReadSnapshot("absent.bin"),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.Null(await gateProbe.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(waiting.IsCompleted);
            resumeContender.Set();
            held.Dispose();
            using var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            LinuxDirectoryWriteLock.AfterContentionForTests = null;
            resumeContender.Set();
            held.Dispose();
            cancellation.Cancel();
            if (waiting is not null)
            {
                try { using var finalLease = await waiting.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { }
            }
            if (gateProbe is not null) await gateProbe.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void NonContentionNativeError_IsTyped_AndDoesNotWaitForTimeout()
    {
        if (!CanRunNative()) return;
        // -1 is never a live Unix descriptor; no process-owned handle is closed or borrowed.
        using var invalid = new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
        var watch = Stopwatch.StartNew();
        var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() =>
            LinuxDirectoryWriteLock.Acquire(invalid, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal(9, error.Errno); // EBADF, not EWOULDBLOCK(11) and not a timeout IOException.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Unexpected native-error wait: {watch.Elapsed}");
        Assert.False(invalid.IsClosed); // Failed acquisition leaves ownership with the caller.
    }

    [Fact]
    public void SeparateDirectories_DoNotContend_AndOrdinaryWritesRemainPossible()
    {
        if (!CanRunNative()) return;
        using var one = new Fixture();
        using var two = new Fixture();
        using var first = LinuxOwnedOutputDirectory.OpenOrCreate(one.Root);
        using var second = LinuxOwnedOutputDirectory.OpenOrCreate(two.Root);
        using var firstLock = first.AcquireWriteLock(TimeSpan.Zero);
        using var secondLock = second.AcquireWriteLock(TimeSpan.Zero);
        File.WriteAllText(Path.Combine(one.Root, "advisory-only"), "ordinary write");
        Assert.Equal("ordinary write", File.ReadAllText(Path.Combine(one.Root, "advisory-only")));
        first.PublishNew("cooperating.bin", new byte[] { 1 });
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(one.Root, "cooperating.bin")));
    }

    [Fact]
    public async Task Cancellation_AndDispose_ReleaseOnlyTheAffectedLease()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => output.AcquireWriteLock(TimeSpan.Zero, canceled.Token));
        using var held = output.AcquireWriteLock(TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> waiting = Task.Run(() => output.AcquireWriteLock(TimeSpan.FromSeconds(2), cancellation.Token));
        await Task.Delay(50);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { using var unexpected = await waiting; });
        Assert.Throws<IOException>(() => output.AcquireWriteLock(TimeSpan.Zero));
        held.Dispose();
        using var acquired = output.AcquireWriteLock(TimeSpan.Zero);
        held.Dispose(); // Disposing an older lease again must not unlock this new one.
        Assert.Throws<IOException>(() => output.AcquireWriteLock(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void InvalidTimeout_IsRejected(int milliseconds)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.Throws<ArgumentOutOfRangeException>(() => output.AcquireWriteLock(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData("lock-before-acquire")]
    [InlineData("lock-acquired")]
    public void RootRenameDuringAcquisition_IsRefused_AndTemporaryLockReleased(string hook)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string root = Path.Combine(fixture.Root, "selected");
        string moved = Path.Combine(fixture.Root, "moved");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(root);
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook) return;
            Directory.Move(root, moved);
            TestDirectory.CreatePrivate(root);
        };
        try { Assert.Throws<IOException>(() => output.AcquireWriteLock(TimeSpan.Zero)); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        using var movedOutput = LinuxOwnedOutputDirectory.OpenOrCreate(moved);
        using var afterRefusal = movedOutput.AcquireWriteLock(TimeSpan.Zero);
        Assert.Empty(Directory.GetFileSystemEntries(moved));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void TimeoutAndSuccessLoops_DoNotLeak_AndCapabilityDisposeIsEnforced()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var held = output.AcquireWriteLock(TimeSpan.Zero);
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int i = 0; i < 64; i++) Assert.Throws<IOException>(() => output.AcquireWriteLock(TimeSpan.Zero));
        held.Dispose();
        for (int i = 0; i < 64; i++) { using var lease = output.AcquireWriteLock(TimeSpan.Zero); }
        int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        Assert.True(after <= before + 3, $"Descriptors: {before} -> {after}");
        using var survivingLease = output.AcquireWriteLock(TimeSpan.Zero);
        output.Dispose();
        Assert.Throws<ObjectDisposedException>(() => output.AcquireWriteLock(TimeSpan.Zero));
        using var independent = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.Throws<IOException>(() => independent.AcquireWriteLock(TimeSpan.Zero));
        survivingLease.Dispose();
        using var final = independent.AcquireWriteLock(TimeSpan.Zero);
    }

    [Fact]
    public async Task SeparateNativeProcess_ContendsWithManagedLease_AndExitReleasesItsLock()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        using var held = output.AcquireWriteLock(TimeSpan.Zero);
        // Independently composed stdlib helper; no shell and no generated external source file.
        const string script = """
            import fcntl, os, sys
            fd = os.open(sys.argv[1], os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                print('UNEXPECTED_ACQUIRE', flush=True)
                sys.exit(1)
            except BlockingIOError:
                print('BLOCKED', flush=True)
            assert sys.stdin.readline().strip() == 'RELEASE'
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            print('ACQUIRED', flush=True)
            # Process exit, deliberately without explicit unlock or close.
            """;
        var start = new ProcessStartInfo("python3")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script); start.ArgumentList.Add(fixture.Root);
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("BLOCKED", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            held.Dispose();
            await process.StandardInput.WriteLineAsync("RELEASE");
            await process.StandardInput.FlushAsync();
            Assert.Equal("ACQUIRED", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, await process.StandardError.ReadToEndAsync());
            using var afterChildExit = output.AcquireWriteLock(TimeSpan.Zero);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (LinuxReadFileSystem.IsSupported) return true;
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOwnedOutputDirectory.OpenOrCreate("/tmp/ffx-lock-refusal"));
        return false;
    }

    [SupportedOSPlatform("linux")]
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = TestDirectory.CreatePrivate(Path.Combine(TestDataPaths.RepoRoot,
            "work", "linux-lock-tests", Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
