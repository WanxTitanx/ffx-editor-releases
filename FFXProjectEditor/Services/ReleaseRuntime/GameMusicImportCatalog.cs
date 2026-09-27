// ============================================================================
// GameMusicImportCatalog — fixed, locally-derived FFX music import allowlist
// PURPOSE : define the only ten game streams the release runtime may decode.
// WHY     : game audio is never redistributed; users may explicitly derive a
//           small local subset from their own validated FSB bank.
// EVIDENCE: metadata was read with vgmstream r2117 from the user-owned FSB5
//           fingerprint pinned below on 2026-08-21. No audio bytes live here.
// MAINT   : a new bank fingerprint or track requires fresh metadata evidence
//           and tests. Never increase MaximumTracks without a release decision.
// ============================================================================

using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public sealed record GameMusicTrackDefinition(
    string Id,
    int StreamIndex,
    string StreamName,
    string DisplayName,
    string OutputFileName,
    int SampleRate,
    int Channels,
    long LoopStartSamples,
    long LoopEndSamples,
    long TotalSamples);

public static class GameMusicImportCatalog
{
    public const int MaximumTracks = 10;
    public const int ExpectedStreamCount = 89;
    public const string ManifestFileName = "game-music-import.json";
    public const string ValidatedVgmstreamSha256 =
        "9431CE9A9422257E126BA01A92C26E6244E07894B33056EEFC8269A3D1B43DF7";

    // The Linux build of the same pinned vgmstream release (r2117) ships as an extensionless
    // static ELF named vgmstream-cli. Its identity is validated exactly like the Windows tool.
    public const string ValidatedVgmstreamSha256Linux =
        "2B05458F470AC6E051848E08CBD6D31A074E1808A648E2C6007418AD4242FC58";

    public const string VgmstreamFileNameWindows = "vgmstream-cli.exe";
    public const string VgmstreamFileNameLinux = "vgmstream-cli";

    public static string VgmstreamFileNameForCurrentPlatform =>
        OperatingSystem.IsWindows() ? VgmstreamFileNameWindows : VgmstreamFileNameLinux;

    public static string ValidatedVgmstreamSha256ForCurrentPlatform =>
        OperatingSystem.IsWindows() ? ValidatedVgmstreamSha256 : ValidatedVgmstreamSha256Linux;

    /// <summary>Expected SHA-256 for an approved tool file name, or null when the name is unknown.</summary>
    public static string? ValidatedVgmstreamSha256For(string? fileName)
    {
        if (string.Equals(fileName, VgmstreamFileNameWindows, StringComparison.OrdinalIgnoreCase))
            return ValidatedVgmstreamSha256;
        if (string.Equals(fileName, VgmstreamFileNameLinux, StringComparison.Ordinal))
            return ValidatedVgmstreamSha256Linux;
        return null;
    }

    /// <summary>Exact (fileName, sha256) identity check for an approved vgmstream tool binary.</summary>
    public static bool IsValidatedVgmstreamTool(string? fileName, string? sha256)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(sha256))
            return false;
        if (string.Equals(fileName, VgmstreamFileNameWindows, StringComparison.OrdinalIgnoreCase))
            return string.Equals(sha256, ValidatedVgmstreamSha256, StringComparison.OrdinalIgnoreCase);
        if (string.Equals(fileName, VgmstreamFileNameLinux, StringComparison.Ordinal))
            return string.Equals(sha256, ValidatedVgmstreamSha256Linux, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public static IReadOnlyList<string> ValidatedSourceSha256 { get; } = Array.AsReadOnly<string>(
    [
        "C3D4547F9716F72FB346E6EB1BDC817FC246BAFA0E18202E189AFB24DBF0ECE4",
    ]);

    public static IReadOnlyList<GameMusicTrackDefinition> Tracks { get; } = Array.AsReadOnly<GameMusicTrackDefinition>(
    [
        new("1_02_zanarkand", 1, "1_02_zanarkand", "To Zanarkand", "001_1_02_zanarkand.wav",
            44_100, 2, 1_119_010, 4_174_895, 4_453_376),
        new("1_05_otherworld", 4, "1_05_otherworld", "Otherworld", "004_1_05_otherworld.wav",
            44_100, 2, 2_588_291, 6_364_848, 6_553_600),
        new("1_17_the_blitzers", 15, "1_17_the_blitzers", "The Blitzers", "015_1_17_the_blitzers.wav",
            44_100, 2, 610_374, 5_169_640, 5_278_528),
        new("1_18_besaid", 16, "1_18_besaid", "Besaid", "016_1_18_besaid.wav",
            44_100, 2, 1_694_002, 8_044_458, 8_238_080),
        new("2_01_yuna_s_theme", 25, "2_01_yuna_s_theme", "Yuna's Theme", "025_2_01_yuna_s_theme.wav",
            44_100, 2, 760_631, 4_691_946, 4_877_888),
        new("2_06_luca", 29, "2_06_luca", "Luca", "029_2_06_luca.wav",
            44_100, 2, 1_758_073, 9_877_276, 10_038_272),
        new("2_11_blitz_off", 33, "2_11_blitz_off", "Blitz Off", "033_2_11_blitz_off.wav",
            44_100, 2, 992_003, 8_424_980, 8_549_376),
        new("4_07_wandering_flame", 70, "4_07_wandering_flame", "Wandering Flame",
            "070_4_07_wandering_flame.wav", 44_100, 2, 844_652, 8_897_104, 9_145_344),
        new("4_08_someday_the_dream_will_end", 71, "4_08_someday_the_dream_will_end",
            "Someday the Dream Will End", "071_4_08_someday_the_dream_will_end.wav",
            44_100, 2, 242_375, 5_173_631, 5_257_728),
        new("m_71_wakka_s_theme", 89, "m_71_wakka_s_theme", "Wakka's Theme", "089_m_71_wakka_s_theme.wav",
            44_100, 2, 530_180, 6_139_747, 6_341_504),
    ]);

    public static GameMusicTrackDefinition GetRequired(string id)
    {
        foreach (GameMusicTrackDefinition track in Tracks)
        {
            if (string.Equals(track.Id, id, StringComparison.Ordinal))
            {
                return track;
            }
        }

        throw new KeyNotFoundException($"Unknown approved FFX music track id: {id}");
    }
}
