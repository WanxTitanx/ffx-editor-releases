using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

// ── Native durable clone contract ──
// Private ext-family scratch only; payload tests deliberately avoid pretending to execute a DLL.
// MAINT: all hook users share the non-parallel filesystem collection and reset in finally.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed partial class LinuxMagicCloneOutputTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(140)]
    [InlineData(9999)]
    public void Publish_WritesExactPrivateBytes_WithNoHiddenArtifacts(int id)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string name = "magic_" + id.ToString("D4", CultureInfo.InvariantCulture) + ".dll";
        byte[] bytes = { 0, 7, 255 };
        LinuxMagicCloneOutput.Publish(fixture.Root, name, id, bytes);
        string actual = Assert.Single(Directory.GetFileSystemEntries(fixture.Root));
        Assert.Equal(Path.Combine(fixture.Root, name), actual);
        Assert.Equal(bytes, File.ReadAllBytes(actual));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(actual));
    }

    [Theory]
    [InlineData("magic_0140.dll", "file")]
    [InlineData("MAGIC_0140.DLL", "file")]
    [InlineData("magic_140.dll", "file")]
    [InlineData("MAGIC_140.DLL", "file")]
    [InlineData("Magic_0140.dLl", "directory")]
    [InlineData("Magic_140.dLl", "link")]
    public void LogicalCollision_RefusesAndPreservesAnyExistingLeaf(string name, string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string collision = Path.Combine(fixture.Root, name);
        if (kind == "directory") Directory.CreateDirectory(collision);
        else if (kind == "link") File.CreateSymbolicLink(collision, "/proc/self/mem");
        else File.WriteAllBytes(collision, new byte[] { 13 });
        var error = Assert.Throws<IOException>(() =>
            LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
        Assert.Equal(collision, Assert.Single(Directory.GetFileSystemEntries(fixture.Root)));
        if (kind == "file") Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(collision));
        if (kind == "link") Assert.Equal("/proc/self/mem", new FileInfo(collision).LinkTarget);
    }

    [Fact]
    public void PartiallyPaddedName_IsNotAnInventedLogicalCollision()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        File.WriteAllBytes(Path.Combine(fixture.Root, "magic_014.dll"), new byte[] { 13 });
        LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0014.dll", 14, new byte[] { 7 });
        Assert.Equal(2, Directory.GetFileSystemEntries(fixture.Root).Length);
        Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(Path.Combine(fixture.Root, "magic_014.dll")));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(fixture.Root, "magic_0014.dll")));
    }

    [Theory]
    [InlineData(-1, "magic_0140.dll")]
    [InlineData(10000, "magic_0140.dll")]
    [InlineData(140, "magic_140.dll")]
    [InlineData(140, "magic_0141.dll")]
    [InlineData(140, "../magic_0140.dll")]
    public void InvalidIdentityOrName_RefusesBeforeCreatingOutput(int id, string name)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string absent = Path.Combine(fixture.Base, "absent");
        Assert.ThrowsAny<ArgumentException>(() => LinuxMagicCloneOutput.Publish(absent, name, id, new byte[] { 7 }));
        Assert.False(Directory.Exists(absent));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
    }

    [Theory]
    [InlineData(8191, true)]
    [InlineData(8192, false)]
    public void AllChildAdmission_ReservesTheVisibleCloneBeforePublication(int count, bool accepted)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        for (int index = 0; index < count; index++)
            File.WriteAllBytes(Path.Combine(fixture.Root, ".unrelated-" + index), Array.Empty<byte>());
        if (accepted) LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 });
        else
        {
            var error = Assert.Throws<IOException>(() =>
                LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
            Assert.IsNotType<LinuxPublicationUncertainException>(error);
            Assert.Contains("child limit", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(count + (accepted ? 1 : 0), Directory.GetFileSystemEntries(fixture.Root).Length);
        Assert.Equal(accepted, File.Exists(fixture.Destination));
    }

    [Fact]
    public async Task CooperatingCaseEquivalentClones_ProduceExactlyOneOutput()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        async Task<bool> Publish(string name) => await Task.Run(() =>
        {
            try { LinuxMagicCloneOutput.Publish(fixture.Root, name, 140, new byte[] { 7 }); return true; }
            catch (IOException error) { Assert.Contains("already exists", error.Message, StringComparison.Ordinal); return false; }
        });
        bool[] results = await Task.WhenAll(Publish("magic_0140.dll"), Publish("MAGIC_0140.DLL"));
        Assert.Single(results.Where(value => value));
        string actual = Assert.Single(Directory.GetFileSystemEntries(fixture.Root));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(actual));
    }

    [SupportedOSPlatformGuard("linux")]
    private static bool CanRunNative() => LinuxReadFileSystem.IsSupported;

    private sealed class Fixture : IDisposable
    {
        internal string Base { get; }
        internal string Root { get; }
        internal string Destination => Path.Combine(Root, "magic_0140.dll");
        [SupportedOSPlatform("linux")]
        internal Fixture()
        {
            Base = TestDirectory.CreatePrivate(Path.Combine(TestDataPaths.RepoRoot, "work",
                "linux-magic-clone-tests", Guid.NewGuid().ToString("N"))).FullName;
            Root = TestDirectory.CreatePrivate(Path.Combine(Base, "output")).FullName;
        }
        public void Dispose() => Directory.Delete(Base, recursive: true);
    }
}
