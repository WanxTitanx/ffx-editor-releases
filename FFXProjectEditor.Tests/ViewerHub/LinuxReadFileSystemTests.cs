using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Linux x86-64 ABI and refusal tests ──────────────────────────────────────────
// Pure decoder/path cases run everywhere. Native cases either exercise libc on Linux x64 or
// explicitly prove that another host cannot enter the Linux syscall boundary.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxReadFileSystemTests
{
    [Fact]
    public void AbiDeclarations_MatchNativeProbeContract()
    {
        Assert.Equal(437, LinuxReadFileSystem.OpenAt2SyscallNumber);
        Assert.Equal(-100, LinuxReadFileSystem.AtCurrentDirectory);
        Assert.Equal(0x1000, LinuxReadFileSystem.AtEmptyPath);
        Assert.Equal(24, Marshal.SizeOf<LinuxReadFileSystem.OpenHow>());
        Assert.Equal(0, Marshal.OffsetOf<LinuxReadFileSystem.OpenHow>(
            nameof(LinuxReadFileSystem.OpenHow.Flags)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<LinuxReadFileSystem.OpenHow>(
            nameof(LinuxReadFileSystem.OpenHow.Mode)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<LinuxReadFileSystem.OpenHow>(
            nameof(LinuxReadFileSystem.OpenHow.Resolve)).ToInt32());
        Assert.Equal(16, Marshal.SizeOf<LinuxReadFileSystem.StatxTimestamp>());
        Assert.Equal(256, Marshal.SizeOf<LinuxReadFileSystem.StatxBuffer>());
        Assert.Equal(28, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.Mode)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.Inode)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.Size)).ToInt32());
        Assert.Equal(96, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.Changed)).ToInt32());
        Assert.Equal(112, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.Modified)).ToInt32());
        Assert.Equal(136, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.DeviceMajor)).ToInt32());
        Assert.Equal(140, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.DeviceMinor)).ToInt32());
        Assert.Equal(144, Marshal.OffsetOf<LinuxReadFileSystem.StatxBuffer>(
            nameof(LinuxReadFileSystem.StatxBuffer.MountId)).ToInt32());

        Assert.Equal(0UL, LinuxReadFileSystem.OpenReadOnly);
        Assert.Equal(0x800UL, LinuxReadFileSystem.OpenNonBlocking);
        Assert.Equal(0x10000UL, LinuxReadFileSystem.OpenDirectory);
        Assert.Equal(0x20000UL, LinuxReadFileSystem.OpenNoFollow);
        Assert.Equal(0x80000UL, LinuxReadFileSystem.OpenCloseOnExec);
        Assert.Equal(1UL, LinuxReadFileSystem.ResolveNoCrossDevice);
        Assert.Equal(2UL, LinuxReadFileSystem.ResolveNoMagicLinks);
        Assert.Equal(4UL, LinuxReadFileSystem.ResolveNoSymbolicLinks);
        Assert.Equal(8UL, LinuxReadFileSystem.ResolveBeneath);
        Assert.Equal(0x13C1U, LinuxReadFileSystem.RequiredStatxMask);
        Assert.Equal(0xF000, LinuxReadFileSystem.FileTypeMask);
        Assert.Equal(0x4000, LinuxReadFileSystem.DirectoryType);
        Assert.Equal(0x8000, LinuxReadFileSystem.RegularFileType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("/tmp//root")]
    [InlineData("/tmp/./root")]
    [InlineData("/tmp/../root")]
    [InlineData("/tmp/root//")]
    [InlineData("/tmp\\root")]
    [InlineData("/tmp/root\0leaf")]
    public void NormalizeAbsoluteRoot_RejectsAmbiguousOrMalformedLinuxPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.NormalizeAbsoluteRoot(path));
    }

    [Fact]
    public void NormalizeAbsoluteRoot_AllowsRootAndOneTrailingSeparator()
    {
        Assert.Equal("/", LinuxReadFileSystem.NormalizeAbsoluteRoot("/"));
        Assert.Equal("/tmp/root", LinuxReadFileSystem.NormalizeAbsoluteRoot("/tmp/root"));
        Assert.Equal("/tmp/root", LinuxReadFileSystem.NormalizeAbsoluteRoot("/tmp/root/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute")]
    [InlineData("nested//leaf.bin")]
    [InlineData("nested/./leaf.bin")]
    [InlineData("nested/../leaf.bin")]
    [InlineData("nested/")]
    [InlineData("nested\\leaf.bin")]
    [InlineData("leaf\0.bin")]
    public void ValidateRelativePath_RejectsAmbiguousOrMalformedLinuxPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.ValidateRelativePath(path));
    }

    [Fact]
    public void ValidateRelativePath_AcceptsNestedLiteralComponents()
    {
        LinuxReadFileSystem.ValidateRelativePath("nested/deeper/leaf.bin");
    }

    [Fact]
    public void PathValidation_RejectsUnpairedUtf16SurrogatesBuiltAtRuntime()
    {
        string high = new((char)0xD800, 1);
        string low = new((char)0xDC00, 1);

        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.NormalizeAbsoluteRoot("/tmp/" + high));
        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.NormalizeAbsoluteRoot("/tmp/" + low));
        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.ValidateRelativePath(high));
        Assert.Throws<ArgumentException>(() => LinuxReadFileSystem.ValidateRelativePath(low));
    }

    [Fact]
    public void DecodeStatx_ReturnsRequiredIdentityAndObservations()
    {
        var value = ValidStatx();

        LinuxReadFileSystem.LinuxFileObservation observation =
            LinuxReadFileSystem.DecodeStatx(value, LinuxReadFileSystem.RegularFileType);

        Assert.Equal(8U, observation.Identity.DeviceMajor);
        Assert.Equal(3U, observation.Identity.DeviceMinor);
        Assert.Equal(123UL, observation.Identity.Inode);
        Assert.Equal(456UL, observation.Identity.MountId);
        Assert.Equal(789, observation.Length);
        Assert.Equal(10, observation.ModifiedSeconds);
        Assert.Equal(20U, observation.ModifiedNanoseconds);
        Assert.Equal(30, observation.ChangedSeconds);
        Assert.Equal(40U, observation.ChangedNanoseconds);
    }

    [Fact]
    public void DecodeStatx_RejectsMissingRequiredFields()
    {
        var value = ValidStatx();
        value.Mask &= ~LinuxReadFileSystem.StatxMountId;

        Assert.Throws<IOException>(() =>
            LinuxReadFileSystem.DecodeStatx(value, LinuxReadFileSystem.RegularFileType));
    }

    [Theory]
    [InlineData(0x1000)]
    [InlineData(0x4000)]
    [InlineData(0xA000)]
    public void DecodeStatx_RejectsUnexpectedObjectType(ushort mode)
    {
        var value = ValidStatx();
        value.Mode = mode;

        Assert.Throws<IOException>(() =>
            LinuxReadFileSystem.DecodeStatx(value, LinuxReadFileSystem.RegularFileType));
    }

    [Fact]
    public void DecodeStatx_RejectsSizeAboveSignedFileStreamRange()
    {
        var value = ValidStatx();
        value.Size = (ulong)long.MaxValue + 1;

        Assert.Throws<IOException>(() =>
            LinuxReadFileSystem.DecodeStatx(value, LinuxReadFileSystem.RegularFileType));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecodeStatx_RejectsInvalidNanoseconds(bool modified)
    {
        var value = ValidStatx();
        if (modified)
            value.Modified.Nanoseconds = 1_000_000_000;
        else
            value.Changed.Nanoseconds = 1_000_000_000;

        Assert.Throws<IOException>(() =>
            LinuxReadFileSystem.DecodeStatx(value, LinuxReadFileSystem.RegularFileType));
    }

    [Fact]
    public void IsMissing_UsesTypedEnoentOnly_NotMessageMatching()
    {
        Assert.True(LinuxReadFileSystem.IsMissing(
            new LinuxReadFileSystem.LinuxNativeIOException("openat2", 2)));
        Assert.False(LinuxReadFileSystem.IsMissing(
            new LinuxReadFileSystem.LinuxNativeIOException("openat2", 13)));
        Assert.False(LinuxReadFileSystem.IsMissing(
            new IOException("Linux openat2 refused (errno 2)")));
    }

    [Fact]
    public void NativeOpen_UsesRetainedDescriptorsOnLinux_OrExplicitlyRefusesOtherHosts()
    {
        if (!LinuxReadFileSystem.IsSupported)
        {
            Assert.False(OperatingSystem.IsLinux() &&
                RuntimeInformation.ProcessArchitecture == Architecture.X64);
            Assert.Throws<PlatformNotSupportedException>(() => LinuxReadFileSystem.OpenRoot("/"));
            return;
        }

        using var sandbox = new NativeFixture();
        string source = sandbox.Write("root/nested/leaf.bin", "native-bytes");
        string rootPath = Path.Combine(sandbox.Root, "root");
        using SafeFileHandle root = LinuxReadFileSystem.OpenRoot(rootPath);
        LinuxReadFileSystem.LinuxFileObservation rootObservation =
            LinuxReadFileSystem.Observe(root, LinuxReadFileSystem.DirectoryType);
        using SafeFileHandle file = LinuxReadFileSystem.OpenRead(root, "nested/leaf.bin");
        LinuxReadFileSystem.LinuxFileObservation fileObservation =
            LinuxReadFileSystem.Observe(file, LinuxReadFileSystem.RegularFileType);
        byte[] bytes = new byte[32];
        int read = RandomAccess.Read(file, bytes, 0);

        Assert.True(rootObservation.Identity.Inode > 0);
        Assert.Equal(new FileInfo(source).Length, fileObservation.Length);
        Assert.Equal("native-bytes", Encoding.UTF8.GetString(bytes, 0, read));
    }

    [Fact]
    public void DescriptorZero_HasTheHostSpecificSafeHandleValidityContract()
    {
        // Do not close the test runner's stdin. The isolated native fd-zero probe forces an actual
        // openat2 return of zero; this independent assertion verifies the managed ownership boundary.
        using var zero = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        Assert.Equal(!OperatingSystem.IsLinux(), zero.IsInvalid);
        Assert.Equal(IntPtr.Zero, zero.DangerousGetHandle());
    }

    [Fact]
    public void PrivateFifo_IsRejectedWithoutBlocking_OrNativeBoundaryIsExplicitlyUnsupported()
    {
        if (!LinuxReadFileSystem.IsSupported)
        {
            Assert.Throws<PlatformNotSupportedException>(() => LinuxReadFileSystem.OpenRoot("/"));
            return;
        }

        using var sandbox = new NativeFixture();
        string root = Directory.CreateDirectory(Path.Combine(sandbox.Root, "root")).FullName;
        string fifo = Path.Combine(root, "pipe.bin");
        var startInfo = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
        startInfo.ArgumentList.Add(fifo);
        using (Process process = Process.Start(startInfo)!)
        {
            Assert.True(process.WaitForExit(5000), "mkfifo timed out");
            Assert.Equal(0, process.ExitCode);
        }

        var stopwatch = Stopwatch.StartNew();
        FileSystemReparseGuard.VerifiedOpenResult result =
            FileSystemReparseGuard.TryOpenVerifiedRead(root, fifo, out var opened);
        stopwatch.Stop();

        Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected, result);
        Assert.Null(opened);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"FIFO refusal took {stopwatch.Elapsed}.");

        string wrongType = Directory.CreateDirectory(Path.Combine(root, "not-a-file.bin")).FullName;
        int before = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        for (int index = 0; index < 128; index++)
        {
            Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
                FileSystemReparseGuard.TryOpenVerifiedRead(root, fifo, out var refusedFifo));
            Assert.Null(refusedFifo);
            Assert.Equal(FileSystemReparseGuard.VerifiedOpenResult.Rejected,
                FileSystemReparseGuard.TryOpenVerifiedRead(root, wrongType, out var refusedDirectory));
            Assert.Null(refusedDirectory);
        }
        int after = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        Assert.True(after <= before + 3, $"Post-open type refusals leaked descriptors: {before} -> {after}.");
    }

    private static LinuxReadFileSystem.StatxBuffer ValidStatx() => new()
    {
        Mask = LinuxReadFileSystem.RequiredStatxMask,
        Mode = LinuxReadFileSystem.RegularFileType,
        Inode = 123,
        Size = 789,
        Changed = new LinuxReadFileSystem.StatxTimestamp { Seconds = 30, Nanoseconds = 40 },
        Modified = new LinuxReadFileSystem.StatxTimestamp { Seconds = 10, Nanoseconds = 20 },
        DeviceMajor = 8,
        DeviceMinor = 3,
        MountId = 456,
    };

    private sealed class NativeFixture : IDisposable
    {
        internal NativeFixture() => Root = Directory.CreateTempSubdirectory("ffx-linux-native-").FullName;

        internal string Root { get; }

        internal string Write(string relative, string content)
        {
            string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            TestDirectory.CreatePrivate(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
