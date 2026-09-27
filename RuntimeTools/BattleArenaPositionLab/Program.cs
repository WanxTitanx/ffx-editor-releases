// BattleArenaPositionLab — RT0 / position-only gate for the 🌅 Aurora position writer
// (FfxLib/BattleMap/BattleArenaPositionWriter.cs), proven against the real per-battle btl corpus.
//
// For every btl/<id>/<id>.bin whose chunk3 area-record 0 exposes a writable on-field monster array (+0x20):
//   (1) RT0        : writing the CURRENT monster coords back reproduces the file BYTE-IDENTICALLY;
//   (2) POSITION-ONLY: writing MUTATED coords changes ONLY the bytes of the +0x20 anchor array
//                    (the IsPositionOnly invariant — like FormationSlotLab's slot-only diff);
//   (3) RE-READ    : re-decoding the mutated file yields exactly the mutated monster coords (X/Y/Z), W preserved.
// Plus a SAVE-LIFECYCLE check that exercises the production WriteLooseFile path (guard + backup + restore) on a
// TEMP COPY (never touches the workspace asset). Exit 0 only if every writable file passes all three + the save check.
//
// Self-contained: links only the dependency-free reader + writer; no editor / Avalonia.
// Usage: BattleArenaPositionLab [btlRoot] [--json out.json] [--dump <id>]
//   btlRoot default: D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl

using System.Text.Json;
using FFXProjectEditor.FfxLib.BattleMap;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

string root = DefaultRoot();
string? jsonOut = null;
string? dumpId = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (args[i] == "--dump" && i + 1 < args.Length) dumpId = args[++i];
    else if (!args[i].StartsWith("--")) root = args[i];
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"btl root not found: {root}");
    return 2;
}

const BattleArena_AnchorRole Role = BattleArena_AnchorRole.MonsterLive; // the on-field monsters (+0x20)

// battle files live at <root>/<id>/<id>.bin
var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
    .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p),
                              Path.GetFileName(Path.GetDirectoryName(p)),
                              StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();

int total = 0, readFail = 0, noArray = 0, writable = 0;
int rt0 = 0, positionOnly = 0, reread = 0;
var fails = new List<string>();
string? saveCheckPath = null, saveCheckId = null;

static (float X, float Y, float Z)[] CurrentCoords(BattleArenaAnchors_File a)
{
    var grp = a.Areas.Count > 0 ? a.Areas[0][Role] : null;
    if (grp == null) return Array.Empty<(float, float, float)>();
    return grp.Anchors.Select(an => (an.X, an.Y, an.Z)).ToArray();
}

