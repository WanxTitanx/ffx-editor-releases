using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class CommandSoundCustomPackRt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultSfxRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us";
        const string DefaultOutputDir = CommandSoundPackService.CustomAudioOutputDir;

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                bool restore = args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase));
                bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string sfxRoot = ArgValue(args, "--sfx-root") ?? DefaultSfxRoot;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repo = CommandSoundCorpusLoader.FindRepoRoot()
                    ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                if (!Path.IsPathRooted(outputDir))
                    outputDir = Path.Combine(repo, outputDir);

                if (restore)
                {
                    int restoreMagicId = int.Parse(ArgValue(args, "--magic-id") ?? "84");
                    CommandSoundPackService.CustomAudioPackResult r =
                        CommandSoundPackService.RestoreCustomAudioBackups(sfxRoot, magicRoot, restoreMagicId);
                    Console.WriteLine(r.Message);
                    return r.Ok ? 0 : 1;
                }

                string? wav = ArgValue(args, "--wav");
                if (string.IsNullOrWhiteSpace(wav) || !File.Exists(wav))
                {
                    Console.WriteLine("FAIL: --wav path required");
                    return 2;
                }

                int magicIdTarget = int.Parse(ArgValue(args, "--magic-id") ?? "84");
                uint seId = uint.Parse(ArgValue(args, "--se-id") ?? ArgValue(args, "--donor-se-id") ?? "9007");
                int sampleIndex = int.TryParse(ArgValue(args, "--fsb-sample-index"), out int si) ? si : 0;
                string locale = ArgValue(args, "--locale") ?? "US";
                bool patchDll = !args.Any(a => a.Equals("--no-dll-patch", StringComparison.OrdinalIgnoreCase));

                ushort? wave = ushort.TryParse(ArgValue(args, "--wave-data-id"), out ushort w) ? w : null;
                if (!wave.HasValue)
                    wave = CommandSoundCorpusLoader.TryGetForMagicId(magicIdTarget)?.WaveDataId;

                var request = new CommandSoundPackService.CustomAudioPackRequest(
                    magicIdTarget,
                    seId,
                    sampleIndex,
                    Path.GetFullPath(wav),
                    locale,
                    patchDll,
                    wave,
                    0,
                    ArgValue(args, "--note"));

                Console.WriteLine("=== Command Sound CUSTOM PACK (FSB sample replace) ===");
                Console.WriteLine($"wav    : {wav}");
                Console.WriteLine($"magic  : {magicIdTarget}");
                Console.WriteLine($"seId   : {seId}");
                Console.WriteLine($"sample : {sampleIndex}");
                Console.WriteLine($"tools  : {FfxAudioToolsLocator.CustomSfxToolsReady}");

                if (dryRun)
                {
                    var dry = Fsb9999SampleReplaceWriter.ReplaceSample(
                        new Fsb9999SampleReplaceWriter.ReplaceRequest(
                            Path.Combine(sfxRoot, "9999_bank00.fsb"),
                            sampleIndex,
                            Path.GetFullPath(wav)),
                        dryRun: true);
                    Console.WriteLine(dry.Message);
                    return dry.Ok ? 0 : 1;
                }

                string packDir = Path.Combine(outputDir, $"magic_{magicIdTarget:D4}");
                CommandSoundPackService.CustomAudioPackResult staged =
                    CommandSoundPackService.StageCustomAudioPack(request, sfxRoot, magicRoot, packDir);
                Console.WriteLine(staged.Message);
                if (staged.ManifestPath != null)
                    Console.WriteLine($"manifest: {staged.ManifestPath}");
                if (!staged.Ok)
                    return 1;

                if (deploy)
                {
                    CommandSoundPackService.CustomAudioPackResult deployed =
                        CommandSoundPackService.DeployCustomAudioPack(staged, sfxRoot, magicRoot);
                    Console.WriteLine(deployed.Message);
                    if (deployed.FsbBackupPath != null)
                        Console.WriteLine($"FSB backup: {deployed.FsbBackupPath}");
                    if (deployed.DllBackupPath != null)
                        Console.WriteLine($"DLL backup: {deployed.DllBackupPath}");
                    return deployed.Ok ? 0 : 1;
                }

                Console.WriteLine("VERDICT: PASS — staged (add --deploy)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
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
