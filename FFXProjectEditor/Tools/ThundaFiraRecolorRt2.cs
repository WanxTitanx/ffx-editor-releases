using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Recolor deployed PS3 textures for ThundaFira clones magic_0716 + magic_0717.
    /// Default <c>--red</c> = texture-only safe path (no DLL patch — bulk vec4 broke RT2).
    /// </summary>
    internal static class ThundaFiraRecolorRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultOutputDir = @"work\thundafira_recolor";

        const string VanillaBackupSuffix = ".backup_thundafira";
        const string BlueBackupSuffix = ".backup_thundafira_blue";
        const string LavaBackupSuffix = ".backup_thundafira_lava";
        const string RedSafeBackupSuffix = ".backup_thundafira_redsafe";
        const string GreenBackupSuffix = ".backup_thundafira_green";
        const string OrangeBoltBackupSuffix = ".backup_thundafira_bolts_orange";
        const string DllBackupSuffix = ".backup_thundafira_dll";

        public static int Run(string[] args)
        {
            try
            {
                bool restore = args.Any(a => a.Equals("--restore-backups", StringComparison.OrdinalIgnoreCase));
                string palette = ResolvePalette(args);
                bool dllTint = args.Any(a => a.Equals("--dll-tint", StringComparison.OrdinalIgnoreCase));
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== ThundaFira RECOLOR (PS3 textures) ===");
                Console.WriteLine($"game    : {gameRoot}");
                Console.WriteLine($"palette : {palette}");
                bool dllBoltTint = false; // RT2 failed: Family D PPP — vec4 patch breaks bolts (2D/disconnected)
                Console.WriteLine($"dll     : skip (Thundaga=Family D PPP — vec4 patch RETIRED after RT2)");
                Console.WriteLine($"restore : {restore}");
                Console.WriteLine($"clones  : magic_{MonsterMagicGrowWriter.ThundaFiraCloneThunderAnimId:D4} + magic_{MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId:D4}");

                Directory.CreateDirectory(outputDir);

                if (restore)
                {
                    string suffix = palette switch
                    {
                        "blue" => BlueBackupSuffix,
                        "lava" or "lava-aggressive" => LavaBackupSuffix,
                        "red-safe" => RedSafeBackupSuffix,
                        "green" => GreenBackupSuffix,
                        "bolts-orange" or "bolts-red" => OrangeBoltBackupSuffix,
                        "vanilla" => VanillaBackupSuffix,
                        _ => BlueBackupSuffix
                    };

                    int restored = 0;
                    foreach (int magicId in palette == "vanilla" ? CloneMagicIds() : ThundaFiraRecolorMagicIds(palette))
                    {
                        string folder = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId);
                        restored += RestoreFolder(folder, suffix);
                    }

                    if (palette is "blue" or "vanilla" or "red-safe" or "green" or "bolts-orange" or "bolts-red")
                        restored += RestoreThunderDll(gameRoot) ? 1 : 0;

                    Console.WriteLine(restored > 0
                        ? $"VERDICT: PASS — restored {restored} asset(s) from backups."
                        : $"VERDICT: FAIL — no backups found.");
                    return restored > 0 ? 0 : 1;
                }

                RestoreThunderDll(gameRoot);

                (Ps3MagicColorTransform phase1, Ps3MagicColorTransform phase2, Ps3MagicColorTransform phase1Strong, Ps3MagicColorTransform phase2Strong, bool strongOnAll716) =
                    PaletteTransforms(palette);

                var folderResults = new List<object>();
                int totalRecolored = 0;

                foreach (int magicId in ThundaFiraRecolorMagicIds(palette))
                {
                    string folder = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId);
                    if (!Directory.Exists(folder))
                    {
                        Console.WriteLine($"skip magic_{magicId:D4}: mods folder missing ({folder})");
                        continue;
                    }

                    bool isBurstPhase = magicId == MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId;
                    bool explosionOnly = palette is "green" or "red-safe" or "blue";
                    bool boltsOnly = palette is "bolts-orange" or "bolts-red" or "phyre-anim1";
                    ThundagaPhyreClassifier.SheetRole? sheetRole = null;
                    if (explosionOnly && !isBurstPhase)
                        sheetRole = ThundagaPhyreClassifier.SheetRole.Explosion;
                    else if (boltsOnly && !isBurstPhase)
                        sheetRole = palette == "phyre-anim1"
                            ? ThundagaPhyreClassifier.SheetRole.Anim1Only
                            : ThundagaPhyreClassifier.SheetRole.Bolt;

                    var recolored = RecolorFolder(
                        folder,
                        isBurstPhase ? phase2 : phase1,
                        isBurstPhase ? phase2Strong : phase1Strong,
                        sourceSuffix: VanillaBackupSuffix,
                        paletteBackupSuffix: palette switch
                        {
                            "red-safe" => RedSafeBackupSuffix,
                            "green" => GreenBackupSuffix,
                            "bolts-orange" or "bolts-red" => OrangeBoltBackupSuffix,
                            "lava-aggressive" => LavaBackupSuffix,
                            _ => BlueBackupSuffix
                        },
                        strongOnAllTextures: (strongOnAll716 || palette is "red-safe" or "bolts-orange" or "bolts-red") && !isBurstPhase,
                        onlyThundagaRole: sheetRole);

                    totalRecolored += recolored.Count;
                    folderResults.Add(new { magicId, phase = isBurstPhase ? "717" : "716", count = recolored.Count });
                    string roleNote = sheetRole switch
                    {
                        ThundagaPhyreClassifier.SheetRole.Explosion => ", explosion sheets only",
                        ThundagaPhyreClassifier.SheetRole.Anim1Only => ", Anim1-only sheet (128_64)",
                        ThundagaPhyreClassifier.SheetRole.GroundImpactFlash => ", ground impact flash (128_128)",
                        ThundagaPhyreClassifier.SheetRole.Bolt => ", KeThRes bolt sheets (13568/13440)",
                        _ => string.Empty
                    };
                    Console.WriteLine($"magic_{magicId:D4}: {recolored.Count} phyre touched ({palette}{roleNote})");
                }

                bool pass = totalRecolored > 0;
                if (palette is "bolts-orange" or "bolts-red" or "bolts-magenta" or "phyre-anim1")
                {
                    Console.WriteLine(palette == "phyre-anim1"
                        ? "NOTE: phyre Anim1-only (_128_64) on magic_0716 — RT2: does NOT change bolt rays."
                        : "NOTE: --bolts-* targets KeThRes bolt sheets 13568+13440 on 0716 (not _128_128 flash).");
                }
                else if (dllTint)
                {
                    Console.WriteLine("WARN: --dll-tint can crash the game or break VFX; RT2 showed 2D lance + hard crash.");
                    if (palette == "red-safe")
                        pass = RecolorThunderDllSafe(gameRoot) || pass;
                    else if (palette == "lava-aggressive")
                        pass = RecolorThunderDllExperimental(gameRoot) || pass;
                }

                string jsonPath = Path.Combine(outputDir, "thundafira_recolor.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
                {
                    palette,
                    dllTint,
                    folders = folderResults,
                    note = "Burst: --green on 0717 | Ground flash only: --bolts-orange (NOT rays) | Restore: --restore-backups --vanilla"
                }, new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine(pass
                    ? "VERDICT: PASS — cast ThundaFira (0x60F8)."
                    : "VERDICT: FAIL — run --thundafira-pack --deploy first.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static IEnumerable<int> ThundaFiraRecolorMagicIds(string palette)
        {
            yield return MonsterMagicGrowWriter.ThundaFiraCloneThunderAnimId;
            if (palette == "split" || palette == "lava-aggressive")
                yield return MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId;
        }

        static IEnumerable<int> CloneMagicIds() => ThundaFiraRecolorMagicIds("split");

        static string ResolvePalette(string[] args)
        {
            if (args.Any(a => a.Equals("--bolts-orange", StringComparison.OrdinalIgnoreCase))
                || args.Any(a => a.Equals("--orange-bolts", StringComparison.OrdinalIgnoreCase)))
                return "bolts-orange";
            if (args.Any(a => a.Equals("--phyre-anim1", StringComparison.OrdinalIgnoreCase)))
                return "phyre-anim1";
            if (args.Any(a => a.Equals("--bolts-red", StringComparison.OrdinalIgnoreCase)))
                return "bolts-red";
            if (args.Any(a => a.Equals("--split", StringComparison.OrdinalIgnoreCase)))
                return "split";
            if (args.Any(a => a.Equals("--green", StringComparison.OrdinalIgnoreCase)))
                return "green";
            if (args.Any(a => a.Equals("--blue", StringComparison.OrdinalIgnoreCase)))
                return "blue";
            if (args.Any(a => a.Equals("--vanilla", StringComparison.OrdinalIgnoreCase)))
                return "vanilla";
            if (args.Any(a => a.Equals("--lava-aggressive", StringComparison.OrdinalIgnoreCase)))
                return "lava-aggressive";
            if (args.Any(a => a.Equals("--lava", StringComparison.OrdinalIgnoreCase)))
                return "lava-aggressive";
            if (args.Any(a => a.Equals("--red", StringComparison.OrdinalIgnoreCase)))
                return "red-safe";
            return "red-safe";
        }

        static (Ps3MagicColorTransform p1, Ps3MagicColorTransform p2, Ps3MagicColorTransform s1, Ps3MagicColorTransform s2, bool all716) PaletteTransforms(string palette) =>
            palette switch
            {
                "blue" => (
                    Ps3MagicColorTransform.ThundaFiraBlueBurst,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraBlueBurst,
                    Ps3MagicColorTransform.Identity,
                    true),
                "green" => (
                    Ps3MagicColorTransform.ThundaFiraGreenBurst,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraGreenBurstStrong,
                    Ps3MagicColorTransform.Identity,
                    true),
                "phyre-anim1" => (
                    Ps3MagicColorTransform.ThundaFiraOrangeBoltStrong,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraOrangeBoltStrong,
                    Ps3MagicColorTransform.Identity,
                    true),
                "bolts-orange" => (
                    Ps3MagicColorTransform.ThundaFiraOrangeBolt,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraOrangeBoltStrong,
                    Ps3MagicColorTransform.Identity,
                    true),
                "bolts-red" => (
                    Ps3MagicColorTransform.ThundaFiraRedSafeThunder,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraRedSafeThunderStrong,
                    Ps3MagicColorTransform.Identity,
                    true),
                "vanilla" => (
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.Identity,
                    false),
                "lava-aggressive" => (
                    Ps3MagicColorTransform.ThundaFiraLavaThunder,
                    Ps3MagicColorTransform.ThundaFiraLavaBurst,
                    Ps3MagicColorTransform.ThundaFiraLavaThunderStrong,
                    Ps3MagicColorTransform.ThundaFiraLavaBurstStrong,
                    true),
                _ => (
                    Ps3MagicColorTransform.ThundaFiraRedSafeBurst,
                    Ps3MagicColorTransform.Identity,
                    Ps3MagicColorTransform.ThundaFiraRedSafeBurstStrong,
                    Ps3MagicColorTransform.Identity,
                    true),
            };

        static void SnapshotFolder(string folder, string suffix, string label)
        {
            int snap = 0;
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories))
            {
                string backup = texture + suffix;
                if (!File.Exists(backup))
                {
                    File.Copy(texture, backup, overwrite: false);
                    snap++;
                }
            }

            if (snap > 0)
                Console.WriteLine($"snapshot: saved {snap} {label} texture(s) in {Path.GetFileName(folder)}");
        }

        static int RestoreFolder(string folder, string backupSuffix)
        {
            if (!Directory.Exists(folder))
                return 0;

            int restored = 0;
            foreach (string backup in Directory.EnumerateFiles(folder, "*" + backupSuffix, SearchOption.AllDirectories))
            {
                string target = backup[..^backupSuffix.Length];
                if (!target.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(backup, target, overwrite: true);
                restored++;
            }

            return restored;
        }

        static bool RestoreThunderDll(string gameRoot)
        {
            string dll = DllPath(gameRoot);
            string backup = dll + DllBackupSuffix;
            if (!File.Exists(backup))
                return false;
            File.Copy(backup, dll, overwrite: true);
            Console.WriteLine("magic_0716.dll: restored from vanilla backup");
            return true;
        }

        static bool RecolorThunderDllAnim1Drastic(string gameRoot, int maxPatches = 2)
        {
            string dll = DllPath(gameRoot);
            if (!File.Exists(dll))
                return false;

            RestoreThunderDll(gameRoot);
            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.ApplyThundaFiraAnim1DrawDrastic(dll, dll, maxPatches);
            Console.WriteLine($"magic_0716.dll: {result.PatchCount} white vec4 → magenta (cap {maxPatches})");
            foreach (MagicDllBytePatch p in result.Patches)
                Console.WriteLine($"  @0x{p.FileOffset:X} {p.Note}");
            return result.PatchCount > 0;
        }

        static bool RecolorThunderDllBoltOrange(string gameRoot, int maxPatches = 2)
        {
            string dll = DllPath(gameRoot);
            if (!File.Exists(dll))
                return false;

            RestoreThunderDll(gameRoot);
            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.ApplyThundaFiraBoltOrangeTint(dll, dll, maxPatches);
            Console.WriteLine($"magic_0716.dll: {result.PatchCount} white vec4 → orange (cap {maxPatches})");
            foreach (MagicDllBytePatch p in result.Patches)
                Console.WriteLine($"  @0x{p.FileOffset:X} {p.Note}");
            return result.PatchCount > 0;
        }

        static bool RecolorThunderDllBoltCrimson(string gameRoot, int maxPatches = 2)
        {
            string dll = DllPath(gameRoot);
            if (!File.Exists(dll))
                return false;

            RestoreThunderDll(gameRoot);
            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.ApplyThundaFiraBoltTint(dll, dll, maxPatches);
            Console.WriteLine($"magic_0716.dll: {result.PatchCount} bolt vec4 → crimson (cap {maxPatches})");
            return result.PatchCount > 0;
        }

        static bool RecolorThunderDllSafe(string gameRoot)
        {
            string dll = DllPath(gameRoot);
            if (!File.Exists(dll))
                return false;

            RestoreThunderDll(gameRoot);
            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.ApplyThundaFiraBoltTint(dll, dll, maxPatches: 4);
            Console.WriteLine($"magic_0716.dll: {result.PatchCount} blue vec4 → crimson (safe cap 4)");
            return result.PatchCount > 0;
        }

        static bool RecolorThunderDllExperimental(string gameRoot)
        {
            string dll = DllPath(gameRoot);
            if (!File.Exists(dll))
                return false;

            RestoreThunderDll(gameRoot);
            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.Apply(
                dll, dll, r: 1.0f, g: 0.12f, b: 0.04f, a: 1.0f, maxPatches: 8);
            Console.WriteLine($"magic_0716.dll: {result.PatchCount} vec4 patch(es) (experimental)");
            return result.PatchCount > 0;
        }

        static string DllPath(string gameRoot) =>
            Path.Combine(gameRoot, "magicFiles", "FFX", "magic_0716.dll");

        static List<string> RecolorFolder(
            string folder,
            Ps3MagicColorTransform defaultTransform,
            Ps3MagicColorTransform strongAtlasTransform,
            string sourceSuffix,
            string paletteBackupSuffix,
            bool strongOnAllTextures = false,
            ThundagaPhyreClassifier.SheetRole? onlyThundagaRole = null)
        {
            var recolored = new List<string>();
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string vanillaBackup = texture + sourceSuffix;
                if (!File.Exists(vanillaBackup))
                    File.Copy(texture, vanillaBackup, overwrite: false);

                string paletteBackup = texture + paletteBackupSuffix;
                if (!File.Exists(paletteBackup))
                    File.Copy(texture, paletteBackup, overwrite: false);

                File.Copy(vanillaBackup, texture, overwrite: true);

                if (onlyThundagaRole.HasValue)
                {
                    ThundagaPhyreClassifier.SheetRole role = ThundagaPhyreClassifier.ClassifyFileName(Path.GetFileName(texture));
                    if (role != onlyThundagaRole.Value)
                    {
                        recolored.Add(texture);
                        continue;
                    }
                }

                Ps3MagicColorTransform transform = strongOnAllTextures || IsLargeColorAtlas(texture)
                    ? strongAtlasTransform
                    : defaultTransform;

                if (!transform.IsIdentity)
                    Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);

                recolored.Add(texture);
            }

            return recolored;
        }

        static bool IsLargeColorAtlas(string path)
        {
            string name = Path.GetFileName(path);
            return name.Contains("_256_512", StringComparison.Ordinal)
                || name.Contains("_512_256", StringComparison.Ordinal)
                || name.Contains("_256_256", StringComparison.Ordinal)
                || name.Contains("_256_128", StringComparison.Ordinal)
                || name.Contains("_128_256", StringComparison.Ordinal);
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
