using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FFXProjectEditor.Tests.Release;

// ============================================================================
// LinuxDesktopDependencyContractTests - product linux-x64-portable publish truth
// WHY: the Linux portable profile is a product build. These source contracts pin
//      self-contained .NET 8, no trimming/single-file/R2R/PDB, devtools removed,
//      viewers on, Linux vgmstream on, and every Windows-only payload off — plus
//      the explicit absence of any post-publish PowerShell target (verification
//      belongs to scripts/release/linux_release.py on the Linux host).
// ============================================================================
public sealed class LinuxDesktopDependencyContractTests
{
    static XElement Property(XDocument profile, string name) =>
        Assert.Single(profile.Root!.Elements("PropertyGroup").Elements(name));

    static string Value(XDocument profile, string name) =>
        Property(profile, name).Value.Trim();

    [Fact]
    public void Profile_TargetsSelfContainedNet8LinuxX64()
    {
        XDocument profile = LoadProfile();
        Assert.Equal("Release", Value(profile, "Configuration"));
        Assert.Equal("net8.0", Value(profile, "TargetFramework"));
        Assert.Equal("linux-x64", Value(profile, "RuntimeIdentifier"));
        Assert.Equal("true", Value(profile, "SelfContained"));
        Assert.Equal("true", Value(profile, "UseAppHost"));
    }

    [Fact]
    public void Profile_DisablesSingleFileTrimR2RAndPdbs()
    {
        XDocument profile = LoadProfile();
        Assert.Equal("false", Value(profile, "PublishSingleFile"));
        Assert.Equal("false", Value(profile, "PublishTrimmed"));
        Assert.Equal("false", Value(profile, "PublishReadyToRun"));
        Assert.Equal("false", Value(profile, "DebugSymbols"));
        Assert.Equal("none", Value(profile, "DebugType"));
        Assert.Equal("false", Value(profile, "IncludeSourceRevisionInInformationalVersion"));
    }

    [Fact]
    public void Profile_IsProductOnlyWithLinuxCapabilities()
    {
        XDocument profile = LoadProfile();
        Assert.Equal("false", Value(profile, "FFXIncludeDevTools"));
        Assert.Equal("true", Value(profile, "FFXShipProductViewers"));
        Assert.Equal("true", Value(profile, "FFXShipAudioTools"));
    }

    [Fact]
    public void Profile_ForwardsEveryWindowsOnlyPayloadOff()
    {
        XDocument profile = LoadProfile();
        Assert.Equal("false", Value(profile, "FFXShipFfxedJar"));
        Assert.Equal("false", Value(profile, "FFXShipWindowsPrerequisites"));
        Assert.Equal("false", Value(profile, "FFXShipRuntimeTools"));
    }

    [Fact]
    public void Profile_RemovesDevToolsFromTheCompileGraph()
    {
        XDocument profile = LoadProfile();
        XElement remove = Assert.Single(profile.Root!.Elements("ItemGroup").Elements("Compile"));
        Assert.Contains("Tools", remove.Attribute("Remove")?.Value ?? string.Empty);
    }

    [Fact]
    public void Profile_HasNoPowerShellPostPublishTarget()
    {
        XDocument profile = LoadProfile();
        Assert.DoesNotContain(profile.Root!.Elements("Target"), t => true);
        string text = System.Text.RegularExpressions.Regex.Replace(
            profile.ToString(), "<!--.*?-->",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.DoesNotContain("pwsh", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_SeedsProductDevToolsOffForBothPortableProfiles()
    {
        XDocument project = LoadProject();
        XElement seed = project.Root!.Elements("PropertyGroup")
            .Elements("FFXIncludeDevTools")
            .Single(e => (e.Attribute("Condition")?.Value ?? string.Empty).Contains("PublishProfile"));
        string condition = seed.Attribute("Condition")?.Value ?? string.Empty;
        Assert.Contains("win-x64-portable", condition);
        Assert.Contains("linux-x64-portable", condition);
    }

    static XDocument LoadProfile() =>
        XDocument.Load(FindRepoFile("FFXProjectEditor", "Properties", "PublishProfiles", "linux-x64-portable.pubxml"));

    static XDocument LoadProject() =>
        XDocument.Load(FindRepoFile("FFXProjectEditor", "FFXProjectEditor.csproj"));

    static string FindRepoFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not find " + Path.Combine(segments));
    }
}
