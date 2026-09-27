using System.Globalization;
using FFXProjectEditor.FfxLib.BattleMap;

namespace BattleCorpusScanLab
{
    /// <summary>
    /// Battle position CORPUS scanner (2026-08-04).
    /// Decodes the chunk3 arena anchors of EVERY battle bin (Steam mod + FFX Extracted) and computes
    /// derived formation metrics (party-&gt;monster distance, wrap arc, lateral spread, camera).
    /// READ-ONLY. Purpose: learn how the engine positions actors so the CustomMix auto-builder /
    /// ApplyCustomPositions can be calibrated to the real corpus.
    /// </summary>
    internal static class Program
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static int Main()
        {
            bool cameraMode = false;
            for (int i = 0; i < Environment.GetCommandLineArgs().Length; i++)
                if (Environment.GetCommandLineArgs()[i] == "--camera") cameraMode = true;
            if (cameraMode) return CameraCrossScan();

            string[] modRoots =
            {
                @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\jppc\battle\btl",
            };
            string extractedRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

            var files = new List<(string id, string path)>();
            foreach (var root in modRoots)
                if (Directory.Exists(root))
                    foreach (var p in Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories))
                        files.Add((Path.GetFileNameWithoutExtension(p), p));
            if (Directory.Exists(extractedRoot))
                foreach (var p in Directory.GetFiles(extractedRoot, "*.bin", SearchOption.AllDirectories))
                {
                    string id = Path.GetFileNameWithoutExtension(p);
                    if (!files.Any(f => f.id == id))   // mod wins; extracted fills the gaps
                        files.Add((id, p));
                }

            Console.WriteLine($"battles to scan : {files.Count}");
            var rows = new List<string[]>();
            int ok = 0, noChunk3 = 0, fail = 0;

            foreach (var (id, path) in files.OrderBy(f => f.id))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    var anchors = BattleArenaAnchors_File.ReadFromBattleBin(id, bytes);
                    ok++;
                    rows.Add(Analyze(id, anchors));
                }
                catch
                {
                    noChunk3++;   // no usable chunk3 anchors (or non-battle bin)
                }
            }

            fail = files.Count - ok - noChunk3;
            Console.WriteLine($"decoded ok      : {ok}");
            Console.WriteLine($"no usable chunk3: {noChunk3}");
            Console.WriteLine($"hard fail       : {fail}");

            // ---- CSV export ----
            string outCsv = Path.Combine(AppContext.BaseDirectory, "battle_position_corpus.csv");
            using (var w = new StreamWriter(outCsv, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("id,areaCount,partyCount,monsterCount," +
                            "party_cx,party_cz,mon_dist_mean,cam_x,cam_y,cam_z," +
                            "arc_deg,spread,mon_ymin,mon_ymax");
                foreach (var r in rows)
                    w.WriteLine(string.Join(",", r.Select(v => v)));
            }
            Console.WriteLine($"csv written     : {outCsv}  ({rows.Count} rows)");

            // ---- Summary (aggregated) ----
            var dists = rows.Where(r => r[6] != "").Select(r => double.Parse(r[6], Inv)).ToList();
            var arcs = rows.Where(r => r[10] != "").Select(r => double.Parse(r[10], Inv)).ToList();
            var spreads = rows.Where(r => r[11] != "").Select(r => double.Parse(r[11], Inv)).ToList();
            Console.WriteLine("\n--- aggregate (battles with data) ---");
            Console.WriteLine($"party->mon distance : n={dists.Count} mean={dists.Average():F1} p10={dists.OrderBy(x=>x).Skip(dists.Count/10).First():F1} p90={dists.OrderBy(x=>x).Skip(dists.Count*9/10).First():F1}");
            Console.WriteLine($"wrap arc (+-deg)    : n={arcs.Count} mean={arcs.Average():F1}");
            Console.WriteLine($"lateral spread      : n={spreads.Count} mean={spreads.Average():F1}");

            Console.WriteLine("\n--- by monster count ---");
            foreach (var g in rows.GroupBy(r => r[3]).OrderBy(g => g.Key))
            {
                var ds = g.Where(r => r[6] != "").Select(r => double.Parse(r[6], Inv)).ToList();
                var ar = g.Where(r => r[10] != "").Select(r => double.Parse(r[10], Inv)).ToList();
                Console.WriteLine($"mon={g.Key,-3} n={g.Count(),-4} " +
                                  $"dist mean={(ds.Any() ? ds.Average() : 0):F1} " +
                                  $"arc half={(ar.Any() ? ar.Average() : 0):F1}");
            }
            return 0;
        }
