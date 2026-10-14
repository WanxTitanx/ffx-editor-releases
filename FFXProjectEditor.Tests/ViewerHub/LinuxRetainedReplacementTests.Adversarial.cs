using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

// ── Boundary interference and refusal coverage ──
// Fixtures change only their private output names. These cases complement the exact exchange
// receipts in the other partial; they do not model a mandatory same-UID filesystem sandbox.
// MAINT: keep hook assertions non-vacuous and preserve every artifact after uncertain exchange.
public sealed partial class LinuxRetainedReplacementTests
{
    [Theory]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    [InlineData("fifo")]
    [InlineData("directory")]
    [InlineData("shared-write")]
    public void UnsafeTarget_IsRefusedBeforePublishing_WithoutFollowingLinks(string kind)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        output.PublishNew("unrelated.bin", new byte[] { 7 });
        var prior = output.ReadSnapshot("target.bin")!;
        if (kind is "symlink" or "fifo" or "directory")
            File.Move(fixture.Path("target.bin"), fixture.Path("original.bin"));
        if (kind == "symlink") File.CreateSymbolicLink(fixture.Path("target.bin"), fixture.Path("unrelated.bin"));
        if (kind == "hardlink") fixture.Run("ln", fixture.Path("target.bin"), fixture.Path("alias.bin"));
        if (kind == "fifo") fixture.Run("mkfifo", fixture.Path("target.bin"));
        if (kind == "directory") TestDirectory.CreatePrivate(fixture.Path("target.bin"));
        if (kind == "shared-write") File.SetUnixFileMode(fixture.Path("target.bin"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);
        Assert.ThrowsAny<IOException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
        Assert.False(File.Exists(fixture.Path("retained.bin")));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(fixture.Path("unrelated.bin")));
        output.PublishNew("still-usable.bin", new byte[] { 4 });
    }

    [Theory]
    [InlineData("target.bin", "bytes")]
    [InlineData("retained.bin", "bytes")]
    [InlineData("target.bin", "mtime")]
    [InlineData("retained.bin", "mtime")]
    [InlineData("target.bin", "safe-mode-drift")]
    [InlineData("retained.bin", "safe-mode-drift")]
    [InlineData("target.bin", "hardlink")]
    [InlineData("retained.bin", "hardlink")]
    [InlineData("target.bin", "symlink")]
    [InlineData("retained.bin", "symlink")]
    [InlineData("target.bin", "fifo")]
    [InlineData("retained.bin", "fifo")]
    public void PostExchangeDrift_IsNotAcceptedAsAnExpectedCtimeChange(string changedLeaf, string change)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        output.PublishNew("unrelated.bin", new byte[] { 7 });
        var prior = output.ReadSnapshot("target.bin")!;
        string changed = fixture.Path(changedLeaf), alias = fixture.Path("alias.bin");
        string saved = fixture.Path("saved-observed.bin"), unrelated = fixture.Path("unrelated.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "replace-exchanged") return;
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            reached = true;
            if (change == "bytes") File.WriteAllBytes(changed, new byte[] { 9 });
            if (change == "mtime") File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddMinutes(1));
            if (change == "safe-mode-drift") File.SetUnixFileMode(changed,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            if (change == "hardlink") fixture.Run("ln", changed, alias);
            if (change is "symlink" or "fifo") File.Move(changed, saved);
            if (change == "symlink") File.CreateSymbolicLink(changed, unrelated);
            if (change == "fifo") fixture.Run("mkfifo", changed);
        };
        try
        {
            var error = Assert.Throws<LinuxReplacementUncertainException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            Assert.True(error.ExchangeCompleted);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(unrelated));
        Assert.Equal(new byte[] { changedLeaf == "target.bin" ? (byte)1 : (byte)2 },
            File.ReadAllBytes(fixture.Path(changedLeaf == "target.bin" ? "retained.bin" : "target.bin")));
        if (change is "symlink" or "fifo")
            Assert.Equal(new byte[] { changedLeaf == "target.bin" ? (byte)2 : (byte)1 }, File.ReadAllBytes(saved));
        Assert.Throws<InvalidOperationException>(() => output.ReadSnapshot("target.bin"));
    }

    [Fact]
    public void MissingLeafAtSyscallWindow_RetainsStageAndOriginal_WithTypedNativeCause()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        string target = fixture.Path("target.bin"), saved = fixture.Path("saved-observed.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "replace-before-exchange") return;
            reached = true;
            File.Move(target, saved);
        };
        try
        {
            var error = Assert.Throws<LinuxReplacementUncertainException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            Assert.False(error.ExchangeCompleted);
            Assert.Equal(2, Assert.IsType<LinuxReadFileSystem.LinuxNativeIOException>(error.InnerException).Errno);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(saved));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(fixture.Path("retained.bin")));
        Assert.False(File.Exists(target));
        Assert.Throws<InvalidOperationException>(() => output.PublishNew("must-not-write.bin", new byte[] { 9 }));
    }

    [Fact]
    public void PreLinkFailure_LeavesCapabilityUsable_AndNoNamedStage()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "replace-before-link") return;
            reached = true;
            throw new IOException("Injected before publication.");
        };
        try
        {
            Assert.Throws<IOException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Single(Directory.GetFiles(fixture.Root));
        Assert.Equal(new byte[] { 1 }, output.ReadSnapshot("target.bin")!.Bytes.ToArray());
        output.PublishNew("still-usable.bin", new byte[] { 4 });
    }

    [Theory]
    [InlineData("target.bin", (byte)1)]
    [InlineData("retained.bin", (byte)2)]
    public void SameBytesAtLastSyscallWindow_DoNotSubstituteForInodeIdentity(string changedLeaf, byte duplicateByte)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        output.PublishNew("duplicate.bin", new byte[] { duplicateByte });
        var prior = output.ReadSnapshot("target.bin")!;
        string changed = fixture.Path(changedLeaf), saved = fixture.Path("saved-observed.bin");
        string duplicate = fixture.Path("duplicate.bin");
        bool reached = false;
        LinuxOwnedOutputDirectory.BeforeOperationForTests = (stage, _) =>
        {
            if (stage != "replace-before-exchange") return;
            reached = true;
            File.Move(changed, saved);
            File.Move(duplicate, changed);
        };
        try
        {
            var error = Assert.Throws<LinuxReplacementUncertainException>(() =>
                output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
            Assert.True(reached);
            Assert.True(error.ExchangeCompleted);
        }
        finally { LinuxOwnedOutputDirectory.BeforeOperationForTests = null; }
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(fixture.Path("target.bin")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(fixture.Path("retained.bin")));
        Assert.Equal(new byte[] { duplicateByte }, File.ReadAllBytes(saved));
        Assert.Equal(3, Directory.GetFiles(fixture.Root).Length);
    }

    [Theory]
    [InlineData("other-object")]
    [InlineData("owner-mismatch")]
    [InlineData("ctime-only")]
    public void FullPreExchangeSnapshot_IsRequiredEvenWhenVisibleBytesMatch(string difference)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var prior = output.ReadSnapshot("target.bin")!;
        if (difference == "other-object")
        {
            output.PublishNew("other.bin", new byte[] { 1 });
            prior = output.ReadSnapshot("other.bin")!;
        }
        if (difference == "owner-mismatch") prior = new LinuxOwnedOutputDirectory.Snapshot(prior.Bytes,
            prior.Observation with { OwnerId = prior.Observation.OwnerId + 1 });
        if (difference == "ctime-only")
        {
            File.SetUnixFileMode(fixture.Path("target.bin"), UnixFileMode.UserRead);
            File.SetUnixFileMode(fixture.Path("target.bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var current = output.ReadSnapshot("target.bin")!;
            Assert.Equal(prior.Observation.File.ModifiedSeconds, current.Observation.File.ModifiedSeconds);
            Assert.Equal(prior.Observation.File.ModifiedNanoseconds, current.Observation.File.ModifiedNanoseconds);
            Assert.NotEqual((prior.Observation.File.ChangedSeconds, prior.Observation.File.ChangedNanoseconds),
                (current.Observation.File.ChangedSeconds, current.Observation.File.ChangedNanoseconds));
        }
        Assert.ThrowsAny<IOException>(() => output.ReplaceRetainingDisplaced("target.bin", prior, "retained.bin", new byte[] { 2 }));
        Assert.False(File.Exists(fixture.Path("retained.bin")));
        Assert.Equal(new byte[] { 1 }, output.ReadSnapshot("target.bin")!.Bytes.ToArray());
    }

    [Fact]
    public void ExistingSafeNonPrivateMode_IsPreserved_WhileReplacementIsPrivate()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(fixture.Root);
        output.PublishNew("target.bin", new byte[] { 1 });
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(fixture.Path("target.bin"), mode);
        output.ReplaceRetainingDisplaced("target.bin", output.ReadSnapshot("target.bin")!, "retained.bin", new byte[] { 2 });
        Assert.Equal(mode, File.GetUnixFileMode(fixture.Path("retained.bin")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Path("target.bin")));
    }
}
