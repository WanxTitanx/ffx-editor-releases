using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Handle-backed filesystem trust boundary ──────────────────────────────────────
// Path.GetFullPath proves only lexical containment. Security-sensitive reads consume the exact
// SafeFileHandle whose final target and file ID were verified. App-owned mutations walk directories
// through NtCreateFile RootDirectory capabilities with OBJ_DONT_REPARSE; creation, promotion, and
// deletion never resolve the protected leaf through a pathname after that.
internal static class FileSystemReparseGuard
{
    private const uint ObjCaseInsensitive = 0x40;
    private const uint ObjDontReparse = 0x1000;
    private const uint FileReadData = 0x0001;
    private const uint FileListDirectory = 0x0001;
    private const uint FileWriteData = 0x0002;
    private const uint FileAddFile = 0x0002;
    private const uint FileAddSubdirectory = 0x0004;
    private const uint FileTraverse = 0x0020;
    private const uint FileDeleteChild = 0x0040;
    private const uint FileReadAttributes = 0x0080;
    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeReparsePoint = 0x400;
    private const uint FileDirectoryFile = 0x1;
    private const uint FileWriteThrough = 0x2;
    private const uint FileSynchronousIoNonAlert = 0x20;
    private const uint FileNonDirectoryFile = 0x40;
    private const uint FileOpen = 1;
    private const uint FileCreate = 2;
    private const uint FileOpenIf = 3;
    private const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    private const int StatusObjectPathNotFound = unchecked((int)0xC000003A);
    private const int StatusNoSuchFile = unchecked((int)0xC000000F);
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private const int FileDispositionInfoClass = 4;
    private const int FileRenameInformationClass = 10;
    private const int FileRenameInformationExClass = 65;
    private const uint FileRenameFlagReplaceIfExists = 0x1;
    private const uint FileRenameFlagPosixSemantics = 0x2;
    private const uint LockfileFailImmediately = 0x1;
    private const uint LockfileExclusiveLock = 0x2;

    internal enum VerifiedOpenResult { Success, NotFound, Rejected }

    internal enum FileIdentityKind { Windows, Linux }

    internal readonly record struct FileIdentity(
        FileIdentityKind Kind,
        ulong VolumeSerialNumber,
        ulong FileIdLow,
        ulong FileIdHigh);

    internal sealed class VerifiedReadFile : IDisposable
    {
        internal VerifiedReadFile(
            FileStream stream,
            string fullPath,
            FileIdentity identity,
            bool ownsWholeFileLock = false)
        {
            Stream = stream;
            FullPath = fullPath;
            Identity = identity;
            OwnsWholeFileLock = ownsWholeFileLock;
        }

        internal FileStream Stream { get; }
        internal string FullPath { get; }
        internal FileIdentity Identity { get; }
        private bool OwnsWholeFileLock { get; }

        public void Dispose()
        {
            if (OwnsWholeFileLock && OperatingSystem.IsWindows() &&
                Identity.Kind == FileIdentityKind.Windows && !Stream.SafeFileHandle.IsClosed)
            {
                var overlapped = new NativeOverlappedData();
                _ = UnlockFileEx(
                    Stream.SafeFileHandle,
                    0,
                    uint.MaxValue,
                    uint.MaxValue,
                    ref overlapped);
            }
            Stream.Dispose();
        }
    }

    internal sealed class VerifiedDirectory : IDisposable
    {
        internal VerifiedDirectory(SafeFileHandle handle, string fullPath, FileIdentityKind kind)
        {
            Handle = handle;
            FullPath = fullPath;
            Kind = kind;
        }

        internal SafeFileHandle Handle { get; }
        internal string FullPath { get; }
        internal FileIdentityKind Kind { get; }
        public void Dispose() => Handle.Dispose();
    }

    // ── Backend-tagged capabilities ──
    // A Unix fd and an NT handle are not interchangeable integers. Keep refusal at every native
    // entry, including internal mutation APIs that are not yet reachable from Linux product flows.
    private static bool HasWindowsCapability(VerifiedDirectory directory) =>
        OperatingSystem.IsWindows() && directory.Kind == FileIdentityKind.Windows && !directory.Handle.IsClosed;

    private static bool HasCurrentReadCapability(VerifiedDirectory directory) =>
        HasWindowsCapability(directory) || (LinuxReadFileSystem.IsSupported &&
            directory.Kind == FileIdentityKind.Linux && !directory.Handle.IsClosed);

    private static void RequireWindowsMutation(VerifiedDirectory? directory = null)
    {
        if (!OperatingSystem.IsWindows() || (directory != null && directory.Kind != FileIdentityKind.Windows))
            throw new PlatformNotSupportedException("This mutation entry requires a Windows filesystem capability.");
        if (directory != null && directory.Handle.IsClosed)
            throw new ObjectDisposedException(nameof(VerifiedDirectory));
    }

    // Internal, null in product execution, and invoked immediately before a protected operation.
    internal static Action<string, string>? BeforeHandleOperationForTests { get; set; }

