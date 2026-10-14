using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace FFXProjectEditor.Tests.Release;

// ── Evaluated desktop target graph ───────────────────────────────────────────────
// Source XML alone cannot prove which SDK items reach a RID-specific build. These tests ask
// MSBuild for the evaluated PackageReference/Reference/Content/Compile graph without restoring or
// compiling, so an unsupported RID or a Windows browser payload leaking into Linux fails closed.
public sealed class LinuxEmbeddedViewerContractTests
{
    [Fact]
    public async Task LinuxX64Graph_ExcludesWindowsWebView2AndSelectsOnlyLinuxFacade()
    {
        EvaluatedGraph graph = await EvaluateAsync("linux-x64");

        Assert.Equal("Linux", graph.TargetPlatform);
        Assert.True(graph.HasDefine("FFX_ENABLE_X11"));
        Assert.False(graph.HasDefine("FFX_ENABLE_WIN32"));
        Assert.Contains("Avalonia.X11", graph.PackageReferences);
        Assert.Contains("Tmds.DBus.Protocol", graph.PackageReferences);
        Assert.DoesNotContain("Avalonia.Win32", graph.PackageReferences);
        Assert.DoesNotContain("Microsoft.Web.WebView2", graph.PackageReferences);
        Assert.DoesNotContain("Microsoft.Web.WebView2.Core", graph.References);
        Assert.DoesNotContain(graph.Content, IsWindowsWebView2Payload);
        Assert.DoesNotContain(graph.Compile, IsWindowsHostSource);
        Assert.Contains(graph.Compile, IsLinuxHostSource);
        Assert.Contains(graph.Compile, IsSharedNavigationPolicySource);
    }

    [Fact]
    public async Task WindowsX64Graph_PreservesWebView2AndSelectsOnlyWindowsHost()
    {
        EvaluatedGraph graph = await EvaluateAsync("win-x64");

        Assert.Equal("Windows", graph.TargetPlatform);
        Assert.True(graph.HasDefine("FFX_ENABLE_WIN32"));
        Assert.False(graph.HasDefine("FFX_ENABLE_X11"));
        Assert.Contains("Avalonia.Win32", graph.PackageReferences);
        Assert.DoesNotContain("Avalonia.X11", graph.PackageReferences);
        Assert.DoesNotContain("Tmds.DBus.Protocol", graph.PackageReferences);
        Assert.Contains("Microsoft.Web.WebView2", graph.PackageReferences);
        Assert.Contains("Microsoft.Web.WebView2.Core", graph.References);
        Assert.Contains(graph.Content, IsWebView2Loader);
        Assert.Contains(graph.Content, IsWebView2OfflineInstaller);
        Assert.Contains(graph.Compile, IsWindowsHostSource);
        Assert.DoesNotContain(graph.Compile, IsLinuxHostSource);
        Assert.Contains(graph.Compile, IsSharedNavigationPolicySource);
    }

    [Fact]
    public async Task WindowsRestoreGraph_UsesRuntimeIdentifiersAndIncludesWindowsBackendDependencies()
    {
        // dotnet restore --runtime forwards RuntimeIdentifiers (plural), while the subsequent
        // build uses RuntimeIdentifier. Both evaluations must select the identical Windows graph.
        EvaluatedGraph graph = await EvaluateAsync(
            runtimeIdentifier: null,
            restoreRuntimeIdentifiers: "win-x64");

        Assert.Equal("Windows", graph.TargetPlatform);
        Assert.True(graph.HasDefine("FFX_ENABLE_WIN32"));
        Assert.False(graph.HasDefine("FFX_ENABLE_X11"));
        Assert.Contains("Avalonia.Win32", graph.PackageReferences);
        Assert.DoesNotContain("Avalonia.X11", graph.PackageReferences);
        Assert.Contains("Microsoft.Web.WebView2", graph.PackageReferences);
        Assert.Contains("Microsoft.Web.WebView2.Core", graph.References);
        Assert.Contains(graph.Compile, IsWindowsHostSource);
        Assert.DoesNotContain(graph.Compile, IsLinuxHostSource);
    }

