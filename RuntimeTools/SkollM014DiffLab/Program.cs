// SkollM014DiffLab — one-shot semantic comparator between two m014.bin variants.
// Reads two monster_*.bin paths via the repo's dependency-free AiScript_File codec,
// runs AiScript_Diff.Compare, and prints a human-readable report locating the
// instructions added by UNI-003 (Slow-on-random-target) versus the previous recipe
// (UNI-003 HasteEnrage = GrantChrProperty(Self, 0x38, 255) under hpBelow 50).
//
// Read-only: never writes anything to disk.
// Usage:
//   SkollM014DiffLab                                          # uses defaults below
//   SkollM014DiffLab <edited.bin> <baseline.bin>

using System.Text;
using FFXProjectEditor.FfxLib.Ai;

const string EditedDefault =
    @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\jppc\battle\mon\_m014\m014.bin";
const string BaselineDefault =
    @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\jppc\battle\mon\_m014\m014 - Skoll Pré Mudancas SIN.bin";

string editedPath   = args.Length > 0 ? args[0] : EditedDefault;
string baselinePath = args.Length > 1 ? args[1] : BaselineDefault;

Console.OutputEncoding = Encoding.UTF8;

if (!File.Exists(editedPath))   { Console.Error.WriteLine($"Edited not found: {editedPath}");   return 2; }
if (!File.Exists(baselinePath)) { Console.Error.WriteLine($"Baseline not found: {baselinePath}"); return 2; }

byte[] editedBytes   = File.ReadAllBytes(editedPath);
byte[] baselineBytes = File.ReadAllBytes(baselinePath);

byte[]? editedAi   = AiScript_File.SliceAiFileFromMonster(editedBytes);
byte[]? baselineAi = AiScript_File.SliceAiFileFromMonster(baselineBytes);
if (editedAi == null)   { Console.Error.WriteLine("Edited: no AiFile partition.");   return 3; }
if (baselineAi == null) { Console.Error.WriteLine("Baseline: no AiFile partition."); return 3; }

AiScriptFile editedScript;
AiScriptFile baselineScript;
try
{
    editedScript   = AiScript_File.Read(editedAi);
    baselineScript = AiScript_File.Read(baselineAi);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Parse failed: {ex.Message}");
    return 4;
}

Console.WriteLine("=== SkollM014DiffLab semantic comparator ===");
Console.WriteLine($"Edited    : {editedPath}");
Console.WriteLine($"            monster=0x{editedBytes.Length:X}  AiFile=0x{editedAi.Length:X}  codelen=0x{editedScript.CodeLength:X}  workers={editedScript.Workers.Count}  closed={editedScript.CodeWalkClosedExactly}");
Console.WriteLine($"Baseline  : {baselinePath}");
Console.WriteLine($"            monster=0x{baselineBytes.Length:X}  AiFile=0x{baselineAi.Length:X}  codelen=0x{baselineScript.CodeLength:X}  workers={baselineScript.Workers.Count}  closed={baselineScript.CodeWalkClosedExactly}");
Console.WriteLine();

Console.WriteLine("--- private storage per worker ---");
for (int k = 0; k < Math.Max(editedScript.Workers.Count, baselineScript.Workers.Count); k++)
{
    int ePriv = k < editedScript.Workers.Count   ? editedScript.Workers[k].PrivateDataLength   : -1;
    int bPriv = k < baselineScript.Workers.Count ? baselineScript.Workers[k].PrivateDataLength : -1;
    Console.WriteLine($"  worker[{k}]: baseline privLen=0x{Math.Max(bPriv, 0):X}  edited privLen=0x{Math.Max(ePriv, 0):X}  Δ={(ePriv - bPriv):+0;-0}");
}
Console.WriteLine();

