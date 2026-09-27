using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using FFXProjectEditor.Services.ReleaseRuntime;
using Xunit;

namespace FFXProjectEditor.Tests.Services.ReleaseRuntime;

public sealed class GameMusicAutoImportTests
{
    [Fact]
    public void SourceLocator_PrefersTheExplicitEnvironmentVariable()
    {
        using EnvVar env = new(GameMusicSourceLocator.EnvironmentVariable);
        string directory = Path.Combine(Path.GetTempPath(), "ffx-music-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string fsb = Path.Combine(directory, GameMusicSourceLocator.MusicFsbFileName);
        File.WriteAllBytes(fsb, Encoding.ASCII.GetBytes("fake-fsb"));
        try
        {
            Environment.SetEnvironmentVariable(GameMusicSourceLocator.EnvironmentVariable, fsb);

            Assert.Equal(Path.GetFullPath(fsb), GameMusicSourceLocator.Locate());
            Assert.Equal(Path.GetFullPath(fsb), GameMusicSourceLocator.EnumerateCandidates().First());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SourceLocator_IgnoresAMissingEnvironmentCandidate()
    {
        using EnvVar env = new(GameMusicSourceLocator.EnvironmentVariable);
        string missing = Path.Combine(Path.GetTempPath(), "ffx-music-locator-" + Guid.NewGuid().ToString("N"), "nope.fsb");
        Environment.SetEnvironmentVariable(GameMusicSourceLocator.EnvironmentVariable, missing);

        // The missing env path is enumerated first but never returned as a hit.
        Assert.Equal(Path.GetFullPath(missing), GameMusicSourceLocator.EnumerateCandidates().First());
        Assert.NotEqual(Path.GetFullPath(missing), GameMusicSourceLocator.Locate());
    }

    [Fact]
    public void ToolCatalog_MapsEachApprovedBinaryToItsPinnedHash()
    {
        Assert.Equal(
            GameMusicImportCatalog.ValidatedVgmstreamSha256,
            GameMusicImportCatalog.ValidatedVgmstreamSha256For("vgmstream-cli.exe"));
        Assert.Equal(
            GameMusicImportCatalog.ValidatedVgmstreamSha256,
            GameMusicImportCatalog.ValidatedVgmstreamSha256For("VGMSTREAM-CLI.EXE"));
        Assert.Equal(
            GameMusicImportCatalog.ValidatedVgmstreamSha256Linux,
            GameMusicImportCatalog.ValidatedVgmstreamSha256For("vgmstream-cli"));
        Assert.Null(GameMusicImportCatalog.ValidatedVgmstreamSha256For("vgmstream-cli.old"));
        Assert.Null(GameMusicImportCatalog.ValidatedVgmstreamSha256For(null));
    }

    [Fact]
    public void ToolCatalog_ValidatesTheLinuxBinaryPair()
    {
        Assert.True(GameMusicImportCatalog.IsValidatedVgmstreamTool(
            "vgmstream-cli", GameMusicImportCatalog.ValidatedVgmstreamSha256Linux));
        Assert.True(GameMusicImportCatalog.IsValidatedVgmstreamTool(
            "vgmstream-cli.exe", GameMusicImportCatalog.ValidatedVgmstreamSha256));
        Assert.False(GameMusicImportCatalog.IsValidatedVgmstreamTool(
            "vgmstream-cli", GameMusicImportCatalog.ValidatedVgmstreamSha256));
        Assert.False(GameMusicImportCatalog.IsValidatedVgmstreamTool(
            "vgmstream-cli.exe", GameMusicImportCatalog.ValidatedVgmstreamSha256Linux));
    }

    [Fact]
    public void ImportedCatalog_AcceptsABatchImportedByTheLinuxTool()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffx-music-linux-tool-" + Guid.NewGuid().ToString("N"));
        string batch = Path.Combine(root, "GameMusic", "linux-batch");
        Directory.CreateDirectory(batch);
        string wave = Path.Combine(batch, "001_1_02_zanarkand.wav");
        File.WriteAllBytes(wave, Encoding.ASCII.GetBytes("RIFF-local-wave"));
        try
        {
            string manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                sourceKind = "user-owned-fsb",
                sourceSha256 = GameMusicImportCatalog.ValidatedSourceSha256[0],
                sourceSize = 123L,
                toolName = "vgmstream-cli",
                toolSha256 = GameMusicImportCatalog.ValidatedVgmstreamSha256Linux,
                importedAtUtc = "2026-09-15T00:00:00Z",
                redistributionAllowed = false,
                notice = "local only",
                tracks = new[]
                {
                    new
                    {
                        id = "1_02_zanarkand",
                        streamIndex = 1,
                        displayName = "To Zanarkand",
                        fileName = Path.GetFileName(wave),
                        size = new FileInfo(wave).Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wave))),
                        metadata = new
                        {
                            streamIndex = 1,
                            streamCount = GameMusicImportCatalog.ExpectedStreamCount,
                            streamName = "1_02_zanarkand",
                            sampleRate = 44_100,
                            channels = 2,
                            loopStartSamples = 0,
                            loopEndSamples = 1,
                            totalSamples = 2,
                        },
                    },
                },
            });
            File.WriteAllText(Path.Combine(batch, GameMusicImportCatalog.ManifestFileName), manifest);

            ImportedGameMusicTrack[] tracks = ImportedGameMusicCatalog.Load(root).ToArray();
            Assert.Single(tracks);
            Assert.Equal("1_02_zanarkand", tracks[0].Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectFile_ShipsTheAudioToolchainByDefault()
    {
        string csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "FFXProjectEditor", "FFXProjectEditor.csproj"));

        Assert.Contains(
            "<FFXShipAudioTools Condition=\"'$(FFXShipAudioTools)' == ''\">true</FFXShipAudioTools>",
            csproj,
            StringComparison.Ordinal);
        Assert.Contains(@"tools\vgmstream\**\*", csproj, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FFXProjectEditor", "FFXProjectEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }

    private sealed class EnvVar : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvVar(string name)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
