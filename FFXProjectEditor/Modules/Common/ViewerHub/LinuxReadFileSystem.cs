using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Linux x86-64 retained read boundary ─────────────────────────────────────────
// ABI declarations derive from Linux UAPI openat2.h/stat.h and Linux man-pages:
// https://man7.org/linux/man-pages/man2/openat2.2.html and /statx.2.html (2026-09-05).
// Adapted from the internal read-only reference Utilities/FFXResearchTools/Safety/
// LinuxNativeFileSystem.cs (2026-09-05): typed errno, ViewerHub integration, and a .NET 8
// string.Split path walk replace the newer-framework span Split enumerator.
// MAINT: this is not a POSIX abstraction or a sandbox. Never add a pathname fallback after a
// refused syscall. Retained descriptors do not freeze bytes, deny hardlinks, or preserve names.
internal static class LinuxReadFileSystem
{
    internal const long OpenAt2SyscallNumber = 437;
    internal const int AtCurrentDirectory = -100;
    internal const int AtEmptyPath = 0x1000;
    internal const ulong OpenReadOnly = 0;
    internal const ulong OpenNonBlocking = 0x800;
    internal const ulong OpenDirectory = 0x10000;
    internal const ulong OpenNoFollow = 0x20000;
    internal const ulong OpenCloseOnExec = 0x80000;
    internal const ulong ResolveNoCrossDevice = 1;
    internal const ulong ResolveNoMagicLinks = 2;
    internal const ulong ResolveNoSymbolicLinks = 4;
    internal const ulong ResolveBeneath = 8;
    internal const uint StatxType = 1;
    internal const uint StatxModifiedTime = 64;
    internal const uint StatxChangedTime = 128;
    internal const uint StatxInode = 256;
    internal const uint StatxSize = 512;
    internal const uint StatxMountId = 4096;
    internal const uint RequiredStatxMask =
        StatxType | StatxModifiedTime | StatxChangedTime | StatxInode | StatxSize | StatxMountId;
    internal const ushort FileTypeMask = 0xF000;
    internal const ushort DirectoryType = 0x4000;
    internal const ushort RegularFileType = 0x8000;
    private const int ErrorNoEntry = 2;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsSupported => OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64;

    internal static SafeFileHandle OpenRoot(string canonicalAbsolutePath)
    {
        EnsureSupported();
        canonicalAbsolutePath = NormalizeAbsoluteRoot(canonicalAbsolutePath);
        return OpenAt(AtCurrentDirectory, canonicalAbsolutePath, new OpenHow
        {
            Flags = OpenReadOnly | OpenDirectory | OpenCloseOnExec | OpenNoFollow,
            Mode = 0,
            Resolve = ResolveNoSymbolicLinks | ResolveNoMagicLinks,
        });
    }

    internal static SafeFileHandle OpenRead(SafeFileHandle root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(root);
        EnsureSupported();
        ValidateRelativePath(relativePath);

        bool acquired = false;
        try
        {
            // Pin dirfd through the syscall. IsClosed followed by DangerousGetHandle alone would
            // allow a racing Dispose to recycle the descriptor before the kernel consumes it.
            root.DangerousAddRef(ref acquired);
            return OpenAt(root.DangerousGetHandle().ToInt32(), relativePath, new OpenHow
            {
                Flags = OpenReadOnly | OpenNonBlocking | OpenCloseOnExec | OpenNoFollow,
                Mode = 0,
                Resolve = ResolveBeneath | ResolveNoSymbolicLinks |
                    ResolveNoMagicLinks | ResolveNoCrossDevice,
            });
        }
        finally
        {
            if (acquired)
                root.DangerousRelease();
        }
    }

    internal static LinuxFileObservation Observe(SafeFileHandle handle, ushort expectedType) =>
        DecodeStatx(ReadMetadata(handle, RequiredStatxMask), expectedType);

    // Shared syscall/pinning only: output ownership requires additional fields, while the
    // established B1 read decoder keeps its original requested mask and acceptance contract.
    internal static StatxBuffer ReadMetadata(SafeFileHandle handle, uint requestedMask)
    {
        ArgumentNullException.ThrowIfNull(handle);
        EnsureSupported();

        bool acquired = false;
        try
        {
            handle.DangerousAddRef(ref acquired);
            int result = Statx(
                handle.DangerousGetHandle().ToInt32(),
                string.Empty,
                AtEmptyPath,
                requestedMask,
                out StatxBuffer value);
            if (result != 0)
                throw NativeRefusal("statx retained-object observation", Marshal.GetLastPInvokeError());
            return value;
        }
        finally
        {
            if (acquired)
                handle.DangerousRelease();
        }
    }

    internal static bool IsMissing(Exception error) =>
        error is LinuxNativeIOException { Errno: ErrorNoEntry };

