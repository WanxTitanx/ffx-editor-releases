using FFXProjectEditor.Core;
using FFXProjectEditor.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── One selected, retained Linux output directory ──
// Create-only publication links an already verified anonymous inode. Pre-link disposal removes
// only that unnamed inode; after linking, failures preserve evidence and fault this capability.
// MAINT: finite identity/byte observations are not mandatory locks, permanent path membership,
// a same-UID sandbox, an atomic snapshot or a power-loss guarantee. Never auto-unlink on failure.
internal sealed partial class LinuxOwnedOutputDirectory : IDisposable
{
    internal const int MaximumBytes = 64 * 1024 * 1024;
    private const string LogCategory = "Filesystem.LinuxOutput";
    private const ushort SharedWriteBits = 0x12; // group-write | other-write
    private const ushort SetIdBits = 0xC00;
    private const ushort SpecialModeBits = 0xE00; // setuid | setgid | sticky, NOT owner-read (0x100)
    private readonly SafeFileHandle _directory;
    private readonly LinuxObjectIdentity _identity;
    private readonly LinuxOwnedOutputDirectory? _parent;
    private readonly int _childDepth;
    private readonly object _gate = new();
    private bool _faulted;
    private bool _disposed;

    private LinuxOwnedOutputDirectory(string fullPath, SafeFileHandle directory, LinuxObjectIdentity identity,
        LinuxOwnedOutputDirectory? parent = null)
    {
        FullPath = fullPath;
        _directory = directory;
        _identity = identity;
        _parent = parent;
        _childDepth = parent is null ? 0 : parent._childDepth + 1;
    }

    internal string FullPath { get; }
    // Null in product execution; tests use the existing non-parallel filesystem hook collection.
    internal static Action<string, string>? BeforeOperationForTests { get; set; }

    // Callers explicitly hold this cooperative lease across their entire read/check/write
    // transaction. PublishNew alone remains create-only and does not silently acquire/reenter it.
    internal IDisposable AcquireWriteLock(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        LinuxDirectoryWriteLock.ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        SafeFileHandle independent;
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            independent = LinuxOutputFileSystem.OpenIndependentDirectory(_directory);
        }

