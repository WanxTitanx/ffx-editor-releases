using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    internal static class Fsb9999AppendLabRt2
    {
        const string DefaultFsb =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us\9999_bank00.fsb";

        public static int Run(string[] args)
        {
            string repo = CommandSoundCorpusLoader.FindRepoRoot()
                ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
            string fsbPath = ArgValue(args, "--fsb") ?? DefaultFsb;
            string jsonOut = ArgValue(args, "--json")
                ?? Path.Combine(repo, @"RuntimeTools\Fev9999AppendLab\fev_append_verdict.json");
            string? wav = ArgValue(args, "--wav");

            Console.WriteLine("=== FSB 9999 Append Lab RT0 ===");
            Console.WriteLine($"fsb   : {fsbPath}");
            Console.WriteLine($"tools : vgmstream={FfxAudioToolsLocator.VgmStreamAvailable} fsbext={FfxAudioToolsLocator.FsbExtAvailable} fsbankcl={FfxAudioToolsLocator.FsbankClAvailable}");

            bool rt0 = false;
            string rt0Msg = "skip";
            int newIndex = -1;

            if (FfxAudioToolsLocator.CustomSfxToolsReady && File.Exists(fsbPath))
            {
                string work = Path.Combine(repo, @"work\fsb9999_append_lab");
                if (Directory.Exists(work))
                {
                    try { Directory.Delete(work, recursive: true); } catch { }
                }
                Directory.CreateDirectory(work);
                string scratch = FfxFsbExt_Service.ScratchDir(work);
                Directory.CreateDirectory(scratch);

                if (string.IsNullOrWhiteSpace(wav))
                {
                    wav = Path.Combine(scratch, "identity.wav");
                    FfxFsbVgmStream_Service.ExportSubsong(fsbPath, 0, wav);
                }

                if (File.Exists(wav))
                {
                    Fsb9999SampleAppendWriter.AppendResult res = Fsb9999SampleAppendWriter.AppendSample(
                        new Fsb9999SampleAppendWriter.AppendRequest(fsbPath, wav, work));
                    rt0 = res.Ok;
                    rt0Msg = res.Message;
                    newIndex = res.NewSampleIndex0;
                    Console.WriteLine($"RT0: {rt0Msg} method={res.Method} index={newIndex}");
                }
                else rt0Msg = "no WAV for append test";
            }
            else if (!File.Exists(fsbPath))
                rt0Msg = "FSB missing";
            else
                rt0Msg = "bootstrap tools";

            var verdict = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                fsb_path = fsbPath,
                rt0_fsb_append = rt0 ? "pass" : "pending",
                rt0_message = rt0Msg,
                new_sample_index = newIndex,
                rt2_new_seid_in_game = "pending",
            };

            Directory.CreateDirectory(Path.GetDirectoryName(jsonOut)!);
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"json: {jsonOut}");
            return rt0 ? 0 : 1;
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
