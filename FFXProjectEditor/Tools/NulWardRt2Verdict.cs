using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Tools
{
    /// <summary>Parse %TEMP%\ffx-hooks.log for NulWard RT2 core + edge scenarios.</summary>
    internal static class NulWardRt2Verdict
    {
        const string DefaultLogPath = @"%TEMP%\ffx-hooks.log";
        const string DefaultOutput = @"RuntimeTools\NulWardLab\nul_ward_rt2_verdict.json";

        public static int Run(string[] args)
        {
            try
            {
                string logPath = Expand(ArgValue(args, "--log") ?? DefaultLogPath);
                string outputPath = ArgValue(args, "--output") ?? DefaultOutput;
                bool requireRuntime = !args.Any(a => a.Equals("--offline-ok", StringComparison.OrdinalIgnoreCase));

                Console.WriteLine("=== Nul Ward RT2 verdict parser ===");
                Console.WriteLine($"log    : {logPath}");
                Console.WriteLine($"output : {outputPath}");

                var lines = File.Exists(logPath)
                    ? File.ReadAllLines(logPath).ToList()
                    : new List<string>();

                int castRadiant = Count(lines, @"NulWard cast #\d+ Radiant");
                int castUmbral = Count(lines, @"NulWard cast #\d+ Umbral");
                int nullHoly = Count(lines, @"NulWard null #\d+.*elem=0x10.*dmgOut=0");
                int nullDark = Count(lines, @"NulWard null #\d+.*elem=80.*dmgOut=0");
                int nullAny = Count(lines, @"NulWard null #\d+");
                int installOk = Count(lines, @"NulWard install result ok=1");
                int p16Lines = Count(lines, @"NulWard P16 #");

                bool corePass = castRadiant >= 1 && castUmbral >= 1 && nullHoly >= 1 && nullDark >= 1;
                bool runtimeEvidence = lines.Count > 0 && installOk >= 1;

                var scenarios = new List<object>
                {
                    Scenario("core_radiant_cast", castRadiant >= 1, $"casts={castRadiant}"),
                    Scenario("core_umbral_cast", castUmbral >= 1, $"casts={castUmbral}"),
                    Scenario("core_null_holy", nullHoly >= 1, $"nulls={nullHoly}"),
                    Scenario("core_null_dark", nullDark >= 1, $"nulls={nullDark}"),
                    Scenario("dll_install_ok", installOk >= 1, $"installOk={installOk}"),
                    Scenario("p16_experiment", p16Lines >= 0, $"p16LogLines={p16Lines}"),
                };

                var payload = new
                {
                    generated = DateTime.UtcNow.ToString("o"),
                    logPath,
                    lineCount = lines.Count,
                    runtimeEvidence,
                    corePass,
                    pass = requireRuntime ? (runtimeEvidence && corePass) : corePass,
                    counts = new
                    {
                        castRadiant,
                        castUmbral,
                        nullHoly,
                        nullDark,
                        nullAny,
                        installOk,
                        p16Lines,
                    },
                    scenarios,
                    note = requireRuntime
                        ? "PASS requires in-game log with cast+null Holy/Dark. Run RuntimeTools/NulWardLab/run_rt2_gate.ps1 after battle."
                        : "Offline mode — counts only; runtime not required.",
                };

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"json: {outputPath}");
                Console.WriteLine(payload.pass ? "VERDICT: PASS" : "VERDICT: FAIL (run in-game RT2 or check log path)");
                return payload.pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static object Scenario(string id, bool pass, string detail) => new { id, pass, detail };

        static int Count(List<string> lines, string pattern) =>
            lines.Count(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase));

        static string Expand(string path) =>
            Environment.ExpandEnvironmentVariables(path);

        static string? ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
