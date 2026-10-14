using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── TreasureMapPrerequisites ────────────────────────────────────────────────────────────
// Fast availability gate before building the index: takara.bin + jppc/map (mapout.vpa) +
// jppc/event/obj (.ebp). Any missing path is reported so the UI can show a clear message
// instead of failing mid-scan.
// ──────────────────────────────────────────────────────────────────────────────────────
public sealed record TreasureMapPrerequisiteResult(IReadOnlyList<string> MissingPaths)
{
    public bool IsValid => MissingPaths.Count == 0;
    public string Message => IsValid ? "Treasure Map source data available." :
        Strings.U_Bb_TreasureMapPrereq +
        Environment.NewLine + Environment.NewLine +
        string.Join(Environment.NewLine, MissingPaths.Select(p => string.Format(Strings.U_Bb_TreasureMapBullet, p))) +
        Environment.NewLine + Environment.NewLine +
        Strings.U_Bb_TreasureMapOnlyTakara;
}

public static class TreasureMapPrerequisites
{
    public static TreasureMapPrerequisiteResult Validate(string masterPath)
    {
        string root = System.IO.Path.GetFullPath(masterPath);
        var missing = new List<string>();
        RequireFile(Path.Combine(root, "jppc", "battle", "kernel", "takara.bin"), "jppc\\battle\\kernel\\takara.bin", missing);
        RequireFolderWith(Path.Combine(root, "jppc", "map"), "mapout.vpa", "jppc\\map (with mapout.vpa)", missing);
        RequireFolderWith(Path.Combine(root, "jppc", "event", "obj"), "*.ebp", "jppc\\event\\obj (with .ebp files)", missing);
        return new TreasureMapPrerequisiteResult(missing);
    }
    private static void RequireFile(string p, string d, List<string> m) { if (!File.Exists(p)) m.Add(d); }
    private static void RequireFolderWith(string p, string pat, string d, List<string> m)
    { if (!Directory.Exists(p) || !Directory.EnumerateFiles(p, pat, SearchOption.AllDirectories).Any()) m.Add(d); }
}
