// AbilityCommandLab — RT0 / localized-edit gate for the FFX ability/command writer
// (FfxLib/Ability/Ability_Command.cs). SPIRA FORGE.
//
// The KernelCommands editor already SAVES command.bin/item.bin/monmagic via Ability_Command.WriteList — but
// that path was never byte-gated. WriteList REBUILDS the whole file (re-serializes every 0x60/0x5C struct AND
// repacks the text file from script bytes), so RT0 byte-identity is NOT guaranteed by construction. This gate
// is the truth-finder (cf. the Monster writer, which silently drifted 361/361 until fixed):
//   (1) RT0: ReadList -> WriteList reproduces each kernel file byte-identically (no-edit);
//   (2) LOCALIZED EDIT: flipping ONE flag (BreaksDamageLimit) on entry 0 changes exactly 1 byte.
// Exit 0 only if every present file passes RT0 (and the localized-edit probe, where RT0 held).
//
// References the editor project (heavy dep tree) but only calls static FfxLib methods — no game, no Avalonia.
// Usage: AbilityCommandLab [masterRoot] [--json out.json] [--dump-cmds 282,104,...]

using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";

string root = DefaultRoot();
string? jsonOut = null;
int[]? dumpCmds = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (args[i] == "--dump-cmds" && i + 1 < args.Length)
        dumpCmds = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s)).ToArray();
    else if (!args[i].StartsWith("--")) root = args[i];
}

if (dumpCmds is { Length: > 0 })
{
    string cmdPath = Path.Combine(root, @"new_uspc\battle\kernel\command.bin");
    if (!File.Exists(cmdPath)) { Console.Error.WriteLine($"missing {cmdPath}"); return 2; }
    var list = Ability_Command.ReadList(File.ReadAllBytes(cmdPath), true);
    foreach (int id in dumpCmds)
    {
        if (id < 0 || id >= list.Count) { Console.WriteLine($"cmd{id}: OUT OF RANGE (count={list.Count})"); continue; }
        var c = list[id];
        Console.WriteLine($"cmd{id}: MenuFlgs=0x{(byte)c.MenuFlgs:X2} SubMenu={c.SubMenuCategorization} SubSub={c.SubSubMenuCategorization} Char={c.CharacterUser} " +
                          $"Misc2=0x{(byte)c.Misc2Flgs:X2} Misc4=0x{(byte)c.Misc4Flgs:X2} CostOD={c.CostOverdrive} ODCat={c.OverdriveCategory} " +
                          $"MenuLeft={c.FlagMisc2MenuLeft} MenuRight={c.FlagMisc2MenuRight} EmptyOD={c.FlagMisc4EmptyOverdrive} " +
                          $"OpenCmdMenu={c.FlagMenuOpenCommandMenu} MainMenu={c.FlagMenuMainMenu}");
    }
    return 0;
}

// (relative path, hasExtraInfo, label). Command + Item carry the extra block (0x60); MonMagic does not (0x5C).
var targets = new (string rel, bool extra, string label)[]
{
    (@"jppc\battle\kernel\command.bin",     true,  "command(JP)"),
    (@"new_uspc\battle\kernel\command.bin", true,  "command(US)"),
    (@"jppc\battle\kernel\item.bin",        true,  "item(JP)"),
    (@"new_uspc\battle\kernel\item.bin",    true,  "item(US)"),
    (@"new_uspc\battle\kernel\monmagic1.bin", false, "monmagic1(US)"),
    (@"new_uspc\battle\kernel\monmagic2.bin", false, "monmagic2(US)"),
    (@"jppc\battle\kernel\monmagic1.bin",   false, "monmagic1(JP)"),
    (@"jppc\battle\kernel\monmagic2.bin",   false, "monmagic2(JP)"),
};

static int CountDiff(byte[] a, byte[] b)
{
    if (a.Length != b.Length) return -1; // length drift
    int n = 0;
    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
    return n;
}

var results = new List<object>();
int present = 0, rt0Pass = 0, editLocal = 0, editTested = 0;
// "certified" scope = the writers we want byte-safe for SPIRA FORGE ability authoring: Command + Item
// (hasExtraInfo). MonMagic (no extra) is tracked separately as a KNOWN-DRIFT risk (not gating, like Monster).
int certPresent = 0, certRt0 = 0, certEdit = 0, certEditTested = 0;
int mmPresent = 0, mmDrift = 0;
var fails = new List<string>();
var driftKnown = new List<string>();