    internal static bool ContainsReparsePointInExistingChain(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? pathRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(pathRoot) || IsReparsePointOrInaccessible(pathRoot)) return true;
            string current = pathRoot;
            foreach (string segment in SplitRelativePath(Path.GetRelativePath(pathRoot, fullPath)))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePointOrInaccessible(current)) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
            PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    internal static VerifiedOpenResult TryOpenVerifiedRead(
        string trustedRoot,
        string candidatePath,
        out VerifiedReadFile? verified)
    {
        verified = null;
        if (LinuxReadFileSystem.IsSupported)
            return TryOpenVerifiedReadLinux(trustedRoot, candidatePath, out verified);
        if (!OperatingSystem.IsWindows()) return VerifiedOpenResult.Rejected;

        string canonicalRoot;
        string candidate;
        string relative;
        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
            candidate = Path.GetFullPath(candidatePath);
            relative = Path.GetRelativePath(canonicalRoot, candidate);
            if (!IsContainedRelativePath(relative) || string.IsNullOrEmpty(relative))
                return VerifiedOpenResult.Rejected;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VerifiedOpenResult.Rejected;
        }

        BeforeHandleOperationForTests?.Invoke("open-read", candidate);
        VerifiedDirectory? root = OpenExistingDirectory(canonicalRoot, mutable: false);
        if (root == null) return VerifiedOpenResult.Rejected;
        using (root)
        {
            string[] segments = SplitRelativePath(relative);
            if (segments.Length == 0) return VerifiedOpenResult.Rejected;
            VerifiedDirectory parent = root;
            bool disposeParent = false;
            try
            {
                for (int index = 0; index < segments.Length - 1; index++)
                {
                    int status = OpenRelativeDirectory(parent, segments[index], mutable: false, create: false,
                        out VerifiedDirectory? child);
                    if (status < 0 || child == null)
                        return IsNotFound(status) ? VerifiedOpenResult.NotFound : VerifiedOpenResult.Rejected;
                    if (disposeParent) parent.Dispose();
                    parent = child;
                    disposeParent = true;
                }

                int fileStatus = NtOpenRelative(
                    parent.Handle, segments[^1], FileReadData | FileReadAttributes | Synchronize,
                    FileShareRead | FileShareWrite | FileShareDelete, FileOpen,
                    FileNonDirectoryFile | FileSynchronousIoNonAlert, FileAttributeNormal,
                    out SafeFileHandle? handle);
                if (fileStatus < 0 || handle == null)
                    return IsNotFound(fileStatus) ? VerifiedOpenResult.NotFound : VerifiedOpenResult.Rejected;
                try
                {
                    if (!TryValidateOpenedFile(handle, candidate, canonicalRoot, out FileIdentity identity))
                        return VerifiedOpenResult.Rejected;
                    var stream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
                    handle = null;
                    verified = new VerifiedReadFile(stream, candidate, identity);
                    return VerifiedOpenResult.Success;
                }
                finally { handle?.Dispose(); }
            }
            finally { if (disposeParent) parent.Dispose(); }
        }
    }

    /// <summary>
    /// Opens one regular-file leaf relative to an already verified directory capability. This is the
    /// mutation-side read primitive: callers must not reopen <see cref="VerifiedDirectory.FullPath"/>
    /// by pathname after acquiring the directory handle.
    /// </summary>
    internal static VerifiedOpenResult TryOpenVerifiedRead(
        VerifiedDirectory directory,
        string fileName,
        out VerifiedReadFile? verified)
    {
        ValidateLeafName(fileName);
        if (!HasCurrentReadCapability(directory))
        {
            verified = null;
            return VerifiedOpenResult.Rejected;
        }
        string expected = Path.Combine(directory.FullPath, fileName);
        BeforeHandleOperationForTests?.Invoke("open-read-relative", expected);
        if (LinuxReadFileSystem.IsSupported)
            return TryOpenVerifiedReadLinux(directory, fileName, expected, out verified);
        if (!HasWindowsCapability(directory))
        {
            verified = null;
            return VerifiedOpenResult.Rejected;
        }
        return TryOpenVerifiedReadRelativeCore(
            directory,
            fileName,
            expected,
            FileReadData | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            acquireWholeFileLock: false,
            out verified);
    }

    /// <summary>
    /// Opens and exclusively byte-range-locks a mutation input. The handle continues to share
    /// read/write/delete so the app's own atomic replacement remains legal, while the kernel lock
    /// rejects same-FileId writes for the full lifetime of the retained lease.
    /// </summary>
    internal static VerifiedOpenResult TryOpenVerifiedMutationRead(
        VerifiedDirectory directory,
        string fileName,
        out VerifiedReadFile? verified)
    {
        ValidateLeafName(fileName);
        string expected = Path.Combine(directory.FullPath, fileName);
        BeforeHandleOperationForTests?.Invoke("open-read-mutation", expected);
        if (!HasWindowsCapability(directory))
        {
            verified = null;
            return VerifiedOpenResult.Rejected;
        }
        return TryOpenVerifiedReadRelativeCore(
            directory,
            fileName,
            expected,
            FileReadData | FileWriteData | FileReadAttributes | Delete | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            acquireWholeFileLock: true,
            out verified);
    }

    /// <summary>
    /// Pins an app-owned parser staging leaf against writes, replacement, and deletion while a
    /// legacy path-based reader reopens it. The lease stays read-only for FileShare.Read callers.
    /// </summary>
    internal static VerifiedOpenResult TryOpenVerifiedStagingRead(
        VerifiedDirectory directory,
        string fileName,
        out VerifiedReadFile? verified)
    {
        ValidateLeafName(fileName);
        string expected = Path.Combine(directory.FullPath, fileName);
        BeforeHandleOperationForTests?.Invoke("open-read-strict", expected);
        if (!HasWindowsCapability(directory))
        {
            verified = null;
            return VerifiedOpenResult.Rejected;
        }
        return TryOpenVerifiedReadRelativeCore(
            directory,
            fileName,
            expected,
            FileReadData | FileReadAttributes | Synchronize,
            FileShareRead,
            acquireWholeFileLock: false,
            out verified);
    }

    internal static VerifiedDirectory OpenOrCreateVerifiedDirectory(string directory)
    {
        RequireWindowsMutation();
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        BeforeHandleOperationForTests?.Invoke("open-directory", fullPath);
        return OpenDirectoryPath(fullPath, create: true, mutable: true) ??
            throw new IOException("The owned overlay directory could not be opened without reparsing.");
    }

    /// <summary>
    /// Creates/verifies an app-owned directory, then reopens its final component without
    /// FILE_SHARE_DELETE so a path-based parser cannot be redirected by renaming that directory.
    /// </summary>
    internal static VerifiedDirectory OpenOrCreateVerifiedStableDirectory(string directory)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        using (OpenOrCreateVerifiedDirectory(fullPath)) { }
        BeforeHandleOperationForTests?.Invoke("open-stable-directory", fullPath);
        return OpenDirectoryPath(fullPath, create: false, mutable: true, stableFinal: true) ??
            throw new IOException("The app-owned staging directory could not be pinned without reparsing.");
    }

    internal static bool TryOpenVerifiedDirectory(string directory, out VerifiedDirectory? verified)
    {
        verified = null;
        if (LinuxReadFileSystem.IsSupported)
            return TryOpenVerifiedDirectoryLinux(directory, out verified);
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            verified = OpenExistingDirectory(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), mutable: false);
            return verified != null;
        }
        catch
        {
            return false;
        }
    }

    internal static FileStream OpenOrCreateExclusiveFile(VerifiedDirectory directory, string fileName)
    {
        RequireWindowsMutation(directory);
        ValidateLeafName(fileName);
        int status = NtOpenRelative(
            directory.Handle, fileName,
            FileReadData | FileWriteData | FileReadAttributes | Synchronize,
            shareAccess: 0, FileOpenIf,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileWriteThrough,
            FileAttributeNormal, out SafeFileHandle? handle);
        if (status < 0 || handle == null)
            throw new IOException($"The cross-process overlay lock could not be acquired (NTSTATUS 0x{status:X8}).");
        return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);
    }

    internal static FileStream CreateNewVerifiedFile(VerifiedDirectory directory, string fileName)
    {
        return CreateNewVerifiedFile(directory, fileName, out _);
    }

    internal static FileStream CreateNewVerifiedFile(
        VerifiedDirectory directory,
        string fileName,
        out FileIdentity identity)
    {
        RequireWindowsMutation(directory);
        ValidateLeafName(fileName);
        string expected = Path.Combine(directory.FullPath, fileName);
        BeforeHandleOperationForTests?.Invoke("open-create", expected);
        int status = NtOpenRelative(
            directory.Handle, fileName,
            FileWriteData | FileReadAttributes | Delete | Synchronize,
            shareAccess: 0, FileCreate,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileWriteThrough,
            FileAttributeNormal, out SafeFileHandle? handle);
        if (status < 0 || handle == null)
            throw new IOException($"The temporary overlay file could not be created (NTSTATUS 0x{status:X8}).");
        try
        {
            if (!TryValidateOpenedFile(handle, expected, directory.FullPath, out identity))
                throw new IOException("The temporary overlay handle did not resolve inside its owned directory.");
            var stream = new FileStream(handle, FileAccess.Write, 64 * 1024, isAsync: false);
            handle = null;
            try
            {
                BeforeHandleOperationForTests?.Invoke("open-create-verified", expected);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        finally { handle?.Dispose(); }
    }

    internal static FileIdentity PromoteOpenedFile(
        VerifiedDirectory directory,
        FileStream temporary,
        string destinationFileName)
    {
        RequireWindowsMutation(directory);
        ValidateLeafName(destinationFileName);
        string expectedDestination = Path.Combine(directory.FullPath, destinationFileName);
        BeforeHandleOperationForTests?.Invoke("promote", expectedDestination);

        return PromoteOpenedFileCore(directory, temporary, destinationFileName, expectedDestination);
    }

    private static FileIdentity PromoteOpenedFileCore(
        VerifiedDirectory directory,
        FileStream temporary,
        string destinationFileName,
        string expectedDestination,
        bool replaceExisting = true,
        bool replaceOpenDestination = false)
    {
        RequireWindowsMutation(directory);
        int rootOffset = checked((int)Marshal.OffsetOf<FileRenameInfoHeader>(
            nameof(FileRenameInfoHeader.RootDirectory)));
        int lengthOffset = checked((int)Marshal.OffsetOf<FileRenameInfoHeader>(
            nameof(FileRenameInfoHeader.FileNameLength)));
        int fileNameOffset = checked((int)Marshal.OffsetOf<FileRenameInfoHeader>(
            nameof(FileRenameInfoHeader.FileName)));
        byte[] fileNameBytes = Encoding.Unicode.GetBytes(destinationFileName);
        // FILE_RENAME_INFO starts with a BOOLEAN/DWORD union.  Zero the whole native buffer and
        // write the DWORD form so padding can never be interpreted as unsupported rename flags.
        // The kernel requires sizeof(FILE_RENAME_INFO) + FileNameLength, even though the name
        // itself begins at Marshal.OffsetOf(FileName) four bytes before the x64 struct size.
        byte[] buffer = new byte[Marshal.SizeOf<FileRenameInfoHeader>() + fileNameBytes.Length];
        uint renameFlags = replaceExisting ? FileRenameFlagReplaceIfExists : 0;
        if (replaceOpenDestination)
            renameFlags |= FileRenameFlagPosixSemantics;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), renameFlags);

        bool rootAdded = false;
        GCHandle pinned = default;
        FileIdentity identity = default;
        try
        {
            directory.Handle.DangerousAddRef(ref rootAdded);
            IntPtr root = directory.Handle.DangerousGetHandle();
            if (IntPtr.Size == 8)
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(rootOffset, 8), root.ToInt64());
            else
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(rootOffset, 4), root.ToInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(
                buffer.AsSpan(lengthOffset, sizeof(uint)), checked((uint)fileNameBytes.Length));
            fileNameBytes.CopyTo(buffer.AsSpan(fileNameOffset));
            pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            if (replaceOpenDestination)
            {
                int renameStatus = NtSetInformationFile(
                    temporary.SafeFileHandle,
                    out _,
                    pinned.AddrOfPinnedObject(),
                    checked((uint)buffer.Length),
                    FileRenameInformationExClass);
                if (renameStatus < 0)
                    throw new IOException(
                        $"The locked overlay promotion failed (NTSTATUS 0x{renameStatus:X8}).");
            }
            else
            {
                int renameStatus = NtSetInformationFile(
                    temporary.SafeFileHandle, out _, pinned.AddrOfPinnedObject(),
                    checked((uint)buffer.Length), FileRenameInformationClass);
                if (renameStatus < 0)
                    throw new IOException($"The overlay promotion failed (NTSTATUS 0x{renameStatus:X8}).");
            }
            if (!TryValidateOpenedFile(
                    temporary.SafeFileHandle, expectedDestination, directory.FullPath, out identity))
                throw new IOException("The promoted overlay handle no longer matched its owned destination.");
        }
        finally
        {
            if (pinned.IsAllocated) pinned.Free();
            if (rootAdded) directory.Handle.DangerousRelease();
        }
        return identity;
    }

    /// <summary>
    /// Replaces one leaf only when its handle-relative state still matches the state captured by the
    /// caller. The check runs after the deterministic test hook and immediately before the kernel
    /// rename, closing the pathname re-read gap for app-owned writers.
    /// </summary>
    internal static FileIdentity PromoteOpenedFileIfUnchanged(
        VerifiedDirectory directory,
        FileStream temporary,
        string destinationFileName,
        VerifiedOpenResult expectedState,
        FileIdentity expectedIdentity)
    {
        RequireWindowsMutation(directory);
        if (expectedState is not (VerifiedOpenResult.Success or VerifiedOpenResult.NotFound))
            throw new ArgumentOutOfRangeException(nameof(expectedState));
        ValidateLeafName(destinationFileName);
        string expectedDestination = Path.Combine(directory.FullPath, destinationFileName);
        BeforeHandleOperationForTests?.Invoke("promote", expectedDestination);

        VerifiedOpenResult actualState = TryOpenVerifiedReadRelativeCore(
            directory,
            destinationFileName,
            expectedDestination,
            FileReadData | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            acquireWholeFileLock: false,
            out VerifiedReadFile? actual);
        using (actual)
        {
            if (actualState != expectedState ||
                (expectedState == VerifiedOpenResult.Success &&
                 (actual == null || actual.Identity != expectedIdentity)))
                throw new IOException("The destination leaf changed before its verified promotion.");
        }

        return PromoteOpenedFileCore(
            directory,
            temporary,
            destinationFileName,
            expectedDestination,
            replaceExisting: expectedState == VerifiedOpenResult.Success,
            replaceOpenDestination: expectedState == VerifiedOpenResult.Success);
    }

    /// <summary>Rechecks a captured leaf identity through the same verified directory handle.</summary>
    internal static bool IsOpenedFileIdentityCurrent(
        VerifiedDirectory directory,
        string fileName,
        FileIdentity expectedIdentity)
    {
        if (!HasWindowsCapability(directory)) return false;
        ValidateLeafName(fileName);
        string expected = Path.Combine(directory.FullPath, fileName);
        VerifiedOpenResult result = TryOpenVerifiedReadRelativeCore(
            directory,
            fileName,
            expected,
            FileReadData | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            acquireWholeFileLock: false,
            out VerifiedReadFile? current);
        using (current)
            return result == VerifiedOpenResult.Success && current?.Identity == expectedIdentity;
    }

    internal static void DeleteOpenedFile(FileStream file)
    {
        RequireWindowsMutation();
        byte[] disposition = { 1 };
        GCHandle pinned = GCHandle.Alloc(disposition, GCHandleType.Pinned);
        try
        {
            if (!SetFileInformationByHandle(
                    file.SafeFileHandle, FileDispositionInfoClass,
                    pinned.AddrOfPinnedObject(), checked((uint)disposition.Length)))
                throw new IOException("The owned overlay file could not be marked for deletion.",
                    new System.ComponentModel.Win32Exception());
        }
        finally { pinned.Free(); }
    }

    /// <summary>Deletes only the exact leaf identity through an already verified directory handle.</summary>
    internal static bool TryDeleteVerifiedFile(
        VerifiedDirectory directory,
        string fileName,
        FileIdentity expectedIdentity)
    {
        if (!HasWindowsCapability(directory)) return false;
        ValidateLeafName(fileName);
        string expected = Path.Combine(directory.FullPath, fileName);
        BeforeHandleOperationForTests?.Invoke("delete-relative", expected);
        int status = NtOpenRelative(
            directory.Handle,
            fileName,
            Delete | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert,
            FileAttributeNormal,
            out SafeFileHandle? handle);
        if (status < 0 || handle == null)
            return false;
        using (handle)
        {
            if (!TryValidateOpenedFile(handle, expected, directory.FullPath, out FileIdentity actual) ||
                actual != expectedIdentity)
                return false;
            byte[] disposition = { 1 };
            GCHandle pinned = GCHandle.Alloc(disposition, GCHandleType.Pinned);
            try
            {
                return SetFileInformationByHandle(
                    handle,
                    FileDispositionInfoClass,
                    pinned.AddrOfPinnedObject(),
                    checked((uint)disposition.Length));
            }
            finally { pinned.Free(); }
        }
    }

    internal static bool TryDeleteVerifiedFile(
        string trustedRoot,
        string candidatePath,
        FileIdentity? expectedIdentity = null)
    {
        if (!OperatingSystem.IsWindows()) return false;
        string canonicalRoot;
        string candidate;
        string relative;
        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
            candidate = Path.GetFullPath(candidatePath);
            relative = Path.GetRelativePath(canonicalRoot, candidate);
            if (!IsContainedRelativePath(relative) || string.IsNullOrEmpty(relative)) return false;
        }
        catch { return false; }

        BeforeHandleOperationForTests?.Invoke("open-delete", candidate);
        VerifiedDirectory? root = OpenExistingDirectory(canonicalRoot, mutable: true);
        if (root == null) return false;
        using (root)
        {
            string[] segments = SplitRelativePath(relative);
            VerifiedDirectory parent = root;
            bool disposeParent = false;
            try
            {
                for (int index = 0; index < segments.Length - 1; index++)
                {
                    int status = OpenRelativeDirectory(parent, segments[index], mutable: true, create: false,
                        out VerifiedDirectory? child);
                    if (status < 0 || child == null) return false;
                    if (disposeParent) parent.Dispose();
                    parent = child;
                    disposeParent = true;
                }
                int statusFile = NtOpenRelative(
                    parent.Handle, segments[^1], Delete | FileReadAttributes | Synchronize,
                    FileShareRead | FileShareWrite | FileShareDelete, FileOpen,
                    FileNonDirectoryFile | FileSynchronousIoNonAlert, FileAttributeNormal,
                    out SafeFileHandle? handle);
                if (statusFile < 0 || handle == null) return false;
                using (handle)
                {
                    if (!TryValidateOpenedFile(handle, candidate, canonicalRoot, out FileIdentity actual) ||
                        expectedIdentity is FileIdentity expected && actual != expected)
                        return false;
                    byte[] disposition = { 1 };
                    GCHandle pinned = GCHandle.Alloc(disposition, GCHandleType.Pinned);
                    try
                    {
                        return SetFileInformationByHandle(
                            handle, FileDispositionInfoClass,
                            pinned.AddrOfPinnedObject(), checked((uint)disposition.Length));
                    }
                    finally { pinned.Free(); }
                }
            }
            finally { if (disposeParent) parent.Dispose(); }
        }
    }

    private static VerifiedDirectory? OpenExistingDirectory(string directory, bool mutable) =>
        OpenDirectoryPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), false, mutable);

    // ── Linux retained read dispatch ──────────────────────────────────────────────
    // The directory FullPath below is only a diagnostic/logical label. Authorization stays on the
    // retained descriptor, so renaming the selected tree cannot redirect a later relative open.
    private static VerifiedOpenResult TryOpenVerifiedReadLinux(
        string trustedRoot,
        string candidatePath,
        out VerifiedReadFile? verified)
    {
        verified = null;
        string canonicalRoot;
        string relative;
        try
        {
            canonicalRoot = LinuxReadFileSystem.NormalizeAbsoluteRoot(trustedRoot);
            LinuxReadFileSystem.ValidateAbsolutePath(candidatePath);
            if (!TryGetLinuxRelativePath(canonicalRoot, candidatePath, out relative))
                return VerifiedOpenResult.Rejected;
        }
        catch (ArgumentException)
        {
            return VerifiedOpenResult.Rejected;
        }

        BeforeHandleOperationForTests?.Invoke("open-read", candidatePath);
        SafeFileHandle? root = null;
        try
        {
            root = LinuxReadFileSystem.OpenRoot(canonicalRoot);
        }
        catch (Exception error) when (IsLinuxReadFailure(error))
        {
            return VerifiedOpenResult.Rejected;
        }

        using (root)
        {
            SafeFileHandle? handle = null;
            try
            {
                try
                {
                    handle = LinuxReadFileSystem.OpenRead(root, relative);
                }
                catch (Exception error) when (LinuxReadFileSystem.IsMissing(error))
                {
                    return VerifiedOpenResult.NotFound;
                }
                catch (Exception error) when (IsLinuxReadFailure(error))
                {
                    return VerifiedOpenResult.Rejected;
                }

                LinuxReadFileSystem.LinuxFileObservation observation =
                    LinuxReadFileSystem.Observe(handle, LinuxReadFileSystem.RegularFileType);
                var stream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
                handle = null;
                verified = new VerifiedReadFile(
                    stream,
                    candidatePath,
                    ToLinuxIdentity(observation.Identity));
                return VerifiedOpenResult.Success;
            }
            catch (Exception error) when (IsLinuxReadFailure(error))
            {
                return VerifiedOpenResult.Rejected;
            }
            finally
            {
                handle?.Dispose();
            }
        }
    }

    private static VerifiedOpenResult TryOpenVerifiedReadLinux(
        VerifiedDirectory directory,
        string fileName,
        string expectedPath,
        out VerifiedReadFile? verified)
    {
        verified = null;
        SafeFileHandle? handle = null;
        try
        {
            handle = LinuxReadFileSystem.OpenRead(directory.Handle, fileName);
            LinuxReadFileSystem.LinuxFileObservation observation =
                LinuxReadFileSystem.Observe(handle, LinuxReadFileSystem.RegularFileType);
            var stream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
            handle = null;
            verified = new VerifiedReadFile(stream, expectedPath, ToLinuxIdentity(observation.Identity));
            return VerifiedOpenResult.Success;
        }
        catch (Exception error) when (LinuxReadFileSystem.IsMissing(error))
        {
            return VerifiedOpenResult.NotFound;
        }
        catch (Exception error) when (IsLinuxReadFailure(error))
        {
            return VerifiedOpenResult.Rejected;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static bool TryOpenVerifiedDirectoryLinux(
        string directory,
        out VerifiedDirectory? verified)
    {
        verified = null;
        SafeFileHandle? handle = null;
        try
        {
            string canonical = LinuxReadFileSystem.NormalizeAbsoluteRoot(directory);
            handle = LinuxReadFileSystem.OpenRoot(canonical);
            _ = LinuxReadFileSystem.Observe(handle, LinuxReadFileSystem.DirectoryType);
            verified = new VerifiedDirectory(handle, canonical, FileIdentityKind.Linux);
            handle = null;
            return true;
        }
        catch (Exception error) when (IsLinuxReadFailure(error))
        {
            return false;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static bool TryGetLinuxRelativePath(string root, string candidate, out string relative)
    {
        relative = string.Empty;
        if (root == "/")
        {
            if (candidate.Length <= 1)
                return false;
            relative = candidate.Substring(1);
        }
        else
        {
            string prefix = root + "/";
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            relative = candidate.Substring(prefix.Length);
        }

        try
        {
            LinuxReadFileSystem.ValidateRelativePath(relative);
            return true;
        }
        catch (ArgumentException)
        {
            relative = string.Empty;
            return false;
        }
    }

    private static FileIdentity ToLinuxIdentity(
        LinuxReadFileSystem.LinuxObjectIdentity identity) =>
        // Linux packs the filesystem device tuple into the legacy volume-sized slot. Inode and
        // mount ID occupy the two file-ID slots; Kind prevents cross-platform equality accidents.
        new(
            FileIdentityKind.Linux,
            ((ulong)identity.DeviceMajor << 32) | identity.DeviceMinor,
            identity.Inode,
            identity.MountId);

    private static bool IsLinuxReadFailure(Exception error) => error is
        ArgumentException or
        IOException or
        UnauthorizedAccessException or
        ObjectDisposedException or
        PlatformNotSupportedException or
        DllNotFoundException or
        EntryPointNotFoundException;

    private static VerifiedOpenResult TryOpenVerifiedReadRelativeCore(
        VerifiedDirectory directory,
        string fileName,
        string expectedPath,
        uint desiredAccess,
        uint shareAccess,
        bool acquireWholeFileLock,
        out VerifiedReadFile? verified)
    {
        verified = null;
        if (!HasWindowsCapability(directory)) return VerifiedOpenResult.Rejected;
        int status = NtOpenRelative(
            directory.Handle,
            fileName,
            desiredAccess,
            shareAccess,
            FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert,
            FileAttributeNormal,
            out SafeFileHandle? handle);
        if (status < 0 || handle == null)
            return IsNotFound(status) ? VerifiedOpenResult.NotFound : VerifiedOpenResult.Rejected;
        try
        {
            if (!TryValidateOpenedFile(handle, expectedPath, directory.FullPath, out FileIdentity identity))
                return VerifiedOpenResult.Rejected;
            FileAccess access = (desiredAccess & FileWriteData) != 0
                ? FileAccess.ReadWrite
                : FileAccess.Read;
            var stream = new FileStream(handle, access, 64 * 1024, isAsync: false);
            handle = null;
            if (acquireWholeFileLock)
            {
                var overlapped = new NativeOverlappedData();
                if (!LockFileEx(
                        stream.SafeFileHandle,
                        LockfileExclusiveLock | LockfileFailImmediately,
                        0,
                        uint.MaxValue,
                        uint.MaxValue,
                        ref overlapped))
                {
                    stream.Dispose();
                    return VerifiedOpenResult.Rejected;
                }
            }
            verified = new VerifiedReadFile(stream, expectedPath, identity, acquireWholeFileLock);
            return VerifiedOpenResult.Success;
        }
        finally { handle?.Dispose(); }
    }

    private static VerifiedDirectory? OpenDirectoryPath(
        string fullPath,
        bool create,
        bool mutable,
        bool stableFinal = false)
    {
        if (!OperatingSystem.IsWindows() || fullPath.StartsWith("\\\\", StringComparison.Ordinal)) return null;
        string? pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(pathRoot)) return null;
        string relative = Path.GetRelativePath(pathRoot, fullPath);
        string[] segments = relative == "." ? Array.Empty<string>() : SplitRelativePath(relative);

        int existingCount = 0;
        if (create)
        {
            string scan = pathRoot;
            foreach (string segment in segments)
            {
                scan = Path.Combine(scan, segment);
                if (!Directory.Exists(scan)) break;
                existingCount++;
            }
        }
        else existingCount = segments.Length;

        bool rootNeedsMutation = mutable && create && existingCount == 0 && segments.Length > 0;
        int rootStatus = NtOpenAbsoluteDirectory(pathRoot, DirectoryAccess(rootNeedsMutation),
            out SafeFileHandle? currentHandle);
        if (rootStatus < 0 || currentHandle == null) return null;

        string currentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pathRoot));
        if (!TryValidateOpenedDirectory(currentHandle, currentPath))
        {
            currentHandle.Dispose();
            return null;
        }

        for (int index = 0; index < segments.Length; index++)
        {
            int mutableThreshold = Math.Max(0, existingCount - 1);
            bool childMutable = mutable && index >= mutableThreshold;
            bool childCreate = create && index >= existingCount;
            uint childShare = stableFinal && index == segments.Length - 1
                ? FileShareRead | FileShareWrite
                : FileShareRead | FileShareWrite | FileShareDelete;
            using SafeFileHandle parentHandle = currentHandle;
            int childStatus = NtOpenRelative(
                parentHandle, segments[index], DirectoryAccess(childMutable),
                childShare,
                childCreate ? FileOpenIf : FileOpen,
                FileDirectoryFile | FileSynchronousIoNonAlert, FileAttributeDirectory,
                out currentHandle);
            if (childStatus < 0 || currentHandle == null) return null;
            currentPath = Path.Combine(currentPath, segments[index]);
            if (!TryValidateOpenedDirectory(currentHandle, currentPath))
            {
                currentHandle.Dispose();
                return null;
            }
        }
        return new VerifiedDirectory(currentHandle, currentPath, FileIdentityKind.Windows);
    }

    private static int OpenRelativeDirectory(
        VerifiedDirectory parent, string segment, bool mutable, bool create,
        out VerifiedDirectory? directory)
    {
        directory = null;
        ValidateLeafName(segment);
        int status = NtOpenRelative(
            parent.Handle, segment, DirectoryAccess(mutable),
            FileShareRead | FileShareWrite | FileShareDelete,
            create ? FileOpenIf : FileOpen,
            FileDirectoryFile | FileSynchronousIoNonAlert, FileAttributeDirectory,
            out SafeFileHandle? handle);
        if (status < 0 || handle == null) return status;
        string expected = Path.Combine(parent.FullPath, segment);
        if (!TryValidateOpenedDirectory(handle, expected))
        {
            handle.Dispose();
            return unchecked((int)0xC0000022);
        }
        directory = new VerifiedDirectory(handle, expected, FileIdentityKind.Windows);
        return status;
    }

    private static uint DirectoryAccess(bool mutable) =>
        FileListDirectory | FileTraverse | FileReadAttributes | Synchronize |
        (mutable ? FileAddFile | FileAddSubdirectory | FileDeleteChild : 0);

    private static int NtOpenAbsoluteDirectory(string path, uint desiredAccess, out SafeFileHandle? handle)
    {
        string ntPath = path.StartsWith("\\\\", StringComparison.Ordinal)
            ? @"\??\UNC\" + path.TrimStart('\\')
            : @"\??\" + path;
        return NtOpen(null, ntPath, desiredAccess,
            FileShareRead | FileShareWrite | FileShareDelete, FileOpen,
            FileDirectoryFile | FileSynchronousIoNonAlert, FileAttributeDirectory, out handle);
    }

    private static int NtOpenRelative(
        SafeFileHandle rootDirectory, string relativeName, uint desiredAccess, uint shareAccess,
        uint disposition, uint options, uint attributes, out SafeFileHandle? handle) =>
        NtOpen(rootDirectory, relativeName, desiredAccess, shareAccess, disposition, options, attributes, out handle);

    private static int NtOpen(
        SafeFileHandle? rootDirectory, string objectName, uint desiredAccess, uint shareAccess,
        uint disposition, uint options, uint attributes, out SafeFileHandle? handle)
    {
        handle = null;
        byte[] nameBytes = Encoding.Unicode.GetBytes(objectName);
        if (nameBytes.Length > ushort.MaxValue - sizeof(char)) return unchecked((int)0xC0000106);

        IntPtr nameBuffer = IntPtr.Zero;
        IntPtr unicodePointer = IntPtr.Zero;
        bool rootAdded = false;
        try
        {
            nameBuffer = Marshal.StringToHGlobalUni(objectName);
            var unicode = new UnicodeString
            {
                Length = checked((ushort)nameBytes.Length),
                MaximumLength = checked((ushort)(nameBytes.Length + sizeof(char))),
                Buffer = nameBuffer,
            };
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodePointer, false);

            IntPtr root = IntPtr.Zero;
            if (rootDirectory != null)
            {
                rootDirectory.DangerousAddRef(ref rootAdded);
                root = rootDirectory.DangerousGetHandle();
            }
            var objectAttributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = root,
                ObjectName = unicodePointer,
                Attributes = ObjCaseInsensitive | ObjDontReparse,
            };
            int status = NtCreateFile(
                out SafeFileHandle opened, desiredAccess, ref objectAttributes, out _, IntPtr.Zero,
                attributes, shareAccess, disposition, options, IntPtr.Zero, 0);
            if (status >= 0 && !opened.IsInvalid) handle = opened;
            else opened.Dispose();
            return status;
        }
        finally
        {
            if (rootAdded) rootDirectory!.DangerousRelease();
            if (unicodePointer != IntPtr.Zero) Marshal.FreeHGlobal(unicodePointer);
            if (nameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static bool TryValidateOpenedDirectory(SafeFileHandle handle, string expectedPath) =>
        TryGetAttributes(handle, out FileAttributeTagInfo attributes) &&
        (attributes.FileAttributes & FileAttributeDirectory) != 0 &&
        (attributes.FileAttributes & FileAttributeReparsePoint) == 0 &&
        TryGetFinalPath(handle, out string finalPath) && PathsEqual(finalPath, expectedPath);

    private static bool TryValidateOpenedFile(
        SafeFileHandle handle, string expectedPath, string trustedRoot, out FileIdentity identity)
    {
        identity = default;
        return TryGetAttributes(handle, out FileAttributeTagInfo attributes) &&
            (attributes.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) == 0 &&
            TryGetFinalPath(handle, out string finalPath) && PathsEqual(finalPath, expectedPath) &&
            IsPathContained(trustedRoot, finalPath) && TryGetIdentity(handle, out identity);
    }

    private static bool TryGetAttributes(SafeFileHandle handle, out FileAttributeTagInfo attributes) =>
        GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, out attributes,
            checked((uint)Marshal.SizeOf<FileAttributeTagInfo>()));

    private static bool TryGetIdentity(SafeFileHandle handle, out FileIdentity identity)
    {
        identity = default;
        if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdInfo info,
                checked((uint)Marshal.SizeOf<FileIdInfo>()))) return false;
        identity = new FileIdentity(
            FileIdentityKind.Windows,
            info.VolumeSerialNumber,
            BinaryPrimitives.ReadUInt64LittleEndian(info.FileId.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(info.FileId.AsSpan(8, 8)));
        return true;
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        var buffer = new StringBuilder(512);
        uint length = GetFinalPathNameByHandle(handle, buffer, checked((uint)buffer.Capacity), 0);
        if (length == 0) return false;
        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)length + 1));
            length = GetFinalPathNameByHandle(handle, buffer, checked((uint)buffer.Capacity), 0);
            if (length == 0 || length >= buffer.Capacity) return false;
        }
        string value = buffer.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            value = @"\\" + value.Substring(8);
        else if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(4);
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        return true;
    }

    private static bool IsPathContained(string root, string candidate)
    {
        try
        {
            string relative = Path.GetRelativePath(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), Path.GetFullPath(candidate));
            return IsContainedRelativePath(relative);
        }
        catch { return false; }
    }

    private static bool IsContainedRelativePath(string relative) =>
        !Path.IsPathRooted(relative) && relative != ".." &&
        !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
        !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static string[] SplitRelativePath(string relative) => relative.Split(
        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
        StringSplitOptions.RemoveEmptyEntries);

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsNotFound(int status) =>
        status is StatusObjectNameNotFound or StatusObjectPathNotFound or StatusNoSuchFile;

    private static void ValidateLeafName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ||
            fileName.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new ArgumentException("A single canonical file name is required.", nameof(fileName));
    }

    private static bool IsReparsePointOrInaccessible(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { internal ushort Length; internal ushort MaximumLength; internal IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { internal IntPtr Status; internal UIntPtr Information; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo { internal uint FileAttributes; internal uint ReparseTag; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] internal byte[] FileId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileRenameInfoHeader
    {
        internal byte ReplaceIfExists;
        internal IntPtr RootDirectory;
        internal uint FileNameLength;
        internal char FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlappedData
    {
        internal UIntPtr Internal;
        internal UIntPtr InternalHigh;
        internal uint Offset;
        internal uint OffsetHigh;
        internal IntPtr EventHandle;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock, IntPtr allocationSize, uint fileAttributes,
        uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle, out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation, uint length, int fileInformationClass);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file, StringBuilder filePath, uint filePathLength, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int fileInformationClass,
        out FileAttributeTagInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int fileInformationClass,
        out FileIdInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file, int fileInformationClass, IntPtr fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockFileEx(
        SafeFileHandle file,
        uint flags,
        uint reserved,
        uint bytesToLockLow,
        uint bytesToLockHigh,
        ref NativeOverlappedData overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnlockFileEx(
        SafeFileHandle file,
        uint reserved,
        uint bytesToUnlockLow,
        uint bytesToUnlockHigh,
        ref NativeOverlappedData overlapped);
}
