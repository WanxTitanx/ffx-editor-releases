using System;
using System.IO;
using System.Runtime.InteropServices;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Managed getdents64 refusal branches ──
// Synthetic native results exercise branches that an invalid input FD cannot reach because
// fstatfs rejects it first. MAINT: callbacks are null in product and always reset in finally.
public sealed partial class LinuxDirectoryInventoryTests
{
    [Fact]
    public void CumulativeNativeByteBudget_IsIndependentOfChildCount()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var directory = LinuxReadFileSystem.OpenRoot(fixture.Root);
        byte[] dot = Record(".");
        int calls = 0;
        LinuxDirectoryInventory.ReadEntriesForTests = (_, buffer) =>
        {
            calls++;
            if (calls > 2) return 0; // A missing budget check fails promptly, never hangs this test.
            int returned = buffer.Length / dot.Length * dot.Length;
            for (int offset = 0; offset < returned; offset += dot.Length) dot.CopyTo(buffer.AsSpan(offset));
            return returned; // Valid dot records do not add any child to the name set.
        };
        try
        {
            var error = Assert.Throws<IOException>(() => LinuxDirectoryInventory.ReadNames(directory, 1));
            Assert.Contains("native byte budget", error.Message, StringComparison.Ordinal);
            Assert.Equal(2, calls);
        }
        finally { LinuxDirectoryInventory.ReadEntriesForTests = null; }
    }

    [Fact]
    public void OversizedNativeReturn_IsRejectedBeforeSlicingTheManagedBuffer()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var directory = LinuxReadFileSystem.OpenRoot(fixture.Root);
        int calls = 0;
        LinuxDirectoryInventory.ReadEntriesForTests = (_, buffer) => { calls++; return buffer.Length + 1; };
        try
        {
            var error = Assert.Throws<IOException>(() => LinuxDirectoryInventory.ReadNames(directory, 1));
            Assert.Contains("native byte budget", error.Message, StringComparison.Ordinal);
            Assert.Equal(1, calls);
        }
        finally { LinuxDirectoryInventory.ReadEntriesForTests = null; }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void ManagedGetdentsFailure_PreservesOperationErrnoAndDescriptorLifetime(int errno)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var directory = LinuxReadFileSystem.OpenRoot(fixture.Root);
        int calls = 0, before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        LinuxDirectoryInventory.ReadEntriesForTests = (_, _) =>
        {
            calls++;
            Marshal.SetLastPInvokeError(errno);
            return -1;
        };
        try
        {
            var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() => LinuxDirectoryInventory.ReadNames(directory, 1));
            Assert.Equal("getdents64 retained directory", error.Operation);
            Assert.Equal(errno, error.Errno);
            Assert.Equal(1, calls);
        }
        finally { LinuxDirectoryInventory.ReadEntriesForTests = null; }
        int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        Assert.True(after <= before + 4, $"Descriptors: {before} -> {after}");
        Assert.Empty(LinuxDirectoryInventory.ReadNames(directory, 1));
    }
}
