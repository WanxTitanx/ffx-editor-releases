using System;
using System.Diagnostics;
using System.IO;

using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    /// <summary>
    /// Shared "open this viewer URL in the system browser" path for every surface whose embed is a
    /// WebView2 host (Windows-only). On Linux/macOS the in-process server and data layer work the
    /// same, so the same loopback URL simply renders in the user's real browser.
    /// </summary>
    public static class ExternalBrowserLauncher
    {
        /// <summary>True when the platform can host the WebView2 embed (Windows only).</summary>
        public static bool IsEmbeddedViewerSupported => OperatingSystem.IsWindows();

        /// <summary>
        /// Opens <paramref name="url"/> in the best available browser. Prefers a real browser binary
        /// over xdg-open because a broken text/html association can route http:// to a text editor.
        /// Returns true when a process was spawned.
        /// </summary>
        public static bool Open(string url)
        {
            try
            {
                string? browser = OperatingSystem.IsMacOS() ? "open"
                    : FindOnPath("firefox") ?? FindOnPath("google-chrome") ?? FindOnPath("chromium")
                        ?? FindOnPath("chromium-browser") ?? FindOnPath("brave-browser")
                        ?? FindOnPath("microsoft-edge") ?? FindOnPath("opera")
                        ?? FindOnPath("xdg-open");
                if (browser == null)
                    return false;

                var psi = new ProcessStartInfo
                {
                    FileName = browser,
                    Arguments = url,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // Snap/Flatpak browsers on a Wayland session need WAYLAND_DISPLAY; they cannot read
                // the XWayland xauth cookie (confinement), so DISPLAY alone leaves them unable to
                // open a window. If the editor itself was launched without the session env (ssh,
                // systemd unit), forward the hint when the session socket exists.
                if (OperatingSystem.IsLinux() &&
                    (!psi.Environment.TryGetValue("WAYLAND_DISPLAY", out string? waylandDisplay) ||
                     string.IsNullOrEmpty(waylandDisplay)))
                {
                    string? runtimeDir = psi.Environment.TryGetValue("XDG_RUNTIME_DIR", out string? xdg)
                        ? xdg : null;
                    if (!string.IsNullOrEmpty(runtimeDir) &&
                        File.Exists(Path.Combine(runtimeDir, "wayland-0")))
                        psi.Environment["WAYLAND_DISPLAY"] = "wayland-0";
                }
                Process.Start(psi);
                DebugLog.Info("ViewerShell.ExternalBrowser",
                    $"Opened viewer URL in the system browser ({browser}).");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Error("ViewerShell.ExternalBrowser",
                    "Failed to launch the system browser for the viewer URL.", ex);
                return false;
            }
        }

        private static string? FindOnPath(string executable)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, executable);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
