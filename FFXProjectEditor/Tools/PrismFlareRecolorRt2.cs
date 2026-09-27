using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>Recolor Prism Flare clone textures in Steam mods (PS3 Magic path) + optional kernel deploy.</summary>
    internal static class PrismFlareRecolorRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultDllRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\prism_flare_recolor";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool drastic = args.Any(a => a.Equals("--drastic", StringComparison.OrdinalIgnoreCase));
                bool strong = args.Any(a => a.Equals("--strong", StringComparison.OrdinalIgnoreCase));
                bool ensureClone = args.Any(a => a.Equals("--ensure-clone", StringComparison.OrdinalIgnoreCase));
                bool restoreBackups = args.Any(a => a.Equals("--restore-backups", StringComparison.OrdinalIgnoreCase));
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string dllRoot = ArgValue(args, "--magic-root") ?? DefaultDllRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Ps3MagicColorTransform castTransform = drastic
                    ? Ps3MagicColorTransform.DrasticMagenta
                    : Ps3MagicColorTransform.PrismViolet;
                Ps3MagicColorTransform fireTransform = drastic
                    ? Ps3MagicColorTransform.DrasticMagenta
                    : strong
                        ? Ps3MagicColorTransform.PrismVioletStrong
                        : Ps3MagicColorTransform.PrismViolet;

                Console.WriteLine("=== Prism Flare RECOLOR (PS3 textures) ===");
                Console.WriteLine($"game    : {gameRoot}");
                Console.WriteLine($"kernel  : {kernelPath}");
                Console.WriteLine($"cast    : {(drastic ? "drastic" : "prism violet")}");
                Console.WriteLine($"fire    : {(drastic ? "drastic" : strong ? "prism violet strong" : "prism violet")}");
                Console.WriteLine($"deploy  : {deploy}");
                Console.WriteLine($"restore : {restoreBackups}");
                Console.WriteLine($"ensure-clone: {ensureClone}");
                Console.WriteLine($"anims   : {MonsterMagicGrowWriter.PrismFlareCloneAnim1Id}/{MonsterMagicGrowWriter.PrismFlareCloneAnim2Id}");

                Directory.CreateDirectory(outputDir);
                var folderResults = new List<(int magicId, string folder, int count, List<string> files)>();
                int totalRecolored = 0;

                if (ensureClone)
                {
                    EnsureClone(82, MonsterMagicGrowWriter.PrismFlareCloneAnim1Id, ps3Root, gameRoot, dllRoot);
                    EnsureClone(86, 715, ps3Root, gameRoot, dllRoot);
                }

                foreach (int magicId in new[] { MonsterMagicGrowWriter.PrismFlareCloneAnim1Id, MonsterMagicGrowWriter.PrismFlareCloneAnim2Id })
                {
                    string modsFolder = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId);
                    if (!Directory.Exists(modsFolder))
                    {
                        Console.WriteLine($"skip magic_{magicId:D4}: mods folder missing ({modsFolder})");
                        continue;
                    }

                    string backupSuffix = drastic ? ".backup_drastic" : ".backup_prism";
                    if (restoreBackups)
                        RestoreBackups(modsFolder, backupSuffix);

                    bool isFirePhase = magicId == MonsterMagicGrowWriter.PrismFlareCloneAnim2Id;
                    var recolored = RecolorFolder(
                        modsFolder,
                        isFirePhase ? fireTransform : castTransform,
                        fireTransform,
                        backupSuffix,
                        aggressiveFireAtlases: strong || isFirePhase);
                    folderResults.Add((magicId, modsFolder, recolored.Count, recolored));
                    totalRecolored += recolored.Count;
                    Console.WriteLine($"magic_{magicId:D4}: recolored {recolored.Count} texture(s) ({(isFirePhase ? "fire phase" : "cast phase")})");
                }

                string? deployedKernel = null;
                if (deploy && File.Exists(kernelPath))
                {
                    byte[] original = File.ReadAllBytes(kernelPath);
                    byte[] patched = MonsterMagicGrowWriter.PatchPrismFlareV2(original, useDedicatedClones: true);
                    string staged = Path.Combine(outputDir, "monmagic2.bin");
                    File.WriteAllBytes(staged, patched);
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backup = kernelPath + ".backup_prism_recolor_" + stamp;
                    File.Copy(kernelPath, backup, overwrite: true);
                    File.Copy(staged, kernelPath, overwrite: true);
                    deployedKernel = backup;
                    Console.WriteLine($"kernel: deployed Anim {MonsterMagicGrowWriter.PrismFlareCloneAnim1Id}/{MonsterMagicGrowWriter.PrismFlareCloneAnim2Id} (backup {Path.GetFileName(backup)})");
                }

                var payload = new
                {
                    transform = drastic ? "drastic" : strong ? "prism_violet_strong_fire" : "prism_violet",
                    anims = $"{MonsterMagicGrowWriter.PrismFlareCloneAnim1Id}/{MonsterMagicGrowWriter.PrismFlareCloneAnim2Id}",
                    folders = folderResults.Select(f => new { magicId = f.magicId, folder = f.folder, count = f.count, files = f.files }),
                    kernel = deployedKernel,
                    deploy
                };
                string jsonPath = Path.Combine(outputDir, "prism_flare_recolor.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

                bool pass = totalRecolored > 0;
                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine(pass
                    ? "VERDICT: PASS — launch game, cast Prism Flare (0x60F7 / row #247)."
                    : "VERDICT: FAIL — no textures recolored. Run with --ensure-clone first.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static void EnsureClone(int sourceId, int targetId, string ps3Root, string gameRoot, string dllRoot)
        {
            string targetMods = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, targetId);
            if (Directory.Exists(targetMods) && Directory.EnumerateFiles(targetMods, "*.dds.phyre", SearchOption.AllDirectories).Any())
            {
                Console.WriteLine($"clone magic_{targetId:D4}: mods already present");
                return;
            }

            MagicEffectCloneDeployResult result = MagicEffectClonePipeline.DeployClone(
                sourceId,
                targetId,
                ps3Root,
                gameRoot,
                dllRoot);
            Console.WriteLine($"clone magic_{sourceId:D4} -> magic_{targetId:D4}: {result.DeployedTextureCount} textures, dll={(result.TargetDllExists ? "ok" : "missing")}");
        }

        static void RestoreBackups(string folder, string backupSuffix)
        {
            int restored = 0;
            foreach (string backup in Directory.EnumerateFiles(folder, "*" + backupSuffix, SearchOption.AllDirectories))
            {
                string target = backup[..^backupSuffix.Length];
                if (!target.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(backup, target, overwrite: true);
                restored++;
            }

            if (restored > 0)
                Console.WriteLine($"restored {restored} texture(s) from {backupSuffix} in {folder}");
        }

        static List<string> RecolorFolder(
            string folder,
            Ps3MagicColorTransform defaultTransform,
            Ps3MagicColorTransform fireAtlasTransform,
            string backupSuffix,
            bool aggressiveFireAtlases)
        {
            var recolored = new List<string>();
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string backup = texture + backupSuffix;
                if (!File.Exists(backup))
                    File.Copy(texture, backup, overwrite: false);

                Ps3MagicColorTransform transform = aggressiveFireAtlases && IsFireAtlasTexture(texture)
                    ? fireAtlasTransform
                    : defaultTransform;
                Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);
                recolored.Add(texture);
            }

            return recolored;
        }

        static bool IsFireAtlasTexture(string path)
        {
            string name = Path.GetFileName(path);
            return name.Contains("_256_512", StringComparison.Ordinal)
                || name.Contains("_512_256", StringComparison.Ordinal)
                || name.Contains("_256_256", StringComparison.Ordinal)
                || name.Contains("_256_128", StringComparison.Ordinal);
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
