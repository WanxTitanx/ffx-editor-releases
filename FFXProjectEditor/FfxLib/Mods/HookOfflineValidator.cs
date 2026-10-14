using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record HookAdmission(string InputSha256, string ValidatorSha256, string Report, bool Admitted);

internal sealed class HookOfflineValidator
{
    public string Executable { get; }
    public HookOfflineValidator(string executable) { ModAuthoringFiles.RejectLinks(executable); Executable = Path.GetFullPath(executable); }
    public async Task<HookAdmission> ValidateAsync(string mode, string input, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        string validatorHash = HookExportTransaction.CurrentHash(Executable) ?? throw new FileNotFoundException("Offline validator not selected.");
        var identity = await RunAsync(Executable, new[] { "identity" }, cancellationToken);
        if (identity.ExitCode != 0 || identity.Output.Trim() != "ffx.editor.contracts.v1 " + HookCompatibilityProfile.SourceRevision)
            throw new InvalidDataException("Offline validator contract mismatch.");
        string before = HookExportTransaction.CurrentHash(input) ?? throw new FileNotFoundException(input);
        var args = new List<string> { mode, input }; args.AddRange(arguments);
        var result = await RunAsync(Executable, args, cancellationToken);
        if (HookExportTransaction.CurrentHash(Executable) != validatorHash || HookExportTransaction.CurrentHash(input) != before)
            throw new IOException("Validator or manifest changed during admission.");
        return new(before, validatorHash, result.Output, result.ExitCode == 0 && result.Output.Trim() == "ADMITTED RT0");
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
        Task<string> stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); return (process.ExitCode, (await stdout) + (await stderr)); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(); char[] buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        { if (result.Length + read > 2 << 20) throw new InvalidDataException("Validator diagnostic output exceeded the limit."); result.Append(buffer, 0, read); }
        return result.ToString();
    }
}
