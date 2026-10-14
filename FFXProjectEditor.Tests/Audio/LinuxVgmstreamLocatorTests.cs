using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FFXProjectEditor.Tests.Audio;

// ============================================================================
// LinuxVgmstreamLocatorTests - canonical app-private audio tool resolution
// WHY: the old locator honored arbitrary overrides and searched the repository
//      root and ModsRoot, which can execute unreviewed binaries. These contracts
//      pin the single app-private resolution path and per-platform honesty.
// ============================================================================
public sealed class LinuxVgmstreamLocatorTests
{
    [Fact]
    public void Locator_ExposesNoOverridePathProperties()
    {
        PropertyInfo[] properties = typeof(FfxAudioToolsLocator).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Override"));
    }

    [Fact]
    public void Locator_Source_HasNoRepositoryOrModsRootFallback()
    {
        string source = MaskComments(File.ReadAllText(SourcePath.For("Services", "Extras", "FfxAudioToolsLocator.cs")));
        Assert.DoesNotContain("ModsRoot", source);
        Assert.DoesNotContain("FindRepoRoot", source);
        Assert.DoesNotContain("VgmStreamOverridePath", source);
        Assert.DoesNotContain("FsbExtOverridePath", source);
        Assert.DoesNotContain("FsbankClOverridePath", source);
    }

    [Fact]
    public void Locator_Source_ResolvesOnlyAppPrivateCanonicalPaths()
    {
        string source = File.ReadAllText(SourcePath.For("Services", "Extras", "FfxAudioToolsLocator.cs"));
        Assert.Contains("PortablePathResolver.BundledPath(\"tools\", \"vgmstream\", \"linux-x64\", \"vgmstream-cli\")", source);
        Assert.Contains("PortablePathResolver.BundledPath(\"tools\", \"vgmstream\", \"vgmstream-cli.exe\")", source);
    }

    [Fact]
    public void Linux_HasNoWindowsAuthoringTools()
    {
        if (!OperatingSystem.IsLinux())
            return; // Windows keeps its authoring tools; this contract is Linux-specific.

        Assert.Null(FfxAudioToolsLocator.LocateFsbExt());
        Assert.Null(FfxAudioToolsLocator.LocateFsbankCl());
        Assert.False(FfxAudioToolsLocator.FsbExtAvailable);
        Assert.False(FfxAudioToolsLocator.FsbankClAvailable);
        Assert.Contains("unavailable on Linux", FfxAudioToolsLocator.BundledToolsStatus);
    }

    [Fact]
    public void Linux_BundledVgmStreamResolvesAppPrivate()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string? cli = FfxAudioToolsLocator.LocateVgmStream();
        Assert.NotNull(cli);
        Assert.True(Path.IsPathRooted(cli));
        string full = Path.GetFullPath(cli!);
        Assert.StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(System.AppContext.BaseDirectory)),
            full);
        Assert.EndsWith(Path.Combine("tools", "vgmstream", "linux-x64", "vgmstream-cli"), full);
    }

    [Fact]
    public void Ps2Service_Source_DelegatesToCanonicalLocatorWithoutOverrides()
    {
        string source = MaskComments(File.ReadAllText(SourcePath.For("Services", "Extras", "Ps2VgmStream_Service.cs")));
        Assert.DoesNotContain("OverridePath", source);
        Assert.DoesNotContain("ModsRoot", source);
        Assert.Contains("FfxAudioToolsLocator.LocateVgmStream()", source);
    }

    [Fact]
    public void Services_UseBoundedRunner_NeverStringArguments()
    {
        string ps2 = File.ReadAllText(SourcePath.For("Services", "Extras", "Ps2VgmStream_Service.cs"));
        string fsb = File.ReadAllText(SourcePath.For("Services", "Extras", "FfxFsbVgmStream_Service.cs"));
        string ext = File.ReadAllText(SourcePath.For("Services", "Extras", "FfxFsbExt_Service.cs"));
        string bank = File.ReadAllText(SourcePath.For("Services", "Extras", "FfxFsbBankCl_Service.cs"));
        string health = File.ReadAllText(SourcePath.For("Services", "Extras", "FfxAudioToolsHealth_Service.cs"));

        foreach (string source in new[] { ps2, fsb, ext, bank, health })
        {
            Assert.Contains("BoundedToolProcessRunner.Run", source);
            Assert.DoesNotContain("Arguments =", source);
            Assert.DoesNotContain("ReadToEnd", source);
        }
    }

    [Fact]
    public void Bootstrap_HasNoPowerShellOrScriptExecution()
    {
        string source = MaskComments(File.ReadAllText(SourcePath.For("Services", "Extras", "FfxAudioToolsBootstrap_Service.cs")));
        Assert.DoesNotContain("powershell", source, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessStartInfo", source);
        (bool ok, string message) = FfxAudioToolsBootstrap_Service.RunBootstrap();
        Assert.False(ok);
        Assert.Contains("not available", message);
    }

    [Fact]
    public void ProductXaml_HasNoToolPickerOrBootstrapButtons()
    {
        string ps2Xaml = File.ReadAllText(SourcePath.For("Modules", "Extras", "Ps2AudioBrowser_Control.axaml"));
        string kernelXaml = File.ReadAllText(SourcePath.For("Modules", "BattleKernel", "Commands", "KernelCommands_Control.axaml"));
        Assert.DoesNotContain("Button_SetVgm", ps2Xaml);
        Assert.DoesNotContain("InstallBattleSfxAudioTools", kernelXaml);
        Assert.DoesNotContain("DetectFsbankClFromSdk", kernelXaml);
        Assert.DoesNotContain("Button_ImportFsbankCl", kernelXaml);
    }

    internal static class SourcePath
    {
        public static string For(params string[] segments)
        {
            DirectoryInfo? directory = new(System.AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine([directory.FullName, "FFXProjectEditor", .. segments]);
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
            throw new System.IO.FileNotFoundException(
                "Could not locate " + Path.Combine(segments) + " from " + System.AppContext.BaseDirectory);
        }
    }

    static string MaskComments(string source)
    {
        // Maintenance banners legitimately explain WHY a fallback was removed, so the
        // contract must judge executable code rather than prose comments.
        return System.Text.RegularExpressions.Regex.Replace(
            source,
            "//.*?$|/\\*.*?\\*/",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline);
    }
}
