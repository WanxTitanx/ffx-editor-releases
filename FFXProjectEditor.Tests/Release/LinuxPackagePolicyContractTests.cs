using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace FFXProjectEditor.Tests.Release;

// ============================================================================
// LinuxPackagePolicyContractTests - linux runtime policy structure and decoys
// WHY: release/linux-runtime-prerequisites.json is the preflight truth for the
//      Linux package CLI. These contracts pin target identity, explicit
//      capability states derived from evidence, unique library names, payload
//      pin placeholders that are null (never wildcards), and forbidden payload
//      classes that must not admit user data or browser backends.
// ============================================================================
public sealed class LinuxPackagePolicyContractTests
{
    static JsonElement Load() =>
        JsonDocument.Parse(File.ReadAllText(FindRepoFile(
            "release", "linux-runtime-prerequisites.json"))).RootElement;

    [Fact]
    public void Policy_TargetsExactlyLinuxX64()
    {
        JsonElement root = Load();
        Assert.Equal("linux-x64", root.GetProperty("target").GetProperty("rid").GetString());
        Assert.Equal("x64", root.GetProperty("target").GetProperty("architecture").GetString());
        Assert.Equal("64", root.GetProperty("target").GetProperty("elfClass").GetString());
        Assert.Equal("x86-64", root.GetProperty("target").GetProperty("elfMachine").GetString());
        Assert.Equal(new[] { "Candidate", "Release" },
            root.GetProperty("strictModes").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public void Policy_HasUniqueMandatoryHostLibraryNames()
    {
        JsonElement libs = Load().GetProperty("hostLibraries").GetProperty("mandatory");
        string[] names = libs.EnumerateArray()
            .Select(l => l.GetProperty("name").GetString())
            .ToArray();
        Assert.True(names.Length >= 17, $"expected the full ldd-derived union, got {names.Length}");
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Policy_DisplayLibrariesAreDlopenLoadedWithEvidence()
    {
        JsonElement display = Load().GetProperty("displaySubsystem");
        Assert.True(display.GetProperty("dlopenLoaded").GetBoolean());
        Assert.Contains(display.GetProperty("libraries").EnumerateArray(),
            l => l.GetProperty("name").GetString() == "libX11.so.6");
        Assert.False(string.IsNullOrWhiteSpace(
            display.GetProperty("$evidence").GetString()));
    }

    [Fact]
    public void Policy_VgmstreamPinMatchesThePinnedUpstreamHash()
    {
        JsonElement binary = Load().GetProperty("payloads").GetProperty("vgmstreamLinux")
            .GetProperty("binary");
        Assert.Equal(
            "2b05458f470ac6e051848e08cbd6d31a074e1808a648e2c6007418ad4242fc58",
            binary.GetProperty("sha256").GetString());
        Assert.Equal("static", binary.GetProperty("linkage").GetString());
    }

    [Fact]
    public void Policy_DotNetPayloadHashesAreExplicitNullsUntilFinalCandidate()
    {
        JsonElement payloads = Load().GetProperty("payloads")
            .GetProperty("dotnetSelfContained")
            .GetProperty("payloadFiles");
        foreach (JsonElement file in payloads.EnumerateArray())
        {
            Assert.True(file.GetProperty("sha256").ValueKind == JsonValueKind.Null,
                $"{file.GetProperty("path").GetString()} must stay null until the final candidate pins it");
        }
    }

    [Fact]
    public void Policy_OptionalCapabilitiesAreAllHonestlyUnavailable()
    {
        JsonElement optional = Load().GetProperty("optionalCapabilities");
        Assert.True(optional.EnumerateObject().Count() >= 5);
        foreach (JsonProperty capability in optional.EnumerateObject())
        {
            Assert.Equal("OPTIONAL_UNAVAILABLE",
                capability.Value.GetProperty("state").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                capability.Value.GetProperty("reason").GetString()));
        }
    }

    [Fact]
    public void Policy_ForbiddenPayloadsNeverAdmitUserDataOrBrowserBackends()
    {
        JsonElement forbidden = Load().GetProperty("forbiddenPayloads");
        string[] global = forbidden.GetProperty("globally").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Contains("*.exe", global);
        Assert.Contains("FFXED.jar", global);
        Assert.Contains("runtime/java/**", global);
        Assert.Contains("prerequisites/**", global);

        string[] browsers = forbidden.GetProperty("browserBackends").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Contains(browsers, b => b.StartsWith("WebKit", StringComparison.Ordinal));

        string[] userData = forbidden.GetProperty("userData").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Contains("saves/**", userData);
    }

    [Fact]
    public void WindowsComponent_PinDriftIsZeroForItsExistingPins()
    {
        // Cross-target guard: the Windows component must not gain or lose pins
        // just because the Linux policy landed.
        JsonDocument allowlist = JsonDocument.Parse(File.ReadAllText(FindRepoFile(
            "release", "package-allowlist.json")));
        JsonElement win = allowlist.RootElement.GetProperty("components")
            .GetProperty("studio-core-win-x64");
        Assert.Equal("win-x64", win.GetProperty("target").GetString());
    }

    [Fact]
    public void Allowlist_HasReconciledLinuxComponent()
    {
        // GF-02: the tracked policy must carry the reconciled studio-core-linux-x64
        // component (identity + structure + deny rules), otherwise every Linux
        // Candidate verify fails with an unreconciled policy component.
        JsonDocument allowlist = JsonDocument.Parse(File.ReadAllText(FindRepoFile(
            "release", "package-allowlist.json")));
        JsonElement linux = allowlist.RootElement.GetProperty("components")
            .GetProperty("studio-core-linux-x64");
        Assert.Equal("linux-x64", linux.GetProperty("target").GetString());
        Assert.Equal("linux-x64-portable", linux.GetProperty("profile").GetString());
        string projectVersion = System.Xml.Linq.XDocument.Load(FindRepoFile(
                "FFXProjectEditor", "FFXProjectEditor.csproj"))
            .Descendants("Version").Select(e => e.Value).First(v => v.Length > 0);
        Assert.Equal(projectVersion, linux.GetProperty("version").GetString());
        Assert.True(linux.GetProperty("selfContained").GetBoolean());

        string[] required = linux.GetProperty("requiredFiles").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Contains("FFXProjectEditor", required);
        Assert.Contains("tools/vgmstream/linux-x64/vgmstream-cli", required);
        Assert.Contains("release-manifest.json", required);

        string[] satellites = linux.GetProperty("requiredSatellites").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Equal(new[] { "de", "es", "fr", "it", "ja", "ko", "pt", "zh" },
            satellites.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        string[] roots = linux.GetProperty("allowedRoots").EnumerateArray()
            .Select(v => v.GetString()).ToArray()!;
        Assert.Contains("tools/", roots);
        Assert.Contains("viewers/", roots);

        JsonElement deny = linux.GetProperty("deny");
        Assert.Contains(".pdb", deny.GetProperty("extensions").EnumerateArray()
            .Select(v => v.GetString()));
        Assert.Contains(".exe", deny.GetProperty("extensions").EnumerateArray()
            .Select(v => v.GetString()));
        Assert.Contains("FFXED.jar", deny.GetProperty("paths").EnumerateArray()
            .Select(v => v.GetString()));
        Assert.Contains("java/", deny.GetProperty("paths").EnumerateArray()
            .Select(v => v.GetString()));

        Assert.False(linux.GetProperty("allowedTopLevel").EnumerateArray()
                .Any(v => v.GetString()!.EndsWith(".pdb", StringComparison.Ordinal)),
            "allowedTopLevel must not admit .pdb files");
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
