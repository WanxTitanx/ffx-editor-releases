// BiancaCatalogLab — RT0 / read-only gate for 🌙 BIANCA (the Aurora Chamber layer-1 foundation).
//
// Proves, on the real HD btlmap corpus + the kernel encounter table, that:
//   (1) CATALOG COMPLETE: BattleMapCatalog_File.Scan captures EVERY scene-shaped folder on disk
//       (independent glob == catalog scene-id set; no scene silently dropped, none invented);
//   (2) ONE PRIMARY MODEL: every scene has exactly one primary mdl/d3d11/<leaf>.dae.phyre present
//       (AllDaeCount == 1, or 2 only when the scene also carries a 2d/ slice);
//   (3) DETERMINISTIC: scanning twice yields the identical catalog (RT0 idempotency of the reader);
//   (4) BRIDGE SOUND: every EncounterTable `map` whose area shipped in HD resolves to a real scene
//       (UNEXPLAINED misses == 0); HD-orphan maps (area not shipped) and unreferenced scenes are
//       logged as EXPECTED, not failures — the HD btlmap set is a known strict subset of the PS2 maps.
// Exit 0 only if (1)-(4) all hold. READ-ONLY: never opens a .phyre byte, never writes/moves/deletes anything.
//
// Self-contained: links only the dependency-free production reader/resolver + EncounterTable_File.
// Usage: BiancaCatalogLab [btlmapRoot] [--encounter <btl.bin>] [--json out.json] [--dump <mapKey>]
//   btlmapRoot default: D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\btlmap
//   --encounter default: D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\kernel\btl.bin

using System.Text.Json;
using System.Text.RegularExpressions;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

string btlmapRoot = BattleMapCatalog_File.DefaultBtlmapRoot;
string encounterPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\kernel\btl.bin";
string? jsonOut = null;
string? dumpMap = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--encounter" && i + 1 < args.Length) encounterPath = args[++i];
    else if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (args[i] == "--dump" && i + 1 < args.Length) dumpMap = args[++i];
    else if (!args[i].StartsWith("--")) btlmapRoot = args[i];
}

if (!Directory.Exists(btlmapRoot))
{
    Console.Error.WriteLine($"btlmap root not found: {btlmapRoot}");
    return 2;
}

var fails = new List<string>();
var sceneLeaf = new Regex(@"^[a-z]{4}\d{2}_[a-z0-9]+$", RegexOptions.Compiled);

// --- Scan the catalog (the production reader) ---
BattleMapCatalog_File catalog;
try { catalog = BattleMapCatalog_File.Scan(btlmapRoot); }
catch (Exception ex) { Console.Error.WriteLine($"Scan threw: {ex}"); return 2; }

// (1) CATALOG COMPLETE — independent disk glob of scene-shaped folders == catalog scene-id set.
var diskScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (string areaDir in Directory.EnumerateDirectories(btlmapRoot))
    foreach (string sceneDir in Directory.EnumerateDirectories(areaDir))
    {
        string leaf = Path.GetFileName(sceneDir);
        if (!sceneLeaf.IsMatch(leaf)) continue;
        // count only folders that actually carry the primary model (a scene with no dae is not catalogable)
        string primary = Path.Combine(sceneDir, "mdl", "d3d11", leaf + ".dae.phyre");
        if (File.Exists(primary)) diskScenes.Add(leaf);
    }
var catalogScenes = new HashSet<string>(catalog.Scenes.Select(s => s.SceneId), StringComparer.OrdinalIgnoreCase);
var droppedByCatalog = diskScenes.Except(catalogScenes).OrderBy(s => s).ToList();
var inventedByCatalog = catalogScenes.Except(diskScenes).OrderBy(s => s).ToList();
bool catalogComplete = droppedByCatalog.Count == 0 && inventedByCatalog.Count == 0;
if (!catalogComplete)
{
    if (droppedByCatalog.Count > 0) fails.Add($"catalog DROPPED {droppedByCatalog.Count} disk scene(s): {string.Join(",", droppedByCatalog.Take(20))}");
    if (inventedByCatalog.Count > 0) fails.Add($"catalog INVENTED {inventedByCatalog.Count} scene(s) not on disk: {string.Join(",", inventedByCatalog.Take(20))}");
}