foreach (var (rel, extra, label) in targets)
{
    string path = Path.Combine(root, rel);
    if (!File.Exists(path)) continue;
    present++;
    if (extra) certPresent++; else mmPresent++;

    byte[] orig = File.ReadAllBytes(path);
    List<Ability_Command> list;
    try { list = Ability_Command.ReadList(orig, extra); }
    catch (Exception ex) { fails.Add($"{label}: ReadList threw {ex.GetType().Name}: {ex.Message}"); results.Add(new { label, ok = false, err = ex.Message }); continue; }

    byte[] re = Ability_Command.WriteList(list, extra);
    bool rt0 = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);
    int firstDiff = -1;
    if (!rt0)
    {
        int lim = Math.Min(orig.Length, re.Length);
        for (int i = 0; i < lim; i++) if (i >= re.Length || orig[i] != re[i]) { firstDiff = i; break; }
        if (firstDiff < 0) firstDiff = lim; // length-only drift
    }

    int editDiff = -1;
    if (rt0)
    {
        rt0Pass++;
        if (extra) certRt0++;
        if (list.Count > 0)
        {
            editTested++;
            if (extra) certEditTested++;
            List<Ability_Command> list2 = Ability_Command.ReadList(orig, extra);
            list2[0].FlagDamageBreaksDamageLimit = !list2[0].FlagDamageBreaksDamageLimit;
            byte[] edited = Ability_Command.WriteList(list2, extra);
            editDiff = CountDiff(orig, edited);
            if (editDiff == 1) { editLocal++; if (extra) certEdit++; }
            else fails.Add($"{label}: 1-flag edit changed {editDiff} byte(s) (expected 1)");
        }
    }
    else if (extra)
    {
        // a Command/Item drift WOULD be a real regression (these are certified byte-safe).
        fails.Add($"{label}: RT0 DRIFT (orig={orig.Length} re={re.Length} firstDiff@0x{firstDiff:X})");
    }
    else
    {
        // MonMagic is now byte-safe via the preserve-only text pool (Ability_Command.WriteList re-emits the
        // original pool verbatim, keeping shared empty-string offsets). A drift here is a REAL regression, so it
        // now FAILS the gate (previously a known, non-gating text-repack drift).
        mmDrift++;
        fails.Add($"{label}: MonMagic RT0 DRIFT (orig={orig.Length} re={re.Length} firstDiff@0x{firstDiff:X})");
    }

    Console.WriteLine($"  {label,-16} entries={list.Count,4} len={orig.Length,7} RT0={(rt0 ? "OK " : "DRIFT")}" +
                      (rt0 ? $" 1flag-edit-diff={editDiff}" : $" firstDiff@0x{firstDiff:X}"));
    results.Add(new { label, entries = list.Count, length = orig.Length, rt0, firstDiff, editDiff });
}

// Gate scope = Command + Item AND MonMagic (all now byte-safe via the preserve-only text pool). A MonMagic RT0
// drift now fails the gate too (it was promoted from known-drift after the preserve-only fix).
bool pass = certPresent > 0 && certRt0 == certPresent && certEdit == certEditTested && mmDrift == 0 && fails.Count == 0;

Console.WriteLine();
Console.WriteLine($"AbilityCommandLab — Ability_Command writer gate @ {root}");
Console.WriteLine($"  files present       : {present}");
Console.WriteLine($"  CERTIFIED (Command/Item):");
Console.WriteLine($"    RT0 byte-identical: {certRt0}/{certPresent}");
Console.WriteLine($"    1-flag localized  : {certEdit}/{certEditTested}");
if (fails.Count > 0)
{
    Console.WriteLine($"  CERTIFIED FAILS ({fails.Count}):");
    foreach (string f in fails) Console.WriteLine($"    {f}");
}
if (mmPresent > 0)
{
    Console.WriteLine($"  MonMagic (now CERTIFIED byte-safe via preserve-only text pool):");
    Console.WriteLine($"    RT0 byte-identical: {mmPresent - mmDrift}/{mmPresent}");
    foreach (string d in driftKnown) Console.WriteLine($"    {d}");
}
Console.WriteLine(pass
    ? "VERDICT: PASS — Command/Item AND MonMagic ability writers round-trip byte-identical; edits localized."
    : "VERDICT: FAIL — an ability writer (Command/Item/MonMagic) broke byte-safety.");

if (jsonOut != null)
{
    var verdict = new
    {
        root, present,
        certified = new { certPresent, certRt0, certEdit, certEditTested },
        monmagic = new { mmPresent, mmDrift, driftKnown },
        pass, results, fails
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return pass ? 0 : 1;
