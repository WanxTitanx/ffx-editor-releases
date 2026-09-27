// ArenaMultiBossLab — deterministic authoring CLI for Arena+ Multi Dark Aeon recipes.

//

// What it does (one shot per tier):

//   1. Reads a recipe JSON  (chunk2 slots + chunk3 monLive positions; optional grow).

//   2. Loads the alias vanilla battle .bin (--vanilla-root or --source).

//   3. Applies chunk2 via Battle_File.WriteWithFormationSlots (slot-only writer; 858/858 RT0 proven).

//   4. Applies chunk3 via BattleArenaPositionWriter or BattleArenaGrowWriter (gated).

//   5. Validates RT0 byte-safety on the result (slot-only + position-only diff guards re-checked).

//   6. Deploys the .bin under the Spira Reforge mod path, creating .spiraforge.bak once.

//

// Compose mode (--compose): player pick list → spread template → same pipeline on HD carrier.

//

// Plan: .cursor/plans/arena_plus_multi_dark_aeon_*.plan.md

// Dossie: docs/reverse/FFX_ARENA_PLUS_MULTI_DARK_AEON_AUTHORING_DOSSIER_2026-06-16.md



using System.Text.Json;

using ArenaMultiBossLab;



// ---------- argv parsing ----------



string? recipeName = null;

string? recipeDir = null;

string? vanillaRoot = null;

string? modRoot = null;
string? positionsRoot = null;   // --positions-root: dir do arena_positions.json (grid editado no hook)

string? sourceOverride = null;

string? catalogPath = null;

string? progressPath = null;

string? tierLockOutPath = null;

string? composePick = null;

string? composeIds = null;

string? composeScenario = null;
string? layoutProfile = null;
bool scanMode = false;
string? scanVanilla = null;
string? scanOut = null;

string? manifestOut = null;

string? cacheDir = null;

bool dryRun = false;
bool ultraMode = false;
string? ultraManifest = null;

bool listRecipes = false;

bool listPicks = false;

bool composeMode = false;

bool validateMode = false;

bool printTierLock = false;

bool jsonOutput = false;

bool scanScenarios = false;

int scanMinActors = 3;

bool autoLayout = false;

string? autoLayoutCheckBin = null;

string? autoLayoutCheckId = null;

int autoLayoutCheckCount = 4;



for (int i = 0; i < args.Length; i++)

{

    switch (args[i])

    {

        case "--recipe" when i + 1 < args.Length: recipeName = args[++i]; break;

        case "--recipe-dir" when i + 1 < args.Length: recipeDir = args[++i]; break;

        case "--vanilla-root" when i + 1 < args.Length: vanillaRoot = args[++i]; break;

        case "--mod-root" when i + 1 < args.Length: modRoot = args[++i]; break;
        case "--positions-root" when i + 1 < args.Length: positionsRoot = args[++i]; break;

        case "--source" when i + 1 < args.Length: sourceOverride = args[++i]; break;

        case "--catalog" when i + 1 < args.Length: catalogPath = args[++i]; break;

        case "--progress" when i + 1 < args.Length: progressPath = args[++i]; break;

        case "--out" when i + 1 < args.Length: tierLockOutPath = args[++i]; break;

        case "--pick" when i + 1 < args.Length: composePick = args[++i]; break;

        case "--ids" when i + 1 < args.Length: composeIds = args[++i]; break;

        case "--scenario" when i + 1 < args.Length: composeScenario = args[++i]; break;

        case "--manifest-out" when i + 1 < args.Length: manifestOut = args[++i]; break;
    case "--layout-profile" when i + 1 < args.Length: layoutProfile = args[++i]; break;
    case "--layout-scan": scanMode = true; break;
    case "--layout-scan-vanilla" when i + 1 < args.Length: scanVanilla = args[++i]; break;
    case "--layout-scan-out" when i + 1 < args.Length: scanOut = args[++i]; break;

        case "--cache-dir" when i + 1 < args.Length: cacheDir = args[++i]; break;

        case "--dry-run": dryRun = true; break;
        case "--ultra" when i + 1 < args.Length: ultraMode = true; ultraManifest = args[++i]; break;

        case "--list": listRecipes = true; break;

        case "--list-picks": listPicks = true; break;

        case "--compose": composeMode = true; break;

        case "--validate": validateMode = true; break;

        case "--print-tier-lock": printTierLock = true; break;

        case "--json": jsonOutput = true; break;

        case "--scan-scenarios": scanScenarios = true; break;

        case "--scan-min" when i + 1 < args.Length: scanMinActors = int.Parse(args[++i]); break;

        case "--auto-layout": autoLayout = true; break;

        case "--auto-layout-check-bin" when i + 1 < args.Length: autoLayoutCheckBin = args[++i]; break;

        case "--auto-layout-check-id" when i + 1 < args.Length: autoLayoutCheckId = args[++i]; break;

        case "--auto-layout-check-count" when i + 1 < args.Length: autoLayoutCheckCount = int.Parse(args[++i]); break;

        case "--help" or "-h":

            PrintHelp();

            return 0;

    }

}



