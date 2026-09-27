using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>Recolor Flan Flood clone phyre (`0718`/`0719`) — magma palette. DLL timing patch aposentado.</summary>
    internal static class FlameFlanFloodRecolorRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultOutputDir = @"work\flameflan_flood_recolor";

        const string VanillaBackupSuffix = ".backup_flameflan_flood";
        const string MagmaBackupSuffix = ".backup_flameflan_flood_magma";

        public static int Run(string[] args)
        {
            try
            {
                bool restore = args.Any(a => a.Equals("--restore-backups", StringComparison.OrdinalIgnoreCase));
                bool vanilla = args.Any(a => a.Equals("--vanilla", StringComparison.OrdinalIgnoreCase));
                bool restorePossibleTimingDll = args.Any(a =>
                    a.Equals("--restore-dll", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--restore-possible-timing-dll", StringComparison.OrdinalIgnoreCase));
                bool gentle = args.Any(a => a.Equals("--gentle", StringComparison.OrdinalIgnoreCase));
                bool patchPossibleTimingDll = args.Any(a =>
                    a.Equals("--dll-possible-timing", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--dll-tint", StringComparison.OrdinalIgnoreCase));
                string palette = vanilla ? "vanilla" : gentle ? "magma-gentle" : "magma-aggressive";
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== Flan Flood RECOLOR (PS3 textures) ===");
                Console.WriteLine($"game    : {gameRoot}");
                Console.WriteLine($"palette : {palette}");
                Console.WriteLine($"restore : {restore}");
                Console.WriteLine($"dll     : {(restorePossibleTimingDll ? "restore magic_0718.dll (possible-timing backup)" : patchPossibleTimingDll ? "DANGER: patch possible-timing vec4 (RT2 fail — opt-in)" : "skip (phyre only)")}");
                Console.WriteLine($"clones  : magic_{MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim1Id:D4} + magic_{MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim2Id:D4}");

                Directory.CreateDirectory(outputDir);

                if (restorePossibleTimingDll && !restore)
                {
                    bool ok = FlanFloodDllPossibleTimingPatch.RestoreCastDllFromPossibleTimingBackup(gameRoot);
                    Console.WriteLine(ok
                        ? $"VERDICT: PASS — restored magic_0718.dll from {FlanFloodDllPossibleTimingPatch.CastDllPossibleTimingBackupSuffix}."
                        : "VERDICT: FAIL — no DLL backup found.");
                    return ok ? 0 : 1;
                }

                if (restore)
                {
                    string suffix = vanilla ? VanillaBackupSuffix : MagmaBackupSuffix;
                    int restored = 0;
                    foreach (int magicId in CloneMagicIds())
                        restored += RestoreFolder(MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId), suffix);

                    bool dllRestored = (restorePossibleTimingDll || vanilla)
                        && FlanFloodDllPossibleTimingPatch.RestoreCastDllFromPossibleTimingBackup(gameRoot);
                    if (dllRestored)
                        Console.WriteLine($"dll: restored magic_0718.dll from {FlanFloodDllPossibleTimingPatch.CastDllPossibleTimingBackupSuffix}");

                    Console.WriteLine(restored > 0 || dllRestored
                        ? $"VERDICT: PASS — restored {restored} texture(s){(dllRestored ? " + possible-timing DLL" : "")}."
                        : "VERDICT: FAIL — no backups found.");
                    return restored > 0 || dllRestored ? 0 : 1;
                }

                var folderResults = new List<object>();
                int totalRecolored = 0;

                foreach (int magicId in CloneMagicIds())
                {
                    string folder = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId);
                    if (!Directory.Exists(folder))
                    {
                        Console.WriteLine($"skip magic_{magicId:D4}: mods folder missing ({folder})");
                        continue;
                    }

                    bool isBurst = magicId == MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim2Id;
                    var transform = isBurst
                        ? Ps3MagicColorTransform.FlameFlanMagmaBurst
                        : Ps3MagicColorTransform.FlameFlanMagmaCast;
                    var strong = isBurst
                        ? Ps3MagicColorTransform.FlameFlanMagmaBurstStrong
                        : Ps3MagicColorTransform.FlameFlanMagmaCastStrong;

                    int count = RecolorFolder(folder, isBurst, transform, strong, VanillaBackupSuffix, MagmaBackupSuffix, aggressive: !gentle);
                    totalRecolored += count;
                    folderResults.Add(new { magicId, phase = isBurst ? "719" : "718", count });
                    Console.WriteLine($"magic_{magicId:D4}: {count} phyre touched (magma)");
                }

                int possibleTimingPatches = 0;
                if (patchPossibleTimingDll)
                {
                    possibleTimingPatches = FlanFloodDllPossibleTimingPatch.TryPatchCastDllPossibleTimingVec4(gameRoot);
                    Console.WriteLine($"magic_0718.dll: {possibleTimingPatches} possible-timing vec4 patched (RT2: advances damage — do not ship)");
                }

                string jsonPath = Path.Combine(outputDir, "flameflan_flood_recolor.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
                {
                    palette,
                    patchPossibleTimingDll,
                    possibleTimingPatches,
                    folders = folderResults,
                    note = "Phyre magma only by default. DLL: FlanFloodDllPossibleTimingPatch (aposentado)."
                }, new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine(totalRecolored > 0 || possibleTimingPatches > 0
                    ? "VERDICT: PASS — relaunch game and cast Flan Flood (0x60F9)."
                    : "VERDICT: FAIL — run --flameflan-flood-pack --deploy first.");
                return totalRecolored > 0 || possibleTimingPatches > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static IEnumerable<int> CloneMagicIds()
        {
            yield return MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim1Id;
            yield return MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim2Id;
        }

        static int RecolorFolder(
            string folder,
            bool isBurstPhase,
            Ps3MagicColorTransform normal,
            Ps3MagicColorTransform strong,
            string vanillaBackupSuffix,
            string paletteBackupSuffix,
            bool aggressive = true)
        {
            int count = 0;
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories))
            {
                string vanillaBackup = texture + vanillaBackupSuffix;
                if (!File.Exists(vanillaBackup))
                    File.Copy(texture, vanillaBackup, overwrite: false);

                string paletteBackup = texture + paletteBackupSuffix;
                if (!File.Exists(paletteBackup))
                    File.Copy(texture, paletteBackup, overwrite: false);

                File.Copy(vanillaBackup, texture, overwrite: true);

                Ps3MagicColorTransform transform = PickTransform(texture, isBurstPhase, normal, strong, aggressive);
                Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);
                count++;
            }

            return count;
        }

        static Ps3MagicColorTransform PickTransform(
            string texturePath,
            bool isBurstPhase,
            Ps3MagicColorTransform normal,
            Ps3MagicColorTransform strong,
            bool aggressive)
        {
            string name = Path.GetFileName(texturePath);
            if (!isBurstPhase && IsCastOpenerSheet(name))
                return Ps3MagicColorTransform.FlameFlanMagmaSpark;

            if (aggressive)
                return strong;

            bool useStrong = name.Contains("_256_", StringComparison.Ordinal)
                || name.Contains("_512_", StringComparison.Ordinal)
                || name.Contains("_128_", StringComparison.Ordinal);
            return useStrong ? strong : normal;
        }

        static bool IsCastOpenerSheet(string fileName) =>
            fileName.Contains("_128_64", StringComparison.Ordinal)
            || fileName.Contains("_128_128", StringComparison.Ordinal);

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
