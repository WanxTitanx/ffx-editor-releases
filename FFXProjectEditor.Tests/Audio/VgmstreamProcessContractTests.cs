using FFXProjectEditor.Services.Tools;
using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace FFXProjectEditor.Tests.Audio;

// ============================================================================
// VgmstreamProcessContractTests - adversarial child-process behavior
// WHY: every bundled CLI runs through BoundedToolProcessRunner. These tests
//      prove the runner survives the failure modes that deadlocked or ballooned
//      the previous synchronous readers: pipe saturation on either stream,
//      concurrent saturation, unbounded output, and hangs.
// ============================================================================
public sealed class VgmstreamProcessContractTests
{
    static ToolRunResult RunSh(string script, int timeoutMs = 5_000, long maxStreamBytes = 8L * 1024 * 1024) =>
        BoundedToolProcessRunner.Run(new ToolRunRequest(
            "/bin/sh", ["-c", script],
            TimeoutMs: timeoutMs,
            MaxStreamBytes: maxStreamBytes));

    [Fact]
    public void SmallOutput_CompletesWithExactTail()
    {
        ToolRunResult result = RunSh("printf hello");
        Assert.True(result.Ok);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StdOutTail);
    }

    [Fact]
    public void StdoutFlood_IsCappedAndDoesNotDeadlock()
    {
        Stopwatch watch = Stopwatch.StartNew();
        ToolRunResult result = RunSh("yes flood-stdout | head -c 20M", timeoutMs: 10_000, maxStreamBytes: 1024 * 1024);
        watch.Stop();
        Assert.Equal(ToolRunStatus.OutputOverflow, result.Status);
        Assert.True(result.StdOutTail.Length <= 128 * 1024);
        Assert.Contains("exceeded", result.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3),
            $"overflow termination took {watch.Elapsed.TotalSeconds:F2}s");
    }

    [Fact]
    public void StderrFlood_IsCappedAndDoesNotDeadlock()
    {
        // The previous sequential reader deadlocked here: stdout never EOFs while
        // stderr fills its pipe. Concurrent drains must survive this shape.
        Stopwatch watch = Stopwatch.StartNew();
        ToolRunResult result = RunSh(
            "yes flood-stderr >&2 | head -c 20M; sleep 0.1",
            timeoutMs: 10_000,
            maxStreamBytes: 1024 * 1024);
        watch.Stop();
        Assert.Equal(ToolRunStatus.OutputOverflow, result.Status);
        Assert.True(result.StdErrTail.Length <= 128 * 1024);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3),
            $"overflow termination took {watch.Elapsed.TotalSeconds:F2}s");
    }

    [Fact]
    public void ConcurrentBothStreams_CompletesWithBothTails()
    {
        ToolRunResult result = RunSh(
            "printf out-one; printf err-one >&2; printf out-two; printf err-two >&2",
            timeoutMs: 10_000);
        Assert.True(result.Ok);
        Assert.Contains("out-one", result.StdOutTail);
        Assert.Contains("out-two", result.StdOutTail);
        Assert.Contains("err-one", result.StdErrTail);
        Assert.Contains("err-two", result.StdErrTail);
    }

    [Fact]
    public void HangingChild_IsKilledByTimeout()
    {
        Stopwatch watch = Stopwatch.StartNew();
        ToolRunResult result = RunSh("sleep 30", timeoutMs: 1_500);
        watch.Stop();
        Assert.Equal(ToolRunStatus.TimedOut, result.Status);
        Assert.True(watch.Elapsed.TotalSeconds < 10, $"timeout kill took {watch.Elapsed.TotalSeconds:F1}s");
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public void NonZeroExit_IsReportedNotMasked()
    {
        ToolRunResult result = RunSh("printf bad >&2; exit 3");
        Assert.Equal(ToolRunStatus.Completed, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Ok);
        Assert.Equal("bad", result.StdErrTail);
    }

    [Fact]
    public void MissingExecutable_FailsToStartWithoutThrowing()
    {
        ToolRunResult result = BoundedToolProcessRunner.Run(new ToolRunRequest(
            "/nonexistent/ffx-missing-tool", ["-h"]));
        Assert.Equal(ToolRunStatus.StartFailed, result.Status);
        Assert.NotEqual("", result.Error);
    }

    [Fact]
    public void TailKeepsOnlyTheEndOfLargeCompletedOutput()
    {
        // ~6 KB total with a 1 KB tail: completes, the front is evicted, the end survives.
        ToolRunResult result = BoundedToolProcessRunner.Run(new ToolRunRequest(
            "/bin/sh", ["-c", "for i in $(seq 1 200); do printf \"0123456789ABCDEFGHIJtail-%s\\n\" $i; done"],
            TimeoutMs: 10_000,
            MaxStreamBytes: 4 * 1024 * 1024,
            TailBytes: 1024));
        Assert.Equal(ToolRunStatus.Completed, result.Status);
        Assert.EndsWith("tail-200", result.StdOutTail);
        Assert.DoesNotContain("tail-1\n", result.StdOutTail);
    }

    [Fact]
    public void Arguments_AreTokenizedNeverConcatenated()
    {
        // A path with spaces must arrive as ONE argument; string Arguments would split it.
        string pathWithSpaces = Path.Combine(Path.GetTempPath(), "ffx dir with spaces", "file name.wav");
        ToolRunResult result = BoundedToolProcessRunner.Run(new ToolRunRequest(
            "/bin/sh", ["-c", "printf %s \"$1\"", "--", pathWithSpaces],
            TimeoutMs: 10_000));
        Assert.True(result.Ok);
        Assert.Equal(pathWithSpaces, result.StdOutTail);
    }
}
