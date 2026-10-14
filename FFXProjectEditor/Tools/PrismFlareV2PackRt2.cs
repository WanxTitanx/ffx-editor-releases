using FFXProjectEditor.FfxLib.Ability;

using FFXProjectEditor.FfxLib.Ps3;

using FFXProjectEditor.Utils.Encoding;

using System;

using System.IO;

using System.Linq;

using System.Security.Cryptography;

using System.Text;

using System.Text.Json;



namespace FFXProjectEditor.Tools

{

    /// <summary>Gameplay v2 + optional prism DLL recolor + deploy/rollback for Prism Flare #247 / 0x60F7.</summary>

    internal static class PrismFlareV2PackRt2

    {

        const string DefaultKernel =

            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";

        const string DefaultMagicRoot =

            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";

        const string DefaultOutputDir = @"work\prism_flare_v2";

        const string BackupSuffix = ".backup_prism_v2_";



        public static int Run(string[] args)

        {

            try

            {

                if (args.Any(a => a.Equals("--rollback", StringComparison.OrdinalIgnoreCase)))

                    return RunRollback(args);



                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));

                bool recolorDll = args.Any(a => a.Equals("--recolor-dll", StringComparison.OrdinalIgnoreCase));
                bool cloneVisuals = args.Any(a => a.Equals("--clone-visuals", StringComparison.OrdinalIgnoreCase));

                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;

                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;

                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                int commandId = int.Parse(ArgValue(args, "--id") ?? MonsterMagicGrowWriter.PrismFlareCommandId.ToString());



                Console.WriteLine("=== Prism Flare V2 PACK RT2 ===");

                Console.WriteLine($"kernel : {kernelPath}");

                Console.WriteLine($"magic  : {magicRoot}");

                Console.WriteLine($"output : {outputDir}");

                Console.WriteLine($"deploy : {deploy}");

                Console.WriteLine($"recolor-dll : {recolorDll} (off by default — bulk vec4 patches caused softlock)");
                Console.WriteLine($"visuals     : {(cloneVisuals ? "714/714 clones" : "82/82 vanilla Multi-Fira (default)")}");



                if (!File.Exists(kernelPath))

                {

                    Console.WriteLine("FAIL: monmagic2.bin not found.");

                    return 2;

                }



                Directory.CreateDirectory(outputDir);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");



                byte[] originalKernel = File.ReadAllBytes(kernelPath);

                byte[] patchedKernel = MonsterMagicGrowWriter.PatchPrismFlareV2(originalKernel, commandId, useDedicatedClones: cloneVisuals);

                string stagedKernel = Path.Combine(outputDir, "monmagic2.bin");

                File.WriteAllBytes(stagedKernel, patchedKernel);



                var entries = Ability_Command.ReadList(patchedKernel, hasExtraInfo: false);

                Ability_Command row = entries[commandId];

                string name = DecodeUs(row.NameScriptBytes);



                string dll714 = Path.Combine(magicRoot, "magic_0714.dll");

                string dll715 = Path.Combine(magicRoot, "magic_0715.dll");

                PrismMagicDllRecolor.RecolorResult? recolor714 = null;

                PrismMagicDllRecolor.RecolorResult? recolor715 = null;



                if (recolorDll)

                {

                    if (File.Exists(dll714))

                    {

                        string out714 = Path.Combine(outputDir, "magic_0714_prism.dll");

                        recolor714 = PrismMagicDllRecolor.Apply(dll714, out714);

                        PrismMagicDllRecolor.WritePatchPlan(dll714, Path.Combine(outputDir, "magic_0714_patch_plan.json"));

                    }

                    if (File.Exists(dll715))

                    {

                        string out715 = Path.Combine(outputDir, "magic_0715_prism.dll");

                        recolor715 = PrismMagicDllRecolor.Apply(dll715, out715);

                        PrismMagicDllRecolor.WritePatchPlan(dll715, Path.Combine(outputDir, "magic_0715_patch_plan.json"));

                    }

                }



                string? deployedKernel = null;

                string? deployed714 = null;

                string? deployed715 = null;

                if (deploy)

                {

                    deployedKernel = BackupAndCopy(stagedKernel, kernelPath, stamp);

                    if (recolor714 != null)

                        deployed714 = BackupAndCopy(recolor714.OutputDll, dll714, stamp);

                    if (recolor715 != null)

                        deployed715 = BackupAndCopy(recolor715.OutputDll, dll715, stamp);

                }



                bool sizeOk = patchedKernel.Length == originalKernel.Length;

                var payload = new

                {

                    commandId,

                    name,

                    row.AttackPower,

                    row.HitCount,

                    elements = row.ElementFlgs.ToString(),

                    slowChance = row.StatusChance.Slow,

                    slowDuration = row.StatusDuration.Slow,

                    anim1 = row.Anim1Id,

                    anim2 = row.Anim2Id,

                    kernelSizePreserved = sizeOk,

                    kernelBytesBefore = originalKernel.Length,

                    kernelBytesAfter = patchedKernel.Length,

                    kernel = new

                    {

                        source = kernelPath,

                        staged = stagedKernel,

                        sourceSha256 = Sha256Hex(originalKernel),

                        outputSha256 = Sha256Hex(patchedKernel),

                        deployed = deployedKernel

                    },

                    magic_0714 = recolor714 == null ? null : new

                    {

                        recolor714.PatchCount,

                        recolor714.SourceSha256,

                        recolor714.OutputSha256,

                        output = recolor714.OutputDll,

                        deployed = deployed714

                    },

                    magic_0715 = recolor715 == null ? null : new

                    {

                        recolor715.PatchCount,

                        recolor715.SourceSha256,

                        recolor715.OutputSha256,

                        output = recolor715.OutputDll,

                        deployed = deployed715

                    }

                };



