using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;

// 🐉 Aurora × noclip link lab — proves the battleId ↔ 0e/<id>.bin encounter mapping + the override flow.
// Usage: Aurora3DLinkLab [btlRoot] [noclip0eRoot] [--json work/encounter-index.json] [--override <battleId>]

var positional = args.TakeWhile(a => !a.StartsWith("--")).ToArray();
string btlRoot = positional.Length > 0 ? positional[0] : EncounterIndexBridge.DefaultBtlRoot;
string encRoot = positional.Length > 1 ? positional[1] : EncounterIndexBridge.DefaultNoclip0eRoot;

EncounterIndexBridge index = EncounterIndexBridge.Build(btlRoot, encRoot);

Console.WriteLine($"corpus btl bins : {index.CorpusBattleCount}");
Console.WriteLine($"noclip 0e bins  : {index.NoclipEncounterCount}");
Console.WriteLine($"matched         : {index.BattleIdToEncounter.Count}");
Console.WriteLine($"similarity      : {index.BattleIdToNearestEncounter.Count} (fallback por blocos 256B)");
Console.WriteLine($"unmatched       : {index.UnmatchedBattleIds.Count}");

foreach (string probe in new[] { "azit03_00", "bika01_00", "bika02_00", "bika02_04", "klyt00_00", "mihn00_00", "zzzz00_00", "sfia00_00" })
{
    bool ok = index.TryResolve(probe, out int encId);
    string via = ok && index.IsSimilarityResolved(probe) ? "SIMILARIDADE" : (ok ? "exato" : "—");
    Console.WriteLine($"  {probe,-12} -> {(ok ? $"0e/{encId:X4}.bin" : "SEM MATCH"),-18} via={via}");
}

if (index.UnmatchedBattleIds.Count > 0)
{
    Console.WriteLine("exemplos sem match: " + string.Join(", ", index.UnmatchedBattleIds.Take(12)));
}

int jsonArg = Array.FindIndex(args, a => a == "--json");
if (jsonArg >= 0 && jsonArg + 1 < args.Length)
{
    string outPath = args[jsonArg + 1];
    File.WriteAllText(outPath, index.ToJson());
    Console.WriteLine($"índice JSON -> {outPath}");
}

int ovArg = Array.FindIndex(args, a => a == "--override");
if (ovArg >= 0 && ovArg + 1 < args.Length)
{
    string battleId = args[ovArg + 1];
    if (!index.TryResolve(battleId, out int encId))
    {
        Console.WriteLine($"OVERRIDE: {battleId} sem correspondência 0e/ — FAIL");
        return 2;
    }
    string src = Path.Combine(btlRoot, battleId, battleId + ".bin");
    string target = Path.Combine(encRoot, $"{encId:X4}.bin");
    string bak = target + ".aurora3d.bak";
    string vanillaHash = Sha256(target);
    if (!File.Exists(bak)) File.Copy(target, bak);
    File.Copy(src, target, overwrite: true);
    string overrideHash = Sha256(target);
    bool overrideOk = overrideHash == Sha256(src);
    // restore
    File.Copy(bak, target, overwrite: true);
    bool restoredOk = Sha256(target) == vanillaHash;
    Console.WriteLine($"OVERRIDE {battleId} -> 0e/{encId:X4}.bin: copy={overrideOk} restore={restoredOk} (vanilla {vanillaHash[..8]}…)");
    if (!overrideOk || !restoredOk) return 3;
}

return index.BattleIdToEncounter.Count > 0 ? 0 : 2;

static string Sha256(string path)
{
    using var fs = File.OpenRead(path);
    using var sha = SHA256.Create();
    return Convert.ToHexString(sha.ComputeHash(fs));
}
