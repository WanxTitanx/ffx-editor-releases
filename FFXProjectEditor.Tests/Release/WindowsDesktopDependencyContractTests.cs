using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FFXProjectEditor.Tests.Release;

// ── Supported desktop dependency contract ──
// These source-only checks keep Windows/Linux dependency policy testable without initializing
// Avalonia, opening a GUI, or requiring the game corpus. They do not prove that either UI renders.
public sealed class WindowsDesktopDependencyContractTests
{
    [Fact]
    public void ProductProject_ContinuesToTargetNet8()
    {
        XDocument project = LoadProductProject();
        string targetFramework = Assert.Single(project
            .Descendants()
            .Where(element => element.Name.LocalName == "TargetFramework"))
            .Value
            .Trim();

        Assert.Equal("net8.0", targetFramework);
    }

    [Fact]
    public void ProductProject_UsesOnlySupportedDesktopBackendsAtTheCoreAvaloniaVersion()
    {
        XDocument project = LoadProductProject();
        IReadOnlyDictionary<string, string> packageVersions = ReadPackageVersions(project);
        string coreAvaloniaVersion = packageVersions["Avalonia"];
        string[] desktopBackends = packageVersions.Keys
            .Where(package => package is "Avalonia.Desktop" or "Avalonia.Skia" or "Avalonia.Win32" or "Avalonia.X11")
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Avalonia.Skia", "Avalonia.Win32", "Avalonia.X11" }, desktopBackends);
        Assert.Equal(coreAvaloniaVersion, packageVersions["Avalonia.Skia"]);
        Assert.Equal(coreAvaloniaVersion, packageVersions["Avalonia.Win32"]);
        Assert.Equal(coreAvaloniaVersion, packageVersions["Avalonia.X11"]);
    }

    [Fact]
    public void ProductProject_UsesPatchedDbusProtocolVersion()
    {
        XDocument project = LoadProductProject();
        IReadOnlyDictionary<string, string> packageVersions = ReadPackageVersions(project);

        Assert.Equal("0.21.3", packageVersions["Tmds.DBus.Protocol"]);
    }