        LinuxDirectoryWriteLock? lease = null;
        try
        {
            var observed = LinuxOutputFileSystem.Observe(independent, DirectoryType);
            RequireOwnedDirectory(observed);
            if (observed.File.Identity != _identity)
                throw new IOException("Linux writer lock descriptor changed directory identity.");
            BeforeOperationForTests?.Invoke("lock-before-acquire", FullPath);
            // Never wait while holding _gate: release/disposal/other independent leases must
            // remain possible. The dedicated description remains alive throughout native retries.
            lease = LinuxDirectoryWriteLock.Acquire(independent, timeout, cancellationToken);
            BeforeOperationForTests?.Invoke("lock-acquired", FullPath);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                EnsureUsable();
                VerifyRoot();
            }
            return lease;
        }
        catch
        {
            lease?.Dispose();
            independent.Dispose();
            throw;
        }
    }

    // Recovery inspection must not create missing ancestors; explicit writers keep the existing path.
    internal static LinuxOwnedOutputDirectory OpenExisting(string absolutePath) => Open(absolutePath, createMissing: false);

    internal static LinuxOwnedOutputDirectory OpenOrCreate(string absolutePath) => Open(absolutePath, createMissing: true);

    private static LinuxOwnedOutputDirectory Open(string absolutePath, bool createMissing)
    {
        LinuxOutputFileSystem.EnsureSupported();
        string root = NormalizeAbsoluteRoot(absolutePath);
        if (PathGuard.IsSystemDirectory(root) || IsProtectedLinuxRoot(root))
            throw new ArgumentException("A system directory cannot be selected for owned Linux output.", nameof(absolutePath));

        SafeFileHandle current = OpenRoot("/");
        try
        {
            foreach (string component in root[1..].Split('/'))
            {
                SafeFileHandle next;
                try { next = LinuxOutputFileSystem.OpenDirectoryComponent(current, component); }
                catch (Exception error) when (createMissing && IsMissing(error))
                {
                    // Reject unsupported storage before even mkdirat, not just before file
                    // publication. Existing mount points can still be walked without mutation.
                    LinuxOutputFileSystem.VerifyFileSystem(current);
                    LinuxOutputFileSystem.CreateDirectoryComponent(current, component);
                    LinuxOutputFileSystem.Sync(current);
                    next = LinuxOutputFileSystem.OpenDirectoryComponent(current, component);
                }
                current.Dispose();
                current = next;
            }
            LinuxOutputFileSystem.VerifyFileSystem(current);
            var observed = LinuxOutputFileSystem.Observe(current, DirectoryType);
            RequireOwnedDirectory(observed);
            var result = new LinuxOwnedOutputDirectory(root, current, observed.File.Identity);
            result.VerifyRoot();
            DebugLog.Info(LogCategory, "Retained selected Linux output directory.");
            return result; // Ownership transfers only after the factory's final observation succeeds.
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    internal Snapshot? ReadSnapshot(string leaf) => ReadSnapshot(leaf, MaximumBytes);

    // Receipt readers need an allocation bound on the opened object, not an earlier pathname
    // metadata check. The default overload and all writer verification retain their64MiB limit.
    internal Snapshot? ReadSnapshot(string leaf, int maximumBytes)
    {
        LinuxOutputFileSystem.ValidateLeaf(leaf);
        if (maximumBytes is < 0 or > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            SafeFileHandle file;
            try { file = OpenRead(_directory, leaf); }
            catch (Exception error) when (IsMissing(error))
            {
                VerifyRoot();
                return null; // Only the initial lookup can classify this operation as missing.
            }
            using (file)
            {
                var before = LinuxOutputFileSystem.Observe(file, RegularFileType);
                RequireOwnedFile(before, expectedLinks: 1, requirePrivateMode: false);
                BeforeOperationForTests?.Invoke("snapshot-open", Path.Combine(FullPath, leaf));
                byte[] bytes = ReadObservedBytes(file, before, maximumBytes);
                using var named = OpenRead(_directory, leaf);
                if (LinuxOutputFileSystem.Observe(named, RegularFileType) != before)
                    throw new IOException("Linux snapshot leaf no longer names the observed file.");
                VerifyRoot();
                return new Snapshot(bytes, before);
            }
        }
    }

    internal Publication PublishNew(string leaf, ReadOnlySpan<byte> bytes)
    {
        LinuxOutputFileSystem.ValidateLeaf(leaf);
        if (bytes.Length > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Linux output exceeds the 64 MiB domain limit.");
        // Capture before any hook or I/O. Caller ownership is not retained across the operation.
        byte[] captured = bytes.ToArray();
        string path = Path.Combine(FullPath, leaf);
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            using var file = LinuxOutputFileSystem.CreateAnonymous(_directory);
            var initial = LinuxOutputFileSystem.Observe(file, RegularFileType);
            RequireOwnedFile(initial, expectedLinks: 0, requirePrivateMode: true);
            bool linked = false;
            try
            {
                RandomAccess.Write(file, captured, 0);
                LinuxOutputFileSystem.Sync(file);
                VerifyPreparedFile(file, captured, initial.File.Identity, expectedLinks: 0);
                BeforeOperationForTests?.Invoke("create-before-link", path);
                VerifyRoot();
                VerifyPreparedFile(file, captured, initial.File.Identity, expectedLinks: 0);
                LinuxOutputFileSystem.LinkNew(file, _directory, leaf);
                linked = true;

                BeforeOperationForTests?.Invoke("create-linked", path);
                LinuxOutputFileSystem.Sync(_directory);
                VerifyPreparedFile(file, captured, initial.File.Identity, expectedLinks: 1);
                using var named = OpenRead(_directory, leaf);
                VerifyPreparedFile(named, captured, initial.File.Identity, expectedLinks: 1);
                VerifyRoot();
                DebugLog.Info(LogCategory, $"Create-only publication verified ({captured.Length} bytes).");
                return new Publication(path, LinuxOutputFileSystem.TagIdentity(initial.File.Identity),
                    Convert.ToHexString(SHA256.HashData(captured)), captured.Length);
            }
            catch (Exception error) when (linked)
            {
                _faulted = true;
                DebugLog.Error(LogCategory, "Publication is uncertain; retained output was not deleted.", error);
                throw new LinuxPublicationUncertainException(path, error);
            }
        }
    }

    // ── Observed byte consistency ──
    // Reads use only retained descriptors, exact length plus EOF, and complete requested metadata.
    // A same-UID adversary can still perform unobserved ABA writes; callers must not infer a lock.
    private static byte[] ReadObservedBytes(SafeFileHandle file,
        LinuxOutputFileSystem.OutputObservation before, int maximumBytes = MaximumBytes)
    {
        if (before.File.Length > maximumBytes)
            throw new IOException(maximumBytes == MaximumBytes
                ? "Linux output snapshot exceeds the 64 MiB domain limit."
                : $"Linux output snapshot exceeds the requested {maximumBytes} byte limit.");
        byte[] bytes = new byte[checked((int)before.File.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int count = RandomAccess.Read(file, bytes.AsSpan(offset), offset);
            if (count == 0) throw new IOException("Linux output ended before its observed length.");
            offset += count;
        }
        Span<byte> beyond = stackalloc byte[1];
        if (RandomAccess.Read(file, beyond, bytes.Length) != 0 ||
            LinuxOutputFileSystem.Observe(file, RegularFileType) != before)
            throw new IOException("Linux output changed during the retained read.");
        return bytes;
    }

    private static void VerifyPreparedFile(SafeFileHandle file, ReadOnlySpan<byte> expected,
        LinuxObjectIdentity identity, uint expectedLinks)
    {
        var observed = LinuxOutputFileSystem.Observe(file, RegularFileType);
        RequireOwnedFile(observed, expectedLinks, requirePrivateMode: true);
        if (observed.File.Identity != identity || observed.File.Length != expected.Length ||
            !ReadObservedBytes(file, observed).AsSpan().SequenceEqual(expected))
            throw new IOException("Linux publication differs from its prepared inode or bytes.");
    }

    private void VerifyRoot()
    {
        _parent?.VerifyChildAnchor(Path.GetFileName(FullPath), _identity);
        var retained = LinuxOutputFileSystem.Observe(_directory, DirectoryType);
        RequireOwnedDirectory(retained);
        using var current = OpenRoot(FullPath); // No links in any ancestor; observation, not the write target.
        var named = LinuxOutputFileSystem.Observe(current, DirectoryType);
        RequireOwnedDirectory(named);
        if (retained.File.Identity != _identity || named.File.Identity != _identity)
            throw new IOException("The selected Linux output directory changed identity or pathname.");
    }

    private static void RequireOwnedDirectory(LinuxOutputFileSystem.OutputObservation value)
    {
        if (value.OwnerId != LinuxOutputFileSystem.EffectiveUserId ||
            (value.Mode & 0x1C0) != 0x1C0 || (value.Mode & (SetIdBits | SharedWriteBits)) != 0)
            throw new IOException("Linux output directory must be owner-accessible without shared write or set-ID permissions.");
    }

    private static void RequireOwnedFile(LinuxOutputFileSystem.OutputObservation value, uint expectedLinks, bool requirePrivateMode)
    {
        if (value.OwnerId != LinuxOutputFileSystem.EffectiveUserId || value.LinkCount != expectedLinks ||
            (value.Mode & (SpecialModeBits | SharedWriteBits)) != 0 || (value.Mode & 0x100) == 0 ||
            (requirePrivateMode && (value.Mode & 0xFFF) != 0x180))
            throw new IOException("Linux output file has unexpected owner, links or permissions.");
    }

    private static bool IsProtectedLinuxRoot(string root)
    {
        foreach (string prefix in new[] { "/proc", "/sys", "/dev", "/run", "/boot", "/root", "/lib", "/lib64" })
            if (root == prefix || root.StartsWith(prefix + "/", StringComparison.Ordinal)) return true;
        return false;
    }

    private void EnsureUsable()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LinuxOwnedOutputDirectory));
        if (_faulted) throw new InvalidOperationException("Linux output capability is faulted after an uncertain publication.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _directory.Dispose();
        }
    }

    internal sealed class Snapshot
    {
        private readonly byte[] _bytes;
        internal Snapshot(ReadOnlySpan<byte> bytes, LinuxOutputFileSystem.OutputObservation observation)
        {
            _bytes = bytes.ToArray();
            Observation = observation;
        }
        internal ReadOnlySpan<byte> Bytes => _bytes;
        internal FileSystemReparseGuard.FileIdentity Identity => LinuxOutputFileSystem.TagIdentity(Observation.File.Identity);
        internal LinuxOutputFileSystem.OutputObservation Observation { get; }
    }
    internal readonly record struct Publication(string FullPath, FileSystemReparseGuard.FileIdentity Identity, string Sha256, long Length);
}

internal sealed class LinuxPublicationUncertainException : IOException
{
    internal LinuxPublicationUncertainException(string path, Exception cause)
        : base("Linux publication could not be verified after linking; output was preserved and must be inspected before retrying.", cause)
        => PossiblyPublishedPath = path;
    internal string PossiblyPublishedPath { get; }
}
