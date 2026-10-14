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
    /// <summary>Full Prism clone pipeline: ps3data mods + DLL + kernel 714/714.</summary>
    internal static class PrismFlareClonePackRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";
        const string DefaultPs3MagicRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultMagicDllRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\prism_flare_clone";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool prismTexture = args.Any(a => a.Equals("--prism-texture", StringComparison.OrdinalIgnoreCase));
                bool drasticColor = args.Any(a => a.Equals("--drastic-color", StringComparison.OrdinalIgnoreCase));
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3MagicRoot;
                string dllRoot = ArgValue(args, "--magic-root") ?? DefaultMagicDllRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                int sourceId = int.Parse(ArgValue(args, "--source") ?? "82");
                int cloneId = int.Parse(ArgValue(args, "--clone") ?? MonsterMagicGrowWriter.PrismFlareCloneAnim1Id.ToString());

                Console.WriteLine("=== Prism Flare CLONE PACK RT2 ===");
                Console.WriteLine($"game   : {gameRoot}");
                Console.WriteLine($"ps3    : {ps3Root}");
                Console.WriteLine($"dll    : {dllRoot}");
                Console.WriteLine($"kernel : {kernelPath}");
                Console.WriteLine($"clone  : magic_{sourceId:D4} -> magic_{cloneId:D4}");
                Console.WriteLine($"deploy : {deploy}");
                Console.WriteLine($"prism-texture : {prismTexture}");
                Console.WriteLine($"drastic-color : {drasticColor}");

                if (drasticColor && prismTexture)
                    Console.WriteLine("NOTE: --drastic-color wins over --prism-texture.");

                Directory.CreateDirectory(outputDir);
                MagicEffectCloneDeployResult clone = MagicEffectClonePipeline.DeployClone(
                    sourceId,
                    cloneId,
                    ps3Root,
                    gameRoot,
                    dllRoot,
                    applyPrismTextureRecolor: prismTexture && !drasticColor,
                    applyDrasticColorRecolor: drasticColor);

                if (!File.Exists(kernelPath))
                {
                    Console.WriteLine("FAIL: monmagic2.bin not found.");
                    return 2;
                }

                byte[] originalKernel = File.ReadAllBytes(kernelPath);
                byte[] patchedKernel = MonsterMagicGrowWriter.PatchPrismFlareV2(
                    originalKernel,
                    useDedicatedClones: true);
                string stagedKernel = Path.Combine(outputDir, "monmagic2.bin");
                File.WriteAllBytes(stagedKernel, patchedKernel);

                var entries = Ability_Command.ReadList(patchedKernel, hasExtraInfo: false);
                Ability_Command row = entries[MonsterMagicGrowWriter.PrismFlareCommandId];
                string name = FfxEncoding.DecodeScript(row.NameScriptBytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);

                string? deployedKernel = null;
                if (deploy)
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backup = kernelPath + $".backup_prism_clone_{stamp}";
                    File.Copy(kernelPath, backup, overwrite: true);
                    File.Copy(stagedKernel, kernelPath, overwrite: true);
                    deployedKernel = backup;
                }

                var family = MagicDllFamilyComparator.BuildSiblingReport(cloneId, dllRoot);
                var payload = new
                {
                    clone,
                    kernel = new
                    {
                        staged = stagedKernel,
                        sizeBefore = originalKernel.Length,
                        sizeAfter = patchedKernel.Length,
                        anim1 = row.Anim1Id,
                        anim2 = row.Anim2Id,
                        deployed = deployedKernel
                    },
                    row = new
                    {
                        id = MonsterMagicGrowWriter.PrismFlareCommandId,
                        name,
                        row.AttackPower,
                        row.HitCount
                    },
                    family = new
                    {
                        family.OverlaySignature,
                        spellCount = family.SpellsUsingThisDll.Count,
                        twinDllCount = family.TwinDllMatches.Count
                    }
                };

                string jsonPath = Path.Combine(outputDir, "prism_flare_clone_pack.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outputDir, "PRISM_FLARE_CLONE_RT2.md"), BuildMarkdown(payload, deploy));

                Console.WriteLine($"ps3 mods: {clone.DeployedPs3Folder} ({clone.DeployedTextureCount} textures)");
                Console.WriteLine($"dll     : {clone.TargetDll}");
                Console.WriteLine($"command : {MonsterMagicGrowWriter.PrismFlareCommandId} {name}");
                Console.WriteLine($"anims   : {row.Anim1Id}/{row.Anim2Id}");
                Console.WriteLine($"kernel  : {originalKernel.Length} -> {patchedKernel.Length}");
                if (clone.RecoloredTexturePath != null)
                    Console.WriteLine($"texture : {clone.RecoloredTextureCount} recolored (first: {clone.RecoloredTexturePath})");
                if (clone.DllColorPatchCount > 0)
                    Console.WriteLine($"dll     : {clone.DllColorPatchCount} vec4 magenta patches on {clone.TargetDll}");
                if (deploy)
                    Console.WriteLine("DEPLOYED kernel + clone assets");
                Console.WriteLine($"json: {jsonPath}");

                bool pass = clone.Pass && patchedKernel.Length == originalKernel.Length;
                Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string BuildMarkdown(object payload, bool deployed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Prism Flare clone pack — RT2");
            sb.AppendLine();
            sb.AppendLine("## Layers");
            sb.AppendLine("1. `data/mods/FFX_Data/GameData/PS3Data/magic/magic_0714` (ff10-file-loader)");
            sb.AppendLine("2. `magicFiles/FFX/magic_0714.dll`");
            sb.AppendLine("3. `monmagic2.bin` row #247 → Anim **714/714**");
            sb.AppendLine();
            sb.AppendLine(deployed ? "**Status:** deployed." : "**Status:** staged only — add `--deploy`.");
            sb.AppendLine();
            sb.AppendLine("## In-game");
            sb.AppendLine("- [ ] Prism Flare full VFX (cast + fire on target)");
            sb.AppendLine("- [ ] Multi-Fira vanilla unchanged");
            sb.AppendLine("- [ ] Optional prism tint if `--prism-texture`");
            sb.AppendLine("- [ ] **Drastic magenta** if `--drastic-color` (all textures + DLL vec4 — RT2 color-path probe)");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            return sb.ToString();
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
