using System;
using System.IO;
using Xunit;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── Private fixture directory contract ──
// Test fixtures need atomic 0700 creation on Linux without invoking the Unix-only
// Directory.CreateDirectory overload on Windows.
public sealed class TestDirectoryTests : IDisposable
{
    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(),
        "ffx-private-directory-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void CreatePrivate_CreatesDirectoryWithPlatformContract()
    {
        string path = Path.Combine(_scratch, "private");

        DirectoryInfo created = TestDirectory.CreatePrivate(path);

        Assert.True(created.Exists);
        Assert.Equal(Path.GetFullPath(path), created.FullName);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(path));
        }
        else
        {
            Assert.True(OperatingSystem.IsWindows(), "Only Linux and Windows test hosts are supported.");
        }
    }
}