Console.WriteLine("--- entrypoints per worker (offsets are AiFile-absolute) ---");
for (int k = 0; k < Math.Max(editedScript.Workers.Count, baselineScript.Workers.Count); k++)
{
    string bType = k < baselineScript.Workers.Count ? baselineScript.Workers[k].InferredType ?? "?" : "-";
    string eType = k < editedScript.Workers.Count   ? editedScript.Workers[k].InferredType ?? "?" : "-";
    string bEps = k < baselineScript.Workers.Count
        ? "[" + string.Join(",", baselineScript.Workers[k].Entrypoints.Select(e => $"0x{baselineScript.ScriptStart + e:X}")) + "]"
        : "[]";
    string eEps = k < editedScript.Workers.Count
        ? "[" + string.Join(",", editedScript.Workers[k].Entrypoints.Select(e => $"0x{editedScript.ScriptStart + e:X}")) + "]"
        : "[]";
    Console.WriteLine($"  worker[{k}]  baseline={bType}@{bEps}");
    Console.WriteLine($"              edited  ={eType}@{eEps}");
}
Console.WriteLine();

IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(baselineScript, editedScript);
var byType = diff.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
int add = byType.GetValueOrDefault(AiDiffType.Added);
int mod = byType.GetValueOrDefault(AiDiffType.Modified);
int rem = byType.GetValueOrDefault(AiDiffType.Removed);
int unch = byType.GetValueOrDefault(AiDiffType.Unchanged);
Console.WriteLine($"DIFF: unchanged={unch}  modified={mod}  added={add}  removed={rem}");
Console.WriteLine();

var grouped = diff
    .Where(e => e.Type is AiDiffType.Added or AiDiffType.Modified or AiDiffType.Removed)
    .GroupBy(e => e.WorkerIndex)
    .OrderBy(g => g.Key)
    .ToList();

foreach (var g in grouped)
{
    int wIx = g.Key;
    string label = wIx < 0 ? "owner=-1" : $"worker[{wIx}]";

    AiWorker? bWorker = wIx >= 0 && wIx < baselineScript.Workers.Count ? baselineScript.Workers[wIx] : null;
    AiWorker? eWorker = wIx >= 0 && wIx < editedScript.Workers.Count   ? editedScript.Workers[wIx]   : null;
    Console.WriteLine($"--- {label}  edited-inferredType={eWorker?.InferredType ?? "?"}  baseline-inferredType={bWorker?.InferredType ?? "?"}");

    DumpEntrypoint("baseline", baselineScript, bWorker);
    DumpEntrypoint("edited  ", editedScript, eWorker);

    Console.WriteLine();
    Console.WriteLine($"  changed entries:");
    foreach (AiDiffEntry e in g.OrderBy(x => x.Offset))
    {
        Console.WriteLine($"    [{e.Type}] @0x{e.Offset:X4}");
        if (e.OriginalText != null) Console.WriteLine($"      -  {e.OriginalText}");
        if (e.EditedText   != null) Console.WriteLine($"      +  {e.EditedText}");
    }
    Console.WriteLine();
}

Console.WriteLine("=== SEMANTIC SYNTHESIS (added instructions) ===");
var addedEntries = diff.Where(d => d.Type == AiDiffType.Added)
    .OrderBy(d => d.WorkerIndex).ThenBy(d => d.Offset).ToList();

int runStart = addedEntries.Count > 0 ? addedEntries.First().Offset : -1;
int runEnd   = addedEntries.Count > 0 ? addedEntries.Last().Offset + 3 : -1;
Console.WriteLine($"Added instruction byte-range: 0x{runStart:X4} .. 0x{runEnd - 1:X4} ({addedEntries.Count} instructions)");
Console.WriteLine();

if (runStart >= 0)
{
    int lo = runStart;
    int hi = runEnd;
    Console.WriteLine($"--- edited disassembly around the added region ---");
    foreach (AiInstruction ins in editedScript.Instructions)
    {
        if (ins.Offset < lo - 6 || ins.Offset > hi + 6) continue;
        Console.WriteLine("  " + AiScript_File.Format(ins) + "   ; " + GlossFor(editedScript, ins));
    }
    Console.WriteLine();
}

Console.WriteLine("=== DONE ===");
return 0;

