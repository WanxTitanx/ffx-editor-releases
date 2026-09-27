using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps2
{
    // Read-only reader for the PS2 "RSD" model bundle (MatEditor toolchain).
    // Proved facts (see docs/history/FFX_PS2_MASTER_ATLAS + memory project-ffx-ps2-rsd):
    //   .rsd = ASCII manifest "@RSD940102" with PLY=/MAT=/GRP=/NTEX=/TEX[i]=/VGR=
    //   .ply = ASCII mesh "@PLY940102" (counts line "NV NN NP")
    //   .ma2 = ASCII material table "@MAT990928" (# Number of Items -> N)
    //   TEX[i] -> .tm2 in the sibling ..\tim\ folder
    // Nothing here mutates files. This is structural truth only (no 3D render).
    internal sealed class Ps2RsdModelEntry
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required string Lane { get; init; }
        public required string PlyName { get; init; }
        public required string MatName { get; init; }
        public required string GrpName { get; init; }
        public required string VgrName { get; init; }
        public required IReadOnlyList<string> TextureNames { get; init; }
        public required int VertexCount { get; init; }
        public required int NormalCount { get; init; }
        public required int PolygonCount { get; init; }
        public required int MaterialCount { get; init; }
        public required bool PlyResolved { get; init; }
        public required bool MatResolved { get; init; }
        public required int TexturesResolved { get; init; }
        public required string Status { get; init; }

        public string GeometrySummary => $"V {VertexCount} · N {NormalCount} · Poly {PolygonCount}";
        public string LinkageSummary => $"PLY {(PlyResolved ? "ok" : "miss")} · MAT {(MatResolved ? "ok" : "miss")} · TEX {TexturesResolved}/{TextureNames.Count}";
        public string TextureSummary => TextureNames.Count == 0 ? "(none)" : string.Join(", ", TextureNames);
    }

    internal sealed class Ps2RsdModelSnapshot
    {
        public required IReadOnlyList<Ps2RsdModelEntry> Entries { get; init; }
        public required string OverviewSummary { get; init; }
    }

    internal static class Ps2RsdModelReader
    {
        public static Ps2RsdModelSnapshot Scan(string? ffxPs2Root)
        {
            List<Ps2RsdModelEntry> entries = [];
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return new Ps2RsdModelSnapshot { Entries = entries, OverviewSummary = "ffx_ps2 root not detected." };

            IEnumerable<string> rsdFiles;
            try
            {
                rsdFiles = Directory.EnumerateFiles(ffxPs2Root, "*.rsd", SearchOption.AllDirectories);
            }
            catch
            {
                return new Ps2RsdModelSnapshot { Entries = entries, OverviewSummary = "ffx_ps2 RSD scan failed." };
            }

            foreach (string rsdPath in rsdFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Ps2RsdModelEntry? entry = TryParse(rsdPath, ffxPs2Root);
                if (entry != null)
                    entries.Add(entry);
            }

            int totalTex = entries.Sum(entry => entry.TextureNames.Count);
            int fullyLinked = entries.Count(entry => entry.Status == "proved");
            return new Ps2RsdModelSnapshot
            {
                Entries = entries,
                OverviewSummary = $"{entries.Count} RSD model bundles · {fullyLinked} fully linked · {totalTex} texture refs"
            };
        }

        static Ps2RsdModelEntry? TryParse(string rsdPath, string root)
        {
            try
            {
                string[] lines = File.ReadAllLines(rsdPath);
                if (lines.Length == 0 || !lines[0].StartsWith("@RSD", StringComparison.Ordinal))
                    return null;

                string ply = "", mat = "", grp = "", vgr = "";
                List<string> textures = [];
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("PLY=", StringComparison.OrdinalIgnoreCase)) ply = line[4..].Trim();
                    else if (line.StartsWith("MAT=", StringComparison.OrdinalIgnoreCase)) mat = line[4..].Trim();
                    else if (line.StartsWith("GRP=", StringComparison.OrdinalIgnoreCase)) grp = line[4..].Trim();
                    else if (line.StartsWith("VGR=", StringComparison.OrdinalIgnoreCase)) vgr = line[4..].Trim();
                    else if (line.StartsWith("TEX[", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = line.IndexOf('=');
                        if (eq >= 0)
                        {
                            string tex = line[(eq + 1)..].Trim();
                            if (tex.Length > 0)
                                textures.Add(tex);
                        }
                    }
                }

                string dir = Path.GetDirectoryName(rsdPath) ?? "";
                bool plyResolved = ply.Length > 0 && File.Exists(Path.Combine(dir, ply));
                bool matResolved = mat.Length > 0 && File.Exists(Path.Combine(dir, mat));

                (int nv, int nn, int np) = plyResolved ? ReadPlyCounts(Path.Combine(dir, ply)) : (0, 0, 0);
                int matCount = matResolved ? ReadMatCount(Path.Combine(dir, mat)) : 0;

                string timDir = Path.Combine(dir, "..", "tim");
                int texResolved = textures.Count(tex => File.Exists(Path.Combine(timDir, tex)));

                string lower = rsdPath.ToLowerInvariant();
                string lane = lower.Contains("encount2") ? "encount2"
                    : lower.Contains("encount") ? "encount"
                    : lower.Contains("mag_") ? "mag"
                    : "other";

                bool fullyLinked = plyResolved && matResolved && textures.Count > 0 && texResolved == textures.Count;

                return new Ps2RsdModelEntry
                {
                    Name = Path.GetFileNameWithoutExtension(rsdPath),
                    FullPath = rsdPath,
                    RelativePath = Path.GetRelativePath(root, rsdPath),
                    Lane = lane,
                    PlyName = ply,
                    MatName = mat,
                    GrpName = grp,
                    VgrName = vgr,
                    TextureNames = textures,
                    VertexCount = nv,
                    NormalCount = nn,
                    PolygonCount = np,
                    MaterialCount = matCount,
                    PlyResolved = plyResolved,
                    MatResolved = matResolved,
                    TexturesResolved = texResolved,
                    Status = fullyLinked ? "proved" : "structural"
                };
            }
            catch
            {
                return null;
            }
        }

        static (int Vertices, int Normals, int Polygons) ReadPlyCounts(string plyPath)
        {
            try
            {
                bool nextIsCounts = false;
                foreach (string raw in File.ReadLines(plyPath))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("# Number of Vertices", StringComparison.OrdinalIgnoreCase))
                    {
                        nextIsCounts = true;
                        continue;
                    }
                    if (nextIsCounts && line.Length > 0 && !line.StartsWith('#'))
                    {
                        string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3
                            && int.TryParse(parts[0], out int nv)
                            && int.TryParse(parts[1], out int nn)
                            && int.TryParse(parts[2], out int np))
                            return (nv, nn, np);
                        return (0, 0, 0);
                    }
                }
            }
            catch
            {
            }
            return (0, 0, 0);
        }

        static int ReadMatCount(string matPath)
        {
            try
            {
                bool nextIsCount = false;
                foreach (string raw in File.ReadLines(matPath))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("# Number of Items", StringComparison.OrdinalIgnoreCase))
                    {
                        nextIsCount = true;
                        continue;
                    }
                    if (nextIsCount && line.Length > 0 && !line.StartsWith('#'))
                        return int.TryParse(line, out int count) ? count : 0;
                }
            }
            catch
            {
            }
            return 0;
        }
    }
}
