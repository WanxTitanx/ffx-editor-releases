using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services.Extras;
using System;
using System.IO;

namespace FFXProjectEditor.Tools
{
    internal static class CommandSoundNewSeIdPackRt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultSfxRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = Has(args, "--deploy");
                bool restore = Has(args, "--restore");
                bool dryRun = Has(args, "--dry-run");
                string magicRoot = Arg(args, "--magic-root") ?? DefaultMagicRoot;
                string sfxRoot = Arg(args, "--sfx-root") ?? DefaultSfxRoot;
                string repo = CommandSoundCorpusLoader.FindRepoRoot()
                    ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string outputDir = Arg(args, "--output")
                    ?? Path.Combine(repo, CommandSoundPackService.NewSeIdAudioOutputDir, $"magic_{Arg(args, "--magic-id") ?? "84"}");

                if (restore)
                {
                    int mid = int.Parse(Arg(args, "--magic-id") ?? "84");
                    var r = CommandSoundPackService.RestoreNewSeIdBackups(sfxRoot, magicRoot, mid);
                    Console.WriteLine(r.Message);
                    return r.Ok ? 0 : 1;
                }

                string? wav = Arg(args, "--wav");
                if (string.IsNullOrWhiteSpace(wav) || !File.Exists(wav))
                {
                    Console.WriteLine("FAIL: --wav required");
                    return 2;
                }

                int magicId = int.Parse(Arg(args, "--magic-id") ?? "84");
                uint newSeId = uint.Parse(Arg(args, "--new-se-id") ?? "8010");
                uint donorSeId = uint.Parse(Arg(args, "--donor-se-id") ?? "9006");
                bool mirrorJp = Has(args, "--mirror-jp");

                var request = new CommandSoundPackService.NewSeIdAudioPackRequest(
                    magicId,
                    newSeId,
                    donorSeId,
                    Path.GetFullPath(wav),
                    Arg(args, "--locale") ?? "US",
                    mirrorJp,
                    null,
                    int.TryParse(Arg(args, "--record-index"), out int ri) ? ri : 0,
                    Arg(args, "--note"));

                Console.WriteLine("=== Command Sound NEW seId pack (FEV+FSB+DLL) ===");
                Console.WriteLine($"magic    : {magicId}");
                Console.WriteLine($"new seId : {newSeId}");
                Console.WriteLine($"donor    : {donorSeId}");
                Console.WriteLine($"tools    : {FfxAudioToolsLocator.CustomSfxToolsReady}");

                if (dryRun)
                {
                    var fsbDry = Fsb9999SampleAppendWriter.AppendSample(
                        new Fsb9999SampleAppendWriter.AppendRequest(
                            Path.Combine(sfxRoot, "9999_bank00.fsb"),
                            Path.GetFullPath(wav)),
                        dryRun: true);
                    Console.WriteLine($"FSB dry: {fsbDry.Message}");
                    return fsbDry.Ok ? 0 : 1;
                }

                if (!Path.IsPathRooted(outputDir))
                    outputDir = Path.Combine(repo, outputDir);

                var staged = CommandSoundPackService.StageNewSeIdAudioPack(request, sfxRoot, magicRoot, outputDir);
                Console.WriteLine(staged.Message);
                if (staged.ManifestPath != null)
                    Console.WriteLine($"manifest: {staged.ManifestPath}");
                if (!staged.Ok)
                    return 1;

                if (deploy)
                {
                    var deployed = CommandSoundPackService.DeployNewSeIdAudioPack(staged, sfxRoot, magicRoot, mirrorJp);
                    Console.WriteLine(deployed.Message);
                    Console.WriteLine($"FEV backup: {deployed.FevBackupPath ?? "n/a"}");
                    Console.WriteLine($"FSB backup: {deployed.FsbBackupPath ?? "n/a"}");
                    Console.WriteLine($"DLL backup: {deployed.DllBackupPath ?? "n/a"}");
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

        static bool Has(string[] args, string key)
        {
            foreach (string a in args)
                if (a.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string? Arg(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
