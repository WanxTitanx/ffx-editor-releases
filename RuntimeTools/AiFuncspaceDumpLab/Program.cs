// AiFuncspaceDumpLab — live dump of the ATEL FUNCSPACE handler tables via the DINPUT8 probe.
// Reads g_FFX_Atel_FuncspaceTables[ns] (the runtime-filled script->C bridge) from the running game
// and resolves each populated handler pointer back to its FFX.exe IDB VA (ImageBase 0x400000) so the
// AI editor can label what every CALL func-id does. Requires the game running with ffx-probe.dll.
//
// IDB VAs (from FFX_recon.i64): g_FFX_Atel_FuncspaceTables @ 0x1328518; entry stride 16, fn ptr @+0,
// funcId = (namespace<<12) | index. FUNCSPACE: 0=Common 1=Math 4=SgEvent 5=ChEvent 6=Camera 7=Battle
// 8=Map 9=Mount 11=Movie 12=Debug 13=AbilityMap.

using System.Text;
using FFXProjectEditor.Services;

const uint IMAGE_BASE = 0x400000;
const uint VA_FUNCSPACE_TABLES = 0x1328518; // g_FFX_Atel_FuncspaceTables
uint Rva(uint va) => va - IMAGE_BASE;

var svc = FfxProbe_Service.Instance;
if (!svc.IsAttached) { Console.Error.WriteLine("probe nao anexado — jogo rodando com ffx-probe.dll?"); return 2; }
uint moduleBase = svc.ModuleBase;
Console.WriteLine($"probe hooked={svc.IsHooked} heartbeat={svc.Heartbeat} moduleBase=0x{moduleBase:X8}");
if (moduleBase == 0) { Console.Error.WriteLine("moduleBase 0 — probe ainda nao reportou base"); return 2; }

uint? ReadU32(uint rva)
{
    var r = svc.Read(rva, 4);
    return r.Ok && r.Data != null ? BitConverter.ToUInt32(r.Data, 0) : (uint?)null;
}
// absolute runtime VA -> FFX.exe IDB VA (only meaningful if it's inside the module)
string IdbVa(uint abs)
{
    if (abs == 0) return "0";
    long idb = (long)abs - moduleBase + IMAGE_BASE;
    return idb >= IMAGE_BASE && idb < 0x2000000 ? $"0x{idb:X}" : $"abs:0x{abs:X8}";
}

// Known names from FFXDataParser ScriptCallTargetLib (cross-check / annotate).
var known = new Dictionary<int, string>
{
    [0x0000] = "wait", [0x005F] = "halt", [0x00A9] = "GetRandomValue",
    [0x013B] = "displayFieldChoice",
    [0x01AD] = "incrementMonsterArenaCaptures",
    [0x01AF] = "changeMonsterArenaCaptures",
    [0x01FA] = "monsterArenaSelection?",
    [0x01FB] = "monsterArenaMenuState?",
    [0x0210] = "setMonsterArenaUnlocked",
    [0x0221] = "resolveMonsterArenaBattle?",
    [0x023B] = "checkMonsterArenaUnlock",
    [0x023C] = "checkMultipleMonsterArenaUnlocks",
    [0x401D] = "showModularMenu",
    [0x6041] = "refSetBtlPolar", [0x6080] = "camRand",
    [0x7002] = "launchBattle",
    [0x700B] = "btlSetDirectCommand", [0x700F] = "btlGetStat", [0x7010] = "btlSearchChr",
    [0x7018] = "btlSetStat", [0x701E] = "btlCountChr", [0x7021] = "btlGetChrNum", [0x7024] = "btlGetBtlScene",
    [0x7035] = "BattleEndType",
};

var namespaces = new (int ns, string name, int count)[]
{
    (0, "Common", 0x250),
    (1, "Math", 0x80),
    (4, "SgEvent", 0x80),
    (6, "Camera", 0x100),
    (7, "Battle", 0x100),
};

var outSb = new StringBuilder();
outSb.AppendLine("# ATEL FUNCSPACE live handler dump (funcId -> handler IDB VA)");
outSb.AppendLine($"# moduleBase=0x{moduleBase:X8}");

foreach (var (ns, nsName, count) in namespaces)
{
    uint? tableAbs = ReadU32(Rva(VA_FUNCSPACE_TABLES) + (uint)(4 * ns));
    if (tableAbs == null || tableAbs.Value == 0)
    {
        Console.WriteLine($"\n=== {nsName} (ns {ns}): table ptr null/0 (nao registrado?) ===");
        continue;
    }
    uint tableRva = tableAbs.Value - moduleBase;
    Console.WriteLine($"\n=== {nsName} (ns {ns}): table @abs 0x{tableAbs:X8} (IDB {IdbVa(tableAbs.Value)}) scanCount=0x{count:X} ===");
    outSb.AppendLine($"\n## {nsName} (ns {ns}) table={IdbVa(tableAbs.Value)} scanCount=0x{count:X}");
    int populated = 0;
    for (int idx = 0; idx < count; idx++)
    {
        uint? fn = ReadU32(tableRva + (uint)(16 * idx));
        if (fn == null) { Console.WriteLine($"  [read fail @idx {idx}]"); break; }
        if (fn.Value == 0) continue;
        populated++;
        int funcId = (ns << 12) | idx;
        string note = known.TryGetValue(funcId, out var n) ? $"  ; {n}" : "";
        string line = $"  {funcId:X4}h (idx {idx,3}) -> {IdbVa(fn.Value),-12}{note}";
        Console.WriteLine(line);
        outSb.AppendLine(line);
    }
    Console.WriteLine($"  -> {populated} handler(s) populados");
    outSb.AppendLine($"# {populated} populated");
}

string outPath = args.Length > 0 ? args[0] : "RuntimeTools/AiFuncspaceDumpLab/funcspace_dump.md";
File.WriteAllText(outPath, outSb.ToString());
Console.WriteLine($"\nOK -> {outPath}");
return 0;
