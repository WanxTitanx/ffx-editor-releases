using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>Grow/patch ThundaFira in monmagic2 + deploy thunder/fire clone assets (716/717).</summary>
    internal static class ThundaFiraPackRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultDllRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\thundafira_pack";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool useSplitPhase = args.Any(a => a.Equals("--split", StringComparison.OrdinalIgnoreCase))
                    || args.Any(a => a.Equals("--mix-firaga", StringComparison.OrdinalIgnoreCase));
                bool useMultiFira = args.Any(a => a.Equals("--multi-fira", StringComparison.OrdinalIgnoreCase));
                bool useFiraga = args.Any(a => a.Equals("--firaga", StringComparison.OrdinalIgnoreCase))
                    || args.Any(a => a.Equals("--mix-firaga", StringComparison.OrdinalIgnoreCase));
                MonsterMagicGrowWriter.ThundaFiraExplosionDonor explosionDonor = useMultiFira
                    ? MonsterMagicGrowWriter.ThundaFiraExplosionDonor.MultiFira
                    : useFiraga
                        ? MonsterMagicGrowWriter.ThundaFiraExplosionDonor.Firaga
                        : MonsterMagicGrowWriter.ThundaFiraExplosionDonor.ThundagaPhase2;
                int phase2SourceId = MonsterMagicGrowWriter.ResolveThundaFiraExplosionSourceId(explosionDonor);
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string dllRoot = ArgValue(args, "--magic-root") ?? DefaultDllRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== ThundaFira PACK RT2 ===");
                Console.WriteLine($"game   : {gameRoot}");
                Console.WriteLine($"kernel : {kernelPath}");
                Console.WriteLine($"deploy : {deploy}");
                Console.WriteLine($"mode   : {(useSplitPhase ? $"split 716/{MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId:D4}" : "thundaga-only 716/716")}");
                if (useSplitPhase)
                {
                    string donorLabel = explosionDonor switch
                    {
                        MonsterMagicGrowWriter.ThundaFiraExplosionDonor.MultiFira => "Multi-Fira",
                        MonsterMagicGrowWriter.ThundaFiraExplosionDonor.Firaga => "Firaga",
                        _ => "Thundaga phase2"
                    };
                    Console.WriteLine($"recipe : Thundaga({MonsterMagicGrowWriter.ThundaFiraVanillaThunderAnimId}) Anim1 + {donorLabel}({phase2SourceId}) Anim2");
                }
                Console.WriteLine($"clones : magic_{MonsterMagicGrowWriter.ThundaFiraCloneThunderAnimId:D4} / magic_{MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId:D4}");

                if (!File.Exists(kernelPath))
                {
                    Console.WriteLine("FAIL: monmagic2.bin not found.");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                byte[] originalKernel = File.ReadAllBytes(kernelPath);
                MonsterMagicGrowResult grow = MonsterMagicGrowWriter.AppendThundaFira(originalKernel);
                byte[] grownKernel = grow.GrownBytes;
                int commandId = grow.NewId;
                byte[] patchedKernel = MonsterMagicGrowWriter.PatchThundaFira(
                    grownKernel,
                    commandId,
                    useDedicatedClones: true,
                    useSplitPhaseExplosion: useSplitPhase,
                    explosionDonor: explosionDonor);

                string stagedKernel = Path.Combine(outputDir, "monmagic2.bin");
                File.WriteAllBytes(stagedKernel, patchedKernel);

                var entries = Ability_Command.ReadList(patchedKernel, hasExtraInfo: false);
                Ability_Command row = entries[commandId];
                string name = DecodeUs(row.NameScriptBytes);

                MagicEffectCloneDeployResult thunderClone = MagicEffectClonePipeline.DeployClone(
                    MonsterMagicGrowWriter.ThundaFiraVanillaThunderAnimId,
                    MonsterMagicGrowWriter.ThundaFiraCloneThunderAnimId,
                    ps3Root,
                    gameRoot,
                    dllRoot);

                MagicEffectCloneDeployResult? fireClone = null;
                int fireTex = 0;
                if (useSplitPhase)
                {
                    fireClone = MagicEffectClonePipeline.DeployClone(
                        phase2SourceId,
                        MonsterMagicGrowWriter.ThundaFiraCloneFireAnimId,
                        ps3Root,
                        gameRoot,
                        dllRoot);
                    Ps3MagicColorTransform fireTransform = explosionDonor switch
                    {
                        MonsterMagicGrowWriter.ThundaFiraExplosionDonor.MultiFira => Ps3MagicColorTransform.ThundaFiraBlueBurst,
                        MonsterMagicGrowWriter.ThundaFiraExplosionDonor.Firaga => Ps3MagicColorTransform.ThundaFiraFiragaBurst,
                        _ => Ps3MagicColorTransform.Identity,
                    };
                    if (!fireTransform.IsIdentity)
                    {
                        fireTex = RecolorModsFolder(
                            fireClone.DeployedPs3Folder,
                            fireTransform,
                            ".backup_thundafira");
                    }
                }

                int thunderTex = RecolorModsFolder(
                    thunderClone.DeployedPs3Folder,
                    Ps3MagicColorTransform.Identity,
                    ".backup_thundafira");

                string? deployedKernel = null;
                if (deploy)
                {
                    deployedKernel = BackupAndCopy(stagedKernel, kernelPath, stamp);
                }

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
                    phase2SourceId,
                    thunderClone = new
                    {
                        thunderClone.SourceMagicId,
                        thunderClone.TargetMagicId,
                        thunderClone.DeployedPs3Folder,
                        thunderClone.DeployedTextureCount,
                        thunderClone.TargetDll,
                        recoloredTextures = thunderTex
                    },
                    fireClone = fireClone == null ? null : new
                    {
                        fireClone.SourceMagicId,
                        fireClone.TargetMagicId,
                        fireClone.DeployedPs3Folder,
                        fireClone.DeployedTextureCount,
                        fireClone.TargetDll,
                        recoloredTextures = fireTex
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

                string jsonPath = Path.Combine(outputDir, "thundafira_pack.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outputDir, "THUNDAFIRA_RT2.md"), BuildMarkdown(payload, deploy));

                Console.WriteLine($"command: #{commandId} {name} operand 0x{grow.Operand:X4}");
                Console.WriteLine($"power={row.AttackPower} hits={row.HitCount} elements={row.ElementFlgs}");
                Console.WriteLine($"anims: {row.Anim1Id}/{row.Anim2Id}");
                Console.WriteLine($"0716 textures: {thunderClone.DeployedTextureCount} (vanilla clone)");
                if (useSplitPhase && fireClone != null)
                    Console.WriteLine($"0717 textures: {fireClone.DeployedTextureCount} ({fireTex} recolored)");
                Console.WriteLine($"kernel: {originalKernel.Length} -> {patchedKernel.Length}");
                if (deploy)
                    Console.WriteLine("DEPLOYED kernel + clone assets");
                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine("VERDICT: PASS — assign AI performCommand 0x60F8 (or grown operand) and cast in-game.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int RecolorModsFolder(string folder, Ps3MagicColorTransform transform, string backupSuffix)
        {
            if (!Directory.Exists(folder))
                return 0;

            int count = 0;
            foreach (string texture in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories))
            {
                string backup = texture + backupSuffix;
                if (!File.Exists(backup))
                    File.Copy(texture, backup, overwrite: false);
                Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, transform);
                count++;
            }

            return count;
        }

        static string BuildMarkdown(object payload, bool deployed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ThundaFira — RT2 pack");
            sb.AppendLine();
            sb.AppendLine("## Visual recipe (default)");
            sb.AppendLine("- **Anim1/Anim2** `magic_0716/0716` — pure Thundaga clone (`0094`); tune explosion phyre only via `--thundafira-recolor --green`");
            sb.AppendLine("- **Split phase** (opt-in `--split`): Anim2 `magic_0717` from Firaga `0090` or `--multi-fira` → `0082`");
            sb.AppendLine("- Textures recolored via `--thundafira-recolor` (`--blue` RT2 cyan, `--lava` crimson; backups per texture)");
            sb.AppendLine();
            sb.AppendLine("## AI operand");
            sb.AppendLine("- Row **#248** → `performCommand 0x60F8` (when grown after Prism #247)");
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
            string backup = target + ".backup_thundafira_" + stamp;
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
