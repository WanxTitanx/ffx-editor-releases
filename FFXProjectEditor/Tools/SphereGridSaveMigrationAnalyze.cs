using System;
using FFXProjectEditor.FfxLib.Save;

namespace FFXProjectEditor.Tools
{
    internal static class SphereGridSaveMigrationAnalyze
    {
        public static int Run(string[] args)
        {
            if (args.Length < 4 || args[0] != "--spheregrid-save-migration-analyze")
            {
                Console.WriteLine("usage: --spheregrid-save-migration-analyze <save> <dat0X.dat> <dat1X.dat> [--json]");
                return 2;
            }

            try
            {
                FfxSaveSphereGridMigrationReport report =
                    FfxSaveSphereGridMigrationAnalyzer.Analyze(args[1], args[2], args[3]);

                bool json = args.Length > 4 && string.Equals(args[4], "--json", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine(json
                    ? FfxSaveSphereGridMigrationAnalyzer.ToJson(report)
                    : FfxSaveSphereGridMigrationAnalyzer.ToText(report));

                return report.CountsFitVanillaSave ? 0 : 3;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}
