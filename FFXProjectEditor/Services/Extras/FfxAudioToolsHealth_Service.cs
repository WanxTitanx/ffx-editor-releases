using FFXProjectEditor.Services.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Services.Extras
{
    /// <summary>Probes bundled vgmstream + fsbext + fsbankcl beside the editor.</summary>
    public static class FfxAudioToolsHealth_Service
    {
        public sealed record ToolProbe(string Name, bool Found, bool Runnable, string? Path, string Message);

        public sealed record HealthReport(
            bool RequiredReady,
            bool FsbankClReady,
            IReadOnlyList<ToolProbe> Tools,
            string Summary);

        public static HealthReport Probe()
        {
            var tools = new List<ToolProbe>
            {
                ProbeCli("vgmstream", FfxAudioToolsLocator.LocateVgmStream(), "-h"),
                ProbeCli("fsbext", FfxAudioToolsLocator.LocateFsbExt(), null),
                ProbeCli("fsbankcl", FfxAudioToolsLocator.LocateFsbankCl(), "-help"),
            };

            bool required = FfxAudioToolsLocator.CustomSfxToolsReady
                && tools[0].Runnable && tools[1].Runnable;
            bool fsbank = tools[2].Found && tools[2].Runnable;
            string summary = BuildSummary(tools, required, fsbank);
            return new HealthReport(required, fsbank, tools, summary);
        }

        static ToolProbe ProbeCli(string name, string? path, string? args)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ToolProbe(name, false, false, path, "not found");

            try
            {
                var arguments = new List<string>();
                if (!string.IsNullOrEmpty(args))
                    arguments.Add(args);
                ToolRunResult run = BoundedToolProcessRunner.Run(new ToolRunRequest(
                    path, arguments,
                    WorkingDirectory: FfxAudioToolsLocator.ToolDirectory(path),
                    TimeoutMs: 15_000));
                if (run.Status != ToolRunStatus.Completed)
                    return new ToolProbe(name, true, false, path, run.Status.ToString().ToLowerInvariant());

                bool runnable = name switch
                {
                    "fsbext" => true,
                    "vgmstream" => run.StdOutTail.Contains("vgmstream", StringComparison.OrdinalIgnoreCase)
                        || run.StdErrTail.Contains("vgmstream", StringComparison.OrdinalIgnoreCase),
                    _ => run.ExitCode == 0,
                };
                return new ToolProbe(name, true, runnable, path, runnable ? "OK" : $"exit {run.ExitCode}");
            }
            catch (Exception ex)
            {
                return new ToolProbe(name, true, false, path, ex.Message);
            }
        }

        static string BuildSummary(IReadOnlyList<ToolProbe> tools, bool required, bool fsbank)
        {
            static string Short(string? p) => string.IsNullOrWhiteSpace(p) ? "?" : Path.GetFileName(Path.GetDirectoryName(p) ?? p);

            string line = string.Join(" | ", tools.Select(t =>
                $"{t.Name}={(t.Runnable ? "OK" : t.Found ? "FAIL" : "MISS")} ({Short(t.Path)})"));

            if (!required)
                return $"Audio tools incomplete — {line}";
            if (!fsbank)
                return $"Core tools ready; fsbankcl optional missing — {line}";
            return $"All battle audio tools ready — {line}";
        }
    }
}
