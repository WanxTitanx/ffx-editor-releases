using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Tools
{
    // --sin-roster-from-btl — pull unique monsters from btl.bin encounter tables for one map prefix.
    //
    // Example:
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-roster-from-btl --prefix mcyt --btl "D:\...\btl.bin" --btl-root "D:\...\btl"
    internal static class SinAreaRosterFromBtlLab
    {
        public static int Run(string[] args)
        {
            string? prefix = null;
            string? btlPath = null;
            string? btlRoot = null;
            string? outCsv = null;
            bool listFormations = false;
            bool findChimera = false;

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--prefix" when i + 1 < args.Length:
                        prefix = args[++i].Trim().ToLowerInvariant();
                        break;
                    case "--btl" when i + 1 < args.Length:
                        btlPath = args[++i];
                        break;
                    case "--btl-root" when i + 1 < args.Length:
                        btlRoot = args[++i];
                        break;
                    case "--out" when i + 1 < args.Length:
                        outCsv = args[++i];
                        break;
                    case "--list-formations":
                        listFormations = true;
                        break;
                    case "--find-in-btl-root":
                        findChimera = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                }
            }

            if (string.IsNullOrWhiteSpace(prefix))
            {
                Console.Error.WriteLine("Missing --prefix (e.g. mcyt for Macalania fields).");
                PrintHelp();
                return 2;
            }

            btlPath ??= @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\kernel\btl.bin";
            btlRoot ??= Path.Combine(Path.GetDirectoryName(btlPath!)!, "..", "btl");
            btlRoot = Path.GetFullPath(btlRoot);

            if (!File.Exists(btlPath))
            {
                Console.Error.WriteLine($"btl.bin not found: {btlPath}");
                return 3;
            }

            try
            {
                if (listFormations)
                {
                    ListFormations(prefix!, btlPath!, btlRoot!);
                    return 0;
                }

                if (findChimera)
                {
                    FindMonstersInBattleRoot(prefix!, btlRoot!, 87, 88);
                    return 0;
                }

                IReadOnlyList<MobRow> rows = Extract(prefix!, btlPath!, btlRoot!);
                PrintReport(prefix, rows, btlPath, btlRoot);

                if (!string.IsNullOrWhiteSpace(outCsv))
                {
                    WriteCsv(outCsv, rows);
                    Console.WriteLine();
                    Console.WriteLine($"Wrote {outCsv}");
                }

                return rows.Count == 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        sealed record MobRow(
            string MonsterId,
            string Name,
            int DictionaryId,
            int FormationHits,
            int BattleFileHits,
            string SampleBattles);

        static void FindMonstersInBattleRoot(string battlePrefix, string btlRoot, params int[] dictIds)
        {
            var want = new HashSet<int>(dictIds);
            bool all = battlePrefix is "*" or "";
            Console.WriteLine(all
                ? $"=== ALL battles under '{btlRoot}' containing [{string.Join(",", want)}] ==="
                : $"=== battles matching '{battlePrefix}*' containing [{string.Join(",", want)}] ===");

            IEnumerable<string> dirs = all
                ? Directory.GetDirectories(btlRoot)
                : Directory.GetDirectories(btlRoot, battlePrefix + "*");

            foreach (string dir in dirs.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string battleId = Path.GetFileName(dir);
                string path = Path.Combine(dir, battleId + ".bin");
                if (!File.Exists(path))
                    continue;

                Battle_File battle;
                try { battle = Battle_File.Read(battleId, File.ReadAllBytes(path)); }
                catch { continue; }

                if (battle.Formation is null)
                    continue;

                var hits = battle.Formation.Slots
                    .Where(s => !s.IsEmpty && want.Contains(s.DictionaryId))
                    .Select(s => $"m{s.DictionaryId:D3} {s.MonsterName}")
                    .ToList();
                if (hits.Count > 0)
                    Console.WriteLine($"  {battleId}: {string.Join("; ", hits)}");
            }
        }

        static void ListFormations(string prefix, string btlPath, string btlRoot)
        {
            EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(btlPath));
            var tables = enc.Tables
                .Where(t => (t.Map ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Map, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id)
                .ToList();

            Console.WriteLine($"=== formations — prefix '{prefix}' ({tables.Count} tables) ===");
            foreach (EncounterTable_Entry table in tables)
            {
                Console.WriteLine($"map={table.Map} tableId={table.Id} groups={table.Groups.Count}");
                foreach (EncounterTable_Group group in table.Groups)
                {
                    int gi = table.Groups.ToList().IndexOf(group);
                    foreach (EncounterTable_Formation form in group.Formations)
                    {
                        string battlePath = Path.Combine(btlRoot, form.BattleId, form.BattleId + ".bin");
                        if (!File.Exists(battlePath))
                        {
                            Console.WriteLine($"  g{gi} {form.BattleId} w={form.Weight} danger={group.Danger} -> (missing bin)");
                            continue;
                        }

                        Battle_File battle = Battle_File.Read(form.BattleId, File.ReadAllBytes(battlePath));
                        string mobs = battle.Formation is null
                            ? "(no formation)"
                            : string.Join(", ", battle.Formation.Slots
                                .Where(s => !s.IsEmpty)
                                .Select(s => $"m{s.DictionaryId:D3} {s.MonsterName}"));
                        Console.WriteLine($"  g{gi} {form.BattleId} w={form.Weight} danger={group.Danger} bf={group.Battlefield} -> {mobs}");
                    }
                }
            }
        }

        static IReadOnlyList<MobRow> Extract(string prefix, string btlPath, string btlRoot)
        {
            EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(btlPath));
            var tables = enc.Tables
                .Where(t => (t.Map ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var byDict = new Dictionary<int, (string name, int formHits, HashSet<string> battles)>();

            foreach (EncounterTable_Entry table in tables)
            {
                foreach (EncounterTable_Group group in table.Groups)
                {
                    foreach (EncounterTable_Formation form in group.Formations)
                    {
                        string battleId = form.BattleId;
                        string battlePath = Path.Combine(btlRoot, battleId, battleId + ".bin");
                        if (!File.Exists(battlePath))
                            continue;

                        Battle_File battle = Battle_File.Read(battleId, File.ReadAllBytes(battlePath));
                        if (battle.Formation is null)
                            continue;

                        foreach (Battle_FormationSlot slot in battle.Formation.Slots.Where(s => !s.IsEmpty))
                        {
                            if (!byDict.TryGetValue(slot.DictionaryId, out var acc))
                                acc = (slot.MonsterName, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                            acc.battles.Add(battleId);
                            byDict[slot.DictionaryId] = (acc.name, acc.formHits + 1, acc.battles);
                        }
                    }
                }
            }

            // battle-file hits (unique battles containing mob)
            var battleHits = new Dictionary<int, int>();
            foreach (KeyValuePair<int, (string name, int formHits, HashSet<string> battles)> kv in byDict)
                battleHits[kv.Key] = kv.Value.battles.Count;

            return byDict
                .OrderBy(kv => kv.Key)
                .Select(kv => new MobRow(
                    $"m{kv.Key:D3}",
                    kv.Value.name,
                    kv.Key,
                    kv.Value.formHits,
                    battleHits[kv.Key],
                    string.Join("|", kv.Value.battles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(6))))
                .ToList();
        }

        static void PrintReport(string prefix, IReadOnlyList<MobRow> rows, string btlPath, string btlRoot)
        {
            Console.WriteLine($"=== Sin roster from btl.bin — prefix '{prefix}' ===");
            Console.WriteLine($"btl     : {btlPath}");
            Console.WriteLine($"btl-root: {btlRoot}");
            Console.WriteLine($"unique monsters: {rows.Count}");
            Console.WriteLine();
            Console.WriteLine("id     name                              slot-hits  battles  sample");
            foreach (MobRow r in rows)
            {
                Console.WriteLine($"{r.MonsterId,-6} {Truncate(r.Name, 32),-32} {r.FormationHits,9} {r.BattleFileHits,8}  {r.SampleBattles}");
            }
        }

        static void WriteCsv(string path, IReadOnlyList<MobRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generated from btl.bin encounter tables — random-field pool (not story gates)");
            sb.AppendLine("# monster_id,role,note,formation_slot_hits,battle_file_hits,sample_battles");
            foreach (MobRow r in rows)
            {
                string role = GuessRole(r);
                sb.AppendLine(string.Join(",",
                    r.MonsterId,
                    role,
                    CsvEscape(r.Name),
                    r.FormationHits.ToString(CultureInfo.InvariantCulture),
                    r.BattleFileHits.ToString(CultureInfo.InvariantCulture),
                    CsvEscape(r.SampleBattles)));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, sb.ToString());
        }

        static string GuessRole(MobRow r)
        {
            // Heuristic for SIN spread — refine manually for story bosses.
            if (r.DictionaryId is 124 or 125)
                return "story_boss";
            if (r.Name.Contains("Chimera", StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains("Evil Eye", StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains("Lord Ochu", StringComparison.OrdinalIgnoreCase))
                return "elite";
            return "trash";
        }

        static string CsvEscape(string s) =>
            s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..(max - 1)] + "…";

        static void PrintHelp()
        {
            Console.WriteLine("""
                --sin-roster-from-btl — unique monsters from encounter tables for a map prefix

                  --prefix <code>     map bucket prefix (Macalania = mcyt)
                  --btl <path>        btl.bin (default: D:\FFX Extracted\...\btl.bin)
                  --btl-root <dir>    battle bins folder (default: ../btl next to kernel)
                  --out <csv>         write spira-sin roster CSV
                """);
        }
    }
}
