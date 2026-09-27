using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Services.Tools;
using System;
using System.IO;
using Xunit;

namespace FFXProjectEditor.Tests.Audio;

// ============================================================================
// VgmstreamOutputPromotionTests - transactional WAV publication
// WHY: a structurally valid WAV is still failed output when vgmstream exits
//      nonzero, and a multi-file export must never expose a partial batch.
// ============================================================================
public sealed class VgmstreamOutputPromotionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ffx-vgm-promotion-").FullName;

    [Fact]
    public void Ps2Single_NonZeroExitDoesNotPublishValidLookingOutput()
    {
        string destination = Path.Combine(_root, "single.wav");

        (bool ok, _) = Ps2VgmStream_Service.ExportOneCore(
            "bank.wd", 1, destination, "/fake/vgmstream", request =>
            {
                WriteValidWav(OutputPath(request));
                return FailedRun();
            });

        Assert.False(ok);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void FsbSingle_NonZeroExitDoesNotPublishValidLookingOutput()
    {
        string destination = Path.Combine(_root, "fsb.wav");

        (bool ok, _) = FfxFsbVgmStream_Service.ExportSubsongCore(
            "bank.fsb", 0, destination, "/fake/vgmstream", request =>
            {
                WriteValidWav(OutputPath(request));
                return FailedRun();
            });

        Assert.False(ok);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.ffx-tmp-*"));
    }

    [Fact]
    public void FsbSingle_PromotionCollisionReturnsFalseAndPreservesDestination()
    {
        string destination = Path.Combine(_root, "collision.wav");
        byte[] foreign = [1, 2, 3, 4];

        (bool ok, _) = FfxFsbVgmStream_Service.ExportSubsongCore(
            "bank.fsb", 0, destination, "/fake/vgmstream", request =>
            {
                WriteValidWav(OutputPath(request));
                File.WriteAllBytes(destination, foreign);
                return SuccessfulRun();
            });

        Assert.False(ok);
        Assert.Equal(foreign, File.ReadAllBytes(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.ffx-tmp-*"));
    }

    [Fact]
    public void Ps2Multi_SecondMoveFailureRollsBackFirstDestination()
    {
        string firstDestination = Path.Combine(_root, "bank_01.wav");
        int moves = 0;

        (bool ok, _, int count) = Ps2VgmStream_Service.ExportAllCore(
            "bank.wd", _root, "/fake/vgmstream", request =>
            {
                string pattern = OutputPath(request);
                WriteValidWav(pattern.Replace("?02s", "01", StringComparison.Ordinal));
                WriteValidWav(pattern.Replace("?02s", "02", StringComparison.Ordinal));
                return SuccessfulRun();
            },
            (source, destination) =>
            {
                moves++;
                if (moves == 2)
                    throw new IOException("synthetic second move failure");
                File.Move(source, destination, overwrite: false);
            });

        Assert.False(ok);
        Assert.Equal(0, count);
        Assert.False(File.Exists(firstDestination));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private static ToolRunResult SuccessfulRun() =>
        new(ToolRunStatus.Completed, 0, "", "", "");

    private static ToolRunResult FailedRun() =>
        new(ToolRunStatus.Completed, 7, "", "tool failed", "");

    private static string OutputPath(ToolRunRequest request)
    {
        for (int index = 0; index < request.Arguments.Count - 1; index++)
        {
            if (request.Arguments[index] == "-o")
                return request.Arguments[index + 1];
        }

        throw new InvalidOperationException("The service did not pass an output path.");
    }

    private static void WriteValidWav(string path)
    {
        byte[] wav = new byte[44];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        File.WriteAllBytes(path, wav);
    }
}
