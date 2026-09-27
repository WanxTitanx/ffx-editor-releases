// BattleArenaGrowLab — RT0 / structural gate for the 🌅 Aurora GROW writer
// (FfxLib/BattleMap/BattleArenaGrowWriter.cs), proven against the real per-battle btl corpus.
//
// For every btl/<id>/<id>.bin whose area-record 0 is grow-eligible (single-area + monLive tight):
//   (1) RT0       : growing to the SAME count (rewrite current coords) reproduces the file BYTE-IDENTICALLY;
//   (2) ADD       : when count < reserve cap, grow by +1 → re-read yields count+1 monLive coords (the appended one
//                   exact), the file re-decodes CLEAN (Notes empty: chunk table + every monster array in bounds),
//                   and the splice is surgical (IsCleanGrow);
//   (3) REMOVE    : when count > 1, shrink by -1 → re-read yields count-1 coords, clean re-decode, surgical.
// Exit 0 only if every grow-eligible file passes RT0 + (ADD where possible) + (REMOVE where possible).
//
// Self-contained: links only the dependency-free reader + writers; no editor / Avalonia.
// Usage: BattleArenaGrowLab [btlRoot] [--json out.json]

using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

string root = DefaultRoot();
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (!args[i].StartsWith("--")) root = args[i];
}
if (!Directory.Exists(root)) { Console.Error.WriteLine($"btl root not found: {root}"); return 2; }

static (float X, float Y, float Z)[] Live(byte[] bin, string id)
{
    var a = BattleArenaAnchors_File.ReadFromBattleBin(id, bin);
    var g = a.Areas.Count > 0 ? a.Areas[0][BattleArena_AnchorRole.MonsterLive] : null;
    return g == null ? Array.Empty<(float, float, float)>() : g.Anchors.Select(an => (an.X, an.Y, an.Z)).ToArray();
}
static bool CleanReread(byte[] bin, string id)
{
    var a = BattleArenaAnchors_File.ReadFromBattleBin(id, bin);
    return a.Notes.Count == 0 && a.Areas.Count >= 1;
}
static ushort[] Slots(byte[] bin)
{
    var f = Battle_File.Read(string.Empty, bin).Formation;
    return f == null ? Array.Empty<ushort>() : f.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();
}

var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
    .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), Path.GetFileName(Path.GetDirectoryName(p)), StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

int total = 0, eligible = 0, rt0 = 0, addTested = 0, addOk = 0, removeTested = 0, removeOk = 0;
int authAddTested = 0, authAddOk = 0, authRemTested = 0, authRemOk = 0;
var fails = new List<string>();

