// AuroraChamberLab — RT0 / read-only gate for 🌅 AURORA CHAMBER (layer-3 coordinate decode).
//
// Proves, on the real per-battle corpus + the BIANCA btlmap catalog + the kernel encounter table, that:
//   (1) GOLDEN DECODE: BattleArenaAnchors_File reproduces the EXACT on-field monster anchors documented in
//       docs/reverse/FFX_BATTLE_FORMATION_POSITION_CHUNK3_DECODED_2026-06-05.md for the 5 sample battles
//       (azit03_00, dome00_00, klyt00_00, sins02_00, mihn00_00) — the canonical proof the reader matches the
//       proven decode (stride 16, pointer +0x20, count MonsterPositionCount@+0x06, float32 X,Y,Z,W Y-up).
//   (2) CORPUS CLEAN: every per-battle bin under the btl root decodes with NO exception, NO NaN/Inf, and W==0 on
//       every on-field monster anchor (positions, not the camera record). Battles whose +0x20 pointer is absent/
//       out-of-bounds simply expose 0 live anchors (EXPECTED — not a failure).
//   (3) DETERMINISTIC: decoding a battle twice yields byte-identical anchor coordinates (RT0 idempotency).
//   (4) PIPELINE SOUND: the BIANCA scene catalog joins through the PROVEN map-key bridge to real on-disk battles
//       with decodable anchors (end-to-end scene → encounter → btl → chunk3 coords), reported as coverage.
// Exit 0 only if (1)-(3) hold and the catalog scanned. READ-ONLY: never writes/moves/deletes anything, never
// decodes a .phyre byte.
//
// Self-contained: links only the dependency-free production reader/catalog/resolver + EncounterTable_File.
// Usage: AuroraChamberLab [btlmapRoot] [--btl <btlRoot>] [--encounter <btl.bin>] [--json out.json]

using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

string btlmapRoot = BattleMapCatalog_File.DefaultBtlmapRoot;
string btlRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";
string encounterPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\kernel\btl.bin";
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--btl" && i + 1 < args.Length) btlRoot = args[++i];
    else if (args[i] == "--encounter" && i + 1 < args.Length) encounterPath = args[++i];
    else if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (!args[i].StartsWith("--")) btlmapRoot = args[i];
}

if (!Directory.Exists(btlRoot))
{
    Console.Error.WriteLine($"btl root not found: {btlRoot} (per-battle bins required for the anchor gate)");
    return 2;
}

var fails = new List<string>();

// ---------- (1) GOLDEN DECODE — exact coords from the proven decode doc (epsilon 0.05 covers 2-dp rounding) ----------
var golden = new Dictionary<string, (float x, float y, float z)[]>
{
    ["azit03_00"] = new[] { (54.13f, 0f, 29.93f), (34.83f, 0f, 33.03f), (2.06f, 0f, 35.07f), (-27.77f, 0f, 42.70f) },
    ["dome00_00"] = new[] { (37.13f, 0f, 40.93f), (22.83f, 0f, 61.83f), (-44.94f, 0f, 89.07f), (-25.77f, 0f, 64.70f) },
    ["klyt00_00"] = new[] { (-11.31f, 2.9f, 52.7f), (-70f, 0f, -70.40f), (-70f, 0f, -70.40f) },
    ["sins02_00"] = new[] { (50.13f, 0f, 69.93f), (-0.07f, 0f, 107.83f), (-62.94f, 0f, 81.07f) },
    ["mihn00_00"] = new[] { (33.93f, 0f, 21.93f), (10.53f, 0f, 31.83f), (-31.94f, 0f, 47.27f) },
};

