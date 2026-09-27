using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Services.Tools
{
    // ============================================================================
    // BoundedToolProcessRunner - the only sanctioned way to spawn bundled CLIs
    // PURPOSE : run vgmstream/fsbext/fsbankcl with concurrent drained pipes, hard
    //           byte ceilings, ring-buffer tails, and process-tree termination.
    // WHY     : the previous services drained stdout then stderr synchronously, so a
    //           child filling one pipe deadlocked the editor before any timeout;
    //           ReadToEnd could also grow without bound on hostile output.
    // MAINT   : every external-tool call site must go through this runner. Never
    //           call Process.Start directly for bundled tools, and never use the
    //           string Arguments property (argument injection via spaces/quotes).
    // ============================================================================

    public enum ToolRunStatus
    {
        Completed,
        TimedOut,
        OutputOverflow,
        StartFailed,
        Failed,
    }

    public sealed record ToolRunRequest(
        string FileName,
        IReadOnlyList<string> Arguments,
        string? WorkingDirectory = null,
        int TimeoutMs = 30_000,
        long MaxStreamBytes = 8L * 1024 * 1024,
        int TailBytes = 64 * 1024);

    public sealed record ToolRunResult(
        ToolRunStatus Status,
        int ExitCode,
        string StdOutTail,
        string StdErrTail,
        string Error)
    {
        public bool Ok => Status == ToolRunStatus.Completed && ExitCode == 0;
    }

    public static class BoundedToolProcessRunner
    {
        private static readonly TimeSpan DrainJoinDeadline = TimeSpan.FromSeconds(2);

        public static ToolRunResult Run(ToolRunRequest request) =>
            RunAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        public static async Task<ToolRunResult> RunAsync(
            ToolRunRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                WorkingDirectory = request.WorkingDirectory ?? Path.GetDirectoryName(request.FileName),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in request.Arguments)
                startInfo.ArgumentList.Add(argument);

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("The tool process was not created.");
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new ToolRunResult(ToolRunStatus.StartFailed, -1, "", "", error.Message);
            }

            try
            {
                using (process)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(request.TimeoutMs);
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                    var overflow = new OverflowState(waitCts);
                    Task<string> stdoutTask = DrainAsync(
                        process.StandardOutput.BaseStream, request, overflow, "stdout");
                    Task<string> stderrTask = DrainAsync(
                        process.StandardError.BaseStream, request, overflow, "stderr");

                    bool exited;
                    try
                    {
                        await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
                        exited = true;
                    }
                    catch (OperationCanceledException)
                    {
                        exited = false;
                    }

                    if (!exited || overflow.Triggered)
                    {
                        KillProcessTree(process);
                        // WHY: a failed kill or inherited pipe handle must not replace the
                        // command timeout with an unbounded drain join.
                        (string drainedStdOut, string drainedStdErr) = await JoinDrainsAsync(
                            process, stdoutTask, stderrTask).ConfigureAwait(false);
                        string reason = overflow.Triggered
                            ? $"tool output exceeded {request.MaxStreamBytes} bytes"
                            : cancellationToken.IsCancellationRequested
                                ? "tool run was cancelled"
                                : $"tool timed out after {request.TimeoutMs} ms";
                        return new ToolRunResult(
                            overflow.Triggered ? ToolRunStatus.OutputOverflow : ToolRunStatus.TimedOut,
                            -1,
                            drainedStdOut,
                            drainedStdErr,
                            reason);
                    }

                    string stdout = await stdoutTask.ConfigureAwait(false);
                    string stderr = await stderrTask.ConfigureAwait(false);
                    return new ToolRunResult(
                        ToolRunStatus.Completed,
                        process.ExitCode,
                        stdout,
                        stderr,
                        "");
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                KillProcessTree(process);
                return new ToolRunResult(ToolRunStatus.Failed, -1, "", "", error.Message);
            }
        }

        static async Task<string> DrainAsync(
            Stream stream,
            ToolRunRequest request,
            OverflowState overflow,
            string name)
        {
            var buffer = new byte[8192];
            var tail = new LinkedList<byte[]>();
            int tailTotal = 0;
            long total = 0;

            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    total += read;
                    if (total > request.MaxStreamBytes)
                    {
                        overflow.Trigger();
                        break;
                    }

                    // Pipe reads may return fewer bytes than requested; store exactly what
                    // arrived so the tail bookkeeping and the final BlockCopy stay in sync.
                    var valid = new byte[read];
                    Buffer.BlockCopy(buffer, 0, valid, 0, read);
                    tail.AddLast(valid);
                    tailTotal += valid.Length;
                    while (tailTotal > request.TailBytes && tail.Count > 0)
                    {
                        // Byte-level trim: a single large read can exceed the tail budget
                        // on its own, so evicting whole chunks is not enough.
                        byte[] front = tail.First!.Value;
                        int excess = tailTotal - request.TailBytes;
                        if (front.Length <= excess)
                        {
                            tailTotal -= front.Length;
                            tail.RemoveFirst();
                        }
                        else
                        {
                            var trimmed = new byte[front.Length - excess];
                            Buffer.BlockCopy(front, excess, trimmed, 0, trimmed.Length);
                            tail.RemoveFirst();
                            tail.AddFirst(trimmed);
                            tailTotal -= excess;
                        }
                    }
                }
            }
            catch (IOException)
            {
                // A killed child closes pipes abruptly; keep whatever tail we have.
            }
            catch (ObjectDisposedException)
            {
                // WHY: the bounded join closes redirected pipes when a killed process
                // leaves an inherited handle open; the completed tail is still valid.
            }

            var bytes = new byte[tailTotal];
            int offset = 0;
            foreach (byte[] chunk in tail)
            {
                Buffer.BlockCopy(chunk, 0, bytes, offset, chunk.Length);
                offset += chunk.Length;
            }
            _ = name;
            return Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n');
        }

        static void KillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception error)
            {
                DebugLog.Error("Audio.ToolRunner", "Failed to terminate the tool process tree.", error);
            }
        }

        static async Task<(string StdOut, string StdErr)> JoinDrainsAsync(
            Process process,
            Task<string> stdoutTask,
            Task<string> stderrTask)
        {
            Task drains = Task.WhenAll(stdoutTask, stderrTask);
            try
            {
                await drains.WaitAsync(DrainJoinDeadline).ConfigureAwait(false);
            }
            catch (TimeoutException error)
            {
                DebugLog.Error("Audio.ToolRunner", "Timed out while joining tool output drains.", error);
                // Closing redirected streams releases any drain still held by an inherited
                // pipe descriptor; completed tails remain available below.
                try { process.StandardOutput.Close(); }
                catch (Exception closeError)
                {
                    DebugLog.Error("Audio.ToolRunner", "Failed to close the tool stdout drain.", closeError);
                }
                try { process.StandardError.Close(); }
                catch (Exception closeError)
                {
                    DebugLog.Error("Audio.ToolRunner", "Failed to close the tool stderr drain.", closeError);
                }
            }

            return (
                stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "",
                stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "");
        }

        sealed class OverflowState
        {
            private readonly CancellationTokenSource _waitCts;
            private int _triggered;

            public OverflowState(CancellationTokenSource waitCts) => _waitCts = waitCts;

            public bool Triggered => Volatile.Read(ref _triggered) != 0;

            public void Trigger()
            {
                if (Interlocked.Exchange(ref _triggered, 1) == 0)
                    _waitCts.Cancel();
            }
        }
    }
}
