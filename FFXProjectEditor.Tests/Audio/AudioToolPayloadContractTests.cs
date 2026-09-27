using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace FFXProjectEditor.Tests.Audio;

// ============================================================================
// AudioToolPayloadContractTests - cross-target payload truth
// WHY: the release manifest and csproj rules are the two sources that decide what
//      ships. These contracts pin (1) the pinned Linux vgmstream hash in
//      tool-dependencies.json, (2) Linux-only csproj inclusion of linux-x64, and
//      (3) Windows exclusion of the Linux subtree plus continued fsbext/fsbankcl.
// ============================================================================
public sealed class AudioToolPayloadContractTests
{
    [Fact]
    public void Manifest_PinsTheExactLinuxVgmstreamPayload()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FindRepoFile(
            "release", "tool-dependencies.json")));
        JsonElement entry = Assert.Single(
            document.RootElement.GetProperty("entries").EnumerateArray()
                .Where(e => e.GetProperty("id").GetString() == "vgmstream-cli"));

        JsonElement linux = Assert.Single(
            entry.GetProperty("approvedPayloads").EnumerateArray()
                .Where(p => p.GetProperty("target").GetString() == "linux-x64"
                    && p.GetProperty("path").GetString()!.EndsWith("vgmstream-cli")));
        Assert.Equal(
            "2b05458f470ac6e051848e08cbd6d31a074e1808a648e2c6007418ad4242fc58",
            linux.GetProperty("sha256").GetString());
        Assert.Equal(7_499_656, linux.GetProperty("bytes").GetInt64());
        Assert.Equal("native", linux.GetProperty("kind").GetString());

        Assert.Contains(entry.GetProperty("approvedPayloads").EnumerateArray(),
            p => p.GetProperty("target").GetString() == "linux-x64"
                && p.GetProperty("path").GetString()!.EndsWith("COPYING"));
    }

    [Fact]
    public void Project_LinuxAudioRuleShipsOnlyTheLinuxSubtree()
    {
        var project = System.Xml.Linq.XDocument.Load(FindRepoFile(
            "FFXProjectEditor", "FFXProjectEditor.csproj"));
        var rules = project.Descendants()
            .Where(e => e.Name.LocalName == "Content"
                && (e.Attribute("Include")?.Value ?? string.Empty).Contains("vgmstream"))
            .ToList();

        var linuxRule = Assert.Single(rules,
            r => (r.Attribute("Include")?.Value ?? string.Empty).Contains("linux-x64"));
        Assert.Contains("$(FFXTargetPlatform)' == 'Linux'",
            linuxRule.Attribute("Condition")?.Value ?? string.Empty);

        var windowsRule = Assert.Single(rules,
            r => !(r.Attribute("Include")?.Value ?? string.Empty).Contains("linux-x64"));
        Assert.Contains("$(FFXTargetPlatform)' == 'Windows'",
            windowsRule.Attribute("Condition")?.Value ?? string.Empty);
        Assert.Contains("linux-x64",
            windowsRule.Attribute("Exclude")?.Value ?? string.Empty);
    }

    [Fact]
    public void Project_WindowsAuthoringToolsStayWindowsOnly()
    {
        var project = System.Xml.Linq.XDocument.Load(FindRepoFile(
            "FFXProjectEditor", "FFXProjectEditor.csproj"));
        foreach (string tool in new[] { "fsbext", "fsbankcl" })
        {
            var rule = Assert.Single(project.Descendants()
                .Where(e => e.Name.LocalName == "Content"
                    && (e.Attribute("Include")?.Value ?? string.Empty).Contains(tool)));
            Assert.Contains("$(FFXTargetPlatform)' == 'Windows'",
                rule.Attribute("Condition")?.Value ?? string.Empty);
        }
    }

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
