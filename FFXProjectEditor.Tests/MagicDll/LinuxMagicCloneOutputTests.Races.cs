using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

// ── Native clone transaction boundaries ──
// Hooks make late changes deterministic; no fallback, success claim or cleanup may hide them.
public sealed partial class LinuxMagicCloneOutputTests
{
    [Fact]
    public void LateExactDestinationOccupation_NeverOverwritesTheOccupant()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, path) =>
        {
            if (stage != "create-before-link") return;
            reached = true;
            File.WriteAllBytes(path, new byte[] { 13 });
        };
        try
        {
            Assert.ThrowsAny<IOException>(() =>
                LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
            Assert.True(reached);
            Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(fixture.Destination));
            Assert.Single(Directory.GetFileSystemEntries(fixture.Root));
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
    }

    [Fact]
    public void LateLegacyCollision_ReportsUncertaintyAndPreservesBothFiles()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string legacy = Path.Combine(fixture.Root, "MAGIC_140.DLL");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "create-before-link") return;
            reached = true;
            File.WriteAllBytes(legacy, new byte[] { 13 });
        };
        try
        {
            var error = Assert.Throws<LinuxPublicationUncertainException>(() =>
                LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
            Assert.True(reached);
            Assert.Equal(fixture.Destination, error.PossiblyPublishedPath);
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Destination));
            Assert.Equal(new byte[] { 13 }, File.ReadAllBytes(legacy));
            Assert.Equal(2, Directory.GetFileSystemEntries(fixture.Root).Length);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
    }

    [Fact]
    public void PostPublicationSameByteSubstitution_CannotClaimThePublishedIdentity()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string saved = fixture.Destination + ".preserved";
        bool reached = false;
        LinuxMagicCloneOutput.BeforeReadbackForTests = path =>
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            reached = true;
            File.Move(path, saved);
            File.WriteAllBytes(path, new byte[] { 7 });
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        };
        try
        {
            var error = Assert.Throws<LinuxPublicationUncertainException>(() =>
                LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
            Assert.True(reached);
            Assert.Equal(fixture.Destination, error.PossiblyPublishedPath);
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Destination));
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(saved));
        }
        finally { LinuxMagicCloneOutput.BeforeReadbackForTests = null; }
    }

    [Theory]
    [InlineData("create-before-link", false)]
    [InlineData("create-linked", true)]
    public void RootRename_DoesNotRedirectPublicationOrEraseEvidence(string boundary, bool linked)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string moved = Path.Combine(fixture.Base, "moved");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != boundary) return;
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            reached = true;
            Directory.Move(fixture.Root, moved);
            TestDirectory.CreatePrivate(fixture.Root);
        };
        try
        {
            var error = Assert.ThrowsAny<IOException>(() =>
                LinuxMagicCloneOutput.Publish(fixture.Root, "magic_0140.dll", 140, new byte[] { 7 }));
            Assert.True(reached);
            Assert.Equal(linked, error is LinuxPublicationUncertainException);
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
            Assert.Equal(linked, File.Exists(Path.Combine(moved, "magic_0140.dll")));
            if (linked) Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(moved, "magic_0140.dll")));
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
    }
}