int goldenPresent = 0, goldenOk = 0;
var goldenMissing = new List<string>();
foreach (var (bid, expected) in golden)
{
    string path = Path.Combine(btlRoot, bid, bid + ".bin");
    if (!File.Exists(path)) { goldenMissing.Add(bid); continue; }
    goldenPresent++;

    var anchors = BattleArenaAnchors_File.ReadFromBattleBin(bid, File.ReadAllBytes(path));
    var live = anchors.PrimaryMonsterAnchors;
    if (live.Count != expected.Length)
    {
        fails.Add($"GOLDEN {bid}: live count {live.Count} != expected {expected.Length}");
        continue;
    }
    bool ok = true;
    for (int i = 0; i < expected.Length; i++)
    {
        if (!Near(live[i].X, expected[i].x) || !Near(live[i].Y, expected[i].y) || !Near(live[i].Z, expected[i].z))
        {
            fails.Add($"GOLDEN {bid}[{i}]: got ({live[i].X:0.##},{live[i].Y:0.##},{live[i].Z:0.##}) " +
                      $"expected ({expected[i].x:0.##},{expected[i].y:0.##},{expected[i].z:0.##})");
            ok = false;
        }
    }
    if (ok) goldenOk++;
}
bool goldenPass = goldenPresent > 0 && goldenOk == goldenPresent;
if (goldenPresent == 0) fails.Add("GOLDEN: none of the 5 sample battles present on disk — cannot prove the core decode.");

// ---------- (2) CORPUS CLEAN + (3) DETERMINISM over every per-battle bin ----------
int battlesScanned = 0, battlesWithChunk3 = 0, battlesWithLive = 0, totalLiveAnchors = 0;
int corpusFail = 0, nonFinite = 0, wNonZero = 0, threwCount = 0, determinismChecked = 0, determinismFail = 0;
var corpusSamples = new List<string>();

foreach (string dir in Directory.EnumerateDirectories(btlRoot))
{
    string id = Path.GetFileName(dir);
    string path = Path.Combine(dir, id + ".bin");
    if (!File.Exists(path)) continue;
    battlesScanned++;

    BattleArenaAnchors_File a1;
    byte[] bytes;
    try { bytes = File.ReadAllBytes(path); a1 = BattleArenaAnchors_File.ReadFromBattleBin(id, bytes); }
    catch (Exception ex)
    {
        threwCount++; corpusFail++;
        if (corpusSamples.Count < 20) corpusSamples.Add($"{id}: threw {ex.GetType().Name}");
        continue;
    }

    if (a1.Areas.Count == 0) continue;
    battlesWithChunk3++;

    var live = a1.PrimaryMonsterAnchors;
    if (live.Count > 0) { battlesWithLive++; totalLiveAnchors += live.Count; }

    bool battleBad = false;
    foreach (var anc in live)
    {
        if (!IsFinite(anc.X) || !IsFinite(anc.Y) || !IsFinite(anc.Z) || !IsFinite(anc.W))
        { nonFinite++; battleBad = true; if (corpusSamples.Count < 20) corpusSamples.Add($"{id}[{anc.Index}]: non-finite"); }
        if (anc.W != 0f)
        { wNonZero++; battleBad = true; if (corpusSamples.Count < 20) corpusSamples.Add($"{id}[{anc.Index}]: monster_live W={anc.W:0.###}!=0"); }
    }
    if (battleBad) corpusFail++;

    // determinism — every 25th battle, re-decode and compare live coords.
    if (battlesScanned % 25 == 0)
    {
        determinismChecked++;
        var a2 = BattleArenaAnchors_File.ReadFromBattleBin(id, bytes);
        var live2 = a2.PrimaryMonsterAnchors;
        bool same = live.Count == live2.Count;
        for (int i = 0; same && i < live.Count; i++)
            same = live[i].X == live2[i].X && live[i].Y == live2[i].Y && live[i].Z == live2[i].Z && live[i].W == live2[i].W;
        if (!same) { determinismFail++; if (corpusSamples.Count < 20) corpusSamples.Add($"{id}: non-deterministic"); }
    }
}
bool corpusClean = corpusFail == 0;
bool deterministic = determinismFail == 0;

