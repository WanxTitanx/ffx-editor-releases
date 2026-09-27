using System;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// Resolves the immutable, bundled noclip web application. Game data stays in the user's selected
    /// extraction and is mapped separately by ViewerHub. No junction, temporary copy, shell command,
    /// network repair, or write-through to user data is performed here.
    /// </summary>
    public static class ServeDirBuilder
    {
        public static string? BuildNoclipServeDir()
        {
            string? root = ViewerHubRuntimeLayout.ResolveBundledViewerRoot(
                AppContext.BaseDirectory,
                "noclip");
            return root != null &&
                   Directory.Exists(Path.Combine(root, "static")) &&
                   File.Exists(Path.Combine(root, "basis_transcoder.wasm"))
                ? root
                : null;
        }
    }
}