/// <summary>Extract + derive metrics for one battle. Returns a CSV row (empty slots for missing data).</summary>
        private static string[] Analyze(string id, BattleArenaAnchors_File anchors)
        {
            var area = anchors.Areas.FirstOrDefault();
            if (area == null)
                return new[] { id, "0", "0", "0", "", "", "", "", "", "", "", "", "", "" };

            var party = area[BattleArena_AnchorRole.PartyFront]?.Anchors ?? new List<BattleArena_Anchor>();
            var mon = area[BattleArena_AnchorRole.MonsterLive]?.Anchors ?? new List<BattleArena_Anchor>();
            var cam = area[BattleArena_AnchorRole.Camera]?.Anchors.FirstOrDefault();

            float pcx = party.Count > 0 ? party.Average(t => t.X) : 0f;
            float pcz = party.Count > 0 ? party.Average(t => t.Z) : 0f;

            // party->monster mean distance (planar)
            double distMean = mon.Count > 0
                ? mon.Average(m => Math.Sqrt(Math.Pow(m.X - pcx, 2) + Math.Pow(m.Z - pcz, 2)))
                : double.NaN;

            // wrap arc: for each monster the angle around the party; the arc = the max angle span.
            double arc = double.NaN;
            if (mon.Count >= 2)
            {
                var angles = mon.Select(m => Math.Atan2(m.Z - pcz, m.X - pcx) * 180.0 / Math.PI).ToList();
                double maxA = angles.Max(), minA = angles.Min();
                double span = maxA - minA;
                if (span > 180.0) span = 360.0 - span;   // wrap around
                arc = span / 2.0;                          // half-arc (like the auto-builder arcHalf)
            }

            // lateral spread = mean distance of monsters from the party->forward axis
            double spread = double.NaN;
            if (mon.Count > 0 && party.Count > 0)
            {
                double fx = pcx - (cam?.X ?? pcx), fz = pcz - (cam?.Z ?? pcz);
                double fl = Math.Sqrt(fx * fx + fz * fz);
                if (fl < 1e-3) { fx = 0; fz = -1; fl = 1; }
                fx /= fl; fz /= fl;
                double rx = fz, rz = -fx;
                var lats = mon.Select(m => Math.Abs((m.X - pcx) * rx + (m.Z - pcz) * rz)).ToList();
                spread = lats.Average();
            }

            string N(double v) => double.IsNaN(v) ? "" : v.ToString("0.##", Inv);
            return new[]
            {
                id,
                anchors.AreaCount.ToString(Inv),
                area.PartyCount.ToString(Inv),
                area.MonsterCount.ToString(Inv),
                pcx.ToString("0.##", Inv),
                pcz.ToString("0.##", Inv),
                N(distMean),
                cam?.X.ToString("0.##", Inv) ?? "",
                cam?.Y.ToString("0.##", Inv) ?? "",
                cam?.Z.ToString("0.##", Inv) ?? "",
                N(arc),
                N(spread),
                mon.Count > 0 ? mon.Min(m => m.Y).ToString("0.##", Inv) : "",
                mon.Count > 0 ? mon.Max(m => m.Y).ToString("0.##", Inv) : "",
            };
        }
/// <summary>
        /// Camera cross-scan (2026-08-04): for every battle, read the chunk0 ATEL camReq (SHOT/TARGET)
        /// and the chunk3 layout (party/monsters), then export a CSV that pairs the camera choice with
        /// the layout. This answers "how is the camera normally chosen" (which SHOT/TARGET for which
        /// monster arrangement).
        /// </summary>
        private static int CameraCrossScan()
        {
            string[] roots =
            {
                @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\jppc\battle\btl",
                @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl",
            };
            var files = new List<(string id, string path)>();
            foreach (var root in roots)
                if (Directory.Exists(root))
                    foreach (var p in Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories))
                    {
                        string id = Path.GetFileNameWithoutExtension(p);
                        if (!files.Any(f => f.id == id)) files.Add((id, p));
                    }

            string outCsv = Path.Combine(AppContext.BaseDirectory, "camera_layout_cross.csv");
            int camOk = 0, layoutOk = 0, both = 0;
            using (var w = new StreamWriter(outCsv, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("id,shot,target,camReqCount,monCount,mon_dist,arc_deg,spread");
                foreach (var (id, path) in files.OrderBy(f => f.id))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string shot = "", target = "", reqCount = "0";
                    try
                    {
                        var cam = BattleCameraScript_File.ReadFromBattleBin(id, bytes);
                        if (cam.ShotCount > 0 && cam.Shots[0].Shot.Editable && cam.Shots[0].Target.Editable)
                        {
                            shot = cam.Shots[0].Shot.Value?.ToString(Inv) ?? "";
                            int tv = cam.Shots[0].Target.Value ?? -1;
                            target = tv == -1 ? "none" : tv.ToString(Inv);
                            reqCount = cam.ShotCount.ToString(Inv);
                            camOk++;
                        }
                    }
                    catch { }

                    string mon = "", dist = "", arc = "", spread = "";
                    try
                    {
                        var a = BattleArenaAnchors_File.ReadFromBattleBin(id, bytes);
                        var area = a.Areas.FirstOrDefault();
                        if (area != null)
                        {
                            var party = area[BattleArena_AnchorRole.PartyFront]?.Anchors ?? new List<BattleArena_Anchor>();
                            var monL = area[BattleArena_AnchorRole.MonsterLive]?.Anchors ?? new List<BattleArena_Anchor>();
                            float pcx = party.Count > 0 ? party.Average(t => t.X) : 0f;
                            float pcz = party.Count > 0 ? party.Average(t => t.Z) : 0f;
                            mon = monL.Count.ToString(Inv);
                            if (monL.Count > 0)
                            {
                                dist = monL.Average(m => Math.Sqrt(Math.Pow(m.X - pcx, 2) + Math.Pow(m.Z - pcz, 2))).ToString("0.##", Inv);
                                var angs = monL.Select(m => Math.Atan2(m.Z - pcz, m.X - pcx) * 180.0 / Math.PI).ToList();
                                if (angs.Count >= 2)
                                {
                                    double sp = angs.Max() - angs.Min();
                                    if (sp > 180) sp = 360 - sp;
                                    arc = (sp / 2).ToString("0.##", Inv);
                                }
                            }
                            layoutOk++;
                        }
                    }
                    catch { }

                    if (shot != "" || mon != "") both++;
                    w.WriteLine($"{id},{shot},{target},{reqCount},{mon},{dist},{arc},{spread}");
                }
            }
            Console.WriteLine($"camera cross-scan: files={files.Count} camReq={camOk} layout={layoutOk} both={both}");
            Console.WriteLine($"csv written      : {outCsv}");
            return 0;
        }
    }
}
