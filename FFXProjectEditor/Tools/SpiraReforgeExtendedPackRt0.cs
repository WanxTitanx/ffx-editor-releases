using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.Tools
{
    internal static class SpiraReforgeExtendedPackRt0
    {
        const string DefaultKernel =
            @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";

        const string DefaultOutputDir = @"work\spira_reforge_extended_pack";

        public static int Run(string[] args)
        {
            try
            {
                string kernel = ArgValue(args, "--kernel") ?? DefaultKernel;
                string outputDir = ResolveOutputDir(ArgValue(args, "--output"));
                bool noWards = args.Any(a => a.Equals("--no-wards", StringComparison.OrdinalIgnoreCase));
                bool noVanillaBuffs = args.Any(a => a.Equals("--no-vanilla-buffs", StringComparison.OrdinalIgnoreCase));
                bool noCopycat = args.Any(a => a.Equals("--no-copycat-patch", StringComparison.OrdinalIgnoreCase));
                IReadOnlySet<SpiraReforgeSpellPack>? packs = ParsePacks(args);

                Console.WriteLine("=== Spira Reforge FULL extended command PACK RT0 ===");
                Console.WriteLine($"kernel        : {kernel}");
                Console.WriteLine($"output        : {outputDir}");
                Console.WriteLine($"wards         : {!noWards}");
                Console.WriteLine($"vanilla buffs : {!noVanillaBuffs}");
                Console.WriteLine($"copycat patch : {!noCopycat}");
                Console.WriteLine($"packs         : {(packs == null ? "ALL" : string.Join(", ", packs))}");

                if (!File.Exists(kernel))
                {
                    Console.Error.WriteLine("FAIL: command.bin not found.");
                    return 2;
                }

                byte[] vanilla = File.ReadAllBytes(kernel);
                int vanillaRows = Ability_Command.ReadList(vanilla, hasExtraInfo: true).Count;
                if (vanillaRows != CommandBinGrowCore.VanillaRowCount && !noWards)
                {
                    Console.WriteLine(
                        $"WARN: input has {vanillaRows} rows; full pack expects {CommandBinGrowCore.VanillaRowCount} vanilla.");
                }

                var options = new SpiraReforgeGrowOptions
                {
                    IncludeWards = !noWards,
                    ApplyVanillaOffensiveRebalance = !noVanillaBuffs,
                    PatchCopycatExclusive = !noCopycat,
                    Packs = packs,
                };

                SpiraReforgeExtendedGrowResult grow = SpiraReforgeExtendedCommandWriter.AppendFullPack(vanilla, options);
                Directory.CreateDirectory(outputDir);
                string staged = Path.Combine(outputDir, "command.bin");
                File.WriteAllBytes(staged, grow.GrownBytes);

                var reread = Ability_Command.ReadList(grow.GrownBytes, hasExtraInfo: true);
                WriteManifestCsv(outputDir, grow, reread);
                WriteManifestJson(outputDir, grow, reread, options);

                Console.WriteLine();
                SpiraReforgeSpellPack? currentPack = null;
                foreach (SpiraReforgeSpellEntry spell in grow.Spells)
                {
                    if (spell.Pack != currentPack)
                    {
                        currentPack = spell.Pack;
                        Console.WriteLine($"--- {currentPack} ---");
                    }

                    Ability_Command row = reread[spell.NewId];
                    string kind = spell.IsMenuOpener ? "menu" : "skill";
                    Console.WriteLine(
                        $"  #{spell.NewId,3} {spell.Name,-18} {kind,-5} donor=#{spell.DonorId,3} P={row.AttackPower,3} MP={row.CostMp,3} multi={row.FlagTargetMulti,5} hits={row.HitCount,2} user={row.CharacterUser} sub={row.SubMenuCategorization}{EffectSummary(row)}");
                }

                if (grow.CopycatPatched)
                {
                    Ability_Command copycat = reread[SpiraReforgeExtendedCommandWriter.CopycatVanillaId];
                    Console.WriteLine();
                    Console.WriteLine(
                        $"Copycat patch: #{SpiraReforgeExtendedCommandWriter.CopycatVanillaId} CharacterUser={copycat.CharacterUser} (Rikku-exclusive vanilla row)");
                }

                if (grow.VanillaOffensiveRebalanceApplied)
                {
                    PrintVanillaBuffSummary(reread);
                    PrintKimahriOverdriveSummary(reread);
                }

                Console.WriteLine();
                Console.WriteLine($"appended   : {grow.AppendedCount} ({grow.SkillCount} skills + {grow.AppendedCount - grow.SkillCount} menu, + wards={grow.WardsIncluded})");
                Console.WriteLine($"rows       : {grow.OriginalEntryCount} -> {grow.NewEntryCount}");
                Console.WriteLine($"bytes      : {grow.OriginalLength} -> {grow.GrownLength}");
                Console.WriteLine($"staged     : {staged}");
                Console.WriteLine(grow.Pass
                    ? "VERDICT: PASS — full extended pack preserves prefix and round-trips."
                    : "VERDICT: FAIL — grow preflight drifted.");

                return grow.Pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static void WriteManifestCsv(string outputDir, SpiraReforgeExtendedGrowResult grow, List<Ability_Command> reread)
        {
            string path = Path.Combine(outputDir, "spira_reforge_extended_manifest.csv");
            var sb = new StringBuilder();
            sb.AppendLine("command_id,learned_move_hex,pack,kind,name,donor_id,owner,mp_cost,multi,hit_count,submenu,notes");
            foreach (SpiraReforgeSpellEntry spell in grow.Spells)
            {
                Ability_Command row = reread[spell.NewId];
                sb.Append(spell.NewId).Append(',');
                sb.Append("0x").Append((0x3000 | spell.NewId).ToString("X4")).Append(',');
                sb.Append(spell.Pack).Append(',');
                sb.Append(spell.IsMenuOpener ? "menu" : "skill").Append(',');
                sb.Append('"').Append(spell.Name.Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(spell.DonorId).Append(',');
                sb.Append(spell.Owner).Append(',');
                sb.Append(row.CostMp).Append(',');
                sb.Append(row.FlagTargetMulti).Append(',');
                sb.Append(row.HitCount).Append(',');
                sb.Append(row.SubMenuCategorization).Append(',');
                string note = spell.IsMenuOpener
                    ? "Append menu opener inside command.bin"
                    : "Append clone; teach via Sphere Grid 0x3000|id";
                sb.Append('"').Append(note).Append('"').AppendLine();
            }

            if (grow.CopycatPatched)
            {
                sb.Append(SpiraReforgeExtendedCommandWriter.CopycatVanillaId).Append(',');
                sb.Append("0x").Append((0x3000 | SpiraReforgeExtendedCommandWriter.CopycatVanillaId).ToString("X4")).Append(',');
                sb.Append("Rikku").Append(',');
                sb.Append("vanilla_patch").Append(',');
                sb.Append("Copycat").Append(',');
                sb.Append(SpiraReforgeExtendedCommandWriter.CopycatVanillaId).Append(',');
                sb.Append("Rikku").Append(',');
                sb.Append(reread[SpiraReforgeExtendedCommandWriter.CopycatVanillaId].CostMp).Append(',');
                sb.Append("false,0,14,");
                sb.Append('"').Append("Vanilla row patch CharacterUser=Rikku").Append('"').AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"manifest   : {path}");
        }

        static void WriteManifestJson(
            string outputDir,
            SpiraReforgeExtendedGrowResult grow,
            List<Ability_Command> reread,
            SpiraReforgeGrowOptions options)
        {
            string path = Path.Combine(outputDir, "spira_reforge_extended_manifest.json");
            var payload = new
            {
                grow = new
                {
                    grow.OriginalEntryCount,
                    grow.NewEntryCount,
                    grow.AppendedCount,
                    grow.SkillCount,
                    grow.WardsIncluded,
                    grow.VanillaOffensiveRebalanceApplied,
                    grow.CopycatPatched,
                    grow.OriginalLength,
                    grow.GrownLength,
                    grow.Pass,
                    options.IncludeWards,
                        options.ApplyVanillaOffensiveRebalance,
                    options.PatchCopycatExclusive,
                },
                spells = grow.Spells.Select(spell =>
                {
                    Ability_Command row = reread[spell.NewId];
                    return new
                    {
                        spell.NewId,
                        learnedMoveHex = $"0x{0x3000 | spell.NewId:X4}",
                        pack = spell.Pack.ToString(),
                        kind = spell.IsMenuOpener ? "menu" : "skill",
                        spell.Name,
                        spell.DonorId,
                        owner = spell.Owner.ToString(),
                        costMp = row.CostMp,
                        costOverdrive = row.CostOverdrive,
                        targetMulti = row.FlagTargetMulti,
                        hitCount = row.HitCount,
                        subMenu = row.SubMenuCategorization,
                        damageFormula = row.DamageFormula.ToString(),
                        attackPower = row.AttackPower,
                    };
                }).ToArray(),
                vanillaPatch = grow.CopycatPatched
                    ? new
                    {
                        id = SpiraReforgeExtendedCommandWriter.CopycatVanillaId,
                        name = "Copycat",
                        characterUser = reread[SpiraReforgeExtendedCommandWriter.CopycatVanillaId].CharacterUser.ToString(),
                    }
                    : null,
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"manifest   : {path}");
        }

        static IReadOnlySet<SpiraReforgeSpellPack>? ParsePacks(string[] args)
        {
            string? raw = ArgValue(args, "--packs");
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            HashSet<SpiraReforgeSpellPack> set = [];
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(part, ignoreCase: true, out SpiraReforgeSpellPack pack))
                    throw new ArgumentException($"Unknown pack '{part}'. Use Kimahri,Lulu,Yuna,Wakka,Rikku,Tidus,Auron.");
                set.Add(pack);
            }

            return set;
        }

        static void PrintVanillaBuffSummary(List<Ability_Command> reread)
        {
            int[] ids = [6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 89];
            Console.WriteLine();
            Console.WriteLine("--- Vanilla offensive buffs ---");
            foreach (int id in ids)
            {
                Ability_Command row = reread[id];
                string name = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)id, out string? n) ? n : $"#{id}";
                Console.WriteLine($"  #{id,3} {name,-16} P={row.AttackPower,3} Acc={row.AttackAccuracy,3} MP={row.CostMp,3}");
            }
        }

        static void PrintKimahriOverdriveSummary(List<Ability_Command> reread)
        {
            int[] ids = [104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115];
            Console.WriteLine();
            Console.WriteLine("--- Kimahri Ronso Rage Overdrive identity ---");
            foreach (int id in ids)
            {
                Ability_Command row = reread[id];
                string name = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)id, out string? n) ? n : $"#{id}";
                Console.WriteLine(
                    $"  #{id,3} {name,-16} P={row.AttackPower,3} OD={row.CostOverdrive,3} formula={row.DamageFormula,-18} destroy={row.FlagMisc3DestroyCaster}{EffectSummary(row)}");
            }

            int mightyBlue = reread.Count > 333 ? 333 : -1;
            if (mightyBlue >= 0)
            {
                Ability_Command blue = reread[mightyBlue];
                Console.WriteLine(
                    $"  #{mightyBlue,3} Mighty Guard (Blue) P={blue.AttackPower,3} MP={blue.CostMp,3}{EffectSummary(blue)}");
            }
        }

        static string EffectSummary(Ability_Command row)
        {
            var effects = new List<string>();
            if (row.FlagMisc2DelayS)
                effects.Add("DelayS");
            if (row.FlagMisc2DelayL)
                effects.Add("DelayL");
            if (row.FlagDamageCanCrit)
                effects.Add("Crit");
            if (row.FlagMisc3Piercing)
                effects.Add("Pierce");
            if (row.FlagDamageHeals)
                effects.Add("Heal");
            if (row.FlagDamageCleansesStatuses)
                effects.Add("Cleanse");
            if (row.FlagStatusDoom)
                effects.Add("Doom");
            if (row.FlagStatusCurse)
                effects.Add("Curse");
            if (row.FlagMisc2RandomTargets)
                effects.Add("RandTgt");
            if (row.ShatterChance != 0)
                effects.Add($"Shatter={row.ShatterChance}");
            if (row.FlagStatusEject)
                effects.Add("Eject");
            if (row.StatusChance.BreakArmor != 0)
                effects.Add($"BrkArm={row.StatusChance.BreakArmor}");
            if (row.StatusChance.BreakMental != 0)
                effects.Add($"BrkMnt={row.StatusChance.BreakMental}");
            if (row.StatusChance.Petrify != 0)
                effects.Add($"Petrify={row.StatusChance.Petrify}");
            if (row.StatusChance.Regen != 0)
                effects.Add($"Regen({row.StatusChance.Regen}/{row.StatusDuration.Regen})");
            if (row.StatusChance.Protect != 0)
                effects.Add($"Protect({row.StatusChance.Protect}/{row.StatusDuration.Protect})");
            if (row.StatusChance.Shell != 0)
                effects.Add($"Shell({row.StatusChance.Shell}/{row.StatusDuration.Shell})");
            if (row.StatusChance.Haste != 0)
                effects.Add($"Haste({row.StatusChance.Haste}/{row.StatusDuration.Haste})");
            if (row.StatBuffFlgs != 0)
                effects.Add($"StatBuff={row.StatBuffFlgs}x{row.StatBuffValue}");

            return effects.Count == 0 ? string.Empty : " " + string.Join(" ", effects);
        }

        static string ResolveOutputDir(string? outputArg)
        {
            string relative = outputArg ?? DefaultOutputDir;
            if (Path.IsPathRooted(relative))
                return relative;

            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.GetFullPath(Path.Combine(repoRoot, relative));
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
