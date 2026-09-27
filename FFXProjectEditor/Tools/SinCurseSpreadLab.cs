using System;
using System.Globalization;
using System.IO;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-curse-spread : offline Modo SIN area roll at the TRANSITION ENTER moment.
    //
    // Runtime: saiu do mapa → entrou de novo → janela de reload da IA → derived_seed aplicado aqui.
    //
    // Example (Thunder Plains, 1ª entrada vs re-entrada):
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-spread --region thunder_plains --master-seed 123456 --transition 0
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-spread --region thunder_plains --master-seed 123456 --transition 1
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-spread --region thunder_plains --master-seed 123456 --compare-transitions 3
    internal static class SinCurseSpreadLab
    {
        public static int Run(string[] args)
        {
            string? region = null;
            string? roster = null;
            string? outPath = null;
            string? threatCsv = null;
            int masterSeed = 42;
            int? derivedOverride = null;
            int transition = 0;
            int? compareTransitions = null;
            int? capOverride = null;
            double rate = SinCurseSpreadPlanner.DefaultInfectionRate;
            bool jsonStdout = false;
            bool listRegions = false;
            bool allPresets = false;

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--region" when i + 1 < args.Length:
                        region = args[++i];
                        break;
                    case "--roster" when i + 1 < args.Length:
                        roster = args[++i];
                        break;
                    case "--master-seed" when i + 1 < args.Length:
                        masterSeed = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--seed" when i + 1 < args.Length:
                        // Legacy: force derived seed directly (skips master+transition formula).
                        derivedOverride = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--transition" when i + 1 < args.Length:
                        transition = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--compare-transitions" when i + 1 < args.Length:
                        compareTransitions = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--rate" when i + 1 < args.Length:
                        rate = double.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--cap" when i + 1 < args.Length:
                        capOverride = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--threat-csv" when i + 1 < args.Length:
                        threatCsv = args[++i];
                        break;
                    case "--out" when i + 1 < args.Length:
                        outPath = args[++i];
                        break;
                    case "--json":
                        jsonStdout = true;
                        break;
                    case "--list-regions":
                        listRegions = true;
                        break;
                    case "--all-presets":
                        allPresets = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                }
            }

            if (listRegions)
            {
                PrintRegions(threatCsv ?? SinCurseSpreadPlanner.DefaultThreatCsv());
                return 0;
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                Console.Error.WriteLine("Missing --region <id> (e.g. thunder_plains). Use --list-regions.");
                PrintHelp();
                return 2;
            }

            string rosterPath = roster ?? SinCurseSpreadPlanner.DefaultRosterPath(region);
            if (!File.Exists(rosterPath))
            {
                Console.Error.WriteLine($"Roster not found: {rosterPath}");
                Console.Error.WriteLine("Create mods/Spira Reforge/arena/spira-sin-area-rosters/<region>.csv or pass --roster.");
                return 3;
            }

            try
            {
                if (compareTransitions is > 0)
                {
                    for (int t = 0; t < compareTransitions.Value; t++)
                    {
                        SinCurseSpreadPlanner.SpreadResult r = PlanOnce(
                            region, rosterPath, masterSeed, t, derivedOverride, rate, capOverride, !allPresets, threatCsv);
                        Console.WriteLine(SinCurseSpreadPlanner.FormatHumanReport(r));
                        if (t + 1 < compareTransitions.Value)
                            Console.WriteLine(new string('-', 72));
                    }

                    return 0;
                }

                SinCurseSpreadPlanner.SpreadResult result = PlanOnce(
                    region, rosterPath, masterSeed, transition, derivedOverride, rate, capOverride, !allPresets, threatCsv);
                string json = SinCurseSpreadPlanner.ToSidecarJson(result);

                if (!string.IsNullOrEmpty(outPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                    File.WriteAllText(outPath, json);
                    Console.WriteLine($"Wrote {outPath}");
                }

                if (jsonStdout || string.IsNullOrEmpty(outPath))
                {
                    if (jsonStdout)
                        Console.WriteLine(json);
                    else
                        Console.WriteLine(SinCurseSpreadPlanner.FormatHumanReport(result));
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        static SinCurseSpreadPlanner.SpreadResult PlanOnce(
            string region,
            string rosterPath,
            int masterSeed,
            int transition,
            int? derivedOverride,
            double rate,
            int? capOverride,
            bool aNowOnly,
            string? threatCsv)
        {
            var options = new SinCurseSpreadPlanner.SpreadOptions
            {
                RegionId = region,
                RosterPath = rosterPath,
                MasterSeed = masterSeed,
                TransitionIndex = transition,
                DerivedSeedOverride = derivedOverride,
                InfectionRate = rate,
                AreaThreatCapOverride = capOverride,
                ANowPresetsOnly = aNowOnly,
                ThreatCsvPath = threatCsv,
            };

            return SinCurseSpreadPlanner.Plan(options);
        }

        static void PrintRegions(string csvPath)
        {
            Console.WriteLine($"=== spira-sin-area-threat ({csvPath}) ===");
            if (!File.Exists(csvPath))
            {
                Console.Error.WriteLine("CSV not found.");
                return;
            }

            foreach (string line in File.ReadAllLines(csvPath))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("region_id", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] p = t.Split(',');
                if (p.Length >= 4)
                    Console.WriteLine($"  {p[0],-22} cap=T{p[3].Trim()}  ({p[1].Trim()})");
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("""
                --sin-curse-spread — roll Sin Curse at SCREEN TRANSITION ENTER (offline preview)

                Runtime contract: leave map → enter map → AI reload window → apply derived_seed NOW.
                Same master_seed + higher transition_index ⇒ new spread (not fixed forever per species).

                  --region <id>              region_id from spira-sin-area-threat.csv (required)
                  --master-seed <int>        save-level seed (SpiraSinState.seed; default 42)
                  --transition <int>         enter count for this region (0=first, 1=left+came back, …)
                  --compare-transitions <n>  print n consecutive enters (reshuffle preview)
                  --seed <int>               legacy: force derived seed (skip master+transition mix)
                  --rate <0..1>              infection attempt fraction (default 0.30)
                  --roster / --cap / --out / --json / --all-presets / --list-regions
                """);
        }
    }
}