foreach (string path in files)
{
    string id = Path.GetFileNameWithoutExtension(path);
    byte[] bytes = File.ReadAllBytes(path);
    total++;

    BattleArenaGrowWriter.GrowPlan plan;
    try { plan = BattleArenaGrowWriter.Plan(bytes); }
    catch (Exception ex) { fails.Add($"{id}: Plan threw {ex.Message}"); continue; }
    if (!plan.CanGrow) continue; // not grow-eligible (multi-area / non-tight) — benign, skipped
    eligible++;

    var cur = Live(bytes, id);
    if (cur.Length != plan.OldCount) { fails.Add($"{id}: live count {cur.Length} != plan {plan.OldCount}"); continue; }

    // (1) RT0 — grow to the SAME count == byte-identical.
    byte[] same;
    try { same = BattleArenaGrowWriter.GrowMonsters(bytes, cur); }
    catch (Exception ex) { fails.Add($"{id}: RT0 GrowMonsters(current) threw {ex.Message}"); continue; }
    if (same.AsSpan().SequenceEqual(bytes)) rt0++;
    else { fails.Add($"{id}: RT0 drift (no-edit grow not byte-identical)"); continue; }

    // (2) ADD +1 (when reserve allows).
    if (plan.OldCount + 1 <= plan.MaxCount)
    {
        addTested++;
        var add = new List<(float X, float Y, float Z)>(cur) { (11.5f, 0.5f, -22.5f) };
        try
        {
            byte[] grown = BattleArenaGrowWriter.GrowMonsters(bytes, add);
            bool clean = BattleArenaGrowWriter.IsCleanGrow(bytes, grown) && CleanReread(grown, id);
            var back = Live(grown, id);
            bool match = back.Length == add.Count;
            for (int i = 0; match && i < back.Length; i++)
                match = back[i].X == add[i].X && back[i].Y == add[i].Y && back[i].Z == add[i].Z;
            // length must really have grown by one element-row
            bool grewBytes = grown.Length == bytes.Length + 16;
            if (clean && match && grewBytes) addOk++;
            else fails.Add($"{id}: ADD fail (clean={clean} match={match} grewBytes={grewBytes})");
        }
        catch (Exception ex) { fails.Add($"{id}: ADD threw {ex.Message}"); }
    }

    // (3) REMOVE -1 (when count > 1).
    if (plan.OldCount - 1 >= 1)
    {
        removeTested++;
        var rem = cur.Take(plan.OldCount - 1).ToArray();
        try
        {
            byte[] shrunk = BattleArenaGrowWriter.GrowMonsters(bytes, rem);
            bool clean = BattleArenaGrowWriter.IsCleanGrow(bytes, shrunk) && CleanReread(shrunk, id);
            var back = Live(shrunk, id);
            bool match = back.Length == rem.Length;
            for (int i = 0; match && i < back.Length; i++)
                match = back[i].X == rem[i].X && back[i].Y == rem[i].Y && back[i].Z == rem[i].Z;
            bool shrankBytes = shrunk.Length == bytes.Length - 16;
            if (clean && match && shrankBytes) removeOk++;
            else fails.Add($"{id}: REMOVE fail (clean={clean} match={match} shrankBytes={shrankBytes})");
        }
        catch (Exception ex) { fails.Add($"{id}: REMOVE threw {ex.Message}"); }
    }

    // (4) AUTHOR — high-level add/remove keeping chunk2 (formation lineup) + chunk3 (anchors) in lock-step.
    //     Indexed by FORMATION-LIVE (not chunk3 count): the new slot = first empty, cloning the last live slot.
    var slotsB = Slots(bytes);
    int liveBefore = slotsB.Count(s => s != 0xFFFF);
    int lastLive = -1; for (int k = 0; k < slotsB.Length; k++) if (slotsB[k] != 0xFFFF) lastLive = k;
    int firstEmpty = -1; for (int k = 0; k < slotsB.Length; k++) if (slotsB[k] == 0xFFFF) { firstEmpty = k; break; }

    if (BattleArenaAuthor.CanAdd(bytes, out _))
    {
        authAddTested++;
        try
        {
            byte[] added = BattleArenaAuthor.AddMonsterCloneLast(bytes);
            var sa = Slots(added);
            bool slotFilled = firstEmpty >= 0 && sa.Length == 8 && sa[firstEmpty] == slotsB[lastLive] && sa[firstEmpty] != 0xFFFF;
            bool liveUp = sa.Count(s => s != 0xFFFF) == liveBefore + 1;
            bool clean = CleanReread(added, id);
            if (slotFilled && liveUp && clean) authAddOk++;
            else fails.Add($"{id}: AUTHOR add fail (slotFilled={slotFilled} liveUp={liveUp} clean={clean})");
        }
        catch (Exception ex) { fails.Add($"{id}: AUTHOR add threw {ex.Message}"); }
    }
    if (BattleArenaAuthor.CanRemove(bytes, out _))
    {
        authRemTested++;
        try
        {
            byte[] removed = BattleArenaAuthor.RemoveLastMonster(bytes);
            var sa = Slots(removed);
            bool slotCleared = lastLive >= 0 && sa.Length == 8 && sa[lastLive] == 0xFFFF;
            bool liveDown = sa.Count(s => s != 0xFFFF) == liveBefore - 1;
            bool clean = CleanReread(removed, id);
            if (slotCleared && liveDown && clean) authRemOk++;
            else fails.Add($"{id}: AUTHOR remove fail (slotCleared={slotCleared} liveDown={liveDown} clean={clean})");
        }
        catch (Exception ex) { fails.Add($"{id}: AUTHOR remove threw {ex.Message}"); }
    }
}

bool pass = eligible > 0 && rt0 == eligible
            && addOk == addTested && removeOk == removeTested
            && authAddOk == authAddTested && authRemOk == authRemTested
            && fails.Count == 0;

Console.WriteLine($"BattleArenaGrowLab — btl corpus @ {root}");
Console.WriteLine($"  files scanned     : {total}");
Console.WriteLine($"  grow-eligible     : {eligible} (single-area + monLive tight)");
Console.WriteLine($"  RT0 (no-edit grow): {rt0}/{eligible}");
Console.WriteLine($"  ADD +1   round-trip: {addOk}/{addTested}");
Console.WriteLine($"  REMOVE -1 round-trip: {removeOk}/{removeTested}");
Console.WriteLine($"  AUTHOR add (chunk3 grow + chunk2 slot clone): {authAddOk}/{authAddTested}");
Console.WriteLine($"  AUTHOR remove (shrink + slot clear)         : {authRemOk}/{authRemTested}");
if (fails.Count > 0)
{
    Console.WriteLine($"  FAILS ({fails.Count}):");
    foreach (var f in fails.Take(40)) Console.WriteLine($"    {f}");
    if (fails.Count > 40) Console.WriteLine($"    ... +{fails.Count - 40} more");
}
Console.WriteLine(pass
    ? "VERDICT: PASS — Aurora GROW writer is byte-exact (RT0) + add/remove round-trips clean on every grow-eligible battle."
    : "VERDICT: FAIL — grow writer broke an invariant (see FAILS).");

if (jsonOut != null)
{
    var verdict = new { root, total, eligible, rt0, addTested, addOk, removeTested, removeOk, authAddTested, authAddOk, authRemTested, authRemOk, pass, fails = fails.Take(200).ToArray() };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return pass ? 0 : 1;
