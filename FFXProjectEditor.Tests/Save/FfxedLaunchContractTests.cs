using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace FFXProjectEditor.Tests.Save;

// ── FFXED source and evaluated-payload contract ────────────────────────────────────
// MSBuild evaluation proves that even an explicitly enabled FFXED switch cannot leak the
// owner-accepted-risk Windows payload into Linux. Runtime tests separately inspect arguments.
public sealed class FfxedLaunchContractTests
{
    [Fact]
    public void SaveEditorLaunch_DoesNotContainLegacyFallbacksOrStringArguments()
    {
        string source = File.ReadAllText(FindRepoFile(
            "FFXProjectEditor", "Modules", "SaveEditor", "SaveEditorHub_Control.axaml.cs"));
        string runtime = File.ReadAllText(FindRepoFile(
            "FFXProjectEditor", "Services", "Tools", "PrivateJavaRuntime.cs"));

        Assert.DoesNotContain("OwnerEnvironmentPaths.ModsRoot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileName = \"java\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFfxedJarPath", source, StringComparison.Ordinal);
        // The launch boundary hardened into FfxedRuntimeLauncher (bundled-private JRE only,
        // hash-pinned jar, ArgumentList instead of string arguments); it must remain the
        // single entry point from the SaveEditor UI.
        Assert.Contains("new FfxedRuntimeLauncher()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnerEnvironmentPaths", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCurrentDirectory", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.Clear", runtime, StringComparison.Ordinal);
        Assert.Contains("startInfo.Environment.Remove(variable)", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinuxGraph_ExcludesFfxedJarAndPrivateJavaEvenWhenFlagIsForcedOn()
    {
        string[] content = await EvaluateContentAsync("linux-x64");

        Assert.DoesNotContain(content, IsFfxedJar);
        Assert.DoesNotContain(content, IsPrivateJavaPayload);
    }

    [Fact]
    public async Task WindowsGraph_RetainsFfxedJarAndPrivateJavaWhenFlagIsEnabled()
    {
        string[] content = await EvaluateContentAsync("win-x64");

        Assert.Contains(content, IsFfxedJar);
        Assert.Contains(content, IsPrivateJavaPayload);
    }

    private static bool IsFfxedJar(string value) =>
        Normalize(value).EndsWith("/ExternalLibs/FFXED/FFXED.jar", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateJavaPayload(string value) =>
        Normalize(value).Contains("/ExternalLibs/FFXED/runtime/win-x64/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        "/" + value.Replace('\\', '/').TrimStart('/');

    private static async Task<string[]> EvaluateContentAsync(string runtimeIdentifier)
    {
        string projectPath = FindRepoFile("FFXProjectEditor", "FFXProjectEditor.csproj");
        string? configuredDotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(configuredDotnetHost) ? "dotnet" : configuredDotnetHost,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getItem:Content",
            "-property:Configuration=Release",
            $"-property:RuntimeIdentifier={runtimeIdentifier}",
            "-property:FFXShipFfxedJar=true",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start MSBuild for FFXED graph evaluation.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string output = await standardOutput;
        string error = await standardError;
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
        int jsonStart = output.IndexOf('{');
        Assert.True(jsonStart >= 0, output);
        using JsonDocument document = JsonDocument.Parse(output[jsonStart..]);
        return document.RootElement
            .GetProperty("Items")
            .GetProperty("Content")
            .EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
            .ToArray();
    }

    private static string FindRepoFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