// (2) ONE PRIMARY MODEL — every scene has its primary dae, and AllDaeCount matches (1, or 2 with a 2d slice).
int primaryOk = 0, daeCountOk = 0;
foreach (var s in catalog.Scenes)
{
    if (s.HasPrimaryDae && s.PrimaryDaeSize > 0) primaryOk++;
    else fails.Add($"{s.SceneId}: primary dae missing/zero ({s.PrimaryDaeRelPath})");

    int expected = s.HasTwoDSlice ? 2 : 1;
    if (s.AllDaeCount == expected) daeCountOk++;
    else fails.Add($"{s.SceneId}: AllDaeCount={s.AllDaeCount}, expected {expected} (HasTwoDSlice={s.HasTwoDSlice})");

    // leaf integrity: AreaCode prefixes MapKey, MapKey == AreaCode+index, scene under its area.
    if (!s.MapKey.StartsWith(s.AreaCode, StringComparison.OrdinalIgnoreCase) || !s.SceneId.StartsWith(s.MapKey, StringComparison.OrdinalIgnoreCase))
        fails.Add($"{s.SceneId}: leaf integrity broken (area={s.AreaCode}, mapKey={s.MapKey})");
}

// (3) DETERMINISTIC — a second scan yields the identical scene-id ordering.
var catalog2 = BattleMapCatalog_File.Scan(btlmapRoot);
bool deterministic = catalog.Scenes.Select(s => s.SceneId).SequenceEqual(catalog2.Scenes.Select(s => s.SceneId), StringComparer.OrdinalIgnoreCase)
                     && catalog.SceneCount == catalog2.SceneCount && catalog.AreaCount == catalog2.AreaCount;
if (!deterministic) fails.Add("catalog scan is NOT deterministic (two scans differ)");

// (4) BRIDGE SOUND — resolve the encounter table; UNEXPLAINED misses must be 0.
int totalGroups = 0, resolvedGroups = 0, hdOrphanMaps = 0;
int distinctMaps = 0, mapsWithHdArea = 0, mapsResolved = 0, unexplained = 0;
var hdOrphanSamples = new List<string>();
var unexplainedSamples = new List<string>();
List<string> orphanScenes = new();
bool encounterOk = File.Exists(encounterPath);
if (!encounterOk)
{
    fails.Add($"encounter table not found: {encounterPath} (bridge invariant skipped)");
}
else
{
    EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
    var resolver = new BattlefieldSceneResolver(catalog);

    // the set of area codes that shipped in HD (the catalog's areas)
    var hdAreas = new HashSet<string>(catalog.Areas.Select(a => a.AreaCode), StringComparer.OrdinalIgnoreCase);

    var matches = resolver.ResolveAll(enc);
    totalGroups = matches.Count;
    resolvedGroups = matches.Count(m => m.Resolved);

    // per distinct map: classify miss as EXPECTED (area not shipped / NN not shipped) vs UNEXPLAINED.
    foreach (var grp in enc.Tables.GroupBy(t => t.Map, StringComparer.OrdinalIgnoreCase))
    {
        distinctMaps++;
        string mapKey = grp.Key ?? "";
        string area = mapKey.Length >= 4 ? mapKey.Substring(0, 4) : mapKey;
        bool areaShipped = hdAreas.Contains(area);
        bool resolved = resolver.ResolvePrimaryScene(mapKey) != null;

        if (resolved) { mapsResolved++; if (areaShipped) mapsWithHdArea++; continue; }
        if (!areaShipped) { hdOrphanMaps++; if (hdOrphanSamples.Count < 30) hdOrphanSamples.Add(mapKey); continue; }

        mapsWithHdArea++;
        // area shipped but this map didn't resolve. EXPECTED iff the specific scene folder truly is not on disk.
        bool folderOnDisk = diskScenes.Any(sc => sc.StartsWith(mapKey + "_", StringComparison.OrdinalIgnoreCase));
        if (folderOnDisk) { unexplained++; if (unexplainedSamples.Count < 30) unexplainedSamples.Add(mapKey); }
        // else: NN simply not shipped in HD -> expected subset miss, not a failure.
    }
    if (unexplained > 0) fails.Add($"BRIDGE: {unexplained} map(s) have a scene folder on disk but failed to resolve: {string.Join(",", unexplainedSamples)}");

    orphanScenes = resolver.OrphanScenes(enc).Select(s => s.SceneId).ToList();

    if (dumpMap != null)
    {
        Console.WriteLine($"--- dump {dumpMap} ---");
        foreach (var s in resolver.ResolveScenes(dumpMap)) Console.WriteLine($"  scene {s}");
        var prim = resolver.ResolvePrimaryScene(dumpMap);
        Console.WriteLine($"  primary: {(prim?.SceneId ?? "<none>")}");
    }
}