recipeDir ??= Path.Combine(AppContext.BaseDirectory, "recipes");



if (listRecipes)

{

    if (!Directory.Exists(recipeDir))

    {

        Console.Error.WriteLine($"recipe dir not found: {recipeDir}");

        return 2;

    }

    Console.WriteLine($"Recipes in {recipeDir}:");

    foreach (string r in Directory.EnumerateFiles(recipeDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))

        Console.WriteLine($"  - {Path.GetFileNameWithoutExtension(r)}");

    return 0;

}



if (validateMode)

{

    return ArenaCatalogValidator.Run(catalogPath, recipeDir, vanillaRoot);

}



if (printTierLock)

{

    return ArenaTierLockReport.Run(catalogPath, progressPath, tierLockOutPath, jsonOutput);

}



if (scanScenarios)

{

    return ScenarioScan.Run(vanillaRoot, scanMinActors);

}

if (!string.IsNullOrEmpty(autoLayoutCheckBin) && !string.IsNullOrEmpty(autoLayoutCheckId))

{

    byte[] bin = File.ReadAllBytes(autoLayoutCheckBin);

    var r = BattleComposeRunner.ComputeAutoLayoutFromCamera(bin, autoLayoutCheckId, autoLayoutCheckCount);

    if (r == null)

    {

        Console.WriteLine($"auto-layout: camera nao extraivel em {autoLayoutCheckId} (sem ref/polar)");

        return 1;

    }

    Console.WriteLine($"auto-layout : {autoLayoutCheckId} count={autoLayoutCheckCount}");

    Console.WriteLine($"party row   : X={r.PartyX:F1} Z={r.PartyZ:F1}  forward=({r.ForwardX:F2},{r.ForwardZ:F2})  right=({r.RightX:F2},{r.RightZ:F2})");

    for (int i = 0; i < r.Monsters.Length; i++)

        Console.WriteLine($"mon[{i}]      : ({r.Monsters[i][0]:F1}, {r.Monsters[i][2]:F1})");

    return 0;

}



if (scanMode)
{
    if (string.IsNullOrEmpty(vanillaRoot) || string.IsNullOrEmpty(scanOut))
    {
        Console.Error.WriteLine("--layout-scan requires --vanilla-root <btlRoot> e --layout-scan-out <path>");
        return 2;
    }
    return LayoutScanner.Run(vanillaRoot, scanVanilla, scanOut);
}

    if (ultraMode && !string.IsNullOrEmpty(ultraManifest))
    {
        return BattleComposeRunner.RunUltra(ultraManifest, vanillaRoot, modRoot, null, dryRun);
    }

if (composeMode || listPicks || !string.IsNullOrEmpty(composePick) || !string.IsNullOrEmpty(composeIds))

{

    return BattleComposeRunner.Run(composePick, composeIds, composeScenario, vanillaRoot, modRoot, positionsRoot, manifestOut, cacheDir, dryRun, listPicks, autoLayout, layoutProfile);

}



if (string.IsNullOrEmpty(recipeName))

{

    Console.Error.WriteLine("missing --recipe <id>   (use --list to enumerate, --help for usage)");

    return 1;

}



string recipePath = Path.IsPathRooted(recipeName)

    ? recipeName

    : Path.Combine(recipeDir, recipeName + (recipeName.EndsWith(".json") ? "" : ".json"));

if (!File.Exists(recipePath))

{

    Console.Error.WriteLine($"recipe not found: {recipePath}");

    return 2;

}



Recipe recipe;

try

