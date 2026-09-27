// FormationSlotLab — RT0 / slot-only gate for the FFX formation writer (FfxLib/Battle/Battle_File.cs).
//
// Proves, on the real per-battle corpus, that Battle_File.WriteWithFormationSlots is byte-safe:
//   for each btl/<id>/<id>.bin with a writable formation chunk ->
//     (1) RT0: writing the CURRENT 8 slots reproduces the file byte-identically;
//     (2) SLOT-ONLY: writing 8 MUTATED slots changes ONLY the 16 bytes at FormationSlotsOffset
//         (the AssertSlotOnlyDiff invariant — like Shop's gate);
//     (3) RE-READ: re-parsing the mutated file yields exactly the mutated slots.
// Exit 0 only if every writable file passes all three. SPIRA FORGE v0.2.
//
// Self-contained: links only the dependency-free production reader/writer; no editor / Avalonia.
// Usage: FormationSlotLab [btlRoot] [--json out.json] [--dump <id>]
//   btlRoot default: D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl

using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;

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

// true if every byte that differs between a and b lies within [off, off+len) (and lengths match).
static bool DiffOnlyAt(byte[] a, byte[] b, int off, int len)
{
    if (a.Length != b.Length) return false;
    for (int p = 0; p < a.Length; p++)
        if (a[p] != b[p] && (p < off || p >= off + len)) return false;
    return true;
}

// battle files live at <root>/<id>/<id>.bin
var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
    .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p),
                              Path.GetFileName(Path.GetDirectoryName(p)),
                              StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();

int total = 0, readFail = 0, noFormation = 0, writable = 0;
int rt0 = 0, slotOnly = 0, reread = 0;
var fails = new List<string>();
string? saveCheckPath = null, saveCheckId = null; // first writable battle — used by the save-lifecycle check

foreach (string path in files)
{
    string id = Path.GetFileNameWithoutExtension(path);
    byte[] bytes = File.ReadAllBytes(path);
    total++;

    Battle_File bf;
    try { bf = Battle_File.Read(id, bytes); }
    catch (Exception ex) { readFail++; fails.Add($"{id}: Read threw {ex.GetType().Name}: {ex.Message}"); continue; }

    if (!bf.CanWriteFormation) { noFormation++; continue; }
    writable++;
    if (saveCheckPath == null) { saveCheckPath = path; saveCheckId = id; }

    ushort[] current = bf.Formation!.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();

    // (1) RT0 — rewriting the same slots reproduces the file exactly.
    byte[] same = bf.WriteWithFormationSlots(current);
    if (same.AsSpan().SequenceEqual(bf.OriginalBytes)) rt0++;
    else { fails.Add($"{id}: RT0 drift writing current slots"); continue; }

    // (2) SLOT-ONLY — mutate all 8 slots; only the 16-byte slot region may change.
    ushort[] mutated = current.Select(v => (ushort)(v ^ 0x1234)).ToArray();
    byte[] edited = bf.WriteWithFormationSlots(mutated);
    int off = bf.FormationSlotsOffset;
    if (DiffOnlyAt(bf.OriginalBytes, edited, off, Battle_File.FormationSlotsLength)) slotOnly++;
    else { fails.Add($"{id}: slot edit changed a byte outside the 16-byte slot region @0x{off:X}"); continue; }

    // (3) RE-READ — the mutated file parses back to exactly the mutated slots.
    Battle_File bf2 = Battle_File.Read(id, edited);
    ushort[] back = bf2.Formation!.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();
    if (back.AsSpan().SequenceEqual(mutated)) reread++;
    else fails.Add($"{id}: re-read slots != mutated slots");

    if (dumpId != null && string.Equals(id, dumpId, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"--- dump {id} ---");
        Console.WriteLine($"formationChunkOffset=0x{bf.FormationChunkOffset:X} slotsOffset=0x{bf.FormationSlotsOffset:X}");
        Console.WriteLine($"current slots: {string.Join(" ", current.Select(v => v.ToString("X4")))}");
        Console.WriteLine($"label: {bf.FormationLabel}");
    }
}

