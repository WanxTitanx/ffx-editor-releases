// ============================================================================
// VgmstreamGameMusicToolRunner — process boundary for local FSB metadata/decode
// PURPOSE : invoke one explicitly supplied vgmstream executable without shell
//           parsing and convert its metadata output into a typed contract.
// WHY     : the release runtime must never auto-download tools or interpolate
//           user paths into a command string.
// MAINT   : keep arguments separated through ArgumentList and bound all waits.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public sealed record GameMusicStreamMetadata(
    int StreamIndex,
    int StreamCount,
    string StreamName,
    int SampleRate,
    int Channels,
    long LoopStartSamples,
    long LoopEndSamples,
    long TotalSamples);

public interface IGameMusicToolRunner
{
    Task<GameMusicStreamMetadata> ProbeAsync(
        string executablePath,
        string sourceFsbPath,
        int streamIndex,
        CancellationToken cancellationToken);

    Task DecodeAsync(
        string executablePath,
        string sourceFsbPath,
        GameMusicTrackDefinition track,
        string outputWavPath,
        CancellationToken cancellationToken);
}

public static class VgmstreamMetadataParser
{
    public static GameMusicStreamMetadata Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("vgmstream metadata is incomplete.");
        }

        int? sampleRate = ReadInt(text, @"^sample rate:\s*(\d+)\s+Hz\s*$");
        int? channels = ReadInt(text, @"^channels:\s*(\d+)\s*$");
        long? loopStart = ReadLong(text, @"^loop start:\s*(\d+)\s+samples");
        long? loopEnd = ReadLong(text, @"^loop end:\s*(\d+)\s+samples");
        long? totalSamples = ReadLong(text, @"^stream total samples:\s*(\d+)");
        int? streamCount = ReadInt(text, @"^stream count:\s*(\d+)\s*$");
        int? streamIndex = ReadInt(text, @"^stream index:\s*(\d+)\s*$");
        Match nameMatch = Regex.Match(
            text,
            @"^stream name:\s*(?<value>[^\r\n]+)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        if (sampleRate == null || channels == null || loopStart == null || loopEnd == null
            || totalSamples == null || streamCount == null || streamIndex == null || !nameMatch.Success)
        {
            throw new InvalidDataException("vgmstream metadata is incomplete.");
        }

        return new GameMusicStreamMetadata(
            streamIndex.Value,
            streamCount.Value,
            nameMatch.Groups["value"].Value.Trim(),
            sampleRate.Value,
            channels.Value,
            loopStart.Value,
            loopEnd.Value,
            totalSamples.Value);
    }

    private static int? ReadInt(string text, string pattern)
    {
        long? value = ReadLong(text, pattern);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static long? ReadLong(string text, string pattern)
    {
        Match match = Regex.Match(
            text,
            pattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success
            && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
                ? value
                : null;
    }
}

public sealed class VgmstreamGameMusicToolRunner : IGameMusicToolRunner
{
    private readonly TimeSpan timeout;

    public VgmstreamGameMusicToolRunner(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (this.timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<GameMusicStreamMetadata> ProbeAsync(
        string executablePath,
        string sourceFsbPath,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            executablePath,
            ["-s", streamIndex.ToString(CultureInfo.InvariantCulture), "-m", sourceFsbPath],
            cancellationToken).ConfigureAwait(false);
        return VgmstreamMetadataParser.Parse(result.StandardOutput);
    }

    public async Task DecodeAsync(
        string executablePath,
        string sourceFsbPath,
        GameMusicTrackDefinition track,
        string outputWavPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputWavPath))
        {
            throw new IOException("Refusing to overwrite an existing decoded WAV.");
        }

        await RunAsync(
            executablePath,
            [
                "-s",
                track.StreamIndex.ToString(CultureInfo.InvariantCulture),
                "-i",
                "-o",
                outputWavPath,
                sourceFsbPath,
            ],
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(outputWavPath))
        {
            throw new InvalidDataException("vgmstream exited successfully without producing a WAV.");
        }
    }

    private async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string fullExecutable = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutable))
        {
            throw new FileNotFoundException("The explicitly selected vgmstream executable was not found.");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = fullExecutable,
            WorkingDirectory = Path.GetDirectoryName(fullExecutable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the selected vgmstream executable.");
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"vgmstream failed with exit code {process.ExitCode}: {Bound(stderr, stdout)}");
            }

            return new ProcessResult(stdout, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"vgmstream exceeded the {timeout.TotalSeconds:0}-second timeout.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation/timeout is already the authoritative failure.
        }
    }

    private static string Bound(string primary, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        value = value.Trim();
        return value.Length <= 512 ? value : value[..512];
    }

    private sealed record ProcessResult(string StandardOutput, string StandardError);
}
