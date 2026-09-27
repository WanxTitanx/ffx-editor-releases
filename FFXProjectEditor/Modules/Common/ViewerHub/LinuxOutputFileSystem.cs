using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Linux x64 exact-descriptor create-only operations ──
// Sources: Linux UAPI and man-pages openat2(2), link(2), statx(2), statfs(2), fsync(2),
// https://man7.org/linux/man-pages/man2/ (2026-09-05). Pinning/ownership patterns adapted
// from internal Utilities/FFXResearchTools/Safety/LinuxOutputNativeFileSystem.cs; this
// boundary instead uses O_TMPFILE + exact-FD link, UID/mode checks, and typed errno.
// ABI verified by work/linux-b2a-abi-20260905-Y6EO5b/probe.c. No pathname/proc fallback.
// MAINT: ext-family and x64 only; name exchange retains both entries, but provides no expected-
// inode CAS, deletion or mandatory lock. See rename(2), same primary source/date above.
internal static class LinuxOutputFileSystem
{
    internal const uint RequiredMask = RequiredStatxMask | 2 | 4 | 8; // mode, nlink, uid
    internal const ulong AnonymousFileFlags = 0x410000 | 2 | OpenCloseOnExec;
    private const uint DirectoryMode = 0x1C0; // 0700
    private const ulong FileMode = 0x180; // 0600
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static uint EffectiveUserId
    {
        get { EnsureSupported(); return GetEffectiveUserId(); }
    }

