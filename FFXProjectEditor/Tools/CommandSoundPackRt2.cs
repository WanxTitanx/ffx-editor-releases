using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Tools
{
    /// <summary>Stage/deploy command sound remap packs (SeSep seId patch in magic_####.dll).</summary>
    internal static class CommandSoundPackRt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultOutputDir = CommandSoundPackService.DefaultOutputDir;

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                bool rt2Demo = args.Any(a => a.Equals("--rt2-fire-firaga", StringComparison.OrdinalIgnoreCase));
                bool restore = args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase));
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== Command Sound PACK (magic DLL SeSep remap) ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"output     : {outputDir}");
                Console.WriteLine($"deploy     : {deploy}");
                Console.WriteLine($"dry-run    : {dryRun}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                if (restore)
                {
                    int restoreMagicId = int.Parse(ArgValue(args, "--magic-id") ?? ArgValue(args, "--target-magic-id") ?? "84");
                    CommandSoundPackService.PackResult restored = CommandSoundPackService.RestoreLatestBackup(magicRoot, restoreMagicId);
                    Console.WriteLine(restored.Message);
                    return restored.Ok ? 0 : 1;
                }

                if (rt2Demo)
                {
                    CommandSoundPackService.PackResult demo = CommandSoundPackService.RunRt2FireToFiragaDemo(magicRoot, outputDir, deploy);
                    Console.WriteLine(demo.Message);
                    if (demo.Patch != null)
                    {
                        Console.WriteLine($"  before seId: {demo.Patch.RecordsBefore.FirstOrDefault()?.SeId}");
                        Console.WriteLine($"  after  seId: {demo.Patch.RecordsAfter.FirstOrDefault()?.SeId}");
                    }
                    if (demo.ManifestPath != null)
                        Console.WriteLine($"manifest: {demo.ManifestPath}");
                    if (demo.DeployedDllPath != null)
                        Console.WriteLine($"deployed: {demo.DeployedDllPath} backup={demo.BackupPath}");
                    File.WriteAllText(
                        Path.Combine(outputDir, "COMMAND_SOUND_RT2.md"),
                        BuildRt2Markdown(demo, deploy));
                    Console.WriteLine(demo.Ok ? "VERDICT: PASS — RT2 Fire→Firaga pack staged" + (deploy ? " + deployed" : "") : "VERDICT: FAIL");
                    return demo.Ok ? 0 : 1;
                }

                int magicId = int.Parse(ArgValue(args, "--magic-id") ?? ArgValue(args, "--target-magic-id") ?? "84");
                uint? seId = uint.TryParse(ArgValue(args, "--se-id"), out uint parsedSe) ? parsedSe : null;
                int donorMagicId = int.TryParse(ArgValue(args, "--donor-magic-id"), out int dm) ? dm : -1;
                int recordIndex = int.TryParse(ArgValue(args, "--record-index"), out int ri) ? ri : 0;

                if (seId == null && donorMagicId >= 0)
                {
                    string donorDll = Path.Combine(magicRoot, $"magic_{donorMagicId:D4}.dll");
                    var donorRecords = MagicDllSoundWriter.ListRecords(donorDll);
                    if (donorRecords.Count == 0)
                    {
                        Console.WriteLine($"FAIL: donor magic_{donorMagicId:D4} has no SeSep");
                        return 3;
                    }
                    seId = donorRecords[Math.Min(recordIndex, donorRecords.Count - 1)].SeId;
                    ushort wave = donorRecords[Math.Min(recordIndex, donorRecords.Count - 1)].WaveDataId;
                    var request = new CommandSoundPackService.PackRequest(magicId, seId.Value, wave, recordIndex, $"donor magic_{donorMagicId:D4}");
                    return FinishPack(request, magicRoot, outputDir, deploy, dryRun);
                }

                if (seId == null)
                {
                    Console.WriteLine("FAIL: specify --se-id or --donor-magic-id");
                    return 4;
                }

                ushort? waveOpt = ushort.TryParse(ArgValue(args, "--wave-data-id"), out ushort w) ? w : null;
                return FinishPack(
                    new CommandSoundPackService.PackRequest(magicId, seId.Value, waveOpt, recordIndex, ArgValue(args, "--note")),
                    magicRoot,
                    outputDir,
                    deploy,
                    dryRun);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int FinishPack(
            CommandSoundPackService.PackRequest request,
            string magicRoot,
            string outputDir,
            bool deploy,
            bool dryRun)
        {
            string packDir = Path.Combine(outputDir, $"magic_{request.TargetMagicId:D4}");
            if (dryRun)
            {
                string dll = Path.Combine(magicRoot, $"magic_{request.TargetMagicId:D4}.dll");
                MagicDllSoundWriter.PatchResult dry = MagicDllSoundWriter.PatchFromCorpusEntry(
                    magicRoot,
                    request.TargetMagicId,
                    request.DonorSeId,
                    request.DonorWaveDataId,
                    request.RecordIndex,
                    dryRun: true);
                Console.WriteLine(dry.Message);
                return dry.Ok ? 0 : 1;
            }

            CommandSoundPackService.PackResult staged = CommandSoundPackService.StagePack(request, magicRoot, packDir);
            Console.WriteLine(staged.Message);
            if (staged.ManifestPath != null)
                Console.WriteLine($"manifest: {staged.ManifestPath}");
            if (!staged.Ok)
                return 1;

            if (deploy)
            {
                CommandSoundPackService.PackResult deployed = CommandSoundPackService.DeployStaged(staged.StagedDllPath!, magicRoot);
                Console.WriteLine(deployed.Message);
                if (deployed.BackupPath != null)
                    Console.WriteLine($"backup: {deployed.BackupPath}");
                return deployed.Ok ? 0 : 1;
            }

            Console.WriteLine("VERDICT: PASS — staged (add --deploy to install)");
            return 0;
        }

        static string BuildRt2Markdown(CommandSoundPackService.PackResult demo, bool deployed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Command Sound RT2 — Fire → Firaga seId");
            sb.AppendLine();
            sb.AppendLine($"Patch `magic_{CommandSoundPackService.Rt2FireMagicId:D4}` seId **{CommandSoundPackService.Rt2FireSeId}** → **{CommandSoundPackService.Rt2FiragaSeId}** (Firaga donor).");
            sb.AppendLine();
            sb.AppendLine("## In-game protocol");
            sb.AppendLine("1. Deploy `ability-sfx-lab` hook");
            sb.AppendLine("2. Cast Fire — log should show `sequenceId=9006` (was 9007 vanilla)");
            sb.AppendLine("3. Restore: `--command-sound-pack --magic-id 84 --restore` or UI Restore backup");
            sb.AppendLine();
            sb.AppendLine(deployed ? "**Status:** deployed to magicFiles\\\\FFX" : "**Status:** staged only");
            sb.AppendLine();
            sb.AppendLine($"Result: {demo.Message}");
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
