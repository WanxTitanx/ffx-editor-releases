using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    internal static class AudioToolsHealthRt2
    {
        public static int Run(string[] args)
        {
            string? repo = FFXProjectEditor.FfxLib.Ability.CommandSoundCorpusLoader.FindRepoRoot();
            string jsonOut = ArgValue(args, "--json")
                ?? Path.Combine(repo ?? ".", @"RuntimeTools\Fsb9999Lab\audio_tools_health.json");

            FfxAudioToolsHealth_Service.HealthReport report = FfxAudioToolsHealth_Service.Probe();

            Console.WriteLine("=== Battle audio tools health ===");
            Console.WriteLine(report.Summary);
            foreach (var t in report.Tools)
                Console.WriteLine($"  {t.Name}: found={t.Found} runnable={t.Runnable} path={t.Path} ({t.Message})");

            var verdict = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                required_ready = report.RequiredReady,
                fsbankcl_ready = report.FsbankClReady,
                summary = report.Summary,
                tools = report.Tools,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(jsonOut)!);
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"json: {jsonOut}");
            Console.WriteLine(report.RequiredReady ? "VERDICT: PASS" : "VERDICT: FAIL");
            return report.RequiredReady ? 0 : 1;
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