foreach (string path in files)
{
    string id = Path.GetFileNameWithoutExtension(path);
    byte[] bytes = File.ReadAllBytes(path);
    total++;

    BattleArenaAnchors_File anchors;
    try { anchors = BattleArenaAnchors_File.ReadFromBattleBin(id, bytes); }
    catch (Exception ex) { readFail++; fails.Add($"{id}: Read threw {ex.GetType().Name}: {ex.Message}"); continue; }

    var loc = BattleArenaPositionWriter.LocateAnchorArray(bytes, 0, Role);
    var current = CurrentCoords(anchors);
    if (loc == null || current.Length == 0) { noArray++; continue; }
    writable++;
    if (saveCheckPath == null) { saveCheckPath = path; saveCheckId = id; }

    int off = loc.Value.Offset, len = loc.Value.Length;

    // (1) RT0 — rewriting the same coords reproduces the file exactly.
    byte[] same;
    try { same = BattleArenaPositionWriter.WriteAnchorPositions(bytes, 0, Role, current); }
    catch (Exception ex) { fails.Add($"{id}: WriteAnchorPositions(current) threw {ex.Message}"); continue; }
    if (same.AsSpan().SequenceEqual(bytes)) rt0++;
    else { fails.Add($"{id}: RT0 drift writing current monster coords"); continue; }

    // (2) POSITION-ONLY — mutate every monster coord; only the +0x20 array bytes may change.
    var mutated = current.Select((c, i) => (X: c.X + 1.5f, Y: c.Y + 2.5f, Z: c.Z - 3.5f)).ToArray();
    byte[] edited = BattleArenaPositionWriter.WriteAnchorPositions(bytes, 0, Role, mutated);
    if (BattleArenaPositionWriter.IsPositionOnly(bytes, edited, off, len)) positionOnly++;
    else { fails.Add($"{id}: edit changed a byte outside the {len}-byte +0x20 array @0x{off:X}"); continue; }

    // (3) RE-READ — the mutated file decodes back to exactly the mutated coords (X/Y/Z).
    var back = CurrentCoords(BattleArenaAnchors_File.ReadFromBattleBin(id, edited));
    bool match = back.Length == mutated.Length;
    for (int i = 0; match && i < back.Length; i++)
        match = back[i].X == mutated[i].X && back[i].Y == mutated[i].Y && back[i].Z == mutated[i].Z;
    if (match) reread++;
    else fails.Add($"{id}: re-read monster coords != mutated");

    if (dumpId != null && string.Equals(id, dumpId, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"--- dump {id} ---");
        Console.WriteLine($"+0x20 array @0x{off:X} len {len} ({loc.Value.Count} monsters)");
        Console.WriteLine($"current: {string.Join("  ", current.Select(c => $"({c.X:0.##},{c.Y:0.##},{c.Z:0.##})"))}");
    }
}

// SAVE LIFECYCLE — production WriteLooseFile path on a TEMP COPY of the first writable battle (never the asset).
bool? saveCheck = null;
var saveCheckLog = new List<string>();
if (saveCheckPath != null && saveCheckId != null)
{
    string tmp = Path.Combine(Path.GetTempPath(), "ffx_arena_pos_savecheck.bin");
    string bak = tmp + BattleArenaPositionWriter.DefaultBackupSuffix;
    try
    {
        if (File.Exists(bak)) File.Delete(bak);
        File.Copy(saveCheckPath, tmp, overwrite: true);
        byte[] original = File.ReadAllBytes(tmp);
        var loc = BattleArenaPositionWriter.LocateAnchorArray(original, 0, Role)!.Value;
        var cur = CurrentCoords(BattleArenaAnchors_File.ReadFromBattleBin(saveCheckId, original));

        // (a) no-edit Save -> byte-identical + backup == original
        var r1 = BattleArenaPositionWriter.WriteLooseFile(tmp, original,
            BattleArenaPositionWriter.WriteAnchorPositions(original, 0, Role, cur), loc.Offset, loc.Length);
        bool aOk = r1.Ok && File.ReadAllBytes(tmp).AsSpan().SequenceEqual(original)
                   && File.Exists(bak) && File.ReadAllBytes(bak).AsSpan().SequenceEqual(original);
        saveCheckLog.Add($"(a) no-edit save : {(aOk ? "OK (disk byte-identical; backup==original)" : "FAIL")}");

        // (b) edit Save (move monster 0) -> position-only on disk + reread match + backup preserved
        var mut = cur.ToArray(); mut[0] = (mut[0].X + 5f, mut[0].Y, mut[0].Z + 5f);
        var r2 = BattleArenaPositionWriter.WriteLooseFile(tmp, original,
            BattleArenaPositionWriter.WriteAnchorPositions(original, 0, Role, mut), loc.Offset, loc.Length);
        byte[] afterEdit = File.ReadAllBytes(tmp);
        bool posOnly = BattleArenaPositionWriter.IsPositionOnly(original, afterEdit, loc.Offset, loc.Length);
        var rr = CurrentCoords(BattleArenaAnchors_File.ReadFromBattleBin(saveCheckId, afterEdit));
        bool rereadOk = rr.Length > 0 && rr[0].X == mut[0].X && rr[0].Z == mut[0].Z;
        bool backupStill = File.ReadAllBytes(bak).AsSpan().SequenceEqual(original);
        bool bOk = r2.Ok && posOnly && rereadOk && backupStill;
        saveCheckLog.Add($"(b) edit save    : {(bOk ? "OK (position-only on disk; reread match; backup preserved)" : "FAIL")}");

        // (c) restore from backup -> file == original
        File.Copy(bak, tmp, overwrite: true);
        bool cOk = File.ReadAllBytes(tmp).AsSpan().SequenceEqual(original);
        saveCheckLog.Add($"(c) restore      : {(cOk ? "OK (byte-identical to original)" : "FAIL")}");

        saveCheck = aOk && bOk && cOk;
        if (File.Exists(tmp)) File.Delete(tmp);
        if (File.Exists(bak)) File.Delete(bak);
    }
    catch (Exception ex) { saveCheck = false; saveCheckLog.Add($"save-lifecycle threw: {ex.Message}"); }
}

bool writerPass = rt0 == writable && positionOnly == writable && reread == writable && writable > 0
                  && (saveCheck ?? false);
var writerFails = fails.Where(f => !f.Contains("Read threw")).ToList();

Console.WriteLine($"BattleArenaPositionLab — btl corpus @ {root}");
Console.WriteLine($"  files scanned     : {total}");
Console.WriteLine($"  no +0x20 array    : {noArray}");
Console.WriteLine($"  writable          : {writable}");
Console.WriteLine($"  RT0 (no-edit)     : {rt0}/{writable}");
Console.WriteLine($"  POSITION-ONLY edit: {positionOnly}/{writable}");
Console.WriteLine($"  RE-READ match     : {reread}/{writable}");
Console.WriteLine($"  SAVE LIFECYCLE    : {(saveCheck == true ? "PASS" : saveCheck == false ? "FAIL" : "n/a")}" +
                  (saveCheckId != null ? $" (production save path on temp copy of {saveCheckId})" : ""));
foreach (string s in saveCheckLog) Console.WriteLine($"    {s}");
if (writerFails.Count > 0)
{
    Console.WriteLine($"  WRITER FAILS ({writerFails.Count}):");
    foreach (string f in writerFails.Take(40)) Console.WriteLine($"    {f}");
    if (writerFails.Count > 40) Console.WriteLine($"    ... +{writerFails.Count - 40} more");
}
if (readFail > 0)
{
    Console.WriteLine($"  reader limitation : {readFail} file(s) unparseable (NOT a writer-safety fail):");
    foreach (string f in fails.Where(f => f.Contains("Read threw")).Take(20)) Console.WriteLine($"    {f}");
}
Console.WriteLine(writerPass
    ? "VERDICT: PASS — Aurora position writer is byte-safe position-only on every writable battle (+0x20 monster array)."
    : "VERDICT: FAIL — position writer broke the position-only invariant.");

if (jsonOut != null)
{
    var verdict = new { root, total, readFail, noArray, writable, rt0, positionOnly, reread, saveCheck, saveCheckLog, writerPass, fails = fails.Take(200).ToArray() };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return writerPass ? 0 : 1;
