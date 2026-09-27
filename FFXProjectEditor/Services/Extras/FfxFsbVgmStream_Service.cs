using FFXProjectEditor.Services.Tools;
using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Services.Extras
{
    /// <summary>vgmstream oracle for FMOD FSB banks (battle SFX 9999_bank00.fsb).</summary>
    public static class FfxFsbVgmStream_Service
    {
        public static bool IsAvailable => FfxAudioToolsLocator.VgmStreamAvailable;

        public sealed record WavFormatInfo(int Channels, int SampleRate, double DurationSeconds);

        static ToolRunResult Run(IReadOnlyList<string> arguments, int timeoutMs = 120000)
        {
            string? cli = FfxAudioToolsLocator.LocateVgmStream();
            if (cli == null)
                return new ToolRunResult(ToolRunStatus.StartFailed, -1, "", "", "vgmstream-cli not found");

            return BoundedToolProcessRunner.Run(new ToolRunRequest(
                cli, arguments,
                WorkingDirectory: Path.GetDirectoryName(cli) ?? AppContext.BaseDirectory,
                TimeoutMs: timeoutMs));
        }

        public static string Metadata(string fsbPath)
        {
            ToolRunResult r = Run(["-m", fsbPath]);
            string text = string.IsNullOrWhiteSpace(r.StdOutTail) ? r.StdErrTail : r.StdOutTail;
            return text.Trim();
        }

        public static int? TryCountSubsongs(string fsbPath)
        {
            string meta = Metadata(fsbPath);
            Match m = Regex.Match(meta, @"stream count:\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                return n;
            m = Regex.Match(meta, @"subsongs:\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out n))
                return n;
            return null;
        }

        public static (bool Ok, string Message) ExportSubsong(string fsbPath, int subsong0Based, string outWav)
        {
            string? cli = FfxAudioToolsLocator.LocateVgmStream();
            if (cli == null)
                return (false, "vgmstream-cli not found");

            return ExportSubsongCore(
                fsbPath, subsong0Based, outWav, cli, BoundedToolProcessRunner.Run);
        }

        // WHY: the process dependency is explicit so a valid-looking partial WAV
        // can be tested independently from the tool's authoritative exit status.
        internal static (bool Ok, string Message) ExportSubsongCore(
            string fsbPath,
            int subsong0Based,
            string outWav,
            string cli,
            Func<ToolRunRequest, ToolRunResult> runTool)
        {
            string finalPath = Path.GetFullPath(outWav);
            string directory = Path.GetDirectoryName(finalPath)!;
            Directory.CreateDirectory(directory);

            if (File.Exists(finalPath))
                return (false, "destination already exists");

            string stagedPath = Path.Combine(
                directory,
                $".{Path.GetFileNameWithoutExtension(finalPath)}.ffx-tmp-{Guid.NewGuid():N}.wav");
            try
            {
                // WHY: vgmstream may leave valid-looking bytes before a nonzero exit.
                // The final path becomes visible only after the full run succeeds.
                ToolRunResult r = runTool(new ToolRunRequest(
                    cli,
                    ["-s", (subsong0Based + 1).ToString(), "-o", stagedPath, fsbPath],
                    WorkingDirectory: Path.GetDirectoryName(cli) ?? AppContext.BaseDirectory,
                    TimeoutMs: 120_000));
                if (!r.Ok)
                    return (false, FailureMessage(r));
                if (!File.Exists(stagedPath) || !Ps2VgmStream_Service.IsFreshNonEmptyRiffWav(stagedPath))
                    return (false, "no valid WAV produced");

                File.Move(stagedPath, finalPath, overwrite: false);
                return (true, "ok");
            }
            catch (Exception ex)
            {
                DebugLog.Warn(
                    "Audio.Vgmstream",
                    $"Failed to publish staged FSB WAV: {ex.GetType().Name}: {ex.Message}");
                return (false, $"could not publish WAV: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
                catch (Exception cleanupError)
                {
                    DebugLog.Warn("Audio.Vgmstream", $"Failed to remove staged FSB WAV: {cleanupError.Message}");
                }
            }
        }

        private static string FailureMessage(ToolRunResult result)
        {
            string message = string.IsNullOrWhiteSpace(result.StdErrTail)
                ? result.Error
                : result.StdErrTail;
            if (string.IsNullOrWhiteSpace(message))
                return "vgmstream failed";
            int newline = message.IndexOf('\n');
            return (newline >= 0 ? message[..newline] : message).Trim();
        }

        public static WavFormatInfo? TryReadWavFormat(string wavPath)
        {
            try
            {
                using var reader = new NAudio.Wave.WaveFileReader(wavPath);
                return new WavFormatInfo(
                    reader.WaveFormat.Channels,
                    reader.WaveFormat.SampleRate,
                    reader.TotalTime.TotalSeconds);
            }
            catch
            {
                return null;
            }
        }

        public static (bool Ok, string Message) ValidateReplacement(string originalWav, string replacementWav)
        {
            WavFormatInfo? orig = TryReadWavFormat(originalWav);
            WavFormatInfo? repl = TryReadWavFormat(replacementWav);
            if (orig == null || repl == null)
                return (false, "Could not read WAV format (need PCM WAV).");

            if (orig.Channels != repl.Channels)
                return (false, $"Channel mismatch: original={orig.Channels}, replacement={repl.Channels}");
            if (orig.SampleRate != repl.SampleRate)
                return (false, $"Sample rate mismatch: original={orig.SampleRate}, replacement={repl.SampleRate}");
            if (repl.DurationSeconds > orig.DurationSeconds + 0.05)
                return (false, $"Replacement longer than original ({repl.DurationSeconds:F2}s > {orig.DurationSeconds:F2}s). fsbext requires same or shorter.");

            return (true, "Format OK for FSB rebuild.");
        }
    }
}