    [Fact]
    public void BuildAvaloniaApp_UsesSupportedDesktopThenSkiaBackends()
    {
        string source = MaskNonCode(File.ReadAllText(FindRepoFile("FFXProjectEditor", "Program.cs")));
        Match buildMethod = Regex.Match(
            source,
            @"public static AppBuilder BuildAvaloniaApp\(\)\s*=>\s*AppBuilder\.Configure<App>\(\)(?<chain>[\s\S]*?);",
            RegexOptions.CultureInvariant);

        Assert.True(buildMethod.Success, "Could not locate the expression-bodied BuildAvaloniaApp method.");
        string[] backendCalls = Regex.Matches(
                buildMethod.Groups["chain"].Value,
                @"\.(?:UsePlatformDetect|UseSupportedDesktop|UseWin32|UseX11|UseSkia)\(\)",
                RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .ToArray();

        Assert.Equal(new[] { ".UseSupportedDesktop()", ".UseSkia()" }, backendCalls);
    }

    [Fact]
    public void DesktopPlatformBootstrap_GuardsEachBackendAndRejectsUnsupportedPlatforms()
    {
        string source = File.ReadAllText(FindRepoFile("FFXProjectEditor", "Services", "DesktopPlatformBootstrap.cs"));
        Assert.True(HasExpectedPlatformBootstrapWiring(source));

        // In-memory negative controls: comments and executable decoys outside the composition
        // boundary cannot replace any required guard, backend call, or unsupported-OS rejection.
        foreach ((string requiredCode, string decoy) in new[]
        {
            ("#if FFX_ENABLE_WIN32", "#if FFX_ENABLE_WIN32\n#endif"),
            ("OperatingSystem.IsWindows()", "internal static class Unrelated { internal static bool Decoy() => OperatingSystem.IsWindows(); }"),
            (".UseWin32()", "internal static class Unrelated { internal static AppBuilder Decoy(AppBuilder builder) => builder.UseWin32(); }"),
            ("#if FFX_ENABLE_X11", "#if FFX_ENABLE_X11\n#endif"),
            ("OperatingSystem.IsLinux()", "internal static class Unrelated { internal static bool Decoy() => OperatingSystem.IsLinux(); }"),
            (".UseX11()", "internal static class Unrelated { internal static AppBuilder Decoy(AppBuilder builder) => builder.UseX11(); }"),
            ("new PlatformNotSupportedException(", "internal static class Unrelated { internal static void Decoy() => throw new PlatformNotSupportedException(); }")
        })
        {
            Assert.False(HasExpectedPlatformBootstrapWiring(
                source.Replace(requiredCode, "/* " + requiredCode + " */", StringComparison.Ordinal)));
            Assert.False(HasExpectedPlatformBootstrapWiring(
                source.Replace(requiredCode, string.Empty, StringComparison.Ordinal) + "\n" + decoy));
        }
    }

    [Fact]
    public void Bootstrap_PreservesExistingNonBackendBuilderAndDesktopLifetimeWiring()
    {
        string source = File.ReadAllText(FindRepoFile("FFXProjectEditor", "Program.cs"));
        Assert.True(HasExpectedBootstrapWiring(source));

        // In-memory negative controls: a preserved spelling is not a preserved executable call.
        foreach (string requiredCall in new[] { ".WithInterFont()", ".LogToTrace()", "BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);" })
        {
            Assert.False(HasExpectedBootstrapWiring(source.Replace(requiredCall, "/* " + requiredCall + " */", StringComparison.Ordinal)));
            Assert.False(HasExpectedBootstrapWiring(source.Replace(requiredCall, string.Empty, StringComparison.Ordinal)
                + "\nclass Unrelated { void Decoy() { " + requiredCall + " } }"));
        }
    }

    private static bool HasExpectedBootstrapWiring(string source)
    {
        source = MaskNonCode(source);
        Match builder = Regex.Match(source,
            @"public static AppBuilder BuildAvaloniaApp\(\)\s*=>\s*AppBuilder\.Configure<App>\(\)(?<chain>[^;]+);",
            RegexOptions.CultureInvariant);
        if (!builder.Success || Regex.Replace(builder.Groups["chain"].Value, @"\s+", string.Empty)
            != ".UseSupportedDesktop().UseSkia().WithInterFont().LogToTrace()")
            return false;

        // Balanced braces confine this check to Main, not a later helper or unrelated class.
        // Require the lifetime call as its terminal statement, not merely somewhere in the file.
        Match main = Regex.Match(source,
            @"public static void Main\(string\[\] args\)\s*\{(?<body>(?:(?>[^{}]+)|(?<nested>\{)|(?<-nested>\}))*)(?(nested)(?!))\}",
            RegexOptions.CultureInvariant);
        return main.Success && main.Groups["body"].Value.TrimEnd()
            .EndsWith("BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);", StringComparison.Ordinal);
    }

    private static bool HasExpectedPlatformBootstrapWiring(string source)
    {
        source = MaskNonCode(source);
        Match bootstrapClass = Regex.Match(source,
            @"internal static class DesktopPlatformBootstrap\s*\{(?<body>(?:(?>[^{}]+)|(?<nested>\{)|(?<-nested>\}))*)(?(nested)(?!))\}",
            RegexOptions.CultureInvariant);
        if (!bootstrapClass.Success)
            return false;

        Match method = Regex.Match(bootstrapClass.Groups["body"].Value,
            @"internal static AppBuilder UseSupportedDesktop\(this AppBuilder builder\)\s*\{(?<body>[^{}]*)\}",
            RegexOptions.CultureInvariant);
        if (!method.Success)
            return false;

        string normalizedBody = Regex.Replace(method.Groups["body"].Value, @"\s+", string.Empty);
        // Each extension method is compiled only in the graph that references its backend
        // package. Runtime OS checks remain as defense in depth and unsupported hosts fail closed.
        return normalizedBody == "#ifFFX_ENABLE_WIN32"
            + "if(OperatingSystem.IsWindows())returnbuilder.UseWin32();#endif"
            + "#ifFFX_ENABLE_X11if(OperatingSystem.IsLinux())returnbuilder.UseX11();#endif"
            + "thrownewPlatformNotSupportedException();";
    }

    // Mask the comments and ordinary/verbatim literals used by this known bootstrap source.
    // This is a narrow source contract, not a general C# parser or a GUI-execution assertion.
    private static string MaskNonCode(string source) => Regex.Replace(source,
        "@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/",
        match => new string(' ', match.Length), RegexOptions.CultureInvariant);

    private static XDocument LoadProductProject() =>
        XDocument.Load(FindRepoFile("FFXProjectEditor", "FFXProjectEditor.csproj"));

    private static IReadOnlyDictionary<string, string> ReadPackageVersions(XDocument project) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => (string?)element.Attribute("Include")
                    ?? throw new InvalidDataException("PackageReference is missing Include."),
                element => (string?)element.Attribute("Version")
                    ?? throw new InvalidDataException("PackageReference is missing Version."),
                StringComparer.Ordinal);

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
