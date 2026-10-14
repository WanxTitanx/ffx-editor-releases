using System;
using Avalonia;

namespace FFXProjectEditor.Services;

// ── Native desktop composition ──
// Select only the host windowing subsystem; calling Win32 on Linux loads kernel32.
// MAINT: keep Skia/fonts/logging in Program and backend versions aligned in the project.
internal static class DesktopPlatformBootstrap
{
    internal static AppBuilder UseSupportedDesktop(this AppBuilder builder)
    {
#if FFX_ENABLE_WIN32
        if (OperatingSystem.IsWindows())
            return builder.UseWin32();
#endif
#if FFX_ENABLE_X11
        if (OperatingSystem.IsLinux())
            return builder.UseX11();
#endif
        throw new PlatformNotSupportedException("The editor supports Windows and Linux desktop backends.");
    }
}
