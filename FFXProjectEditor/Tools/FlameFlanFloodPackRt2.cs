using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>Grow/patch Flan Flood (Waterga clone 718/719) for FlameFlan — DLL/phyre only, no FFX.exe.</summary>
    internal static class FlameFlanFloodPackRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultDllRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\flameflan_flood_pack";
        const string BackupSuffix = ".backup_flameflan_flood";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool skipRecolor = args.Any(a => a.Equals("--skip-recolor", StringComparison.OrdinalIgnoreCase));
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string dllRoot = ArgValue(args, "--magic-root") ?? DefaultDllRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== Flan Flood PACK (FlameFlan Waterga clone) ===");
                Console.WriteLine($"game    : {gameRoot}");
                Console.WriteLine($"kernel  : {kernelPath}");
                Console.WriteLine($"deploy  : {deploy}");
                Console.WriteLine($"recolor : {(skipRecolor ? "skip" : "magma (FlameFlan)")}");
                Console.WriteLine($"clones  : magic_{MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim1Id:D4} / magic_{MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim2Id:D4}");
                Console.WriteLine($"donors  : magic_{MonsterMagicGrowWriter.FlameFlanVanillaWatergaAnim1Id:D4} / magic_{MonsterMagicGrowWriter.FlameFlanVanillaWatergaAnim2Id:D4}");

                if (!File.Exists(kernelPath))
                {
                    Console.WriteLine("FAIL: monmagic2.bin not found.");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                byte[] originalKernel = File.ReadAllBytes(kernelPath);
                MonsterMagicGrowResult grow = MonsterMagicGrowWriter.AppendFlameFlanFlood(originalKernel);
                int commandId = grow.NewId;
                byte[] patchedKernel = MonsterMagicGrowWriter.PatchFlameFlanFlood(
                    grow.GrownBytes,
                    commandId,
                    useDedicatedClones: true);

                string stagedKernel = Path.Combine(outputDir, "monmagic2.bin");
                File.WriteAllBytes(stagedKernel, patchedKernel);

                var entries = Ability_Command.ReadList(patchedKernel, hasExtraInfo: false);
                Ability_Command row = entries[commandId];
                string name = DecodeUs(row.NameScriptBytes);

                MagicEffectCloneDeployResult castClone = MagicEffectClonePipeline.DeployClone(
                    MonsterMagicGrowWriter.FlameFlanVanillaWatergaAnim1Id,
                    MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim1Id,
                    ps3Root,
                    gameRoot,
                    dllRoot);

                MagicEffectCloneDeployResult burstClone = MagicEffectClonePipeline.DeployClone(
                    MonsterMagicGrowWriter.FlameFlanVanillaWatergaAnim2Id,
                    MonsterMagicGrowWriter.FlameFlanCloneWatergaAnim2Id,
                    ps3Root,
                    gameRoot,
                    dllRoot);

                int castTex = skipRecolor
                    ? 0
                    : RecolorModsFolder(castClone.DeployedPs3Folder, Ps3MagicColorTransform.FlameFlanMagmaCast, BackupSuffix);
                int burstTex = skipRecolor
                    ? 0
                    : RecolorModsFolder(burstClone.DeployedPs3Folder, Ps3MagicColorTransform.FlameFlanMagmaBurst, BackupSuffix);

                string? deployedKernel = null;
                if (deploy)
                    deployedKernel = BackupAndCopy(stagedKernel, kernelPath, stamp);

                var payload = new
                {
                    commandId,
                    operand = $"0x{grow.Operand:X4}",
                    name,
                    row.AttackPower,
                    row.HitCount,
                    elements = row.ElementFlgs.ToString(),
                    anim1 = row.Anim1Id,
                    anim2 = row.Anim2Id,
                    castClone = new
                    {
                        castClone.SourceMagicId,
                        castClone.TargetMagicId,
                        castClone.DeployedPs3Folder,
                        castClone.DeployedTextureCount,
                        castClone.TargetDll,
                        recoloredTextures = castTex
                    },
                    burstClone = new
                    {
                        burstClone.SourceMagicId,
                        burstClone.TargetMagicId,
                        burstClone.DeployedPs3Folder,
                        burstClone.DeployedTextureCount,
                        burstClone.TargetDll,
                        recoloredTextures = burstTex
                    },
                    kernel = new
                    {
                        source = kernelPath,
                        staged = stagedKernel,
                        grown = grow.OriginalEntryCount != grow.NewEntryCount,
                        sizeBefore = originalKernel.Length,
                        sizeAfter = patchedKernel.Length,
                        deployed = deployedKernel
                    }
                };

                string jsonPath = Path.Combine(outputDir, "flameflan_flood_pack.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outputDir, "FLAN_FLOOD_RT2.md"), BuildMarkdown(payload, deploy, skipRecolor));

                Console.WriteLine($"command: #{commandId} {name} operand 0x{grow.Operand:X4}");
                Console.WriteLine($"power={row.AttackPower} hits={row.HitCount} elements={row.ElementFlgs}");
                Console.WriteLine($"anims: {row.Anim1Id}/{row.Anim2Id}");
                Console.WriteLine($"718 textures: {castClone.DeployedTextureCount} ({castTex} magma)");
                Console.WriteLine($"719 textures: {burstClone.DeployedTextureCount} ({burstTex} magma)");
                if (deploy)
                    Console.WriteLine("DEPLOYED kernel + clone assets (no FFX.exe)");
                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine("VERDICT: PASS — assign AI performCommand 0x60F9 (or grown operand); RT2 cast pending.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        internal static int RecolorModsFolder(string folder, Ps3MagicColorTransform transform, string backupSuffix)
        {
            if (!Directory.Exists(folder))
                return 0;

            bool isBurst = folder.Contains("magic_0719", StringComparison.OrdinalIgnoreCase);
            Ps3MagicColorTransform strong = isBurst
                ? Ps3MagicColorTransform.FlameFlanMagmaBurstStrong
                : Ps3MagicColorTransform.FlameFlanMagmaCastStrong;

            int count = 0;
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories))
            {
                string backup = texture + backupSuffix;
                if (!File.Exists(backup))
                    File.Copy(texture, backup, overwrite: false);
                File.Copy(backup, texture, overwrite: true);

                string name = Path.GetFileName(texture);
                Ps3MagicColorTransform picked = !isBurst && (name.Contains("_128_64", StringComparison.Ordinal) || name.Contains("_128_128", StringComparison.Ordinal))
                    ? Ps3MagicColorTransform.FlameFlanMagmaSpark
                    : name.Contains("_256_", StringComparison.Ordinal) || name.Contains("_512_", StringComparison.Ordinal) || name.Contains("_128_", StringComparison.Ordinal)
                        ? strong
                        : transform;
                Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, picked);
                count++;
            }

            return count;
        }

        static string BuildMarkdown(object payload, bool deployed, bool skipRecolor)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Flan Flood — FlameFlan Waterga LAB");
            sb.AppendLine();
            sb.AppendLine("## Visual recipe");
            sb.AppendLine("- **Anim1/Anim2** `magic_0718` / `magic_0719` — Waterga clone (`0096` / `0097`)");
            sb.AppendLine("- **No FFX.exe patch** — only `monmagic2.bin` row + PS3 phyre + `magic_####.dll` clones");
            sb.AppendLine($"- Default palette: **magma/coral** (FlameFlan){(skipRecolor ? " — skipped this run" : "")}");
            sb.AppendLine("- Restore: `--flameflan-flood-recolor --restore-backups --vanilla`");
            sb.AppendLine();
            sb.AppendLine("## AI operand");
            sb.AppendLine("- Row **#249** → `performCommand 0x60F9` (when grown after ThundaFira #248)");
            sb.AppendLine();
            sb.AppendLine(deployed ? "**Status:** deployed." : "**Status:** staged only — add `--deploy`.");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            return sb.ToString();
        }

        static string BackupAndCopy(string source, string target, string stamp)
        {
            string backup = target + ".backup_flameflan_flood_" + stamp;
            if (File.Exists(target))
                File.Copy(target, backup, overwrite: true);
            File.Copy(source, target, overwrite: true);
            return backup;
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

        static string DecodeUs(byte[] bytes) =>
            FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);
    }
}
