using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Bounded enumeration through a fresh retained directory description ──
// Linux x64/glibc getdents64 ABI: d_reclen at16, d_type at18, d_name at19.
// Primary contract: https://man7.org/linux/man-pages/man2/getdents.2.html (2026-09-05).
// Independently implemented decoder; d_ino/d_type are hints, never proof of child identity.
// MAINT: local ext-family only; no DIR* ownership transfer, proc-path fallback, recursion or sorting.
internal static class LinuxDirectoryInventory
{
    internal const int MaximumEntries = 8192;
    private const int BufferBytes = 32768;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Null in product. Deterministic native-result controls use the non-parallel filesystem tests.
    internal static Func<int, byte[], nint>? ReadEntriesForTests { get; set; }

    internal static void ValidateLimit(int maximumEntries)
    {
        if (maximumEntries is < 1 or > MaximumEntries)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
    }

    internal static IReadOnlyList<string> ReadNames(SafeFileHandle directory, int maximumEntries)
    {
        ValidateLimit(maximumEntries);
        LinuxOutputFileSystem.EnsureSupported();
        LinuxOutputFileSystem.VerifyFileSystem(directory);
        using var independent = LinuxOutputFileSystem.OpenIndependentDirectory(directory);
        byte[] buffer = new byte[BufferBytes];
        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        long wireLimit = (maximumEntries + 2L) * 280 + BufferBytes;
        bool acquired = false;
        try
        {
            independent.DangerousAddRef(ref acquired);
            while (true)
            {
                var readForTests = ReadEntriesForTests;
                long count = readForTests == null
                    ? GetEntries(independent.DangerousGetHandle().ToInt32(), buffer, (nuint)buffer.Length)
                    : readForTests(independent.DangerousGetHandle().ToInt32(), buffer);
                if (count < 0)
                    throw new LinuxNativeIOException("getdents64 retained directory", Marshal.GetLastPInvokeError());
                if (count == 0) return Array.AsReadOnly(new List<string>(names).ToArray());
                totalBytes += count;
                if (count > buffer.Length || totalBytes > wireLimit)
                    throw new IOException("Linux directory inventory exceeded its bounded native byte budget.");
                AppendEntries(buffer.AsSpan(0, checked((int)count)), names, maximumEntries);
            }
        }
        finally { if (acquired) independent.DangerousRelease(); }
    }

    // Decode only a complete returned buffer; malformed lengths/names or repeated names fail closed.
    // The count cap applies to all direct children, including unrelated and hidden entries.
    internal static void AppendEntries(ReadOnlySpan<byte> buffer, HashSet<string> names, int maximumEntries)
    {
        ValidateLimit(maximumEntries);
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (buffer.Length - offset < 24)
                throw new IOException("Linux directory inventory has a truncated native record.");
            int size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 16, 2));
            if (size < 24 || size > 280 || size % 8 != 0 || size > buffer.Length - offset)
                throw new IOException("Linux directory inventory has an invalid native record length.");
            var nameBytes = buffer.Slice(offset + 19, size - 19);
            int terminator = nameBytes.IndexOf((byte)0);
            if (terminator is < 1 or > 255)
                throw new IOException("Linux directory inventory has an invalid native leaf length.");
            string name;
            try
            {
                name = StrictUtf8.GetString(nameBytes[..terminator]);
                if (name is not ("." or "..")) LinuxOutputFileSystem.ValidateLeaf(name);
            }
            catch (ArgumentException error)
            {
                throw new IOException("Linux directory inventory contains an unrepresentable or unsafe leaf.", error);
            }
            if (name is not ("." or "..") && (!names.Add(name) || names.Count > maximumEntries))
                throw new IOException("Linux directory inventory has duplicate names or exceeds its child limit.");
            offset += size;
        }
    }

    [DllImport("libc", EntryPoint = "getdents64", SetLastError = true)]
    private static extern nint GetEntries(int descriptor, [Out] byte[] buffer, nuint count);
}
