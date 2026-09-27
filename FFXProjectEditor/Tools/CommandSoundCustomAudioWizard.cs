using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Tools;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Generates a SPIRA-style manifest for custom battle SFX (WAV → FMOD → deploy FEV+FSB + optional seId patch).
    /// Does not embed FMOD SDK — documents external steps.
    /// </summary>
    internal static class CommandSoundCustomAudioWizard
    {
        const string DefaultOutputDir = @"work\command_sound_custom";

        public static int Run(string[] args)
        {
            try
            {
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repo = CommandSoundCorpusLoader.FindRepoRoot()
                    ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string outPath = Path.IsPathRooted(outputDir) ? outputDir : Path.Combine(repo, outputDir);
                string wavPath = ArgValue(args, "--wav") ?? "";
                int targetMagicId = int.TryParse(ArgValue(args, "--magic-id"), out int m) ? m : 84;
                uint? targetSeId = uint.TryParse(ArgValue(args, "--se-id"), out uint s) ? s : null;
                string locale = ArgValue(args, "--locale") ?? "US";

                Directory.CreateDirectory(outPath);

                var manifest = new
                {
                    generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                    tier = "custom_audio_tier2",
                    target_magic_id = targetMagicId,
                    target_se_id = targetSeId,
                    source_wav = string.IsNullOrWhiteSpace(wavPath) ? null : Path.GetFullPath(wavPath),
                    locale,
                    steps = new[]
                    {
                        "1. Import WAV into FMOD Studio project cloned from game bank 9999",
                        "2. Assign or reuse FMOD event index matching target seId (see fev9999_sequence_index.json)",
                        "3. Build 9999.fev + 9999_bank00.fsb via fsbankcl / FMOD Studio export",
                        "4. Stage files under work/command_sound_custom/staged/sfx/" + locale,
                        "5. Patch magic_####.dll seId if using new sequence slot (--command-sound-pack)",
                        "6. Deploy FEV+FSB to game SFX/" + locale + "/ with backup",
                        "7. RT2: ability-sfx-lab + cast spell + verify sequenceId in ffx-hooks.log",
                    },
                    deploy_paths = new
                    {
                        fev = $"SFX/{locale}/9999.fev",
                        fsb = $"SFX/{locale}/9999_bank00.fsb",
                        common_txt = $"SFX/{locale}/9999_common.txt",
                        loop_txt = $"SFX/{locale}/9999_loop.txt",
                        magic_dll = $"magicFiles/FFX/magic_{targetMagicId:D4}.dll",
                    },
                    external_tools = new[]
                    {
                        "FMOD Studio — https://www.fmod.com/",
                        "vgmstream — preview FSB",
                        "fsbankcl — bank rebuild",
                    },
                    related_gates = new[]
                    {
                        "--fev9999-corpus-wave7",
                        "--command-sound-pack",
                        "--magicdll-sound-corpus-wave6",
                    },
                };

                string jsonPath = Path.Combine(outPath, $"custom_audio_magic_{targetMagicId:D4}.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                string scriptPath = Path.Combine(outPath, "CUSTOM_AUDIO_DEPLOY.md");
                File.WriteAllText(scriptPath, BuildDeployDoc(manifest, jsonPath));

                Console.WriteLine("=== Command Sound Custom Audio Wizard ===");
                Console.WriteLine($"manifest: {jsonPath}");
                Console.WriteLine($"guide   : {scriptPath}");
                Console.WriteLine("VERDICT: PASS — manifest generated (external FMOD rebuild required)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string BuildDeployDoc(object manifest, string jsonPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Custom Battle SFX — Deploy Guide");
            sb.AppendLine();
            sb.AppendLine("Tier 2 pipeline: new WAV requires FMOD bank rebuild, not only DLL seId patch.");
            sb.AppendLine();
            sb.AppendLine($"Manifest: `{jsonPath}`");
            sb.AppendLine();
            sb.AppendLine("## Quick path (reuse existing in-game sound)");
            sb.AppendLine();
            sb.AppendLine("Use `--command-sound-pack --magic-id N --donor-magic-id M --deploy` — no FEV rebuild.");
            sb.AppendLine();
            sb.AppendLine("## Full custom path");
            sb.AppendLine();
            sb.AppendLine("1. Run `--fev9999-corpus-wave7` on extracted `sound_pc/sfx/us`");
            sb.AppendLine("2. Open FMOD Studio; import WAV; map to free `seId` slot");
            sb.AppendLine("3. Export `9999.fev` + `9999_bank00.fsb`");
            sb.AppendLine("4. Backup vanilla SFX files; copy staged build");
            sb.AppendLine(Strings.F2_5_command_sound_pack_with_new_se_id_on_t_821f01ed);
            sb.AppendLine("6. RT2 with `ability-sfx-lab`");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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