// --- SAVE LIFECYCLE check — exercises the PRODUCTION save path (FormationSlotWriter, the exact code the
// Formation Editor calls) on a TEMP COPY of the first writable battle. NEVER touches the workspace/asset
// (read-only assets respected). Proves: no-edit Save = byte-identical on disk + backup==original;
// edit Save = slot-only on disk + reread correct + backup preserved; restore from backup = byte-identical.
bool? saveCheck = null;
var saveCheckLog = new List<string>();
if (saveCheckPath != null && saveCheckId != null)
{
    string tmp = Path.Combine(Path.GetTempPath(), "ffx_formation_savecheck.bin");
    string bak = tmp + FormationSlotWriter.DefaultBackupSuffix;
    try
    {
        if (File.Exists(bak)) File.Delete(bak);
        File.Copy(saveCheckPath, tmp, overwrite: true);
        byte[] original = File.ReadAllBytes(tmp);
        Battle_File sbf = Battle_File.Read(saveCheckId, original);
        int off = sbf.FormationSlotsOffset;
        ushort[] cur = sbf.Formation!.Slots.Select(s => (ushort)s.RawMonsterId).ToArray();

        // (a) no-edit Save -> file byte-identical (RT0) + backup created == original
        var r1 = FormationSlotWriter.WriteLooseFile(tmp, sbf.OriginalBytes, sbf.WriteWithFormationSlots(cur), off, Battle_File.FormationSlotsLength);
        bool aOk = r1.Ok && File.ReadAllBytes(tmp).AsSpan().SequenceEqual(original)
                   && File.Exists(bak) && File.ReadAllBytes(bak).AsSpan().SequenceEqual(original);
        saveCheckLog.Add($"(a) no-edit save : {(aOk ? "OK (disk byte-identical; backup==original)" : "FAIL")}");

        // (b) edit Save (flip slot 0) -> slot-only on disk + reread match + backup preserved (still original)
        ushort[] mut = (ushort[])cur.Clone(); mut[0] = (ushort)(mut[0] ^ 0x1234);
        var r2 = FormationSlotWriter.WriteLooseFile(tmp, sbf.OriginalBytes, sbf.WriteWithFormationSlots(mut), off, Battle_File.FormationSlotsLength);
        byte[] afterEdit = File.ReadAllBytes(tmp);
        bool slotOnlyDisk = FormationSlotWriter.IsSlotOnly(original, afterEdit, off, Battle_File.FormationSlotsLength);
        var rr = Battle_File.Read(saveCheckId, afterEdit);
        bool rereadOk = (ushort)rr.Formation!.Slots[0].RawMonsterId == mut[0];
        bool backupStill = File.ReadAllBytes(bak).AsSpan().SequenceEqual(original);
        bool bOk = r2.Ok && slotOnlyDisk && rereadOk && backupStill;
        saveCheckLog.Add($"(b) edit save    : {(bOk ? "OK (slot-only on disk; reread match; backup preserved)" : "FAIL")}");

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

// The gate proves WRITER SAFETY (the dangerous thing): every file the reader can LOAD and that
// exposes a writable formation must round-trip slot-only. A read failure is a SEPARATE, benign
// reader limitation — such a file can't be loaded by the editor either, so it is never writable.
// We report read failures loudly (no silent cap) but they do NOT fail the writer gate.
// The save-lifecycle check additionally proves the on-disk write/backup/restore path is byte-safe.
bool writerPass = rt0 == writable && slotOnly == writable && reread == writable && writable > 0
                  && (saveCheck ?? false);

var writerFails = fails.Where(f => !f.Contains("Read threw")).ToList();

Console.WriteLine($"FormationSlotLab — btl corpus @ {root}");
Console.WriteLine($"  files scanned     : {total}");
Console.WriteLine($"  no formation      : {noFormation}");
Console.WriteLine($"  writable          : {writable}");
Console.WriteLine($"  RT0 (no-edit)     : {rt0}/{writable}");
Console.WriteLine($"  SLOT-ONLY edit    : {slotOnly}/{writable}");
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
    // honesty: surface the reader limitation, do not bury it.
    Console.WriteLine($"  reader limitation : {readFail} file(s) unparseable by Battle_File.Read (NOT editable, NOT a writer-safety fail):");
    foreach (string f in fails.Where(f => f.Contains("Read threw")).Take(40)) Console.WriteLine($"    {f}");
}
Console.WriteLine(writerPass ? "VERDICT: PASS — formation writer is byte-safe slot-only on every loadable/writable file."
                             : "VERDICT: FAIL — formation writer broke the slot-only invariant.");

if (jsonOut != null)
{
    var verdict = new
    {
        root,
        total,
        readFail,
        noFormation,
        writable,
        rt0,
        slotOnly,
        reread,
        saveCheck,
        saveCheckLog,
        writerPass,
        fails = fails.Take(200).ToArray()
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return writerPass ? 0 : 1;
