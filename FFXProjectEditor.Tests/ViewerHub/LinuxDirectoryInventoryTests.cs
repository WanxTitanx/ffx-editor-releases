using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Bounded retained-directory inventory ──
// Native fixtures live on the selected work filesystem, not the host's tmpfs /tmp.
// Pure decoder cases are independent of Linux execution; d_type/d_ino never authorize a file.
// MAINT: no payload, arbitrary directory tree or user extraction is scanned by these tests.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed partial class LinuxDirectoryInventoryTests
{
    [Fact]
    public void Decoder_PreservesCaseAndUtf8_IgnoresDots_AndDoesNotTrustNativeTypeHints()
    {
        byte[] bytes = Record(".").Concat(Record("..")).Concat(Record("A.bin"))
            .Concat(Record("a.bin")).Concat(Record("β.bin")).Concat(Record(new string('x', 255))).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        LinuxDirectoryInventory.AppendEntries(bytes, names, 4);
        Assert.Equal(4, names.Count);
        Assert.Contains("A.bin", names);
        Assert.Contains("a.bin", names);
        Assert.Contains("β.bin", names);
        Assert.Contains(new string('x', 255), names);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("zero")]
    [InlineData("unaligned")]
    [InlineData("truncated")]
    [InlineData("too-large")]
    [InlineData("missing-nul")]
    [InlineData("invalid-utf8")]
    [InlineData("empty")]
    [InlineData("traversal")]
    [InlineData("backslash")]
    [InlineData("too-long")]
    [InlineData("duplicate")]
    public void Decoder_RejectsMalformedRecordsAndAmbiguousNames(string defect)
    {
        byte[] bytes = Record("a");
        if (defect == "short") bytes = new byte[19];
        if (defect == "zero") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 0);
        if (defect == "unaligned") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 25);
        if (defect == "truncated") bytes = bytes[..^1];
        if (defect == "too-large") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 288);
        if (defect == "missing-nul") bytes.AsSpan(19).Fill((byte)'x');
        if (defect == "invalid-utf8") bytes[19] = 0xff;
        if (defect == "empty") bytes[19] = 0;
        if (defect == "traversal") bytes = Record("../x");
        if (defect == "backslash") bytes = Record("a\\b");
        if (defect == "too-long") bytes = Record(new string('x', 256));
        if (defect == "duplicate") bytes = bytes.Concat(bytes).ToArray();
        Assert.Throws<IOException>(() => LinuxDirectoryInventory.AppendEntries(bytes,
            new HashSet<string>(StringComparer.Ordinal), 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8193)]
    public void InventoryLimit_IsExplicitAndBounded(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LinuxDirectoryInventory.ValidateLimit(limit));

    [Fact]
    public void NativeInventory_IsExactAndRepeatedReadsHaveIndependentCursors()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.Empty(output.ListChildNames());
        string[] expected = { ".hidden", "A.bin", "a.bin", "β.bin" };
        foreach (string leaf in expected) output.PublishNew(leaf, new byte[] { 1 });
        for (int pass = 0; pass < 3; pass++)
            Assert.Equal(expected, output.ListChildNames(4).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Throws<IOException>(() => output.ListChildNames(3));
        Assert.Equal(4, output.ListChildNames(4).Count);
    }

    [Fact]
    public void NativeInventory_ReadsMultiplePagesAndCountsUnrelatedChildren()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        for (int index = 0; index < 1200; index++) fixture.Create($"unrelated-entry-{index:D4}.bin", 1);
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        var names = output.ListChildNames(1200);
        Assert.Equal(1200, names.Count);
        Assert.Contains("unrelated-entry-0000.bin", names);
        Assert.Contains("unrelated-entry-1199.bin", names);
        Assert.Throws<IOException>(() => output.ListChildNames(1199));
        Assert.Equal(1200, output.ListChildNames(1200).Count);
    }

    [Fact]
    public void NativeInventory_ListsSpecialNamesWithoutFollowingOrOpeningTheirTargets()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        fixture.Create("regular.bin", 1);
        TestDirectory.CreatePrivate(fixture.Path("directory"));
        File.CreateSymbolicLink(fixture.Path("link"), "/proc/self/mem");
        fixture.Run("ln", fixture.Path("regular.bin"), fixture.Path("hardlink"));
        fixture.Run("mkfifo", fixture.Path("fifo"));
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        Assert.Equal(new[] { "directory", "fifo", "hardlink", "link", "regular.bin" },
            output.ListChildNames().OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void NativeInvalidDescriptor_PreservesTypedEbAdf()
    {
        if (!CanRunNative()) return;
        using var invalid = new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
        var error = Assert.Throws<LinuxReadFileSystem.LinuxNativeIOException>(() => LinuxDirectoryInventory.ReadNames(invalid, 1));
        Assert.Equal(9, error.Errno);
    }

    [Theory]
    [InlineData("inventory-before-read")]
    [InlineData("inventory-read")]
    [InlineData("metadata-open")]
    public void RootReplacement_DoesNotInspectTheSubstitutedDirectory(string hook)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string selected = fixture.Path("selected"), moved = fixture.Path("moved");
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(selected);
        output.PublishNew("target.bin", new byte[] { 1 });
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook) return;
            reached = true;
            Directory.Move(selected, moved);
            TestDirectory.CreatePrivate(selected);
        };
        try
        {
            Assert.ThrowsAny<IOException>(() =>
            {
                if (hook == "metadata-open") output.ReadFileMetadata("target.bin");
                else output.ListChildNames();
            });
            Assert.True(reached);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Empty(Directory.GetFileSystemEntries(selected));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(System.IO.Path.Combine(moved, "target.bin")));
    }

    [Theory]
    [InlineData("inventory-before-read")]
    [InlineData("inventory-read")]
    public void DirectoryMutationDuringInventory_IsRefusedButDoesNotFaultReadCapability(string hook)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("original.bin", new byte[] { 1 });
        string extra = fixture.Path("extra.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != hook) return;
            reached = true;
            File.WriteAllBytes(extra, new byte[] { 2 });
        };
        try { Assert.Throws<IOException>(() => output.ListChildNames()); Assert.True(reached); }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(2, output.ListChildNames().Count);
    }

    private static byte[] Record(string name)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name);
        int size = (19 + utf8.Length + 1 + 7) & ~7;
        byte[] bytes = new byte[size]; // Unknown d_type and inode0 must not be mistaken for proof.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), checked((ushort)size));
        utf8.CopyTo(bytes.AsSpan(19));
        return bytes;
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative()
    {
        if (LinuxReadFileSystem.IsSupported) return true;
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOwnedOutputDirectory.OpenOrCreate("/tmp/ffx-inventory-refusal"));
        return false;
    }

    [SupportedOSPlatform("linux")]
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = TestDirectory.CreatePrivate(System.IO.Path.Combine(TestDataPaths.RepoRoot,
            "work", "linux-inventory-tests", Guid.NewGuid().ToString("N"))).FullName;
        internal string Path(string leaf) => System.IO.Path.Combine(Root, leaf);
        internal void Create(string leaf, byte value)
        {
            File.WriteAllBytes(Path(leaf), new[] { value });
            File.SetUnixFileMode(Path(leaf), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        internal void Run(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(5000)) { child.Kill(); throw new TimeoutException("Fixture helper timed out."); }
            Assert.Equal(0, child.ExitCode);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