    [Theory]
    [InlineData("linux-arm64")]
    [InlineData("linux-musl-x64")]
    [InlineData("win-arm64")]
    [InlineData("osx-x64")]
    public async Task UnsupportedExplicitRid_FailsDuringNormalRestoreWithReleaseTargetDiagnostic(
        string runtimeIdentifier)
    {
        ProcessResult result = await RunIsolatedRestoreAsync(runtimeIdentifier);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            $"Unsupported RuntimeIdentifier '{runtimeIdentifier}'; only win-x64 and linux-x64 are release targets.",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedExplicitRid_CannotBeReclassifiedByAnExternalPlatformProperty()
    {
        ProcessResult result = await RunIsolatedRestoreAsync(
            "osx-x64",
            "-property:FFXRequestedRuntimeIdentifier=win-x64",
            "-property:FFXTargetPlatform=Windows",
            "-property:FFXUnsupportedRuntimeIdentifier=false");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unsupported RuntimeIdentifier 'osx-x64'", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RidlessGraph_UsesOnlyTheCurrentBuildHostForDeveloperConvenience()
    {
        EvaluatedGraph graph = await EvaluateAsync(runtimeIdentifier: null);
        string expected = OperatingSystem.IsWindows() ? "Windows" :
            OperatingSystem.IsLinux() ? "Linux" : string.Empty;

        Assert.Equal(expected, graph.TargetPlatform);
    }

    private static bool IsWindowsWebView2Payload(string value) =>
        IsWebView2Loader(value) || IsWebView2OfflineInstaller(value);

    private static bool IsWebView2Loader(string value) =>
        Normalize(value).EndsWith("/WebView2Loader.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsWebView2OfflineInstaller(string value) =>
        Normalize(value).Contains(
            "/WindowsPrerequisites/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe",
            StringComparison.OrdinalIgnoreCase) ||
        Normalize(value).EndsWith(
            "/prerequisites/webview2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsHostSource(string value) =>
        Normalize(value).EndsWith(
            "/Modules/Common/ViewerShell/WebView2Host.cs",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLinuxHostSource(string value) =>
        Normalize(value).EndsWith(
            "/Modules/Common/ViewerShell/WebView2Host.Linux.cs",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedNavigationPolicySource(string value) =>
        Normalize(value).EndsWith(
            "/Modules/Common/ViewerShell/WebView2NavigationPolicy.cs",
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        "/" + value.Replace('\\', '/').TrimStart('/');

    private static async Task<EvaluatedGraph> EvaluateAsync(
        string? runtimeIdentifier,
        string? restoreRuntimeIdentifiers = null)
    {
        var arguments = new List<string>
        {
            "-getProperty:FFXTargetPlatform,DefineConstants",
            "-getItem:PackageReference,Reference,Content,Compile",
            "-property:Configuration=Release",
            "-property:FFXShipWindowsPrerequisites=true",
        };
        if (runtimeIdentifier != null)
            arguments.Add($"-property:RuntimeIdentifier={runtimeIdentifier}");
        if (restoreRuntimeIdentifiers != null)
            arguments.Add($"-property:RuntimeIdentifiers={restoreRuntimeIdentifiers}");

        ProcessResult result = await RunMsBuildAsync(arguments.ToArray());
        Assert.True(result.ExitCode == 0, result.CombinedOutput);

        int jsonStart = result.StandardOutput.IndexOf('{');
        Assert.True(jsonStart >= 0, result.CombinedOutput);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput[jsonStart..]);
        JsonElement root = document.RootElement;
        string targetPlatform = root
            .GetProperty("Properties")
            .GetProperty("FFXTargetPlatform")
            .GetString() ?? string.Empty;
        string defineConstants = root
            .GetProperty("Properties")
            .GetProperty("DefineConstants")
            .GetString() ?? string.Empty;

        return new EvaluatedGraph(
            targetPlatform,
            defineConstants,
            ReadItemIdentities(root, "PackageReference"),
            ReadItemIdentities(root, "Reference"),
            ReadItemIdentities(root, "Content"),
            ReadItemIdentities(root, "Compile"));
    }

    private static string[] ReadItemIdentities(JsonElement root, string itemName) =>
        root.GetProperty("Items")
            .GetProperty(itemName)
            .EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
            .ToArray();

    private static async Task<ProcessResult> RunMsBuildAsync(params string[] arguments)
    {
        string projectPath = FindRepoFile("FFXProjectEditor", "FFXProjectEditor.csproj");
        var dotnetArguments = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet",
        };
        dotnetArguments.AddRange(arguments);
        return await RunDotNetAsync(dotnetArguments.ToArray());
    }

    private static async Task<ProcessResult> RunIsolatedRestoreAsync(
        string runtimeIdentifier,
        params string[] properties)
    {
        string projectPath = FindRepoFile("FFXProjectEditor", "FFXProjectEditor.csproj");
        string isolationRoot = Directory.CreateTempSubdirectory("ffx-c1-rid-restore-").FullName;
        string intermediateOutputPath =
            Path.Combine(isolationRoot, "obj") + Path.DirectorySeparatorChar;
        string packagesPath = Path.Combine(isolationRoot, "packages");
        try
        {
            var dotnetArguments = new List<string>
            {
                "restore",
                projectPath,
                "--runtime",
                runtimeIdentifier,
                "--packages",
                packagesPath,
                "--nologo",
                "--verbosity",
                "quiet",
                $"-property:BaseIntermediateOutputPath={intermediateOutputPath}",
                $"-property:MSBuildProjectExtensionsPath={intermediateOutputPath}",
            };
            dotnetArguments.AddRange(properties);
            return await RunDotNetAsync(dotnetArguments.ToArray());
        }
        finally
        {
            Directory.Delete(isolationRoot, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunDotNetAsync(params string[] arguments)
    {
        string? configuredDotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(configuredDotnetHost)
                ? "dotnet"
                : configuredDotnetHost,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the dotnet process for contract evaluation.");
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

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
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

    private sealed record EvaluatedGraph(
        string TargetPlatform,
        string DefineConstants,
        IReadOnlyCollection<string> PackageReferences,
        IReadOnlyCollection<string> References,
        IReadOnlyCollection<string> Content,
        IReadOnlyCollection<string> Compile)
    {
        public bool HasDefine(string value) => DefineConstants
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(value, StringComparer.Ordinal);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
