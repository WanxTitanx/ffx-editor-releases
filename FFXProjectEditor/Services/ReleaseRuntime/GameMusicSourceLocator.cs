// ============================================================================
// GameMusicSourceLocator — automatic ffx_music_bank00.fsb discovery
// PURPOSE : resolve the user-owned FSB bank from the paths the editor already
//           knows (game install, extracted ps3data, extraction root, volume
//           sibling "FFX Extracted") so the default track import can run
//           without a manual file pick.
// WHY     : the user selects the game install once; derivation of local music
//           must not require hunting a 554 MB bank inside data/mods or the VBF.
// MAINT   : probes are bounded — fixed relative paths plus one directory level
//           under "<volume>/FFX Extracted". No recursive disk scans, no writes.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public static class GameMusicSourceLocator
{
    public const string MusicFsbFileName = "ffx_music_bank00.fsb";
    public const string EnvironmentVariable = "FFX_MUSIC_FSB";

    // Inside the extracted tree: <extract>/ffx_data/gamedata/ps3data/sound_pc/music/.
    private static readonly string[] SoundPcMusicRelative =
        ["sound_pc", "music", MusicFsbFileName];

    private static readonly string[] Ps3DataTail = ["gamedata", "ps3data"];

    /// <summary>Returns the first existing candidate path, or null when no bank is reachable.</summary>
    public static string? Locate()
    {
        foreach (string candidate in EnumerateCandidates())
        {
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Inaccessible candidates are simply not capabilities.
            }
        }
        return null;
    }

    /// <summary>Ordered candidate paths. Public for diagnostics and tests.</summary>
    public static IEnumerable<string> EnumerateCandidates()
    {
        string? env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
            yield return Path.GetFullPath(env.Trim().Trim('"'));

        string? ps3Data = PortablePathResolver.Ps3DataRoot;
        if (ps3Data != null)
            yield return Combine(ps3Data, SoundPcMusicRelative);

        string? extracted = PortablePathResolver.ExtractedFfxRoot;
        if (extracted != null)
            yield return Combine(
                Path.Combine(extracted, "ffx_data", Ps3DataTail[0], Ps3DataTail[1]),
                SoundPcMusicRelative);

        string? gameRoot = PortablePathResolver.GameInstallRoot;
        if (gameRoot != null)
        {
            // Loader mirror layouts seen in the wild: FFX_Data\GameData\PS3Data (canonical
            // loader case) and the lowercase extraction spelling under data\mods.
            string mods = Path.Combine(gameRoot, "data", "mods");
            yield return Combine(
                Path.Combine(mods, "FFX_Data", "GameData", "PS3Data"),
                SoundPcMusicRelative);
            yield return Combine(
                Path.Combine(mods, "ffx_data", Ps3DataTail[0], Ps3DataTail[1]),
                SoundPcMusicRelative);
        }

        // Extraction sibling probe: the documented layout is
        // "<sibling-of-install>\FFX Extracted\<Game>\ffx_data\..." — typically a directory
        // next to SteamLibrary (D:\FFX Extracted on Windows, /mnt/<vol>/FFX Extracted on
        // Linux). Walking every anchor's ancestors catches both the drive-root case and
        // the mount-point case that Path.GetPathRoot alone would miss.
        foreach (string anchorParent in EnumerateAnchorParents(gameRoot))
        {
            foreach (string extractionParent in EnumerateExtractionParents(anchorParent))
            {
                foreach (string gameDir in EnumerateSubdirectoriesBounded(extractionParent))
                {
                    yield return Combine(
                        Path.Combine(gameDir, "ffx_data", Ps3DataTail[0], Ps3DataTail[1]),
                        SoundPcMusicRelative);
                }
            }
        }
    }

    // Ancestor directories of every known anchor (install root, master, extraction roots),
    // from the anchor's parent up to the filesystem root, deduplicated. Bounded: each
    // anchor contributes at most its own depth (~6 levels on real layouts).
    private static IEnumerable<string> EnumerateAnchorParents(string? gameRoot)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string? anchor in new[]
        {
            gameRoot,
            PortablePathResolver.MasterRoot,
            PortablePathResolver.ExtractedFfxRoot,
            PortablePathResolver.FfxPs2Root,
        })
        {
            if (string.IsNullOrWhiteSpace(anchor))
                continue;

            DirectoryInfo? current;
            try
            {
                current = Directory.GetParent(Path.GetFullPath(anchor));
            }
            catch
            {
                continue;
            }

            while (current != null && seen.Add(current.FullName))
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }
    }

    private static IEnumerable<string> EnumerateExtractionParents(string volumeRoot)
    {
        string[] names = ["FFX Extracted", "FFX_Extracted", "ffx_extracted"];
        foreach (string name in names)
        {
            string candidate = Path.Combine(volumeRoot, name);
            if (Directory.Exists(candidate))
                yield return candidate;
        }
    }

    private static IEnumerable<string> EnumerateSubdirectoriesBounded(string parent)
    {
        const int MaxEntries = 16;
        int yielded = 0;
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateDirectories(parent).GetEnumerator();
        }
        catch
        {
            yield break;
        }

        using (enumerator)
        {
            while (yielded < MaxEntries)
            {
                bool moved;
                try
                {
                    moved = enumerator.MoveNext();
                }
                catch
                {
                    yield break;
                }
                if (!moved)
                    yield break;
                yielded++;
                yield return enumerator.Current;
            }
        }
    }

    private static string Combine(string root, string[] tail)
    {
        string path = root;
        foreach (string segment in tail)
            path = Path.Combine(path, segment);
        return path;
    }
}
