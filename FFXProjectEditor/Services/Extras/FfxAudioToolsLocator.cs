using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Services.Extras
{
    // ============================================================================
    // FfxAudioToolsLocator - canonical app-private audio tool resolution
    // PURPOSE : resolve vgmstream/fsbext/fsbankcl ONLY from the installed package
    //           tree, with platform-aware honest availability.
    // WHY     : the previous resolver honored arbitrary override paths and fell
    //           back to the repository root and ModsRoot, which can execute an
    //           unreviewed binary from user-controlled locations. Public builds
    //           must trust nothing outside AppContext.BaseDirectory.
    // MAINT   : adding a tool means (1) a pinned, reviewed payload under tools/ or
    //           ExternalLibs/, (2) exactly one canonical relative path here, (3) a
    //           platform gate that returns null where the tool is not shipped.
    // ============================================================================

    public static class FfxAudioToolsLocator
    {
        public static string? LocateVgmStream() =>
            OperatingSystem.IsWindows()
                ? PortablePathResolver.BundledPath("tools", "vgmstream", "vgmstream-cli.exe")
                : OperatingSystem.IsLinux()
                    ? PortablePathResolver.BundledPath("tools", "vgmstream", "linux-x64", "vgmstream-cli")
                    : null;

        public static string? LocateFsbExt() =>
            OperatingSystem.IsWindows()
                ? PortablePathResolver.BundledPath("tools", "fsbext", "fsbext.exe")
                : null;

        public static string? LocateFsbankCl() =>
            OperatingSystem.IsWindows()
                ? PortablePathResolver.BundledPath("tools", "fsbankcl", "fsbankcl.exe")
                  ?? PortablePathResolver.BundledPath("tools", "fsbankcl", "fsbankexcl.exe")
                : null;

        public static bool VgmStreamAvailable => LocateVgmStream() != null;
        public static bool FsbExtAvailable => LocateFsbExt() != null;
        public static bool FsbankClAvailable => LocateFsbankCl() != null;
        public static bool CustomSfxToolsReady => VgmStreamAvailable && FsbExtAvailable;
        public static bool AllBattleSfxToolsReady => CustomSfxToolsReady && FsbankClAvailable;

        public static string BundledToolsStatus
        {
            get
            {
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                    return "Audio tools: unsupported platform.";

                string vgmWhere = DescribeToolPath(LocateVgmStream());
                if (OperatingSystem.IsLinux())
                {
                    // fsbext/fsbankcl are Windows-only authoring tools; keep the status honest.
                    return $"Audio tools (bundled): vgmstream [{vgmWhere}]; fsbext/fsbankcl unavailable on Linux.";
                }

                string fsbWhere = DescribeToolPath(LocateFsbExt());
                string fsbank = FsbankClAvailable ? " + fsbankcl" : "";
                return $"Audio tools (bundled): vgmstream [{vgmWhere}] + fsbext [{fsbWhere}]{fsbank}.";
            }
        }

        /// <summary>Directory containing the tool exe (DLL search path for fsbankexcl/Qt).</summary>
        public static string ToolDirectory(string cliPath) =>
            Path.GetDirectoryName(cliPath) ?? AppContext.BaseDirectory;

        public static string DescribeToolPath(string? path) =>
            string.IsNullOrWhiteSpace(path) ? "missing" : "bundled";

        public static IReadOnlyList<string> SdkSearchRootsPublic => FfxFsbankClImport_Service.SdkSearchRootsPublic;
    }
}