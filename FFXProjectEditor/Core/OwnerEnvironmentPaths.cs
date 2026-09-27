using System;

namespace FFXProjectEditor
{
    /// <summary>
    /// Owner-environment defaults resolved from environment variables with neutral fallbacks.
    /// Release payloads must not embed the owner's absolute corpus paths (the Candidate
    /// portability scan forbids PATH_CANDIDATE/drive-absolute findings in shipped assemblies).
    /// Set any of these in the dev environment to restore the original behavior:
    ///   FFX_EXTRACTED_ROOT   (was D:\FFX Extracted\FFX)
    ///   FFX_STEAM_DATA_ROOT  (was D:\SteamLibrary\steamapps\common)
    ///   FFX_MODS_ROOT        (was D:\FFX Mods)
    /// </summary>
    internal static class OwnerEnvironmentPaths
    {
        /// <summary>The extracted reference tree (ffx_ps2/ffx_data). Autodetection-only consumers.</summary>
        public static string ExtractedRoot =>
            Environment.GetEnvironmentVariable("FFX_EXTRACTED_ROOT")
            ?? "<ffx-extracted-root>/FFX";

        /// <summary>Root of the Steam common directory (contains the FFX&FFX-2 HD Remaster folder).</summary>
        public static string SteamCommonRoot =>
            Environment.GetEnvironmentVariable("FFX_STEAM_DATA_ROOT")
            ?? "<ffx-steam-common>/steamapps/common";

        /// <summary>Owner's modding toolbox root (VBFExtract, FFXDataParser, FFXED.jar...).</summary>
        public static string ModsRoot =>
            Environment.GetEnvironmentVariable("FFX_MODS_ROOT")
            ?? "<ffx-mods-root>";
    }
}