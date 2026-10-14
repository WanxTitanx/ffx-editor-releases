using System;

namespace FFXProjectEditor.Services.Extras
{
    // ============================================================================
    // FfxAudioToolsBootstrap_Service - honest capability state, never a repair run
    // PURPOSE : report that runtime bootstrap/repair of audio tools is not part
    //           of the product, so UIs surface an unavailable state instead of
    //           executing repository scripts or downloading anything.
    // WHY     : the old implementation invoked powershell.exe against a repository
    //           script with -ExecutionPolicy Bypass, which is a release-surface
    //           arbitrary-execution path and depends on a developer checkout.
    // MAINT   : tool payloads are pinned at build time (see FFXProjectEditor.csproj
    //           audio tool Content rules and tools/*/PROVENANCE.md). There is no
    //           runtime installation flow; do not reintroduce one.
    // ============================================================================

    public static class FfxAudioToolsBootstrap_Service
    {
        public const string UnavailableReason =
            "Audio tools ship with the package; runtime bootstrap is not available in this build.";

        public static (bool Ok, string Message) RunBootstrap() => (false, UnavailableReason);

        public static (bool Ok, string Message) VerifyBattleAudioTools()
        {
            FfxAudioToolsHealth_Service.HealthReport report = FfxAudioToolsHealth_Service.Probe();
            return (report.RequiredReady, report.Summary);
        }
    }
}
