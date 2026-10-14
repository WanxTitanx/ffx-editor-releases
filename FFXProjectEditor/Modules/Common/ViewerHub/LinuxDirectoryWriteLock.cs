using FFXProjectEditor.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── One cooperative Linux directory-lock lease ──
// Contract/flags: https://man7.org/linux/man-pages/man2/flock.2.html (2026-09-05).
// Independently opened descriptions contend; dup/another managed wrapper is not a new lease.
// MAINT: this never denies ordinary I/O, prevents arbitrary renames, or replaces byte/identity
// checks. The caller supplies a validated independent descriptor and keeps ownership on failure.
internal sealed class LinuxDirectoryWriteLock : IDisposable
{
    private SafeFileHandle? _ownedDirectory;
    // Null in product execution; the serialized filesystem tests synchronize an actual native
    // contention retry here to prove that capability operations do not wait for this lease.
    internal static Action? AfterContentionForTests { get; set; }

    private LinuxDirectoryWriteLock(SafeFileHandle ownedDirectory) => _ownedDirectory = ownedDirectory;

    internal static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Linux writer lock timeout must be within zero and ten seconds.");
    }

    internal static LinuxDirectoryWriteLock Acquire(SafeFileHandle independentDirectory,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        LinuxOutputFileSystem.EnsureSupported();
        ValidateTimeout(timeout);
        var clock = Stopwatch.StartNew();
        bool firstAttempt = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstAttempt && clock.Elapsed >= timeout)
                throw new IOException("Timed out waiting for a cooperating Linux output writer.");
            firstAttempt = false;
            if (TryAcquire(independentDirectory))
            {
                DebugLog.Info("Filesystem.LinuxOutput", "Acquired cooperative directory writer lock.");
                return new LinuxDirectoryWriteLock(independentDirectory);
            }

            AfterContentionForTests?.Invoke();
            TimeSpan remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new IOException("Timed out waiting for a cooperating Linux output writer.");
            int interval = Math.Max(1, (int)Math.Ceiling(Math.Min(25, remaining.TotalMilliseconds)));
            _ = cancellationToken.WaitHandle.WaitOne(interval);
        }
    }

    private static bool TryAcquire(SafeFileHandle directory)
    {
        bool acquired = false;
        try
        {
            directory.DangerousAddRef(ref acquired);
            if (Flock(directory.DangerousGetHandle().ToInt32(), 2 | 4) == 0) // LOCK_EX | LOCK_NB
                return true;
            int error = Marshal.GetLastPInvokeError();
            if (error == 11) return false; // Linux EWOULDBLOCK; not every IOException is contention.
            throw new LinuxReadFileSystem.LinuxNativeIOException("flock cooperative directory writer", error);
        }
        finally { if (acquired) directory.DangerousRelease(); }
    }

    public void Dispose()
    {
        // Closing this sole independent description releases only its lock, also after process
        // termination. No LOCK_UN on the capability's different retained directory description.
        Interlocked.Exchange(ref _ownedDirectory, null)?.Dispose();
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
}