    internal static LinuxFileObservation DecodeStatx(StatxBuffer value, ushort expectedType)
    {
        if ((value.Mask & RequiredStatxMask) != RequiredStatxMask)
            throw new IOException("Linux statx omitted a required identity or observation field.");
        if ((value.Mode & FileTypeMask) != expectedType)
            throw new IOException("The retained Linux object has an unexpected file type.");
        if (value.Size > long.MaxValue ||
            value.Modified.Nanoseconds >= 1_000_000_000 ||
            value.Changed.Nanoseconds >= 1_000_000_000)
            throw new IOException("Linux statx returned an unsupported size or invalid timestamp.");

        return new LinuxFileObservation(
            new LinuxObjectIdentity(
                value.DeviceMajor,
                value.DeviceMinor,
                value.Inode,
                value.MountId),
            checked((long)value.Size),
            value.Modified.Seconds,
            value.Modified.Nanoseconds,
            value.Changed.Seconds,
            value.Changed.Nanoseconds);
    }

    internal static string NormalizeAbsoluteRoot(string path)
    {
        ValidateCharacters(path);
        if (!path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A Linux root must be an absolute slash path.", nameof(path));

        string normalized = path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)
            ? path.Substring(0, path.Length - 1)
            : path;
        ValidateComponents(normalized, absolute: true);
        return normalized;
    }

    internal static void ValidateAbsolutePath(string path)
    {
        ValidateCharacters(path);
        if (!path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A Linux path must be absolute.", nameof(path));
        ValidateComponents(path, absolute: true);
    }

    internal static void ValidateRelativePath(string path)
    {
        ValidateCharacters(path);
        if (path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A Linux child path must be relative.", nameof(path));
        ValidateComponents(path, absolute: false);
    }

    private static void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Retained Linux reads require the Linux x86-64 ABI.");
    }

    private static SafeFileHandle OpenAt(int directory, string path, OpenHow how)
    {
        long result = OpenAt2(
            OpenAt2SyscallNumber,
            directory,
            path,
            in how,
            checked((nuint)Marshal.SizeOf<OpenHow>()));
        if (result < 0)
            throw NativeRefusal("openat2 read-only lookup", Marshal.GetLastPInvokeError());

        // Unix descriptor zero is valid. SafeFileHandle owns exactly this successful syscall result.
        return new SafeFileHandle(new IntPtr(result), ownsHandle: true);
    }

    private static LinuxNativeIOException NativeRefusal(string operation, int error) =>
        new(operation, error);

    private static void ValidateCharacters(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || path.Contains('\0') || path.Contains('\\'))
            throw new ArgumentException("Linux paths must use unambiguous slash syntax.", nameof(path));
        try
        {
            _ = StrictUtf8.GetByteCount(path);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Linux path contains malformed UTF-16.", nameof(path), exception);
        }
    }

    private static void ValidateComponents(string path, bool absolute)
    {
        if (absolute && path == "/")
            return;

        string remainder = absolute ? path.Substring(1) : path;
        foreach (string segment in remainder.Split('/', StringSplitOptions.None))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                throw new ArgumentException("Linux path contains an empty or dot component.", nameof(path));
        }
    }

    internal sealed class LinuxNativeIOException : IOException
    {
        internal LinuxNativeIOException(string operation, int errno)
            : base(
                $"Linux {operation} refused (errno {errno}); no weaker fallback was attempted.",
                new Win32Exception(errno))
        {
            Operation = operation;
            Errno = errno;
        }

        internal string Operation { get; }
        internal int Errno { get; }
    }

    internal readonly record struct LinuxObjectIdentity(
        uint DeviceMajor,
        uint DeviceMinor,
        ulong Inode,
        ulong MountId);

    internal readonly record struct LinuxFileObservation(
        LinuxObjectIdentity Identity,
        long Length,
        long ModifiedSeconds,
        uint ModifiedNanoseconds,
        long ChangedSeconds,
        uint ChangedNanoseconds);

    // ── Fixed Linux UAPI layouts ─────────────────────────────────────────────────
    // Verified by work/linux-bootstrap-20260905-EOqe9W/linux-b1-abi-probe.c:
    // open_how=24, statx=256, timestamp=16. These offsets are not libc struct stat.
    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenHow
    {
        internal ulong Flags;
        internal ulong Mode;
        internal ulong Resolve;
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    internal struct StatxTimestamp
    {
        internal long Seconds;
        internal uint Nanoseconds;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct StatxBuffer
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(16)] internal uint LinkCount;
        [FieldOffset(20)] internal uint OwnerId;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(96)] internal StatxTimestamp Changed;
        [FieldOffset(112)] internal StatxTimestamp Modified;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
        [FieldOffset(144)] internal ulong MountId;
    }

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long OpenAt2(
        long number,
        int directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        in OpenHow how,
        nuint size);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out StatxBuffer value);
}
