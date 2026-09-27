using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    internal static class NulWardPackRt2
    {
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";

        const string DefaultKernelJp =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\jppc\battle\kernel\command.bin";

        const string DefaultOutputDir = @"work\nul_ward_pack";

        public static int Run(string[] args)
        {
            try
            {
                bool deploy = args.Any(a => a.Equals("--deploy", StringComparison.OrdinalIgnoreCase));
                string kernelUs = ArgValue(args, "--kernel") ?? DefaultKernel;
                string kernelJp = ArgValue(args, "--kernel-jp") ?? DefaultKernelJp;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== Nul Ward PACK RT2 (Radiant + Umbral) ===");
                Console.WriteLine($"kernel us : {kernelUs}");
                Console.WriteLine($"kernel jp : {kernelJp}");
                Console.WriteLine($"output    : {outputDir}");
                Console.WriteLine($"deploy    : {deploy}");

                if (!File.Exists(kernelUs))
                {
                    Console.WriteLine("FAIL: US command.bin not found.");
                    return 2;
                }

                bool hookAware = !args.Any(a => a.Equals("--vanilla-nul-status", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"hook-aware status: {hookAware} (pass --vanilla-nul-status to keep Tide/Shock inflict bytes)");

                Directory.CreateDirectory(outputDir);
                byte[] originalUs = File.ReadAllBytes(kernelUs);
                CommandGrowResult grow = CommandGrowWriter.AppendElementWards(originalUs, hookAware);
                string stagedUs = Path.Combine(outputDir, "command.bin");
                File.WriteAllBytes(stagedUs, grow.GrownBytes);

                string? backupUs = null;
                string? backupJp = null;
                if (deploy)
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    backupUs = kernelUs + $".backup_nul_ward_{stamp}";
                    File.Copy(kernelUs, backupUs, overwrite: true);
                    File.Copy(stagedUs, kernelUs, overwrite: true);

                    if (File.Exists(kernelJp))
                    {
                        byte[] originalJp = File.ReadAllBytes(kernelJp);
                        CommandGrowResult growJp = CommandGrowWriter.AppendElementWards(originalJp, hookAware);
                        backupJp = kernelJp + $".backup_nul_ward_{stamp}";
                        File.Copy(kernelJp, backupJp, overwrite: true);
                        File.WriteAllBytes(kernelJp, growJp.GrownBytes);
                    }
                    else
                    {
                        Console.WriteLine($"WARN: JP kernel missing — skipped: {kernelJp}");
                    }
                }

                var reread = Ability_Command.ReadList(grow.GrownBytes, hasExtraInfo: true);
                Ability_Command radiant = reread[CommandGrowWriter.RadiantWardCommandId];
                Ability_Command umbral = reread[CommandGrowWriter.UmbralWardCommandId];

                var payload = new
                {
                    grow = new
                    {
                        grow.OriginalEntryCount,
                        grow.NewEntryCount,
                        grow.RadiantWardId,
                        grow.UmbralWardId,
                        grow.OriginalLength,
                        grow.GrownLength,
                        grow.Pass,
                    },
                    radiant = DescribeRow(radiant),
                    umbral = DescribeRow(umbral),
                    deploy = new
                    {
                        us = backupUs,
                        jp = backupJp,
                    },
                    note = hookAware
                        ? "Status inflict cleared — NulWardHook owns Holy/Dark blocks when ffx-hooks apply is on."
                        : "Legacy: Tide/Shock inflict bytes still set (pre-hook cosmetic).",
                };

                string jsonPath = Path.Combine(outputDir, "nul_ward_pack.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outputDir, "NUL_WARD_PACK_RT2.md"), BuildMarkdown(payload, deploy));

                Console.WriteLine($"id {grow.RadiantWardId}: {grow.RadiantWardName} anim {radiant.Anim1Id}/{radiant.Anim2Id} elem=0x{((byte)radiant.ElementFlgs):X2}");
                Console.WriteLine($"id {grow.UmbralWardId}: {grow.UmbralWardName} anim {umbral.Anim1Id}/{umbral.Anim2Id} elem=0x{((byte)umbral.ElementFlgs):X2}");
                Console.WriteLine($"kernel: {grow.OriginalLength} -> {grow.GrownLength} bytes ({grow.OriginalEntryCount} -> {grow.NewEntryCount} rows)");
                if (deploy)
                    Console.WriteLine("DEPLOYED command.bin (US" + (backupJp != null ? " + JP" : "") + ")");
                Console.WriteLine($"json: {jsonPath}");

                bool pass = grow.Pass;
                Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static object DescribeRow(Ability_Command row) => new
        {
            name = FfxEncoding.DecodeScript(row.NameScriptBytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true),
            description = FfxEncoding.DecodeScript(row.DescriptionScriptBytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true),
            row.Anim1Id,
            row.Anim2Id,
            element = $"0x{((byte)row.ElementFlgs):X2}",
            row.CostMp,
            row.MoveRank,
            nulTide = row.StatusChance.NulTide,
            nulBlaze = row.StatusChance.NulBlaze,
            nulShock = row.StatusChance.NulShock,
            nulFrost = row.StatusChance.NulFrost,
        };

        static string BuildMarkdown(object payload, bool deployed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Nul Ward pack — RT2");
            sb.AppendLine();
            sb.AppendLine("| Spell | ID | Theme |");
            sb.AppendLine("|-------|-----|-------|");
            sb.AppendLine($"| **Radiant Ward** | {CommandGrowWriter.RadiantWardCommandId} | Holy null (clone NulTide) |");
            sb.AppendLine($"| **Umbral Ward** | {CommandGrowWriter.UmbralWardCommandId} | Dark null (clone NulShock) |");
            sb.AppendLine();
            sb.AppendLine($"Deployed: **{deployed}**");
            sb.AppendLine();
            sb.AppendLine("Engine gap: vanilla only has 4 element-null status bytes; true Holy/Dark block needs RT2 + hook.");
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
