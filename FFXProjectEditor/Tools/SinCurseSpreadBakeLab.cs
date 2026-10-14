using System;
using System.Globalization;
using System.IO;
using FFXProjectEditor.FfxLib.Ai.Sin;

namespace FFXProjectEditor.Tools
{
    // --sin-curse-bake : apply spread manifest → mod-folder m###.bin (AppendGuardedAction + backup)
    //
    // Typical flow:
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-spread --region thunder_plains --master-seed 42 --transition 0 --out work/sin/thunder_t0.json
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-bake --spread work/sin/thunder_t0.json --vanilla-root "D:\FFX Extracted\FFX\ffx_ps2" --dry-run
    //   dotnet run --project FFXProjectEditor -c Release -- --sin-curse-bake --spread work/sin/thunder_t0.json --vanilla-root "D:\FFX Extracted\FFX\ffx_ps2"
    internal static class SinCurseSpreadBakeLab
    {
        public static int Run(string[] args)
        {
            string? spread = null;
            string? modRoot = null;
            string? vanillaRoot = null;
            string? reportPath = null;
            bool dryRun = false;
            bool failOnUnmapped = false;
            bool planAndBake = false;
            bool possessedOpener = false;
            string? region = null;
            int masterSeed = 42;
            int transition = 0;
            double rate = SinCurseSpreadPlanner.DefaultInfectionRate;

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--spread" when i + 1 < args.Length:
                        spread = args[++i];
                        break;
                    case "--mod-mon-root" when i + 1 < args.Length:
                        modRoot = args[++i];
                        break;
                    case "--vanilla-root" when i + 1 < args.Length:
                        vanillaRoot = args[++i];
                        break;
                    case "--report" when i + 1 < args.Length:
                        reportPath = args[++i];
                        break;
                    case "--region" when i + 1 < args.Length:
                        region = args[++i];
                        break;
                    case "--master-seed" when i + 1 < args.Length:
                        masterSeed = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--transition" when i + 1 < args.Length:
                        transition = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--rate" when i + 1 < args.Length:
                        rate = double.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--fail-on-unmapped":
                        failOnUnmapped = true;
                        break;
                    case "--plan-and-bake":
                        planAndBake = true;
                        break;
                    case "--possessed-opener":
                        possessedOpener = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                }
            }

            try
            {
                if (planAndBake)
                {
                    if (string.IsNullOrWhiteSpace(region))
                    {
                        Console.Error.WriteLine("--plan-and-bake requires --region");
                        return 2;
                    }

                    string roster = SinCurseSpreadPlanner.DefaultRosterPath(region);
                    var plan = SinCurseSpreadPlanner.Plan(new SinCurseSpreadPlanner.SpreadOptions
                    {
                        RegionId = region,
                        RosterPath = roster,
                        MasterSeed = masterSeed,
                        TransitionIndex = transition,
                        InfectionRate = rate,
                    });

                    spread = Path.Combine(Path.GetTempPath(), $"spira-sin-spread-{region}-t{transition}.json");
                    File.WriteAllText(spread, SinCurseSpreadPlanner.ToSidecarJson(plan));
                    Console.WriteLine($"Planned → {spread} ({plan.Summary.Infected}/{plan.Summary.TotalSlots} infected)");
                }

                if (string.IsNullOrWhiteSpace(spread))
                {
                    Console.Error.WriteLine("Missing --spread <json> (or use --plan-and-bake --region ...).");
                    PrintHelp();
                    return 2;
                }

                var bake = SinCurseSpreadBaker.Bake(new SinCurseSpreadBaker.BakeOptions
                {
                    SpreadJsonPath = spread,
                    ModMonRoot = modRoot,
                    VanillaFfxPs2Root = vanillaRoot,
                    DryRun = dryRun,
                    SkipUnmapped = !failOnUnmapped,
                    ApplyPossessedOpener = possessedOpener,
                    ReportPath = reportPath,
                });

                Console.WriteLine(SinCurseSpreadBaker.FormatHumanReport(bake));
                if (bake.Summary.Failed > 0)
                    return 1;
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("""
                --sin-curse-bake — patch mod m###.bin from spira-sin-spread manifest

                  --spread <json>              spread sidecar from --sin-curse-spread (required)
                  --plan-and-bake              run spread planner first (needs --region)
                  --region / --master-seed / --transition / --rate   planner flags (with --plan-and-bake)
                  --vanilla-root <ffx_ps2>     seed mod copies from extracted game when missing
                  --mod-mon-root <path>        default: mods/Spira Reforge/data/.../battle/mon
                  --dry-run                    emit+validate only, no disk write
                  --possessed-opener           prepend Possessed by Yu Yevon! before each SIN preset
                  --fail-on-unmapped           treat unmapped presets as errors
                  --report <json>              write bake report (non-dry-run)
                """);
        }
    }
}