                string jsonPath = Path.Combine(outputDir, "prism_flare_v2_pack.json");

                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

                File.WriteAllText(Path.Combine(outputDir, "PRISM_FLARE_V2_RT2.md"), BuildMarkdown(payload, deploy, recolorDll));



                Console.WriteLine($"command: {commandId} {name}");

                Console.WriteLine($"power={row.AttackPower} hits={row.HitCount} slow={row.StatusChance.Slow}%/{row.StatusDuration.Slow}t");

                Console.WriteLine($"anims: {row.Anim1Id}/{row.Anim2Id}");

                Console.WriteLine($"kernel size: {originalKernel.Length} -> {patchedKernel.Length} ({(sizeOk ? "OK" : "DRIFT")})");

                Console.WriteLine($"kernel staged: {stagedKernel}");

                if (recolor714 != null)

                    Console.WriteLine($"0714 patches: {recolor714.PatchCount} -> {recolor714.OutputDll}");

                if (recolor715 != null)

                    Console.WriteLine($"0715 patches: {recolor715.PatchCount} -> {recolor715.OutputDll}");

                if (deploy)

                    Console.WriteLine("DEPLOYED to game paths (backups with stamp " + stamp + ")");

                else

                    Console.WriteLine("Staged only. Re-run with --deploy to install.");



                Console.WriteLine($"json: {jsonPath}");

                bool pass = sizeOk;

                Console.WriteLine(pass ? "VERDICT: PASS (gameplay-only kernel patch)" : "VERDICT: FAIL — kernel size drift");

                return pass ? 0 : 1;

            }

            catch (Exception ex)

            {

                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");

                return 1;

            }

        }



        static int RunRollback(string[] args)

        {

            string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;

            string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;

            string? stamp = ArgValue(args, "--stamp");



            Console.WriteLine("=== Prism Flare V2 ROLLBACK ===");

            int restored = 0;

            restored += RestoreFromBackup(kernelPath, stamp);

            restored += RestoreFromBackup(Path.Combine(magicRoot, "magic_0714.dll"), stamp);

            restored += RestoreFromBackup(Path.Combine(magicRoot, "magic_0715.dll"), stamp);



            if (restored == 0)

            {

                Console.WriteLine("FAIL: no backup_prism_v2_* files found.");

                return 1;

            }



            Console.WriteLine($"Restored {restored} file(s).");

            if (File.Exists(kernelPath))

                Console.WriteLine($"monmagic2 size: {new FileInfo(kernelPath).Length}");

            Console.WriteLine("VERDICT: ROLLBACK OK");

            return 0;

        }



        static int RestoreFromBackup(string targetPath, string? stamp)

        {

            if (!File.Exists(targetPath))

                return 0;



            string dir = Path.GetDirectoryName(targetPath) ?? ".";

            string name = Path.GetFileName(targetPath);

            string pattern = name + BackupSuffix + (stamp ?? "*");

            string? backup = Directory.GetFiles(dir, pattern)

                .OrderByDescending(File.GetLastWriteTimeUtc)

                .FirstOrDefault();

            if (backup == null)

            {

                Console.WriteLine($"skip (no backup): {targetPath}");

                return 0;

            }



            File.Copy(backup, targetPath, overwrite: true);

            Console.WriteLine($"restored: {targetPath} <- {Path.GetFileName(backup)}");

            return 1;

        }



        static string BuildMarkdown(object payload, bool deployed, bool recolorDll)

        {

            var sb = new StringBuilder();

            sb.AppendLine("# Prism Flare v2 — RT2 pack (gameplay-only)");

            sb.AppendLine();

            sb.AppendLine("## Gameplay v2");

            sb.AppendLine("- Power **42**");

            sb.AppendLine("- Elements **Fire + Thunder** (text pool untouched)");

            sb.AppendLine("- Slow **65% / 4 turns**");

            sb.AppendLine("- Visual **714 / 714** (Multi-Fira pattern: same DLL both phases; clone of `0082`)");

            sb.AppendLine();

            if (recolorDll)

                sb.AppendLine("## Visual (opt-in)");

                sb.AppendLine("- `--recolor-dll` enabled — use only after gameplay RT2 passes");

            sb.AppendLine();

            sb.AppendLine("## Rollback");

            sb.AppendLine("```");

            sb.AppendLine("FFXProjectEditor --prism-flare-v2-pack --rollback");

            sb.AppendLine("```");

            sb.AppendLine();

            sb.AppendLine(deployed ? "**Status:** deployed to Steam mod paths." : "**Status:** staged in `work/prism_flare_v2/` only.");

            sb.AppendLine();

            sb.AppendLine("```json");

            sb.AppendLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

            sb.AppendLine("```");

            return sb.ToString();

        }



        static string BackupAndCopy(string source, string target, string stamp)

        {

            string backup = target + BackupSuffix + stamp;

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



        static string Sha256Hex(byte[] bytes)

        {

            byte[] hash = SHA256.HashData(bytes);

            return Convert.ToHexString(hash).ToLowerInvariant();

        }

    }

}


