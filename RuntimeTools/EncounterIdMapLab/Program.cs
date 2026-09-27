// EncounterIdMapLab — read-only data investigation. SPIRA FORGE.
//
// Question: does `EncounterTable_Formation.BattleId` (= $"{map}_{formationId:00}", e.g. "bsil_03") map 1:1 to a
// btl_* folder name on disk (e.g. "azit03_00")? This decides whether a Field-Hub "click an encounter -> edit its
// formation" handoff can resolve a battle file directly, or needs a translation table.
//
// Usage: EncounterIdMapLab [masterRoot] [--json out.json]

using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;

static string DefaultMaster() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";

string master = DefaultMaster();
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (!args[i].StartsWith("--")) master = args[i];
}

string btlRoot = Path.Combine(master, "jppc", "battle", "btl");
string btlBin = Path.Combine(master, "jppc", "battle", "kernel", "btl.bin");

if (!Directory.Exists(btlRoot)) { Console.Error.WriteLine($"btl root not found: {btlRoot}"); return 2; }
if (!File.Exists(btlBin)) { Console.Error.WriteLine($"btl.bin not found: {btlBin}"); return 2; }

// filesystem ids: <btl>/<id>/<id>.bin
var folderIds = Directory.EnumerateDirectories(btlRoot)
    .Select(d => Path.GetFileName(d)!)
    .Where(id => !string.IsNullOrEmpty(id) && File.Exists(Path.Combine(btlRoot, id, id + ".bin")))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

// encounter BattleIds
EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(btlBin));
var lookup = enc.BuildReferenceLookup();
var encIds = lookup.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

int matched = encIds.Count(id => folderIds.Contains(id));
var missing = encIds.Where(id => !folderIds.Contains(id)).ToList();
var unreferenced = folderIds.Where(id => !lookup.ContainsKey(id))
    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

Console.WriteLine($"EncounterIdMapLab @ {master}");
Console.WriteLine($"  btl_* folders (with <id>.bin) : {folderIds.Count}");
Console.WriteLine($"  encounter BattleIds (distinct): {encIds.Count}");
Console.WriteLine($"  BattleId == folder (matched)  : {matched}/{encIds.Count}");
Console.WriteLine($"  BattleId with NO folder       : {missing.Count}");
Console.WriteLine($"  folders never referenced      : {unreferenced.Count}");

// sample tuples: (Map, FormationId, BattleId) and whether the folder exists
Console.WriteLine("  sample encounter refs (map / formationId / BattleId / folder?):");
foreach (var id in encIds.Take(12))
{
    var r = lookup[id][0];
    Console.WriteLine($"    {r.Map,-8} fid={r.FormationId,3}  BattleId={r.BattleId,-12} folder={(folderIds.Contains(id) ? "YES" : "no ")}");
}
if (missing.Count > 0)
{
    Console.WriteLine($"  sample BattleIds with NO folder: {string.Join(", ", missing.Take(20))}");
}
if (unreferenced.Count > 0)
{
    Console.WriteLine($"  sample folders never referenced: {string.Join(", ", unreferenced.Take(20))}");
}

// verdict
string verdict;
if (encIds.Count > 0 && matched == encIds.Count)
    verdict = "DIRECT — every EncounterTable BattleId is a btl_* folder; handoff can GetPathBattle(BattleId) directly.";
else if (matched == 0)
    verdict = "DISJOINT — no BattleId matches a folder name; the schemes differ, a translation table is required.";
else
    verdict = $"PARTIAL — {matched}/{encIds.Count} BattleIds map directly; the rest need a resolver. See samples.";
Console.WriteLine($"VERDICT: {verdict}");

if (jsonOut != null)
{
    var report = new
    {
        master, folderCount = folderIds.Count, encIdCount = encIds.Count, matched,
        missingCount = missing.Count, unreferencedCount = unreferenced.Count,
        missingSample = missing.Take(50).ToArray(),
        unreferencedSample = unreferenced.Take(50).ToArray(),
        verdict
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return 0;