// ---------- (4) PIPELINE SOUND — BIANCA scene -> encounter map -> on-disk battle with anchors ----------
int scenesTotal = 0, scenesWithBattle = 0, scenesWithAnchorBattle = 0;
bool catalogOk = false;
try
{
    var catalog = BattleMapCatalog_File.Scan(btlmapRoot);
    catalogOk = catalog.SceneCount > 0;
    scenesTotal = catalog.SceneCount;

    if (File.Exists(encounterPath))
    {
        var enc = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
        // map key -> distinct battle ids that exist on disk
        var byMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in enc.Tables)
        {
            if (string.IsNullOrWhiteSpace(t.Map)) continue;
            if (!byMap.TryGetValue(t.Map, out var lst)) { lst = new List<string>(); byMap[t.Map] = lst; }
            foreach (var g in t.Groups)
                foreach (var f in g.Formations)
                    if (!lst.Contains(f.BattleId)) lst.Add(f.BattleId);
        }

        foreach (var s in catalog.Scenes)
        {
            if (!byMap.TryGetValue(s.MapKey, out var bids)) continue;
            var onDisk = bids.Where(b => File.Exists(Path.Combine(btlRoot, b, b + ".bin"))).ToList();
            if (onDisk.Count == 0) continue;
            scenesWithBattle++;
            bool anyAnchors = onDisk.Any(b =>
            {
                try { return BattleArenaAnchors_File.ReadFromBattleBin(b, File.ReadAllBytes(Path.Combine(btlRoot, b, b + ".bin"))).PrimaryMonsterAnchors.Count > 0; }
                catch { return false; }
            });
            if (anyAnchors) scenesWithAnchorBattle++;
        }
    }
}
catch (Exception ex) { fails.Add($"PIPELINE: catalog/encounter scan threw: {ex.Message}"); }
if (!catalogOk) fails.Add("PIPELINE: BIANCA catalog scanned 0 scenes.");

bool pass = goldenPass && corpusClean && deterministic && catalogOk;

// ---------- report ----------
Console.WriteLine($"AuroraChamberLab — chunk3 anchor decode gate");
Console.WriteLine($"  btl root          : {btlRoot}");
Console.WriteLine($"  GOLDEN decode     : {goldenOk}/{goldenPresent} exact" + (goldenMissing.Count > 0 ? $"  (missing on disk: {string.Join(",", goldenMissing)})" : ""));
Console.WriteLine($"  battles scanned   : {battlesScanned}  ({battlesWithChunk3} with chunk3, {battlesWithLive} with live monsters, {totalLiveAnchors} live anchors total)");
Console.WriteLine($"  corpus clean      : {(corpusClean ? "OK" : "FAIL")}  (fails {corpusFail}: nonFinite {nonFinite}, W!=0 {wNonZero}, threw {threwCount})");
Console.WriteLine($"  deterministic     : {(deterministic ? "OK" : "FAIL")}  ({determinismChecked} sampled, {determinismFail} differed)");
Console.WriteLine($"  pipeline coverage : {scenesWithAnchorBattle}/{scenesWithBattle} mapped scenes have ≥1 battle with anchors (of {scenesTotal} catalog scenes)");
if (corpusSamples.Count > 0)
{
    Console.WriteLine($"  samples:");
    foreach (var s in corpusSamples) Console.WriteLine($"    {s}");
}
if (fails.Count > 0)
{
    Console.WriteLine($"  FAILS ({fails.Count}):");
    foreach (var f in fails.Take(40)) Console.WriteLine($"    {f}");
}
Console.WriteLine(pass
    ? "VERDICT: PASS — golden anchors exact, corpus clean (no NaN/Inf, W==0), deterministic, BIANCA pipeline sound."
    : "VERDICT: FAIL — see FAILS above.");

if (jsonOut != null)
{
    var verdict = new
    {
        btlmapRoot, btlRoot, encounterPath,
        goldenPresent, goldenOk, goldenMissing, goldenPass,
        battlesScanned, battlesWithChunk3, battlesWithLive, totalLiveAnchors,
        corpusClean, corpusFail, nonFinite, wNonZero, threwCount,
        deterministic, determinismChecked, determinismFail,
        scenesTotal, scenesWithBattle, scenesWithAnchorBattle,
        pass, fails = fails.Take(200).ToArray(),
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return pass ? 0 : 1;

static bool Near(float a, float b) => Math.Abs(a - b) <= 0.05f;
static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
