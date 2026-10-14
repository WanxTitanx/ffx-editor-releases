using FFXProjectEditor.Services.Tools;
using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Services.Extras
{
    // ============================================================================
    // Ps2VgmStream_Service - PS2 .wd decode oracle through the bundled vgmstream
    // PURPOSE : decode PS2 PlayStation-ADPCM banks read-only via the app-private
    //           vgmstream CLI, promoting only fresh validated WAV outputs.
    // WHY     : vgmstream is the proved codec path (818/843 banks decode to
    //           coherent WAV; variants such as SDBse fail). The old service
    //           (1) resolved the CLI from overrides/ModsRoot/repo, (2) drained
    //           pipes synchronously (deadlock), and (3) accepted any matching
    //           historical file in the output directory as current-run success.
    // MAINT   : all process execution goes through BoundedToolProcessRunner with
    //           ArgumentList tokens; outputs are produced in owned staging and
    //           promoted with no-overwrite moves after RIFF validation.
    // ============================================================================

    internal static class Ps2VgmStream_Service
    {
        public static string? Locate() => FfxAudioToolsLocator.LocateVgmStream();
        public static bool IsAvailable => Locate() != null;

        public static string Metadata(string wdPath)
        {
            string? cli = Locate();
            if (cli == null)
                return "vgmstream is not bundled with this build.";

            ToolRunResult run = BoundedToolProcessRunner.Run(new ToolRunRequest(
                cli, ["-m", wdPath], TimeoutMs: 30_000));
            string text = string.IsNullOrWhiteSpace(run.StdOutTail) ? run.StdErrTail : run.StdOutTail;
            if (!run.Ok && string.IsNullOrWhiteSpace(text))
                text = run.Error.Length > 0 ? run.Error : "vgmstream could not open this bank (likely a blocked WD variant).";
            return text.Trim();
        }

        // Export every subsong to outDir as "<bank>_NN.wav". Returns (ok, message, count).
        public static (bool Ok, string Message, int Count) ExportAll(string wdPath, string outDir)
        {
            string? cli = Locate();
            if (cli == null)
                return (false, "vgmstream is not bundled with this build.", 0);

            return ExportAllCore(wdPath, outDir, cli, BoundedToolProcessRunner.Run);
        }

        // WHY: explicit process and promotion dependencies let the transactional
        // boundary exercise real failure paths without publishing test artifacts.
        internal static (bool Ok, string Message, int Count) ExportAllCore(
            string wdPath,
            string outDir,
            string cli,
            Func<ToolRunRequest, ToolRunResult> runTool,
            Action<string, string>? promoteFile = null)
        {
            promoteFile ??= static (source, destination) =>
                File.Move(source, destination, overwrite: false);
            string bank = Path.GetFileNameWithoutExtension(wdPath);
            string staging = Path.Combine(outDir, $".ffx-vgm-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(outDir);
                Directory.CreateDirectory(staging);

                string pattern = Path.Combine(staging, $"{bank}_?02s.wav");
                ToolRunResult run = runTool(new ToolRunRequest(
                    cli,
                    ["-S", "0", "-o", pattern, wdPath],
                    TimeoutMs: 120_000));

                if (!run.Ok)
                    return (false, FirstLine(run.StdErrTail, run.Error), 0);

                // Count only files produced inside the current run's owned staging.
                List<string> staged = Directory.EnumerateFiles(staging, $"{bank}_*.wav")
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (staged.Count == 0)
                    return (false, run.Ok
                        ? "vgmstream produced no WAV (blocked variant?)."
                        : FirstLine(run.StdErrTail, run.Error), 0);

                List<(string Staged, string Final)> ready = [];
                foreach (string stagedPath in staged)
                {
                    if (!IsFreshNonEmptyRiffWav(stagedPath))
                        return (false, $"vgmstream produced an invalid WAV: {Path.GetFileName(stagedPath)}", 0);

                    string finalPath = Path.Combine(outDir, Path.GetFileName(stagedPath));
                    if (File.Exists(finalPath))
                        return (false, $"Destination already exists: {Path.GetFileName(finalPath)}", 0);
                    ready.Add((stagedPath, finalPath));
                }

                List<string> published = [];
                try
                {
                    foreach ((string stagedPath, string finalPath) in ready)
                    {
                        promoteFile(stagedPath, finalPath);
                        published.Add(finalPath);
                    }
                }
                catch (Exception ex)
                {
                    // WHY: a WAV batch is one user-visible export. If any promotion
                    // fails, remove every destination published by this attempt.
                    for (int index = published.Count - 1; index >= 0; index--)
                    {
                        try { File.Delete(published[index]); }
                        catch (Exception rollbackError)
                        {
                            DebugLog.Error("Audio.Vgmstream", "Failed to roll back a published WAV.", rollbackError);
                        }
                    }
                    return (false, $"Could not publish WAV batch: {ex.Message}", 0);
                }

                return (true, $"Exported {staged.Count} WAV file(s).", staged.Count);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception cleanupError)
                {
                    DebugLog.Warn("Audio.Vgmstream", $"Failed to remove WAV staging: {cleanupError.Message}");
                }
            }
        }

        // Export a single subsong (1-based) to outWav. Returns (ok, message).
        public static (bool Ok, string Message) ExportOne(string wdPath, int subsong1Based, string outWav)
        {
            string? cli = Locate();
            if (cli == null)
                return (false, "vgmstream is not bundled with this build.");

            return ExportOneCore(wdPath, subsong1Based, outWav, cli, BoundedToolProcessRunner.Run);
        }

        // WHY: the process dependency is explicit so exit status and staged-file
        // shape remain independently verifiable at the publication boundary.
        internal static (bool Ok, string Message) ExportOneCore(
            string wdPath,
            int subsong1Based,
            string outWav,
            string cli,
            Func<ToolRunRequest, ToolRunResult> runTool)
        {
            string? dir = Path.GetDirectoryName(outWav);
            try
            {
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                if (File.Exists(outWav))
                    return (false, $"Destination already exists: {Path.GetFileName(outWav)}");

                // Decode into a unique owned staging name, then promote on validation.
                string stagedPath = $"{outWav}.ffx-tmp-{Guid.NewGuid():N}";
                try
                {
                    ToolRunResult run = runTool(new ToolRunRequest(
                        cli,
                        ["-s", subsong1Based.ToString(), "-o", stagedPath, wdPath]));

                    if (!run.Ok)
                        return (false, FirstLine(run.StdErrTail, run.Error));

                    if (!File.Exists(stagedPath))
                        return (false, "vgmstream produced no WAV (blocked variant?).");
                    if (!IsFreshNonEmptyRiffWav(stagedPath))
                        return (false, "vgmstream produced an invalid WAV file.");

                    File.Move(stagedPath, outWav, overwrite: false);
                    return (true, $"Decoded subsong {subsong1Based}.");
                }
                finally
                {
                    try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
                    catch (Exception cleanupError)
                    {
                        DebugLog.Warn("Audio.Vgmstream", $"Failed to remove staged WAV: {cleanupError.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        internal static bool IsFreshNonEmptyRiffWav(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length < 44 || info.LastWriteTimeUtc < DateTime.UtcNow.AddMinutes(-10))
                    return false;
                using var file = File.OpenRead(path);
                Span<byte> header = stackalloc byte[12];
                return file.Read(header) == 12
                    && header[..4].SequenceEqual("RIFF"u8)
                    && header[8..].SequenceEqual("WAVE"u8);
            }
            catch
            {
                return false;
            }
        }

        static string FirstLine(string stderr, string fallback)
        {
            string s = string.IsNullOrWhiteSpace(stderr) ? fallback : stderr;
            if (string.IsNullOrWhiteSpace(s)) return "vgmstream failed.";
            int nl = s.IndexOf('\n');
            return (nl > 0 ? s[..nl] : s).Trim();
        }
    }
}
