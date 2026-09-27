using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.Services.Extras;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    internal static class Fsb9999LabRt2
    {
        const string DefaultFsb =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us\9999_bank00.fsb";

        public static int Run(string[] args)
        {
            string repo = CommandSoundCorpusLoader.FindRepoRoot()
                ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
            string fsbPath = ArgValue(args, "--fsb") ?? DefaultFsb;
            string jsonOut = ArgValue(args, "--json")
                ?? Path.Combine(repo, @"RuntimeTools\Fsb9999Lab\fsb9999_verdict.json");

            bool toolsOk = FfxAudioToolsLocator.CustomSfxToolsReady;
            bool rt0 = false;
            bool rt1 = false;
            bool rtAppend = false;
            string rt0Msg = "skip";
            string rt1Msg = "skip";
            string rtAppendMsg = "skip";

            Console.WriteLine("=== FSB 9999 Lab RT0/RT1 ===");
            Console.WriteLine($"fsb   : {fsbPath}");
            Console.WriteLine($"tools : vgmstream={FfxAudioToolsLocator.VgmStreamAvailable} fsbext={FfxAudioToolsLocator.FsbExtAvailable} fsbankcl={FfxAudioToolsLocator.FsbankClAvailable}");
            Console.WriteLine($"health: {FfxAudioToolsHealth_Service.Probe().Summary}");

            if (toolsOk && File.Exists(fsbPath))
            {
                var rt0Res = Fsb9999SampleReplaceWriter.RoundTripIdentity(fsbPath, Path.Combine(repo, @"work\fsb9999_lab"));
                rt0 = rt0Res.Ok;
                rt0Msg = rt0Res.Message;
                Console.WriteLine($"RT0: {rt0Msg}");

                string work = Path.Combine(repo, @"work\fsb9999_lab\rt1");
                Directory.CreateDirectory(work);
                string scratch = FfxFsbExt_Service.ScratchDir(work);
                Directory.CreateDirectory(scratch);
                string identityWav = Path.Combine(scratch, "identity.wav");
                (bool decOk, string decMsg) = FfxFsbVgmStream_Service.ExportSubsong(fsbPath, 0, identityWav);
                if (decOk)
                {
                    Fsb9999SampleReplaceWriter.ReplaceResult swap = Fsb9999SampleReplaceWriter.ReplaceSample(
                        new Fsb9999SampleReplaceWriter.ReplaceRequest(fsbPath, 0, identityWav, work));
                    rt1 = swap.Ok && swap.OutputFsbPath != null && File.Exists(swap.OutputFsbPath);
                    rt1Msg = swap.Message;
                }
                else rt1Msg = decMsg;
                Console.WriteLine($"RT1: {rt1Msg}");

                string appendWork = Path.Combine(repo, @"work\fsb9999_lab\append");
                if (decOk)
                {
                    Fsb9999SampleAppendWriter.AppendResult append = Fsb9999SampleAppendWriter.AppendSample(
                        new Fsb9999SampleAppendWriter.AppendRequest(fsbPath, identityWav, appendWork));
                    rtAppend = append.Ok;
                    rtAppendMsg = append.Message;
                    Console.WriteLine($"RT0-append: {rtAppendMsg}");
                }
            }
            else if (!File.Exists(fsbPath))
                rt0Msg = "FSB path missing";
            else
                rt0Msg = "Run scripts/bootstrap_fsb_audio_tools.ps1";

            var verdict = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                fsb_path = fsbPath,
                tools = new
                {
                    vgmstream = FfxAudioToolsLocator.VgmStreamAvailable,
                    fsbext = FfxAudioToolsLocator.FsbExtAvailable,
                    fsbankcl = FfxAudioToolsLocator.FsbankClAvailable,
                },
                rt0_fsb_identity = rt0 ? "pass" : "fail",
                rt0_message = rt0Msg,
                rt1_identity_swap = rt1 ? "pass" : "pending",
                rt1_message = rt1Msg,
                rt0_fsb_append = rtAppend ? "pass" : "pending",
                rt0_append_message = rtAppendMsg,
                rt2_custom_fsb_in_game = "pending",
                rt2_new_seid_in_game = "pending",
            };

            Directory.CreateDirectory(Path.GetDirectoryName(jsonOut)!);
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"json: {jsonOut}");
            Console.WriteLine(toolsOk && rt0 ? "VERDICT: PASS" : "VERDICT: FAIL/PENDING");
            return toolsOk && rt0 ? 0 : toolsOk ? 1 : 2;
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
