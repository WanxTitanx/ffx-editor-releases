using System;
using System.IO;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── Cross-platform private fixture directories ──
// Linux fixtures require atomic 0700 creation, while the Unix-mode overload is unsupported
// on Windows. MAINT: keep unsupported hosts fail-closed instead of silently weakening privacy.
internal static class TestDirectory
{
    private const UnixFileMode PrivateMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static DirectoryInfo CreatePrivate(string path)
    {
        if (OperatingSystem.IsLinux())
            return Directory.CreateDirectory(path, PrivateMode);
        if (OperatingSystem.IsWindows())
            return Directory.CreateDirectory(path);

        throw new PlatformNotSupportedException(
            "Private test directories require Linux or Windows.");
    }
}
