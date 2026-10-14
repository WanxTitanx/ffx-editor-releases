using FFXProjectEditor.Services.ReleaseRuntime;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FFXProjectEditor.Tests.Services.ReleaseRuntime;

public sealed class GameMusicProductIntegrationTests
{
    [Fact]
    public void ProductSources_UseLocalCatalogAndStrictFfxedLauncher()
    {
        string repoRoot = FindRepoRoot();
        string audioStudio = File.ReadAllText(Path.Combine(
            repoRoot,
            "FFXProjectEditor",
            "Services",
            "AudioStudio_Service.cs"));
        string saveEditorHub = File.ReadAllText(Path.Combine(
            repoRoot,
            "FFXProjectEditor",
            "Modules",
            "SaveEditor",
            "SaveEditorHub_Control.axaml.cs"));

        Assert.Contains("ImportedGameMusicCatalog.Load", audioStudio, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Assets\Audio\Music", audioStudio, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new FfxedRuntimeLauncher().Launch()", saveEditorHub, StringComparison.Ordinal);
        Assert.DoesNotContain("FFX_FFXED_JAR", saveEditorHub, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", saveEditorHub, StringComparison.Ordinal);
        Assert.DoesNotContain("FileName = \"java\"", saveEditorHub, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSeidMap_CompilesRepositoryOverridesOnlyForDevTools()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "FFXProjectEditor",
            "FfxLib",
            "Ability",
            "SeidFsbMapLoader.cs"));
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "#if FFX_INCLUDE_DEVTOOLS\n        public const string WorkOverrideRelativePath",
            normalized,
            StringComparison.Ordinal);
        int resolveGuard = normalized.IndexOf("#if FFX_INCLUDE_DEVTOOLS", normalized.IndexOf("ResolveMapPath", StringComparison.Ordinal), StringComparison.Ordinal);
        int repoLookup = normalized.IndexOf("string? repo = CommandSoundCorpusLoader.FindRepoRoot();", resolveGuard, StringComparison.Ordinal);
        int resolveGuardEnd = normalized.IndexOf("#endif", repoLookup, StringComparison.Ordinal);
        Assert.True(resolveGuard >= 0 && repoLookup > resolveGuard && resolveGuardEnd > repoLookup);
        Assert.Contains(
            "#if FFX_INCLUDE_DEVTOOLS\n            if (norm.Contains(\"/work/fev9999_corpus_wave8/\"",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reinstall editor or run wave8 gate", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductPhyreHelperSources_DoNotEmbedDeveloperMachineDefaults()
    {
        string repoRoot = FindRepoRoot();
        string[] relativeSources =
        [
            Path.Combine("RuntimeTools", "PhyreMapExportLab", "Program.cs"),
            Path.Combine("RuntimeTools", "PhyreModelExportLab", "Program.cs"),
            Path.Combine("RuntimeTools", "PhyreModelExportLab", "PhyreDescriptorParser.cs"),
            Path.Combine("RuntimeTools", "PhyreModelExportLab", "DescriptorStaticGltfWriter.cs"),
        ];

        foreach (string relativeSource in relativeSources)
        {
            string source = File.ReadAllText(Path.Combine(
                new[] { repoRoot }.Concat(relativeSource.Split('\\', '/')).ToArray()));
            Assert.DoesNotContain(@"D:\", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Users\", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"RuntimeTools\", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RuntimeTools/", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"\work\", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("work/", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SelectionModel_RequiresAnExplicitTrackAndConsent()
    {
        GameMusicImportSelectionModel model = new();

        Assert.Equal(10, model.Tracks.Count);
        Assert.All(model.Tracks, track => Assert.False(track.IsSelected));
        Assert.False(model.CanImport);

        model.Tracks.Single(track => track.Id == "1_02_zanarkand").IsSelected = true;
        Assert.False(model.CanImport);

        model.UserConfirmedOwnershipAndLocalUse = true;
        Assert.True(model.CanImport);
        Assert.Equal(["1_02_zanarkand"], model.GetExplicitSelection());

        model.IsBusy = true;
        Assert.False(model.CanImport);
    }

    [Fact]
    public void ImportedCatalog_LoadsOnlyApprovedContainedWaveFiles()
    {
        using TestWorkspace workspace = new();
        string importRoot = Path.Combine(workspace.Root, "Audio");
        string batch = Path.Combine(importRoot, "GameMusic", "approved-batch");
        Directory.CreateDirectory(batch);

        string approvedWave = Path.Combine(batch, "001_1_02_zanarkand.wav");
        File.WriteAllBytes(approvedWave, Encoding.ASCII.GetBytes("RIFF-local-wave"));
        string escapedWave = Path.Combine(importRoot, "escape.wav");
        File.WriteAllBytes(escapedWave, Encoding.ASCII.GetBytes("RIFF-escape"));

        WriteManifest(batch,
        [
            Track("1_02_zanarkand", 1, Path.GetFileName(approvedWave), approvedWave),
            Track("unknown-track", 2, "unknown.wav", approvedWave),
            Track("1_18_besaid", 16, @"..\..\escape.wav", escapedWave),
        ]);

        ImportedGameMusicTrack[] tracks = ImportedGameMusicCatalog.Load(importRoot).ToArray();

        ImportedGameMusicTrack track = Assert.Single(tracks);
        Assert.Equal("1_02_zanarkand", track.Id);
        Assert.Equal("To Zanarkand", track.DisplayName);
        Assert.Equal(Path.GetFullPath(approvedWave), track.FilePath);
        Assert.Equal(GameMusicImportCatalog.GetRequired(track.Id).LoopStartSamples, track.LoopStartSamples);
        Assert.Equal(GameMusicImportCatalog.GetRequired(track.Id).LoopEndSamples, track.LoopEndSamples);
    }

    [Fact]
    public void ImportedCatalog_MergesNewestValidBatchInFixedCatalogOrder()
    {
        using TestWorkspace workspace = new();
        string importRoot = Path.Combine(workspace.Root, "Audio");
        string older = Path.Combine(importRoot, "GameMusic", "older");
        string newer = Path.Combine(importRoot, "GameMusic", "newer");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);

        string olderZanarkand = WriteWave(older, "old-zanarkand.wav");
        string newerZanarkand = WriteWave(newer, "new-zanarkand.wav");
        string besaid = WriteWave(newer, "besaid.wav");
        WriteManifest(older, [Track("1_02_zanarkand", 1, Path.GetFileName(olderZanarkand), olderZanarkand)],
            importedAtUtc: "2026-08-20T00:00:00Z");
        WriteManifest(newer,
        [
            Track("1_18_besaid", 16, Path.GetFileName(besaid), besaid),
            Track("1_02_zanarkand", 1, Path.GetFileName(newerZanarkand), newerZanarkand),
        ], importedAtUtc: "2026-08-21T00:00:00Z");

        ImportedGameMusicTrack[] tracks = ImportedGameMusicCatalog.Load(importRoot).ToArray();

        Assert.Equal(["1_02_zanarkand", "1_18_besaid"], tracks.Select(track => track.Id));
        Assert.Equal(Path.GetFullPath(newerZanarkand), tracks[0].FilePath);
    }

    [Fact]
    public void ImportedCatalog_RejectsAWaveChangedAfterManifestPublication()
    {
        using TestWorkspace workspace = new();
        string importRoot = Path.Combine(workspace.Root, "Audio");
        string batch = Path.Combine(importRoot, "GameMusic", "tampered");
        Directory.CreateDirectory(batch);
        string wave = WriteWave(batch, "zanarkand.wav");
        WriteManifest(batch, [Track("1_02_zanarkand", 1, Path.GetFileName(wave), wave)]);
        byte[] tampered = File.ReadAllBytes(wave);
        tampered[^1] ^= 0x5A;
        File.WriteAllBytes(wave, tampered);

        Assert.Empty(ImportedGameMusicCatalog.Load(importRoot));
    }

    private static object Track(string id, int streamIndex, string fileName, string actualFile) => new
    {
        id,
        streamIndex,
        displayName = "manifest-controlled-name-is-ignored",
        fileName,
        size = new FileInfo(actualFile).Length,
        sha256 = Sha256(actualFile),
        metadata = new
        {
            streamIndex,
            streamCount = GameMusicImportCatalog.ExpectedStreamCount,
            streamName = id,
            sampleRate = 44_100,
            channels = 2,
            loopStartSamples = 0,
            loopEndSamples = 1,
            totalSamples = 2,
        },
    };

    private static string WriteWave(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFF-local-wave"));
        return path;
    }

    private static void WriteManifest(string directory, object[] tracks, string importedAtUtc = "2026-08-21T00:00:00Z")
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            sourceKind = "user-owned-fsb",
            sourceSha256 = GameMusicImportCatalog.ValidatedSourceSha256[0],
            sourceSize = 123L,
            toolName = "vgmstream-cli.exe",
            toolSha256 = GameMusicImportCatalog.ValidatedVgmstreamSha256,
            importedAtUtc,
            redistributionAllowed = false,
            notice = "local only",
            tracks,
        });
        File.WriteAllText(Path.Combine(directory, GameMusicImportCatalog.ManifestFileName), json);
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "FFXProjectEditor", "Modules")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "ffx-game-music-product-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            string allowedParent = Path.Combine(Path.GetTempPath(), "ffx-game-music-product-tests")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(Root);
            if (fullRoot.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}

public sealed class FfxedRuntimeLauncherTests
{
    [Fact]
    public void PinnedToolHashes_MatchThePreservedRepositoryArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string jar = Path.Combine(repoRoot, "ExternalLibs", "FFXED", "FFXED.jar");
        string vgmstream = Path.Combine(repoRoot, "tools", "vgmstream", "vgmstream-cli.exe");