{

    string json = File.ReadAllText(recipePath);

    recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOpts.Instance) ?? throw new InvalidDataException("null recipe");

}

catch (Exception ex)

{

    Console.Error.WriteLine($"recipe parse failed: {ex.Message}");

    return 2;

}



string sourcePath;

if (!string.IsNullOrEmpty(sourceOverride))

{

    sourcePath = sourceOverride;

}

else if (!string.IsNullOrEmpty(vanillaRoot))

{

    sourcePath = Path.Combine(vanillaRoot, recipe.SourceBattleId, recipe.SourceBattleId + ".bin");

}

else

{

    Console.Error.WriteLine("missing --vanilla-root <btlRoot> or --source <path>");

    return 1;

}



return BattleRecipeApplicator.Apply(recipe, sourcePath, vanillaRoot, modRoot, dryRun).ExitCode;



static void PrintHelp()

{

    Console.WriteLine("ArenaMultiBossLab — Arena+ Multi Dark Aeon authoring CLI");

    Console.WriteLine();

    Console.WriteLine("usage:");

    Console.WriteLine("  ArenaMultiBossLab --recipe <id> [--recipe-dir <path>]");

    Console.WriteLine("                    --vanilla-root <btlRoot>  (or --source <bin>)");

    Console.WriteLine("                    [--mod-root <btlRoot> | --dry-run]");

    Console.WriteLine("  ArenaMultiBossLab --compose --pick valefor,ifrit,ixion");

    Console.WriteLine("                    --vanilla-root <btlRoot> [--mod-root <btlRoot> | --dry-run]");

    Console.WriteLine("                    [--ids 0x114E,0x114F,0x1150]  (alternative to --pick)");

    Console.WriteLine("                    [--scenario macalania_forest|macalania_open|macalania_open2|remiem|...]  (Custom Mix only)");

    Console.WriteLine("                    [--manifest-out <json>] [--cache-dir <dir>]");

    Console.WriteLine("  ArenaMultiBossLab --list-picks   (8 aeon ticks + carrier map)");

    Console.WriteLine("  ArenaMultiBossLab --list         (enumerate recipes)");

    Console.WriteLine("  ArenaMultiBossLab --validate     (validate catalog + cross-check recipes)");

    Console.WriteLine("                    [--catalog <path>]        (default: locate Spira Reforge mod)");

    Console.WriteLine("                    [--vanilla-root <btlRoot>] (enables full recipe dry-run sweep)");

    Console.WriteLine("  ArenaMultiBossLab --print-tier-lock  (compute LOCKED/READY/CLEARED per row)");

    Console.WriteLine("                    [--catalog <path>] [--progress <path>] [--out <json>] [--json]");

    Console.WriteLine();

    Console.WriteLine("Compose rules:");

    Console.WriteLine("  3/4/5 actors total (Magus tick = +3 slots 0x1155..0x1157; Penance 0x1158 out of scope)");

    Console.WriteLine("  3 -> mcyt00_22 | 4 -> nagi05_23 | 5 -> nagi05_22 (dedicated; presets untouched)");
    Console.WriteLine("  --scenario picks camera/chunk0 template (Custom Mix only; Gauntlet presets fixed)");
    Console.WriteLine("  F7: Custom Mix x3/x4/x5 launch the composed bin on those carriers");

    Console.WriteLine();

    Console.WriteLine("Recipe JSON schema:");

    Console.WriteLine("  id                  string");

    Console.WriteLine("  tier                duo | trio | quartet | penta");

    Console.WriteLine("  source_battle_id    'kino00_70' (vanilla clone source)");

    Console.WriteLine("  output_battle_id    'zzzz00_240' (deploy target; defaults to source)");

    Console.WriteLine("  alias_battle_id     deprecated alias of source_battle_id");

    Console.WriteLine("  token_f7            '0x00DC0046' (F7 row sequestrada)");

    Console.WriteLine("  base_template       informativo");

    Console.WriteLine("  bosses              ['Dark Valefor', ...]");

    Console.WriteLine("  chunk2_slots        ['0x114E','0x114F','0xFFFF',...] (exatamente 8)");

    Console.WriteLine("  chunk3_mode         preserve | position-only | grow");

    Console.WriteLine("  chunk3_monster_live [[X,Y,Z], ...] (>= bosses count)");

}