bool pass = catalogComplete
            && primaryOk == catalog.SceneCount
            && daeCountOk == catalog.SceneCount
            && deterministic
            && encounterOk
            && unexplained == 0
            && catalog.SceneCount > 0;

// --- report ---
Console.WriteLine($"BiancaCatalogLab — btlmap @ {btlmapRoot}");
Console.WriteLine($"  areas             : {catalog.AreaCount}");
Console.WriteLine($"  scenes            : {catalog.SceneCount}  (total primary {catalog.TotalPrimaryBytes:N0} bytes)");
Console.WriteLine($"  catalog complete  : {(catalogComplete ? "OK" : "FAIL")} (disk {diskScenes.Count} == catalog {catalogScenes.Count})");
Console.WriteLine($"  one primary model : {primaryOk}/{catalog.SceneCount} primary present, {daeCountOk}/{catalog.SceneCount} dae-count exact");
Console.WriteLine($"  deterministic     : {(deterministic ? "OK" : "FAIL")}");
Console.WriteLine($"  stub scenes       : {catalog.Scenes.Count(s => s.IsStub)} ({string.Join(",", catalog.Scenes.Where(s => s.IsStub).Select(s => s.SceneId))})");
Console.WriteLine($"  2d-slice scenes   : {catalog.Scenes.Count(s => s.HasTwoDSlice)} ({string.Join(",", catalog.Scenes.Where(s => s.HasTwoDSlice).Select(s => s.SceneId))})");
if (encounterOk)
{
    Console.WriteLine($"  encounter table   : {encounterPath}");
    Console.WriteLine($"  groups resolved   : {resolvedGroups}/{totalGroups}");
    Console.WriteLine($"  distinct maps     : {distinctMaps}  ({mapsResolved} resolved to an HD scene)");
    Console.WriteLine($"  HD-orphan maps    : {hdOrphanMaps} (PS2-only/cut/system maps, EXPECTED) e.g. {string.Join(",", hdOrphanSamples.Take(12))}");
    Console.WriteLine($"  UNEXPLAINED misses: {unexplained}  (folder on disk but resolver failed — MUST be 0)");
    Console.WriteLine($"  orphan scenes     : {orphanScenes.Count} (HD scenes with no encounter map, EXPECTED) {string.Join(",", orphanScenes)}");
}
if (fails.Count > 0)
{
    Console.WriteLine($"  FAILS ({fails.Count}):");
    foreach (var f in fails.Take(40)) Console.WriteLine($"    {f}");
    if (fails.Count > 40) Console.WriteLine($"    ... +{fails.Count - 40} more");
}
Console.WriteLine(pass
    ? "VERDICT: PASS — BIANCA catalog complete + deterministic, every shipped scene has one model, the map->scene bridge has 0 unexplained misses."
    : "VERDICT: FAIL — see FAILS above.");

if (jsonOut != null)
{
    var verdict = new
    {
        btlmapRoot,
        encounterPath,
        catalog.AreaCount,
        catalog.SceneCount,
        catalog.TotalPrimaryBytes,
        catalogComplete,
        droppedByCatalog,
        inventedByCatalog,
        primaryOk,
        daeCountOk,
        deterministic,
        stubScenes = catalog.Scenes.Where(s => s.IsStub).Select(s => s.SceneId).ToArray(),
        twoDScenes = catalog.Scenes.Where(s => s.HasTwoDSlice).Select(s => s.SceneId).ToArray(),
        encounterOk,
        totalGroups,
        resolvedGroups,
        distinctMaps,
        mapsResolved,
        hdOrphanMaps,
        hdOrphanSamples,
        unexplained,
        unexplainedSamples,
        orphanScenes,
        pass,
        fails = fails.Take(200).ToArray(),
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return pass ? 0 : 1;