        Assert.Equal(FfxedRuntimeLauncher.ExpectedJarSha256, Hash(jar));
        Assert.Equal(GameMusicImportCatalog.ValidatedVgmstreamSha256, Hash(vgmstream));
    }

    [Fact]
    public void Prepare_UsesOnlyPrivateJavaAndBundledJarWithSeparatedArguments()
    {
        using TestWorkspace workspace = TestWorkspace.CreateComplete();
        CapturingProcessStarter starter = new();
        FfxedRuntimeLauncher launcher = workspace.CreateLauncher(starter);

        FfxedLaunchPreparation preparation = launcher.Prepare();

        Assert.Equal(FfxedLaunchFailure.None, preparation.Failure);
        ProcessStartInfo startInfo = Assert.IsType<ProcessStartInfo>(preparation.StartInfo);
        Assert.Equal(workspace.JavaPath, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(workspace.JarDirectory, startInfo.WorkingDirectory);
        Assert.Equal(["-jar", workspace.JarPath], startInfo.ArgumentList);
        Assert.DoesNotContain("JAVA_HOME", startInfo.Environment.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepare_RejectsMissingPrivateRuntimeEvenWhenPathContainsJava()
    {
        using TestWorkspace workspace = TestWorkspace.CreateComplete(includePrivateJava: false);
        string pathJavaDirectory = Path.Combine(workspace.Root, "path-java");
        Directory.CreateDirectory(pathJavaDirectory);
        File.WriteAllText(Path.Combine(pathJavaDirectory, "java.exe"), "not-used");
        string? previousPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", pathJavaDirectory + Path.PathSeparator + previousPath);
        try
        {
            FfxedLaunchPreparation preparation = workspace.CreateLauncher(new CapturingProcessStarter()).Prepare();

            Assert.Equal(FfxedLaunchFailure.PrivateJavaRuntimeMissing, preparation.Failure);
            Assert.Null(preparation.StartInfo);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
        }
    }

    [Fact]
    public void Prepare_RejectsJarWhoseHashDoesNotMatchThePinnedValue()
    {
        using TestWorkspace workspace = TestWorkspace.CreateComplete();
        FfxedRuntimeLauncher launcher = new(
            workspace.Root,
            workspace.CreateStrictLocator(),
            new CapturingProcessStarter(),
            expectedJarSha256: new string('F', 64));

        FfxedLaunchPreparation preparation = launcher.Prepare();

        Assert.Equal(FfxedLaunchFailure.JarHashMismatch, preparation.Failure);
        Assert.Null(preparation.StartInfo);
    }

    [Fact]
    public void Launch_DelegatesThePreparedShellFreeStartInfo()
    {
        using TestWorkspace workspace = TestWorkspace.CreateComplete();
        CapturingProcessStarter starter = new();

        FfxedLaunchResult result = workspace.CreateLauncher(starter).Launch();

        Assert.True(result.Started);
        Assert.Equal(FfxedLaunchFailure.None, result.Failure);
        Assert.NotNull(starter.Captured);
        Assert.False(starter.Captured!.UseShellExecute);
    }

    private sealed class CapturingProcessStarter : IFfxedProcessStarter
    {
        public ProcessStartInfo? Captured { get; private set; }

        public bool Start(ProcessStartInfo startInfo)
        {
            Captured = startInfo;
            return true;
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, string javaPath, string jarPath, string jarHash)
        {
            Root = root;
            JavaPath = javaPath;
            JarPath = jarPath;
            JarHash = jarHash;
        }

        public string Root { get; }
        public string JavaPath { get; }
        public string JarPath { get; }
        public string JarHash { get; }
        public string JarDirectory => Path.GetDirectoryName(JarPath)!;

        public static TestWorkspace CreateComplete(bool includePrivateJava = true)
        {
            string root = Path.Combine(Path.GetTempPath(), "ffxed-runtime-launcher-tests", Guid.NewGuid().ToString("N"));
            string javaPath = Path.Combine(root, "runtimes", "java", "bin", "javaw.exe");
            string jarPath = Path.Combine(root, "ExternalLibs", "FFXED", "FFXED.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(javaPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            if (includePrivateJava)
            {
                File.WriteAllText(javaPath, "private-java");
            }
            File.WriteAllText(jarPath, "bundled-jar");
            string jarHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jarPath)));
            return new TestWorkspace(root, Path.GetFullPath(javaPath), Path.GetFullPath(jarPath), jarHash);
        }

        public BundledJavaRuntimeLocator CreateStrictLocator() => new(
            new BundledJavaRuntimeLocatorOptions
            {
                BaseDirectory = Root,
                AllowJavaHomeFallback = false,
                AllowPathFallback = false,
            },
            new SystemJavaRuntimeEnvironment());

        public FfxedRuntimeLauncher CreateLauncher(IFfxedProcessStarter starter) => new(
            Root,
            CreateStrictLocator(),
            starter,
            expectedJarSha256: JarHash);

        public void Dispose()
        {
            string allowedParent = Path.Combine(Path.GetTempPath(), "ffxed-runtime-launcher-tests")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(Root);
            if (fullRoot.StartsWith(allowedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExternalLibs", "FFXED", "FFXED.jar")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }
}