    internal static void EnsureSupported()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Owned Linux output requires the Linux x86-64 ABI.");
    }

    internal static void ValidateLeaf(string leaf)
    {
        ValidateRelativePath(leaf);
        if (leaf.Contains('/') || StrictUtf8.GetByteCount(leaf) > 255)
            throw new ArgumentException("Linux output requires one direct child of at most 255 UTF-8 bytes.", nameof(leaf));
    }

    internal static OutputObservation Observe(SafeFileHandle handle, ushort type) =>
        DecodeOutput(ReadMetadata(handle, RequiredMask), type);

    internal static OutputObservation DecodeOutput(StatxBuffer value, ushort type)
    {
        if ((value.Mask & RequiredMask) != RequiredMask)
            throw new IOException("Linux output statx omitted required ownership fields.");
        return new(DecodeStatx(value, type), value.Mode, value.OwnerId, value.LinkCount);
    }

    internal static FileSystemReparseGuard.FileIdentity TagIdentity(LinuxObjectIdentity value) =>
        new(FileSystemReparseGuard.FileIdentityKind.Linux,
            ((ulong)value.DeviceMajor << 32) | value.DeviceMinor, value.Inode, value.MountId);

    internal static void RequireExtFileSystem(StatFsBuffer value)
    {
        if (value.Type != 0xEF53)
            throw new IOException("Owned Linux output requires a local ext-family filesystem.");
    }

    internal static void VerifyFileSystem(SafeFileHandle directory)
    {
        EnsureSupported();
        bool acquired = false;
        try
        {
            directory.DangerousAddRef(ref acquired);
            if (FStatFs(directory.DangerousGetHandle().ToInt32(), out StatFsBuffer value) != 0)
                throw new LinuxNativeIOException("fstatfs output directory", Marshal.GetLastPInvokeError());
            RequireExtFileSystem(value);
        }
        finally { if (acquired) directory.DangerousRelease(); }
    }

    // The explicitly selected absolute root may include mount points. Each component is still
    // opened without symlinks. Actual child file operations below the final root deny mount hops.
    internal static SafeFileHandle OpenDirectoryComponent(SafeFileHandle parent, string leaf)
    {
        ValidateLeaf(leaf);
        return OpenChild(parent, leaf, OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec,
            0, ResolveBeneath | ResolveNoSymbolicLinks | ResolveNoMagicLinks);
    }

    internal static void CreateDirectoryComponent(SafeFileHandle parent, string leaf)
    {
        EnsureSupported();
        ValidateLeaf(leaf);
        bool acquired = false;
        try
        {
            parent.DangerousAddRef(ref acquired);
            if (MkdirAt(parent.DangerousGetHandle().ToInt32(), leaf, DirectoryMode) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != 17) throw new LinuxNativeIOException("mkdirat output component", error);
            }
        }
        finally { if (acquired) parent.DangerousRelease(); }
    }

    internal static SafeFileHandle CreateAnonymous(SafeFileHandle parent) =>
        // Fixed kernel-relative dot, never supplied by callers; no public path validation bypass.
        OpenChild(parent, ".", AnonymousFileFlags, FileMode,
            ResolveBeneath | ResolveNoSymbolicLinks | ResolveNoMagicLinks | ResolveNoCrossDevice);

    internal static SafeFileHandle OpenOwnedChildDirectory(SafeFileHandle parent, string leaf)
    {
        ValidateLeaf(leaf);
        return OpenChild(parent, leaf, OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec,
            0, ResolveBeneath | ResolveNoSymbolicLinks | ResolveNoMagicLinks | ResolveNoCrossDevice);
    }

    internal static SafeFileHandle OpenIndependentDirectory(SafeFileHandle parent) =>
        // A fresh open description, not dup: flock must contend even for two leases of one root.
        OpenChild(parent, ".", OpenReadOnly | OpenDirectory | OpenCloseOnExec | OpenNoFollow, 0,
            ResolveBeneath | ResolveNoSymbolicLinks | ResolveNoMagicLinks | ResolveNoCrossDevice);

    internal static void LinkNew(SafeFileHandle file, SafeFileHandle parent, string leaf)
    {
        EnsureSupported();
        ValidateLeaf(leaf);
        bool fileAcquired = false, parentAcquired = false;
        try
        {
            file.DangerousAddRef(ref fileAcquired);
            parent.DangerousAddRef(ref parentAcquired);
            if (LinkAt(file.DangerousGetHandle().ToInt32(), string.Empty,
                parent.DangerousGetHandle().ToInt32(), leaf, AtEmptyPath) != 0)
                throw new LinuxNativeIOException("linkat exact-FD create-only publication", Marshal.GetLastPInvokeError());
        }
        finally
        {
            if (parentAcquired) parent.DangerousRelease();
            if (fileAcquired) file.DangerousRelease();
        }
    }

    internal static void Sync(SafeFileHandle handle)
    {
        EnsureSupported();
        bool acquired = false;
        try
        {
            handle.DangerousAddRef(ref acquired);
            if (Fsync(handle.DangerousGetHandle().ToInt32()) != 0)
                throw new LinuxNativeIOException("fsync retained output object", Marshal.GetLastPInvokeError());
        }
        finally { if (acquired) handle.DangerousRelease(); }
    }

    // RENAME_EXCHANGE atomically swaps two names, including unexpected objects at those names.
    // Higher layers retain both descriptors and verify the result; there is no unsafe fallback.
    internal static void Exchange(SafeFileHandle parent, string firstLeaf, string secondLeaf)
    {
        EnsureSupported();
        ValidateLeaf(firstLeaf);
        ValidateLeaf(secondLeaf);
        bool acquired = false;
        try
        {
            parent.DangerousAddRef(ref acquired);
            int descriptor = parent.DangerousGetHandle().ToInt32();
            if (RenameAt2(descriptor, firstLeaf, descriptor, secondLeaf, 2) != 0)
                throw new LinuxNativeIOException("renameat2 retained name exchange", Marshal.GetLastPInvokeError());
        }
        finally { if (acquired) parent.DangerousRelease(); }
    }

    private static SafeFileHandle OpenChild(SafeFileHandle parent, string path, ulong flags, ulong mode, ulong resolve)
    {
        EnsureSupported();
        bool acquired = false;
        try
        {
            parent.DangerousAddRef(ref acquired);
            var how = new OpenHow { Flags = flags, Mode = mode, Resolve = resolve };
            long result = OpenAt2(OpenAt2SyscallNumber, parent.DangerousGetHandle().ToInt32(),
                path, in how, (nuint)Marshal.SizeOf<OpenHow>());
            if (result < 0)
                throw new LinuxNativeIOException("openat2 retained output object", Marshal.GetLastPInvokeError());
            return new SafeFileHandle(new IntPtr(result), ownsHandle: true);
        }
        finally { if (acquired) parent.DangerousRelease(); }
    }

    internal readonly record struct OutputObservation(
        LinuxFileObservation File, ushort Mode, uint OwnerId, uint LinkCount);

    [StructLayout(LayoutKind.Explicit, Size = 120)]
    internal struct StatFsBuffer
    {
        [FieldOffset(0)] internal long Type;
    }

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long OpenAt2(long number, int directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, in OpenHow how, nuint size);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string leaf, uint mode);

    [DllImport("libc", EntryPoint = "linkat", SetLastError = true)]
    private static extern int LinkAt(int file, [MarshalAs(UnmanagedType.LPUTF8Str)] string empty,
        int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string leaf, int flags);

    [DllImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static extern int FStatFs(int directory, out StatFsBuffer value);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(int firstDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string firstLeaf,
        int secondDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string secondLeaf, uint flags);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