static void DumpEntrypoint(string tag, AiScriptFile s, AiWorker? w)
{
    if (w == null) return;
    for (int ei = 0; ei < w.Entrypoints.Count; ei++)
    {
        int abs = s.ScriptStart + w.Entrypoints[ei];
        int idx = -1;
        for (int i = 0; i < s.Instructions.Count; i++)
        {
            if (s.Instructions[i].Offset == abs) { idx = i; break; }
        }
        if (idx < 0)
        {
            Console.WriteLine($"  ep[{tag}/{ei}] -> 0x{abs:X} (no instruction at that offset)");
            continue;
        }
        int end = Math.Min(idx + 14, s.Instructions.Count);
        Console.WriteLine($"  ep[{tag}/{ei}] -> 0x{abs:X}");
        for (int j = idx; j < end; j++)
        {
            AiInstruction ins = s.Instructions[j];
            Console.WriteLine("    " + AiScript_File.Format(ins) + "   ; " + GlossFor(s, ins));
        }
    }
}

static string GlossFor(AiScriptFile s, AiInstruction ins)
{
    if (ins.Opcode == 0xAE && ins.HasOperand)
    {
        ushort op = ins.Operand;
        if (op == 0xFFF3) return "[target] SelfRef (owner/self)";
        if (op == 0xFFEF) return "[target] LastAttacker (0xFFEF)";
        if (op == 0xFFF2) return "[target] FrontlineChars (all 3 player chars)";
        if (op == 0xFFF1) return "[target] AllAeons";
        if (op == 0xFFF0) return "[target] PredefinedGroup / Random Enemies (RT2: group-random)";
        if (op == 0xFFEC) return "[target] SingleActor";
        if (op == 0xFFE9) return "[target] NonAeonActors";
        if (op == 0xFFFB) return "[target] AllActors";
        if (op == 0xFFFE) return "[target] ActiveActors (LAB)";
        if (op == 0xFFFA) return "[target] Character#1";
        if (op == 0xFFF9) return "[target] Character#2";
        if (op == 0xFFF8) return "[target] Character#3";
        if (op == 0xFFFD) return "[target] TargetActors";
        if (op == 0xFFFC) return "[target] TargetActorsNow";
        if (op == 0xFFFF) return "[target] Null";
        if (op == 0x00FF) return "[target] None";
        string? fld = AiChrPropertyNames.Get(op);
        if (fld != null) return $"[field=0x{op:X4}] {fld}";
        if ((op & 0xF000) == 0x3000) return $"[cmd=0x{op:X4}] character/black-magic id=0x{op & 0xFFF:X3}";
        if ((op & 0xF000) == 0x4000) return $"[cmd=0x{op:X4}] monster/aeon id=0x{op & 0xFFF:X3}";
        return $"[imm=0x{op:X4} dec={op}]";
    }
    if ((ins.Opcode == 0xB5 || ins.Opcode == 0xD8) && ins.HasOperand)
    {
        return CallName(ins.Operand);
    }
    return "—";
}

static string CallName(ushort id)
{
    return (id >> 12) switch
    {
        0x0 => id == 0x00A9
            ? "[call=0x00A9] GetRandomValue (RNG; no operands, returns 0..N on stack)"
            : $"[call=0x{id:X4}] (namespace 0x{id >> 12:X})",
        0x6 => id switch
        {
            0x700B => "[call=0x700B] performCommand (push target, commandId; goes via CTB queue)",
            0x700F => "[call=0x700F] readChrProperty (push target, field; returns value on stack)",
            0x7018 => "[call=0x7018] writeChrProperty (push target, field, value)",
            0x705A => "[call=0x705A] forcePerformCommand (push target, commandId; immediate)",
            0x70AA => "[call=0x70AA] getStatField (push field; returns value)",
            0x70AB => "[call=0x70AB] setStatField (push field, value)",
            0x70E0 => "[call=0x70E0] IsCounterattackAllowed (gate; no operands)",
            _ => $"[call=0x{id:X4}]",
        },
        0x7 => $"[call=0x{id:X4}] chr-property namespace",
        _ => $"[call=0x{id:X4}] namespace=0x{id >> 12:X}",
    };
}
