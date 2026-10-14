using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    internal static class KimahriExtendedPackRt0
    {
        const string DefaultKernel =
            @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";

        const string DefaultOutputDir = @"work\kimahri_extended_pack";

        static string ResolveOutputDir(string? outputArg)
        {
            string relative = outputArg ?? DefaultOutputDir;
            if (Path.IsPathRooted(relative))
                return relative;

            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.GetFullPath(Path.Combine(repoRoot, relative));
        }

        public static int Run(string[] args)
        {
            try
            {
                string kernel = ArgValue(args, "--kernel") ?? DefaultKernel;
                string outputDir = ResolveOutputDir(ArgValue(args, "--output"));
                bool withWards = args.Any(a => a.Equals("--with-wards", StringComparison.OrdinalIgnoreCase));

                Console.WriteLine("=== Kimahri Blue Mage command PACK RT0 ===");
                Console.WriteLine($"kernel     : {kernel}");
                Console.WriteLine($"output     : {outputDir}");
                Console.WriteLine($"with-wards : {withWards}");

                if (!File.Exists(kernel))
                {
                    Console.Error.WriteLine("FAIL: command.bin not found.");
                    return 2;
                }

                byte[] source = File.ReadAllBytes(kernel);
                int sourceRows = Ability_Command.ReadList(source, hasExtraInfo: true).Count;
                Console.WriteLine($"input rows : {sourceRows}");

                if (withWards)
                {
                    if (sourceRows != CommandGrowWriter.RadiantWardCommandId)
                    {
                        Console.Error.WriteLine(
                            $"FAIL: --with-wards expects exactly {CommandGrowWriter.RadiantWardCommandId} vanilla rows, got {sourceRows}.");
                        return 3;
                    }

                    CommandGrowResult wardGrow = CommandGrowWriter.AppendElementWards(source, hookHandlesNulStatus: true);
                    source = wardGrow.GrownBytes;
                    sourceRows = wardGrow.NewEntryCount;
                    Console.WriteLine($"ward grow  : {wardGrow.OriginalEntryCount} -> {wardGrow.NewEntryCount} (PASS={wardGrow.Pass})");
                    if (!wardGrow.Pass)
                    {
                        Console.Error.WriteLine("FAIL: Nul Ward grow preflight failed.");
                        return 4;
                    }
                }

                KimahriExtendedGrowResult grow = KimahriExtendedCommandWriter.AppendKimahriBasePack(source);
                Directory.CreateDirectory(outputDir);
                string staged = Path.Combine(outputDir, "command.bin");
                File.WriteAllBytes(staged, grow.GrownBytes);

                var reread = Ability_Command.ReadList(grow.GrownBytes, hasExtraInfo: true);
                WriteManifestCsv(outputDir, grow, reread);
                WriteManifestJson(outputDir, grow, reread);

                Console.WriteLine();
                Console.WriteLine($"appended   : {grow.AppendedCount} ({KimahriExtendedCommandWriter.RonsoRageDonorIds.Length} Ronso Blue Mage + Demita + Lancet+)");
                foreach (KimahriExtendedSpellEntry spell in grow.Spells)
                {
                    Ability_Command row = reread[spell.NewId];
                    Console.WriteLine(
                        $"  #{spell.NewId,3} {spell.Name,-16} donor=#{spell.DonorId,3} kind={spell.Kind,-13} MP={row.CostMp,3} OD={row.CostOverdrive,3} user={row.CharacterUser}");
                }

                Console.WriteLine($"rows       : {grow.OriginalEntryCount} -> {grow.NewEntryCount}");
                Console.WriteLine($"bytes      : {grow.OriginalLength} -> {grow.GrownLength}");
                Console.WriteLine($"staged     : {staged}");
                Console.WriteLine(grow.Pass
                    ? "VERDICT: PASS — Kimahri Blue Mage pack preserves prefix and round-trips."
                    : "VERDICT: FAIL — grow preflight drifted.");

                return grow.Pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        static void WriteManifestCsv(string outputDir, KimahriExtendedGrowResult grow, List<Ability_Command> reread)
        {
            string path = Path.Combine(outputDir, "kimahri_extended_manifest.csv");
            var sb = new StringBuilder();
            sb.AppendLine("command_id,learned_move_hex,name,donor_id,kind,mp_cost,character_user,notes");
            foreach (KimahriExtendedSpellEntry spell in grow.Spells)
            {
                Ability_Command row = reread[spell.NewId];
                sb.Append(spell.NewId).Append(',');
                sb.Append("0x").Append((0x3000 | spell.NewId).ToString("X4")).Append(',');
                sb.Append('"').Append(spell.Name.Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(spell.DonorId).Append(',');
                sb.Append(spell.Kind).Append(',');
                sb.Append(row.CostMp).Append(',');
                sb.Append("Kimahri").Append(',');
                sb.Append('"').Append(DescribeNotes(spell).Replace("\"", "\"\"")).Append('"').AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"manifest   : {path}");
        }

        static string DescribeNotes(KimahriExtendedSpellEntry spell) => spell.Kind switch
        {
            KimahriExtendedSpellKind.RonsoBlueMage =>
                $"Ronso Rage #{spell.DonorId} as Blue Mage Skill (MP replaces OD gauge)",
            KimahriExtendedSpellKind.Demita => "Demi clone + Multi + Kimahri-only gravity AoE",
            KimahriExtendedSpellKind.LancetPlus => "Lancet clone; mob-skill learn hook TBD",
            _ => "",
        };

        static void WriteManifestJson(string outputDir, KimahriExtendedGrowResult grow, List<Ability_Command> reread)
        {
            string path = Path.Combine(outputDir, "kimahri_extended_manifest.json");
            var payload = new
            {
                grow = new
                {
                    grow.OriginalEntryCount,
                    grow.NewEntryCount,
                    grow.AppendedCount,
                    grow.OriginalLength,
                    grow.GrownLength,
                    grow.Pass,
                },
                spells = grow.Spells.Select(spell =>
                {
                    Ability_Command row = reread[spell.NewId];
                    return new
                    {
                        spell.NewId,
                        learnedMoveHex = $"0x{0x3000 | spell.NewId:X4}",
                        spell.Name,
                        spell.DonorId,
                        kind = spell.Kind.ToString(),
                        costMp = row.CostMp,
                        costOverdrive = row.CostOverdrive,
                        characterUser = row.CharacterUser.ToString(),
                        targetMulti = row.FlagTargetMulti,
                        subMenu = row.SubMenuCategorization,
                        damageFormula = row.DamageFormula.ToString(),
                        attackPower = row.AttackPower,
                    };
                }).ToArray(),
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"manifest   : {path}");
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
